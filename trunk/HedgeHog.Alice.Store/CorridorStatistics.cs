using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;

namespace HedgeHog.Alice.Store {
  public class CorridorStatistics : HedgeHog.Models.ModelBase {

    public double AverageHigh { get; set; }
    public double AverageLow { get; set; }
    public double AverageHeight { get { return AverageHigh - AverageLow; } }
    public double Density { get; set; }
    double _AskHigh;

    public double AskHigh {
      get { return _AskHigh; }
      set {
        var h = Height;
        _AskHigh = value;
        //if (Height / h > 1.5) ClearCorridorFib();
      }
    }
    double _BidLow;

    public double BidLow {
      get { return _BidLow; }
      set {
        var h = Height;
        _BidLow = value;
        //if (Height / h > 1.5) ClearCorridorFib();
      }
    }
    public double Height { get { return AskHigh - BidLow; } }
    public double HeightInPips { get { return InPips == null ? 0 : InPips(Height); } }
    public DateTime EndDate { get; set; }
    public DateTime StartDate { get; set; }
    public int Periods { get; set; }
    public int Iterations { get; set; }
    public double HeightByRegression {
      get { return TradingMacro.RatesLast.Where(r => r.PriceAvg1 != 0).Select(r => r.PriceAvg2 - r.PriceAvg3).FirstOrDefault(); }
    }

    public CorridorStatistics() {

    }
    public CorridorStatistics(double density, double averageHigh, double averageLow, double askHigh, double bidLow, int periods, DateTime endDate, DateTime startDate) {
      Init(density, averageHigh, averageLow, askHigh, bidLow, periods, endDate, startDate, 0);
    }

