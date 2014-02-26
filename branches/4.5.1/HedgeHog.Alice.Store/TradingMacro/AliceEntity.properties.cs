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
using ReactiveUI;
using System.Reactive.Linq;
using System.Linq.Expressions;

namespace HedgeHog.Alice.Store {
  public static class SuppResExtentions {
    public static SuppRes[] Active(this ICollection<SuppRes> supReses,bool isBuy) {
      return supReses.Active().IsBuy(isBuy);
    }
    static SuppRes[] Active(this ICollection<SuppRes> supReses) {
      return supReses.Where(sr => sr.IsActive).ToArray();
    }
    public static SuppRes[] IsBuy(this ICollection<SuppRes> supReses, bool isBuy) {
      return supReses.Where(sr => sr.IsBuy == isBuy).ToArray();
    }
  }
  public partial class SuppRes {
    public class EntryOrderIdEventArgs : EventArgs {
      public string NewId { get; set; }
      public string OldId { get; set; }
      public EntryOrderIdEventArgs(string newId,string oldId) {
        this.NewId = newId;
        this.OldId= oldId;
      }
    }
    public static readonly double TradesCountMinimum = 1;
    public static readonly string RemovedOrderTag = "X";
    public bool IsBuy { get { return !IsSupport; } }
    public bool IsSell { get { return IsSupport; } }
    private bool _IsActive = true;
    public bool IsActive {
      get { return _IsActive; }
      set {
        if (_IsActive != value) {
          _IsActive = value;
          if (!value) EntryOrderId = "";
          OnIsActiveChanged();
        }
      }
    }
    #region IsGhost
    IDisposable _isGhostDisposable;
    public bool IsGhost {
      get {
        if (_isGhostDisposable == null) {
          _isGhostDisposable = this.SubscribeToPropertiesChanged(sr => OnPropertyChanged("IsGhost")
            , x => x.InManual
            , x => x.IsExitOnly
            , x => x.CanTrade
            , x => x.TradesCount
            );
        }
        return InManual 
          && IsExitOnly 
          && CanTrade 
          && TradesCount <= 0; 
      }
      set {
        if (!IsExitOnly) throw new Exception("Not an exit Level.");
        if (value) {
          InManual = true;
          CanTrade = true;
          TradesCount = TradesCount.Min(0);
        } else {
          InManual = false;
          CanTrade = false;
          TradesCount = 9;
        }
      }
    }

    #endregion
    #region CanTrade
    private bool _CanTrade;
    public bool CanTrade {
      get { return _CanTrade; }
      set {
        if (_CanTrade != value) {
          //if (value && IsExitOnly)
          //  Scheduler.Default.Schedule(() => CanTrade = false);
          _CanTrade = value;
          OnPropertyChanged("CanTrade");
          OnPropertyChanged("CanTradeEx");
          RaiseCanTradeChanged();
        }
      }
    }
    #endregion

    public bool CanTradeEx {
      get { return CanTrade; }
      set {
        if (CanTradeEx == value || InManual) return;
        CanTrade = value;
      }
    }
    public double TradesCountEx {
      get { return TradesCount; }
      set {
        if (TradesCount == value || InManual) return;
        TradesCount = value;
      }
    }

    int _rateExErrorCounter = 0;// This is to ammend some wierd bug in IEntityChangeTracker.EntityMemberChanged or something that it calls
    public double RateEx {
      get { return Rate; }
      set {
        if (Rate == value || this.InManual) return;
        var valuePrev = Rate;
        try {
          Rate = value;
          _rateExErrorCounter = 0;
        } catch (Exception exc) {
          if (_rateExErrorCounter > 100) throw;
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(new Exception("Rate: "+new { Prev = valuePrev, Curr = Rate } + ""));
        }
      }
    }

    #region CanTradeChanged Event
    event EventHandler<EventArgs> CanTradeChangedEvent;
    public event EventHandler<EventArgs> CanTradeChanged {
      add {
        if (CanTradeChangedEvent == null || !CanTradeChangedEvent.GetInvocationList().Contains(value))
          CanTradeChangedEvent += value;
      }
      remove {
        CanTradeChangedEvent -= value;
      }
    }
    protected void RaiseCanTradeChanged() {
      if (CanTradeChangedEvent != null) CanTradeChangedEvent(this, new EventArgs());
    }
    #endregion

