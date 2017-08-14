using System;
using System.Collections;
using C = System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using HedgeHog;
using HedgeHog.Bars;
using HedgeHog.Shared;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using MvvmFoundation.Wpf;
using System.Threading.Tasks.Dataflow;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

[assembly: CLSCompliant(true)]
namespace Order2GoAddIn {
  #region COM
  [Guid("D5FB5C05-8EF5-4bcc-BA92-10706FD640BB")]
  [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
  [ComVisible(true)]
  public interface IOrder2GoEvents {
    [DispId(1)]
    void RowChanged(string tableType, string rowId);
    [DispId(2)]
    void RowAdded(object tableType, string rowId);
  }
  #endregion


  #region EventArgs classes
  public class OrderErrorEventArgs : ErrorEventArgs {
    public OrderErrorEventArgs(Exception exception) : base(exception) { }
  }
  public class PendingOrderEventArgs : EventArgs {
    public PendingOrder Order;
    public PendingOrderEventArgs(PendingOrder po) {
      this.Order = po;
    }
  }
  #endregion

  [ClassInterface(ClassInterfaceType.AutoDual), ComSourceInterfaces(typeof(IOrder2GoEvents))]
  [Guid("2EC43CB6-ED3D-465c-9AF3-C0BBC622663E")]
  [ComVisible(true)]
  public class FXCoreWrapper : IDisposable, ITradesManager {
    private readonly string ERROR_FILE_NAME = "FXCM.log";
    public Func<Trade, double> CommissionByTrade { get; set; }

    #region Constants
    public const string TABLE_ACCOUNTS = "accounts";
    public const string TABLE_OFFERS = "offers";
    public const string TABLE_ORDERS = "orders";
    public const string TABLE_CLOSED = "closed";
    public const string TABLE_SUMMARY = "summary";
    public const string TABLE_TRADES = "trades";
    public const string TABLE_CLOSED_TRADES = "closed trades";
    const string FIELD_ACCOUNTID = "AccountID";
    const string FIELD_POINTSIZE = "PointSize";
    const string FIELD_DIGITS = "Digits";
    const string FIELD_TRADEID = "TradeID";
    const string FIELD_ORDERID = "OrderID";
    const string FIELD_INSTRUMENT = "Instrument";
    const string FIELD_AMOUNT_K = "AmountK";
    const string FIELD_TIME = "Time";
    const string FIELD_ASK = "Ask";
    const string FIELD_BID = "Bid";
    const string FIELD_OPEN = "Open";
    const string FIELD_PL = "PL";
    const string FIELD_BALANCE = "Balance";
    const string FIELD_USABLEMARGIN = "UsableMargin";
    const string FIELD_MARGINCALL = "MarginCall";
    const string FIELD_EQUITY = "Equity";
    const string FIELD_DAYPL = "DayPL";
    const string FIELD_BIDCHANGEDIRECTION = "BidChangeDirection";
    const string FIELD_ASKCHANGEDIRECTION = "AskChangeDirection";
    const string FIELD_GROSSPL = "GrossPL";
    const string FIELD_CLOSETIME = "CloseTime";
    const string FIELD_SELLAVGOPEN = "SellAvgOpen";
    const string FIELD_BUYAVGOPEN = "BuyAvgOpen";
    const string FIELD_LIMIT = "Limit";
    const string FIELD_PIPCOST = "PipCost";
    #endregion

    #region Exceptions
    public class OrderExecutionException : Exception {
      public OrderExecutionException(string Message, Exception inner) : base(Message, inner) { }
    }
    #endregion

    #region Events

    #region PendingOrderCompletedEvent
    event EventHandler<PendingOrderEventArgs> PendingOrderCompletedEvent;
    public event EventHandler<PendingOrderEventArgs> PendingOrderCompleted {
      add {
        if (PendingOrderCompletedEvent == null || !PendingOrderCompletedEvent.GetInvocationList().Contains(value))
          PendingOrderCompletedEvent += value;
      }
      remove {
        PendingOrderCompletedEvent -= value;
      }
    }
    void RaisePendingOrderCompleted(PendingOrder po) {
      if(PendingOrderCompletedEvent != null)
        PendingOrderCompletedEvent(this, new PendingOrderEventArgs(po));
    }
    #endregion

    #region OrderErrorEvent
    event EventHandler<OrderErrorEventArgs> OrderErrorEvent;
    public event EventHandler<OrderErrorEventArgs> OrderError {
      add {
        if (OrderErrorEvent == null || !OrderErrorEvent.GetInvocationList().Contains(value))
          OrderErrorEvent += value;
      }
      remove {
        OrderErrorEvent -= value;
      }
    }

    void RaiseOrderError(Exception exception) {
      try {
        if(OrderErrorEvent != null)
          OrderErrorEvent(this, new OrderErrorEventArgs(exception));
      } catch(Exception exc) { FileLogger.LogToFile(exc, ERROR_FILE_NAME); }
    }
    #endregion

    #region ErrorEvent
    event EventHandler<ErrorEventArgs> ErrorEvent;
    public event EventHandler<ErrorEventArgs> Error {
      add {
        if (ErrorEvent == null || !ErrorEvent.GetInvocationList().Contains(value))
          ErrorEvent += value;
      }
      remove {
        ErrorEvent -= value;
      }
    }
    void RaiseError(Exception exception, string pair, bool isBuy, int lot, double stop, double limit, string remark) {
      RaiseError(new ErrorEventArgs(exception, pair, isBuy, lot, stop, limit, remark));
    }
    void RaiseError(Exception exception) {
      RaiseError(new ErrorEventArgs(exception));
    }
    void RaiseError(ErrorEventArgs eventArgs) {
      try {
        if(ErrorEvent != null)
          ErrorEvent(this, eventArgs);
        //else Debug.Fail(eventArgs.Error + "");
      } catch(Exception exc) { FileLogger.LogToFile(exc, ERROR_FILE_NAME); }
    }
    #endregion

    #region RowChangedEvent
    public delegate void RowChangedEventHandler(string TableType, string RowID);
    event RowChangedEventHandler RowChangedEvent;
    public event RowChangedEventHandler RowChanged {
      add {
        if (RowChangedEvent == null || !RowChangedEvent.GetInvocationList().Contains(value))
          RowChangedEvent += value;
      }
      remove {
        RowChangedEvent -= value;
      }
    }
    void RaiseRowChanged(string tableType, string rowID) {
      if(RowChangedEvent != null)
        RowChangedEvent(tableType, rowID);
    }
    #endregion

    #region PriceChangedEvent
    BroadcastBlock<PriceChangedEventArgs> _PriceChangedBroadcast;
    public BroadcastBlock<PriceChangedEventArgs> PriceChangedBroadcast {
      get {
        if(_PriceChangedBroadcast == null)
          _PriceChangedBroadcast = new BroadcastBlock<PriceChangedEventArgs>(e => e);
        return _PriceChangedBroadcast;
      }
      set { _PriceChangedBroadcast = value; }
    }

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
    public void RaisePriceChanged( Price price) {
      RaisePriceChanged( price, GetAccount(), GetTrades());
    }
    void RaisePriceChanged(Price price, Account account, Trade[] trades) {
      var e = new PriceChangedEventArgs( price, account, trades);
      if(_PriceChangedBroadcast != null)
        PriceChangedBroadcast.SendAsync(e);
      if(PriceChangedEvent != null)
        PriceChangedEvent(this, e);
    }
    #endregion

    #region OrderRemovedEvent
    event OrderRemovedEventHandler OrderRemovedEvent;
    public event OrderRemovedEventHandler OrderRemoved {
      add {
        if (OrderRemovedEvent == null || !OrderRemovedEvent.GetInvocationList().Contains(value))
          OrderRemovedEvent += value;
      }
      remove {
        OrderRemovedEvent -= value;
      }
    }
    void RaiseOrderRemoved(Order order) {
      if(OrderRemovedEvent != null)
        OrderRemovedEvent(order);
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

    #region TradeChangedEvent
    event EventHandler<TradeEventArgs> TradeChangedEvent;
    public event EventHandler<TradeEventArgs> TradeChanged {
      add {
        if (TradeChangedEvent == null || !TradeChangedEvent.GetInvocationList().Contains(value))
          TradeChangedEvent += value;
      }
      remove {
        TradeChangedEvent -= value;
      }
    }
    void RaiseTradeChanged(Trade Trade) {
      if(TradeChangedEvent != null)
        TradeChangedEvent(this, new TradeEventArgs(Trade));
    }
    #endregion

    #region RequestComleteEvent

    event EventHandler<RequestEventArgs> RequestCompleteEvent;
    public event EventHandler<RequestEventArgs> RequestComplete {
      add {
        if (RequestCompleteEvent == null || !RequestCompleteEvent.GetInvocationList().Contains(value))
          RequestCompleteEvent += value;
      }
      remove {
        RequestCompleteEvent -= value;
      }
    }
    void OnRequestComplete(string requestId) {
      if(RequestCompleteEvent != null)
        RequestCompleteEvent(this, new RequestEventArgs(requestId));
    }

    #endregion

    #region SessionStatusChangedEvent
    public class SesstionStatusEventArgs : EventArgs {
      public TradingServerSessionStatus Status { get; set; }
      public SesstionStatusEventArgs(TradingServerSessionStatus status) {
        this.Status = status;
      }
    }

    event EventHandler<SesstionStatusEventArgs> SessionStatusChangedEvent;
    public event EventHandler<SesstionStatusEventArgs> SessionStatusChanged {
      add {
        if (SessionStatusChangedEvent == null || !SessionStatusChangedEvent.GetInvocationList().Contains(value))
          SessionStatusChangedEvent += value;
      }
      remove {
        SessionStatusChangedEvent -= value;
      }
    }
    private void RaiseSessionStatusChanged(TradingServerSessionStatus status) {
      if(SessionStatusChangedEvent != null)
        SessionStatusChangedEvent(this, new SesstionStatusEventArgs(status));
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

    #region OrderChangedEvent
    event EventHandler<OrderEventArgs> OrderChangedEvent;
    public event EventHandler<OrderEventArgs> OrderChanged {
      add {
        if (OrderChangedEvent == null || !OrderChangedEvent.GetInvocationList().Contains(value))
          OrderChangedEvent += value;
      }
      remove {
        OrderChangedEvent -= value;
      }
    }

    void RaiseOrderChanged(Order Order) {
      if(OrderChangedEvent != null)
        OrderChangedEvent(this, new OrderEventArgs(Order));
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

    #region RequestFailedEvent
    event EventHandler<RequestEventArgs> RequestFailedEvent;
    public event EventHandler<RequestEventArgs> RequestFailed {
      add {
        if (RequestFailedEvent == null || !RequestFailedEvent.GetInvocationList().Contains(value))
          RequestFailedEvent += value;
      }
      remove {
        RequestFailedEvent -= value;
      }
    }

    protected void OnRequestFailed(string requestId, string error) {
      if(RequestFailedEvent != null)
        RequestFailedEvent(this, new RequestEventArgs(requestId, error));
    }
    #endregion
    #endregion

    #region Properties
    public bool IsInTest { get; set; }
    private string _pair = "";
    public event EventHandler PairChanged;
    public string Pair_ {
      get { return _pair; }
      set {
        if(_pair == value)
          return;
        _pair = value.ToUpper();
        if(Desk != null && FindOfferByPair(value) == null)
          Desk.SetOfferSubscription(value, "Enabled");
        if(PairChanged != null)
          PairChanged(this, new EventArgs());
      }
    }

    FXCore.RowAut FindOfferByPair(string pair) {
      try {
        return Desk == null ? null : Desk.FindRowInTable(TABLE_OFFERS, FIELD_INSTRUMENT, pair) as FXCore.RowAut;
      } catch(ArgumentException) {
        return null;
      }
    }

    int _minimumQuantity = 0;

    string _accountID = "";
    public string AccountID {
      get {
        if(_accountID == "") {
          var account = GetAccount();
          if(account != null)
            _accountID = account.ID;
        }
        return _accountID;
      }
    }

    private bool? _isHedged;
    public bool IsHedged {
      get {
        if(!_isHedged.HasValue)
          _isHedged = GetAccount().Hedging;
        return _isHedged.Value;
      }
    }


    private bool? _Hedging;

    public bool Hedging {
      get {
        if(_Hedging == null)
          _Hedging = GetAccount().Hedging;
        return _Hedging.Value;
      }
    }

    public bool IsLoggedIn { get; set; }
    #endregion

    #region Constructor
    PropertyObserver<ICoreFX> _coreFxObserver;
    public ICoreFX CoreFX { get; set; }
    //public FXCoreWrapper() : this(new CoreFX(), null, null) { }
    //public FXCoreWrapper(CoreFX coreFX) : this(coreFX, null, null) { }
    public FXCoreWrapper(ICoreFX coreFX, Func<Trade, double> commissionByTrade) : this(coreFX, null, commissionByTrade) { }
    public FXCoreWrapper(ICoreFX coreFX, string pair, Func<Trade, double> commissionByTrade) {
      if(coreFX == null)
        throw new NullReferenceException("coreFx parameter can npt be null.");
      this.CoreFX = coreFX;
      IsLoggedIn = coreFX.IsLoggedIn;
      this.CoreFX.LoggedIn += coreFX_LoggedInEvent;
      this.CoreFX.LoggedOff += coreFX_LoggedOffEvent;
      this.CoreFX.LoggingOff += CoreFX_LoggingOff;
      _coreFxObserver = new PropertyObserver<ICoreFX>(CoreFX)
        .RegisterHandler(cfx => cfx.SessionStatus, h => this.RaiseSessionStatusChanged(h.SessionStatus));
      PendingOrders.CollectionChanged += PendingOrders_CollectionChanged;
      this.CommissionByTrade = commissionByTrade;
    }

    void CoreFX_LoggingOff(object sender, LoggedInEventArgs e) {
      if(e.IsInVirtualTrading)
        return;
      try {
        _accountID = "";
        tableIndices.Clear();
        Unsubscribe();
      } catch(Exception exc) {
        RaiseError(exc);
      }
    }

    void coreFX_LoggedOffEvent(object sender, LoggedInEventArgs e) {
      IsLoggedIn = false;
    }

    void coreFX_LoggedInEvent(object sender, LoggedInEventArgs e) {
      IsLoggedIn = true;
      if(e.IsInVirtualTrading)
        return;
      try {
        TradesReset();
        Subscribe();
      } catch(Exception exc) {
        IsLoggedIn = false;
        RaiseError(exc);
      }
    }

    void PendingOrders_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      switch(e.Action) {
        case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
          var po = e.OldItems[0] as PendingOrder;
          if(po.Parent == null)
            RaisePendingOrderCompleted(po);
          break;
      }
    }

    public FXCore.TradeDeskAut Desk {
      get {
        return CoreFX == null ? null : ((CoreFX)CoreFX).Desk;
      }
    }
    #endregion

    #region Get (Ticks/Bars)
    private static object lockHistory = new object();

    public Tick[] GetTicks(string pair, int tickCount, Func<List<Tick>, List<Tick>> map) {
      if(map == null)
        map = l => l;
      DateTime startDate = DateTime.MinValue;
      var endDate = ServerTime;
      var ticks = map(GetTicks(pair, startDate, endDate));
      int timeoutCount = 2;
      if(ticks.Count() > 0) {
        var dateMin = ticks.Min(b => b.StartDate);
        endDate = ticks.SkipWhile(ts => ts.StartDate == dateMin).First().StartDate;
        while(ticks.Count() < tickCount) {
          try {
            var t = map(GetTicks(pair, startDate, endDate));
            if(t.Count() == 0)
              break;
            ticks = ticks.Union(t).OrderBars().ToList();
            dateMin = ticks.Min(b => b.StartDate);
            if(endDate > dateMin) {
              endDate = ticks.SkipWhile(ts => ts.StartDate == dateMin).First().StartDate;
              //System.Diagnostics.Debug.WriteLine("Ticks[{2}]:{0} @ {1:t}", ticks.Count(), endDate.ToLongTimeString(), Pair);
            } else
              endDate = endDate.AddSeconds(-3);
          } catch(Exception exc) {
            if(exc.Message.ToLower().Contains("timeout")) {
              //coreFX.LogOn();
              if(timeoutCount-- == 0)
                break;
            }
          }
        }
      }
      return ticks.OrderBars().ToArray();
    }

    //public Rate[] GetBarsBase(string pair, int period, DateTime startDate, Action<string> callBack = null) {
    //  return GetBarsBase(pair, period, startDate, TradesManagedStatic.FX_DATE_NOW,callBack);
    //}

    public void GetBarsBase<TBar>(string pair, int period, int periodsBack, DateTime startDate, DateTime endDate, List<TBar> ticks, Func<List<TBar>, List<TBar>> map, Action<RateLoadingCallbackArgs<TBar>> callBack = null) where TBar : Rate, new() {
      if(map == null)
        map = l => l;
      if(period >= 1) {
        startDate = startDate.Round(period);
        if(endDate != TradesManagerStatic.FX_DATE_NOW)
          endDate = endDate.Round(period);
      }
      if(startDate == DateTime.MinValue)
        startDate = TradesManagerStatic.FX_DATE_NOW;
      int timeoutCount = 3;
      Func<string, bool> isTimeOut = s => Regex.IsMatch(s, "time[A-Z ]{0,3}out", RegexOptions.IgnoreCase);
      Func<IList<TBar>> getBars = () => {
        while(true)
          try {
            return map(GetBarsBase_<TBar>(pair, period, startDate, endDate));
          } catch(Exception exc) {
            LogMessage.Send(new Exception(new { pair, period, periodsBack, startDate, endDate } + "", exc));
            if(!isTimeOut(exc.Message) || timeoutCount-- <= 0)
              throw new Exception(string.Format("Pair:{0},Period:{1}", pair, period), exc);
            LogMessage.Send("Sleep for 10 second");
            var timeout = 11;
            while(timeout-- > 0) {
              LogMessage.Send("Re-trying GetBarsBase in " + timeout + " seconds");
              Thread.Sleep(1000);
            }
            LogMessage.Send("Re-starting GetBarsBase");
          }
      };
      Func<bool> doContinue = () => (endDate == TradesManagerStatic.FX_DATE_NOW || startDate != TradesManagerStatic.FX_DATE_NOW && startDate < endDate) || (periodsBack > 0 && ticks.Count() < periodsBack);
      while(doContinue()) {
        try {
          DateTime timer = DateTime.Now;
          var t = getBars();
          var sw = DateTime.Now - timer;
          t.RemoveAll(b => b.StartDate < startDate);
          if(t.Count() == 0)
            break;
          var ticksNew = t.Except(ticks).ToList();
          if(ticksNew.Count == 0)
            break;
          ticks.AddRange(ticksNew);
          var tickMinDate = ticks.Min(b => b.StartDate);
          var msg = "Bars[" + pair + "]<" + period + ">:" + ticks.Count() + " @ " + endDate + "::" + sw.TotalSeconds.Round(3);
          var rlc = new RateLoadingCallbackArgs<TBar>(msg, ticksNew);
          callBack?.Invoke(rlc);
          if(rlc.IsProcessed)
            ticks.Clear();
          if(endDate == TradesManagerStatic.FX_DATE_NOW || endDate > tickMinDate) {
            endDate = tickMinDate;
          } else
            endDate = endDate.AddSeconds(-30);
        } catch(Exception exc) {
          throw new Exception(string.Format("Pair:{0},Period:{1}", pair, period), exc);
        }
      }
      ticks.Sort();
      var removeCount = Math.Max(0, periodsBack == 0 ? 0 : ticks.Count - periodsBack);//, ticks.Count(t => t.StartDate < startDate));
      ticks.RemoveRange(0, removeCount);
    }
    //public Rate[] GetBars(string pair, int period, int barsCount) {
    //  var ticks = GetBarsFromHistory(pair, period, barsCount).ToList();
    //  GetBarsBase(pair, period, barsCount, ticks);
    //  return ticks.ToArray();
    //}

    //public void GetBars(string pair, int period, int minutesBack, DateTime endTime, List<Rate> ticks) {
    //  if (ticks.Count() > 0) {
    //    var lastDate = ticks.Max(r => r.StartDate);
    //    if (lastDate < endTime)
    //      ticks.AddRange(GetBarsBase(pair, period, lastDate, endTime).Except(ticks).OrderBars());
    //  }
    //  GetBarsBase(pair, period, minutesBack, ticks);
    //}
    //private void GetBarsBase(string pair, int period, int periodsBack, List<Rate> rates) {
    //  int timeoutCount = 1;
    //  while (rates.Count()< periodsBack) {
    //    try {
    //      var endDate = rates.Select(t1 => t1.StartDate).DefaultIfEmpty(TradesManagedStatic.FX_DATE_NOW).Min();
    //      var t = GetBarsBase_(pair, period, DateTime.MinValue, endDate);
    //      rates.AddRange(t.Except(rates));
    //    } catch (Exception exc) {
    //      if (exc.Message.ToLower().Contains("timeout")) {
    //        if (timeoutCount-- == 0) break;
    //      }
    //    }
    //  }
    //  rates.Sort();
    //  rates.RemoveRange(0, rates.Count() - periodsBack);
    //}
    List<TBar> GetBarsBase_<TBar>(string pair, int period, DateTime startDate, DateTime endDate) where TBar : Rate {
      try {
        return period == 0 ?
          GetTicks(pair, startDate, endDate).Cast<TBar>().ToList() :
          GetBarsFromHistory(pair, period, startDate, endDate).Cast<TBar>().ToList();
      } catch(Exception exc) {
        var empty = (period == 0 ? new Tick[] { }.Cast<TBar>() : new TBar[] { }).ToList();
        var e = new Exception("StartDate:" + startDate + ",EndDate:" + endDate + ":" + Environment.NewLine + "\t" + exc.Message, exc);
        if(exc.Message.Contains("The specified date's range is empty.") ||
            exc.Message.Contains("Object reference not set to an instance of an object.")) {
          return empty;
        }
        throw;
      }
    }

    public IList<Rate> GetBarsFromHistory(string pair, int period, DateTime startDate) {
      return GetBarsFromHistory(pair, period, startDate, TradesManagerStatic.FX_DATE_NOW);
    }
    static object rateFromMarketRateLocker = new object();
    Rate RateFromMarketRate(string pair, FXCore.MarketRateAut r) {
      var digits = GetDigits(pair);
      lock(rateFromMarketRateLocker) {
        return new Rate(true) {
          AskClose = Math.Round(r.AskClose, digits),
          AskHigh = Math.Round(r.AskHigh, digits),
          AskLow = Math.Round(r.AskLow, digits),
          AskOpen = Math.Round(r.AskOpen, digits),
          BidClose = Math.Round(r.BidClose, digits),
          BidHigh = Math.Round(r.BidHigh, digits),
          BidLow = Math.Round(r.BidLow, digits),
          BidOpen = Math.Round(r.BidOpen, digits),
          StartDate2 = DateTime.SpecifyKind(r.StartDate, DateTimeKind.Utc),
          Volume = r.Volume
        };
      }
    }
    public List<Tick> GetTicks(string pair, DateTime startDate, DateTime endDate) {
      return GetBarsFromHistory(pair, 0, startDate, endDate).Cast<Tick>().ToList();
    }
    public Tick[] GetTicks(string pair, DateTime startDate, DateTime endDate, int barsMax) {
      lock(lockHistory) {
        if(endDate != TradesManagerStatic.FX_DATE_NOW)
          endDate = ConvertDateToUTC(endDate);
        startDate = ConvertDateToUTC(startDate);
        var mr = //RunWithTimeout.WaitFor<FXCore.MarketRateEnumAut>.Run(TimeSpan.FromSeconds(5), () =>
         ((FXCore.MarketRateEnumAut)Desk.GetPriceHistoryUTC(pair, "t1", startDate, endDate, barsMax, true, true))
         .Cast<FXCore.MarketRateAut>().ToArray();
        //);
        return mr.Select((r, i) => new Tick(ConvertDateToLocal(r.StartDate), r.AskOpen, r.BidOpen, i, true)).OrderBars().ToArray();
      }
    }
    double? _historyTimeAverage;
    [MethodImpl(MethodImplOptions.Synchronized)]
    public IList<Rate> GetBarsFromHistory(string pair, int period, DateTime startDate, DateTime endDate) {
      //lock (lockHistory) {
      if(endDate != TradesManagerStatic.FX_DATE_NOW)
        endDate = ConvertDateToUTC(endDate);
      if(startDate != TradesManagerStatic.FX_DATE_NOW)
        startDate = ConvertDateToUTC(startDate);
      var periodToRun = period;// Enum.GetValues(typeof(BarsPeriodTypeFXCM)).Cast<int>().Where(i => i <= period).Max();
      try {
        var d = DateTime.Now;
        var mr =
          //RunWithTimeout.WaitFor<FXCore.MarketRateAut[]>.Run(TimeSpan.FromMilliseconds(_historyTimeAverage.GetValueOrDefault(1000)*2), () =>
          ((FXCore.MarketRateEnumAut)Desk.GetPriceHistoryUTC(pair, (BarsPeriodType)periodToRun + "", startDate, endDate, int.MaxValue, true, true))
          .Cast<FXCore.MarketRateAut>().ToList();
        //);
        var ms = (DateTime.Now - d).TotalMilliseconds;
        _historyTimeAverage = _historyTimeAverage.Cma(10, ms);
        if(period == 0)
          return mr.GroupBy(r => r.StartDate)
            .SelectMany(g => g.Select((r, i) => new Tick(ConvertDateToLocal(r.StartDate), r.AskOpen, r.BidOpen, i, true))).ToArray();
        var ret = mr.Select((r, i) => period == 0 ? new Tick(ConvertDateToLocal(r.StartDate), r.AskOpen, r.BidOpen, i, true) : RateFromMarketRate(pair, r)).ToList();
        return period == periodToRun ? ret : ret.GetMinuteTicks(period).OrderBars().ToList();
      } catch(ThreadAbortException exc) {
        RaiseError(new Exception("Pair:" + pair + ",period:" + period + ",startDate:" + startDate, exc));
        return new Rate[0];
      }
      //);
      //}
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private Rate[] GetBarsFromHistory(string pair, int period, int barsCount) {
      //lock (lockHistory) {
      var mr = //RunWithTimeout.WaitFor<FXCore.MarketRateEnumAut>.Run(TimeSpan.FromSeconds(5), () =>
        ((FXCore.MarketRateEnumAut)Desk.GetPriceHistoryUTC(pair, (BarsPeriodType)period + "", DateTime.MinValue, TradesManagerStatic.FX_DATE_NOW, barsCount, true, true))
        .Cast<FXCore.MarketRateAut>().ToArray();
      return mr.Select((r) => RateFromMarketRate(pair, r)).ToArray();
      //);
      //}
    }

    public void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      GetBars(pair, Period, periodsBack, StartDate, EndDate, Bars, null, doTrim, map);
    }
    public void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, Func<List<Rate>, List<Rate>> map) {
      GetBars(pair, Period, periodsBack, StartDate, EndDate, Bars, null, true, map);
    }
    public void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, Action<RateLoadingCallbackArgs<Rate>> callBack, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      if(periodsBack == 0 && StartDate == TradesManagerStatic.FX_DATE_NOW)
        throw new ArgumentOutOfRangeException("Either periodsBack or startDate must have a real value.");
      if(Bars.Count == 0)
        GetBarsBase(pair, Period, periodsBack, StartDate, EndDate, Bars, map, callBack);
      if(Bars.Count == 0)
        return;
      if(EndDate != TradesManagerStatic.FX_DATE_NOW)
        foreach(var bar in Bars.Where(b => b.StartDate > EndDate).ToArray())
          Bars.Remove(bar);
      if(Bars.Count == 0) {
        Debug.Fail("Should not be here, man!");
        //GetBars(pair, Period, StartDate, EndDate, Bars);
      } else {
        var minimumDate = Bars.Min(b => b.StartDate);
        if(StartDate < minimumDate)
          GetBarsBase(pair, Period, 0, StartDate, minimumDate, Bars, map, callBack);
        var maximumDate = Bars.Select(b => b.StartDate).DefaultIfEmpty(StartDate).Max();
        if(EndDate == TradesManagerStatic.FX_DATE_NOW || EndDate > maximumDate) {
          GetBarsBase(pair, Period, 0, maximumDate, EndDate, Bars, map, callBack);
        }
      }
      if(EndDate != TradesManagerStatic.FX_DATE_NOW)
        Bars.RemoveAll(b => b.StartDate > EndDate);
      if(Bars.Count < periodsBack)
        GetBarsBase(pair, Period, periodsBack, TradesManagerStatic.FX_DATE_NOW, Bars.Select(b => b.StartDate).DefaultIfEmpty(EndDate).Min(), Bars, map, callBack);
      Bars.Sort();
      if(doTrim) {
        var countMaximum = TradesManagerStatic.GetMaxBarCount(periodsBack, StartDate, Bars);
        //Dimok: Warn when negative
        Bars.RemoveRange(0, Math.Max(0, Bars.Count - countMaximum));
      }
    }

    #endregion

    #region FX Tables
    public Account GetAccount() {
      if(accountInternal == null) {
        accountInternal = GetAccount_Slow();
        TradesReset();
      }
      return accountInternal;
    }
    public Account GetAccount_Slow() {
      Stopwatch sw = Stopwatch.StartNew();
      try {
        var id = ((CoreFX)CoreFX).accountSubId;
        var row = GetRows(TABLE_ACCOUNTS).First(a => string.IsNullOrEmpty(id) || (a.CellValue(FIELD_ACCOUNTID) + "").EndsWith(id));
        //Debug.WriteLine("GetAccount1:{0} ms", sw.Elapsed.TotalMilliseconds);
        var trades = new Trade[] { };
        var account = new Account() {
          ID = row.CellValue(FIELD_ACCOUNTID) + "",
          Balance = (double)row.CellValue(FIELD_BALANCE),
          UsableMargin = (double)row.CellValue(FIELD_USABLEMARGIN),
          IsMarginCall = row.CellValue(FIELD_MARGINCALL) + "" == "W",
          Equity = (double)row.CellValue(FIELD_EQUITY),
          Hedging = row.CellValue("Hedging").ToString() == "Y",
          //Trades = includeOtherInfo ? trades = GetTrades("") : null,
          //StopAmount = includeOtherInfo ? trades.Sum(t => t.StopAmount) : 0,
          //LimitAmount = includeOtherInfo ? trades.Sum(t => t.LimitAmount) : 0,
          ServerTime = ServerTime
        };
        //Debug.WriteLine("GetAccount2:{0} ms", sw.Elapsed.TotalMilliseconds);
        //account.PipsToMC = summary == null ? 0 :
        //  (int)(account.UsableMargin / Math.Max(.1, (Math.Abs(summary.BuyLots - summary.SellLots) / 10000)));
        //Debug.WriteLine("GetAccount3:{0} ms", sw.Elapsed.TotalMilliseconds);
        return account;
      } catch(Exception exc) {
        RaiseError(exc);
        return null;
      } finally {
        //Debug.WriteLine("{0}@{2:mm:ss} - {1:n1}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds,DateTime.Now);
      }
    }

    private Account GetAccount(NameValueParser row) {
      accountInternal.ID = row.Get(FIELD_ACCOUNTID);
      accountInternal.Balance = row.GetDouble(FIELD_BALANCE);
      accountInternal.UsableMargin = row.GetDouble(FIELD_USABLEMARGIN);
      accountInternal.IsMarginCall = row.Get(FIELD_MARGINCALL) == "W";
      accountInternal.Equity = row.GetDouble(FIELD_EQUITY);
      accountInternal.DayPL = row.GetDouble(FIELD_DAYPL);
      accountInternal.Hedging = row.Get("Hedging").ToString() == "Y";
      accountInternal.Trades = GetTrades();
      accountInternal.StopAmount = accountInternal.Trades.Sum(t => t.StopAmount);
      accountInternal.LimitAmount = accountInternal.Trades.Sum(t => t.LimitAmount);
      accountInternal.ServerTime = ServerTime;
      //accountInternal.PipsToMC = (int)(accountInternal.UsableMargin * PipsToMarginCallPerUnitCurrency(accountInternal.Trades));
      accountInternal.PipsToMC = PipsToMarginCallCore(accountInternal).ToInt();
      return accountInternal;
    }

    public double CommisionPending { get { return GetTrades("").Sum(t => t.Lots) / 10000; } }

    private static bool IsBuy(object BS) { return BS + "" == "B"; }
    private static bool IsBuy(string BS) { return BS == "B"; }

    #region Close Positions
    public void TruncatePositions() {
      var buys = GetTrades(true).Count();
      var sells = GetTrades(false).Count();
      if(buys == 0 || sells == 0)
        return;
      ClosePositions(buys - sells, sells - buys);
    }
    public void ClosePositionsExceptLast() {
      var lastTrade = GetTrades().OrderBy(t => t.Id).LastOrDefault();
      if(lastTrade != null)
        ClosePositions(lastTrade.Buy ? 1 : 0, lastTrade.Buy ? 0 : 1);
    }
    public void ClosePositions() { ClosePositions(0, 0); }
    public void ClosePositions(int buyMin, int sellMin) {
      //DIMOK: Test ClosePositions(...)
      int errorCount = 3;
      var summury = GetTrades("");
      sellMin = Math.Max(0, sellMin);
      buyMin = Math.Max(0, buyMin);
      while(summury.Length > sellMin || summury.Length > buyMin) {
        try {
          var trades = GetTrades("", true);
          if(trades.Length > buyMin)
            FixOrderClose(trades.OrderBy(t => t.Time).First().Id);
          trades = GetTrades("", false);
          if(trades.Length > buyMin)
            FixOrderClose(trades.OrderBy(t => t.Time).First().Id);
          summury = GetTrades("");
        } catch(Exception exc) {
          if(errorCount-- == 0)
            throw;
        }
      }
    }
    #endregion

    #region Trading Helpers
    public double GetMaxDistance(bool buy) {
      var trades = GetTrades().Where(t => t.Buy == buy).Select((t, i) => new { Pos = i, t.Open });
      if(trades.Count() < 2)
        return 0;
      var distance = (from t0 in trades
                      join t1 in trades on t0.Pos equals t1.Pos - 1
                      select Math.Abs(t0.Open - t1.Open)).Max(d => d);
      return distance;
    }
    public int CanTrade(Trade[] otherTrades, bool buy, double minDelta, int totalLots, bool unisex) {
      var minLoss = (from t in otherTrades
                     where (unisex || t.Buy == buy)
                     select t.PL
                    ).DefaultIfEmpty(1000).Max();
      var hasProfitTrades = otherTrades.Where(t => t.Buy == buy && t.PL > 0).Count() > 0;
      var tradeInterval = (otherTrades.Select(t => t.Time).DefaultIfEmpty().Max() - ServerTime).Duration();
      return minLoss.Between(-minDelta, 1) || hasProfitTrades || tradeInterval.TotalSeconds < 10 ? 0 : totalLots;
    }

    #endregion
    public double RateForPipAmount(Price price) { return price.Ask.Avg(price.Bid); }
    public double RateForPipAmount(double ask, double bid) { return ask.Avg(bid); }

    public Trade TradeFactory(string pair) {
      return Trade.Create(this, pair, GetPipSize(pair), GetBaseUnitSize(pair), CommissionByTrade);
    }

    #region Get Tables
    #region GetOffers
    public Offer GetOffer(string pair) {
      return GetOffers().SingleOrDefault(o => o.Pair == pair.ToUpper());
    }
    public Offer[] GetOffers() {
      try {
        return (from t in GetRows(TABLE_OFFERS)
                select new Offer() {
                  OfferID = (String)t.CellValue("OfferID"),
                  Pair = (String)t.CellValue("Instrument"),
                  InstrumentType = (int)t.CellValue("InstrumentType"),
                  Bid = (Double)t.CellValue("Bid"),
                  Ask = (Double)t.CellValue("Ask"),
                  Hi = (Double)t.CellValue("Hi"),
                  Low = (Double)t.CellValue("Low"),
                  IntrS = (Double)t.CellValue("IntrS"),
                  IntrB = (Double)t.CellValue("IntrB"),
                  ContractCurrency = (String)t.CellValue("ContractCurrency"),
                  ContractSize = (int)t.CellValue("ContractSize"),
                  Digits = (int)t.CellValue("Digits"),
                  DefaultSortOrder = (int)t.CellValue("DefaultSortOrder"),
                  PipCost = (Double)t.CellValue("PipCost"),
                  MMRLong = (Double)t.CellValue("MMR"),
                  Time = (DateTime)t.CellValue("Time"),
                  BidChangeDirection = (int)t.CellValue("BidChangeDirection"),
                  AskChangeDirection = (int)t.CellValue("AskChangeDirection"),
                  HiChangeDirection = (int)t.CellValue("HiChangeDirection"),
                  LowChangeDirection = (int)t.CellValue("LowChangeDirection"),
                  QuoteID = (String)t.CellValue("QuoteID"),
                  BidID = (String)t.CellValue("BidID"),
                  AskID = (String)t.CellValue("AskID"),
                  BidExpireDate = (DateTime)t.CellValue("BidExpireDate"),
                  AskExpireDate = (DateTime)t.CellValue("AskExpireDate"),
                  BidTradable = (String)t.CellValue("BidTradable"),
                  AskTradable = (String)t.CellValue("AskTradable"),
                  PointSize = (Double)t.CellValue("PointSize"),
                }).ToArray();
      } catch(Exception exc) {
        if(wasRowDeleted(exc))
          return GetOffers();
        throw;
      }
    }
    #endregion

    #region GetSummary
    public Summary[] GetSummaries(Trade[] trades) {
      try {
        var rowsSumm = GetRows(TABLE_SUMMARY);
        var s = rowsSumm
          .Select(t => new Summary() {
            Pair = t.CellValue(FIELD_INSTRUMENT) + "",
            AmountK = ((long)(double)t.CellValue("AmountK")),
            BuyLots = ((long)(double)t.CellValue("BuyAmountK")) * 1000,
            SellLots = ((long)(double)t.CellValue("SellAmountK")) * 1000,
            BuyNetPL = ((double)t.CellValue("BuyNetPL")),
            //BuyNetPLPip = ((double)t.CellValue("BuyNetPLPip")),
            SellNetPL = ((double)t.CellValue("SellNetPL")),
            //SellNetPLPip = ((double)t.CellValue("SellNetPLPip")),
            NetPL = ((double)t.CellValue("NetPL")),
            SellAvgOpen = (double)t.CellValue(FIELD_SELLAVGOPEN),
            BuyAvgOpen = (double)t.CellValue(FIELD_BUYAVGOPEN),
            StopAmount = trades.Where(tr => tr.Pair == t.CellValue(FIELD_INSTRUMENT) + "").Sum(trd => trd.StopAmount),
            LimitAmount = trades.Where(tr => tr.Pair == t.CellValue(FIELD_INSTRUMENT) + "").Sum(trd => trd.LimitAmount)
          }).ToArray();
        return s;
      } catch(System.Runtime.InteropServices.COMException) {
        return GetSummaries(trades);
      }
    }
    public Summary GetSummary(string Pair) {
      return GetSummary(Pair, GetTrades(Pair));
    }
    public Summary GetSummary(string Pair, Trade[] trades) {
      var rowsSumm = GetRows(TABLE_SUMMARY).Where(r => new[] { "", r.CellValue(FIELD_INSTRUMENT).ToString() }.Contains(Pair));
      var s = rowsSumm
        .Select(t => new Summary() {
          Pair = t.CellValue(FIELD_INSTRUMENT) + "",
          BuyLots = ((long)(double)t.CellValue("BuyAmountK")) * 1000,
          SellLots = ((long)(double)t.CellValue("SellAmountK")) * 1000,
          BuyNetPL = ((double)t.CellValue("BuyNetPL")),
          //BuyNetPLPip = ((double)t.CellValue("BuyNetPLPip")),
          SellNetPL = ((double)t.CellValue("SellNetPL")),
          //SellNetPLPip = ((double)t.CellValue("SellNetPLPip")),
          NetPL = ((double)t.CellValue("NetPL")),
          SellAvgOpen = (double)t.CellValue(FIELD_SELLAVGOPEN),
          BuyAvgOpen = (double)t.CellValue(FIELD_BUYAVGOPEN),
        }).SingleOrDefault();
      if(s == null)
        return new Summary();
      var rows = trades.Where(t => t.IsBuy).OrderByDescending(t => t.Open).ToArray();
      if(rows.Count() > 0) {
        var rowFirst = rows.First();
        s.BuyPriceFirst = rowFirst.Open;
        s.BuyLotsFirst = rowFirst.Lots;
        s.BuyTradeID_First = rowFirst.Id;

        var rowLast = rows.LastTrade();
        s.BuyPriceLast = rowLast.Open;
        s.BuyLotsLast = rowLast.Lots;
        s.BuyTradeID_Last = rowLast.Id;

        s.BuyPositions = rows.Count();
      }
      rows = trades.Where(t => !t.IsBuy).OrderBy(t => t.Open).ToArray();
      if(rows.Count() > 0) {
        var rowFirst = rows.First();
        s.SellPriceFirst = rowFirst.Open;
        s.SellLotsFirst = rowFirst.Lots;
        s.SellTradeID_First = rowFirst.Id;

        var rowLast = rows.LastTrade();
        s.SellTradeID_Last = rowLast.Id;
        s.SellPriceLast = rowLast.Open;
        s.SellLotsLast = rowLast.Lots;

        s.SellPositions = rows.Count();
      }
      return s;
    }
    #endregion

    #region GetRows
    private FXCore.RowAut[] GetRows(string tableName, string pair) {
      return GetRows(tableName).Where(r => r.CellValue(FIELD_INSTRUMENT) + "" == pair).ToArray();
    }
    public void RefreshOrders() {
      GetTable(TABLE_ORDERS).Refresh();
    }
    private FXCore.RowAut[] GetRows(string tableName, bool updateTable = false) {
      if(!IsLoggedIn)
        return new FXCore.RowAut[0];
      //lock (lockGetRows) {
      var table = GetTable(tableName);
      if(table == null)
        return new FXCore.RowAut[0];
      var rows = table.Rows as FXCore.RowsEnumAut;
      try {
        var rows1 = rows.Cast<FXCore.RowAut>().ToArray();
        return rows1;
      } catch(Exception exc) {
        if(!updateTable)
          return GetRows(tableName, true);
        else
          throw new ArrayTypeMismatchException();
      }
      //}
    }
    #endregion

    #region GetTrades
    Trade[] _emptyTrades = new Trade[0];
    public Trade[] GetTrades() {
      try {
        return OpenTrades.Any() ? OpenTrades.Values.ToArray() : _emptyTrades;
      } catch(Exception exc) {
        RaiseError(exc);
        TradesReset();
        return IsLoggedIn ? GetTrades() : _emptyTrades;
      }
    }
    void TradesReset() { _OpenTrades = null; }
    public Trade GetTrade(string tradeId) { return GetTrades().Where(t => t.Id == tradeId).SingleOrDefault(); }
    public Trade GetTrade(bool buy, bool last) { return last ? GetTradeLast(buy) : GetTradeFirst(buy); }
    public Trade GetTradeFirst(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).FirstOrDefault(); }
    public Trade GetTradeLast(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).LastOrDefault(); }
    public Trade[] GetTrades(bool buy) { return GetTrades().Where(t => t.Buy == buy).ToArray(); }
    public Trade[] GetTrades(string pair) { return GetTrades().ByPair(pair); }
    public Trade[] GetTrades(string pair, bool buy) {
      return GetTrades().ByPair(pair).Where(t => t.Buy == buy).ToArray();
    }
    public Trade[] GetTradesInternal(string Pair) {
      try {
        var trades = (from t in GetRows(TABLE_TRADES)
                      orderby t.CellValue("BS") + "", t.CellValue("TradeID") + "" descending
                      where new[] { "", (t.CellValue(FIELD_INSTRUMENT) + "").ToLower() }.Contains(Pair.ToLower())
                      select InitTrade(t)).ToArray();
        return trades;
      } catch(Exception exc) {
        if(wasRowDeleted(exc))
          return GetTradesInternal(Pair);
        RaiseError(exc);
        return null;
      }
    }
    static bool wasRowDeleted(Exception exc) { return exc.Message.ToLower().Contains("row was deleted"); }

