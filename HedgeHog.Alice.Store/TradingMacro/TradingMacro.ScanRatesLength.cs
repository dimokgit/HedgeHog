using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    void ScanRatesLengthByStDevMin() {
      var ratesInternal = UseRatesInternal(ri => ri.Reverse().ToList(_priceAvg), 5000);
      var countMin = ratesInternal.Count;
      var start = BarsCount;
      var end = RatesInternal.Count - 1;
      var func = MonoidsCore.ToFunc(true, 0, 0.0, (ok, l, sd) => new { ok, l, sd });
      var last = func(false, 0, 0.0);
      var getCount = Lib.GetIterator((_start, _end, _isOk, _nextStep) => {
        var _last = func(false, 0, 0.0);
        var sdMin = InPoints(WaveStDevRatio);
        return Lib.IteratonSequence(_start, _end, _nextStep)
        .Select(i => {
          var rates = ratesInternal.GetRange(0, i.Min(countMin));
          var stDev = rates.HeightByRegressoin();
          var x = func(stDev > sdMin, rates.Count, stDev);
          if (_last.sd < stDev) _last = x;
          return x;
        })
        .SkipWhile(a => _isOk(a.ok))
        .Take(1)
        .IfEmpty(() => _last);
      });
      Func<bool, bool> isOk = b => !b;
      var divider = 100.0;
      Func<int, int> nextStep = i => Lib.IteratonSequenceNextStep(i, divider);
      while (true) {
        var c = getCount(start, end, isOk, nextStep).Single().l;
        if (nextStep(c).Abs() <= 1) {
          BarsCountCalc = c;
          break;
        }
        divider *= -2;
        start = c; end = start + nextStep(c) * 3;
        if (divider < 0) { isOk = b => b; } else { isOk = b => !b; }
      }
    }
    static bool IsTresholdOk(double value, double treshold) {
      return treshold >= 0 ? value >= treshold : value < -treshold;
    }
    static bool IsTresholdAbsOk(double value, double treshold) {
      return treshold >= 0 ? value.Abs() >= treshold : value.Abs() < -treshold;
    }
    delegate T IterationLooperDelegate<T>(int start, int end, int loop);
    IterationLooperDelegate<T> GetLoper<T>(Func<int, int, int> nextStep, Func<int, T> looper, Func<IEnumerable<T>, T> counter) {
      return (start, end, loop) =>
        counter(Lib.IteratonSequence(start, end, i => nextStep(i, loop)).Select(looper));
    }
    void ScanRatesLengthByRelativeStDev() {
      if (!this._isCorridorStopDateManual)
        return;
      var ratesInternal = UseRatesInternal(ri => ri.Reverse().Select(_priceAvg).ToList(), 5000);
      var lopper = MonoidsCore.ToFunc(0, i => {
        var rates = ratesInternal.GetRange(0, i.Min(ratesInternal.Count));
        var rsd = rates.Height() / rates.StandardDeviation();
        return new { rates.Count, rsd };
      });
      var start = BarsCount;
      var end = BarsCountCount() - 1;
      var nextStep = Lib.IteratonSequencePower(end, IteratorScanRatesLengthLastRatio);
      var corridor = GetLoper(nextStep, lopper, cors => cors
        .OrderBy(a => a.rsd)
        .First());
      var corridors = IteratorLopper(start, end, nextStep, corridor, c => c.Count);
      var lastRsd = corridors.OrderBy(x => x.rsd)
        .Do(x => BarsCountCalc = x.Count)
        .First().rsd;
      OnRatesArrayChaged = () => OnRatesArrayChaged_SetVoltsByRsd(lastRsd);
    }
    void ScanRatesLengthByStDevMin2() {
      if (CorridorStartDate.HasValue) {
        //BarsCountCalc = (CorridorStats.Rates.Count * 1.1).Max(BarsCountCalc).Min(BarsCountCount()).ToInt();
        if (CorridorStats.Rates.Count * 1.05 > RatesArray.Count)
          SetCorridorStartDateToNextWave(true);
        return;
      }
      var rateA = new { StartDate = DateTime.Now, PriceAvg = 0.0 };
      Func<DateTime, Rate, Rate, bool> isBetweenRates = (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate);
      var isBetween = MonoidsCore.ToFunc(DateTime.Now, rateA, rateA, (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate));
      var startIndex = BarsCount;
      var prices = UseRatesInternal(ri => ri.Reverse().Select(r => new { r.StartDate, r.PriceAvg }).ToList());
      var isTicks = BarPeriod == BarsPeriodType.t1;
      if (isTicks) {
        var startIndexDate = prices[startIndex].StartDate;
        prices = prices.GroupAdjacentTicks(TimeSpan.FromMinutes(1), a => a.StartDate, g => new { StartDate = g.Key, PriceAvg = g.Average(r => r.PriceAvg) }).ToList();
        startIndex = prices.FuzzyFind(startIndexDate, isBetween);
      }
      var getCount = Lib.GetIterator((_start, _end, _isOk, _nextStep) => {
        var last = new { i = 0, d = DateTime.Now, ok = false, std = double.MinValue };
        return Lib.IteratonSequence(_start, _end, _nextStep)
          .Select(i => {
            //InPips(prices.GetRange(0, i.Min(prices.Count)).StandardDeviation()).Yield()
            var range = prices.GetRange(0, i.Min(prices.Count));
            var std = InPips(range.ToArray(r => r.PriceAvg).HeightByRegressoin());
            return new { i, d = range.Last().StartDate, ok = std <= RatesStDevMinInPips, std };
          })
          .SkipWhile(a => { if (last.std < a.std)last = a; return _isOk(a.ok); })
          .Take(1)
          .IfEmpty(() => last)
          .Select(a => a.d);
      });
      Func<IEnumerable<DateTime>> defaultDate = () => new[] { RatesArray[0].StartDate };
      Func<DateTime, int> dateToIndex = date => prices.FuzzyFind(date, isBetween);
      var corrDate = BarsCountLastDate;
      try {
        Lib.IteratorLoopPow(prices.Count, IteratorLastRatioForCorridor, startIndex, prices.Count, getCount,
          a => dateToIndex(corrDate = a.IfEmpty(defaultDate).Single()));
        BarsCountLastDate = corrDate.Max(BarsCountLastDate);
        BarsCountCalc = UseRatesInternal(rl => rl.Count - rl.TakeWhile(r => r.StartDate < BarsCountLastDate).Count());
      } catch (Exception exc) {
        Log = exc;
      }
    }
    DateTime __barsCountLastDate = DateTime.MinValue;
    public DateTime BarsCountLastDate {
      get { return __barsCountLastDate; }
      set { __barsCountLastDate = value; }
    }
    static IList<T> IteratorLopper<T>(int start, int end, Func<int, int, int> nextStep, IterationLooperDelegate<T> corridor, Func<T, int> getCount) {
      var corridors = corridor(start, end, 0).Yield().ToList();
      for (var loop = 0; true; ) {
        var stepPrev = nextStep(getCount(corridors.Last()), loop);
        if (stepPrev <= 1) break;
        loop++;
        var dir = loop % 2 == 0 ? 1 : -1;
        start = getCount(corridors.Last()); end = start + stepPrev * 2 * dir;
        var c = corridor(start, end, loop * dir);
        corridors.Add(c);
      }
      return corridors;
    }
    public static int IteratorLoopPow<T>(int start, int end, Func<int, int, int> nsp, Func<int, int, Func<bool, bool>, Func<int, int>, T> getCounter, Func<T, int> countMap) {
      int s = start, e = end;
      var divider = 1;
      Func<int, int, int> nextStep = (i, l) => nsp(i, l) * divider;
      Func<bool, bool> skipWhile = b => b;
      bool doContinue = false;
      for (var i = 0; true; i++) {
        Func<int, int> ns = j => nextStep(j, i);
        var sw = i % 2 == 0 ? skipWhile : b => !skipWhile(b);
        var count = countMap(getCounter(s, e, sw, ns));
        var step = ns(s).Abs().Max(ns(e).Abs()) * divider;
        if (!count.Between(s - step, e + step))
          if (step.Abs() > 1) doContinue = true;
          else throw new Exception(new { func = "IteratorLoopPow: !count.between(start,end)", count, start = s, end = e } + "");
        if (ns(count).Abs() <= 1)
          return count;
        if (doContinue) continue;
        divider = -divider;
        e = s; s = count;// -ns(count) * 2; e = count + ns(count) * 3;
      }
    }

    void ScanRatesLengthByCorridorLength() {
      Func<IEnumerable<int>> defaultLength = () => CorridorStats.Rates.Count.YieldIf(c => c > 0, () => RatesArray.Count);
      var ratesInternal = UseRatesInternal(ri => ri.Reverse().Select(_priceAvg).ToList(), 5000);
      var countMax = BarsCountCount() - 1;
      var start = BarsCount;
      var nsp = Lib.IteratonSequencePower(countMax, IteratorScanRatesLengthLastRatio);
      var corridors = Lib.IteratonSequence(BarsCount, countMax, i => nsp(i, 0))
        .Select(i => {
          var rates = ratesInternal.GetRange(0, i.Min(countMax));
          var ratesStDev = rates.StandardDeviation() * _stDevUniformRatio / 2;
          var corrLength = CalcCorridorLengthByHeightByRegressionMin(rates, ratesStDev, 0,10,DoFineTuneCorridor);
          return new { corrLength = rates.Count, corrRatio = corrLength.Div(rates.Count) };
        })
        .Where(x => x.corrRatio > 0)
        .OrderBy(x => x.corrRatio)
        .Take(1);
      var lastRsd = 0.0;
      BarsCountCalc = corridors
        .Do(x => lastRsd = x.corrRatio)
        .Select(x => x.corrLength)
        .Concat(defaultLength())
        .First();
      OnRatesArrayChaged = () => OnRatesArrayChaged_SetVoltsByRsd(lastRsd);
    }
    #region IteratorScanRatesLengthLastRatio
    private double _IteratorScanRatesLengthLastRatio = 3;
    [Category(categoryCorridor)]
    [DisplayName("ISRLLR")]
    [Description("IteratorScanRatesLengthLastRatio")]
    public double IteratorScanRatesLengthLastRatio {
      get { return _IteratorScanRatesLengthLastRatio; }
      set {
        if (_IteratorScanRatesLengthLastRatio != value) {
          _IteratorScanRatesLengthLastRatio = value;
          OnPropertyChanged("IteratorScanRatesLengthLastRatio");
        }
      }
    }

    #endregion
  }
}
