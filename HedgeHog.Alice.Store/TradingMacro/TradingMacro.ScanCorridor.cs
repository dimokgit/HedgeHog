﻿using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog.Bars;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using ReactiveUI;
using static HedgeHog.MonoidsCore;
using static HedgeHog.IEnumerableCore;
using HedgeHog.Shared;
using RI = System.Tuple<HedgeHog.Bars.Rate, int>;
using TL = HedgeHog.Bars.Rate.TrendLevels;
using TLS = System.Lazy<System.Collections.Generic.IList<HedgeHog.Bars.Rate>>;
using static HedgeHog.MathCore;
using static HedgeHog.Core.JsonExtensions;
using ReactiveUI.Legacy;

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
    private const double TLsOverlap = 0.9;

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

    private CorridorStatistics ScanCorridorBy12345(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorBy12345(true, ratesForCorridor, priceHigh, priceLow);
    }
    private CorridorStatistics ScanCorridorByAll5(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      return ScanCorridorBy12345(null, ratesForCorridor, priceHigh, priceLow);
    }

    struct RateGroup {
      public IList<Tuple<Rate, int>> Range { get; }
      public Tuple<Rate, int>[] MinMax { get; }
      public double Avg { get; }
      public double Min { get; }
      public double Max { get; }
      public int Index { get; }
      public DateTime StartDate { get; }
      public double Distance { get; private set; }

      public static double Miner(RI r) => r.Item1.BidLow;
      public static double Maxer(RI r) => r.Item1.AskHigh;

      public int Count { get; }

      public RateGroup(IList<Tuple<Rate, int>> range) {
        try {
          Range = range;
          MinMax = range.MinMaxBy(r => r.Item1.BidLow, r => r.Item1.AskHigh);
          Avg = range.Average(r => r.Item1.PriceAvg);
          Min = MinMax[0].Item1.BidLow;
          Max = MinMax[1].Item1.AskHigh;
          Index = range[0].Item2;
          StartDate = range[0].Item1.StartDate;
          Distance = range.Distances(r => r.Item1.PriceAvg).Last().Item2;
          Count = range.Count;
        } catch {
          throw;
        }
      }
      public static RateGroup Create(IList<RI> range) => new RateGroup(range);
    }
    #region GroupRatesCount
    private int _GroupRatesCount = 2000;
    [Category(categoryCorridor)]
    [WwwSetting(wwwSettingsTrends)]
    public int GroupRatesCount {
      get { return _GroupRatesCount; }
      set {
        if(_GroupRatesCount != value) {
          _GroupRatesCount = value;
          OnPropertyChanged("GroupRatesCount");
        }
      }
    }

    #endregion
    private static List<RateGroup> GroupRatesImpl(IList<Rate> ratesForCorridor, int sampleMin) {
      var buffrerSize = ratesForCorridor.Count.Div(sampleMin).Ceiling().Max(2);
      var grouped = ratesForCorridor.Select(Tuple.Create<Rate, int>)
        .Buffer(buffrerSize, buffrerSize - 1)
        .Where(b => b.Count > 1)
        .Select(RateGroup.Create).ToList();
      return grouped;
    }

    private CorridorStatistics ScanCorridorBy12345(bool? skipAll, IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
        var ratesArray = ratesForCorridor.Where(r => !r.PriceCMALast.IsNaN()).ToList();
        var ratesForCorr = _ratesArrayCoeffs.Take(1)
          .Select(_ => {
            var redRates = ratesArray.GetRange(ratesArray.Count - 2, 2);
            redRates.Reverse();
            WaveShort.Rates = redRates;
            return new { redRates, trend = new { StDev = 0, Coeffs = new[] { 0.0, 0.0 } } };
          })
        .ToArray();
      if(TrendLinesTrendsAll.Any()) {
        if(ratesArray.Count < 60)
          return ratesForCorr.Select(x => new CorridorStatistics(this, x.redRates, x.trend.StDev, x.trend.Coeffs)).FirstOrDefault();

        bool mustResetAllTrendLevels = true || _mustResetAllTrendLevels;
        _mustResetAllTrendLevels = false;
        var grouped = GroupRates(ratesArray);
        var distanceTotal = grouped.Sum(rg => rg.Distance);

        #region Funcs
        Func<RI, double> miner = ri => ri.Item1.BidLow;
        Func<RI, double> maxer = ri => ri.Item1.AskHigh;
        Func<TradeLevelsPreset, double, Func<TL, TL>> tagTL = (pl, dist) => tl => {
          tl.Color = pl + "";
          tl.Distance = dist;
          return tl;
        };
        var def = ToFunc((IList<Rate> rts, int skip, int count, double dist) => new { rates = rts, skip, count, dist });
        var anonDef = def(new Rate[0], 0, 0, 0.0);
        var bs = ToFunc((int perc, TradeLevelsPreset pl, bool isMin, int[][] skips) => {
          if(perc <= 0)
            return new[] { anonDef }.AsSingleable();
          var skip = skips.Where(s => s.Length == 1).Concat().ToArray();
          var digits = Digits();
          var skip1 = skip.FirstOrDefault();
          var grouped2 = grouped.SkipWhile(r => r.Index < skip1).ToList();
          if(grouped2.Count <= 1)
            return new[] { anonDef }.Take(0).AsSingleable();
          var skipRanges = skip.Any() ? new int[0][] : skips.Where(sr => sr.Any()).ToArray();
          var distances = grouped2.RunningSum(rg => rg.Distance).Select(t => t.Map((rg, d) => new { rg, d })).ToList();
          var distChunc = distanceTotal / 100.0 * perc;
          var res = Partitioner.Create(Enumerable.Range(0, distances.Count).ToArray(), true)
          .AsParallel()
          .Select(i => {
            var distStart = distances[i].d;
            var i2 = i + 1;
            var min = distances[i].rg.MinMax[0];
            var max = distances[i].rg.MinMax[1];
            var distCurr = double.NaN;
            while(i2 < distances.Count && (distCurr = distances[i2].d - distStart) < distChunc) {
              if(distances[i2].rg.MinMax[0].Item1.BidLow < min.Item1.BidLow)
                min = distances[i2].rg.MinMax[0];
              else if(distances[i2].rg.MinMax[1].Item1.AskHigh > max.Item1.AskHigh)
                max = distances[i2].rg.MinMax[1];
              i2++;
            }
            var isOut = i2 >= distances.Count;
            var start = grouped2[i].Index;
            var end = grouped2[i2].With(rg => rg.Index + rg.Count);
            var height = (maxer(max).Max(maxer(grouped2[i].MinMax[1]), maxer(grouped2[i2].MinMax[1]))
            - miner(min).Min(miner(grouped2[i].MinMax[0]), miner(grouped2[i2].MinMax[0])))
            .RoundBySqrt((digits - 1).Max(0));
            return new { start = start, count = end - start, startEnd = new[] { start, end }, height, isOut, i, i2, dist = distCurr };
          })
          .TakeWhile(x => !x.isOut)
          .Where(x => (from sr in skipRanges where x.startEnd.DoSetsOverlap(sr) select 0).IsEmpty())
          .Where(x => skipRanges.Concat().Where(i => i.Between(x.startEnd[0], x.startEnd[1])).IsEmpty())
          .AsEnumerable();

          res = isMin
          ? res.MinByOrEmpty(x => x.height)
          : res.MaxByOrEmpty(x => x.height);

          res = UseFlatTrends
          ? res.OrderBy(x => grouped2.GetRange(x.i, x.i2 - x.i).LinearSlope(y => y.Avg).Abs())
          : res.OrderByDescending(x => x.start);

          return res
          .Take(1)
          .Select(x => {
            var tlRates = CalcTrendLines(x.start, x.count, tagTL(pl, x.dist), !isMin && false);
            var tl = IsTrendsEmpty(tlRates);
            return def(tlRates, x.start, x.count, x.dist);
          })
          .AsSingleable();
        });

        #endregion

        var skipEmpty = new int[0][];
        var skipFirst = skipEmpty;
        Func<int, bool, TradeLevelsPreset, int[][], Singleable<IList<Rate>>> calcTrendLines = (start, isMin, pl, skip) => {
          return bs(start, pl, isMin, skip)
          .Do(x => {
            if(x.rates.Any())
              if(skipAll.HasValue) {
                if(!skipFirst.Any() || skipAll.Value)
                  skipFirst = new[] { new[] { x.skip } };
              }
          })
          .Select(x => x.rates)
          .AsSingleable();
        };

        Func<TLS, TLS, bool> isNewTL = (tlOld, tlNew) =>
        mustResetAllTrendLevels || (
          IsRatesLengthStable &&
          IsTrendsEmpty(tlOld).With(tl => tl.IsEmpty ||
          IsTrendsEmpty(tlNew).With(tl2 => !tl2.IsEmpty && tl.EndDate < tl2.EndDate)));
        Action<TLS, TLS, Action> setTLs = (tlOld, tlNew, setter) => {
          if(isNewTL(tlOld, tlNew)) {
            setter();
          }
        };
        Func<TL, int[]> tlStartIndex0 = tl => ratesArray.FuzzyIndex(tl.StartDate, (d, r1, r2) => d.Between(r1.StartDate, r2.StartDate));
        var tlStartIndex = tlStartIndex0.Memoize(tl => tl.StartDate);
        Func<TLS, int[]> skipByTL = tls => IsTrendsEmpty(tls)
        .With(tl => tl.IsEmpty ? new int[0] : tlStartIndex(tl))
        .ToArray();

        TrendBlueInt().Pairwise((s, c) => new { s })
          .Where(x => x.s != 0)
          .SelectMany(p =>
            calcTrendLines(p.s.Abs(), p.s > 0, TradeLevelsPreset.Blue, mustResetAllTrendLevels ? new int[0][] : new[] { skipByTL(TrendLines2) })
            .Concat(() => calcTrendLines(p.s.Abs(), true, TradeLevelsPreset.Blue, skipEmpty))
            )
          .DefaultIfEmpty(new Rate[0])
          .Take(1)
          .Select(tl => Lazy.Create(() => tl, TrendLines2.Value, exc => Log = exc))
          .ForEach(tl => setTLs(TrendLines2, tl, () => TrendLines2 = tl));
        var overlapSlack = TLsOverlap - 1;
        Func<TL, bool?> isTLMinMax = tl => (TrendLinesMinMax.SingleOrDefault(t => t.Item1.Color == tl.Color).Item2);
        Func<TL, TL[]> tlsPrev = tl => TrendLinesTrendsAll
          .Skip(TrendLinesTrendsAll.ToList().IndexOf(tl) + 1)
          .SkipWhile(tl0 => isTLMinMax(tl0) != isTLMinMax(tl))
          .ToArray();
        Func<TL, int[]> tlRange = tl => tlStartIndex(tl).With(ii => ii.Select(i => new[] { i, i + tl.Count }.SlackRange(overlapSlack)).Concat()).ToArray();
        Func<IEnumerable<TL>, int[][]> tlRangesPrev = tls => tls.Select(tl => tlRange(tl)).ToArray();
        Func<TLS, int[][]> tlRanges = tls => IsTrendsEmpty2(tls).SelectMany(tl => tlRangesPrev(tlsPrev(tl))).ToArray();
        Func<int[], Action<TLS>, TLS, TradeLevelsPreset, Singleable<IList<Rate>>> doTL = (ints, tl, tlDef, color)
           => ints.Pairwise((s, c) => new { s = s.Abs(), skip = skipFirst.IfEmpty(() => tlRanges(tlDef))/*.DefaultIfEmpty(skipByTL(tlDef))*/.ToArray(), isMin = s > 0 })
           .Where(x => x.s > 0)
           .SelectMany(p => calcTrendLines(p.s, p.isMin, color, p.skip).Concat(() => calcTrendLines(p.s, p.isMin, color, skipEmpty)).Take(1))
           .IfEmpty(() => new[] { tlDef.Value })
           .Do(ctl => tl(Lazy.Create(() => ctl)))
           .AsSingleable();

        (
          //from td in TradeConditionHasAny(BlueAngOk).DefaultIfEmpty(TradeDirections.Both)
          //where td.HasAny()
          from tlr in doTL(TrendRedInt(), tl => setTLs(TrendLines, tl, () => TrendLines = tl), TrendLines, TradeLevelsPreset.Red)
          from tlp in doTL(TrendPlumInt(), tl => setTLs(TrendLines3, tl, () => TrendLines3 = tl), TrendLines3, TradeLevelsPreset.Plum)
          from tlg in doTL(TrendGreenInt(), tl => setTLs(TrendLines1, tl, () => TrendLines1 = tl), TrendLines1, TradeLevelsPreset.Green)
            //from tll in doTL(TrendLimeInt(), tl => setTLs(TrendLines0, tl, () => TrendLines0 = tl), TrendLines0, TradeLevelsPreset.Lime)
        select true
         ).Count();

        TrendLinesTrendsAll
          .Permutation((tl1, tl2) => tlRange(tl1).IsEmpty() || tlRange(tl2).IsEmpty() || tlRange(tl1).DoSetsOverlap(overlapSlack, tlRange(tl2)))
          .Where(b => b)
          .Take(1)
          .ForEach(_ => _mustResetAllTrendLevels = true);
        {
          var endDates = Trends.Skip(1).Where(tl => tl.Color != null && tl.Color != TradeLevelsPreset.Blue + "" && !tl.IsEmpty)
            .OrderBy(tl => tl.EndDate)
            //.DefaultIfEmpty(TLBlue)
            .Select(a => new { a.EndDate, a.Count })
            .ToList();
          double trendToRatesRatio = TrendRanges.Select(i => i[0].Abs()).Where(i => i.Between(1, 98)).DefaultIfEmpty(0).Min() / 100.0;
          var distances = grouped.RunningSum(rg => rg.Distance).Select(t => t.Map((rg, Distance) => new { rg.Range.First().Item1.StartDate, Index = rg.Range.First().Item2, Distance }));
          var maxDistance = distanceTotal * (1 - trendToRatesRatio);
          var dateMaxs = distances.SkipWhile(g => g.Distance <= maxDistance).Take(1);
          var ii = (from ed in endDates
                    from dateMax in dateMaxs
                    where ed.EndDate < dateMax.StartDate
                    from i in ratesArray.FuzzyIndex(ed.EndDate, (d, p, n) => d.Between(p.StartDate, n.StartDate))
                    select (i - ed.Count * .2).ToInt()
                    )
                    .Take(1)
                    .Concat(dateMaxs.Select(dm => dm.Index))
                    .Take(1)
                    .ToArray();
          Trends2
            .Where(tl => tl.TL.IsNullOrEmpty())
            .Select(t => t.Set)
            .Take(1)
            .Zip(ii, (tl, i) => (tl, trend: CalcTrendLines(ratesArray.GetRange(i, ratesArray.Count - i), _ => _)))
            .ForEach(t => t.tl(t.trend));
        }
      }
      GetShowVoltageFunction()();
      GetShowVoltageFunction(VoltageFunction2, 1)();
      
      return ratesForCorr.Select(x => new CorridorStatistics(this, x.redRates, x.trend.StDev, x.trend.Coeffs)).FirstOrDefault();
    }

    private CorridorStatistics ScanCorridorByM1Wave(List<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var xxx = (from ra in UseRates(ra => ra)
                 from r in ra.Take(1)
                 from wrs in TradingMacroM1(tm => tm.WaveRanges)
                 from wr in wrs.TakeWhile(wr => wr.StartDate >= r.StartDate).IfEmpty(() => wrs.Take(1))
                 orderby wr.Distance descending
                 select ra.SkipWhile(r => r.StartDate < wr.StartDate).TakeWhile(r => r.StartDate <= wr.EndDate)
                 )
                 .Take(1)
                 .Concat();
      TrendLines2 = Lazy.Create(() => CalcTrendLines(xxx.ToList(), tl => tl), TrendLines2.Value, exc => Log = exc);

      var ratesForCorr = _ratesArrayCoeffs.Take(1)
        .Select(_ => {
          var redRates = RatesArray.GetRange(RatesArray.Count - 2, 2);
          redRates.Reverse();
          WaveShort.Rates = redRates;
          return new { redRates, trend = new { StDev = 0, Coeffs = new[] { 0.0, 0.0 } } };
        })
      .ToArray();

      GetShowVoltageFunction()();
      GetShowVoltageFunction(VoltageFunction2, 1)();
      return ratesForCorr.Select(x => new CorridorStatistics(this, x.redRates, x.trend.StDev, x.trend.Coeffs)).FirstOrDefault();
    }

    private bool _mustResetAllTrendLevels;

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
          ? UseRatesInternal(BarCountByDate).RunIfEmpty(() => BarsCountCalc).First()
          : _BarsCountCalc.GetValueOrDefault(BarsCount);
      }
      set {
        if(_BarsCountCalc == value) {
          IsRatesLengthStable = true;
          return;
        }

        var newBarsCount = value == 0 ? (int?)null : value.Min(RatesInternal.Count);
        IsRatesLengthStable = RatesArray.Count.Ratio(newBarsCount.GetValueOrDefault()) < 1.05;
        OnPropertyChanged("BarsCountCalc");
        _BarsCountCalc = newBarsCount;
        if(_BarsCountCalc.HasValue) {
          BarsCountDate = null;
          if(_barsCount < BarsCount)
            throw new Exception(new { newBarsCount, Is = "Less Then", BarsCount } + "");
          //Log = new Exception(new { BarsCountCalc, BarsCountDate } + "");
        }
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
