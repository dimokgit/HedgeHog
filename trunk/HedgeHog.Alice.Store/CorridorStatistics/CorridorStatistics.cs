using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;
using HedgeHog;
using HedgeHog.Shared;
using System.Reactive.Subjects;
using NotifyCollectionChangedWrapper;
using System.Collections.ObjectModel;

namespace HedgeHog.Alice.Store {
  public class CorridorStatistics : HedgeHog.Models.ModelBase {
    public class LegInfo {
      public double Slope { get; set; }
      public double _pointSize = double.NaN;
      private double _angle = double.NaN;
      public double Angle {
        get { return _angle; }
        set { _angle = value; }
      }
      public double CalcAngle(double pointSize) {
        if (double.IsNaN(pointSize)) throw new ArgumentException("Must be positive number.", "pointSize");
        if (pointSize == _pointSize) return _angle;
        _pointSize = pointSize;
        Angle = Slope.Angle(pointSize);
        return Angle;
      }
      public Rate Rate1 { get; set; }
      public Rate Rate2 { get; set; }
      public LegInfo(Rate rateBase, Rate rateOther, TimeSpan interval,double pointSize = double.NaN)
        : this(rateBase, new[] { rateOther }, interval,pointSize) {
      }
      public LegInfo(Rate rateBase, Rate[] ratesOther, TimeSpan interval, double pointSize = double.NaN) {
        this.Rate1 = rateBase;
        Rate rate2;
        this.Slope = CalculateSlope(rateBase, ratesOther, interval, out rate2);
        this.Rate2 = rate2;
        if (false) {
          var rates = new[] { rateBase, ratesOther[0] }.OrderBars().ToArray();
          var y = rates[1].PriceAvg > rates[0].PriceAvg ? rates[1].PriceHigh - rates[0].PriceLow : rates[1].PriceLow - rates[0].PriceHigh;
          var x = (rates[1].StartDate - rates[0].StartDate).TotalMinutes / interval.TotalMinutes;
          this.Slope = y / x;
        }
        if (!double.IsNaN(pointSize))
          CalcAngle(pointSize);
      }
      static double CalculateSlope(Rate rate1, ICollection<Rate> rates2, TimeSpan interval,out Rate rate) {
        var slopes = new List<Tuple<Rate,double>>();
        rates2.ToList().ForEach(r => {
          slopes.Add(new Tuple<Rate, double>(r, CalculateSlope(rate1, r, interval)));
        });
        var t = slopes.OrderByDescending(s => s.Item2.Abs()).First();
        rate = t.Item1;
        return t.Item2;
      }
      static double CalculateSlope(Rate rate1, Rate rate2, TimeSpan interval) {
        var rates = new[] { rate1, rate2 }.OrderBars().ToArray();
        var y = rates[1].PriceAvg > rates[0].PriceAvg ? rates[1].PriceHigh - rates[0].PriceLow : rates[1].PriceLow - rates[0].PriceHigh;
        var x = (rates[1].StartDate - rates[0].StartDate).TotalMinutes / interval.TotalMinutes;
        return y / x;

      }
    }
    NotifyCollectionChangedWrapper<LegInfo> _LegInfos;
    public NotifyCollectionChangedWrapper<LegInfo> LegInfos {
      get {
        if (_LegInfos == null) {
          _LegInfos = new NotifyCollectionChangedWrapper<LegInfo>(new ObservableCollection<LegInfo>());
          _LegInfos.CollectionChanged += LegInfos_CollectionChanged;
        }
        return _LegInfos;
      }
    }
    ~CorridorStatistics() {
      if (_LegInfos != null)
        _LegInfos.CollectionChanged -= LegInfos_CollectionChanged;
    }
    void LegInfos_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      RaisePropertyChanged(() => LegsAngleAverage);
    }
    public void LegInfosClear() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(() => LegInfos.Clear());
    }
    public LegInfo LegInfosAdd(Rate rate1, Rate rate2, TimeSpan interval) {
      var li = new LegInfo(rate1, rate2, interval);
      GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(() => LegInfos.Add(li));
      return li;
    }

    public double LegsAngleAverage {
      get {
        if (LegInfos.Count == 0) return double.NaN;
        return LegInfos.Average(li => li.Angle.Abs());
      }
    }

    public double[] priceLine { get; set; }
    public Func<Rate,double> priceHigh { get; set; }
    public Func<Rate,double> priceLow { get; set; }

    private double _StDev = double.NaN;
    public double StDev {
      get { return _StDev; }
      set {
        if (_StDev == value) return;
        _StDev = value;
        RaisePropertyChanged(() => StDev);
      }
    }
    public double Slope { get; private set; }
    public LineInfo LineLow { get; private set; }
    public LineInfo LineHigh { get; private set; }

    public TrendLevel TrendLevel {
      get {
        if (LineLow == null) return Store.TrendLevel.None;
        return  LineLow.Slope.Error(Slope) < LineHigh.Slope.Error(Slope) ? TrendLevel.Support : TrendLevel.Resistance;
      }
    }

    public Rate[] GetRates(IEnumerable<Rate> rates) { return rates.Skip(rates.Count() - Periods).ToArray(); }

    double _HeightUp0= double.NaN;
    public double HeightUp0 {
      get { return _HeightUp0; }
      private set { _HeightUp0 = value; }
    }
    double _HeightUp = double.NaN;
    public double HeightUp {
      get { return _HeightUp; }
      set { _HeightUp = value; }
    }

    double _HeightDown0 = double.NaN;
    public double HeightDown0 {
      get { return _HeightDown0; }
      private set { _HeightDown0 = value; }
    }
    double _HeightDown = double.NaN;
    public double HeightDown {
      get { return _HeightDown; }
      set { _HeightDown = value; }
    }
    public double HeightUpDown0 { get { return HeightUp0 + HeightDown0; } }
    public double HeightUpDown0InPips { get { return TradesManagerStatic.InPips(HeightUpDown0,_pipSize) ; } }
    public double HeightUpDown { get { return HeightUp + HeightDown; } }
    public double HeightUpDownInPips { get { return TradesManagerStatic.InPips(HeightUpDown, _pipSize); } }

    public double HeightUpDown0ToSpreadRatio { get { return HeightUpDown0 / Spread; } }

    public DateTime EndDate { get; private set; }

    private DateTime _StartDate;
    public DateTime StartDate {
      get { return _StartDate; }
      private set {
        if (_StartDate != value) {
          _StartDate = value;
          RaisePropertyChanged("StartDate");
        }
      }
    }

    public int Periods { get; set; }
    public int Iterations { get; set; }

    public CorridorStatistics() {

    }
    public CorridorStatistics(ICollection<Rate> rates, double stDev, double[] coeffs, double heightUp0, double heightDown0, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate) {
      Init(rates,stDev, coeffs, heightUp0, heightDown0, heightUp, heightDown, lineHigh, lineLow, periods, endDate, startDate, 0,0);
    }

    public void Init(CorridorStatistics cs,double pipSize) {
      this._pipSize = pipSize;
      this.priceLine = cs.priceLine;
      this.priceHigh = cs.priceHigh;
      this.priceLow = cs.priceLow;
      Init(cs.Rates, cs.StDev, cs.Coeffs, cs.HeightUp0, cs.HeightDown0, cs.HeightUp, cs.HeightDown, cs.LineHigh, cs.LineLow, cs.Periods, cs.EndDate, cs.StartDate, cs.Iterations, cs.CorridorCrossesCount);
      this.Spread = cs.Spread;
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
        LegInfos.Clear();
        LegInfos.AddRange(cs.LegInfos);
      });
      RaisePropertyChanged(
        () => HeightUpDown0, () => HeightUpDown, () => HeightUpDown0InPips, () => HeightUpDownInPips, () => HeightUpDown0ToSpreadRatio);
    }

    public void Init(ICollection<Rate> rates, double stDev, double[] coeffs, double heightUp0, double heightDown0, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate, int iterations, int corridorCrossesCount) {
      this.Rates = rates;
      this.StDev = stDev;
      this.LineHigh = lineHigh;
      this.LineLow = lineLow;
      this.EndDate = endDate;
      this.Coeffs = coeffs;
      this.Slope = rates.IsReversed() ? -coeffs[1] : coeffs[1];
      this.Periods = periods;
      this.Iterations = iterations;
      this.HeightUp = heightUp;
      this.HeightUp0 = heightUp0;
      this.HeightDown = heightDown;
      this.HeightDown0 = heightDown0;
      this.CorridorCrossesCount = corridorCrossesCount;
      this.Corridornes = rates.Select(r => r.Spread).ToArray().AverageByIterations(2, low: true).Average() / rates.Height();
      this.Density = rates.Density();
      // Must the last one
      this.StartDate = startDate;
      RaisePropertyChanged("Height");
      RaisePropertyChanged("HeightInPips");
    }


    #region CorridorFib
    private double _buyStopByCorridor;
    public double BuyStopByCorridor {
      get { return _buyStopByCorridor; }
      protected set {
        _buyStopByCorridor = value;
        RaisePropertyChanged("BuyStopByCorridor");
      }
    }

    private double _sellStopByCorridor;
    public double SellStopByCorridor {
      get { return _sellStopByCorridor; }
      protected set {
        _sellStopByCorridor = value;
        RaisePropertyChanged("SellStopByCorridor");
      }
    }

    private double _CorridorFibInstant;
    public double CorridorFibInstant {
      get { return _CorridorFibInstant; }
      set {
        if (_CorridorFibInstant != value) {
          _CorridorFibInstant = value;
          CorridorFib = value;
          RaisePropertyChanged("CorridorFibInstant");
        }
      }
    }

    private double _CorridorFib = double.NaN;
    public double CorridorFib {
      get { return _CorridorFib; }
      set {
        if (value != 0 && _CorridorFib != value) {
          //_CorridorFib = Lib.CMA(_CorridorFib, 0, TicksPerMinuteMinimum, Math.Min(99, value.Abs()) * Math.Sign(value));
          _CorridorFib = Lib.Cma(_CorridorFib, CorridorFibCmaPeriod, value);
          CorridorFibAverage = _CorridorFib;
          RaisePropertyChanged("CorridorFib");
        }
      }
    }

    private double _CorridorFibAverage = double.NaN;
    public double CorridorFibAverage {
      get { return _CorridorFibAverage; }
      set {
        if (value != 0 && _CorridorFibAverage != value) {
          _CorridorFibAverage = Lib.Cma(_CorridorFibAverage, CorridorFibCmaPeriod, value);
          RaisePropertyChanged("CorridorFibAverage");
        }
      }
    }

    public void SetCorridorFib(double buyStop, double sellStop, double cmaPeriod) {
      var cfiMax = 500;
      CorridorFibCmaPeriod = cmaPeriod;
      BuyStopByCorridor = Math.Max(0, buyStop);
      SellStopByCorridor = Math.Max(0, sellStop);
      var cf = BuyStopByCorridor == 0 ? -bigFibAverage.Average()
                : SellStopByCorridor == 0 ? bigFibAverage.Average()
                : Fibonacci.FibRatioSign(BuyStopByCorridor, SellStopByCorridor);
      CorridorFibInstant = cf > 0 ? Math.Min(cfiMax, cf) : Math.Max(-cfiMax, cf);
      if (CorridorFibInstant > 100 && CorridorFibInstant != bigFibAverage.Last()) {
        if (bigFibAverage.Count > 20) bigFibAverage.Dequeue();
        bigFibAverage.Enqueue(CorridorFibInstant);
      }
    }
    void ClearCorridorFib() {
      _CorridorFibInstant = _CorridorFibAverage = _CorridorFib = 0;
    }
    public double CorridorFibCmaPeriod { get; set; }

    double _corridornes;
    public double Corridornes {
      get { return _corridornes; }
      set {
        if (_corridornes == value) return;
        _corridornes = value;
        RaisePropertyChanged("Corridornes");
        RaisePropertyChanged("MinutesBack");
      }
    }

    Func<double, double> _InPips = null;

    private double _FibMinimum = 1;
    public double FibMinimum {
      get { return _FibMinimum; }
      set {
        if (_FibMinimum != value) {
          _FibMinimum = value;
          RaisePropertyChanged("FibMinimum");
        }
      }
    }

    #endregion


    Queue<double> bigFibAverage = new Queue<double>(new[] { 100.0 });

    public int IsCurrentInt { get { return Convert.ToInt32(IsCurrent); } }
    bool _IsCurrent;
    public bool IsCurrent {
      get { return _IsCurrent; }
      set {
        if (_IsCurrent != value) {
          _IsCurrent = value;
          RaisePropertyChanged("IsCurrent");
          RaisePropertyChanged("IsCurrentInt");
        }
      }
    }



    public Store.TradingMacro TradingMacro { get; set; }
    public CorridorStatistics(Store.TradingMacro tradingMacro) {
      this.TradingMacro = tradingMacro;
    }

    bool IsAngleOk(bool buy) {
      var tm = TradingMacro;
      var a = TradingMacro.TradingAngleRange;
      if (!tm.TradeByAngle) return TradingMacro.CorridorAngle.Abs() <= a;
      return (tm.TradeAndAngleSynced? buy:!buy) ? TradingMacro.CorridorAngle > a : TradingMacro.CorridorAngle < -a;
    }

    Func<Rate, Rate, Rate> peak = (ra, rn) => new[] { ra, rn }.OrderBy(r=>r.PriceHigh).Last();
    Func<Rate, Rate, Rate> valley = (ra, rn) => new[] { ra, rn }.OrderBy(r=>r.PriceLow).First();
    private bool? lastSignal;


    public double[] Coeffs { get; set; }
    private int _CorridorCrossesCount;
    public int CorridorCrossesCount {
      get { return _CorridorCrossesCount; }
      set {
        if (_CorridorCrossesCount != value) {
          _CorridorCrossesCount = value;
          RaisePropertyChanged("CorridorCrossesCount");
        }
      }
    }


    ICollection<Rate> _Rates = new List<Rate>();

    public ICollection<Rate> Rates {
      get { return _Rates; }
      set { _Rates = value; }
    }

    private double _Spread;
    private double _pipSize;
    public double Spread {
      get { return _Spread; }
      set {
        if (_Spread != value) {
          _Spread = value;
          RaisePropertyChanged(() => Spread, () => SpreadInPips);
        }
      }
    }
    public double SpreadInPips { get { return TradesManagerStatic.InPips(Spread, _pipSize); } }


    public double Density { get; set; }
  }

  public enum TrendLevel { None, Resistance, Support }
}