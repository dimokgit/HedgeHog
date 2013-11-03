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
      var ratesCount = CorridorDistance.Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
      var count = ratesInternalReversed.Length - ratesCount;
      Func<Rate, double> value = r => r.PriceWave;
      Func<Rate, double> volatilityValue = r => _priceAvg(r).Abs(value(r));
      Func<IList<Rate>, double> volatility = rates =>
        rates.StDev(value).Div(rates.StDev(_priceAvg));
      if (GetVoltage(ratesInternalReversed[10]).IsNaN()) {
        Log = new Exception("Loading volts.");
        Enumerable.Range(0, count).ToList().ForEach(index => {
          var rates = ratesInternalReversed.CopyToArray(index, ratesCount);
          try {
            SetMAByFtt(rates);
          } catch (Exception exc) {
            return;
          }
          SetVoltage(rates[0], volatility(rates));
        });
        Log = new Exception("Done Loading volts.");
      }
      if (value(RatesArray.Last()).IsNaN()) SetMAByFtt(RatesArray);
      CorridorCorrelation = volatility(WaveShort.Rates);
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
            SetVoltage(rates[0], rates.Volatility(_priceAvg, GetPriceMA, UseSpearmanVolatility));
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
        Func<IList<Rate>, double> calcVolatility = (rates) => {
          var ints = rates.ReverseIfNot().Select(_priceAvg).ToArray().Integral(60).Select(v => new { h = v.Height(), v = v }).ToArray();
          var avgHeigth = 0.0;
          var stDevHeight = ints.Select(v => v.h).ToArray().StDev(out avgHeigth);
          var min = avgHeigth - stDevHeight * 2;
          var max = avgHeigth + stDevHeight * 2;
          return ints.Where(v => v.h.Between(min, max)).Select(v => v.v).ToArray().AsParallel()
            .Select(g => g.RsdNormalized(d => d)).Average() * 100;
        };
        if (GetVoltage(RatesInternal.AsEnumerable().Reverse().ElementAt(10)).IsNaN()) {
          var ratesInternalReversed = RatesInternal.AsEnumerable().Reverse().ToArray();
          var ratesCount = BarsCount.Max(1440.Div(BarPeriodInt).ToInt()).Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
          var count = ratesInternalReversed.Length - ratesCount;
          Log = new Exception("Loading volts.");
          ParallelEnumerable.Range(0, count).ForAll(index => {
            try {
              var rates = ratesInternalReversed.CopyToArray(index, ratesCount);
              SetVoltage(rates[0], calcVolatility(rates));
            } catch {
              Debugger.Break();
              throw;
            }
          });
          Log = new Exception("Done Loading volts.");
        }
        return ShowVolts(calcVolatility(RatesArray), 2);
    }
    IDisposable _t;
    private CorridorStatistics ShowVoltsByStDevByHeight() {
      var averageIterations = 1;
      RatesArray.Select(_priceAvg).ToArray().StDevByRegressoin();
      Func<IList<double>, double> calcVolatility = (rates) =>
        rates.Integral(_integrationPeriod).AsParallel()
          .Select(g => g.StDevByRegressoin()).ToArray()
          .AverageByIterations(-averageIterations).Average()
          .Div(rates.StDevByRegressoin()) * 100;
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
    private CorridorStatistics ShowVoltsByCorridorRsd() {
      Func<IList<double>, double> calcVolatility = (rates) => rates.RsdNormalized();
      if (_t == null && GetVoltage(RatesInternal.AsEnumerable().Reverse().ElementAt(10)).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = RatesInternal.ReverseIfNot();
          var ratesInternalReversed = RatesInternal.Select(_priceAvg).Reverse().ToArray();
          var ratesCount = CorridorDistance;
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
      return ShowVolts(calcVolatility(WaveShort.Rates.Select(_priceAvg).ToArray()), 3);
    }
    private CorridorStatistics ShowVoltsByCorridorRsdI() {
      Func<IList<double>, double> calcVolatility = (rates) => rates.RsdIntegral(60.Div(BarPeriodInt).ToInt());
      if (_t == null && GetVoltage(RatesInternal.AsEnumerable().Reverse().ElementAt(10)).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = RatesInternal.ReverseIfNot();
          var ratesInternalReversed = RatesInternal.Select(_priceAvg).Reverse().ToArray();
          var ratesCount = CorridorDistance;
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
      return ShowVolts(calcVolatility(WaveShort.Rates.Select(_priceAvg).ToArray()), 3);
    }
    private CorridorStatistics ShowVoltsByCorridorStDevIR() {
      Func<IList<double>, double> calcVolatility = (rates) => InPips(rates.Integral(60.Div(BarPeriodInt).ToInt(), values => values.SafeArray().StDevByRegressoin()).Average());
      if (_t == null && GetVoltage(RatesInternal.AsEnumerable().Reverse().ElementAt(10)).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = RatesInternal.ReverseIfNot();
          var ratesInternalReversed = RatesInternal.Select(_priceAvg).Reverse().ToArray();
          var ratesCount = CorridorDistance;
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
      return ShowVolts(calcVolatility(WaveShort.Rates.Select(_priceAvg).ToArray()), 3);
    }

    private CorridorStatistics ShowVoltsByStDevSumRatio() {
      Func<IList<double>,IList<double>, double> calcVolatility = (ratesSmall,ratesBig) => {
        var stDevReg = ratesSmall.StDevByRegressoin();
        var stDevPrice = ratesSmall.StDev();
        return (stDevReg + stDevPrice) / ratesBig.StDevByRegressoin();
      };
      if (_t == null && GetVoltage(RatesInternal.AsEnumerable().Reverse().ElementAt(10)).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = RatesInternal.ReverseIfNot();
          var ratesInternalReversed = RatesInternalReversedOriginal.Select(_priceAvg).ToArray();
          var ratesCount = CorridorDistanceRatio.ToInt();
          var count = BarsCount.Min(ratesInternalReversed.Length - ratesCount);
          Log = new Exception("Loading volts.");
          Enumerable.Range(0, count).ToList().ForEach(index => {
            var ratesSmall = ratesInternalReversed.CopyToArray(index, ratesCount);
            var ratesBig = ratesInternalReversed.CopyToArray(index, BarsCount);
            SetVoltage(RatesInternalReversedOriginal[index], calcVolatility(ratesSmall, ratesBig));
          });
          _t = null;
          Log = new Exception("Done Loading volts.");
        });
      }
      return ShowVolts(calcVolatility(WaveShort.Rates.Select(_priceAvg).ToArray(), RatesArray.Select(_priceAvg).ToArray()), 1);
    }
  }
}