    public Trade[] GetClosedTrades(string Pair) {
      //      lock (getTradesLock) {
      try {
        return (from t in GetRows(TABLE_CLOSED_TRADES)
                orderby t.CellValue("BS") + "", t.CellValue("TradeID") + "" descending
                where new[] { "", (t.CellValue(FIELD_INSTRUMENT) + "").ToLower() }.Contains(Pair.ToLower())
                select InitClosedTrade(t)).ToArray();
      } catch(Exception exc) {
        RaiseError(exc);
        return new Trade[0];
      }
      //    }
    }
    Trade InitClosedTrade(FXCore.RowAut t) {
      var trade = TradeFactory(t.CellValue(FIELD_INSTRUMENT) + "");
      {
        trade.Id = t.CellValue("TradeID") + "";
        trade.Buy = (t.CellValue("BS") + "") == "B";
        trade.IsBuy = (t.CellValue("BS") + "") == "B";
        trade.PL = (double)t.CellValue("PL");
        trade.GrossPL = (double)t.CellValue("GrossPL");
        trade.Lots = (Int32)t.CellValue("Lot");
        trade.Open = (double)t.CellValue("Open");
        trade.Close = (double)t.CellValue("Close");
        trade.Time2 = (DateTime)t.CellValue("OpenTime");// ((DateTime)t.CellValue("Time")).AddHours(coreFX.ServerTimeOffset);
        trade.Time2Close = (DateTime)t.CellValue("CloseTime");// ((DateTime)t.CellValue("Time")).AddHours(coreFX.ServerTimeOffset);
        trade.OpenOrderID = t.CellValue("OpenOrderID") + "";
        trade.OpenOrderReqID = t.CellValue("OpenOrderReqID") + "";
        trade.Remark = new TradeRemark(t.CellValue("OQTXT") + "");
        trade.CommissionByTrade = CommissionByTrade;
        trade.Kind = PositionBase.PositionKind.Closed;
      };
      trade.StopAmount = StopAmount(trade);
      trade.LimitAmount = LimitAmount(trade);
      return trade;
    }
    double StopAmount(Trade trade) {
      if(trade.Stop == 0)
        return 0;
      var diff = InPips(trade.Pair, trade.Stop - trade.Open);
      return PipsAndLotInMoney(trade.IsBuy ? diff : -diff, trade.Lots, trade.Pair);
    }
    double LimitAmount(Trade trade) {
      if(trade.Limit == 0)
        return 0;
      var diff = InPips(trade.Pair, trade.Limit - trade.Open);
      return PipsAndLotInMoney(trade.IsBuy ? diff : -diff, trade.Lots, trade.Pair);
    }
    Trade InitTrade(FXCore.RowAut t) {
      var trade = TradeFactory(t.CellValue(FIELD_INSTRUMENT) + "");
      {
        trade.Id = t.CellValue("TradeID") + "";
        trade.Buy = (t.CellValue("BS") + "") == "B";
        trade.IsBuy = (bool)t.CellValue("IsBuy");
        trade.PL = (double)t.CellValue("PL");
        trade.GrossPL = (double)t.CellValue("GrossPL");
        trade.Lots = (Int32)t.CellValue("Lot");
        trade.Open = (double)t.CellValue("Open");
        trade.Close = (double)t.CellValue("Close");
        trade.Time2 = (DateTime)t.CellValue("Time");// ((DateTime)t.CellValue("Time")).AddHours(coreFX.ServerTimeOffset);
        trade.OpenOrderID = t.CellValue("OpenOrderID") + "";
        trade.OpenOrderReqID = t.CellValue("OpenOrderReqID") + "";
        //StopOrderID = t.CellValue("StopOrderID") + "";
        //LimitOrderID = t.CellValue("LimitOrderID") + "";
        trade.Remark = new TradeRemark(t.CellValue("QTXT") + "");
        trade.CommissionByTrade = CommissionByTrade;
      };
      if(!IsFIFO(trade.Pair)) {
        trade.Limit = (double)t.CellValue("Limit");
        trade.Stop = (double)t.CellValue("Stop");
      } else {
        var netLimit = GetNetLimitOrder(trade);
        if(netLimit != null)
          trade.Limit = netLimit.Rate;
        ;
        var netStop = GetNetStopOrder(trade);
        if(netStop != null)
          trade.Stop = netStop.Rate;
      }

      trade.StopAmount = StopAmount(trade);
      trade.LimitAmount = LimitAmount(trade);
      return trade;
    }
    Trade InitTrade(FXCore.ParserAut t) {
      var trade = TradeFactory(t.GetValue(FIELD_INSTRUMENT) + "");
      {
        trade.Id = t.GetValue("TradeID") + "";
        trade.Pair = t.GetValue(FIELD_INSTRUMENT) + "";
        trade.Buy = (t.GetValue("BS") + "") == "B";
        trade.IsBuy = (bool)t.GetValue("IsBuy");
        trade.PL = (double)t.GetValue("PL");
        trade.GrossPL = (double)t.GetValue("GrossPL");
        trade.Limit = (double)t.GetValue("Limit");
        trade.Stop = (double)t.GetValue("Stop");
        trade.Lots = (Int32)t.GetValue("Lot");
        trade.Open = (double)t.GetValue("Open");
        trade.Close = (double)t.GetValue("Close");
        trade.Time2 = (DateTime)t.GetValue("Time");// ((DateTime)t.GetValue("Time")).AddHours(coreFX.ServerTimeOffset);
        trade.OpenOrderID = t.GetValue("OpenOrderID") + "";
        trade.OpenOrderReqID = t.GetValue("OpenOrderReqID") + "";
        trade.Remark = new TradeRemark(t.GetValue("QTXT") + "");
        trade.CommissionByTrade = CommissionByTrade;
      };
      trade.StopAmount = StopAmount(trade);
      trade.LimitAmount = LimitAmount(trade);
      trade.IsParsed = true;
      return trade;
    }
    Trade InitTrade(string t) {
      return InitTrade(new NameValueParser(t));
    }
    Trade InitTrade(NameValueParser t) {
      var trade = TradeFactory(t.Get(FIELD_INSTRUMENT));
      {
        trade.Id = t.Get("TradeID");
        trade.Pair = t.Get(FIELD_INSTRUMENT);
        trade.Buy = (t.Get("BS") + "") == "B";
        trade.IsBuy = t.GetBool("IsBuy");
        trade.PL = t.GetDouble("PL");
        trade.GrossPL = t.GetDouble("GrossPL");
        trade.Limit = t.GetDouble("Limit");
        trade.Stop = t.GetDouble("Stop");
        trade.Lots = t.GetInt("Lot");
        trade.Open = t.GetDouble("Open");
        trade.Close = t.GetDouble("Close");
        trade.Time2 = t.GetDateTime("Time");// ((DateTime)t.GetValue("Time")).AddHours(coreFX.ServerTimeOffset);
        trade.OpenOrderID = t.Get("OpenOrderID");
        trade.OpenOrderReqID = t.Get("OpenOrderReqID");
        trade.Remark = new TradeRemark(t.Get("QTXT"));
        trade.CommissionByTrade = CommissionByTrade;
      };
      if(IsFIFO(trade.Pair)) {
        var netLimit = GetNetLimitOrder(trade);
        if(netLimit != null)
          trade.Limit = netLimit.Rate;
        ;
        var netStop = GetNetStopOrder(trade);
        if(netStop != null)
          trade.Stop = netStop.Rate;
      }
      trade.StopAmount = StopAmount(trade);
      trade.LimitAmount = LimitAmount(trade);
      trade.IsParsed = true;
      return trade;
    }
    #endregion

