using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.DataVisualization.Charting;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    bool isInFlight;
    private CorridorStatistics ScanCorridorByFftMA(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ratesReversed = ratesForCorridor.ReverseIfNot();
      Func<double[], double[], double> calcVolts = (s1, s2) =>
        InPips(MathNet.Numerics.Statistics.Statistics.StandardDeviation(s1.Zip(s2, (d1, d2) => d1 - d2).ToArray()));
      if (!isInFlight) {
        isInFlight = true;
        var barsCount = BarsCountCalc;
        TaskPoolScheduler.Default.Schedule(() => {
          UseRatesInternal(ri => ri.CopyLast(barsCount * 2).Reverse<Rate>().Skip(1))
            .SkipWhile(rate => GetVoltage(rate).IsNotNaN())
            .Buffer(barsCount, 1)
            .TakeWhile(rates => rates.Count == barsCount)
            .AsParallel()
            .ForEach(rates => NewMethod(rates, rs => rs[0], null, calcVolts));
          isInFlight = false;
        });
      }
      var voltsAll = ratesForCorridor.Select(GetVoltage).Where(Lib.IsNotNaN).ToArray();
      OnGeneralPurpose(() => {
        var vh = voltsAll.AverageByIterations(VoltsHighIterations).DefaultIfEmpty().Average();
        GetVoltageHigh = () => vh;
        var va = voltsAll.AverageByIterations(VoltsAvgIterations).DefaultIfEmpty().Average();
        GetVoltageAverage = () => va;
      }, IsInVitualTrading);
      var priceMAs = RatesArray.ToArray(GetPriceMA);
      LineMA = priceMAs.Regression(1, (coefs, line) => line);
      SetVoltage(RatesArray.Last(), calcVolts(priceMAs, RatesArray.ToArray(_priceAvg)));
      var count0 = RatesArray.Select(GetPriceMA)
        .Zip(LineMA, (m, l) => m.SignUp(l) )
        .ToArray();
      var count1 = count0
        .Zip(count0.Skip(1), (s1, s2) => s1 != s2)
        .Select((b, i) => new { b, i })
        .Where(a => a.b)
        .ToArray(a => a.i);
      var count = count1
        .Zip(count1.Skip(1), (i1, i2) => i2 - i1);
      Func<IList<Rate>, int> scan = rates => {
        return count.Max();
      };
      return ScanCorridorLazy(ratesForCorridor.ReverseIfNot(), scan);
    }

    private double[] NewMethod(IList<Rate> rates, Func<IList<Rate>, Rate> rate, Action<Rate, double> setMAs,Func<double[], double[], double> calcVolts) {
      List<double> mas = new List<double>(rates.Count);
      Action<Rate, double, Action<Rate, double>> setMA = (r, d, a) => { mas.Add(d); if (a != null)a(r, d); };
      SetMAByFtt(rates, _priceAvg, (r, d) => setMA(r, d, setMAs), PriceCmaLevels);
      var priceMAs = mas.ToArray();
      var lineMA = new double[0];
      priceMAs.Regression(1, (coefs, line) => lineMA = line);
      SetVoltage(rate(rates), calcVolts(priceMAs, rates.ToArray(_priceAvg)));
      return lineMA;
    }
  }
}
