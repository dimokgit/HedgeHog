using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;

namespace HedgeHog.Alice.Store {
  public class CorridorStatistics : HedgeHog.Models.ModelBase {

    public Func<Rate,double> priceLine { get; set; }
    public Func<Rate,double> priceHigh { get; set; }
    public Func<Rate,double> priceLow { get; set; }

    public double Density { get; set; }
    public double Slope { get; set; }
    public LineInfo LineLow { get; set; }
    public LineInfo LineHigh { get; set; }

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
      set { _HeightUp0 = value; }
    }
    double _HeightUp;
    public double HeightUp {
      get { return _HeightUp; }
      set { _HeightUp = value; }
    }

    double _HeightDown0;
    public double HeightDown0 {
      get { return _HeightDown0; }
      set { _HeightDown0 = value; }
    }
    double _HeightDown;
    public double HeightDown {
      get { return _HeightDown; }
      set { _HeightDown = value; }
    }
    public double HeightUpDown0 { get { return HeightUp0 + HeightDown0; } }
    public double HeightUpDown { get { return HeightUp + HeightDown; } }
    public double HeightUpDownInPips0 { get { return InPips == null ? 0 : InPips(HeightUpDown0); } }
    public double HeightUpDownInPips { get { return InPips == null ? 0 : InPips(HeightUpDown); } }

    public double HeightHigh {
      get { return TradingMacro.RatesLast.Where(r => r.PriceAvg1 != 0).Select(r => r.PriceAvg2-r.PriceAvg1).FirstOrDefault(); }
    }
    
    public double HeightLow {
      get { return TradingMacro.RatesLast.Where(r => r.PriceAvg1 != 0).Select(r => r.PriceAvg1 - r.PriceAvg3).FirstOrDefault(); }
    }

    public TradeDirections TradeDirection {
      get { return TradeDirections.None;/* HeightHigh > HeightLow ? TradeDirections.Up : TradeDirections.Down;*/ }
    }

    public double HeightInPips { get { return InPips == null ? 0 : InPips(Height); } }
    public DateTime EndDate { get; set; }

    private DateTime _StartDate;
    public DateTime StartDate {
      get { return _StartDate; }
      set {
        if (_StartDate != value) {
          _StartDate = value;
          OnPropertyChanged("StartDate");
        }
      }
    }

    public int Periods { get; set; }
    public int Iterations { get; set; }
    public double Height {
      get { return TradingMacro.RatesLast.Where(r => r.PriceAvg1 != 0).Select(r => r.PriceAvg2 - r.PriceAvg3).FirstOrDefault(); }
    }

    public double Thinness { get { return Periods / HeightUpDownInPips; } }

    public CorridorStatistics() {

    }
    public CorridorStatistics(double density, double slope, double heightUp0, double heightDown0, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate) {
      Init(density, slope, heightUp0, heightDown0, heightUp, heightDown, lineHigh, lineLow, periods, endDate, startDate, 0);
    }

    public void Init(double density, double slope, double heightUp0, double heightDown0, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate, int iterations) {
      this.Density = density;
      this.LineHigh = lineHigh;
      this.LineLow = lineLow;
      this.EndDate = endDate;
      // Mast go before StartDate
      this.Slope = slope;
      this.StartDate = startDate;
      this.Periods = periods;
      this.Iterations = iterations;
      this.HeightUp = heightUp;
      this.HeightUp0 = heightUp0;
      this.HeightDown = heightDown;
      this.HeightDown0 = heightDown0;
      //Corridornes = TradingMacro.CorridorCalcMethod == Models.CorridorCalculationMethod.Density ? Density : 1 / Density;
      Corridornes = Density;
      OnPropertyChanged("Height");
      OnPropertyChanged("HeightInPips");
    }


    #region CorridorFib
    private double _buyStopByCorridor;
    public double BuyStopByCorridor {
      get { return _buyStopByCorridor; }
      protected set {
        _buyStopByCorridor = value;
        OnPropertyChanged("BuyStopByCorridor");
      }
    }

    private double _sellStopByCorridor;
    public double SellStopByCorridor {
      get { return _sellStopByCorridor; }
      protected set {
        _sellStopByCorridor = value;
        OnPropertyChanged("SellStopByCorridor");
      }
    }

    private double _CorridorFibInstant;
    public double CorridorFibInstant {
      get { return _CorridorFibInstant; }
      set {
        if (_CorridorFibInstant != value) {
          _CorridorFibInstant = value;
          CorridorFib = value;
          OnPropertyChanged("CorridorFibInstant");
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
          OnPropertyChanged("CorridorFib");
        }
      }
    }

