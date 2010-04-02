using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using HedgeHog;
using HedgeHog.Bars;

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

  public class UserNotLoggedInException : Exception {
    public UserNotLoggedInException(string message) : base(message) { }
  }

  [Serializable]
  public class TradeRemark {
    const char PIPE = '|';
    int _tradeWaveInMinutes = 0;
    public int TradeWaveInMinutes { 
      get { return _tradeWaveInMinutes; } 
      set {
        if (value < 1000)
          _tradeWaveInMinutes = value;
        else _tradeWaveInMinutes = 0;
      }
    }
    double _tradeWaveHeight = 0;
    public double TradeWaveHeight {
      get { return _tradeWaveHeight; }
      set { _tradeWaveHeight = value; }
    }
    double _angle = 0;
    public double Angle {
      get { return _angle; }
      set { _angle = value; }
    }
    public TradeRemark(int tradeWaveInMinutes, double tradeWaveHeight,double angle) {
      TradeWaveInMinutes = tradeWaveInMinutes;
      TradeWaveHeight = Math.Round(tradeWaveHeight, 1);
      Angle = Math.Round(angle, 2);
    }
    public TradeRemark(string remark) {
      var info = remark.Split(new[] { PIPE }, StringSplitOptions.RemoveEmptyEntries);
      if (info.Length > 0) int.TryParse(info[0], out _tradeWaveInMinutes);
      if (info.Length > 1) double.TryParse(info[1], out _tradeWaveHeight);
      if (info.Length > 2) double.TryParse(info[2], out _angle);
    }
    public override string ToString() {
      return string.Join(PIPE+"", 
        new object[] {
          TradeWaveInMinutes.ToString("000"),
          TradeWaveHeight ,
          Angle
        }.Select(o => o + "").ToArray());
    }
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

    #endregion

    #region Properties
    private DateTime _prevOfferDate;
    private string _pair = "";
    public string PriceFormat {
      get { return "{0:0."+"".PadRight(Digits,'0')+"}"; }
    }
    public string Pair {
      get { return _pair; }
      set {
        if (_pair == value) return;
        _pair = value.ToUpper();
          if (Desk != null && FindOfferByPair(value) == null) Desk.SetOfferSubscription(value, "Enabled");
        PointSize = 0;
        Digits = 0;
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
    public FXCoreWrapper() {
      TradesCountChanged += (t) => { isTradePending = false; };
    }
    public FXCoreWrapper(CoreFX coreFX):this(coreFX,null) { }
    public FXCoreWrapper(CoreFX coreFX,string pair) {
      this.coreFX = coreFX;
      if (pair != null) this.Pair = pair;
      isWiredUp = true;
      coreFX.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
      coreFX.LoggedOffEvent += new EventHandler<EventArgs>(coreFX_LoggedOffEvent);
    }
    ~FXCoreWrapper() {
      LogOff();
      coreFX = null;
    }
    public bool LogOn(string pair,CoreFX core, string user, string password, bool isDemo) {
      return LogOn(pair, core, user, password, new Uri(""), isDemo);
    }
    bool isWiredUp;
    public bool LogOn(string pair, CoreFX core, string user, string password, string url, bool isDemo) {
      return LogOn(pair, core, user, password, new Uri(url), isDemo);
    }
    public bool LogOn(string pair, CoreFX core, string user, string password, Uri url, bool isDemo) {
      coreFX = core;
      if (!isWiredUp) {
        isWiredUp = true;
        core.LoggedInEvent += new EventHandler<EventArgs>(coreFX_LoggedInEvent);
        core.LoggedOffEvent += new EventHandler<EventArgs>(coreFX_LoggedOffEvent);
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
      Unsubscribe();
    }
    #endregion

    public IEnumerable<Tick> GetTicks(DateTime startDate, DateTime endDate) {
      return GetTicks(startDate, endDate, maxTicks);
    }
    private static object lockHistory = new object();
    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<Tick> GetTicks(DateTime startDate, DateTime endDate, int barsMax) {
      lock (lockHistory) {
        var mr = //RunWithTimeout.WaitFor<FXCore.MarketRateEnumAut>.Run(TimeSpan.FromSeconds(5), () =>
         ((FXCore.MarketRateEnumAut)Desk.GetPriceHistory(Pair, "t1", startDate, endDate, barsMax, true, true)).Cast<FXCore.MarketRateAut>().ToArray();
        //);
        return mr.Select((r, i) => new Tick(r.StartDate, r.AskOpen, r.BidOpen, i, true)).ToList();
      }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
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
              System.Diagnostics.Debug.WriteLine("Ticks:" + ticks.Count()+" @ "+endDate.ToLongTimeString());
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
      return ticks.OrderBars().Take(tickCount).ToArray();
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
    IEnumerable<Rate> GetBarsBase_(int period, DateTime startDate, DateTime endDate) {
      try {
        return period == 0 ?
          GetTicks(startDate, endDate).Cast<Rate>() :
          GetBars(period, startDate, endDate);
      } catch (Exception exc) {
        var empty = period == 0 ? new Tick[] { }.Cast<Rate>() : new Rate[] { };
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

    public DateTime ServerTime { get { return coreFX.ServerTime; } } 
    #region FX Tables
    public Account GetAccount() {
      var row = GetRows(TABLE_ACCOUNTS).First();
      var account = new Account() {
        ID = row.CellValue(FIELD_ACCOUNTID) + "",
        Balance = (double)row.CellValue(FIELD_BALANCE),
        UsableMargin = (double)row.CellValue(FIELD_USABLEMARGIN),
        IsMarginCall = row.CellValue(FIELD_MARGINCALL)+"" == "W",
        Equity = (double)row.CellValue(FIELD_EQUITY),
        Hedging = row.CellValue("Hedging").ToString() == "Y"
      };
      account.PipsToMC = (int)(account.UsableMargin * PipsToMarginCallPerUnitCurrency());
      //account.PipsToMC = summary == null ? 0 :
      //  (int)(account.UsableMargin / Math.Max(.1, (Math.Abs(summary.BuyLots - summary.SellLots) / 10000)));
      return account;
    }
    private double PipsToMarginCallPerUnitCurrency() {
      var sm = (from s in GetRows(TABLE_SUMMARY)
                join o in GetRows(TABLE_OFFERS)
                on s.CellValue(FIELD_INSTRUMENT) + "" equals o.CellValue(FIELD_INSTRUMENT) + ""
                let pc = (double)o.CellValue(FIELD_PIPCOST)
                select new {
                  Amount = (double)s.CellValue("AmountK"),
                  PipCost = pc >= 10 ? pc * .01 : pc
                }).ToArray();
      if (sm.Count() == 0 || sm.Sum(s=>s.Amount) == 0) return 1 / .1;
      var pipCostWieghtedAverage = sm.Sum(s => s.Amount * s.PipCost) / sm.Sum(s => s.Amount);
      return -1 / (sm.Sum(s => s.Amount) * pipCostWieghtedAverage);
    }
    public Summary GetSummary() {
      var rowsSumm = GetRows(TABLE_SUMMARY,Pair);
      var s = rowsSumm
        .Select(t => new Summary() {
          BuyLots = ((long)(double)t.CellValue("BuyAmountK")) * 1000,
          SellLots = ((long)(double)t.CellValue("SellAmountK")) * 1000,
          BuyNetPL = ((double)t.CellValue("BuyNetPL")),
          //BuyNetPLPip = ((double)t.CellValue("BuyNetPLPip")),
          SellNetPL = ((double)t.CellValue("SellNetPL")),
          //SellNetPLPip = ((double)t.CellValue("SellNetPLPip")),
          NetPL = ((double)t.CellValue("NetPL")),
          OfferID = t.CellValue("SellNetPL") + "",
          SellAvgOpen = (double)t.CellValue(FIELD_SELLAVGOPEN),
          BuyAvgOpen = (double)t.CellValue(FIELD_BUYAVGOPEN),
          PriceCurrent = GetPrice(),
          PointSize = PointSize
        }).SingleOrDefault();
      if (s == null) return Summary.Initialize(GetPrice());
      var rows = GetRows(TABLE_TRADES,Pair,  true).OrderByDescending(t => (t.CellValue(FIELD_OPEN) + "")).ToArray();
      if (rows.Count() > 0) {
        var rowFirst = rows.First();
        s.BuyPriceFirst = (double)rowFirst.CellValue(FIELD_OPEN);
        s.BuyLotsFirst = (int)rowFirst.CellValue("Lot");
        s.BuyTradeID_First = rowFirst.CellValue(FIELD_TRADEID) + "";

        var rowLast = rows.Last();
        s.BuyPriceLast = (double)rowLast.CellValue(FIELD_OPEN);
        s.BuyLotsLast = (int)rowLast.CellValue("Lot");
        s.BuyTradeID_Last = rowLast.CellValue(FIELD_TRADEID) + "";

        s.BuyPositions = rows.Count();
      }
      rows = GetRows(TABLE_TRADES,Pair,  false).OrderBy(t => (t.CellValue(FIELD_OPEN) + "")).ToArray();
      if (rows.Count() > 0) {
        var rowFirst = rows.First();
        s.SellPriceFirst = (double)rowFirst.CellValue(FIELD_OPEN);
        s.SellLotsFirst = (int)rowFirst.CellValue("Lot");
        s.SellTradeID_First = rowFirst.CellValue(FIELD_TRADEID) + "";

        var rowLast = rows.Last();
        s.SellTradeID_Last = rowLast.CellValue(FIELD_TRADEID) + "";
        s.SellPriceLast = (double)rowLast.CellValue(FIELD_OPEN);
        s.SellLotsLast = (int)rowLast.CellValue("Lot");

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
    private FXCore.RowAut[] GetRows(string tableName) { return GetRows(tableName, (bool?)null); }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private FXCore.RowAut[] GetRows(string tableName, bool? isBuy) {
        var ret = (GetTable(tableName).Rows as FXCore.RowsEnumAut).Cast<FXCore.RowAut>().ToArray();
        if (isBuy != null)
          try {
            ret = ret.Where(t => (t.CellValue("BS") + "") == (isBuy.Value ? "B" : "S")).ToArray();
          } catch {
            return GetRows(tableName, isBuy);
          }
        return ret;
    }
    [CLSCompliant(false)]
    public FXCore.TableAut GetTable(string TableName) {
        return Desk.FindMainTable(TableName) as FXCore.TableAut;
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
    public void ClosePositions() { ClosePositions(0, 0); }
    public void ClosePositions(int buyMin,int sellMin) {
      //DIMOK: Test ClosePositions(...)
      int errorCount = 3;
      var summury = GetSummary();
      sellMin = Math.Max(0, sellMin);
      buyMin = Math.Max(0, buyMin);
      while (summury.SellPositions > sellMin || summury.BuyPositions > buyMin) {
        try {
          if (summury.BuyPositions > buyMin) FixOrderClose(true, false);
          if (summury.SellPositions > sellMin) FixOrderClose(false, false);
          summury = GetSummary();
        } catch (Exception exc) {
          if (errorCount-- > 0) RaiseError(exc);
          else throw;
        }
      }
    }
    #endregion

    public void CloseProfit(bool buy, double minProfitInPips) {
      if (!GetAccount().Hedging) {
        var lots = (int)GetTradesToClose(buy, minProfitInPips).Sum(r => r.Lots);
        fixOrderOpenSure(!buy, lots, 0, 0,"");
      } else
        new Thread(() => {
          var tradesIDs = GetTradesToClose(buy, minProfitInPips).Select(r => r.Id).ToArray();
          while (tradesIDs.Length > 0) {
            foreach (var tradeID in tradesIDs)
              try {
                FixOrderClose(tradeID);
              } catch { }
            tradesIDs = GetTradesToClose(buy, minProfitInPips).Select(r => r.Id).ToArray();
          }
        }).Start();
    }

    public Trade[] GetTradesToClose(bool buy, double minProfitInPips) {
      try {
        return (from t in GetRows(TABLE_TRADES)
                  join tid in GetOrders() on (t.CellValue(FIELD_TRADEID) + "") equals tid into tids
                  from oTID in tids.DefaultIfEmpty()
                  orderby (double)t.CellValue("GrossPL")
                  where
                    oTID == null &&
                    IsBuy(t.CellValue("BS") + "") == buy &&
                    (t.CellValue(FIELD_INSTRUMENT) + "") == Pair &&
                    (double)t.CellValue("PL") >= minProfitInPips
                  select new Trade() {
                    Id = t.CellValue("TradeID") + "",
                    Buy = (t.CellValue("BS") + "") == "B",
                    PL = (double)t.CellValue("PL"),
                    GrossPL = (double)t.CellValue("GrossPL"),
                    Limit = (double)t.CellValue("Limit"),
                    Lots = (Int32)t.CellValue("Lot"),
                    Open = (double)t.CellValue("Open")
                  }).OrderBy(t => t.Id).ToArray();
      } catch(Exception exc) {
        RaiseError(exc);
          return new Trade[]{};
      }
    }
    public double GetMaxDistance(bool buy) {
      var trades = GetTrades().Where(t => t.Buy == buy).Select((t, i) => new { Pos = i, t.Open });
      if (trades.Count() < 2) return 0;
      var distance = (from t0 in trades
                      join t1 in trades on t0.Pos equals t1.Pos-1
                      select Math.Abs(t0.Open-t1.Open)).Max(d=>d);
      return distance;
    }
    FXCore.ITimeZoneConverterAut timeZoneConverter{get{return Desk.TimeZoneConverter as FXCore.ITimeZoneConverterAut;}}
    DateTime ConvertDateToLocal(DateTime date) {
      var converter = timeZoneConverter;
      return converter.Convert(date, converter.ZONE_UTC, converter.ZONE_LOCAL);
    }

    public Trade GetTrade(string tradeId) { return GetTrades().Where(t => t.Id == tradeId).SingleOrDefault(); }
    public Trade GetTrade(bool buy, bool last) { return last ? GetTradeLast(buy) : GetTradeFirst(buy); }
    public Trade GetTradeFirst(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).FirstOrDefault(); }
    public Trade GetTradeLast(bool buy) { return GetTrades(buy).OrderBy(t => t.Id).LastOrDefault(); }
    public IEnumerable<Trade> GetTrades(bool buy) { return GetTrades().Where(t => t.Buy == buy); }
    public IEnumerable<Trade> GetTrades() {
      lock (this) {
        var ret = from t in GetRows(TABLE_TRADES)
                  orderby t.CellValue("BS") + "", t.CellValue("TradeID") + "" descending
                  where (t.CellValue(FIELD_INSTRUMENT) + "") == Pair
                  select new Trade() {
                    Id = t.CellValue("TradeID") + "",
                    Buy = (t.CellValue("BS") + "") == "B",
                    PL = (double)t.CellValue("PL"),
                    GrossPL = (double)t.CellValue("GrossPL"),
                    Limit = (double)t.CellValue("Limit"),
                    Lots = (Int32)t.CellValue("Lot"),
                    Open = (double)t.CellValue("Open"),
                    Time = ConvertDateToLocal((DateTime)t.CellValue("Time")),// ((DateTime)t.CellValue("Time")).AddHours(coreFX.ServerTimeOffset),
                    Remark = new TradeRemark(t.CellValue("QTXT")+"")
                  };
        return ret;
      }
    }
    public bool CanTrade(bool buy, double minDelta) {
      if (isTradePending) return false;
      double price = GetOffer(buy);
      return (from t in GetRows(TABLE_TRADES)
              where (t.CellValue(FIELD_INSTRUMENT) + "") == Pair &&
                IsBuy(t.CellValue("BS")) == buy &&
                (double)t.CellValue(FIELD_OPEN) <= price + PointSize * minDelta &&
                (double)t.CellValue(FIELD_OPEN) > price - PointSize * minDelta
              //((Buy && (double)t.CellValue(FIELD_OPEN) - Price < PointSize * MinDelta) ||
              //(!Buy && Price - (double)t.CellValue(FIELD_OPEN) < PointSize * MinDelta))
              select t).Count() == 0;
    }
    //DIMOK: Add waveInMinutes condition
    public int CanTrade2(bool buy, double minDelta,int totalLots,bool unisex) {
      if (isTradePending) return 0;
      double price = GetOffer(buy);
      var trades = (from t in GetRows(TABLE_TRADES)
                  where (t.CellValue(FIELD_INSTRUMENT) + "") == Pair &&
                    (unisex || IsBuy(t.CellValue("BS")) == buy) &&
                    (double)t.CellValue(FIELD_OPEN) <= price + PointSize * minDelta &&
                    (double)t.CellValue(FIELD_OPEN) > price - PointSize * minDelta
                  //((Buy && (double)t.CellValue(FIELD_OPEN) - Price < PointSize * MinDelta) ||
                  //(!Buy && Price - (double)t.CellValue(FIELD_OPEN) < PointSize * MinDelta))
                  select t).ToArray();
      var lots = trades.Sum(t => (int)t.CellValue("Lot"));
      if (lots > 0) return 0;
      return totalLots - lots;
    }
    public int CanTrade(double price, Trade[] otherTrades, bool buy, double minDelta, int totalLots, bool unisex) {
      var trades = (from t in otherTrades
                    where (unisex || t.Buy == buy) &&
                      t.Open.Between(price - PointSize * minDelta, price + PointSize * minDelta)
                    //((Buy && (double)t.CellValue(FIELD_OPEN) - Price < PointSize * MinDelta) ||
                    //(!Buy && Price - (double)t.CellValue(FIELD_OPEN) < PointSize * MinDelta))
                    select t).ToArray();
      var lots = trades.Sum(t => (int)t.Lots);
      if (lots > 0) return 0;
      return totalLots - lots;
    }

    public double GetOffer(bool buy) {
      var t = GetTable(TABLE_OFFERS).FindRow(FIELD_INSTRUMENT,Pair,0) as FXCore.RowAut;
      return (double)(buy ? t.CellValue("Ask") : t.CellValue("Bid"));
    }
    public string[] GetOrders() {
      var ret = from t in GetRows(TABLE_ORDERS)
                where t.CellValue(FIELD_INSTRUMENT)+"" == Pair
                select t.CellValue("TradeID") + "";
      return ret.ToArray();
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
        FixOrderClose(trade.Id, Desk.FIX_CLOSEMARKET);
    }
    public void FixOrderClose(string tradeId) { FixOrderClose(tradeId, Desk.FIX_CLOSEMARKET); }
    public void FixOrderClose(string tradeId, int mode) {
      object psOrderID, psDI;
      try {
        var row = GetTable(TABLE_TRADES).FindRow(FIELD_TRADEID, tradeId, 0) as FXCore.RowAut;
        var price = GetPrice();
        var dRate = mode == Desk.FIX_CLOSEMARKET ? 0 : IsBuy(row.CellValue("BS")) ? price.Bid : price.Ask;
        if (GetAccount().Hedging) {
          //RunWithTimeout.WaitFor<object>.Run(TimeSpan.FromSeconds(5), () => {
          Desk.CreateFixOrder(mode, tradeId, dRate, 0, "", "", "", true, (int)row.CellValue("Lot"), "", out psOrderID, out psDI);
          while (GetTrade(tradeId) != null)
            Thread.Sleep(100);
          //  return null;
          //});
        } else {
          var trade = GetTrades().SingleOrDefault(t => t.Id == tradeId);
          if (trade != null) {
            fixOrderOpenSure(!trade.Buy, (int)trade.Lots, 0, 0, "");
          }
        }
      } catch (Exception exc) {
          RaiseOrderError(new OrderExecutionException("Closing TradeID:" + tradeId + " failed.", exc));
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

    public double InPips(int level, double price, int roundTo) { return Math.Round(InPips(level, price), roundTo); }
    public double InPips(int level,double price) {
      if (level == 0) return price;
      return InPips(--level, price / PointSize); 
    }
    public double InPips(double price, int roundTo) { return Math.Round(InPips(price), roundTo); }
    public double InPips(double price) { return price / PointSize; }

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
    public void Subscribe() {
      Unsubscribe();
      mSink = new FXCore.TradeDeskEventsSinkClass();
      FX_RowChangedHandler  = new FXCore.ITradeDeskEvents_OnRowChangedEventHandler(FxCore_RowChanged);
      FX_RowAddedHandler = new FXCore.ITradeDeskEvents_OnRowAddedEventHandler(mSink_ITradeDeskEvents_Event_OnRowAdded);
      FX_RowRemovingdHandler = new FXCore.ITradeDeskEvents_OnRowBeforeRemoveEventHandler(mSink_ITradeDeskEvents_Event_OnRowBeforeRemove);
      mSink.ITradeDeskEvents_Event_OnRowChanged += FX_RowChangedHandler;
      mSink.ITradeDeskEvents_Event_OnRowAdded += FX_RowAddedHandler;
      mSink.ITradeDeskEvents_Event_OnRowBeforeRemove += FX_RowRemovingdHandler;
      mSink.ITradeDeskEvents_Event_OnRowBeforeRemoveEx += new FXCore.ITradeDeskEvents_OnRowBeforeRemoveExEventHandler(mSink_ITradeDeskEvents_Event_OnRowBeforeRemoveEx);
      mSubscriptionId = Desk.Subscribe(mSink);
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
        } catch { }
        mSink = null;
        try {
          Desk.Unsubscribe(mSubscriptionId);
        } catch { }
      }
      mSubscriptionId = -1;
    }
    #endregion

    bool _tradePending;
    DateTime _tradePendingDate = DateTime.MinValue;

    bool isTradePending {
      get {
        return _tradePending && _tradePendingDate.AddSeconds(3) > DateTime.Now;
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
            row = table.FindRow(FIELD_TRADEID, RowID, 0) as FXCore.RowAut;
            if ((row.CellValue(FIELD_INSTRUMENT) + "") == Pair && TradesCountChanged != null)
              TradesCountChanged(GetTrades().OrderBy(t => t.Id).Last());
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
      FXCore.TableAut table = _table as FXCore.TableAut;
      FXCore.RowAut row;
      RaiseRowChanged(table.Type, rowID);
      switch (table.Type.ToLower()) {
        case "offers":
          row = table.FindRow("OfferID", rowID, 0) as FXCore.RowAut;
          if ((DateTime)row.CellValue(FIELD_TIME) != _prevOfferDate && (row.CellValue(FIELD_INSTRUMENT)+"") == Pair && PriceChanged != null) {
            _prevOfferDate = (DateTime)row.CellValue(FIELD_TIME);
            PriceChanged(GetPrice(row));
          }
          break;
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
  public class Price : HedgeHog.Bars.Price {  }
  [Serializable]
  public class Trade {
    [DisplayName("")]
    public string Id { get; set; }
    [DisplayName("BS")]
    public bool Buy { get; set; }
    [DisplayName("##")]
    [DisplayFormat(DataFormatString = "{0}")]
    public TradeRemark Remark { get; set; }
    [DisplayName("")]
    public double Open { get; set; }
    [DisplayName("")]
    public double Limit { get; set; }
    public double PL { get; set; }
    [DisplayName("")]
    public double GrossPL { get; set; }
    [DisplayFormat(DataFormatString = "{0:dd HH:mm}")]
    public DateTime Time { get; set; }
    public int Lots { get; set; }
    public override string ToString() {
      var x = new XElement(GetType().Name,
      GetType().GetProperties().Select(p => new XElement(p.Name, p.GetValue(this, null) + "")));
      return x.ToString(SaveOptions.DisableFormatting);
    }
  }
  public class Account {
    public string ID { get; set; }
    public double Balance { get;  set; }
    public double Equity { get;  set; }
    public double UsableMargin { get;  set; }
    public bool IsMarginCall { get;  set; }
    public int PipsToMC { get;  set; }
    public bool Hedging { get;  set; }
  }
  public class Summary {
    public static Summary Initialize(Price Price){
      return new Summary() { PriceCurrent = Price };
    }
    public string OfferID { get; set; }
    public string BuyTradeID_Last { get; set; }
    public string BuyTradeID_First { get; set; }
    public string SellTradeID_Last { get; set; }
    public string SellTradeID_First { get; set; }
    public double SellNetPL { get; set; }
    public double SellNetPLPip { get { return SellNetPL / SellLots * 10000; } }
    public double BuyNetPL { get; set; }
    public double BuyNetPLPip { get { return BuyNetPL / BuyLots * 10000; } }
    public double SellLots { get; set; }
    public double BuyLots { get; set; }
    public double Amount { get; set; }
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
  static class Extentions {
    public static int ToInt(this double d) { return (int)Math.Round(d, 0); }
    public static DateTime Round(this DateTime dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static bool Between(this double value, double d1, double d2) {
      return d1>d2?Between(value,d2,d1): d1 <= value && value <= d2;
    }
    public static bool Between(this DateTime value, DateTime d1, DateTime d2) {
      return d1>d2?Between(value,d2,d1): d1 <= value && value <= d2;
    }
  }
}
