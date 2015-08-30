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
    static bool IsTresholdAbsOk(TimeSpan value, TimeSpan treshold) {
      return treshold.TotalMinutes >= 0 ? value.Duration() >= treshold : value.Duration() < -treshold;
    }
    delegate T IterationLooperDelegate<T>(int start, int end, int loop);
    IterationLooperDelegate<T> GetLoper<T>(Func<int, int, int> nextStep, Func<int, T> looper, Func<IEnumerable<T>, T> counter) {
      return (start, end, loop) =>
        counter(Lib.IteratonSequence(start, end, i => nextStep(i, loop)).Select(looper));
    }
    void ScanRatesLengthByStDevMin2(int indexMinOriginal) {
      var indexMin = indexMinOriginal;
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
        var useResults = false;
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
            .IfEmpty(() => {
              useResults = last.i.Ratio(indexMin) > 1.03;
              return last;
            })
            .Select(a => a.d);
        });
        Func<IEnumerable<DateTime>> defaultDate = () => new[] { RatesArray[0].StartDate };
        Func<DateTime, int> dateToIndex = date => prices.FuzzyFind(date, isBetween);
        var corrDate = BarsCountLastDate;
        var maxCount = Lazy.Create(() => UseRatesInternal(ri => ri.Count));
        Lib.IteratorLoopPow(prices.Count, IteratorLastRatioForCorridor, startIndex, prices.Count, getCount,
          a => dateToIndex(corrDate = a.IfEmpty(defaultDate).Single()));
 {
          if(!WaveRanges.TakeLast(1).Any(wr => corrDate.Between(wr.StartDate, wr.EndDate)))
            BarsCountLastDate = corrDate;//.Max(BarsCountLastDate);
          UseRatesInternal(rl => rl.Count - rl.TakeWhile(r => r.StartDate < BarsCountLastDate).Count()).ForEach(x => {
            BarsCountCalc = x;
            SetTpsAverages();
            SetTicksPerSecondAverage(RatesArray.Last().TpsAverage);
          });
        }
      } catch(Exception exc) {
        Log = exc;
      }
    }
    void ScanRatesLengthByDistanceMin() {
      if(IsCorridorFrozen()) {
        //BarsCountCalc = (CorridorStats.Rates.Count * 1.1).Max(BarsCountCalc).Min(BarsCountCount()).ToInt();
        if(CorridorStats.Rates.Count * 1.05 > RatesArray.Count) {
          //SetCorridorStartDateToNextWave(true);
          BarsCountCalc = (CorridorStats.Rates.Count * 1.05).Ceiling();
        }
        return;
      }
      var rdm = InPoints(RatesDistanceMinCalc.Value);
      UseRatesInternal(rs => {
        var cmas = GetCma(rs.Reverse().ToArray());
        var cmas2 = GetCma2(cmas);
        return cmas.Zip(cmas2, (v1, v2) => v1.Abs(v2))
          .Distances()
          .TakeWhile(i => i <= rdm)
          .Count();
        return cmas
          .Distances()
          .TakeWhile(i => i <= rdm)
          .Count();
      }
      ).ForEach(count => {
        //ScanRatesLengthByStDevMin2(count);
        BarsCountCalc = count;
      });
      return;
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
        if(_UseM1Corridor != value) {
          _UseM1Corridor = value;
          OnPropertyChanged("UseM1Corridor");
        }
      }
    }

    #endregion
    [WwwSetting(wwwSettingsCorridorOther)]
    public double RatesDistanceInPips { get; private set; }
  }
}
