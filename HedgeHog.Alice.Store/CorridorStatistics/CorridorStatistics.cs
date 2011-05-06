using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Store {
  public class CorridorStatistics : HedgeHog.Models.ModelBase {

    public Func<Rate,double> priceLine { get; set; }
    public Func<Rate,double> priceHigh { get; set; }
    public Func<Rate,double> priceLow { get; set; }

    public double Density { get; private set; }
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

    public bool AdjustHeight(IEnumerable<Rate> rates, Func<CorridorStatistics, bool> filter, int iterationsStart, int iterationsEnd = 10) {
      while (!filter(this)) {
        GetRates(rates).GetCorridorHeights(
          priceLine, priceHigh, priceLow, CorridorStaticBaseExtentions.priceHeightComparer, 1, ++iterationsStart, out _HeightUp, out _HeightDown);
        if (iterationsStart > iterationsEnd) break;
      }
      return filter(this);
    }

    double _HeightUp0;
    public double HeightUp0 {
      get { return _HeightUp0; }
      private set { _HeightUp0 = value; }
    }
    double _HeightUp;
    public double HeightUp {
      get { return _HeightUp; }
      set { _HeightUp = value; }
    }

    double _HeightDown0;
    public double HeightDown0 {
      get { return _HeightDown0; }
      private set { _HeightDown0 = value; }
    }
    double _HeightDown;
    public double HeightDown {
      get { return _HeightDown; }
      set { _HeightDown = value; }
    }
    public double HeightUpDown0 { get { return HeightUp0 + HeightDown0; } }
    public double HeightUpDown { get { return HeightUp + HeightDown; } }

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
    public CorridorStatistics(double density, double[] coeffs, double heightUp0, double heightDown0, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate) {
      Init(density, coeffs, heightUp0, heightDown0, heightUp, heightDown, lineHigh, lineLow, periods, endDate, startDate, 0,0);
    }

    public void Init(double density, double[] coeffs, double heightUp0, double heightDown0, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate, int iterations, int corridorCrossesCount) {
      this.Density = density;
      this.LineHigh = lineHigh;
      this.LineLow = lineLow;
      this.EndDate = endDate;
      this.Coeffs = coeffs;
      this.Slope = coeffs[1];
      this.Periods = periods;
      this.Iterations = iterations;
      this.HeightUp = heightUp;
      this.HeightUp0 = heightUp0;
      this.HeightDown = heightDown;
      this.HeightDown0 = heightDown0;
      this.Corridornes = Density;
      this.CorridorCrossesCount = corridorCrossesCount;
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

    private double _CorridorFib;
    public double CorridorFib {
      get { return _CorridorFib; }
      set {
        if (value != 0 && _CorridorFib != value) {
          //_CorridorFib = Lib.CMA(_CorridorFib, 0, TicksPerMinuteMinimum, Math.Min(99, value.Abs()) * Math.Sign(value));
          _CorridorFib = Lib.CMA(_CorridorFib, double.MinValue, CorridorFibCmaPeriod, value);
          CorridorFibAverage = _CorridorFib;
          RaisePropertyChanged("CorridorFib");
        }
      }
    }

    private double _CorridorFibAverage;
    public double CorridorFibAverage {
      get { return _CorridorFibAverage; }
      set {
        if (value != 0 && _CorridorFibAverage != value) {
          _CorridorFibAverage = Lib.CMA(_CorridorFibAverage, double.MinValue, CorridorFibCmaPeriod, value);
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


    public Rate[] Rates { get; set; }
  }

  public enum TrendLevel { None, Resistance, Support }
}