    #region GetOrders
    string[] _stopLimitOrderTypes = new[] { "S", "L", "SE", "LE" };
    bool IsNetOrderFilter(Order order) {
      return order.Status != "S" && (IsFIFO(order.Pair) ? order.IsNetOrder || order.OCOBulkID > 0 : _stopLimitOrderTypes.Contains(order.Type));
    }
    bool IsEntryOrderFilter(Order order) { return order.OCOBulkID == 0 && string.IsNullOrWhiteSpace(order.PrimaryOrderID) && !order.IsNetOrder; }
    Func<Order, string> _ordersOrderBy = o => o.OrderID;
    string GetNetStopOrderId(string pair) {
      object stopId, limitId;
      Desk.GetNetSLOrders(pair, AccountID, false, out stopId, out limitId);
      return stopId + "";
    }
    public double GetNetSLLimitOrder(string pair) {
      object stopId, limitId;
      Desk.GetNetSLOrders(pair, AccountID, false, out stopId, out limitId);
      return EntryOrders.Where(kv => kv.Key == limitId + "").Select(eo => eo.Value.Rate).DefaultIfEmpty().Single();
    }
    public double GetNetSLOrder(string pair) {
      object stopId, limitId;
      Desk.GetNetSLOrders(pair, AccountID, false, out stopId, out limitId);
      return EntryOrders.Where(kv => kv.Key == limitId + "").Select(eo => eo.Value.Rate).DefaultIfEmpty().Single();
    }

