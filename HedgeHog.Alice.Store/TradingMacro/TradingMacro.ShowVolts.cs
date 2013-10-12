using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using HedgeHog.Bars;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Threading;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    CorridorStatistics ShowVoltsByStDevPercentage() {
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      var middle = WaveShort.Rates.Average(_priceAvg);
      var levelUp = middle + corridor.StDevByHeight;
      var levelDown = middle - corridor.StDevByHeight;
      var prices = RatesArray.Select(_priceAvg).ToArray();
      var stDevIn = prices.Where(p => p.Between(levelDown, levelUp)).ToArray().StDev();
      var stDevOut = prices.Where(p => !p.Between(levelDown, levelUp)).ToArray().StDev();
      var stDevRatio = GetVoltage(RatePrev).Cma(prices.Length / 100.0, stDevOut.Percentage(stDevIn));
      SetVoltage(RateLast, stDevRatio);
      var voltageAvg = RatesArray.Select(GetVoltage).SkipWhile(v => v.IsNaN()).Average();
      GetVoltageAverage = () => voltageAvg;
      return corridor;
    }
    private CorridorStatistics ShowVoltsByVolatility() {
      var ratesInternalReversed = RatesInternal.AsEnumerable().Reverse().ToArray();
      var ratesCount = 1440.Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
      var count = ratesInternalReversed.Length - ratesCount;
      if (GetVoltage(ratesInternalReversed[10]).IsNaN()) {
        //Log = new Exception("Loading volts.");
        Enumerable.Range(0, count).ToList().ForEach(index => {
          var rates = new Rate[ratesCount];
          Array.Copy(ratesInternalReversed, index, rates, 0, rates.Length);
          rates.SetCma((p, r) => r.PriceAvg, PriceCmaLevels, PriceCmaLevels);
          SetVoltage(rates[0], rates.Volatility(_priceAvg, GetPriceMA));
        });
        //Log = new Exception("Done Loading volts.");
      }
      CorridorCorrelation = ratesInternalReversed.Take(ratesCount).ToArray().Volatility(_priceAvg, GetPriceMA);
      return ShowVolts(CorridorCorrelation, 1);
    }

    private CorridorStatistics ShowVoltsByHourlyStDevAvg() {
      if (false) {
        var ratesInternalReversed = RatesInternal.AsEnumerable().Reverse().ToArray();
        var ratesCount = 1440.Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
        var count = ratesInternalReversed.Length - ratesCount;
        if (GetVoltage(ratesInternalReversed[10]).IsNaN()) {
          //Log = new Exception("Loading volts.");
          Enumerable.Range(0, count).ToList().ForEach(index => {
            var rates = new Rate[ratesCount];
            Array.Copy(ratesInternalReversed, index, rates, 0, rates.Length);
            rates.SetCma((p, r) => r.PriceAvg, PriceCmaLevels, PriceCmaLevels);
            SetVoltage(rates[0], rates.Volatility(_priceAvg, GetPriceMA));
          });
          //Log = new Exception("Done Loading volts.");
        }
      }
      var a = RatesArray.AsEnumerable().Reverse().ToArray();
      CorridorCorrelation = InPips(a.Integral(60).Select(g => g.StDev(_priceAvg)).ToArray().AverageInRange(2, -2).Average());
      return ShowVolts(CorridorCorrelation, 1);
    }

    int _integrationPeriod { get { return CorridorHeightMax.ToInt(); } }

    private CorridorStatistics ShowVoltsByHourlyRsdAvg() {
      var averageIterations = 2;
      Func<IList<Rate>, double> calcVolatility = (rates) =>
        rates.ReverseIfNot().Integral(_integrationPeriod).AsParallel().Select(g => g.RsdNormalized(_priceAvg)).ToArray().AverageInRange(averageIterations, -averageIterations).Average() * 100;
      if (GetVoltage(RatesInternal.AsEnumerable().Reverse().ElementAt(10)).IsNaN()) {
        var ratesInternalReversed = RatesInternal.AsEnumerable().Reverse().ToArray();
        var ratesCount = BarsCount.Max(1440.Div(BarPeriodInt).ToInt()).Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
        var count = ratesInternalReversed.Length - ratesCount;
        Log = new Exception("Loading volts.");
        ParallelEnumerable.Range(0, count).ForAll(index => {
          var rates = ratesInternalReversed.CopyToArray(index, ratesCount);
          SetVoltage(rates[0], calcVolatility(rates));
        });
        Log = new Exception("Done Loading volts.");
      }
      return ShowVolts(CorridorCorrelation = calcVolatility(RatesArray), 2);
    }
    IDisposable _t;
    private CorridorStatistics ShowVoltsByStDevByHeight() {
      var averageIterations = 1;
      RatesArray.Select(_priceAvg).ToArray().StDevByRegressoin();
      Func<IList<double>, double> calcVolatility = (rates) =>
        InPips(rates.Integral(_integrationPeriod).AsParallel().Select(g => g.StDevByRegressoin()).ToArray().AverageByIterations(-averageIterations).Average());
      if (_t == null && GetVoltage(RatesInternal.AsEnumerable().Reverse().ElementAt(10)).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = RatesInternal.ReverseIfNot();
          var ratesInternalReversed = RatesInternal.Select(_priceAvg).Reverse().ToArray();
          var ratesCount = BarsCount.Max(1440.Div(BarPeriodInt).ToInt()).Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
          var count = BarsCount.Min(ratesInternalReversed.Length - ratesCount);
          Log = new Exception("Loading volts.");
          Enumerable.Range(0, count).ToList().ForEach(index => {
            var rates = ratesInternalReversed.CopyToArray(index, ratesCount);
            SetVoltage(RatesInternalReversedOriginal[index], calcVolatility(rates));
          });
          _t = null;
          Log = new Exception("Done Loading volts.");
        });
      }
      return ShowVolts(CorridorCorrelation = calcVolatility(RatesArray.Select(_priceAvg).Reverse().ToArray()), 2);
    }
  }
}
