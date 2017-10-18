/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBSampleApp.util;
using static HedgeHog.Core.JsonExtensions;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using HedgeHog;
using System.Collections.Concurrent;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ReactiveUI;
using PosArg = System.Tuple<string, IBApi.Contract, IBApp.PositionMessage>;
using System.Diagnostics;
using OpenOrderArg = System.Tuple<int, IBApi.Contract, IBApi.Order, IBApi.OrderState>;
using OrderStatusArg = System.Tuple<int, string, double, double, bool, double>;
namespace IBApp {
  public class AccountManager :DataManager {
    public enum OrderStatuses { Cancelled, Inactive, PreSubmitted };

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
    private bool _useVerbouse = false;
    private Action<object> _verbous => _useVerbouse ? _defaultMessageHandler : o => { };
    private readonly string _accountCurrency = "USD";
    #endregion

    #region Properties
    public Account Account { get; private set; }
    private readonly ReactiveList<Trade> OpenTrades = new ReactiveList<Trade>();
    private readonly ConcurrentDictionary<string, PositionMessage> _positions = new ConcurrentDictionary<string, PositionMessage>();
    private readonly ReactiveList<Trade> ClosedTrades = new ReactiveList<Trade>();
    public Func<Trade, double> CommissionByTrade = t => t.Lots * .008;
    IObservable<(Offer o, bool b)> _offerMMRs = TradesManagerStatic.dbOffers
        .Select(o => new[] { (o, b: true), (o, b: false) })
        .Concat()
        .ToObservable();

    #endregion

    #region Methods

    #endregion

    #region Ctor
    public AccountManager(IBClientCore ibClient, string accountId, Func<Trade, double> commissionByTrade, Action<object> onMessage) : base(ibClient, ACCOUNT_ID_BASE) {
      CommissionByTrade = commissionByTrade;
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
      ibClient.OpenOrder += OnOpenOrder;
      ibClient.OrderStatus += OnOrderStatus;
      //IbClient.ExecDetails += OnExecution;
      IbClient.Position += OnPosition;
      //ibClient.UpdatePortfolio += IbClient_UpdatePortfolio;
      OpenTrades.ItemsAdded.Subscribe(RaiseTradeAdded);
      OpenTrades.ItemsRemoved.Subscribe(RaiseTradeRemoved);
      ClosedTrades.ItemsAdded.Subscribe(RaiseTradeClosed);
      ibClient.Error += OnError;
      _defaultMessageHandler(nameof(AccountManager) + " is ready");
    }

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
    void OnWhatIf(Action a) {
      WhatIfSubject.OnNext(a);
    }
    #endregion

    private void FetchMMR(string pair) {
      OnWhatIf(() => OpenTradeWhatIf(pair, true));
      OnWhatIf(() => OpenTradeWhatIf(pair, false));
    }

    private void OnError(int reqId, int code, string error, Exception exc) {
      if(new[] { 110, 382, 383 }.Contains(code))
        RaiseOrderRemoved(reqId);
      if(_orderContracts.ContainsKey(reqId + "")) {
        var contract = _orderContracts[reqId + ""].contract + "";
        var order = _orderContracts[reqId + ""].order + "";
        Trace(new { contract, code, error, order });
      }
    }

    /*
    public bool CloseTrade(Trade trade, int lot, Price price) {
      if(trade.Lots <= lot)
        CloseTrade(trade);
      else {
        var newTrade = trade.Clone();
        newTrade.Lots = trade.Lots - lot;
        newTrade.Id = NewTradeId() + "";
        var e = new PriceChangedEventArgs(trade.Pair, price ?? GetPrice(trade.Pair), GetAccount(), GetTrades());
        newTrade.UpdateByPrice(this, e);
        trade.Lots = lot;
        trade.UpdateByPrice(this, e);
        CloseTrade(trade);
        tradesOpened.Add(newTrade);
      }
      return true;
    }
    */
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
      trade.Kind = PositionBase.PositionKind.Closed;
      trade.TradesManager = null;
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
      if(OrderAddedEvent != null)
        OrderAddedEvent(this, new OrderEventArgs(Order));
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
    #region OpenOrder Subject
    object _OpenOrderSubjectLocker = new object();
    ISubject<OpenOrderArg> _OpenOrderSubject;

