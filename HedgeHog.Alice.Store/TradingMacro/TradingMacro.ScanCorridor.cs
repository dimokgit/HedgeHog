﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using HedgeHog.Bars;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    private double _CorridorLength;
    public double CorridorLength {
      get { return _CorridorLength; }
      set {
        if (_CorridorLength == value) return;
        _CorridorLength = value;
        OnPropertyChanged(() => CorridorLength);
      }
    }

    #region ScanCorridor Extentions
    #region New
    private CorridorStatistics ScanCorridorByStDevHeight(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var pipSize = 1 / PointSize;
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        return 0;// (p.PriceHigh - p.PriceLow) * (p.PriceCMALast - n.PriceCMALast).Abs() * pipSize;
      });
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      this.RatesStDevAdjusted = RatesStDev * RatesStDevToRatesHeightRatio / CorridorDistanceRatio;
      var waveByDistance = ratesReversed.TakeWhile(r => r.RunningHeight <= RatesStDev).ToArray();
      WaveDistance = waveByDistance.LastBC().Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      if (!WaveShort.HasDistance) {
        WaveShort.Rates = null;
        WaveShort.Rates = waveByDistance;
      } else
        WaveShort.SetRatesByDistance(ratesReversed);

      var startStopRates = WaveShort.Rates.ReverseIfNot();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates.LastBC().StartDate)
        .Max(IsCorridorForwardOnly ? CorridorStats.StartDate : DateTime.MinValue);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return new CorridorStatistics(this, corridorRates, double.NaN, new[] { corridorRates.Average(r => r.PriceAvg), 0.0 });
      }
      return null;
    }
    private CorridorStatistics ScanCorridorByDistance42(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed[0].Distance1 = 0;
      ratesReversed[0].RunningHigh = double.MinValue;
      ratesReversed[0].RunningLow = double.MaxValue;
      var hikeMin = PointSize / 10;
      var cp = CorridorPrice();
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        var height = (cp(p) - cp(n)).Abs().Max(hikeMin) / PointSize;
        for (double i = 110, h = height; i < DistanceIterations; i++)
          height *= h;
        height = Math.Pow(height, DistanceIterations);
          n.Distance1 = p.Distance1 + (IsCorridorForwardOnly ? 1 / height : height);
        return /*1 /*/ height;
      });
      var ratesDistance = ratesForCorridor.Distance();
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      CorridorLength = (CorridorDistanceRatio * (1 + Fibonacci.FibRatioSign(StDevByPriceAvg, StDevByHeight))).Max(1);
      var halfRatio = ratesDistance / (CorridorDistanceRatio < 10 ? CorridorDistanceRatio.Max(1) : BarsCount / CorridorLength);
      var c = ratesReversed.Count(r => r.Distance <= halfRatio);
      if (c < 5) {
        c = 30;
        halfRatio = ratesReversed[29].Distance;
      }
      var waveByDistance = ratesReversed.Take(/*ratesReversed.Count -*/ c).ToArray();
      WaveDistance = waveByDistance.LastBC().Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      WaveShort.Rates = null;
      if (!WaveShort.HasDistance) WaveShort.Rates = waveByDistance;
      else WaveShort.SetRatesByDistance(ratesReversed);

      var distanceShort = WaveShort.Rates.LastBC().Distance / 2;
      var distanceShort1 = WaveShort.Rates.LastBC().Distance1 / 2;
      WaveTradeStart.Rates = null;
      var tradeWave = WaveShort.Rates.TakeWhile(r => r.Distance < distanceShort).ToArray();
      var tradeWave1 = WaveShort.Rates.TakeWhile(r => r.Distance1 < distanceShort1).ToArray();
      WaveTradeStart.Rates = tradeWave.Length < tradeWave1.Length ? tradeWave : tradeWave1;

      distanceShort = WaveTradeStart.Rates.LastBC().Distance / 2;
      distanceShort1 = WaveTradeStart.Rates.LastBC().Distance1 / 2;
      WaveTradeStart1.Rates = null;
      tradeWave = WaveTradeStart.Rates.TakeWhile(r => r.Distance < distanceShort).ToArray();
      tradeWave1 = WaveTradeStart.Rates.TakeWhile(r => r.Distance1 < distanceShort1).ToArray();
      WaveTradeStart1.Rates = tradeWave.Length < tradeWave1.Length ? tradeWave : tradeWave1;

      var distanceLeft = WaveDistance * 2;
      WaveShortLeft.Rates = null;
      WaveShortLeft.Rates = ratesReversed.Take(CorridorLength.ToInt()).ToArray();

      if (!WaveTradeStart.HasRates || !WaveShortLeft.HasRates) return null;

      var startStopRates = WaveShort.Rates.ReverseIfNot();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates.LastBC().StartDate);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return WaveShort.Rates.ScanCorridorWithAngle(cp, cp, TimeSpan.Zero, PointSize, CorridorCalcMethod);
      }
      return null;
    }
    private CorridorStatistics ScanCorridorByDistance43(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed[0].Distance1 = 0;
      ratesReversed[0].RunningHigh = double.MinValue;
      ratesReversed[0].RunningLow = double.MaxValue;
      var hikeMin = PointSize / 10;
      var cp = CorridorPrice();
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        var height = (cp(p) - cp(n)).Abs().Max(hikeMin) / PointSize;
        for (double i = 110, h = height; i < DistanceIterations; i++)
          height *= h;
        height = Math.Pow(height, DistanceIterations);
        n.Distance1 = p.Distance1 + (IsCorridorForwardOnly ? 1 / height : height);
        return /*1 /*/ height;
      });
      var ratesDistance = ratesForCorridor.Distance();
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      var fib = 1 + Fibonacci.FibRatio(StDevByPriceAvg, StDevByHeight);
      CorridorLength = (CorridorDistanceRatio * fib).Max(1);
      var halfRatio = ratesDistance / (CorridorDistanceRatio < 10 ? CorridorDistanceRatio.Max(1) : BarsCount / CorridorLength);
      var c = ratesReversed.Count(r => r.Distance <= halfRatio);
      if (c < 5) {
        c = 30;
        halfRatio = ratesReversed[29].Distance;
      }
      var waveByDistance = ratesReversed.Take(/*ratesReversed.Count -*/ c).ToArray();
      WaveDistance = waveByDistance.LastBC().Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      WaveShort.Rates = null;
      if (!WaveShort.HasDistance) WaveShort.Rates = waveByDistance;
      else WaveShort.SetRatesByDistance(ratesReversed);

      var distanceShort = WaveShort.Rates.LastBC().Distance / 2;
      var distanceShort1 = WaveShort.Rates.LastBC().Distance1 / 2;
      WaveTradeStart.Rates = null;
      var tradeWave = WaveShort.Rates.TakeWhile(r => r.Distance < distanceShort).ToArray();
      var tradeWave1 = WaveShort.Rates.TakeWhile(r => r.Distance1 < distanceShort1).ToArray();
      WaveTradeStart.Rates = tradeWave.Length < tradeWave1.Length ? tradeWave : tradeWave1;

      distanceShort = WaveTradeStart.Rates.LastBC().Distance / 2;
      distanceShort1 = WaveTradeStart.Rates.LastBC().Distance1 / 2;
      WaveTradeStart1.Rates = null;
      tradeWave = WaveTradeStart.Rates.TakeWhile(r => r.Distance < distanceShort).ToArray();
      tradeWave1 = WaveTradeStart.Rates.TakeWhile(r => r.Distance1 < distanceShort1).ToArray();
      WaveTradeStart1.Rates = tradeWave.Length < tradeWave1.Length ? tradeWave : tradeWave1;

      var distanceLeft = WaveDistance * 2;
      WaveShortLeft.Rates = null;
      WaveShortLeft.Rates = ratesReversed.Take(CorridorLength.ToInt()).ToArray();

      if (!WaveTradeStart.HasRates || !WaveShortLeft.HasRates) return null;

      var startStopRates = WaveShort.Rates.ReverseIfNot();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates.LastBC().StartDate);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return WaveShort.Rates.ScanCorridorWithAngle(cp, cp, TimeSpan.Zero, PointSize, CorridorCalcMethod);
      }
      return null;
    }
    #endregion
    #endregion

    #region DistanceIterationsReal
    void DistanceIterationsRealClear() { _DistanceIterationsReal = 0; }
    private double _DistanceIterationsReal;
    [DisplayName("Distance Iterations")]
    [Description("DistanceIterationsReal=F(DistanceIterations,X)")]
    [Category(categoryCorridor)]
    public double DistanceIterationsReal {
      get { return _DistanceIterationsReal; }
      set {
        if (_DistanceIterationsReal < value) {
          _DistanceIterationsReal = value;
          OnPropertyChanged(() => DistanceIterationsReal);
        }
      }
    }

    #endregion
  }
}