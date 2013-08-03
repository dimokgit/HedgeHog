using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
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

  }
}
