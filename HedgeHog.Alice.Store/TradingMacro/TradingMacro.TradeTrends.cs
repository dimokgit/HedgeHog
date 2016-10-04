using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
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

    int[] TrendInts(IEnumerable<int> ints) {
      return ints.Concat(new[] { 0, -1 }).Take(2).ToArray();
    }

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
      }
    }
  }
}
