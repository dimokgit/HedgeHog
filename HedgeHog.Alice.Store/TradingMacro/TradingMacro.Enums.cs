using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice {
  public enum VoltageFunction {
    None = 0,
    t1,
    DistanceMacd,
    StDev,
    AvgLineRatio,
    Rsd,
    PPM,
    PpmM1,
    Equinox,
    BPA1,
    StDevInsideOutRatio,
    HourlyStDevAvg,
    StDevSumRatio
  }
}
namespace HedgeHog.Alice.Store {
  public static class EnumMixins {
    public static double IfNotDirect(this TradingMacroTakeProfitFunction e, double takeProfit, Func<double, double> calc) {
      return e.IsDirect() ? takeProfit : calc(takeProfit);
    }
    public static bool IsDirect(this TradingMacroTakeProfitFunction e) {
      return e == TradingMacroTakeProfitFunction.TradeHeight || e == TradingMacroTakeProfitFunction.Pips;
    }
  }
  public enum RatesLengthFunction {
    None = 0,
    DistanceMin,
    DistanceMinSmth,
    DistanceMin0,
    TimeFrame,
    DMTF,
    M1Wave,
    M1WaveAvg,
    M1WaveAvg2,
    M1WaveAvg3,
    M1CorrsAvg
  }
  public enum ScanCorridorFunction {
    StDevSplits = 46,
    StDevSplits3 = 48,
    Ftt = 64,
    OneTwoThree = 5,
  }
  public enum TrailingWaveMethod {
    SimpleMoveBO = 4,
    SimpleMove = 5,
    SimpleMoveRng = 6,
    ManualRange = 10,
    GreenStrip = 11,
    StDevAngle = -14,
    BigGap = -20,
    FrameAngle = 81,
    FrameAngle2 = 82,
    FrameAngle3 = 83,
    FrameAngle31 = 84,
    FrameAngle32 = 85,
    FrameAngle4 = 86,
    StDevFlat = 87,
    StDevFlat2 = 88,
    StDevFlat3 = 89,
    LongCross = 80,
    LongLine = 160,
    Recorder = 1000
  }
  public enum TradingMacroTakeProfitFunction {
    BuySellLevels,
    RatesHeight,
    Pips,
    Lime,
    Green,
    Greenish,
    Red,
    Blue,
    TradeHeight,
    StDev,
    M1StDev
  }
  public enum TradeLevelBy {
    None = 0,
    PriceCma,
    PriceAvg1,
    BlueAvg1,
    GreenAvg1,
    LimeAvg1,
    PriceAvg2,
    PriceAvg3,
    PriceRB2,
    PriceRB3,
    PriceHigh,
    PriceLow,
    BlueMax,
    BlueMin,
    RedMax,
    RedMin,
    GreenMax,
    GreenMin,
    LimeMax,
    LimeMin,
    PriceLimeH,
    PriceLimeL,
    PriceHigh0,
    PriceLow0,
    PriceMax,
    PriceMin,
    PriceAvg1Max,
    PriceAvg1Min,
    GreenStripL,
    BlueStripL,
    RedStripL,
    MaxRG,
    MinRG,
    AvgLineMax,
    AvgLineMin,
    Avg22,
    Avg23,
    BoilingerUp,
    BoilingerDown,
    GreenStripH,
    BlueStripH,
    RedStripH
  }
  public enum WaveSmoothBys {
    Distance,
    Minutes,
    StDev,
    MinDist
  }
  public enum CorridorHeightMethods {
    ByMA = 0,
    ByPriceAvg = 1,
    ByStDevH = 20,
    ByStDevP = 21,
    ByStDevPUD = 22,
    ByStDevHP = 23,
    ByStDevMin = 30,
    ByStDevMin2 = 31,
    ByStDevMax = 40
  }
  public enum TurnOffFunctions {
    Void = -1,
    WaveShortLeft = 0,
    WaveShortAndLeft = 10,
    WaveHeight = 20,
    Correlation = 30,
    Variance = 31,
    InMiddle_4 = 50,
    InMiddle_5 = 51,
    InMiddle_6 = 52,
    InMiddle_7 = 53,
    CrossCount = 60
  }
  public enum ExitFunctions {
    Void = -1,
    GrossTP = 4,
    Wavelette = 5,
    JumpOut = 6,
    Limit = 7,
    Friday = 8,
    CorrTouch = 10
  }
  public enum Freezing {
    None = 0, Freez = 1, Float = 2
  }
  public enum CorridorCalculationMethod {
    Height = 1,
    Price = 2,
    HeightUD = 3,
    Minimum = 4,
    Maximum = 5,
    PriceAverage = 6,
    PowerMeanPower = 7,
    RootMeanSquare = 8,
    MinMax
  }
  [Flags]
  public enum LevelType {
    CenterOfMass = 1, Magnet = 2, CoM_Magnet = CenterOfMass | Magnet
  }
  [Flags]
  public enum Strategies {
    None = 0,
    Auto = 1,
    Hot = 2,
    Universal = Hot * 2, UniversalA = Universal + Auto
  }
  public enum MovingAverageValues {
    PriceAverage = 0, Volume = 1, PriceSpread = 2, PriceMove = 3
  }
  public enum TradeCrossMethod {
    PriceCurr = -1,
    PriceAvg = 0,
    PriceCMA = 1,
    ChartAskBid = 2,
    PriceAvg1 = 10,
  }
  public enum CorridorHighLowMethod {
    AskHighBidLow = 0,
    Average = 1,
    BidHighAskLow = 2,
    BidLowAskHigh = 3,
    AskLowBidHigh = 4,
    PriceMA = 8
  }
  public enum ChartHighLowMethod {
    AskBidByReg = 0, Average = 1, AskBidByMA = 2, Trima = 3, Volts, Volts2, Volts3
  }
  public enum MovingAverageType {
    Cma = 0, FFT = 4, FFT2 = 5
  }

  public enum TradeLevelsPreset {
    None = 0,
    Lime = 1,
    Green = 2,
    Red = 3,
    Blue = 4,
    Lime23 = 9,
    Green23 = 5,
    Red23 = 6,
    Blue23 = 7,
    NarrowR = 8,
    Corridor2R = 11,
    MinMax = 13,
  }
  public enum CorridorByStDevRatio {
    HPAverage = 0,
    Height,
    Price,
    Height2,
    HeightPrice,
    Price12,
    Price2
  }
}
