using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Bars;
using TL = HedgeHog.Bars.Rate.TrendLevels;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    static Lazy<IList<Rate>> _trenLinesEmptyRates = new Lazy<IList<Rate>>(() => new Rate[] { new Rate { Trends = Rate.TrendLevels.Empty }, new Rate { Trends = Rate.TrendLevels.Empty } });
    Lazy<IList<Rate>> _trendLines = _trenLinesEmptyRates;
    public Lazy<IList<Rate>> TrendLines {
      get { return _trendLines; }
      private set { _trendLines = value; }
    }
    Lazy<IList<Rate>> _trendLines0 = _trenLinesEmptyRates;
    public Lazy<IList<Rate>> TrendLines0 {
      get { return _trendLines0; }
      private set { _trendLines0 = value; }
    }
    Lazy<IList<Rate>> _trendLines1 = _trenLinesEmptyRates;
    public Lazy<IList<Rate>> TrendLines1 {
      get { return _trendLines1; }
      private set { _trendLines1 = value; }
    }
    Lazy<IList<Rate>> _trendLines2 = _trenLinesEmptyRates;
    public Lazy<IList<Rate>> TrendLines2 {
      get { return _trendLines2; }
      private set { _trendLines2 = value; }
    }
    Lazy<IList<Rate>> _trendLines3 = _trenLinesEmptyRates;
    public Lazy<IList<Rate>> TrendLines3 {
      get { return _trendLines3; }
      private set { _trendLines3 = value; }
    }

    int[] TrendInts(IEnumerable<int> ints) {
      return ints.Concat(new[] { 0, -1 }).Take(2).ToArray();
    }
    Action<string>[] _trendSetters {
      get {
        return new Action<string>[] {
              (r)=>TrendBlue=r+"",
              (r)=>TrendRed=r+"",
              (r)=>TrendPlum=r+"",
              (r)=>TrendGreen=r+"",
              (r)=>TrendLime=r+""
      };
      }
    }
    public TL[] Trends => new[] { TLLime, TLGreen, TLPlum, TLRed, TLBlue };
    (string TL, Action<IList<Rate>> Set)[] Trends2 => new (string, Action<IList<Rate>>)[] {
      ( TrendLime, tl => TLLime2(tl)),
      (TrendGreen, tl => TLGreen2(tl)),
      (TrendPlum, tl => TLPlum2(tl)),
      (TrendRed, tl => TLRed2(tl)),
      (TrendBlue, tl => TLBlue2(tl))
    };//, TLGreen, TLPlum, TLRed, TLBlue };
    int[][] TrendRanges => new[] { TrendLimeInt(), TrendGreenInt(), TrendPlumInt(), TrendRedInt(), TrendBlueInt() };
    public static TL IsTrendsEmpty(Lazy<IList<Rate>> trends) {
      if(trends == null)
        return TL.Empty;
      var v = trends.Value;
      return IsTrendsEmpty(v);
    }

    public static TL IsTrendsEmpty(IList<Rate> v) {
      return v == null || v.IsEmpty() ? TL.Empty : v.Skip(1).Select(r => r.Trends).LastOrDefault() ?? TL.Empty;
    }

    public static IEnumerable<TL> IsTrendsEmpty2(Lazy<IList<Rate>> trends) {
      if(trends == null)
        yield break;
      var v = trends.Value;
      if(v == null || v.IsEmpty())
        yield break;
      foreach(var tl in v.Skip(1).Select(r => r.Trends).Where(tl => !tl.IsEmpty))
        yield return tl;
    }
    IEnumerable<TL> TrendLinesNotEmpty { get { return TrendLinesTrendsAll.Where(tl => !tl.IsEmpty); } }

    void TLBlue2(IList<Rate> tl) => TrendLines2 = Lazy.Create(() => tl);
    void TLGreen2(IList<Rate> tl) => TrendLines1 = Lazy.Create(() => tl);
    void TLLime2(IList<Rate> tl) => TrendLines0 = Lazy.Create(() => tl);
    void TLRed2(IList<Rate> tl) => TrendLines = Lazy.Create(() => tl);
    void TLPlum2(IList<Rate> tl) => TrendLines3 = Lazy.Create(() => tl);

    public TL TLBlue { get { return IsTrendsEmpty(TrendLines2); } }
    public TL TLGreen { get { return IsTrendsEmpty(TrendLines1); } }
    public TL TLLime { get { return IsTrendsEmpty(TrendLines0); } }
    public TL TLRed { get { return IsTrendsEmpty(TrendLines); } }
    public TL TLPlum { get { return IsTrendsEmpty(TrendLines3); } }
    public IEnumerable<Tuple<TL, bool, int>> TrendLinesMinMax {
      get {
        var ints = new Func<string>[] { () => TrendLime, () => TrendGreen, () => TrendPlum, () => TrendRed, () => TrendBlue }
          .Select(a => !string.IsNullOrWhiteSpace(a()));
        var minMax = MonoidsCore.ToFunc((int perc) => new { perc, isMin = perc > 0 });
        var minMaxs = TrendRanges.Select(i => minMax(i[0])).ToArray();
        var isOk = MonoidsCore.ToFunc((bool ok, TL tl) => new { ok, tl });
        var isMin = MonoidsCore.ToFunc(isOk(false, null), minMax(0), (x, mm) => new { x.ok, x.tl, mm.isMin, mm.perc });
        return ints.Zip(Trends, isOk).Zip(minMaxs, isMin).Where(t => t.ok).Select(t => Tuple.Create(t.tl, t.isMin, t.perc));
      }
    }
    public TL[] TrendLinesTrendsAll {
      get {
        var ints = new[] { TrendLime, TrendGreen, TrendPlum, TrendRed, TrendBlue }.Select(string.IsNullOrWhiteSpace);
        var isOk = MonoidsCore.ToFunc((bool isEmpty, TL tl) => new { ok = !isEmpty, tl });
        return ints.Zip(Trends, isOk).Where(t => t.ok).Select(t => t.tl).ToArray();
      }
    }
    IEnumerable<TL> TrendsByDate => Trends.Where(t => !t.IsEmpty).OrderBy(tl => tl.EndDate);
    IList<TL> TrendLinesByDate => TrendLinesTrendsAll.OrderBy(tl => tl.EndDate).ToArray();
    public TL[] TrendLinesFlat { get { return TrendLinesTrendsAll.SkipLast(1).ToArray(); } }
    public IEnumerable<TL> TradeTrendLines =>
        TradeTrendsInt.Select(i => Trends[i])
          .IfEmpty(() => TrendLinesTrendsAll.Where(tl => !tl.IsEmpty).OrderByDescending(tl => tl.EndDate).Take(1));
    public double TradeTrendLinesAvg(Func<TL, double> selector) {
      return TradeTrendLines.ToArray(selector)
        .Permutation()
        .Select(t => t.Item1.ToPercent(t.Item2))
        //.OrderBy(d=>d)
        //.Take(3)
        .DefaultIfEmpty(0)
        .RootMeanPower(0.5)
        .ToInt();
    }
    private static Func<TradingMacro, double> TradeTrendPrice(Func<TL, double> price) {
      return tm => tm.TradeTrendLines.Select(price).Single();
    }
    private static Func<TradingMacro, double> TradeTrendsPriceMax(Func<TL, double> price) {
      return tm => tm.TradeTrendLines.Select(price).OrderByDescending(d => d).DefaultIfEmpty(double.NaN).First();
    }
    private static Func<TradingMacro, double> TradeTrendsPriceMin(Func<TL, double> price) {
      return tm => tm.TradeTrendLines.Select(price).OrderBy(d => d).DefaultIfEmpty(double.NaN).First();
    }

    #region TrendsAll
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrends)]
    [DisplayName("Trends ALL")]
    public int TrendsAll {
      get { return 0; }
      set {
        if(0 != value) {
          var s = value.Sign();
          var i = value.Abs();
          var c = (100.0 / TLsOverlap / (i * 2 + 1)).Floor() * s;
          _trendSetters.Take(i).ForEach(a => a(c + ""));
          _trendSetters.Skip(i).ForEach(a => a(""));
        }
      }
    }

    #endregion
    string _tradeTrends = "0,1,2,3";
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTradingCorridor)]
    [Description("0,1,2")]
    [DisplayName("Trade Trends")]
    public string TradeTrends {
      get { return _tradeTrends; }
      set {
        if(_tradeTrends == value)
          return;
        _tradeTrends = value;
        if(string.IsNullOrWhiteSpace(_tradeTrends))
          _tradeTrends = "";
        TradeTrendsInt = SplitterInt(_tradeTrends);
      }
    }

    int[] _tradeTrendsInt;
    public int[] TradeTrendsInt {
      get { return _tradeTrendsInt ?? (_tradeTrendsInt = SplitterInt(TradeTrends)); }
      set { _tradeTrendsInt = value; }
    }

    string _trendPlum = "1,3";
    int[] TrendPlumInt(string s = null) { return TrendInts((s ?? _trendPlum).Splitter(',').Select(int.Parse)); }
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrends)]
    [Description("0(start),2(count)")]
    public string TrendPlum {
      get { return _trendPlum; }
      set {
        if(_trendPlum == value)
          return;
        TrendPlumInt(_trendPlum = value);
        TrendLines3 = _trenLinesEmptyRates;
        TLPlum.ClearRates();
        OnPropertyChanged(nameof(TrendPlum));
      }
    }

    string _trendLime = "0,1";
    int[] TrendLimeInt(string s = null) { return TrendInts((s ?? _trendLime).Splitter(',').Select(int.Parse)); }
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrends)]
    [Description("0(start),2(count)")]
    public string TrendLime {
      get { return _trendLime; }
      set {
        if(_trendLime == value)
          return;
        TrendLimeInt(_trendLime = value);
        TrendLines0 = _trenLinesEmptyRates;
        TLLime.ClearRates();
        OnPropertyChanged(nameof(TrendLime));
      }
    }

    string _trendGreen = "0,2";
    int[] TrendGreenInt(string s = null) { return TrendInts((s ?? _trendGreen).Splitter(',').Select(int.Parse)); }
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrends)]
    [Description("0(start),2(count)")]
    public string TrendGreen {
      get { return _trendGreen; }
      set {
        if(_trendGreen == value)
          return;
        TrendGreenInt(_trendGreen = value);
        TrendLines1 = _trenLinesEmptyRates;
        TLGreen.ClearRates();
        OnPropertyChanged(nameof(TrendGreen));
      }
    }

    string _trendRed = "0,4";
    int[] TrendRedInt(string s = null) { return TrendInts((s ?? _trendRed).Splitter(',').Select(int.Parse)); }
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrends)]
    [Description("0(start),2(count)")]
    public string TrendRed {
      get { return _trendRed; }
      set {
        if(_trendRed == value)
          return;
        TrendRedInt(_trendRed = value);
        TrendLines = _trenLinesEmptyRates;
        TLRed.ClearRates();
        OnPropertyChanged(nameof(TrendRed));
      }
    }

    string _trendBlue = "0,7";
    int[] TrendBlueInt(string s = null) { return TrendInts((s ?? _trendBlue).Splitter(',').Select(int.Parse)); }
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrends)]
    [Description("0(start),2(count)")]
    public string TrendBlue {
      get { return _trendBlue; }
      set {
        if(_trendBlue == value)
          return;
        TrendBlueInt(_trendBlue = value);
        TrendLines2 = _trenLinesEmptyRates;
        TLBlue.ClearRates();
        OnPropertyChanged(nameof(TrendBlue));
      }
    }
  }
}
