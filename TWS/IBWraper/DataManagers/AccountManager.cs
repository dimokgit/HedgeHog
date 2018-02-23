using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog.Shared;
using IBApi;
using HedgeHog;
using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ReactiveUI;
using PosArg = System.Tuple<string, IBApi.Contract, IBApp.PositionMessage>;
using OpenOrderArg = System.Tuple<int, IBApi.Contract, IBApi.Order, IBApi.OrderState>;
using PositionHandker = System.Action<string, IBApi.Contract, double, double>;
using OrderStatusHandler = System.Action<int, string, double, double, double, int, int, double, int, string>;
using PortfolioHandler = System.Action<IBApi.Contract, double, double, double, double, double, double, string>;
using OpenOrderHandler = System.Action<int, IBApi.Contract, IBApi.Order, IBApi.OrderState>;
using static IBApp.AccountManager;

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
    private readonly ReactiveList<Trade> OpenTrades = new ReactiveList<Trade>();
    private readonly ReactiveList<Trade> ClosedTrades = new ReactiveList<Trade>();
    public Func<Trade, double> CommissionByTrade = t => t.Lots * .008;

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

      OpenTrades.ItemsAdded.Subscribe(RaiseTradeAdded).SideEffect(s => _strams.Add(s));
      OpenTrades.ItemsRemoved.Subscribe(RaiseTradeRemoved).SideEffect(s => _strams.Add(s));
      ClosedTrades.ItemsAdded.Subscribe(RaiseTradeClosed).SideEffect(s => _strams.Add(s));
      ibClient.Error += OnError;

      #region Observables
      var posObs = Observable.FromEvent<PositionHandker, (string account, Contract contract, double pos, double avgCost)>(
        onNext => (string a, Contract b, double c, double d) => onNext((a, b, c, d)),
        h => IbClient.Position += h,
        h => IbClient.Position -= h
        );
      var ooObs = Observable.FromEvent<OpenOrderHandler, (int orderId, Contract contract, IBApi.Order order, OrderState orderState)>(
        onNext => (int orderId, Contract contract, IBApi.Order order, OrderState orderState) => onNext((orderId, contract, order, orderState)),
        h => IbClient.OpenOrder += h,
        h => IbClient.OpenOrder -= h
        );
      var osObs = Observable.FromEvent<OrderStatusHandler, (int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld)>(
        onNext
        => (int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld)
        => onNext((orderId, status, filled, remaining, avgFillPrice, permId, parentId, lastFillPrice, clientId, whyHeld)),
        h => IbClient.OrderStatus += h,
        h => IbClient.OrderStatus -= h
        );

      var portObs = Observable.FromEvent<PortfolioHandler, (Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName)>(
        onNext => (Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName) => onNext((contract, position, marketPrice, marketValue, averageCost, unrealisedPNL, realisedPNL, accountName)),
        h => IbClient.UpdatePortfolio += h,
        h => IbClient.UpdatePortfolio -= h
        );

      #endregion
      #region Subscibtions
      posObs
        .Where(x => x.account == _accountId)
        .Do(x => _verbous("* " + new { Position = new { x.contract.LocalSymbol, x.pos, x.account } }))
        .Subscribe(a => OnPosition(a.contract, a.pos, a.avgCost))
        .SideEffect(s => _strams.Add(s));
      ooObs
        .Where(x => x.order.Account == _accountId)
        .DistinctUntilChanged(a => a.orderId)
        //.Do(x => _verbous("* " + new { OpenOrder = new { x.contract.LocalSymbol, x.order.OrderId } }))
        .Subscribe(a => OnOrderStartedImpl(a.orderId, a.contract, a.order, a.orderState))
        .SideEffect(s => _strams.Add(s));
      osObs
        .Where(t => _orderContracts.Any(oc => oc.Key == t.orderId && oc.Value.order.Account == _accountId))
        .Select(t => new { t.orderId, t.status, t.filled, t.remaining, t.whyHeld, isDone = (t.status, t.remaining).IsOrderDone() })
        .DistinctUntilChanged()
        .Do(x => _verbous("* " + new { OrderStatus = x, _orderContracts[x.orderId].order.Account }))
        .Do(t => _orderContracts.Where(oc => oc.Key == t.orderId && t.status != "Inactive").ForEach(oc => (t.status, t.filled, t.remaining, t.isDone).With(os => _orderStatuses.AddOrUpdate(oc.Value.contract.Symbol, i => os, (i, u) => os))))
        .Where(o => (o.status, o.remaining).IsOrderDone())
        .Subscribe(o => RaiseOrderRemoved(o.orderId))
        .SideEffect(s => _strams.Add(s));
      portObs
        .Where(x => x.accountName == _accountId)
        .Select(t => new { t.contract.LocalSymbol, t.position, t.unrealisedPNL,t.accountName })
        .Take(1)
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
    ConcurrentDictionary<int, (IBApi.Order order, IBApi.Contract contract)> _orderContracts = new ConcurrentDictionary<int, (IBApi.Order order, IBApi.Contract contract)>();
    ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)> _orderStatuses = new ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)>();
    public ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)> OrderStatuses { get => _orderStatuses; }

    private void RaiseOrderRemoved(int orderId) {
      if(_orderContracts.ContainsKey(orderId)) {
        var cd = _orderContracts[orderId];
        var o = cd.order;
        var c = cd.contract;
        RaiseOrderRemoved(new HedgeHog.Shared.Order {
          IsBuy = o.Action == "BUY",
          Lot = (int)o.TotalQuantity,
          Pair = c.Instrument,
          IsEntryOrder = IsEntryOrder(o)
        });
      }
    }

    #endregion

    #region Position
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
          .Select(ot => new Action(() => ot.Lots = posMsg.Quantity
            .SideEffect(Lots => _verbous(new { ChangePosition = new { ot.Pair, ot.IsBuy, Lots } }))))
          .DefaultIfEmpty(() => OpenTrades.Add(TradeFromPosition(contract, position, averageCost)
            .SideEffect(t => _verbous(new { OpenPosition = new { t.Pair, t.IsBuy, t.Lots } }))))
          .ToList()
          .ForEach(a => a());
      }

      TraceTrades("Positions: ", OpenTrades);
    }
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
      if(!_orderContracts.TryGetValue(reqId, out var oc)) return;
      IbClient.SetRequestHandled(reqId);
      if(new[] { 103, 110, 200, 201, 202, 203, 382, 383 }.Contains(code)) {
        _orderStatuses.TryRemove(oc.contract?.Symbol + "", out var os);
        RaiseOrderRemoved(reqId);
      }
      switch(code) {
        case 404:
          var contract = oc.contract + "";
          var order = oc.order + "";
          _verbous(new { contract, code, error, order });
          _defaultMessageHandler("Request Global Cancel");
          IbClient.ClientSocket.reqGlobalCancel();
          break;
      }
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

    private static bool IsEntryOrder(IBApi.Order o) => new[] { "MKT", "LMT" }.Contains(o.OrderType);
    private void OnOrderStartedImpl(int reqId, IBApi.Contract c, IBApi.Order o, IBApi.OrderState os) {
      if(!o.WhatIf) {
        _orderContracts.TryAdd(o.OrderId, (o, c));
        _verbous(new { OnOpenOrder = new { c, o, os } });
        RaiseOrderAdded(new HedgeHog.Shared.Order {
          IsBuy = o.Action == "BUY",
          Lot = (int)o.TotalQuantity,
          Pair = c.Instrument,
          IsEntryOrder = IsEntryOrder(o)
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
    public Trade[] GetTrades() { return OpenTrades.ToArray(); }
    public Trade[] GetClosedTrades() { return ClosedTrades.ToArray(); }
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
        Tif = "DAY",
        OutsideRth = isPreRTH,
        WhatIf = whatIf,
        OverridePercentageConstraints = true
      };
      if(orderType == "LMT") {
        var d = TradesManagerStatic.GetDigits(pair) - 1;
        var offset = isPreRTH ? 1.001 : 1;
        o.LmtPrice = Math.Round(buy ? price.Value.Ask * offset : price.Value.Bid / offset, d);
      }
      _orderContracts.TryAdd(o.OrderId, (o, c));
      _verbous(new { plaseOrder = new { o, c } });
      IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      return null;
    }
    public PendingOrder OpenSpreadTrade((string pair, bool buy, int lots)[] legs, double takeProfit, double stopLoss, string remark, bool whatIf) {
      var isStock = legs.All(l => l.pair.IsUSStock());
      var legs2 = legs.Select(t => (t.pair, t.buy, t.lots, price: IbClient.GetPrice(t.pair))).ToArray();
      var price = legs2[0].price;
      var rth = Lazy.Create(() => new[] { price.Time.Date.AddHours(9.5), price.Time.Date.AddHours(16) });
      var isPreRTH = !whatIf && isStock && !price.Time.Between(rth.Value);
      var orderType = "MKT";
      var c = ContractSamples.StockComboContract();
      var o = new IBApi.Order() {
        OrderId = NetOrderId(),
        Action = legs[0].buy ? "BUY" : "SELL",
        OrderType = orderType,
        TotalQuantity = legs[0].lots,
        Tif = "DAY",
        OutsideRth = isPreRTH,
        WhatIf = whatIf,
        OverridePercentageConstraints = true
      };
      _orderContracts.TryAdd(o.OrderId, (o, c));
      _verbous(new { plaseOrder = new { o, c } });
      IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      return null;
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
      .IfEmpty(() => {
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
    private void TraceTrades(string label, IEnumerable<Trade> trades) => _defaultMessageHandler(label + (trades.Count() > 1 ? "\n" : "") + string.Join("\n", trades.Select(ot => new { ot.Pair, ot.Position, ot.Time, ot.Commission })));
    public override string ToString() => new { IbClient, CurrentAccount = _accountId } + "";
    #endregion
  }
  public static class Mixins {
    public static bool IsOrderDone(this (string status, double remaining) order) =>
      EnumUtils.Contains<OrderCancelStatuses>(order.status) || EnumUtils.Contains<OrderDoneStatuses>(order.status) && order.remaining == 0;

    //public static void Verbous<T>(this T v)=>_ve
    public static bool IsPreSubmited(this IBApi.OrderState order) => order.Status == "PreSubmitted";

    public static bool IsBuy(this IBApi.Order o) => o.Action == "BUY";

    private static (string symbol, bool isBuy) Key(string symbol, bool isBuy) => (symbol.WrapPair(), isBuy);
    private static (string symbol, bool isBuy) Key2(string symbol, bool isBuy) => Key(symbol, !isBuy);

    public static (string symbol, bool isBuy) Key(this PositionMessage t) => Key(t.Contract.LocalSymbol, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this PositionMessage t) => Key2(t.Contract.LocalSymbol, t.IsBuy);

    public static (string symbol, bool isBuy) Key(this Trade t) => Key(t.Pair, t.IsBuy);
    public static (string symbol, bool isBuy) Key2(this Trade t) => Key2(t.Pair, t.IsBuy);
  }
}