    public void Init(double density, double averageHigh, double averageLow, double askHigh, double bidLow, int periods, DateTime endDate, DateTime startDate, int iterations) {
      this.Density = density;
      this.AverageHigh = averageHigh;
      this.AverageLow = averageLow;
      if (TradingMacro != null) {
        var n = 2;
        if (this.AverageHeight > TradingMacro.CorridorHeightMinimum * n) AverageHeightCurrentMinimum = this.AverageHeight;
        else if (this.AverageHeightCurrentMinimum > this.AverageHeight)
          this.AverageHeightCurrentMinimum = this.AverageHeight;
      }
      this.AskHigh = askHigh;
      this.BidLow = bidLow;
      this.EndDate = endDate;
      this.StartDate = startDate;
      this.Periods = periods;
      this.Iterations = iterations;
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
          OnPropertyChanged("TradeSignal");
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
        OnPropertyChanged("IsCorridornessOk");
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
          OnPropertyChanged("TradeSignal");
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
    public bool IsCorridornessOk {
      get { return Corridornes <= TradingMacro.CorridornessMin; }
    }
    bool? _TradeSignal;
    public bool? TradeSignal {
      get {
        Func<bool?> tradeSignal8 = () => {
          //if (!IsCorridorAvarageHeightOk) return null;
          var sell = InPips(PriceCmaDiffHigh).Round(1) >= 0;
          var buy = InPips(PriceCmaDiffLow).Round(1) <= 0;
          if (buy && sell) return null;
          var doSell = sell && TradingMacro.RateDirection < 0;// || TradingMacro.BarHeight60 > 0 && PriceCmaDiffLow >= TradingMacro.BarHeight60;
          var doBuy = buy && TradingMacro.RateDirection > 0;// || TradingMacro.BarHeight60 > 0 && (-PriceCmaDiffHigh) >= TradingMacro.BarHeight60;
          return doSell == doBuy ? (bool?)null : doBuy;
        };
        Func<bool?> tradeSignal9 = () => {
          var ret = tradeSignal8();
          if (ret.HasValue) return ret;
          var sell = PriceCmaDiffHigh.Abs();
          var buy = PriceCmaDiffLow.Abs();
          return sell == buy ? (bool?)null
            : sell / buy < .1 && TradingMacro.RateDirection < 0 ? false : buy / sell < .1 && TradingMacro.RateDirection > 0 ? true : (bool?)null;
        };
        Func<bool?> tradeSignal10 = () => {
          if (AverageHeight / AverageHeightCurrentMinimum < 1.1) return null;
          var sell = InPips(PriceCmaDiffHigh).Round(1) >= 0;
          var buy = InPips(PriceCmaDiffLow).Round(1) <= 0;
          if (buy && sell) return null;
          var doSell = buy;
          var doBuy = sell;
          return doSell == doBuy ? (bool?)null : doBuy;
        };
        //var ts = tradeSignal10();
        var ts = tradeSignal8();
        if (ts != _TradeSignal)
          OnPropertyChanged("TradeSignal");
        _TradeSignal = ts;
        return _TradeSignal;
      }
    }
    public bool? OpenSignal {
      get {
        var ratioTreshold = 1;
        if (!TradeSignal.HasValue) return null;
        if (TradeSignal.Value) return (TradingMacro.CorridorAngle < -2 /*|| HeightsRatio >= ratioTreshold*/) ? TradeSignal : null;
        if (!TradeSignal.Value) return (TradingMacro.CorridorAngle > 2 /*|| HeightsRatio >= ratioTreshold*/) ? TradeSignal : null;
        return null;
      }
    }

    public bool? CloseSignal {
      get {
        if (TradeSignal.HasValue) {
          return !TradeSignal;
          if (TradeSignal.Value) return (TradingMacro.CorridorAngle < 3 /*|| HeightsRatio >= ratioTreshold*/) ? false : (bool?)null;
          if (!TradeSignal.Value) return (TradingMacro.CorridorAngle > -3 /*|| HeightsRatio >= ratioTreshold*/) ? true : (bool?)null;
        }
        //if (TradingMacro.CorridorAngle < 0) return false;
        //if (TradingMacro.CorridorAngle > 0) return true;
        return null;
      }
    }


    //double priceCmaForAverageHigh { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.PriceCurrent.Ask; } }
    double priceCmaForAverageHigh {
      get {
        var rl = RateForDiffHigh;
        return rl == null ? 0 : rl.PriceHigh;
      }
    }
    //double priceCmaForAverageLow { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.PriceCurrent.Bid; } }
    double priceCmaForAverageLow { 
      get {
        var rl = RateForDiffLow;
        return rl == null ? 0 : rl.PriceLow;
      }
    }

    //public double PriceCmaDiffHigh { get { return priceCmaForAverageHigh - AverageHigh; } }
    //public double PriceCmaDiffLow { get { return priceCmaForAverageLow - AverageLow; } }
    public double PriceCmaDiffHigh {
      get {
        var rl = RateForDiffHigh;
        return rl == null ? 0 : rl.PriceHigh - rl.PriceAvg2;
      }
    }

    private Rate RateForDiffHigh {
      get {
        return TradingMacro.RatesLast.Where(r => r.PriceAvg1 > 0).OrderBy(r => r.PriceHigh).LastOrDefault();
      }
    }
    public double PriceCmaDiffLow {
      get {
        var rl = RateForDiffLow;
        var res = rl == null ? 0 : rl.PriceLow - rl.PriceAvg3;
        return res;
      }
    }

    private Rate RateForDiffLow {
      get {
        return TradingMacro.RatesLast.Where(r => r.PriceAvg1 > 0).OrderBy(r => r.PriceLow).FirstOrDefault();
      }
    }

    public double HeightsRatio { get { return HeightByRegression / AverageHeight; } }

    public bool IsCorridorAvarageHeightOk {
      get {
        var addOn = 0;
        //TradingMacro.LimitCorridorByBarHeight ? 0 :
        //  PriceCmaDiffHigh > 0
        //  ? /*TradingMacro.PriceCmaDiffHighFirst*/ +TradingMacro.PriceCmaDiffHighLast
        //  : PriceCmaDiffLow < 0 ? /*-TradingMacro.PriceCmaDiffLowFirst*/ -TradingMacro.PriceCmaDiffLowLast
        //  : 0;
        return TradingMacro.CorridorHeightMinimum > 0
               && AverageHeight >= TradingMacro.CorridorHeightMinimum
               //&& HeightByRegression >= TradingMacro.CorridorHeightMinimum
               //&& TradingMacro.CorridorAngle.Abs() < 2
               //&& HeightsRatio >= .7
               ;
        return GetCorridorAverageHeightOk(TradingMacro, AverageHeight + Math.Max(0, addOn), AverageHeightCurrentMinimum);
      }
    }
    public static bool GetCorridorAverageHeightOk(TradingMacro tm, double averageHeight, double averageHeightCurrentMinimum) {
      return averageHeight > 0 && tm.CorridorHeightMinimum > 0 && averageHeightCurrentMinimum > 0
        //&& AverageHeight / tm.CorridorHeightMinimum >= .9;
        && averageHeightCurrentMinimum <= tm.CorridorHeightMinimum;
    }
    private double _AverageHeightCurrentMinimum;
    public double AverageHeightCurrentMinimum {
      get { return _AverageHeightCurrentMinimum; }
      set {
        if (_AverageHeightCurrentMinimum != value) {
          _AverageHeightCurrentMinimum = value;
          OnPropertyChanged("AverageHeightCurrentMinimum");
        }
      }
    }
  }
  public static class CorridorStaticBaseExtentions {
    public static Dictionary<int, CorridorStatistics> GetCorridornesses(this IEnumerable<Rate> rates, bool useStDev) {
      try {
        if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
        else rates = rates.ToArray();
        var corridornesses = new Dictionary<int, CorridorStatistics>();
        if (rates.Count() < 0) {
          var source = Enumerable.Range(1, rates.Count() - 1);
          source.AsParallel().ForAll(i => {
            try {
              var cs = ScanCorridor(rates.Take(i).ToArray(), useStDev);
              lock (corridornesses) {
                corridornesses.Add(i, cs);
              }
            } catch (Exception) {
              //              Debug.Fail(exc + "");
            }
          });
        } else
          for (var periods = (rates.Count()*.01).ToInt(); periods < rates.Count(); periods++) {
            var cs = ScanCorridorWithAngle(rates.Take(periods).ToArray(), useStDev);
            //if (cs.Corridornes < corridornessMinimum && cs.Height >= corridorHeightMinimum)
            corridornesses.Add(periods, cs);
          }
        return corridornesses;
      } catch (Exception exc) {
        Debug.Fail(exc + "");
        throw;
      }
    }

