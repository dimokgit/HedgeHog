using System;
using System.Collections;
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

  #region FxcmIndicatorsExtensions
  public static class FxcmIndicatorsExtensions {
    public static void FillTSI_CR(this Rate[] ticks) {
      (from dp in Indicators.TSI_CR(ticks)
       join tick in ticks on dp.Time equals tick.StartDate
       select new { tick, dp }).ToList()
       .ForEach(tdp => { tdp.tick.PriceTsi = tdp.dp.Point; tdp.tick.PriceTsiCR = tdp.dp.Point1; });
    }
    public static void FillTSI(this Rate[] ticks, Action<Rate, double?> priceRsi) {
      (from dp in Indicators.TSI(ticks)
       join tick in ticks on dp.Time equals tick.StartDate
       select new { tick, dp.Point }
                  ).ToList().ForEach(tdp => priceRsi(tdp.tick, tdp.Point));
    }
    public static void FillRLW(this Rate[] ticks) {
      (from dp in Indicators.RLW(ticks,14)
       join tick in ticks on dp.Time equals tick.StartDate
       select new { tick, dp.Point }
                  ).ToList().ForEach(tdp => tdp.tick.PriceRlw = tdp.Point);
    }
    private static void FillRSI(this Rate[] ticks, int period, Func<Rate, double> priceSource, Action<Rate, double?> priceRsi) {
      (from dp in Indicators.RSI(ticks, priceSource, period)
                  join tick in ticks on dp.Time equals tick.StartDate
                  select new { tick, dp.Point }
                  ).ToList().ForEach(tdp => priceRsi(tdp.tick, tdp.Point));
    }
    private static void FillRSI(this Rate[] ticks, int period,
      Func<Rate, double> priceSource, Func<Rate, double?> priceDestination, 
      Action<Rate, double?> priceRsi) 
    {
      var startDate = (ticks.FirstOrDefault(t => priceDestination(t).HasValue) ?? ticks.First()).StartDate;
      IndicatorPoint dpPrevious = new IndicatorPoint();
      ticks.Where(t => t.StartDate >= startDate).ToList().ForEach(t => {
        if (!priceDestination(t).HasValue) {
          Rate[] ticksLocal = ticks
            .Where(t1 => t1.StartDate.Between(t.StartDate.AddMinutes(-period - 2), t.StartDate))
            .ToArray().GetMinuteTicks(1);
          if (ticksLocal.Count() >= period) {
            var dp = Indicators.RSI(ticksLocal, priceSource, period).Last();
            if (dp.Point == 0) dp.Point = dpPrevious.Point;
            else dpPrevious = dp;
            priceRsi(t, dp.Point);
          }
        }
      }
      );
    }
    public static void FillTSI(this Rate[] rates, 
      Func<Rate, double?> priceDestination, Action<Rate, double?> priceRsi) {
      int period = 14;
      var startDate = (rates.FirstOrDefault(t => priceDestination(t).HasValue) ?? rates.First()).StartDate;
      IndicatorPoint dpPrevious = new IndicatorPoint();
      rates.Where(t => t.StartDate >= startDate).ToList().ForEach(t => {
        if (!priceDestination(t).HasValue) {
          Rate[] ticksLocal = rates
            .Where(t1 => t1.StartDate.Between(t.StartDate.AddMinutes(-period - 2), t.StartDate))
            .ToArray().GetMinuteTicks(1);
          if (ticksLocal.Count() >= period) {
            var dp = Indicators.TSI(ticksLocal).Last();
            if (dp.Point == 0) dp.Point = dpPrevious.Point;
            else dpPrevious = dp;
            priceRsi(t, dp.Point);
          }
        }
      }
      );
    }

    //static void FractalSell<T>(T[] rates,T rate)where T:Rate {
    //  if (rates[1].AskHigh > Math.Max(rates[0].AskHigh, rates[2].AskHigh))
    //    rate.FractalSell = rates[2].PriceClose;
    //  else rate.FractalSell = 0;
    //}
    //static void FractalBuy<T>(T[] rates,T rate) where T : Rate {
    //  if (rates[1].BidLow < Math.Min(rates[0].BidLow, rates[2].BidLow))
    //    rate.FractalBuy = rates[2].PriceClose;
    //  else rate.FractalBuy = 0;
    //}
    //public static void FillFractal_<T>(this IEnumerable<T> ticks) where T : Rate {
    //  var lastFractal = ticks.LastOrDefault(t => t.FractalSell.HasValue || t.FractalBuy.HasValue);
    //  var startDate = lastFractal == null ? ticks.First().StartDate.AddMinutes(-3): lastFractal.StartDate;
    //  ticks.Where(t => t.StartDate >= startDate).ToList().ForEach(t => {
    //      var ticksLocal = ticks
    //        .Where(t1 => t1.StartDate.Between(t.StartDate.AddMinutes(-3), t.StartDate))
    //        .ToArray().GetMinuteTicks(1).ToArray();
    //      if (ticksLocal.Length > 3) {
    //        FractalSell(ticksLocal.Skip(1).ToArray(), t);
    //        FractalBuy(ticksLocal.Skip(1).ToArray(), t);
    //      }
    //  }
    //  );
    //  var fb = ticks.Where(t => t.FractalBuy > 0).ToArray();
    //  var fs = ticks.Where(t => t.FractalSell > 0).ToArray();
    //}
    //public static void FillFractal(this Rate[] rates) {
    //  var lastFractal = rates.LastOrDefault(t => t.FractalSell.HasValue || t.FractalBuy.HasValue);
    //  var startDate = lastFractal == null ? rates.First().StartDate.AddMinutes(-3) : lastFractal.StartDate;
    //  for (int i = 1; i < rates.Length - 1; i++) {
    //    var ratesLocal = new[] { rates[i - 1], rates[i], rates[i + 1] };
    //    FractalSell(ratesLocal, rates[i]);
    //    FractalBuy(ratesLocal, rates[i]);
    //  }
    //  var fb = rates.Where(t => t.FractalBuy > 0).ToArray();
    //  var fs = rates.Where(t => t.FractalSell > 0).ToArray();
    //}

  }
  #endregion

  #region EventArgs classes
  public class ErrorEventArgs : EventArgs {
    public Exception Error { get; set; }
    public ErrorEventArgs(Exception error) {
      this.Error = error;
    }
  }
  public class OrderErrorEventArgs : ErrorEventArgs {
    public OrderErrorEventArgs(Exception exception) : base(exception) { }
  }
  public class PendingOrderEventArgs : EventArgs {
    public PendingOrder Order;
    public PendingOrderEventArgs(PendingOrder po) {
      this.Order = po;
    }
  }
  public class TradeEventArgs : EventArgs {
    public Trade Trade;
    public TradeEventArgs(Trade trade) {
      this.Trade = trade;
    }
  }
  #endregion

  [ClassInterface(ClassInterfaceType.AutoDual), ComSourceInterfaces(typeof(IOrder2GoEvents))]
  [Guid("2EC43CB6-ED3D-465c-9AF3-C0BBC622663E")]
  [ComVisible(true)]
  public sealed class FXCoreWrapper:IDisposable {
    private readonly string ERROR_FILE_NAME = "FXCM.log";

    

    #region Constants
    public readonly DateTime FX_DATE_NOW = DateTime.FromOADate(0);
    public const string TABLE_ACCOUNTS = "accounts";
    public const string TABLE_OFFERS = "offers";
    public const string TABLE_ORDERS = "orders";
    public const string TABLE_CLOSED = "closed";
    public const string TABLE_SUMMARY = "summary";
    public const string TABLE_TRADES = "trades";
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
    const string FIELD_BIDCHANGEDIRECTION = "BidChangeDirection";
    const string FIELD_ASKCHANGEDIRECTION = "AskChangeDirection";
    const string FIELD_GROSSPL = "GrossPL";
    const string FIELD_CLOSETIME = "CloseTime";
    const string FIELD_SELLAVGOPEN = "SellAvgOpen";
    const string FIELD_BUYAVGOPEN = "BuyAvgOpen";
    const string FIELD_LIMIT = "Limit";
    const string FIELD_PIPCOST = "PipCost";
    #endregion

    #region Events

    public event EventHandler<PendingOrderEventArgs> PendingOrderCompleted;
    void RaisePendingOrderCompleted(PendingOrder po) {
      if (PendingOrderCompleted != null) PendingOrderCompleted(this, new PendingOrderEventArgs(po));
    }

    public event EventHandler<OrderErrorEventArgs> OrderError;
    void RaiseOrderError(Exception exception) {
      try {
        if (OrderError != null) OrderError(this, new OrderErrorEventArgs(exception));
      } catch (Exception exc) { FileLogger.LogToFile(exc,ERROR_FILE_NAME); }
    }
    public event EventHandler<ErrorEventArgs> Error;
    void RaiseError(Exception exception) {
      try {
        if (Error != null) Error(this, new ErrorEventArgs(exception));
        else Debug.Fail(exception + "");
      } catch (Exception exc) { FileLogger.LogToFile(exc,ERROR_FILE_NAME); }
    }
    public class PairNotFoundException : NotSupportedException {
      public PairNotFoundException(string Pair, string table) : base("Pair " + Pair + " not found in " + table + " table.") { }
      public PairNotFoundException(string Pair) : base("Pair " + Pair + " not found.") { }
    }
    public class OrderExecutionException : Exception {
      public OrderExecutionException(string Message, Exception inner) : base(Message, inner) { }
    }

    public delegate void RowChangedEventHandler(string TableType, string RowID);
    public event RowChangedEventHandler RowChanged;
    void RaiseRowChanged(string tableType, string rowID) {
      if (RowChanged != null) RowChanged(tableType, rowID);
    }

    public delegate void PriceChangedEventHandler(Price Price);
    public event PriceChangedEventHandler PriceChanged;

    public delegate void OrderRemovedEventHandler(Order order);
    public event OrderRemovedEventHandler OrderRemoved;
    void RaiseOrderRemoved(Order order) {
      if (OrderRemoved != null) OrderRemoved(order);
    }

    public delegate void TradeAddedEventHandler(Trade trade);
    public event TradeAddedEventHandler TradeAdded;

    void RaiseTradeAdded(Trade trade) {
      if (TradeAdded != null) TradeAdded(trade);
    }

    public event EventHandler<TradeEventArgs> TradeClosed;
    void RaiseTradeClosed(Trade trade) {
      if (TradeClosed != null)
        TradeClosed(this, new TradeEventArgs(trade));
    }

    #endregion

    #region Properties
    private DateTime _prevOfferDate;
    private string _pair = "";
    public string PriceFormat {
      get { return "{0:0."+"".PadRight(Digits,'0')+"}"; }
    }
    public event EventHandler PairChanged;
    public string Pair {
      get { return _pair; }
      set {
        if (_pair == value) return;
        _pair = value.ToUpper();
          if (Desk != null && FindOfferByPair(value) == null) Desk.SetOfferSubscription(value, "Enabled");
        PointSize = 0;
        Digits = 0;
        if (PairChanged != null) PairChanged(this, new EventArgs());
      }
    }

    FXCore.RowAut FindOfferByPair(string pair) {
      try {
        return Desk == null ? null : Desk.FindRowInTable(TABLE_OFFERS, FIELD_INSTRUMENT, pair) as FXCore.RowAut;
      } catch (ArgumentException) {
        return null;
      }
    }
    private double _pointSize;
    public double PointSize {
      get {
        if (_pointSize > 0) return _pointSize;
        if (Pair == "") return 1;
        if (Pair.Length == 0) throw new PairNotFoundException("[Empty]");
        var row = FindOfferByPair(Pair);
        if( row == null ) throw new PairNotFoundException(Pair, TABLE_OFFERS);
        _pointSize = (double)row.CellValue(FIELD_POINTSIZE);
        return _pointSize;
      }
      set {
        _pointSize = value;
      }
    }

    private int _digits;
    public int Digits {
      get {
        if (_digits > 0) return _digits;
        if (Pair.Length == 0) throw new PairNotFoundException("[Empty]");
        var row = FindOfferByPair(Pair);
        if (row == null) throw new PairNotFoundException(Pair, TABLE_OFFERS);
        _digits = (int)row.CellValue(FIELD_DIGITS);
        return _digits;
      }
      set {
        _digits = value;
      }
    }

    int _minimumQuantity = 0;

    string _accountID = "";

    public string AccountID {
      get {
        if (_accountID == "") _accountID = GetAccount(false).ID;
        return _accountID;
      }
    }

    public bool IsLoggedIn { get { 
      try {
        return  Desk != null && Desk.IsLoggedIn();
      } catch { }
      return false;
    } } 
    #endregion

    #region Constructor
    CoreFX coreFX;

    public CoreFX CoreFX {
      get { return coreFX; }
      set { coreFX = value; }
    }
    public FXCoreWrapper(CoreFX coreFX) : this(coreFX, null) { }
    public FXCoreWrapper(CoreFX coreFX,string pair) {
      this.coreFX = coreFX;
      if (pair != null) this.Pair = pair;
      coreFX.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
      coreFX.LoggedOffEvent += new EventHandler<EventArgs>(coreFX_LoggedOffEvent);
      isWiredUp = true;
      PendingOrders.CollectionChanged += PendingOrders_CollectionChanged;
    }

    void PendingOrders_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      switch (e.Action) {
        case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
          var po = e.OldItems[0] as PendingOrder;
          if( po.Parent == null) RaisePendingOrderCompleted(po);
          break;
      }
    }
    ~FXCoreWrapper() {
      LogOff();
      coreFX = null;
    }
    public bool LogOn(string pair,CoreFX core, string user, string password, bool isDemo) {
      return LogOn(pair, core, user, password, new Uri(CoreFX.DefaultUrl), isDemo);
    }
    bool isWiredUp;
    public bool LogOn(string pair, CoreFX core, string user, string password, string url, bool isDemo) {
      return LogOn(pair, core, user, password, new Uri(url), isDemo);
    }
    public bool LogOn(string pair, CoreFX core, string user, string password, Uri url, bool isDemo) {
      if (this.coreFX != null) 
        throw new NotSupportedException(GetType().Name + ".LogOn is not supported when " + GetType().Name + ".coreFx is pre-set in cobstructor.");
      coreFX = core;
      if (!isWiredUp) {
        core.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
        core.LoggedOffEvent += new EventHandler<EventArgs>(coreFX_LoggedOffEvent);
        isWiredUp = true;
      }
      this.Pair = pair;
      if (!core.IsLoggedIn) {
        if (!core.LogOn(user, password, url.ToString(), isDemo))
          return false;
      } else {
        Subscribe();
      }
      return true;
    }

    void coreFX_LoggedOffEvent(object sender, EventArgs e) {
      Unsubscribe();
    }

    void coreFX_LoggedInEvent(object sender, EventArgs e) {
      Subscribe();
    }
    public void LogOn() {
      coreFX.LogOn();
    }

    public FXCore.TradeDeskAut Desk {
      get {
        return coreFX == null ? null : coreFX.Desk;
      }
    }
    public void LogOff() {
      if( coreFX!=null)
        coreFX.Logout();
    }
    #endregion

    #region Get (Ticks/Bars)
    public List<Tick> GetTicks(DateTime startDate, DateTime endDate) {
      return GetTicks(startDate, endDate, maxTicks);
    }
    private static object lockHistory = new object();
    public List<Tick> GetTicks(DateTime startDate, DateTime endDate, int barsMax) {
      lock (lockHistory) {
        var mr = //RunWithTimeout.WaitFor<FXCore.MarketRateEnumAut>.Run(TimeSpan.FromSeconds(5), () =>
         ((FXCore.MarketRateEnumAut)Desk.GetPriceHistory(Pair, "t1", startDate, endDate, barsMax, true, true)).Cast<FXCore.MarketRateAut>().ToArray();
        //);
        return mr.Select((r, i) => new Tick(r.StartDate, r.AskOpen, r.BidOpen, i, true)).ToList();
      }
    }

    public Tick[] GetTicks(int tickCount) {
      DateTime startDate = DateTime.MinValue;
      var ticks = GetTicks(startDate, FX_DATE_NOW,tickCount);
      int timeoutCount = 2;
      if (ticks.Count() > 0) {
        var dateMin = ticks.Min(b => b.StartDate);
        var endDate = ticks.SkipWhile(ts => ts.StartDate == dateMin).First().StartDate;
        while (ticks.Count() < tickCount) {
          try {
            var t = GetTicks(startDate, endDate);
            if (t.Count() == 0) break;
            ticks = ticks.Union(t).OrderBars().ToList();
            dateMin = ticks.Min(b => b.StartDate);
            if (endDate > dateMin) {
              endDate = ticks.SkipWhile(ts => ts.StartDate == dateMin).First().StartDate;
              System.Diagnostics.Debug.WriteLine("Ticks[{2}]:{0} @ {1:t}", ticks.Count(), endDate.ToLongTimeString(), Pair);
            } else
              endDate = endDate.AddSeconds(-3);
          } catch (Exception exc) {
            if (exc.Message.ToLower().Contains("timeout")) {
              coreFX.LogOn();
              if (timeoutCount-- == 0) break;
            }
          }
        }
      }
      return ticks.OrderBars().ToArray();
    }

    public IEnumerable<Rate> GetBarsBase(int period, DateTime startDate) {
      return GetBarsBase(period, startDate, DateTime.FromOADate(0));
    }

    public IEnumerable<Rate> GetBarsBase(int period, DateTime startDate, DateTime endDate) {
      if (period >= 1) {
        startDate = startDate.Round(period);
        if (endDate != DateTime.FromOADate(0)) endDate = endDate.Round(period).AddMinutes(period);
      }
      var ticks = GetBarsBase_(period, startDate, endDate);
      int timeoutCount = 1;
      if (ticks.Count() > 0) {
        endDate = ticks.Min(b => b.StartDate);
        while (startDate < endDate) {
          try {
            var t = GetBarsBase_(period, startDate, endDate);
            if (t.Count() == 0) break;
            ticks = ticks.Union(t).ToList();
            if (endDate > ticks.Min(b => b.StartDate)) {
              endDate = ticks.Min(b => b.StartDate);
              System.Diagnostics.Debug.WriteLine("Bars<" + period + ">:" + ticks.Count() + " @ " + endDate.ToLongTimeString());
            } else
              endDate = endDate.AddSeconds(-30);
          } catch (Exception exc) {
            if (exc.Message.ToLower().Contains("timeout")) {
              if (timeoutCount-- == 0) break;
            }
          }
        }
      }
      return ticks.OrderBars();
    }
    public IEnumerable<Rate> New_GetBarsBase(int period, DateTime startDate, DateTime endDate) {
      var ed = endDate == FX_DATE_NOW ? DateTime.MaxValue : endDate;
      if (period >= 1) {
        startDate = startDate.Round().Round(period);
        if (endDate != DateTime.FromOADate(0)) endDate = endDate.Round(period).AddMinutes(period);
      }
      var ticks = GetBarsBase_(period, startDate, endDate);
      int timeoutCount = 1;
      if (ticks.Count() > 0) {
        endDate = ticks.Min(b => b.StartDate);
        while (startDate < endDate) {
          try {
            var t = GetBarsBase_(period, startDate, endDate);
            if (t.Count() == 0) break;
            ticks = ticks.Union(t).ToList();
            if (endDate > ticks.Min(b => b.StartDate)) {
              endDate = ticks.Min(b => b.StartDate);
              System.Diagnostics.Debug.WriteLine("Bars<" + period + ">:" + ticks.Count() + " @ " + endDate.ToLongTimeString());
            } else
              endDate = endDate.AddSeconds(-60);
          } catch (Exception exc) {
            if (exc.Message.ToLower().Contains("timeout")) {
              if (timeoutCount-- == 0) break;
            }
          }
        }
      }
      return ticks.Where(startDate, ed).OrderBars();
    }
    readonly int maxTicks = 300;
    [MethodImpl(MethodImplOptions.Synchronized)]
    List<Rate> GetBarsBase_(int period, DateTime startDate, DateTime endDate) {
      try {
        return period == 0 ?
          GetTicks(startDate, endDate).Cast<Rate>().ToList() :
          GetBars(period, startDate, endDate);
      } catch (Exception exc) {
        var empty = (period == 0 ? new Tick[] { }.Cast<Rate>() : new Rate[] { }).ToList();
        var e = new Exception("StartDate:" + startDate + ",EndDate:" + endDate + ":" + Environment.NewLine + "\t" + exc.Message, exc);
        if (exc.Message.Contains("The specified date's range is empty.") ||
            exc.Message.Contains("Object reference not set to an instance of an object.")) {
          return empty;
        }
        throw e;
      }
    }

    public List<Rate> GetBars(int period, DateTime startDate) {
      return GetBars(period, startDate, DateTime.FromOADate(0));
    }
    Rate RateFromMarketRate(FXCore.MarketRateAut r) {
      return new Rate(true) {
        AskClose = Math.Round(r.AskClose, this.Digits), AskHigh = Math.Round(r.AskHigh, Digits),
        AskLow = Math.Round(r.AskLow, Digits), AskOpen = Math.Round(r.AskOpen, Digits),
        BidClose = Math.Round(r.BidClose, Digits), BidHigh = Math.Round(r.BidHigh, Digits),
        BidLow = Math.Round(r.BidLow, Digits), BidOpen = Math.Round(r.BidOpen, Digits),
        StartDate = r.StartDate
      };
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<Rate> GetBars(int period, DateTime startDate, DateTime endDate) {
      lock (lockHistory) {
        var mr = //RunWithTimeout.WaitFor<FXCore.MarketRateEnumAut>.Run(TimeSpan.FromSeconds(5), () =>
          ((FXCore.MarketRateEnumAut)Desk.GetPriceHistory(Pair, (BarsPeriodType)period + "", startDate, endDate, int.MaxValue, true, true)).Cast<FXCore.MarketRateAut>().ToArray();
        return mr.Select((r) => RateFromMarketRate(r)).ToList();
        //);
      }
    }

    public void GetBars(int Period, DateTime StartDate, DateTime EndDate, ref List<Rate> Bars) {
      if (Bars == null) Bars = Period == 0 ? new List<Tick>().OfType<Rate>().ToList() : new List<Rate>();
      if (Bars.Count == 0)
        Bars = GetBarsBase(Period, StartDate, EndDate).OrderBars().ToList();
      if (Bars.Count == 0) return;
      if (EndDate != DateTime.FromOADate(0))
        foreach (var bar in Bars.Where(b => b.StartDate > EndDate).ToArray())
          Bars.Remove(bar);
      if (Bars.Count == 0) GetBars(Period, StartDate, EndDate, ref Bars);
      else {
        var minimumDate = Bars.Min(b => b.StartDate);
        if (StartDate < minimumDate)
          Bars = Bars.Union(GetBarsBase(Period, StartDate, minimumDate)).OrderBars().ToList();
        var maximumDate = Bars.Max(b => b.StartDate);
        if (EndDate == DateTime.FromOADate(0) || EndDate > maximumDate) {
          var b = GetBarsBase(Period, maximumDate, EndDate);
          Bars = Bars.Union(b).OrderBars().ToList();
        }
      }
      if (EndDate != DateTime.FromOADate(0))
        foreach (var bar in Bars.Where(b => b.StartDate > EndDate).ToArray())
          Bars.Remove(bar);
    }
    #endregion

    #region FX Tables
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public Account GetAccount() { return GetAccount(true); }
    public Account GetAccount(bool includeOtherInfo) {
      var row = GetRows(TABLE_ACCOUNTS).First();
      var trades = new Trade[]{};
      var account = new Account() {
        ID = row.CellValue(FIELD_ACCOUNTID) + "",
        Balance = (double)row.CellValue(FIELD_BALANCE),
        UsableMargin = (double)row.CellValue(FIELD_USABLEMARGIN),
        IsMarginCall = row.CellValue(FIELD_MARGINCALL)+"" == "W",
        Equity = (double)row.CellValue(FIELD_EQUITY),
        Hedging = row.CellValue("Hedging").ToString() == "Y",
        Trades = includeOtherInfo ? trades = GetTrades("") : null,
        StopAmount = includeOtherInfo? AmountOfStopLoss(trades):0,
        ServerTime = ServerTime
      };
      if( includeOtherInfo)
        account.PipsToMC = (int)(account.UsableMargin * PipsToMarginCallPerUnitCurrency());
      //account.PipsToMC = summary == null ? 0 :
      //  (int)(account.UsableMargin / Math.Max(.1, (Math.Abs(summary.BuyLots - summary.SellLots) / 10000)));
      return account;
    }
    double AmountOfStopLoss(Trade[] trades) {
      var sum = 0.0;
      trades.Where(t=>t.Stop>0).ToList().ForEach(t => sum += PipsToMoney(InPips(t.Pair,(t.Open - t.Stop).Abs()), t.Lots, GetPipCost(t.Pair), MinimumQuantity));
      return sum;
    }
    public double CommisionPending { get { return GetTrades("").Sum(t => t.Lots) / 10000; } }

    private static bool IsBuy(object BS) { return BS + "" == "B"; }
    private static bool IsBuy(string BS) { return BS == "B"; }

    #region Close Positions
    public void TruncatePositions() {
      var buys = GetTrades(true).Count();
      var sells = GetTrades(false).Count();
      if (buys == 0 || sells == 0) return;
      ClosePositions(buys - sells, sells - buys);
    }
    public void ClosePositionsExceptLast() {
      var lastTrade = GetTrades().OrderBy(t => t.Id).LastOrDefault();
      if (lastTrade != null) ClosePositions(lastTrade.Buy ? 1 : 0, lastTrade.Buy ? 0 : 1);
    }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public void ClosePositions() { ClosePositions(0, 0); }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public void ClosePositions(int buyMin, int sellMin) {
      //DIMOK: Test ClosePositions(...)
      int errorCount = 3;
      var summury = GetTrades("");
      sellMin = Math.Max(0, sellMin);
      buyMin = Math.Max(0, buyMin);
      while (summury.Length > sellMin || summury.Length > buyMin) {
        try {
          var trades = GetTrades("", true);
          if (trades.Length > buyMin) FixOrderClose(trades.OrderBy(t=>t.Time).First().Id);
          trades = GetTrades("", false);
          if (trades.Length > buyMin) FixOrderClose(trades.OrderBy(t => t.Time).First().Id);
          summury = GetTrades("");
        } catch (Exception exc) {
          if (errorCount-- == 0) throw;
        }
      }
    }
    #endregion

    #region Trading Helpers
    public double GetMaxDistance(bool buy) {
      var trades = GetTrades().Where(t => t.Buy == buy).Select((t, i) => new { Pos = i, t.Open });
      if (trades.Count() < 2) return 0;
      var distance = (from t0 in trades
                      join t1 in trades on t0.Pos equals t1.Pos - 1
                      select Math.Abs(t0.Open - t1.Open)).Max(d => d);
      return distance;
    }
    public int CanTrade2(bool buy, double minDelta, int totalLots, bool unisex) {
      double price = GetOffer(buy);
      return CanTrade(GetTrades(), buy, minDelta, totalLots, unisex);
    }
    public int CanTrade(Trade[] otherTrades, bool buy, double minDelta, int totalLots, bool unisex) {
      var minLoss = (from t in otherTrades
                     where (unisex || t.Buy == buy)
                     select t.PL
                    ).DefaultIfEmpty(1000).Max();
      var hasProfitTrades = otherTrades.Where(t => t.Buy == buy && t.PL > 0).Count() > 0 ;
      var tradeInterval = (otherTrades.Select(t => t.Time).DefaultIfEmpty().Max() - ServerTime).Duration();
      return minLoss.Between(-minDelta, 1) || hasProfitTrades || tradeInterval.TotalSeconds < 10 ? 0 : totalLots;
    }
    public double GetDollarsPerHour() {
      var ret = from t in GetRows(TABLE_CLOSED)
                where t.CellValue(FIELD_INSTRUMENT) + "" == Pair
                orderby t.CellValue(FIELD_CLOSETIME)
                select t;
      if (ret.Count() < 2) return 0;
      double dollars = ret.Sum(r => (double)r.CellValue(FIELD_GROSSPL));
      DateTime timeFirst = (DateTime)ret.First().CellValue(FIELD_CLOSETIME);
      DateTime timeLast = (DateTime)ret.Last().CellValue(FIELD_CLOSETIME);
      return dollars / timeLast.Subtract(timeFirst).TotalSeconds * 60 * 60;
    }
    public double GetAverageProfit(bool buy) {
      var ret = from t in GetRows(TABLE_CLOSED)
                where t.CellValue(FIELD_INSTRUMENT) + "" == Pair &&
                IsBuy(t.CellValue("BS")) == buy &&
                (double)t.CellValue(FIELD_PL) > 0
                select t;
      if (ret.Count() < 1) return 0;
      return ret.Average(r => (double)r.CellValue(FIELD_PL));
    }
    public Price GetPrice() { return GetPrice(Pair); }
    public Price GetPrice(string pair) {
      return GetPrice(GetRows(TABLE_OFFERS, pair).FirstOrDefault());
    }
    private Price GetPrice(FXCore.RowAut Row) {
      return new Price() { Ask = (double)Row.CellValue(FIELD_ASK), Bid = (double)Row.CellValue(FIELD_BID), AskChangeDirection = (int)Row.CellValue(FIELD_ASKCHANGEDIRECTION), BidChangeDirection = (int)Row.CellValue(FIELD_BIDCHANGEDIRECTION), Time = ((DateTime)Row.CellValue(FIELD_TIME)).AddHours(coreFX.ServerTimeOffset), Pair = Pair };
    }

    #endregion

    #region Get Tables

    #region GetOffers
    public Offer[] GetOffers() {
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
                MMR = (Double)t.CellValue("MMR"),
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
    }
    #endregion

    #region GetSummary
    public Summary GetSummary() {
      var ret = GetSummary(Pair);
      ret.PriceCurrent = GetPrice();
      ret.PointSize = PointSize;
      return ret;
    }
    public Summary[] GetSummaries() {
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
          }).ToArray();
        return s;
      } catch (System.Runtime.InteropServices.COMException) {
        return GetSummaries();
      }
    }
    public Summary GetSummary(string Pair) {
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
      if (s == null) return new Summary();
      var rows = GetTrades(Pair, true).OrderByDescending(t => t.Open).ToArray();
      if (rows.Count() > 0) {
        var rowFirst = rows.First();
        s.BuyPriceFirst = rowFirst.Open;
        s.BuyLotsFirst = rowFirst.Lots;
        s.BuyTradeID_First = rowFirst.Id;

        var rowLast = rows.Last();
        s.BuyPriceLast = rowLast.Open;
        s.BuyLotsLast = rowLast.Lots;
        s.BuyTradeID_Last = rowLast.Id;

        s.BuyPositions = rows.Count();
      }
      rows = GetTrades(Pair, false).OrderBy(t => t.Open).ToArray();
      if (rows.Count() > 0) {
        var rowFirst = rows.First();
        s.SellPriceFirst = rowFirst.Open;
        s.SellLotsFirst = rowFirst.Lots;
        s.SellTradeID_First = rowFirst.Id;

        var rowLast = rows.Last();
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
    private FXCore.RowAut[] GetRows(string tableName, string Pair, bool? IsBuy) {
      return GetRows(tableName, IsBuy).Where(r => r.CellValue(FIELD_INSTRUMENT) + "" == Pair).ToArray();
    }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private FXCore.RowAut[] GetRows(string tableName) { return GetRows(tableName, (bool?)null); }
    object lockGetRows = new object();
    private FXCore.RowAut[] GetRows(string tableName, bool? isBuy) {
      //lock (lockGetRows) {
      var ret = (GetTable(tableName).Rows as FXCore.RowsEnumAut).Cast<FXCore.RowAut>().ToArray();
      if (isBuy != null)
        try {
          ret = ret.Where(t => (t.CellValue("BS") + "") == (isBuy.Value ? "B" : "S")).ToArray();
        } catch {
          return GetRows(tableName, isBuy);
        }
      return ret;
      //}
    }
    #endregion

    #region GetTrades
    public Trade GetTrade(string tradeId) { return GetTrades("").Where(t => t.Id == tradeId).SingleOrDefault(); }
    public Trade GetTrade(bool buy, bool last) { return last ? GetTradeLast(buy) : GetTradeFirst(buy); }
    public Trade GetTradeFirst(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).FirstOrDefault(); }
    public Trade GetTradeLast(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).LastOrDefault(); }
    public Trade[] GetTrades(bool buy) { return GetTrades().Where(t => t.Buy == buy).ToArray(); }
    public Trade[] GetTrades() {
      return GetTrades(Pair);
    }
    object getTradesLock = new object();
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public Trade[] GetTrades(string Pair, bool buy) {
      return GetTrades(Pair).Where(t => t.Buy == buy).ToArray();
    }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public Trade[] GetTrades(string Pair) {
      //      lock (getTradesLock) {
      var ret = from t in GetRows(TABLE_TRADES)
                orderby t.CellValue("BS") + "", t.CellValue("TradeID") + "" descending
                where new[] { "", (t.CellValue(FIELD_INSTRUMENT) + "").ToLower() }.Contains(Pair.ToLower())
                select InitTrade(t);
      return ret.ToArray();
      //    }
    }
    Trade InitTrade(FXCore.RowAut t) {
      return new Trade() {
        Id = t.CellValue("TradeID") + "",
        Pair = t.CellValue(FIELD_INSTRUMENT) + "",
        Buy = (t.CellValue("BS") + "") == "B",
        PL = (double)t.CellValue("PL"),
        GrossPL = (double)t.CellValue("GrossPL"),
        Limit = (double)t.CellValue("Limit"),
        Stop = (double)t.CellValue("Stop"),
        Lots = (Int32)t.CellValue("Lot"),
        Open = (double)t.CellValue("Open"),
        Close = (double)t.CellValue("Close"),
        Time = ConvertDateToLocal((DateTime)t.CellValue("Time")),// ((DateTime)t.CellValue("Time")).AddHours(coreFX.ServerTimeOffset),
        OpenOrderID = t.CellValue("OpenOrderID") + "",
        OpenOrderReqID = t.CellValue("OpenOrderReqID") + "",
        Remark = new TradeRemark(t.CellValue("QTXT") + "")
      };
    }
    Trade InitTrade(FXCore.ParserAut t) {
      return new Trade() {
        Id = t.GetValue("TradeID") + "",
        Pair = t.GetValue(FIELD_INSTRUMENT) + "",
        Buy = (t.GetValue("BS") + "") == "B",
        PL = (double)t.GetValue("PL"),
        GrossPL = (double)t.GetValue("GrossPL"),
        Limit = (double)t.GetValue("Limit"),
        Stop = (double)t.GetValue("Stop"),
        Lots = (Int32)t.GetValue("Lot"),
        Open = (double)t.GetValue("Open"),
        Close = (double)t.GetValue("Close"),
        Time = ConvertDateToLocal((DateTime)t.GetValue("Time")),// ((DateTime)t.GetValue("Time")).AddHours(coreFX.ServerTimeOffset),
        OpenOrderID = t.GetValue("OpenOrderID") + "",
        OpenOrderReqID = t.GetValue("OpenOrderReqID") + "",
        Remark = new TradeRemark(t.GetValue("QTXT") + "")
      };
    }
    #endregion

    #region GetOffers
    public double GetOffer(bool buy) {
      var t = GetTable(TABLE_OFFERS).FindRow(FIELD_INSTRUMENT,Pair,0) as FXCore.RowAut;
      return (double)(buy ? t.CellValue("Ask") : t.CellValue("Bid"));
    }
    public Order[] GetOrders(string Pair) {
      var ttt = (from t in GetRows(TABLE_ORDERS)
                where new[] { (t.CellValue(FIELD_INSTRUMENT) + "").ToLower(), "" }.Contains(Pair.ToLower())
                select t).ToArray();
      var orderList = new List<Order>();
      try {
        foreach (var t in ttt) 
          orderList.Add(InitOrder(t));
        return orderList.ToArray();
      } catch (System.Runtime.InteropServices.COMException exc) {
        if (exc.Message.ToLower().Contains("row was deleted"))
          return GetOrders(Pair);
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
      return order;
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
      return order;
    }
    #endregion

    [CLSCompliant(false)]
    public FXCore.TableAut GetTable(string TableName) {
      return coreFX.Desk.FindMainTable(TableName) as FXCore.TableAut;
    }

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
      lock (globalOrderPending) {
        object requestID;
        try {
          var isStop = fixOrderKnd == Desk.FIX_STOP;
          var isLimit = fixOrderKnd == Desk.FIX_LIMIT;
          var stop = isStop ? rate:0;
          var limit = isLimit ? rate : 0;
          var po = new PendingOrder(tradeId, stop, limit, remark);
          if (!PendingOrders.Contains(po)) {
            var rateFlag = isStop ? Desk.SL_STOP : isLimit ? Desk.SL_LIMIT : Desk.SL_NONE;
            coreFX.Desk.CreateFixOrderAsync(fixOrderKnd,tradeId, rate, 0, "", "", "", true, 0, remark, rateFlag, out requestID);
            po.RequestId = requestID + "";
            PendingOrders.Add(po);
          }
          return po;
        } catch (Exception exc) {
          RaiseError(new Exception(string.Format("TradeId:{0},Stop:{1},Remark:{2}", tradeId, rate, remark), exc));
          return null;
        }
      }
    }

    #endregion

    #region Entry
    int GetFixOrderLind(bool buy, Price currentPrice, double orderRate) {
      if (buy)
        return currentPrice.Ask > orderRate ? Desk.FIX_ENTRYLIMIT : Desk.FIX_ENTRYSTOP;
      return currentPrice.Bid < orderRate ? Desk.FIX_ENTRYLIMIT : Desk.FIX_ENTRYSTOP;
    }

    public PendingOrder FixOrderOpenEntry(string pair, bool buy, int lots, double rate, double stop, double limit, string remark) {
      lock (globalOrderPending) {
        object requestID;
        try {
          var po = new PendingOrder(pair, buy, lots, rate, stop, limit, remark);
          if (!PendingOrders.Contains(po)) {
            var fixOrderKind = GetFixOrderLind(buy, GetPrice(pair), rate);
            coreFX.Desk.CreateFixOrderAsync(fixOrderKind, "", rate, 0, "", AccountID, pair, buy, lots, remark, 0, out requestID);
            po.RequestId = requestID + "";
            PendingOrders.Add(po);
          }
          return po;
        } catch (Exception exc) {
          throw new Exception(string.Format("Pair:{0},Buy:{1},Lots:{2}", pair, buy, lots), exc);
        }
      }
    }
    #endregion

    #region Open
    ObservableCollection<PendingOrder> _pendingOrders = new ObservableCollection<PendingOrder>();
    public ObservableCollection<PendingOrder> PendingOrders {
      get { return _pendingOrders; }
      set { _pendingOrders = value; }
    }
    public void FixOrderOpen(bool buy, int lots, double takeProfit, double stopLoss, string remark) {
      FixOrderOpen(Pair, buy, lots, takeProfit, stopLoss, remark);
    }
    static private object globalOrderPending = new object();
    public PendingOrder FixOrderOpen(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark) {
      lock (globalOrderPending) {
        object requestID;
        try {
          var po = new PendingOrder(pair, buy, lots, 0, stopLoss, takeProfit, remark);
          if (PendingOrders.Contains(po)) return po;
          coreFX.Desk.CreateFixOrderAsync(coreFX.Desk.FIX_OPENMARKET, "", 0, 0, "", AccountID, pair, buy, lots, remark, 0, out requestID);
          //var iSLType = Desk.SL_NONE;
          //if (stopLoss > 0) iSLType += Desk.SL_STOP;
          //if (takeProfit > 0) iSLType += Desk.SL_LIMIT;
          //Desk.OpenTrade2(AccountID, pair, buy, lots, 0, "", 0, iSLType, stopLoss, takeProfit, 0, out psOrderID, out psDI);
          po.RequestId = requestID + "";
          PendingOrders.Add(po);
          return po;
        } catch (Exception exc) {
          throw new Exception(string.Format("Pair:{0},Buy:{1},Lots:{2}", pair, buy, lots), exc);
        }
      }
    }
    #endregion

    #region Close
    public PendingOrder FixOrderClose(string tradeId, string remark) {
      var trade = GetTrades("").SingleOrDefault(t => t.Id == tradeId);
      if (trade == null) {
        RaiseError(new Exception("TradeId:" + tradeId + " not found."));
        return null;
      }
      return FixOrderClose(tradeId, trade.Lots, remark);
    }
    public PendingOrder FixOrderClose(string tradeId, int lots, string remark) {
      lock (globalClosePending) {
        object requestID;
        try {
          var po = new PendingOrder(tradeId, 0, 0, remark);
          if (PendingOrders.Contains(po)) return po;
          coreFX.Desk.CreateFixOrderAsync(coreFX.Desk.FIX_CLOSEMARKET, tradeId, 0, 0, "", "", "", true, lots, remark, 0, out requestID);
          po.RequestId = requestID + "";
          PendingOrders.Add(po);
          return po;
        } catch (Exception exc) {
          RaiseError(new Exception(string.Format("TradeId:{0},Lots:{1}", tradeId, lots), exc));
          return null;
        }
      }
    }
    public void FixOrderClose(bool buy, bool last) {
      var trade = GetTrade(buy, last);
      if (trade != null)
        FixOrderClose(trade.Id, Desk.FIX_CLOSEMARKET, GetPrice());
    }
    public object[] FixOrdersClose(params string[] tradeIds) {
      var ordersList = new List<object>();
      foreach (var tradeId in tradeIds)
        ordersList.Add(FixOrderClose(tradeId));
      return ordersList.ToArray();
    }
    public object[] FixOrdersCloseAll() {
      var ordersList = new List<object>();
      var trade = GetTrades("").FirstOrDefault();
      while (trade != null) {
        ordersList.Add(FixOrderClose(trade.Id));
        trade = GetTrades("").FirstOrDefault();
      }
      return ordersList.ToArray();
    }
    public object FixOrderClose(string tradeId) { return FixOrderClose(tradeId, coreFX.Desk.FIX_CLOSEMARKET, null as Price); }
    static private object globalClosePending = new object();
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public object FixOrderClose(string tradeId, int mode, Price price) {
      lock (globalClosePending) {
        object psOrderID = null, psDI;
        try {
          var row = GetTable(TABLE_TRADES).FindRow(FIELD_TRADEID, tradeId, 0) as FXCore.RowAut;
          var dRate = mode == coreFX.Desk.FIX_CLOSEMARKET || price == null ? 0 : IsBuy(row.CellValue("BS")) ? price.Bid : price.Ask;
          if (GetAccount(false).Hedging) {
            //RunWithTimeout.WaitFor<object>.Run(TimeSpan.FromSeconds(5), () => {
            coreFX.Desk.CreateFixOrder(mode, tradeId, dRate, 0, "", "", "", true, (int)row.CellValue("Lot"), "", out psOrderID, out psDI);
            try {
              while (GetTrade(tradeId) != null)
                Thread.Sleep(10);
            } catch (System.Runtime.InteropServices.COMException comExc) {
              RaiseOrderError(new OrderExecutionException("Closing TradeID:" + tradeId, comExc));
            }
            //  return null;
            //});
          } else {
            throw new NotSupportedException("Non hedging account.");
            var trade = GetTrades("").SingleOrDefault(t => t.Id == tradeId);
            if (trade != null) {
              FixOrderOpen(trade.Pair, !trade.Buy, (int)trade.Lots, 0, 0, trade.Id);
            }
          }
          return psOrderID;
        } catch (Exception exc) {
          //RaiseOrderError(new OrderExecutionException("Closing TradeID:" + tradeId + " failed.", exc));
          throw exc;
        }
      }
    }
    #endregion

    #region Stop/Loss
    public void FixOrderSetNetLimits(double takeProfit, bool buy) {
      object a, b;
      var dg = Digits; var ps = PointSize;
      takeProfit = Math.Round(takeProfit, dg);
      var ret = from t in GetRows(TABLE_TRADES)
                where (t.CellValue(FIELD_INSTRUMENT) + "") == Pair &&
                  IsBuy(t.CellValue("BS")) == buy &&
                  Math.Abs(Math.Round((double)t.CellValue(FIELD_LIMIT), dg) - takeProfit) > ps / 10
                select new {
                  TradeID = t.CellValue("TradeID") + ""
                };
      foreach (var t in ret) {
        try {
          Desk.CreateFixOrder(Desk.FIX_LIMIT, t.TradeID, takeProfit, 0, "", "", "", true, 0, "", out a, out b);
        } catch (Exception) {
          FixOrderSetNetLimits(takeProfit, buy);
        }
      }
    }


    public void FixOrderSetLimit(string tradeId, double takeProfit, string remark) {
      if (takeProfit == 0)
        Desk.DeleteTradeStopLimit(tradeId, false);
      else {
        object a, b;
        Desk.CreateFixOrder(Desk.FIX_LIMIT, tradeId, takeProfit, 0, "", "", "", true, 0, remark, out a, out b);
      }
    }
    public void FixOrderSetStop(string tradeId, double stopLoss, string remark) {
      if (stopLoss == 0)
        Desk.DeleteTradeStopLimit(tradeId, true);
      else {
        object a, b;
        Desk.CreateFixOrder(Desk.FIX_STOP, tradeId, stopLoss, 0, "", "", "", true, 0, remark, out a, out b);
      }
    }


    public void FixOrderDeleteLimits(int pipsLost, double pipsToProfit) {
        var ret = from t in GetRows(TABLE_TRADES)
                  where (t.CellValue(FIELD_INSTRUMENT) + "") == Pair &&
                        (double)t.CellValue(FIELD_PL) < pipsLost
                  select t.CellValue("TradeID") + "";
        object a, b;
        foreach (var tID in ret)
          try {
              Desk.CreateFixOrder(Desk.FIX_LIMIT, tID, 0, 0, "", "", "", true, 0, "", out a, out b);
          } catch(Exception exception) {
            RaiseError(exception);
          }
        var ret1 = from t in GetRows(TABLE_TRADES)
                   where (t.CellValue(FIELD_INSTRUMENT) + "") == Pair &&
                     (double)t.CellValue(FIELD_LIMIT) > 0 &&
                     Math.Round(Math.Abs((double)t.CellValue(FIELD_OPEN) - (double)t.CellValue(FIELD_LIMIT)), Digits) < Math.Round(pipsToProfit * PointSize, Digits)
                   select new {
                     TradeID = t.CellValue("TradeID") + "",
                     Open = (double)t.CellValue(FIELD_OPEN),
                     Limit = (double)t.CellValue(FIELD_OPEN) + pipsToProfit * PointSize * (IsBuy(t.CellValue("BS")) ? 1 : -1)
                   };
        foreach (var t in ret1)
          try {
              Desk.CreateFixOrder(Desk.FIX_LIMIT, t.TradeID, t.Limit, 0, "", "", "", true, 0, "", out a, out b);
          } catch { }
    }
    #endregion

    #endregion

    #region Pips/Points Converters
    public double InPips(int level, double? price, int roundTo) { return Math.Round(InPips(level, price), roundTo); }
    public double InPips(int level,double? price) {
      if (level == 0) return price.GetValueOrDefault();
      return InPips(--level, price / PointSize); 
    }
    public double InPips(double? price, int roundTo) { return Math.Round(InPips(price), roundTo); }
    public double InPips( double? price) { return (price / PointSize).GetValueOrDefault(); }
    public double InPips(string pair, double? price) { return (price / GetPipSize(pair)).GetValueOrDefault(); }

    public double InPoints(double? price) { return (price * PointSize).GetValueOrDefault(); }
    public double InPoints(string pair, double? price) { return (price * GetPipSize(pair)).GetValueOrDefault(); }

    Dictionary<string, double> pointSizeDictionary = new Dictionary<string, double>();
    public double GetPipSize(string pair) {
      pair = pair.ToUpper();
      if (!pointSizeDictionary.ContainsKey(pair))
        GetOffers().ToList().ForEach(o => pointSizeDictionary[o.Pair] = o.PointSize);
      return pointSizeDictionary[pair];
    }

    Dictionary<string, double> pipCostDictionary = new Dictionary<string, double>();
    public double GetPipCost(string pair) {
      pair = pair.ToUpper();
      if (!pipCostDictionary.ContainsKey(pair))
        GetOffers().ToList().ForEach(o => pipCostDictionary[o.Pair] = o.PipCost);
      return pipCostDictionary[pair];
    }
    public static double PipsToMoney(double pips, double lots, double pipCost, double baseUnitSize) {
      return pips * lots * pipCost / baseUnitSize;
    }
    private double PipsToMarginCallPerUnitCurrency() {
      var sm = (from s in GetSummaries()
                join o in pipCostDictionary
                on s.Pair equals o.Key
                let pc = o.Value
                select new {
                  AmountK = s.AmountK,
                  PipCost = pc >= 10 ? pc * .01 : pc
                }).ToArray();
      if (sm.Count() == 0 || sm.Sum(s => s.AmountK) == 0) return 1 / .1;
      var pipCostWieghtedAverage = sm.Sum(s => s.AmountK * s.PipCost) / sm.Sum(s => s.AmountK);
      return -1 / (sm.Sum(s => s.AmountK) * pipCostWieghtedAverage);
    }

    #endregion

    #region (Un)Subscribe

    FXCore.TradeDeskEventsSinkClass mSink;
    int mSubscriptionId = -1;
    bool isSubsribed = false;
    public void Subscribe() {
      if (isSubsribed) return;
      Unsubscribe();
      mSink = new FXCore.TradeDeskEventsSinkClass();
      mSink.ITradeDeskEvents_Event_OnRowChangedEx += FxCore_RowChanged;
      mSink.ITradeDeskEvents_Event_OnRowAddedEx += mSink_ITradeDeskEvents_Event_OnRowAdded;
      mSink.ITradeDeskEvents_Event_OnRowBeforeRemoveEx += mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx;
      mSink.ITradeDeskEvents_Event_OnRequestCompleted += mSink_ITradeDeskEvents_Event_OnRequestCompleted;
      mSink.ITradeDeskEvents_Event_OnRequestFailed += mSink_ITradeDeskEvents_Event_OnRequestFailed;
      mSubscriptionId = Desk.Subscribe(mSink);
      isSubsribed = true;
    }

    public void Unsubscribe() {
      if (mSubscriptionId != -1) {
        try {
          mSink.ITradeDeskEvents_Event_OnRowChangedEx -= FxCore_RowChanged;
          mSink.ITradeDeskEvents_Event_OnRowAddedEx -= mSink_ITradeDeskEvents_Event_OnRowAdded;
          mSink.ITradeDeskEvents_Event_OnRowBeforeRemoveEx -= mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx;
          mSink.ITradeDeskEvents_Event_OnRequestCompleted -= mSink_ITradeDeskEvents_Event_OnRequestCompleted;
          mSink.ITradeDeskEvents_Event_OnRequestFailed -= mSink_ITradeDeskEvents_Event_OnRequestFailed;
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
        PendingOrders.Remove(po);
    }

    void mSink_ITradeDeskEvents_Event_OnRowAdded(object _table, string RowID,string rowText) {
      try {
        System.Threading.Thread.CurrentThread.Priority = ThreadPriority.Highest;
        FXCore.TableAut table = _table as FXCore.TableAut;
        FXCore.RowAut row;
        FXCore.ParserAut parser = Desk.GetParser() as FXCore.ParserAut;
        Func<FXCore.TableAut, FXCore.RowAut, string> showTable = (t, r) => {
          var columns = (t.Columns as FXCore.ColumnsEnumAut).Cast<FXCore.ColumnAut>()
            .Where(c => (c.Title + "").Length != 0).Select(c=>new {c.Title, Value=r.CellValue(c.Title)+""});
          return new XElement(t.Type.Replace(" ","_"), columns.Select(c => new XAttribute(c.Title,c.Value)))
            .ToString(SaveOptions.DisableFormatting);
        };
        switch (table.Type.ToLower()) {
          case "trades": {
              lock (globalOrderPending) {
                parser.ParseEventRow(rowText, table.Type);
                var tradeParsed = InitTrade(parser);
                var poTrade = PendingOrders.SingleOrDefault(po => po.RequestId == tradeParsed.OpenOrderReqID);
                if (poTrade != null && !poTrade.HasKids(PendingOrders)) RemovePendingOrder(poTrade);
                if (new[] { "", tradeParsed.Pair }.Contains(Pair))
                  RaiseTradeAdded(tradeParsed);
                var trade = GetTrades("").SingleOrDefault(t => t.Id == tradeParsed.Id);
                if (trade == null && poTrade!=null) {
                  FixOrderOpen(poTrade.Pair, poTrade.Buy, poTrade.Lot, poTrade.Limit, poTrade.Stop, poTrade.Remark);
                }
              }
            }
            break;
          case "orders":
            parser.ParseEventRow(rowText, table.Type);
            var order = InitOrder(parser);
            var poOrder = PendingOrders.SingleOrDefault(o => o.RequestId == order.RequestID);
            if (poOrder != null) {
              poOrder.OrderId = order.OrderID;
              poOrder.TradeId = order.TradeID;
              var complete = true;
              if (order.Stop != poOrder.Stop) {
                complete = false;
                FixCreateStop(poOrder.TradeId, poOrder.Stop, poOrder.Remark).Parent = poOrder;
              }
              if (order.Limit != poOrder.Limit) {
                complete = false;
                FixCreateLimit(poOrder.TradeId, poOrder.Limit, poOrder.Remark).Parent = poOrder;
              }
              if( complete) PendingOrders.Remove(poOrder);
            }
            break;
          case "closed trades": {
              row = table.FindRow(FIELD_TRADEID, RowID, 0) as FXCore.RowAut;
              System.IO.File.AppendAllText("ClosedTrades.txt", showTable(table, row) + Environment.NewLine);

              var poTrade = PendingOrders.SingleOrDefault(po => po.TradeId == RowID);
              if (poTrade != null && !poTrade.HasKids(PendingOrders)) PendingOrders.Remove(poTrade);
            }
            break;
        }
      } catch (Exception exc) { RaiseError(exc); }
    }

    void FxCore_RowChanged(object _table, string rowID, string rowText) {
      try {
        FXCore.TableAut table = _table as FXCore.TableAut;
        FXCore.RowAut row;
        FXCore.ParserAut parser = Desk.GetParser() as FXCore.ParserAut;
        RaiseRowChanged(table.Type, rowID);
        switch (table.Type.ToLower()) {
          case "offers":
            row = table.FindRow("OfferID", rowID, 0) as FXCore.RowAut;
            if ((DateTime)row.CellValue(FIELD_TIME) != _prevOfferDate
              && new[] { "", row.CellValue(FIELD_INSTRUMENT) + "" }.Contains(Pair.ToUpper())
              && PriceChanged != null) {
              _prevOfferDate = (DateTime)row.CellValue(FIELD_TIME);
              PriceChanged(GetPrice(row));
            }
            break;
          case TABLE_ORDERS:
            parser.ParseEventRow(rowText, table.Type);
            var order = InitOrder(parser);
            var poOrder = PendingOrders.SingleOrDefault(po => po.RequestId == order.RequestID);
            if (poOrder != null && !poOrder.HasKids(PendingOrders)) PendingOrders.Remove(poOrder);
            break;
          case TABLE_TRADES:
            //parser.ParseEventRow(rowText, table.Type);
            //var trade = InitTrade(parser);
            //var poTrade = PendingOrders.SingleOrDefault(po => po.RequestId == trade.OpenOrderReqID);
            //if (poTrade != null && !poTrade.HasKids(PendingOrders)) RemovePendingOrder(poTrade);
            break;
        }
      } catch (Exception exc) {
        RaiseError(exc);
      }
    }

    void mSink_ITradeDeskEvents_Event_OnRequestCompleted(string sRequestID) {
      try {
        //Debug.WriteLine("Request +" + sRequestID + " completed.");
        //var po = PendingOrders.SingleOrDefault(o => o.RequestId == sRequestID);
        //if (po != null && !po.HasKids(PendingOrders)) 
        //  RemovePendingOrder(po);
      } catch (Exception exc) {
        RaiseError(exc);
      }
    }

    void mSink_ITradeDeskEvents_Event_OnRequestFailed(string sRequestID, string sError) {
      Debug.WriteLine("Request +" + sRequestID + " failed.");
    }



    void mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx(object _table, string RowID, string sExtInfo) {
      try {
        FXCore.TableAut table = _table as FXCore.TableAut;
        Dictionary<string, string> fields = parse(sExtInfo);
        switch (table.Type.ToLower()) {
          case "trades":
            break;
        }
      } catch (Exception exc) {
        RaiseError(exc);
      }
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
      LogOff();
      coreFX = null;
      GC.SuppressFinalize(this);
    }

    #endregion

    #region FXCore Helpers
    dynamic _tradingSettingsProvider;
    dynamic TradingSettingsProvider{
      get{
        if( _tradingSettingsProvider == null)_tradingSettingsProvider = Desk.TradingSettingsProvider;
        return _tradingSettingsProvider;
      }
    }
    public int MinimumQuantity {
      get {
        if (_minimumQuantity == 0){
          _minimumQuantity = TradingSettingsProvider.GetMinQuantity("EUR/USD", AccountID);
        }
        return _minimumQuantity;
      }
    }

    public class TradingSettings {
      string pair;
      string accountId;
      dynamic TradingSettingsProvider;
      public TradingSettings(FXCore.TradingSettingsProviderAut tsp,string accountId,string pair) {
        this.TradingSettingsProvider = tsp;
        this.accountId = accountId;
        this.pair = pair;
      }
      public int GetBaseUnitSize { get { return TradingSettingsProvider.GetBaseUnitSize(pair,accountId); } }
      public int GetCondDistEntryLimit { get { return TradingSettingsProvider.GetCondDistEntryLimit(pair); } }
      public int GetCondDistEntryStop { get { return TradingSettingsProvider.GetCondDistEntryStop(pair); } }
      public int GetCondDistLimitForEntryOrder { get { return TradingSettingsProvider.GetCondDistLimitForEntryOrder(pair); } }
      public int GetCondDistLimitForTrade { get { return TradingSettingsProvider.GetCondDistLimitForTrade(pair); } }
      public int GetCondDistStopForEntryOrder { get { return TradingSettingsProvider.GetCondDistStopForEntryOrder(pair); } }
      public int GetCondDistStopForTrade { get { return TradingSettingsProvider.GetCondDistStopForTrade(pair); } }
      public int GetMarketStatus { get { return TradingSettingsProvider.GetMarketStatus(pair); } }
      public int GetMaxQuantity { get { return TradingSettingsProvider.GetMaxQuantity(pair,accountId); } }
      public int GetMinQuantity { get { return TradingSettingsProvider.GetMinQuantity(pair,accountId); } }
    }
    TradingSettings _tradingSettingsHelper;
    Dictionary<string, TradingSettings> tradingSettingsCatalog = new Dictionary<string, TradingSettings>();


    public TradingSettings GetTradingSettings(string pair) {
      if (!tradingSettingsCatalog.ContainsKey(pair))
        tradingSettingsCatalog.Add(pair, new TradingSettings(TradingSettingsProvider as FXCore.TradingSettingsProviderAut,AccountID, pair));
      return tradingSettingsCatalog[pair];
    }

    public DateTime ServerTimeCached { get; set; }
    public DateTime ServerTime { get { return ServerTimeCached = coreFX.ServerTime; } }
    FXCore.ITimeZoneConverterAut timeZoneConverter { get { return coreFX.Desk.TimeZoneConverter as FXCore.ITimeZoneConverterAut; } }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    DateTime ConvertDateToLocal(DateTime date) {
      var converter = timeZoneConverter;
      return converter.Convert(date, converter.ZONE_UTC, converter.ZONE_LOCAL);
    }
    static string GetOrderStatusDescr(string orderStatus) {
      if (orderStatus.Length != 1)
        return "Unknown";
      switch (orderStatus[0]) {
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
      for (int i = 0; i < tokensCount; i += 2)
        fields.Add(tokens[i], tokens[i + 1]);
      return fields;
    }
    #endregion
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
    public static Summary Initialize(Price Price){
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
    public HedgeHog.Bars.Price PriceCurrent { get; set; }
    public double NetPL { get; set; }
  }
  #endregion
}
