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
    public enum OrderCancelStatuses { Cancelled };
    public enum OrderDoneStatuses { Filled };
    public enum OrderHeldReason { locate };

    #region Constants
    private const int ACCOUNT_ID_BASE = 50000000;

    private const string ACCOUNT_SUMMARY_TAGS = "AccountType,NetLiquidation,TotalCashValue,SettledCash,AccruedCash,BuyingPower,EquityWithLoanValue,PreviousEquityWithLoanValue,"
             + "GrossPositionValue,ReqTEquity,ReqTMargin,SMA,InitMarginReq,MaintMarginReq,AvailableFunds,ExcessLiquidity,Cushion,FullInitMarginReq,FullMaintMarginReq,FullAvailableFunds,"
             + "FullExcessLiquidity,LookAheadNextChange,LookAheadInitMarginReq ,LookAheadMaintMarginReq,LookAheadAvailableFunds,LookAheadExcessLiquidity,HighestSeverity,DayTradesRemaining,Leverage";
    private const string GTC = "GTC";

    //private const int BaseUnitSize = 1;
    #endregion

    #region Fields
    private bool accountSummaryRequestActive = false;
    private bool accountUpdateRequestActive = false;
    private string _accountId;
    private readonly Action<object> _defaultMessageHandler;
    private bool _useVerbouse = true;
    private Action<object> _verbous => _useVerbouse ? _defaultMessageHandler : o => { };
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
    public AccountManager(IBClientCore ibClient, string accountId, Func<string, Trade> createTrade, Func<Trade, double> commissionByTrade, Action<object> onMessage) : base(ibClient, ACCOUNT_ID_BASE) {
      CommissionByTrade = commissionByTrade;
      CreateTrade = createTrade;
      Account = new Account();
      _accountId = accountId;
      _defaultMessageHandler = onMessage ?? new Action<object>(o => { throw new NotImplementedException(new { onMessage } + ""); });

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
        .Where(e=>e.PropertyName=="Lots")
        .Select(e=>e.Sender)
        .Subscribe(RaiseTradeChanged)
        .SideEffect(s => _strams.Add(s));
      OpenTrades.ItemsRemoved.Subscribe(RaiseTradeRemoved).SideEffect(s => _strams.Add(s));
      ClosedTrades.ItemsAdded.Subscribe(RaiseTradeClosed).SideEffect(s => _strams.Add(s));
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
        .Throttle(TimeSpan.FromSeconds(2))
        .Subscribe(_ => {
          var combosAll = ComboTradesAllImpl().ToArray();
          Trace(new { combosAll = combosAll.Flatter("") });
          combosAll
          .Do(comboAll => Trace(new { comboAll }))
          .Where(ca => ca.orderId == 0)
          .ForEach(ca => {
            CancelAllOrders("Updating combo exit");
            OpenOrUpdateLimitOrderByProfit(ca.contract.Instrument, ca.position, 0, ca.open, 0.2);
          });
        }).SideEffect(s => _strams.Add(s));
      OpenOrderObservable
        .Where(x => x.order.Account == _accountId)
        .Do(UpdateOrder)
        .Distinct(a => a.orderId)
        //.Do(x => _verbous("* " + new { OpenOrder = new { x.contract.LocalSymbol, x.order.OrderId } }))
        .Subscribe(a => OnOrderStartedImpl(a.orderId, a.contract, a.order, a.orderState))
        .SideEffect(s => _strams.Add(s));
      osObs
        .Do(t => _verbous(t))
        .Where(t => UseOrderContracts(ocs => ocs.Where(oc => oc.Key == t.orderId && oc.Value.order.Account == _accountId)).Concat().Any())
        .Select(t => new { t.orderId, t.status, t.filled, t.remaining, t.whyHeld, isDone = (t.status, t.remaining).IsOrderDone() })
        .Distinct()
        .Do(x => UseOrderContracts(oc => _verbous("* " + new { OrderStatus = x, oc[x.orderId].order.Account })))
        .Do(t => UseOrderContracts(ocs => ocs.Where(oc => oc.Key == t.orderId && t.status != "Inactive")
          .ForEach(oc => oc.Value.status = (t.status, t.filled, t.remaining, t.isDone))
        ))
        .Where(o => (o.status, o.remaining).IsOrderDone())
        .SelectMany(o => UseOrderContracts(ocs => ocs.Where(oc => oc.Key == o.orderId)).Concat())
        .Subscribe(o => RaiseOrderRemoved(o.Value))
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

      _defaultMessageHandler($"{nameof(AccountManager)}:{_accountId} is ready");
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
              .Subscribe(s => _defaultMessageHandler(s), exc => { });
          }
        return _TraceSubject;
      }
    }
    void OnTraceSubject(object p) {
      TraceSubject.OnNext(p);
    }
    #endregion



    #region OrderStatus
    public class OrdeContractHolder {
      public readonly IBApi.Order order;
      public readonly IBApi.Contract contract;
      (string status, double filled, double remaining, bool isDone) _status;
      public (string status, double filled, double remaining, bool isDone) status {
        get { return _status; }
        set { _status = value; }
      }
      public bool isDone => status.isDone;
      public OrdeContractHolder(IBApi.Order order, IBApi.Contract contract) {
        this.order = order;
        this.contract = contract;
        this.status = ("new", 0, order.TotalQuantity, false);
      }
    }
    public IEnumerable<T> UseOrderContracts<T>(Func<ConcurrentDictionary<int, OrdeContractHolder>, T> func, int timeoutInMilliseconds = 3000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      if(!Monitor.TryEnter(_OpenTradeSync, timeoutInMilliseconds)) {
        throw new TimeoutException(message);
      }
      Stopwatch sw = Stopwatch.StartNew();
      T ret;
      try {
        ret = func(OrderContractsInternal);
      } catch(Exception exc) {
        Trace(exc);
        yield break;
      } finally {
        Monitor.Exit(_OpenTradeSync);
        if(sw.ElapsedMilliseconds > timeoutInMilliseconds) {
          Trace(message + $" SpentMoreThen {timeoutInMilliseconds} ms");
        }
      }
      yield return ret;
    }
    public void UseOrderContracts(Action<ConcurrentDictionary<int, OrdeContractHolder>> action, [CallerMemberName] string Caller = "") {
      Func<ConcurrentDictionary<int, OrdeContractHolder>, Unit> f = rates => { action(rates); return Unit.Default; };
      UseOrderContracts(f, 3000, Caller).Count();
    }

    public ConcurrentDictionary<int, OrdeContractHolder> OrderContractsInternal { get; } = new ConcurrentDictionary<int, OrdeContractHolder>();
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

    public (Contract contract, int position, double open, double price)
      ContractPosition((IBApi.Contract contract, double pos, double avgCost) p) =>
       (p.contract, position: p.pos.ToInt(), open: p.avgCost * p.pos, p.avgCost / p.contract.ComboMultiplier);

    ConcurrentDictionary<string, (Contract contract, int position, double open, double price)> _positions = new ConcurrentDictionary<string, (Contract contract, int position, double open, double price)>();
    public ICollection<(Contract contract, int position, double open, double price)> Positions => _positions.Values;
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
      UseOrderContracts(orderContracts => {
        if(!orderContracts.TryGetValue(reqId, out var oc)) return;
        if(new[] { 202 }.Contains(code)) {
          RaiseOrderRemoved(oc);
          orderContracts.TryRemove(reqId, out var oc2);
        }
        switch(code) {
          case 404:
            var contract = oc.contract + "";
            var order = oc.order + "";
            _verbous(new { contract, code, error, order });
            _defaultMessageHandler("Request Global Cancel");
            CancelAllOrders("Request Global Cancel");
            break;
        }
      });
    }
    #endregion

    #region IDisposable Support
    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
      if(!disposedValue) {
        if(disposing) {
          // TODO: dispose managed state (managed objects).
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

    private void UpdateOrder((int orderId, Contract contract, IBApi.Order order, OrderState orderState) t) {
      UseOrderContracts(orderContracts =>
        orderContracts.AddOrUpdate(t.orderId, new OrdeContractHolder(t.order, t.contract), (id, och) => {
          och.order.LmtPrice = t.order.LmtPrice;
          return och;
        }));
    }

    private static bool IsEntryOrder(IBApi.Order o) => new[] { "MKT", "LMT" }.Contains(o.OrderType);
    private void OnOrderStartedImpl(int reqId, IBApi.Contract c, IBApi.Order o, IBApi.OrderState os) {
      if(!o.WhatIf) {
        UseOrderContracts(orderContracts => {
          orderContracts.TryAdd(o.OrderId, new OrdeContractHolder(o, c));
          _verbous(new { OnOpenOrderImpl = new { c, o, os } });
          RaiseOrderAdded(new HedgeHog.Shared.Order {
            IsBuy = o.Action == "BUY",
            Lot = (int)o.TotalQuantity,
            Pair = c.Instrument,
            IsEntryOrder = IsEntryOrder(o)
          });
        });
      } else if(GetTrades().IsEmpty()) {
        //RaiseOrderRemoved(o.OrderId);
        var offer = TradesManagerStatic.GetOffer(c.Instrument);
        var isBuy = o.IsBuy();
        var levelrage = (o.LmtPrice * o.TotalQuantity) / (double.Parse(os.InitMargin) - InitialMarginRequirement);
        if(levelrage != 0 && !double.IsInfinity(levelrage))
          if(isBuy) {
            offer.MMRLong = 1 / levelrage;
            _defaultMessageHandler(new { offer = new { offer.Pair, offer.MMRLong } });
          } else {
            offer.MMRShort = 1 / levelrage;
            _defaultMessageHandler(new { offer = new { offer.Pair, offer.MMRShort } });
          }
      }
    }
    #endregion

    #region Trades
    public IList<Trade> GetTrades() { return OpenTrades.ToList(); }
    public IList<Trade> GetClosedTrades() { return ClosedTrades.ToList(); }
    #endregion

    Action IfEmpty(object o) => () => throw new Exception(o.ToJson());
    #region Butterfly

    #endregion

    #region OpenOrder
    private int NetOrderId() => IbClient.ValidOrderId();
    public PendingOrder OpenTradeWhatIf(string pair, bool buy) {
      var anount = GetTrades().Where(t => t.Pair == pair).Select(t => t.GrossPL).DefaultIfEmpty(Account.Equity).Sum() / 2;
      return OpenTrade(pair, buy, pair.IsFuture() ? 1 : 100, 0, 0, "", null, true);
    }
    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price) {
      return OpenTrade(pair, buy, lots, takeProfit, stopLoss, remark, price, false);
    }
    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price_, bool whatIf) {
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
        orderContracts.TryAdd(o.OrderId, new OrdeContractHolder(o, c));
        _verbous(new { plaseOrder = new { o, c } });
        IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      });
      return null;
    }
    public void OpenOrUpdateLimitOrderByProfit(string instrument, int position, int orderId, double openAmount, double profitAmount) {
      UseOrderContracts(orderContracts => {
        var pa = profitAmount >= 1 ? profitAmount : openAmount.Abs() * profitAmount;
        if(orderContracts.TryGetValue(orderId, out var och) && !och.isDone) {
          if(och.contract.Instrument != instrument)
            throw new Exception($"{nameof(OpenOrUpdateLimitOrderByProfit)}:{new { orderId, och.contract.Instrument, dontMatch = instrument }}");
          var limit = OrderPrice(priceFromProfit(pa, position, och.contract.ComboMultiplier, openAmount), och.contract);
          UpdateOrder(orderId, limit);
        } else { // Create new order
          Contract.FromCache(instrument)
            .Count(1, new { OpenOrUpdateOrder = new { instrument, unexpected = "count in cache" } })
            .ForEach(c => {
              var lmtPrice = OrderPrice(priceFromProfit(pa, position, c.ComboMultiplier, openAmount), c);
              OpenTrade(c, -position, lmtPrice, false);
            });
        }
      });
    }
    public void OpenOrUpdateLimitOrder(string instrument, int position, int orderId, double lmpPrice) {
      UseOrderContracts(orderContracts => {
        if(orderContracts.TryGetValue(orderId, out var och) && !och.isDone) {
          if(och.contract.Instrument != instrument)
            throw new Exception($"{nameof(OpenOrUpdateLimitOrder)}:{new { orderId, och.contract.Instrument, dontMatch = instrument }}");
          UpdateOrder(orderId, OrderPrice(lmpPrice, och.contract));
        } else {
          Contract.FromCache(instrument)
            .Count(1, new { OpenOrUpdateOrder = new { instrument, unexpected = "count in cache" } })
            .ForEach(c => OpenTrade(c, -position, lmpPrice, false));
        }
      });
    }
    public void UpdateOrder(int orderId, double lmpPrice, int minTickMultiplier = 1) {
      UseOrderContracts(orderContracts => {
        if(!orderContracts.TryGetValue(orderId, out var och))
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
          OnUpdateError(e, $"{nameof(UpdateOrder)}:{och.contract}:{new { order.LmtPrice }}");
          if(e.errorCode == 110)
            UpdateOrder(orderId, lmpPrice, ++minTickMultiplier);
        }, () => { });
        IbClient.ClientSocket.placeOrder(order.OrderId, och.contract, order);
      });
    }
    private void OnUpdateError((int reqId, int code, string error, Exception exc) e, string trace) {
      UseOrderContracts(orderContracts => {
        Trace(trace + e);
        if(!orderContracts.TryGetValue(e.reqId, out var oc)) return;
        if(new[] { /*103, 110,*/ 200, 201, 202, 203, 321, 382, 383 }.Contains(e.code)) {
          //OrderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
          RaiseOrderRemoved(oc);
          orderContracts.TryRemove(e.reqId, out var oc2);
        }
      });
    }
    private void OnOpenError((int reqId, int code, string error, Exception exc) e, string trace) {
      UseOrderContracts(orderContracts => {
        Trace(trace + e);
        if(!orderContracts.TryGetValue(e.reqId, out var oc)) return;
        if(new[] { 103, 110, 200, 201, 203, 321, 382, 383 }.Contains(e.code)) {
          //OrderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
          RaiseOrderRemoved(oc);
          orderContracts.TryRemove(e.reqId, out var oc2);
        }
        switch(e.code) {
          case 404:
            var contract = oc.contract + "";
            var order = oc.order + "";
            _defaultMessageHandler("Requesting Global Cancel should be initiated");
            //IbClient.ClientSocket.reqGlobalCancel();
            break;
        }
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
        orderContracts.TryAdd(o.OrderId, new OrdeContractHolder(o, c));
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
              .Subscribe(a => a(), exc => _defaultMessageHandler(exc));
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
        _defaultMessageHandler(nameof(FetchMMR) + " started");
        TradesManagerStatic.dbOffers.Where(o => !o.Pair.IsCurrenncy()).ToObservable().Subscribe(o => FetchMMR(o.Pair));
      })
      .ForEach(t => OnTraceSubject(new { FetchMMRs = new { t.Pair, t.IsBuy, t.Lots, Message = "Won't run" } }));
    #endregion

    #region Overrrides/helpers
    private static Func<Trade, bool> IsEqual(PositionMessage position) => ot => ot.Key().Equals(position.Key());
    private static Func<Trade, bool> IsEqual2(PositionMessage position) => ot => ot.Key2().Equals(position.Key());
    private static Func<Trade, bool> IsEqual2(Trade trade) => ot => ot.Key().Equals(trade.Key2());

    private int CloseTrade(DateTime execTime, double execPrice, Trade closedTrade, int closeLots) {
      if(closeLots >= closedTrade.Lots) {// Full close
        closedTrade.Time2Close = execTime;
        closedTrade.Close = execPrice;

        if(!OpenTrades.Remove(closedTrade))
          throw new Exception($"Couldn't remove {nameof(closedTrade)} from {nameof(OpenTrades)}");
        ClosedTrades.Add(closedTrade);
        return closeLots - closedTrade.Lots;
      } else {// Partial close
        var trade = closedTrade.Clone();
        trade.CommissionByTrade = closedTrade.CommissionByTrade;
        trade.Lots = closeLots;
        trade.Time2Close = execTime;
        trade.Close = execPrice;
        closedTrade.Lots -= trade.Lots;
        ClosedTrades.Add(trade);
        return 0;
      }
    }
    private void TraceTrades(string label, IEnumerable<Trade> trades)
      => _defaultMessageHandler(label
        + (trades.Count() > 1 ? "\n" : "")
        + string.Join("\n", trades.OrderBy(t => t.Pair).Select(ot => new { ot.Pair, ot.Position, ot.Open, ot.Time, ot.Commission })));
    public override string ToString() => new { IbClient, CurrentAccount = _accountId } + "";
    #endregion
  }
  static class Mixins {
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
