﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using HedgeHog.Bars;
using System.ComponentModel;
using C = System.Collections.Concurrent;
using System.Diagnostics;

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
    private CorridorStatistics ScanCorridorByDayDistance(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
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

      //CorridorLength = (CorridorDistanceRatio * (1 + Fibonacci.FibRatioSign(StDevByPriceAvg, StDevByHeight))).Max(1);
      
      var dayRatio = ratesDistance * 1440 / BarsCount.Max(1440);
      CorridorLength = ratesReversed.Count(r => r.Distance <= dayRatio);
      var dayWave = ratesReversed.Take(/*ratesReversed.Count -*/ CorridorLength.ToInt()).ToArray();

      var corridorDistance = dayWave.LastBC().Distance * CorridorDistanceRatio / 1440;
      var waveByDistance = dayWave.TakeWhile(r => r.Distance <= corridorDistance).ToArray();
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
    private CorridorStatistics ScanCorridorByRegression(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var groupLength = 5;
      var dateMin = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(-BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var polyOrder = PolyOrder;
      var ratesReversedOriginal = ratesForCorridor.ReverseIfNot();
      var ratesReversed = ratesReversedOriginal.TakeWhile(r=>r.StartDate>=dateMin).Shrink(CorridorPrice,groupLength).ToArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      var ratesToRegress = new List<double>() { ratesReversed[0] };
      foreach (var rate in ratesReversed.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count / 2).ToArray().Height() > stDev) break;
      }
      var correlations = new { corr = 0.0, length = 0 }.IEnumerable().ToList();
      var corr = double.NaN;
      var locker = new object();
      Enumerable.Range(ratesToRegress.Count, ratesReversed.Length - ratesToRegress.Count).AsParallel().ForAll(rates => {
        var prices = new double[rates];
        Array.Copy(ratesReversed, prices, prices.Length);
        var coeffs = prices.Regress(polyOrder);
        var parabola = new double[prices.Length];
        for (var i = 0; i < prices.Length; i++)
          parabola[i] = coeffs.RegressionValue(i);
        corr = corr.Cma(5, alglib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length).Abs());
        lock (locker) {
          correlations.Add(new { corr, length = prices.Length });
        }
      });
      correlations.Sort((a, b) => a.corr.CompareTo(b.corr));
      if (!correlations.Any()) correlations.Add(new { corr, length = 1000000 });
      WaveShort.Rates = null;
      WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(correlations[0].length * groupLength).TakeWhile(r => r.StartDate >= dateMin).ToArray();
      CorridorCorrelation = ratesReversedOriginal.Volatility(_priceAvg, GetPriceMA);
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ScanCorridorByParabola_4(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var groupLength = 5;
      var dateMin = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(-BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var polyOrder = PolyOrder;
      var rates1 = ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateMin).Select(CorridorPrice).ToArray();
      var coeffsCorridor = rates1.Regress(40);
      coeffsCorridor.SetRegressionPrice(0, rates1.Length, (i, v) => rates1[i] = v);
      var ratesReversed = rates1.Shrink( groupLength).ToArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      var ratesToRegress = ratesReversed.Take((CorridorDistanceRatio / groupLength).ToInt().Max(PolyOrder)).ToList();
      foreach (var rate in ratesReversed.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count / 2).ToArray().Height() > stDev) break;
      }
      var correlations = new { corr = 0.0, length = 0 }.IEnumerable().ToList();
      var corr = double.NaN;
      var locker = new object();
      Enumerable.Range(ratesToRegress.Count, (ratesReversed.Length - ratesToRegress.Count).Max(0)).AsParallel().ForAll(rates => {
        double[] parabola = ratesReversed.Regression(rates, polyOrder);
        double[] coeffs;
        double[] line = ratesReversed.Regression(rates, polyOrder - 1, out coeffs);
        corr = corr.Cma(5, alglib.correlation.pearsoncorrelation(ref line, ref parabola, rates));
        //var sineOffset = Math.Sin(Math.PI / 2 - coeffs[1] / PointSize);
        //corr = corr.Cma(5, line.Zip(parabola, (l, p) => ((l - p) * sineOffset).Abs()).Average());
        lock (locker) {
          correlations.Add(new { corr, length = rates });
        }
      });
      correlations.Sort((a, b) => -a.corr.CompareTo(b.corr));
      if (!correlations.Any()) correlations.Add(new { corr, length = ratesReversed.Length });
      var lengthByCorr = correlations[0].length;
      if (ShowParabola) {
        var parabola = ratesReversed.Regression(lengthByCorr, polyOrder).UnShrink(groupLength).Take(ratesForCorridor.Count).ToArray();
        var i = 0;
        foreach (var rfc in ratesForCorridor.ReverseIfNot().Take(parabola.Length))
          rfc.PriceCMALast = parabola[i++];
      }
      {
        WaveShort.Rates = null;
        var rates = ratesForCorridor.ReverseIfNot().Take(lengthByCorr * groupLength).ToList();
        WaveShort.Rates = (rates.LastBC().StartDate - dateMin).TotalMinutes * BarPeriodInt < groupLength 
          ? ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateMin).ToList()
          : rates;
      }
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ScanCorridorByParabola(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var groupLength = 5;
      var dateMin = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(this.IsCorridorForwardOnly ? 0 : -BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var polyOrder = PolyOrder;
      var ratesReversed = ratesForCorridor.ReverseIfNot().ToArray();
      var ratesShrunken = ratesReversed.TakeWhile(r => r.StartDate >= dateMin).Shrink(CorridorPrice, groupLength).ToArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      var ratesToRegress = ratesShrunken.Take(1.Min((CorridorDistanceRatio / groupLength).ToInt().Max(PolyOrder))).ToList();
      foreach (var rate in ratesShrunken.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count).ToArray().Height() > stDev) break;
      }
      var correlations = new { corr = 0.0, length = 0,vol=0.0,sort = 0.0 }.IEnumerable().ToList();
      var corr = double.NaN;
      var locker = new object();
      Enumerable.Range(ratesToRegress.Count, (ratesShrunken.Length - ratesToRegress.Count).Max(0)).AsParallel().ForAll(rates => {
        double[] coeffs,source;
        double[] parabola = ratesShrunken.Regression(rates, polyOrder,out coeffs,out source);
        double[] line = ratesShrunken.Regression(rates, 1,out coeffs,out source);
        var sineOffset = Math.Sin(Math.PI / 2 - coeffs[1] / PointSize);
        var start = (rates * .15).ToInt();
        var count = (rates * .7).ToInt();
        var cp = CorridorPrice();
        var rates1 = line.Skip(start).Take(count).Zip(ratesReversed.Skip(start).Take(count), (l, p) => ((cp(p) - l ) * sineOffset))
          .OrderBy(d => d).ToArray();
        var parabolaHeight = (rates1[0].Abs() + rates1.LastBC()) / 2;// 
        //line.Zip(parabola, (l, p) => ((l - p) * sineOffset).Abs()).Max();
        if (parabolaHeight > StDevByHeight) {
          var vol = 1 / coeffs[1].Abs().Angle(PointSize);// ratesReversed.Take(rates).ToArray().Volatility(_priceAvg, GetPriceMA);// / coeffs[1].Abs();
          corr = corr.Cma(15, alglib.correlation.pearsoncorrelation(ref source, ref parabola, rates));
          //var sineOffset = Math.Sin(Math.PI / 2 - coeffs[1] / PointSize);
          //corr = corr.Cma(5, line.Zip(parabola, (l, p) => ((l - p) * sineOffset).Abs()).Average());
          lock (locker) {
            correlations.Add(new { corr, length = rates, vol, sort = corr * vol * vol });
          }
        }
      });
      correlations.Sort((a, b) => -(a.sort).CompareTo(b.sort));
      if (!correlations.Any()) correlations.Add(new { corr, length = ratesShrunken.Length, vol = 0.0,sort=0.0 });
      var lengthByCorr = correlations[0].length;
      if (ShowParabola) {
        var parabola = ratesShrunken.Regression(lengthByCorr, polyOrder).UnShrink(groupLength).Take(ratesForCorridor.Count).ToArray();
        var i = 1;
        foreach (var rfc in ratesForCorridor.ReverseIfNot().Skip(1).Take(parabola.Length-1))
          rfc.PriceCMALast = parabola[i++];
      }
      {
        WaveShort.Rates = null;
        var rates = ratesForCorridor.ReverseIfNot().Take(lengthByCorr * groupLength).ToList();
        WaveShort.Rates = rates;
      }
      CorridorCorrelation = ratesForCorridor.ReverseIfNot().Take((WaveShort.Rates.Count * 1.5).ToInt()).ToArray().Volatility(_priceAvg,GetPriceMA);
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ScanCorridorByParabola_5(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var groupLength = 5;
      var dateMin = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(this.IsCorridorForwardOnly ? 0 : -BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var polyOrder = PolyOrder;
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var ratesShrunken = ratesReversed.TakeWhile(r => r.StartDate >= dateMin).Shrink(CorridorPrice, groupLength).ToArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      var ratesToRegress = ratesShrunken.Take((CorridorDistanceRatio / groupLength).ToInt().Max(PolyOrder)).ToList();
      foreach (var rate in ratesShrunken.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count / 2).ToArray().Height() > stDev) break;
      }
      var correlations = new { corr = 0.0, length = 0, vol = 0.0 }.IEnumerable().ToList();
      var corr = double.NaN;
      var locker = new object();
      Enumerable.Range(ratesToRegress.Count, (ratesShrunken.Length - ratesToRegress.Count).Max(0)).AsParallel().ForAll(rates => {
        double[] coeffs, source;
        double[] parabola = ratesShrunken.Regression(rates, polyOrder, out coeffs, out source);
        double[] parabola1 = ratesShrunken.Regression(rates, polyOrder - 1);
        coeffs = source.Regress(1);
        var vol = ratesReversed.Take(rates).ToArray().Volatility(_priceAvg, GetPriceMA) / coeffs[1].Abs();
        var c = alglib.correlation.pearsoncorrelation(ref parabola1, ref parabola, rates);
        corr = corr.Cma(15, c * vol);
        //var sineOffset = Math.Sin(Math.PI / 2 - coeffs[1] / PointSize);
        //corr = corr.Cma(5, line.Zip(parabola, (l, p) => ((l - p) * sineOffset).Abs()).Average());
        lock (locker) {
          correlations.Add(new { corr, length = rates, vol });
        }
      });
      correlations.Sort((a, b) => -a.corr.CompareTo(b.corr));
      if (!correlations.Any()) correlations.Add(new { corr, length = ratesShrunken.Length, vol = 0.0 });
      var lengthByCorr = correlations[0].length;
      if (ShowParabola) {
        var parabola = ratesShrunken.Regression(lengthByCorr, polyOrder).UnShrink(groupLength).Take(ratesForCorridor.Count).ToArray();
        var i = 1;
        foreach (var rfc in ratesForCorridor.ReverseIfNot().Skip(1).Take(parabola.Length - 1))
          rfc.PriceCMALast = parabola[i++];
      }
      {
        WaveShort.Rates = null;
        var rates = ratesForCorridor.ReverseIfNot().Take(lengthByCorr * groupLength).ToList();
        WaveShort.Rates = (rates.LastBC().StartDate - dateMin).TotalMinutes * BarPeriodInt < groupLength
          ? ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateMin).ToList()
          : rates;
      }
      CorridorCorrelation = ratesForCorridor.ReverseIfNot().Take((WaveShort.Rates.Count * 1.5).ToInt()).ToArray().Volatility(_priceAvg, GetPriceMA);
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private CorridorStatistics ScanCorridorByParabola_2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var dateMin = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate : DateTime.MinValue;
      var groupLength = 5;
      var polyOrder = PolyOrder;
      var ratesReversed = ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateMin).Shrink(CorridorPrice, groupLength).ToArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      var ratesToRegress = new List<double>() { ratesReversed[0] };
      foreach (var rate in ratesReversed.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count / 2).ToArray().Height() > stDev) break;
      }
      var correlations = new { corr = 0.0, length = 0 }.IEnumerable().ToList();
      var corr = double.NaN;
      var locker = new object();
      Enumerable.Range(ratesToRegress.Count, ratesReversed.Length - ratesToRegress.Count).AsParallel().ForAll(rates => {
        double[] coeffs;
        double[] prices;
        double[] parabola = ratesReversed.Regression(rates, polyOrder,out coeffs, out prices);
        corr = corr.Cma(5, alglib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length));
        lock (locker) {
          correlations.Add(new { corr, length = prices.Length });
        }
      });
      correlations.Sort((a, b) => -a.corr.CompareTo(b.corr));
      if (!correlations.Any()) correlations.Add(new { corr, length = ratesReversed.Length });
      var lengthByCorr = correlations[0].length.Max(correlations.LastBC().length);
      {
        var parabola = ratesReversed.Regression(lengthByCorr, polyOrder).UnShrink(groupLength).Take(ratesForCorridor.Count).ToArray();
        var i = 0;
        foreach (var rfc in ratesForCorridor.ReverseIfNot().Take(parabola.Length))
          rfc.PriceCMALast = parabola[i++];
      }
      WaveShort.Rates = null;
      WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(lengthByCorr * groupLength).TakeWhile(r => r.StartDate >= dateMin).ToArray();
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private CorridorStatistics ScanCorridorByParabola_1(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      int groupLength = 5;
      var ratesReversed = ratesForCorridor.Shrink(CorridorPrice, groupLength).ToArray();
      var ratesToRegress = new List<double>() { ratesReversed[0] };
      foreach (var rate in ratesReversed.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count / 2).ToArray().Height() > StDevByPriceAvg) break;
      }
      var correlations = new { corr = 0.0, length = 0 }.IEnumerable().ToList();
      var corr = double.NaN;
      foreach (var rate in ratesReversed.Skip(ratesToRegress.Count)) {
        ratesToRegress.Add(rate);
        var prices = ratesToRegress.ToArray();
        var pl = prices.Length / 2;
        var pricesLeft = ratesToRegress.LastBCs(pl).ToArray();
        var pricesRight = ratesToRegress.Take(pl).ToArray();
        var priceses = new[] { pricesLeft, pricesRight }.OrderBy(pss => pss.Height()).First();
        var ph = (pricesLeft.Height() + pricesRight.Height()) / 2;
        var pm = (prices.Max() + prices.Min() - ph) / 2;
        var pr = ph / (pl * pl);
        var px = Enumerable.Range(0, pl).ToArray();
        px = px.Select(v => -v).OrderBy(v => v).Concat(px).ToArray();
        var parabola = new double[ratesToRegress.Count];
        var i = 0;
        px.ForEach(x => parabola[i++] = x * x * pr + pm);
        corr = corr.Cma(3, alglib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length.Min(parabola.Length)).Abs());
        correlations.Add(new { corr, length = prices.Length });
      }
      correlations.Sort((a, b) => -a.corr.CompareTo(b.corr));
      WaveShort.Rates = null;
      WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(correlations[0].length * groupLength).ToArray();
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private CorridorStatistics ScanCorridorBySinus(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      int groupLength = 5;
      var cp = CorridorPrice() ?? _priceAvg;
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var dateMin = DateTime.MinValue;// WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(this.IsCorridorForwardOnly ? 0 : -BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var ratesShrunken = ratesReversed.TakeWhile(r => r.StartDate >= dateMin).Shrink(cp, groupLength).ToArray();
      double max = double.NaN, min = double.NaN;
      int countMin = 1;
      var stDev = StDevByHeight.Max(StDevByPriceAvg);
      foreach (var rate in ratesShrunken) {
        max = rate.Max(max);
        min = rate.Min(min);
        if (max - min > stDev) break;
        countMin++;
      }
      var correlations = new { corr = 0.0, length = 0, parabola = new double[0], angle = 0.0 }.IEnumerable().ToList();
      var locker = new object();
      var corr = double.NaN;
      Enumerable.Range(countMin, (ratesShrunken.Length - countMin).Max(0)).AsParallel().ForAll(rates => {
        var height = 0.0;
        double[] prices = null;
        double[] coeffs;
        var line = ratesShrunken.Regression(rates, 1, out coeffs, out prices);
        var angle = coeffs[1].Angle(PointSize) / groupLength;
        var sineOffset = Math.Cos(-angle * Math.PI / 180);
        var heights = new List<double>(new double[rates]);
        Enumerable.Range(0, rates).ForEach(i => heights[i] = (line[i] - ratesShrunken[i]) * sineOffset);
        heights.Sort();
        height = heights[0].Abs() + heights.LastBC();
        if (height > stDev) {
          //var parabola = MathExtensions.Sin(180, rates, height / 2, coeffs.RegressionValue(rates / 2), WaveStDevRatio.ToInt());
          var parabola = MathExtensions.Sin(180, rates, height / 2, prices.Average(), WaveStDevRatio.ToInt());
          lock (locker) {
            corr = corr.Cma(25, alglib.correlation.pearsoncorrelation(ref prices, ref parabola, rates).Abs());
            correlations.Add(new { corr, length = prices.Length, parabola, angle });
          }
        }
      });
      ChartPriceHigh = ShowParabola ? r => r.PriceTrima : (Func<Rate, double>)null;
      correlations.RemoveAll(c => c == null);
      correlations.Sort((a, b) => -a.corr.CompareTo(b.corr));
      if (correlations.Count > 0 && correlations[0].angle.Abs().ToInt() <= 1) {
        CorridorCorrelation = correlations[0].corr;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(correlations[0].length * groupLength).ToArray();
        if (ShowParabola) {
          var parabola = correlations[0].parabola.UnShrink(groupLength).ToArray();
          Enumerable.Range(0, ratesReversed.Count).ForEach(i => ratesReversed[i].PriceTrima = i < parabola.Length ? parabola[i] : GetPriceMA(ratesReversed[i]));
        }
      } else {
        var dm = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate : DateTime.MinValue;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesReversed.TakeWhile(r => r.StartDate >= dm).ToArray();
      }
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private CorridorStatistics ScanCorridorBySinus_1(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      int groupLength = 5;
      var cp = _priceAvg ?? CorridorPrice();
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var dateMin = DateTime.MinValue;// WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(this.IsCorridorForwardOnly ? 0 : -BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var ratesShrunken = ratesReversed.TakeWhile(r => r.StartDate >= dateMin).Shrink(cp, groupLength).ToArray();
      double max = double.NaN, min = double.NaN;
      int countMin = 1;
      var stDev = StDevByHeight.Max(StDevByPriceAvg);
      foreach (var rate in ratesShrunken) {
        max = rate.Max(max);
        min = rate.Min(min);
        if (max - min > stDev) break;
        countMin++;
      }
      var correlations = new { corr = 0.0, length = 0, parabola = new double[0] }.IEnumerable().ToList();
      var corr = double.NaN;
      var locker = new object();
      Enumerable.Range(countMin, (ratesShrunken.Length - countMin).Max(0)).AsParallel().ForAll(rates => {
        var height = 0.0;
        double[] prices = null;
        double[] coeffs;
        var line = ratesShrunken.Regression(rates, 1, out coeffs, out prices);
        var angle = coeffs[1].Angle(PointSize) / groupLength;
        var sineOffset = Math.Cos(-angle * Math.PI / 180);
        var heights = new List<double>(new double[rates]);
        Enumerable.Range(0, rates).ForEach(i => heights[i] = (line[i] - ratesShrunken[i]) * sineOffset);
        heights.Sort();
        height = heights[0].Abs() + heights.LastBC();
        if (height.Between(stDev,stDev*2) && angle.Abs().ToInt() <= 1) {
          //var parabola = MathExtensions.Sin(180, rates, height / 2, coeffs.RegressionValue(rates / 2), WaveStDevRatio.ToInt());
          var parabola = MathExtensions.Sin(180, rates, height / 2, prices.Average(), WaveStDevRatio.ToInt());
          lock (locker) {
            var pearson = alglib.correlation.pearsoncorrelation(ref prices, ref parabola, rates).Abs();
            var spearman = alglib.correlation.spearmanrankcorrelation(prices, parabola, rates).Abs();
            corr = corr.Cma(13, corr);
            correlations.Add(new { corr, length = prices.Length, parabola });
          }
        }
      });
      ChartPriceHigh = ShowParabola ? r => r.PriceTrima : (Func<Rate,double>)null;
      correlations.Sort((a, b) => -a.corr.CompareTo(b.corr));
      if (correlations.Count > 0) {
        CorridorCorrelation = correlations[0].corr;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(correlations[0].length * groupLength).ToArray();
        if (ShowParabola) {
          var parabola = correlations[0].parabola.UnShrink(groupLength).ToArray();
          Enumerable.Range(0, ratesReversed.Count).ForEach(i => ratesReversed[i].PriceTrima = i < parabola.Length ? parabola[i] : GetPriceMA(ratesReversed[i]));
        }
      } else {
        var dm = WaveShort.Rates.LastBC().StartDate;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesReversed.TakeWhile(r => r.StartDate >= dm).ToArray();
      }
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ScanCorridorByCrossesStarter(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var rates = DayWaveByDistance(ratesForCorridor, CorridorDistanceRatio).OrderBars().ToArray();
      WaveShortLeft.Rates = null;
      WaveShortLeft.Rates = rates.ReverseIfNot().ToArray();
      return ScanCorridorByCrosses(rates, priceHigh, priceLow);
    }
    private CorridorStatistics ScanCorridorSimple(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = CorridorPrice();
      var startMax = CorridorStopDate == DateTime.MinValue ? DateTime.MaxValue : CorridorStopDate;
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      if (CorridorStartDate.HasValue) {
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= CorridorStartDate.Value).ToArray();
      } else {
        var w = CorridorDistanceRatio.ToInt();
        var dateLeft = RateLast.StartDate.AddDays(-7);
        var dateRight = dateLeft.AddMinutes(-w * BarPeriodInt);
        var rates1 = RatesInternal.SkipWhile(r => r.StartDate < dateRight).TakeWhile(r => r.StartDate <= dateLeft).Reverse().ToArray();
        var ratesForDistance = ratesReversed.Take(CorridorDistanceRatio.ToInt()).ToArray();
        Func<Rate, double> calcDist = r => Math.Pow(r.PriceAvg, DistanceIterations);
        var distance = ratesForDistance.CalcDistance(calcDist);
        var distance1 = rates1.CalcDistance(calcDist);
        var ratio = distance / distance1;
        WaveShortLeft.Rates = null;
        WaveShortLeft.Rates = ratesReversed.Take((CorridorDistanceRatio * ratio).ToInt()).ToArray();
        ratesReversed[0].RunningLow = ratesReversed[0].RunningHigh = cp(ratesReversed[0]);
        ratesReversed.Aggregate((p, n) => {
          var v = cp(n);
          n.RunningLow = p.RunningLow.Min(v);
          n.RunningHigh = p.RunningHigh.Max(v);
          return n;
        });
        var countMin = 2;
        var heightMin = GetValueByTakeProfitFunction(TradingDistanceFunction).Min(RatesHeight * .9);
        var ratesCount = ratesReversed.Count(r => countMin-- > 0 || r.RunningHeight < heightMin);
        var rates = ratesReversed.Select(cp).ToArray();
        var ratesAngle = rates.Take(ratesCount).ToArray().Regress(1).LineSlope().Abs().Angle(PointSize);
        for (;ratesCount <rates.Length ; ratesCount += (ratesCount / 100) + 1) {
          var rates0 = new double[ratesCount + 1];
          Array.Copy(rates, rates0, rates0.Length);
          var height = rates0.Height();
          var stDev = rates0.StDev();
          if (WaveStDevRatio > 0 && Fibonacci.FibRatio(height, stDev) < WaveStDevRatio) continue;
          var angle = rates0.Regress(1).LineSlope().Abs().Angle(PointSize);
          if (angle < ratesAngle) break;
          ratesAngle = angle;
        }
        WaveShort.Rates = null;
        WaveShort.Rates = ratesReversed.Take(ratesCount).ToArray();
      }
      return WaveShort.Rates.SkipWhile(r => r.StartDate > startMax).ToArray()
        .ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private CorridorStatistics ScanCorridorSimpleWithOneCross(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = CorridorPrice();
      var startMax = CorridorStopDate == DateTime.MinValue ? DateTime.MaxValue : CorridorStopDate;
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      if (CorridorStartDate.HasValue) {
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= CorridorStartDate.Value).ToArray();
      } else {
        var w = CorridorDistanceRatio.ToInt();
        var dateLeft = RateLast.StartDate.AddDays(-7);
        var dateRight = dateLeft.AddMinutes(-w * BarPeriodInt);
        var rates1 = RatesInternal.SkipWhile(r => r.StartDate < dateRight).TakeWhile(r => r.StartDate <= dateLeft).Reverse().ToArray();
        var ratesForDistance = ratesReversed.Take(CorridorDistanceRatio.ToInt()).ToArray();
        Func<Rate, double> calcDist = r => Math.Pow(r.PriceAvg, DistanceIterations);
        var distance = ratesForDistance.CalcDistance(calcDist);
        var distance1 = rates1.CalcDistance(calcDist);
        var ratio = distance / distance1;
        WaveShortLeft.Rates = null;
        WaveShortLeft.Rates = ratesReversed.Take((CorridorDistanceRatio * ratio).ToInt()).ToArray();
        ratesReversed[0].RunningLow = ratesReversed[0].RunningHigh = cp(ratesReversed[0]);
        ratesReversed.Aggregate((p, n) => {
          var v = cp(n);
          n.RunningLow = p.RunningLow.Min(v);
          n.RunningHigh = p.RunningHigh.Max(v);
          return n;
        });
        var countMin = 2;
        var heightMin = GetValueByTakeProfitFunction(TradingDistanceFunction).Min(RatesHeight * .9);
        var ratesCount = ratesReversed.Count(r => countMin-- > 0 || r.RunningHeight < heightMin);
        var rates = ratesReversed.Select(cp).ToArray();
        var ratesAngle = rates.Take(ratesCount).ToArray().Regress(1).LineSlope().Abs().Angle(PointSize);
        var hasCrossStart = false;
        var hasCrossStop = false;
        var ratesCrossHeight = new Dictionary<int, double>();
        Func<int> ratesCountStep =()=> (ratesCount / 100) + 1;
        for (; ratesCount < rates.Length; ratesCount += ratesCountStep()) {
          var rates0 = new double[(ratesCount + ratesCountStep()).Min(rates.Length)];
          Array.Copy(rates, rates0, rates0.Length);
          var stDevHeight = rates0.StDev() * 2;
          var line = rates0.Regression(rates0.Length, 1);
          var line0Max = line.Take(line.Length / 2).ToArray().Zip(rates.Take(rates0.Length / 2), (l, r) => (l - r).Abs() - stDevHeight).Count(d => d >= 0);
          var line1Max = line.Skip(line.Length / 2).ToArray().Zip(rates.Skip(rates0.Length / 2), (l, r) => (l - r).Abs() - stDevHeight).Count(d => d >= 0);
          ratesCrossHeight.Add(rates0.Length, new[] { line0Max, line0Max }.Average());
          hasCrossStart = line0Max + line1Max >= (ratesCount * .01).Ceiling();
          if (!hasCrossStart && !hasCrossStop) continue;
          if (hasCrossStart) { hasCrossStop = true; continue; }
          if (hasCrossStop) break;
        }
        if (!hasCrossStop)
          ratesCount = ratesCrossHeight.OrderByDescending(d => d.Value).First().Key;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesReversed.Take(ratesCount).ToArray();
      }
      return WaveShort.Rates.SkipWhile(r => r.StartDate > startMax).ToArray()
        .ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private CorridorStatistics ScanCorridorStDevUpDown(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = CorridorPrice();
      var startMax = CorridorStopDate == DateTime.MinValue ? DateTime.MaxValue : CorridorStopDate;
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      if (CorridorStartDate.HasValue) {
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= CorridorStartDate.Value).ToArray();
      } else {
        var w = CorridorDistanceRatio.ToInt();
        var dateLeft = RateLast.StartDate.AddDays(-7);
        var dateRight = dateLeft.AddMinutes(-w * BarPeriodInt);
        var rates1 = RatesInternal.SkipWhile(r => r.StartDate < dateRight).TakeWhile(r => r.StartDate <= dateLeft).Reverse().ToArray();
        var ratesForDistance = ratesReversed.Take(CorridorDistanceRatio.ToInt()).ToArray();
        Func<Rate, double> calcDist = r => Math.Pow(r.PriceAvg, DistanceIterations);
        var distance = ratesForDistance.CalcDistance(calcDist);
        var distance1 = rates1.CalcDistance(calcDist);
        var ratio = distance / distance1;
        WaveShortLeft.Rates = null;
        WaveShortLeft.Rates = ratesReversed.Take((CorridorDistanceRatio * ratio).ToInt()).ToArray();
        ratesReversed[0].RunningLow = ratesReversed[0].RunningHigh = cp(ratesReversed[0]);
        ratesReversed.Aggregate((p, n) => {
          var v = cp(n);
          n.RunningLow = p.RunningLow.Min(v);
          n.RunningHigh = p.RunningHigh.Max(v);
          return n;
        });
        var countMin = 2;
        var heightMin = GetValueByTakeProfitFunction(TradingDistanceFunction).Min(RatesHeight * .9);
        var ratesCount = ratesReversed.Count(r => countMin-- > 0 || r.RunningHeight < heightMin);
        var rates = ratesReversed.Select(cp).ToArray();
        var ratesAngle = rates.Take(ratesCount).ToArray().Regress(1).LineSlope().Abs().Angle(PointSize);
        var hasCrossStart = false;
        var hasCrossStop = false;
        var ratesCrossHeight = new Dictionary<int, double>();
        Func<int> ratesCountStep = () => (ratesCount / 100) + 1;
        for (; ratesCount < rates.Length; ratesCount += ratesCountStep()) {
          var rates0 = new double[(ratesCount + ratesCountStep()).Min(rates.Length)];
          Array.Copy(rates, rates0, rates0.Length);
          double[] lineUp;
          double stDevUp;
          double[] lineDown;
          double stDevDown;
          var line = rates0.Regression(rates0.Length, 1);
          GetStDevUpDown(rates0, line, out lineUp, out stDevUp, out lineDown, out stDevDown);
          var line0Max = lineUp.Zip(rates0, (l, r) => (l - r).Abs() - stDevUp * 2).Count(d => d >= 0);
          var line1Max = lineDown.Zip(rates0, (l, r) => (l - r).Abs() - stDevDown * 2).Count(d => d >= 0);
          ratesCrossHeight.Add(rates0.Length, new[] { line0Max, line0Max }.Average());
          hasCrossStart = line0Max + line1Max >= (ratesCount * .01).Ceiling();
          if (!hasCrossStart && !hasCrossStop) continue;
          if (hasCrossStart) { hasCrossStop = true; continue; }
          if (hasCrossStop) break;
        }
        if (!hasCrossStop)
          ratesCount = ratesCrossHeight.OrderByDescending(d => d.Value).First().Key;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesReversed.Take(ratesCount).ToArray();
      }
      return WaveShort.Rates.SkipWhile(r => r.StartDate > startMax).ToArray()
        .ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private static void GetStDevUpDown(double[] rates, double[] line, out double stDevUp, out double stDevDown) {
      double[] ratesUp, ratesDown;
      GetStDevUpDown(rates, line, out ratesUp, out stDevUp, out ratesDown, out stDevDown);
    }
    private static void GetStDevUpDown(double[] rates, double[] line, out double[] ratesUp, out double stDevUp, out double[] ratesDown, out double stDevDown) {
      ratesUp = rates.Where((r, i) => r > line[i]).ToArray();
      stDevUp = ratesUp.StDev() * 2;
      ratesDown = rates.Where((r, i) => r < line[i]).ToArray();
      stDevDown = ratesDown.StDev() * 2;
    }

    private CorridorStatistics ScanCorridorByBalance(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var rates = ratesForCorridor.ReverseIfNot().Select(_priceAvg).ToArray();
      var ratesCount = rates.Length - 1;
      var sign = double.NaN;
      double upDownRatioPrev = 0;
      Func<int, int> ratesStep = (rc) => (rc / 100) + 1;
      var upDownRatios = "".Select(s => new { ud = 0.0, angle = 0.0,vb = new MathExtensions.Box<double>(0) }).Take(0).ToDictionary(a => 0, a => a);
      object upDownRatiosLocker = new object();
      double upDownRatioCma = double.NaN;
      var rateCounts = new List<int>();
      for (; ratesCount >= 10; ratesCount -= ratesStep(ratesCount*0))
        rateCounts.Add(ratesCount);
      rateCounts.AsParallel().ForEach(rateCount => {
        double[] coeffs;
        var line = rates.Regression(rateCount, 1, out coeffs);
        double upCount = line.Select((l, i) => rates[i] > l).Count(d => d);
        double downCount = rateCount - upCount;
        var upDownRatio = Fibonacci.FibRatioSign(upCount, downCount);
        lock (upDownRatiosLocker)
          upDownRatios.Add(rateCount, new { ud = upDownRatio, angle = coeffs.LineSlope().Angle(PointSize), vb = new MathExtensions.Box<double>(0) });
        ratesReversed[rateCount].DistanceHistory = upDownRatioCma = upDownRatioCma.Cma(ratesStep(rates.Length - rateCount), upDownRatio);
        /*
        continue;
        if (upDownRatio.Sign() != sign.IfNaN(upDownRatio.Sign())) {
          ratesCount += (upDownRatio / (upDownRatioPrev - upDownRatio) * ratesStep(ratesCount)).Abs().ToInt();
          break;
        }
        upDownRatioPrev = upDownRatio;
        sign = upDownRatio.Sign();
         */
      });
      upDownRatios.OrderByDescending(kv=>kv.Key).ToArray().SetRegressionPrice1(PolyOrder, kv => kv.Value.ud
        , (kv, d) => { ratesReversed[kv.Key].Distance1 = d; kv.Value.vb.Value = d; });
      ratesReversed.Aggregate((p, n) => {
        p.DistanceHistory = p.DistanceHistory.IfNaN(n.DistanceHistory.IfNaN(0));
        n.DistanceHistory = n.DistanceHistory.IfNaN(p.DistanceHistory.IfNaN(0));
        p.Distance1 = p.Distance1.IfNaN(n.Distance1.IfNaN(0));
        n.Distance1 = n.Distance1.IfNaN(p.Distance1.IfNaN(0));
        return n;
      });
      var udSorted = upDownRatios.Where(kv=>kv.Key>CorridorDistanceRatio).OrderByDescending(ud => ud.Value.ud.Abs()).ToArray();
      ratesCount = udSorted[0].Key;
      //var firstTen = upDownRatios.OrderBy(kv => kv.Value.ud.Abs()).Take(10).OrderByDescending(kv => kv.Value.angle.Abs()).ToArray();
      //if (!WaveShort.HasRates || !firstTen.Any(kv => WaveShort.Rates.Count.Between(kv.Key - ratesStep(kv.Key), kv.Key + ratesStep(kv.Key))))
      //  ratesCount = firstTen.First().Key;
      //else 
      //  ratesCount = WaveShort.Rates.Count + 1;
      WaveShort.Rates = null;
      if (CorridorStartDate.HasValue)
        WaveShort.Rates = ratesReversed.TakeWhile(r => r.StartDate >= CorridorStartDate.Value).ToArray();
      else
        WaveShort.Rates = RatesArray.ReverseIfNot().Take(ratesCount).ToArray();
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      return WaveShort.Rates.SkipWhile(r => r.StartDate > startMax).ToArray()
        .ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    public double CorridorBalance() {
      var rates = CorridorStats.Rates;
      Action<Rate,double> a = (r,d)=>r.PriceAvg1 = d;
      rates.SetRegressionPrice1(1, _priceAvg, a);
      var upCount = rates.Count(r => r.PriceAvg > r.PriceAvg1);
      var downCount = rates.Count(r => r.PriceAvg < r.PriceAvg1);
      return Fibonacci.FibRatioSign(upCount , downCount);
    }
    private CorridorStatistics ScanCorridorByCrosses(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = _priceAvg;// CorridorPrice();
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var variance = Math.Sqrt(ratesForCorridor.Select(r => Math.Pow(cp(r) - _priceAvg(r), 2)).Average());
      var tests = new { count = 0, height = 0.0, rate = 0.0, fib = 0.0, h = 0.0, l = 0.0, length = 0 }.IEnumerable().ToList();
      var locker = new object();
      var rateBottom = ratesReversed.Min(_priceAvg);
      var prices = ratesReversed.Select(cp).ToArray();
      var ratesCount = prices.Length;
      var line = rateBottom;
      Action<int> runCrosses = i => {
        int length;
        var crosses = prices.CrossesInMiddle(line, out length);
        var crossesCount = crosses.Count;
        if (crossesCount > 1) {
          var l = crosses.Where(d => d > 0).Max();
          var h = crosses.Where(d => d < 0).Min().Abs();
          var height = h + l;
          lock (locker)
            tests.Add(new { count = crossesCount, height, rate = line, fib = Fibonacci.FibRatio(h, l), h, l, length });
        }
        line += PointSize;
      };
      var section = PolyOrder;
      var oneThird = RatesHeight / section;
      var oneThirdInPips = InPips(oneThird).ToInt();
      Enumerable.Range(0, oneThirdInPips).AsParallel().ForAll(runCrosses);
      line = rateBottom + oneThird * (section - 1);
      Enumerable.Range(0, oneThirdInPips).AsParallel().ForAll(runCrosses);
      tests = tests.OrderByDescending(t => t.count).ThenByDescending(t => t.length).ToList();
      var testGroup = (tests.Count / 10).Max(1);
      #region tests0
      var tests0 = Enumerable.Range(0, 0.Max(tests.Count - testGroup)).AsParallel().Select(i => {
        var a = tests.Skip(i).Take(testGroup).ToArray();
        var count = a.Max(t1 => t1.count);
        var height = a.Max(t1 => t1.height);
        var rate = a.Average(t1 => t1.rate);
        var l = a.Max(t1 => t1.l);
        var h = a.Max(t1 => t1.h);
        var length = a.Max(t1 => t1.length);
        return new { count, height, rate, fib = Fibonacci.FibRatio(h, l), h, l, length };
      });
      #endregion
      var tests01 = (from t in tests
                     group t by t.count into g
                     select new { count = g.Key, h = g.Max(g1 => g1.h), l = g.Max(g1 => g1.l), height = g.Max(g1 => g1.height), length = g.Max(g1 => g1.length), rate = g.Average(g1 => g1.rate) }
                     ).ToArray();
      var tests1 = tests01.Where(t => t.count > WaveStDevRatio)
        .OrderByDescending(t => t.count)
        .ToList();
      var prevIndex = tests1.IndexOf(tests1.OrderBy(t => (t.rate - MagnetPrice).Abs()).FirstOrDefault());
      if (prevIndex < 0 || prevIndex > tests.Count / 10) prevIndex = 0;
      var test = tests1.Skip(prevIndex).FirstOrDefault();
      Action<double> setCOM = v => { CorridorCorrelation = 0; };
      if (test != null) {
        MagnetPrice = test.rate;
        setCOM = v => {
          var offset = Math.Sqrt(test.l * test.h);// prices.Take(test.length).ToArray().StDev();
          _CenterOfMassBuy = MagnetPrice + offset;
          _CenterOfMassSell = MagnetPrice - offset;
          var count = WaveShort.Rates.Count;
          CorridorCorrelation = count >= ratesForCorridor.Count * .9 && count > CorridorDistanceRatio ? 1 : 0;
        };
      }

      var lenghtByWave = WaveShort.HasRates ? WaveShort.Rates.Count + 1 : CorridorDistanceRatio.ToInt();
      var waveLength0 = tests01.Select(t=>t.length).DefaultIfEmpty(lenghtByWave).First();// prices.PrevNext().SkipWhile(pn => !MagnetPrice.Between(pn[0], pn[1])).Count();
      var waveLength = waveLength0;
      WaveShort.Rates = null;
      WaveShort.Rates = ratesReversed.Take(waveLength).ToArray();
      setCOM(variance);

      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
      corridor.CorridorCrossesCount = tests.Select(t => t.count).FirstOrDefault();
      return corridor;
    }
    private CorridorStatistics ScanCorridorByCrossesWithAngle(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = CorridorPrice();
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var variance = Math.Sqrt(ratesForCorridor.Select(r => Math.Pow(cp(r) - _priceAvg(r), 2)).Average());
      var tests = new { count = 0, height = 0.0, rate = 0.0, fib = 0.0, h = 0.0, l = 0.0, length = 0 }.IEnumerable().ToList();
      var locker = new object();
      var rateBottom = ratesReversed.Min(_priceAvg);
      var prices = ratesReversed.Select(cp).ToArray();
      var ratesCount = prices.Length;
      var line = rateBottom;
      Action<int> runCrosses = i => {
        int length;
        var crosses = prices.CrossesInMiddle(line, out length);
        var crossesCount = crosses.Count;
        if (crossesCount > 1) {
          var l = crosses.Where(d => d > 0).Max();
          var h = crosses.Where(d => d < 0).Min().Abs();
          var height = h + l;
          lock (locker)
            tests.Add(new { count = crossesCount, height, rate = line, fib = Fibonacci.FibRatio(h, l), h, l, length });
        }
        line += PointSize;
      };
      var section = PolyOrder;
      var oneThird = RatesHeight / section;
      var oneThirdInPips = InPips(oneThird).ToInt();
      Enumerable.Range(0, oneThirdInPips).AsParallel().ForAll(runCrosses);
      line = rateBottom + oneThird * (section - 1);
      Enumerable.Range(0, oneThirdInPips).AsParallel().ForAll(runCrosses);
      tests = tests.OrderByDescending(t => t.count).ThenByDescending(t => t.length).ToList();
      var testGroup = (tests.Count / 10).Max(1);
      #region tests0
      var tests0 = Enumerable.Range(0, 0.Max(tests.Count - testGroup)).AsParallel().Select(i => {
        var a = tests.Skip(i).Take(testGroup).ToArray();
        var count = a.Max(t1 => t1.count);
        var height = a.Max(t1 => t1.height);
        var rate = a.Average(t1 => t1.rate);
        var l = a.Max(t1 => t1.l);
        var h = a.Max(t1 => t1.h);
        var length = a.Max(t1 => t1.length);
        return new { count, height, rate, fib = Fibonacci.FibRatio(h, l), h, l, length };
      });
      #endregion
      var tests01 = (from t in tests
                     group t by t.count into g
                     select new { count = g.Key, h = g.Max(g1 => g1.h), l = g.Max(g1 => g1.l), height = g.Max(g1 => g1.height), length = g.Max(g1 => g1.length), rate = g.Average(g1 => g1.rate) }
                     ).ToArray();
      var tests1 = tests01.Where(t => t.count > WaveStDevRatio)
        .OrderByDescending(t => t.count)
        .ToList();
      var prevIndex = tests1.IndexOf(tests1.OrderBy(t => (t.rate - MagnetPrice).Abs()).FirstOrDefault());
      if (prevIndex < 0 || prevIndex > tests.Count / 10) prevIndex = 0;
      var test = tests1.Skip(prevIndex).FirstOrDefault();
      Action<double> setCOM = v => { CorridorCorrelation = 0; };
      if (test != null) {
        MagnetPrice = test.rate;
        setCOM = v => {
          var offset = Math.Sqrt(test.l * test.h);// prices.Take(test.length).ToArray().StDev();
          _CenterOfMassBuy = MagnetPrice + offset;
          _CenterOfMassSell = MagnetPrice - offset;
          var count = WaveShort.Rates.Count;
          CorridorCorrelation = count >= ratesForCorridor.Count * .9 && count > CorridorDistanceRatio ? 1 : 0;
        };
      }
      var waveLength0 = tests01[0].length;// prices.PrevNext().SkipWhile(pn => !MagnetPrice.Between(pn[0], pn[1])).Count();
      var waveLength = waveLength0;
      WaveShort.Rates = null;
      WaveShort.Rates = ratesReversed.Take(waveLength).ToArray();
      setCOM(variance);

      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);

      if (test == null) {
        var count = corridor.Rates.Count;
        var coeffs = corridor.Coeffs;
        var height = corridor.StDevByHeight;
        var bottom = coeffs[0] - height*1.5;
        var values = corridor.Rates.Select(cp).ToArray();
        tests.Clear();
        var crossesMax = 0;
        Enumerable.Range(0, InPips(height * 3).Floor()).AsParallel().ForEach(i => {
          if (crossesMax >= WaveStDevRatio) return;
          var cf = new[] { bottom, coeffs[1] };
          var ray = cf.RegressionValues(count);
          crossesMax = values.CrossesInMiddle(ray).Count.Max(crossesMax);
          bottom += PointSize;
        });
        corridor.CorridorCrossesCount = crossesMax;
        if (crossesMax >= WaveStDevRatio && coeffs[1].Angle(PointSize) <= 0) {
          _crossesOk = true;
          MagnetPrice = coeffs[0];
          var offset = corridor.StDevByHeight.Min(corridor.StDevByPriceAvg) * 2;
          _CenterOfMassBuy = MagnetPrice + offset;
          _CenterOfMassSell = MagnetPrice - offset;
        } else
          _crossesOk = false;
      } else {
        _crossesOk = true;
        corridor.CorridorCrossesCount = test.count;
      }
      return corridor;
    }
    private CorridorStatistics ScanCorridorByCrosses_2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      //ratesForCorridor = DayWaveByDistance(ratesForCorridor).OrderBars().ToArray();
      int groupLength = 5;
      var cp = CorridorPrice();
      var dateMin = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(this.IsCorridorForwardOnly ? 0 : -BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var ratesReversed = ratesForCorridor/*.SkipWhile(r => r.StartDate < dateMin).ToArray()*/.ReverseIfNot();
      var variance = Math.Sqrt(ratesForCorridor.Select(r => Math.Pow(cp(r) - _priceAvg(r), 2)).Average());
      var tests = new { count = 0, height = 0.0, rate = 0.0, fib = 0.0, h = 0.0, l = 0.0,length = 0 }.IEnumerable().ToList();
      var locker = new object();
      var rateBottom = ratesReversed.Min(_priceAvg);
      var prices = ratesReversed.Select(cp).ToArray();
      var waveletteLeft = 0;// prices.Wavelette().Count;
      var waveletteRight = prices.Reverse().ToArray().Wavelette().Count;
      prices = prices.Skip(waveletteLeft).Take(prices.Length - waveletteLeft - waveletteRight).ToArray();
      var ratesCount = prices.Length;
      var line = rateBottom;
      Enumerable.Range(0, RatesHeightInPips.Floor()).AsParallel().ForAll(i => {
        int length;
        var crosses = prices.CrossesInMiddle(line,out length);
        var crossesCount = crosses.Count;
        if (crossesCount > 1) {
          var l = crosses.Where(d => d > 0).Max();
          var h = crosses.Where(d => d < 0).Min().Abs();
          var height = h + l;
          lock (locker)
            tests.Add(new { count = crossesCount, height, rate = line, fib = Fibonacci.FibRatio(h, l), h, l,length });
        }
        line += PointSize;
      });
      var tests1 = tests//.Where(t => t.height > StDevByHeight.Max(StDevByPriceAvg))
        .OrderByDescending(t => t.height / t.fib)
        .ToList();
      var prevIndex = tests1.IndexOf(tests1.OrderBy(t => (t.rate - MagnetPrice).Abs()).First());
      if (prevIndex < 0 || prevIndex > tests.Count / 2) prevIndex = 0;
      var test = tests1[prevIndex];
      Action<double> setCOM = v => { CorridorCorrelation = 0; };
      if (tests1.Any()) {
        MagnetPrice = test.rate;
        setCOM = v => {
          var offset = test.height;
          _CenterOfMassBuy = MagnetPrice + offset;
          _CenterOfMassSell = MagnetPrice - offset;
          CorridorCorrelation = WaveShort.Rates.Count > RatesArray.Count * .75 ? 1 : 0;
        };
      }
      var waveLength0 = test.length;// prices.PrevNext().SkipWhile(pn => !MagnetPrice.Between(pn[0], pn[1])).Count();
      var waveLength = waveLength0;
      WaveShort.Rates = null;
      WaveShort.Rates = ratesReversed.Take(waveLength).ToArray();
      setCOM(variance);

      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private CorridorStatistics ScanCorridorByCrosses_1(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      int groupLength = 5;
      var cp = CorridorPrice();
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var dateMin =  WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(this.IsCorridorForwardOnly ? 0 : -BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var ratesShrunken = ratesReversed.TakeWhile(r => r.StartDate >= dateMin).Shrink(cp, groupLength).ToArray();
      double max = double.NaN, min = double.NaN;
      int countMin = 1;
      var stDev = StDevByHeight.Max(StDevByPriceAvg);
      foreach (var rate in ratesShrunken) {
        max = rate.Max(max);
        min = rate.Min(min);
        if (max - min > stDev) break;
        countMin++;
      }
      var crossesMax = 0;
      var tests = new { count = 0, length = 0, height = 0.0, angle = 0.0 }.IEnumerable().ToList();
      var locker = new object();
      Enumerable.Range(countMin, (ratesShrunken.Length - countMin).Max(0)).AsParallel().ForAll(rates => {
        double[] prices = null;
        double[] coeffs;
        var line = ratesShrunken.Regression(rates, 1, out coeffs, out prices);
        var angle = coeffs[1].Angle(PointSize) / groupLength;
        var sineOffset = Math.Cos(angle * Math.PI / 180).Abs();
        var crosses = line.CrossesInMiddle(prices);
        var crossesCount = crosses.Count;
        if (crossesCount == 0) return;
        var height = crosses.Max(d => d.Abs()); ;
        lock (locker)
          tests.Add(new { count = crossesCount, length = rates,  height,angle });
      });
      var tests1 = tests.Where(t => t.angle.Abs() <= 1 && t.height > StDevByHeight+StDevByPriceAvg)
        .OrderByDescending(t => t.height)
        .ThenBy(t => t.length)
        .ToArray();
      if (tests1.Any()) {
        CorridorCorrelation = 1;
        var lengthMax = tests1[0].length * groupLength;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(lengthMax).ToArray();
      } else {
        CorridorCorrelation = 0;
        var dm = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate : DateTime.MinValue;
        WaveShort.Rates = null;
        WaveShort.Rates = ratesReversed.TakeWhile(r => r.StartDate >= dm).ToArray();
      }
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ScanCorridorByStDev(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = _priceAvg ?? CorridorPrice();
      var ratesReversed = ratesForCorridor.ReverseIfNot().Select(cp).ToArray();
      var rateLastIndex = 1;
      ratesReversed.TakeWhile(r => ratesReversed.Take(++rateLastIndex).ToArray().Height() < StDevByPriceAvg+StDevByHeight).Count();
      for (; rateLastIndex < ratesReversed.Length - 1; rateLastIndex += (rateLastIndex / 100) + 1) {
        var coeffs = ratesReversed.Take(rateLastIndex).ToArray().Regress(1);
        if (coeffs[1].Angle(PointSize).Abs() < 1)
          break;
      }
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(rateLastIndex).ToArray();
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorPrice, CorridorPrice, TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    #endregion
    private IList<Rate> DayWaveByDistanceByPriceHeight(IList<Rate> ratesForCorridor, double periods) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed[0].Distance1 = 0;
      ratesReversed[0].RunningHigh = double.MinValue;
      ratesReversed[0].RunningLow = double.MaxValue;
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        var height = (n.PriceHigh - n.PriceLow) / PointSize;
        height = Math.Pow(height, DistanceIterations);
        n.Distance1 = p.Distance1 + (IsCorridorForwardOnly ? 1 / height : height);
        return /*1 /*/ height;
      }, ratesReversed[0].PriceHigh - ratesReversed[0].PriceLow);
      var ratesDistance = ratesForCorridor.Distance();
      var dayRatio = ratesDistance * periods / periods.Max(ratesForCorridor.Count);
      Func<Rate, bool> distComp = r => r.Distance <= dayRatio;
      return ratesReversed.TakeWhile(distComp).ToArray();
    }
    private IList<Rate> DayWaveByDistance(IList<Rate> ratesForCorridor, double periods, Func<Rate,double> price = null) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed[0].Distance1 = 0;
      ratesReversed[0].RunningHigh = double.MinValue;
      ratesReversed[0].RunningLow = double.MaxValue;
      var hikeMin = 1 / 10;
      var cp = price ?? CorridorPrice();
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        var height = (cp(p) - cp(n)).Abs() / PointSize;
        height = Math.Pow(height, DistanceIterations);
        n.Distance1 = p.Distance1 + (IsCorridorForwardOnly ? 1 / height.Max(hikeMin) : height);
        return /*1 /*/ height;
      });
      var ratesDistance = ratesForCorridor.Distance();
      var dayRatio = ratesDistance * periods / periods.Max(ratesForCorridor.Count);
      return ratesReversed.TakeWhile(r => r.Distance <= dayRatio).ToArray();
    }

    #endregion

    #region DistanceIterationsReal
    void DistanceIterationsRealClear() { _DistanceIterationsReal = 0; }
    private double _DistanceIterationsReal;
    private bool _crossesOk;

    #region DistancePerBar
    private double _DistancePerBar;
    public double DistancePerBar {
      get { return _DistancePerBar; }
      set {
        if (_DistancePerBar != value) {
          _DistancePerBar = value;
          OnPropertyChanged("DistancePerBar");
        }
      }
    }
    #endregion
    #region DistancePerPip
    private double _DistancePerPip;
    public double DistancePerPip {
      get { return _DistancePerPip; }
      set {
        if (_DistancePerPip != value) {
          _DistancePerPip = value;
          OnPropertyChanged("DistancePerPip");
        }
      }
    }
    #endregion
    
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

    public double CorridorCorrelation { get; set; }
  }
}
