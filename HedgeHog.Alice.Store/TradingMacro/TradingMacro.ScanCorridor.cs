using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog.Bars;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reflection;
using ReactiveUI;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    #region ScanCorridor Extentions
    #region New
    private IEnumerable<T> VoltsByTimeframe<T>(int frameLength, TimeSpan timeMax, TimeSpan timeMin, List<Rate> ratesAll2, Func<double, DateTime, T> selector) {
      return ratesAll2
        .Select((rate, i) => new { rate, i, isIn = rate.StartDate.TimeOfDay.Between(timeMin, timeMax) })
        .DistinctUntilChanged(a => a.isIn)
        .SkipWhile(a => !a.isIn)
        .Buffer(2)
        .Where(b => b.Count == 2)
        .Select(b => ratesAll2.GetRange(b[0].i, b[1].i - b[0].i))
        .Where(chunk => chunk.Count > frameLength * 0.9)
        .Select(chunk => selector(chunk.Sum(GetVoltage), chunk[0].StartDate));
    }
    private double[] VoltsFromInternalRates(int frameLength, int offset) {
      var ratesAll = UseRatesInternal(ri =>
        ri.CopyLast(offset + 1440 * (DistanceDaysBack + 2) + frameLength * 2).Reverse<Rate>().Skip(offset).ToArray().FillDistance().ToList());
      var timeMax = ratesAll[0].StartDate.TimeOfDay;
      var timeMin = ratesAll[0].StartDate.AddMinutes(-BarPeriodInt * frameLength).TimeOfDay;
      var dateMax = ratesAll.Select(r => r.StartDate).SkipWhile(r => (ratesAll[0].StartDate - r).TotalDays < 1)
        .SkipWhile(date => !date.TimeOfDay.Between(timeMin, timeMax))
        .Select(date => RangeEdgeRight(date, timeMin, timeMax))
        .First();
      var chunks = VoltsByTimeframe(frameLength, timeMax, timeMin, ratesAll, (dist, date) => new { dist, date }).Take(DistanceDaysBack + 1).ToArray();
      return chunks.Select(a => a.dist).Skip(1).ToArray();
    }

    private void SetVoltsByStDevDblIntegral3(IList<Rate> ratesReversed, int frameLength) {
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => chunk[0].VoltageLocal.IsNaN())
        .ForEach(chunk => chunk[0].VoltageLocal = InPips(chunk.StDev(_priceAvg)));
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => chunk[0].VoltageLocal2.IsNaN() && chunk.Last().VoltageLocal.IsNotNaN())
        .ForEach(chunk => chunk[0].VoltageLocal2 = chunk.Average(r => r.VoltageLocal));
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => GetVoltage(chunk[0]).IsNaN() && chunk.Last().VoltageLocal2.IsNotNaN())
        .ForEach(chunk => SetVoltage(chunk[0], chunk.StDev(r => r.VoltageLocal2)));
      ratesReversed
        .Buffer(VoltsBelowAboveLengthMin.ToInt(), 1)
        .TakeWhile(chunk => GetVoltage2(chunk[0]).IsNaN() && GetVoltage(chunk.Last()).IsNotNaN())
        .ForEach(chunk => {
          var volts = chunk.ToArray(GetVoltage);
          var regRes = volts.Regression(1, (coeffs, line) => new { angle = -AngleFromTangent(coeffs.LineSlope(), () => CalcTicksPerSecond(CorridorStats.Rates)), line });
          var corr = AlgLib.correlation.pearsoncorrelation(volts, regRes.line);
          chunk[0].VoltageLocal0 = new[] { regRes.angle, corr };
          SetVoltage2(chunk[0], regRes.angle.Abs());
        });
    }
    private void SetVoltsByStDevDblIntegral4(IList<Rate> ratesReversed, int frameLength, Func<Rate, double> getPrice) {
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => chunk[0].VoltageLocal.IsNaN())
        .ForEach(chunk => chunk[0].VoltageLocal = InPips(chunk.StDev(getPrice)));
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => chunk[0].VoltageLocal2.IsNaN() && chunk.Last().VoltageLocal.IsNotNaN())
        .ForEach(chunk => chunk[0].VoltageLocal2 = chunk.Average(r => r.VoltageLocal));
      Func<Rate, double> gv3 = r => r.VoltageLocal3;
      Action<Rate, double> sv3 = (r, v) => r.VoltageLocal3 = v;
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => gv3(chunk[0]).IsNaN() && chunk.Last().VoltageLocal2.IsNotNaN())
        .ForEach(chunk => sv3(chunk[0], chunk.StDev(r => r.VoltageLocal2)));
      ratesReversed
        .Buffer(VoltsBelowAboveLengthMin.ToInt(), 1)
        .TakeWhile(chunk => GetVoltage2(chunk[0]).IsNaN() && gv3(chunk.Last()).IsNotNaN())
        .ForEach(chunk => {
          var volts = chunk.ToArray(gv3);
          var regRes = volts.Regression(1, (coeffs, line) => new { angle = -AngleFromTangent(coeffs.LineSlope(), () => CalcTicksPerSecond(CorridorStats.Rates)), line });
          chunk[0].VoltageLocal0 = new[] { regRes.angle };
          SetVoltage2(chunk[0], regRes.angle);
        });
      OnGeneralPurpose(() =>
      ratesReversed
        .Buffer(BarsCountCalc, 1)
        .TakeWhile(chunk => GetVoltage(chunk[0]).IsNaN() && GetVoltage2(chunk.Last()).IsNotNaN())
        .ForEach(chunk => {
          double[] waves = chunk.WavesByAngle(r => r.VoltageLocal0[0], frameLength).ToArray();
          var c = waves.AverageByIterations(-2).Average();
          int wavesCount = BarsCountCalc / frameLength / 2;
          SetVoltage(chunk[0], waves.Where(v => v >= c).Take(wavesCount).Average() / frameLength - 1);
        }), true);
    }

    #region ScanCorridorByStDevTripleIntegral
    private void SetVoltsByStDevTripleIntegral(IList<Rate> ratesReversed, int frameLength) {
      Func<double, double, bool> takeWhile = (d1, d2) => d1.IsNaN() && d2.IsNotNaN();
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => chunk[0].VoltageLocal.IsNaN())
        .ForEach(chunk => chunk[0].VoltageLocal = InPips(chunk.StDev(_priceAvg)));
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => chunk[0].VoltageLocal2.IsNaN() && chunk.Last().VoltageLocal.IsNotNaN())
        .ForEach(chunk => chunk[0].VoltageLocal2 = chunk.Average(r => r.VoltageLocal));
      ratesReversed
        .Buffer(frameLength, 1)
        .TakeWhile(chunk => chunk[0].VoltageLocal3.IsNaN() && chunk.Last().VoltageLocal2.IsNotNaN())
        .ForEach(chunk => chunk[0].VoltageLocal3 = chunk.StDev(r => r.VoltageLocal2));
      ratesReversed
        .Buffer(VoltsBelowAboveLengthMin.ToInt(), 1)
        .TakeWhile(chunk => takeWhile(GetVoltage(chunk[0]), chunk.Last().VoltageLocal3))
        .ForEach(chunk => {
          var volts = chunk.ToArray(r => r.VoltageLocal3);
          var regRes = volts.Regression(1, (coeffs, line) => new { angle = AngleFromTangent(coeffs.LineSlope().Abs(), () => CalcTicksPerSecond(CorridorStats.Rates)), line });
          var corr = AlgLib.correlation.pearsoncorrelation(volts, regRes.line);
          chunk[0].VoltageLocal0 = new[] { regRes.angle, corr };
          SetVoltage(chunk[0], regRes.angle);
          SetVoltage2(chunk[0], corr);
        });
    }
    #endregion
    #region ScanCorridorByDistance
    private CorridorStatistics ScanCorridorByDistanceMax(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesAll = UseRatesInternal(ri => ri.CopyLast(1440 * 2 + CorridorDistance * 2).Reverse<Rate>().ToArray().FillDistance().ToList());
      var dateMax = ratesAll[0].StartDate.AddDays(-1);
      var timeMax = dateMax.TimeOfDay;
      var timeMin = dateMax.AddMinutes(-BarPeriodInt * CorridorDistance).TimeOfDay;
      var ratesAll2 = ratesAll.SkipWhile(rate => rate.StartDate > dateMax).ToList();
      var chunks = ratesAll2
        .Select((rate, i) => new { rate, i, isIn = rate.StartDate.TimeOfDay.Between(timeMin, timeMax) })
        .DistinctUntilChanged(a => a.isIn)
        .SkipWhile(a => !a.isIn)
        .Buffer(2)
        .Where(b => b.Count == 2)
        .Select(b => ratesAll2.GetRange(b[0].i, b[1].i - b[0].i))
        .Where(chunk => chunk.Count > CorridorDistance * 0.9)
        .Select(chunk => chunk.Distance())
        .ToArray();
      var distanceMax = chunks.DefaultIfEmpty().Max();

      Func<IList<Rate>, int> scan = rates => distanceMax == 0
        ? CorridorDistance
        : rates.TakeWhile(r => r.Distance <= distanceMax).Count();
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorByDistance51(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      if (UseRatesInternal(ri => ri.CopyLast(10)).First().Distance.IsNaN()) {
        Log = new Exception("Loading distance volts ...");
        Enumerable.Range(1, BarsCountCalc).ForEach(offset =>
          DistancesFromInternalRates(offset, (rates, volts) => SetVoltage(rates[0], volts)));
      };
      Action<IList<Rate>, double> setVoltsLocal = (rates, volts) => rates
        .TakeWhile(r => GetVoltage(r).IsNaN())
        .ForEach(r => {
          SetVoltage(r, volts);
          var date = r.StartDate.AddDays(-1).TimeOfDay;
          var bar = BarPeriodInt.FromMinutes();
          rates.Skip(1400).SkipWhile(rp => rp.StartDate.TimeOfDay > date)
            .SkipWhile(rp => GetVoltage(rp).IfNaN(0) == 0).Take(1)
            .ForEach(rp => SetVoltage2(r, GetVoltage(rp)));
        });
      var distances = DistancesFromInternalRates(0, setVoltsLocal).DefaultIfEmpty().Memoize();
      var distanceMin = distances.Min();
      var distanceMax = new Lazy<double>(() => distances.Max());
      Func<IList<Rate>, int, int> getCount = (rates, count) => count > CorridorDistance ? count : rates.TakeWhile(r => r.Distance <= distanceMax.Value).Count();
      Func<IList<Rate>, int> scan = rates => distanceMin == 0
        ? CorridorDistance
        : rates.TakeWhile(r => r.Distance <= distanceMin).Count();
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan);
    }
    #endregion

    DateTime RangeEdgeRight(DateTime date, TimeSpan from, TimeSpan to) {
      if (to > from) {
        return date.Date + to;
      }
      var d = date.Date;
      if (date.Between(d.Add(from), d.AddDays(1))) return d.AddDays(1).Add(to);
      return d.Add(to);
    }
    private double[] DistancesFromInternalRates(int offset, Action<IList<Rate>, double> setVolts) {
      var ratesAll = UseRatesInternal(ri =>
        ri.CopyLast(offset + 1440 * (DistanceDaysBack + 2) + CorridorDistance * 2).Reverse<Rate>().Skip(offset).ToArray().FillDistance().ToList());
      var timeMax = ratesAll[0].StartDate.TimeOfDay;
      var timeMin = ratesAll[0].StartDate.AddMinutes(-BarPeriodInt * CorridorDistance).TimeOfDay;
      var dateMax = ratesAll.Select(r => r.StartDate).SkipWhile(r => (ratesAll[0].StartDate - r).TotalDays < 1)
        .SkipWhile(date => !date.TimeOfDay.Between(timeMin, timeMax))
        .Select(date => RangeEdgeRight(date, timeMin, timeMax))
        .First();
      //var ratesAll2 = ratesAll.SkipWhile(rate => rate.StartDate > dateMax).ToList();
      var chunks = DistancesByTimeframe(timeMax, timeMin, ratesAll, (dist, date) => new { dist, date }).Take(DistanceDaysBack + 1).ToArray();
      //if (chunks.Length != DistanceDaysBack + 1) Log = new Exception("chunks.Length!=DistanceDaysBack+1");
      if (setVolts != null) {
        var volts = InPips(chunks.Select(a => a.dist).Average() / CorridorDistance);
        setVolts(ratesAll, volts);
        var voltsAll = ratesAll.Select(GetVoltage).Take(BarsCountCalc).ToArray();
        Task.Factory.StartNew(() => {
          var vh = voltsAll.AverageByIterations(VoltsHighIterations).DefaultIfEmpty().Average();
          GetVoltageHigh = () => vh;
          var va = voltsAll.AverageByIterations(VoltsAvgIterations).DefaultIfEmpty().Average();
          GetVoltageAverage = () => va;

        });
      }
      //SetVoltage2(ratesAll[0], GetVoltageHigh() / GetVoltageAverage() - 1);
      if (!IsInVitualTrading)
        GlobalStorage.Instance.ResetGenericList(chunks.Select(ch => new { Distance = InPips(ch.dist / CorridorDistance).Round(2), Date = ch.date }));
      return chunks.Select(a => a.dist).Skip(1).ToArray();
    }
    #region VoltsAvgIterations
    private int _VoltsAvgIterations;
    [Category(categoryCorridor)]
    public int VoltsAvgIterations {
      get { return _VoltsAvgIterations; }
      set {
        if (_VoltsAvgIterations != value) {
          _VoltsAvgIterations = value;
          OnPropertyChanged("VoltsAvgIterations");
        }
      }
    }

    #endregion
    public int GetWorkingDays(DateTime from, DateTime to) {
      var dayDifference = (int)to.Subtract(from).TotalDays;
      return Enumerable
          .Range(1, dayDifference)
          .Select(x => from.AddDays(x))
          .Count(x => x.DayOfWeek != DayOfWeek.Saturday && x.DayOfWeek != DayOfWeek.Sunday);
    }
    private CorridorStatistics ScanCorridorByDistance5(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesAll = UseRatesInternal(ri => ri.CopyLast(1440 * DistanceDaysBack + CorridorDistance * 2).Reverse<Rate>().ToArray().FillDistance().ToList());
      var dateMax = ratesAll[0].StartDate.AddDays(-1);
      var timeMax = dateMax.TimeOfDay;
      var timeMin = dateMax.AddMinutes(-BarPeriodInt * CorridorDistance).TimeOfDay;
      var ratesAll2 = ratesAll.SkipWhile(rate => rate.StartDate > dateMax).ToList();
      var chunks = DistancesByTimeframe(timeMax, timeMin, ratesAll2, (dist, date) => new { dist, date }).Select(a => a.dist).ToArray();
      var distanceMin = chunks.DefaultIfEmpty().Min();
      var volts = InPips(chunks.Average() / CorridorDistance);
      ratesAll.TakeWhile(r => GetVoltage(r).IsNaN()).ForEach(r => SetVoltage(r, volts));

      Func<IList<Rate>, int> scan = rates => distanceMin == 0
        ? CorridorDistance
        : rates.TakeWhile(r => r.Distance <= distanceMin).Count();
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan);
    }

    private IEnumerable<T> DistancesByTimeframe<T>(TimeSpan timeMax, TimeSpan timeMin, List<Rate> ratesAll2, Func<double, DateTime, T> selector) {
      return ratesAll2
        .Select((rate, i) => new { rate, i, isIn = rate.StartDate.TimeOfDay.Between(timeMin, timeMax) })
        .DistinctUntilChanged(a => a.isIn)
        .SkipWhile(a => !a.isIn)
        .Buffer(2)
        .Where(b => b.Count == 2)
        .Select(b => ratesAll2.GetRange(b[0].i, b[1].i - b[0].i))
        .Where(chunk => chunk.Count > CorridorDistance * 0.9)
        .Select(chunk => selector(chunk.Distance(), chunk[0].StartDate));
    }
    private CorridorStatistics ScanCorridorByDistance3(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesAll = UseRatesInternal(ri => ri.CopyLast(1440 * 2 + CorridorDistance * 2).Reverse<Rate>().ToArray().FillDistance().ToArray());
      var ratesPrev = from r0 in ratesAll.Take(CorridorDistance + 1)
                      join r1 in ratesAll.Skip(CorridorDistance + 1) on r0.StartDate.TimeOfDay equals r1.StartDate.TimeOfDay into rg
                      select rg;
      var list2 = ratesPrev.Aggregate(new List<List<Rate>>(),
        (list, grp) => {
          grp.ForEach((r, i) => {
            if (list.Count < i + 1) list.Add(new List<Rate>());
            list[i].Add(r);
          });
          return list;
        }
        );
      var list3 = list2.Select(list =>
          (from da in new DateTime((long)list.Select(r => r.StartDate.Ticks / 1000).Average() * 1000).Yield()
           from rate in list
           where (rate.StartDate - da).Duration().TotalDays < .5
           select rate).ToList()
      );
      var distances = list3
        .Where(l => l.Count > CorridorDistance * 0.9)
        .Select(l => new { distance = l.Distance(), rate = l[0], count = l.Count });
      var distanceMin = distances.Select(d => d.distance).DefaultIfEmpty().Min();
      //ratesPrev.Clump(1440)
      //.Select(c => c.ToArray())
      //.Where(c => c.Length == 1440)
      //.Select(c => c.Take(CorridorDistance).ToArray().Distance().Abs())
      //.Average();

      Func<IList<Rate>, int> scan = rates => distanceMin == 0
        ? CorridorDistance
        : rates.TakeWhile(r => r.Distance <= distanceMin).Count();
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorByDistance2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesAll = UseRatesInternal(ri => ri.CopyLast(1440 * DistanceDaysBack + CorridorDistance * 2).Reverse<Rate>().ToArray().FillDistance().ToArray());
      var ratesPrev = from r0 in ratesAll.Take(CorridorDistance + 1)
                      join r1 in ratesAll.Skip(CorridorDistance + 1) on r0.StartDate.TimeOfDay equals r1.StartDate.TimeOfDay into rg
                      select rg;
      var list2 = ratesPrev.Aggregate(new List<List<Rate>>(),
        (list, grp) => {
          grp.ForEach((r, i) => {
            if (list.Count < i + 1) list.Add(new List<Rate>());
            list[i].Add(r);
          });
          return list;
        }
        );
      var list3 = list2.Select(list =>
          (from da in new DateTime((long)list.Select(r => r.StartDate.Ticks / 1000).Average() * 1000).Yield()
           from rate in list
           where (rate.StartDate - da).Duration().TotalDays < .5
           select rate).ToList()
      );
      var distances = list3
        .Where(l => l.Count > CorridorDistance * 0.9)
        .Select(l => new { distance = l.Distance(), rate = l[0], count = l.Count });
      var distanceMin = distances.Select(d => d.distance).DefaultIfEmpty().Average();
      //ratesPrev.Clump(1440)
      //.Select(c => c.ToArray())
      //.Where(c => c.Length == 1440)
      //.Select(c => c.Take(CorridorDistance).ToArray().Distance().Abs())
      //.Average();

      Func<IList<Rate>, int> scan = rates => distanceMin == 0
        ? CorridorDistance
        : rates.TakeWhile(r => r.Distance <= distanceMin).Count();
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorByDistance(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var revsCount = RatesArray.Count + CorridorDistance * 2;
      var ratesReversed = RatesArray.ReverseIfNot().FillDistance().SafeArray();
      var distanceMin = RatesArray[0].Distance / (RatesArray.Count / CorridorDistance);
      Func<IList<Rate>, int> scan = rates => ratesReversed.TakeWhile(r => r.Distance <= distanceMin).Count();
      return ScanCorridorLazy(ratesReversed, scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorByFrameAngle(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var rateStart = RatesArray
          .SkipWhile(r => GetVoltage(r) < GetVoltageHigh())
          .TakeWhile(r => GetVoltage(r) > GetVoltageHigh())
          //.OrderByDescending(r => GetVoltage(r))
          .LastOrDefault();
        var rateStop = rates
          .SkipWhile(r => GetVoltage(r).IfNaN(double.MaxValue) > GetVoltageAverage())
          .TakeWhile(r => GetVoltage(r) < GetVoltageAverage())
          //.OrderBy(r => GetVoltage(r))
          .LastOrDefault();
        if (rateStart == null || rateStop == null) {
          SetCorridorStopDate(null);
          return rates.Count - 1;
        }
        SetCorridorStopDate(rateStop > rateStart ? rateStop : null);
        return (rates.TakeWhile(r => r >= rateStart).Count() - 1).Max(CorridorDistance);
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySpike30(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.Select(_priceAvg).ToArray();
        var spikeOut = new { length = 0, distance = 0.0 };
        var spikeProjector = spikeOut.ToFunc(0, 0.0, (length, distance) => new { length, distance });
        var spike = new[] { spikeOut }.AsParallel().ToFunc((IEnumerable<int>)null
          , (op) => Spikes31(prices, Partitioner.Create(op.ToArray(), true), spikeProjector));
        var lengths1 = spike(Lib.IteratonSequence(10, rates.Count))
          .OrderByDescending(t => t.distance)
          .Select(t => t.length)
          .Take(rates.Count / 20)
          .ToArray()
          .GroupByLambda((l1, l2) => l1.Abs(l2) <= Lib.IteratonSequenceNextStep(l1).Max(Lib.IteratonSequenceNextStep(l2)))
          .Take(3)
          .Select(g => g.Select(g1 => g1))
          .SelectMany(l => l)
          .SelectMany(length => {
            var ns = (Lib.IteratonSequenceNextStep(length) * 1.1).ToInt();
            return Enumerable.Range(length - ns, ns * 2);
          })
          .Distinct()
          .Where(l => l <= rates.Count)
          .ToArray();
        return spike(lengths1.DefaultIfEmpty(rates.Count))
          .OrderByDescending(t => t.distance)
          .First().length;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySpike231(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.Select(_priceAvg).ToArray();
        var lengths = Partitioner.Create(Lib.IteratonSequence(10, rates.Count).ToArray(), true);
        var lengths1 = Spikes23(prices, lengths)
          .OrderByDescending(t => t.Item2)
          .Select(t => t.Item1)
          .Take(rates.Count / 20)
          .ToArray()
          .GroupByLambda((l1, l2) => l1.Abs(l2) <= Lib.IteratonSequenceNextStep(l1).Max(Lib.IteratonSequenceNextStep(l2)))
          .Take(3)
          .Select(g => g.Select(g1 => g1))
          .SelectMany(l => l)
          .SelectMany(length => {
            var ns = (Lib.IteratonSequenceNextStep(length) * 1.1).ToInt();
            return Enumerable.Range(length - ns, ns * 2);
          })
          .Distinct()
          .Where(l => l <= rates.Count)
          .ToArray();
        return Spikes23(prices, Partitioner.Create(lengths1.DefaultIfEmpty(rates.Count).ToArray(), true))
          .OrderByDescending(t => t.Item2)
          .First().Item1;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySpike23(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.Select(_priceAvg).ToArray();
        var lengths = Partitioner.Create(Lib.IteratonSequence(10, rates.Count).ToArray(), true);
        var lengths1 = Spikes23(prices, lengths)
          .OrderByDescending(t => t.Item2)
          .Select(t => t.Item1)
          .Take(rates.Count / 20)
          .ToArray()
          .GroupByLambda((l1, l2) => l1.Abs(l2) <= Lib.IteratonSequenceNextStep(l1).Max(Lib.IteratonSequenceNextStep(l2)))
          .Take(3)
          .Select(g => g.Select(g1 => g1))
          .SelectMany(l => l)
          .SelectMany(length => {
            var ns = (Lib.IteratonSequenceNextStep(length) * 1.1).ToInt();
            return Enumerable.Range(length - ns, ns * 2);
          })
          .Distinct()
          .Where(l => l <= rates.Count)
          .ToArray();
        return Spikes(rates, Partitioner.Create(lengths1.DefaultIfEmpty(rates.Count).ToArray(), true), _priceAvg, _priceAvg)
          .OrderByDescending(t => t.Item2)
          .First().Item1;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySpike22(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates;
        var lengths = Partitioner.Create(Lib.IteratonSequence(10, rates.Count).ToArray(), true);
        var lengths1 = Spikes22(prices, lengths, _priceAvg, _priceAvg)
          .OrderByDescending(t => t.Item2)
          .Select(t => t.Item1)
          .Take(rates.Count / 20)
          .ToArray()
          .GroupByLambda((l1, l2) => l1.Abs(l2) <= Lib.IteratonSequenceNextStep(l1).Max(Lib.IteratonSequenceNextStep(l2)))
          .Take(3)
          .Select(g => g.Select(g1 => g1))
          .SelectMany(l => l)
          .SelectMany(length => {
            var ns = (Lib.IteratonSequenceNextStep(length) * 1.1).ToInt();
            return Enumerable.Range(length - ns, ns * 2);
          })
          .Distinct()
          .Where(l => l <= rates.Count)
          .ToArray();
        return Spikes(rates, Partitioner.Create(lengths1.DefaultIfEmpty(rates.Count).ToArray(), true), _priceAvg, _priceAvg)
          .OrderByDescending(t => t.Item2)
          .First().Item1;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySpike21(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates;
        var lengths = Partitioner.Create(Lib.IteratonSequence(10, rates.Count).ToArray(), true);
        var lengths1 = Spikes(prices, lengths, _priceAvg, _priceAvg)
          .OrderByDescending(t => t.Item2)
          .Select(t => t.Item1)
          .Take(rates.Count / 20)
          .ToArray()
          .GroupByLambda((l1, l2) => l1.Abs(l2) <= Lib.IteratonSequenceNextStep(l1).Max(Lib.IteratonSequenceNextStep(l2)))
          .Take(3)
          .Select(g => g.Select(g1 => g1))
          .SelectMany(l => l)
          .SelectMany(length => {
            var ns = (Lib.IteratonSequenceNextStep(length) * 1.1).ToInt();
            return Enumerable.Range(length - ns, ns * 2);
          })
          .Distinct()
          .Where(l => l <= rates.Count)
          .ToArray();
        return Spikes(rates, Partitioner.Create(lengths1.DefaultIfEmpty(rates.Count).ToArray(), true), _priceAvg, _priceAvg)
          .OrderByDescending(t => t.Item2)
          .First().Item1;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySpike2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.ToArray(r => r.BidHigh);
        var lengths = Partitioner.Create(Enumerable.Range(10, rates.Count - 10).ToArray(), true);
        //Lib.IteratonSequence(10, rates.Count)
        var counter = (
          from length in lengths.AsParallel()
          let rates2 = prices.CopyToArray(length)
          let coeffs = rates2.Regress(1)
          let leftPoint = coeffs.RegressionValue(length - 1)
          let rightPoint = coeffs.RegressionValue(0)
          let distance = new[] { 
            rates2.Last() - leftPoint,
            leftPoint - rates2.Last(),
            rates2[0] - rightPoint,
            rightPoint - rates2[0] 
          }
          .Select(v => v.Abs())
          .OrderByDescending(v => v)
          .Take(2)
          .Sum()
          orderby distance descending
          select length
        )
        .First();
        return counter;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private static ParallelQuery<T> Spikes31<T>(IList<double> prices, OrderablePartitioner<int> lengths, Func<int, double, T> projector) {
      return (
        from length in lengths.AsParallel()
        where length > 60
        let rates2 = prices.CopyToArray(length)
        let regRes = rates2.Regression(1, (coeffs, line) => new { coeffs, line })
        let heights = GetStDevUpDown(rates2, regRes.line, (up, down) => new[] { up, down })//.HeightByRegressoin()
        let distance = heights.Max() / heights.Min()
        select projector(length, distance)
      );
    }
    private static ParallelQuery<T> Spikes30<T>(IList<double> prices, OrderablePartitioner<int> lengths, Func<int, double, T> projector) {
      return (
        from length in lengths.AsParallel()
        let rates2 = prices.CopyToArray(length)
        let regRes = rates2.Regression(1, (coeffs, line) => new { coeffs, line })
        let heights = GetStDevUpDown(rates2, regRes.line, (up, down) => new[] { up, down })//.HeightByRegressoin()
        let distance = heights.Sum() / (heights.Max() / heights.Min())
        select projector(length, distance)
      );
    }
    private static ParallelQuery<T> Spikes24<T>(List<double> prices, OrderablePartitioner<int> lengths, Func<double, double, double> calcHeight, Func<int, double, T> projector) {
      Func<IEnumerable<double>, IEnumerable<double>, double> priceHeight = (price, line) => price.Zip(line, calcHeight).MaxBy(d => d.Abs()).First();
      Func<double[], double[], int, double> priceHeightLast = (price, line, chunk) => priceHeight(price.CopyLast(chunk), line.CopyLast(chunk));
      Func<IList<double>, IList<double>, int, double> priceHeightFirst = (price, line, chunk) => priceHeight(price.Take(chunk), line.Take(chunk));
      Func<double[], double[], int, double[]> priceHeights = (price, line, chunk)
        => new[] { priceHeightLast(price, line, chunk), priceHeightFirst(price, line, chunk) };
      return (
        from length in lengths.AsParallel()
        let rates2 = prices.CopyToArray(length)
        let regRes = rates2.Regression(1, (coeffs, line) => new { coeffs, line })
        let heights = priceHeights(rates2, regRes.line, 1)
        let distance = heights.Sum().Abs() / (heights.Max(d => d.Abs()) / heights.Min(d => d.Abs()))
        select projector(length, distance)
      );
    }
    private static ParallelQuery<Tuple<int, double>> Spikes23(IList<double> prices, OrderablePartitioner<int> lengths) {
      return (
        from length in lengths.AsParallel()
        let rates2 = prices.CopyToArray(length)
        let regRes = rates2.Regression(1, (coeffs, line) => new { coeffs, line })
        let stDev = GetStDevUpDown(rates2, regRes.line, (up, down) => new { up, down })//.HeightByRegressoin()
        from left in Enumerable.Range(length, 1).TakeWhile(l => l < prices.Count)
        .Select(l => new { point = regRes.coeffs.RegressionValue(l), price = prices[l], length = l })
        let leftPoint = left.point
        let leftPointUp = leftPoint + stDev.up * 2
        let leftPointDown = leftPoint - stDev.down * 2
        let leftPrice = left.price
        let distances = new[] { 
            leftPrice - leftPoint,
            leftPoint - leftPrice
        }
        let distance = distances.Max()
        select Tuple.Create(left.length, distance/*/stDev*/)
      );
    }
    private static ParallelQuery<Tuple<int, double>> Spikes22<T>(IList<T> prices, OrderablePartitioner<int> lengths, Func<T, double> priceUp, Func<T, double> priceDown) {
      return (
        from length in lengths.AsParallel()
        let rates2 = prices.CopyToArray(length)
        let coeffs = rates2.Regress(1, v => priceDown(v).Avg(priceUp(v)))
        let stDev = rates2.Select(v => priceDown(v).Avg(priceUp(v))).ToArray().StDev()//.HeightByRegressoin()
        let leftPoint = coeffs.RegressionValue(length)
        let leftPointUp = leftPoint + stDev * 2
        let leftPointDown = leftPoint - stDev * 2
        let leftPrice = prices[length]
        let distances = new[] { 
            priceUp(leftPrice) - leftPoint,
            leftPoint - priceDown(leftPrice)
        }
        let distance = distances.Max()
        select Tuple.Create(length, distance/*/stDev*/)
      );
    }
    private static ParallelQuery<Tuple<int, double>> Spikes<T>(IList<T> prices, OrderablePartitioner<int> lengths, Func<T, double> priceUp, Func<T, double> priceDown) {
      return (
        from length in lengths.AsParallel()
        let rates2 = prices.CopyToArray(length)
        let coeffs = rates2.Regress(1, v => priceDown(v).Avg(priceUp(v)))
        //let stDev = rates2.Select(v => priceDown(v).Avg(priceUp(v))).ToArray().HeightByRegressoin()
        let leftPoint = coeffs.RegressionValue(length - 1)
        //let rightPoint = coeffs.RegressionValue(0)
        let distances = new[] { 
            priceUp(rates2.Last()) - leftPoint,
            leftPoint - priceDown(rates2.Last())
            //,
            //priceUp(rates2[0]) - rightPoint,
            //rightPoint - priceDown(rates2[0])
        }
        let distance = distances
        .Select(v => v.Abs())
        .Buffer(2)
        .Select(b => b.Max())
          //.Where(v => v > stDev)
        .Sum()
        select Tuple.Create(length, distance/*/stDev*/)
      );
    }

    private CorridorStatistics ScanCorridorBySpike(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.ToArray(_priceAvg);
        var lengths = Partitioner.Create(Enumerable.Range(10, rates.Count - 10).ToArray(), true);
        //Lib.IteratonSequence(10, rates.Count)
        var counter = (
          from length in lengths.AsParallel()
          let rates2 = prices.CopyToArray(length)
          let coeffs = rates2.Regress(1)
          let leftPoint = coeffs.RegressionValue(length - 1)
          let rightPoint = coeffs.RegressionValue(0)
          let distance = rates2.Last().Abs(leftPoint).Avg(rates2[0].Abs(rightPoint))
          orderby distance descending
          select length
        )
        .First();
        return counter;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }

    private CorridorStatistics ScanCorridorByBigGap(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        return PriceRangeGaps(rates.Take(CorridorDistance).ToArray())
          .First()
          .Take(1)
          .Select(gap => new { stop = gap.First(), start = gap.Last() })
          .Do(gap => SetCorridorStopDate(gap.stop))
          .Select(gap => rates.TakeWhile(rate => rate >= gap.start).Count())
          .First();
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorByBigGap2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var gapsAll = PriceRangeGaps(rates.Take(CorridorDistance).ToArray()).ToArray();
        Func<IEnumerable<IGrouping<int, Rate>>, bool> whereAll = gaps => true;
        Func<IEnumerable<IGrouping<int, Rate>>, bool> whereByCurrent = gaps => gaps.SelectMany(gap => gap.Select(_priceAvg)).Yield()
            .Select(rts => rts.Memoize(3))
            .Where(rts => rts.Count() > CorrelationMinimum)
            .Select(rts => new { min = rts.Min(), max = rts.Max() })
            .Any(rts => CurrentPrice.Average.Between(rts.min, rts.max));
        Func<Func<IEnumerable<IGrouping<int, Rate>>, bool>, IEnumerable<int>> getGaps = where => {
          return gapsAll
            .Where(where)
            .Take(1)
            .SelectMany(gaps => gaps
              .Take(1)
              .Select(gap => new { stop = gap.First(), start = gap.Last() })
              .Do(gap => SetCorridorStopDate(gap.stop))
              .Select(gap => rates.TakeWhile(rate => rate >= gap.start).Count())
              );
        };
        return getGaps(whereByCurrent).IfEmpty(() => getGaps(whereAll)).First();
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private IEnumerable<IEnumerable<IGrouping<int, Rate>>> PriceRangeGaps(IList<Rate> rates) {
      return rates.PriceRangeGaps(InPoints(1), _priceAvg, InPips);
    }
    Queue<double> _swQueue = new Queue<double>();
    private CorridorStatistics ScanCorridorTillFlat21(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.ToArray(_priceAvg);
        Func<int> getPrevCount = () => CorridorStats.Rates.Last().StartDate.Yield()
          .Select(date => RatesArray.ReverseIfNot().TakeWhile(r => r.StartDate >= date).Count())
          .DefaultIfEmpty(CorridorDistance).First();

        Stopwatch sw = Stopwatch.StartNew();
        var swDict = new Dictionary<string, double>();
        swDict.Add("1", sw.ElapsedMilliseconds);

        var counter =
          Lib.IteratonSequence(10, rates.Count)
          .ToObservable()
          .Select(length => Observable.Start(() => {
            var rates2 = prices.CopyToArray(length);
            var coeffs = rates2.Regress(1);
            var height = rates2.HeightByRegressoin2();
            return new { length, slope = coeffs.LineSlope(), height, heightOk = height.Between(StDevByHeight, StDevByPriceAvg) };
          }
          , ThreadPoolScheduler.Instance))
          .Merge(1)
          .SkipWhile(a => !a.heightOk)
          .TakeWhile(a => a.heightOk)
          .DistinctUntilChanged(d => d.slope.Sign())
          .Take(2).Select(a => a.length)
          .ToEnumerable()
          .IfEmpty(getPrevCount).Last();

        _swQueue.Enqueue(sw.ElapsedMilliseconds);
        Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, _swQueue.Average());

        return counter;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorTillFlat2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.ToArray(_priceAvg);
        Func<int> getPrevCount = () => CorridorStats.Rates.CopyLast(1).Select(r => r.StartDate)
          .Select(date => RatesArray.ReverseIfNot().TakeWhile(r => r.StartDate >= date).Count())
          .DefaultIfEmpty(CorridorDistance).First();
        Stopwatch sw = Stopwatch.StartNew();
        var counter = (
          from length in Lib.IteratonSequence(10, rates.Count)
          let rates2 = prices.CopyToArray(length)
          let coeffs = rates2.Regress(1)
          let height = rates2.HeightByRegressoin(coeffs)
          let height2 = rates2.HeightByRegressoin2(coeffs)
          select new { length, slope = coeffs.LineSlope(), heightMin = height.Min(height2), heightMax = height.Max(height2) }
        )
        .SkipWhile(a => a.heightMax < StDevByHeight)
        .TakeWhile(a => a.heightMin < StDevByHeight)
        .GroupBy(d => d.slope.Sign())
        .Buffer(2)
        .Take(1)
        .Where(b => b.Count > 1)
        .Select(b => {
          if (b[1].First().length < CorridorDistance) return 0;
          SetCorridorStopDate(RatesArray.Last());
          return b[1].First().length;
        })
        .Where(length => length > 0)
        .IfEmpty(getPrevCount).Last();
        if (counter >= RatesArray.Count) {
          SetCorridorStopDate(null);
          counter = RatesArray.Count - 1;
        }
        return counter;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }


    private void LongestGap(IList<Rate> ratesForCorridor) {
      var middle = ratesForCorridor.Average(_priceAvg);
      var result = ratesForCorridor.Gaps(middle, _priceAvg).MaxBy(g => g.Count()).First().Count();
    }

    private CorridorStatistics ScanCorridorFixed(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      Func<Rate, double> distanceFunc = r => GetVoltage(r).Abs();
      var rates = MonoidsCore.ToLazy(() =>
        ratesReversed
        .Zip(ratesReversed.Skip(1), (r1, r2) => new { d = r1.PriceAvg.Abs(r2.PriceAvg), v = GetVoltage(r1) }));
      var distanceMin = from rs in rates
                        select rs
                        .Where(a => a.v.Between(GetVoltageAverage(), GetVoltageHigh()))
                        .Select(a => a.d).DefaultIfEmpty(double.NaN).Sum();
      var distanceSum = 0.0;
      var scan =
         (from r in rates
          select GetVoltage(RatesArray[0]).IsNotNaN() && distanceMin.Value > 0 ? r.Select(a => distanceSum += a.d).TakeWhile(d => d < distanceMin.Value).Count() : CorridorDistance
           );
      return ScanCorridorLazy(ratesReversed, scan, GetShowVoltageFunction());
    }

    private CorridorStatistics ScanCorridorByStDevBalance(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
      var lazyCount = new Lazy<int>(() => CalcCorridorByStDevBalanceAndMaxLength2(ratesReversed.Select(_priceAvg).ToArray(), CorridorDistanceRatio.ToInt()));
      return ScanCorridorLazy(ratesReversed, lazyCount, GetShowVoltageFunction());
    }


    private CorridorStatistics ScanCorridorByStDevBalanceR(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
      var lazyCount = new Lazy<int>(() => CalcCorridorByStDevBalanceAndMaxLength3(ratesReversed.Select(_priceAvg).ToArray(), CorridorDistanceRatio.ToInt()));
      return ScanCorridorLazy(ratesReversed, lazyCount, ShowVoltsByAboveBelow);
    }
    private CorridorStatistics ShowVoltsByAboveBelow() {
      var waveRates = WaveShort.Rates.Select(_priceAvg).ToArray();
      var line = waveRates.Line();
      var zipped = line.Zip(waveRates, (l, r) => r.Sign(l)).GroupBy(s => s).ToArray();
      double upsCount = zipped.First(z => z.Key == 1).Count();
      double downsCount = zipped.First(z => z.Key == -1).Count();
      MagnetPriceRatio = GetVoltage(RatePrev).Cma(WaveShort.Rates.Count / 100.0, upsCount.Percentage(downsCount).Min(.5).Max(-.5));
      SetVoltage(RateLast, RatesHeight / StDevByPriceAvg);
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    Func<MathExtensions.Extream<Rate>, int, bool> _distanceHeightRatioExtreamPredicate = (e, i) => true;
    private CorridorStatistics ScanCorridorByDistanceHeightRatio(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var revsCount = RatesArray.Count + CorridorDistance * 2;
      var ratesReversed = UseRatesInternal(ri => ri.SafeArray().CopyToArray(ri.Count - revsCount, revsCount).Reverse().ToArray().FillDistance().SafeArray());
      var lazyCount = new Lazy<int>(() => CalcCorridorByDistanceHeightRatioMinMax(ratesReversed, CorridorDistance, _distanceHeightRatioExtreamPredicate));
      Action postProcess = () => MagnetPrice = WaveShort.Rates.Average(_priceAvg);
      return ScanCorridorLazy(ratesReversed, lazyCount, ShowVoltsByDistanceAverage, postProcess);
    }

    private CorridorStatistics ShowVoltsByDistanceAverage() {
      return ShowVoltsByDistanceAverage(true);
    }
    private CorridorStatistics ShowVoltsByDistanceAverage(bool show) {
      if (RatesArray.Last().Distance.IsNaN())
        UseRatesInternal(ri => ri.Reverse<Rate>()/*.Take(RatesArray.Count + CorridorDistance * 4)*/.FillDistance());
      Func<IList<Rate>, double> calcVolatility = (rates) => InPips(rates.Distance() / CorridorDistance);
      var revs = UseRatesInternal(ri => ri.Reverse<Rate>());
      if (revs.Skip(1).Take(10).Select(GetVoltage).Any(v => v.IsNaN())) {
        revs.TakeWhile(r => !r.Distance.IsNaN()).Integral(CorridorDistance).ToList()
          .ForEach(rates => SetVoltage(rates[0], calcVolatility(rates)));
      }
      var voltsCurrent = calcVolatility(RatesArray.Reverse<Rate>().Take(CorridorDistance).ToArray());
      if (!show) { SetVoltage(RatesArray.Last(), voltsCurrent); return null; }
      return ShowVolts(voltsCurrent, 3);
    }
    private CorridorStatistics ScanCorridorByDistanceHeightRatioMin(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
      var lazyCount = new Lazy<int>(() => CalcCorridorByDistanceHeightRatio(ratesReversed, CorridorDistance, true));
      return ScanCorridorLazy(ratesReversed, lazyCount, ShowVoltsByDistanceAverage);
    }
    private CorridorStatistics ScanCorridorByDistanceHeightRatioMax(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
      var lazyCount = new Lazy<int>(() => CalcCorridorByDistanceHeightRatio(ratesReversed, CorridorDistance, false));
      return ScanCorridorLazy(ratesReversed, lazyCount, ShowVoltsByDistanceAverage);
    }
    private int CalcCorridorByDistanceHeightRatio(Rate[] ratesReversed, int range, bool useMin) {
      var ps = PointSize;
      ratesReversed = UseRatesInternal(ri => ri.Reverse<Rate>().Take(RatesArray.Count + CorridorDistance).ToArray());
      ratesReversed.FillDistance();
      var datas = ratesReversed.Integral(range)
        .Select(Extensions.Distance)
        .Integral(CorridorDistance)
        .AsParallel()
        .Select((a, i) => new { da = a.Sum(), i }).ToArray();
      var data = (useMin ? datas.OrderBy(a => a.da) : datas.OrderByDescending(a => a.da)).First();
      if (data.i >= RatesArray.Count - CorridorDistance * 1.1) {
        datas = datas.Take(RatesArray.Count - CorridorDistance * 2).ToArray();
        data = (useMin ? datas.OrderBy(a => a.da) : datas.OrderByDescending(a => a.da)).FirstOrDefault() ?? data;
      }
      //.First(a => {
      //  return a.values.Regress(1, v => v.v.PriceAvg).LineSlope().Angle(BarPeriodInt, ps).Abs() < TradingAngleRange;
      //});
      // Scan forward
      var stopIndex = data.i;
      var startIndex = data.i + range - 1;
      if (CorridorStats.Rates.Any()) {
        CorridorStats.StopRate = RatesArray.ReverseIfNot().Skip(stopIndex).First();
        _CorridorStopDate = CorridorStats.StopRate.StartDate;
      }
      return range;
    }
    HedgeHog.MathExtensions.Extream<Rate> extreamHill;
    HedgeHog.MathExtensions.Extream<Rate> extreamValley;
    private int CalcCorridorByDistanceHeightRatioMinMax(Rate[] ratesReversed, int range, Func<MathExtensions.Extream<Rate>, int, bool> extreamCondition) {
      ShowVoltsByDistanceAverage(false);
      var ps = PointSize;
      var corrRate = CorridorStats.Rates.LastOrDefault();
      Func<bool, int, int> getExtreamIndex = (isHill, index) => {
        var rates = ratesReversed.CopyToArray(index, range).Select((r, i) => new { r, i }).OrderBy(d => GetVoltage(d.r));
        return ratesReversed.ToList().IndexOf((isHill ? rates.Last() : rates.First()).r);
      };
      Action setCentersOfMass = () => {
        if (extreamValley.Element < extreamHill.Element) return;
        var indexStart = getExtreamIndex(false, ratesReversed.ToList().IndexOf(extreamValley.Element));
        var count = getExtreamIndex(true, ratesReversed.ToList().IndexOf(extreamHill.Element)) - indexStart;
        var rates = ratesReversed.CopyToArray(indexStart, count);
        CenterOfMassBuy = rates.Max(_priceAvg);
        CenterOfMassSell = rates.Min(_priceAvg);
      };
      Action<MathExtensions.Extream<Rate>, MathExtensions.Extream<Rate>, Action>
        triggerSCoM = (eFrom, eTo, swap) => {
          if (eTo.Element == eFrom.Element) return;
          swap();
          setCentersOfMass();
        };
      int? takeCount0 = corrRate == null ? (int?)null : RatesArray.Count - RatesArray.IndexOf(corrRate);
      var takeCount = takeCount0.HasValue ? (range * 1.1).ToInt() : int.MaxValue;
      Func<int, int> getIndex = (take) => {
        var extreams = ratesReversed.Take(take)
          .TakeWhile(d => !GetVoltage(d).IsNaN())
          .Extreams(GetVoltage, range);
        var first = extreams.Where(extreamCondition).FirstOrDefault();
        if (first == null) return 0;
        var levels = extreams.SkipWhile(d => d.Slope > 0).Take(2).ToArray();
        if (levels.Length == 2) {
          extreamValley = levels[0];
          extreamHill = levels[1];
          setCentersOfMass();
        } else {
          if (first.Slope > 0) triggerSCoM(first, extreamHill, () => extreamHill = first);
          else triggerSCoM(first, extreamValley, () => extreamValley = first);
        }
        return getExtreamIndex(first.Slope > 0, first.Index);
      };
      var index2 = getIndex(takeCount);
      if (index2 > 0) {
        SetCorridorStopDate(null);
        return index2;
      }
      return !takeCount0.HasValue ? getIndex(int.MaxValue) : takeCount0.Value;
    }
    private int CalcCorridorByDistanceHeightRatioMinMax_Good6(Rate[] ratesReversed, int range) {
      ShowVoltsByDistanceAverage(false);
      var ps = PointSize;
      var corrRate = CorridorStats.Rates.LastOrDefault();
      Func<bool, int, int> getExtreamIndex = (isHill, index) => {
        var rates = ratesReversed.CopyToArray(index, range).Select((r, i) => new { r, i }).OrderBy(d => GetVoltage(d.r));
        return (isHill ? rates.Last().i : rates.First().i) + index;
      };
      int? takeCount0 = corrRate == null ? (int?)null : RatesArray.Count - RatesArray.IndexOf(corrRate);
      var takeCount = takeCount0.HasValue ? (range * 1.1).ToInt() : int.MaxValue;
      Func<int, int> getIndex = (take) => {
        var extreams = ratesReversed.Take(take)
          .TakeWhile(d => !GetVoltage(d).IsNaN())
          .Extreams(GetVoltage, range);
        var first = extreams.SkipWhile(d => d.Slope < 0).FirstOrDefault();
        if (first == null) return 0;
        return getExtreamIndex(true, first.Index);
      };
      var index2 = getIndex(takeCount);
      if (index2 > 0) {
        CorridorStopDate = DateTime.MinValue;
        return index2;
      }
      return !takeCount0.HasValue ? getIndex(int.MaxValue) : takeCount0.Value;
    }
    private int CalcCorridorByDistanceHeightRatioMinMax_Good5(Rate[] ratesReversed, int range) {
      ShowVoltsByDistanceAverage(false);
      var ps = PointSize;
      var corrRate = CorridorStats.Rates.LastOrDefault();
      Func<bool, int, int> getExtreamIndex = (isHill, index) => {
        var rates = ratesReversed.CopyToArray(index, range).Select((r, i) => new { r, i }).OrderBy(d => GetVoltage(d.r));
        return (isHill ? rates.Last().i : rates.First().i) + index;
      };
      Func<int, int, int> getIndex = (take, slopeChangeCounter) => {
        var slopSignPrev = 0;
        var extreamIndexPrev = int.MinValue / 2;
        var mustContinue = true;
        var extreams = ratesReversed.Take(take)
          .TakeWhile(d => !GetVoltage(d).IsNaN())
          .Extreams(GetVoltage, range
          , extr => {
            if (extr.Slope.Sign() != slopSignPrev && extr.Index - extreamIndexPrev > Environment.ProcessorCount * 2) {
              extreamIndexPrev = extr.Index;
              slopSignPrev = extr.Slope.Sign();
              mustContinue = slopeChangeCounter-- > 0;
              return true;
            }
            return mustContinue;
          });
        var first = extreams/*.SkipWhile(d => d.Slope < 0)*/.FirstOrDefault();
        if (first == null) return 0;
        var index = first.Index;
        var levels = extreams.SkipWhile(d => d.Slope > 0).Take(2).ToArray();
        if (levels.Length == 2) {
          var indexStart = getExtreamIndex(false, levels[0].Index);
          var count = getExtreamIndex(true, levels[1].Index) - indexStart;
          var rates = ratesReversed.CopyToArray(indexStart, count);
          CenterOfMassBuy = rates.Max(_priceAvg);
          CenterOfMassSell = rates.Min(_priceAvg);
        }
        return getExtreamIndex(first.Slope > 0, index);
      };
      return getIndex(int.MaxValue, 3);
    }
    private int CalcCorridorByDistanceHeightRatioMinMax_Good4(Rate[] ratesReversed, int range) {
      ShowVoltsByDistanceAverage(false);
      var ps = PointSize;
      var corrRate = CorridorStats.Rates.LastOrDefault();
      int? takeCount0 = corrRate == null ? (int?)null : RatesArray.Count - RatesArray.IndexOf(corrRate);
      var takeCount = takeCount0.HasValue ? (range * 1.1).ToInt() : int.MaxValue;
      Func<int, int> getIndex = (take) => {
        var datas = ratesReversed.Take(take).Select(GetVoltage).Integral(range)
          .Select((rates, i) => new { rates, i })
          .TakeWhile(d => !d.rates.Last().IsNaN())
          .AsParallel()
          .Select(d => new { d.i, slope = d.rates.Regress(1).LineSlope() })
          .OrderBy(d => d.i).ToArray();
        var extreams = new { i = 0, slope = 0.0 }.YieldBreak().ToList();
        datas
          .SkipWhile(d => d.slope.IsNaN())
          .TakeWhile(d => !d.slope.IsNaN())
          .Aggregate((p, n) => {
            if (n.slope.Sign() != p.slope.Sign()) extreams.Add(p);
            return n;
          });
        var first = extreams.OrderBy(d => d.i).SkipWhile(d => d.slope < 0).FirstOrDefault();
        if (first == null) return 0;
        var index = first.i;
        return ratesReversed.CopyToArray(index, range).Select((r, i) => new { r, i }).OrderBy(d => GetVoltage(d.r)).Last().i + index;
      };
      var index2 = getIndex(takeCount);
      if (index2 > 0) return index2;
      return !takeCount0.HasValue ? getIndex(int.MaxValue) : takeCount0.Value;
    }

    private int CalcCorridorByDistanceHeightRatioMinMax_Good3(Rate[] ratesReversed, int range) {
      ShowVoltsByDistanceAverage(false);
      var ps = PointSize;
      var corrRate = CorridorStats.Rates.LastOrDefault();
      var takeCount = corrRate == null ? int.MaxValue : RatesArray.Count - RatesArray.IndexOf(corrRate) + (range * 1.1).ToInt();
      Func<int, int, int> getIndex = (take, iterations) => {
        var datas = ratesReversed.Take(take).Select(GetVoltage).Integral(range)
          .Select((rates, i) => new { rates, i, h = rates.Height() })
          .TakeWhile(d => !d.h.IsNaN())
          .ToArray().AverageByIterations(d => d.h, (v, a) => v <= a, iterations)
          .AsParallel()
          .Select(d => new { d.i, slope = d.rates.Regress(1).LineSlope() })
          .OrderBy(d => d.i).ToArray();
        var extreams = new { i = 0, slope = 0.0 }.YieldBreak().ToList();
        datas
          .SkipWhile(d => d.slope.IsNaN())
          .TakeWhile(d => !d.slope.IsNaN())
          .Aggregate((p, n) => {
            if (n.slope.Sign() != p.slope.Sign()) extreams.Add(p);
            return n;
          });
        var first = extreams.OrderBy(d => d.i).SkipWhile(d => d.slope < 0).FirstOrDefault();
        if (first == null) return 0;
        var index = first.i;
        return ratesReversed.CopyToArray(index, range).Select((r, i) => new { r, i }).OrderBy(d => GetVoltage(d.r)).Last().i + index;
      };
      var iter = 2;
      do {
        var index = getIndex(takeCount, iter);
        if (index > 0) return index;
      } while (iter-- > 0);
      return getIndex(int.MaxValue, 0);
    }
    private int CalcCorridorByDistanceHeightRatioMinMax_Good2(Rate[] ratesReversed, int range) {
      ShowVoltsByDistanceAverage(false);
      var ps = PointSize;
      var datas = ratesReversed.Integral(range)
        .AsParallel()
        .Select((rates, i) => new { i, slope = rates.Regress(1, GetVoltage).LineSlope() })
        .OrderBy(d => d.i).ToArray();
      var extreams = new { i = 0, slope = 0.0 }.YieldBreak().ToList();
      datas
        .SkipWhile(d => d.slope.IsNaN())
        .TakeWhile(d => !d.slope.IsNaN())
        .Aggregate((p, n) => {
          if (n.slope.Sign() != p.slope.Sign()) extreams.Add(p);
          return n;
        });
      var index = extreams.OrderBy(d => d.i).SkipWhile(d => d.slope < 0).First().i;
      index = ratesReversed.CopyToArray(index, range).Select((r, i) => new { r, i }).OrderBy(d => GetVoltage(d.r)).Last().i + index;
      return index;
    }

    private int CalcCorridorByDistanceHeightRatioMinMax_Good(Rate[] ratesReversed, int range) {
      var ps = PointSize;
      var datas = ratesReversed.Integral(range)
        .Select(Extensions.Distance)
        .Integral(range)
        .AsParallel()
        .Select((a, i) => new { da = a.Sum(), i }).ToArray();
      ShowVoltsByDistanceAverage(false);
      var dataMin = new { data = datas.OrderBy(a => a.da).First(), sign = 1 };
      var dataMax = new { data = datas.OrderByDescending(a => a.da).First(), sign = -1 };
      var data = new[] { dataMin, dataMax }.Where(d => d.data.i > 0).OrderBy(d => d.data.i).First();
      if (false) {
        var index = datas.Zip(datas.Skip(1), (p, n) => new { p, n }).SkipWhile(pn => pn.n.da >= pn.p.da).TakeWhile(pn => pn.n.da < pn.p.da).Last().n.i;
        var rates = ratesReversed.CopyToArray(index, range);
        var a1 = rates.Select((r, i) => new { r, i }).OrderBy(a2 => GetVoltage(a2.r)).First().i;
        rates = ratesReversed.CopyToArray(a1 + index, range);
        CenterOfMassBuy = rates.Max(_priceAvg);
        CenterOfMassSell = rates.Min(_priceAvg);
      } else if (data == dataMin) {
        var rates = ratesReversed.CopyToArray(dataMin.data.i, range);
        var a1 = rates.Select((r, i) => new { r, i }).OrderBy(a2 => GetVoltage(a2.r)).First().i;
        rates = ratesReversed.CopyToArray(a1 + dataMin.data.i, range);
        CenterOfMassBuy = rates.Max(_priceAvg);
        CenterOfMassSell = rates.Min(_priceAvg);
      }
      var stopIndex = data.data.i;
      {
        var rates = ratesReversed.CopyToArray(stopIndex, range);
        var a1 = rates.Select((r, i) => new { r, i }).OrderBy(a2 => data.sign * GetVoltage(a2.r)).First().i;
        return a1 + stopIndex;
      }
    }

    private int CalcCorridorByDistanceBellowAvg(Rate[] ratesReversed, int range) {
      var ps = PointSize;
      ratesReversed = UseRatesInternal(ri => ri.Reverse<Rate>().Take(RatesArray.Count + CorridorDistance).ToArray());
      ratesReversed.FillDistance();
      var datas = ratesReversed.Integral(range)
        .Select(Extensions.Distance)
        .Integral(CorridorDistance)
        .AsParallel()
        .Select((a, i) => new { da = a.Sum(), i }).ToArray();
      var dataMin = datas.OrderBy(a => a.da).First();
      var dataMax = datas.OrderByDescending(a => a.da).First();
      var data = new[] { dataMin, dataMax }.OrderBy(d => d.i).First();
      var stopIndex = data.i;
      var startIndex = data.i + range - 1;
      if (CorridorStats.Rates.Any()) {
        CorridorStats.StopRate = RatesArray.ReverseIfNot().Skip(stopIndex).First();
        _CorridorStopDate = CorridorStats.StopRate.StartDate;
      } else
        CorridorStopDate = RatesArray.ReverseIfNot().Skip(stopIndex).First().StartDate;
      return range;
    }

    private CorridorStatistics ScanCorridorByMinStDevInRange(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
      var lazyCount = new Lazy<int>(() => CalcCorridorByMinStDevInRange(ratesReversed.Select(_priceAvg).ToArray(), CorridorDistance));
      return ScanCorridorLazy(ratesReversed, lazyCount, GetShowVoltageFunction());
    }
    private int CalcCorridorByMinStDevInRange(double[] ratesReversed, int range) {
      var ps = PointSize;
      //ratesReversed = RatesInternal.sel.Reverse<Rate>().Take(RatesArray.Count + CorridorDistance).ToArray();
      var data = ratesReversed.Take(1400).Integral(range)
        .Select((values, i) => new { stDev = values.StDev(), i })
        .OrderBy(a => a.stDev).First();
      var stopIndex = data.i;
      var startIndex = data.i + range - 1;
      if (CorridorStats.Rates.Any()) {
        CorridorStats.StopRate = RatesArray.ReverseIfNot().Skip(stopIndex).First();
        _CorridorStopDate = CorridorStats.StopRate.StartDate;
      }
      return range;
    }

    private int CorridorStartIndex() {
      return !CorridorStats.Rates.Any() ? int.MaxValue : RatesArray.IndexOf(CorridorStats.Rates.Last());
    }

    private int CalcCorridorByStDevBalanceAndMaxLength3(double[] ratesReversed, int stopIndex) {
      var stDevRatio = 0.99;
      Func<double, double, bool> isMore = (h, p) => h / p >= stDevRatio;
      var lenths = Lib.IteratonSequence(stopIndex / 10, stopIndex).ToArray();
      return Partitioner.Create(lenths, true).AsParallel()
        .Select(length => new { ratio = CalcStDevBalanceRatio(ratesReversed, length), length }).ToArray()
        .OrderByDescending(r => r.ratio).First().length;
    }

    private int CalcCorridorByStDevBalanceAndMaxLength2(double[] ratesReversed, int startIndex) {
      var stDevRatio = 0.99;
      Func<double, double, bool> isMore = (h, p) => h / p >= stDevRatio;
      var lenths = Lib.IteratonSequence(startIndex, ratesReversed.Length).ToArray();
      return Partitioner.Create(lenths, true).AsParallel()
        .Select(length => new { ratio = CalcStDevBalanceRatio(ratesReversed, length), length }).ToArray()
        .OrderByDescending(r => r.length)
        .SkipWhile(r => r.ratio < stDevRatio)
        .TakeWhile(r => r.ratio > stDevRatio)
        .OrderByDescending(r => r.ratio)
        .Select(r => r.length).DefaultIfEmpty(startIndex).First();
    }

    private int CalcCorridorByStDevBalanceAndMaxLength(double[] ratesReversed, int startIndex) {
      var stDevRatio = 0.99;
      Func<double, double, bool> isMore = (h, p) => h / p >= stDevRatio;
      return Lib.IteratonSequence(startIndex, ratesReversed.Length)
        .Where(length => CalcStDevBalance(ratesReversed, isMore, length))
        .DefaultIfEmpty(startIndex).Max();
    }


    private int CalcCorridorByStDevBalance(double[] ratesReversed, int startIndex) {
      var stDevRatio = 0.99;
      Func<double, double, bool> isLess = (h, p) => h / p < stDevRatio;
      Func<double, double, bool> isMore = (h, p) => h / p >= stDevRatio;
      return Lib.IteratonSequence(startIndex, ratesReversed.Length)
        .SkipWhile(length => CalcStDevBalance(ratesReversed, isLess, length))
        .TakeWhile(length => CalcStDevBalance(ratesReversed, isMore, length))
        .DefaultIfEmpty(startIndex).Last();
    }

    private static bool CalcStDevBalance(double[] ratesReversed, Func<double, double, bool> isLess, int length) {
      var rates = new double[length];
      Array.Copy(ratesReversed, 0, rates, 0, length);
      var stDevH = rates.StDevByRegressoin();
      var stDevP = rates.StDev();
      return isLess(stDevH, stDevP);
    }
    private static double CalcStDevBalanceRatio(double[] ratesReversed, int length) {
      var rates = new double[length];
      Array.Copy(ratesReversed, 0, rates, 0, length);
      var stDevH = rates.StDevByRegressoin();
      var stDevP = rates.StDev();
      return stDevH / stDevP;
    }
    Func<IList<Rate>, int, int, Harmonic[]> _FastHarmonics;
    private Func<IList<Rate>, int, int, Harmonic[]> FastHarmonics {
      get {
        if (_FastHarmonics != null) return _FastHarmonics;
        _FastHarmonics = MemoizeFastHarmonics();
        this.WhenAny(tm => tm.BarsCount, _ => "BarsCount")
        .Merge(this.WhenAny(tm => BarPeriod, _ => "Bars"))
        .Merge(this.WhenAny(tm => CorridorDistanceRatio, _ => "CorridorDistanceRatio"))
        .Subscribe(_ => FastHarmonics = null);
        return _FastHarmonics;
      }
      set { _FastHarmonics = value ?? MemoizeFastHarmonics(); }
    }
    Func<IList<Rate>, int, int, Harmonic[]> MemoizeFastHarmonics() {
      var map = new ConcurrentDictionary<Tuple<DateTime, DateTime, int, int>, Harmonic[]>();
      return (ratesForCorridor, minsPerHour, minsMax) => {
        var key = Tuple.Create(ratesForCorridor[0].StartDate, ratesForCorridor.Last().StartDate, minsPerHour, minsMax);
        Harmonic[] harmonics;
        if (map.TryGetValue(key, out harmonics))
          return harmonics;
        harmonics = (from mins in Enumerable.Range(minsPerHour, minsMax - minsPerHour).AsParallel()
                     from harm in CalcHurmonicsAll(ratesForCorridor, mins).Select(h => new { h.Height, h.Hours, mins })
                     group harm by harm.Height into gh
                     let a = new Harmonic(gh.Select(h => h.Hours).Min(m => m), gh.Key)
                     orderby a.Height / a.Hours descending
                     select a
                         ).ToArray();
        map.TryAdd(key, harmonics);
        return harmonics;
      };
    }

    SortedDictionary<DateTime, double> _timeFrameHeights = new SortedDictionary<DateTime, double>();

    private void SetCOMs(IList<Rate> revs, IEnumerable<KeyValuePair<DateTime, double>> tfs) {
      var thirdDateStart = tfs.OrderBy(kv => kv.Value).Select(kv => kv.Key).DefaultIfEmpty().First();
      SetCOMs(revs, thirdDateStart);
    }

    private void SetCOMs(IList<Rate> revs, DateTime thirdDateStart) {
      var secondIndex = revs.TakeWhile(r => r.StartDate > thirdDateStart).Count();
      double min, max;
      revs.Skip(secondIndex).Take(CorridorDistance).Height(out min, out max);
      CenterOfMassBuy = max;
      CenterOfMassSell = min;
    }

    private CorridorStatistics ScanCorridorByTime(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), new Lazy<int>(() => (BarsCountCalc * CorridorDistanceRatio).ToInt()), ShowVoltsByVolatility);
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

      var countOut = Partitioner.Create(Enumerable.Range(countStart, 0.Max(ratesReversed.Length - countStart)).ToArray(), true)
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
      var corridorRates = RatesArray.SkipWhile(r => r.StartDate < bigBarStart).Reverse().ToList();
      if (corridorRates.Count > 1) {
        return new CorridorStatistics(this, corridorRates, double.NaN, new[] { corridorRates.Average(r => r.PriceAvg), 0.0 });
      }
      return null;
    }

    private CorridorStatistics ScanCorridorByHorizontalLineCrosses(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      WaveShort.Rates = null;
      double level;
      var rates = CorridorByVerticalLineCrosses2(ratesForCorridor.ReverseIfNot(), _priceAvg, CorridorDistanceRatio.ToInt(), out level);
      if (rates != null && rates.Any() && (!IsCorridorForwardOnly || rates.LastBC().StartDate > CorridorStats.StartDate)) {
        MagnetPrice = level;
        WaveShort.Rates = rates;
      }
      if (!WaveShort.HasRates) {
        if (CorridorStats.Rates != null && CorridorStats.Rates.Any()) {
          var dateStop = CorridorStats.Rates.LastBC().StartDate;
          WaveShort.Rates = ratesForCorridor.ReverseIfNot().TakeWhile(r => r.StartDate >= dateStop).ToArray();
        } else
          WaveShort.Rates = ratesForCorridor.ReverseIfNot();
      }
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }
    private double CrossesAverage(IList<Rate> rates) {
      var levels = ParallelEnumerable.Range(0, InPips(RatesHeight).ToInt()).Select(level => _RatesMin + InPoints(level));
      return levels.Aggregate(new List<double>(), (list, level) => {
        var line = Enumerable.Repeat(level, rates.Count).ToArray();
        var crosses = rates.Crosses(line, _priceAvg).Count();
        list.Add(crosses);
        return list;
      }, list => list.AverageByIterations(1).Average());
    }

    private double CrossesAverageByRegression(IList<Rate> rates) {
      var regressionLine = rates.Select(_priceAvg).ToArray().Regression(1);
      var zipped = regressionLine.Zip(rates, (l, r) => r.PriceAvg - l).ToArray();
      var min = zipped.Min();
      var max = zipped.Max();
      var height = max - min;
      var point = InPoints(1);
      var offsets = ParallelEnumerable.Range(0, InPips(height).ToInt()).Select(h => min + h * point);
      return offsets.Aggregate(new List<double>(), (list, offset) => {
        var line = regressionLine.Select(p => p + offset).ToArray();
        var crosses = rates.Crosses(line, _priceAvg).Count();
        list.Add(crosses);
        return list;
      }, list => list.AverageByIterations(1).Average());
    }

    private static T GetStDevUpDown<T>(double[] rates, double[] line, Func<double, double, T> stDevUpDownProjector) {
      double stDevUp, stDevDown;
      GetStDevUpDown(rates, line, out stDevUp, out stDevDown);
      return stDevUpDownProjector(stDevUp, stDevDown);
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

    public Func<double> GetVoltageHigh = () => 0;
    public Func<double> GetVoltageAverage = () => 0;
    public Func<double> GetVoltageLow = () => 0;
    public Func<Rate, double> GetVoltage = r => r.DistanceHistory;
    public Func<Rate, double[]> GetVoltages = r => r.VoltageLocal0;
    double VoltageCurrent { get { return GetVoltage(RateLast); } }
    public Action<Rate, double> SetVoltage = (r, v) => r.DistanceHistory = v;
    public Action<Rate, string, double> SetVoltageKey = (r, k, v) => r.DistanceHistory = v;
    public Func<Rate, double> GetVoltage2 = r => r.Distance1;
    public Action<Rate, double> SetVoltage2 = (r, v) => r.Distance1 = v;

    #region _corridors
    DateTime _corridorStartDate1 = DateTime.MinValue;
    int _corridorLength1 = 0;
    public int CorridorLength1 {
      get { return _corridorLength1; }
      set { _corridorLength1 = value; }
    }
    DateTime _corridorStartDate2 = DateTime.MinValue;
    int _corridorLength2 = 0;
    public int CorridorLength2 {
      get { return _corridorLength2; }
      set { _corridorLength2 = value; }
    }
    #endregion
    private CorridorStatistics ScanCorridorByStDevByHeight(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorByStDevByHeight(ratesForCorridor, rates => rates.ToArray(_priceAvg), priceHigh, priceLow);
    }
    #region ShowSecondTrend
    private bool _ShowSecondTrend = true;
    [Category(categoryCorridor)]
    [Description("Freeze Corridor Start Date")]
    public bool ShowSecondTrend {
      get { return _ShowSecondTrend; }
      set {
        if (_ShowSecondTrend != value) {
          _ShowSecondTrend = value;
          if (!value) _corridorLength2 = 0;
          OnPropertyChanged("ShowSecondTrend");
        }
      }
    }
    #endregion
    #region ClearCOMs
    private bool _ClearCOMs = true;
    [Category(categoryCorridor)]
    [Description("Clear Centers Of Mass")]
    public bool ClearCOMs {
      get { return _ClearCOMs; }
      set {
        if (_ClearCOMs != value) {
          _ClearCOMs = value;
          if (value) {
            CenterOfMassBuy = CenterOfMassSell = RatesArray.Average(_priceAvg);
            ClearCOMs = false;
          } else
            OnPropertyChanged("ClearCOMs");
        }
      }
    }
    #endregion
    private CorridorStatistics ScanCorridorByStDevByHeightFft(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorByStDevByHeight(ratesForCorridor, rates => rates.ToArray(GetPriceMA), priceHigh, priceLow);
    }
    private CorridorStatistics ScanCorridorByStDevByHeight(IList<Rate> ratesForCorridor, Func<IList<Rate>, IEnumerable<double>> tranc, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var cp = _priceAvg ?? CorridorPrice();
      var heightMin = ScanCorridorByStDevAndAngleHeightMinEx;
      var heightMin2 = ScanCorridorByStDevAndAngleHeightMinEx2;
      Func<int> freezedCount = () => CorridorStats.Rates.CopyLast(1).Select(r => RatesArray.SkipWhile(r2 => r2.StartDate < r.StartDate).Count()).FirstOrDefault(CorridorDistance);
      Func<Rate, double> priceAvg02 = rate => (rate.PriceAvg2 - rate.PriceAvg1) / 2 + rate.PriceAvg1;
      Func<Rate, double> priceAvg03 = rate => rate.PriceAvg1 + (rate.PriceAvg3 - rate.PriceAvg1) / 2;
      var freeze = CorridorStats.Rates.SkipWhile(r => r.PriceAvg1.IsNaN()).Take(1)
        .Any(rate => GetTradeEnterBy(true)(rate) >= priceAvg02(rate) || GetTradeEnterBy(false)(rate) <= priceAvg03(rate));
      Func<IList<Rate>, int> scan = rates => {
        if (freeze) return freezedCount();
        var defaultCount = rates.Count;
        var prices = tranc(rates).ToList();
        int count = 0;
        Func<int, double, int> getHeight = (c, h) => count = _corridorLength2 = CalcCorridorLengthByHeightByRegressionMin(prices, h, defaultCount, c, DoFineTuneCorridor);
        //Func<int, double, int> getHeight2 = (c, h) => _corridorLength2 = CalcCorridorLengthByHeightByRegressionMin(prices, h, defaultCount, DoFineTuneCorridor ? (c * 0.5).ToInt() : c, false);
        new[] { new { h = heightMin(prices), a = getHeight, i = "" }/*, new { h = heightMin2(prices), a = getHeight2, i = "2" }*/ }
          .Take(ShowSecondTrend ? 2 : 1)
          .Do(a => { if (a.h.IsNaN())throw new Exception(new StackFrame().GetMethod().Name + ":height" + a.i + " is NaN"); })
          .OrderBy(a => a.h)
          .Aggregate(10, (c, a) => a.a(c, a.h));
        if (TicksPerSecondAverage == 0) return count;
        var waveWidth = BarPeriod == BarsPeriodType.m1 ? 60 : _corridorLength2;// 3600.Div(TicksPerSecondAverage).ToInt();
        var maxCount = 1000;
        var bufferCount = (rates.Count - count).Div(maxCount).ToInt().Max(1);
        var ratesForWave = rates
          .Skip(count)
          .Buffer(bufferCount)
          .Select(b => new { Price = b.Average(r => r.PriceCMALast), StartDate = b.Last().StartDate }).ToList();
        //var s = string.Join(Environment.NewLine, ratesForWave.Select(r => r.StartDate + "," + r.PriceCMALast));
        //string.IsNullOrEmpty(s);
        //ratesForWave.Reverse();
        var cmas = rates.Cma(r => r.PriceCMALast, CmaPeriodByRatesCount() * CmaRatioForWaveLength);
        var csv = rates.Select((r, i) => new { r, i }).Zip(cmas, (r, ma) => new { r.r.PriceCMALast, ma }).Csv("{0},{1}", x => x.PriceCMALast, x => x.ma);
        csv.IsEmpty();
        var dmas = rates.Select((r, i) => new { r, i })
          .Zip(cmas, (r, ma) => new { r, ma })
          .DistinctUntilChanged(x => x.r.r.PriceCMALast.Sign(x.ma))
          .ToArray();
        var widths = dmas.Zip(dmas.Skip(1), (dma1, dma2) => (double)dma2.r.i - dma1.r.i).DefaultIfEmpty(0.0).ToArray();
        var widthAvg = widths.AverageByIterations(-1).Average();
        var waveWidth2 = widths.Where(w => w > widthAvg).Average().Div(bufferCount).ToInt();
        return ratesForWave
          .Extreams(waveWidth2, r => r.Price, r => r.StartDate)
          .Skip(1)
          .Take(1)
          .Select(w => rates.FuzzyFind(w.Item2, (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate)))
          .DefaultIfEmpty(count)
          .Select(c => (Func<int>)(() => c))
          .Concat(new[] { freezedCount }.Where(_ => IsCorridorForwardOnly))
          .Max(a => a());
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan);
    }
    private CorridorStatistics ScanCorridorByWaveCount(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<int> freezedCount = () => CorridorStats.Rates.CopyLast(1).Select(r => RatesArray.SkipWhile(r2 => r2.StartDate < r.StartDate).Count()).FirstOrDefault(CorridorDistance);
      Func<int> freezedCount2 = () => CorridorStats.Rates.CopyLast(1)
        .Select(r => r.StartDate)
        .SelectMany(sd => RatesArray.FuzzyIndex(sd, (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate)))
        .FirstOrDefault(CorridorDistance);
      //if (freezedCount() != freezedCount2())
      //  throw new Exception("freezedCount() != freezedCount2()");
      Func<Rate, double> priceAvg02 = rate => (rate.PriceAvg2 - rate.PriceAvg1) / 2 + rate.PriceAvg1;
      Func<Rate, double> priceAvg03 = rate => rate.PriceAvg1 + (rate.PriceAvg3 - rate.PriceAvg1) / 2;
      Func<bool> freeze = () => !_scanCorridorByWaveCountMustReset && CorridorStats.Rates.SkipWhile(r => r.PriceAvg1.IsNaN()).Take(1)
        .Any(rate => GetTradeEnterBy(true)(rate) >= priceAvg02(rate) || GetTradeEnterBy(false)(rate) <= priceAvg03(rate));
      var reversed = ratesForCorridor.ReverseIfNot();
      reversed.TakeWhile(r => r.VoltageLocal3.IsNaN()).ForEach(r => r.VoltageLocal3 = TicksPerSecondAverage);
      return ScanCorridorLazy(reversed, MonoidsCore.ToLazy(() => ScanCorridorByWaveCountImpl(reversed)));
    }

    private int ScanCorridorByWaveCountImpl(IList<Rate> rates) {
      _scanCorridorByWaveCountMustReset = false;
      var cmas = rates.Cma(r => r.PriceCMALast, CmaPeriodByRatesCount() * CmaRatioForWaveLength);
      if (CmaRatioForWaveLength == 0) {
        var csv = rates.Select((r, i) => new { r, i }).Zip(cmas, (r, ma) => new { r.r.PriceCMALast, ma }).Csv("{0},{1}", x => x.PriceCMALast, x => x.ma);
        csv.IsEmpty();
      }

      var maxCount = 1000;
      var bufferCount = (rates.Count).Div(maxCount).Max(1).ToInt();

      var dmas = rates.Select((r, i) => new { r, i })
        .Zip(cmas, (r, ma) => new { r, ma })
        .DistinctUntilChanged(x => x.r.r.PriceCMALast.Sign(x.ma))
        .ToArray();

      var widths = dmas.Zip(dmas.Skip(1), (dma1, dma2) => (double)dma2.r.i - dma1.r.i).DefaultIfEmpty(0.0).ToArray();
      var widthAvg = widths.AverageByIterations(1).Average();
      var waveWidth = widthAvg.Div(bufferCount).ToInt();

      var ratesForWave = rates
        .Buffer(bufferCount)
        .Select(b => new { Price = b.Average(r => r.PriceCMALast), StartDate = b.Last().StartDate });
      var extreams = ratesForWave.Extreams(waveWidth, r => r.Price, r => r.StartDate).ToArray();
      var extreams2 = extreams.Scan(Tuple.Create(0, DateTime.Now, 0.0), (p, t) => {
        return p.Item1 == 0 ? t
          : t.Item1 - p.Item1 < waveWidth / 2
          ? p
          : p.Item3.SignUp() == t.Item3.SignUp()
          ? p
          : t;
      });
      Func<DateTime, Rate, Rate, bool> isBetween = (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate);
      Func<List<Tuple<int, DateTime, double>>> newList = () => new List<Tuple<int, DateTime, double>>();
      var extreams21 = extreams.Scan(newList(), (l, t) => {
        if (l.Count > 0 && t.Item1 - l[0].Item1 > waveWidth / 2)
          l = newList();
        l.Add(t);
        return l;
      })
      .Select(l => l[0])
      .GroupByAdjacent(t => t.Item3.SignUp())
      .Select(g => g.Last())
      .ToArray();
      var extreams3 = extreams21
        .DistinctUntilChanged(t => t.Item1)
        .SelectMany(w => rates.FuzzyIndex(w.Item2, isBetween))
        .ToList();
      var index = extreams3
        .Skip(CorridorWaveCount - 1)
        .Take(1)
        .DefaultIfEmpty(extreams3.DefaultIfEmpty(rates.Count - 1).Last())
        //.Where(_ => !freeze())
        //.DefaultIfEmpty(freezedCount)
        .Single();

      var interWaveStep = 2;
      var xx = extreams3
        .TakeWhile(c => c < index);
      _corridorLength1 = xx.TakeLast(interWaveStep)
        .DefaultIfEmpty(index)
        .First();
      _corridorStartDate1 = rates[_corridorLength1].StartDate;

      _corridorLength2 = extreams3.SkipWhile(c => c <= index).Take(interWaveStep).DefaultIfEmpty(index).Last();
      _corridorStartDate2 = rates[_corridorLength2].StartDate;
      return index + 1;
    }
    Lazy<IList<Rate>> _trendLines = null;
    public Lazy<IList<Rate>> TrendLines {
      get { return _trendLines; }
      private set { _trendLines = value; }
    }
    Lazy<IList<Rate>> _trendLines1 = null;
    public Lazy<IList<Rate>> TrendLines1 {
      get { return _trendLines1; }
      private set { _trendLines1 = value; }
    }
    Lazy<IList<Rate>> _trendLines2 = null;
    public Lazy<IList<Rate>> TrendLines2 {
      get { return _trendLines2; }
      private set { _trendLines2 = value; }
    }
    private double MaGapMax(IList<Rate> rates) {
      return rates.Skip(rates.Count.Div(1.1).ToInt()).Max(r => r.PriceCMALast.Abs(r.PriceAvg));
    }
    #region CorridorWaveCount
    private int _CorridorWaveCount = 3;
    [Category(categoryActive)]
    [WwwSetting(Group = wwwSettingsCorridor)]
    public int CorridorWaveCount {
      get { return _CorridorWaveCount; }
      set {
        if (_CorridorWaveCount != value) {
          _scanCorridorByWaveCountMustReset = true;
          _CorridorWaveCount = value.Max(1);
          OnPropertyChanged("CorridorWaveCount");
        }
      }
    }

    #endregion
    #region CmaRatioForWaveLength
    bool _scanCorridorByWaveCountMustReset;
    private double _CmaRatioForWaveLength = 3;
    [Category(categoryActive)]
    public double CmaRatioForWaveLength {
      get { return _CmaRatioForWaveLength; }
      set {
        if (_CmaRatioForWaveLength != value) {
          _CmaRatioForWaveLength = value;
          OnPropertyChanged("CmaRatioForWaveLength");
        }
      }
    }

    #endregion
    #region DoFineTuneCorridor
    private bool _DoFineTuneCorridor;
    [Category(categoryCorridor)]
    [DisplayName("Fine Tune")]
    public bool DoFineTuneCorridor {
      get { return _DoFineTuneCorridor; }
      set {
        if (_DoFineTuneCorridor != value) {
          _DoFineTuneCorridor = value;
          OnPropertyChanged("DoFineTuneCorridor");
        }
      }
    }

    #endregion
    private int CalcCorridorLengthByHeightByRegressionMin(List<double> prices, double heightMin, int defaultCount, int start, bool fineTune) {
      var getCount = Lib.GetIterator((_start, _end, _isOk, _nextStep) => {
        return Partitioner.Create(Lib.IteratonSequence(_start, _end, _nextStep).ToArray(), true)
          .AsParallel()
          .Select(i => new { i, ok = prices.GetRange(0, i.Min(prices.Count)).HeightByRegressoin() <= heightMin })
          .SkipWhile(a => _isOk(a.ok))
          .Take(1)
          .Select(a => a.i);
      });
      var getCount2 = Lib.GetIterator((_start, _end, _isOk, _nextStep) => {
        var heightPrev = double.NaN;
        Func<double, bool> isHeightOk = h => {
          var ok = h >= heightPrev.IfNaN(h);
          heightPrev = h;
          return ok;
        };
        return Lib.IteratonSequence(_start, _end, _nextStep)
          .Do(i => heightPrev = i == _start ? double.NaN : heightPrev)
          .Select(i => new { i, ok = isHeightOk(prices.GetRange(0, i.Min(prices.Count)).HeightByRegressoin()) })
          .SkipWhile(a => _isOk(a.ok))
          .Take(1)
          .Select(a => a.i);
      });
      var count = Lib.IteratorLoopPow(prices.Count, IteratorLastRatioForCorridor, start, prices.Count, getCount, a => a.IfEmpty(() => new[] { defaultCount }).Single());
      if (fineTune) {
        int? count2 = null;
        count = Lib.IteratorLoopPow(prices.Count, IteratorLastRatioForCorridor, count, prices.Count, getCount2,
          a => (count2 = a.IfEmpty(() => new[] { count2.GetValueOrDefault(count) }).Single()).Value);
      }
      return count;
    }

    CorridorStatistics ScanCorridorTillFlat3(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      Func<IList<Rate>, int> scan = rates => {
        var heightMin = ScanCorridorByStDevAndAngleHeightMin();// *Math.E;
        var ratesInternal = rates.Select(_priceAvg).ToList();
        var start = CalcCorridorLengthByHeightByRegressionMin(ratesInternal, heightMin, ratesInternal.Count, 10, DoFineTuneCorridor);
        if (start < CorridorDistance) return start;
        else {
          var lopper = MonoidsCore.ToFunc(0, i => {
            var count = i.Min(ratesInternal.Count);
            return new { count, slopeSign = ratesInternal.GetRange(0, i.Min(ratesInternal.Count)).LinearSlope().Sign() };
          });
          var end = ratesForCorridor.Count - 1;
          var nextStep = Lib.IteratonSequencePower(end, IteratorScanRatesLengthLastRatio);
          var corridor = GetLoper(nextStep, lopper, cors => cors
            .DistinctUntilChanged(a => a.slopeSign)
            .Take(2)
            .Buffer(2)
            .Where(b => b.Count == 2)
            .Select(b => b[1])
            .DefaultIfEmpty(new { count = start, slopeSign = 0 })
            .First());
          return IteratorLopper(start, end, nextStep, corridor, a => a.count).Last().count;
        }
      };
      return ScanCorridorLazy(ratesReversed, rates => scan(rates), GetShowVoltageFunction());
    }

    #region IteratorLastRatioForCorridor
    private double _IteratorLastRatioForCorridor = 3;
    [Category(categoryCorridor)]
    [DisplayName("ILRfC")]
    [Description("IteratorLastRationForCorridor")]
    public double IteratorLastRatioForCorridor {
      get { return _IteratorLastRatioForCorridor; }
      set {
        if (_IteratorLastRatioForCorridor != value) {
          _IteratorLastRatioForCorridor = value;
          OnPropertyChanged("IteratorLastRatioForCorridor");
        }
      }
    }

    #endregion
    #endregion
    #endregion

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

    #region Custom1
    private double _Custom1;
    public double Custom1 {
      get { return _Custom1; }
      set {
        if (_Custom1 != value) {
          _Custom1 = value;
          OnPropertyChanged("Custom1");
        }
      }
    }

    #endregion

    void ResetBarsCountCalc() {
      _BarsCountCalc = null;
      OnPropertyChanged("BarsCountCalc");
    }
    private int? _BarsCountCalc;
    [DisplayName("Bars Count Calc(45,360,..)")]
    [Category(categoryCorridor)]
    [Dnr]
    public int BarsCountCalc {
      get { return _BarsCountCalc.GetValueOrDefault(BarsCount); }
      set {
        if (_BarsCountCalc == value) return;
        _BarsCountCalc = value == 0 ? (int?)null : value;
        OnPropertyChanged("BarsCountCalc");
      }
    }

  }
}