    public double GetNetOrderRate(string pair, bool isStop, bool getFromInternal = false) {
      return GetNetOrders(pair, getFromInternal)
        .Where(IsNetOrderFilter)
        .Where(o => o.Type.StartsWith(isStop ? "S" : "L"))
        .Select(o => o.Rate).DefaultIfEmpty().Single();
    }
    public Order GetNetStopOrder(Trade trade, bool getFromInternal = false) {
      return GetNetOrders(trade, getFromInternal).Where(IsNetOrderFilter).LastOrDefault(o => o.Type.StartsWith("S"));
    }
    public Order GetNetLimitOrder(Trade trade, bool getFromInternal = false) {
      return GetNetOrders(trade, getFromInternal).Where(IsNetOrderFilter).LastOrDefault(o => o.Type.StartsWith("L"));
    }
    public Order[] GetNetOrders(Trade trade, bool getFromInternal = false) {
      return GetNetOrders(trade.Pair, getFromInternal);
    }
    public Order[] GetNetOrders(string pair, bool getFromInternal = false) {
      var orders = GetOrders(pair, getFromInternal);
      return orders.Where(IsNetOrderFilter).ToArray();
      //if (IsFIFO(pair))
      //  return orders.Where(IsNetOrderFilter).ToArray();
      //var orderIds = new[]{trade.LimitOrderID,trade.StopOrderID};
      //return orders.Where(o => orderIds.Contains(o.OrderID)).ToArray();
    }
    public Order[] GetEntryOrders(string Pair, bool getFromInternal = false) {
      return GetOrders(Pair, getFromInternal).Where(IsEntryOrderFilter).ToArray();
    }
    public Order[] GetOrders(string Pair) { return GetOrders(Pair, false); }
    public Order[] GetOrders(string Pair, bool getFromInternal = false) {
      return (from order in ((/*true||*/getFromInternal) ? GetOrdersInternal(Pair) : EntryOrders.Values.ToArray())
              where order.Pair == Pair || Pair == ""
              orderby order.OrderID
              select order).ToArray();
    }
    public Order[] GetOrdersInternal(string Pair) {
      try {
        return (from t in GetRows(TABLE_ORDERS)
                where new[] { (t.CellValue(FIELD_INSTRUMENT) + "").ToLower(), "" }.Contains(Pair.ToLower())
                select InitOrder(t)).ToArray();
      } catch(System.Runtime.InteropServices.COMException exc) {
        if(wasRowDeleted(exc))
          return GetOrdersInternal(Pair);
        throw;
      }
    }
    Order InitOrder(FXCore.RowAut row) {
      var order = new Order();
      order.OrderID = (String)row.CellValue("OrderID");
      order.RequestID = (String)row.CellValue("RequestID");
      order.AccountID = (String)row.CellValue("AccountID");
      order.AccountName = (String)row.CellValue("AccountName");
      order.OfferID = (String)row.CellValue("OfferID");
      order.Pair = (String)row.CellValue("Instrument");
      order.TradeID = (String)row.CellValue("TradeID");
      order.NetQuantity = (Boolean)row.CellValue("NetQuantity");
      order.BS = (String)row.CellValue("BS");
      order.Stage = (String)row.CellValue("Stage");
      order.Side = (int)row.CellValue("Side");
      order.Type = (String)row.CellValue("Type");
      order.FixStatus = (String)row.CellValue("FixStatus");
      order.Status = (String)row.CellValue("Status");
      order.StatusCode = (int)row.CellValue("StatusCode");
      order.StatusCaption = (String)row.CellValue("StatusCaption");
      order.Lot = (int)row.CellValue("Lot");
      order.AmountK = (Double)row.CellValue("AmountK");
      order.Rate = (Double)row.CellValue("Rate");
      order.SellRate = (Double)row.CellValue("SellRate");
      order.BuyRate = (Double)row.CellValue("BuyRate");
      order.Stop = (Double)row.CellValue("Stop");
      //order.UntTrlMove = (Double)t.CellValue("UntTrlMove");
      order.Limit = (Double)row.CellValue("Limit");
      order.Time = (DateTime)row.CellValue("Time");
      order.IsBuy = (Boolean)row.CellValue("IsBuy");
      order.IsConditionalOrder = (Boolean)row.CellValue("IsConditionalOrder");
      order.IsEntryOrder = (Boolean)row.CellValue("IsEntryOrder");
      order.Lifetime = (int)row.CellValue("Lifetime");
      order.AtMarket = (String)row.CellValue("AtMarket");
      order.TrlMinMove = (int)row.CellValue("TrlMinMove");
      order.TrlRate = (Double)row.CellValue("TrlRate");
      order.Distance = (int)row.CellValue("Distance");
      order.GTC = (String)row.CellValue("GTC");
      order.Kind = (String)row.CellValue("Kind");
      order.QTXT = (String)row.CellValue("QTXT");
      //order.StopOrderID = (String)t.CellValue("StopOrderID");
      //order.LimitOrderID = (String)t.CellValue("LimitOrderID");
      order.TypeSL = (int)row.CellValue("TypeSL");
      order.TypeStop = (int)row.CellValue("TypeStop");
      order.TypeLimit = (int)row.CellValue("TypeLimit");
      order.OCOBulkID = (int)row.CellValue("OCOBulkID");
      order.PrimaryOrderID = (string)row.CellValue("PrimaryOrderID");

      //order.PipCost = GetPipCost(order.Pair);
      order.PointSize = GetPipSize(order.Pair);
      //order.PipsTillRate = InPips(order.Pair, (order.Rate - GetCurrentPrice(order.Pair, order.IsBuy)).Abs());
      order.PipsTillRate = (int)row.CellValue("Distance");
      if(order.Stop != 0) {
        order.StopInPips = order.GetStopInPips((p, s) => InPips(p, s));
        order.StopInPoints = order.GetStopInPoints((p, s) => InPoints(p, s));
        order.StopAmount = PipsAndLotInMoney(order.StopInPips, order.Lot, order.Pair);
      }
      if(order.Limit != 0) {
        order.LimitInPips = order.GetLimitInPips((p, l) => InPips(p, l));
        order.LimitInPoints = order.GetLimitInPoints((p, l) => InPoints(p, l));
        order.LimitAmount = PipsAndLotInMoney(order.LimitInPips, order.Lot, order.Pair);
      }
      return order;
    }
    double StopAmount(Order order) {
      var pips = (order.TypeStop > 1 ? order.Stop : InPips(order.Pair, order.Stop - order.Rate)).Abs();
      if(!order.IsBuy)
        pips = -pips;
      return PipsAndLotInMoney(pips, order.Lot, order.Pair);
    }
    double LimitAmount(Order order) {
      return PipsAndLotInMoney(order.TypeLimit > 1 ? order.Limit : InPips(order.Pair, order.Limit - order.Rate), order.Lot, order.Pair).Abs();
    }
    double DifferenceInMoney(double price1, double price2, int lot, string pair) {
      return PipsAndLotInMoney(InPips(pair, price1 - price2), lot, pair);
    }

    Order InitOrder(FXCore.ParserAut row) {
      var order = new Order();
      order.OrderID = (String)row.GetValue("OrderID");
      order.RequestID = (String)row.GetValue("RequestID");
      order.AccountID = (String)row.GetValue("AccountID");
      order.AccountName = (String)row.GetValue("AccountName");
      order.OfferID = (String)row.GetValue("OfferID");
      order.Pair = (String)row.GetValue("Instrument");
      order.TradeID = (String)row.GetValue("TradeID");
      order.NetQuantity = (Boolean)row.GetValue("NetQuantity");
      order.BS = (String)row.GetValue("BS");
      order.Stage = (String)row.GetValue("Stage");
      order.Side = (int)row.GetValue("Side");
      order.Type = (String)row.GetValue("Type");
      order.FixStatus = (String)row.GetValue("FixStatus");
      order.Status = (String)row.GetValue("Status");
      order.StatusCode = (int)row.GetValue("StatusCode");
      order.StatusCaption = (String)row.GetValue("StatusCaption");
      order.Lot = (int)row.GetValue("Lot");
      order.AmountK = (Double)row.GetValue("AmountK");
      order.Rate = (Double)row.GetValue("Rate");
      order.SellRate = (Double)row.GetValue("SellRate");
      order.BuyRate = (Double)row.GetValue("BuyRate");
      order.Stop = (Double)row.GetValue("Stop");
      //order.UntTrlMove = (Double)t.GetValue("UntTrlMove");
      order.Limit = (Double)row.GetValue("Limit");
      order.Time = (DateTime)row.GetValue("Time");
      order.IsBuy = (Boolean)row.GetValue("IsBuy");
      order.IsConditionalOrder = (Boolean)row.GetValue("IsConditionalOrder");
      order.IsEntryOrder = (Boolean)row.GetValue("IsEntryOrder");
      order.Lifetime = (int)row.GetValue("Lifetime");
      order.AtMarket = (String)row.GetValue("AtMarket");
      order.TrlMinMove = (int)row.GetValue("TrlMinMove");
      order.TrlRate = (Double)row.GetValue("TrlRate");
      order.Distance = (int)row.GetValue("Distance");
      order.GTC = (String)row.GetValue("GTC");
      order.Kind = (String)row.GetValue("Kind");
      order.QTXT = (String)row.GetValue("QTXT");
      //order.StopOrderID = (String)t.GetValue("StopOrderID");
      //order.LimitOrderID = (String)t.GetValue("LimitOrderID");
      order.TypeSL = (int)row.GetValue("TypeSL");
      order.TypeStop = (int)row.GetValue("TypeStop");
      order.TypeLimit = (int)row.GetValue("TypeLimit");
      order.OCOBulkID = (int)row.GetValue("OCOBulkID");
      //order.PipCost = GetPipCost(order.Pair);
      order.PointSize = GetPipSize(order.Pair);
      order.PipsTillRate = (int)row.GetValue("Distance");
      if(order.Stop != 0) {
        order.StopInPips = order.GetStopInPips((p, s) => InPips(p, s));
        order.StopInPoints = order.GetStopInPoints((p, s) => InPoints(p, s));
        order.StopAmount = PipsAndLotInMoney(order.StopInPips, order.Lot, order.Pair);
      }
      if(order.Limit != 0) {
        order.LimitInPips = order.GetLimitInPips((p, l) => InPips(p, l));
        order.LimitInPoints = order.GetLimitInPoints((p, l) => InPoints(p, l));
        order.LimitAmount = PipsAndLotInMoney(order.LimitInPips, order.Lot, order.Pair);
      }
      return order;
    }

