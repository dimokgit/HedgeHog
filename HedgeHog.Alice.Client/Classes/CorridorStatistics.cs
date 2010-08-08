using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Client {
  public class CorridorStatistics : HedgeHog.Models.ModelBase {
    public double AverageHigh { get; set; }
    public double AverageLow { get; set; }
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
    public Models.TradingMacro TradingMacro { get; set; }

    public CorridorStatistics(Models.TradingMacro tradingMacro) {
      this.TradingMacro = tradingMacro;
    }
    public CorridorStatistics(double density, double averageHigh, double averageLow, double askHigh, double bidLow, int periods, DateTime endDate, DateTime startDate) {
      Init(density, averageHigh, averageLow, askHigh, bidLow, periods, endDate, startDate, 0);
    }

    public void Init(double density, double averageHigh, double averageLow, double askHigh, double bidLow, int periods, DateTime endDate, DateTime startDate, int iterations) {
      this.Density = density;
      this.AverageHigh = averageHigh;
      this.AverageLow = averageLow;
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

    public bool IsCorridornessOk {
      get { return Corridornes <= TradingMacro.CorridornessMin; }
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

    public bool? TradeSignal {
      get {
        var fibInstant = CorridorFibInstant.Round(1);
        var fib = CorridorFib.Round(1);
        var fibAvg = CorridorFibAverage.Round(1);
        #region Trade Signals
        Func<bool?> tradeSignal1 = () => {
          return fibAvg < -FibMinimum && fib > fibAvg /*&& fibInstant < fib*/ ? true :
            fibAvg > +FibMinimum && fib < fibAvg /*&& fibInstant > fib*/ ? false :
            (bool?)null;
        };
        Func<bool?> tradeSignal2 = () => {
          return fibInstant < -FibMinimum && fib > fibAvg && fibInstant < fib && fib < 0 ? true :
                 fibInstant > +FibMinimum && fib < fibAvg && fibInstant > fib && fib > 0 ? false :
            (bool?)null;
        };
        Func<bool?> tradeSignal3 = () => {
          var isFibAvgOk = fibAvg.Abs() > FibMinimum / 2;
          return fib > fibAvg && fibInstant < fib && fibAvg < 0 && isFibAvgOk ? true :
                 fib < fibAvg && fibInstant > fib && fibAvg > 0 && isFibAvgOk ? false :
            (bool?)null;
        };
        Func<bool?> tradeSignal4 = () => {
          var isFibAvgOk = fibAvg.Abs() >= FibMinimum && fib.Abs() <= FibMinimum;
          return fib > fibAvg && (fibInstant < 0 && fibAvg < 0) && isFibAvgOk ? true :
                 fib < fibAvg && (fibInstant > 0 && fibAvg > 0) && isFibAvgOk ? false :
            (bool?)null;
        };
        #endregion
        Func<bool?> tradeSignal5 = () => {
          if (TradingMacro.PriceCmaCounter < TradingMacro.TicksPerMinuteMaximun * 2) return null;
          //if (!TradingMacro.IsSpeedOk) return null;
          var pdp23 = TradingMacro.PriceCma23DiffernceInPips;
          var pdp = TradingMacro.PriceCmaDiffernceInPips;
          return PriceCmaDiffHigh > 0 && pdp < 0 && pdp23 < 0 ? false :
                 PriceCmaDiffLow < 0 && pdp > 0 && pdp23 > 0 ? true :
            (bool?)null;
        };
        return tradeSignal5();
      }
    }

    double priceCmaForAverage { get { return TradingMacro.PriceCurrent.Average; } }

    public double PriceCmaDiffHigh { get { return priceCmaForAverage - AverageHigh; } }
    public double PriceCmaDiffLow { get { return priceCmaForAverage - AverageLow; } }

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
  }
  public static class Extensions {

    public static Dictionary<int, CorridorStatistics> GetCorridornesses(this IEnumerable<Rate> rates, bool useStDev) {
      if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
      var corridornesses = new Dictionary<int, CorridorStatistics>();
      for (var periods = 1; periods < rates.Count(); periods++) {
        var cs = ScanCorridor(rates.Take(periods), useStDev);
        //if (cs.Corridornes < corridornessMinimum && cs.Height >= corridorHeightMinimum)
          corridornesses.Add(periods, cs);
      }
      return corridornesses;
    }

    public static CorridorStatistics ScanCorridornesses(this IEnumerable<Rate> rates, int iterations, Dictionary<int, CorridorStatistics> corridornesses, double corridornessMinimum, double corridorHeightMinimum) {
      if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
      Func<CorridorStatistics, double, bool> filter = 
        (cs, dm) => cs.Density > dm && cs.Corridornes < corridornessMinimum && cs.Height >= corridorHeightMinimum;
      var corrAverage = corridornesses.Values.Average(c => c.Density);
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
      var corr = corrAfterAverage.OrderBy(c => c.Key).Last();
      corr.Value.Iterations = iterations;
      //var ratesForCorridor = rates.Take(corr.Value.Periods);
      ////.Where(r => r.StartDate >= startDate).ToArray();
      //corr.Value.AskHigh = ratesForCorridor.Max(r => r.AskHigh);
      //corr.Value.BidLow = ratesForCorridor.Min(r => r.BidLow);
      return corr.Value;
    }
    static CorridorStatistics ScanCorridor(IEnumerable<Rate> rates, bool useStDev) {
      var averageHigh = rates.Average(r => r.PriceHigh);
      var averageLow = rates.Average(r => r.PriceLow);
      var askHigh = rates.Max(r => r.AskHigh);
      var bidLow = rates.Min(r => r.BidLow);
      if (useStDev) {
        var values = new List<double>();
        rates.ToList().ForEach(r => values.AddRange(new[] { r.PriceHigh, r.PriceLow, r.PriceOpen, r.PriceClose }));
        return new CorridorStatistics(1 / values.StdDev(), averageHigh, averageLow, askHigh, 0, rates.Count(), rates.First().StartDate, rates.Last().StartDate);
      }
      var count = 0.0;// rates.Count(rate => rate.PriceLow <= averageHigh && rate.PriceHigh >= averageLow);
      foreach (var rate in rates)
        if (rate.PriceLow <= averageHigh && rate.PriceHigh >= averageLow) count++;
      var ratesForAverageHigh = rates.Where(rate => rate.PriceLow >= averageHigh).ToArray();
      var ratesForAverageLow = rates.Where(rate => rate.PriceHigh <= averageLow).ToArray();
      if (ratesForAverageHigh.Length > 0)
        averageHigh = ratesForAverageHigh.Average(r => r.PriceLow);
      if (ratesForAverageLow.Length > 0)
        averageLow = ratesForAverageLow.Average(r => r.PriceHigh);
      return new CorridorStatistics(count / rates.Count(), averageHigh, averageLow, askHigh, bidLow, rates.Count(), rates.First().StartDate, rates.Last().StartDate);
    }

  }
}
