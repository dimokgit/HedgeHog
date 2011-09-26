using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using HedgeHog.Bars;
using HedgeHog.Alice.Store.Metadata;
using HedgeHog.Shared;

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
    [Category(categoryCorridor)]
    public CorridorCalculationMethod CorridorCalcMethod {
      get { return (CorridorCalculationMethod)this.CorridorMethod; }
      set {
        if (this.CorridorMethod != (int)value) {
          this.CorridorMethod = (int)value;
          OnPropertyChanged(TradingMacroMetadata.CorridorCalcMethod);
        }
      }
    }

    #region PriceCmaLevels
    [DisplayName("Price Cma Levels")]
    [Category(categoryXXX)]
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

    [DisplayName("Corridor Crosses Count")]
    [Description("Corridor Crosses Count Minimum")]
    [Category(categoryCorridor)]
    public int CorridorCrossesCountMinimum_ {
      get { return CorridorCrossesCountMinimum; }
      set {
        CorridorCrossesCountMinimum = value;
        OnPropertyChanged(TradingMacroMetadata.CorridorCrossesCountMinimum_);
      }
    }

    [DisplayName("Trading Distance Function")]
    [Category(categoryActive)]
    public TradingMacroTakeProfitFunction TradingDistanceFunction {
      get { return (TradingMacroTakeProfitFunction)PowerRowOffset; }
      set {
        PowerRowOffset = (int)value;
        OnPropertyChanged(TradingMacroMetadata.TradingDistanceFunction);
      }
    }


    [DisplayName("Take Profit Function")]
    [Category(categoryActive)]
    public TradingMacroTakeProfitFunction TakeProfitFunction {
      get { return (TradingMacroTakeProfitFunction)TakeProfitFunctionInt; }
      set { 
        TakeProfitFunctionInt = (int)value;
        OnPropertyChanged(TradingMacroMetadata.TakeProfitFunction);
      }
    }
    
    [DisplayName("Trade On Cross Only")]
    [Category(categoryTrading)]
    public bool TradeOnCrossOnly_ {
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
    [Category(categoryCorridor)]
    public bool DoStreatchRates_ {
      get { return DoStreatchRates; }
      set { DoStreatchRates = value; }
    }

    [DisplayName("Trade On Level Cross")]
    [Category(categoryXXX)]
    public bool TradeOnLevelCrossOnly {
      get { return StrictTradeClose; }
      set { StrictTradeClose = value; }
    }

    [DisplayName("Density Min")]
    [Category(categoryCorridor)]
    public double DensityMin {
      get { return SpreadShortToLongTreshold; }
      set {
        if (SpreadShortToLongTreshold == value) return;
        SpreadShortToLongTreshold = value;
        OnPropertyChanged(TradingMacroMetadata.DensityMin);
      }
    }

    [DisplayName("SuppRes Levels Count")]
    [Category(categoryCorridor)]
    public int SuppResLevelsCount_ {
      get { return SuppResLevelsCount; }
      set { SuppResLevelsCount = value; }
    }

    [DisplayName("StDev Ratio Min")]
    [Category(categoryCorridor)]
    [Description("BigCorr.StDev / CurrCorr.StDev")]
    public double CorridorStDevRatioMin {
      get { return StDevToSpreadRatio; }
      set {
        if (StDevToSpreadRatio != value) {
          StDevToSpreadRatio = value;
          OnPropertyChanged(TradingMacroMetadata.CorridorStDevRatioMin);
        }
      }
    }

    [DisplayName("StDev Ratio Max")]
    [Category(categoryCorridor)]
    [Description("BigCorr.StDev / CurrCorr.StDev")]
    public double CorridorStDevRatioMax_ {
      get { return CorridorStDevRatioMax; }
      set {
        if (CorridorStDevRatioMax != value) {
          CorridorStDevRatioMax = value;
          OnPropertyChanged(TradingMacroMetadata.CorridorStDevRatioMax_);
        }
      }
    }



    [DisplayName("Corridor Height Multiplier")]
    [Category(categoryCorridor)]
    [Description("Ex: CorrHeighMin = SpreadMax * X")]
    public double CorridorHeightMultiplier {
      get { return CorridornessMin; }
      set { 
        CorridornessMin = value;
        OnPropertyChanged(TradingMacroMetadata.CorridorHeightMultiplier);
      }
    }

    [DisplayName("Streach Trading Distance")]
    [Category(categoryTrading)]
    [Description("Ex: PL < tradingDistance * (X ? trades.Length:1)")]
    public bool StreachTradingDistance_ {
      get { return StreachTradingDistance; }
      set { 
        StreachTradingDistance = value;
        OnPropertyChanged(TradingMacroMetadata.StreachTradingDistance_);
      }
    }

    [DisplayName("Close On Open Only")]
    [Category(categoryActive)]
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
    [Category(categoryTrading)]
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
    const string categoryCorridor = "Corridor";
    const string categoryTrading = "Trading";
    public const string categoryActive = "Active";

    [Category(categoryXXX)]
    [DisplayName("Ratio For Breakout")]
    public double CorridorRatioForBreakout_ {
      get { return CorridorRatioForBreakout; }
      set { CorridorRatioForBreakout = value; }
    }
    [Category(categoryXXX)]
    [DisplayName("Ratio For Range")]
    [Description("Minimum Ratio to use Range strategy.")]
    public double CorridorRatioForRange_ {
      get { return CorridorRatioForRange; }
      set { CorridorRatioForRange = value; }
    }

    [Category(categoryXXX)]
    [DisplayName("Reverse Power")]
    [Description("Calc power from rates.OrderBarsDescending().")]
    public bool ReversePower_ {
      get { return ReversePower; }
      set { ReversePower = value; }
    }


    [Category(categoryXXX)]
    [DisplayName("Correlation Treshold")]
    [Description("Ex: if(Corr >  X) return sell")]
    public double CorrelationTreshold_ {
      get { return CorrelationTreshold; }
      set { CorrelationTreshold = value; }
    }

    [Category(categoryCorridor)]
    [DisplayName("Range Ratio For TradeLimit")]
    [Description("Ex:Exit when PL > Range * X")]
    public double RangeRatioForTradeLimit_ {
      get { return RangeRatioForTradeLimit; }
      set { 
        RangeRatioForTradeLimit = value;
        OnPropertyChanged(TradingMacroMetadata.RangeRatioForTradeLimit_);
      }
    }

    [Category(categoryCorridor)]
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
    [Category(categoryXXX)]
    public int PriceCmaPeriod {
      get { return LongMAPeriod; }
      set {
        if (LongMAPeriod == value) return;
        LongMAPeriod = value;
        OnPropertyChanged(TradingMacroMetadata.PriceCmaPeriod);
      }
    }

    [DisplayName("Trading Angle Range")]
    [Category(categoryXXX)]
    public double TradingAngleRange_ {
      get { return TradingAngleRange; }
      set {
        if (TradingAngleRange == value) return;
        TradingAngleRange = value;
        OnPropertyChanged(TradingMacroMetadata.TradingAngleRange_);
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

    [DisplayName("Corridor Minimum Length Ratio")]
    [Category(categoryActive)]
    [Description("corr.Perios < rates.Periods * X/10")]
    public int CorridorMinimumLengthRatio {
      get { return BarPeriodsHigh; }
      set {
        if (BarPeriodsHigh != value) {
          BarPeriodsHigh = value;
          OnPropertyChanged(TradingMacroMetadata.CorridorMinimumLengthRatio);
        }
      }
    }


    [DisplayName("StDev Level Lock")]
    [Category(categoryActive)]
    public int StDevLevelLock {
      get { return CorridorIterationsIn; }
      set {
        CorridorIterationsIn = value;
        OnPropertyChanged(TradingMacroMetadata.StDevLevelLock);
      }
    }
    [DisplayName("StDev Level Load")]
    [Category(categoryActive)]
    public int StDevLevelLoad {
      get { return CorridorIterationsOut; }
      set {
        CorridorIterationsOut = value;
        OnPropertyChanged(TradingMacroMetadata.StDevLevelLoad);
      }
    }



    [DisplayName("Corridor StDev To SpreadMin")]
    [Description("CorridorStDev/SpreadMin > X")]
    [Category(categoryActive)]
    public double CorridorStDevToSpreadMin {
      get { return FibMin; }
      set { 
        FibMin = value;
        OnPropertyChanged(TradingMacroMetadata.CorridorStDevToSpreadMin);
      }
    }

    [Category(categoryCorridor)]
    [DisplayName("Is SuppRes Manual")]
    public bool IsSuppResManual_ {
      get { return IsSuppResManual; }
      set { IsSuppResManual = value; }
    }

    [DisplayName("Is Gann Angles Manual")]
    [Category(categoryCorridor)]
    public bool IsGannAnglesManual_ {
      get { return IsGannAnglesManual; }
      set { IsGannAnglesManual = value; }
    }

    [DisplayName("Is Cold On Trades")]
    [Description("Is not Hot when has trades")]
    [Category(categoryTrading)]
    public bool IsColdOnTrades_ {
      get { return IsColdOnTrades; }
      set {
        if (IsColdOnTrades == value) return;
        IsColdOnTrades = value;
        OnPropertyChanged(TradingMacroMetadata.IsColdOnTrades_);
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

    public bool IsHotStrategy { get { return Strategy.HasFlag(Strategies.Hot); } }
    public bool IsAutoStrategy { get { return Strategy.HasFlag(Strategies.Auto); } }
    public bool IsCold { get { return IsColdOnTrades && Trades.Length > 0; } }

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

    public Freezing FreezeType {
      get { return (Freezing)this.FreezLimit; }
      set {
        if (this.FreezLimit != (int)value) {
          this.FreezLimit = (int)value;
          OnPropertyChanged(TradingMacroMetadata.FreezeType);
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

    double? _currentSpread;
    Price _currentPrice;
    public Price CurrentPrice {
      get { return _currentPrice; }
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
  }
}
