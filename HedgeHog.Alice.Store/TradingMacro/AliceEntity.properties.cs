using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using HedgeHog.Bars;
using HedgeHog.Alice.Store.Metadata;
using HedgeHog.Shared;
using HedgeHog.Models;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Linq.Expressions;
using ReactiveUI;

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
        if (_MonthsOfHistory != value) {
          _MonthsOfHistory = value;
          OnPropertyChanged(TradingMacroMetadata.MonthsOfHistory);
        }
      }
    }

    #endregion

    [DisplayName("Corridor StDev By")]
    [Category(categoryActiveFuncs)]
    public CorridorCalculationMethod CorridorCalcMethod {
      get { return (CorridorCalculationMethod)this.CorridorMethod; }
      set {
        if (this.CorridorMethod != (int)value) {
          this.CorridorMethod = (int)value;
          OnPropertyChanged(TradingMacroMetadata.CorridorCalcMethod);
        }
      }
    }


    #region DoLogSaveRates
    private bool _DoLogSaveRates;
    [Category(categoryXXX)]
    public bool DoLogSaveRates {
      get { return _DoLogSaveRates; }
      set {
        if (_DoLogSaveRates != value) {
          _DoLogSaveRates = value;
          OnPropertyChanged("DoLogSaveRates");
        }
      }
    }

    #endregion
    //RatesHeightMinimum
    public string _TestRatesHeightMinimum = "";
    [DisplayName("RatesHeightMinimum")]
    [Category(categoryTest)]
    public string TestRatesHeightMinimum {
      get { return _TestRatesHeightMinimum; }
      set {
        if (_TestRatesHeightMinimum != value) {
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
    public string TestFileName {
      get { return _TestFileName; }
      set {
        if (_TestFileName != value) {
          _TestFileName = value;
          OnPropertyChanged("TestFileName");
        }
      }
    }

    public string _TestBarsCount = "";
    [DisplayName("BarsCount")]
    [Category(categoryTest)]
    public string TestBarsCount {
      get { return _TestBarsCount; }
      set {
        if (_TestBarsCount != value) {
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
        if (_TestCorrelationMinimum != value) {
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
        if (_TestCorridorDistanceRatio != value) {
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
        if (_TestWaveStDevRatio != value) {
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
        if (_TestDistanceIterations != value) {
          _TestDistanceIterations = value;
          OnPropertyChanged("TestDistanceIterations");
        }
      }
    }

    public bool _UseTestFile;
    [Category(categoryTestControl)]
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
        if (_TestPriceCmaLevels != value) {
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
        if (string.IsNullOrWhiteSpace(value)) v = Guid.NewGuid();
        else if (value == "0") v = 0.Guid();
        else Guid.TryParse(value, out v);
        if (_TestSuperSessionUid != v) {
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
        if (_TestProfitToLossExitRatio != value) {
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
        if (_TurnOffOnProfit != value) {
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
        if (_DoNews != value) {
          _DoNews = value;
          OnPropertyChanged("DoNews");
        }
      }
    }

    #endregion

    #region PriceCmaLevels
    [DisplayName("Price CMA Levels")]
    [WwwSetting(wwwSettingsCorridorCMA)]
    [Category(categoryActive)]
    public double PriceCmaLevels_ {
      get { return PriceCmaLevels; }
      set {
        if (PriceCmaLevels != value) {
          PriceCmaLevels = value;
          OnPropertyChanged(TradingMacroMetadata.PriceCmaLevels_);
        }
      }
    }

    #endregion
    #region MovingAverageType
    int _movingAverageTypeDefault = 0;
    [DisplayName("Moving Average Type")]
    [Category(categoryActiveFuncs)]
    public MovingAverageType MovingAverageType {
      get { return (MovingAverageType)MovingAverageTypeInt.GetValueOrDefault(_movingAverageTypeDefault); }
      set {
        if (_MovingAverageTypeInt.GetValueOrDefault(_movingAverageTypeDefault) != (int)value) {
          MovingAverageTypeInt = (int)value;
          OnPropertyChanged(TradingMacroMetadata.MovingAverageType);
        }
      }
    }

    #endregion

    #region BuyLevelBy
    private TradeLevelBy _LevelBuyBy;
    [Category(categoryActiveFuncs)]
    public TradeLevelBy LevelBuyBy {
      get { return _LevelBuyBy; }
      set {
        if (_LevelBuyBy != value) {
          _LevelBuyBy = value;
          OnPropertyChanged("LevelBuyBy");
        }
      }
    }
    #endregion

    #region LevelBuyCloseBy
    private TradeLevelBy _LevelBuyCloseBy;
    [Category(categoryActiveFuncs)]
    public TradeLevelBy LevelBuyCloseBy {
      get { return _LevelBuyCloseBy; }
      set {
        if (_LevelBuyCloseBy != value) {
          _LevelBuyCloseBy = value;
          OnPropertyChanged("LevelBuyCloseBy");
        }
      }
    }
    #endregion
    #region LevelSellCloseBy
    private TradeLevelBy _LevelSellCloseBy;
    [Category(categoryActiveFuncs)]
    public TradeLevelBy LevelSellCloseBy {
      get { return _LevelSellCloseBy; }
      set {
        if (_LevelSellCloseBy != value) {
          _LevelSellCloseBy = value;
          OnPropertyChanged("LevelSellCloseBy");
        }
      }
    }

    #endregion

    #region LevelSellBy
    private TradeLevelBy _LevelSellBy;
    [Category(categoryActiveFuncs)]
    public TradeLevelBy LevelSellBy {
      get { return _LevelSellBy; }
      set {
        if (_LevelSellBy != value) {
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
        if (_CorridorByStDevRatioFunc != value) {
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
        if (_CorridorByStDevRatioFunc2 != value) {
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
        if (_UseVoltage != value) {
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
        if (_ForceOpenTrade != value) {
          _ForceOpenTrade = value;
          OnPropertyChanged(TradingMacroMetadata.ForceOpenTrade);
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
        if (CorridorCrossHighLowMethodInt != (int)value) {
          CorridorCrossHighLowMethodInt = (int)value;
          OnPropertyChanged(TradingMacroMetadata.CorridorCrossHighLowMethod);
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
        if (CorridorLengthMinimum != value) {
          CorridorLengthMinimum = value;
          OnPropertyChanged(TradingMacroMetadata.CorridorLengthMinimum_);
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
        OnPropertyChanged(TradingMacroMetadata.CorridorHighLowMethod);
      }
    }

    [DisplayName("MaxLot By TakeProfit Ratio")]
    [Description("MaxLotSize < LotSize*N")]
    [Category(categoryXXX)]
    public double MaxLotByTakeProfitRatio_ {
      get { return MaxLotByTakeProfitRatio; }
      set {
        MaxLotByTakeProfitRatio = value;
        OnPropertyChanged(TradingMacroMetadata.MaxLotByTakeProfitRatio_);
      }
    }


    [DisplayName("Voltage Function")]
    [Category(categoryActiveFuncs)]
    public VoltageFunction VoltageFunction_ {
      get { return (VoltageFunction)VoltageFunction; }
      set {
        if (VoltageFunction != (int)value) {
          VoltageFunction = (int)value;
          OnPropertyChanged("VoltageFunction_");
        }
      }
    }
    partial void OnVoltageFunctionChanged() {
      OnPropertyChanged("VoltageFunction_");
    }
    [DisplayName("Scan Corridor By")]
    [Category(categoryActiveFuncs)]
    [Description("ScanCorridor By")]
    public ScanCorridorFunction ScanCorridorBy {
      get { return (ScanCorridorFunction)StDevAverageLeewayRatio; }
      set {
        if (StDevAverageLeewayRatio != (int)value) {
          StDevAverageLeewayRatio = (int)value;
          OnPropertyChanged("ScanCorridorBy");
        }
      }
    }

    #region RatesLenghBy
    private RatesLengthFunction _RatesLengthBy = RatesLengthFunction.TimeFrame;
    [DisplayName("Rates Length By")]
    [Category(categoryActiveFuncs)]
    public RatesLengthFunction RatesLengthBy {
      get { return _RatesLengthBy; }
      set {
        if(_RatesLengthBy != value) {
          _RatesLengthBy = value;
          OnPropertyChanged("RatesLengthBy");
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
        if (this.FreezLimit != (int)value) {
          this.FreezLimit = (int)value;
          OnPropertyChanged("TrailingDistanceFunction");
        }
      }
    }

    [DisplayName("Trading Distance")]
    [Category(categoryActiveFuncs)]
    [Description("TradingDistanceFunction")]
    [WwwSetting(Group = wwwSettingsTradingOther)]
    public TradingMacroTakeProfitFunction TradingDistanceFunction {
      get { return (TradingMacroTakeProfitFunction)PowerRowOffset; }
      set {
        PowerRowOffset = (int)value;
        OnPropertyChanged(TradingMacroMetadata.TradingDistanceFunction);
      }
    }

    [DisplayName("Take Profit")]
    [Category(categoryActiveFuncs)]
    [Description("TakeProfitFunction")]
    [WwwSetting(Group = wwwSettingsTrading)]
    public TradingMacroTakeProfitFunction TakeProfitFunction {
      get { return (TradingMacroTakeProfitFunction)TakeProfitFunctionInt; }
      set {
        TakeProfitFunctionInt = (int)value;
        OnPropertyChanged(TradingMacroMetadata.TakeProfitFunction);
      }
    }

    [DisplayName("Show Trade On Chart")]
    [Category(categoryCorridor)]
    public bool DoShowTradeOnChart {
      get { return TradeOnCrossOnly; }
      set {
        if (TradeOnCrossOnly == value) return;
        TradeOnCrossOnly = value;
        OnPropertyChanged(() => DoShowTradeOnChart);
      }
    }


    GannAngles _GannAnglesList;
    public GannAngles GannAnglesList {
      get {
        if (_GannAnglesList == null) {
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
    private bool IsTradingHour_Old(DateTime time) {
      var hours = TradingHoursRange.Split('-').Select(s => DateTime.Parse(s).Hour).ToArray();
      return hours[0] < hours[1] ? time.Hour.Between(hours[0], hours[1]) : !time.Hour.Between(hours[0] - 1, hours[1] + 1);
    }
    private bool IsTradingHour(DateTime time) {
      var times = TradingHoursRange.Split('-').Select(s => DateTime.Parse(s)).ToArray();
      return time.TimeOfDay.Between(times[0].TimeOfDay, times[1].TimeOfDay);
    }
    DayOfWeek[] TradingDays() {
      switch (TradingDaysRange) {
        case WeekDays.Full: return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        case WeekDays.MoTh: return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        case WeekDays.MoTuFr: return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        case WeekDays.TuFr: return new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        case WeekDays.TuTh: return new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        case WeekDays.SuFr: return new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        case WeekDays.SuTh: return new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
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
    public string TradingHoursRange {
      get { return CorridorIterations; }
      set {
        if (CorridorIterations == value) return;
        CorridorIterations = value;
        OnPropertyChanged(() => TradingHoursRange);
      }
    }

    [DisplayName("Trading Days")]
    [Category(categoryActiveFuncs)]
    public WeekDays TradingDaysRange {
      get { return (WeekDays)CorridorRatioForRange; }
      set {
        if (CorridorRatioForRange == (int)value) return;
        CorridorRatioForRange = (int)value;
        OnPropertyChanged(() => TradingDaysRange);
      }
    }

    [DisplayName("SuppRes Levels Count")]
    [Category(categoryCorridor)]
    public int SuppResLevelsCount_ {
      get { return SuppResLevelsCount; }
      set {
        if (SuppResLevelsCount == value) return;
        SuppResLevelsCount = value;
        OnPropertyChanged(TradingMacroMetadata.SuppResLevelsCount_);
      }
    }

    [DisplayName("Correlation Min")]
    [Category(categoryCorridor)]
    public double CorrelationMinimum {
      get { return StDevToSpreadRatio; }
      set {
        if (StDevToSpreadRatio != value) {
          StDevToSpreadRatio = value;
          OnPropertyChanged(() => CorrelationMinimum);
        }
      }
    }

    [DisplayName("ProfitToLossExitRatio")]
    [Category(categoryXXX)]
    [Description("Trades.Lot() / AllowedLotSize")]
    public double ProfitToLossExitRatio_ {
      get { return ProfitToLossExitRatio; }
      set {
        if (ProfitToLossExitRatio != value) {
          ProfitToLossExitRatio = value;
          OnPropertyChanged(TradingMacroMetadata.ProfitToLossExitRatio_);
        }
      }
    }

    [DisplayName("CorridorDistanceRatio")]
    [Category(categoryActive)]
    [Description("X > 1 ? X : BarsCount * CorridorDistanceRatio")]
    public double CorridorDistanceRatio {
      get { return CorridorStDevRatioMax; }
      set {
        if (CorridorStDevRatioMax != value) {
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
        if (ExtreamCloseOffset != value) {
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
        if (CurrentLossInPipsCloseAdjustment != value) {
          CurrentLossInPipsCloseAdjustment = value;
          OnPropertyChanged("CanTradeLocalRatio");
        }
      }
    }

    #region IsTakeBack
    private bool _IsTakeBack;
    [WwwSetting()]
    [Category(categoryActiveYesNo)]
    [Description("Set exit level to no-loss.")]
    public bool IsTakeBack {
      get { return _IsTakeBack; }
      set {
        if (_IsTakeBack != value) {
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
        if (CorridorBigToSmallRatio != value) {
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
        OnPropertyChanged(TradingMacroMetadata.StreatchTakeProfit);
      }
    }

    [DisplayName("Close On Open Only")]
    [Category(categoryXXX)]
    [Description("Close position only when opposite opens.")]
    public bool CloseOnOpen_ {
      get { return CloseOnOpen; }
      set {
        CloseOnOpen = value;
        OnPropertyChanged(TradingMacroMetadata.CloseOnOpen_);
      }
    }

    [DisplayName("Exit By BuySell Level")]
    [Category(categoryActiveYesNo)]
    [Description("(X ? _buySell:eve.Rate : trade.Open) + takeProfit ")]
    public bool ExitByBuySellLevel {
      get { return CloseOnProfit; }
      set {
        CloseOnProfit = value;
        OnPropertyChanged(() => ExitByBuySellLevel);
      }
    }

    [DisplayName("Close On Profit Only")]
    [Category(categoryXXX_NU)]
    [Description("Ex: if( PL > Limit) OpenTrade()")]
    public bool CloseOnProfitOnly_ {
      get { return CloseOnProfitOnly; }
      set {
        if (CloseOnProfitOnly == value) return;
        CloseOnProfitOnly = value;
        OnPropertyChanged(Metadata.TradingMacroMetadata.CloseOnProfitOnly_);
      }
    }

    [DisplayName("Reverse Strategy")]
    [Category(categoryXXX)]
    public bool ReverseStrategy_ {
      get { return ReverseStrategy; }
      set {
        if (ReverseStrategy == value) return;
        ReverseStrategy = value;
        OnPropertyChanged(Metadata.TradingMacroMetadata.ReverseStrategy_);
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

    public const string wwwSettingsLiveOrders = "3. Live Orders";
    public const string wwwSettingsCorridorAngles = "2.0 Corridor Angles";

    public const string wwwSettingsCorridorCMA = "2.1 Corridor CMA";
    public const string wwwSettingsCorridorOther = "2.2 Corridor";

    public const string wwwSettingsTrading = "1.0 Trading";
    public const string wwwSettingsTradingCorridor = "1.1 Trading Corridor";
    public const string wwwSettingsTradingOther = "1.2 Trading Other";
    public const string wwwSettingsTradingConditions = "1.3 Trading Conditions";
    public const string wwwInfoAngles = "Angles";

    #region CloseAfterTradingHours
    private bool _CloseAfterTradingHours;
    [Category(categoryActiveYesNo)]
    public bool CloseAfterTradingHours {
      get { return _CloseAfterTradingHours; }
      set {
        if (_CloseAfterTradingHours != value) {
          _CloseAfterTradingHours = value;
          OnPropertyChanged("CloseAfterTradingHours");
          SaveActiveSettings();
        }
      }
    }

    #endregion
    [WwwSetting(Group = wwwSettingsTradingOther)]
    [Category(categoryActive)]
    [Description("_buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum")]
    public int TradeCountMax {
      get { return CorridorRatioForBreakout.ToInt(); }
      set {
        CorridorRatioForBreakout = value;
        OnPropertyChanged(Metadata.TradingMacroMetadata.CorridorCrossesMaximum);
      }
    }

    [DisplayName("CorridorHeight Max")]
    [Category(categoryXXX)]
    public double CorridorHeightMax {
      get { return FibMin; }
      set {
        if (FibMin == value) return;
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
        if (ReversePower == value) return;
        ReversePower = value;
        OnPropertyChanged(() => IsCorridorForwardOnly);
      }
    }

    [Category(categoryActive)]
    [DisplayName("TakeProfit Limit Ratio")]
    [Description("Ex:Exit <= TakeProfit * X")]
    public double TakeProfitLimitRatio {
      get {
        if (RangeRatioForTradeStop < 1) {
          RangeRatioForTradeStop = 1;
          Log = new Exception("TakeProfitLimitRatio is less then 1. It is being set to 1.".Formater(TakeProfitLimitRatio));
          OnPropertyChanged(() => TakeProfitLimitRatio);
        }
        return RangeRatioForTradeStop;
      }
      set {
        if (RangeRatioForTradeStop == value) return;
        RangeRatioForTradeStop = value;
        OnPropertyChanged(() => TakeProfitLimitRatio);
      }
    }

    [Category(categoryActiveYesNo)]
    [DisplayName("Trading Ratio By PMC")]
    [WwwSetting(wwwSettingsTradingOther)]
    public bool TradingRatioByPMC {
      get { return TradeByAngle; }
      set { TradeByAngle = value; }
    }

    [Category(categoryXXX_NU)]
    [DisplayName("Trade On BOW")]
    [Description("If ExitOnFriday, consider this as well")]
    public bool TradeOnBOW {
      get { return TradeByFirstWave.GetValueOrDefault(); }
      set {
        if (TradeByFirstWave == value) return;
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
        if (LongMAPeriod == (int)value) return;
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
        if (TradingAngleRange == value) return;
        TradingAngleRange = value;
        OnPropertyChanged(() => TradingAngleRange_);
      }
    }

    double _trendAngleGreen;
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public double TrendAngleGreen{
      get { return _trendAngleGreen; }
      set {
        if(_trendAngleGreen == value)
          return;
        _trendAngleGreen = value;
        OnPropertyChanged(() => TrendAngleGreen);
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


    public double TrendAngleBlue0 { get; set; }
    public double TrendAngleBlue1 { get; set; }
    string _trendAngleBlue = "0";
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    [Description("Range or single value: 15-30 or 60 or -60")]
    public string TrendAngleBlue {
      get { return _trendAngleBlue; }
      set {
        if(_trendAngleBlue == value)
          return;

        _trendAngleBlue = value.Trim();
        OnPropertyChanged(() => TrendAngleBlue);

        var spans = _trendAngleBlue.StartsWith("-") 
          ? new[] { _trendAngleBlue } 
          : value.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
        if(spans.IsEmpty())
          spans = new[] { "0" };
        TrendAngleBlue0 = double.Parse(spans[0]);
        TrendAngleBlue1 = spans.Length > 1 ? double.Parse(spans[1]) : double.NaN;

      }
    }

    int _trendAnglesSdrRange;
    [DisplayName("Angle RSD range in %")]
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public int TrendAnglesSdrRange {
      get { return _trendAnglesSdrRange; }
      set {
        if(_trendAnglesSdrRange == value)
          return;
        _trendAnglesSdrRange = value;
        OnPropertyChanged(() => TrendAnglesSdrRange);
      }
    }

    int _trendAnglesPerc;
    [DisplayName("AngleR.Percentage(AngleB): -200 - 200")]
    [WwwSetting(Group = wwwSettingsCorridorAngles)]
    [Category(categoryActive)]
    public int TrendAnglesPerc {
      get { return _trendAnglesPerc; }
      set {
        if(_trendAnglesPerc == value)
          return;
        if(!value.Between(-200, 200)) {
          Log = new Exception(new { TrendAnglesPerc = value, MustBe = " from -200 to 200" } + "");
          return;
        }
        _trendAnglesPerc = value;
        OnPropertyChanged(() => TrendAnglesPerc);
      }
    }
    int _trendHeightPerc;
    [DisplayName("ThredA.Height.Percentage(ThredB.Height): -200 - 200")]
    [WwwSetting(Group = wwwSettingsTradingConditions)]
    [Category(categoryActive)]
    public int TrendHeightPerc {
      get {        return _trendHeightPerc;      }
      set {
        _trendHeightPerc = value;
        OnPropertyChanged(() => TrendHeightPerc);
      }
    }
    double _macdRsdAvgLevel = 100;
    [WwwSetting(Group = wwwSettingsTradingConditions)]
    [Category(categoryActive)]
    public double MacdRsdAvgLevel {
      get {        return _macdRsdAvgLevel;      }
      set {
        _macdRsdAvgLevel = value;
        OnPropertyChanged(() => MacdRsdAvgLevel);
      }
    }


    [DisplayName("Reset On Balance")]
    [Category(categoryXXX_NU)]
    public double ResetOnBalance_ {
      get { return ResetOnBalance.GetValueOrDefault(0); }
      set {
        if (ResetOnBalance == value) return;
        ResetOnBalance = value;
        OnPropertyChanged(TradingMacroMetadata.ResetOnBalance_);
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
      switch (method) {
        case TradeCrossMethod.PriceAvg: return r => r.PriceAvg;
        case TradeCrossMethod.PriceAvg1: return r => r.PriceAvg1;
        case TradeCrossMethod.PriceCMA: return r => r.PriceCMALast;
        case TradeCrossMethod.ChartAskBid:
          return !isBuy.HasValue
          ? r => r.PriceChartAsk.Avg(r.PriceChartBid)
          : isBuy.Value
          ? r => r.PriceChartAsk
          : new Func<Rate, double>(r => r.PriceChartBid);
        case TradeCrossMethod.PriceCurr:
          if (!isBuy.HasValue) return _ => CurrentPrice.Average;
          if (isBuy.Value) return _ => CurrentPrice.Ask; else return _ => CurrentPrice.Bid;
      }
      throw new NotSupportedException(method.GetType().Name + "." + method + " is not supported");
    }
    TradeCrossMethod[] _tradeEnterByCalc = new TradeCrossMethod[0];
    [DisplayName("Trade Enter By")]
    [Category(categoryActiveFuncs)]
    public TradeCrossMethod TradeEnterBy {
      get { return _tradeEnterByCalc.DefaultIfEmpty((TradeCrossMethod)BarPeriodsHigh).Single(); }
      set {
        if (BarPeriodsHigh != (int)value) {
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
        if (BarPeriodsLow != (int)value) {
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
        if (CorridorIterationsIn == (int)value) return;
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
        if (_ShowTrendLines == value) return;
        _ShowTrendLines = value;
        OnPropertyChanged(() => ShowParabola);
      }
    }


    #region MaximumPositions
    [DisplayName("Maximum Positions")]
    [Category(categoryTrading)]
    public int MaximumPositions_ {
      get { return MaximumPositions; }
      set {
        if (MaximumPositions != value) {
          MaximumPositions = value;
          OnPropertyChanged(TradingMacroMetadata.MaximumPositions_);
        }
      }
    }

    #endregion
    BarsPeriodType _barPeriodCalc = BarsPeriodType.none;
    [Category(categoryActiveFuncs)]
    public BarsPeriodType BarPeriodCalc {
      get { return _barPeriodCalc; }
      set {
        if (_barPeriodCalc == value) return;
        _barPeriodCalc = value;
        RatesInternal.Clear();
        OnPropertyChanged("BarPeriodCalc");
      }
    }
    public int BarPeriodInt { get { return (int)BarPeriod; } }
    [DisplayName("Bars Period")]
    [Category(categoryActiveFuncs)]
    public BarsPeriodType BarPeriod {
      get { return (BarsPeriodType)LimitBar; }
      set {
        if (LimitBar != (int)value) {
          LimitBar = (int)value;
          OnPropertyChanged(TradingMacroMetadata.BarPeriod);
        }
      }
    }

    [DisplayName("Bars Count(45,360,..)")]
    [Category(categoryActive)]
    public int BarsCount {
      get { return CorridorBarMinutes; }
      set {
        if (CorridorBarMinutes != value) {
          CorridorBarMinutes = value;
          BarsCountCalc = value;
          OnPropertyChanged(TradingMacroMetadata.BarsCount);
        }
      }
    }

    #region BarsCountMax
    private int _BarsCountMax;
    [Category(categoryActive)]
    [Description("BarsCountCount = BarsCountMax < 20 ? BarsCount * BarsCountMax : BarsCountMax;")]
    public int BarsCountMax {
      get { return _BarsCountMax < 1 ? BarsCountMax = 0 : _BarsCountMax; }
      set {
        if (value < 1) {
          Log = new Exception("BarsCountMax reset from " + _BarsCountMax + " to 10");
          value = 10;
        }
        if (_BarsCountMax != value) {
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
        if (_CanTradeAlwaysOn != value) {
          _CanTradeAlwaysOn = value;
          OnPropertyChanged("CanTradeAlwaysOn");
        }
      }
    }

    #endregion

    [DisplayName("Current Loss")]
    [Category(categoryTrading)]
    public double CurrentLoss_ {
      get { return CurrentLoss; }
      set {
        if (CurrentLoss != value) {
          CurrentLoss = value;
          OnPropertyChanged(TradingMacroMetadata.CurrentLoss_);
          GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => OnPropertyChanged(TradingMacroMetadata.CurrentGross));
        }
      }
    }


    [DisplayName("Trading Ratio")]
    [Description("Lot Size By % from Account Balance[0.1] or N*1000")]
    [Category(categoryTrading)]
    public double TradingRatio_ {
      get { return TradingRatio; }
      set {
        if (TradingRatio != value) {
          TradingRatio = value;
          OnPropertyChanged(TradingMacroMetadata.TradingRatio_);
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
        if (_IsAutoSync != value) {
          _IsAutoSync = value;
          OnPropertyChanged(TradingMacroMetadata.IsAutoSync);
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
        if (_SyncAll != value) {
          _SyncAll = value;
          OnPropertyChanged(TradingMacroMetadata.SyncAll);
        }
      }
    }

    #endregion

    [DisplayName("LoadRates Seconds Warning")]
    [Category(categoryXXX)]
    public int LoadRatesSecondsWarning_ {
      get { return LoadRatesSecondsWarning; }
      set {
        if (LoadRatesSecondsWarning != value) {
          LoadRatesSecondsWarning = value;
          OnPropertyChanged(() => LoadRatesSecondsWarning_);
        }
      }
    }
    #region LastProfitStartDate
    DateTime? _LastProfitStartDate;
    [DisplayName("Last Profit StartDate")]
    [Category(categoryTrading)]
    public DateTime? LastProfitStartDate {
      get { return _LastProfitStartDate; }
      set {
        if (_LastProfitStartDate != value) {
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
        if (VolumeTresholdIterations != (int)value) {
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
        if (_TradeDistanceInPips != value) {
          _TradeDistanceInPips = value;
          OnPropertyChanged(TradingMacroMetadata.TradeDistanceInPips);
        }
      }
    }

    private int _CalculatedLotSize;
    public int CalculatedLotSize {
      get { return _CalculatedLotSize; }
      set {
        if (_CalculatedLotSize != value) {
          _CalculatedLotSize = value;
          OnPropertyChanged(TradingMacroMetadata.CalculatedLotSize);
        }
      }
    }

    public Freezing FreezeStopType {
      get { return (Freezing)this.FreezeStop; }
      set {
        if (this.FreezeStop != (int)value) {
          this.FreezeStop = (int)value;
          OnPropertyChanged(TradingMacroMetadata.FreezeStopType);
        }
      }
    }

    private double _VolumeShort;
    public double VolumeShort {
      get { return _VolumeShort; }
      set {
        if (_VolumeShort != value) {
          _VolumeShort = value;
          OnPropertyChanged(TradingMacroMetadata.VolumeShort);
          VolumeShortToLongRatio = VolumeShort / VolumeLong;
        }
      }
    }
    private double _VolumeLong;
    public double VolumeLong {
      get { return _VolumeLong; }
      set {
        if (_VolumeLong != value) {
          _VolumeLong = value;
          OnPropertyChanged(TradingMacroMetadata.VolumeLong);
          VolumeShortToLongRatio = VolumeShort / VolumeLong;
        }
      }
    }

    private double _VolumeShortToLongRatio;
    public double VolumeShortToLongRatio {
      get { return _VolumeShortToLongRatio; }
      set {
        if (_VolumeShortToLongRatio != value) {
          _VolumeShortToLongRatio = value;
          OnPropertyChanged(TradingMacroMetadata.VolumeShortToLongRatio);
        }
      }
    }

    public bool IsCharterMinimized { get; set; }
    private bool _ShowProperties;
    public bool ShowProperties {
      get { return _ShowProperties; }
      set {
        if (_ShowProperties != value) {
          _ShowProperties = value;
          OnPropertyChanged(TradingMacroMetadata.ShowProperties);
        }
      }
    }


    DateTime _lastRatePullTime;
    public DateTime LastRatePullTime {
      get { return _lastRatePullTime; }
      set {
        if (_lastRatePullTime == value) return;
        _lastRatePullTime = value;
        OnPropertyChanged(TradingMacroMetadata.LastRatePullTime);
      }
    }

    public double AngleInRadians { get { return Math.Atan(Angle) * (180 / Math.PI); } }
    double _angle;
    public double Angle {
      get { return _angle; }
      set {
        if (_angle == value) return;
        _angle = value;
        OnPropertyChanged(TradingMacroMetadata.Angle);
        OnPropertyChanged(TradingMacroMetadata.AngleInRadians);
      }
    }



    private double _BarHeightHigh;
    public double BarHeightHigh {
      get { return _BarHeightHigh; }
      set {
        if (_BarHeightHigh != value) {
          _BarHeightHigh = value;
          OnPropertyChanged(TradingMacroMetadata.BarHeightHigh);
        }
      }
    }

    double? _StopAmount;
    public double? StopAmount {
      get { return _StopAmount; }
      set {
        if (_StopAmount == value) return;
        _StopAmount = value;
        OnPropertyChanged(TradingMacroMetadata.StopAmount);
      }
    }
    double? _LimitAmount;
    public double? LimitAmount {
      get { return _LimitAmount; }
      set {
        if (_LimitAmount == value) return;
        _LimitAmount = value;
        OnPropertyChanged(TradingMacroMetadata.LimitAmount);
      }
    }

    double? _netInPips;
    public double? NetInPips {
      get { return _netInPips; }
      set {
        if (_netInPips == value) return;
        _netInPips = value;
        OnPropertyChanged(TradingMacroMetadata.NetInPips);
      }
    }

    private double _SlackInPips;
    public double SlackInPips {
      get { return _SlackInPips; }
      set {
        if (_SlackInPips != value) {
          _SlackInPips = value;
          OnPropertyChanged(TradingMacroMetadata.SlackInPips);
        }
      }
    }

    private double _CurrentLossPercent;
    public double CurrentLossPercent {
      get { return _CurrentLossPercent; }
      set {
        if (_CurrentLossPercent != value) {
          _CurrentLossPercent = value;
          OnPropertyChanged(TradingMacroMetadata.CurrentLossPercent);
        }
      }
    }

    private bool _IsSelectedInUI;
    public bool IsSelectedInUI {
      get { return _IsSelectedInUI; }
      set {
        if (_IsSelectedInUI == value) return;
        _IsSelectedInUI = value;
        OnPropertyChanged(TradingMacroMetadata.IsSelectedInUI);
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
        if (RateLast != null && !double.IsNaN(RateLast.PriceAvg1)) {
          //if(!IsInVitualTrading)
          //  RateLast.AddTick(_currentPrice);
          RateLast.SetPriceChart();
        }
        OnPropertyChanged(TradingMacroMetadata.CurrentPrice);
        var currentSpread = RoundPrice(this._currentSpread.Cma(10, this._currentPrice.Spread));
        if (currentSpread == this._currentSpread) return;
        this._currentSpread = currentSpread;
        SetPriceSpreadOk();
      }
    }

    double _balanceOnStop;
    public double BalanceOnStop {
      get { return _balanceOnStop; }
      set {
        if (_balanceOnStop == value) return;
        _balanceOnStop = value;
        OnPropertyChanged(TradingMacroMetadata.BalanceOnStop);
      }
    }

    double _balanceOnLimit;
    public double BalanceOnLimit {
      get { return _balanceOnLimit; }
      set {
        if (_balanceOnLimit == value) return;
        _balanceOnLimit = value;
        OnPropertyChanged(TradingMacroMetadata.BalanceOnLimit);
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
        if (_CanShowNews != value) {
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
      if ((suppRes ?? BuyLevel) == BuyLevel)
        SetLevelBy(BuyLevel, rate, tl => LevelBuyBy = tl);
      if ((suppRes ?? BuyCloseLevel) == BuyCloseLevel)
        SetLevelBy(BuyCloseLevel, rate, tl => LevelBuyCloseBy = tl);
      if ((suppRes ?? SellLevel) == SellLevel)
        SetLevelBy(SellLevel, rate, tl => LevelSellBy = tl);
      if ((suppRes ?? SellCloseLevel) == SellCloseLevel)
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

    public string PairPlain { get { return Pair.ToLower().Replace("/", ""); } }

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
