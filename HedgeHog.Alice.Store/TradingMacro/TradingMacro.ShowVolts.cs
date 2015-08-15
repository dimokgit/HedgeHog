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
using System.Reactive.Subjects;
using System.ComponentModel;
using System.Collections.Concurrent;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    private CorridorStatistics ShowVoltsByCorrelation() {
      var volts = InPips(WaveShort.Rates.Select(r => r.PriceAvg.Abs(r.PriceCMALast)).StandardDeviation());
      return ShowVolts(volts, 2);
    }
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
        .Select(rates => new { rate = rates[0], angle = AngleFromTangent(rates.Regress(1, _priceAvg).LineSlope().Abs(),()=>CalcTicksPerSecond(CorridorStats.Rates)) })
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
    

    private void SetVoltFuncs() {
      if (GetVoltage(RatesArray[0]).IsNotNaN()) {
        var volts = RatesArray.Select(r => GetVoltage(r)).Where(Lib.IsNotNaN).DefaultIfEmpty().ToArray();
        var voltsAvg = 0.0;
        var voltsStDev = volts.StDev(out voltsAvg);
        GetVoltageAverage = () => voltsAvg - voltsStDev;
        GetVoltageHigh = () => voltsAvg + voltsStDev;
        GetVoltageLow = () => voltsAvg - voltsStDev * 2;
      }
    }
    #region SetCentersOfMass Subject
    object _SetCentersOfMassSubjectLocker = new object();
    ISubject<Action> _SetCentersOfMassSubject;
    ISubject<Action> SetCentersOfMassSubject {
      get {
        lock (_SetCentersOfMassSubjectLocker)
          if (_SetCentersOfMassSubject == null) {
            _SetCentersOfMassSubject = new Subject<Action>();
            _SetCentersOfMassSubject.SubscribeToLatestOnBGThread(exc => Log = exc, ThreadPriority.Lowest);
            //.Latest().ToObservable(new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }))
            //.Subscribe(s => s(), exc => Log = exc);
          }
        return _SetCentersOfMassSubject;
      }
    }
    CorridorStatistics OnSetCentersOfMass() {
      SetCentersOfMassSubject.OnNext(SetCentersOfMass);
      return ShowVoltsNone();
    }
    #endregion
    #region SetBarsCountCalc Subject
    object _SetBarsCountCalcSubjectLocker = new object();
    ISubject<Action> _SetBarsCountCalcSubject;
    ISubject<Action> SetBarsCountCalcSubject {
      get {
        lock (_SetBarsCountCalcSubjectLocker)
          if (_SetBarsCountCalcSubject == null) {
            IObservable<Action> o = null;
            _SetBarsCountCalcSubject = _SetBarsCountCalcSubject.InitBufferedObservable(ref o,exc => Log = exc);
            o
              .ObserveOn(new EventLoopScheduler())
              .Subscribe(a => a());
          }
        return _SetBarsCountCalcSubject;
      }
    }
    void OnSetBarsCountCalc(Action p) {
      SetBarsCountCalcSubject.OnNext(p);
    }
    void OnSetBarsCountCalc() { OnSetBarsCountCalc(ScanRatesLengthByDistanceMin); }
    #endregion

    double _stDevUniformRatio = Math.Sqrt(12);
    void OnRatesArrayChaged_SetVoltsByRsd(double volt) {
      RatesArray.TakeWhile(r => GetVoltage(r).IsNaN()).ForEach(r => SetVoltage(r, volt));
      RatesArray.Reverse<Rate>().TakeWhile(r => GetVoltage(r).IsNaN()).ForEach(r => SetVoltage(r, volt));
    }
    Action OnRatesArrayChaged = () => { };
    IEnumerable<T> GetSenterOfMassStrip<T>(IList<double> rates, double height, int roundOffset, Func<double[], double, double, T> map) {
      var rates2 = rates.SafeArray();
      rates.CopyTo(rates2, 0);
      return rates2.BufferVertical2(r => RoundPrice(r, roundOffset), height, (b, t, c) => new { b, t, c })
        .OrderByDescending(a => a.c)
        .Take(1)
        .Select(a => map(rates2, a.t, a.b));
    }
    private void SetCentersOfMass() {
      return;
      var height = ScanCorridorByStDevAndAngleHeightMin();
      GetSenterOfMassStrip(RatesArray.ToArray(_priceAvg), height, 0, (rates, t, b) => new { rates = UseVoltage ? rates : null, t, b })
        .ForEach(a => {
          CenterOfMassBuy = a.t;
          CenterOfMassSell = a.b;
          if (UseVoltage) {
            var frameLength = VoltsFrameLength;
            double waveCount = a.rates
              .Buffer(frameLength, 1)
              .Where(b => b.Count == frameLength)
              .Select(b => b.LinearSlope().Sign())
              .DistinctUntilChanged()
              .Count();
            //var prices = RatesArray.Select(_priceAvg).Buffer(RatesArray.Count / 2).ToArray();
            //var ratio = prices[1].StDev() / prices[0].StDev();// RatesHeight / StDevByPriceAvg;
            RatesArray.Where(r => GetVoltage(r).IsNaN()).ForEach(r => SetVoltage(r, RatesArray.Count / waveCount / frameLength));
          }
        });
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