    public static CorridorStatistics ScanCorridornesses(this IEnumerable<Rate> rates, int iterations, Dictionary<int, CorridorStatistics> corridornesses, double corridornessMinimum, double corridorHeightMinimum) {
      if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
      Func<CorridorStatistics, double, bool> filter =
        (cs, dm) => cs.Density > dm && cs.Corridornes < corridornessMinimum && cs.Height >= corridorHeightMinimum;
      var values = corridornesses.Values;
      var corrAverage = values.Average(c => c.Density);
      var corrAfterAverage = corridornesses.Where(c => filter(c.Value, corrAverage)).ToArray();

      for (var i = iterations - 1; i > 0 && corrAfterAverage.Count() > 0; i--) {
        var avg = corrAfterAverage.Average(c => c.Value.Density);
        var corrAvg = corrAfterAverage.Where(c => filter(c.Value, avg)).ToArray();
        if (corrAvg.Length > 0) {
          corrAverage = avg;
          corrAfterAverage = corrAvg;
        } else break;
      }
      if (corrAfterAverage.Count() == 0) corrAfterAverage = corridornesses.ToArray();
      //var corr = corrAfterAverage.OrderBy(c => c.Key).Last();
      var corr = corrAfterAverage.OrderBy(c => c.Value.AverageHeight).Last();
      corr.Value.Iterations = iterations;
      //var ratesForCorridor = rates.Take(corr.Value.Periods);
      ////.Where(r => r.StartDate >= startDate).ToArray();
      //corr.Value.AskHigh = ratesForCorridor.Max(r => r.AskHigh);
      //corr.Value.BidLow = ratesForCorridor.Min(r => r.BidLow);
      return corr.Value;
    }
    static CorridorStatistics ScanCorridor(IEnumerable<Rate> rates, bool useStDev) {
      try {
        var averageHigh = rates.Average(r => r.PriceHigh);
        var averageLow = rates.Average(r => r.PriceLow);
        var askHigh = rates.Max(r => r.PriceAvg/*.AskHigh*/);
        var bidLow = rates.Min(r => r.PriceAvg/*.BidLow*/);
        if (useStDev) {
          var values = new List<double>();
          rates.ToList().ForEach(r => values.AddRange(new[] { r.PriceHigh, r.PriceLow, r.PriceOpen, r.PriceClose }));
          return new CorridorStatistics(1 / values.StdDev(), averageHigh, averageLow, askHigh, 0, rates.Count(), rates.First().StartDate, rates.Last().StartDate);
        }
        var count = 0.0;// rates.Count(rate => rate.PriceLow <= averageHigh && rate.PriceHigh >= averageLow);
        //foreach (var rate in rates)
        rates.AsParallel().ForAll(rate => {
          if (rate.PriceLow <= averageHigh && rate.PriceHigh >= averageLow) count++;
        });
        var ratesForAverageHigh = rates.Where(rate => rate.PriceLow >= averageHigh).AsParallel().ToArray();
        var ratesForAverageLow = rates.Where(rate => rate.PriceHigh <= averageLow).AsParallel().ToArray();
        if (ratesForAverageHigh.Length > 0) {
          var prices = ratesForAverageHigh.Select(r => r.PriceHigh/*.PriceLow*/).ToArray();
          averageHigh = prices.Average();
          averageHigh = prices.Where(p => p >= averageHigh).DefaultIfEmpty(averageHigh).Average();
        }
        if (ratesForAverageLow.Length > 0) {
          var prices = ratesForAverageLow.Select(r => r.PriceLow/*.PriceHigh*/).ToArray();
          averageLow = prices.Average();
          averageLow = prices.Where(p => p <= averageLow).DefaultIfEmpty(averageLow).Average();
        }
        return new CorridorStatistics(count / rates.Count(), averageHigh, averageLow, askHigh, bidLow, rates.Count(), rates.First().StartDate, rates.Last().StartDate);
      } catch (Exception exc) {
        Debug.Fail(exc + "");
        throw;
      }
    }
    static CorridorStatistics ScanCorridorWithAngle(IEnumerable<Rate> rates, bool useStDev) {
      try {
        Func<Rate, double> priceGet = rate => rate.PriceAvg4;
        Action<Rate, double> priceSet = (rate,d) => rate.PriceAvg4 = d;
        rates.SetRegressionPrice(1, rate => rate.PriceAvg, priceSet);
        Func<Rate, double> priceHigh = rate => rate.PriceLow - priceGet(rate);
        Func<Rate, double> priceLow = rate => priceGet(rate) - rate.PriceHigh;
        var averageHigh = rates.Select(r => priceHigh(r)).Where(p => p > 0).Average();
        var averageLow = rates.Select(r => priceLow(r)).Where(p => p > 0).Average();
        var count = 0.0;
        rates.AsParallel().ForAll(rate => {
          if (priceLow(rate) >= averageLow || priceHigh(rate) >= averageHigh) count++;
        });
        var askHigh = rates.Max(r => r.PriceAvg/*.AskHigh*/);
        var bidLow = rates.Min(r => r.PriceAvg/*.BidLow*/);
        rates.ToList().ForEach(r => priceSet(r, 0));
        return new CorridorStatistics(count / rates.Count(), askHigh, bidLow, askHigh, bidLow, rates.Count(), rates.First().StartDate, rates.Last().StartDate);
      } catch (Exception exc) {
        Debug.Fail(exc + "");
        throw;
      }
    }
  }
}