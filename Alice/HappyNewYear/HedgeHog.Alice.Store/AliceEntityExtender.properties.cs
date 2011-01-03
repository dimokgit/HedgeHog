using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    [DisplayName("Gann Angles Offset in Rads")]
    [Category(categoryCorridor)]
    public double GannAnglesOffset_ {
      get { return GannAnglesOffset.GetValueOrDefault(); }
      set { GannAnglesOffset = value; }
    }

    [DisplayName("Gann Angle Index Minimum")]
    [Category(categoryCorridor)]
    public int GannAngleIndexMinimum_ {
      get { return GannAngleIndexMinimum; }
      set { GannAngleIndexMinimum = value; }
    }

    [DisplayName("Price CMA Period")]
    [Category(categoryCorridor)]
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
    [Category(categoryCorridor)]
    public int IterationsForPower {
      get { return CorridorIterationsIn; }
      set { CorridorIterationsIn = value; }
    }

    [DisplayName("Iterations For Corridor Heights")]
    [Description("Ex: highs.AverageByIteration(N)")]
    [Category(categoryCorridor)]
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

    [Category(categoryCorridor)]
    [DisplayName("Is SuppRes Manual")]
    public bool IsSuppResManual { get; set; }

  }
}
