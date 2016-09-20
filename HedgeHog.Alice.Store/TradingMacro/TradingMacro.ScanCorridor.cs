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

    private const string MULTI_VALUE_SEPARATOR = ";";
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
    public int CorridorLengthLime { get; private set; }
    public int CorridorLengthGreen { get; private set; }
    public int CorridorLengthRed { get; private set; }
    DateTime _corridorStartDate2 = DateTime.MinValue;
    public int CorridorLengthBlue { get; private set; }
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
      var rates = ratesForCorridor.ToList();
      var ri = new { r = (Rate)null, i = 0 };
      var miner = MonoidsCore.ToFunc(ri, r => r.r.BidLow);
      var maxer = MonoidsCore.ToFunc(ri, r => r.r.AskHigh);
      var groupMap = MonoidsCore.ToFunc(ri.Yield().ToList(), range => new {
        rmm = range.MinMaxBy(miner, r => r.r.AskHigh),
        a = range.Average(r => r.r.PriceAvg)
      });
      var grouped = Lazy.Create(() => ratesForCorridor
        .Select((r, i) => new { r, i })
        .ToList()
        .GroupedDistinct(r => r.r.StartDate.AddMilliseconds(-r.r.StartDate.Millisecond), groupMap));
      rates.Reverse();
      var legs = (
        BarPeriodInt > 0 || CmaMACD == null || _macdDiastances.IsEmpty()
        ? MacdDistancesSmoothedReversed(rates)
        : _macdDiastances
        ).Select((d, i) => new { d, i }).Take(rates.Count).ToList();
      var leg = legs.TakeLast(1).Select(l => l.d.Div(7)).DefaultIfEmpty(1).Single();
      var sectionStarts = legs.DistinctUntilChanged(a => a.d.Div(leg).Floor()).ToList();
      var sections = sectionStarts.Zip(sectionStarts.Skip(1), (p, n) => new { end = n.i, start = p.i }).ToList();
      if(sections.Count != 7) {
        //Log = new Exception(new { sections = new { sections.Count } } + "");
      }
      //var sections2 = sectionStarts.Scan(new { end=0,start=0},(p, n) => new { end = n.i, start = p.end }).ToList();
      //sections2.Count();

      #region Funcs

      Func<TradeLevelsPreset, Func<Rate.TrendLevels, Rate.TrendLevels>> tagTL = (pl) => tl => { tl.Color = pl + ""; return tl; };
      Func<int, TradeLevelsPreset, IList<Rate>> bs = (perc, pl) => {
        var digits = Digits();
        var grouped2 = grouped.Value.ToList();
        if(grouped2.Count <= 1)
          return new List<Rate>();
        var distances = grouped2.Distances(x => x.a).Select((t, i) => new { t, i }).ToList();
        var distChunc = distances.Last().t.Item2 / 100.0 * perc;
        var res = Partitioner.Create(Enumerable.Range(0, distances.Count).ToArray(), true)
        .AsParallel()
        .Select(i => {
          var distStart = distances[i].t.Item2;
          var i2 = i + 1;
          var min = distances[i].t.Item1.rmm[0];
          var max = distances[i].t.Item1.rmm[1];
          while(i2 < distances.Count && distances[i2].t.Item2 - distStart <= distChunc) {
            if(distances[i2].t.Item1.rmm[0].r.BidLow < min.r.BidLow)
              min = distances[i2].t.Item1.rmm[0];
            else if(distances[i2].t.Item1.rmm[1].r.AskHigh > max.r.AskHigh)
              max = distances[i2].t.Item1.rmm[1];
            i2++;
          }
          var isOut = i2 >= distances.Count;
          var start = grouped2[i].rmm.Min(r => r.i);
          var end = grouped2[i2].rmm.Max(r => r.i);
          var count = end - start;
          //var mines = new[] { i, i2 }.Select(i0 => maxer(grouped2[i0].rmm[0])).ToArray();
          //var maxes = new[] { i, i2 }.Select(i0 => maxer(grouped2[i0].rmm[1])).ToArray();
          var height = (maxer(max).Max(maxer(grouped2[i].rmm[1]), maxer(grouped2[i2].rmm[1]))
          - miner(min).Min(miner(grouped2[i].rmm[0]), miner(grouped2[i2].rmm[0]))).Round(digits);
          return new { start, count, height, isOut, i, i2 };
        })
        .TakeWhile(x => !x.isOut)
        .MinByOrEmpty(x => x.height)
        .ToArray()
        .AsEnumerable();

        res = UseFlatTrends
        ? res.OrderBy(x => grouped2.GetRange(x.i, x.i2 - x.i).LinearSlope(y => y.a).Abs())
        : res.OrderByDescending(x => x.start);

        return res
        .OrderByDescending(x => x.start)
        //.OrderBy(x => grouped2.GetRange(x.i, x.i2 - x.i).LinearSlope(y => y.a).Abs())
        .Take(1)
        .Select(x => CalcTrendLines(x.start, x.count, tagTL(pl)))
        .SelectMany(x => x)
        .ToList();
      };

      Func<Rate, int> rateIndex = rate => rates.FuzzyFind(rate, (r, r1, r2) => r.StartDate.Between(r1.StartDate, r2.StartDate));
      Func<int, int, Rate> getExtreamRate = (start, end) => {
        var offset = ((end - start) * 0.3).ToInt();
        var range = rates.GetRange(0, rates.Count.Min(end));
        var line = range.ToArray(r => r.PriceAvg).Line();
        var skip = end /*- start*/ - offset;// - offset.Div(2).ToInt();
        var zip = line.Skip(skip).Zip(range.Skip(skip), (l, r) => new { l = l.Abs(r.PriceAvg), r });
        return zip.OrderByDescending(x => x.l).Take(1).Select(x => x.r).DefaultIfEmpty(range.Last()).Single();
      };
      Func<int, IEnumerableCore.Singleable<Rate>> getRate = start =>
        (start < sections.Count
        ? sections.GetRange(start, 1).Select(a => getExtreamRate(a.start, a.end))
        : new Rate[0]).AsSingleable();
      Func<Exception, IEnumerable<int>> handler = exc => { Log = exc; return new int[0]; };
      Func<int, IEnumerable<int>> getLength = (index) => {
        return getRate(index)
        .Select(rateIndex)
        .Catch(handler);
      };
      #endregion

      var legIndexes = new[] { 0 }.Concat(Enumerable.Range(0, sections.Count).SelectMany(i => getLength(i))).ToList();

      Func<int, int, TradeLevelsPreset, IList<Rate>> calcTrendLines = (start, end, pl) => {
        if(end <= 0)
          return CalcTrendLines(0, tagTL(pl));
        if(start > 7)
          return bs(start, pl);
        var e = end < legIndexes.Count ? legIndexes[end] : ratesForCorridor.Count;
        return CalcTrendLines(RatesArray.Count - e, e - legIndexes[start], tagTL(pl));
      };

      CorridorLengthLime = legIndexes[1];
      TrendLimeInt().Pairwise((s, c) => new { s, e = s + c })
        .ForEach(p => TrendLines0 = Lazy.Create(() => calcTrendLines(p.s, p.e, TradeLevelsPreset.Lime), TrendLines0.Value, exc => Log = exc));

      CorridorLengthGreen = legIndexes[2];
      TrendGreenInt().Pairwise((s, c) => new { s, e = s + c })
        .ForEach(p => TrendLines1 = Lazy.Create(() => calcTrendLines(p.s, p.e, TradeLevelsPreset.Green), TrendLines1.Value, exc => Log = exc));

      TrendRedInt().Pairwise((s, c) => new { s, e = s + c })
        .ForEach(p => TrendLines = Lazy.Create(() => calcTrendLines(p.s, p.e, TradeLevelsPreset.Red), TrendLines.Value, exc => Log = exc));

      TrendPlumInt().Pairwise((s, c) => new { s, e = s + c })
        .ForEach(p => TrendLines3 = Lazy.Create(() => calcTrendLines(p.s, p.e, TradeLevelsPreset.Plum), TrendLines3.Value, exc => Log = exc));

      CorridorLengthBlue = ratesForCorridor.Count;
      TrendLines2 = Lazy.Create(() => CalcTrendLines(CorridorLengthBlue, tagTL(TradeLevelsPreset.Blue)), TrendLines2.Value, exc => Log = exc);

      var ratesForCorr = getRate(3)
        .ToArray()
        .Select(rate => rateIndex(rate))
        .DefaultIfEmpty(ratesForCorridor.Count - 1)
        .Select(redLength => {
          var redRates = RatesArray.GetRange(RatesArray.Count - redLength, redLength);
          redRates.Reverse();
          WaveShort.Rates = redRates;
          return new { redRates, trend = TrendLinesRedTrends };
        })
      .ToArray();

      GetShowVoltageFunction()();
      return ratesForCorr.Select(x => new CorridorStatistics(this, x.redRates, x.trend.StDev, x.trend.Coeffs)).FirstOrDefault();
    }

    private CorridorStatistics ScanCorridorBy1234(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var ri = new { r = (Rate)null, i = 0 };
      var miner = MonoidsCore.ToFunc(ri, r => r.r.BidLow);
      var maxer = MonoidsCore.ToFunc(ri, r => r.r.AskHigh);
      var groupMap = MonoidsCore.ToFunc(ri.Yield().ToList(), range => new {
        rmm = range.MinMaxBy(miner, r => r.r.AskHigh),
        a = range.Average(r => r.r.PriceAvg)
      });
      var groupMap2 = MonoidsCore.ToFunc(ri, r => new {
        rmm = new[] { r, r },
        a = r.r.PriceAvg
      });
      var rates = MonoidsCore.ToFunc(0, 0, (skip, count) => ratesForCorridor.SafeList().GetRange(skip, count).Select((r, i) => new { r, i }));
      var grouped = MonoidsCore.ToFunc(0, 0, (skip, count) =>
       BarPeriod == BarsPeriodType.t1
       ? rates(skip, count).ToList().GroupedDistinct(r => r.r.StartDate.AddMilliseconds(-r.r.StartDate.Millisecond), groupMap)
       : rates(skip, count).Select(groupMap2));
      //var sections2 = sectionStarts.Scan(new { end=0,start=0},(p, n) => new { end = n.i, start = p.end }).ToList();
      //sections2.Count();

      #region Funcs

      Func<TradeLevelsPreset, Func<Rate.TrendLevels, Rate.TrendLevels>> tagTL = (pl) => tl => { tl.Color = pl + ""; return tl; };
      var anonDef = new { rates = (IList<Rate>)null, skip = 0, count = 0 };
      var def = MonoidsCore.ToFunc((IList<Rate>)null, 0, 0, (rts, skip, count) => new { rates = rts, skip, count });
      var bs = MonoidsCore.ToFunc(0, TradeLevelsPreset.Lime, false, 0, 0, (perc, pl, isMin, skip, count) => {
        var digits = Digits();
        var grouped2 = grouped(skip, count).ToList();
        if(grouped2.Count <= 1)
          return anonDef;
        var distances = grouped2.Distances(x => x.a).Select((t, i) => new { t, i }).ToList();
        var distChunc = distances.Last().t.Item2 / 100.0 * perc;
        var res = Partitioner.Create(Enumerable.Range(0, distances.Count).ToArray(), true)
        .AsParallel()
        .Select(i => {
          var distStart = distances[i].t.Item2;
          var i2 = i + 1;
          var min = distances[i].t.Item1.rmm[0];
          var max = distances[i].t.Item1.rmm[1];
          while(i2 < distances.Count && distances[i2].t.Item2 - distStart <= distChunc) {
            if(miner(distances[i2].t.Item1.rmm[0]) < miner(min))
              min = distances[i2].t.Item1.rmm[0];
            if(maxer(distances[i2].t.Item1.rmm[1]) > maxer(max))
              max = distances[i2].t.Item1.rmm[1];
            i2++;
          }
          var isOut = i2 >= distances.Count;
          var start = grouped2[i].rmm.Min(r => r.i);
          var end = grouped2[i2].rmm.Max(r => r.i);
          //var mines = new[] { i, i2 }.Select(i0 => maxer(grouped2[i0].rmm[0])).ToArray();
          //var maxes = new[] { i, i2 }.Select(i0 => maxer(grouped2[i0].rmm[1])).ToArray();
          var height = (maxer(max).Max(maxer(grouped2[i].rmm[1]), maxer(grouped2[i2].rmm[1]))
          - miner(min).Min(miner(grouped2[i].rmm[0]), miner(grouped2[i2].rmm[0]))).Round(digits);
          return new { start = start + skip, count = end - start, height, isOut, i, i2 };
        })
        .TakeWhile(x => !x.isOut).AsEnumerable();

        res = isMin
        ? res.MinByOrEmpty(x => x.height)
        : res.MaxByOrEmpty(x => x.height);

        res = UseFlatTrends
        ? res.OrderBy(x => grouped2.GetRange(x.i, x.i2 - x.i).LinearSlope(y => y.a).Abs())
        : res.OrderByDescending(x => x.start);

        return res
        .OrderByDescending(x => x.start)
        //.OrderBy(x => grouped2.GetRange(x.i, x.i2 - x.i).LinearSlope(y => y.a).Abs())
        .Take(1)
        .Select(x => def(CalcTrendLines(x.start, x.count, tagTL(pl)), x.start, x.count))
        .DefaultIfEmpty(anonDef)
        .Single();
      });

      #endregion


      int? skipFirst = null;
      int? countFirst = null;
      Func<int, bool, TradeLevelsPreset, int, int, IList<Rate>> calcTrendLines = (start, isMin, pl, skip, count) => {
        var x = bs(start, pl, isMin, skip, count);
        if(!skipFirst.HasValue) {
          skipFirst = x.skip;
          countFirst = RatesArray.Count - x.skip;// x.count;
        }
        return x.rates;
      };

      TrendRedInt().Pairwise((s, c) => new { s })
        .Select(p => calcTrendLines(p.s, false, TradeLevelsPreset.Red, 0, ratesForCorridor.Count))
        .ForEach(tl => TrendLines = Lazy.Create(() => tl, TrendLines.Value, exc => Log = exc));

      //CorridorLengthLime = legIndexes[1];
      TrendLimeInt().Pairwise((s, c) => new { s, skip = skipFirst.Value, count = countFirst.Value })
        .ForEach(p => TrendLines0 = Lazy.Create(() => calcTrendLines(p.s, true, TradeLevelsPreset.Lime, p.skip, p.count), TrendLines0.Value, exc => Log = exc));

      //CorridorLengthGreen = legIndexes[2];
      TrendGreenInt().Pairwise((s, c) => new { s, skip = skipFirst.Value, count = countFirst.Value })
        .ForEach(p => TrendLines1 = Lazy.Create(() => calcTrendLines(p.s, true, TradeLevelsPreset.Green, p.skip, p.count), TrendLines1.Value, exc => Log = exc));

      TrendPlumInt().Pairwise((s, c) => new { s, skip = skipFirst.Value, count = countFirst.Value })
        .ForEach(p => TrendLines3 = Lazy.Create(() => calcTrendLines(p.s, true, TradeLevelsPreset.Plum, p.skip, p.count), TrendLines3.Value, exc => Log = exc));

      CorridorLengthBlue = ratesForCorridor.Count;
      TrendLines2 = Lazy.Create(() => CalcTrendLines(CorridorLengthBlue, tagTL(TradeLevelsPreset.Blue)), TrendLines2.Value, exc => Log = exc);

      var ratesForCorr = _ratesArrayCoeffs.Take(1)
        .Select(_ => {
          var redRates = RatesArray.GetRange(RatesArray.Count - 2, 2);
          redRates.Reverse();
          WaveShort.Rates = redRates;
          return new { redRates, trend = new { StDev = 0, Coeffs = new[] { 0.0, 0.0 } } };
        })
      .ToArray();

      GetShowVoltageFunction()();
      return ratesForCorr.Select(x => new CorridorStatistics(this, x.redRates, x.trend.StDev, x.trend.Coeffs)).FirstOrDefault();
    }

    public bool IsCorridorFrozen() {
      return CorridorStartDate.HasValue || (TradeConditionsEvalStartDate().Any() && SuppRes.Any(sr => sr.CanTrade));
    }


    #region GreenRedBlue
    static int[] _greenRedBlueDefault = new[] { 2, 2, 2 };
    int[] _greenRedBlue = _greenRedBlueDefault;
    private string _GreenRedBlue;
    [Category(categoryCorridor)]
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
              _GreenRedBlue = string.Join(MULTI_VALUE_SEPARATOR, _greenRedBlueDefault);
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
      get { lock(_waveRangesLocker) return _waveRanges; }
      set {
        lock(_waveRangesLocker) {
          _waveRanges = value;
          WaveRangesGRB = value.Take(_greenRedBlue.Sum()).ToList();
        }
      }
    }
    WaveRange _waveRangeSum = new WaveRange(0);
    public WaveRange WaveRangeSum {
      get { return _waveRangeSum; }
      set { _waveRangeSum = value ?? new WaveRange(0); }
    }

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
    Lazy<IList<Rate>> _trendLines0 = new Lazy<IList<Rate>>(() => _trenLinesEmptyRates);
    public Lazy<IList<Rate>> TrendLines0 {
      get { return _trendLines0; }
      private set { _trendLines0 = value; }
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
    Lazy<IList<Rate>> _trendLines3 = new Lazy<IList<Rate>>(() => _trenLinesEmptyRates);
    public Lazy<IList<Rate>> TrendLines3 {
      get { return _trendLines3; }
      private set { _trendLines3 = value; }
    }
    private double MaGapMax(IList<Rate> rates) {
      return rates.Skip(rates.Count.Div(1.1).ToInt()).Max(r => r.PriceCMALast.Abs(r.PriceAvg));
    }
    #region CmaRatioForWaveLength
    private double _CmaRatioForWaveLength = 0;
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsCorridorCMA)]
    [DisplayName("CmaRatioFor WL")]
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
    private int? _BarsCountCalc = null;
    [DisplayName("Bars Count Calc(45,360,..)")]
    [Category(categoryCorridor)]
    [Dnr]
    public int BarsCountCalc {
      get {
        return BarsCountDate.HasValue
          ? UseRatesInternal(BarCountByDate).DefaultIfEmpty(() => BarsCountCalc).First()
          : _BarsCountCalc.GetValueOrDefault(BarsCount);
      }
      set {
        if(_BarsCountCalc == value)
          return;

        IsRatesLengthStable = RatesArray.Count.Ratio(value) < 1.05;

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

    WaveRange _waveRangeTail = new WaveRange(0);

    public WaveRange WaveRangeTail {
      get { return _waveRangeTail; }
      set { _waveRangeTail = value ?? new WaveRange(0); }
    }


    public double WaveHeightPower { get; set; }
  }
}
