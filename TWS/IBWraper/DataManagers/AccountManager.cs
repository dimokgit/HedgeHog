using HedgeHog;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using ReactiveUI;
using ReactiveUI.Legacy;
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
using OpenOrderHandler = System.Action<IBSampleApp.messages.OpenOrderMessage>;
using ORDER_STATUS = System.Nullable<(string status, double filled, double remaining, bool isDone)>;
using OrderStatusHandler = System.Action<IBSampleApp.messages.OrderStatusMessage>;
using PortfolioHandler = System.Action<IBApp.UpdatePortfolioMessage>;
using POSITION_OBSERVABLE = System.IObservable<IBApp.PositionMessage>;
using PositionHandler = System.Action<IBApp.PositionMessage>;
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
    private bool _useVerbouse = false;
    private Action<object> _verbous => _useVerbouse ? Trace : o => { };
    private readonly string _accountCurrency = "USD";
    #endregion

    #region Properties
    public Account Account { get; private set; }
    public readonly ReactiveList<Trade> OpenTrades = new ReactiveList<Trade> { ChangeTrackingEnabled = true };
    private readonly ReactiveList<Trade> ClosedTrades = new ReactiveList<Trade>();
    public Func<Trade, double> CommissionByTrade = t => t.Lots * .008;
    public POSITION_OBSERVABLE PositionsObservable { get; private set; }
    public IObservable<OpenOrderMessage> OpenOrderObservable { get; private set; }
    public IObservable<OrderStatusMessage> OrderStatusObservable { get; private set; }
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
      IbClient.ClientSocket.reqOpenOrders();
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
      IScheduler elFactory = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = nameof(AccountManager) });

      PositionsObservable = Observable.FromEvent<PositionHandler, PositionMessage>(
        onNext => (PositionMessage m) => Try(() => onNext(m), nameof(IbClient.Position)),
        h => IbClient.Position += h,//.SideEffect(_ => Trace($"+= IbClient.Position")),
        h => IbClient.Position -= h//.SideEffect(_ => Trace($"-= IbClient.Position"))
        )
        .ObserveOn(elFactory)
        .Publish().RefCount()
        //.Spy("**** AccountManager.PositionsObservable ****")
        ;
      OpenOrderObservable = Observable.FromEvent<OpenOrderHandler, OpenOrderMessage>(
        onNext => (OpenOrderMessage m) =>
        Try(() => onNext(m), nameof(IbClient.OpenOrder)),
        h => IbClient.OpenOrder += h,
        h => IbClient.OpenOrder -= h
        )
        .ObserveOn(elFactory)
        .Publish().RefCount();
      OrderStatusObservable = Observable.FromEvent<OrderStatusHandler, OrderStatusMessage>(
        onNext
        => (OrderStatusMessage m) => Try(() => onNext(m), nameof(IbClient.OrderStatus)),
        h => IbClient.OrderStatus += h,
        h => IbClient.OrderStatus -= h
        )
        .ObserveOn(elFactory)
        .Publish().RefCount();

      var portObs = Observable.FromEvent<PortfolioHandler, UpdatePortfolioMessage>(
        onNext => (UpdatePortfolioMessage m) => Try(() => onNext(m), nameof(IbClient.UpdatePortfolio)),
        h => IbClient.UpdatePortfolio += h,
        h => IbClient.UpdatePortfolio -= h
        )
        .ObserveOn(elFactory)
        .Publish().RefCount();


      #endregion
      #region Subscibtions
      IScheduler esPositions = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "Positions" });
      IScheduler esPositions2 = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "Positions2" });
      DataManager.DoShowRequestErrorDone = false;
      PositionsObservable
        .Where(x => x.Account == _accountId && !NoPositionsPlease)
        .Do(x => _verbous("* " + new { Position = new { x.Contract.LocalSymbol, x.Position, x.AverageCost, x.Account } }))
        //.SubscribeOn(esPositions2)
        //.Spy("**** AccountManager.OnPosition ****")
        //.Where(x => x.pos != 0)
        //.Distinct(x => new { x.contract.LocalSymbol, x.pos, x.avgCost, x.account })
        .SelectMany(p =>
          from cd in IbClient.ReqContractDetailsCached(p.Contract)
          select (p.Account, contract: cd.Contract, p.Position, p.AverageCost)
        )
        .Subscribe(a => OnPosition(a.contract, a.Position, a.AverageCost), () => { Trace("posObs done"); })
        .SideEffect(s => _strams.Add(s));
      PositionsObservable
        .Take(0)
        .Throttle(TimeSpan.FromSeconds(2))
        .Subscribe(_ => {
          ResetPortfolioExitOrder();
        }).SideEffect(s => _strams.Add(s));
      OpenOrderObservable
        .Where(x => x.Order.Account == _accountId)
        .Do(x => Verbose($"* OpenOrder: {new { x.Order.OrderId, x.Order.PermId, x.OrderState.Status, x.Contract.LocalSymbol } }"))
        //.Do(UpdateOrder)
        .Distinct(a => new { a.Order.PermId })
        .Subscribe(a => OnOrderImpl(a))
        .SideEffect(s => _strams.Add(s));
      OrderStatusObservable
        .Do(t => Verbose("* OrderStatus " + new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld, isDone = (t.Status, t.Remaining).IsOrderDone() }))
        .Where(t => OrderContractsInternal.ByOrderId(t.OrderId).Any(oc => oc.order.Account == _accountId))
        .SelectMany(t => OrderContractsInternal.ByOrderId(t.OrderId, och => new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld, och.order.LmtPrice }))
        .Distinct()
        .Do(t => Verbose("* OrderStatus " + new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld, isDone = (t.Status, t.Remaining).IsOrderDone() }))
        //.Do(x => UseOrderContracts(oc => _verbous("* " + new { OrderStatus = x, Account = oc.ByOrderId(x.orderId, och => och.order.Account).SingleOrDefault() })))
        .Do(t => {
          OrderContractsInternal.ByOrderId(t.OrderId).Where(oc => t.Status != "Inactive")
            //.SelectMany(oc => new[] { oc }.Concat(ocs.ByOrderId(oc.order.ParentId).Where(och => och.isNew)))
            .ForEach(oc => {
              oc.status = new OrdeContractHolder.Status(t.Status, t.Filled, t.Remaining);
              oc.order.LmtPrice = t.LmtPrice;
            });
          IbClient.ClientSocket.reqAllOpenOrders();
        }
        )
        .Where(o => (o.Status, o.Remaining).IsOrderDone())
        .SelectMany(o => UseOrderContracts(ocs => ocs.ByOrderId(o.OrderId)).Concat())
        .Do(o => RaiseOrderRemoved(o))
        .Throttle(TimeSpan.FromMinutes(1))
        .Subscribe(_ => {
          Verbose($"{nameof(ResetPortfolioExitOrder)}: scheduled");
          NewThreadScheduler.Default.Schedule(2.FromSeconds(), ResetPortfolioExitOrder);
        })
        .SideEffect(s => _strams.Add(s));

      portObs
        .Where(x => x.AccountName == _accountId)
        .Select(t => new { t.Contract.LocalSymbol, t.Position, t.UnrealisedPNL, t.AccountName })
        .Timeout(TimeSpan.FromSeconds(5))
        .Where(x => x.Position != 0)
        .CatchAndStop(() => new TimeoutException())
        .Subscribe(x => _verbous("* " + new { Portfolio = x }), () => _verbous($"portfolioStream is done."))
        .SideEffect(s => _strams.Add(s));

      DateTime thStart() => ibClient.ServerTime.Date.AddHours(9).AddMinutes(15);
      DateTime thEnd() => ibClient.ServerTime.Date.AddHours(16);
      var shouldExecute = (
      from pair in IbClient.PriceChangeObservable.Select(_ => _.EventArgs.Price.Pair)
      where !ibClient.ServerTime.Between(thStart(), thEnd())
      from oc in OrderContractsInternal.ByLocalSymbool(pair)
      where oc.ShouldExecute
      from paren in OrderContractsInternal.ToArray().Where(o => o.order.OrderId == oc.order.ParentId).DefaultIfEmpty()
      where (paren == null || paren.isFilled)
      select oc
      )
      .Sample(1.FromSeconds())
      .Distinct(oc => oc.order.PermId)
      .ObserveOn(elFactory)
      .Subscribe(oc => {
        Trace($"WillExecute: {oc}");
        var child = OrderContractsInternal.ToArray().Where(ch => ch.order.ParentId == oc.order.OrderId).ToArray();
        CancelOrder(oc.order.OrderId);
        oc.order.Conditions.Clear();
        oc.order.OrderId = NetOrderId();
        (from pos in PlaceOrder(oc.order, oc.contract)
         from poe in pos
         where !poe.error.HasError
         select poe.order into po
         from ch in child
         select ch.SE(_ => { _.order.ParentId = po.OrderId; _.order.OrderId = 0; })
         ).Subscribe(ch => PlaceOrder(ch.order, ch.contract));
      });
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
    public Action[] UseOrderContractsDeferred(Action<List<OrdeContractHolder>> func, int timeoutInMilliseconds = 10000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      if(!_mutexOpenTrade.Wait(timeoutInMilliseconds)) {
        Trace(message + " could't enter Monitor");
        return new Action[0];
      }
      Stopwatch sw = Stopwatch.StartNew();
      Action ret = () => {
        _mutexOpenTrade.Release();
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

    public IList<T> UseOrderContracts<T>(Func<List<OrdeContractHolder>, T> func, int timeoutInMilliseconds = 10000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      if(!_mutexOpenTrade.Wait(timeoutInMilliseconds)) {
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
        _mutexOpenTrade.Release();
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
      }, 10000, Caller).Count();

    }
    public void UseOrderContracts(Action<List<OrdeContractHolder>> action, [CallerMemberName] string Caller = "") {
      Func<List<OrdeContractHolder>, Unit> f = rates => { action(rates); return Unit.Default; };
      UseOrderContracts(f, 10000, Caller).Count();
    }
    public IList<T> UseOrderContracts<T>(Func<IBClientCore, List<OrdeContractHolder>, T> func, int timeoutInMilliseconds = 10000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      if(false && !_mutexOpenTrade.Wait(timeoutInMilliseconds)) {
        Trace(message + " could't enter Monitor");
        return new T[0];
      }
      Stopwatch sw = Stopwatch.StartNew();
      T ret;
      try {
        ret = func(IbClient, OrderContractsInternal);
      } catch(Exception exc) {
        Trace(exc);
        return new T[0];
      } finally {
        _mutexOpenTrade.Release();
        if(sw.ElapsedMilliseconds > timeoutInMilliseconds) {
          Trace(message + $" Spent {sw.ElapsedMilliseconds} ms");
        }
      }
      return new[] { ret };
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
      OrderContractsInternal.ByOrderId(reqId).ToList().ForEach(oc => {
        if(new[] { ORDER_CAMCELLED }.Contains(code)) {
          RaiseOrderRemoved(oc);
          OrderContractsInternal.Remove(oc);
        }
      });
    }
    #endregion

    #region IDisposable Support
    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
      if(!disposedValue) {
        if(disposing) {
          _strams.ForEach(s => s.Dispose());
        }
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
      //TradeRemovedEvent?.Invoke(this, new TradeEventArgs(trade));
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
    private void OnOrderImpl(OpenOrderMessage m) {
      if(!m.Order.WhatIf) {
        Trace($"{nameof(OnOrderImpl)}:{m.Order},{m.Contract},{m.OrderState.Status}");
        var ochs = OrderContractsInternal.ByOrderId(m.Order.OrderId).ToList();
        if(ochs.IsEmpty()) {
          OrderContractsInternal.TryAdd((OrdeContractHolder)m);
          RaiseOrderAdded(new HedgeHog.Shared.Order {
            IsBuy = m.Order.Action == "BUY",
            Lot = (int)m.Order.TotalQuantity,
            Pair = m.Contract.Instrument,
            IsEntryOrder = IsEntryOrder(m.Order)
          });
        } else {
          ochs.ForEach(och => {
            och.order.LmtPrice = m.Order.LmtPrice;
          });
        }
      } else if(GetTrades().IsEmpty()) {
        // TODO: WhatIf leverage, MMR
        //RaiseOrderRemoved(o.OrderId);
        var offer = TradesManagerStatic.GetOffer(m.Contract.Instrument);
        var isBuy = m.Order.IsBuy();
        var levelrage = (m.Order.LmtPrice * m.Order.TotalQuantity) / (double.Parse(m.OrderState.InitMarginChange));
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
    public IObservable<(OpenOrderMessage order, ErrorMessage error)[]> OpenTradeWhatIf(string pair, bool buy) {
      var anount = GetTrades().Where(t => t.Pair == pair).Select(t => t.GrossPL).DefaultIfEmpty(Account.Equity).Sum() / 2;
      if(!IBApi.Contract.Contracts.TryGetValue(pair, out var contract))
        throw new Exception($"Pair:{pair} is not fround in Contracts");
      return OpenTrade(contract, contract.IsFuture ? 1 : 100);
    }
    public void OpenOrUpdateLimitOrderByProfit2(string instrument, int position, int orderId, double openAmount, double profitAmount) {
      var pa = profitAmount >= 1 ? profitAmount : openAmount.Abs() * profitAmount;
      OrderContractsInternal.ByOrderId(orderId)
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
    public void CancelOrder(int orderId) => IbClient.ClientSocket.cancelOrder(orderId);
    public void UpdateOrder(int orderId, double lmpPrice, int minTickMultiplier = 1) {
      UseOrderContracts(orderContracts => {
        var och = orderContracts.ByOrderId(orderId).SingleOrDefault();
        if(och == null)
          throw new Exception($"UpdateTrade: {new { orderId, not = "found" }}");
        if(och.isDone)
          throw new Exception($"UpdateTrade: {new { orderId, och.isDone }}");
        if(och.order.IsLimit && lmpPrice == 0) {
          Trace($"{nameof(UpdateOrder)}: cancelling pending {new { och.order, och.contract }}");
          IbClient.ClientSocket.cancelOrder(orderId);
          return;
        }
        var order = och.order;
        //var minTick = och.contract.MinTick;
        order.LmtPrice = OrderPrice(lmpPrice, och.contract);//  Math.Round(lmpPrice / minTick) * minTick;
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
      Trace(trace + e);
      OrderContractsInternal.ByOrderId(e.reqId).ToList().ForEach(oc => {
        if(new[] { 200, 201, 203, 321, 382, 383 }.Contains(e.code)) {
          //OrderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
          RaiseOrderRemoved(oc);
          OrderContractsInternal.Remove(oc);
        }
      });
    }


    public IObservable<(OpenOrderMessage order,ErrorMessage error)[]> PlaceOrder(IBApi.Order order, Contract contract) {
      if(order.OrderId == 0)
        order.OrderId = NetOrderId();
      var obs = OpenOrderObservable.Where(m => m.OrderId == order.OrderId)
        .Do(_ => Trace($"{nameof(PlaceOrder)}: {new { _.Order, _.Contract }}"))
        .Take(1);
      var oso = OrderStatusObservable.Select(_ => _.OrderId);
      var wte = IbClient.WireWithError(order.OrderId, obs, oso, _ => _.OrderId, e => OpenTradeError(contract, order, e, new { }));
      IbClient.ClientSocket.placeOrder(order.OrderId, contract, order);
      return wte.ToArray();
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
        _verbous(new { plaseOrder = new { o, c } });
        IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      });
      return null;
    }
    double OrderPrice(double orderPrice, Contract contract) {
      var minTick = contract.MinTick();
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
      OnWhatIf(() => OpenTradeWhatIf(pair, true).Subscribe());
      OnWhatIf(() => OpenTradeWhatIf(pair, false).Subscribe());
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
  public static class Mixins {
    public static void TryAdd(this List<OrdeContractHolder> source, OrdeContractHolder orderContractHolder) {
      source.ByOrderId(orderContractHolder.order.OrderId).RunIfEmpty(() => source.Add(orderContractHolder));
    }

    public static IEnumerable<T> ByOrderId<T>(this IEnumerable<OrdeContractHolder> source, int orderId, Func<OrdeContractHolder, T> map)
      => source.ByOrderId(orderId).Select(map);
    public static IEnumerable<OrdeContractHolder> ByOrderId(this IEnumerable<OrdeContractHolder> source, int orderId)
      => source.ToList().Where(och => och.order.OrderId == orderId);
    public static IEnumerable<OrdeContractHolder> ByLocalSymbool(this IEnumerable<OrdeContractHolder> source, string localSymbol)
      => source.ToList().Where(och => och.contract.LocalSymbol == localSymbol);
    public static bool IsOrderDone(this (string status, double remaining) order) =>
      EnumUtils.Contains<OrderCancelStatuses>(order.status) || EnumUtils.Contains<OrderDoneStatuses>(order.status) && order.remaining == 0;

    //public static void Verbous<T>(this T v)=>_ve
    public static bool IsPreSubmited(this IBApi.OrderState order) => order.Status == "PreSubmitted";

    public static bool IsSell(this IBApi.Order o) => o.Action == "SELL";
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
