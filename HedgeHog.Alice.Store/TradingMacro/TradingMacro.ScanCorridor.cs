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
    #region SpikeHeightAbs
    bool _spikeHeightAbs;
    [Category(categoryActiveYesNo)]
    public bool SpikeHeightAbs {
      get { return _spikeHeightAbs; }
      set {
        _spikeHeightAbs = value;
        OnPropertyChanged(() => SpikeHeightAbs);
      }
    }
    #endregion
    private CorridorStatistics ScanCorridorByDistance(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var revsCount = RatesArray.Count + CorridorDistance * 2;
      var ratesReversed = RatesArray.ReverseIfNot().FillDistance().SafeArray();
      var distanceMin = RatesArray[0].Distance/(RatesArray.Count / CorridorDistance);
      Func<IList<Rate>,int> scan = rates => ratesReversed.TakeWhile(r => r.Distance <= distanceMin).Count();
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
    private CorridorStatistics ScanCorridorBySpike24(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<double, double, double> calcHeight = (p, l) => SpikeHeightAbs ? p.Abs(l) :  p - l;
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.Select(_priceAvg).ToArray();
        var spikeOut = new { length = 0, distance = 0.0 };
        var spikeProjector = spikeOut.ToFunc(0, 0.0, (length, distance) => new { length, distance });
        var spike = new[] { spikeOut }.AsParallel().ToFunc((IEnumerable<int>)null
          , (op) => Spikes24(prices, Partitioner.Create(op.ToArray(), true), calcHeight, spikeProjector));
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
    private static ParallelQuery<T> Spikes24<T>(IList<double> prices, OrderablePartitioner<int> lengths, Func<double, double, double> calcHeight, Func<int, double, T> projector) {
      Func<IEnumerable<double>, IEnumerable<double>, double> priceHeight = (price, line) => price.Zip(line, calcHeight).MaxBy(d => d.Abs()).First();
      Func<IList<double>, IList<double>, int, double> priceHeightLast = (price, line, chunk) => priceHeight(price.TakeLast(chunk), line.TakeLast(chunk));
      Func<IList<double>, IList<double>, int, double> priceHeightFirst = (price, line, chunk) => priceHeight(price.Take(chunk), line.Take(chunk));
      Func<IList<double>, IList<double>, int, double[]> priceHeights = (price, line, chunk)
        => new[] { priceHeightLast(price, line, chunk), priceHeightFirst(price, line, chunk) };
      return (
        from length in lengths.AsParallel()
        let rates2 = prices.CopyToArray(length)
        let regRes = rates2.Regression(1, (coeffs, line) => new { coeffs, line })
        let heights = priceHeights(rates2, regRes.line, 1)
        let distance = heights.Sum().Abs() / (heights.Max(d=>d.Abs()) / heights.Min(d=>d.Abs()))
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
        let coeffs = rates2.Regress(1,v=>priceDown(v).Avg(priceUp(v)))
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
        .Select(b=>b.Max())
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
          .Select(length=>Observable.Start(() =>{
          var rates2 = prices.CopyToArray(length);
          var coeffs = rates2.Regress(1);
          var height = rates2.HeightByRegressoin2();
          return new { length, slope = coeffs.LineSlope(), height, heightOk = height.Between(StDevByHeight, StDevByPriceAvg) };
          }
          ,ThreadPoolScheduler.Instance))
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
        Func<int> getPrevCount = () => CorridorStats.Rates.Select(r=>r.StartDate).TakeLast(1)
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
    private CorridorStatistics ScanCorridorTillFlat20(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var prices = rates.ToArray(_priceAvg);
        Func<int> getPrevCount = () => CorridorStats.Rates.Last().StartDate.Yield()
          .Select(date => RatesArray.ReverseIfNot().TakeWhile(r => r.StartDate >= date).Count())
          .DefaultIfEmpty(CorridorDistance).First();
        Stopwatch sw = Stopwatch.StartNew();
        var swDict = new Dictionary<string, double>();
        swDict.Add("1", sw.ElapsedMilliseconds);
        var counter = (
          from length in Lib.IteratonSequence(10, rates.Count)
          let rates2 = prices.CopyToArray(length)
          let coeffs = rates2.Regress(1)
          let height = rates2.HeightByRegressoin2()
          select new { length, slope = coeffs.LineSlope(), height, heightOk = height.Between(StDevByHeight, StDevByPriceAvg) }
        )
        .SkipWhile(a => !a.heightOk)
        .TakeWhile(a => a.heightOk)
        .OrderBy(a => a.length)
        .DistinctUntilChanged(d => d.slope.Sign())
        .Take(2).Select(a => a.length).IfEmpty(getPrevCount).Last();
        
        _swQueue.Enqueue(sw.ElapsedMilliseconds);
        Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, _swQueue.Average());

        return counter;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorTillFlat(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<IList<Rate>, int> scan = (rates) => {
        var counter = (
          from revs in rates.ReverseIfNot().Take((CorridorDistance * (Timeframe2CorridorLengthRatio + 1.0)).ToInt()).ToArray().Yield()
          from length in Lib.IteratonSequence(CorridorDistance, revs.Length)
          select new { length, slope = revs.CopyToArray(0, length).Regress(1, _priceAvg).LineSlope() }
        ).DistinctUntilChanged(d => d.slope.Sign())
        .Take(2).Select(a=>a.length).DefaultIfEmpty(CorridorDistance).Last();
        return counter;
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan, GetShowVoltageFunction());
    }

    private void LongestGap(IList<Rate> ratesForCorridor) {
      var middle = ratesForCorridor.Average(_priceAvg);
      var result = ratesForCorridor.Gaps(middle, _priceAvg).MaxBy(g => g.Count()).First().Count();
    }

    private CorridorStatistics ScanCorridorFixed(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), r => CorridorDistance, GetShowVoltageFunction());
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
        triggerSCoM = (eFrom, eTo,swap) => {
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
      Func<int,int, int> getIndex = (take,iterations) => {
        var datas = ratesReversed.Take(take).Select(GetVoltage).Integral(range)
          .Select((rates, i) => new { rates, i, h = rates.Height() })
          .TakeWhile(d => !d.h.IsNaN())
          .ToArray().AverageByIterations(d => d.h, (v, a) => v <= a, iterations)
          .AsParallel()
          .Select(d => new { d.i, slope = d.rates.Regress(1).LineSlope() })
          .OrderBy(d => d.i).ToArray();
        var extreams = new { i = 0, slope = 0.0 }.Return(0).ToList();
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

    public int HarmonicMin { get; set; }
    IList<Harmonic> CalcFractals(IList<Rate> ratesForCorridor) {
      var minsPerHour = 20;
      var harmonics = FastHarmonics(ratesForCorridor, minsPerHour,60);
      Func<Rate, double> price = rate => rate.PriceCMALast;
      Func<IList<Rate>, DateTime, DateTime, IEnumerable<Rate>> revRatesByDates = (rates, dtMin, dtMax) =>
        rates.SkipWhile(r => r.StartDate > dtMax).TakeWhile(r => r.StartDate >= dtMin);
      var fractalAnon = new { slope = 0, date = DateTime.MinValue };
      var calcFractals = fractalAnon.Yield().ToFunc(new Rate[0] as IList<Rate>, DateTime.MinValue, 0);
      calcFractals = (rates, dateToStart, distance) => rates
            .Integral(distance)
            .TakeWhile(chunk => chunk[0].StartDate > dateToStart)
            .Select(chunk => new { slope = chunk.Regress(1, price).LineSlope().SignUp(), date = chunk[0].StartDate })
            .DistinctUntilChanged(a => a.slope).Skip(1);//.SkipLast(1);
      var revs = ratesForCorridor.ReverseIfNot();
      var harmonicHours = harmonics.Select(h=>h.Hours).DefaultIfEmpty(ratesForCorridor.Count).ToArray();
      var fractalDistance = harmonicHours.Take(2).OrderBy(h => h).First().ToInt();
      HarmonicMin = harmonicHours[0].ToInt();
        //FractalTimes.Concat(revs[0].StartDate.Yield()).OrderBy(date => date)
        //.Yield(dates => dates.ToArray())
        //.Select(dates => dates.Zip(dates.Skip(1), (d1, d2) => new { min = d1.Min(d2), max = d1.Max(d2) }))
        //.SelectMany(dates => dates, (dates, date) => revRatesByDates(revs, date.min, date.max).Count() * CorrelationMinimum)
        //.DefaultIfEmpty(60)
        //.Max().ToInt();
      FractalTimes =
        (from f in calcFractals(revs, DateTime.MinValue, fractalDistance)
         let rates = revs.SkipWhile(r => r.StartDate > f.date).Take(fractalDistance).ToArray()
         select (f.slope < 0 ? rates.MaxBy(_priceAvg) : rates.MinBy(_priceAvg)).First().StartDate).ToArray();

      return harmonics;
    }

    Func<IList<Rate>, int,int, Harmonic[]> _FastHarmonics;
    private Func<IList<Rate>, int,int, Harmonic[]> FastHarmonics {
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
    Func<IList<Rate>, int,int, Harmonic[]> MemoizeFastHarmonics() {
      var map = new ConcurrentDictionary<Tuple<DateTime, DateTime, int,int>, Harmonic[]>();
      return (ratesForCorridor, minsPerHour,minsMax) => {
        var key = Tuple.Create(ratesForCorridor[0].StartDate, ratesForCorridor.Last().StartDate, minsPerHour, minsMax);
        Harmonic[] harmonics;
        if (map.TryGetValue(key, out harmonics))
          return harmonics;
        harmonics = (from mins in Enumerable.Range(minsPerHour, minsMax-minsPerHour).AsParallel()
                     from harm in CalcHurmonicsAll(ratesForCorridor, mins).Select(h => new { h.Height, h.Hours, mins })
                     group harm by harm.Height into gh
                     let a = new Harmonic(gh.Select(h => h.Hours).Min(m => m), gh.Key)
                     orderby a.Height/a.Hours descending
                     select a
                         ).ToArray();
        map.TryAdd(key, harmonics);
        return harmonics;
      };
    }

    SortedDictionary<DateTime, double> _timeFrameHeights = new SortedDictionary<DateTime, double>();
    private CorridorStatistics ScanCorridorByTimeFrameAndAngle2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<Rate, double> price = rate => rate.PriceCMALast;
      Func<IList<Rate>, DateTime, DateTime, IEnumerable<Rate>> revRatesByDates = (rates, dtMin, dtMax) =>
        rates.SkipWhile(r => r.StartDate > dtMax).TakeWhile(r => r.StartDate >= dtMin);
      var fractalAnon = new { slope = 0, date = DateTime.MinValue };
      var calcFractals = fractalAnon.Yield().ToFunc(new Rate[0] as IList<Rate>, DateTime.MinValue, 0);
      calcFractals = (rates, dateToStart, distance) => rates
            .Integral(distance)
            .TakeWhile(chunk => chunk[0].StartDate > dateToStart)
            .Select(chunk => new { slope = chunk.Regress(1, price).LineSlope().SignUp(), date = chunk[0].StartDate })
            .DistinctUntilChanged(a => a.slope).Skip(1);//.SkipLast(1);
      var revs = ratesForCorridor.ReverseIfNot();
      var corridorDistance = FractalTimes.OrderBy(date => date)
        .TakeLast(3)
        .Yield(dates => dates.ToArray())
        .Where(dates => dates.Length == 3)
        .Select(dates => revs.SkipWhile(r => r.StartDate > dates[2]).TakeWhile(r => r.StartDate >= dates[0]).Count())
        .DefaultIfEmpty(CorridorDistance).First().Max(CorridorDistance);
      Func<DateTime, double> stDev = date => revs.SkipWhile(r => r.StartDate > date).Take(corridorDistance).Height(price);
      DateTime dateMax = revs[0].StartDate, dateMin = revs.TakeLast((corridorDistance * 1.1).ToInt()).First().StartDate;
      _timeFrameHeights.Keys.Where(d => !d.Between(dateMin, dateMax)).ToList().ForEach(d => _timeFrameHeights.Remove(d));
      //_timeFrameHeights.TakeWhile(kv => kv.Value == double.MaxValue).Select(kv => kv.Key).ToList().ForEach(d => _timeFrameHeights.Remove(d));
      //_timeFrameHeights.Clear();
      var dateStart = _timeFrameHeights.Keys.LastOrDefault();
      Func<IList<Rate>, int> scan = (rates) => {
        calcFractals(rates,dateStart,corridorDistance)
          .Select(a => new { a.slope, a.date, stDev = stDev(a.date) })
          .ForEach(a => { if (_timeFrameHeights.ContainsKey(a.date))_timeFrameHeights.Remove(a.date); _timeFrameHeights.Add(a.date, a.stDev); });
        //if (!_timeFrameHeights.ContainsKey(dateMax))
        //  _timeFrameHeights.Add(dateMax, double.MaxValue);

        var dateStop = _timeFrameHeights.DefaultIfEmpty(new KeyValuePair<DateTime,double>(revs[0].StartDate,0.0)).MinBy(a => a.Value).First().Key  ;
        var index = revs.TakeWhile(r => r.StartDate > dateStop).Count();
        SetCorridorStopDate(revs[index]);
        var count = index + corridorDistance;
        if (index > ratesForCorridor.Count* 2.0/ 3) {
          var secondDateStart = revs[index- revs.Count / 3+1].StartDate;
          var tfs = _timeFrameHeights.SkipWhile(kv => kv.Key < secondDateStart);
          SetCOMs(revs, tfs);
        } else if (index < ratesForCorridor.Count / 3) {
          revs.Skip(revs.Count * 2 / 3).Take(1).Select(r => r.StartDate)
            .ForEach(secondDateStart => {
              var tfs = _timeFrameHeights.TakeWhile(kv => kv.Key < secondDateStart);
              SetCOMs(revs, tfs);
            });
        }
        return count;
      };
      return ScanCorridorLazy(revs, scan, GetShowVoltageFunction());
    }

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
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), new Lazy<int>(() => (BarsCountCalc * CorridorDistanceRatio).ToInt()), ShowVoltsByVolatility);
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

    private CorridorStatistics ScanCorridorByRsdMax(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.Reverse().ToArray();
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

    private CorridorStatistics ScanCorridorByRsdFft(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot().ToArray();
      var dist = ratesReversed.Distance();
      //CorridorStDevRatioMax = FttMax = ratesReversed.Select(_priceAvg).FftFrequency(FftReversed).Min(ratesReversed.Length).ToInt();
      Lazy<int> lenghForwardOnly = new Lazy<int>(() => {
        var date = (CorridorStats.Rates.LastOrDefault() ?? new Rate()).StartDate;
        return ratesReversed.TakeWhile(r => r.StartDate >= date).Count();
      });

      var minuteMin = 30;
      var harmonics = CalcHurmonicsAll(ratesReversed, minuteMin).ToList();
      harmonics.SortByLambda(h => -h.Height);
      CorridorStDevRatioMax = harmonics.Sum(h => h.Hours * h.Height) / harmonics.Sum(h => h.Height);

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

      //SetVoltages(ratesForCorridor, ratesReversed);

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
      var halfRatio = ratesDistance / (CorridorDistanceRatio < 10 ? CorridorDistanceRatio.Max(1) : BarsCountCalc / CorridorLength);
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
      var halfRatio = ratesDistance / (CorridorDistanceRatio < 10 ? CorridorDistanceRatio.Max(1) : BarsCountCalc / CorridorLength);
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
      
      var dayRatio = ratesDistance * 1440 / BarsCountCalc.Max(1440);
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
      var ratesReversed = ratesReversedOriginal.TakeWhile(r => r.StartDate >= dateMin).Shrink(CorridorPrice, groupLength).ToArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      var ratesToRegress = new List<double>() { ratesReversed[0] };
      foreach (var rate in ratesReversed.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count / 2).ToArray().Height() > stDev && ratesToRegress.Count > polyOrder * 2) break;
      }
      var correlations = new { corr = 0.0, length = 0 }.IEnumerable().ToList();
      var corr = double.NaN;
      var locker = new object();
      var start = ratesToRegress.Count.Max(polyOrder + 1);
      var corrLength = ratesReversed.Length.Sub(start);
      if (corrLength > 1)
        Partitioner.Create(Enumerable.Range(start, corrLength).ToArray(), true)
          .AsParallel().ForAll(rates => {
            try {
              var prices = new double[rates];
              Array.Copy(ratesReversed, prices, prices.Length);
              var parabola = prices.Regression(polyOrder);
              corr = corr.Cma(5, AlgLib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length).Abs());
              lock (locker) {
                correlations.Add(new { corr, length = prices.Length });
              }
            } catch(Exception exc) {
              Log = exc;
            }
          });
      correlations.Sort((a, b) => a.corr.CompareTo(b.corr));
      if (!correlations.Any()) correlations.Add(new { corr = GetVoltage(RatePrev), length = 1000000 });
      WaveShort.ResetRates(ratesForCorridor.ReverseIfNot().Take(correlations[0].length * groupLength).TakeWhile(r => r.StartDate >= dateMin).ToArray());
      CorridorCorrelation = ratesReversedOriginal.Volatility(_priceAvg, GetPriceMA, UseSpearmanVolatility);
      return ShowVolts(CorridorCorrelation, 1);
    }

    private CorridorStatistics 
      ScanCorridorByRegression2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), CalcCorridorByRegressionCorrelation, GetShowVoltageFunction());
    }

    private int CalcCorridorByRegressionCorrelation(IList<Rate> ratesReversedOriginal) {
      var groupLength = WaveShort.HasRates ? (WaveShort.Rates.Count / 100).Max(1) : 5;
      var dateMin = WaveShort.HasRates ? WaveShort.Rates.LastBC().StartDate.AddMinutes(-BarPeriodInt * groupLength * 2) : DateTime.MinValue;
      var polyOrder = PolyOrder;
      var ratesReversed = ratesReversedOriginal.TakeWhile(r => r.StartDate >= dateMin).Shrink(CorridorPrice, groupLength).ToArray();
      var stDev = ratesReversed.Take(BarsCountCalc).ToArray().StDev();
      var ratesToRegress = new List<double>() { ratesReversed[0] };
      foreach (var rate in ratesReversed.Skip(1)) {
        ratesToRegress.Add(rate);
        if (ratesToRegress.Take(ratesToRegress.Count / 2).ToArray().Height() > stDev) break;
      }
      var correlationsQueue = new { corr = 0.0, length = 0 }.Yield().ToConcurrentQueue();
      var corr = double.NaN;
      var locker = new object();
      var start = ratesToRegress.Count.Max(polyOrder + 1);
      Partitioner.Create(Enumerable.Range(start, ratesReversed.Length.Sub(start)).ToArray(), true)
        .AsParallel().ForAll(rates => {
          try {
            var prices = ratesReversed.CopyToArray(rates);
            var parabola = prices.Regression(polyOrder);
            corr = corr.Cma(5, AlgLib.correlation.pearsoncorrelation(ref prices, ref parabola, prices.Length).Abs());
            correlationsQueue.Enqueue(new { corr, length = prices.Length });
          } catch {
            Debugger.Break();
          }
        });
      var correlations = correlationsQueue.OrderBy(b => b.corr).ToList();
      if (!correlations.Any()) correlations.Add(new { corr = GetVoltage(RatePrev), length = 1000000 });
      return correlations[0].length * groupLength;
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
      var correlations = new { corr = 0.0, length = 0 }.YieldBreak().ToList();
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
      CorridorCorrelation = ratesForCorridor.ReverseIfNot().Take((WaveShort.Rates.Count * 1.5).ToInt()).ToArray().Volatility(_priceAvg, GetPriceMA, UseSpearmanVolatility);
      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
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
    private CorridorStatistics ScanCorridorByHorizontalLineCrossesFft(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();

      var minuteMin = 30;
      var harmonics = CalcHurmonicsAll(ratesReversed, minuteMin).ToList();
      harmonics.SortByLambda(h => -h.Height);
      CorridorStDevRatioMax = harmonics.Sum(h => h.Hours * h.Height) / harmonics.Sum(h => h.Height);
      var heightAvg = harmonics.Average(h => h.Height);
      var harmonic = harmonics.OrderBy(h => h.Height.Abs(heightAvg)).First();
      harmonic.IsAggregate = true;
      var harmonicPosition = (double)harmonics.IndexOf(harmonic);
      var harmonicPositionRatio = harmonicPosition / harmonics.Count;
      GlobalStorage.Instance.ResetGenericList(_harmonics = harmonics);

      WaveShort.ResetRates(ratesReversed.Take(CorridorDistanceRatio.ToInt()).ToArray());

      //SetVoltages(ratesForCorridor, ratesReversed);
      //SetVoltage(ratesReversed[0], 2 * CorridorDistanceRatio / ratesForCorridor.Count);

      //var maCrossesAvg = double.NaN;
      //var maCrossesStDev = ratesReversed.Select(GetVoltage).TakeWhile(v => !v.IsNaN()).ToArray().StDev(out maCrossesAvg);
      //var avg = (ratesForCorridor.Max(GetVoltage) + ratesForCorridor.Select(GetVoltage).SkipWhile(v => v.IsNaN()).Min()) / 2;
      var volts = ratesForCorridor.Select(GetVoltage).SkipWhile(v => v.IsNaN()).ToArray();
      var avg = volts.Average();
      Custom1 = avg * 1000;
      GetVoltageAverage = () => avg;
      GetVoltageHigh = () => volts.AverageByIterations(DistanceIterations).Average();
      

      return WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
    }

    private void SetVoltages(IList<Rate> ratesForCorridor, IList<Rate> ratesReversed) {
      var cmaPeriods = 10;
      var crossAverageIterations = PolyOrder;
      var wavePrices = WaveShort.Rates.Select(_priceAvg).ToArray();
      var crosssesRatio = wavePrices.CrossesAverageRatioByRegression(InPoints(1), crossAverageIterations);
      SetVoltage(ratesReversed[0], GetVoltage(ratesReversed[1]).Cma(cmaPeriods, crosssesRatio));
      if (GetVoltage(ratesReversed[wavePrices.Count()]).IsNaN()) {
        var pricesReversed = ratesReversed.Select(_priceAvg).ToArray();
        ParallelEnumerable.Range(1, ratesForCorridor.Count - wavePrices.Count())
          .ForAll(index => {
            var prices = new double[wavePrices.Count()];
            Array.Copy(pricesReversed, index, prices, 0, prices.Length);
            var cr = prices.CrossesAverageRatioByRegression(InPoints(1), crossAverageIterations);
            SetVoltage(ratesReversed[index], GetVoltage(ratesReversed[index - 1]).Cma(cmaPeriods, cr));
          });
      }
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
        var rates1 = UseRatesInternal(ri => ri.SkipWhile(r => r.StartDate < dateRight).TakeWhile(r => r.StartDate <= dateLeft).Reverse().ToArray());
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
        var rates1 = UseRatesInternal(ri => ri.SkipWhile(r => r.StartDate < dateRight).TakeWhile(r => r.StartDate <= dateLeft).Reverse().ToArray());
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
        var rates1 = UseRatesInternal(ri => ri.SkipWhile(r => r.StartDate < dateRight).TakeWhile(r => r.StartDate <= dateLeft).Reverse().ToArray());
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

    private static T GetStDevUpDown<T>(double[] rates, double[] line, Func<double,double,T> stDevUpDownProjector) {
      double stDevUp, stDevDown;
      GetStDevUpDown(rates, line,  out stDevUp, out stDevDown);
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

    
    public Func<double> GetVoltageHigh = () => 0;
    public Func<double> GetVoltageAverage = () => 0;
    public Func<Rate, double> GetVoltage = r => r.DistanceHistory;
    double VoltageCurrent { get { return  GetVoltage(RateLast); } }
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
        upDownRatiosForAreas.AsParallel().SetRegressionPriceP(PolyOrder, kv => kv.Value.value
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
