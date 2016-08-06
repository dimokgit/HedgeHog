using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

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
    #region ScanRatesLengthByStDevMin2
    /// <summary>
    /// Keep it around as Lib.GetIterator example
    /// </summary>
    /// <param name="indexMinOriginal"></param>
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
          prices = prices.GroupedDistinct(a => a.StartDate.AddMilliseconds(-a.StartDate.Millisecond), g => new { StartDate = g[0].StartDate, PriceAvg = g.Average(r => r.PriceAvg) }).ToList();
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
            .DefaultIfEmpty(() => {
              useResults = last.i.Ratio(indexMin) > 1.03;
              return last;
            })
            .Select(a => a.d);
        });
        Func<IEnumerable<DateTime>> defaultDate = () => new[] { RatesArray[0].StartDate };
        Func<DateTime, int> dateToIndex = date => prices.FuzzyFind(date, isBetween);
        var corrDate = BarsCountLastDate;
        Lib.IteratorLoopPow(prices.Count, IteratorLastRatioForCorridor, startIndex, prices.Count, getCount,
          a => dateToIndex(corrDate = a.IfEmpty(defaultDate).Single()));
        {
          if(!WaveRanges.TakeLast(1).Any(wr => corrDate.Between(wr.StartDate, wr.EndDate)))
            BarsCountLastDate = corrDate;//.Max(BarsCountLastDate);
          UseRatesInternal(rl => rl.Count - rl.TakeWhile(r => r.StartDate < BarsCountLastDate).Count()).ForEach(x => {
            BarsCountCalc = x;
            SetTicksPerSecondAverage(RatesArray.Last().TpsAverage);
          });
        }
      } catch(Exception exc) {
        Log = exc;
      }
    }
    #endregion

    void ScanRatesLengthByDistanceMin0() {
      BarsCountCalc = GetRatesLengthByDistanceMinByMacd(DistanceByMACD).DefaultIfEmpty(BarsCountCalc).Single();
    }
    void ScanRatesLengthByDistanceMin() {
      BarsCountCalc = GetRatesLengthByDistanceMinByMacd(DistanceByMACD2).DefaultIfEmpty(BarsCountCalc).Single();
    }
    double _macd2Rsd = double.NaN;
    public double MacdRsdAvg { get; set; }
    bool _isRatesLengthStable = false;
    public bool IsRatesLengthStable {
      get { return _isRatesLengthStable; }
      set { _isRatesLengthStable = value; }
    }

    IEnumerable<int> GetRatesLengthByDistanceMinByMacd(Func<IList<Rate>, int, Action<double, double>, IEnumerable<double>> macd) {
      var distances = new List<double>(BarsCountCalc);
      Action<double, double> addDistance = (p, n) => distances.Add(p.Abs(n));
      return UseRatesInternal(rs => rs.ToList())
        .Select(rs => {
          rs.Reverse();
          var rdm = InPoints(RatesDistanceMin);
          var count = macd(rs, BarsCountCalc, addDistance)
            .Skip(BarsCount)
            .TakeWhile(i => i <= rdm)
            .Count() + BarsCount;
          return new { count };//, length = RatesTimeSpan(rs.GetRange(0, count)) };
        })
        .Select(x => {
          _macd2Rsd = distances.RelativeStandardDeviationSmoothed(1) * 100;// / x.length.TotalDays;
          const double adjuster = 0;
          if(x.count * adjuster > BarsCountMax) {
            BarsCountMax = (BarsCountMax * adjuster).Ceiling();
            Log = new Exception(new { BarsCountMax, PairIndex, Action = "Stretched" } + "");
          }
          IsRatesLengthStable = RatesArray.Count.Ratio(x.count) < 1.05;
          return x.count;
        });
    }

    void ScanRatesLengthByDistanceMinSmoothed() {
      BarsCountCalc = GetRatesLengthByDistanceMinByMacdSmoothed()
        //.Concat(BarsCountByM1())
        .OrderBy(d => d)
        .DefaultIfEmpty(BarsCountCalc)
        .Take(1)
        .Concat(new[] { BarsCount })
        .Concat(new[] { GetRatesCountByTimeFrame(RatesArray.Last().StartDate, TimeFrameTresholdTimeSpan) })
        .OrderByDescending(d => d)
        .Do(count => IsRatesLengthStable = RatesArray.Count.Ratio(count) < 1.05)
        .First();
    }
    object _macdDiastancesLocker = new object();
    List<double> _macdDiastances = new List<double>();

    IEnumerable<int> GetRatesLengthByDistanceMinByMacdSmoothed() {
      var distances = new List<double>(BarsCountCalc);
      Action<double, double> addDistance = (p, n) => distances.Add(p.Abs(n));
      var cmaPeriod = CmaPeriodByRatesCount(BarsCountCalc);
      var rdm = InPoints(RatesDistanceMin);
      return UseRatesInternal(rs => rs.GetRange((BarsCountCalc * 1.1).ToInt().Min(rs.Count)).ToList())
        .Select(rs => {
          _macdDiastances = Macd(rs, BarsCountCalc)
            .Pairwise((v1, v2) => v1.Abs(v2))
            .ToArray()
            .Cma(cmaPeriod)
            .Reverse()
            .Distances(addDistance)
            .Skip(BarsCount + 1)
            .TakeWhile(i => i <= rdm)
            .ToList();
          var count = _macdDiastances.Count + BarsCount + 1;
          return new { count };//, length = RatesTimeSpan(rs.GetRange(0, count)) };
        })
        .Select(x => {
          return x.count;
        });
    }
    IEnumerable<double> MacdDistancesSmoothedReversed(IList<Rate> rates) {
      var cmaPeriod = CmaPeriodByRatesCount(rates.Count);
      return Macd(rates, null)
        .Pairwise((v1, v2) => v1.Abs(v2))
        .ToArray()
        .Cma(cmaPeriod)
        .Reverse()
        .Distances();
    }
    private IList<double> Macd(IList<Rate> rs, int? cmaPeriodCount) {
      var cmas = GetCma(rs, cmaPeriodCount);
      var cmas2 = GetCma2(cmas, cmaPeriodCount);
      var macd = cmas.Zip(cmas2, (v1, v2) => v1.Abs(v2)).ToArray();
      return macd;
    }

    private IEnumerable<double> DistanceByMACD(IList<Rate> rs, int cmaPeriodCount, Action<double, double> onDistance = null) {
      IEnumerable<double> macd = CmaMACD = Macd(rs, cmaPeriodCount);
      return macd.Distances(onDistance);
    }

    private IEnumerable<double> DistanceByMACD2(IList<Rate> rs, int cmaPeriodCount, Action<double, double> onDistance = null) {
      var macd = Macd(rs, cmaPeriodCount);
      var macd2 = CmaMACD = macd.Zip(macd.Skip(1), (v1, v2) => v1.Abs(v2));
      return macd2.Distances(onDistance);
    }

    public void ScanRatesLengthByDistanceMinAndCrossesCount() {
      UseRatesInternal(rs => rs.ToList())
        .Select(rs => {
          rs.Reverse();
          var rdm = InPoints(RatesDistanceMin);
          var cmas = GetCma(rs, BarsCountCalc);
          var cmas2 = GetCma2(cmas, BarsCountCalc);
          var crosses = cmas.CrossesSmoothed(cmas2);
          var macd = cmas.Zip(cmas2, (v1, v2) => v1.Abs(v2)).ToArray();
          var macd2 = CmaMACD = macd.Zip(macd.Skip(1), (v1, v2) => v1.Abs(v2));
          var macd3 = macd2
            .Distances();
          return macd3
            .Skip(BarsCount)
            .TakeWhile(i => i <= rdm)
            .Count() + BarsCount;
        })
        .ForEach(count => {
          const double adjuster = 0;
          if(count * adjuster > BarsCountMax) {
            BarsCountMax = (BarsCountMax * adjuster).Ceiling();
            Log = new Exception(new { BarsCountMax, PairIndex, Action = "Stretched" } + "");
          }
          BarsCountCalc = count;
        });
    }

    void ScanRatesLengthByTimeFrame() {
      if(BarPeriod != BarsPeriodType.t1)
        BarsCountCalc = TimeFrameTresholdTimeSpan.TotalMinutes.ToInt();
      else
        UseRates(ra => ra.Last().StartDate)
         .ForEach(dateEnd => {
           var dateStart = dateEnd.Subtract(TimeFrameTresholdTimeSpan);
           UseRatesInternal(ri => ri.FuzzyIndex(dateStart, (ds, r1, r2) => ds.Between(r1.StartDate, r2.StartDate)))
           .SelectMany(i => i)
           .ForEach(i => {
             var count = RatesInternal.Count - i;
             if(count >= BarsCount)
               BarsCountCalc = count;
           });
         });
      IsRatesLengthStable = true;
    }

    void ScanRatesLengthByDistanceMinAndimeFrame() {
      var count = GetRatesLengthByDistanceMinByMacd(DistanceByMACD2);
      var counts = GetRatesLengthsByTimeFrameRange();
      BarsCountCalc = counts[0].Max(counts).Min(counts[1]);
    }
    int[] GetRatesLengthsByTimeFrameRange() {
      if(BarPeriod != BarsPeriodType.t1)
        return new[] { TimeFrameTresholdTimeSpan.TotalMinutes.ToInt(), TimeFrameTresholdTimeSpan.TotalMinutes.ToInt() };
      else {
        Func<DateTime, TimeSpan, int> getCount = (dateEnd, timeFrame) =>
          UseRatesInternal(ri => ri.FuzzyIndex(dateEnd.Subtract(timeFrame), (ds, r1, r2) => ds.Between(r1.StartDate, r2.StartDate)))
           .SelectMany(i => i, (_, i) => RatesInternal.Count - i)
           .Single();

        var counts = UseRates(ra => ra.Last().StartDate)
         .SelectMany(dateEnd => new[] { new { dateEnd, timeSpan = TimeFrameTresholdTimeSpan2 }, new { dateEnd, timeSpan = TimeFrameTresholdTimeSpan2 } })
         .Select(x => getCount(x.dateEnd, x.timeSpan))
         .ToArray();
        return counts;
      }
    }

    DateTime[] _ratesStartDate;
    void UnFreezeRatesStartDate() {
      _ratesStartDate = new DateTime[0];
    }
    void FreezeRatesStartDate() {
      _ratesStartDate = RatesArray.Take(1)
        .Select(r => r.StartDate)
        .ToArray();
    }
    void ScanRatesLengthByM1Wave() {
      if(BarPeriod != BarsPeriodType.t1)
        throw new Exception("ScanRatesLengthByM1Wave is only supported for BarsPeriodType." + BarsPeriodType.t1);
      TradingMacroM1(tm => tm.WaveRanges.Take(1), w => w)
        .Select(wr => wr.StartDate)
        .Where(_ => /*!BuySellLevels.Any(sr => sr.CanTrade) &&*/ !HaveTrades())
        .SelectMany(date => UseRatesInternal(rates => rates.SkipWhile(r => r.StartDate < date).Count()))
        .ForEach(count => BarsCountCalc = count.Max(BarsCount));
    }
    void ScanRatesLengthByM1WaveAvg(bool doUseTotalMinutes, Func<TradingMacro, IEnumerable<WaveRange>> getWr) {
      if(BarPeriod != BarsPeriodType.t1)
        throw new Exception("ScanRatesLengthByM1Wave is only supported for BarsPeriodType." + BarsPeriodType.t1);
      try {
        (from tm in TradingMacroOther()
         let wr = getWr(tm)
         where wr != null
         let distMin = InPoints(wr.Where(w=>w!=null).Average(w => w.Distance))
         let dateMin = wr.Select(w => w.TotalMinutes).OrderByDescending(m => m).Take(1).Select(m => ServerTime.AddMinutes(-m))
         from dates in tm.UseRatesInternal(rates => rates.BackwardsIterator()
         .Distances(_priceAvg).SkipWhile(t => t.Item2 < distMin)
         .Select(t => t.Item1.StartDate)
         .Take(1)
         .Concat(dateMin.Where(_ => doUseTotalMinutes))
         .Concat(tm.WaveRanges.Take(1).Select(wr => wr.StartDate))
         .OrderBy(d => d)
         .Take(1))
         from date in dates
         from counts in UseRatesInternal(rates => rates.FuzzyIndex(date, (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate)))
         from count in counts
         select RatesInternal.Count - count
         ).ForEach(count => BarsCountCalc = count.Max(BarsCount));
      } catch(Exception exc) {
        Log = exc;
      }
    }
    void ScanRatesLengthByM1CorridorsAvg() {
      if(BarPeriod != BarsPeriodType.t1)
        throw new Exception("ScanRatesLengthByM1Wave is only supported for BarsPeriodType." + BarsPeriodType.t1);
      (from tm in TradingMacroOther()
       let distance = tm.TrendLinesTrendsAll.Skip(0).Select(tl => tl.StDev * 4).DefaultIfEmpty().Average()
       select UseRatesInternal(rates => rates.BackwardsIterator().Distances(_priceAvg).SkipWhile(t => t.Item2 < distance).Count()) into counts
       from count in counts
       select count
       ).ForEach(count => BarsCountCalc = count.Max(BarsCount));
    }
    IEnumerable<int> BarsCountByM1() {
      return TradingMacroOther()
        .SelectMany(tm => tm.WaveRanges.Take(1))
        .Select(wr => wr.StartDate)
        .SelectMany(date => UseRatesInternal(rates => rates.SkipWhile(r => r.StartDate < date).Count()));
    }
    private int GetRatesCountByTimeFrame(DateTime dateEnd, TimeSpan timeFrame) {
      var dateStart = dateEnd.Subtract(timeFrame);
      return UseRatesInternal(ri => ri.FuzzyIndex(dateStart, (ds, r1, r2) => ds.Between(r1.StartDate, r2.StartDate)))
       .SelectMany(i => i, (_, i) => BarsCountCalc.Max(RatesInternal.Count - i))
       .Single();
    }

    DateTime __barsCountLastDate = DateTime.MinValue;
    public DateTime BarsCountLastDate {
      get { return __barsCountLastDate; }
      set { __barsCountLastDate = value; }
    }

    double _crossCountRatioForCorridorLength = 1;
    //[WwwSetting(wwwSettingsCorridorCMA)]
    //[Category(categoryActive)]
    public double CrossCountRatioForCorridorLength {
      get {
        return _crossCountRatioForCorridorLength;
      }

      set {
        if(_crossCountRatioForCorridorLength == value)
          return;
        _crossCountRatioForCorridorLength = value;
        OnPropertyChanged("CrossCountRatioForCorridorLength");
      }
    }

    public IEnumerable<double> CmaMACD {
      get;
      private set;
    }
  }
}
