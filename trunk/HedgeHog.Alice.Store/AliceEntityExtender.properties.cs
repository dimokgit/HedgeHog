using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using HedgeHog.Bars;
using HedgeHog.Alice.Store.Metadata;

namespace HedgeHog.Alice.Store {
  public partial class SuppRes {
    public static readonly double TradesCountMinimum = 1;

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
      set { IterationsForSuppResLevels = value; }
    }


    [DisplayName("Corridor Height Multiplier")]
    [Category(categoryXXX)]
    [Description("Ex: CorrUp = PriceRegr + Up + corrHeight*X")]
    public double CorridorHeightMultiplier {
      get { return CorridornessMin; }
      set { CorridornessMin = value; }
    }

    [DisplayName("Streach Trading Distance")]
    [Category(categoryTrading)]
    [Description("Ex: PL < tradingDistance * (X ? trades.Length:1)")]
    public bool StreachTradingDistance_ {
      get { return StreachTradingDistance; }
      set { StreachTradingDistance = value; }
    }

    [DisplayName("Close On Open Only")]
    [Category(categoryTrading)]
    [Description("Close position only when opposite opens.")]
    public bool CloseOnOpen_ {
      get { return CloseOnOpen; }
      set { CloseOnOpen = value; }
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
      set { ReverseStrategy = value; }
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
      set { RangeRatioForTradeLimit = value; }
    }

    [Category(categoryCorridor)]
    [DisplayName("Range Ratio For TradeStop")]
    [Description("Ex:Exit when PL < -Range * X")]
    public double RangeRatioForTradeStop_ {
      get { return RangeRatioForTradeStop; }
      set { RangeRatioForTradeStop = value; }
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



    public double CorridorHeightsRatio { get { return Fibonacci.FibRatioSign(CorridorStats.HeightHigh, CorridorStats.HeightLow); } }

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

    [DisplayName("Bars Period")]
    [Category(categoryCorridor)]
    public BarsPeriodType LimitBar_ {
      get { return (BarsPeriodType)LimitBar; }
      set {
        if (LimitBar != (int)value) {
          LimitBar = (int)value;
          OnPropertyChanged(TradingMacroMetadata.LimitBar_);
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

    public int GannAngle1x1Index { get { return GannAnglesList.Angle1x1Index; } }

    public bool IsHot { get { return Strategy == Strategies.Hot; } }
  }
}
