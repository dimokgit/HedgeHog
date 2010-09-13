﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;

namespace HedgeHog.Alice.Store {
  public class CorridorStatistics:HedgeHog.Models.ModelBase {

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

      public CorridorStatistics() {

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
        var fibInstant = CorridorFibInstant.Round(1);
        var fib = CorridorFib.Round(1);
        var fibAvg = CorridorFibAverage.Round(1);
        #region Trade Signals
        //Func<bool?> tradeSignal1 = () => {
        //  return fibAvg < -FibMinimum && fib > fibAvg /*&& fibInstant < fib*/ ? true :
        //    fibAvg > +FibMinimum && fib < fibAvg /*&& fibInstant > fib*/ ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal2 = () => {
        //  return fibInstant < -FibMinimum && fib > fibAvg && fibInstant < fib && fib < 0 ? true :
        //         fibInstant > +FibMinimum && fib < fibAvg && fibInstant > fib && fib > 0 ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal3 = () => {
        //  var isFibAvgOk = fibAvg.Abs() > FibMinimum / 2;
        //  return fib > fibAvg && fibInstant < fib && fibAvg < 0 && isFibAvgOk ? true :
        //         fib < fibAvg && fibInstant > fib && fibAvg > 0 && isFibAvgOk ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal4 = () => {
        //  var isFibAvgOk = fibAvg.Abs() >= FibMinimum && fib.Abs() <= FibMinimum;
        //  return fib > fibAvg && (fibInstant < 0 && fibAvg < 0) && isFibAvgOk ? true :
        //         fib < fibAvg && (fibInstant > 0 && fibAvg > 0) && isFibAvgOk ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal5 = () => {
        //  if (TradingMacro.PriceCmaCounter < TradingMacro.TicksPerMinuteMaximun * 2) return null;
        //  //if (!TradingMacro.IsSpeedOk) return null;
        //  var pdp23 = TradingMacro.PriceCma23DiffernceInPips;
        //  var pdp = TradingMacro.PriceCmaDiffernceInPips;
        //  return PriceCmaDiffHigh > 0 && pdp < 0 && pdp23 < 0 ? false :
        //         PriceCmaDiffLow < 0 && pdp > 0 && pdp23 > 0 ? true :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal6 = () => {
        //  if (!IsCorridorAvarageHeightOk) return null;
        //  //if (TradingMacro.PriceCmaCounter < TradingMacro.TicksPerMinuteMaximun * 2) return null;
        //  var pdhFirst = TradingMacro.PriceCmaDiffHighWalker.CmaArray.First();
        //  var pdhLast = TradingMacro.PriceCmaDiffHighWalker.CmaArray.Last();
        //  var pdlFirst = TradingMacro.PriceCmaDiffLowWalker.CmaArray.First();
        //  var pdlLast = TradingMacro.PriceCmaDiffLowWalker.CmaArray.Last();
        //  return (pdhFirst > 0 || pdhLast > 0) && pdhFirst <= pdhLast ? false :
        //         (pdlFirst < 0 || pdlLast < 0) && pdlFirst >= pdlLast ? true :
        //    (bool?)null;
        //};
        #endregion
        Func<bool?> tradeSignal7 = () => {
          if (!IsCorridorAvarageHeightOk) return null;
          var doSell = InPips(PriceCmaDiffHigh).Round(1) >= 0;// || TradingMacro.BarHeight60 > 0 && PriceCmaDiffLow >= TradingMacro.BarHeight60;
          var doBuy = InPips(PriceCmaDiffLow).Round(1) <= 0;// || TradingMacro.BarHeight60 > 0 && (-PriceCmaDiffHigh) >= TradingMacro.BarHeight60;
          return doSell == doBuy ? (bool?)null : doBuy;
        };
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
          //if (!IsCorridorAvarageHeightOk) return null;
          var sell = InPips(PriceCmaDiffHigh).Round(1) >= 0;
          var buy = InPips(PriceCmaDiffLow).Round(1) <= 0;
          if (buy && sell) return null;
          var doSell = sell;
          var doBuy = buy;
          return doSell == doBuy ? (bool?)null : doBuy;
        };
        var ts = tradeSignal8();
        if (ts != _TradeSignal)
          OnPropertyChanged("TradeSignal");
        _TradeSignal = ts;
        return _TradeSignal;
      }
    }

    //double priceCmaForAverageHigh { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.PriceCurrent.Ask; } }
    double priceCmaForAverageHigh { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.RateLastAsk; } }
    //double priceCmaForAverageLow { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.PriceCurrent.Bid; } }
    double priceCmaForAverageLow { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.RateLastBid; } }

    public double PriceCmaDiffHigh { get { return priceCmaForAverageHigh - AverageHigh; } }
    public double PriceCmaDiffLow { get { return priceCmaForAverageLow - AverageLow; } }

    public bool IsCorridorAvarageHeightOk {
      get {
        var addOn = 0;
        //TradingMacro.LimitCorridorByBarHeight ? 0 :
        //  PriceCmaDiffHigh > 0
        //  ? /*TradingMacro.PriceCmaDiffHighFirst*/ +TradingMacro.PriceCmaDiffHighLast
        //  : PriceCmaDiffLow < 0 ? /*-TradingMacro.PriceCmaDiffLowFirst*/ -TradingMacro.PriceCmaDiffLowLast
        //  : 0;
        return GetCorridorAverageHeightOk(TradingMacro,AverageHeight + Math.Max(0, addOn));
      }
    }
    public static bool GetCorridorAverageHeightOk(TradingMacro tm, double AverageHeight) {
      return AverageHeight > 0 && tm.BarHeight60 > 0
        && AverageHeight / tm.CorridorHeightMinimum >= (tm.LimitCorridorByBarHeight ? .9 : 1);
    }

  }
  public static class CorridorStaticBaseExtentions {
    public static Dictionary<int, CorridorStatistics> GetCorridornesses(this IEnumerable<Rate> rates, bool useStDev) {
      try {
        if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
        else rates = rates.ToArray();
        var corridornesses = new Dictionary<int, CorridorStatistics>();
        if (rates.Count() > 10) {
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
          for (var periods = 1; periods < rates.Count(); periods++) {
            var cs = ScanCorridor(rates.Take(periods).ToArray(), useStDev);
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
          var prices = ratesForAverageHigh.Select(r => r.PriceAvg/*.PriceLow*/).ToArray();
          averageHigh = prices.Average();
          averageHigh = prices.Where(p => p >= averageHigh).DefaultIfEmpty(averageHigh).Average();
        }
        if (ratesForAverageLow.Length > 0) {
          var prices = ratesForAverageLow.Select(r => r.PriceAvg/*.PriceHigh*/).ToArray();
          averageLow = prices.Average();
          averageLow = prices.Where(p => p <= averageLow).DefaultIfEmpty(averageLow).Average();
        }
        return new CorridorStatistics(count / rates.Count(), averageHigh, averageLow, askHigh, bidLow, rates.Count(), rates.First().StartDate, rates.Last().StartDate);
      } catch (Exception exc) {
        Debug.Fail(exc + "");
        throw;
      }
    }
  }
}
