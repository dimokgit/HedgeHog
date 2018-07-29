using HedgeHog;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using ReactiveUI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using static IBApi.IBApiMixins;
using static IBApp.AccountManager;
using OpenOrderHandler = System.Action<int, IBApi.Contract, IBApi.Order, IBApi.OrderState>;
using ORDER_STATUS = System.Nullable<(string status, double filled, double remaining, bool isDone)>;
using OrderStatusHandler = System.Action<int, string, double, double, double, int, int, double, int, string>;
using PortfolioHandler = System.Action<IBApi.Contract, double, double, double, double, double, double, string>;
using POSITION_OBSERVABLE = System.IObservable<(string account, IBApi.Contract contract, double pos, double avgCost)>;
using PositionHandler = System.Action<string, IBApi.Contract, double, double>;
namespace IBApp {
  public partial class AccountManager :DataManager, IDisposable {
    public enum OrderCancelStatuses { Cancelled, PendingCancel };
    public enum OrderDoneStatuses { Filled };
    public enum OrderHeldReason { locate };

    #region Constants
    private const int ACCOUNT_ID_BASE = 50000000;

    private const string ACCOUNT_SUMMARY_TAGS = "AccountType,NetLiquidation,TotalCashValue,SettledCash,AccruedCash,BuyingPower,EquityWithLoanValue,PreviousEquityWithLoanValue,"
             + "GrossPositionValue,ReqTEquity,ReqTMargin,SMA,InitMarginReq,MaintMarginReq,AvailableFunds,ExcessLiquidity,Cushion,FullInitMarginReq,FullMaintMarginReq,FullAvailableFunds,"
             + "FullExcessLiquidity,LookAheadNextChange,LookAheadInitMarginReq ,LookAheadMaintMarginReq,LookAheadAvailableFunds,LookAheadExcessLiquidity,HighestSeverity,DayTradesRemaining,Leverage";
    private const string GTC = "GTC";
    private const string GTD = "GTD";
    private const int E110 = 110;
    private const int ORDER_CAMCELLED = 202;

    //private const int BaseUnitSize = 1;
    #endregion

    #region Fields
    private bool accountSummaryRequestActive = false;
    private bool accountUpdateRequestActive = false;
    private string _accountId;
    private bool _useVerbouse = true;
    private Action<object> _verbous => _useVerbouse ? Trace : o => { };
    private readonly string _accountCurrency = "USD";
    #endregion

    #region Properties
    public Account Account { get; private set; }
    public readonly ReactiveList<Trade> OpenTrades = new ReactiveList<Trade> { ChangeTrackingEnabled = true };
    private readonly ReactiveList<Trade> ClosedTrades = new ReactiveList<Trade>();
    public Func<Trade, double> CommissionByTrade = t => t.Lots * .008;
    public POSITION_OBSERVABLE PositionsObservable { get; private set; }
    public IObservable<(int orderId, Contract contract, IBApi.Order order, OrderState orderState)> OpenOrderObservable { get; private set; }

    Func<string, Trade> CreateTrade { get; set; }

    IObservable<(Offer o, bool b)> _offerMMRs = TradesManagerStatic.dbOffers
        .Select(o => new[] { (o, b: true), (o, b: false) })
        .Concat()
        .ToObservable();

    #endregion

    #region Methods

    #endregion

