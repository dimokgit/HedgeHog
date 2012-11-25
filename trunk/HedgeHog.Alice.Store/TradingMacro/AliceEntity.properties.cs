using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using HedgeHog.Bars;
using HedgeHog.Alice.Store.Metadata;
using HedgeHog.Shared;
using HedgeHog.Models;

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
    #region CanTrade
    private bool _CanTrade;
    public bool CanTrade {
      get { return _CanTrade; }
      set {
        if (_CanTrade != value) {
          _CanTrade = value;
          OnPropertyChanged("CanTrade");
          RaiseCanTradeChanged();
        }
      }
    }
    #endregion
    int _rateExErrorCounter = 0;// This is to ammend some wierd bug in IEntityChangeTracker.EntityMemberChanged or something that it calls
    public double RateEx {
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

    public bool InManual { get; set; }

    public void ResetPricePosition() { PricePosition = double.NaN; }
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
        CrossedEvent -= value;
      }
    }
    protected void RaiseCrossed(double pricePosition) {
      if (CrossedEvent != null) CrossedEvent(this, new CrossedEvetArgs(pricePosition));
    }
    #endregion

    public void SetPrice(double price) {
        PricePosition = (price - Rate).Sign();
    }

  }
  public partial class TradingMacro {

    TradingStatistics _tradingStatistics = new TradingStatistics();
    public TradingStatistics TradingStatistics {
      get { return _tradingStatistics; }
      set { _tradingStatistics = value; }
    }

    #region MonthsOfHistory
    private int _MonthsOfHistory;
    [DisplayName("MonthsOfHistory")]
    [Category(categoryXXX)]
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
    [Category(categoryActive)]
    public CorridorCalculationMethod CorridorCalcMethod {
      get { return (CorridorCalculationMethod)this.CorridorMethod; }
      set {
        if (this.CorridorMethod != (int)value) {
          this.CorridorMethod = (int)value;
          OnPropertyChanged(TradingMacroMetadata.CorridorCalcMethod);
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

    [Category(categoryTest)]
    public bool UseTestFile { get; set; }

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

    public bool TestSuperSessionUidIsEmpty { get { return TestSuperSessionUid == Guid.Empty || TestSuperSessionUid == 1.Guid(); } }
    private bool _TestSuperSessionUidError = false;
    private Guid _testSuperSessionUidDefault = 1.Guid();
    private Guid _TestSuperSessionUid;
    public Guid TestSuperSessionUid { get { return _TestSuperSessionUid; } }
    [DisplayName("SuperSession Uid")]
    [Category(categoryTest)]
    public string TestSuperSessionUid_ {
      get {
        return _TestSuperSessionUid.ToString();
        if (_TestSuperSessionUid == Guid.Empty && !_TestSuperSessionUidError)
          try {
            _TestSuperSessionUid = GlobalStorage.UseForexContext(c =>
              c.t_Session.ToArray().Where(s => s.SuperUid.GetValueOrDefault(Guid.Empty) != Guid.Empty)
              .OrderByDescending(s => s.Timestamp).Select(s => s.SuperUid.Value)
              .DefaultIfEmpty(_testSuperSessionUidDefault).First());
          } catch (Exception exc) {
            GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
            _TestSuperSessionUidError = true;
          }
      }
      set {
        Guid v = Guid.Empty;
        Guid.TryParse(value, out v);
        if (_TestSuperSessionUid != v) {
          _TestSuperSessionUid = v;
          OnPropertyChanged("TestSuperSessionUid_");
          OnPropertyChanged("TestSuperSessionUid");
        }
      }
    }

    #endregion

    string _TestStopRateWaveOffset = "";
    [DisplayName("Stop Rate Wave Offset")]
    [Category(categoryTest)]
    public string TestStopRateWaveOffset {
      get { return _TestStopRateWaveOffset; }
      set {
        if (_TestStopRateWaveOffset != value) {
          _TestStopRateWaveOffset = value;
          OnPropertyChanged("TestStopRateWaveOffset");
        }
      }
    }

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
    [Category(categoryXXX)]
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
    bool _logTrades;
    [DisplayName("Log Trades")]
    [Category(categoryActive)]
    public bool LogTRades{
      get { return _logTrades; }
      set {
          _logTrades = value;
          OnPropertyChanged("LohTrades");
      }
    }

    #endregion

    #region ForceOpenTrade
    private bool? _ForceOpenTrade;
    [DisplayName("Force Open Trade")]
    [Category(categoryTrading)]
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
    [Category(categoryCorridor)]
    public CorridorHighLowMethod CorridorHighLowMethod {
      get { return (CorridorHighLowMethod)CorridorHighLowMethodInt; }
      set {
        CorridorHighLowMethodInt = (int)value;
        OnPropertyChanged(TradingMacroMetadata.CorridorHighLowMethod);
      }
    }

    [DisplayName("MaxLot By TakeProfit Ratio")]
    [Description("MaxLotSize < LotSize*N")]
    [Category(categoryTrading)]
    public double MaxLotByTakeProfitRatio_ {
      get { return MaxLotByTakeProfitRatio; }
      set {
        MaxLotByTakeProfitRatio = value;
        OnPropertyChanged(TradingMacroMetadata.MaxLotByTakeProfitRatio_);
      }
    }

    [DisplayName("Scan Corridor By")]
    [Category(categoryActive)]
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
    [Category(categoryActive)]
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
    [Category(categoryActive)]
    [Description("TradingDistanceFunction")]
    public TradingMacroTakeProfitFunction TradingDistanceFunction {
      get { return (TradingMacroTakeProfitFunction)PowerRowOffset; }
      set {
        PowerRowOffset = (int)value;
        OnPropertyChanged(TradingMacroMetadata.TradingDistanceFunction);
      }
    }


    [DisplayName("Take Profit")]
    [Category(categoryActive)]
    [Description("TakeProfitFunction")]
    public TradingMacroTakeProfitFunction TakeProfitFunction {
      get { return (TradingMacroTakeProfitFunction)TakeProfitFunctionInt; }
      set { 
        TakeProfitFunctionInt = (int)value;
        OnPropertyChanged(TradingMacroMetadata.TakeProfitFunction);
      }
    }

    [DisplayName("Symmetrical Buy/Sell")]
    [Description("Move Buy level up when Sell moves down.")]
    [Category(categoryTrading)]
    public bool SymmetricalBuySell {
      get { return TradeOnCrossOnly; }
      set { TradeOnCrossOnly = value; }
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
    [Category(categoryXXX_NU)]
    public bool DoStreatchRates_ {
      get { return DoStreatchRates; }
      set { DoStreatchRates = value; }
    }

    [DisplayName("Corridor Follows Price")]
    [Category(categoryCorridor)]
    public bool CorridorFollowsPrice {
      get { return StrictTradeClose; }
      set { StrictTradeClose = value; }
    }

    [DisplayName("WaveAverage Iteration")]
    [Category(categoryActive)]
    public double WaveAverageIteration {
      get { return SpreadShortToLongTreshold; }
      set {
        if (SpreadShortToLongTreshold == value) return;
        SpreadShortToLongTreshold = value;
        OnPropertyChanged(TradingMacroMetadata.WaveAverageIteration);
      }
    }

    private bool IsTradingHour(DateTime time) {
      var hours = TradingHoursRange.Split('-').Select(s => DateTime.Parse(s).Hour).ToArray();
      return hours[0] < hours[1] ? time.Hour.Between(hours[0], hours[1]) : !time.Hour.Between(hours[0], hours[1]);
    }
    [DisplayName("Trading Hours Range")]
    [Category(categoryActive)]
    public string TradingHoursRange {
      get { return CorridorIterations; }
      set {
        if (CorridorIterations == value) return;
        CorridorIterations = value;
        OnPropertyChanged(TradingMacroMetadata.TradingHoursRange);
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

    #region StDevAverateRatioThreshold
    private double _StDevAverateRatioMin;
    [DisplayName("StDev Averate Ratio Min")]
    [Category(categoryTrading)]
    public double StDevAverateRatioMin {
      get { return _StDevAverateRatioMin; }
      set {
        if (_StDevAverateRatioMin != value) {
          _StDevAverateRatioMin = value;
          OnPropertyChanged("StDevAverateRatioMin");
        }
      }
    }

    #endregion

    #region StDevAverateRatioMax
    private double _StDevAverateRatioMax;
    [DisplayName("StDev Averate Ratio Max")]
    [Category(categoryTrading)]
    public double StDevAverateRatioMax {
      get { return _StDevAverateRatioMax; }
      set {
        if (_StDevAverateRatioMax != value) {
          _StDevAverateRatioMax = value;
          OnPropertyChanged("StDevAverateRatioMax");
        }
      }
    }

    #endregion

    [DisplayName("PLToCorridorExitRatio")]
    [Category(categoryXXX_NU)]
    [Description("exit = PL * X > CorridorHeight")]
    public double PLToCorridorExitRatio {
      get { return StDevToSpreadRatio; }
      set {
        if (StDevToSpreadRatio != value) {
          StDevToSpreadRatio = value;
          OnPropertyChanged("PLToCorridorExitRatio");
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
        }
      }
    }

    [DisplayName("Extream Close Offset")]
    [Category(categoryXXX)]
    [Description("Extream Close Offset in pips")]
    public int ExtreamCloseOffset_ {
      get { return ExtreamCloseOffset; }
      set {
        if (ExtreamCloseOffset != value) {
          ExtreamCloseOffset = value;
          OnPropertyChanged(TradingMacroMetadata.ExtreamCloseOffset_);
        }
      }
    }

    [DisplayName("Current Loss Close Adjustment")]
    [Category(categoryXXX)]
    [Description("CurrentLossInPips * 0.9")]
    public double CurrentLossInPipsCloseAdjustment_ {
      get { return CurrentLossInPipsCloseAdjustment; }
      set {
        if (CurrentLossInPipsCloseAdjustment != value) {
          CurrentLossInPipsCloseAdjustment = value;
          OnPropertyChanged(TradingMacroMetadata.CurrentLossInPipsCloseAdjustment_);
        }
      }
    }

    [DisplayName("MagnetCrossMinimum")]
    [Category(categoryXXX_NU)]
    [Description("Not Used")]
    public int MagnetCrossMinimum {
      get { return CorridorBigToSmallRatio.ToInt(); }
      set {
        if (CorridorBigToSmallRatio != value) {
          CorridorBigToSmallRatio = value;
          OnPropertyChanged(TradingMacroMetadata.MagnetCrossMinimum);
        }
      }
    }

    [DisplayName("Streatch TakeProfit")]
    [Category(categoryXXX_NU)]
    [Description("Ex: TakeProfitCurr = TakeProfit + CurrentLoss")]
    public bool StreatchTakeProfit {
      get { return StreachTradingDistance; }
      set { 
        StreachTradingDistance = value;
        OnPropertyChanged(TradingMacroMetadata.StreatchTakeProfit);
      }
    }

    [DisplayName("Close On Open Only")]
    [Category(categoryTrading)]
    [Description("Close position only when opposite opens.")]
    public bool CloseOnOpen_ {
      get { return CloseOnOpen; }
      set { 
        CloseOnOpen = value;
        OnPropertyChanged(TradingMacroMetadata.CloseOnOpen_);
      }
    }

    [DisplayName("Close On Profit")]
    [Category(categoryTrading)]
    [Description("Ex: if( PL > Limit) CloseTrade()")]
    public bool CloseOnProfit_ {
      get { return CloseOnProfit; }
      set { CloseOnProfit = value; }
    }

    [DisplayName("Close On Profit Only")]
    [Category(categoryActive)]
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
    [Category(categoryTrading)]
    public bool ReverseStrategy_ {
      get { return ReverseStrategy; }
      set {
        if (ReverseStrategy == value) return;
        ReverseStrategy = value;
        OnPropertyChanged(Metadata.TradingMacroMetadata.ReverseStrategy_);
      }
    }


    [DisplayName("Close All On Profit")]
    [Category(categoryTrading)]
    [Description("Ex: if(trade.PL > Profit) ClosePair()")]
    public bool CloseAllOnProfit_ {
      get { return CloseAllOnProfit; }
      set { CloseAllOnProfit = value; }
    }

    const string categoryXXX = "XXX";
    const string categoryXXX_NU = "XXX_NU";
    const string categoryCorridor = "Corridor";
    const string categoryTrading = "Trading";
    public const string categoryActive = "Active";
    public const string categoryTest = "Test";
    public const string categorySession = "Session";

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
    [Category(categoryXXX_NU)]
    [DisplayName("RatesToStDev Min")]
    public double RatesToStDevRatioMinimum {
      get { return CorridorRatioForRange; }
      set { 
        CorridorRatioForRange = value;
        OnPropertyChanged("RatesToStDevRatioMinimum");
      }
    }

    [DisplayName("RatesToStDev Max")]
    [Category(categoryXXX_NU)]
    public double RatesToStDevRatioMaximum {
      get { return FibMin; }
      set {
        FibMin = value;
        OnPropertyChanged("RatesToStDevRatioMaximum");
      }
    }

    [Category(categoryActive)]
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
    [DisplayName("RatesHeight Min")]
    public double RatesHeightMinimum {
      get { return CorrelationTreshold; }
      set {
        if (CorrelationTreshold == value) return;
        CorrelationTreshold = value;
        OnPropertyChanged("RatesHeightMinimum");
        OnPropertyChanged("RatesHeightMinimumInPips");
      }
    }
    public double RatesHeightMinimumInPips { get { return InPips(RatesHeightMinimum); } }

    [Category(categoryXXX)]
    [DisplayName("Range Ratio For TradeLimit")]
    [Description("Not in use.")]
    public double RangeRatioForTradeLimit_ {
      get { return RangeRatioForTradeLimit; }
      set { 
        RangeRatioForTradeLimit = value;
        OnPropertyChanged(TradingMacroMetadata.RangeRatioForTradeLimit_);
      }
    }

    [Category(categoryXXX)]
    [DisplayName("Range Ratio For TradeStop")]
    [Description("Ex:Exit when PL < -Range * X")]
    public double RangeRatioForTradeStop_ {
      get { return RangeRatioForTradeStop; }
      set { 
        RangeRatioForTradeStop = value;
        OnPropertyChanged(TradingMacroMetadata.RangeRatioForTradeStop_);
      }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade By Angle")]
    public bool TradeByAngle_ {
      get { return TradeByAngle; }
      set { TradeByAngle = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade And Angle Are Synced")]
    public bool TradeAndAngleSynced_ {
      get { return TradeAndAngleSynced; }
      set { TradeAndAngleSynced = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade By First Wave")]
    [Description("If not - will trade by last wave")]
    public bool? TradeByFirstWave_ {
      get { return TradeByFirstWave; }
      set { TradeByFirstWave = value; }
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


    [DisplayName("Price CMA Period")]
    [Category(categoryActive)]
    public int PriceCmaPeriod {
      get { return LongMAPeriod; }
      set {
        if (LongMAPeriod == value) return;
        LongMAPeriod = value;
        OnPropertyChanged(TradingMacroMetadata.PriceCmaPeriod);
      }
    }

    [DisplayName("Trading Angle Range")]
    [Category(categoryTrading)]
    public double TradingAngleRange_ {
      get { return TradingAngleRange; }
      set {
        if (TradingAngleRange == value) return;
        TradingAngleRange = value;
        OnPropertyChanged(TradingMacroMetadata.TradingAngleRange_);
      }
    }

    [DisplayName("Reset On Balance")]
    [Category(categoryTrading)]
    public double ResetOnBalance_ {
      get { return ResetOnBalance.GetValueOrDefault(0); }
      set {
        if (ResetOnBalance == value) return;
        ResetOnBalance = value;
        OnPropertyChanged(TradingMacroMetadata.ResetOnBalance_);
      }
    }

    [DisplayName("Trade By Rate Direction")]
    [Category(categoryTrading)]
    public bool TradeByRateDirection_ {
      get { return TradeByRateDirection; }
      set { TradeByRateDirection = value; }
    }

    [DisplayName("Close By Momentum")]
    [Category(categoryTrading)]
    [Description("Close trade when rate changes direction.")]
    public bool CloseByMomentum_ {
      get { return CloseByMomentum; }
      set { CloseByMomentum = value; }
    }

    [DisplayName("Corr Height/Spread - Low")]
    [Category(categoryXXX)]
    [Description("Lock buy/sell when H/S < X/10")]
    public double CorridorHeightToSpreadRatioLow {
      get { return BarPeriodsLow / 10.0; }
      set { BarPeriodsLow = (int)value; }

    }

    [DisplayName("Price CMA Value")]
    [Category(categoryActive)]
    [Description("MovingAverageValue enum")]
    public MovingAverageValues MovingAverageValue {
      get { return (MovingAverageValues)BarPeriodsHigh; }
      set {
        if (BarPeriodsHigh != (int)value) {
          BarPeriodsHigh = (int)value;
          OnPropertyChanged(() => MovingAverageValue);
        }
      }
    }


    [DisplayName("Blackout Hours")]
    [Category(categoryActive)]
    public int BlackoutHoursTimeframe {
      get { return CorridorIterationsIn; }
      set {
        CorridorIterationsIn = value;
        OnPropertyChanged("BlackoutHoursTimeframe");
      }
    }
    [DisplayName("StopRate Wave Offset")]
    [Category(categoryActive)]
    public int StopRateWaveOffset {
      get { return CorridorIterationsOut; }
      set {
        CorridorIterationsOut = value;
        OnPropertyChanged("StopRateWaveOffset");
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

    bool _ShowTrendLines = true;
    [DisplayName("Show Trend Lines")]
    [Category(categoryCorridor)]
    public bool ShowTrendLines {
      get { return _ShowTrendLines; }
      set { _ShowTrendLines = value; }
    }

    #region MaximumPositions
    [DisplayName("Maximum Positions")]
    [Category(categoryActive)]
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
    [Category(categoryActive)]
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

    [DisplayName("Adjust TimeframeBy Lot")]
    [Description("Do Adjust Timeframe By Allowed Lot")]
    [Category(categoryXXX)]
    public bool DoAdjustTimeframeByAllowedLot_ {
      get { return DoAdjustTimeframeByAllowedLot; }
      set {
        if (DoAdjustTimeframeByAllowedLot != value) {
          DoAdjustTimeframeByAllowedLot = value;
          OnPropertyChanged(TradingMacroMetadata.DoAdjustTimeframeByAllowedLot_);
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
    [Category(categoryActive)]
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
    #region VolumeTresholdIterations
    [DisplayName("Corridor Minimum Ratio")]
    [Description("Cooridor.Rates.Count > Rates.Count/N")]
    [Category(categoryCorridor)]
    public int CorridorMinimumRatio {
      get { return VolumeTresholdIterations; }
      set {
        if (VolumeTresholdIterations != value) {
          VolumeTresholdIterations = value;
          OnPropertyChanged("VolumeTresholdIterations_");
        }
      }
    }

    #endregion
    #region StDevTresholdIterations
    [DisplayName("StDev Iterations")]
    [Description("StDev Treshold Iterations")]
    [Category(categoryActive)]
    public int StDevTresholdIterations_ {
      get { return StDevTresholdIterations; }
      set {
        if (StDevTresholdIterations != value) {
          StDevTresholdIterations = value;
          OnPropertyChanged("StDevTresholdIterations_");
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

    Price GetVirtualCurrentPrice() {
      try {
        var rate = RatesArray.LastOrDefault();
        return new Price(Pair, rate, TradesManager.ServerTime, PointSize, TradesManager.GetDigits(Pair), true);
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
  }
}