    ISubject<OpenOrderArg> OpenOrderSubject {
      get {
        lock(_OpenOrderSubjectLocker)
          if(_OpenOrderSubject == null) {
            _OpenOrderSubject = new Subject<OpenOrderArg>();
            _OpenOrderSubject
              //.Do(t => Verbous(new { OpenOrderSubject = new { reqId = t.Item1, status = t.Item4 } }))
              //.Where(t => t.Item4.Status == "Filled")
              .DistinctUntilChanged(t => t.Item1)
              .Subscribe(t => OnOrderStartedImpl(t.Item1, t.Item2, t.Item3, t.Item4), exc => _defaultMessageHandler(exc), () => _defaultMessageHandler(nameof(OpenOrderSubject) + " is gone"));
          }
        return _OpenOrderSubject;
      }
    }
    void OnOpenOrderPush(OpenOrderArg p) => OpenOrderSubject.OnNext(p);
    #endregion
    private static bool IsEntryOrder(IBApi.Order o) => new[] { "MKT", "LMT" }.Contains(o.OrderType);
    private void OnOpenOrder(int reqId, IBApi.Contract c, IBApi.Order o, IBApi.OrderState os) =>
      OnOpenOrderPush(Tuple.Create(reqId, c, o, os));
    private void OnOrderStartedImpl(int reqId, IBApi.Contract c, IBApi.Order o, IBApi.OrderState os) {
      if(!os.IsPreSubmited()) {
        _orderContracts.TryAdd(o.OrderId + "", (o, c));
        _verbous(new { OnOpenOrder = new { reqId, c, o, os } });
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
    #region OrderStatus
    #region OrderStatus Subject
    object _OrderStatusSubjectLocker = new object();
    ISubject<OrderStatusArguments> _OrderStatusSubject;

    ISubject<OrderStatusArguments> OrderStatusSubject {
      get {
        lock(_OrderStatusSubjectLocker)
          if(_OrderStatusSubject == null) {
            _OrderStatusSubject = new Subject<OrderStatusArguments>();
            _OrderStatusSubject
              //.Do(t => Verbous(new { OrderStatusSubject = new { reqId = t.Item1, status = t.Item4 } }))
              .Where(t => t.IsDone)
              .DistinctUntilChanged(a => new { a.OrderId, a.Status, a.Remaining })
              .Subscribe(t => OnOrderStatusImpl(t), exc => _defaultMessageHandler(exc), () => _defaultMessageHandler(nameof(OrderStatusSubject) + " is gone"));
          }
        return _OrderStatusSubject;
      }
    }
    void OnOrderStatusPush(OrderStatusArguments p) {
      OrderStatusSubject.OnNext(p);
    }
    #endregion
    class OrderStatusArguments {

      public OrderStatusArguments(int orderId,Contract contract, string status, double filled, double remaining, double avgFillPrice) {
        OrderId = orderId;
        Contract = contract;
        Status = status;
        Filled = filled;
        Remaining = remaining;
        AvgFillPrice = avgFillPrice;
      }

      public int OrderId { get; private set; }
      public Contract Contract { get; private set; }
      public string Status { get; private set; }
      public double Filled { get; private set; }
      public double Remaining { get; private set; }
      public double AvgFillPrice { get; private set; }
      public bool IsDone => Enum.GetNames(typeof(OrderStatuses)).Contains(Status) || "Filled" == Status && Remaining == 0;
      public override string ToString() => new { OrderId, Contract.Symbol, Status, Filled, Remaining, IsDone } + "";
    }
    ConcurrentDictionary<string, (IBApi.Order order, IBApi.Contract contract)> _orderContracts = new ConcurrentDictionary<string, (IBApi.Order order, IBApi.Contract contract)>();
    private void OnOrderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld) {
      _orderContracts.TryGetValue(orderId + "", out var c);
      OnOrderStatusPush(new OrderStatusArguments(orderId, c.contract, status, filled, remaining, avgFillPrice));
      _verbous(new { OrderStatus = status,c.contract?.Symbol, orderId, filled, rem = remaining, permId, parentId, price = lastFillPrice, clientId, whyHeld });
    }
    private void OnOrderStatusImpl(OrderStatusArguments arg) {
      _defaultMessageHandler(new { OrderStatusImpl = arg });
      DoOrderStatus(arg.OrderId + "", arg.AvgFillPrice, arg.Filled.ToInt());
      RaiseOrderRemoved(arg.OrderId);
    }

    private void RaiseOrderRemoved(int orderId) {
      if(_orderContracts.ContainsKey(orderId + "")) {
        var cd = _orderContracts[orderId + ""];
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

    private void DoOrderStatus(string orderId, double avgPrice, int lots) {
      try {
        //_verbous(new { OnExecDetails = new { reqId, contract, execution } });
        var order = _orderContracts[orderId].order;
        var symbol = _orderContracts[orderId].contract.Instrument;
        var execTime = IbClient.ServerTime;
        var execPrice = avgPrice;

        #region Create Trade

        var trade = Trade.Create(IbClient, symbol, TradesManagerStatic.GetPointSize(symbol), TradesManagerStatic.GetBaseUnitSize(symbol), null);
        trade.Id = DateTime.Now.Ticks + "";
        trade.Buy = order.Action == "BUY";
        trade.IsBuy = trade.Buy;
        trade.Time2 = execTime;
        trade.Time2Close = execTime;
        trade.Open = trade.Close = execPrice;

        trade.Lots = lots;

        trade.OpenOrderID = orderId + "";
        trade.CommissionByTrade = CommissionByTrade;
        #endregion

        #region Close Trades
        OpenTrades
          .ToArray()
          .Where(t => t.OpenOrderID != orderId)
          .Where(IsNotEqual(trade))
          .Scan(0, (_, otClose) => {
            var cL = CloseTrade(execTime, execPrice, otClose, trade.Lots);
            return trade.Lots = cL;
          })
          .TakeWhile(l => l > 0)
          .Count();
        #endregion

        if(trade.Lots > 0) {
          //var oldTrade = OpenTrades.SingleOrDefault(t => t.OpenOrderID == orderId);
          //if(oldTrade != null) {
          //  oldTrade.Lots += trade.Lots;
          //  oldTrade.Open = execution.AvgPrice;
          //} else
          OpenTrades.Add(trade);
        }

        OpenTrades
          .Where(t => t.Lots == 0)
          .ToList()
          .ForEach(t => OpenTrades.Remove(t));

        //TraceTrades("Opened: ", OpenTrades);
        //TraceTrades("Closed: ", ClosedTrades);
      } catch(Exception exc) {
        _defaultMessageHandler(exc);
      }
      int CloseTrade(DateTime execTime, double execPrice, Trade closedTrade, int closeLots) {
        if(closeLots >= closedTrade.Lots) {// Full close
          closedTrade.Time2Close = execTime;
          closedTrade.Close = execPrice;

          ClosedTrades.Add(closedTrade);
          if(!OpenTrades.Remove(closedTrade))
            throw new Exception($"Couldn't remove {nameof(closedTrade)} from {nameof(OpenTrades)}");
          return closeLots - closedTrade.Lots;
        } else {// Partial close
          var trade = closedTrade.Clone();
          trade.CommissionByTrade = closedTrade.CommissionByTrade;
          trade.Lots = closeLots;
          trade.Time2Close = execTime;
          trade.Close = execPrice;
          closedTrade.Lots -= trade.Lots;
          if(trade.Lots > 0)
            ClosedTrades.Add(trade);
          return 0;
        }
      }
    }
    #endregion

    #region Trades
    public Trade[] GetTrades() { return OpenTrades.ToArray(); }
    public Trade[] GetClosedTrades() { return ClosedTrades.ToArray(); }
    #endregion
    public class PositionNotFoundException :Exception {
      public PositionNotFoundException(string symbol) : base(new { symbol } + "") { }
    }

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
      _orderContracts.TryAdd(o.OrderId + "", (o, c));
      _verbous(new { plaseOrder = new { o, c } });
      IbClient.ClientSocket.placeOrder(o.OrderId, c, o);
      return null;
    }
    #endregion

    #region IB Handlers
    #region Execution Subject
    object _ExecutionSubjectLocker = new object();
    ISubject<Tuple<int, Contract, Execution>> _ExecutionSubject;
    ISubject<Tuple<int, Contract, Execution>> ExecutionSubject {
      get {
        lock(_ExecutionSubjectLocker)
          if(_ExecutionSubject == null) {
            _ExecutionSubject = new Subject<Tuple<int, Contract, Execution>>();
            _ExecutionSubject
              //.Where(t => t.Item1 == -1)
              //.DistinctUntilChanged(t => {
              //  //_defaultMessageHandler(new { ExecutionSubject = new { OrderId = t.Item3?.OrderId } });
              //  return t.Item3?.ExecId;
              //})
              .Subscribe(s => OnExecDetails(s.Item1, s.Item2, s.Item3), exc => _defaultMessageHandler(exc), () => _defaultMessageHandler(new { _defaultMessageHandler = "Completed" }));
          }
        return _ExecutionSubject;
      }
    }
    void OnExecution(int reqId, Contract contract, Execution execution) {
      ExecutionSubject.OnNext(Tuple.Create(reqId, contract, execution));
    }
    #endregion
    ConcurrentDictionary<string, int> _executions = new ConcurrentDictionary<string, int>();
    private void OnExecDetails(int reqId, Contract contract, Execution execution) {
      try {
        //_verbous(new { OnExecDetails = new { reqId, contract, execution } });
        var symbol = contract.Instrument;
        var execTime = execution.Time.FromTWSString();
        var execPrice = execution.AvgPrice;
        var orderId = execution.OrderId + "";

        #region Create Trade

        var trade = Trade.Create(IbClient, symbol, TradesManagerStatic.GetPointSize(symbol), TradesManagerStatic.GetBaseUnitSize(symbol), null);
        trade.Id = execution.PermId + "";
        trade.Buy = execution.Side == "BOT";
        trade.IsBuy = trade.Buy;
        trade.Time2 = execTime;
        trade.Time2Close = execution.Time.FromTWSString();
        trade.Open = trade.Close = execPrice;

        _executions.TryGetValue(orderId, out int orderLot);
        trade.Lots = execution.CumQty - orderLot;
        _executions.AddOrUpdate(orderId, execution.CumQty, (s, i) => execution.CumQty);

        trade.OpenOrderID = execution.OrderId + "";
        trade.CommissionByTrade = CommissionByTrade;
        #endregion

        #region Close Trades
        OpenTrades
          .ToArray()
          .Where(t => t.OpenOrderID != orderId)
          .Where(IsNotEqual(trade))
          .Scan(0, (lots, otClose) => {
            var cL = CloseTrade(execTime, execPrice, otClose, trade.Lots);
            return trade.Lots = cL;
          })
          .TakeWhile(l => l > 0)
          .Count();
        #endregion

        if(trade.Lots > 0) {
          //var oldTrade = OpenTrades.SingleOrDefault(t => t.OpenOrderID == orderId);
          //if(oldTrade != null) {
          //  oldTrade.Lots += trade.Lots;
          //  oldTrade.Open = execution.AvgPrice;
          //} else
          OpenTrades.Add(trade);
        }

        OpenTrades
          .Where(t => t.Lots == 0)
          .ToList()
          .ForEach(t => OpenTrades.Remove(t));

        PositionMessage position;
        if(!_positions.TryGetValue(contract.LocalSymbol, out position))
          throw new PositionNotFoundException(symbol);
        //var tradeByPosition = TradeFromPosition(contract.LocalSymbol, position, execTime,orderId);
        //TraceTrades("tradeByPositiont:", new[] { tradeByPosition });

        //var openLots = (from ot in OpenTrades
        //                where tradeByPosition.Pair == ot.Pair
        //                group ot by ot.IsBuy into g
        //                select new { isBuy = g.Key, sum = g.Sum(t => t.Lots) }
        //                ).ToArray();
        //try {
        //  Passager.ThrowIf(() => openLots.Length > 1);
        //  openLots.ForEach(openLot => {
        //    Passager.ThrowIf(() => openLot.isBuy != position.IsBuy);
        //    Passager.ThrowIf(() => openLot.sum != position.Quantity);
        //  });
        //}catch(Exception exc) {
        //  _defaultMessageHandler(exc);
        //  OpenTrades.ToList().ForEach(ot => CloseTrade(execTime, execPrice, ot, ot.Lots));
        //  OpenTrades.Add(tradeByPosition);
        //}
        TraceTrades("Opened: ", OpenTrades);
        TraceTrades("Closed: ", ClosedTrades);
      } catch(Exception exc) {
        _defaultMessageHandler(exc);
      }
    }

    private static Func<Trade, bool> IsEqual(PositionMessage position) => ot => ot.Key().Equals(position.Key());
    private static Func<Trade, bool> IsNotEqual(Trade trade) => ot => ot.Key().Equals(trade.Key2());

    private int CloseTrade(DateTime execTime, double execPrice, Trade closedTrade, int closeLots) {
      if(closeLots >= closedTrade.Lots) {// Full close
        closedTrade.Time2Close = execTime;
        closedTrade.Close = execPrice;

        ClosedTrades.Add(closedTrade);
        if(!OpenTrades.Remove(closedTrade))
          throw new Exception($"Couldn't remove {nameof(closedTrade)} from {nameof(OpenTrades)}");
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

    /*
 Trade ctAdd;
 if(OpenTrades.TryRemove(trade.Key2(), out ctAdd)) {
   ctAdd.Time2 = IbClient.ServerTime;
   ClosedTrades.Add(ctAdd);
 }
*/
    #region Position
    #region Position Subject - Fires once
    object _PositionSubjectLocker = new object();
    ISubject<PosArg> _PositionSubject;
    ISubject<PosArg> PositionSubject {
      get {
        lock(_PositionSubjectLocker)
          if(_PositionSubject == null) {
            _PositionSubject = new Subject<PosArg>();
            _PositionSubject
              .DistinctUntilChanged(t => new { t.Item2.LocalSymbol, t.Item3.Position })
              .Where(x => x.Item3.Position != 0)
              .Timeout(TimeSpan.FromSeconds(10))
              .Catch(new Func<Exception, IObservable<PosArg>>(e => new PosArg[] { null }.ToObservable()))
              .TakeWhile(pa => pa != null)
              .Subscribe(s => OnFirstPosition(s.Item2, s.Item3)
                , exc => _defaultMessageHandler(exc)
                , () => {
                  _defaultMessageHandler($"{nameof(PositionSubject)} is done.");
                  FetchMMRs();
                });
          }
        return _PositionSubject;
      }
    }

    public double InitialMarginRequirement { get; private set; }

    void OnPositionSubject(string account, Contract contract, PositionMessage pm) {
      PositionSubject.OnNext(Tuple.Create(account, contract, pm));
    }
    #endregion

    private void OnPosition(string account, Contract contract, double pos, double avgCost) {
      var position = new PositionMessage(account, contract, pos, avgCost);
      _positions.AddOrUpdate(position.Key().Item1, position, (k, p) => position);
      //_defaultMessageHandler(nameof(OnPosition) + ": " + string.Join("\n", _positions));
      OnPositionSubject(account, contract, position);
      //IbClient.ClientSocket.reqExecutions(IbClient.NextOrderId, new ExecutionFilter() {
      //  Symbol = contract.LocalSymbol,
      //  //Side = (trade.IsBuy ? ExecutionFilter.Sides.BUY : ExecutionFilter.Sides.SELL) + "",
      //  Time = IbClient.ServerTime.ToTWSString()
      //});
    }

    private void OnFirstPosition(Contract contract, PositionMessage position) {
      _defaultMessageHandler(position);
      var st = IbClient.ServerTime;

      if(position.Position != 0 && !OpenTrades.Any(IsEqual(position)))
        OpenTrades.Add(TradeFromPosition(contract.LocalSymbol, position, st, ""));

      TraceTrades("Opened: ", OpenTrades);
      FetchMMRs();
    }

    private void TraceTrades(string label, IEnumerable<Trade> trades) {
      _defaultMessageHandler(label + (trades.Count() > 1 ? "\n" : "")
        + string.Join("\n", trades.Select(ot => new { ot.Pair, ot.Position, ot.Time, ot.Commission })));
    }

    private Trade TradeFromPosition(string symbol, PositionMessage position, DateTime st, string openOrderId) {
      var trade = Trade.Create(IbClient, symbol, TradesManagerStatic.GetPointSize(symbol), TradesManagerStatic.GetBaseUnitSize(symbol), null);
      trade.Id = DateTime.Now.Ticks + "";
      trade.Buy = position.Position > 0;
      trade.IsBuy = trade.Buy;
      trade.Time2 = st;
      trade.Time2Close = IbClient.ServerTime;
      trade.Open = position.AverageCost;
      trade.Lots = position.Position.Abs().ToInt();
      trade.OpenOrderID = openOrderId;
      trade.CommissionByTrade = CommissionByTrade;
      return trade;
    }
    #endregion

    private void OnUpdatePortfolio(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName) {
      var pu = new UpdatePortfolioMessage(contract, position, marketPrice, marketValue, averageCost, unrealisedPNL, realisedPNL, accountName);
    }

    private void OnUpdateAccountValue(string key, string value, string currency, string accountName) {
      if(currency == _accountCurrency) {
        switch(key) {
          case "EquityWithLoanValue":
            Account.Equity = double.Parse(value);
            break;
          case "MaintMarginReq":
            Account.UsableMargin = double.Parse(value);
            break;
          case "ExcessLiquidity":
            Account.ExcessLiquidity = double.Parse(value);
            break;
          case "UnrealizedPnL":
            Account.Balance = Account.Equity - double.Parse(value);
            break;
          case "InitMarginReq":
            InitialMarginRequirement = double.Parse(value);
            break;
        }
        //_defaultMessageHandler(new AccountValueMessage(key, value, currency, accountName));
      }
    }

    public void FetchMMRs() => GetTrades()
      .IfEmpty(() => {
        _defaultMessageHandler(nameof(FetchMMR) + " started");
        TradesManagerStatic.dbOffers.Where(o => !o.Pair.IsCurrenncy()).ToObservable().Subscribe(o => FetchMMR(o.Pair));
      })
      .ForEach(t => _defaultMessageHandler(new { FetchMMRs = new { t.Pair, t.IsBuy, t.Lots, Message = "Won't run" } }));
    private void OnAccountSummary(int requestId, string account, string tag, string value, string currency) {
      if(currency == _accountCurrency) {
        switch(tag) {
          case "EquityWithLoanValue":
            Account.Equity = double.Parse(value);
            break;
          case "MaintMarginReq":
            Account.UsableMargin = double.Parse(value);
            break;
        }
        //_defaultMessageHandler(new AccountSummaryMessage(requestId, account, tag, value, currency));
      }
    }
    private void OnAccountSummaryEnd(int obj) {
      accountSummaryRequestActive = false;
    }
    #endregion

    #region Requests
    public void RequestAccountSummary() {
      if(!accountSummaryRequestActive) {
        accountSummaryRequestActive = true;
        IbClient.ClientSocket.reqAccountSummary(NextReqId(), "All", ACCOUNT_SUMMARY_TAGS);
      } else {
        IbClient.ClientSocket.cancelAccountSummary(CurrReqId());
      }
    }

    public void SubscribeAccountUpdates() {
      if(!accountUpdateRequestActive) {
        accountUpdateRequestActive = true;
        IbClient.ClientSocket.reqAccountUpdates(true, _accountId);
      } else {
        IbClient.ClientSocket.reqAccountUpdates(false, _accountId);
        accountUpdateRequestActive = false;
      }
    }

    public void RequestPositions() {
      if(PositionSubject != null)
        IbClient.ClientSocket.reqPositions();
    }
    #endregion

    #region Overrrides
    public override string ToString() {
      return new { IbClient, CurrentAccount = _accountId } + "";
    }
    #endregion
  }
  public static class Mixins {
    public static bool IsPreSubmited(this IBApi.OrderState order) => order.Status == AccountManager.OrderStatuses.PreSubmitted + "";

    public static bool IsBuy(this IBApi.Order o) => o.Action == "BUY";

    private static Tuple<string, bool> Key(string symbol, bool isBuy) => Tuple.Create(symbol.WrapPair(), isBuy);
    private static Tuple<string, bool> Key2(string symbol, bool isBuy) => Key(symbol, !isBuy);

    public static Tuple<string, bool> Key(this PositionMessage t) => Key(t.Contract.LocalSymbol, t.IsBuy);
    public static Tuple<string, bool> Key2(this PositionMessage t) => Key2(t.Contract.LocalSymbol, t.IsBuy);

    public static Tuple<string, bool> Key(this Trade t) => Key(t.Pair, t.IsBuy);
    public static Tuple<string, bool> Key2(this Trade t) => Key2(t.Pair, t.IsBuy);
  }
}