    #region Ctor
    List<IDisposable> _strams = new List<IDisposable>();
    public AccountManager(IBClientCore ibClient, string accountId, Func<string, Trade> createTrade, Func<Trade, double> commissionByTrade) : base(ibClient, ACCOUNT_ID_BASE) {
      CommissionByTrade = commissionByTrade;
      CreateTrade = createTrade;
      Account = new Account();
      _accountId = accountId;

      RequestAccountSummary();
      SubscribeAccountUpdates();
      RequestPositions();
      IbClient.ClientSocket.reqAllOpenOrders();
      IbClient.ClientSocket.reqAutoOpenOrders(true);

      IbClient.AccountSummary += OnAccountSummary;
      IbClient.AccountSummaryEnd += OnAccountSummaryEnd;
      IbClient.UpdateAccountValue += OnUpdateAccountValue;
      IbClient.UpdatePortfolio += OnUpdatePortfolio;

      OpenTrades.ItemsAdded.Delay(TimeSpan.FromSeconds(5)).Subscribe(RaiseTradeAdded).SideEffect(s => _strams.Add(s));
      OpenTrades.ItemChanged
        .Where(e => e.PropertyName == "Lots")
        .Select(e => e.Sender)
        .Subscribe(RaiseTradeChanged)
        .SideEffect(s => _strams.Add(s));
      OpenTrades.ItemsRemoved.SubscribeOn(TaskPoolScheduler.Default).Subscribe(RaiseTradeRemoved).SideEffect(s => _strams.Add(s));
      //ClosedTrades.ItemsAdded.SubscribeOn(TaskPoolScheduler.Default).Subscribe(RaiseTradeClosed).SideEffect(s => _strams.Add(s));
      ibClient.Error += OnError;

      #region Observables
      void Try(Action a, string source) {
        try {
          a();
        } catch(Exception exc) {
          Trace(new Exception(source, exc));
        }
      }
      IScheduler elFactory() => TaskPoolScheduler.Default;// new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true });

      PositionsObservable = Observable.FromEvent<PositionHandler, (string account, Contract contract, double pos, double avgCost)>(
        onNext => (string a, Contract b, double c, double d) => Try(() => onNext((a, b, c, d)), nameof(IbClient.Position)),
        h => IbClient.Position += h,//.SideEffect(_ => Trace($"+= IbClient.Position")),
        h => IbClient.Position -= h//.SideEffect(_ => Trace($"-= IbClient.Position"))
        )
        .Publish().RefCount()
        //.Spy("**** AccountManager.PositionsObservable ****")
        ;
      OpenOrderObservable = Observable.FromEvent<OpenOrderHandler, (int orderId, Contract contract, IBApi.Order order, OrderState orderState)>(
        onNext => (int orderId, Contract contract, IBApi.Order order, OrderState orderState) =>
        Try(() => onNext((orderId, contract, order, orderState)), nameof(IbClient.OpenOrder)),
        h => IbClient.OpenOrder += h,
        h => IbClient.OpenOrder -= h
        ).Publish().RefCount();
      var osObs = Observable.FromEvent<OrderStatusHandler, (int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld)>(
        onNext
        => (int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld)
        => Try(() => onNext((orderId, status, filled, remaining, avgFillPrice, permId, parentId, lastFillPrice, clientId, whyHeld)), nameof(IbClient.OrderStatus)),
        h => IbClient.OrderStatus += h,
        h => IbClient.OrderStatus -= h
        );

      var portObs = Observable.FromEvent<PortfolioHandler, (Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName)>(
        onNext => (Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName)
        => Try(() => onNext((contract, position, marketPrice, marketValue, averageCost, unrealisedPNL, realisedPNL, accountName)), nameof(IbClient.UpdatePortfolio)),
        h => IbClient.UpdatePortfolio += h,
        h => IbClient.UpdatePortfolio -= h
        );

      #endregion
      #region Subscibtions
      IScheduler esPositions = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "Positions" });
      IScheduler esPositions2 = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "Positions2" });
      DataManager.DoShowRequestErrorDone = false;
      PositionsObservable
        .Where(x => x.account == _accountId && !NoPositionsPlease)
        .Do(x => _verbous("* " + new { Position = new { x.contract.LocalSymbol, x.pos, x.avgCost, x.account } }))
        //.SubscribeOn(esPositions2)
        //.Spy("**** AccountManager.OnPosition ****")
        //.Where(x => x.pos != 0)
        //.Distinct(x => new { x.contract.LocalSymbol, x.pos, x.avgCost, x.account })
        .SelectMany(p =>
          from cd in IbClient.ReqContractDetailsCached(p.contract)
          select (p.account, contract: cd.Summary, p.pos, p.avgCost)
        )
        .ObserveOn(esPositions)
        .Subscribe(a => OnPosition(a.contract, a.pos, a.avgCost), () => { Trace("posObs done"); })
        .SideEffect(s => _strams.Add(s));
      PositionsObservable
        .Take(0)
        .Throttle(TimeSpan.FromSeconds(2))
        .Subscribe(_ => {
          ResetPortfolioExitOrder();
        }).SideEffect(s => _strams.Add(s));
      OpenOrderObservable
        .Where(x => x.order.Account == _accountId)
        .Do(x => Verbose0($"* OpenOrder: {new { x.order.OrderId, x.orderState.Status, x.contract.LocalSymbol } }"))
        //.Do(UpdateOrder)
        .Distinct(a => new { a.orderId, a.order.LmtPrice })
        .Subscribe(a => OnOrderImpl(a.orderId, a.contract, a.order, a.orderState))
        .SideEffect(s => _strams.Add(s));
      osObs
        .Do(t => Verbose0("* OrderStatus " + new { t.orderId, t.status, t.filled, t.remaining, t.whyHeld, isDone = (t.status, t.remaining).IsOrderDone() }))
        .Where(t => UseOrderContracts(ocs => ocs.ByOrderId(t.orderId)).Concat().Any(oc => oc.order.Account == _accountId))
        .SelectMany(t => UseOrderContracts(ocs => ocs.ByOrderId(t.orderId, och => new { t.orderId, t.status, t.filled, t.remaining, t.whyHeld, och.order.LmtPrice })).Concat())
        .Distinct()
        .Do(t => Verbose("* OrderStatus " + new { t.orderId, t.status, t.filled, t.remaining, t.whyHeld, isDone = (t.status, t.remaining).IsOrderDone() }))
        //.Do(x => UseOrderContracts(oc => _verbous("* " + new { OrderStatus = x, Account = oc.ByOrderId(x.orderId, och => och.order.Account).SingleOrDefault() })))
        .Do(t => UseOrderContracts(ocs => {
          ocs.ByOrderId(t.orderId).Where(oc => t.status != "Inactive")
            //.SelectMany(oc => new[] { oc }.Concat(ocs.ByOrderId(oc.order.ParentId).Where(och => och.isNew)))
            .ForEach(oc => {
              oc.status = (t.status, t.filled, t.remaining);
              oc.order.LmtPrice = t.LmtPrice;
            });
          IbClient.ClientSocket.reqAllOpenOrders();
        }
        ))
        .Where(o => (o.status, o.remaining).IsOrderDone())
        .SelectMany(o => UseOrderContracts(ocs => ocs.ByOrderId(o.orderId)).Concat())
        .Do(o => RaiseOrderRemoved(o))
        .Throttle(TimeSpan.FromMinutes(1))
        .Subscribe(_ => {
          Verbose($"{nameof(ResetPortfolioExitOrder)}: scheduled");
          NewThreadScheduler.Default.Schedule(2.FromSeconds(), ResetPortfolioExitOrder);
        })
        .SideEffect(s => _strams.Add(s));

      portObs
        .Where(x => x.accountName == _accountId)
        .Select(t => new { t.contract.LocalSymbol, t.position, t.unrealisedPNL, t.accountName })
        .Timeout(TimeSpan.FromSeconds(5))
        .Where(x => x.position != 0)
        .CatchAndStop(() => new TimeoutException())
        .Subscribe(x => _verbous("* " + new { Portfolio = x }), () => _verbous($"portfolioStream is done."))
        .SideEffect(s => _strams.Add(s));
      #endregion

      IbClient.ClientSocket.reqAllOpenOrders();

      Trace($"{nameof(AccountManager)}:{_accountId} is ready");
    }

    private void ResetPortfolioExitOrder() {
      Trace($"{nameof(ResetPortfolioExitOrder)}: skipped");
      return;
      var combosAll = ComboTradesAllImpl().ToArray();
      Trace(new { combosAll = combosAll.Flatter("") });
      combosAll
      .Do(comboAll => Trace(new { comboAll }))
      .Where(ca => ca.orderId == 0)
      .ForEach(ca => {
        CancelAllOrders("Updating combo exit");
        OpenOrUpdateLimitOrderByProfit2(ca.contract.Instrument, ca.position, 0, ca.open, 0.25);
      });
    }

    #region TraceSubject Subject
    object _TraceSubjectLocker = new object();
    ISubject<object> _TraceSubject;
    ISubject<object> TraceSubject {
      get {
        lock(_TraceSubjectLocker)
          if(_TraceSubject == null) {
            _TraceSubject = new Subject<object>();
            _TraceSubject
              .DistinctUntilChanged()
              .Subscribe(s => Trace(s), exc => { });
          }
        return _TraceSubject;
      }
    }
    void OnTraceSubject(object p) {
      TraceSubject.OnNext(p);
    }
    #endregion



    #region OrderStatus
    public class OrdeContractHolder :IEquatable<OrdeContractHolder> {
      public readonly IBApi.Order order;
      public readonly IBApi.Contract contract;
      (string status, double filled, double remaining) _status;
      public (string status, double filled, double remaining) status {
        get { return _status; }
        set { _status = value; }
      }
      public bool isDone => (status.status, status.remaining).IsOrderDone();
      public bool isNew => status.status == "new";
      public bool isSubmitted => status.status == "Submitted";
      public OrdeContractHolder(IBApi.Order order, IBApi.Contract contract) {
        this.order = order;
        this.contract = contract;
        this.status = ("new", 0, order.TotalQuantity);
      }
      public OrdeContractHolder(IBApi.Order order, IBApi.Contract contract, string status, double filled, double remaining) {
        this.order = order;
        this.contract = contract;
        this.status = (status, filled, remaining);
      }

      public bool Equals(OrdeContractHolder other) => order + "," + contract == other.order + "," + other.contract;
    }
    public Action[] UseOrderContractsDeferred(Action<List<OrdeContractHolder>> func, int timeoutInMilliseconds = 3000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      if(!Monitor.TryEnter(_OpenTradeSync, timeoutInMilliseconds)) {
        Trace(message + " could't enter Monitor");
        return new Action[0];
      }
      Stopwatch sw = Stopwatch.StartNew();
      Action ret =  () => {
        Monitor.Exit(_OpenTradeSync) ;
        if(sw.ElapsedMilliseconds > timeoutInMilliseconds) 
          Trace(message + $" Spent {sw.ElapsedMilliseconds} ms");
        };
      try {
        func(OrderContractsInternal);
        return new[] { ret };
      } catch(Exception exc) {
        Trace(exc);
        ret();
      }
      return new Action[0];
    }

    public IList<T> UseOrderContracts<T>(Func<List<OrdeContractHolder>, T> func, int timeoutInMilliseconds = 3000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      if(!Monitor.TryEnter(_OpenTradeSync, timeoutInMilliseconds)) {
        Trace(message + " could't enter Monitor");
        return new T[0];
      }
      Stopwatch sw = Stopwatch.StartNew();
      T ret;
      try {
        ret = func(OrderContractsInternal);
      } catch(Exception exc) {
        Trace(exc);
        return new T[0];
      } finally {
        Monitor.Exit(_OpenTradeSync);
        if(sw.ElapsedMilliseconds > timeoutInMilliseconds) {
          Trace(message + $" Spent {sw.ElapsedMilliseconds} ms");
        }
      }
      return new[] { ret };
    }
    public void UseOrderContracts<T>(Func<List<OrdeContractHolder>, IEnumerable<T>> func, Action<T> action, [CallerMemberName] string Caller = "") {
      UseOrderContracts(_ => {
        func(_).ForEach(action);
        return Unit.Default;
      }, 3000, Caller).Count();

    }
    public void UseOrderContracts(Action<List<OrdeContractHolder>> action, [CallerMemberName] string Caller = "") {
      Func<List<OrdeContractHolder>, Unit> f = rates => { action(rates); return Unit.Default; };
      UseOrderContracts(f, 3000, Caller).Count();
    }

    public List<OrdeContractHolder> OrderContractsInternal { get; } = new List<OrdeContractHolder>();
    //public ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)> OrderStatuses { get; } = new ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)>();

    private void RaiseOrderRemoved(OrdeContractHolder cd) {
      var o = cd.order;
      var c = cd.contract;
      RaiseOrderRemoved(new HedgeHog.Shared.Order {
        IsBuy = o.Action == "BUY",
        Lot = (int)o.TotalQuantity,
        Pair = c.Instrument,
        IsEntryOrder = IsEntryOrder(o)
      });
    }

    #endregion

    #region Position
    public static bool NoPositionsPlease = false;

    public (Contract contract, int position, double open, double price, double pipCost)
      ContractPosition((IBApi.Contract contract, double pos, double avgCost) p) =>
       (p.contract, position: p.pos.ToInt(), open: p.avgCost * p.pos, p.avgCost / p.contract.ComboMultiplier, pipCost: 0.01 * p.contract.ComboMultiplier * p.pos.Abs());

    ConcurrentDictionary<string, (Contract contract, int position, double open, double price, double pipCost)> _positions = new ConcurrentDictionary<string, (Contract contract, int position, double open, double price, double pipCost)>();
    public ICollection<(Contract contract, int position, double open, double price, double pipCost)> Positions => _positions.Values;
    //public Subject<ICollection<(Contract contract, int position, double open)>> ContracPositionsSubject = new Subject<ICollection<(Contract contract, int position, double open)>>();

    void OnPosition(Contract contract, double position, double averageCost) {
      var posMsg = new PositionMessage("", contract, position, averageCost);
      if(position == 0) {
        OpenTrades
         .Where(t => t.Pair == contract.LocalSymbol)
         .ToList()
         .ForEach(ot => OpenTrades.Remove(ot)
         .SideEffect(_ => _verbous(new { RemovedPosition = new { ot.Pair, ot.IsBuy, ot.Lots } })));
      } else {
        OpenTrades
          .Where(IsEqual2(posMsg))
         .ToList()
         .ForEach(ot => OpenTrades.Remove(ot)
         .SideEffect(_ => _verbous(new { RemovedPosition = new { ot.Pair, ot.IsBuy, ot.Lots } })));
        OpenTrades
          .Where(IsEqual(posMsg))
          .Select(ot
            => new Action(() => ot.Lots = posMsg.Quantity
            .SideEffect(Lots => _verbous(new { ChangePosition = new { ot.Pair, ot.IsBuy, Lots } })))
            )
          .DefaultIfEmpty(() => contract.SideEffect(c
          => OpenTrades.Add(TradeFromPosition(Subscribe(c), position, averageCost)
          .SideEffect(t => _verbous(new { OpenPosition = new { t.Pair, t.IsBuy, t.Lots } })))))
          .ToList()
          .ForEach(a => a());
      }

      TraceTrades("OnPositions: ", OpenTrades);
      var cp = ContractPosition((contract, position, averageCost));
      _positions.AddOrUpdate(cp.contract.Key, cp, (k, v) => cp);
      //if(IbClient.ClientId == 0 && !_positions.Values.Any(p => p.position != 0))
      //  CancelAllOrders("Canceling stale orders");
    }

    private Contract Subscribe(Contract c) => IbClient.SetContractSubscription(c);

    Trade TradeFromPosition(Contract contract, double position, double avgCost) {
      var st = IbClient.ServerTime;
      var trade = CreateTrade(contract.LocalSymbol);
      trade.Id = DateTime.Now.Ticks + "";
      trade.Buy = position > 0;
      trade.IsBuy = trade.Buy;
      trade.Time2 = st;
      trade.Time2Close = IbClient.ServerTime;
      trade.Open = avgCost;
      trade.Lots = position.Abs().ToInt();
      trade.OpenOrderID = "";
      trade.CommissionByTrade = CommissionByTrade;
      return trade;
    }

    #endregion

    private void OnError(int reqId, int code, string error, Exception exc) {
      UseOrderContracts(orderContracts =>
        orderContracts.ByOrderId(reqId).ToList().ForEach(oc => {
          if(new[] { ORDER_CAMCELLED }.Contains(code)) {
            RaiseOrderRemoved(oc);
            orderContracts.Remove(oc);
          }
        }));
    }
    #endregion

    #region IDisposable Support
    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
      if(!disposedValue) {
        if(disposing) {
          _strams.ForEach(s => s.Dispose());
        }

        // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
        // TODO: set large fields to null.

        disposedValue = true;
      }
    }

    // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
    // ~AccountManager() {
    //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
    //   Dispose(false);
    // }

    // This code added to correctly implement the disposable pattern.
    public void Dispose() {
      // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
      Dispose(true);
      // TODO: uncomment the following line if the finalizer is overridden above.
      // GC.SuppressFinalize(this);
    }
    #endregion


    #region Events

    #region TradeAdded Event
    //public class TradeEventArgs : EventArgs {
    //  public Trade Trade { get; private set; }
    //  public TradeEventArgs(Trade trade) : base() {
    //    Trade = trade;
    //  }
    //}
    event EventHandler<TradeEventArgs> TradeAddedEvent;
    public event EventHandler<TradeEventArgs> TradeAdded {
      add {
        if(TradeAddedEvent == null || !TradeAddedEvent.GetInvocationList().Contains(value))
          TradeAddedEvent += value;
      }
      remove {
        TradeAddedEvent -= value;
      }
    }
    protected void RaiseTradeAdded(Trade trade) {
      TradeAddedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion


    #region TradeChanged Event
    event EventHandler<TradeEventArgs> TradeChangedEvent;
    public event EventHandler<TradeEventArgs> TradeChanged {
      add {
        if(TradeChangedEvent == null || !TradeChangedEvent.GetInvocationList().Contains(value))
          TradeChangedEvent += value;
      }
      remove {
        TradeChangedEvent -= value;
      }
    }
    protected void RaiseTradeChanged(Trade trade) {
      TradeChangedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion


    #region TradeRemoved Event
    event EventHandler<TradeEventArgs> TradeRemovedEvent;
    public event EventHandler<TradeEventArgs> TradeRemoved {
      add {
        if(TradeRemovedEvent == null || !TradeRemovedEvent.GetInvocationList().Contains(value))
          TradeRemovedEvent += value;
      }
      remove {
        TradeRemovedEvent -= value;
      }
    }
    protected void RaiseTradeRemoved(Trade trade) {
      ClosedTrades.Add(trade);
      RaiseTradeClosed(trade);
      TradeRemovedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion

    #region TradeClosedEvent
    event EventHandler<TradeEventArgs> TradeClosedEvent;
    public event EventHandler<TradeEventArgs> TradeClosed {
      add {
        if(TradeClosedEvent == null || !TradeClosedEvent.GetInvocationList().Contains(value))
          TradeClosedEvent += value;
      }
      remove {
        if(TradeClosedEvent != null)
          TradeClosedEvent -= value;
      }
    }
    void RaiseTradeClosed(Trade trade) {
      trade.CloseTrade();
      TradeClosedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion


    #endregion

    #region OpenOrder
    #region OrderAddedEvent
    event EventHandler<OrderEventArgs> OrderAddedEvent;
    public event EventHandler<OrderEventArgs> OrderAdded {
      add {
        if(OrderAddedEvent == null || !OrderAddedEvent.GetInvocationList().Contains(value))
          OrderAddedEvent += value;
      }
      remove {
        OrderAddedEvent -= value;
      }
    }

    void RaiseOrderAdded(HedgeHog.Shared.Order Order) {
      OrderAddedEvent?.Invoke(this, new OrderEventArgs(Order));
    }
    #endregion

    #region OrderRemovedEvent
    public event OrderRemovedEventHandler OrderRemovedEvent;
    public event OrderRemovedEventHandler OrderRemoved {
      add {
        if(OrderRemovedEvent == null || !OrderRemovedEvent.GetInvocationList().Contains(value))
          OrderRemovedEvent += value;
      }
      remove {
        OrderRemovedEvent -= value;
      }
    }

    void RaiseOrderRemoved(HedgeHog.Shared.Order args) => OrderRemovedEvent?.Invoke(args);
    #endregion


    private static bool IsEntryOrder(IBApi.Order o) => new[] { "MKT", "LMT" }.Contains(o.OrderType);
    private void OnOrderImpl(int reqId, IBApi.Contract c, IBApi.Order o, IBApi.OrderState os) {
      if(!o.WhatIf) {
        UseOrderContracts(orderContracts => {
          Trace($"{nameof(OnOrderImpl)}:{o},{c},{os.Status}");
          var ochs = orderContracts.ByOrderId(o.OrderId).ToList();
          if(ochs.IsEmpty()) {
            orderContracts.TryAdd(new OrdeContractHolder(o, c));
            RaiseOrderAdded(new HedgeHog.Shared.Order {
              IsBuy = o.Action == "BUY",
              Lot = (int)o.TotalQuantity,
              Pair = c.Instrument,
              IsEntryOrder = IsEntryOrder(o)
            });
          } else {
            ochs.ForEach(och => {
              och.order.LmtPrice = o.LmtPrice;
            });
          }
        });
      } else if(GetTrades().IsEmpty()) {
        //RaiseOrderRemoved(o.OrderId);
        var offer = TradesManagerStatic.GetOffer(c.Instrument);
        var isBuy = o.IsBuy();
        var levelrage = (o.LmtPrice * o.TotalQuantity) / (double.Parse(os.InitMargin) - InitialMarginRequirement);
        if(levelrage != 0 && !double.IsInfinity(levelrage))
          if(isBuy) {
            offer.MMRLong = 1 / levelrage;
            Trace(new { offer = new { offer.Pair, offer.MMRLong } });
          } else {
            offer.MMRShort = 1 / levelrage;
            Trace(new { offer = new { offer.Pair, offer.MMRShort } });
          }
      }
    }
    #endregion

    #region Trades
    public IList<Trade> GetTrades() { return OpenTrades.ToList(); }
    public IList<Trade> GetClosedTrades() { return ClosedTrades.ToList(); }
    public void SetClosedTrades(IEnumerable<Trade> trades) => ClosedTrades.AddRange(new ReactiveList<Trade>(trades));
    #endregion

    Action IfEmpty(object o) => () => throw new Exception(o.ToJson());
    #region Butterfly

    #endregion

    #region OpenOrder
    private int NetOrderId() => IbClient.ValidOrderId();
    public PendingOrder OpenTradeWhatIf(string pair, bool buy) {
      var anount = GetTrades().Where(t => t.Pair == pair).Select(t => t.GrossPL).DefaultIfEmpty(Account.Equity).Sum() / 2;
      return OpenTrade(ContractSamples.ContractFactory(pair), pair.IsFuture() ? 1 : 100, 0.0, 0.0, false, DateTime.MaxValue);
    }
    public PendingOrder OpenTrade_remove(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price) {
      return OpenTrade_remove(pair, buy, lots, takeProfit, stopLoss, remark, price, false);
    }
    public PendingOrder OpenTrade_remove(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price_, bool whatIf) {
      UseOrderContracts(orderContracts => {
        var isStock = pair.IsUSStock();
        var price = Lazy.Create(() => price_ ?? IbClient.GetPrice(pair));
        var rth = Lazy.Create(() => new[] { price.Value.Time.Date.AddHours(9.5), price.Value.Time.Date.AddHours(16) });
        var isPreRTH = !whatIf && isStock && !price.Value.Time.Between(rth.Value);
        var orderType = whatIf ? "MKT" : isPreRTH ? "LMT" : "MKT";
        var c = ContractSamples.ContractFactory(pair);
        var o = new IBApi.Order() {
          OrderId = NetOrderId(),
          Action = buy ? "BUY" : "SELL",
          OrderType = orderType,
          Account = _accountId,
          TotalQuantity = whatIf ? lots : pair.IsUSStock() ? lots : lots,
          Tif = GTC,
          OutsideRth = isPreRTH,
          WhatIf = whatIf,
          OverridePercentageConstraints = true
        };
        if(orderType == "LMT") {
          var d = TradesManagerStatic.GetDigits(pair) - 1;
          var offset = isPreRTH ? 1.001 : 1;
          o.LmtPrice = Math.Round(buy ? price.Value.Ask * offset : price.Value.Bid / offset, d);
        }
        orderContracts.TryAdd(new OrdeContractHolder(o, c));
        _verbous(new { placeOrder = new { o, c } });
        IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      });
      return null;
    }
    public void OpenOrUpdateLimitOrderByProfit2(string instrument, int position, int orderId, double openAmount, double profitAmount) {
      UseOrderContracts(orderContracts => {
        var pa = profitAmount >= 1 ? profitAmount : openAmount.Abs() * profitAmount;
        orderContracts.ByOrderId(orderId)
        .Where(och => !och.isDone)
        .Do(och => {
          if(och.contract.Instrument != instrument)
            throw new Exception($"{nameof(OpenOrUpdateLimitOrderByProfit2)}:{new { orderId, och.contract.Instrument, dontMatch = instrument }}");
          var limit = OrderPrice(priceFromProfit(pa, position, och.contract.ComboMultiplier, openAmount), och.contract);
          UpdateOrder(orderId, limit);
        })
        .RunIfEmpty(() => { // Create new order
          Contract.FromCache(instrument)
            .Count(1, new { OpenOrUpdateOrder = new { instrument, unexpected = "count in cache" } })
            .ForEach(c => {
              var lmtPrice = OrderPrice(priceFromProfit(pa, position, c.ComboMultiplier, openAmount), c);
              OpenTrade(c, -position, lmtPrice, 0.0, false, DateTime.MaxValue);
            });
        });
      });
    }
    public void OpenOrUpdateLimitOrderByProfit3(Contract contract, int position, int orderId, double openPrice, double profitAmount) {
      var limit = profitAmount >= 1 ? profitAmount / contract.PipCost() * position : openPrice * profitAmount;
      OpenOrUpdateLimitOrder(contract, position, orderId, openPrice + limit);
    }
    public void OpenOrUpdateLimitOrder(Contract contract, int position, int orderId, double lmpPrice) {
      UseOrderContracts(orderContracts =>
        orderContracts.ByOrderId(orderId)
        .Where(och => !och.isDone)
        .Do(och => {
          if(och.contract.Instrument != contract.Instrument)
            throw new Exception($"{nameof(OpenOrUpdateLimitOrder)}:{new { orderId, och.contract.Instrument, dontMatch = contract.Instrument }}");
          UpdateOrder(orderId, OrderPrice(lmpPrice, och.contract));
        })
        .RunIfEmpty(() => OpenTrade(contract, -position, lmpPrice, 0.0, false, DateTime.MaxValue)
      ));
    }
    public void UpdateOrder(int orderId, double lmpPrice, int minTickMultiplier = 1) {
      UseOrderContracts(orderContracts => {
        var och = orderContracts.ByOrderId(orderId).SingleOrDefault();
        if(och == null)
          throw new Exception($"UpdateTrade: {new { orderId, not = "found" }}");
        if(och.isDone)
          throw new Exception($"UpdateTrade: {new { orderId, och.isDone }}");
        if(lmpPrice == 0) {
          IbClient.ClientSocket.cancelOrder(orderId);
          return;
        }
        var order = och.order;
        //var minTick = och.contract.MinTick;
        order.LmtPrice = OrderPrice(lmpPrice, och.contract, minTickMultiplier);//  Math.Round(lmpPrice / minTick) * minTick;
        if(order.OpenClose.IsNullOrWhiteSpace())
          order.OpenClose = "C";
        order.VolatilityType = 0;
        IbClient.WatchReqError(orderId, e => {
          OnOpenError(e, $"{nameof(UpdateOrder)}:{och.contract}:{new { order.LmtPrice }}");
          if(e.errorCode == E110)
            UpdateOrder(orderId, lmpPrice, ++minTickMultiplier);
        }, () => { });
        IbClient.ClientSocket.placeOrder(order.OrderId, och.contract, order);
      });
    }
    private void OnOpenError((int reqId, int code, string error, Exception exc) e, string trace) {
      UseOrderContracts(orderContracts => {
        Trace(trace + e);
        orderContracts.ByOrderId(e.reqId).ToList().ForEach(oc => {
          if(new[] { 200, 201, 203, 321, 382, 383 }.Contains(e.code)) {
            //OrderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
            RaiseOrderRemoved(oc);
            orderContracts.Remove(oc);
          }
        });
      });
    }


    public int PlaceOrder(IBApi.Order order, Contract contract) {
      if(order.OrderId == 0)
        order.OrderId = NextReqId();
      IbClient.ClientSocket.placeOrder(order.OrderId, contract, order);
      return order.OrderId;
    }
    public PendingOrder OpenSpreadTrade((string pair, bool buy, int lots)[] legs, double takeProfit, double stopLoss, string remark, bool whatIf) {
      UseOrderContracts(orderContracts => {
        var isStock = legs.All(l => l.pair.IsUSStock());
        var legs2 = legs.Select(t => (t.pair, t.buy, t.lots, price: IbClient.GetPrice(t.pair))).ToArray();
        var price = legs2[0].price;
        var rth = Lazy.Create(() => new[] { price.Time.Date.AddHours(9.5), price.Time.Date.AddHours(16) });
        var isPreRTH = !whatIf && isStock && !price.Time.Between(rth.Value);
        var orderType = "MKT";
        var c = ContractSamples.StockComboContract();
        var o = new IBApi.Order() {
          Account = _accountId,
          OrderId = NetOrderId(),
          Action = legs[0].buy ? "BUY" : "SELL",
          OrderType = orderType,
          TotalQuantity = legs[0].lots,
          Tif = GTC,
          OutsideRth = isPreRTH,
          WhatIf = whatIf,
          OverridePercentageConstraints = true
        };
        orderContracts.TryAdd(new OrdeContractHolder(o, c));
        _verbous(new { plaseOrder = new { o, c } });
        IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      });
      return null;
    }
    double OrderPrice(double orderPrice, Contract contract) => OrderPrice(orderPrice, contract, 1);
    double OrderPrice(double orderPrice, Contract contract, int minTickMultilier) {
      var minTick = contract.MinTick() * minTickMultilier;
      var p = (Math.Round(orderPrice / minTick) * minTick);
      p = Math.Round(p, 4);
      return p;
    }
    #endregion

    #region FetchMMR
    #region WhatIf Subject
    object _WhatIfSubjectLocker = new object();
    ISubject<Action> _WhatIfSubject;
    ISubject<Action> WhatIfSubject {
      get {
        lock(_WhatIfSubjectLocker)
          if(_WhatIfSubject == null) {
            _WhatIfSubject = new Subject<Action>();
            _WhatIfSubject
              //.Throttle(TimeSpan.FromSeconds(1))
              .Subscribe(a => a(), exc => Trace(exc));
          }
        return _WhatIfSubject;
      }
    }

    #endregion
    void OnWhatIf(Action a) => WhatIfSubject.OnNext(a);
    private void FetchMMR(string pair) {
      OnWhatIf(() => OpenTradeWhatIf(pair, true));
      OnWhatIf(() => OpenTradeWhatIf(pair, false));
    }
    public void FetchMMRs() => GetTrades()
      .OnEmpty(() => {
        Trace(nameof(FetchMMR) + " started");
        TradesManagerStatic.dbOffers.Where(o => !o.Pair.IsCurrenncy()).ToObservable().Subscribe(o => FetchMMR(o.Pair));
      })
      .ForEach(t => OnTraceSubject(new { FetchMMRs = new { t.Pair, t.IsBuy, t.Lots, Message = "Won't run" } }));
    #endregion

    #region Overrrides/helpers
    private static Func<Trade, bool> IsEqual(PositionMessage position) => ot => ot.Key().Equals(position.Key());
    private static Func<Trade, bool> IsEqual2(PositionMessage position) => ot => ot.Key2().Equals(position.Key());
    private static Func<Trade, bool> IsEqual2(Trade trade) => ot => ot.Key().Equals(trade.Key2());

    private void TraceTrades(string label, IEnumerable<Trade> trades)
      => Trace(label
        + (trades.Count() > 1 ? "\n" : "")
        + string.Join("\n", trades.OrderBy(t => t.Pair).Select(ot => new { ot.Pair, ot.Position, ot.Open, ot.Time, ot.Commission })));
    public override string ToString() => new { IbClient, CurrentAccount = _accountId } + "";
    #endregion
  }
  static class Mixins {
    public static void TryAdd(this List<OrdeContractHolder> source, OrdeContractHolder orderContractHolder) {
      source.ByOrderId(orderContractHolder.order.OrderId).RunIfEmpty(() => source.Add(orderContractHolder));
    }

    public static IEnumerable<T> ByOrderId<T>(this IEnumerable<OrdeContractHolder> source, int orderId, Func<OrdeContractHolder, T> map)
      => source.ByOrderId(orderId).Select(map);
    public static IEnumerable<OrdeContractHolder> ByOrderId(this IEnumerable<OrdeContractHolder> source, int orderId)
      => source.Where(och => och.order.OrderId == orderId);
    public static bool IsOrderDone(this (string status, double remaining) order) =>
      EnumUtils.Contains<OrderCancelStatuses>(order.status) || EnumUtils.Contains<OrderDoneStatuses>(order.status) && order.remaining == 0;

    //public static void Verbous<T>(this T v)=>_ve
    public static bool IsPreSubmited(this IBApi.OrderState order) => order.Status == "PreSubmitted";

    public static bool IsBuy(this IBApi.Order o) => o.Action == "BUY";
    public static double TotalPosition(this IBApi.Order o) => o.IsBuy() ? o.TotalQuantity : -o.TotalQuantity;

    private static (string symbol, bool isBuy) Key(string symbol, bool isBuy) => (symbol.WrapPair(), isBuy);
    private static (string symbol, bool isBuy) Key2(string symbol, bool isBuy) => Key(symbol, !isBuy);

    public static (string symbol, bool isBuy) Key(this PositionMessage t) => Key(t.Contract.LocalSymbol, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this PositionMessage t) => Key2(t.Contract.LocalSymbol, t.IsBuy);

    public static (string symbol, bool isBuy) Key(this Trade t) => Key(t.Pair, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this Trade t) => Key2(t.Pair, t.IsBuy);

    public static string Key(this Contract c) => c.Symbol + ":" + string.Join(",", c.ComboLegs?.Select(l => l.ConId)) ?? "";
  }
}
