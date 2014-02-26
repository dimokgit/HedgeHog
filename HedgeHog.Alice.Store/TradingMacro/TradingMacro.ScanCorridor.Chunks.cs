using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Text;
using HedgeHog;
using HedgeHog.Bars;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    private static int CalcCorridorByMinWaveHeight(Rate[] rates, double minHeight, int wavesCount) {
      for (var i = wavesCount; i < rates.Length; i += wavesCount) {
        if (Chunk(rates, i, wavesCount).Min() > minHeight)
          return i;
      }
      return rates.Length;
    }


    private CorridorStatistics ScanCorridorBySplitHeights(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), CalcCorridorBySplitHeights, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySplitHeights2(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), CalcCorridorBySplitHeights2, GetShowVoltageFunction());
    }
    private CorridorStatistics ScanCorridorBySplitHeights3(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), CalcCorridorBySplitHeights3, GetShowVoltageFunction());
    }
    private int CalcCorridorBySplitHeights3(IList<Rate> rates) {
      var ratesReversedOriginal = rates.ReverseIfNot().SafeArray();
      var stDev = StDevByPriceAvg.Avg(StDevByHeight);
      Func<int> getDefault = () => {
        var startDate = CorridorStats.Rates.TakeLast(1).Select(r => r.StartDate).DefaultIfEmpty().First();
        return ratesReversedOriginal.TakeWhile(r => r.StartDate >= startDate).Count();
      };
      return RunSplits(rates, _priceAvg, CorrelationMinimum.ToInt()).IfEmpty(getDefault).Max();
    }
    private static IEnumerable<int> RunSplits(IList<Rate> rates, Func<Rate, double> price, int startIndex) {
      var ratesReversedOriginal = rates.ReverseIfNot().Select(price).SafeArray();
      return (
        from i in Partitioner.Create(Enumerable.Range(startIndex, 10000)
          .TakeWhile(i=> i < ratesReversedOriginal.Length-1).ToArray(), true).AsParallel()
        from a in new {
          first = ratesReversedOriginal.CopyToArray(i / 2),
          second = ratesReversedOriginal.CopyToArray(i / 2, i / 2)
        }.Yield()
        from b in new {
          index = i,
          splits = new {
            first = new { stDev = a.first.StDev() },
            second = new { stDev = a.second.StDev() }
          }
        }.Yield()
        select new { b, ok = b.splits.second.stDev / 2 > b.splits.first.stDev })
        .SkipWhile(b => !b.ok)
        .TakeWhile(b => b.ok)
        .Select(b => b.b.index);
    }
    private int CalcCorridorBySplitHeights(IList<Rate> rates) {
      var ratesReversedOriginal = rates.ReverseIfNot().SafeArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      for (var i = 2; i < ratesReversedOriginal.Length; i += 2)
        if (ratesReversedOriginal.CopyToArray(i / 2, i / 2).Height()
          .Min(ratesReversedOriginal.SafeArray().CopyToArray(i / 2).Height()) > stDev)
          return i;
      return ratesReversedOriginal.Length;
    }
    private int CalcCorridorBySplitHeights2(IList<Rate> ratesOriginal) {
      var rates = ratesOriginal.ReverseIfNot().SafeArray();
      var stDev = StDevByPriceAvg.Max(StDevByHeight);
      var hopsCount = PolyOrder;
      var lengthMin = 0;
      Task.Factory.StartNew(() => {
        for (var i = hopsCount; i < rates.Length; i += hopsCount)
          if (Chunk(rates, i, hopsCount - 1).Min() > stDev) {
            lengthMin = i;
            WaveShortLeft.ResetRates(rates.Take(lengthMin).ToArray());
            break;
          };
      });
      var hops = new List<int>();
      for (var i = hopsCount; i < rates.Length; i += hopsCount) hops.Add(i);
      var lengthMax = double.NaN;
      Parallel.ForEach(Partitioner.Create(hops.ToArray(), true), new ParallelOptions() { MaxDegreeOfParallelism = IsInVitualTrading ? -1 : 1 }
        , (i, pls) => {
        if (Chunk(rates, i, hopsCount).Min() > stDev) {
          lengthMax = lengthMax.Min(i);
          pls.Stop();
        }
      });
      if (lengthMin == 0) WaveShortLeft.ResetRates(rates);
      return lengthMax.IfNaN(rates.Length).ToInt();
    }

    private static double[] Chunk(Rate[] rates, int chunksLength, int chunksCount) {
      var hops = Enumerable.Range(0, chunksCount).ToArray();
      var chunk = chunksLength / chunksCount;
      var chunks = hops.Select(hop => hop * chunk).ToArray();
      var heights = chunks.Select(start => rates.CopyToArray(start, chunk).Height()).ToArray();
      return heights;
    }
  }
}