    public bool IsGroupIdEmpty { get { return GroupId == Guid.Empty; } }
    #region GroupId
    private Guid _GroupId = Guid.Empty;
    public Guid GroupId {
      get { return _GroupId; }
      set {
        if (_GroupId != value) {
          _GroupId = value;
          OnPropertyChanged("GroupId");
        }
      }
    }

    #endregion

    #region CrossesCount
    private int _CrossesCount;
    public int CrossesCount {
      get { return _CrossesCount; }
      set {
        if (_CrossesCount != value) {
          _CrossesCount = value;
          OnPropertyChanged("CrossesCount");
        }
      }
    }
    #endregion

    #region Scan Event
    event EventHandler<EventArgs> ScanEvent;
    public event EventHandler<EventArgs> Scan {
      add {
        if (ScanEvent == null || !ScanEvent.GetInvocationList().Contains(value))
          ScanEvent += value;
      }
      remove {
        ScanEvent -= value;
      }
    }
    public void OnScan() {
      if (ScanEvent != null) ScanEvent(this, new EventArgs());
    }
    #endregion


    #region CorridorDate
    private DateTime _CorridorDate;
    public DateTime CorridorDate {
      get { return _CorridorDate; }
      set {
        if (_CorridorDate != value) {
          _CorridorDate = value;
          OnPropertyChanged("CorridorDate");
        }
      }
    }

    #endregion

    private string _EntryOrderId;
    public string EntryOrderId {
      get { return _EntryOrderId; }
      set {
        if (_EntryOrderId != value) {
          var oldId = value != RemovedOrderTag ? "" : _EntryOrderId;
          _EntryOrderId = value == RemovedOrderTag ? "" : value;
           OnEntryOrderIdChanged(_EntryOrderId, oldId);
        }
      }
    }

    #region TradesCountChanging Event
    public class TradesCountChangingEventArgs : EventArgs {
      public double NewValue { get; set; }
      public double OldValue { get; set; }
    }
    event EventHandler<TradesCountChangingEventArgs> TradesCountChangingEvent;
    public event EventHandler<TradesCountChangingEventArgs> TradesCountChanging {
      add {
        if (TradesCountChangingEvent == null || !TradesCountChangingEvent.GetInvocationList().Contains(value))
          TradesCountChangingEvent += value;
      }
      remove {
        TradesCountChangingEvent -= value;
      }
    }
    protected void RaiseTradesCountChanging(double newValue) {
      if (TradesCountChangingEvent != null)
        TradesCountChangingEvent(this, new TradesCountChangingEventArgs { NewValue = newValue, OldValue = TradesCount });
    }
    double _tradesCountPrev = double.NaN;
    partial void OnTradesCountChanging(global::System.Double value) {
      if (_tradesCountPrev == value) return;
      _tradesCountPrev = TradesCount;
      RaiseTradesCountChanging(value);
    }
    #endregion

    #region TradeCountChanged Event
    event EventHandler<EventArgs> TradesCountChangedEvent;
    public event EventHandler<EventArgs> TradesCountChanged {
      add {
        if (TradesCountChangedEvent == null || !TradesCountChangedEvent.GetInvocationList().Contains(value))
          TradesCountChangedEvent += value;
      }
      remove {
        TradesCountChangedEvent -= value;
      }
    }
    protected void RaiseTradesCountChanged() {
      if (_tradesCountPrev == TradesCount) return;
      _tradesCountPrev = TradesCount;
      if (TradesCountChangedEvent != null) TradesCountChangedEvent(this, new EventArgs());
    }
    partial void OnTradesCountChanged() {
      RaiseTradesCountChanged();
    }
    #endregion



    event EventHandler<EntryOrderIdEventArgs> EntryOrderIdChangedEvent;
    public event EventHandler<EntryOrderIdEventArgs> EntryOrderIdChanged {
      add {
        if (EntryOrderIdChangedEvent == null || !EntryOrderIdChangedEvent.GetInvocationList().Contains(value))
          EntryOrderIdChangedEvent += value;
      }
      remove {
        EntryOrderIdChangedEvent -= value;
      }
    }
    void OnEntryOrderIdChanged(string newId,string oldId) {
      if (EntryOrderIdChangedEvent != null) EntryOrderIdChangedEvent(this,new EntryOrderIdEventArgs(newId,oldId));
    }