    private double _CorridorFibAverage;
    public double CorridorFibAverage {
      get { return _CorridorFibAverage; }
      set {
        if (value != 0 && _CorridorFibAverage != value) {
          _CorridorFibAverage = Lib.CMA(_CorridorFibAverage, double.MinValue, CorridorFibCmaPeriod, value);
          OnPropertyChanged("CorridorFibAverage");
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
        OnPropertyChanged("Corridornes");
        OnPropertyChanged("MinutesBack");
      }
    }

    Func<double, double> _InPips = null;

    public Func<double, double> InPips {
      get { return _InPips; }
      set { _InPips = value; }
    }

    private double _FibMinimum;
    public double FibMinimum {
      get { return _FibMinimum; }
      set {
        if (_FibMinimum != value) {
          _FibMinimum = value;
          OnPropertyChanged("FibMinimum");
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
          OnPropertyChanged("IsCurrent");
          OnPropertyChanged("IsCurrentInt");
        }
      }
    }



    public Store.TradingMacro TradingMacro { get; set; }
    public CorridorStatistics(Store.TradingMacro tradingMacro) {
      this.TradingMacro = tradingMacro;
    }

    bool? _TradeSignal;
    public bool? TradeSignal {
      get {
        Func<bool?> tradeSignal10 = () => {
          if (TradingMacro.CorridorAngle > 0 && PriceCmaDiffHigh > 0) return true;
          if (TradingMacro.CorridorAngle > 0 && PriceCmaDiffLow < 0) return true;
          if (TradingMacro.CorridorAngle < 0 && PriceCmaDiffLow < 0) return false;
          if (TradingMacro.CorridorAngle < 0 && PriceCmaDiffHigh > 0) return false;
          return null;
        };
        //var ts = tradeSignal9();
        bool? ts = null;// breakOutLocker();
        if (ts != _TradeSignal)
          OnPropertyChanged("TradeSignal");
        _TradeSignal = ts;
        return _TradeSignal;
      }
    }
    public bool? OpenSignal {
      get {
        bool? b;
        var m = GetTradingHeight(TradingMacro.CorridorHeightMultiplier);
        switch (TradingMacro.Strategy & (Strategies.Range | Strategies.Breakout | Strategies.Brange)) {
          case Strategies.Breakout:
            b = OpenBreakout(-m,1000);
            break;
          case Strategies.Range:
            b = OpenRange(-m,1000);
            break;
          case Strategies.Brange:
            b = OpenBrange(0, 1000);
            break;
          default: return null;
        }
        if (b.HasValue) {
          if (b.Value && (!canBuy || IsSellLock)) return null;
          if (!b.Value && (!canSell || IsBuyLock)) return null;
          return lastSignal = b;
        }
        return null;
      }
    }

    bool IsSellLock { get { return TradingMacro.IsSellLock; } }
    bool IsBuyLock { get { return TradingMacro.IsBuyLock; } }

    private double GetTradingHeight(double multiplier) {
      var m = Math.Max(1, HeightUpDownInPips) * multiplier;
      return m;
    }
    bool IsAngleOk(bool buy) {
      var tm = TradingMacro;
      var a = TradingMacro.TradingAngleRange;
      if (!tm.TradeByAngle) return TradingMacro.CorridorAngle.Abs() < a;
      return (tm.TradeAndAngleSynced? buy:!buy) ? TradingMacro.CorridorAngle > a : TradingMacro.CorridorAngle < -a;
    }

    bool canBuy { get { return TradeDirection != TradeDirections.Down && IsAngleOk(true); } }
    bool canSell { get { return TradeDirection != TradeDirections.Up && IsAngleOk(false); } }
    public bool? CloseSignal {
      get {
        if (TradingMacro.CloseOnOpen) return null;
        switch (TradingMacro.Strategy) {
          case Strategies.Range:
            var m = Math.Max(1, HeightUpDownInPips);
            var r = OpenRange(m/10, 100);
            return r.HasValue ? !r : null;
          case Strategies.Breakout:
            return CloseBreakout();
            var b = OpenBreakout(0, 100);
            return b.HasValue ? !b : null;
          case Strategies.Brange:
            var br = OpenBrange(0, 100);
            return br.HasValue ? !br : null;
        }
        return null;
      }
    }

    private bool? OpenBrange(int level, double m) {
      var or = OpenRange(level,m);
      var ob = OpenBreakout(level,m);
      //var rangeAngle = 3; if (TradingMacro.CorridorAngle.Abs() <= rangeAngle) return or;
      var breakeAngle = 8;
      if (TradingMacro.CorridorAngle > breakeAngle && (ob == false || or == false)) return false;
      if (TradingMacro.CorridorAngle < -breakeAngle && (ob == true || or == true)) return true;
      return null;
      bool? buy = new bool?(TradingMacro.CorridorAngle<0);
      return (or == buy || ob == buy) ? buy : null;
    }
    public bool? OpenRange(double level, double m) {
      if (PriceCmaDiffHigh.HasValue && InPips(PriceCmaDiffHigh.Value).Between(-level, m)) return GetSignal(false);
      if (PriceCmaDiffLow.HasValue && InPips(PriceCmaDiffLow.Value).Between(-m, level)) return GetSignal(true);
      return null;
    }
    private bool? CloseRange() {
      var rl = RateForDiffHigh;
      if (rl != null && diffPriceHigh(rl) - rl.PriceAvg1 < 0) return GetSignal(false);
      rl = RateForDiffLow;
      if (rl != null && diffPriceLow(rl) - rl.PriceAvg1 > 0) return GetSignal(true);
      return null;
    }
    bool GetSignal(bool signal) { return TradingMacro.ReverseStrategy ? !signal : signal; }
    private bool? OpenBreakout(double level, double m) {
      if (OpenRange(0, 100).HasValue) return null;
      var rates = TradingMacro.RatesLast;
      var rateLast = rates.Last();
      var ratePrev = rates[rates.Length-2];
      if( InPips(diffPriceHigh(rateLast) - rateLast.PriceAvg2).Between(-level,m) &&
         !InPips(diffPriceHigh(ratePrev) - ratePrev.PriceAvg2).Between(-level,m)&& 
         !IsSellLock) return GetSignal(true);
      //if (PriceCmaDiffHigh.HasValue && InPips(PriceCmaDiffHigh.Value).Between(-level,m) && !IsSellLock) return GetSignal(true);
      if (InPips(diffPriceLow(rateLast) - rateLast.PriceAvg3).Between(-m,level) &&
         !InPips(diffPriceLow(ratePrev) - ratePrev.PriceAvg3).Between(-m,level) &&
         !IsBuyLock) return GetSignal(false);

      //if (PriceCmaDiffLow.HasValue && InPips(PriceCmaDiffLow.Value).Between(-m, level) && !IsBuyLock) return GetSignal(false);
      return null;
      return OpenRange(level,m);
      if (true && lastSignal.HasValue) {
        if (lastSignal.Value && PriceCmaDiffLow.HasValue && InPips(PriceCmaDiffLow.Value) < m ) return GetSignal(true);
        if (!lastSignal.Value && PriceCmaDiffHigh.HasValue && InPips(PriceCmaDiffHigh.Value) > -m) return GetSignal(false);
      }
      return null;
    }
    private bool? CloseBreakout() {
      var or = OpenRange(0, 100);
      if (or.HasValue) return !or;
      if (PriceDiffHigh > 0) {
        if (!TradingMacro.ReverseStrategy)
          TradingMacro.IsBuyLock = false;
        return GetSignal(false);
      }
      if (PriceDiffLow < 0) {
        if (!TradingMacro.ReverseStrategy)
          TradingMacro.IsSellLock = false;
        return GetSignal(true);
      }
      return null;
    }
    static Func<Rate, double> diffPriceHigh = r => TradingMacro.GetPriceHigh(r);
    static Func<Rate, double> diffPriceLow = r => TradingMacro.GetPriceHigh(r);
    public double? PriceCmaDiffHigh {
      get {
        //var extreamHigh = LineHigh == null?null:  LineHigh.Line.OrderBars().LastOrDefault();
        //if (extreamHigh != null) return extreamHigh.PriceHigh - extreamHigh.PriceAvg2;
        var rl = RateForDiffHigh;
        return rl == null ? (double?)null : TradingMacro.GetPriceHigh(rl) - rl.PriceAvg2;
      }
    }
    public double? PriceDiffHigh {
      get {
        var rl = RateForDiffHigh;
        return rl == null ? (double?)null : TradingMacro.GetPriceHigh(rl) - rl.PriceAvg1;
      }
    }
    public double? PriceDiffLow {
      get {
        var rl = RateForDiffLow;
        return rl == null ? (double?)null : TradingMacro.GetPriceLow(rl) - rl.PriceAvg1;
      }
    }

    Func<Rate, Rate, Rate> peak = (ra, rn) => new[] { ra, rn }.OrderBy(r=>r.PriceHigh).Last();
    Func<Rate, Rate, Rate> valley = (ra, rn) => new[] { ra, rn }.OrderBy(r=>r.PriceLow).First();
    private bool? lastSignal;

    private Rate RateForDiffHigh {
      get {
        var rates = TradingMacro.RatesLast.Where(r => r.PriceAvg1 > 0).ToArray();
        return rates.OrderBy(r => r.PriceHigh).LastOrDefault();
        var rm = rates.OrderBy(r => r.PriceHigh).FirstOrDefault();
        var rh = rates.FindExtreams(peak,1).DefaultIfEmpty(rm).First();
        return rh;
      }
    }

    public double? PriceCmaDiffLow {
      get {
        //var extreamLow = LineLow == null?null: LineLow.Line.OrderBars().LastOrDefault();
        //if (extreamLow != null) return extreamLow.PriceLow - extreamLow.PriceAvg3;
        var rl = RateForDiffLow;
        var res = rl == null ? (double?)null : diffPriceLow(rl) - rl.PriceAvg3;
        return res;
      }
    }
    private Rate RateForDiffLow {
      get {
        var rates = TradingMacro.RatesLast.Where(r => r.PriceAvg1 > 0).ToArray();
        return rates.OrderBy(r => r.PriceLow).FirstOrDefault();
        var rm = rates.OrderBy(r => r.PriceLow).LastOrDefault();
        var rh = rates.FindExtreams(valley,1).DefaultIfEmpty(rm).First();
        return rh;
      }
    }
  }

  public enum TrendLevel { None, Resistance, Support }
}