    Order InitOrder(string t) { return InitOrder(new NameValueParser(t)); }
    Order InitOrder(NameValueParser row) {
      var order = new Order();
      order.OrderID = row.Get("OrderID");
      order.RequestID = row.Get("RequestID");
      order.AccountID = row.Get("AccountID");
      order.AccountName = row.Get("AccountName");
      order.OfferID = row.Get("OfferID");
      order.Pair = row.Get("Instrument");
      order.TradeID = row.Get("TradeID");
      order.NetQuantity = row.Get("NetQuantity") == "Y";
      order.BS = row.Get("BS");
      order.Stage = row.Get("Stage");
      order.Side = row.GetInt("Side");
      order.Type = row.Get("Type");
      order.FixStatus = row.Get("FixStatus");
      order.Status = row.Get("Status");
      order.StatusCode = row.GetInt("StatusCode");
      order.StatusCaption = row.Get("StatusCaption");
      order.Lot = row.GetInt("Lot");
      order.AmountK = row.GetDouble("AmountK");
      order.Rate = row.GetDouble("Rate");
      order.SellRate = row.GetDouble("SellRate");
      order.BuyRate = row.GetDouble("BuyRate");
      order.Stop = row.GetDouble("Stop");
      //order.UntTrlMove = t.GetValue("UntTrlMove");
      order.Limit = row.GetDouble("Limit");
      order.Time = row.GetDateTime("Time");
      order.IsBuy = row.GetBool("IsBuy");
      order.IsConditionalOrder = row.GetBool("IsConditionalOrder");
      order.IsEntryOrder = row.GetBool("IsEntryOrder");
      order.Lifetime = row.GetInt("Lifetime");
      order.AtMarket = row.Get("AtMarket");
      order.TrlMinMove = row.GetInt("TrlMinMove");
      order.TrlRate = row.GetDouble("TrlRate");
      order.Distance = row.GetInt("Distance");
      order.GTC = row.Get("GTC");
      order.Kind = row.Get("Kind");
      order.QTXT = row.Get("QTXT");
      //order.StopOrderID = t.GetValue("StopOrderID");
      //order.LimitOrderID = t.GetValue("LimitOrderID");
      order.TypeSL = row.GetInt("TypeSL");
      order.TypeStop = row.GetInt("TypeStop");
      order.TypeLimit = row.GetInt("TypeLimit");
      order.OCOBulkID = row.GetInt("OCOBulkID");
      //order.PipCost = GetPipCost(order.Pair);
      order.PointSize = GetPipSize(order.Pair);
      order.PipsTillRate = row.GetInt("Distance");
      if(order.Stop != 0) {
        order.StopInPips = order.GetStopInPips((p, s) => InPips(p, s));
        order.StopInPoints = order.GetStopInPoints((p, s) => InPoints(p, s));
        order.StopAmount = PipsAndLotInMoney(order.StopInPips, order.Lot, order.Pair);
      }
      if(order.Limit != 0) {
        order.LimitInPips = order.GetLimitInPips((p, l) => InPips(p, l));
        order.LimitInPoints = order.GetLimitInPoints((p, l) => InPoints(p, l));
        order.LimitAmount = PipsAndLotInMoney(order.LimitInPips, order.Lot, order.Pair);
      }
      return order;
    }


    #endregion

    #region GetTables
    static object _tablesManagerLocker = new object();
    FXCore.TablesManagerAut _tablesManager;

    public FXCore.TablesManagerAut TablesManager {
      get {
        if(!IsLoggedIn)
          return null;
        lock(_tablesManagerLocker)
          if(_tablesManager == null)
            _tablesManager = (FXCore.TablesManagerAut)Desk.TablesManager;
        return _tablesManager;
      }
    }
    //    Dictionary<string, int> tableIndices = new Dictionary<string, int>();
    C.ConcurrentDictionary<string, FXCore.TableAut> tableIndices = new C.ConcurrentDictionary<string, FXCore.TableAut>();
    [CLSCompliant(false)]
    public FXCore.TableAut GetTable(string TableName, bool updateTable = false) {
      return ((CoreFX)CoreFX).Table(TableName);
      if(!IsLoggedIn)
        return null;
      if(updateTable && tableIndices.ContainsKey(TableName)) {
        FXCore.TableAut t;
        tableIndices.TryRemove(TableName, out t);
      }
      Func<string, FXCore.TableAut> addTable = (tableName) => {
        for(var i = 1; i <= TablesManager.TableCount; i++) {
          var t = TablesManager.Table(i) as FXCore.TableAut;
          if(t.Type == tableName) {
            return t;
          }
        }
        throw new Exception("Table " + TableName + " not found in FXCM.");
      };
      return tableIndices.GetOrAdd(TableName, addTable);
    }
    #endregion

    #region GetPrice
    #region CurrentPrices
    Dictionary<string, Price> currentPrices = new Dictionary<string, Price>();
    Price GetCurrentPrice(string pair) { return !currentPrices.ContainsKey(pair) ? GetPriceInternal(pair) : currentPrices[pair]; }
    void SetCurrentPrice(Price price) { currentPrices[price.Pair] = price; }
    #endregion
    public Price GetPrice(string pair, bool useInternal) { return useInternal ? GetPriceInternal(pair) : GetCurrentPrice(pair); }
    public Price GetPrice(string pair) { return GetCurrentPrice(pair); }
    Price GetPriceInternal(string pair) {
      var price = GetPrice(GetRows(TABLE_OFFERS, pair).FirstOrDefault());
      if(price == null) {
        tableIndices.Clear();
        throw new NullReferenceException();
      }
      return price;
    }
    private Price GetPrice(FXCore.RowAut Row) {
      return Row == null ? null : new Price(Row.CellValue(FIELD_INSTRUMENT) + "") {
        Ask = (double)Row.CellValue(FIELD_ASK),
        Bid = (double)Row.CellValue(FIELD_BID),
        AskChangeDirection = (int)Row.CellValue(FIELD_ASKCHANGEDIRECTION),
        BidChangeDirection = (int)Row.CellValue(FIELD_BIDCHANGEDIRECTION),
        Time2 = ConvertDateToLocal((DateTime)Row.CellValue(FIELD_TIME))
      };
    }
    private Price GetPrice(FXCore.ParserAut Row) {
      return new Price(Row.GetValue(FIELD_INSTRUMENT) + "") {
        Ask = (double)Row.GetValue(FIELD_ASK),
        Bid = (double)Row.GetValue(FIELD_BID),
        AskChangeDirection = (int)Row.GetValue(FIELD_ASKCHANGEDIRECTION),
        BidChangeDirection = (int)Row.GetValue(FIELD_BIDCHANGEDIRECTION),
        Time2 = ConvertDateToLocal((DateTime)Row.GetValue(FIELD_TIME))
      };
    }
    private Price GetPrice(NameValueParser Row) {
      return new Price(Row.Get(FIELD_INSTRUMENT)) {
        Ask = Row.GetDouble(FIELD_ASK),
        Bid = Row.GetDouble(FIELD_BID),
        AskChangeDirection = Row.GetInt(FIELD_ASKCHANGEDIRECTION),
        BidChangeDirection = Row.GetInt(FIELD_BIDCHANGEDIRECTION),
        Time2 = ConvertDateToLocal(Row.GetDateTime(FIELD_TIME))
      };
    }
    #endregion

    #endregion

    #endregion

    #region FIX

    #region STOP/LIMIT
    public PendingOrder FixCreateStop(string tradeId, double stop, string remark) {
      return FixCreateStopLimit(Desk.FIX_STOP, tradeId, stop, remark);
    }
    public PendingOrder FixCreateLimit(string tradeId, double limit, string remark) {
      return FixCreateStopLimit(Desk.FIX_LIMIT, tradeId, limit, remark);
    }
    PendingOrder FixCreateStopLimit(int fixOrderKnd, string tradeId, double rate, string remark) {
      object requestID;
      var isStop = fixOrderKnd == Desk.FIX_STOP;
      var isLimit = fixOrderKnd == Desk.FIX_LIMIT;
      var stop = isStop ? rate : 0;
      var limit = isLimit ? rate : 0;
      var po = new PendingOrder(tradeId, stop, limit, remark);
      try {
        //lock (PendingOrders) {
        //  if (PendingOrders.Contains(po)) return po;
        //  PendingOrders.Add(po);
        //}
        var rateFlag = isStop ? Desk.SL_STOP : isLimit ? Desk.SL_LIMIT : Desk.SL_NONE;
        lock(globalOrderPending) {
          Desk.CreateFixOrder3Async(fixOrderKnd, tradeId, rate, 0, "", "", "", true, 0, remark, rateFlag, 0, Desk.TIF_GTC, out requestID);
        }
        po.RequestId = requestID + "";
        return po;
      } catch(ArgumentException exc) {
        PendingOrders.Remove(po);
        if(exc.Message.Contains("The trade with the specified identifier is not found."))
          throw new TradeNotFoundException(tradeId);
        RaiseError(new Exception(string.Format("TradeId:{0},Stop:{1},Remark:{2}", tradeId, rate, remark), exc));
        return null;
      } catch(Exception exc) {
        RemovePendingOrder(po);
        RaiseError(new Exception(string.Format("TradeId:{0},Stop:{1},Remark:{2}", tradeId, rate, remark), exc));
        return null;
      }
    }


