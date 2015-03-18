using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using HedgeHog.Bars;
using System.Collections.Concurrent;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    public void SetCorridorStartDateToNextWave(bool backwards = false) {
      var waveWidth = BarPeriod == BarsPeriodType.m1 ? 60 : 3600.Div(TicksPerSecondAverage).ToInt();
      var indexOffset = waveWidth / 6;
      var waves = RatesArray.Reverse<Rate>().ToArray().Extreams(waveWidth, r=>r.PriceCMALast, r => r.StartDate);
      var startIndex = CorridorStats.Rates.Count - 1;
      var waves2 = !backwards
        ? waves.SkipWhile(w => w.Item1 < startIndex + indexOffset).Take(1)
        : waves.TakeWhile(w => w.Item1 < startIndex - indexOffset).TakeLast(1);
      waves2
        .ForEach(t => CorridorStartDate = RatesArray[RatesArray.Count - t.Item1].StartDate);
    }
    private IList<int> MACrosses(IList<Rate> rates, int frame) {
      var rates1 = rates.Zip(rates.Skip(1), (f, s) => new { f = f.PriceAvg, s = s.PriceAvg, ma = f.PriceCMALast }).ToArray();
      var crosses = new int[0].ToConcurrentQueue();
      Partitioner.Create(Enumerable.Range(0, rates.Count - frame).ToArray(), true).AsParallel()
        .ForAll(i => {
          var rates2 = rates1.ToArray(frame);
          Array.Copy(rates1, i, rates2, 0, frame);
          crosses.Enqueue(rates2.Count(r => r.f.Sign(r.ma) != r.s.Sign(r.ma)));
        });
      return crosses.ToArray();
    }
    private CorridorStatistics ScanCorridorByHorizontalLineCrosses1(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
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
  }
}
