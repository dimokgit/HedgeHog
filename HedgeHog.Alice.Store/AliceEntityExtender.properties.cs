using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using HedgeHog.Bars;
using HedgeHog.Alice.Store.Metadata;

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

    [DisplayName("Take Profit Function")]
    [Category(categoryTrading)]
    public TradingMacroTakeProfitFunction TakeProfitFunction {
      get { return (TradingMacroTakeProfitFunction)TakeProfitFunctionInt; }
      set { 
        TakeProfitFunctionInt = (int)value;
        OnPropertyChanged(TradingMacroMetadata.TakeProfitFunction);
      }
    }

    [DisplayName("Trades Count Buy")]
    [Description("Reset TradesCount Buy")]
    [Category(categoryCorridor)]
    public double TradesCountBuy {
      get { return GetTradesCountFromSuppRes(true); }
      set {
        if (GetTradesCountFromSuppRes(true) != value) {
          SuppResResetTradeCounts(Resistances, value);
          OnPropertyChanged(Metadata.TradingMacroMetadata.TradesCountBuy);
        }
      }
    }

    
    [DisplayName("Trades Count Sell")]
    [Description("Reset TradesCount Sell")]
    [Category(categoryCorridor)]
    public double TradesCountSell {
      get { return GetTradesCountFromSuppRes(false); }
      set {
        if (GetTradesCountFromSuppRes(true) != value) {
          SuppResResetTradeCounts(Supports, value);
          OnPropertyChanged(Metadata.TradingMacroMetadata.TradesCountSell);
        }
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


    [DisplayName("StDev Levels")]
    [Description("StDev Levels - .5,2,2.5")]
    [Category(categoryCorridor)]
    public string FibMax_ {
      get { return FibMax; }
      set { FibMax = value; }
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

    [DisplayName("Spread Short/Long Treshold")]
    [Category(categoryXXX)]
    public double SpreadShortToLongTreshold_ {
      get { return SpreadShortToLongTreshold; }
      set { SpreadShortToLongTreshold = value; }
    }

    [DisplayName("SuppRes Level Type")]
    [Category(categoryTrading)]
    public LevelType LevelType_ {
      get { return (LevelType)LevelType; }
      set { LevelType = (int)value; }
    }

    [DisplayName("SuppRes Levels Count")]
    [Category(categoryCorridor)]
    public int SuppResLevelsCount_ {
      get { return SuppResLevelsCount; }
      set { SuppResLevelsCount = value; }
    }

    [DisplayName("SuppRes Level Type Iterations")]
    [Category(categoryTrading)]
    public int IterationsForSuppResLevels_ {
      get { return IterationsForSuppResLevels; }
      set { 
        IterationsForSuppResLevels = value;
        OnPropertyChanged(TradingMacroMetadata.IterationsForSuppResLevels_);
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
    [Category(categoryTrading)]
    [Description("Close position only when opposite opens.")]
    public bool CloseOnOpen_ {
      get { return CloseOnOpen; }
      set { 
        CloseOnOpen = value;
        OnPropertyChanged(TradingMacroMetadata.CloseOnOpen);
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
      set { CloseOnProfitOnly = value; }
    }

    [DisplayName("Power Volatility Minimum")]
    [Category(categoryTrading)]
    [Description("Ex: CanTrade = Power > (Power-Avg)/StDev")]
    public double PowerVolatilityMinimum_ {
      get { return PowerVolatilityMinimum; }
      set { PowerVolatilityMinimum = value; }
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
      set { LongMAPeriod = value; }
    }

    [DisplayName("Trading Angle Range")]
    [Category(categoryTrading)]
    public double IterationsForPower_ {
      get { return TradingAngleRange; }
      set { TradingAngleRange = value; }
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
    [DisplayName("Corr Height/Spread - High")]
    [Category(categoryXXX)]
    [Description("Buy/Sell when locked && H/S > X/10")]
    public double CorridorHeightToSpreadRatioHigh {
      get { return BarPeriodsHigh / 10.0; }
      set { BarPeriodsHigh = (int)value; }
    }


    [DisplayName("Iterations For Power")]
    [Description("Number of Iterations to calculate power for wave")]
    [Category(categoryXXX)]
    public int IterationsForPower {
      get { return CorridorIterationsIn; }
      set { CorridorIterationsIn = value; }
    }

    [DisplayName("Iterations For Corridor Heights")]
    [Description("Ex: highs.AverageByIteration(N)")]
    [Category(categoryXXX)]
    public int IterationsForCorridorHeights {
      get { return CorridorIterationsOut; }
      set { CorridorIterationsOut = value; }
    }


    [DisplayName("Power Row Offset")]
    [Description("Ex: Speed = Spread / (row + X)")]
    [Category(categoryXXX)]
    public int PowerRowOffset_ {
      get { return PowerRowOffset; }
      set { PowerRowOffset = value; }
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
      set { IsColdOnTrades = value; }
    }

    [DisplayName("Bars Period")]
    [Category(categoryCorridor)]
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
    [Category(categoryCorridor)]
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
    [Category(categoryCorridor)]
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
          OnPropertyChanged(TradingMacroMetadata.CurrentGross);
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

    public bool IsHot { get { return Strategy == Strategies.Hot; } }
    public bool IsCold { get { return IsColdOnTrades && Trades.Length > 0; } }

    private bool _IsAutoSync;
    [DisplayName("Is Auto Sync")]
    [Category(categoryCorridor)]
    public bool IsAutoSync {
      get { return _IsAutoSync; }
      set {
        if (_IsAutoSync != value) {
          _IsAutoSync = value;
          OnPropertyChanged("IsAutoSync");
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

  }
}
