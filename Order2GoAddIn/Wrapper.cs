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

[assembly: CLSCompliant(true)]
namespace Order2GoAddIn {
  [Guid("D5FB5C05-8EF5-4bcc-BA92-10706FD640BB")]
  [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
  [ComVisible(true)]
  public interface IOrder2GoEvents {
    [DispId(1)]
    void RowChanged(string tableType, string rowId);
    [DispId(2)]
    void RowAdded(object tableType, string rowId);
  }

  [CLSCompliant(false)]
  public delegate void OnTradesCountChangedDelegate(Trade trade);

  public class UserNotLoggedInException : Exception {
    public UserNotLoggedInException(string message) : base(message) { }
  }

  public static class RateExtensions {
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
  public delegate void ErrorEventHandler(Exception exception);
  public delegate void OrderErrorEventHandler(Exception exception);
  [ClassInterface(ClassInterfaceType.AutoDual), ComSourceInterfaces(typeof(IOrder2GoEvents))]
  [Guid("2EC43CB6-ED3D-465c-9AF3-C0BBC622663E")]
  [ComVisible(true)]
  public sealed class FXCoreWrapper:IDisposable {
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

    public event OrderErrorEventHandler OrderError;
    void RaiseOrderError(Exception exception) {
      if (OrderError != null) OrderError(exception);
    }
    public event ErrorEventHandler Error;
    void RaiseError(Exception exception) {
      if (Error != null) Error(exception);
    }
    public class PairNotFoundException : NotSupportedException {
      public PairNotFoundException(string Pair, string table) : base("Pair " + Pair + " not found in " + table + " table.") { }
      public PairNotFoundException(string Pair) : base("Pair " + Pair + " not found.") { }
    }
    public class OrderExecutionException : Exception {
      public OrderExecutionException(string Message, Exception inner) : base(Message, inner) { }
    }

    #region Events
    public delegate void RowChangedEventHandler(string TableType, string RowID);
    public event RowChangedEventHandler RowChanged;
    void RaiseRowChanged(string tableType, string rowID) {
      if (RowChanged != null) RowChanged(tableType, rowID);
    }

    public delegate void OrderAddedEventHandler(FXCore.RowAut fxRow);
    public event OrderAddedEventHandler OrderAdded;
    void RaiseOrderAdded( FXCore.RowAut fxRow) {
      if (OrderAdded != null) OrderAdded(fxRow);
    }
    
    public delegate void RowRemovingdEventHandler(string TableType, string RowID);
    public event RowRemovingdEventHandler RowRemoving;
    void RaiseRowRemoving(string tableType, string rowID) {
      if (RowRemoving != null) RowRemoving(tableType, rowID);
    }

    public delegate void PriceChangedEventHandler(Price Price);
    public event PriceChangedEventHandler PriceChanged;

    public delegate void TradesCountChangedEventHandler(Trade trade);
    public event TradesCountChangedEventHandler TradesCountChanged;

    public static OnTradesCountChangedDelegate OnTradeCountChangedCallback;

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

    public bool IsLoggedIn { get { 
      try {
        return  Desk != null && Desk.IsLoggedIn();
      } catch { }
      return false;
    } } 
    #endregion

    #region Constructor
    CoreFX coreFX;
    public FXCoreWrapper():this(new CoreFX()) {    }
    public FXCoreWrapper(CoreFX coreFX):this(coreFX,null) { }
    public FXCoreWrapper(CoreFX coreFX,string pair) {
      this.coreFX = coreFX;
      if (pair != null) this.Pair = pair;
      coreFX.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
      coreFX.LoggedOffEvent += new EventHandler<EventArgs>(coreFX_LoggedOffEvent);
      isWiredUp = true;
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
      coreFX.Logout();
    }
    #endregion

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

    public DateTime ServerTimeCached { get; set; }
    public DateTime ServerTime { get { return ServerTimeCached = coreFX.ServerTime; } } 
    #region FX Tables
    [MethodImpl(MethodImplOptions.Synchronized)]
    public Account GetAccount() {
      var row = GetRows(TABLE_ACCOUNTS).First();
      var account = new Account() {
        ID = row.CellValue(FIELD_ACCOUNTID) + "",
        Balance = (double)row.CellValue(FIELD_BALANCE),
        UsableMargin = (double)row.CellValue(FIELD_USABLEMARGIN),
        IsMarginCall = row.CellValue(FIELD_MARGINCALL)+"" == "W",
        Equity = (double)row.CellValue(FIELD_EQUITY),
        Hedging = row.CellValue("Hedging").ToString() == "Y",
        Trades = GetTrades("")
      };
      account.PipsToMC = (int)(account.UsableMargin * PipsToMarginCallPerUnitCurrency());
      //account.PipsToMC = summary == null ? 0 :
      //  (int)(account.UsableMargin / Math.Max(.1, (Math.Abs(summary.BuyLots - summary.SellLots) / 10000)));
      return account;
    }
    public double CommisionPending { get { return GetTrades("").Sum(t => t.Lots) / 10000; } }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private double PipsToMarginCallPerUnitCurrency() {
      var sm = (from s in GetSummaries()
                join o in GetOffers()
                on s.Pair equals o.Pair
                let pc = o.PipCost
                select new {
                  AmountK = s.AmountK,
                  PipCost = pc >= 10 ? pc * .01 : pc
                }).ToArray();
      if (sm.Count() == 0 || sm.Sum(s=>s.AmountK) == 0) return 1 / .1;
      var pipCostWieghtedAverage = sm.Sum(s => s.AmountK * s.PipCost) / sm.Sum(s => s.AmountK);
      return -1 / (sm.Sum(s => s.AmountK) * pipCostWieghtedAverage);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
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
    public Summary GetSummary() {
      var ret = GetSummary(Pair);
      ret.PriceCurrent = GetPrice();
      ret.PointSize = PointSize;
      return ret;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
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
    [MethodImpl(MethodImplOptions.Synchronized)]
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
      var rows = GetTrades(Pair,  true).OrderByDescending(t => t.Open).ToArray();
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
      rows = GetTrades(Pair,  false).OrderBy(t => t.Open).ToArray();
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

    private FXCore.RowAut[] GetRows(string tableName, string pair) {
      return GetRows(tableName).Where(r => r.CellValue(FIELD_INSTRUMENT) + "" == pair).ToArray();
    }
    private FXCore.RowAut[] GetRows(string tableName, string Pair, bool? IsBuy) {
        return GetRows(tableName,IsBuy).Where(r => r.CellValue(FIELD_INSTRUMENT) + "" == Pair).ToArray();
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private FXCore.RowAut[] GetRows(string tableName) { return GetRows(tableName, (bool?)null); }
    object lockGetRows = new object();
    private FXCore.RowAut[] GetRows(string tableName, bool? isBuy) {
      lock (lockGetRows) {
        var ret = (GetTable(tableName).Rows as FXCore.RowsEnumAut).Cast<FXCore.RowAut>().ToArray();
        if (isBuy != null)
          try {
            ret = ret.Where(t => (t.CellValue("BS") + "") == (isBuy.Value ? "B" : "S")).ToArray();
          } catch {
            return GetRows(tableName, isBuy);
          }
        return ret;
      }
    }
    [CLSCompliant(false)]
    [MethodImpl(MethodImplOptions.Synchronized)]
    public FXCore.TableAut GetTable(string TableName) {
        return coreFX.Desk.FindMainTable(TableName) as FXCore.TableAut;
    }

    private static bool IsBuy(object BS) { return BS + "" == "B"; }
    private static bool IsBuy(string BS) { return BS == "B"; }

    public IEnumerable<Rate> GetMinuteBars( IEnumerable<Rate> fxRates, int period) {
      return (from t in fxRates
              where period > 0
              group t by t.StartDate.Round(period) into tg
              where tg != null
              orderby tg.Key
              select new Rate() {
                AskHigh = tg.Max(t => t.AskHigh),
                AskLow = tg.Min(t => t.AskLow),
                BidHigh = tg.Max(t => t.BidHigh),
                BidLow = tg.Min(t => t.BidLow),
                StartDate = tg.Key
              }
                ).ToArray();
    }

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
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ClosePositions() { ClosePositions(0, 0); }
    [MethodImpl(MethodImplOptions.Synchronized)]
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

    public double GetMaxDistance(bool buy) {
      var trades = GetTrades().Where(t => t.Buy == buy).Select((t, i) => new { Pos = i, t.Open });
      if (trades.Count() < 2) return 0;
      var distance = (from t0 in trades
                      join t1 in trades on t0.Pos equals t1.Pos-1
                      select Math.Abs(t0.Open-t1.Open)).Max(d=>d);
      return distance;
    }
    FXCore.ITimeZoneConverterAut timeZoneConverter { get { return coreFX.Desk.TimeZoneConverter as FXCore.ITimeZoneConverterAut; } }
    [MethodImpl(MethodImplOptions.Synchronized)]
    DateTime ConvertDateToLocal(DateTime date) {
      var converter = timeZoneConverter;
      return converter.Convert(date, converter.ZONE_UTC, converter.ZONE_LOCAL);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public Trade GetTrade(string tradeId) { return GetTrades("").Where(t => t.Id == tradeId).SingleOrDefault(); }
    public Trade GetTrade(bool buy, bool last) { return last ? GetTradeLast(buy) : GetTradeFirst(buy); }
    public Trade GetTradeFirst(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).FirstOrDefault(); }
    public Trade GetTradeLast(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).LastOrDefault(); }
    public Trade[] GetTrades(bool buy) { return GetTrades().Where(t => t.Buy == buy).ToArray(); }
    public Trade[] GetTrades() {
      return GetTrades(Pair);
    }
    static object getTradesLock = new object();
    [MethodImpl(MethodImplOptions.Synchronized)]
    public Trade[] GetTrades(string Pair, bool buy) {
      return GetTrades(Pair).Where(t => t.Buy == buy).ToArray();
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public Trade[] GetTrades(string Pair) {
      lock (getTradesLock) {
        var ret = from t in GetRows(TABLE_TRADES)
                  orderby t.CellValue("BS") + "", t.CellValue("TradeID") + "" descending
                  where new[] { "", (t.CellValue(FIELD_INSTRUMENT) + "").ToLower() }.Contains(Pair.ToLower())
                  select new Trade() {
                    Id = t.CellValue("TradeID") + "",
                    Pair = t.CellValue(FIELD_INSTRUMENT) + "",
                    Buy = (t.CellValue("BS") + "") == "B",
                    PL = (double)t.CellValue("PL"),
                    GrossPL = (double)t.CellValue("GrossPL"),
                    Limit = (double)t.CellValue("Limit"),
                    Lots = (Int32)t.CellValue("Lot"),
                    Open = (double)t.CellValue("Open"),
                    Close = (double)t.CellValue("Close"),
                    Time = ConvertDateToLocal((DateTime)t.CellValue("Time")),// ((DateTime)t.CellValue("Time")).AddHours(coreFX.ServerTimeOffset),
                    OpenOrderID = t.CellValue("OpenOrderID")+"",
                    OpenOrderReqID = t.CellValue("OpenOrderReqID")+"",
                    Remark = new TradeRemark(t.CellValue("QTXT") + "")
                  };
        return ret.ToArray();
      }
    }
    //DIMOK: Add waveInMinutes condition
    public int CanTrade2(bool buy, double minDelta,int totalLots,bool unisex) {
      if (isTradePending) return 0;
      double price = GetOffer(buy);
      return CanTrade(GetTrades(), buy, minDelta, totalLots, unisex);
    }
    public int CanTrade(Trade[] otherTrades, bool buy, double minDelta, int totalLots, bool unisex) {
      if (isTradePending) return 0;
      var minLoss = (from t in otherTrades
                     where (unisex || t.Buy == buy)
                     select t.PL
                    ).DefaultIfEmpty(1000).Max();
      var hasProfitTrades = otherTrades.Where(t => t.Buy == buy && t.PL > 0).Count() > 0 ;
      var tradeInterval = (otherTrades.Select(t => t.Time).DefaultIfEmpty().Max() - ServerTime).Duration();
      return minLoss.Between(-minDelta, 1) || hasProfitTrades || tradeInterval.TotalSeconds < 10 ? 0 : totalLots;
    }

    public double GetOffer(bool buy) {
      var t = GetTable(TABLE_OFFERS).FindRow(FIELD_INSTRUMENT,Pair,0) as FXCore.RowAut;
      return (double)(buy ? t.CellValue("Ask") : t.CellValue("Bid"));
    }
    public Order[] GetOrders(string Pair) {
      var ttt = from t in GetRows(TABLE_ORDERS)
                where new[] { (t.CellValue(FIELD_INSTRUMENT) + "").ToLower(), "" }.Contains(Pair.ToLower())
                select t;
      var orderList = new List<Order>();
      foreach (var t in ttt) {
        var order = new Order();
        order.OrderID = (String)t.CellValue("OrderID");
        order.RequestID = (String)t.CellValue("RequestID");
        order.AccountID = (String)t.CellValue("AccountID");
        order.AccountName = (String)t.CellValue("AccountName");
        order.OfferID = (String)t.CellValue("OfferID");
        order.Instrument = (String)t.CellValue("Instrument");
        order.TradeID = (String)t.CellValue("TradeID");
        order.NetQuantity = (Boolean)t.CellValue("NetQuantity");
        order.BS = (String)t.CellValue("BS");
        order.Stage = (String)t.CellValue("Stage");
        order.Side = (int)t.CellValue("Side");
        order.Type = (String)t.CellValue("Type");
        order.FixStatus = (String)t.CellValue("FixStatus");
        order.Status = (String)t.CellValue("Status");
        order.StatusCode = (int)t.CellValue("StatusCode");
        order.StatusCaption = (String)t.CellValue("StatusCaption");
        order.Lot = (int)t.CellValue("Lot");
        order.AmountK = (Double)t.CellValue("AmountK");
        order.Rate = (Double)t.CellValue("Rate");
        order.SellRate = (Double)t.CellValue("SellRate");
        order.BuyRate = (Double)t.CellValue("BuyRate");
        order.Stop = (Double)t.CellValue("Stop");
        //order.UntTrlMove = (Double)t.CellValue("UntTrlMove");
        order.Limit = (Double)t.CellValue("Limit");
        order.Time = (DateTime)t.CellValue("Time");
        order.IsBuy = (Boolean)t.CellValue("IsBuy");
        order.IsConditionalOrder = (Boolean)t.CellValue("IsConditionalOrder");
        order.IsEntryOrder = (Boolean)t.CellValue("IsEntryOrder");
        order.Lifetime = (int)t.CellValue("Lifetime");
        order.AtMarket = (String)t.CellValue("AtMarket");
        order.TrlMinMove = (int)t.CellValue("TrlMinMove");
        order.TrlRate = (Double)t.CellValue("TrlRate");
        order.Distance = (int)t.CellValue("Distance");
        order.GTC = (String)t.CellValue("GTC");
        order.Kind = (String)t.CellValue("Kind");
        order.QTXT = (String)t.CellValue("QTXT");
        //order.StopOrderID = (String)t.CellValue("StopOrderID");
        //order.LimitOrderID = (String)t.CellValue("LimitOrderID");
        order.TypeSL = (int)t.CellValue("TypeSL");
        order.TypeStop = (int)t.CellValue("TypeStop");
        order.TypeLimit = (int)t.CellValue("TypeLimit");
        order.OCOBulkID = (int)t.CellValue("OCOBulkID");
        orderList.Add(order);
      }
      return orderList.ToArray();
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
    FXCore.RowAut FindOfferByPair(string pair) {
      try {
        return Desk == null ? null : Desk.FindRowInTable(TABLE_OFFERS, FIELD_INSTRUMENT, pair) as FXCore.RowAut;
      } catch (ArgumentException) {
        return null;
      }
    } 
    #endregion

    #region FIX
    public void FixOrderOpen(bool buy, int lots, double takeProfit, double stopLoss,string remark) {
      fixOrderOpen(buy, lots, takeProfit, stopLoss, remark);
      return;
      new Thread(() => {
        while (isTradePending)
          Thread.Sleep(100);
        isTradePending = true;
        fixOrderOpen(buy, lots, takeProfit, stopLoss,remark);
      }
        ).Start();
    }
    private bool fixOrderOpen(bool buy, int lots, double takeProfit,  string remark) {
      return fixOrderOpen(buy, lots, takeProfit, 0, remark);
    }
    private bool fixOrderOpen(bool buy, int lots, double takeProfit, double stopLoss, string remark) {
      if (isTradePending) return false;
        object psOrderID, psDI;
        try {
          var price = GetOffer(buy);
          var limit = Math.Round(takeProfit < 0 ? -takeProfit : takeProfit == 0 ? 0 : price + takeProfit * PointSize * (buy ? 1 : -1), Digits);
          var stop = Math.Round(stopLoss < 0 ? -stopLoss : stopLoss == 0 ? 0 : price - stopLoss * PointSize * (buy ? 1 : -1), Digits);
          //Desk.OpenTrade(GetAccount().ID, Pair, buy, lots, price, "", 1, stop, limit, 0, out psOrderID, out psDI);
          Desk.CreateFixOrder(Desk.FIX_OPENMARKET, "", price, 0, "", GetAccount().ID, Pair, buy, lots, remark, out psOrderID, out psDI);
          isTradePending = true;
          return true;
        } catch (Exception exc) {
           RaiseOrderError(new OrderExecutionException((buy ? "Buy" : "Sell") + lots + " lots failed.", exc));
          return false;
        }
    }
    private void fixOrderOpenSure(bool buy, int lots, double takeProfit, double stopLoss,string remark) {
      var ok = false;
      var count = 10;
      for (; !ok && count > 0; count--)
        ok = fixOrderOpen(buy, lots, takeProfit, stopLoss,remark);
    }
    public void FixOrderClose(bool buy, bool last) {
      var trade = GetTrade(buy, last);
      if (trade != null)
        FixOrderClose(trade.Id, Desk.FIX_CLOSEMARKET,GetPrice());
    }

    static List<string> pendingOrders = new List<string>();
    private static object globalOrderPending = new object();
    private bool isGlobalOrderPending { get { return GetOrders("").Length > 0; } }
    public bool FixOrderOpen(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark) {
      string orderId, tradeId;
      return FixOrderOpen(pair, buy, lots, takeProfit, stopLoss, remark, out orderId, out tradeId);
    }
    public bool FixOrderOpen(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, out string orderId, out string tradeId) {
      if (isGlobalOrderPending) { orderId = tradeId = ""; return false; }
      lock (globalOrderPending) {
        object psOrderID, psDI;
        try {
          coreFX.Desk.CreateFixOrder(coreFX.Desk.FIX_OPENMARKET, "", 0, 0, "", GetAccount().ID, pair, buy, lots, remark, out psOrderID, out psDI);
          var orders = GetOrders("");
          var trades = GetTrades("");
          orderId = psOrderID+"";
          tradeId = trades.Where(t => t.OpenOrderID == psOrderID + "").Select(t => t.Id).FirstOrDefault() + "";
          return !string.IsNullOrWhiteSpace(tradeId);
        } catch (Exception exc) {
          throw new Exception(string.Format("Pair:{0},Buy:{1},Lots:{2}", pair, buy, lots),exc);
        }
      }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public object[] FixOrdersClose(params string[] tradeIds) {
      var ordersList = new List<object>();
      foreach (var tradeId in tradeIds)
        ordersList.Add(FixOrderClose(tradeId));
      return ordersList.ToArray();
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public object[] FixOrdersCloseAll() {
      var ordersList = new List<object>();
      var trade = GetTrades("").FirstOrDefault();
      while (trade != null) {
        ordersList.Add(FixOrderClose(trade.Id));
        trade = GetTrades("").FirstOrDefault();
      }
      return ordersList.ToArray();
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public object FixOrderClose(string tradeId) { return FixOrderClose(tradeId, coreFX.Desk.FIX_CLOSEMARKET, null); }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public object FixOrderClose(string tradeId, int mode, Price price) {
      object psOrderID=null, psDI;
      try {
        var row = GetTable(TABLE_TRADES).FindRow(FIELD_TRADEID, tradeId, 0) as FXCore.RowAut;
        var dRate = mode == coreFX.Desk.FIX_CLOSEMARKET || price == null ? 0 : IsBuy(row.CellValue("BS")) ? price.Bid : price.Ask;
        if (GetAccount().Hedging) {
          //RunWithTimeout.WaitFor<object>.Run(TimeSpan.FromSeconds(5), () => {
          coreFX.Desk.CreateFixOrder(mode, tradeId, dRate, 0, "", "", "", true, (int)row.CellValue("Lot"), "", out psOrderID, out psDI);
          while (GetTrade(tradeId) != null)
            Thread.Sleep(100);
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
    public void FixOrderSetNetLimitsBuy(double takeProfit) {
      FixOrderSetNetLimits(takeProfit, true);
    }
    public void FixOrderSetNetLimitsSell(double takeProfit) {
      FixOrderSetNetLimits(takeProfit, false);
    }
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
          } catch(Exception) {
            FixOrderSetNetLimits(takeProfit, buy);
          }
        }
    }
    public void FixOrderDeleteLimits(string[] tradeId) { tradeId.ToList().ForEach(t => FixOrderDeleteLimits(t)); }
    public void FixOrderDeleteLimits(string tradeId) {
      object a, b;
      Desk.CreateFixOrder(Desk.FIX_LIMIT, tradeId, 0, 0, "", "", "", true, 0, "", out a, out b);
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

    public double InPips(int level, double? price, int roundTo) { return Math.Round(InPips(level, price), roundTo); }
    public double InPips(int level,double? price) {
      if (level == 0) return price.GetValueOrDefault();
      return InPips(--level, price / PointSize); 
    }
    public double InPips(double? price, int roundTo) { return Math.Round(InPips(price), roundTo); }
    public double InPips(double? price) { return (price / PointSize).GetValueOrDefault(); }

    public interface FXNewOfferListener {
      /// <summary>
      /// When new offer appears
      /// </summary>
      void OnNewOffer(Tick tick);
    }

    public string[] GetInstruments() {
      if (Desk == null)
        throw new UserNotLoggedInException("this.Desk");

      List<string> instr = new List<string>();
      var rows = GetRows(TABLE_OFFERS);
      foreach (var row in rows)
        instr.Add(row.CellValue(FIELD_INSTRUMENT).ToString());
      return instr.ToArray();
    }


    FXCore.TradeDeskEventsSinkClass mSink;
    int mSubscriptionId = -1;

    #region (Un)Subscribe
    ///// <summary>
    ///// Make subscription of offers of the specified instrument
    ///// </summary>
    //public void SubscribeForOffers(string instrument, FXNewOfferListener listener) {
    //  Unsubscribe();
    //  mInstrument = instrument;
    //  mListener = listener;
    //  mSink = new FXCore.TradeDeskEventsSinkClass();
    //  mSink.ITradeDeskEvents_Event_OnRowChanged += new FXCore.ITradeDeskEvents_OnRowChangedEventHandler(FxCore_RowChanged);
    //  mSink.ITradeDeskEvents_Event_OnRowAdded += new FXCore.ITradeDeskEvents_OnRowAddedEventHandler(mSink_ITradeDeskEvents_Event_OnRowAdded);
    //  mSubscriptionId = mDesk.Subscribe(mSink);
    //}

    //void mSink_ITradeDeskEvents_Event_OnRowAdded(object pTableDisp, string sRowID) {
    //  if (RowAdded != null) RowAdded(((FXCore.TableAut)pTableDisp).Type, sRowID);
    //}
    FXCore.ITradeDeskEvents_OnRowAddedEventHandler FX_RowAddedHandler;
    FXCore.ITradeDeskEvents_OnRowChangedEventHandler FX_RowChangedHandler;
    FXCore.ITradeDeskEvents_OnRowBeforeRemoveEventHandler FX_RowRemovingdHandler;
    FXCore.ITradeDeskEvents_OnRowBeforeRemoveExEventHandler FX_RowRemovingExHandler;
    bool isSubsribed = false;
    public void Subscribe() {
      if (isSubsribed) return;
      Unsubscribe();
      mSink = new FXCore.TradeDeskEventsSinkClass();
      FX_RowChangedHandler  = new FXCore.ITradeDeskEvents_OnRowChangedEventHandler(FxCore_RowChanged);
      FX_RowAddedHandler = new FXCore.ITradeDeskEvents_OnRowAddedEventHandler(mSink_ITradeDeskEvents_Event_OnRowAdded);
      FX_RowRemovingdHandler = new FXCore.ITradeDeskEvents_OnRowBeforeRemoveEventHandler(mSink_ITradeDeskEvents_Event_OnRowBeforeRemove);
      FX_RowRemovingExHandler = new FXCore.ITradeDeskEvents_OnRowBeforeRemoveExEventHandler(mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx);
      mSink.ITradeDeskEvents_Event_OnRowChanged += FX_RowChangedHandler;
      mSink.ITradeDeskEvents_Event_OnRowAdded += FX_RowAddedHandler;
      mSink.ITradeDeskEvents_Event_OnRowBeforeRemove += FX_RowRemovingdHandler;
      mSink.ITradeDeskEvents_Event_OnRowBeforeRemoveEx += FX_RowRemovingExHandler;
      mSubscriptionId = Desk.Subscribe(mSink);
      isSubsribed = true;
    }

    void mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx(object _table, string RowID, string sExtInfo) {
      try {
        FXCore.TableAut table = _table as FXCore.TableAut;
        switch (table.Type.ToLower()) {
          case "trades":
            System.IO.File.AppendAllText("Trades.txt", sExtInfo + Environment.NewLine);
            break;
        }
      } catch (Exception exc) {
        RaiseError(exc);
      }
    }

    public void Unsubscribe() {
      if (mSubscriptionId != -1) {
        try {
          mSink.ITradeDeskEvents_Event_OnRowChanged -= FX_RowChangedHandler;
          mSink.ITradeDeskEvents_Event_OnRowAdded -= FX_RowAddedHandler;
          mSink.ITradeDeskEvents_Event_OnRowBeforeRemove -= FX_RowRemovingdHandler;
          mSink.ITradeDeskEvents_Event_OnRowBeforeRemoveEx -= FX_RowRemovingExHandler;
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

    bool _tradePending;
    DateTime _tradePendingDate = DateTime.MinValue;

    bool isTradePending {
      get {
        return _tradePending /*&& _tradePendingDate.AddSeconds(3) > DateTime.Now*/;
      }
      set {
        _tradePending = value;
        if (value) _tradePendingDate = DateTime.Now;
      }
    }

    void mSink_ITradeDeskEvents_Event_OnRowBeforeRemove(object _table, string RowID) {
      FXCore.TableAut table = _table as FXCore.TableAut;
      RaiseRowRemoving(table.Type, RowID);
    }

    void mSink_ITradeDeskEvents_Event_OnRowAdded(object _table, string RowID) {
      try {
        System.Threading.Thread.CurrentThread.Priority = ThreadPriority.Highest;
        FXCore.TableAut table = _table as FXCore.TableAut;
        FXCore.RowAut row;
        Func<FXCore.TableAut, FXCore.RowAut, string> showTable = (t, r) => {
          var columns = (t.Columns as FXCore.ColumnsEnumAut).Cast<FXCore.ColumnAut>()
            .Where(c => (c.Title + "").Length != 0).Select(c=>new {c.Title, Value=r.CellValue(c.Title)+""});
          return new XElement(t.Type.Replace(" ","_"), columns.Select(c => new XAttribute(c.Title,c.Value)))
            .ToString(SaveOptions.DisableFormatting);
        };
        switch (table.Type.ToLower()) {
          case "trades":
            isTradePending = false;
            row = table.FindRow(FIELD_TRADEID, RowID, 0) as FXCore.RowAut;
            var trade = GetTrades().OrderBy(t => t.Id).Last();
            if (new[]{"", (row.CellValue(FIELD_INSTRUMENT) + "").ToUpper()}.Contains(Pair) && TradesCountChanged != null)
              TradesCountChanged(trade);
            if( OnTradeCountChangedCallback!= null)
              OnTradeCountChangedCallback(trade);
            break;
          case "orders":
            row = table.FindRow(FIELD_ORDERID, RowID, 0) as FXCore.RowAut;
            if (row.CellValue(FIELD_INSTRUMENT) + "" == Pair)
              RaiseOrderAdded(row);
            break;
          case "closed trades":
            row = table.FindRow(FIELD_TRADEID, RowID, 0) as FXCore.RowAut;
            System.IO.File.AppendAllText("ClosedTrades.txt", showTable(table, row) + Environment.NewLine);
            break;
        }
      } catch (Exception exc) { RaiseError(exc); }
    }

    public Price GetPrice() {
      return GetPrice(GetRows(TABLE_OFFERS, Pair).FirstOrDefault());
    }
    private Price GetPrice(FXCore.RowAut Row) {
      return new Price() { Ask = (double)Row.CellValue(FIELD_ASK), Bid = (double)Row.CellValue(FIELD_BID), AskChangeDirection = (int)Row.CellValue(FIELD_ASKCHANGEDIRECTION), BidChangeDirection = (int)Row.CellValue(FIELD_BIDCHANGEDIRECTION), Time = ((DateTime)Row.CellValue(FIELD_TIME)).AddHours(coreFX.ServerTimeOffset), Pair = Pair };
    }
    void FxCore_RowChanged(object _table, string rowID) {
      try {
        FXCore.TableAut table = _table as FXCore.TableAut;
        FXCore.RowAut row;
        RaiseRowChanged(table.Type, rowID);
        switch (table.Type.ToLower()) {
          case "offers":
            row = table.FindRow("OfferID", rowID, 0) as FXCore.RowAut;
            if ((DateTime)row.CellValue(FIELD_TIME) != _prevOfferDate 
              && new[]{"", row.CellValue(FIELD_INSTRUMENT) + ""}.Contains(Pair.ToUpper())
              && PriceChanged != null) {
              _prevOfferDate = (DateTime)row.CellValue(FIELD_TIME);
              PriceChanged(GetPrice(row));
            }
            break;
        }
      } catch (Exception exc) {
        RaiseError(exc);
      }
    }

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

    #region IDisposable Members

    public void Dispose() {
      LogOff();
      coreFX = null;
      GC.SuppressFinalize(this);
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
  public class Price : HedgeHog.Bars.Price { }
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
    public Price PriceCurrent { get; set; }

    double _pipCost;
    public double PipCost {
      get { return _pipCost; }
      set { _pipCost = value >= 10 ? value * .01 : value; }
    }
    public double NetPL { get; set; }
  }
  #endregion
  public static class Extentions {
    //public static int ToInt(this double d) { return (int)Math.Round(d, 0); }
    //public static DateTime Round(this DateTime dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    //public static bool Between(this double value, double d1, double d2) {
    //  return d1>d2?Between(value,d2,d1): d1 <= value && value <= d2;
    //}
    //public static bool Between(this DateTime value, DateTime d1, DateTime d2) {
    //  return d1>d2?Between(value,d2,d1): d1 <= value && value <= d2;
    //}
  }
}