    EventHandler _IsActiveChanged;
    public event EventHandler IsActiveChanged {
      add {
        if (_IsActiveChanged == null || !_IsActiveChanged.GetInvocationList().Contains(value))
          _IsActiveChanged += value;
      }
      remove {
        _IsActiveChanged -= value;
      }
    }
    protected void OnIsActiveChanged() {
      if (_IsActiveChanged != null)
        _IsActiveChanged(this, EventArgs.Empty);
    }
    EventHandler _rateChangedDelegate;
    public event EventHandler RateChanged {
      add {
        if ( _rateChangedDelegate == null || !_rateChangedDelegate.GetInvocationList().Contains(value))
          _rateChangedDelegate += value;
      }
      remove {
        _rateChangedDelegate -= value;
      }
    }
    partial void OnRateChanged() {
      if (_rateChangedDelegate != null)
        _rateChangedDelegate(this, EventArgs.Empty);
    }
    private int _Index;
    public int Index {
      get { return _Index; }
      set {
        if (_Index != value) {
          _Index = value;
          OnPropertyChanged("Index");
        }
      }
    }
    protected override void OnPropertyChanged(string property) {
      base.OnPropertyChanged(property);
    }

    #region IsExitOnly
    private bool _IsExitOnly;
    public bool IsExitOnly {
      get { return _IsExitOnly; }
      set {
        if (_IsExitOnly != value) {
          _IsExitOnly = value;
          OnPropertyChanged("IsExitOnly");
          if (value) CanTrade = false;
        }
      }
    }

    #endregion
    bool _InManual;
    public bool InManual {
      get { return _InManual; }
      set {
        if (_InManual == value) return;
        _InManual = value;
        OnPropertyChanged("InManual");
      }
    }

    public void ResetPricePosition() { _pricePrev = PricePosition = double.NaN; }
    double _pricePosition = double.NaN;
    public double PricePosition {
      get { return _pricePosition; }
      set {
        if (_pricePosition != value) {
          var prev = _pricePosition;
          _pricePosition = value;
          if (value != 0 && !double.IsNaN(value) && !double.IsNaN(prev))
            RaiseCrossed(value);
        }
      }
    }


    #region Crossed Event
    public void ClearCrossedHandlers() {
      if (CrossedEvent != null)
        CrossedEvent.GetInvocationList().ToList().ForEach(h => CrossedEvent -= h as EventHandler<CrossedEvetArgs>);
    }
    public class CrossedEvetArgs : EventArgs {
      public double Direction { get; set; }
      public CrossedEvetArgs(double direction) {
        this.Direction = direction;
      }
    }
    event EventHandler<CrossedEvetArgs> CrossedEvent;
    public event EventHandler<CrossedEvetArgs> Crossed {
      add {
        if (CrossedEvent == null || !CrossedEvent.GetInvocationList().Contains(value))
          CrossedEvent += value;
      }
      remove {
        if (value == null) {
          if (CrossedEvent != null)
            CrossedEvent.GetInvocationList().Cast<EventHandler<CrossedEvetArgs>>().ForEach(d => CrossedEvent -= d);
        } else
          CrossedEvent -= value;
      }
    }
    protected void RaiseCrossed(double pricePosition) {
      if (CrossedEvent != null) CrossedEvent(this, new CrossedEvetArgs(pricePosition));
    }
    #endregion

    double _pricePrev = double.NaN;
    public void SetPrice(double price) {
      if (double.IsNaN(price))
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(new Exception("price is NaN."));
      else {
        if (Rate.Between(price, _pricePrev)) {
          _pricePosition = _pricePrev - Rate;
        }
        _pricePrev = price;
        PricePosition = (price - Rate).IfNaN(0).Sign();
      }
    }

    public DateTime? TradeDate { get; set; }
  }
  public partial class TradingMacro {

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

    [DisplayName("Calc Method")]
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