    [MethodImpl(MethodImplOptions.Synchronized)]
    public string CreateEntryOrder(string pair, bool isBuy, int amount, double rate, double stop, double limit) {
      try {
        object psRequestId = "";
        object psOrderId = "";
        Desk.CreateEntryOrder3(AccountID, pair, isBuy, amount, rate, 0, 0, stop, limit, 0, 0, out psOrderId, out psRequestId);
        return psOrderId + "";

        string orderId = "";
        var slType = (stop > 0 ? Desk.SL_PEGSTOPOPEN : 0) + (limit > 0 ? Desk.SL_PEGLIMITOPEN : 0);
        bool orderDone = false;
        var rc = new EventHandler<RequestEventArgs>((s, e) => {
          if(e.RequestId == psRequestId + "") {
            orderDone = true;
            var order = GetOrders("").OrderByRequestId(psRequestId + "");
            if(order != null) {
              orderId = order.OrderID;
              EntryOrders[orderId] = order;
            }
          }
        });
        var rf = new EventHandler<RequestEventArgs>((s, e) => {
          if(e.RequestId == psRequestId + "")
            orderDone = true;
        });
        RequestFailed += rf;
        RequestComplete += rc;
        Desk.CreateEntryOrder3Async(AccountID, pair, isBuy, amount, rate, 0, slType, stop, limit, 0, 0, out psRequestId);
        Task.Factory.StartNew(() => {
          while(!orderDone) {
            Thread.Sleep(100);
          }
        }).Wait(TimeSpan.FromMinutes(1));
        RequestFailed -= rf;
        RequestComplete -= rc;
        return orderId;
      } catch(Exception exc) {
        throw new Exception(new { pair, isBuy, amount, rate, stop, limit } + "", exc);
      }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ChangeEntryOrderStopLimit(string orderId, double rate, bool isStop) {
      object psRequestId;
      Desk.ChangeEntryOrderStopLimit3Async(orderId, rate, isStop ? Desk.SL_STOP : Desk.SL_LIMIT, 0, out psRequestId);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ChangeOrderRate(Order order, double rate) {
      try {
        Desk.ChangeOrderRate(order.OrderID, rate, 0);
        return;
      } catch(Exception exc) {
        DeleteOrder(order.OrderID);
        FixOrderOpenEntry(order.Pair, order.IsBuy, order.Lot, rate, order.Stop, order.Limit, order.QTXT);
        return;
      }

      if(GetFixOrderKindString(order.Pair, order.IsBuy, rate) != order.Type) {
        DeleteOrder(order.OrderID);
        FixOrderOpenEntry(order.Pair, order.IsBuy, order.Lot, rate, order.Stop, order.Limit, order.QTXT);
      } else
        Desk.ChangeOrderRate(order.OrderID, rate, 0);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ChangeOrderRate(string orderId, double rate) {
      object psRequestId;
      Desk.ChangeOrderRate2Async(orderId, rate, 0, out psRequestId);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ChangeOrderAmount(string orderId, int lot) {
      object psRequestId;
      Desk.ChangeEntryOrderAmountAsync(orderId, lot, out psRequestId);
    }
    public void ChangeOrderAmount(Order order, int lot) {
      ChangeOrderAmount(order.OrderID, lot);
    }
    #endregion

    #region Entry
    public void DeleteOrders(string Pair, bool isBuy) {
      DeleteOrders(GetOrdersInternal(Pair).Where(o => o.IsBuy == isBuy).ToArray());
    }
    public void DeleteOrders(string pair) {
      DeleteOrders(GetOrders(pair));
    }
    public void DeleteOrders(params Order[] orders) {
      orders.ToList().ForEach(order => DeleteOrder(order.OrderID));
    }
    public void DeleteOrders(params string[] orderIds) {
      orderIds.ToList().ForEach(orderId => DeleteOrder(orderId));
    }
    public void DeleteOrder(Order order) {
      DeleteOrder(order.OrderID);
    }

    public bool DeleteOrder(string orderId) {
      return DeleteOrder(orderId, true);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public bool DeleteOrder(string orderId, bool async = true) {
      try {
        if(GetOrders("").Any(o => o.OrderID == orderId)) {
          object reqId;
          if(async)
            Desk.DeleteOrderAsync(orderId, out reqId);
          else
            Desk.DeleteOrder(orderId);
          EntryOrders.Remove(orderId);
        }
        return true;
      } catch(Exception exc) {
        EntryOrdersReset();
        throw new Exception("OrderId:" + orderId, exc);
      }
    }
    public string GetFixOrderKindString(string pair, bool buy, double orderRate) {
      return GetFixOrderKindString(buy, GetPrice(pair), orderRate);
    }
    public string GetFixOrderKindString(bool buy, Price currentPrice, double orderRate) {
      var kind = GetFixOrderKind(buy, currentPrice, orderRate);
      return kind == Desk.FIX_ENTRYLIMIT ? "LE" : kind == Desk.FIX_ENTRYSTOP ? "SE" : "";
    }
    int GetFixOrderKind(string pair, bool buy, double orderRate) {
      return GetFixOrderKind(buy, GetPrice(pair), orderRate);
    }
    int GetFixOrderKind(bool buy, Price currentPrice, double orderRate) {
      if(buy)
        return currentPrice.Ask > orderRate ? Desk.FIX_ENTRYLIMIT : Desk.FIX_ENTRYSTOP;
      return currentPrice.Bid < orderRate ? Desk.FIX_ENTRYLIMIT : Desk.FIX_ENTRYSTOP;
    }

    public PendingOrder FixOrderOpenEntry(string pair, bool buy, int lots, double rate, double stop, double limit, string remark) {
      lock(globalOrderPending) {
        object requestID;
        try {
          var po = new PendingOrder(pair, buy, lots, rate, stop, limit, remark);
          if(!PendingOrders.Contains(po)) {
            var fixOrderKind = GetFixOrderKind(buy, GetPrice(pair), rate);
            Desk.CreateFixOrderAsync(fixOrderKind, "", rate, 0, "", AccountID, pair, buy, lots, remark, 0, out requestID);
            po.RequestId = requestID + "";
            PendingOrders.Add(po);
          }
          return po;
        } catch(Exception exc) {
          throw new Exception(string.Format("Pair:{0},Buy:{1},Lots:{2}", pair, buy, lots), exc);
        }
      }
    }
    public void ChangeEntryOrderRate(string orderId, double rate) {
      var order = GetOrders("").SingleOrDefault(o => o.OrderID == orderId);
      if(order == null)
        return;
      var shouldBeType = GetFixOrderKindString(order.Pair, order.IsBuy, rate);
      if(shouldBeType == order.Type)
        Desk.ChangeOrderRate2(orderId, rate, 1);
      else {
        Desk.DeleteOrder(orderId);
        FixOrderOpenEntry(order.Pair, order.IsBuy, order.Lot, rate, order.Stop, order.Limit, order.QTXT);
      }
    }
    public void ChangeEntryOrderLot(string orderId, int lot) {
      object o1;
      var order = GetOrders("").SingleOrDefault(o => o.OrderID == orderId);
      if(order != null)
        Desk.ChangeEntryOrderAmountAsync(orderId, lot, out o1);
    }
    public void ChangeEntryOrderPeggedLimit(string orderId, double rate) {
      object o1;
      var order = GetOrders("").SingleOrDefault(o => o.OrderID == orderId);
      if(order != null) {
        rate = order.IsBuy ? rate.Abs() : -rate.Abs();
        Desk.ChangeEntryOrderStopLimit2(orderId, rate, Desk.SL_PEGLIMITOPEN, 0, out o1);
      }
    }
    public void ChangeEntryOrderPeggedStop(string orderId, double rate) {
      object o;
      Desk.ChangeEntryOrderStopLimit2(orderId, rate, Desk.SL_PEGSTOPCLOSE, 0, out o);
    }
    public void DeleteEntryOrderLimit(string orderId) { DeleteEntryOrderStopLimit(orderId, false); }
    public void DeleteEntryOrderStop(string orderId) { DeleteEntryOrderStopLimit(orderId, true); }
    public void DeleteEntryOrderStopLimit(string orderId, bool isStop) {
      Desk.DeleteEntryOrderStopLimit(orderId, isStop);
    }
    #endregion

    #region Open
    ObservableCollection<PendingOrder> _pendingLimits = new ObservableCollection<PendingOrder>();
    public ObservableCollection<PendingOrder> PendingLimits { get { return _pendingLimits; } set { _pendingLimits = value; } }

    ObservableCollection<PendingOrder> _pendingStops = new ObservableCollection<PendingOrder>();
    public ObservableCollection<PendingOrder> PendingStops { get { return _pendingStops; } set { _pendingStops = value; } }

    ObservableCollection<PendingOrder> _pendingOrders = new ObservableCollection<PendingOrder>();
    public ObservableCollection<PendingOrder> PendingOrders { get { return _pendingOrders; } set { _pendingOrders = value; } }

    static private object globalOrderPending = new object();
    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price) {
      return OpenTrade(pair, buy, lots, takeProfit, stopLoss, price == null ? 0 : buy ? price.Ask : price.Bid, remark);
    }
    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, double rate, string remark) {
      object orderId, DI;
      try {
        Desk.OpenTrade2(AccountID, pair, buy, lots, rate, "", 0, Desk.SL_NONE, 0, 0, 0, out orderId, out DI);
      } catch(Exception exc) {
        RaiseError(exc);
      }
      return null;
      return FixOrderOpen(pair, buy, lots, takeProfit, stopLoss, remark);
    }
    public PendingOrder FixOrderOpen(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark) {
      object requestID;
      var po = new PendingOrder(pair, buy, lots, 0, stopLoss, takeProfit, remark);
      try {
        lock(PendingOrders) {
          if(PendingOrders.Contains(po))
            return po;
          PendingOrders.Add(po);
        }
        lock(globalOrderPending) {
          Desk.CreateFixOrderAsync(Desk.FIX_OPENMARKET, "", 0, 0, "", AccountID, pair, buy, lots, remark, 0, out requestID);
        }
        //var iSLType = Desk.SL_NONE;
        //if (stopLoss > 0) iSLType += Desk.SL_STOP;
        //if (takeProfit > 0) iSLType += Desk.SL_LIMIT;
        //Desk.OpenTrade2(AccountID, pair, buy, lots, 0, "", 0, iSLType, stopLoss, takeProfit, 0, out psOrderID, out psDI);
        po.RequestId = requestID + "";
        return po;
      } catch(Exception exc) {
        RemovePendingOrder(po);
        RaiseError(new Exception(string.Format("Pair:{0},Buy:{1},Lots:{2}", pair, buy, lots), exc), pair, buy, lots, stopLoss, takeProfit, remark);
        return null;
      }
    }
    #endregion

    #region Close
    public void CloseTradesAsync(Trade[] trades) {
      foreach(var trade in trades)
        CloseTradeAsync(trade);
    }

    public void CloseTradeAsync(Trade trade) {
      object o1;
      try {
        Desk.CloseTradeAsync(trade.Id, trade.Lots, 0, "", 0, out o1);
      } catch(Exception exc) {
        RaiseError(exc);
      }
    }
    public void CloseTrades(Trade[] trades) {
      if(trades.Length == 0)
        return;
      if(IsHedged)
        foreach(var trade in trades)
          CloseTrade(trade);
      else {
        var buys = trades.IsBuy(true);
        if(buys.Length > 0)
          OpenTrade(buys[0].Pair, false, buys.Sum(t => t.Lots), 0, 0, "", null);

        var sells = trades.IsBuy(false);
        if(sells.Length > 0)
          OpenTrade(sells[0].Pair, true, sells.Sum(t => t.Lots), 0, 0, "", null);
      }
    }
    public void CloseTrade(string tradeId) {
      CloseTrade(GetTrade(tradeId));
    }
    public void CloseTrade(Trade trade) {
      CloseTrade(trade, trade.Lots);
    }
    public bool CloseTrade(Trade trade, int lot) { return CloseTrade(trade, lot, null); }

    public bool CloseTrade(Trade trade, int lot, Price price) {
      object o1, o2;
      try {
        if(IsHedged)
          Desk.CloseTrade(trade.Id, lot, trade.Close, "", 0, out o1, out o2);
        else
          OpenTrade(trade.Pair, !trade.Buy, trade.Lots, 0, 0, "", null);
        return true;
      } catch(Exception exc) {
        RaiseError(exc);
        return false;
      }
    }
    public void CloseAllTrades() {
      var trades = GetTrades("");
      var pairs = trades.Select(t => t.Pair).Distinct().ToArray();
      foreach(var pair in pairs) {
        object o1, o2;
        if(trades.Any(t => t.Pair == pair && t.IsBuy))
          Desk.CloseTradesByInstrument(pair, AccountID, true, 0, "", 0, out o1, out o2);
        if(trades.Any(t => t.Pair == pair && !t.IsBuy))
          Desk.CloseTradesByInstrument(pair, AccountID, false, 0, "", 0, out o1, out o2);
      }
    }
    public PendingOrder FixOrderClose(string tradeId, string remark) {
      var trade = GetTrades("").SingleOrDefault(t => t.Id == tradeId);
      if(trade == null) {
        RaiseError(new Exception("TradeId:" + tradeId + " not found."));
        return null;
      }
      return FixOrderClose(tradeId, trade.Lots, remark);
    }
    public PendingOrder FixOrderClose(string tradeId, int lots, string remark) {
      lock(globalClosePending) {
        object requestID;
        var po = new PendingOrder(tradeId, 0, 0, remark);
        try {
          lock(PendingOrders) {
            if(PendingOrders.Contains(po))
              return po;
            PendingOrders.Add(po);
          }
          Desk.CreateFixOrderAsync(Desk.FIX_CLOSEMARKET, tradeId, 0, 0, "", "", "", true, lots, remark, 0, out requestID);
          po.RequestId = requestID + "";
          return po;
        } catch(Exception exc) {
          RemovePendingOrder(po);
          RaiseError(new Exception(string.Format("TradeId:{0},Lots:{1}", tradeId, lots), exc));
          return null;
        }
      }
    }
    public object[] FixOrdersClose(params string[] tradeIds) {
      var ordersList = new List<object>();
      foreach(var tradeId in tradeIds)
        ordersList.Add(FixOrderClose(tradeId));
      return ordersList.ToArray();
    }
    public object[] FixOrdersCloseAll() {
      var ordersList = new List<object>();
      var trade = GetTrades("").FirstOrDefault();
      while(trade != null) {
        ordersList.Add(FixOrderClose(trade.Id));
        trade = GetTrades("").FirstOrDefault();
      }
      return ordersList.ToArray();
    }
    public object FixOrderClose(string tradeId) { return FixOrderClose(tradeId, Desk.FIX_CLOSEMARKET, null as Price); }
    public bool ClosePair(string pair) {
      var b = ClosePair(pair, true);
      var s = ClosePair(pair, false);
      return b || s;
    }
    public bool ClosePair(string pair, bool buy, int lot) {
      try {
        if(this.IsFIFO(pair)) {
          var lotToDelete = Math.Min(lot, GetTradesInternal(pair).IsBuy(buy).Lots());
          if(lotToDelete > 0) {
            OpenTrade(pair, !buy, lotToDelete, 0, 0, "", null);
          } else {
            RaiseError(new Exception("Pair [" + pair + "] does not have positions to close."));
            return false;
          }
        } else {
          while(lot > 0) {
            var tradeToClose = GetTrades(pair).FirstOrDefault();
            if(tradeToClose == null)
              return false;
            lot -= tradeToClose.Lots;
            CloseTrade(tradeToClose);
          }
        }
        return true;
      } catch(Exception exc) {
        RaiseError(exc);
        return false;
      }
    }
    public bool ClosePair(string pair, bool buy) {
      try {
        if(GetTrades(buy).ByPair(pair).Count() > 0) {
          object o1;
          Desk.CloseTradesByInstrument2Async(pair, AccountID, buy, 0, "", 0, Desk.TIF_GTC, out o1);
        }
      } catch(Exception exc) {
        RaiseError(new ErrorEventArgs(exc));
        return false;
      }
      return true;
    }
    static private object globalClosePending = new object();
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public object FixOrderClose(string tradeId, int mode, Price price) {
      return FixOrderClose(tradeId, mode, price, 0);
    }
    public object FixOrderClose(string tradeId, int mode, Price price, int lot) {
      lock(globalClosePending) {
        object psOrderID = null, psDI;
        var trade = GetTradesInternal("").Where(t => t.Id == tradeId).SingleOrDefault();
        try {
          if(trade == null)
            return null;
          if(lot == 0)
            lot = trade.Lots;
          var dRate = mode == Desk.FIX_CLOSEMARKET || price == null ? 0 : trade.Buy ? price.Bid : price.Ask;
          if(Hedging) {
            //RunWithTimeout.WaitFor<object>.Run(TimeSpan.FromSeconds(5), () => {
            Desk.CreateFixOrder(mode, tradeId, dRate, 0, "", "", "", true, lot, "", out psOrderID, out psDI);
            try {
              while(GetTrade(tradeId) != null)
                Thread.Sleep(10);
            } catch(System.Runtime.InteropServices.COMException comExc) {
              RaiseOrderError(new OrderExecutionException("Closing TradeID:" + tradeId, comExc));
            }
            //  return null;
            //});
          } else {
            ClosePair(trade.Pair, trade.Buy, trade.Lots);
            return null;
          }
          return psOrderID;
        } catch(Exception exc) {
          //RaiseOrderError(new OrderExecutionException("Closing TradeID:" + tradeId + " failed.", exc));
          throw exc;
        }
      }
    }
    #endregion

    #region Stop/Loss

    C.ConcurrentDictionary<string, bool> IsFifoDictionary = new C.ConcurrentDictionary<string, bool>();
    public bool IsFIFO(string pair) {
      return IsFifoDictionary.GetOrAdd(pair, GetFifo);
      /*
      if (can_close == ps.PERMISSION_ENABLED)
        Console.WriteLine("Closing Enabled");
      else if (can_close == ps.PERMISSION_DISABLED)
        Console.WriteLine("Closing Disabled");
      else if (can_close == ps.PERMISSION_HIDDEN)
        Console.WriteLine("Not available");
       * */
    }

    private bool GetFifo(string pair) {
      FXCore.PermissionCheckerAut ps = (FXCore.PermissionCheckerAut)Desk.PermissionChecker;
      int can_net = ps.CanCreateNetStopLimitOrder(pair);
      int can_close = ps.CanCreateMarketCloseOrder(pair);
      return can_close == ps.PERMISSION_HIDDEN;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="tradeId"></param>
    /// <param name="takeProfit">Negative fot Sell Trade</param>
    /// <param name="remark"></param>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void FixOrderSetLimit(string tradeId, double takeProfit, string remark) {
      try {
        object a = "", b;
        var trade = GetTrade(tradeId);
        if(trade == null)
          return;
        var isBuy = trade.Buy;
        var pair = trade.Pair;
        double limitRate = takeProfit;
        var prevRate = 0.0;
        var spreadToAdd = (isBuy ? 1 : -1) * GetCurrentPrice(pair).Spread;
        Stopwatch sw = Stopwatch.StartNew();
        if(IsFIFO(trade.Pair)) {
          if(takeProfit == 0)
            Desk.DeleteNetStopLimitAsync(trade.Pair, AccountID, trade.Buy, false, out a);
          else {
            try {
              var order = GetNetLimitOrder(trade);
              if(order != null)
                prevRate = order.Rate;
              else if(GetTrades(pair).Length == 0)
                return;
              Desk.ChangeNetStopLimit2Async(pair, AccountID, isBuy, limitRate, false, 0, out a);
            } catch(Exception exc) {
              TradesReset();
              EntryOrdersReset();
              ReLoginIfCommandIsDisabled(exc);
              RaiseError(new Exception(new { pair, limitRate, prevRate } + "", exc));
            } finally {
              //Debug.WriteLine("{0}:{1:n1}ms for {2} from {3} to {4} @ " + DateTime.Now.ToString("mm:ss.fff")
              //, MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, pair, prevRate, limitRate);
            }
          }
        } else {
          //if (/*trade.Limit == 0 && */InPips(trade.Pair, takeProfit).Abs() < 500)
          if(takeProfit == 0)
            Desk.DeleteTradeStopLimit(tradeId, false);
          else {
            Desk.ChangeTradeStopLimit3(tradeId, limitRate, false, 0, out b);
          }
        }
      } catch(Exception exc) {
        RaiseError(new ErrorEventArgs(exc));
      }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="tradeId"></param>
    /// <param name="stopLoss">Negative for Buy Trade</param>
    /// <param name="remark"></param>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void FixOrderSetStop(string tradeId, double stopLoss, string remark) {
      try {
        object a = "", b;
        var trade = GetTrade(tradeId);
        if(trade == null)
          return;
        var isBuy = trade.Buy;
        var pair = trade.Pair;
        var spreadToAdd = (isBuy ? -1 : 1) * GetCurrentPrice(pair).Spread;
        var prevRate = 0.0;
        double stopRate = stopLoss;
        Stopwatch sw = Stopwatch.StartNew();
        if(IsFIFO(trade.Pair)) {
          if(stopLoss == 0)
            Desk.DeleteNetStopLimit(trade.Pair, AccountID, trade.Buy, true);
          else {
            try {
              var prevOrder = GetNetStopOrder(trade);
              if(prevOrder != null)
                prevRate = prevOrder.Rate;
              Desk.ChangeNetStopLimit2Async(pair, AccountID, isBuy, stopRate, true, 0, out a);
            } catch(Exception exc) {
              TradesReset();
              EntryOrdersReset();
              ReLoginIfCommandIsDisabled(exc);
              RaiseError(new Exception(new { pair, stopRate, prevRate } + "", exc));
            } finally {
              //Debug.WriteLine("{0}:{1:n1}ms for {2} from {3} to {4} @ " + DateTime.Now.ToString("mm:ss.fff")
              //  , MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, pair, prevRate, stopRate);
            }
          }
        } else {
          if(stopLoss == 0)
            Desk.DeleteTradeStopLimit(tradeId, true);
          else {
            Desk.ChangeTradeStopLimit3(tradeId, stopRate, true, 0, out b);
          }
        }
      } catch(Exception exc) {
        RaiseError(exc);
      }
    }

    private void ReLoginIfCommandIsDisabled(Exception exc) {
      if(exc.Message.Contains("This command is disabled."))
        Task.Factory.StartNew(() => CoreFX.ReLogin());
    }


    #endregion

    #endregion

    #region Pips/Points Converters
    public double Round(string pair, double value, int digitOffset = 0) { return Math.Round(value, GetDigits(pair) + digitOffset); }
    public double InPips(string pair, double? price) { return (price / GetPipSize(pair)).GetValueOrDefault(); }

    public double InPoints(string pair, double? price) { return TradesManagerStatic.InPoins(this, pair, price); }

    C.ConcurrentDictionary<string, double> pointSizeDictionary = new C.ConcurrentDictionary<string, double>() /*{ { "EUR/USD", .0001 } }*/;
    public double GetPipSize(string pair) {
      if(!IsLoggedIn)
        return double.NaN;
      pair = pair.ToUpper();
      Func<string, double> foo = p => {
        var offer = GetOffer(p);
        if(offer == null) {
          Desk.SetOfferSubscription(pair, "Enabled");
          offer = GetOffer(pair);
        }
        return offer.PointSize;
      };
      return pointSizeDictionary.GetOrAdd(pair, foo);
      //}
    }
    C.ConcurrentDictionary<string, double> pipCostDictionary = new C.ConcurrentDictionary<string, double>();
    //public double GetPipCost(string pair) {
    //  if (!IsLoggedIn) return double.NaN;
    //  pair = pair.ToUpper();
    //  if (!pipCostDictionary.ContainsKey(pair))
    //    GetOffers().ToList().ForEach(o => pipCostDictionary[o.Pair] = o.PipCost);
    //  Func<string, double> getPipCost = p => {
    //    CoreFX.SetOfferSubscription(p);
    //    return GetOffer(p).PipCost;
    //  };
    //  return pipCostDictionary.GetOrAdd(pair, getPipCost);
    //}

    Dictionary<string, int> digitDictionary = new Dictionary<string, int>() { { "EUR/USD", 5 } };
    public int GetDigits(string pair) {
      if(!IsLoggedIn)
        return 0;
      pair = pair.ToUpper();
      if(!digitDictionary.ContainsKey(pair))
        GetOffers().ToList().ForEach(o => digitDictionary[o.Pair] = o.Digits);
      return digitDictionary[pair];
    }

    public double PipsToMarginCall { get { return PipsToMarginCallCore(accountInternal); } }
    public double PipsToMarginCallCore(Account account) {
      var trades = GetTrades();
      if(!trades.Any())
        return int.MaxValue;
      var summaries = (
        from t in trades
        group t by t.Pair into sms
        let lot = sms.Sum(t => t.Lots)
        let rate = RateForPipAmount(GetPrice(sms.Key))
        let pipSize = GetPipSize(sms.Key)
        select new {
          Pair = sms.Key, Amount = (double)lot, Trades = sms.ToArray(),
          PipAmount = TradesManagerStatic.PipAmount(sms.Key, lot, rate, pipSize)
        }).ToArray();
      //Func<string,double> pipAnount = (pair)=>TradesManagerStatic.PipAmount(pair,)
      var ptmcs = from t in summaries
                  select new {
                    t.Pair,
                    t.Amount,
                    PMC = TradesManagerStatic.PipToMarginCall(
                      t.Trades.Lots(),
                      t.Trades.GrossInPips(),
                      account.Balance,
                      GetOffer(t.Pair).MMRLong,
                      GetBaseUnitSize(t.Pair),
                      t.PipAmount)
                  };
      return ptmcs.Select(p => p.PMC * p.Amount).DefaultIfEmpty(double.NaN).Sum() / ptmcs.Select(p => p.Amount).DefaultIfEmpty(double.NaN).Sum();
    }

    private double PipsToMarginCallPerUnitCurrency(Trade[] trades) {
      var summaries = (from t in trades
                       group t by t.Pair into sms
                       select new { Pair = sms.Key, AmountK = sms.Sum(t => t.Lots / 1000) }
      ).ToArray();

      var sm = (from s in summaries
                join o in pipCostDictionary
                on s.Pair equals o.Key
                let pc = o.Value
                select new {
                  AmountK = s.AmountK,
                  PipCost = pc >= 10 ? pc * .01 : pc
                }).ToArray();
      if(sm.Count() == 0 || sm.Sum(s => s.AmountK) == 0)
        return 1 / .1;
      var pipCostWieghtedAverage = sm.Sum(s => s.AmountK * s.PipCost) / sm.Sum(s => s.AmountK);
      return -1 / (sm.Sum(s => s.AmountK) * pipCostWieghtedAverage);
    }

    public static double PipsAndLotToMoney(double pips, double lots, double pipCost, double baseUnitSize) {
      return pips * lots * pipCost / baseUnitSize;
    }
    public double PipsAndLotInMoney(double pips, int lot, string pair) {
      return TradesManagerStatic.PipsAndLotToMoney(pair, pips, lot, GetPrice(pair).Average, GetPipSize(pair));
      //return PipsAndLotToMoney(pips, lot, GetPipCost(pair), GetBaseUnitSize(pair));
    }

    #endregion

    #region (Un)Subscribe

    FXCore.TradeDeskEventsSinkClass mSink;
    int mSubscriptionId = -1;
    bool isSubsribed = false;
    public void Subscribe() {
      if(isSubsribed)
        return;
      Unsubscribe();
      mSink = new FXCore.TradeDeskEventsSinkClass();
      mSink.ITradeDeskEvents_Event_OnRowChangedEx += FxCore_RowChanged;
      mSink.ITradeDeskEvents_Event_OnRowAddedEx += mSink_ITradeDeskEvents_Event_OnRowAddedEx;
      mSink.ITradeDeskEvents_Event_OnRowBeforeRemoveEx += mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx;
      mSink.ITradeDeskEvents_Event_OnRequestCompleted += mSink_ITradeDeskEvents_Event_OnRequestCompleted;
      mSink.ITradeDeskEvents_Event_OnRequestFailed += mSink_ITradeDeskEvents_Event_OnRequestFailed;
      mSink.ITradeDeskEvents_Event_OnSessionStatusChanged += mSink_ITradeDeskEvents_Event_OnSessionStatusChanged;
      mSubscriptionId = Desk.Subscribe(mSink);
      isSubsribed = true;
    }

    public void Unsubscribe() {
      if(mSubscriptionId != -1) {
        try {
          mSink.ITradeDeskEvents_Event_OnRowChangedEx -= FxCore_RowChanged;
          mSink.ITradeDeskEvents_Event_OnRowAddedEx -= mSink_ITradeDeskEvents_Event_OnRowAddedEx;
          mSink.ITradeDeskEvents_Event_OnRowBeforeRemoveEx -= mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx;
          mSink.ITradeDeskEvents_Event_OnRequestCompleted -= mSink_ITradeDeskEvents_Event_OnRequestCompleted;
          mSink.ITradeDeskEvents_Event_OnRequestFailed -= mSink_ITradeDeskEvents_Event_OnRequestFailed;
          mSink.ITradeDeskEvents_Event_OnSessionStatusChanged -= mSink_ITradeDeskEvents_Event_OnSessionStatusChanged;
        } catch { }
        mSink = null;
        try {
          Desk.Unsubscribe(mSubscriptionId);
        } catch { }
        isSubsribed = false;
      }
      mSubscriptionId = -1;
    }

    #endregion

    #region FXCore Event Handlers
    void RemovePendingOrder(PendingOrder po) {
      lock(PendingOrders) {
        if(po != null)
          PendingOrders.Remove(po);
      }
    }

    List<string> ClosedTradeIDs = new List<string>();

    public Order[] EnsureEntryOrders(string pair) {
      _entryOrders = null;
      return GetEntryOrders(pair);
    }
    static object _entryOrdersLocker = new object();
    Dictionary<string, Order> _entryOrders;
    Dictionary<string, Order> EntryOrders {
      get {
        lock(_entryOrdersLocker) {
          if(_entryOrders == null)
            _entryOrders = GetOrdersInternal("").ToDictionary(o => o.OrderID, o => o);
          return _entryOrders;
        }
      }
    }
    void EntryOrdersReset() {
      _entryOrders = null;
    }

    static object _OpenTradesLocker = new object();
    ConcurrentDictionary<string, Trade> _OpenTrades;
    ConcurrentDictionary<string, Trade> OpenTrades {
      get {
        if(_OpenTrades == null)
          lock(_OpenTradesLocker) {
            try {
              _OpenTrades = new ConcurrentDictionary<string, Trade>(GetTradesInternal("").Select(o => new KeyValuePair<string, Trade>(o.Id, o)));
            } catch(Exception exc) {
              RaiseError(exc);
              _OpenTrades = new ConcurrentDictionary<string, Trade>(GetTradesInternal("").Select(o => new KeyValuePair<string, Trade>(o.Id, o)));
            }
          }
        return _OpenTrades;
      }
    }
    void OpenTradesReset() {
      lock(_OpenTradesLocker)
        _OpenTrades = null;
    }
    void OpenTrades_Remove(string tradeId) {
      Trade t;
      if(OpenTrades.ContainsKey(tradeId))
        OpenTrades.TryRemove(tradeId, out t);
    }

    void mSink_ITradeDeskEvents_Event_OnRowAddedEx(object _table, string RowID, string rowText) {
      try {
        FXCore.TableAut table = _table as FXCore.TableAut;
        FXCore.RowAut row;
        Func<FXCore.TableAut, FXCore.RowAut, string> showTable = (t, r) => {
          var columns = (t.Columns as FXCore.ColumnsEnumAut).Cast<FXCore.ColumnAut>()
            .Where(c => (c.Title + "").Length != 0).Select(c => new { c.Title, Value = r.CellValue(c.Title) + "" });
          return new XElement(t.Type.Replace(" ", "_"), columns.Select(c => new XAttribute(c.Title, c.Value)))
            .ToString(SaveOptions.DisableFormatting);
        };
        FXCore.ParserAut parser = Desk.GetParser() as FXCore.ParserAut;
        switch(table.Type.ToLower()) {
          #region Trades
          case "trades": {
              TradesReset();
              var tradeParsed = InitTrade(new NameValueParser(rowText));
              RaiseTradeAdded(tradeParsed);
              break;
              #region Pending Stuff
              lock(globalOrderPending) {
                var poStop = PendingStops.SingleOrDefault(po => po.TradeId == RowID);
                if(poStop != null) {
                  try {
                    FixCreateStop(poStop.TradeId, poStop.Stop, poStop.Remark);
                    PendingStops.Remove(poStop);
                  } catch(TradeNotFoundException) {
                    Debug.WriteLine("Tarde " + RowID + " is not found. Again!");
                  }
                }
                var poLimit = PendingLimits.SingleOrDefault(po => po.TradeId == RowID);
                if(poLimit != null) {
                  try {
                    FixCreateLimit(poLimit.TradeId, poLimit.Limit, poLimit.Remark);
                    PendingLimits.Remove(poLimit);
                  } catch(TradeNotFoundException) {
                    Debug.WriteLine("Tarde " + RowID + " is not found. Again!");
                  }
                }
                var poTrade = PendingOrders.SingleOrDefault(po => po.RequestId == tradeParsed.OpenOrderReqID);
                ////if (poTrade != null && !poTrade.HasKids(PendingOrders)) RemovePendingOrder(poTrade);
                Trade trade = GetTrade(tradeParsed.Id);
                //if (new[] { "", tradeParsed.Pair }.Contains(Pair))
                //  RaiseTradeAdded(tradeParsed);
                if(trade == null && poTrade != null) {
                  FixOrderOpen(poTrade.Pair, poTrade.Buy, poTrade.Lot, poTrade.Limit, poTrade.Stop, poTrade.Remark);
                }
              }
              #endregion
            }
            break;
          #endregion
          #region Orders
          case TABLE_ORDERS:
            EntryOrdersReset();
            var order = InitOrder(rowText);
            RaiseOrderAdded(order);
            var poOrder = PendingOrders.SingleOrDefault(o => o.RequestId == order.RequestID);
            if(poOrder != null) {
              poOrder.OrderId = order.OrderID;
              poOrder.TradeId = order.TradeID;
              var complete = true;
              try {
                if(order.Stop != poOrder.Stop) {
                  FixCreateStop(poOrder.TradeId, poOrder.Stop, poOrder.Remark);
                  complete = false;
                }
                if(order.Limit != poOrder.Limit) {
                  //FixCreateLimit(poOrder.TradeId, poOrder.Limit, poOrder.Remark);
                  complete = false;
                }
              } catch(TradeNotFoundException) {
                PendingLimits.Add(poOrder);
                PendingStops.Add(poOrder);
                complete = true;
              }
              //if (complete) 
              PendingOrders.Remove(poOrder);
            }
            break;
          #endregion
          case "closed trades": {
              TradesReset();
              row = table.FindRow(FIELD_TRADEID, RowID, 0) as FXCore.RowAut;
              var trade = InitClosedTrade(row);
              RaiseTradeClosed(trade);
              try {
                if(ClosedTradeIDs.Contains(trade.Id))
                  break;
                ClosedTradeIDs.Add(trade.Id);
              } catch { }
            }
            break;
        }
      } catch(Exception exc) { RaiseError(exc); }
    }

    Account accountInternal = null;
    DateTime _rowChangeHeartbeatDateTime;
    void FxCore_RowChanged(object _table, string rowID, string rowText) {
      _rowChangeHeartbeatDateTime = DateTime.Now;
      try {
        FXCore.TableAut table = _table as FXCore.TableAut;
        FXCore.RowAut row;
        RaiseRowChanged(table.Type, rowID);
        switch(table.Type.ToLower()) {
          case "offers":
            var price = GetPrice(new NameValueParser(rowText));
            SetCurrentPrice(price);
            RaisePriceChanged( price);
            break;
          case TABLE_ACCOUNTS:
            GetAccount(new NameValueParser(rowText));
            break;
          case TABLE_ORDERS:
            var order = InitOrder(rowText);
            EntryOrders[rowID] = order;
            RaiseOrderChanged(order);
            break;
          case TABLE_TRADES:
            var trade = InitTrade(new NameValueParser(rowText));
            OpenTrades.AddOrUpdate(rowID, trade, (k, t) => trade);
            RaiseTradeChanged(trade);
            break;
        }
      } catch(Exception exc) {
        RaiseError(exc);
      }
    }

    void mSink_ITradeDeskEvents_Event_OnRequestCompleted(string sRequestID) {
      try {
        //Debug.WriteLine("Request +" + sRequestID + " completed.");
        //var po = PendingOrders.SingleOrDefault(o => o.RequestId == sRequestID);
        //if (po != null && !po.HasKids(PendingOrders)) 
        //  RemovePendingOrder(po);
        TradesReset();
        EntryOrdersReset();
        var order = GetOrdersInternal("").SingleOrDefault(o => o.RequestID == sRequestID);
        if(order != null)
          EntryOrders[order.OrderID] = order;
        OnRequestComplete(sRequestID);
      } catch(Exception exc) {
        RaiseError(exc);
      }
      AsyncReset();
    }

    void mSink_ITradeDeskEvents_Event_OnRequestFailed(string sRequestID, string sError) {
      TradesReset();
      EntryOrdersReset();
      OnRequestFailed(sRequestID, sError);
      RemovePendingOrder(PendingOrders.SingleOrDefault(po => po.RequestId == sRequestID));
      AsyncReset();
    }
    void mSink_ITradeDeskEvents_Event_OnSessionStatusChanged(string sStatus) {
      var status = (TradingServerSessionStatus)Enum.Parse(typeof(TradingServerSessionStatus), sStatus);
      if(status == TradingServerSessionStatus.Connected) {
        Unsubscribe();
        Subscribe();
        IsLoggedIn = true;
      } else
        IsLoggedIn = false;
      RaiseSessionStatusChanged(status);
      AsyncReset();
    }


    void mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx(object _table, string RowID, string sExtInfo) {
      try {
        TradesReset();
        EntryOrdersReset();
        FXCore.TableAut table = _table as FXCore.TableAut;
        switch(table.Type.ToLower()) {
          case TABLE_TRADES:
            try {
              OpenTrades_Remove(RowID);
              var trade = InitTrade(sExtInfo);
              RaiseTradeRemoved(trade);
            } catch(Exception exc) {
              Debug.Fail(exc + "");
            }
            break;
          case TABLE_ORDERS:
            try {
              EntryOrders.Remove(RowID);
              var order = InitOrder(sExtInfo);
              TradesReset();
              RaiseOrderRemoved(order);
            } catch(Exception exc) {
              Debug.Fail(exc + "");
            }
            break;
        }
      } catch(Exception exc) {
        RaiseError(exc);
      }
      AsyncReset();
    }

    private void AsyncReset() {
      Task.Factory.StartNew(() => {
        TradesReset();
        EntryOrdersReset();
      });
    }
    #endregion


    #region COM Helpers
    [ComRegisterFunctionAttribute]
    static void RegisterFunction(Type t) {
      Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(
          "CLSID\\{" + t.GUID.ToString().ToUpper() +
             "}\\Programmable");
    }

    [ComUnregisterFunctionAttribute]
    static void UnregisterFunction(Type t) {
      Microsoft.Win32.Registry.ClassesRoot.DeleteSubKey(
          "CLSID\\{" + t.GUID.ToString().ToUpper() +
            "}\\Programmable");
    }
    public string Version() {
      return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + "";
    }
    #endregion

    #region IDisposable Members

    public void Dispose() {
      //GC.SuppressFinalize(this);
    }

    #endregion

    #region FXCore Helpers

    #region Report
    private string GetReport(DateTime dateFrom, DateTime dateTo) {
      var url = Desk.GetReportURL(AccountID, dateFrom, dateTo, "XLS", "", "", 0);
      var wc = new System.Net.WebClient();
      var s = wc.DownloadString(url);
      return s;
    }
    public List<Trade> GetTradesFromReport(DateTime dateFrom, DateTime dateTo) {
      var xml = GetReport(dateFrom, dateTo);
      var xDoc = XElement.Parse(xml);
      var ss = xDoc.GetNamespaceOfPrefix("ss").NamespaceName;
      var worksheet = xDoc.Element(XName.Get("Worksheet", ss));
      var ticketNode = worksheet.Descendants(XName.Get("Data", ss)).Where(x => x.Value == "Ticket #");
      var ticketRow = ticketNode.First().Ancestors(XName.Get("Row", ss)).First();
      var row = ticketRow.NextNode as XElement;
      var trades = new List<Trade>();
      Func<int, XElement> getData = i => row.Descendants(XName.Get("Data", ss)).ElementAt(i);
      while(row.Elements().Count() == 13) {
        var ticket = getData(0);
        if(ticket == null)
          new Exception("Can't find [Ticket #] column");
        var pair = getData(1).Value;
        var volume = (int)double.Parse(getData(2).Value.Replace(",", ""));
        var timeOpen = DateTime.Parse(getData(3).Value);
        var isBuy = getData(4).Value == "";
        double priceOpen = double.Parse(getData(isBuy ? 5 : 4).Value);
        row = row.NextNode as XElement;
        double priceClose = double.Parse(getData(isBuy ? 4 : 5).Value);
        var timeClose = DateTime.Parse(getData(3).Value);
        var grossPL = double.Parse(getData(6).Value);
        var pl = TradesManagerStatic.MoneyAndLotToPips(pair, grossPL, volume, priceClose, GetPipSize(pair));
        var commission = double.Parse(getData(7).Value);
        var rollover = double.Parse(getData(8).Value);
        var trade = TradeFactory(pair);
        {
          trade.Buy = isBuy;
          trade.Open = priceOpen;
          trade.Close = priceClose;
          trade.Commission = commission + rollover;
          trade.GrossPL = grossPL;
          trade.PL = pl;
          trade.Id = ticket.Value;
          trade.IsBuy = isBuy;
          trade.Lots = volume;
          trade.Time2 = TimeZoneInfo.ConvertTimeToUtc(timeOpen);
          trade.Time2Close = TimeZoneInfo.ConvertTimeToUtc(timeClose);
          trade.OpenOrderID = "";
          trade.OpenOrderReqID = "";
        }
        trades.Add(trade);
        row = row.NextNode as XElement;
      }
      return trades;
    }
    public Trade GetLastTrade(string pair) {
      try {
        var trades = GetClosedTrades(pair);
        if(trades.Length == -1)
          trades = GetTradesFromReport(DateTime.Now.AddDays(-7), DateTime.Now.AddDays(1).Date).ToArray();
        return trades.DefaultIfEmpty(() => TradeFactory(pair)).OrderBy(t => t.Id).Last();
      } catch(Exception exc) {
        RaiseError(exc);
        return null;
      }
    }
    #endregion


    dynamic _tradingSettingsProvider;
    dynamic TradingSettingsProvider {
      get {
        if(_tradingSettingsProvider == null && Desk != null)
          _tradingSettingsProvider = Desk.TradingSettingsProvider;
        return _tradingSettingsProvider;
      }
    }
    private C.ConcurrentDictionary<string, int> _baseUnitSize = new C.ConcurrentDictionary<string, int>();
    public int GetBaseUnitSize(string pair) {
      if(!IsLoggedIn)
        return 0;

      pair = pair.ToUpper();
      Func<string, int> foo = p => {
        return TradingSettingsProvider.GetBaseUnitSize(p, AccountID);
      };

      return _baseUnitSize.GetOrAdd(pair, foo(pair));
    }

    private Dictionary<string, int> _MMR = new Dictionary<string, int>();
    public int GetMMR(string pair) {
      if(_MMR.Count == 0)
        foreach(var offer in GetOffers())
          try {
            _MMR.Add(offer.Pair, (int)TradingSettingsProvider.GetMMR(offer.Pair, AccountID));
          } catch(Exception exc) {
            throw;
          }
      return _MMR[pair];
    }


    public class TradingSettings {
      string pair;
      string accountId;
      dynamic TradingSettingsProvider;
      public TradingSettings(FXCore.TradingSettingsProviderAut tsp, string accountId, string pair) {
        this.TradingSettingsProvider = tsp;
        this.accountId = accountId;
        this.pair = pair;
      }
      public int GetBaseUnitSize { get { return TradingSettingsProvider.GetBaseUnitSize(pair, accountId); } }
      public int GetCondDistEntryLimit { get { return TradingSettingsProvider.GetCondDistEntryLimit(pair); } }
      public int GetCondDistEntryStop { get { return TradingSettingsProvider.GetCondDistEntryStop(pair); } }
      public int GetCondDistLimitForEntryOrder { get { return TradingSettingsProvider.GetCondDistLimitForEntryOrder(pair); } }
      public int GetCondDistLimitForTrade { get { return TradingSettingsProvider.GetCondDistLimitForTrade(pair); } }
      public int GetCondDistStopForEntryOrder { get { return TradingSettingsProvider.GetCondDistStopForEntryOrder(pair); } }
      public int GetCondDistStopForTrade { get { return TradingSettingsProvider.GetCondDistStopForTrade(pair); } }
      public int GetMarketStatus { get { return TradingSettingsProvider.GetMarketStatus(pair); } }
      public int GetMaxQuantity { get { return TradingSettingsProvider.GetMaxQuantity(pair, accountId); } }
      public int GetMinQuantity { get { return TradingSettingsProvider.GetMinQuantity(pair, accountId); } }
    }
    Dictionary<string, TradingSettings> tradingSettingsCatalog = new Dictionary<string, TradingSettings>();


    public TradingSettings GetTradingSettings(string pair) {
      if(!tradingSettingsCatalog.ContainsKey(pair))
        tradingSettingsCatalog.Add(pair, new TradingSettings(TradingSettingsProvider as FXCore.TradingSettingsProviderAut, AccountID, pair));
      return tradingSettingsCatalog[pair];
    }

    TimeSpan? _utcTimeSpan;
    TimeSpan UtcTimeSpan {
      get {
        if(_utcTimeSpan == null)
          _utcTimeSpan = DateTime.Now - DateTime.UtcNow;
        return _utcTimeSpan.Value;
      }
    }
    public void SetServerTime(DateTime serverTime) {
      throw new NotImplementedException();
    }
    public DateTime ServerTimeCached { get; set; }
    public DateTime ServerTime { get { return ServerTimeCached = CoreFX.ServerTime; } }

    public bool HasTicks => true;
    static object converterLocker = new object();
    DateTime ConvertDateToLocal(DateTime date) {
      return TimeZoneInfo.ConvertTimeFromUtc(date, TimeZoneInfo.Local);
    }
    DateTime ConvertDateToUTC(DateTime date) {
      return TimeZoneInfo.ConvertTimeToUtc(date);
    }

    C.ConcurrentDictionary<string, double> leverages = new C.ConcurrentDictionary<string, double>();

    public double Leverage(string pair,bool isBuy) {
      if(!IsLoggedIn)
        return 0;
      Func<string, double> addLeverage = p => {
        CoreFX.SetOfferSubscription(p);
        var offer = GetOffer(p);
        return GetBaseUnitSize(pair) / (isBuy? offer.MMRLong:offer.MMRShort);
      };
      return leverages.GetOrAdd(pair, addLeverage);
    }

    static string GetOrderStatusDescr(string orderStatus) {
      if(orderStatus.Length != 1)
        return "Unknown";
      switch(orderStatus[0]) {
        case 'W':
          return "Waiting";
        case 'P':
          return "Processing";
        case 'Q':
          return "Requoted";
        case 'C':
          return "Cancelled";
        case 'E':
          return "Executing";
        case 'R':
          return "Rejected";
        case 'T':
          return "Expired";
        case 'F':
          return "Executed";
        case 'I':
          return "Dealer Intervention";
      }
      return "Unknown";
    }
    protected static Dictionary<string, string> parse(string extInfo) {
      string[] tokens = extInfo.Split(new char[] { '=', ';' });
      Dictionary<string, string> fields = new Dictionary<string, string>();
      int tokensCount = tokens.Length % 2 == 1 ? tokens.Length - 1 : tokens.Length;
      for(int i = 0; i < tokensCount; i += 2)
        fields.Add(tokens[i], tokens[i + 1]);
      return fields;
    }
    #endregion

    #region ITradesManager Members
    #endregion


    public void ResetClosedTrades(string pair) {
      throw new NotImplementedException();
    }
  }

  #region Data Classes
  [Guid("832E1324-EB43-4689-8605-07D7DD82E238")]
  [ComVisible(true)]
  public enum PriceDirectionType { Up = 1, Flat = 0, Down = -1 }

  [ClassInterface(ClassInterfaceType.AutoDual), ComSourceInterfaces(typeof(IOrder2GoEvents))]
  [Guid("5183ADD7-0BA2-4937-B9CE-BC8E5CAC4C80")]
  [ComVisible(true)]
  [Serializable]
  //public class Price : HedgeHog.Bars.Price { }
  public class Summary {
    public static Summary Initialize(Price Price) {
      return new Summary() { PriceCurrent = Price };
    }
    public string Pair { get; set; }
    public string OfferID { get; set; }
    public string BuyTradeID_Last { get; set; }
    public string BuyTradeID_First { get; set; }
    public string SellTradeID_Last { get; set; }
    public string SellTradeID_First { get; set; }
    public double SellNetPL { get; set; }
    //public double SellNetPLPip { get; set; }
    public double SellNetPLPip { get { return SellNetPL / SellLots * 10000; } }
    public double BuyNetPL { get; set; }
    //public double BuyNetPLPip { get; set; }
    public double BuyNetPLPip { get { return BuyNetPL / BuyLots * 10000; } }
    public double SellLots { get; set; }
    public double BuyLots { get; set; }
    public double AmountK { get; set; }
    public double Amount { get { return AmountK * 1000; } }
    public double BuyPriceFirst { get; set; }
    public int BuyLotsFirst { get; set; }
    public double BuyPriceLast { get; set; }
    public int BuyLotsLast { get; set; }
    public double SellPriceFirst { get; set; }
    public int SellLotsFirst { get; set; }
    public double SellPriceLast { get; set; }
    public int SellLotsLast { get; set; }
    public double BuyDelta { get { return BuyPriceFirst - BuyPriceLast; } }
    public double SellDelta { get { return SellPriceLast - SellPriceFirst; } }
    public double SellLPP { get { return SellDelta == 0 ? SellLots : SellLots / SellDelta * PointSize; } }
    public double BuyLPP { get { return BuyDelta == 0 ? BuyLots : BuyLots / BuyDelta * PointSize; } }
    public double PointSize { get; set; }
    public double SellPositions { get; set; }
    public double BuyPositions { get; set; }
    public double BuyToSellLots { get { return (double)Math.Min(BuyLots, SellLots) / Math.Max(BuyLots, SellLots); } }
    public double SellAvgOpen { get; set; }
    public double BuyAvgOpen { get; set; }
    public double StopAmount { get; set; }
    public double LimitAmount { get; set; }
    public Price PriceCurrent { get; set; }
    public double NetPL { get; set; }
  }
  #endregion

  [Serializable]
  public class EntryOrderFailedException : Exception {
    public EntryOrderFailedException() { }
    public EntryOrderFailedException(string message) : base(message) { }
    public EntryOrderFailedException(string message, Exception inner) : base(message, inner) { }
    protected EntryOrderFailedException(
    SerializationInfo info,
    StreamingContext context)
      : base(info, context) { }
  }
  public class TradeNotFoundException : Exception {
    public string TradeId { get; set; }
    public TradeNotFoundException(string tradeId)
      : base("The trade with the specified identifier[" + tradeId + "] is not found.") {
      this.TradeId = tradeId;
    }
  }
}
