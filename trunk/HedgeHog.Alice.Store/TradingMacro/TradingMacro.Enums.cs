﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store {
  public enum ScanCorridorFunction {
    Void = 0,
    HorizontalProbe = 10,
    WaveDistance42 = 72,
    WaveDistance43 = 73,
    WaveStDevHeight = 8,
    DayDistance = 90,
    Regression = 100,
    Parabola = 110,
    Sinus = 20,
    Sinus1 = 21,
    StDevAngle = 40,
    Simple = 41,
    StDevSimple1Cross = 42,
    StDevUDCross = 43,
    Balance = 50,
    Height = 60,
    Time = 61,
    TimeFrame = 62,
    Rsd = 63
  }
  public enum TrailingWaveMethod {
    Magnet = 1,
    Magnet2 = 2,
    CountWithAngle = 92,
    Count0 = 91,
    Wavelette = 93,
    Manual = 110,
    StDev = 120,
    Rsd = 140,
    Backdoor = 150
  }
  public enum TradingMacroTakeProfitFunction {
    CorridorStDevMin = 3,
    CorridorStDevMax = 4,
    CorridorHeight = 5,
    BuySellLevels = 6,
    RatesHeight = 10,
    RatesStDevMax = 11,
    RatesHeight_2 = 12,
    WaveShort = 13,
    WaveTradeStart = 14,
    RatesStDevMin = 15,
    Spread = 20,
    Zero = 25,
    PriceSpread = 26,
    WaveShortStDev = 27,
    WaveTradeStartStDev = 28,
    RatesHeight_3 = 103,
    RatesHeight_4 = 104,
    RatesHeight_5 = 105

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
    GrossTP1 = 6,
    Limit = 7,
    Friday = 8
  }
  public enum MedianFunctions {
    Void = -1,
    WaveShort = 0,
    WaveTrade = 1,
    Density = 2,
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
    Min2 = 31,
    Sum = 40,
    Wave = 50,
    Rates3 = 60,
    Rates2 = 61,
    Rates = 62,
    Rates4 = 63,
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
    Universal = Hot * 2, UniversalA = Universal + Auto
  }
  public enum MovingAverageValues { PriceAverage = 0, Volume = 1, PriceSpread = 2, PriceMove = 3 }
  public enum TradeCrossMethod {
    PriceAvg = 0,
    PriceCMA = 1,
    ChartAskBid = 2
  }
  public enum CorridorHighLowMethod { AskHighBidLow = 0, Average = 1, BidHighAskLow = 2, BidLowAskHigh = 3, AskLowBidHigh = 4, AskBidByMA = 5, PriceByMA = 6, BidAskByMA = 7, PriceMA = 8 }
  public enum ChartHighLowMethod { AskBidByReg = 0, Average = 1,AskBidByMA = 2,Trima = 3 }
  public enum MovingAverageType { Cma = 0, Trima = 1, Regression = 2, RegressByMA = 3 }

}