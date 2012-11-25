using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

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
    private CorridorStatistics ScanCorridorByDistanceHalf(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var pipSize = 1 / PointSize;
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        return 1/(p.PriceHigh - p.PriceLow) /** (p.PriceCMALast - n.PriceCMALast).Abs()*/ * pipSize;
      });
      var ratesDistance = ratesForCorridor.Distance();
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      this.RatesStDevAdjusted = RatesStDev * RatesStDevToRatesHeightRatio / CorridorDistanceRatio;
      var c = ratesReversed.TakeWhile(r => r.Distance <= ratesDistance / 2).Count();
      var waveByDistance = ratesReversed.Take(/*ratesReversed.Count -*/ c).ToArray();
      WaveDistance = waveByDistance.LastBC().Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      if (!WaveShort.HasDistance) {
        WaveShort.Rates = null;
        WaveShort.Rates = waveByDistance;
      } else
        WaveShort.SetRatesByDistance(ratesReversed);
      WaveShortLeft.Rates = null;
      WaveShortLeft.Rates = ratesForCorridor.Take(ratesForCorridor.Count- WaveShort.Rates.Count).ToArray();

      var startStopRates = WaveShort.Rates.ReverseIfNot();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates.LastBC().StartDate)
        .Max(IsCorridorForwardOnly ? CorridorStats.StartDate : DateTime.MinValue);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return new CorridorStatistics(this, corridorRates, double.NaN, new[] { corridorRates.Average(r => r.PriceAvg), 0.0 });
      }
      return null;
    }

    private CorridorStatistics ScanCorridorByDistanceQuater(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed[0].Distance1 = 0;
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        var height = (p.PriceHigh - p.PriceLow);
        n.Distance1 = p.Distance1 + 1 / height * PointSize;
        return /*1 /*/ height / PointSize;
      });
      var ratesDistance = ratesForCorridor.Distance();
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      var halfRatio = ratesDistance / (CorridorDistanceRatio > 10 ? BarsCount / CorridorDistanceRatio : CorridorDistanceRatio.Max(1));
      var c = ratesReversed.Count(r => r.Distance <= halfRatio) + 1;
      var waveByDistance = ratesReversed.Take(/*ratesReversed.Count -*/ c).ToArray();
      WaveDistance = waveByDistance.LastBC().Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      WaveShort.Rates = null;
      if (!WaveShort.HasDistance) WaveShort.Rates = waveByDistance;
      else WaveShort.SetRatesByDistance(ratesReversed);
      WaveShortLeft.Rates = null;
      WaveShortLeft.Rates = ratesForCorridor.Take(ratesForCorridor.Count - WaveShort.Rates.Count).ToArray();

      WaveShort.Rates.FillRunningValue((r, d) => r.Distance1 = d, r => r.Distance1, (p, n) => 1/(p.PriceHigh - p.PriceLow) * PointSize);

      var distanceShort = WaveShort.Rates.LastBC().Distance1 / 2;
      WaveTradeStart.Rates = null;
      WaveTradeStart.Rates = WaveShort.Rates.TakeWhile(r => r.Distance1 < distanceShort).ToArray();

      var startStopRates = WaveShort.Rates.ReverseIfNot();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates.LastBC().StartDate);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return WaveShort.Rates.ScanCorridorWithAngle(r => r.PriceAvg, r => r.PriceAvg, TimeSpan.Zero, PointSize, CorridorCalcMethod);
      }
      return null;
    }

    private CorridorStatistics ScanCorridorByDistanceQuater1(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed[0].Distance1 = 0;
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        var height = (p.PriceAvg - n.PriceAvg).Abs() / PointSize;
        height = height;// *height * height;
        n.Distance1 = p.Distance1 + (IsCorridorForwardOnly ? 1 / height : height);
        return /*1 /*/ height;
      });
      var ratesDistance = ratesForCorridor.Distance();
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      var halfRatio = ratesDistance / (CorridorDistanceRatio > 10 ? BarsCount / CorridorDistanceRatio : CorridorDistanceRatio.Max(1));
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

      var distanceShort = WaveShort.Rates.LastBC().Distance1 / 2;
      WaveTradeStart.Rates = null;
      WaveTradeStart.Rates = WaveShort.Rates.TakeWhile(r => r.Distance1 < distanceShort).ToArray();

      var distanceLeft = WaveDistance * 2;
      WaveShortLeft.Rates = null;
      WaveShortLeft.Rates = ratesReversed.Skip(WaveShort.Rates.Count).Take(WaveShort.Rates.Count - WaveTradeStart.Rates.Count).ToArray();

      if (!WaveTradeStart.HasRates || !WaveShortLeft.HasRates) return null;

      var startStopRates = WaveShort.Rates.ReverseIfNot();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates.LastBC().StartDate);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return WaveShort.Rates.ScanCorridorWithAngle(r => r.PriceAvg, r => r.PriceAvg, TimeSpan.Zero, PointSize, CorridorCalcMethod);
      }
      return null;
    }

    private CorridorStatistics ScanCorridorByDistanceQuater2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed[0].Distance1 = 0;
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        var height = (p.PriceAvg - n.PriceAvg).Abs() / PointSize;
        n.Distance1 = p.Distance1 + (IsCorridorForwardOnly ? 1 / height : height);
        return /*1 /*/ height;
      });
      var ratesDistance = ratesForCorridor.Distance();
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      CorridorLength = CorridorDistanceRatio * (1 + Fibonacci.FibRatioSign(StDevByPriceAvg, StDevByHeight).Max(0));
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

      var distanceShort = WaveShort.Rates.LastBC().Distance1 / 2;
      WaveTradeStart.Rates = null;
      WaveTradeStart.Rates = WaveShort.Rates.TakeWhile(r => r.Distance1 < distanceShort).ToArray();

      var distanceLeft = WaveDistance * 2;
      WaveShortLeft.Rates = null;
      WaveShortLeft.Rates = ratesReversed.Take(CorridorLength.ToInt()).ToArray();

      if (!WaveTradeStart.HasRates || !WaveShortLeft.HasRates) return null;

      var startStopRates = WaveShort.Rates.ReverseIfNot();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates.LastBC().StartDate);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return WaveShort.Rates.ScanCorridorWithAngle(r => r.PriceAvg, r => r.PriceAvg, TimeSpan.Zero, PointSize, CorridorCalcMethod);
      }
      return null;
    }


    private CorridorStatistics ScanCorridorByStDev(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed.FillRunningValue((r, d) => { }, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        return double.NaN;
      });
      RateLast.Mass = 0;// ratesReversed.Take(ratesReversed.Count).Average(r => r.PriceCmaRatio);

      this.RatesStDevAdjusted = RatesStDev * RatesStDevToRatesHeightRatio / CorridorDistanceRatio;
      var waveByStDev = ratesReversed.TakeWhile(r => !(r.RunningHeight >= RatesStDev * RatesStDevToRatesHeightRatio / CorridorDistanceRatio)).ToArray();
      if (waveByStDev.Length == 1) waveByStDev = ratesReversed.Take(2).ToArray();
      WaveDistance = waveByStDev.LastBC().Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      if (!WaveShort.HasDistance)
        WaveShort.Rates = waveByStDev;
      else
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
    private CorridorStatistics ScanCorridorByDistanceByRatesHeight(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var pipSize = 1 / PointSize;
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        return (p.PriceHigh - p.PriceLow) * (p.PriceCMALast - n.PriceCMALast).Abs() * pipSize;
      });
      //RatesArray.ReverseIfNot().FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => (p.PriceHigh - p.PriceLow));
      _waves = ratesReversed.Partition(r => r.PriceStdDev != 0).ToArray();
      foreach (var wave in _waves) {
        wave[0].Mass = 0;
        foreach (var rate in wave.Skip(1))
          rate.Mass = rate.Distance - wave[0].Distance;
      }
      Func<IList<Rate>, double> getMesure = w => /*w.MaxStDev() * */w.Distance();

      var waveMax = _waves.OrderByDescending(getMesure).First();//.OrderByDescending(w => w.MaxStDev()).First();
      WaveHigh = _waves.SkipWhile(w => w.Distance() * 2 < waveMax.Distance()).TakeWhile(w => w != waveMax).DefaultIfEmpty(waveMax).OrderByDescending(getMesure).Take(1).OrderByDescending(w => w[0].StartDate).First();

      WaveLength = _waves.Select(w => (double)w.Count).ToArray().AverageInRange(WaveAverageIteration.ToInt() - 1, WaveAverageIteration.ToInt() - 1).Average().ToInt();
      if (false) WaveAverage = _waves.Select(w => w.Height()).ToArray().AverageInRange(WaveAverageIteration.ToInt()).Average();

      WaveDistanceForTrade = RatesHeight;
      this.RatesStDevAdjusted = RatesStDev * RatesStDevToRatesHeightRatio / CorridorDistanceRatio;
      var waveByStDev = ratesReversed.TakeWhile(r => !(r.RunningHeight >= RatesStDev * RatesStDevToRatesHeightRatio / CorridorDistanceRatio)).ToArray();
      WaveDistance = waveByStDev.LastBC().Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      if (!WaveShort.HasDistance)
        WaveShort.Rates = waveByStDev;
      else
        WaveShort.SetRatesByDistance(ratesReversed);

      var startStopRates = WaveHigh.GetWaveStartStopRates();
      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates[0].StartDate)
        .Max(IsCorridorForwardOnly ? CorridorStats.StartDate : DateTime.MinValue);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return new CorridorStatistics(this, corridorRates, double.NaN, new[] { corridorRates.Average(r => r.PriceAvg), 0.0 });
      }
      return null;
    }
    #endregion
    #region Old
    private CorridorStatistics ScanCorridorByPercentage(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      {
        RatesArray.ReverseIfNot().FillDistanceByHeight();
        _waves = ratesForCorridor.Partition(r => r.PriceStdDev != 0).Where(l => l.Count > 1).ToList();
        var a = _waves.Select(w => w.Height()).ToArray();
        var b = a.AverageByIterations(WaveAverageIteration, false).Average();
        var c = a.AverageByIterations(WaveAverageIteration - 1).Average();
        WaveAverage = a.Where(v => v.Between(c, b)).Average();
        a = _waves.Select(w => (double)w.Count).ToArray();
        WaveLength = a.AverageInRange(WaveAverageIteration.ToInt()).Average().ToInt();
      }
      int indexMax = CorridorStartDate.HasValue ? RatesArray.Count(r => r.StartDate >= CorridorStartDate.Value) : 0;
      if (indexMax == 0) {
        int index = (RatesArray.Count / 15.0).ToInt();
        var rev = RatesArray.ReverseIfNot().ToArray();
        double avg = 0;
        double distance = double.MinValue;
        double mp1 = 0;
        int mp2 = 0;
        double min = double.NaN, max = double.NaN;
        #region getWaveLevels
        Func<IList<Rate>, double> getWaveLevels = wave => {
          var rate = wave.LastBC();
          min = min.Min(rate.PriceAvg);
          max = max.Max(rate.PriceAvg);
          mp1 += rate.PriceAvg;
          mp2++;
          avg = (mp1 / mp2);
          return (avg - (max + min) / 2).Abs();
        };
        #endregion
        #region Init global locals
        {// Init global locals
          try {
            var rates1 = rev.Take(index).Where(r => r.Spread > 0).ToArray();
            min = rates1.Min(r => r.PriceAvg);
            max = rates1.Max(r => r.PriceAvg);
            mp1 = rates1.Sum(r => r.PriceAvg);
            mp2 = rates1.Length;
            index++;
          } catch (Exception exc) {
            Log = exc;
          }
        }
        #endregion
        var length = CorridorStats.Rates.Count > 0 && false ? (CorridorStats.Rates.Count * 1.1).ToInt().Min(rev.Length) : rev.Length;
        for (; index <= length; index++) {
          var rates = new Rate[index];
          Array.Copy(rev, rates, index);
          var d = getWaveLevels(rates);
          if (!double.IsNaN(d)) {
            if (d >= distance) {
              indexMax = index;
              distance = d;
            }
          }
        }
        MagnetPricePosition = distance;
      }
      var ratesByIndexMax = RatesArray.TakeEx(-indexMax).ToArray();
      var bigBarStart = ratesByIndexMax[0].StartDate.Max(CorridorStats.Rates.Count > 0 ? CorridorStats.StartDate : DateTime.MinValue);
      {
        var b = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).ToList();
        if (b.Count > 1) {
          SetMagnetPrice(b);
          CorridorsRates.Clear();
          CorridorsRates.Add(b);
          return CorridorsRates[0].ReverseIfNot().ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
        }
      }
      return null;
    }

    private CorridorStatistics ScanCorridorByPercentage2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      {
        _waves = ratesForCorridor.Partition(r => r.PriceStdDev != 0).Where(l => l.Count > 1).ToList();
        var a = _waves.Select(w => w.Height()).ToArray();
        var b = a.AverageByIterations(WaveAverageIteration, false).Average();
        var c = a.AverageByIterations(WaveAverageIteration - 1).Average();
        WaveAverage = a.Where(v => v.Between(c, b)).Average();
        a = _waves.Select(w => (double)w.Count).ToArray();
        WaveLength = a.AverageInRange(WaveAverageIteration.ToInt()).Average().ToInt();
      }
      int indexMax = CorridorStartDate.HasValue ? RatesArray.Count(r => r.StartDate >= CorridorStartDate.Value) : 0;
      if (indexMax == 0) {
        int index = (RatesArray.Count / 15.0).ToInt();
        var rev = RatesArray.ReverseIfNot().ToArray();
        double avg = 0;
        double distance = double.MinValue;
        double mp1 = 0;
        int mp2 = 0;
        double min = double.NaN, max = double.NaN;
        Func<IList<Rate>, double, double, double, bool> isWaveOk = (wave, a, l1, l2) => {
          if (a.Between(l1, l2)) return false;
          var up = wave[0].PriceAvg < wave.LastBC().PriceAvg;
          return up && a < l2 || !up && a > l1;
        };
        var okIndexes = "".Select(a => new { d = 0.0, i = 0 }).ToList();
        #region getWaveLevels
        Func<IList<Rate>, double> getWaveLevels = wave => {
          var rate = wave.LastBC();
          min = min.Min(rate.PriceAvg);
          max = max.Max(rate.PriceAvg);
          mp1 += rate.PriceAvg;
          mp2++;
          avg = (mp1 / mp2);
          return (avg - (max + min) / 2).Abs();
        };
        #endregion
        #region Init global locals
        {// Init global locals
          try {
            var rates1 = rev.Take(index).Where(r => r.Spread > 0).ToArray();
            min = rates1.Min(r => r.PriceAvg);
            max = rates1.Max(r => r.PriceAvg);
            mp1 = rates1.Sum(r => r.PriceAvg);
            mp2 = rates1.Length;
            index++;
          } catch (Exception exc) {
            Log = exc;
          }
        }
        #endregion
        var length = CorridorStats.Rates.Count > 0 && false ? (CorridorStats.Rates.Count * 1.1).ToInt().Min(rev.Length) : rev.Length;
        indexMax = length;
        for (; index <= length; index++) {
          var rates = new Rate[index];
          Array.Copy(rev, rates, index);
          var d = getWaveLevels(rates);
          if (!double.IsNaN(d)) {
            if (d >= distance) {
              indexMax = index;
              distance = d;
            }
          }
        }
        MagnetPricePosition = distance;
      }
      var ratesByIndexMax = RatesArray.TakeEx(-indexMax).ToArray();
      var bigBarStart = ratesByIndexMax[0].StartDate.Max(CorridorStats.Rates.Count > 0 ? CorridorStats.StartDate : DateTime.MinValue);
      {
        var b = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).ToList();
        if (b.Count > 1) {
          SetMagnetPrice(b);
          WaveHigh = b;
          CorridorsRates.Clear();
          CorridorsRates.Add(b);
          return CorridorsRates[0].ReverseIfNot().ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
        }
      }
      return null;
    }
    private CorridorStatistics ScanCorridorByWaveDistance_Old(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      {
        RatesArray.ReverseIfNot().FillDistanceByHeight();
        _waves = RatesArray.ReverseIfNot().Partition(r => r.PriceStdDev != 0).Where(l => l.Count > 1).ToList();
        var a = _waves.Select(w => w.Height()).ToArray();
        var b = a.AverageByIterations(WaveAverageIteration, false).Average();
        var c = a.AverageByIterations(WaveAverageIteration - 1).Average();
        WaveAverage = a.Where(v => v.Between(c, b)).Average();
        {
          a = _waves.Select(w => (double)w.Count).ToArray();
          var avg = a.Average();
          var stDev = a.StDev();
          var r1 = avg - stDev;
          var r2 = avg + stDev;
          a = a.Where(cnt => cnt.Between(r1, r2)).ToArray();
          //WaveLength = a.AverageInRange(WaveAverageIteration.ToInt()).Average().ToInt();
          WaveLength = a.Average().ToInt();
        }
        var d = _waves.Select(w => new { w, d = w.Distance() }).ToArray().AverageByIterations(w => w.d, (d1, d2) => d1 > d2, 4).OrderByDescending(w => w.w[0].StartDate).ToArray();
        WaveHigh = d.First().w;
        //_waves.OrderByDescending(w => w.Distance()).First();
        WaveDistance = d.Average(w => w.d);
      }
      var bigBarStart = CorridorStartDate.GetValueOrDefault(WaveHigh.LastBC().StartDate.Max(CorridorStats.StartDate));
      {
        var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToList();
        if (corridorRates.Count > 1) {
          return corridorRates.ReverseIfNot().ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
        }
      }
      return null;
    }


    private CorridorStatistics ScanCorridorByWaveRelative(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      //RatesArray.ReverseIfNot().FillDistanceByHeight();
      //RatesArray.ReverseIfNot().FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => (p.PriceHigh - p.PriceLow) * p.Volume);
      RatesArray.ReverseIfNot().FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => (p.PriceHigh - p.PriceLow) * (p.PriceCMALast - n.PriceCMALast).Abs());
      _waves = RatesArray.ReverseIfNot().Partition(r => r.PriceStdDev != 0).ToArray();
      Func<IList<Rate>, double> getMesure = w => w.MaxStDev() * w.Distance();
      var wavesDistanceMin = StDevAverages.LastBC(2);// _waves.Select(w => w.MaxStDev()).Average();///.ToArray().AverageByIterations(-1).Average();
      var waves = _waves.Where(w => w.MaxStDev() >= wavesDistanceMin);
      var waveMax = waves.SkipWhile(w => w.MaxStDev() < StDevAverages[0]).First();//.OrderByDescending(w => w.MaxStDev()).First();
      WaveHigh = waves.TakeWhile(w => w != waveMax).DefaultIfEmpty(waveMax).OrderByDescending(getMesure).Take(2).OrderByDescending(w => w[0].StartDate).First();

      WaveLength = _waves.Select(w => (double)w.Count).ToArray().AverageInRange(WaveAverageIteration.ToInt()).Average().ToInt();
      if (false)
        WaveAverage = _waves.Select(w => w.Height()).ToArray().AverageInRange(WaveAverageIteration.ToInt()).Average();
      WaveDistance = WaveHigh.Distance();// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      WaveShort.Rates = WaveInfo.RatesByDistance(RatesArray, WaveShort.Distance.IfNaN(WaveDistance)).ToArray();

      var bigBarStart = CorridorStartDate.GetValueOrDefault(WaveHigh.LastBC().StartDate)
        .Max(IsCorridorForwardOnly ? CorridorStats.StartDate : DateTime.MinValue);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToList();
      if (corridorRates.Count > 1) {
        return new CorridorStatistics(this, corridorRates, double.NaN, new[] { corridorRates.Average(r => r.PriceAvg), 0.0 });
      }
      return null;
    }

    private CorridorStatistics ScanCorridorByWaveDistance(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      RatesArray.ReverseIfNot().FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => (p.PriceHigh - p.PriceLow) * (p.PriceCMALast - n.PriceCMALast).Abs());
      _waves = RatesArray.ReverseIfNot().Partition(r => r.PriceStdDev != 0).ToArray();
      Func<IList<Rate>, double> getMesure = w => /*w.MaxStDev() * */w.Distance();
      var wavesDistanceMin = StDevAverages.LastBC(2);// _waves.Select(w => w.MaxStDev()).Average();///.ToArray().AverageByIterations(-1).Average();
      var waves = _waves.Where(w => w.MaxStDev() >= wavesDistanceMin);
      var waveMax = waves.SkipWhile(w => w.MaxStDev() < StDevAverages[0]).First();//.OrderByDescending(w => w.MaxStDev()).First();
      WaveHigh = waves.TakeWhile(w => w != waveMax).DefaultIfEmpty(waveMax).OrderByDescending(getMesure).Take(2).OrderByDescending(w => w[0].StartDate).First();

      WaveLength = _waves.Select(w => (double)w.Count).ToArray().AverageInRange(WaveAverageIteration.ToInt()).Average().ToInt();
      if (false)
        WaveAverage = _waves.Select(w => w.Height()).ToArray().AverageInRange(WaveAverageIteration.ToInt()).Average();
      WaveDistance = WaveHigh.Distance();// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      WaveShort.Rates = WaveInfo.RatesByDistance(RatesArray, WaveShort.Distance.IfNaN(WaveDistance)).ToArray();

      var bigBarStart = CorridorStartDate.GetValueOrDefault(WaveHigh.LastBC().StartDate)
        .Max(IsCorridorForwardOnly ? CorridorStats.StartDate : DateTime.MinValue);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToList();
      if (corridorRates.Count > 1) {
        return new CorridorStatistics(this, corridorRates, double.NaN, new[] { corridorRates.Average(r => r.PriceAvg), 0.0 });
      }
      return null;
    }

    private CorridorStatistics ScanCorridorByWaveDistance1(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      RatesArray.ReverseIfNot().FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => (p.PriceHigh - p.PriceLow) * InPips(p.PriceCMALast - n.PriceCMALast).Abs());
      //RatesArray.ReverseIfNot().FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => (p.PriceHigh - p.PriceLow));
      _waves = RatesArray.ReverseIfNot().Partition(r => r.PriceStdDev != 0).ToArray();
      foreach (var wave in _waves) {
        wave[0].Mass = 0;
        foreach (var rate in wave.Skip(1))
          rate.Mass = rate.Distance - wave[0].Distance;
      }
      Func<IList<Rate>, double> getMesure = w => /*w.MaxStDev() * */w.Distance();

      var waveMax = _waves.OrderByDescending(getMesure).First();//.OrderByDescending(w => w.MaxStDev()).First();
      WaveHigh = _waves.SkipWhile(w => w.Distance() * 2 < waveMax.Distance()).TakeWhile(w => w != waveMax).DefaultIfEmpty(waveMax).OrderByDescending(getMesure).Take(1).OrderByDescending(w => w[0].StartDate).First();

      WaveLength = _waves.Select(w => (double)w.Count).ToArray().AverageInRange(WaveAverageIteration.ToInt()).Average().ToInt();
      if (false)
        WaveAverage = _waves.Select(w => w.Height()).ToArray().AverageInRange(WaveAverageIteration.ToInt()).Average();
      var startStopRates = WaveHigh.GetWaveStartStopRates();
      WaveDistance = startStopRates[0].Distance - startStopRates[1].Distance;// waves.Where(w => w[0] > WaveHigh[0]).Select(w => w.Distance()).OrderByDescending(d => d).Take(2).Average(() => WaveHigh.Distance());
      WaveShort.Rates = WaveInfo.RatesByDistance(RatesArray, WaveShort.Distance.IfNaN(WaveDistance)).ToArray();

      var bigBarStart = CorridorStartDate.GetValueOrDefault(startStopRates[0].StartDate)
        .Max(IsCorridorForwardOnly ? CorridorStats.StartDate : DateTime.MinValue);
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToArray();
      if (corridorRates.Length > 1) {
        return new CorridorStatistics(this, corridorRates, double.NaN, new[] { corridorRates.Average(r => r.PriceAvg), 0.0 });
      }
      return null;
    }


    private CorridorStatistics ScanCorridorByFibonacci(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      {
        _waves = ratesForCorridor.Partition(r => r.PriceStdDev != 0).Where(l => l.Count > 1).ToList();
        var a = _waves.Select(w => w.Height()).ToArray();
        var b = a.AverageByIterations(WaveAverageIteration, false).Average();
        var c = a.AverageByIterations(WaveAverageIteration - 1).Average();
        WaveAverage = a.Where(v => v.Between(c, b)).Average();
      }
      int index = 10;
      var rev = RatesArray.ReverseIfNot().ToArray();
      double avg = 0;
      double distance = double.MinValue;
      double mp1 = 0, mp2 = 0;
      double min = double.NaN, max = double.NaN;
      double waveHightMin = WaveAverage;
      Func<IList<Rate>, double, double, double, bool> isWaveOk = (wave, a, l1, l2) => {
        if (a.Between(l1, l2)) return false;
        var up = wave[0].PriceAvg < wave.LastBC().PriceAvg;
        return up && a < l2 || !up && a > l1;
      };
      var okIndexes = "".Select(a => new { d = 0.0, i = 0 }).ToList();
      #region getWaveLevels
      Func<IList<Rate>, double> getWaveLevels = wave => {
        var rate = wave.LastBC();
        min = min.Min(rate.PriceAvg);
        max = max.Max(rate.PriceAvg);
        var height = max - min;
        var middle = min + height / 2;
        //avg = double.IsNaN(avg) ? rates.Average() : (avg * (index - 1) + rates.LastByCount()) / index;
        //avg = CalcMagnetPrice(wave);
        mp1 += rate.PriceAvg;
        mp2++;
        var fibLevels = Fibonacci.Levels(max, min);
        if (fibLevels[4] - fibLevels[5] < waveHightMin) return double.NaN;
        avg = (mp1 / mp2);
        return (((double)avg - fibLevels[4]).Max(fibLevels[5] - (double)avg) - SpreadForCorridor).Max(0);
      };
      #endregion
      #region Init global locals
      {// Init global locals
        try {
          var rates1 = rev.Take(index).Where(r => r.Spread > 0).ToArray();
          min = rates1.Min(r => r.PriceAvg);
          max = rates1.Max(r => r.PriceAvg);
          mp1 = rates1.Sum(r => r.PriceAvg);
          mp2 = rates1.Length;
          index++;
        } catch (Exception exc) {
          Log = exc;
        }
      }
      #endregion
      var length = CorridorStats.Rates.Count > 0 && false ? (CorridorStats.Rates.Count * 1.1).ToInt().Min(rev.Length) : rev.Length;
      int indexMax = length;
      for (; index < length; index++) {
        var rates = new Rate[index];
        Array.Copy(rev, rates, index);
        var d = getWaveLevels(rates);
        if (!double.IsNaN(d)) {
          if (d >= distance) {
            indexMax = index;
            distance = d;
          }
        }
      }
      MagnetPricePosition = distance;
      var ratesByIndexMax = RatesArray.TakeEx(-indexMax).ToArray();
      var bigBarStart = ratesByIndexMax[0].StartDate.Max(CorridorStats.StartDate);
      {
        var b = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).ToList();
        if (b.Count > 1) {
          SetMagnetPrice(b);
          _isWaveOk = isWaveOk(b, MagnetPrice, _CenterOfMassBuy, _CenterOfMassSell);
          WaveHigh = b;
          CorridorsRates.Clear();
          CorridorsRates.Add(b);
          return CorridorsRates[0].ReverseIfNot().ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
        }
      }
      return null;
    }
    #endregion
    #endregion
  }
}
