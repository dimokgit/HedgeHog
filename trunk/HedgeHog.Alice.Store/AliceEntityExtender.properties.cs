using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

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


    [DisplayName("Spread Short/Long Treshold")]
    [Category(categoryCorridor)]
    public double SpreadShortToLongTreshold_ {
      get { return SpreadShortToLongTreshold; }
      set { SpreadShortToLongTreshold = value; }
    }

    [DisplayName("Corridor Height Multiplier")]
    [Category(categoryTrading)]
    [Description("Ex: CorrUp = PriceRegr + Up + corrHeight*X")]
    public double CorridorHeightMultiplier {
      get { return CorridornessMin; }
      set { CorridornessMin = value; }
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


    [Category(categoryTrading)]
    [DisplayName("Correlation Treshold")]
    [Description("Ex: if(Corr >  X) return sell")]
    public double CorrelationTreshold_ {
      get { return CorrelationTreshold; }
      set { CorrelationTreshold = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Range Ratio For TradeLimit")]
    [Description("Ex:Exit when PL > Range * X")]
    public double RangeRatioForTradeLimit_ {
      get { return RangeRatioForTradeLimit; }
      set { RangeRatioForTradeLimit = value; }
    }

    [Category(categoryTrading)]
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


    [DisplayName("Gann Angles Offset in Rads")]
    [Category(categoryCorridor)]
    public double GannAnglesOffset_ {
      get { return GannAnglesOffset.GetValueOrDefault(); }
      set { GannAnglesOffset = value; }
    }

    [DisplayName("Gann Angles")]
    [Category(categoryCorridor)]
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
    [Category(categoryTrading)]
    [Description("Lock buy/sell when H/S < X/10")]
    public double CorridorHeightToSpreadRatioLow {
      get { return BarPeriodsLow / 10.0; }
      set { BarPeriodsLow = (int)value; }

    }
    [DisplayName("Corr Height/Spread - High")]
    [Category(categoryTrading)]
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
    [Category(categoryCorridor)]
    public int PowerRowOffset_ {
      get { return PowerRowOffset; }
      set { PowerRowOffset = value; }
    }

    [Category(categoryXXX)]
    [DisplayName("Is SuppRes Manual")]
    public bool IsSuppResManual { get; set; }

    [DisplayName("Is Gann Angles Manual")]
    [Category(categoryCorridor)]
    public bool IsGannAnglesManual_ {
      get { return IsGannAnglesManual; }
      set { IsGannAnglesManual = value; }
    }



    public int GannAngle1x1Index { get { return GannAnglesList.Angle1x1Index; } }
  }
}
