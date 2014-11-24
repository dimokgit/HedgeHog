using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;
using HedgeHog.Bars;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Threading;
using System.Reactive.Concurrency;
using System.Threading;
using System.Reactive.Linq;
using System.Reactive.Disposables;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    CorridorStatistics ShowVoltsByFractalDensity() {
      var volts = !FractalTimes.Any() ? double.NaN
        : (from range in new { dateMin = FractalTimes.Min(), dateMax = FractalTimes.Max() }.Yield()
           let rates = RatesArray.SkipWhile(r => r.StartDate <= range.dateMin).TakeWhile(r => r.StartDate <= range.dateMax).ToArray()
           select rates.Length.Div(FractalTimes.Count()).ToInt()).First();
      RatesArray.TakeWhile(r => GetVoltage(r).IsNaN())
        .ForEach(r => SetVoltage(r, volts));
      return ShowVolts(volts, 2);
    }
    CorridorStatistics ShowVoltsByFrameAngle() {
      var frameLength = CorridorDistance;
      RatesInternal.AsEnumerable().Reverse().Take(RatesArray.Count + CorridorDistance * 2)
        .Integral(frameLength)
        .AsParallel()
        .Select(rates => new { rate = rates[0], angle = AngleFromTangent(rates.Regress(1, _priceAvg).LineSlope().Abs()) })
        .ToArray()
        .OrderByDescending(a => a.rate.StartDate)
        .Integral(frameLength * 2)
        .ForEach(b => SetVoltage(b[0].rate, b.Average(a => a.angle)));
      return ShowVolts(GetVoltage(RateLast), 2);
    }
    CorridorStatistics ShowVoltsByVolume() {
      RatesArray.AsEnumerable().Reverse().TakeWhile(r => GetVoltage(r).IsNaN())
        .ForEach(rate => { SetVoltage(rate, rate.Volume); });
      return ShowVolts(RateLast.Volume, 2);
    }
    CorridorStatistics ShowVoltsByRsd() {
      return ShowVolts(RatesRsd, 2);
    }
    CorridorStatistics ShowVoltsByHarmonicMin() {
      RatesInternal.Select((r, i) => new { r, i })
        .Skip(CorridorDistance - 1)
        .SkipWhile(r => !GetVoltage(r.r).IsNaN())
        .Select(r => r.i)
        .ForEach(endIndex => {
          var a = RatesInternal.CopyToArray(endIndex - CorridorDistance + 1, CorridorDistance);
          var hour = FastHarmonics(a, 29, 30).First().Hours;
          SetVoltage(RatesInternal[endIndex], hour);
        });
      //RatesArray.AsEnumerable().Reverse().TakeWhile(r => GetVoltage(r).IsNaN())
      //  .ForEach(rate => { SetVoltage(rate, HarmonicMin); });
      return ShowVolts(HarmonicMin, 3);
    }
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
    static double CalcVolatility(IList<Rate> rates, Func<Rate, double> getValue, Func<Rate, double> line) {
      return CalcVolatility(rates.ToArray(getValue), rates.ToArray(line));
    }
    static double CalcVolatility(IList<double> values, IList<double> line = null) {
      line = line ?? values.Regression(1, (c, l) => l);
      var threes = line.Buffer(3, 1).Where(b => b.Count == 3).ToArray();
      return values.Skip(1).SkipLast(1)
        .Zip(line.Skip(1).SkipLast(1), (z1, z2) => z1 - z2)
        .Zip(threes, (abs, b3) => (abs * MathExtensions.Offset(b3.Regress(1).LineSlope(), 0.01)).Abs())
        .ToArray()
        .AverageByIterations(-1)
        .StDev();
    }

    EventLoopScheduler _setVoltsScheduler = new EventLoopScheduler();
    CompositeDisposable _setVoltsSubscriber = null;
    private CorridorStatistics ShowVoltsByVolatility() {
      if (WaveShort.HasRates == false)
        WaveShort.Rates = RatesArray.TakeLast(CorridorDistance).Reverse().ToArray();
      var corridor = WaveShort.Rates.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
      var line = corridor.Coeffs.RegressionLine(corridor.Rates.Count);
      Func<Rate, double> priceFunc = r => r.VoltageLocal;
      Action<Rate, double> priceAction = (r, v) => r.VoltageLocal = v;
      Func<Rate, double> priceFunc2 = r => r.VoltageLocal2;
      Action<Rate, double> priceAction2 = (r, v) => r.VoltageLocal2 = v;
      Func<Rate, double> priceFuncSlow = r => r.VoltageLocal3;
      Action<Rate, double> priceActionSlow = (r, v) => r.VoltageLocal3 = v;
      Func<Rate, double> priceFuncFast = r => r.VoltageLocal0[0];
      Action<Rate, double> priceActionFast = (r, v) => r.VoltageLocal0 = new[] { v };
      var frameLength = VoltsFrameLength == 0 ? CorridorDistance : VoltsFrameLength;
      var cmaLevelsFast = GetFftHarmonicsByRatesCountAndRatio(frameLength, PriceFftLevelsFast);
      var cmaLevelsSlow = GetFftHarmonicsByRatesCountAndRatio(frameLength, PriceFftLevelsSlow);

      //var corridorRates = RatesArray.TakeLast(frameLength)/*.Reverse()*/.ToArray();
      //SetMAByFtt(corridorRates, _priceAvg, priceActionFast, cmaLevelsFast);
      //SetMAByFtt(corridorRates, _priceAvg, priceActionSlow, cmaLevelsSlow);
      //SetVoltage(corridorRates[0], InPips(CalcVolatility(corridorRates, priceFuncFast, priceFuncSlow)));
      {
        var corridorRates = RatesArray.TakeLast(frameLength + PriceFftLevelsFast.Max(PriceFftLevelsSlow)).ToArray();
        var trimaFast = corridorRates.GetTrima(PriceFftLevelsFast).Reverse().Take(frameLength).ToArray();
        var trimaSlow = corridorRates.GetTrima(PriceFftLevelsSlow).Reverse().Take(frameLength).ToArray();
        SetVoltage(corridorRates.Last(), InPips(CalcVolatility(trimaFast, trimaSlow)));
      }
      if (_setVoltsSubscriber == null || _setVoltsSubscriber.IsDisposed) {
        var voltRates0 = UseRatesInternal(ri => ri.TakeLast(BarsCount * 2).ToArray());
        var voltRates = voltRates0
          .Reverse()
          .Zip(voltRates0.GetTrima(PriceFftLevelsFast).Reverse(), (r, f) => new { r, f })
          .Zip(voltRates0.GetTrima(PriceFftLevelsSlow).Reverse(), (a, s) => new { a.r, a.f, s });
        _setVoltsSubscriber = voltRates
          .SkipWhile(a => GetVoltage(a.r).IsNotNaN())
          .Buffer(frameLength, 1)
          .TakeWhile(b => b.Count == frameLength)
          .Where(b => GetVoltage(b[0].r).IsNaN())
          .ToObservable(_setVoltsScheduler)
          .Do(b => {
            //SetMAByFtt(b, _priceAvg, priceAction, cmaLevelsFast);
            //SetMAByFtt(b, _priceAvg, priceAction2, cmaLevelsSlow);
            //var v = CalcVolatility(b, priceFunc, priceFunc2);
            var trimaFast = b.Select(a => a.f).ToArray();
            var trimaSlow = b.Select(a => a.s).ToArray();
            var v = CalcVolatility(trimaFast, trimaSlow);
            SetVoltage(b[0].r, InPips(v));
          })
          .DefaultIfEmpty()
          .SubscribeOn(TaskPoolScheduler.Default)
          .LastAsync().Subscribe(_ => {
            SetVoltFuncs();
          }) as CompositeDisposable;
      }
      return corridor;

      var ratesInternalReversed = UseRatesInternal(ri => ri.AsEnumerable().Reverse().ToArray());
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
            SetMAByFtt(rates, _priceAvg, (rate, v) => rate.PriceWave = v, PriceCmaLevels);
          } catch (Exception exc) {
            return;
          }
          SetVoltage(rates[0], volatility(rates));
        });
        Log = new Exception("Done Loading volts.");
      }
      if (value(RatesArray.Last()).IsNaN()) SetMAByFtt(RatesArray, _priceAvg, (rate, v) => rate.PriceWave = v, PriceCmaLevels);
      CorridorCorrelation = volatility(WaveShort.Rates);
      return corridor;
    }

    private static int GetFftHarmonicsByRatesCountAndRatio(int frameLength, double fftHarmsRatio) {
      return (frameLength * fftHarmsRatio/100).ToInt();
    }

    private void SetVoltFuncs() {
      if (GetVoltage(RatesArray[0]).IsNotNaN()) {
        var volts = RatesArray.Select(r => GetVoltage(r)).Where(Lib.IsNotNaN).DefaultIfEmpty().ToArray();
        var voltsAvg = 0.0;
        var voltsStDev = volts.StDev(out voltsAvg);
        GetVoltageAverage = () => voltsAvg - voltsStDev;
        GetVoltageHigh = () => voltsAvg + voltsStDev;
      }
    }

    private CorridorStatistics ShowVoltsByHourlyStDevAvg() {
      if (false) {
        var ratesInternalReversed = UseRatesInternal(ri => ri.AsEnumerable().Reverse().ToArray());
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
      if (GetVoltage(UseRatesInternal(ri => ri.AsEnumerable().Reverse().ElementAt(10))).IsNaN()) {
        var ratesInternalReversed = UseRatesInternal(ri => ri.AsEnumerable().Reverse().ToArray());
        var ratesCount = BarsCountCalc.Max(1440.Div(BarPeriodInt).ToInt()).Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
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
      if (_t == null && GetVoltage(UseRatesInternal(ri => ri.AsEnumerable().Reverse().ElementAt(10))).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = UseRatesInternal(ri => ri.ReverseIfNot());
          var ratesInternalReversed = UseRatesInternal(ri => ri.Select(_priceAvg).Reverse().ToArray());
          var ratesCount = BarsCountCalc.Max(1440.Div(BarPeriodInt).ToInt()).Div(BarPeriodInt).ToInt();// BarsCountCalc.Value;
          var count = BarsCountCalc.Min(ratesInternalReversed.Length - ratesCount);
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
      if (_t == null && GetVoltage(UseRatesInternal(ri => ri.AsEnumerable().Reverse().ElementAt(10))).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = UseRatesInternal(ri => ri.ReverseIfNot());
          var ratesInternalReversed = RatesInternalReversedOriginal.Select(_priceAvg).ToArray();
          var ratesCount = CorridorDistance;
          var count = BarsCountCalc.Min(ratesInternalReversed.Length - ratesCount);
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
      if (_t == null && GetVoltage(UseRatesInternal(ri => ri.AsEnumerable().Reverse().ElementAt(10))).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = UseRatesInternal(ri => ri.ReverseIfNot());
          var ratesInternalReversed = UseRatesInternal(ri => ri.Select(_priceAvg).Reverse().ToArray());
          var ratesCount = CorridorDistance;
          var count = BarsCountCalc.Min(ratesInternalReversed.Length - ratesCount);
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
      if (_t == null && GetVoltage(UseRatesInternal(ri => ri.AsEnumerable().Reverse().ElementAt(10))).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = UseRatesInternal(ri => ri.ReverseIfNot());
          var ratesInternalReversed = UseRatesInternal(ri => ri.Select(_priceAvg).Reverse().ToArray());
          var ratesCount = CorridorDistance;
          var count = BarsCountCalc.Min(ratesInternalReversed.Length - ratesCount);
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
      Func<IList<double>, IList<double>, double> calcVolatility = (ratesSmall, ratesBig) => {
        var stDevReg = ratesSmall.StDevByRegressoin();
        var stDevPrice = ratesSmall.StDev();
        return (stDevReg + stDevPrice) / ratesBig.StDevByRegressoin();
      };
      if (_t == null && GetVoltage(UseRatesInternal(ri => ri.AsEnumerable().Reverse().ElementAt(10))).IsNaN()) {
        _t = new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }).Schedule(() => {
          var RatesInternalReversedOriginal = UseRatesInternal(ri => ri.ReverseIfNot());
          var ratesInternalReversed = RatesInternalReversedOriginal.Select(_priceAvg).ToArray();
          var ratesCount = CorridorDistanceRatio.ToInt();
          var count = BarsCountCalc.Min(ratesInternalReversed.Length - ratesCount);
          Log = new Exception("Loading volts.");
          Enumerable.Range(0, count).ToList().ForEach(index => {
            var ratesSmall = ratesInternalReversed.CopyToArray(index, ratesCount);
            var ratesBig = ratesInternalReversed.CopyToArray(index, BarsCountCalc);
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
