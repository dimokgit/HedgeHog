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
    void ScanRatesLengthByStDevMin2(int indexMin) {
      try {
        if(IsCorridorFrozen()) {
          //BarsCountCalc = (CorridorStats.Rates.Count * 1.1).Max(BarsCountCalc).Min(BarsCountCount()).ToInt();
          if(CorridorStats.Rates.Count * 1.05 > RatesArray.Count) {
            //SetCorridorStartDateToNextWave(true);
            BarsCountCalc = (CorridorStats.Rates.Count * 1.05).Ceiling();
          }
          return;
        }
        var rateA = new { StartDate = DateTime.Now, PriceAvg = 0.0 };
        Func<DateTime, Rate, Rate, bool> isBetweenRates = (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate);
        var isBetween = MonoidsCore.ToFunc(DateTime.Now, rateA, rateA, (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate));
        int startIndex = indexMin > 0 ? indexMin : BarsCount;
        var prices = UseRatesInternal(ri => ri.Reverse().Select(r => new { r.StartDate, PriceAvg = r.PriceCMALast }).ToList()).SelectMany(p => p).ToList();
        if(prices.Count == 0)
          return;
        var isTicks = BarPeriod == BarsPeriodType.t1;
        if(isTicks) {
          var startIndexDate = prices[startIndex.Min(prices.Count - 1)].StartDate;
          prices = prices.GroupAdjacentTicks(TimeSpan.FromMinutes(1), a => a.StartDate, g => new { StartDate = g.Key, PriceAvg = g.Average(r => r.PriceAvg) }).ToList();
          startIndex = prices.FuzzyFind(startIndexDate, isBetween);
          if(indexMin != 0)
            indexMin = startIndex;
        }
        var getCount = Lib.GetIterator((_start, _end, _isOk, _nextStep) => {
          var last = new { i = 0, d = prices[indexMin].StartDate, ok = false, std = double.MinValue };
          return Lib.IteratonSequence(_start, _end, _nextStep)
            .Where(i => i > indexMin)
            .Select(i => {
              //InPips(prices.GetRange(0, i.Min(prices.Count)).StandardDeviation()).Yield()
              var range = prices.GetRange(0, i.Min(prices.Count));
              var std = InPips(range.ToArray(r => r.PriceAvg).HeightByRegressoin());
              return new { i, d = range.Last().StartDate, ok = std <= RatesStDevMinInPips, std };
            })
            .SkipWhile(a => { if(last.std < a.std) last = a; return _isOk(a.ok); })
            .Take(1)
            .IfEmpty(() => last)
            .Select(a => a.d);
        });
        Func<IEnumerable<DateTime>> defaultDate = () => new[] { RatesArray[0].StartDate };
        Func<DateTime, int> dateToIndex = date => prices.FuzzyFind(date, isBetween);
        var corrDate = BarsCountLastDate;
        var maxCount = Lazy.Create(() => UseRatesInternal(ri => ri.Count));
        Lib.IteratorLoopPow(prices.Count, IteratorLastRatioForCorridor, startIndex, prices.Count, getCount,
          a => dateToIndex(corrDate = a.IfEmpty(defaultDate).Single()));
        if(!WaveRanges.TakeLast(1).Any(wr => corrDate.Between(wr.StartDate, wr.EndDate)))
          BarsCountLastDate = corrDate;//.Max(BarsCountLastDate);
        UseRatesInternal(rl => rl.Count - rl.TakeWhile(r => r.StartDate < BarsCountLastDate).Count()).ForEach(x => {
          BarsCountCalc = x;
          SetTpsAverages();
          SetTicksPerSecondAverage(RatesArray.Last().TpsAverage);
        }); 
      } catch(Exception exc) {
        Log = exc;
      }
    }
    void ScanRatesLengthByDistanceMin() {
      if (IsCorridorFrozen()) {
        //BarsCountCalc = (CorridorStats.Rates.Count * 1.1).Max(BarsCountCalc).Min(BarsCountCount()).ToInt();
        if (CorridorStats.Rates.Count * 1.05 > RatesArray.Count) {
          //SetCorridorStartDateToNextWave(true);
          BarsCountCalc = (CorridorStats.Rates.Count * 1.05).Ceiling();
        }
        return;
      }
      var rdm = InPoints(RatesDistanceMinCalc.Value);
      Func<IEnumerable<Rate>,IEnumerable<double>> distancesByCma = rs =>rs
        .Cma(_priceAvg, CmaPeriodByRatesCount(RatesArray.Count))
        .Distances();
      UseRatesInternal(rs => distsnacesByCma(rs)
        .TakeWhile(i => i <= rdm)
        .Count()
        ).ForEach(count => {
          ScanRatesLengthByStDevMin2(count);
          OnGeneralPurpose(() => RatesDistanceInPips = InPips(UseRates(rs => distancesByCma(rs).Last())).Round(), false);
        });
      return;
    }

    private IEnumerable<double> distsnacesByCma(ReactiveUI.ReactiveList<Rate> rs) {
      return rs
              .Cma(_priceAvg, CmaPeriodByRatesCount(RatesArray.Count))
              .Distances();
    }

    DateTime __barsCountLastDate = DateTime.MinValue;
    public DateTime BarsCountLastDate {
      get { return __barsCountLastDate; }
      set { __barsCountLastDate = value; }
    }
    #region UseM1Corridor
    private int _UseM1Corridor;
    [Category(categoryXXX)]
    public int UseM1Corridor {
      get { return _UseM1Corridor; }
      set {
        if (_UseM1Corridor != value) {
          _UseM1Corridor = value;
          OnPropertyChanged("UseM1Corridor");
        }
      }
    }

    #endregion
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


    double _corridorLengthDiff = 1.2;

    [Category(categoryCorridor)]
    [WwwSettingAttribute(wwwSettingsCorridorOther)]
    public double CorridorLengthDiff {
      get { return _corridorLengthDiff; }
      set {
        if (_corridorLengthDiff == value) return;
        _corridorLengthDiff = value;
        OnPropertyChanged("CorridorLengthDiff");
      }
    }

    [WwwSetting(wwwSettingsCorridorOther)]
    public double RatesDistanceInPips { get; private set; }
  }
}
