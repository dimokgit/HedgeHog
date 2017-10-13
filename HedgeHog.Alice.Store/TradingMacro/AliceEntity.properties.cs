using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using HedgeHog.Bars;
using HedgeHog.Shared;
using HedgeHog.Models;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Linq.Expressions;
using ReactiveUI;
using System.Threading.Tasks;
using static HedgeHog.Core.JsonExtensions;
namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    bool IsPrimaryMacro {
      get {
        return IsTrader;
        //TradingStatistics != null && TradingStatistics.TradingMacros.Take(1).Any(tm => tm == this); 
      }
    }

    TradingStatistics _tradingStatistics = new TradingStatistics();
    public TradingStatistics TradingStatistics {
      get { return _tradingStatistics; }
      set { _tradingStatistics = value; }
    }

    #region MonthsOfHistory
    private int _MonthsOfHistory;
    public int MonthsOfHistory {
      get { return _MonthsOfHistory; }
      set {
        if(_MonthsOfHistory != value) {
          _MonthsOfHistory = value;
          OnPropertyChanged(nameof(MonthsOfHistory));
        }
      }
    }

    #endregion

    CorridorCalculationMethod _CorridorCalcMethod;
    [DisplayName("Corridor StDev By")]
    [WwwSetting(wwwSettingsCorridorFuncs)]
    [Category(categoryActiveFuncs)]
    public CorridorCalculationMethod CorridorCalcMethod {
      get { return _CorridorCalcMethod; }
      set {
        if(_CorridorCalcMethod != value) {
          _CorridorCalcMethod = value;
          OnPropertyChanged(nameof(CorridorCalcMethod));
        }
      }
    }


    #region DoLogSaveRates
    private bool _DoLogSaveRates;
    [Category(categoryXXX)]
    [IsNotStrategy]
    public bool DoLogSaveRates {
      get { return _DoLogSaveRates; }
      set {
        if(_DoLogSaveRates != value) {
          _DoLogSaveRates = value;
          OnPropertyChanged("DoLogSaveRates");
        }
      }
    }

    #endregion
    bool _loadHistoryRealTime;
    [Category(categoryXXX)]
    [IsNotStrategy]
    [Dnr]
    public bool LoadHistoryRealTime {
      get {
        return _loadHistoryRealTime;
      }

      set {
        _loadHistoryRealTime = value;
        OnPropertyChanged(() => LoadHistoryRealTime);
      }
    }

    //RatesHeightMinimum
    public string _TestRatesHeightMinimum = "";
    [DisplayName("RatesHeightMinimum")]
    [Category(categoryTest)]
    public string TestRatesHeightMinimum {
      get { return _TestRatesHeightMinimum; }
      set {
        if(_TestRatesHeightMinimum != value) {
          _TestRatesHeightMinimum = value;
          OnPropertyChanged("TestRatesHeightMinimum");
        }
      }
    }

    double _testMinimumBalancePerc = -.7;
    [Category(categoryTestControl)]
    public double TestMinimumBalancePerc {
      get { return _testMinimumBalancePerc; }
      set {
        _testMinimumBalancePerc = value;
        OnPropertyChanged("TestMinimumBalancePerc");
      }
    }

    public string _TestFileName = "";
    [DisplayName("Test File Name")]
    [Category(categoryTestControl)]
    [Dnr]
    public string TestFileName {
      get { return _TestFileName; }
      set {
        if(_TestFileName != value) {
          _TestFileName = value;
          OnPropertyChanged("TestFileName");
        }
      }
    }
    string _closedSession;
    [DisplayName("Closed Session")]
    [Category(categoryTestControl)]
    [Dnr]
    public string TestClosedSession {
      get { return _closedSession; }
      set {
        _closedSession = value;
        OnPropertyChanged(() => TestClosedSession);
      }
    }

    string _prevSession;
    [DisplayName("Previous Session")]
    [Category(categoryTestControl)]
    [Dnr]
    public string TestPrevSession {
      get { return _prevSession; }
      set {
        _prevSession = value;
        OnPropertyChanged(() => TestPrevSession);
      }
    }
    int _testBalance = 50000;
    [DisplayName("Starting Balance")]
    [Category(categoryTestControl)]
    [Dnr]
    public int TestBalance {
      get { return _testBalance; }
      set {
        _testBalance = value;
        OnPropertyChanged(() => TestBalance);
      }
    }

    public string _TestBarsCount = "";
    [DisplayName("BarsCount")]
    [Category(categoryTest)]
    public string TestBarsCount {
      get { return _TestBarsCount; }
      set {
        if(_TestBarsCount != value) {
          _TestBarsCount = value;
          OnPropertyChanged("TestBarsCount");
        }
      }
    }

    public string _TestCorrelationMinimum = "";
    [DisplayName("CorrelationMinimum")]
    [Category(categoryTest)]
    public string TestCorrelationMinimum {
      get { return _TestCorrelationMinimum; }
      set {
        if(_TestCorrelationMinimum != value) {
          _TestCorrelationMinimum = value;
          OnPropertyChanged("TestCorrelationMinimum");
        }
      }
    }


    string _TestCorridorDistanceRatio = "";
    [DisplayName("CorridorDistanceRatio")]
    [Category(categoryTest)]
    public string TestCorridorDistanceRatio {
      get { return _TestCorridorDistanceRatio; }
      set {
        if(_TestCorridorDistanceRatio != value) {
          _TestCorridorDistanceRatio = value;
          OnPropertyChanged("TestCorridorDistanceRatio");
        }
      }
    }

    public string _TestWaveStDevRatio = "";
    [DisplayName("Wave StDev Ratio")]
    [Category(categoryTest)]
    public string TestWaveStDevRatio {
      get { return _TestWaveStDevRatio; }
      set {
        if(_TestWaveStDevRatio != value) {
          _TestWaveStDevRatio = value;
          OnPropertyChanged("TestWaveStDevRatio");
        }
      }
    }

    public string _TestDistanceIterations = "";
    [DisplayName("Distance Iterations")]
    [Category(categoryTest)]
    public string TestDistanceIterations {
      get { return _TestDistanceIterations; }
      set {
        if(_TestDistanceIterations != value) {
          _TestDistanceIterations = value;
          OnPropertyChanged("TestDistanceIterations");
        }
      }
    }

    public bool _UseTestFile = true;
    [Category(categoryTestControl)]
    [Dnr]
    public bool UseTestFile {
      get { return _UseTestFile; }
      set {
        _UseTestFile = value;
        OnPropertyChanged("UseTestFile");
      }
    }
    [Category(categoryTestControl)]
    [DisplayName("Use Super Session")]
    public bool TestUseSuperSession { get; set; }

    string _TestPriceCmaLevels = "";
    [DisplayName("Price CMA Levels")]
    [Category(categoryTest)]
    public string TestPriceCmaLevels {
      get { return _TestPriceCmaLevels; }
      set {
        if(_TestPriceCmaLevels != value) {
          _TestPriceCmaLevels = value;
          OnPropertyChanged("TestPriceCmaLevels");
        }
      }
    }
    #region TestSuperSessionUid

    private Guid _TestSuperSessionUid;
    public Guid TestSuperSessionUid { get { return _TestSuperSessionUid; } }
    [DisplayName("SuperSession Uid")]
    [Category(categoryTestControl)]
    public string TestSuperSessionUid_ {
      get {
        return _TestSuperSessionUid.ToString().ToUpper();
      }
      set {
        Guid v = Guid.Empty;
        if(string.IsNullOrWhiteSpace(value))
          v = Guid.NewGuid();
        else if(value == "0")
          v = 0.Guid();
        else
          Guid.TryParse(value, out v);
        if(_TestSuperSessionUid != v) {
          _TestSuperSessionUid = v;
          OnPropertyChanged("TestSuperSessionUid_");
          OnPropertyChanged("TestSuperSessionUid");
        }
      }
    }

    #endregion

    string _TestProfitToLossExitRatio = "";
    [DisplayName("ProfitToLossExitRatio")]
    [Category(categoryTest)]
    public string TestProfitToLossExitRatio {
      get { return _TestProfitToLossExitRatio; }
      set {
        if(_TestProfitToLossExitRatio != value) {
          _TestProfitToLossExitRatio = value;
          OnPropertyChanged("TestProfitToLossExitRatio");
        }
      }
    }

    #region TurnOffOnProfit
    private bool _TurnOffOnProfit;
    [Category(categoryTrading)]
    public bool TurnOffOnProfit {
      get { return _TurnOffOnProfit; }
      set {
        if(_TurnOffOnProfit != value) {
          _TurnOffOnProfit = value;
          OnPropertyChanged("TurnOffOnProfit");
        }
      }
    }

    #endregion
    #region DoNews
    private bool _DoNews = true;
    [Category(categoryTrading)]
    public bool DoNews {
      get { return _DoNews; }
      set {
        if(_DoNews != value) {
          _DoNews = value;
          OnPropertyChanged("DoNews");
        }
      }
    }

    #endregion

    #region MovingAverageType
    int _movingAverageTypeDefault = 0;
    [DisplayName("Moving Avg By")]
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsCorridorFuncs)]
    public MovingAverageType MovingAverageType {
      get { return (MovingAverageType)MovingAverageTypeInt.GetValueOrDefault(_movingAverageTypeDefault); }
      set {
        if(_MovingAverageTypeInt.GetValueOrDefault(_movingAverageTypeDefault) != (int)value) {
          MovingAverageTypeInt = (int)value;
          OnPropertyChanged(nameof(MovingAverageType));
        }
      }
    }

    #endregion

    #region BuyLevelBy
    private TradeLevelBy _LevelBuyBy;
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTradingCorridor)]
    public TradeLevelBy LevelBuyBy {
      get { return _LevelBuyBy; }
      set {
        if(_LevelBuyBy != value) {
          _LevelBuyBy = value;
          OnPropertyChanged("LevelBuyBy");
        }
      }
    }
    #endregion

    #region LevelBuyCloseBy
    private TradeLevelBy _LevelBuyCloseBy;
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTradingCorridor)]
    public TradeLevelBy LevelBuyCloseBy {
      get { return _LevelBuyCloseBy; }
      set {
        if(_LevelBuyCloseBy != value) {
          _LevelBuyCloseBy = value;
          OnPropertyChanged("LevelBuyCloseBy");
        }
      }
    }
    #endregion
    #region LevelSellCloseBy
    private TradeLevelBy _LevelSellCloseBy;
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTradingCorridor)]
    public TradeLevelBy LevelSellCloseBy {
      get { return _LevelSellCloseBy; }
      set {
        if(_LevelSellCloseBy != value) {
          _LevelSellCloseBy = value;
          OnPropertyChanged("LevelSellCloseBy");
        }
      }
    }

    #endregion

    #region LevelSellBy
    private TradeLevelBy _LevelSellBy;
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTradingCorridor)]
    public TradeLevelBy LevelSellBy {
      get { return _LevelSellBy; }
      set {
        if(_LevelSellBy != value) {
          _LevelSellBy = value;
          OnPropertyChanged("LevelSellBy");
        }
      }
    }
    #endregion

    #region CorridorByStDevRatioFunc
    private CorridorByStDevRatio _CorridorByStDevRatioFunc;
    [Category(categoryActiveFuncs)]
    public CorridorByStDevRatio CorridorByStDevRatioFunc {
      get { return _CorridorByStDevRatioFunc; }
      set {
        if(_CorridorByStDevRatioFunc != value) {
          _CorridorByStDevRatioFunc = value;
          OnPropertyChanged("CorridorByStDevRatioFunc");
        }
      }
    }
    private CorridorByStDevRatio _CorridorByStDevRatioFunc2;
    [Category(categoryActiveFuncs)]
    public CorridorByStDevRatio CorridorByStDevRatioFunc2 {
      get { return _CorridorByStDevRatioFunc2; }
      set {
        if(_CorridorByStDevRatioFunc2 != value) {
          _CorridorByStDevRatioFunc2 = value;
          OnPropertyChanged("CorridorByStDevRatioFunc2");
        }
      }
    }
    #endregion

    #region UseVoltage
    private bool _UseVoltage;
    [Category(categoryActiveYesNo)]
    public bool UseVoltage {
      get { return _UseVoltage; }
      set {
        if(_UseVoltage != value) {
          _UseVoltage = value;
          OnPropertyChanged("UseVoltage");
        }
      }
    }
    #endregion

    #region LogTrades
    bool _logTrades = true;
    [DisplayName("Log Trades")]
    [Category(categoryTrading)]
    [Dnr]
    public bool LogTrades {
      get { return _logTrades; }
      set {
        _logTrades = value;
        OnPropertyChanged(() => LogTrades);
      }
    }

    #endregion

    #region ForceOpenTrade
    private bool? _ForceOpenTrade;
    [DisplayName("Force Open Trade")]
    [Category(categoryXXX)]
    public bool? ForceOpenTrade {
      get { return _ForceOpenTrade; }
      set {
        if(_ForceOpenTrade != value) {
          _ForceOpenTrade = value;
          OnPropertyChanged(nameof(ForceOpenTrade));
        }
      }
    }

    #endregion



    #region CorridorCrossHighLowMethod
    [DisplayName("High/Low(Cross)Method")]
    [Category(categoryCorridor)]
    public CorridorHighLowMethod CorridorCrossHighLowMethod {
      get { return (CorridorHighLowMethod)CorridorCrossHighLowMethodInt; }
      set {
        if(CorridorCrossHighLowMethodInt != (int)value) {
          CorridorCrossHighLowMethodInt = (int)value;
          OnPropertyChanged(nameof(CorridorCrossHighLowMethod));
        }
      }
    }

    #endregion

    #region CorridorLengthMinimum
    [DisplayName("Length Min")]
    [Category(categoryCorridor)]
    public double CorridorLengthMinimum_ {
      get { return CorridorLengthMinimum; }
      set {
        if(CorridorLengthMinimum != value) {
          CorridorLengthMinimum = value;
          OnPropertyChanged(nameof(CorridorLengthMinimum_));
        }
      }
    }

    #endregion

    [DisplayName("Corridor H/L Method")]
    [Description("Method for price above/below regression line")]
    [Category(categoryActiveFuncs)]
    public CorridorHighLowMethod CorridorHighLowMethod {
      get { return (CorridorHighLowMethod)CorridorHighLowMethodInt; }
      set {
        CorridorHighLowMethodInt = (int)value;
        OnPropertyChanged(nameof(CorridorHighLowMethod));
      }
    }

    VoltageFunction _voltageFunction;
    [DisplayName("Voltage Function")]
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsCorridorFuncs)]
    public VoltageFunction VoltageFunction {
      get { return _voltageFunction; }
      set {
        if(_voltageFunction != value) {
          _voltageFunction = value;
          ResetVoltages();
          OnPropertyChanged("VoltageFunction");
        }
      }
    }

    #region VoltageFunction2
    private VoltageFunction _voltageFunction2;
    [DisplayName("Voltage Function 2")]
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsCorridorFuncs)]
    public VoltageFunction VoltageFunction2 {
      get { return _voltageFunction2; }
      set {
        if(_voltageFunction2 != value) {
          _voltageFunction2 = value;
          OnPropertyChanged("VoltageFunction2");
        }
      }
    }

    #endregion
    private void ResetVoltages() {
      UseRatesInternal(rates => rates.ForEach(r => {
        SetVoltage(r, double.NaN);
        SetVoltage2(r, double.NaN);
      }));
    }

    ScanCorridorFunction _ScanCorridorBy;
    [DisplayName("Scan Corridor By")]
    [Category(categoryActiveFuncs)]
    [Description("ScanCorridor By")]
    [WwwSetting(wwwSettingsCorridorFuncs)]
    public ScanCorridorFunction ScanCorridorBy {
      get { return _ScanCorridorBy; }
      set {
        if(_ScanCorridorBy != value) {
          _ScanCorridorBy = value;
          OnPropertyChanged("ScanCorridorBy");
        }
      }
    }

    #region RatesLenghBy
    private RatesLengthFunction _RatesLengthBy = RatesLengthFunction.TimeFrame;
    [DisplayName("Rates Length By")]
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsCorridorFuncs)]
    public RatesLengthFunction RatesLengthBy {
      get { return _RatesLengthBy; }
      set {
        if(_RatesLengthBy != value) {
          _RatesLengthBy = value;
          if(IsLoaded) {
            BarsCountCalc = BarCountSmoothed = 0;
            Task.Delay(1000).ContinueWith(t => IsRatesLengthStable = _RatesLengthBy == RatesLengthFunction.None);
          }
          OnPropertyChanged(nameof(RatesLengthBy));
        }
      }
    }

    #endregion

    [DisplayName("Trailing Distance")]
    [Category(categoryActiveFuncs)]
    [Description("TrailingDistanceFunction")]
    public TrailingWaveMethod TrailingDistanceFunction {
      get { return (TrailingWaveMethod)this.FreezLimit; }
      set {
        if(this.FreezLimit != (int)value) {
          this.FreezLimit = (int)value;
          OnPropertyChanged("TrailingDistanceFunction");
        }
      }
    }

    [DisplayName("Trading Distance")]
    [Category(categoryActiveFuncs)]
    [Description("TradingDistanceFunction")]
    [WwwSetting(Group = wwwSettingsTradingConditions)]
    public TradingMacroTakeProfitFunction TradingDistanceFunction {
      get { return (TradingMacroTakeProfitFunction)PowerRowOffset; }
      set {
        PowerRowOffset = (int)value;
        OnPropertyChanged(nameof(TradingDistanceFunction));
      }
    }

    TradingMacroTakeProfitFunction _TakeProfitFunction = default(TradingMacroTakeProfitFunction);
    [DisplayName("Take Profit")]
    [Category(categoryActiveFuncs)]
    [Description("TakeProfitFunction")]
    [WwwSetting(Group = wwwSettingsTrading)]
    public TradingMacroTakeProfitFunction TakeProfitFunction {
      get {  return _TakeProfitFunction; }
      set {
        if(TakeProfitFunction == value)
          return;
        _TakeProfitFunction = value;
          OnPropertyChanged(nameof(TakeProfitFunction));
      }
    }

    [DisplayName("Show Trade On Chart")]
    [Category(categoryCorridor)]
    public bool DoShowTradeOnChart {
      get { return TradeOnCrossOnly; }
      set {
        if(TradeOnCrossOnly == value)
          return;
        TradeOnCrossOnly = value;
        OnPropertyChanged(() => DoShowTradeOnChart);
      }
    }


    GannAngles _GannAnglesList;
    public GannAngles GannAnglesList {
      get {
        if(_GannAnglesList == null) {
          _GannAnglesList = new GannAngles(GannAngles);
          _GannAnglesList.PropertyChanged += (o, p) => {
            GannAngles = o.ToString();
          };
        }
        return _GannAnglesList;
      }
    }


    [DisplayName("Streatch Rates")]
    [Description("Streatch Rates to Corridor")]
    [Category(categoryXXX)]
    public bool DoStreatchRates_ {
      get { return DoStreatchRates; }
      set { DoStreatchRates = value; }
    }
    [Description("RatesArray.Height() > PrevHeightAvg")]
    [DisplayName("Use Prev Height")]
    [Category(categoryActiveYesNo)]
    public bool UsePrevHeight {
      get { return StrictTradeClose; }
      set {
        if(StrictTradeClose == value)
          return;
        StrictTradeClose = value;
        OnPropertyChanged(() => UsePrevHeight);
      }
    }
    partial void OnStrictTradeCloseChanged() {
      OnPropertyChanged(() => UsePrevHeight);
    }

    private bool IsTradingDay(DateTime time) {
      return TradingDays().Contains(time.DayOfWeek);
    }
    private bool IsTradingHour(DateTime time) {
      return IsTradingHour2(TradingHoursRange, time);
    }
    public static bool IsTradingHour(string range, DateTime time) {
      var times = range.Split('-')
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .DefaultIfEmpty("0:00").Select(s => DateTime.Parse(s).TimeOfDay).ToArray();
      var tod = time.TimeOfDay;
      return IsTimeSpanRangeOk(times, tod);
    }

    private static bool IsTimeSpanRangeOk(TimeSpan[] times, TimeSpan tod) {
      return !IsTimeRangeReversed(times)
        ? tod.Between(times.First(), times.Last())
        : tod >= times.First() || tod <= times.Last();
    }

    private static bool IsTimeRangeReversed(TimeSpan[] times) {
      return times.First() > times.Last();
    }

    public static bool IsTradingHour2(string range, DateTime time) {
      string[][] ranges;
      if(range.TryFromJson(out ranges)) {
        if(ranges == null)
          return true;
        var timeSpans = ranges.Select(r => r.Select(t => TimeSpan.Parse(t)).ToArray());
        var ands = timeSpans.Where(tsr => !IsTimeRangeReversed(tsr)).Select(ts => IsTimeSpanRangeOk(ts, time.TimeOfDay)).DefaultIfEmpty(true).Any(b=>b);
        var ors = timeSpans.Where(tsr => IsTimeRangeReversed(tsr)).All(ts => IsTimeSpanRangeOk(ts, time.TimeOfDay));
        return ands && ors;
      }
      return IsTradingHour(range,time);
    }
    DayOfWeek[] TradingDays() {
      switch(TradingDaysRange) {
        case WeekDays.Full:
          return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        case WeekDays.MoTh:
          return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        case WeekDays.MoTuFr:
          return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        case WeekDays.TuFr:
          return new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        case WeekDays.TuTh:
          return new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        case WeekDays.SuFr:
          return new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        case WeekDays.SuTh:
          return new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
      }
      throw new NotImplementedException(new { TradingDaysRange } + "");
    }

    public enum WeekDays {
      Full = DayOfWeek.Monday + DayOfWeek.Tuesday + DayOfWeek.Wednesday + DayOfWeek.Thursday + DayOfWeek.Friday,
      MoTh = DayOfWeek.Monday + DayOfWeek.Tuesday + DayOfWeek.Wednesday + DayOfWeek.Thursday,
      MoTuFr = DayOfWeek.Monday + DayOfWeek.Tuesday + DayOfWeek.Friday,
      TuFr = DayOfWeek.Tuesday + DayOfWeek.Wednesday + DayOfWeek.Thursday + DayOfWeek.Friday,
      TuTh = DayOfWeek.Tuesday + DayOfWeek.Wednesday + DayOfWeek.Thursday,
      SuFr = DayOfWeek.Monday + DayOfWeek.Tuesday + DayOfWeek.Wednesday + DayOfWeek.Thursday + DayOfWeek.Friday + DayOfWeek.Saturday,
      SuTh = DayOfWeek.Monday + DayOfWeek.Tuesday + DayOfWeek.Wednesday + DayOfWeek.Thursday + DayOfWeek.Saturday
    }
    [DisplayName("Trading Hours")]
    [Description("21:00-5:00")]
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsTradingParams)]
    public string TradingHoursRange {
      get { return CorridorIterations; }
      set {
        if(CorridorIterations == value)
          return;
        CorridorIterations = value;
        OnPropertyChanged(() => TradingHoursRange);
      }
    }

    [DisplayName("Trading Days")]
    [Category(categoryActiveFuncs)]
    public WeekDays TradingDaysRange {
      get { return (WeekDays)CorridorRatioForRange; }
      set {
        if(CorridorRatioForRange == (int)value)
          return;
        CorridorRatioForRange = (int)value;
        OnPropertyChanged(() => TradingDaysRange);
      }
    }

    [DisplayName("SuppRes Levels Count")]
    [Category(categoryCorridor)]
    public int SuppResLevelsCount_ {
      get { return SuppResLevelsCount; }
      set {
        if(SuppResLevelsCount == value)
          return;
        SuppResLevelsCount = value;
        OnPropertyChanged(nameof(SuppResLevelsCount_));
      }
    }

    [DisplayName("Correlation Min")]
    [Category(categoryCorridor)]
    public double CorrelationMinimum {
      get { return StDevToSpreadRatio; }
      set {
        if(StDevToSpreadRatio != value) {
          StDevToSpreadRatio = value;
          OnPropertyChanged(() => CorrelationMinimum);
        }
      }
    }

    [DisplayName("CorridorDistanceRatio")]
    [Category(categoryActive)]
    [Description("X > 1 ? X : BarsCount * CorridorDistanceRatio")]
    public double CorridorDistanceRatio {
      get { return CorridorStDevRatioMax; }
      set {
        if(CorridorStDevRatioMax != value) {
          CorridorStDevRatioMax = value;
          OnPropertyChanged(() => CorridorDistanceRatio);
          OnPropertyChanged(() => CorridorDistance);
        }
      }
    }

    [DisplayName("ExtreamCloseOffset")]
    [Category(categoryXXX_NU)]
    public int ExtreamCloseOffset_ {
      get { return ExtreamCloseOffset; }
      set {
        if(ExtreamCloseOffset != value) {
          ExtreamCloseOffset = value;
          OnPropertyChanged("ExtreamCloseOffset");
        }
      }
    }

    [Category(categoryActive)]
    [Description("CanTradeLocal Ratio")]
    public double CanTradeLocalRatio {
      get { return CurrentLossInPipsCloseAdjustment; }
      set {
        if(CurrentLossInPipsCloseAdjustment != value) {
          CurrentLossInPipsCloseAdjustment = value;
          OnPropertyChanged("CanTradeLocalRatio");
        }
      }
    }

    #region IsTakeBack
    private bool _IsTakeBack;
    [WwwSetting(wwwSettingsTradingProfit)]
    [Category(categoryActiveYesNo)]
    [Description("Set exit level to no-loss.")]
    public bool IsTakeBack {
      get { return _IsTakeBack; }
      set {
        if(_IsTakeBack != value) {
          if(value) {
            UseLastLoss = false;
          }
          _IsTakeBack = value;
          OnPropertyChanged("IsTakeBack");
        }
      }
    }

    #endregion

    [Category(categoryXXX_NU)]
    public double CorridorBigToSmallRatio_ {
      get { return CorridorBigToSmallRatio; }
      set {
        if(CorridorBigToSmallRatio != value) {
          CorridorBigToSmallRatio = value;
          OnPropertyChanged("CorridorBigToSmallRatio_");
        }
      }
    }

    [DisplayName("Streatch TakeProfit")]
    [Category(categoryTrading)]
    [Description("Ex: ExitLevel = tradeLevel + TakeProfit")]
    public bool StreatchTakeProfit {
      get { return StreachTradingDistance; }
      set {
        StreachTradingDistance = value;
        OnPropertyChanged(nameof(StreatchTakeProfit));
      }
    }

    [DisplayName("Close On Open Only")]
    [Category(categoryXXX)]
    [Description("Close position only when opposite opens.")]
    public bool CloseOnOpen_ {
      get { return CloseOnOpen; }
      set {
        CloseOnOpen = value;
        OnPropertyChanged(nameof(CloseOnOpen_));
      }
    }

    bool _ExitByBuySellLevel=true;
    [DisplayName("Exit By BuySell Level")]
    [Category(categoryActiveYesNo)]
    [Description("(X ? _buySell:eve.Rate : trade.Open) + takeProfit ")]
    [WwwSetting(wwwSettingsTradingProfit)]
    public bool ExitByBuySellLevel {
      get { return _ExitByBuySellLevel; }
      set {
        if(_ExitByBuySellLevel != value) {
          _ExitByBuySellLevel = value;
          OnPropertyChanged(() => ExitByBuySellLevel);
        }
      }
    }

    [DisplayName("Close On Profit Only")]
    [Category(categoryXXX_NU)]
    [Description("Ex: if( PL > Limit) OpenTrade()")]
    public bool CloseOnProfitOnly_ {
      get { return CloseOnProfitOnly; }
      set {
        if(CloseOnProfitOnly == value)
          return;
        CloseOnProfitOnly = value;
        OnPropertyChanged(nameof(CloseOnProfitOnly_));
      }
    }

    [DisplayName("Adjust Exit By Time")]
    [Category(categoryActiveYesNo)]
    [Description("Adjust exit level according to price movements since Trade started.")]
    [WwwSetting(wwwSettingsTrading)]
    public bool DoAdjustExitLevelByTradeTime {
      get { return CloseAllOnProfit; }
      set {
        if(CloseAllOnProfit == value)
          return;
        CloseAllOnProfit = value;
        IsTakeBack = false;
        this.UseLastLoss = false;
        Log = new Exception(new { IsTakeBack, UseLastLoss } + "");
        OnPropertyChanged("DoAdjustExitLevelByTradeTime");
      }
    }

    const string categoryXXX = "XXX";
    const string categoryXXX_NU = "XXX_NU";
    public const string categoryCorridor = "Corridor";
    public const string categoryTrading = "Trading";
    public const string categoryActive = "Active";
    public const string categoryActiveYesNo = "Active Yes or No";
    public const string categoryActiveFuncs = "Active Funcs";
    public const string categoryTest = "Test";
    public const string categoryTestControl = "Test Control";
    public const string categorySession = "Session";

    public const string wwwSettingsTrading = "1.0 Trading";
    public const string wwwSettingsTradingCorridor = "1.1 Trading Corridor";
    public const string wwwSettingsTradingProfit = "1.2 Trading Profit";
    public const string wwwSettingsTradingConditions = "1.3 Trading Conditions";
    public const string wwwSettingsTradingParams = "1.4 Trading Params";

    public const string wwwSettingsTrends = "2.0 Corridor Trends";
    public const string wwwSettingsCorridorAngles = "2.1 Corridor Angles";
    public const string wwwSettingsCorridorCMA = "2.2 Corridor CMA";
    public const string wwwSettingsCorridorOther = "2.3 Corridor";
    public const string wwwSettingsCorridorEquinox = "hide 2.4 Equinox";
    public const string wwwSettingsAO = "hide 2.5 AO Periods";
    public const string wwwSettingsCorridorFuncs = "3 Corridor Func";
    public const string wwwSettingsVoltage = "4 Voltage";
    public const string wwwSettingsBars = "5 Bars";

    #region CloseAfterTradingHours
    private bool _CloseAfterTradingHours;
    [Category(categoryActiveYesNo)]
    public bool CloseAfterTradingHours {
      get { return _CloseAfterTradingHours; }
      set {
        if(_CloseAfterTradingHours != value) {
          _CloseAfterTradingHours = value;
          OnPropertyChanged("CloseAfterTradingHours");
          SaveActiveSettings();
        }
      }
    }

    #endregion

    bool _IsContinuousTrading;
    [WwwSetting(Group = wwwSettingsTradingConditions)]
    [Category(categoryActive)]
    [Description("buySellLevels.CanTrade can be set even when Trades.Count > 0")]
    public bool IsContinuousTrading {
      get { return _IsContinuousTrading; }
      set {
        _IsContinuousTrading = value;
        OnPropertyChanged(nameof(IsContinuousTrading));
      }
    }

    int _TradeCountMax= 0;
    [WwwSetting(Group = wwwSettingsTradingConditions)]
    [Category(categoryActive)]
    [Description("_buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum")]
    public int TradeCountMax {
      get { return _TradeCountMax; }
      set {
        _TradeCountMax = value;
        OnPropertyChanged("TradeCountMax");
      }
    }

    [DisplayName("CorridorHeight Max")]
    [Category(categoryXXX)]
    public double CorridorHeightMax {
      get { return FibMin; }
      set {
        if(FibMin == value)
          return;
        FibMin = value;
        OnPropertyChanged("CorridorHeightMax");
      }
    }

    [Category(categoryActiveYesNo)]
    [DisplayName("Corr.Forward Only")]
    [Description("IsCorridorForwardOnly")]
    public bool IsCorridorForwardOnly {
      get { return ReversePower; }
      set {
        if(ReversePower == value)
          return;
        ReversePower = value;
        OnPropertyChanged(() => IsCorridorForwardOnly);
      }
    }

    [Category(categoryActive)]
    [DisplayName("TakeProfit Limit Ratio")]
    [Description("Ex:Exit <= TakeProfit * X")]
    public double TakeProfitLimitRatio {
      get {
        if(RangeRatioForTradeStop < 1) {
          RangeRatioForTradeStop = 1;
          Log = new Exception("TakeProfitLimitRatio is less then 1. It is being set to 1.".Formater(TakeProfitLimitRatio));
          OnPropertyChanged(() => TakeProfitLimitRatio);
        }
        return RangeRatioForTradeStop;
      }
      set {
        if(RangeRatioForTradeStop == value)
          return;
        RangeRatioForTradeStop = value;
        OnPropertyChanged(() => TakeProfitLimitRatio);
      }
    }

    [Category(categoryActiveYesNo)]
    [DisplayName("Trading Ratio By PMC")]
    [WwwSetting(wwwSettingsTradingConditions)]
    public bool TradingRatioByPMC { get; set; }

    [Category(categoryXXX_NU)]
    [DisplayName("Trade On BOW")]
    [Description("If ExitOnFriday, consider this as well")]
    [WwwSetting(wwwSettingsTradingConditions)]
    public bool TradeOnBOW {
      get { return TradeByFirstWave.GetValueOrDefault(); }
      set {
        if(TradeByFirstWave == value)
          return;
        TradeByFirstWave = value;
        OnPropertyChanged(() => TradeOnBOW);
      }
    }

    //[DisplayName("Gann Angles Offset in Rads")]
    //[Category(categoryCorridor)]
    public double GannAnglesOffset_ {
      get { return GannAnglesOffset.GetValueOrDefault(); }
      set { GannAnglesOffset = value; }
    }

    //[DisplayName("Gann Angles")]
    //[Category(categoryCorridor)]
    public string GannAngles_ {
      get { return GannAngles; }
      set { GannAngles = value; }
    }


    [DisplayName("Chart High Method")]
    [Category(categoryCorridor)]
    public ChartHighLowMethod ChartHighMethod {
      get { return (ChartHighLowMethod)LongMAPeriod; }
      set {
        if(LongMAPeriod == (int)value)
          return;
        LongMAPeriod = (int)value;
        OnPropertyChanged(() => ChartHighMethod);
      }
    }

    [Category(categoryCorridor)]
    [DisplayName("Chart Low Method")]
    public ChartHighLowMethod ChartLowMethod {
      get { return (ChartHighLowMethod)RangeRatioForTradeLimit; }
      set {
        RangeRatioForTradeLimit = (double)value;
        OnPropertyChanged(() => ChartLowMethod);
      }
    }

    [DisplayName("Trading Angle Range")]
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public double TradingAngleRange_ {
      get { return TradingAngleRange; }
      set {
        if(TradingAngleRange == value)
          return;
        TradingAngleRange = value;
        OnPropertyChanged(() => TradingAngleRange_);
      }
    }

    double _trendAngleGreen;
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public double TrendAngleGreen {
      get { return _trendAngleGreen; }
      set {
        if(_trendAngleGreen == value)
          return;
        _trendAngleGreen = value;
        OnPropertyChanged(() => TrendAngleGreen);
      }
    }

    double _trendAngleLime;
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public double TrendAngleLime {
      get { return _trendAngleLime; }
      set {
        if(_trendAngleLime == value)
          return;
        _trendAngleLime = value;
        OnPropertyChanged(() => TrendAngleLime);
      }
    }

    double _trendAngleRed;
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public double TrendAngleRed {
      get { return _trendAngleRed; }
      set {
        if(_trendAngleRed == value)
          return;
        _trendAngleRed = value;
        OnPropertyChanged(() => TrendAngleRed);
      }
    }

    double _trendAnglePlum;
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public double TrendAnglePlum {
      get { return _trendAnglePlum; }
      set {
        if(_trendAnglePlum == value)
          return;
        _trendAnglePlum = value;
        OnPropertyChanged(() => TrendAnglePlum);
      }
    }

    public double TrendAngleLast0 { get; set; }
    public double TrendAngleLast1 { get; set; }
    string _trendAngleLast = "";
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    [Description("Range or single value: 15-30 or 60 or -60")]
    public string TrendAngleLast {
      get { return _trendAngleLast; }
      set {
        if(_trendAngleLast == value)
          return;
        var spans = ParseJsonRange<double>(_trendAngleLast = value?.Trim() ?? "");
        TrendAngleLast0 = spans[0];
        TrendAngleLast1 = spans.Skip(1).DefaultIfEmpty(double.NaN).Last();
        OnPropertyChanged(nameof(TrendAngleLast));
      }
    }
    public double TrendAnglePrev0 { get; set; }
    public double TrendAnglePrev1 { get; set; }
    string _trendAnglePrev = "";
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    [Description("Range or single value: 15-30 or 60 or -60")]
    public string TrendAnglePrev {
      get { return _trendAnglePrev; }
      set {
        if(_trendAnglePrev == value)
          return;
        var spans = ParseJsonRange<double>(_trendAnglePrev = value.Trim());
        TrendAnglePrev0 = spans[0];
        TrendAnglePrev1 = spans.Skip(1).DefaultIfEmpty(double.NaN).Last();
        OnPropertyChanged(nameof(TrendAnglePrev));
      }
    }
    public double TrendAnglePrev20 { get; set; }
    public double TrendAnglePrev21 { get; set; }
    string _trendAnglePrev2 = "";
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    [Description("Range or single value: 15-30 or 60 or -60")]
    public string TrendAnglePrev2 {
      get { return _trendAnglePrev2; }
      set {
        if(_trendAnglePrev2 == value)
          return;
        var spans = ParseJsonRange<double>(_trendAnglePrev2 = value.Trim());
        TrendAnglePrev20 = spans[0];
        TrendAnglePrev21 = spans.Skip(1).DefaultIfEmpty(double.NaN).Last();
        OnPropertyChanged(nameof(TrendAnglePrev2));
      }
    }


    public double TrendAngleBlue0 { get; set; }
    public double TrendAngleBlue1 { get; set; }
    string _trendAngleBlue = "";
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    [Description("Range or single value: 15-30 or 60 or -60")]
    public string TrendAngleBlue {
      get { return _trendAngleBlue; }
      set {
        if(_trendAngleBlue == value)
          return;
        var spans = ParseJsonRange<double>(_trendAngleBlue = value.Trim());
        TrendAngleBlue0 = spans[0];
        TrendAngleBlue1 = spans.Skip(1).DefaultIfEmpty(double.NaN).Last();
        OnPropertyChanged(nameof(TrendAngleBlue));
      }
    }

    public double VoltRange0 { get; set; }
    public double VoltRange1 { get; set; }
    string _VoltRange = "[0]";
    [WwwSetting(Group = wwwSettingsVoltage)]
    [Category(categoryActive)]
    [Description("Range or single value: 15-30 or 60 or -60")]
    public string VoltRange {
      get { return _VoltRange; }
      set {
        if(_VoltRange == value)
          return;
        var spans = ParseJsonRange<double>(_VoltRange = (value ?? "").Trim());
        VoltRange0 = spans[0];
        VoltRange1 = spans.Concat(new[] { double.NaN }).Take(2).Last();
        OnPropertyChanged(nameof(VoltRange));
      }
    }

    public static T[] ParseJsonRange<T>(string range) {
      T[] spans = null;
      var ranges = new[] { range, $"[{range}]" };
      if(!ranges.Any(r => r.TryFromJson(out spans)))
        throw new Exception(new { range, Is = "Not Json" } + "");

      if((spans?.IsEmpty()).GetValueOrDefault(true))
        spans = new[] { default(T) };
      return spans;
    }

    public double VoltRange_20 { get; set; }
    public double VoltRange_21 { get; set; }
    string _VoltRange_2 = "[0]";
    [WwwSetting(Group = wwwSettingsVoltage)]
    [Category(categoryActive)]
    [Description("Range or single value: 15-30 or 60 or -60")]
    public string VoltRange_2 {
      get { return _VoltRange_2; }
      set {
        if(_VoltRange_2 == value)
          return;
        var spans = ParseJsonRange<double>(_VoltRange_2 = (value ?? "").Trim());
        VoltRange_20 = spans[0];
        VoltRange_21 = spans.Concat(new[] { double.NaN }).Take(2).Last();

        OnPropertyChanged(nameof(VoltRange_2));
      }
    }


    #region VoltAvgRange
    private double _VoltAvgRange;
    [WwwSetting(Group = wwwSettingsVoltage)]
    [Category(categoryActive)]
    public double VoltAvgRange {
      get { return _VoltAvgRange; }
      set {
        if(_VoltAvgRange != value) {
          _VoltAvgRange = value;
          OnPropertyChanged(nameof(VoltAvgRange));
        }
      }
    }

    #endregion
    #region VoltAvgIter
    private int _VoltAverageIterations = 2;
    [Description("Volt Avg Iter")]
    [WwwSetting(Group = wwwSettingsVoltage)]
    [Category(categoryActive)]
    public int VoltAverageIterations {
      get { return _VoltAverageIterations; }
      set {
        if(_VoltAverageIterations != value) {
          _VoltAverageIterations = value;
          OnPropertyChanged(nameof(VoltsAvgIterations));
        }
      }
    }

    #endregion

    double _waveStDevPower = 10;
    [Description("wrs.Select(w => w.StDev).PowerMeanPowerByPosition(X)")]
    [WwwSetting(Group = wwwSettingsTradingParams)]
    [Category(categoryActive)]
    public double WaveStDevPower {
      get { return _waveStDevPower; }
      set {
        if(_waveStDevPower == value)
          return;
        _waveStDevPower = value;
        OnPropertyChanged(() => WaveStDevPower);
      }
    }
    double _waveStDevPowerS = 0.5;
    [Description("wrs.Select(w => w.StDev).PowerMeanPowerByPosition(X)")]
    [WwwSetting(Group = wwwSettingsTradingParams)]
    [Category(categoryActive)]
    public double WaveStDevPowerS {
      get { return _waveStDevPowerS; }
      set {
        if(_waveStDevPowerS == value)
          return;
        _waveStDevPowerS = value;
        OnPropertyChanged(() => WaveStDevPowerS);
      }
    }
    double _trendHeightPerc;
    [Description("ThredA.Height.Percentage(ThredB.Height): -200 - 200")]
    [WwwSetting(Group = wwwSettingsTradingParams)]
    [Category(categoryActive)]
    public double TrendHeightPerc {
      get { return _trendHeightPerc; }
      set {
        _trendHeightPerc = value;
        OnPropertyChanged(() => TrendHeightPerc);
      }
    }

    double _trendEquinoxPerc;
    [WwwSetting(wwwSettingsCorridorEquinox)]
    [Category(categoryActive)]
    public double EquinoxPerc {
      get { return _trendEquinoxPerc; }
      set {
        _trendEquinoxPerc = value;
        OnPropertyChanged(() => EquinoxPerc);
      }
    }

    [DisplayName("Reset On Balance")]
    [Category(categoryXXX_NU)]
    public double ResetOnBalance_ {
      get { return ResetOnBalance.GetValueOrDefault(0); }
      set {
        if(ResetOnBalance == value)
          return;
        ResetOnBalance = value;
        OnPropertyChanged(nameof(ResetOnBalance_));
      }
    }

    [DisplayName("Trade By Rate Direction")]
    [Category(categoryXXX_NU)]
    public bool TradeByRateDirection_ {
      get { return TradeByRateDirection; }
      set { TradeByRateDirection = value; }
    }

    [DisplayName("Close By Momentum")]
    [Category(categoryXXX_NU)]
    [Description("Close trade when rate changes direction.")]
    public bool CloseByMomentum_ {
      get { return CloseByMomentum; }
      set { CloseByMomentum = value; }
    }

    Func<Rate, double> GetTradeEnterBy(bool? isBuy) { return _getTradeBy(TradeEnterBy, isBuy); }
    Func<Rate, double> GetTradeExitBy(bool? isBuy) { return _getTradeBy(TradeExitBy, isBuy.HasValue ? !isBuy : null); }
    Func<Rate, double> _getTradeBy(TradeCrossMethod method, bool? isBuy = null) {
      switch(method) {
        case TradeCrossMethod.PriceAvg:
          return r => r.PriceAvg;
        case TradeCrossMethod.PriceAvg1:
          return r => r.PriceAvg1;
        case TradeCrossMethod.PriceCMA:
          return r => r.PriceCMALast;
        case TradeCrossMethod.ChartAskBid:
          return !isBuy.HasValue
          ? r => r.PriceChartAsk.Avg(r.PriceChartBid)
          : isBuy.Value
          ? r => r.PriceChartAsk
          : new Func<Rate, double>(r => r.PriceChartBid);
        case TradeCrossMethod.PriceCurr:
          if(!isBuy.HasValue)
            return _ => (CurrentPrice?.Average).GetValueOrDefault();
          if(isBuy.Value)
            return _ => (CurrentPrice?.Ask).GetValueOrDefault();
          else
            return _ => (CurrentPrice?.Bid).GetValueOrDefault();
      }
      throw new NotSupportedException(method.GetType().Name + "." + method + " is not supported");
    }
    TradeCrossMethod[] _tradeEnterByCalc = new TradeCrossMethod[0];
    [DisplayName("Trade Enter By")]
    [Category(categoryActiveFuncs)]
    public TradeCrossMethod TradeEnterBy {
      get { return _tradeEnterByCalc.DefaultIfEmpty((TradeCrossMethod)BarPeriodsHigh).Single(); }
      set {
        if(BarPeriodsHigh != (int)value) {
          BarPeriodsHigh = (int)value;
          OnPropertyChanged(() => TradeEnterBy);
        }
      }
    }
    [DisplayName("Trade Exit By")]
    [Category(categoryActiveFuncs)]
    public TradeCrossMethod TradeExitBy {
      get { return (TradeCrossMethod)BarPeriodsLow; }
      set {
        if(BarPeriodsLow != (int)value) {
          BarPeriodsLow = (int)value;
          OnPropertyChanged(() => TradeExitBy);
        }
      }
    }


    [DisplayName("Turn Off Function")]
    [Category(categoryTrading)]
    public TurnOffFunctions TurnOffFunction {
      get { return (TurnOffFunctions)CorridorIterationsIn; }
      set {
        if(CorridorIterationsIn == (int)value)
          return;
        CorridorIterationsIn = (int)value;
        OnPropertyChanged("TurnOffFunction");
      }
    }
    double _waveStDevRatioSqrt = double.NaN;
    partial void OnSpreadShortToLongTresholdChanged() {
      _waveStDevRatioSqrt = Math.Sqrt(WaveStDevRatio);
    }
    [DisplayName("Wave StDev Ratio")]
    [Description("Wave Corridor = StDev*sqrt(X)")]
    [Category(categoryXXX)]
    public double WaveStDevRatio {
      get { return SpreadShortToLongTreshold; }
      set {
        SpreadShortToLongTreshold = value;
        OnPropertyChanged(() => WaveStDevRatio);
      }
    }

    [Category(categoryXXX)]
    [DisplayName("Is SuppRes Manual")]
    public bool IsSuppResManual_ {
      get { return IsSuppResManual; }
      set { IsSuppResManual = value; }
    }

    [DisplayName("Is Gann Angles Manual")]
    [Category(categoryXXX)]
    public bool IsGannAnglesManual_ {
      get { return IsGannAnglesManual; }
      set { IsGannAnglesManual = value; }
    }

    bool _ShowTrendLines = false;
    [DisplayName("Show Trend Lines")]
    [Category(categoryCorridor)]
    public bool ShowParabola {
      get { return _ShowTrendLines; }
      set {
        if(_ShowTrendLines == value)
          return;
        _ShowTrendLines = value;
        OnPropertyChanged(() => ShowParabola);
      }
    }


    BarsPeriodType _barPeriodCalc = BarsPeriodType.none;
    [Category(categoryActiveFuncs)]
    public BarsPeriodType BarPeriodCalc {
      get { return _barPeriodCalc; }
      set {
        if(_barPeriodCalc == value)
          return;
        _barPeriodCalc = value;
        RatesInternal.Clear();
        OnPropertyChanged("BarPeriodCalc");
      }
    }
    public int BarPeriodInt { get { return (int)BarPeriod; } }
    [DisplayName("Bars Period")]
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsBars)]
    public BarsPeriodType BarPeriod {
      get { return (BarsPeriodType)LimitBar; }
      set {
        if(LimitBar != (int)value) {
          LimitBar = (int)value;
          OnPropertyChanged(nameof(BarPeriod));
        }
      }
    }
    [WwwSetting(wwwSettingsBars)]
    public double BumpRatio { get; set; } = 1;

    private int _barsCount;
    [DisplayName("Bars Count")]
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsBars)]
    public int BarsCount {
      get { return _barsCount; }
      set {
        if(_barsCount != value) {
          _barsCount = value;
          BarsCountCalc = value;
          OnPropertyChanged(nameof(BarsCount));
        }
      }
    }

    #region BarsCountMax
    private int _BarsCountMax;
    [Category(categoryActive)]
    [Description("BarsCountCount = BarsCountMax < 20 ? BarsCount * BarsCountMax : BarsCountMax;")]
    [WwwSetting(wwwSettingsBars)]
    public int BarsCountMax {
      get { return _BarsCountMax < 1 ? BarsCountMax = 0 : _BarsCountMax; }
      set {
        if(value < 1) {
          Log = new Exception("BarsCountMax reset from " + _BarsCountMax + " to 10");
          value = 10;
        }
        if(_BarsCountMax != value) {
          _BarsCountMax = value;
          OnPropertyChanged("BarsCountMax");
        }
      }
    }

    #endregion

    #region CanTradeAlwaysOn
    private bool _CanTradeAlwaysOn;
    [Category(categoryActiveYesNo)]
    [DisplayName("Can Trade On")]
    [Description("CanTradeAlwaysOn")]
    public bool CanTradeAlwaysOn {
      get { return _CanTradeAlwaysOn; }
      set {
        if(_CanTradeAlwaysOn != value) {
          _CanTradeAlwaysOn = value;
          OnPropertyChanged("CanTradeAlwaysOn");
        }
      }
    }

    #endregion

    [DisplayName("Current Loss")]
    [Category(categoryTrading)]
    [IsNotStrategy]
    public double CurrentLoss_ {
      get { return CurrentLoss; }
      set {
        if(CurrentLoss != value) {
          CurrentLoss = value;
          OnPropertyChanged(nameof(CurrentLoss_));
          GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => OnPropertyChanged(nameof(CurrentGross)));
        }
      }
    }

    public int GannAngle1x1Index { get { return GannAnglesList.Angle1x1Index; } }

    public bool IsAutoStrategy { get { return Strategy.HasFlag(Strategies.Auto); } }

    private bool _IsAutoSync;
    [DisplayName("Is Auto Sync")]
    [Category(categoryCorridor)]
    public bool IsAutoSync {
      get { return _IsAutoSync; }
      set {
        if(_IsAutoSync != value) {
          _IsAutoSync = value;
          OnPropertyChanged(nameof(IsAutoSync));
        }
      }
    }

    #region SyncAll
    private bool _SyncAll;
    [DisplayName("Sync All")]
    [Category(categoryCorridor)]
    public bool SyncAll {
      get { return _SyncAll; }
      set {
        if(_SyncAll != value) {
          _SyncAll = value;
          OnPropertyChanged(nameof(SyncAll));
        }
      }
    }

    #endregion

    [DisplayName("LoadRates Seconds Warning")]
    [Category(categoryXXX)]
    public int LoadRatesSecondsWarning_ {
      get { return LoadRatesSecondsWarning; }
      set {
        if(LoadRatesSecondsWarning != value) {
          LoadRatesSecondsWarning = value;
          OnPropertyChanged(() => LoadRatesSecondsWarning_);
        }
      }
    }
    #region LastProfitStartDate
    DateTime? _LastProfitStartDate;
    public DateTime? LastProfitStartDate {
      get { return _LastProfitStartDate; }
      set {
        if(_LastProfitStartDate != value) {
          _LastProfitStartDate = value;
          OnPropertyChanged("LastProfitStartDate");
        }
      }
    }

    #endregion
    #region ExitFunction
    [DisplayName("ExitFunction")]
    [Category(categoryTrading)]
    public ExitFunctions ExitFunction {
      get { return (ExitFunctions)VolumeTresholdIterations; }
      set {
        if(VolumeTresholdIterations != (int)value) {
          VolumeTresholdIterations = (int)value;
          OnPropertyChanged("ExitFunction");
        }
      }
    }

    #endregion

    private double _TradeDistanceInPips;
    double TradeDistanceInPips {
      get { return _TradeDistanceInPips; }
      set {
        if(_TradeDistanceInPips != value) {
          _TradeDistanceInPips = value;
          OnPropertyChanged(nameof(TradeDistanceInPips));
        }
      }
    }

    private int _CalculatedLotSize;
    public int CalculatedLotSize {
      get { return _CalculatedLotSize; }
      set {
        if(_CalculatedLotSize != value) {
          _CalculatedLotSize = value;
          OnPropertyChanged(nameof(CalculatedLotSize));
        }
      }
    }

    public Freezing FreezeStopType {
      get { return (Freezing)this.FreezeStop; }
      set {
        if(this.FreezeStop != (int)value) {
          this.FreezeStop = (int)value;
          OnPropertyChanged(nameof(FreezeStopType));
        }
      }
    }

    private double _VolumeShort;
    public double VolumeShort {
      get { return _VolumeShort; }
      set {
        if(_VolumeShort != value) {
          _VolumeShort = value;
          OnPropertyChanged(nameof(VolumeShort));
          VolumeShortToLongRatio = VolumeShort / VolumeLong;
        }
      }
    }
    private double _VolumeLong;
    public double VolumeLong {
      get { return _VolumeLong; }
      set {
        if(_VolumeLong != value) {
          _VolumeLong = value;
          OnPropertyChanged(nameof(VolumeLong));
          VolumeShortToLongRatio = VolumeShort / VolumeLong;
        }
      }
    }

    private double _VolumeShortToLongRatio;
    public double VolumeShortToLongRatio {
      get { return _VolumeShortToLongRatio; }
      set {
        if(_VolumeShortToLongRatio != value) {
          _VolumeShortToLongRatio = value;
          OnPropertyChanged(nameof(VolumeShortToLongRatio));
        }
      }
    }

    public bool IsCharterMinimized { get; set; }
    private bool _ShowProperties;
    public bool ShowProperties {
      get { return _ShowProperties; }
      set {
        if(_ShowProperties != value) {
          _ShowProperties = value;
          OnPropertyChanged(nameof(ShowProperties));
        }
      }
    }


    DateTime _lastRatePullTime;
    public DateTime LastRatePullTime {
      get { return _lastRatePullTime; }
      set {
        if(_lastRatePullTime == value)
          return;
        _lastRatePullTime = value;
        OnPropertyChanged(nameof(LastRatePullTime));
      }
    }

    public double AngleInRadians { get { return Math.Atan(Angle) * (180 / Math.PI); } }
    double _angle;
    public double Angle {
      get { return _angle; }
      set {
        if(_angle == value)
          return;
        _angle = value;
        OnPropertyChanged(nameof(Angle));
        OnPropertyChanged(nameof(AngleInRadians));
      }
    }



    private double _BarHeightHigh;
    public double BarHeightHigh {
      get { return _BarHeightHigh; }
      set {
        if(_BarHeightHigh != value) {
          _BarHeightHigh = value;
          OnPropertyChanged(nameof(BarHeightHigh));
        }
      }
    }

    double? _StopAmount;
    public double? StopAmount {
      get { return _StopAmount; }
      set {
        if(_StopAmount == value)
          return;
        _StopAmount = value;
        OnPropertyChanged(nameof(StopAmount));
      }
    }
    double? _LimitAmount;
    public double? LimitAmount {
      get { return _LimitAmount; }
      set {
        if(_LimitAmount == value)
          return;
        _LimitAmount = value;
        OnPropertyChanged(nameof(LimitAmount));
      }
    }

    double? _netInPips;
    public double? NetInPips {
      get { return _netInPips; }
      set {
        if(_netInPips == value)
          return;
        _netInPips = value;
        OnPropertyChanged(nameof(NetInPips));
      }
    }

    private double _SlackInPips;
    public double SlackInPips {
      get { return _SlackInPips; }
      set {
        if(_SlackInPips != value) {
          _SlackInPips = value;
          OnPropertyChanged(nameof(SlackInPips));
        }
      }
    }

    private double _CurrentLossPercent;
    public double CurrentLossPercent {
      get { return _CurrentLossPercent; }
      set {
        if(_CurrentLossPercent != value) {
          _CurrentLossPercent = value;
          OnPropertyChanged(nameof(CurrentLossPercent));
        }
      }
    }

    private bool _IsSelectedInUI;
    public bool IsSelectedInUI {
      get { return _IsSelectedInUI; }
      set {
        if(_IsSelectedInUI == value)
          return;
        _IsSelectedInUI = value;
        OnPropertyChanged(nameof(IsSelectedInUI));
      }
    }
    public DateTime ServerTime {
      get {
        return TradesManager == null || !TradesManager.IsLoggedIn
          ? DateTime.MinValue
          : TradesManager.ServerTime;
      }
    }
    double? _currentSpread;
    Price _currentPrice;
    public Price CurrentPrice {
      get {
        return _currentPrice;
      }
      set {
        _currentPrice = value;
        if(RateLast != null && !double.IsNaN(RateLast.PriceAvg1)) {
          //if(!IsInVitualTrading)
          //  RateLast.AddTick(_currentPrice);
          RateLast.SetPriceChart();
        }
        OnPropertyChanged(nameof(CurrentPrice));
        var currentSpread = RoundPrice(this._currentSpread.Cma(10, this._currentPrice.Spread));
        if(currentSpread == this._currentSpread)
          return;
        this._currentSpread = currentSpread;
        SetPriceSpreadOk();
      }
    }

    double _balanceOnStop;
    public double BalanceOnStop {
      get { return _balanceOnStop; }
      set {
        if(_balanceOnStop == value)
          return;
        _balanceOnStop = value;
        OnPropertyChanged(nameof(BalanceOnStop));
      }
    }

    double _balanceOnLimit;
    public double BalanceOnLimit {
      get { return _balanceOnLimit; }
      set {
        if(_balanceOnLimit == value)
          return;
        _balanceOnLimit = value;
        OnPropertyChanged(nameof(BalanceOnLimit));
      }
    }

    readonly WaveInfo _waveShort;
    public WaveInfo WaveShort { get { return _waveShort; } }
    WaveInfo _waveShortLeft;
    public WaveInfo WaveShortLeft { get { return _waveShortLeft ?? (_waveShortLeft = new WaveInfo(this)); } }

    public System.Threading.CancellationToken ReplayCancelationToken { get; set; }

    public Func<Rate, double> ChartPriceHigh { get; set; }
    public Func<Rate, double> ChartPriceLow { get; set; }

    public DateTime? LineTimeMin { get; set; }

    #region CanShowNews
    private bool _CanShowNews;
    [Category(categoryCorridor)]
    [DisplayName("Can Show News")]
    public bool CanShowNews {
      get { return _CanShowNews; }
      set {
        if(_CanShowNews != value) {
          _CanShowNews = value;
          OnPropertyChanged("CanShowNews");
        }
      }
    }

    #endregion

    IEnumerable<TradeLevelBy> GetLevelByByProximity(SuppRes suppRes) {
      return (from tl in TradeLevelFuncs.Where(tl => tl.Key != TradeLevelBy.None)
              where suppRes.InManual
              let b = new { level = tl.Key, dist = suppRes.Rate.Abs(tl.Value()) }
              orderby b.dist
              select b.level
              )
              .DefaultIfEmpty(TradeLevelBy.None)
              .Take(1);
    }
    public void SetLevelsBy(SuppRes suppRes = null) {
      var rate = RateLast;
      if((suppRes ?? BuyLevel) == BuyLevel)
        SetLevelBy(BuyLevel, rate, tl => LevelBuyBy = tl);
      if((suppRes ?? BuyCloseLevel) == BuyCloseLevel)
        SetLevelBy(BuyCloseLevel, rate, tl => LevelBuyCloseBy = tl);
      if((suppRes ?? SellLevel) == SellLevel)
        SetLevelBy(SellLevel, rate, tl => LevelSellBy = tl);
      if((suppRes ?? SellCloseLevel) == SellCloseLevel)
        SetLevelBy(SellCloseLevel, rate, tl => LevelSellCloseBy = tl);
    }
    private void SetLevelBy(SuppRes suppRes, Rate rate, Action<TradeLevelBy> setLevel) {
      GetLevelByByProximity(suppRes)
        .Do(setLevel)
        .ForEach(_ => suppRes.InManual = false);
    }
    private void ResetLevelBys() {
      LevelBuyBy = LevelBuyCloseBy = LevelSellBy = LevelSellCloseBy = TradeLevelBy.None;
    }

    public string PairPlain { get { return TradesManagerStatic.WrapPair(Pair); } }

    private string _pairHedge = "";
    [WwwSetting]
    public string PairHedge {
      get => _pairHedge;
      set {
        _pairHedge = value;
      }
    }

    [WwwSetting]
    public double HedgeMMR { get; set; } = 1;

    bool _FitRatesToPlotter;

    [Category(categoryCorridor)]
    public bool FitRatesToPlotter {
      get { return _FitRatesToPlotter; }
      set {
        _FitRatesToPlotter = value;
        OnPropertyChanged(() => FitRatesToPlotter);
      }
    }

    public int IpPort { get; set; }
  }
}
