using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HedgeHog;
using HedgeHog.Bars;
using HedgeHog.Shared;
using static HedgeHog.Shared.TradesManagerStatic;
using OpenOrderArg = System.Tuple<int, IBApi.Contract, IBApi.Order, IBApi.OrderState>;
namespace IBApp {
  public class IBWraper : HedgeHog.Shared.ITradesManager {
    private readonly IBClientCore _ibClient;
    private static int _nextOrderId=1;
    private int NetOrderId() => _ibClient.ValidOrderId();
    private void Trace(object o) { _ibClient.Trace(o); }
    private void Verbous(object o) { _ibClient.Trace(o); }

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
              .Where(t => t.Item4.Status == "Filled")
              .DistinctUntilChanged(t => t.Item1)
              .Subscribe(t => OnOpenOrderImpl(t.Item1, t.Item2, t.Item3, t.Item4), exc => Trace(exc), () => Trace(nameof(OpenOrderSubject) + " is gone"));
          }
        return _OpenOrderSubject;
      }
    }
    void OnOpenOrder(OpenOrderArg p) {
      OpenOrderSubject.OnNext(p);
    }
    #endregion


    public IBWraper(ICoreFX coreFx, Func<Trade, double> commissionByTrade) {
      CommissionByTrade = commissionByTrade;

      CoreFX = coreFx;
      _ibClient = (IBClientCore)CoreFX;
      _ibClient.PriceChanged += OnPriceChanged;
      _ibClient.OpenOrder += OnOpenOrder;
      //_ibClient.OrderStatus += OnOrderStatus;
      _ibClient.CommissionByTrade = commissionByTrade;
      _ibClient.TradeAdded += (s, e) => RaiseTradeAdded(e.Trade);
      _ibClient.TradeRemoved += (s, e) => { RaiseTradeClosed(e.Trade); RaiseTradeRemoved(e.Trade); };
    }

    #region OpenOrder
    private void OnOrderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld) {
      Verbous(new {
        OrderStatus = new {
          orderId, status, filled, remaining, avgFillPrice, permId, parentId, lastFillPrice, clientId, whyHeld
        }
      });
    }

    private static bool IsEntryOrder(IBApi.Order o) => new[] { "MKT", "LMT" }.Contains(o.OrderType);
    private void OnOpenOrder(int reqId, IBApi.Contract c, IBApi.Order o, IBApi.OrderState os) =>
      OnOpenOrder(Tuple.Create(reqId, c, o, os));
    private void OnOpenOrderImpl(int reqId, IBApi.Contract c, IBApi.Order o, IBApi.OrderState os) {
      Trace(new { OnOpenOrder = new { reqId, c, o, os } });
      RaiseOrderAdded(new Order {
        IsBuy = o.Action == "BUY",
        Lot = (int)o.TotalQuantity,
        Pair = c.Instrument,
        IsEntryOrder = IsEntryOrder(o)
      });
    }
    #endregion

    private void OnPriceChanged(object sender, PriceChangedEventArgs e) {
      var price = e.Price;
      RaisePriceChanged( price);
    }

    #region ITradesManager - Implemented

    public PendingOrder OpenTrade(string Pair, bool isBuy, int lot, double takeProfit, double stopLoss, double rate, string comment) {
      throw new NotImplementedException();
    }

    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price) {
      var c = ContractSamples.ContractFactory(pair);
      var o = new IBApi.Order() {
        OrderId = NetOrderId(),
        Action = buy ? "BUY" : "SELL",
        OrderType = "MKT",
        TotalQuantity = lots,
        Tif = "DAY",

      };
      _ibClient.ClientSocket.placeOrder(o.OrderId, c, o);
      return null;
    }


    #region Methods
    public int GetBaseUnitSize(string pair) => TradesManagerStatic.IsCurrenncy(pair) ? 1000 : 1;
    public double Leverage(string pair) => 1;
    public Trade TradeFactory(string pair) => Trade.Create(this, pair, GetPipSize(pair), GetBaseUnitSize(pair), CommissionByTrade);

    public double InPips(string pair, double? price) => price.GetValueOrDefault() / GetPipSize(pair);
    public double RateForPipAmount(Price price) { return price.Ask.Avg(price.Bid); }
    public double RateForPipAmount(double ask, double bid) { return ask.Avg(bid); }
    TBar ToRate<TBar>(DateTime date, double open, double high, double low, double close, int volume, int count) where TBar : Rate, new() {
      return Rate.Create<TBar>(date, high, low, true);
    }
    public void GetBarsBase<TBar>(string pair
      , int period
      , int periodsBack
      , DateTime startDate
      , DateTime endDate
      , List<TBar> ticks
      , Func<List<TBar>, List<TBar>> map
      , Action<RateLoadingCallbackArgs<TBar>> callBack = null
      ) where TBar : Rate, new() {

      var contract = ContractSamples.ContractFactory(pair);
      var isDone = false;
      Func<DateTime, DateTime> fxDate = d => d == FX_DATE_NOW ? new DateTime(DateTime.Now.Ticks, DateTimeKind.Local) : d;
      endDate = fxDate(endDate);
      startDate = fxDate(startDate);
      var timeUnit = period == 0 ? TimeUnit.S : TimeUnit.D;
      var barSize = period == 0 ? BarSize._1_secs : BarSize._1_min;
      var duration = (endDate - startDate).Duration();
      var lastTime = DateTime.Now;
      new HistoryLoader<TBar>(
        _ibClient,
        contract,
        periodsBack,
        endDate.Max(startDate),
        duration,
        timeUnit,
        barSize,
        ToRate<TBar>,
         list => { ticks.AddRange(list); ticks.Sort(); isDone = true; },
         list => {
           //var x = new { ReqId = _reqId, contract.Symbol, EndDate = _endDate, Duration = Duration(_barSize, _timeUnit, _duration) } + ""));

           callBack(new RateLoadingCallbackArgs<TBar>(
             new {
               HistoryLoader = new {
                 StartDate = list.FirstOrDefault()?.StartDate,
                 EndDate = list.LastOrDefault()?.StartDate,
                 timeUnit, barSize,
                 contract.Symbol,
                 duration = HistoryLoader<Rate>.Duration(barSize, timeUnit, duration)
               }
             } + "",
             list));
           lastTime = DateTime.Now;
         },
         exc => {
           isDone = !(exc is SoftException);
           Trace(exc);
           lastTime = DateTime.Now;
         });
      while(!isDone) {
        Thread.Sleep(300);
        if(lastTime.AddMinutes(1) < DateTime.Now) {
          Trace(new { GetBarsBase = new { lastTime, DateTime.Now, error = "Timeout" } });
          break;
        }
      }
      return;
    }
    public void GetBars(string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      throw new NotImplementedException();
    }

    public void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, Action<RateLoadingCallbackArgs<Rate>> callBack, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      GetBarsBase(pair, Period, periodsBack, StartDate, EndDate, Bars, map, callBack);
    }
    public Account GetAccount() {
      try {
        return _ibClient.AccountManager?.Account;
      } catch(Exception exc) {
        RaiseError(exc);
        return null;
      }
    }
    public Trade[] GetTrades() => _ibClient.AccountManager?.GetTrades() ?? new Trade[0];
    public Trade[] GetTrades(string pair) => GetTrades().Where(t => t.Pair.WrapPair() == pair.WrapPair()).ToArray();
    public Trade[] GetTradesInternal(string Pair) => GetTrades(Pair);
    public Trade[] GetClosedTrades(string pair) => _ibClient.AccountManager?.GetClosedTrades() ?? new Trade[0];

    #endregion

    #region Error Event
    event EventHandler<ErrorEventArgs> ErrorEvent;
    public event EventHandler<ErrorEventArgs>  Error {
      add {
        if (ErrorEvent == null || !ErrorEvent.GetInvocationList().Contains(value))
          ErrorEvent += value;
      }
      remove {
        ErrorEvent -= value;
      }
    }
    protected void RaiseError(Exception exc) {
      if(ErrorEvent != null)
        ErrorEvent(this, new ErrorEventArgs(exc));
    }
    #endregion

    #region PriceChangedEvent

    event EventHandler<PriceChangedEventArgs> PriceChangedEvent;
    public event EventHandler<PriceChangedEventArgs> PriceChanged {
      add {
        if (PriceChangedEvent == null || !PriceChangedEvent.GetInvocationList().Contains(value))
          PriceChangedEvent += value;
      }
      remove {
        PriceChangedEvent -= value;
      }
    }
    void RaisePriceChanged( Price price, Account account, Trade[] trades) {
      var e = new PriceChangedEventArgs( price, account, trades);
      PriceChangedEvent?.Invoke(this, e);
    }

    public void RaisePriceChanged( Price price) {
      RaisePriceChanged( price, GetAccount(), GetTrades());
    }
    #endregion

    #region OrderAddedEvent
    event EventHandler<OrderEventArgs> OrderAddedEvent;
    public event EventHandler<OrderEventArgs> OrderAdded {
      add {
        if (OrderAddedEvent == null || !OrderAddedEvent.GetInvocationList().Contains(value))
          OrderAddedEvent += value;
      }
      remove {
        OrderAddedEvent -= value;
      }
    }

    void RaiseOrderAdded(Order Order) {
      if(OrderAddedEvent != null)
        OrderAddedEvent(this, new OrderEventArgs(Order));
    }
    #endregion

    #region TradeAddedEvent
    event EventHandler<TradeEventArgs> TradeAddedEvent;
    public event EventHandler<TradeEventArgs> TradeAdded {
      add {
        if (TradeAddedEvent == null || !TradeAddedEvent.GetInvocationList().Contains(value))
          TradeAddedEvent += value;
      }
      remove {
        if (TradeAddedEvent != null)
          TradeAddedEvent -= value;
      }
    }
    void RaiseTradeAdded(Trade trade) {
      if(TradeAddedEvent != null)
        TradeAddedEvent(this, new TradeEventArgs(trade));
    }
    #endregion

    #region TradeRemovedEvent
    event TradeRemovedEventHandler TradeRemovedEvent;
    public event TradeRemovedEventHandler TradeRemoved {
      add {
        if (TradeRemovedEvent == null || !TradeRemovedEvent.GetInvocationList().Contains(value))
          TradeRemovedEvent += value;
      }
      remove {
        TradeRemovedEvent -= value;
      }
    }
    void RaiseTradeRemoved(Trade trade) {
      if(TradeRemovedEvent != null)
        TradeRemovedEvent(trade);
    }
    #endregion

    #region TradeClosedEvent
    event EventHandler<TradeEventArgs> TradeClosedEvent;
    public event EventHandler<TradeEventArgs> TradeClosed {
      add {
        if (TradeClosedEvent == null || !TradeClosedEvent.GetInvocationList().Contains(value))
          TradeClosedEvent += value;
      }
      remove {
        if (TradeClosedEvent != null)
          TradeClosedEvent -= value;
      }
    }
    void RaiseTradeClosed(Trade trade) {
      if(TradeClosedEvent != null)
        TradeClosedEvent(this, new TradeEventArgs(trade));
    }
    #endregion


    #region Properties
    public bool HasTicks => false;
    public bool IsLoggedIn => _ibClient.IsLoggedIn;
    public DateTime ServerTime {
      get {
        return DateTime.Now + _ibClient._serverTimeOffset;
      }
    }
    #endregion
    #endregion

    #region ITradesManager
    public Func<Trade, double> CommissionByTrade { get; private set; }

    public ICoreFX CoreFX { get; set; }

    public bool IsHedged {
      get {
        throw new NotImplementedException();
      }
    }

    public bool IsInTest { get; set; }

    public double PipsToMarginCall {
      get {
        throw new NotImplementedException();
      }
    }

    public event EventHandler<OrderEventArgs> OrderChanged;
    public event OrderRemovedEventHandler OrderRemoved;
    public event EventHandler<RequestEventArgs> RequestFailed;

    public void ChangeEntryOrderLot(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedLimit(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedStop(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderRate(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeOrderAmount(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeOrderRate(Order order, double rate) {
      throw new NotImplementedException();
    }

    public void CloseAllTrades() {
      throw new NotImplementedException();
    }

    public bool ClosePair(string pair) {
      var lotBuy = GetTradesInternal(pair).IsBuy(true).Lots();
      if(lotBuy > 0)
        ClosePair(pair, true, lotBuy);
      var lotSell = GetTradesInternal(pair).IsBuy(false).Lots();
      if(lotSell > 0)
        ClosePair(pair, false, lotSell);
      return lotBuy > 0 || lotSell > 0;
    }

    public bool ClosePair(string pair, bool isBuy) {
      throw new NotImplementedException();
    }

    public bool ClosePair(string pair, bool buy, int lot) {
      try {
        var lotToDelete = Math.Min(lot, GetTradesInternal(pair).IsBuy(buy).Lots());
        if(lotToDelete > 0) {
          OpenTrade(pair, !buy, lotToDelete, 0, 0, "", null);
        } else {
          RaiseError(new Exception("Pair [" + pair + "] does not have positions to close."));
          return false;
        }
        return true;
      } catch(Exception exc) {
        RaiseError(exc);
        return false;
      }
    }

    public void CloseTrade(Trade trade) {
      throw new NotImplementedException();
    }

    public bool CloseTrade(Trade trade, int lot, Price price) {
      throw new NotImplementedException();
    }

    public void CloseTradeAsync(Trade trade) {
      throw new NotImplementedException();
    }

    public void CloseTradesAsync(Trade[] trades) {
      throw new NotImplementedException();
    }

    public string CreateEntryOrder(string pair, bool isBuy, int amount, double rate, double stop, double limit) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderLimit(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderStop(string orderId) {
      throw new NotImplementedException();
    }

    public bool DeleteOrder(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteOrders(string pair) {
      RaiseError(new NotImplementedException(nameof(DeleteOrders)));
    }

    public PendingOrder FixCreateLimit(string tradeId, double limit, string remark) {
      throw new NotImplementedException();
    }

    public object FixOrderClose(string tradeId) {
      throw new NotImplementedException();
    }

    public object FixOrderClose(string tradeId, int mode, Price price, int lot) {
      throw new NotImplementedException();
    }

    public object[] FixOrdersClose(params string[] tradeIds) {
      throw new NotImplementedException();
    }

    public void FixOrderSetLimit(string tradeId, double takeProfit, string remark) {
      throw new NotImplementedException();
    }

    public void FixOrderSetStop(string tradeId, double stopLoss, string remark) {
      throw new NotImplementedException();
    }

    public IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate) {
      throw new NotImplementedException();
    }


    void RaiseNotImplemented(string NotImplementedException) {
      Trace(new NotImplementedException(new { NotImplementedException } + ""));
    }
    void RaiseShouldBeImplemented(string NotImplementedException) {
      //Trace(new NotImplementedException(new { NotImplementedException } + ""));
    }

    public int GetDigits(string pair) => TradesManagerStatic.GetDigits(pair);

    public Trade GetLastTrade(string pair) {
      RaiseNotImplemented(nameof(GetLastTrade));
      return null;
    }

    public Order GetNetLimitOrder(Trade trade, bool getFromInternal = false) {
      RaiseShouldBeImplemented(nameof(GetNetLimitOrder));
      return null;
    }

    public Order[] GetOrders(string pair) {
      RaiseShouldBeImplemented(nameof(GetOrders));
      return new Order[0];
    }

    public double GetNetOrderRate(string pair, bool isStop, bool getFromInternal = false) {
      throw new NotImplementedException();
    }

    public Offer GetOffer(string pair) {
      return TradesManagerStatic.GetOffer(pair);
    }

    public Offer[] GetOffers() {
      throw new NotImplementedException();
    }


    //public double GetPipCost(string pair) {
    //  throw new NotImplementedException();
    //}

    public double GetPipSize(string pair) => TradesManagerStatic.GetPointSize(pair);

    public Price GetPrice(string pair) {
      return _ibClient.GetPrice(pair);
    }

    public Tick[] GetTicks(string pair, int periodsBack, Func<List<Tick>, List<Tick>> map) {
      throw new NotImplementedException();
    }

    public double InPoints(string pair, double? price) { return InPoins(this, pair, price); }

    public void RefreshOrders() {
      throw new NotImplementedException();
    }

    public void ResetClosedTrades(string pair) {
      throw new NotImplementedException();
    }

    public double Round(string pair, double value, int digitOffset = 0) { return Math.Round(value, GetDigits(pair) + digitOffset); }

    public void SetServerTime(DateTime serverTime) {
      throw new NotImplementedException();
    }
    #endregion
  }
}