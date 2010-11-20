﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;

namespace HedgeHog.Alice.Store {
  public class CorridorStatistics : HedgeHog.Models.ModelBase {

    public double Density { get; set; }
    public double Slop { get; set; }

    public double HeightUp { get; set; }
    public double HeightDown { get; set; }
    public double HeightUpDown { get { return HeightUp + HeightDown; } }
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
    public DateTime StartDate { get; set; }
    public int Periods { get; set; }
    public int Iterations { get; set; }
    public double Height {
      get { return TradingMacro.RatesLast.Where(r => r.PriceAvg1 != 0).Select(r => r.PriceAvg2 - r.PriceAvg3).FirstOrDefault(); }
    }

    public double Thinness { get { return Periods / HeightUpDownInPips; } }

    public CorridorStatistics() {

    }
    public CorridorStatistics(double density, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate) {
      Init(density, heightUp, heightDown,lineHigh,lineLow, periods, endDate, startDate, 0);
    }

    public void Init(double density, double heightUp, double heightDown, LineInfo lineHigh, LineInfo lineLow, int periods, DateTime endDate, DateTime startDate, int iterations) {
      this.Density = density;
      this.LineHigh = lineHigh;
      this.LineLow = lineLow;
      this.EndDate = endDate;
      this.StartDate = startDate;
      this.Periods = periods;
      this.Iterations = iterations;
      this.HeightUp = heightUp;
      this.HeightDown = heightDown;
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

    public void ResetLock() { IsBuyLock = IsSellLock = false; }
    bool _isBuyLock;
    public bool IsBuyLock {
      get { return _isBuyLock; }
      set { 
        _isBuyLock = value;
        if (value) IsSellLock = false;
        OnPropertyChanged("IsBuyLock");
      }
    }
    bool _isSellLock;
    public bool IsSellLock {
      get { return _isSellLock; }
      set { 
        _isSellLock = value;
        if (value) IsBuyLock = false;
        OnPropertyChanged("IsSellLock");
      }
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
        var m = Math.Max(1, HeightUpDownInPips * .1);
        switch (TradingMacro.Strategy & (Strategies.Range | Strategies.Breakout)) {
          case Strategies.Breakout:
            b = OpenBreakout(0,m);
            break;
          case Strategies.Range:
            b = OpenRange(2,0);
            break;
          default: return null;
        }
        if (b.HasValue) {
          return lastSignal = b;
        }
        return null;
      }
    }
    bool canBuy { get { return TradeDirection != TradeDirections.Down && TradingMacro.CorridorAngle > 0; } }
    bool canSell { get { return TradeDirection != TradeDirections.Up && TradingMacro.CorridorAngle < 0; } }
    public bool? CloseSignal {
      get {
        return null;
        switch (TradingMacro.Strategy) {
          case Strategies.Range:
            var r = OpenRange(0,0);
            return r.HasValue ? !r : null;
          case Strategies.Breakout:
            var b = OpenBreakout(0,0);
            return b.HasValue ? !b : null;
        }
        return null;
      }
    }

    private bool? OpenRange(int level,double m) {
      if (canSell && PriceCmaDiffHigh.HasValue && InPips(PriceCmaDiffHigh.Value).Between(-level,m)) return false;
      if (canBuy && PriceCmaDiffLow.HasValue && InPips(PriceCmaDiffLow.Value).Between(-m, level) ) return true;
      return null;
    }
    private bool? OpenBreakout(int level,double m) {
      if (canBuy && PriceCmaDiffHigh.HasValue && InPips(PriceCmaDiffHigh.Value).Between(-level,m) && !IsSellLock) return true;
      if (canSell && PriceCmaDiffLow.HasValue && InPips(PriceCmaDiffLow.Value).Between(-m,level) && !IsBuyLock) return false;
      return OpenRange(level,m);
      if (true && lastSignal.HasValue) {
        if (lastSignal.Value && PriceCmaDiffLow.HasValue && InPips(PriceCmaDiffLow.Value) < m ) return true;
        if (!lastSignal.Value && PriceCmaDiffHigh.HasValue && InPips(PriceCmaDiffHigh.Value) > -m ) return false;
      }
      return null;
    }

    public double? PriceCmaDiffHigh {
      get {
        //var extreamHigh = LineHigh == null?null:  LineHigh.Line.OrderBars().LastOrDefault();
        //if (extreamHigh != null) return extreamHigh.PriceHigh - extreamHigh.PriceAvg2;
        var rl = RateForDiffHigh;
        return rl == null ? (double?)null : TradingMacro.GetPriceHigh(rl) - rl.PriceAvg2;
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
        var res = rl == null ? (double?)null : TradingMacro.GetPriceLow(rl) - rl.PriceAvg3;
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

    public LineInfo LineLow { get; set; }

    public LineInfo LineHigh { get; set; }
  }
}