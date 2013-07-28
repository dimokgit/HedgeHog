using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using HedgeHog.Bars;
using System.ComponentModel;
using C = System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Concurrency;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using HedgeHog.Models;

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
    private CorridorStatistics ScanCorridorByTimeFrameAndAngle(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      WaveShort.Rates = null;
      var isAutoCorridor = startMax.IsMax() && !CorridorStartDate.HasValue;
      var length = CorridorDistanceRatio.ToInt();
      var doubles = Enumerable.Range(0, ratesForCorridor.Count).Select(i => (double)i).ToArray();
      var hasCorridor = false;
      if (isAutoCorridor) {
        var pricesAll = ratesReversed.Select(_priceAvg).ToArray();
        var stop = ratesReversed.Count - length;
        Func<int,int, int[]> iterate = (start,step) => {
          var anglePrev = double.NaN;
          var startPrev = 0;
          for (; start < stop; start += step) {
            var prices = new double[length];
            Array.Copy(pricesAll, start, prices, 0, length);
            var dX = new double[length];
            Array.Copy(doubles, 0, dX, 0, length);
            var slope = dX.Regress(prices,1).LineSlope();
            var angle = AngleFromTangent(slope).Abs();
            if (slope.Sign() != anglePrev.IfNaN(slope).Sign() || angle <= TradingAngleRange) {
              hasCorridor = true;
              return new int[] { startPrev, start };
            }
            anglePrev = slope;
            startPrev = start;
          }
          return new int[] { stop, stop };
        };
        var step1 = length/10;
        var steps = iterate(0,step1);
        steps = iterate((steps[0] - step1).Max(0), 1);
        WaveShort.Rates = ratesReversed.Skip(steps[0]).Take(length).ToArray();
      } else
        WaveShort.Rates = ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      if (hasCorridor) _CorridorStartDateByScan = WaveShort.Rates.LastBC().StartDate;
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ScanCorridorByTime(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      WaveShort.Rates = null;
      WaveShort.Rates = startMax.IsMax() && !CorridorStartDate.HasValue
        ? ratesReversed.Take(CorridorDistanceRatio.ToInt()).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      var ma = WaveShort.Rates.Select(GetPriceMA()).ToArray();
      var rates = WaveShort.Rates.Select(_priceAvg).ToArray();
      var rsdReal = CalcRsd(WaveShort.Rates.Select(_priceAvg).ToArray());
      SetVoltage(RateLast, rsdReal);
      if (GetVoltage(RatesArray[0]).IfNaN(0) == 0)
        Enumerable.Range(1, ratesReversed.Count() - CorridorDistanceRatio.ToInt() - 1).AsParallel().ForEach(i => {
          var rates1 = new Rate[CorridorDistanceRatio.ToInt()];
          Array.Copy(ratesReversed, i, rates1, 0, rates1.Length);
          SetVoltage( rates1[0], CalcRsd(rates1.Select(_priceAvg).ToArray()));
        });
      var volts = ratesForCorridor.Select(GetVoltage).SkipWhile(v => v.IfNaN(0) == 0).ToArray();
      double voltsAvg;
      var voltsStdev = volts.StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg-voltsStdev*2;
      GetVoltageHigh = () => voltsAvg + voltsStdev * 2;
      return corridor;
    }

    private CorridorStatistics ScanCorridorByTimeMinAndAngleMax(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
      Func<int> corridorLengthAction = () => CalcCorridorLengthByMaxAngle(ratesReversed.Select(_priceAvg).ToArray(), CorridorDistanceRatio.ToInt(), DistanceIterations);
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      WaveShort.Rates = null;
      WaveShort.Rates = startMax.IsMax() && !CorridorStartDate.HasValue
        ? ratesReversed.Take(corridorLengthAction()).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      var ma = WaveShort.Rates.Select(GetPriceMA()).ToArray();
      var rates = WaveShort.Rates.Select(_priceAvg).ToArray();
      var rsdReal = CalcRsd(WaveShort.Rates.Select(_priceAvg).ToArray());
      SetVoltage(RateLast, rsdReal);
      var volts = ratesForCorridor.Select(GetVoltage).SkipWhile(v => v.IfNaN(0) == 0).ToArray();
      double voltsAvg;
      var voltsStdev = volts.StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg;
      GetVoltageHigh = () => voltsAvg + voltsStdev * 2;
      SetVoltage2(RateLast, voltsAvg);
      if (GetVoltage(RatesArray[0]).IfNaN(0) == 0) {
        ratesForCorridor.ForEach(r => SetVoltage2(r, voltsAvg));
        Enumerable.Range(1, ratesReversed.Count() - CorridorDistanceRatio.ToInt() - 1).AsParallel().ForEach(i => {
          var rates1 = new Rate[CorridorDistanceRatio.ToInt()];
          Array.Copy(ratesReversed, i, rates1, 0, rates1.Length);
          SetVoltage(rates1[0], CalcRsd(rates1.Select(_priceAvg).ToArray()));
        });
      }
      return corridor;
    }

    private CorridorStatistics ScanCorridorByRsdFft(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot().ToArray();
      var dist = ratesReversed.Distance();
      CorridorStDevRatioMax = FttMax = ratesReversed.Select(_priceAvg).FftFrequency(FftReversed).Min(ratesReversed.Length).ToInt();
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        var date = (CorridorStats.Rates.LastOrDefault() ?? new Rate()).StartDate;
        return ratesReversed.TakeWhile(r => r.StartDate >= date).Count();
      });

      Func<int> corridorLengthAction = () =>
        CalcCorridorLengthByRsdFast(
        ratesReversed.Select(_priceAvg).Take(IsCorridorForwardOnly ? lenghForwardOnly.Value : ratesReversed.Length).ToArray(), CorridorDistanceRatio.ToInt(),
        DistanceIterations);
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      var lengthMax = !IsCorridorForwardOnly || CorridorStats.StartDate.IsMin() ? int.MaxValue : lenghForwardOnly.Value;
      WaveShort.Rates = null;
      WaveShort.Rates = startMax.IsMax() && !CorridorStartDate.HasValue
        ? ratesReversed.Take(corridorLengthAction().Min(lengthMax)).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      var ma = WaveShort.Rates.Select(GetPriceMA()).ToArray();
      var rates = WaveShort.Rates.Select(_priceAvg).ToArray();
      var rsdReal = CalcRsd(WaveShort.Rates.Select(_priceAvg).ToArray());
      return corridor;
    }

    private CorridorStatistics ScanCorridorByFft(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot().ToArray();

      FttMax = ratesReversed.Select(_priceAvg).FftFrequency(FftReversed).ToInt();

      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        var date = CorridorStats.Rates.Last().StartDate;
        return ratesReversed.TakeWhile(r => r.StartDate >= date).Count();
      });
      var lengthMax = !IsCorridorForwardOnly || CorridorStats.StartDate.IsMin() ? int.MaxValue : lenghForwardOnly.Value;
      WaveShort.Rates = null;
      WaveShort.Rates = startMax.IsMax() && !CorridorStartDate.HasValue
        ? ratesReversed.Take(FttMax.Min(lengthMax)).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);

      SetVoltage(ratesReversed[0], WaveShort.Rates.Select(_priceAvg).FftFrequency(FftReversed) / (double)WaveShort.Rates.Count);
      var volts = RatesArray.Select(r => GetVoltage(r)).SkipWhile(v => v.IsNaN()).ToArray();
      double voltsAvg;
      var voltsStdev = volts.StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg;
      GetVoltageHigh = () => voltsAvg + voltsStdev;
      return corridor;
    }

    private int FftFrequency_(double[] rates, bool reversed) {
      alglib.complex[] bins;
      var line = rates.Regression(1);
      IEnumerable<double> ratesFft = rates;
      if (reversed) ratesFft = ratesFft.Reverse();
      alglib.fftr1d(ratesFft.Zip(line, (r, l) => r - l).ToArray(), out bins);
      return bins.Select(b => Math.Sqrt(b.x * b.x + b.y * b.y).ToInt()).Skip(1).Take(5).Max();
    }

    private CorridorStatistics ScanCorridorByRsdMax_New(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot().ToArray();
      var distances = new List<double>();
      SetVoltage(ratesReversed[0], InPips(ratesReversed.Distance2(_priceAvg, distances)));
      if (CorridorCrossesMaximum>0) {
        var distance = GetVoltage(ratesReversed[0]) / CorridorCrossesMaximum;
        CorridorStDevRatioMax = distances.TakeWhile(d => InPips(d) <= distance).Count();
      }
      Func<int> corridorLengthAction = () => CalcCorridorLengthByRsd(ratesReversed.Select(_priceAvg).ToArray(), CorridorDistanceRatio.ToInt(), DistanceIterations);
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        var date = CorridorStats.Rates.Last().StartDate;
        return ratesReversed.TakeWhile(r => r.StartDate >= date).Count();
      });
      var lengthMax = !IsCorridorForwardOnly || CorridorStats.StartDate.IsMin() ? int.MaxValue : lenghForwardOnly.Value;
      WaveShort.Rates = null;
      WaveShort.Rates = startMax.IsMax() && !CorridorStartDate.HasValue
        ? ratesReversed.Take(corridorLengthAction().Min(lengthMax)).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      var ma = WaveShort.Rates.Select(GetPriceMA()).ToArray();
      var rates = WaveShort.Rates.Select(_priceAvg).ToArray();
      ratesReversed[0].Distance = ratesReversed.Distance2(_priceAvg);

      var ratesInternalArray = RatesInternal.ReverseIfNot().ToArray();
      for (var i = 0; i < ratesReversed.Length && GetVoltage(ratesReversed[i]).IsNaN(); i++) {
        var rates1 = new Rate[ratesReversed.Length];
        Array.Copy(ratesInternalArray, i, rates1, 0, rates1.Length);
        SetVoltage(rates1[0], InPips(rates1.Distance2(_priceAvg)));
      }
      alglib.complex[] bins;
      var line = ratesReversed.Select(_priceAvg).ToArray().Regression(1);
      alglib.fftr1d(RatesArray.Zip(line,(r,l)=> r.PriceAvg-l).ToArray(), out bins);
      var magnitudes = bins.Select(b => Math.Sqrt(b.x * b.x + b.y * b.y).ToInt()).ToArray();
      FttMax = magnitudes.Skip(1).Take(5).Max();
      SetVoltage2(ratesReversed[0], FttMax);
      return corridor;
    }

    private CorridorStatistics ScanCorridorByRsdMax_Old(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot().ToArray();
      ratesReversed[0].Distance = ratesReversed.Distance2(_priceAvg);
      var dist = ratesReversed.Distance();
      Func<int> corridorLengthAction = () => CalcCorridorLengthByRsd(ratesReversed.Select(_priceAvg).ToArray(), CorridorDistanceRatio.ToInt(), DistanceIterations);
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      var startMin = CorridorStartDate.GetValueOrDefault(ratesReversed[CorridorDistanceRatio.ToInt() - 1].StartDate);
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        var date = CorridorStats.Rates.Last().StartDate;
        return ratesReversed.TakeWhile(r => r.StartDate >= date).Count();
      });
      var lengthMax = !IsCorridorForwardOnly || CorridorStats.StartDate.IsMin() ? int.MaxValue : lenghForwardOnly.Value;
      WaveShort.Rates = null;
      WaveShort.Rates = startMax.IsMax() && !CorridorStartDate.HasValue
        ? ratesReversed.Take(corridorLengthAction().Min(lengthMax)).ToArray()
        : ratesReversed.SkipWhile(r => r.StartDate > startMax).TakeWhile(r => r.StartDate >= startMin).ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      var ma = WaveShort.Rates.Select(GetPriceMA()).ToArray();
      var rates = WaveShort.Rates.Select(_priceAvg).ToArray();
      var rsdReal = CalcRsd(WaveShort.Rates.Select(_priceAvg).ToArray());
      SetVoltage(RateLast, rsdReal);
      var volts = ratesForCorridor.Select(GetVoltage).SkipWhile(v => v.IfNaN(0) == 0).ToArray();
      double voltsAvg;
      var voltsStdev = volts.StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg;
      GetVoltageHigh = () => voltsAvg + voltsStdev * 2;
      SetVoltage2(RateLast, voltsAvg);
      if (GetVoltage(RatesArray[0]).IfNaN(0) == 0) {
        ratesForCorridor.ForEach(r => SetVoltage2(r, voltsAvg));
        Enumerable.Range(1, ratesReversed.Count() - CorridorDistanceRatio.ToInt() - 1).AsParallel().ForEach(i => {
          var rates1 = new Rate[CorridorDistanceRatio.ToInt()];
          Array.Copy(ratesReversed, i, rates1, 0, rates1.Length);
          SetVoltage(rates1[0], CalcRsd(rates1.Select(_priceAvg).ToArray()));
        });
      }
      return corridor;
    }

    int CalcCorridorLengthByRsdFast(double[] ratesReversed, int countStart, double diffRatio) {
      var rsdMax = double.NaN;
      Func<int, int> calcRsd = (count) => {
        var rates = new double[count];
        Array.Copy(ratesReversed, rates, count);
        var rsd = this.CalcRsd(rates);
        if (rsdMax.Ratio(rsd) > diffRatio) return count;
        if (rsdMax.IsNaN()) rsdMax = rsd;
        return 0;
      };

      var countOut = Partitioner.Create( Enumerable.Range(countStart, 0.Max(ratesReversed.Length - countStart)).ToArray(),true)
        .AsParallel()
        .Select(count => calcRsd(count))
        .FirstOrDefault(count => count > 0);
      return countOut > 0 ? countOut : ratesReversed.Length;
    }


    int CalcCorridorLengthByRsd(double[] ratesReversed, int countStart, double diffRatio) {
      var rsdMax = double.NaN;
      for (; countStart <= ratesReversed.Length; countStart++) {
        var rates = new double[countStart];
        Array.Copy(ratesReversed, rates, countStart);
        var rsd = this.CalcRsd(rates);
        if (rsdMax.Ratio(rsd) > diffRatio)
          break;
        else if (rsdMax.IsNaN())
          rsdMax = rsd;
      }
      return countStart;
    }
    int CalcCorridorLengthByRsdMax(double[] ratesReversed, int countStart, double diffRatio) {
      var rsdMax = 0.0;
      for (; countStart <= ratesReversed.Length; countStart++) {
        var rates = new double[countStart];
        Array.Copy(ratesReversed, rates, countStart);
        var rsd = this.CalcRsd(rates);
        if (rsdMax / rsd > diffRatio)
          break;
        else if (rsd > rsdMax)
          rsdMax = rsd;
      }
      return countStart;
    }
    int CalcCorridorLengthByMaxAngle(double[] ratesReversed, int countStart, double angleDiffRatio) {
      var bp = BarPeriodInt;
      var ps = PointSize;
      var angleMax = 0.0;
      for (; countStart <= ratesReversed.Length; countStart++) {
        var rates = new double[countStart];
        Array.Copy(ratesReversed, rates, countStart);
        var coeffs = rates.Regress(1);
        var angle = coeffs[1].Angle(bp, ps).Abs();
        if (angleMax / angle > angleDiffRatio)
          break;
        else if (angle > angleMax)
          angleMax = angle;
      }
      return countStart;
    }

    int CalcCorridorLengthByMaxAngle2(double[] ratesReversed, int countStart, double angleDiffRatio) {
      var bp = BarPeriodInt;
      var ps = PointSize;
      var angleMax = 0.0;
      var countMax = 0;
      Func<int, double> calcAngle = count => {
        var rates = new double[count];
        Array.Copy(ratesReversed, rates, count);
        var coeffs = rates.Regress(1);
        return coeffs[1].Angle(bp, ps).Abs();
      };
      var obs = Observable.Range(countStart, ratesReversed.Length - countStart)
        .SelectMany(count => Observable.Start(() => new { angle = calcAngle(count), count }, TaskPoolScheduler.Default))
        .Do(a => angleMax = a.angle.Max(angleMax))
        .First(a => angleMax / a.angle > angleDiffRatio);
      return obs.count;
    }

    private double CalcRsd(IList<double> rates) {
      double ratesMin, ratesMax;
      var stDev = rates.StDev(out ratesMax, out ratesMin);
      return stDev / (ratesMax - ratesMin);
    }

    private CorridorStatistics ScanCorridorByHeight(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var corridorHeightMax = InPoints(CorridorHeightMax.Abs());
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      ratesReversed.FillRunningHeight();
      WaveShort.Rates = null;
      WaveShort.Rates = ratesReversed.TakeWhile(r => r.RunningHeight < corridorHeightMax).ToArray();
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

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
        return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        corr = corr.Cma(5, AlgLib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length).Abs());
        lock (locker) {
          correlations.Add(new { corr, length = prices.Length });
        }
      });
      correlations.Sort((a, b) => a.corr.CompareTo(b.corr));
      if (!correlations.Any()) correlations.Add(new { corr, length = 1000000 });
      WaveShort.Rates = null;
      WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(correlations[0].length * groupLength).TakeWhile(r => r.StartDate >= dateMin).ToArray();
      CorridorCorrelation = ratesReversedOriginal.Volatility(_priceAvg, GetPriceMA);
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        corr = corr.Cma(5, AlgLib.correlation.pearsoncorrelation(ref line, ref parabola, rates));
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
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
          var vol = 1 / coeffs[1].Abs().Angle(BarPeriodInt, PointSize);// ratesReversed.Take(rates).ToArray().Volatility(_priceAvg, GetPriceMA);// / coeffs[1].Abs();
          corr = corr.Cma(15, AlgLib.correlation.pearsoncorrelation(ref source, ref parabola, rates));
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
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private CorridorStatistics ScanCorridorByHorizontalLineCrosses(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      double level;
      var rates = CorridorByVerticalLineCrosses2(ratesForCorridor.ReverseIfNot(), _priceAvg, CorridorDistanceRatio.ToInt(), out level);
      var corridorOk = rates != null && rates.Any() && (!IsCorridorForwardOnly || rates.LastBC().StartDate >= CorridorStats.StartDate)
        && MagnetPrice.IfNaN(0).Abs(level) > rates.Height();
      if (corridorOk) {
        MagnetPrice = level;
        WaveShort.ResetRates(rates);
      } else if (CorridorStats.Rates != null && CorridorStats.Rates.Any()) {
        var dateStop = CorridorStats.Rates.LastBC().StartDate;
        WaveShort.ResetRates(ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateStop).ToArray());
      } else
        WaveShort.ResetRates(ratesForCorridor.ReverseIfNot());

      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        corr = corr.Cma(5, AlgLib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length));
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
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        corr = corr.Cma(3, AlgLib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length.Min(parabola.Length)).Abs());
        correlations.Add(new { corr, length = prices.Length });
      }
      correlations.Sort((a, b) => -a.corr.CompareTo(b.corr));
      WaveShort.Rates = null;
      WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(correlations[0].length * groupLength).ToArray();
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        var angle = coeffs[1].Angle(BarPeriodInt, PointSize) / groupLength;
        var sineOffset = Math.Cos(-angle * Math.PI / 180);
        var heights = new List<double>(new double[rates]);
        Enumerable.Range(0, rates).ForEach(i => heights[i] = (line[i] - ratesShrunken[i]) * sineOffset);
        heights.Sort();
        height = heights[0].Abs() + heights.LastBC();
        if (height > stDev) {
          //var parabola = MathExtensions.Sin(180, rates, height / 2, coeffs.RegressionValue(rates / 2), WaveStDevRatio.ToInt());
          var parabola = MathExtensions.Sin(180, rates, height / 2, prices.Average(), WaveStDevRatio.ToInt());
          lock (locker) {
            corr = corr.Cma(25, AlgLib.correlation.pearsoncorrelation(ref prices, ref parabola, rates).Abs());
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
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        var angle = coeffs[1].Angle(BarPeriodInt, PointSize) / groupLength;
        var sineOffset = Math.Cos(-angle * Math.PI / 180);
        var heights = new List<double>(new double[rates]);
        Enumerable.Range(0, rates).ForEach(i => heights[i] = (line[i] - ratesShrunken[i]) * sineOffset);
        heights.Sort();
        height = heights[0].Abs() + heights.LastBC();
        if (height.Between(stDev,stDev*2) && angle.Abs().ToInt() <= 1) {
          //var parabola = MathExtensions.Sin(180, rates, height / 2, coeffs.RegressionValue(rates / 2), WaveStDevRatio.ToInt());
          var parabola = MathExtensions.Sin(180, rates, height / 2, prices.Average(), WaveStDevRatio.ToInt());
          lock (locker) {
            var pearson = AlgLib.correlation.pearsoncorrelation(ref prices, ref parabola, rates).Abs();
            var spearman = AlgLib.correlation.spearmanrankcorrelation(prices, parabola, rates).Abs();
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
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        var ratesAngle = rates.Take(ratesCount).ToArray().Regress(1).LineSlope().Abs().Angle(BarPeriodInt, PointSize);
        for (;ratesCount <rates.Length ; ratesCount += (ratesCount / 100) + 1) {
          var rates0 = new double[ratesCount + 1];
          Array.Copy(rates, rates0, rates0.Length);
          var height = rates0.Height();
          var stDev = rates0.StDev();
          if (WaveStDevRatio > 0 && Fibonacci.FibRatio(height, stDev) < WaveStDevRatio) continue;
          var angle = rates0.Regress(1).LineSlope().Abs().Angle(BarPeriodInt, PointSize);
          if (angle < ratesAngle) break;
          ratesAngle = angle;
        }
        WaveShort.Rates = null;
        WaveShort.Rates = ratesReversed.Take(ratesCount).ToArray();
      }
      return WaveShort.Rates.SkipWhile(r => r.StartDate > startMax).ToArray()
        .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        var ratesAngle = rates.Take(ratesCount).ToArray().Regress(1).LineSlope().Abs().Angle(BarPeriodInt, PointSize);
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
        .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
        var ratesAngle = rates.Take(ratesCount).ToArray().Regress(1).LineSlope().Abs().Angle(BarPeriodInt, PointSize);
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
        .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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

    private CorridorStatistics ScanCorridorVoid(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      alglib.complex[] bins = CalcFftStats(ratesForCorridor, PolyOrder);

      var binAmps = bins.Skip(1).Take(bins.Length / 2).Select(b => Math.Log(b.ComplexValue())).ToArray();
      var xs = Enumerable.Range(1, binAmps.Length).ToArray();
      var binIndexes = xs.Select(i => Math.Log(i)).ToArray();
      var binCoefs = binIndexes.Regress(binAmps, 1);
      var slope = binCoefs.LineSlope();
      var c = Math.Exp(binCoefs.LineValue());
      Func<int, double> powerLine = x => c * Math.Pow(x, slope);
      var powerBins = xs.Select(powerLine).ToArray();

      var binAmplitudes = powerBins.Select((a, i) => new { a, i }).ToArray();
      var binsLine = binAmplitudes.Select(a => a.a).ToArray().Regression(1);
      var binCrosses = binAmplitudes.Crosses(binsLine, d => d.a).ToArray();

      var binsTotal = bins.Skip(1).Take(1440).Select(MathExtensions.ComplexValue).ToArray().Sum();
      var binHigh = powerBins.Take(binCrosses[0].i).Sum();
      var binLow = powerBins.Skip(binCrosses[0].i).Sum();
      var binRatio = binHigh / binLow;
      SetVoltage(RateLast, binRatio);
      var voltsAvg = 0.0;
      var voltsStDev = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).Where(v => !v.IsNaN()).ToArray().StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg;
      GetVoltageHigh = () => voltsAvg - voltsStDev * 2;

      
      var corr = binCrosses[0].i;// binAmplitudes.TakeWhile(b => (binSum += b.a) < binsTotal).Count();

      //Func<int, IEnumerable<alglib.complex>> repeat = (count) => { return Enumerable.Repeat(new alglib.complex(0), count); };
      //var bins1 = bins.Take(corr).Concat(repeat(bins.Length - corr)).ToArray();
      //double[] ifft;
      //alglib.fftr1dinv(bins1, out ifft);
      //Enumerable.Range(0, ifft.Length).ForEach(i => SetVoltage(ratesForCorridor[i], InPips(ifft[i])));

      CorridorStDevRatioMax = powerBins.Take(binCrosses[0].i).Average();// binCrosses[0].i;
      WaveShort.ResetRates(ratesForCorridor.ReverseIfNot().Take(CorridorDistanceRatio.ToInt()).ToArray());
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    class Harmonic : Models.ModelBase {
      #region Position
      private double _Position;
      [Display(Name = "Pos")]
      public double Position {
        get { return _Position; }
        set {
          if (_Position != value) {
            _Position = value;
            RaisePropertyChanged("Position");
          }
        }
      }

      #endregion
      #region Minutes
      private int _Minutes;
      [Display(AutoGenerateField = false)]
      public int Minutes {
        get { return _Minutes; }
        set {
          if (_Minutes != value) {
            _Minutes = value;
            RaisePropertyChanged("Minutes");
            RaisePropertyChanged("Hours");
          }
        }
      }
      #endregion
      
      [Display(Name = "Hrs")]
      public double Hours { get { return (Minutes / 60.0).Round(1); } }
      #region Height
      private double _Height;
      [Display(Name = "Hght")]
      public double Height {
        get { return _Height; }
        set {
          if (_Height != value) {
            _Height = value;
            RaisePropertyChanged("Height");
          }
        }
      }

      #endregion
      #region InRange
      private bool _InRange;
      [Display(AutoGenerateField = false)]
      public bool InRange {
        get { return _InRange; }
        set {
          if (_InRange != value) {
            _InRange = value;
            RaisePropertyChanged("InRange");
            RaisePropertyChanged("Color");
          }
        }
      }
      #endregion
      #region IsAggregate
      private bool _IsAggregate;
      [Display(AutoGenerateField = false)]
      public bool IsAggregate {
        get { return _IsAggregate; }
        set {
          if (_IsAggregate != value) {
            _IsAggregate = value;
            RaisePropertyChanged("IsAggregate");
            RaisePropertyChanged("FontWeight");
          }
        }
      }
      #endregion
      string _Color = Colors.PowderBlue.ToString().Replace("#FF", "#52");
      [Display(AutoGenerateField = false)]
      public string Color {
        get { return InRange ? _Color : ""; }
      }
      [Display(AutoGenerateField = false)]
      public string FontWeight {
        get { return IsAggregate ? "Bold" : "Normal"; }
      }
      public Harmonic() { }
      public Harmonic(int position,int minutes,double height) {
        this.Position = position;
        this.Minutes = minutes;
        this.Height = height;
      }

      public override string ToString() {
        return new { Position, Hours, Height } + "";
      }
    }
    
    IList<Harmonic> _harmonics;
    ValueTrigger<bool> _canTradeByHarmonicsTrigger = new ValueTrigger<bool>(false);
    bool CanTradeByHarmonics() { return HarmonicsAverage < _harmonics.Count / 4.0; }

    int _harmonicStartIndex = 0;
    private CorridorStatistics ScanCorridorByHorizontalLineCrosses2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {

      var binRates = ratesForCorridor;
      var bins = binRates.Select(_priceAvg).FftSignalBins(false);
      var frequencyRange = 20;
      List<Harmonic> harmonics = new List<Harmonic>(new Harmonic[frequencyRange]);
      double[][] iffts = new double[harmonics.Count][];
      Func<double, double> calcMinutes = h => binRates.Count / h;
      ParallelEnumerable.Range(0, harmonics.Count).ForAll(index => {
        var harmonic = index + 1;
        var minutes = calcMinutes(harmonic);
        var rangeMin = minutes - 30;
        var rangeMax = minutes + 30;
        var range = Enumerable.Range(0, harmonics.Count).Where(i => calcMinutes(i).Between(rangeMin, rangeMax)).ToArray();
        var bins1 = bins.FftHarmonic(range[0],range.Length);
        double[] ifft;
        alglib.fftr1dinv(bins1.SafeArray(), out ifft);
        var height = ifft.Max();
        harmonics[index] = new Harmonic(index, minutes.ToInt(), InPips(height).Round(1));
        iffts[index] = ifft;
      });
      harmonics.SortByLambda(w => -w.Height);
      var xxx1 = harmonics.AverageByIterations(w => w.Height, (w, v) => w >= v, PolyOrder).ToArray();
      xxx1.ForEach(w => { w.InRange = true; });
      var harmonicMain = harmonics.Where(w => w.Position > _harmonicStartIndex).ToArray()
        .AverageByIterations(w => w.Height, (w, v) => w >= v, 2)
        .OrderByDescending(w => w.Position).First();
      harmonicMain.IsAggregate = true;
      ParallelEnumerable.Range(0, binRates.Count).ForAll(i => SetVoltage(binRates[i], InPips(iffts[harmonicMain.Position.ToInt()][i])));
      CorridorStDevRatioMax = harmonicMain.Hours * 60;
      _harmonics = harmonics.ToArray();
      HarmonicsAverage = harmonics.TakeWhile(w => w.InRange).Average(w => w.Position);
      harmonics.Add(new Harmonic { Position = HarmonicsAverage.Round(1) });
      GlobalStorage.Instance.ResetGenericList(harmonics);

      double level = double.NaN;
      var rates = !CanTradeByHarmonics() ? null
        : CorridorByVerticalLineCrosses2(ratesForCorridor.ReverseIfNot(), _priceAvg, CorridorDistanceRatio.ToInt(), out level);
      var corridorOk = rates != null && rates.Any() && (!IsCorridorForwardOnly || rates.LastBC().StartDate >= CorridorStats.StartDate)
        && (IsCorridorForwardOnly || MagnetPrice.IfNaN(0).Abs(level) > TradingDistance);
      if (corridorOk) {
        MagnetPrice = level;
        WaveShort.ResetRates(rates);
      } else if (CorridorStats.Rates != null && CorridorStats.Rates.Any()) {
        var dateStop = CorridorStats.Rates.LastBC().StartDate;
        WaveShort.ResetRates(ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateStop).ToArray());
      } else
        WaveShort.ResetRates(ratesForCorridor.ReverseIfNot());

      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private alglib.complex[] CalcFftStats(IList<Rate> corridorRates, int ifftSkpip) {
      alglib.complex[] bins;
      corridorRates.Select(_priceAvg).FftFrequency(false, out bins);
      return bins;
      Func<int, IEnumerable<alglib.complex>> repeat = (count) => { return Enumerable.Repeat(new alglib.complex(0), count); };
      //alglib.fftr1dinv(bins.Take(ifftSkpip).Concat(Enumerable.Repeat(new alglib.complex(0), bins.Length - ifftSkpip)).ToArray(), out ifft);
      //var bins1 = new[] { bins.Take(1), NewMethod(ifftSkpip - 1), new[] { bins[ifftSkpip] }, NewMethod(bins.Length - ifftSkpip - 1) }
      //  .SelectMany(b => b).ToArray();
      var bins1 = new[] { bins.Take(1), repeat(ifftSkpip - 1), bins.Skip(ifftSkpip) }.SelectMany(b => b).ToArray();
      double[] ifft;
      alglib.fftr1dinv(bins1, out ifft);
      //Enumerable.Range(0, corridorRates.Count).ForEach(i => SetVoltage(corridorRates[i], InPips(ifft[i])));
      SetVoltage(RateLast, InPips(ifft.Select(v => v.Abs()).ToArray().AverageByIterations(-1).Average()));
      var voltsAvg = 0.0;
      var voltsStDev = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).Where(v => !v.IsNaN()).ToArray().StDev(out voltsAvg);
      GetVoltageAverage = () => voltsAvg;
      GetVoltageHigh = () => voltsAvg - voltsStDev * 2;
      return bins;
    }

    public Func<double> GetVoltageHigh = () => 0;
    public Func<double> GetVoltageAverage = () => 0;
    public Func<Rate, double> GetVoltage = r => r.DistanceHistory;
    public Action<Rate, double> SetVoltage = (r, v) => r.DistanceHistory = v;
    public Func<Rate, double> GetVoltage2 = r => r.Distance1;
    public Action<Rate, double> SetVoltage2 = (r, v) => r.Distance1 = v;
    private CorridorStatistics ScanCorridorByBalance(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      CrossesDensity = ratesForCorridor.Select(_priceAvg).CrossesInMiddle(ratesForCorridor.Select(GetPriceMA())).Count/(double)ratesForCorridor.Count;
      ratesForCorridor.TakeWhile(r => double.IsNaN(r.CrossesDensity)).ForEach(r => r.CrossesDensity = CrossesDensity);
      ratesForCorridor.LastBC().CrossesDensity = CrossesDensity;
      var crossesDensityAverage = ratesForCorridor.Average(r => r.CrossesDensity);
      ratesForCorridor.TakeWhile(r => double.IsNaN(r.CrossesDensityAverage)).ForEach(r => r.CrossesDensityAverage = crossesDensityAverage);
      ratesForCorridor.LastBC().CrossesDensityAverage = crossesDensityAverage;
      GetVoltageAverage = () => crossesDensityAverage;
      GetVoltage = r => r.CrossesDensity;
      GetVoltage2 = r => r.CrossesDensityAverage;
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      var corridorHeightMax = InPoints(50);
      var corridorHeightMin = InPoints(25);
      ratesReversed[0].RunningLow = ratesReversed[0].RunningHigh = _priceAvg(ratesReversed[0]);
      int corridorLengthMax = 0, corridorLengthMin = 0;
      for (; corridorLengthMax < ratesReversed.Count - 1; corridorLengthMax++) {
        var n = ratesReversed[corridorLengthMax + 1];
        var p = ratesReversed[corridorLengthMax];
        var v = _priceAvg(n);
        n.RunningLow = p.RunningLow.Min(v);
        n.RunningHigh = p.RunningHigh.Max(v);
        if (n.RunningHeight > corridorHeightMin && corridorLengthMin == 0) corridorLengthMin = corridorLengthMax;
        if (n.RunningHeight > corridorHeightMax) break;
      }
      var rates = ratesForCorridor.ReverseIfNot().Take(corridorLengthMax).Select(_priceAvg).ToArray();
      Func<int, Lib.Box<int>> newBoxInt = (i) => new Lib.Box<int>(i);
      Func<double, Lib.Box<double>> newBox0 = (d) => new Lib.Box<double>(d);
      Func<Lib.Box<double>> newBox = () => newBox0(double.NaN);
      Func<int, int> ratesStep = NestStep;
      var udAnon = new { ud = 0.0, angle = 0.0, vb = newBox(), udCma = newBox() };
      var upDownRatios1 = udAnon.IEnumerable().ToDictionary(a => 0, a => a);
      var upDownRatios = new[] { udAnon }.Take(0).ToConcurrentDictionary(a => 0, a => a);
      
      var rateCounts = new List<int>();
      for (var i = rates.Length; i >= 10; i -= ratesStep(i))
        rateCounts.Add(i);
      rateCounts.AsParallel().ForAll(rateCount => {
        double[] coeffs;
        var line = rates.Regression(rateCount, 1, out coeffs);
        double upCount = line.Select((l, i) => rates[i] > l).Count(d => d);
        double downCount = rateCount - upCount;
        var upDownRatio = Fibonacci.FibRatioSign(upCount, downCount);
        upDownRatios.TryAdd(rateCount-1, new { ud = upDownRatio, angle = coeffs.LineSlope().Angle(BarPeriodInt, PointSize), vb = newBox(), udCma = newBox() });
      });
      var ratesCount = corridorLengthMax;

      var upDownRatiosKV = upDownRatios.OrderByDescending(kv => kv.Key)/*.TakeWhile(kv => kv.Key >= corridorLengthMin)*/.ToArray();
      if (upDownRatiosKV.Any()) {
        upDownRatiosKV[0].Value.udCma.Value = upDownRatiosKV[0].Value.ud;
        upDownRatiosKV.Aggregate((p, n) => {
          n.Value.udCma.Value = p.Value.udCma.Value.Cma(ratesStep(rates.Length - p.Key), n.Value.ud);
          return n;
        });

        var upDownRatiosForAreas = Enumerable.Range(upDownRatios.Keys.Min(), upDownRatios.Keys.Max() - upDownRatios.Keys.Min() + 1)
          .Select(i => new { i, v = new { value = newBox0(upDownRatios.Keys.Contains(i) ? upDownRatios[i].udCma : double.NaN), line = newBox0(double.NaN) } }).ToDictionary(a => a.i, a => a.v).ToArray();
        //upDownRatios.OrderBy(kv => kv.Key).SkipWhile(kv => kv.Key < CorridorDistanceRatio).ToArray();
        upDownRatiosForAreas.FillGaps(kv => double.IsNaN(kv.Value.value), kv => kv.Value.value, (kv, d) => kv.Value.value.Value = d);
        Task.Factory.StartNew(() => upDownRatiosForAreas.ForEach(
          kv => ratesReversed[kv.Key].DistanceHistory = kv.Key < CorridorDistanceRatio ? double.NaN : kv.Value.value));
        upDownRatiosForAreas.SetRegressionPrice1(PolyOrder, kv => kv.Value.value
          , (kv, d) => { if (kv.Key > CorridorDistanceRatio) ratesReversed[kv.Key].Distance1 = d; kv.Value.line.Value = d; });

        var areaInfos = upDownRatiosForAreas.OrderBy(kv => kv.Key).SkipWhile(kv => kv.Key < CorridorDistanceRatio).ToArray()
          .Areas(kv => kv.Key, kv => kv.Value.value - kv.Value.line);
        areaInfos.Remove(areaInfos.Last());
        if (areaInfos.Any()) {
          ratesCount = areaInfos.OrderByDescending(ai => ai.Area.Abs()).First().Stop;
          var angle = 0.0;
          foreach (var udr in upDownRatios.OrderBy(ud => ud.Key).SkipWhile(ud => ud.Key < ratesCount)) {
            if (udr.Value.angle.Abs() >= angle) {
              angle = udr.Value.angle.Abs();
              ratesCount = udr.Key;
            } else break;
          }
        } else {
          var udSorted = upDownRatios.Where(kv => kv.Key > CorridorDistanceRatio)
            .OrderByDescending(ud => ud.Value.udCma.Value.Abs() + ud.Value.udCma.Value.Abs(ud.Value.vb)).ToArray();
          if (DistanceIterations >= 0 ? udSorted[0].Value.udCma.Value.Abs() > DistanceIterations : udSorted[0].Value.udCma.Value.Abs() < DistanceIterations.Abs())
            ratesCount = udSorted[0].Key;
          else if (WaveShort.HasRates) {

            var startDate = WaveShort.Rates.LastBC().StartDate;
            ratesCount = ratesReversed.TakeWhile(r => r.StartDate >= startDate).Count();
          }
        }
      }
      //var firstTen = upDownRatios.OrderBy(kv => kv.Value.ud.Abs()).Take(10).OrderByDescending(kv => kv.Value.angle.Abs()).ToArray();
      //if (!WaveShort.HasRates || !firstTen.Any(kv => WaveShort.Rates.Count.Between(kv.Key - ratesStep(kv.Key), kv.Key + ratesStep(kv.Key))))
      //  ratesCount = firstTen.First().Key;
      //else 
      //  ratesCount = WaveShort.Rates.Count + 1;
      WaveShort.Rates = null;
      if (CorridorStartDate.HasValue)
        WaveShort.Rates = ratesReversed.TakeWhile(r => r.StartDate >= CorridorStartDate.Value).ToArray();
      else {
        WaveShort.Rates = RatesArray.ReverseIfNot().Take(ratesCount).ToArray();
      }
      var startMax = CorridorStopDate.IfMin(DateTime.MaxValue);
      return WaveShort.Rates.SkipWhile(r => r.StartDate > startMax).ToArray()
        .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private static int NestStep(int rc) {
      return (rc / 100.0).ToInt() + 1;
    }

    public double CorridorBalance() {
      var rates = CorridorStats.Rates;
      Action<Rate,double> a = (r,d)=>r.PriceAvg1 = d;
      rates.SetRegressionPrice1(1, _priceAvg, a);
      var upCount = rates.Count(r => r.PriceAvg > r.PriceAvg1);
      var downCount = rates.Count(r => r.PriceAvg < r.PriceAvg1);
      return Fibonacci.FibRatioSign(upCount , downCount);
    }

    private CorridorStatistics ScanCorridorByStDevAndAngle(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = _priceAvg ?? CorridorPrice();
      var ratesReversed = ratesForCorridor.ReverseIfNot().Select(cp).ToArray();
      var rateLastIndex = 1;
      ratesReversed.TakeWhile(r => ratesReversed.Take(++rateLastIndex).ToArray().Height() < StDevByPriceAvg+StDevByHeight).Count();
      for (; rateLastIndex < ratesReversed.Length - 1; rateLastIndex += (rateLastIndex / 100) + 1) {
        var coeffs = ratesReversed.Take(rateLastIndex).ToArray().Regress(1);
        if (coeffs[1].Angle(BarPeriodInt, PointSize).Abs() < 1)
          break;
      }
        WaveShort.Rates = null;
        WaveShort.Rates = ratesForCorridor.ReverseIfNot().Take(rateLastIndex).ToArray();
        return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
    #region CrossesCount
    private double _CrossesDensity;
    private DateTime _CorridorStartDateByScan;
    public double CrossesDensity {
      get { return _CrossesDensity; }
      set {
        if (_CrossesDensity != value) {
          _CrossesDensity = value;
          OnPropertyChanged("CrossesDensity");
        }
      }
    }

    #endregion

    public double HarmonicsAverage { get; set; }
  }
}