    [Category(categoryTestControl)]
    public bool UseTestFile { get; set; }
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
    [Category(categoryActive)]
    public int PriceCmaLevels_ {
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

    #region LogTrades
    bool _logTrades = true;
    [DisplayName("Log Trades")]
    [Category(categoryTrading)]
    public bool LogTrades{
      get { return _logTrades; }
      set {
          _logTrades = value;
          OnPropertyChanged(()=>LogTrades);
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

    [DisplayName("High/Low Method")]
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
    [Category(categoryActive)]
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
    [Category(categoryActiveYesNo)]
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
    private bool IsTradingHour(DateTime time) {
      var hours = TradingHoursRange.Split('-').Select(s => DateTime.Parse(s).Hour).ToArray();
      return hours[0] < hours[1] ? time.Hour.Between(hours[0], hours[1]) : !time.Hour.Between(hours[0] - 1, hours[1] + 1);
    }
    DayOfWeek[] TradingDays() {
      switch (TradingDaysRange) {
        case WeekDays.Full: return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        case WeekDays.MoTh: return new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
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
    WeekDays _TradingDaysRange = WeekDays.Full;

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
    [Category(categoryActive)]
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
    [Category(categoryActive)]
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
    [Description("CanTrade = MagnetPrice.Between CenterOfMass")]
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

    [DisplayName("Median Function")]
    [Category(categoryActiveFuncs)]
    public MedianFunctions MedianFunction {
      get { return (MedianFunctions)ExtreamCloseOffset; }
      set {
        if (ExtreamCloseOffset != (int)value) {
          ExtreamCloseOffset = (int)value;
          OnPropertyChanged("MedianFunction");
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

    [DisplayName("Variance Function")]
    [Category(categoryActiveFuncs)]
    public VarainceFunctions VarianceFunction {
      get { return (VarainceFunctions)CorridorBigToSmallRatio.ToInt(); }
      set {
        if (CorridorBigToSmallRatio != (double)value) {
          CorridorBigToSmallRatio = (double)value;
          OnPropertyChanged("VarianceFunction");
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
        if(CloseOnProfitOnly == value)return;
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
    public bool DoAdjustExitLevelByTradeTime {
      get { return CloseAllOnProfit; }
      set { 
        CloseAllOnProfit = value;
        OnPropertyChanged("DoAdjustExitLevelByTradeTime");
      }
    }

    const string categoryXXX = "XXX";
    const string categoryXXX_NU = "XXX_NU";
    const string categoryCorridor = "Corridor";
    const string categoryTrading = "Trading";
    public const string categoryActive = "Active";
    public const string categoryActiveYesNo = "Active Yes or No";
    public const string categoryActiveFuncs = "Active Funcs";
    public const string categoryTest = "Test";
    public const string categoryTestControl = "Test Control";
    public const string categorySession = "Session";

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

    [Category(categoryActive)]
    [DisplayName("CorridorCrossesMaximum")]
    [Description("_buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum")]
    public int CorridorCrossesMaximum {
      get { return CorridorRatioForBreakout.ToInt(); }
      set { 
        CorridorRatioForBreakout = value;
        OnPropertyChanged(Metadata.TradingMacroMetadata.CorridorCrossesMaximum);
      }
    }

    [DisplayName("CorridorHeight Max")]
    [Category(categoryActive)]
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
    [Category(categoryActive)]
    [DisplayName("Eval")]
    [Description("Ex:a.upPeak+a.downValley>0")]
    public string Eval {
      get { return FibMax; }
      set {
        if (FibMax == value) return;
        FibMax = value;
        OnPropertyChanged(() => Eval);
      }
    }

    [Category(categoryActiveYesNo)]
    [DisplayName("Trading Ratio By PMC")]
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
    [Category(categoryActive)]
    public double TradingAngleRange_ {
      get { return TradingAngleRange; }
      set {
        if (TradingAngleRange == value) return;
        TradingAngleRange = value;
        OnPropertyChanged(() => TradingAngleRange_);
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
        case TradeCrossMethod.PriceCMA: return r => r.PriceCMALast;
        case TradeCrossMethod.ChartAskBid:
          if (!isBuy.HasValue) throw new NotSupportedException(new { method, isBuy } + " is not supported.");
          if (isBuy.Value) return r => r.PriceChartAsk; else return r => r.PriceChartBid;
        case TradeCrossMethod.PriceCurr:
          if (!isBuy.HasValue) return _ => CurrentPrice.Average;
          if (isBuy.Value) return _ => CurrentPrice.Ask; else return _ => CurrentPrice.Bid;
      }
      throw new NotSupportedException(method.GetType().Name + "." + method + " is not supported");
    }

    [DisplayName("Trade Enter By")]
    [Category(categoryActiveFuncs)]
    public TradeCrossMethod TradeEnterBy {
      get { return (TradeCrossMethod)BarPeriodsHigh; }
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
    [Category(categoryActiveFuncs)]
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
    [Category(categoryActive)]
    public double WaveStDevRatio {
      get { return SpreadShortToLongTreshold; }
      set {
        SpreadShortToLongTreshold = value;
        OnPropertyChanged(() => WaveStDevRatio);
      }
    }

    [DisplayName("Corridor Height By")]
    [Category(categoryActiveFuncs)]
    public CorridorHeightMethods CorridorHeightMethod {
      get { return (CorridorHeightMethods)CorridorIterationsOut; }
      set {
        if (CorridorIterationsOut == (int)value) return;
        CorridorIterationsOut = (int)value;
        OnPropertyChanged(() => CorridorHeightMethod);
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
          OnPropertyChanged(TradingMacroMetadata.BarsCount);
        }
      }
    }

    [DisplayName("Spearman Volatility")]
    [Category(categoryActiveYesNo)]
    public bool UseSpearmanVolatility {
      get { return DoAdjustTimeframeByAllowedLot; }
      set {
        if (DoAdjustTimeframeByAllowedLot != value) {
          DoAdjustTimeframeByAllowedLot = value;
          OnPropertyChanged("UseSpearmanVolatility");
        }
      }
    }

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
    [Category(categoryActiveFuncs)]
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
    #region StDevTresholdIterations
    [DisplayName("Distance Iterations")]
    [Description("Math.Pow(height,N)")]
    [Category(categoryActive)]
    public double DistanceIterations {
      get { return CorrelationTreshold; }
      set {
        if (CorrelationTreshold != value) {
          CorrelationTreshold = value;
          OnPropertyChanged("DistanceIterations");
        }
      }
    }
    #endregion

    #region PolyOrder 
    [Category(categoryActive)]
    public int PolyOrder {
      get { return StDevTresholdIterations; }
      set {
        if (StDevTresholdIterations != value) {
          StDevTresholdIterations = value;
          OnPropertyChanged(() => PolyOrder);
        }
      }
    }
    partial void OnStDevTresholdIterationsChanged() {
      OnPropertyChanged(() => PolyOrder);
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

    #region ClosePriceMode
    [Category(categoryTrading)]
    public ClosePriceMode ClosePriceMode {
      get { return Price.ClosePriceMode; }
      set {
        if (Price.ClosePriceMode != value) {
          Price.ClosePriceMode = value;
          OnPropertyChanged("ClosePriceMode");
        }
      }
    }

    #endregion

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
        return IsInVitualTrading
          ? _Rates.Count > 0
          ? _Rates.LastBC().StartDate.AddMinutes(BarPeriodInt)
          : DateTime.MinValue 
          : TradesManager == null || !TradesManager.IsLoggedIn ? DateTime.MinValue 
          : TradesManager.ServerTime;
      }
    }
    Price GetVirtualCurrentPrice() {
      try {
        var rate = RatesArray.LastOrDefault();
        return new Price(Pair, rate, ServerTime, PointSize, TradesManager.GetDigits(Pair), true);
      } catch {
        throw;
      }
    }
    double? _currentSpread;
    Price _currentPrice;
    public Price CurrentPrice {
      get {
        return IsInVitualTrading ? GetVirtualCurrentPrice() : _currentPrice;
      }
      set {
        _currentPrice = value;
        if (RateLast != null && !double.IsNaN(RateLast.PriceAvg1)) {
          if(!IsInVitualTrading)
            RateLast.AddTick(_currentPrice);
          RateLast.SetPriceChart();
        }
        OnPropertyChanged(TradingMacroMetadata.CurrentPrice);
        var currentSpread = RoundPrice(Lib.Cma(this._currentSpread, 10, this._currentPrice.Spread));
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
  }
}
