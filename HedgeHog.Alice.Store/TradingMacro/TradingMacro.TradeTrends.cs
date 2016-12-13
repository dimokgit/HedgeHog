using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    static Lazy<IList<Rate>> _trenLinesEmptyRates = new Lazy<IList<Rate>>(()=> new Rate[] { new Rate { Trends = Rate.TrendLevels.Empty }, new Rate { Trends = Rate.TrendLevels.Empty } });
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
    static int[][] _trendsCountByRange =new int[][] {
      new[]{ 33,2 },
      new[]{ 20,3 },
      new[]{ 14,4 },
      new[]{ 11,5 },
    };
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

    #region TrendsAll
    private int _TrendsAll = 0;
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTrends)]
    [DisplayName("Trends ALL")]
    public int TrendsAll {
      get { return _TrendsAll; }
      set {
        Action<int[]> setTrends = ii => {
          var ratio = ii[0] + "";
          var count = ii[1];
          _trendSetters
            .Select((ts, i) => new { ts, i })
            .ForEach(x => x.ts(x.i < count ? ratio : ""));
        };
        if(_TrendsAll != value) {
          _TrendsAll = value;
          _trendsCountByRange
            .Where(i => value > 0 && value <= i[0])
            .TakeLast(1)
            .Select(ii => new[] { value, ii[1] })
            .ForEach(setTrends);
          OnPropertyChanged(nameof(TrendsAll));
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
          _tradeTrends = (TrendLinesTrendsAll.Length - 1).ToString();
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
        OnPropertyChanged(nameof(TrendBlue));
      }
    }
  }
}
