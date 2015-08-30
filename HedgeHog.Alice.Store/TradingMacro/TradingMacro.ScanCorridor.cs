using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog.Bars;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reflection;
using ReactiveUI;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    #region ScanCorridor Extentions
    #region New

    DateTime RangeEdgeRight(DateTime date, TimeSpan from, TimeSpan to) {
      if(to > from) {
        return date.Date + to;
      }
      var d = date.Date;
      if(date.Between(d.Add(from), d.AddDays(1)))
        return d.AddDays(1).Add(to);
      return d.Add(to);
    }
    #region VoltsAvgIterations
    private int _VoltsAvgIterations;
    [Category(categoryCorridor)]
    public int VoltsAvgIterations {
      get { return _VoltsAvgIterations; }
      set {
        if(_VoltsAvgIterations != value) {
          _VoltsAvgIterations = value;
          OnPropertyChanged("VoltsAvgIterations");
        }
      }
    }

    #endregion
    public int GetWorkingDays(DateTime from, DateTime to) {
      var dayDifference = (int)to.Subtract(from).TotalDays;
      return Enumerable
          .Range(1, dayDifference)
          .Select(x => from.AddDays(x))
          .Count(x => x.DayOfWeek != DayOfWeek.Saturday && x.DayOfWeek != DayOfWeek.Sunday);
    }


    public Func<double> GetVoltageHigh = () => 0;
    public Func<double> GetVoltageAverage = () => 0;
    public Func<double> GetVoltageLow = () => 0;
    public Func<Rate, double> GetVoltage = r => r.DistanceHistory;
    public Func<Rate, double[]> GetVoltages = r => r.VoltageLocal0;
    double VoltageCurrent { get { return GetVoltage(RateLast); } }
    public Action<Rate, double> SetVoltage = (r, v) => r.DistanceHistory = v;
    public Action<Rate, string, double> SetVoltageKey = (r, k, v) => r.DistanceHistory = v;
    public Func<Rate, double> GetVoltage2 = r => r.Distance1;
    public Action<Rate, double> SetVoltage2 = (r, v) => r.Distance1 = v;

    #region _corridors
    DateTime _corridorStartDate1 = DateTime.MinValue;
    int _corridorLength1 = 0;
    public int CorridorLength1 {
      get { return _corridorLength1; }
      set { _corridorLength1 = value; }
    }
    DateTime _corridorStartDate2 = DateTime.MinValue;
    int _corridorLength2 = 0;
    public int CorridorLength2 {
      get { return _corridorLength2; }
      set { _corridorLength2 = value; }
    }
    #endregion
    #region ClearCOMs
    private bool _ClearCOMs = true;
    [Category(categoryCorridor)]
    [Description("Clear Centers Of Mass")]
    public bool ClearCOMs {
      get { return _ClearCOMs; }
      set {
        if(_ClearCOMs != value) {
          _ClearCOMs = value;
          if(value) {
            CenterOfMassBuy = CenterOfMassSell = RatesArray.Average(_priceAvg);
            ClearCOMs = false;
          } else
            OnPropertyChanged("ClearCOMs");
        }
      }
    }
    #endregion
    private CorridorStatistics ScanCorridorBy123(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var rates = ratesForCorridor.Reverse().ToList();
      var legs = rates.Select(r => r.PriceCMALast).Distances().Select((d, i) => new { d, i }).ToList();
      var leg = legs.Last().d.Div(6);
      var sectionStarts = legs.DistinctUntilChanged(a => a.d.Div(leg).Floor()).ToList();
      var sections = sectionStarts.Zip(sectionStarts.Skip(1), (p, n) => new { end = n.i, start = p.i }).ToList();
      Func<Rate, int> rateIndex = rate => rates.FuzzyFind(rate, (r, r1, r2) => r.StartDate.Between(r1.StartDate, r2.StartDate));
      {
        Func<int, int, Rate> getExtreamRate = (start, end) => {
          var offset = ((end - start) * 0.3).ToInt();
          var range = rates.GetRange(0, rates.Count.Min(end + offset));
          var line = range.ToArray(r => r.PriceAvg).Line();
          var skip = start;
          var zip = line.Skip(skip - offset.Div(2).ToInt()).Zip(range.Skip(skip), (l, r) => new { l = l.Abs(r.PriceAvg), r });
          return zip.MaxBy(x => x.l).First().r;
        };
        Func<int, Rate> getRate = start =>
          sections.GetRange(start, 1).Select(a => getExtreamRate(a.start, a.end)).First();
        var rate = getRate(1);
        try {
          _corridorLength1 = rateIndex(rate);
        } catch(Exception exc) {
          Log = exc;
        }
        _corridorLength2 = ratesForCorridor.Count;
        return ScanCorridorLazy(rates, MonoidsCore.ToLazy(() => rateIndex(getRate(3))));
      }
    }
    private CorridorStatistics ScanCorridorByWaveCount(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      Func<int> freezedCount = () => CorridorStats.Rates.CopyLast(1).Select(r => RatesArray.SkipWhile(r2 => r2.StartDate < r.StartDate).Count()).FirstOrDefault(CorridorDistance);
      Func<int> freezedCount2 = () => CorridorStats.Rates.CopyLast(1)
        .Select(r => r.StartDate)
        .SelectMany(sd => RatesArray.FuzzyIndex(sd, (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate)))
        .FirstOrDefault(CorridorDistance);
      //if (freezedCount() != freezedCount2())
      //  throw new Exception("freezedCount() != freezedCount2()");
      Func<Rate, double> priceAvg02 = rate => (rate.PriceAvg2 - rate.PriceAvg1) / 2 + rate.PriceAvg1;
      Func<Rate, double> priceAvg03 = rate => rate.PriceAvg1 + (rate.PriceAvg3 - rate.PriceAvg1) / 2;
      Func<bool> freeze = () => !_scanCorridorByWaveCountMustReset && CorridorStats.Rates.SkipWhile(r => r.PriceAvg1.IsNaN()).Take(1)
        .Any(rate => GetTradeEnterBy(true)(rate) >= priceAvg02(rate) || GetTradeEnterBy(false)(rate) <= priceAvg03(rate));
      var reversed = ratesForCorridor.ReverseIfNot();
      var count = ScanCorridorByWaveCountImpl(reversed.ToList());
      return ScanCorridorLazy(reversed, MonoidsCore.ToLazy(() => count));
    }

    private int ScanCorridorByWaveCountImpl(List<Rate> rates) {
      _scanCorridorByWaveCountMustReset = false;
      //rates.Cma(r => r.PriceCMALast, CmaPeriodByRatesCount() * CmaRatioForWaveLength, (r, d) => r.PriceRsiP = d);
      rates.Cma(r => r.PriceCMALast, CmaPeriodByRatesCount(), (r, d) => r.PriceRsiP = d);
      for(var i = 0; i < CmaRatioForWaveLength; i++)
        rates.Cma(r => r.PriceRsiP, CmaPeriodByRatesCount(), (r, d) => r.PriceRsiP = d);
      //if (!TradeConditionsEval<TradeConditionUseCorridorAttribute>(true).Single()) {
      //  WaveRangeTail = new WaveRange(0);
      //  var distance = InPoints(RatesDistanceMin) / 3;
      //}
      Func<IList<int>> getExtreams = () => {
        var maxCount = 1000;
        var bufferCount = (rates.Count).Div(maxCount).Max(1).ToInt();

        var dmas = rates.Select((r, i) => new { r, i })
          .Zip(rates, (r, ma) => new { r, ma = ma.PriceRsiP })
          .DistinctUntilChanged(x => x.r.r.PriceCMALast.Sign(x.ma))
          .ToArray();

        var waveWidthAvgIterCnt = 1;
        var widths = dmas.Zip(dmas.Skip(1), (dma1, dma2) => (double)dma2.r.i - dma1.r.i).DefaultIfEmpty(0.0).ToArray();
        var widthAvg = widths.AverageByIterations(waveWidthAvgIterCnt).Average();
        var waveWidth = widthAvg.Div(bufferCount).ToInt();

        var ratesForWave = rates
          .Buffer(bufferCount)
          .Select(b => new { Price = b.Average(r => r.PriceCMALast), StartDate = b.Last().StartDate });
        var extreams = ratesForWave.Extreams(waveWidth, r => r.Price, r => r.StartDate).ToArray();
        var extreams2 = extreams.Scan(Tuple.Create(0, DateTime.Now, 0.0), (p, t) => {
          return p.Item1 == 0 ? t
            : t.Item1 - p.Item1 < waveWidth / 2
            ? p
            : p.Item3.SignUp() == t.Item3.SignUp()
            ? p
            : t;
        });
        Func<DateTime, Rate, Rate, bool> isBetween = (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate);
        Func<List<Tuple<int, DateTime, double>>> newList = () => new List<Tuple<int, DateTime, double>>();
        var extreams21 = extreams.Scan(newList(), (l, t) => {
          if(l.Count > 0 && t.Item1 - l[0].Item1 > waveWidth / 2)
            l = newList();
          l.Add(t);
          return l;
        })
        .Select(l => l[0])
        .GroupByAdjacent(t => t.Item3.SignUp())
        .Select(g => g.Last())
        .ToArray();
        return extreams21
          .DistinctUntilChanged(t => t.Item1)
          .SelectMany(w => rates.FuzzyIndex(w.Item2, isBetween))
          .ToList();
      };
      var extreams3 = getExtreams();
      var index = extreams3
        .Skip(_greenRedBlue.Take(2).Sum() - 1)
        .Take(1)
        .DefaultIfEmpty(extreams3.DefaultIfEmpty(rates.Count - 1).Last())
        //.Where(_ => !freeze())
        //.DefaultIfEmpty(freezedCount)
        .Single();

      #region Average Wave Height Calc
      var corridorWaves = new List<List<WaveRange>>();
      var makeWaves = MonoidsCore.ToFunc(() => {
        var wr = new[] { 0 }.Concat(extreams3)
          .Zip(extreams3, (p, n) => rates.GetRange(p, n - p))
          .Do(range => range.Reverse())
          .ToList(range => new WaveRange(range, PointSize, BarPeriod));
        //SetCorridorStopDate(rates[extreams3[0]]);

        WaveRange wTail = new WaveRange(0);
        #region Split First Wave
        var splitIndex = wr[0].Range.IndexOf(wr[0].Slope > 0
          ? wr[0].Range.OrderByDescending(r => r.AskHigh).First()
          : wr[0].Range.OrderBy(r => r.AskLow).First()
          );
        var wrCount = wr[0].Count;
        if(splitIndex.Div(wr.Count) > .3) {
          var range = wr[0].Range;
          var wr1 = range.GetRange(0, splitIndex + 1).ToList();
          wr = new[] { wr1 }.Select(w => new WaveRange(w, PointSize, BarPeriod)).Concat(wr.Skip(1)).ToList();
          var rangeTail = range.GetRange(splitIndex + 1, range.Count - (splitIndex + 1));
          if(rangeTail.Any() && rangeTail.Last().StartDate.Subtract(rangeTail[0].StartDate).TotalSeconds > 3)
            wTail = new WaveRange(rangeTail, PointSize, BarPeriod) { IsTail = true };
        }
        #endregion

        //#region Split Last Wave
        //wr.TakeLast(0).ToArray()
        //  .ForEach(wrl => {
        //    var splitIndexLast = wrl.Range.IndexOf(wrl.Slope > 0
        //      ? wrl.Range.OrderByDescending(r => r.AskHigh).First()
        //      : wrl.Range.OrderBy(r => r.AskLow).First()
        //      );
        //    var wrCountLast = wrl.Count;
        //    var range = wrl.Range;
        //    var rangeSplit = range.GetRange(splitIndexLast, wrCountLast - splitIndexLast).ToList();
        //    wr = wr.SkipLast(1).Concat(new[] { new WaveRange(rangeSplit, PointSize) }).ToList();
        //  });
        //#endregion


        #region Wave Stats
        Func<Func<WaveRange, double>, double> avg = value => wr.Select(value).ToArray().AverageByStandardDeviation();
        var wa = WaveRangeAvg = new WaveRange(1) {
          Distance = avg(w => w.Distance),
          DistanceByRegression = avg(w => w.DistanceByRegression),
          WorkByHeight = avg(w => w.WorkByHeight),
          WorkByTime = avg(w => w.WorkByTime),
          Angle = avg(w => w.Angle.Abs()),
          Height = avg(w => w.Height),
          StDev = avg(w => w.StDev),
          UID = wr.Average(w => w.UID)
        };
        if(wTail.TotalSeconds < 3)
          wTail = new WaveRange();
        while(
          false &&
          wr
            .Where(w => w.WorkByTime * WorkByTimeRatio < wa.WorkByTime)
            .Take(1)
            .Select(w => wr.IndexOf(w))
            .Do(i => {
              var wavesSmoothed = (i == 0
                ? new[] { WaveRange.Merge(wr.Take(2).Reverse()) }.Concat(wr.Skip(2)).ToList()
                : i == wr.Count - 1
                ? wr.SkipLast(2).Concat(new[] { WaveRange.Merge(wr.TakeLast(2).Reverse()) })
                : wr.Take(i - 1).Concat(new[] { WaveRange.Merge(wr.GetRange(i - 1, 3).Reverse<WaveRange>()) }).Concat(wr.Skip(i + 2))
                ).ToList();
              wr = wavesSmoothed;
            })
            .Any()) { };
        Func<Func<WaveRange, double>, double> rsd = value => wr.Sum(value);
        WaveRangeSum = new WaveRange(1) {
          Distance = rsd(w => w.Distance),
          DistanceByRegression = rsd(w => w.DistanceByRegression),
          WorkByHeight = rsd(w => w.WorkByHeight),
          WorkByTime = rsd(w => w.WorkByTime),
          Angle = rsd(w => w.Angle),
          Height = rsd(w => w.Height),
          StDev = rsd(w => w.StDev),
          UID = wr.Average(w => w.UID)
        };
        #endregion
        #region Elliot Waves
        {
          var criterias = new Func<WaveRange, double>[] {
          w => w.Distance,
          w => w.DistanceByRegression,
          w => w.WorkByHeight ,
          w => w.Height
          };
          var waveRangesForElliot = criterias.Select(c => wr.OrderByDescending(c).First()).Distinct().ToList();
          if(waveRangesForElliot.Count == 1)
            waveRangesForElliot.First().IsSuper = true;
        }
        #endregion
        return new { wr, wTail };
      });

      #endregion

      if(!IsCorridorFrozen()) {
        var wrwt = makeWaves();
        WaveRanges = wrwt.wr;
        WaveRangeTail = wrwt.wTail;
        var extreams4 = new[] { WaveRangeTail }
          .Concat(WaveRanges)
          .Select(wr => new { d = wr.Distance, c = wr.Count })
          .Scan((wrp, wrn) => new { d = wrp.d + wrn.d, c = wrp.c + wrn.c })
          .ToArray();
        var length4 = MonoidsCore.ToFunc(0, 0.0, (i, d) => extreams4
          .Skip(i - 1)
          .SkipWhile(x => x.d < d)
          .DefaultIfEmpty(extreams4.Last())
          .First());

        //Func<int, int> length = i => extreams3          .Skip(i - 1)          .Take(1)          .DefaultIfEmpty(index)          .First();
        //_corridorLength1 = length(_greenRedBlue[0]);
        var indexGreen = length4(_greenRedBlue[0], WaveRangeAvg.Distance * _greenRedBlue[0]);
        _corridorLength1 = indexGreen.c;
        _corridorStartDate1 = rates[_corridorLength1].StartDate;

        var indexRB = MonoidsCore.ToFunc(indexGreen, 0.0, 0, (indeX, distance, rbIndex) => extreams4
          .SkipWhile(x => x.c <= indeX.c)
          .Skip(_greenRedBlue[rbIndex] - 1)
          .SkipWhile(x => x.d < indeX.d + distance * _greenRedBlue[rbIndex])
          .DefaultIfEmpty(indeX)
          .First());

        var indexRed = indexRB(indexGreen, WaveRangeAvg.Distance, 0);
        index = indexRed.c;

        var indexBlue = indexRB(indexRed, WaveRangeAvg.Distance, 0);
        _corridorLength2 = indexBlue.c;
        _corridorStartDate2 = rates[_corridorLength2].StartDate;

        if(new[] { BuyLevel, SellLevel }.All(sr => sr != null && !sr.InManual) && index.Ratio(CorridorStats.Rates.Count) >= 1.01)
          ResetSuppResesPricePosition();
        CorridorLengths = new[] { _corridorLength1, index, _corridorLength2 };
      } else {
        var firstWaveRange = rates.TakeWhile(r => r.StartDate >= WaveRanges[0].StartDate).Reverse().ToList();
        WaveRanges = new[] { new WaveRange(firstWaveRange, PointSize, BarPeriod) }.Concat(WaveRanges.Skip(1)).ToList();
        WaveRangeTail = new WaveRange(0);
      }

      Func<WaveRange, double> bfp = w => w.DistanceByRegression;// WaveRangeAvg.BestFitProp();
      WaveFirstSecondRatio = WaveRanges.Take(1).Select(w1 => bfp(w1) / bfp(WaveRangeAvg)).FirstOrDefault();

      WaveHeightAverage = WaveRangeAvg.Height;
      WaveHeightPower = new[]{
          WaveRangeAvg.WorkByHeight,
          WaveRangeAvg.Height ,
          WaveRangeAvg.DistanceByRegression
        }.StandardDeviation();
      return index + 1;
    }

    public bool IsCorridorFrozen() {
      return CorridorStartDate.HasValue || (TradeConditionsEvalStartDate().Any() && SuppRes.Any(sr => sr.CanTrade));
    }


    #region GreenRedBlue
    static int[] _greenRedBlueDefault = new[] { 2, 2, 2 };
    int[] _greenRedBlue = _greenRedBlueDefault;
    private string _GreenRedBlue;
    [Category(categoryCorridor)]
    [WwwSetting(wwwSettingsCorridor)]
    public string GreenRedBlue {
      get { return _GreenRedBlue; }
      set {
        if(_GreenRedBlue != value) {
          try {
            _greenRedBlue = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s)).ToArray();
            if(_greenRedBlue.Any(i => i <= 0))
              throw new Exception("All elemebts must be >=0 in _greenRedBlue " + _greenRedBlue.ToJson());
            if(_greenRedBlue.Any())
              _GreenRedBlue = value;
            else
              _GreenRedBlue = string.Join(",", _greenRedBlueDefault);
            OnPropertyChanged("GreenRedBlue");
          } catch(Exception exc) {
            Log = exc;
          }
        }
      }
    }
    #endregion

    public IList<WaveRange> WaveRangesWithTail {
      get { return new[] { WaveRangeTail }.Where(wr => wr != null && wr.Count > 0).Concat(WaveRanges).ToArray(); }
    }
    object _waveRangesLocker = new object();
    List<WaveRange> _waveRanges = new List<WaveRange>();
    public List<WaveRange> WaveRangesGRB { get; set; }
    public List<WaveRange> WaveRanges {
      get { lock (_waveRangesLocker) return _waveRanges; }
      set {
        lock (_waveRangesLocker) {
          _waveRanges = value;
          WaveRangesGRB = value.Take(_greenRedBlue.Sum()).ToList();
        }
      }
    }
    public WaveRange WaveRangeSum { get; set; }
    WaveRange _waveRangeAvg = new WaveRange(0);

    public WaveRange WaveRangeAvg {
      get { return _waveRangeAvg; }
      set { _waveRangeAvg = value ?? new WaveRange(0); }
    }
    static Rate[] _trenLinesEmptyRates = new Rate[] { new Rate { Trends = Rate.TrendLevels.Empty }, new Rate { Trends = Rate.TrendLevels.Empty } };
    Lazy<IList<Rate>> _trendLines = new Lazy<IList<Rate>>(() => _trenLinesEmptyRates);
    public Lazy<IList<Rate>> TrendLines {
      get { return _trendLines; }
      private set { _trendLines = value; }
    }
    Lazy<IList<Rate>> _trendLines1 = new Lazy<IList<Rate>>(() => _trenLinesEmptyRates);
    public Lazy<IList<Rate>> TrendLines1 {
      get { return _trendLines1; }
      private set { _trendLines1 = value; }
    }
    Lazy<IList<Rate>> _trendLines2 = new Lazy<IList<Rate>>(() => _trenLinesEmptyRates);
    public Lazy<IList<Rate>> TrendLines2 {
      get { return _trendLines2; }
      private set { _trendLines2 = value; }
    }
    private double MaGapMax(IList<Rate> rates) {
      return rates.Skip(rates.Count.Div(1.1).ToInt()).Max(r => r.PriceCMALast.Abs(r.PriceAvg));
    }
    #region CmaRatioForWaveLength
    bool _scanCorridorByWaveCountMustReset;
    private double _CmaRatioForWaveLength = 0;
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsCorridorCMA)]
    public double CmaRatioForWaveLength {
      get { return _CmaRatioForWaveLength; }
      set {
        if(_CmaRatioForWaveLength != value) {
          _CmaRatioForWaveLength = value;
          OnPropertyChanged("CmaRatioForWaveLength");
        }
      }
    }

    #endregion

    #region IteratorLastRatioForCorridor
    private double _IteratorLastRatioForCorridor = 3;
    [Category(categoryCorridor)]
    [DisplayName("ILRfC")]
    [Description("IteratorLastRationForCorridor")]
    public double IteratorLastRatioForCorridor {
      get { return _IteratorLastRatioForCorridor; }
      set {
        if(_IteratorLastRatioForCorridor != value) {
          _IteratorLastRatioForCorridor = value;
          OnPropertyChanged("IteratorLastRatioForCorridor");
        }
      }
    }

    #endregion
    #endregion
    #endregion

    private bool _crossesOk;

    #region DistancePerBar
    private double _DistancePerBar;
    public double DistancePerBar {
      get { return _DistancePerBar; }
      set {
        if(_DistancePerBar != value) {
          _DistancePerBar = value;
          OnPropertyChanged("DistancePerBar");
        }
      }
    }
    #endregion
    #region DistancePerPip
    private double _DistancePerPip;
    public double DistancePerPip {
      get { return _DistancePerPip; }
      set {
        if(_DistancePerPip != value) {
          _DistancePerPip = value;
          OnPropertyChanged("DistancePerPip");
        }
      }
    }
    #endregion

    public double CorridorCorrelation { get; set; }
    #region CrossesCount
    private double _CrossesDensity;
    private DateTime _CorridorStartDateByScan;
    public double CrossesDensity {
      get { return _CrossesDensity; }
      set {
        if(_CrossesDensity != value) {
          _CrossesDensity = value;
          OnPropertyChanged("CrossesDensity");
        }
      }
    }

    #endregion

    public double HarmonicsAverage { get; set; }

    #region Custom1
    private double _Custom1;
    public double Custom1 {
      get { return _Custom1; }
      set {
        if(_Custom1 != value) {
          _Custom1 = value;
          OnPropertyChanged("Custom1");
        }
      }
    }

    #endregion

    void ResetBarsCountCalc() {
      _BarsCountCalc = null;
      BarsCountDate = null;
      OnPropertyChanged("BarsCountCalc");
    }
    #region BarsCountDate
    private DateTime? _BarsCountDate = null;
    public DateTime? BarsCountDate {
      get { return _BarsCountDate; }
      set {
        if(_BarsCountDate != value) {
          _BarsCountDate = value;
          OnPropertyChanged("BarsCountDate");
        }
      }
    }

    #endregion
    bool IsBarsCountCalcSet { get { return _BarsCountCalc.HasValue; } }
    private int? _BarsCountCalc;
    [DisplayName("Bars Count Calc(45,360,..)")]
    [Category(categoryCorridor)]
    [Dnr]
    public int BarsCountCalc {
      get {
        return BarsCountDate.HasValue
          ? UseRatesInternal(BarCountByDate).IfEmpty(() => BarsCountCalc).First()
          : _BarsCountCalc.GetValueOrDefault(BarsCount);
      }
      set {
        if(_BarsCountCalc == value)
          return;
        _BarsCountCalc = value == 0 ? (int?)null : value;
        if(_BarsCountCalc.HasValue) {
          BarsCountDate = null;
          //Log = new Exception(new { BarsCountCalc, BarsCountDate } + "");
        }
        OnPropertyChanged("BarsCountCalc");
      }
    }

    private int BarCountByDate(ReactiveList<Rate> rs) {
      return (rs.Count - rs.TakeWhile(r => r.StartDate < BarsCountDate).Count()).Min(rs.Count);
    }

    double _waveHeightAverage;
    public double WaveHeightAverage {
      get { return _waveHeightAverage; }
      set {
        if(_waveHeightAverage != value) {
          _waveHeightAverage = value;
          OnPropertyChanged("WaveHeightAverage");
        }
      }
    }

    double _waveFirstSecondRatio;

    public double WaveFirstSecondRatio {
      get { return _waveFirstSecondRatio; }
      set {
        _waveFirstSecondRatio = value;
        OnPropertyChanged("WaveFirstSecondRatio");
      }
    }

    #region WorkByTimeRatio
    private int _WorkByTimeRatio = 10;
    [Category(categoryXXX)]
    [WwwSetting]
    public int WorkByTimeRatio {
      get { return _WorkByTimeRatio; }
      set {
        if(_WorkByTimeRatio != value) {
          _WorkByTimeRatio = value;
          OnPropertyChanged("WorkByTimeRatio");
        }
      }
    }

    #endregion


    WaveRange _waveRangeTail = new WaveRange(0);

    public WaveRange WaveRangeTail {
      get { return _waveRangeTail; }
      set { _waveRangeTail = value ?? new WaveRange(0); }
    }


    public double WaveHeightPower { get; set; }

    int[] _corridorLengths = new int[0];

    public int[] CorridorLengths {
      get { return _corridorLengths; }
      set { _corridorLengths = value; }
    }
  }
}
