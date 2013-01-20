using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store {
  public enum ScanCorridorFunction {
    WaveDistance42 = 72,
    WaveDistance43 = 73,
    WaveStDevHeight = 8,
    DayDistance = 90,
    Regression = 100,
    Parabola = 110,
    Sinus = 20,
    Sinus1 = 21,
    Crosses = 30,
    CrossesStarter = 31,
    CrossesWithAngle = 32,
    StDev = 40
  }
  public enum TrailingWaveMethod {
    WaveCommon = -1,
    WaveAuto = 0,
    WaveShort = 1,
    WaveTrade = 2,
    WaveMax = 3,
    WaveMax1 = 31,
    WaveMax2 = 32,
    WaveMax3 = 33,
    WaveMin = 4,
    WaveLeft = 5,
    WaveRight = 6,
    HorseShoe = 70,
    Sinus = 80,
    Count = 90,
    CountWithAngle = 92,
    Count0 = 91,
    Wavelette = 93
  }
  public enum TradingMacroTakeProfitFunction {
    CorridorStDev = 3,
    Corridor_RH_2 = 4,
    CorridorHeight = 5,
    BuySellLevels = 6,
    RatesHeight = 10,
    RatesStDev = 11,
    RatesHeight_2 = 12,
    WaveShort = 13,
    WaveTradeStart = 14,
    RatesStDevMin = 15,
    Spread = 20,
    Zero = 25,
    PriceSpread = 26,
    WaveShortStDev = 27,
    WaveTradeStartStDev = 28,

  }
  public enum CorridorHeightMethods {
    ByMA = 0,
    ByPriceAvg = 1
  }
  public enum TurnOffFunctions {
    Void = -1,
    WaveShortLeft = 0,
    WaveShortAndLeft = 10,
    WaveHeight = 20,
    Correlation = 30,
    Variance = 31,
    BuySellHeight = 40
  }
  public enum ExitFunctions {
    Void = -1,
    Exit0 = 0,
    Exit = 1,
    Exit1 = 2,
    Exit2 = 3,
    GrossTP = 4
  }
  public enum MedianFunctions {
    WaveShort = 0,
    WaveTrade = 1,
    WaveStart = 20,
    WaveStart1 = 21,
    Regression = 30,
    Regression1 = 31
  }
  public enum VarainceFunctions {
    Zero = -1,
    Price = 0,
    Hight = 10,
    Max = 20,
    Min = 30,
    Sum = 40,
    Wave = 50,
    Rates3 = 60,
    Rates2 = 61,
    Rates = 62,
    StDevSqrt = 70
  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { Height = 1, Price = 2, HeightUD = 3, Minimum = 4, Maximum = 5, PriceAverage = 6 }
  [Flags]
  public enum LevelType { CenterOfMass = 1, Magnet = 2, CoM_Magnet = CenterOfMass | Magnet }
  [Flags]
  public enum Strategies {
    None = 0,
    Auto = 1,
    Hot = 2,
    Ender = Hot * 2, EnderA = Ender + Auto,
    Universal = Ender * 2, UniversalA = Universal + Auto
  }
  public enum MovingAverageValues { PriceAverage = 0, Volume = 1, PriceSpread = 2, PriceMove = 3 }
  public enum TradeCrossMethod {
    PriceAvg = 0,
    PriceCMA = 1
  }
}
