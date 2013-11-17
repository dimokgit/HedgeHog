using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog.Bars;
using HedgeHog.Shared;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Specialized;
using System.Windows;
using HedgeHog.Alice.Store.Metadata;
using System.Linq.Expressions;
using System.Windows.Input;
using Gala = GalaSoft.MvvmLight.Command;
using System.Data.Objects.DataClasses;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Web;
using System.Web.Caching;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Runtime.Caching;
using System.Reactive;
using System.Threading.Tasks.Dataflow;
using System.Collections.Concurrent;
using HedgeHog.Models;
using System.Web.UI;
using DynamicExpresso;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    bool IsTradingHour() { return IsTradingHour(ServerTime) && !IsEndOfWeek(); }
    bool IsTradingDay() { return IsTradingDay(ServerTime); }
    bool IsTradingTime() { return IsTradingDay() && IsTradingHour(); }
    public bool TradingTimeState { get { try { return IsTradingTime(); } catch { throw; } } }
    private bool IsEndOfWeek() {
      return ServerTime.DayOfWeek == DayOfWeek.Friday && ServerTime.ToUniversalTime().TimeOfDay > TimeSpan.Parse("2" + (ServerTime.IsDaylightSavingTime() ? "0" : "1") + ":45")
        || (!TradeOnBOW && IsBeginningOfWeek());
    }
    private bool IsBeginningOfWeek() { return ServerTime.DayOfWeek == DayOfWeek.Sunday; }

    bool _CloseAtZero;

    public bool CloseAtZero {
      get { return _CloseAtZero; }
      set {
        if (_CloseAtZero == value) return;
        _CloseAtZero = value;
        _trimAtZero = false;
        MustExitOnReverse = false;
        OnPropertyChanged(() => CloseAtZero);
      }
    }
    bool _trimAtZero;
    bool _trimToLotSize;

    #region CorridorDistance
    public int CorridorDistance {
      get { return (CorridorDistanceRatio > 1 ? CorridorDistanceRatio : RatesArray.Count * CorridorDistanceRatio).ToInt(); }
    }
    #endregion

    #region Corridor Start/Stop
    public double StartStopDistance { get; set; }
    double GetDistanceByDates(DateTime start, DateTime end) {
      var a = RatesArray.FindBar(start);
      var b = RatesArray.FindBar(end);
      return a.Distance - b.Distance;
    }
    partial void OnCorridorStartDateChanged() {
      if (!CorridorStartDate.HasValue)
        CorridorStopDate = DateTime.MinValue;
      return;
      if (CorridorStartDate.HasValue && RatesArray.Count > 0) {
        var rateStart = RatesArray.SkipWhile(r => r.StartDate <= CorridorStartDate.Value);
        if (StartStopDistance > 0) {
          var a = RatesArray.FindBar(CorridorStartDate.Value);
          CorridorStats.StopRate = rateStart.TakeWhile(r => a.Distance - r.Distance <= StartStopDistance).Last();
        } else
          CorridorStats.StopRate = rateStart.Skip(60).FirstOrDefault() ?? RatesArray.LastBC();
        _CorridorStopDate = CorridorStats.StopRate.StartDate;
      }
    }


    void _broadcastCorridorDateChanged() {
      Action<int> a = u => {
        try {
          //Debug.WriteLine("broadcastCorridorDatesChange.Proc:{0:n0},Start:{1},Stop:{2}", u, CorridorStartDate, CorridorStopDate);
          if (!RatesArray.Any()) return;
          OnScanCorridor(RatesArray);
          SetLotSize();
          RunStrategy();
          RaiseShowChart();
        } catch (Exception exc) {
          Log = exc;
        }
      };
      if (false && IsInVitualTrading) Scheduler.CurrentThread.Schedule(TimeSpan.FromMilliseconds(1), () => a(0));
      else broadcastCorridorDatesChange.SendAsync(a);
    }
    partial void OnCorridorStartDateChanging(DateTime? value) {
      if (value == CorridorStartDate) return;
      _broadcastCorridorDateChanged();
    }

    bool _isCorridorStopDateManual { get { return CorridorStopDate != DateTime.MinValue; } }
    DateTime _CorridorStopDate;
    public DateTime CorridorStopDate {
      get { return _CorridorStopDate; }
      set {
        if (value == DateTime.MinValue || RateLast == null || value > RateLast.StartDate) {
          _CorridorStopDate = value;
          CorridorStats.StopRate = null;
        } else {
          value = value.Min(RateLast.StartDate).Max(CorridorStartDate.GetValueOrDefault(CorridorStats.StartDate).Add((BarPeriodInt * 2).FromMinutes()));
          if (_CorridorStopDate == value && CorridorStats.StopRate != null) return;
          _CorridorStopDate = value;
          if (value == RateLast.StartDate)
            CorridorStats.StopRate = RateLast;
          else {
            var index = RatesArray.IndexOf(new Rate() { StartDate = value });
            CorridorStats.StopRate = RatesArray.Reverse<Rate>().SkipWhile(r => r.StartDate > value).First();
            if (CorridorStats.StopRate != null)
              _CorridorStopDate = CorridorStats.StopRate.StartDate;
          }
          if (CorridorStats.Rates.Any())
            StartStopDistance = CorridorStats.Rates.LastBC().Distance - CorridorStats.StopRate.Distance;
        }
        OnPropertyChanged("CorridorStopDate");
        _broadcastCorridorDateChanged();
      }
    }
    #endregion


    Models.ObservableValue<bool> _isCorridorDistanceOk = new Models.ObservableValue<bool>(false);
 
    IEnumerable<Rate> _waveMiddle {
      get {
        return WaveShort.Rates.Skip(WaveTradeStart.Rates.Count);
      }
    }


    public delegate Rate[] SetTrendLinesDelegate();
    double[] _getRegressionLeftRightRates() {
      var rateLeft = CorridorStats.Coeffs.RegressionValue(CorridorStats.Rates.Count - 1);
      var rightIndex = RatesArray.ReverseIfNot().IndexOf(CorridorStats.Rates.LastBC());
      var rateRight = new[] { rateLeft, -CorridorStats.Coeffs[1] }.RegressionValue(rightIndex);
      return new[] { rateLeft, rateRight };
    }

    Rate[] _setTrendLineDefault() {
      double h, l;
      h = l = CorridorStats.StDevMin;
      if (CorridorStats == null || !CorridorStats.Rates.Any()) return new[] { new Rate(), new Rate() };
      var rates = new[] { RatesArray.LastBC(), CorridorStats.Rates.LastBC() };
      var regRates = _getRegressionLeftRightRates();

      rates[0].PriceChartAsk = rates[0].PriceChartBid = double.NaN;

      rates[0].PriceAvg1 = regRates[1];
      rates[0].PriceAvg2 = rates[0].PriceAvg1 + h * 2;
      rates[0].PriceAvg3 = rates[0].PriceAvg1 - l * 2;
      rates[1].PriceAvg1 = regRates[0];
      rates[1].PriceAvg2 = rates[1].PriceAvg1 + h * 2;
      rates[1].PriceAvg3 = rates[1].PriceAvg1 - l * 2;
      return rates;
    }
    SetTrendLinesDelegate _SetTrendLines;
    public SetTrendLinesDelegate SetTrendLines {
      get { return _SetTrendLines ?? _setTrendLineDefault; }
      set { _SetTrendLines = value; }
    }
    void ForEachSuppRes(params Action<SuppRes>[] actions) { var l = SuppRes.ToList(); actions.ForEach(action => l.ForEach(sr => action(sr))); }
    double _buyLevelNetOpen() { return Trades.IsBuy(true).NetOpen(_buyLevel.Rate); }
    double _sellLevelNetOpen() { return Trades.IsBuy(false).NetOpen(_sellLevel.Rate); }
    Action _adjustEnterLevels = () => { throw new NotImplementedException(); };
    Action<double?,double?> _adjustExitLevels = (buyLevel,selLevel) => { throw new NotImplementedException(); };
    Action _exitTrade = () => { throw new NotImplementedException(); };

    Func<Rate, double> _priceAvg = rate => rate.PriceAvg;
    void SetCorridorStartDateAsync(DateTime? date) {
      Scheduler.CurrentThread.Schedule(TimeSpan.FromMilliseconds(1), () => CorridorStartDate = date);
    }

    private void StrategyEnterUniversal() {
      if (!RatesArray.Any()) return;

      #region ============ Local globals ============
      #region Loss/Gross
      Func<double> currentGrossInPips = () => CurrentGrossInPips;
      Func<double> currentLoss = () => CurrentLoss;
      Func<double> currentGross = () => CurrentGross;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<bool> isCorridorFrozen = () => LotSizeByLossBuy >= MaxLotSize;
      #endregion
      _isSelfStrategy = true;
      var reverseStrategy = new ObservableValue<bool>(false);
      Func<bool, double> crossLevelDefault = isBuy => isBuy ? _RatesMax + RatesHeight : _RatesMin - RatesHeight;
      Action resetCloseAndTrim = () => CloseAtZero = _trimAtZero = _trimToLotSize = false;

      #endregion

      #region ============ Init =================

      if (_strategyExecuteOnTradeClose == null) {
        if (IsInVitualTrading && Trades.Any()) throw new Exception("StrategyEnterUniversal: All trades must be closed befor strategy init.");
        if (IsInVitualTrading) TurnOffSuppRes(RatesArray.Select(r => r.PriceAvg).DefaultIfEmpty().Average());
        #region SetTrendLines
        Func<Func<Rate, double>, double, double[]> getStDevByAverage1 = (price, skpiRatio) => {
          var line = new double[CorridorStats.Rates.Count];
          try {
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          } catch (IndexOutOfRangeException) {
            line = new double[CorridorStats.Rates.Count];
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          }
          var hl = CorridorStats.Rates.Select((r, i) => price(r) - line[i]).ToArray();
          var hlh = hl.Where(d => d > 0).ToArray();
          var h = hlh.Average();
          h = hlh.Where(d => d >= h).Average();
          var hll = hl.Where(d => d < 0).ToArray();
          var l = hll.Average();
          l = hll.Where(d => d < l).Average().Abs();
          return new[] { h/2, l/2 };
        };
        Func<Func<Rate, double>, double, double[]> getStDevByAverage = (price, skpiRatio) => {
          var line = new double[CorridorStats.Rates.Count];
          try {
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          } catch (IndexOutOfRangeException) {
            line = new double[CorridorStats.Rates.Count];
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          }
          var hl = CorridorStats.Rates.Select((r, i) => price(r) - line[i]).ToArray();
          var h = hl.Where(d => d > 0).Average();
          var l = hl.Where(d => d < 0).Average().Abs();
          return new[] { h, l };
        };
        Func<Func<Rate, double>, double[]> getStDevUpDownByPriceAverage = null;
        getStDevUpDownByPriceAverage = (price) => {
          var line = new double[CorridorStats.Rates.Count];
          try {
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          } catch (IndexOutOfRangeException) {
            return getStDevUpDownByPriceAverage(price);
          }
          double stDevUp, stDevDown;
          GetStDevUpDown(CorridorStats.Rates.Select(price).ToArray(), line, out stDevUp, out stDevDown);
          return new[] { stDevUp, stDevDown };
        };
        Func<double[]> getStDev = () => {
          switch (CorridorHeightMethod) {
            case CorridorHeightMethods.ByMA: return getStDevByAverage(CorridorPrice,.1);
            case CorridorHeightMethods.ByPriceAvg: return getStDevByAverage1(_priceAvg, 0.1);
            case CorridorHeightMethods.ByStDevH: return new[] { CorridorStats.StDevByHeight, CorridorStats.StDevByHeight };
            case CorridorHeightMethods.ByStDevHP: return new[] { CorridorStats.StDevByHeight, CorridorStats.StDevByHeight, CorridorStats.StDevByHeight * 2 + CorridorStats.StDevByPriceAvg, CorridorStats.StDevByHeight * 2 + CorridorStats.StDevByPriceAvg };
            case CorridorHeightMethods.ByStDevP: return new[] { CorridorStats.StDevByPriceAvg, CorridorStats.StDevByPriceAvg };
            case CorridorHeightMethods.ByStDevPUD: return getStDevUpDownByPriceAverage(CorridorPrice);
            case CorridorHeightMethods.ByStDevMin: return new[] { CorridorStats.StDevByHeight.Min(CorridorStats.StDevByPriceAvg), CorridorStats.StDevByHeight.Min(CorridorStats.StDevByPriceAvg) };
            case CorridorHeightMethods.ByStDevMin2:
              var stDev = CorridorStats.StDevByHeight.Min(CorridorStats.StDevByPriceAvg);
              return new[] { stDev, stDev, stDev * 4, stDev * 4 };
            case CorridorHeightMethods.ByStDevMax: return new[] { CorridorStats.StDevByHeight.Max(CorridorStats.StDevByPriceAvg), CorridorStats.StDevByHeight.Max(CorridorStats.StDevByPriceAvg) };
          }
          throw new NotSupportedException("CorridorHeightMethods." + CorridorHeightMethod + " is not supported.");
        };
        Func<double[]> getRegressionLeftRightRates = () => {
          var rateLeft = CorridorStats.Coeffs.RegressionValue(CorridorStats.Rates.Count - 1);
          var rightIndex = RatesArray.ReverseIfNot().IndexOf(CorridorStats.Rates.LastBC());
          var rateRight = new[] { rateLeft, -CorridorStats.Coeffs[1] }.RegressionValue(rightIndex);
          return new[] { rateLeft, rateRight };
        };
        Func<int, Rate[]> setTrendLines = (levels) => {
          double h, l, h1, l1;
          double[] hl = getStDev == null ? new[] { CorridorStats.StDevMin, CorridorStats.StDevMin } : getStDev();
          h = hl[0] * 2;
          l = hl[1] * 2;
          if (hl.Length >= 4) {
            h1 = hl[2];
            l1 = hl[3];
          } else {
            h1 = hl[0] * 3;
            l1 = hl[1] * 3;
          }
          if (CorridorStats == null || !CorridorStats.Rates.Any()) return new[] { new Rate(), new Rate() };
          var rates = new[] { RatesArray.LastBC(), CorridorStats.Rates.LastBC() };
          var regRates = getRegressionLeftRightRates();

          rates[0].PriceChartAsk = rates[0].PriceChartBid = double.NaN;
          rates[0].PriceAvg1 = regRates[1];
          rates[1].PriceAvg1 = regRates[0];

          if (levels > 1) {
            rates[0].PriceAvg2 = rates[0].PriceAvg1 + h;
            rates[0].PriceAvg3 = rates[0].PriceAvg1 - l;
            rates[1].PriceAvg2 = rates[1].PriceAvg1 + h;
            rates[1].PriceAvg3 = rates[1].PriceAvg1 - l;
          }
          if (levels > 2) {
            rates[0].PriceAvg21 = rates[0].PriceAvg1 + h1;
            rates[0].PriceAvg31 = rates[0].PriceAvg1 - l1;
            rates[1].PriceAvg21 = rates[1].PriceAvg1 + h1;
            rates[1].PriceAvg31 = rates[1].PriceAvg1 - l1;
          }
          return rates;
        };
        SetTrendLines = () => setTrendLines(3);
        #endregion
        Func<bool, Func<Rate, double>> tradeExit = isBuy => MustExitOnReverse ? _priceAvg : GetTradeExitBy(isBuy);
        Func<bool, double> exitPrice = isBuy => CalculateLastPrice(tradeExit(isBuy));
        Action onEOW = () => { };
        #region Exit Funcs
        #region exitOnFriday
        Func<bool> exitOnFriday = () => {
          if (!SuppRes.Any(sr => sr.InManual)) {
            bool isEOW = IsAutoStrategy && IsEndOfWeek();
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots(),"exitOnFriday");
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              return true;
            }
          }
          return false;
        };
        Func<bool?> exitOnCorridorTouch = () => {
          var mustSell = false;
          var mustBuy = false;
          var currentBuy = CalculateLastPrice(_priceAvg);
          var currentSell = currentBuy;
          if (currentBuy >= RateLast.PriceAvg2) mustSell = true;
          if (currentSell <= RateLast.PriceAvg3) mustBuy = true;
          if (Trades.HaveBuy() && mustSell || Trades.HaveSell() && mustBuy)
            MustExitOnReverse = true;
          return mustBuy ? mustBuy : mustSell ? false : (bool?)null;
        };
        #endregion
        #region exitByLimit
        Action exitByLimit = () => {
          if (!exitOnFriday() && CurrentGross > 0 && TradesManager.MoneyAndLotToPips(OpenTradesGross, Trades.Lots(), Pair) >= TakeProfitPips * ProfitToLossExitRatio)
              CloseAtZero = true;
        };
        #endregion
        #region exitByHarmonic
        Action exitByHarmonic = () => {
          if ( CurrentGrossInPips >= _harmonics[0].Height )
            CloseAtZero = true;
        };
        #endregion
        #region exitByGrossTakeProfit
        Func<bool> exitByGrossTakeProfit = () => 
          Trades.Lots() >= LotSize * ProfitToLossExitRatio && TradesManager.MoneyAndLotToPips(-currentGross(), LotSize, Pair) <= TakeProfitPips;
        #endregion

        //TradesManager.MoneyAndLotToPips(-currentGross(), LotSizeByLossBuy, Pair)
        #region exitByLossGross
        Func<bool> exitByLossGross = () => 
          Trades.Lots() >= LotSize * ProfitToLossExitRatio && currentLoss() < currentGross() * ProfitToLossExitRatio;// && LotSizeByLossBuy <= LotSize;
        #endregion
        #region exitVoid
        Action exitVoid = () => { };
        #endregion
        #region exitByWavelette
        Action exitByWavelette = () => {
          double als = LotSizeByLossBuy;
          if (exitOnFriday()) return;
          var waveOk = WaveShort.Rates.Count < CorridorDistanceRatio;
          if (Trades.Lots() > als && waveOk)
            _trimAtZero = true;
          if (currentGrossInPips() >= TakeProfitPips || currentGross() > 0 && waveOk)
            CloseAtZero = true;
          else if (Trades.Lots() > LotSize && currentGross() > 0)
            _trimAtZero = true;
        };
        #endregion
        #region exitByTakeProfit
        Action exitByTakeProfit = () => {
          double als = LotSizeByLossBuy;
          if (exitOnFriday()) return;
          if (exitByGrossTakeProfit())
            CloseTrades(Trades.Lots() - LotSize, "exitByTakeProfit");
          else if (Trades.Lots() > LotSize && currentGross() > 0)
            _trimAtZero = true;
        };
        #endregion
        #region exitByTakeProfit_1
        Action exitByTakeProfit_1 = () => {
          double als = LotSizeByLossBuy;
          var lots = Trades.Lots();
          if (exitOnFriday()) return;
          if (exitByGrossTakeProfit())
            _trimAtZero = true;
          else if (lots >= LotSize * ProfitToLossExitRatio && -currentLoss() / currentGross() > 10)
            _trimAtZero = true;
          else if (lots > LotSize && currentGross() > 0)
            _trimAtZero = true;
        };
        #endregion
        #region exitFunc
        Func<Action> exitFunc = () => {
          switch (ExitFunction) {
            case Store.ExitFunctions.Void: return exitVoid;
            case Store.ExitFunctions.Friday: return () => exitOnFriday();
            case Store.ExitFunctions.GrossTP: return exitByTakeProfit;
            case Store.ExitFunctions.GrossTP1: return exitByTakeProfit_1;
            case Store.ExitFunctions.Wavelette: return exitByWavelette;
            case Store.ExitFunctions.Limit: return exitByLimit;
            case Store.ExitFunctions.Harmonic: return exitByHarmonic;
            case Store.ExitFunctions.CorrTouch: return () => exitOnCorridorTouch();
          }
          throw new NotSupportedException(ExitFunction + " exit function is not supported.");
        };
        #endregion
        #endregion
        #region TurnOff Funcs
        Action<double, Action> turnOffIfCorridorInMiddle_ = (sections,a) => {
          var segment = RatesHeight / sections;
          var rest = (RatesHeight - segment) / 2;
          var bottom = _RatesMin + rest;
          var top = _RatesMax - rest;
          var tradeLevel = (_buyLevel.Rate + _sellLevel.Rate) / 2;
          if ((_buyLevel.CanTrade || _sellLevel.CanTrade) && IsAutoStrategy && tradeLevel.Between(bottom, top)) a();
        };
        Action<Action> turnOffByCrossCount = a => { if (_buyLevel.TradesCount.Min(_sellLevel.TradesCount) < CorridorCrossesMaximum)a(); };
        Action<Action> turnOffByWaveHeight = a => { if (WaveShort.RatesHeight < RatesHeight * .75)a(); };
        Action<Action> turnOffByWaveShortLeft = a => { if (WaveShort.Rates.Count < WaveShortLeft.Rates.Count)a(); };
        Action<Action> turnOffByWaveShortAndLeft = a => { if (WaveShortLeft.Rates.Count < CorridorDistanceRatio && WaveShort.Rates.Count < WaveShortLeft.Rates.Count)a(); };
        Action<Action> turnOff = a => {
          switch (TurnOffFunction) {
            case Store.TurnOffFunctions.Void: return;
            case Store.TurnOffFunctions.WaveHeight: turnOffByWaveHeight(a); return;
            case Store.TurnOffFunctions.WaveShortLeft: turnOffByWaveShortLeft(a); return;
            case Store.TurnOffFunctions.WaveShortAndLeft: turnOffByWaveShortAndLeft(a); return;
            case Store.TurnOffFunctions.InMiddle_4: turnOffIfCorridorInMiddle_(4, a); return;
            case Store.TurnOffFunctions.InMiddle_5: turnOffIfCorridorInMiddle_(5, a); return;
            case Store.TurnOffFunctions.InMiddle_6: turnOffIfCorridorInMiddle_(6, a); return;
            case Store.TurnOffFunctions.InMiddle_7: turnOffIfCorridorInMiddle_(7, a); return;
            case Store.TurnOffFunctions.CrossCount: turnOffByCrossCount(a); return;
          }
          throw new NotSupportedException(TurnOffFunction + " Turnoff function is not supported.");
        };
        #endregion
        if (_adjustEnterLevels != null) _adjustEnterLevels.GetInvocationList().Cast<Action>().ForEach(d => _adjustEnterLevels -= d);
        if (_buyLevel != null) _buyLevel.Crossed -= null;
        if (_sellLevel != null) _sellLevel.Crossed -= null;
        bool exitCrossed = false;
        Action<Trade> onCloseTradeLocal = null;
        Action<Trade> onOpenTradeLocal = null;

        #region Levels
        if (SuppResLevelsCount != 2) SuppResLevelsCount = 2;
        SuppRes.ForEach(sr => sr.IsExitOnly = false);
        var buyCloseLevel = Support1(); buyCloseLevel.IsExitOnly = true;
        var sellCloseLevel = Resistance1(); sellCloseLevel.IsExitOnly = true;
        BuyLevel = Resistance0();
        SellLevel = Support0();
        if (BuyLevel.Rate.Min(SellLevel.Rate) == 0) BuyLevel.RateEx = SellLevel.RateEx = RatesArray.Middle();
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
        ObservableValue<double> ghostLevelOffset = new ObservableValue<double>(0);
        Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
        Action<Func<SuppRes, bool>, Action<SuppRes>> _buySellLevelsForEachWhere = (where, a) => _buySellLevels.Where(where).ToList().ForEach(sr => a(sr));
        _buySellLevelsForEach(sr => sr.ResetPricePosition());
        Action<SuppRes, bool> setCloseLevel = (sr, overWrite) => {
          if (!overWrite && sr.InManual) return;
          sr.InManual = false;
          sr.CanTrade = false;
          if (sr.TradesCount != 9) sr.TradesCount = 9;
        };
        Func<SuppRes,SuppRes> suppResNearest = supRes=>  _suppResesForBulk().Where(sr => sr.IsSupport != supRes.IsSupport).OrderBy(sr => (sr.Rate - supRes.Rate).Abs()).First();
        Action<bool> setCloseLevels = (overWrite) =>  setCloseLevel(buyCloseLevel, overWrite);
        setCloseLevels += (overWrite) => setCloseLevel(sellCloseLevel, overWrite);
        ForEachSuppRes(sr => { sr.InManual = false; sr.ResetPricePosition(); sr.ClearCrossedHandlers(); });
        setCloseLevels(true);
        #region updateTradeCount
        Action<SuppRes, SuppRes> updateTradeCount = (supRes, other) => {
          if (supRes.TradesCount <= other.TradesCount) other.TradesCount = supRes.TradesCount - 1;
        };
        Func<SuppRes,SuppRes> updateNeares = supRes => {
          var other = suppResNearest(supRes);
          updateTradeCount(supRes, other);
          return other;
        };
        #endregion
        Func<SuppRes, bool> canTradeLocal = sr => true;
        Func<bool> isTradingHourLocal = ()=> IsTradingHour() && IsTradingDay();
        Func<SuppRes, bool> suppResCanTrade = (sr) =>
          isTradingHourLocal() && 
          canTradeLocal(sr) && 
          sr.CanTrade && 
          sr.TradesCount <= 0 && 
          !HasTradesByDistance(sr.IsBuy);
        Func<bool> isProfitOk = () => Trades.HaveBuy() && RateLast.BidHigh > buyCloseLevel.Rate ||
          Trades.HaveSell() && RateLast.AskLow < sellCloseLevel.Rate;
        #endregion

        #region SuppRes Event Handlers
        #region enterCrossHandler
        Func<SuppRes, bool> enterCrossHandler = (suppRes) => {
          if (!IsTradingActive) return false;
          var isBuy = suppRes.IsBuy;
          var lot = Trades.IsBuy(!isBuy).Lots();
          var canTrade = suppResCanTrade(suppRes);
          if (canTrade) {
            lot += AllowedLotSizeCore();
            suppRes.TradeDate = ServerTime;
          }
          //var ghost = SuppRes.SingleOrDefault(sr => sr.IsExitOnly && sr.IsBuy == isBuy && sr.InManual && sr.CanTrade && sr.TradesCount <= 0);
          //if (ghost != null) {
          //  var real = _buySellLevels.Single(sr => sr.IsBuy == isBuy);
          //  if (real.IsBuy && real.Rate < ghost.Rate || real.IsSell && real.Rate > ghost.Rate)
          //    real.Rate = ghost.Rate;
          //}
          OpenTrade(isBuy, lot, "enterCrossHandler:" + new { isBuy, suppRes.IsExitOnly });
          return canTrade;
        };
        #endregion
        #region exitCrossHandler
        Action<SuppRes> exitCrossHandler = (sr) => {
          if (!IsTradingActive) return;
          exitCrossed = true;
          var lot = Trades.Lots() - (_trimToLotSize ? LotSize.Max(Trades.Lots() / 2) : _trimAtZero ? AllowedLotSizeCore() : 0);
          resetCloseAndTrim();
          CloseTrades(lot, "exitCrossHandler:" + new { sr.IsBuy, sr.IsExitOnly });
        };
        #endregion
        #endregion

        #region Crossed Events
        #region Enter Levels
        #region crossedEnter
        EventHandler<SuppRes.CrossedEvetArgs> crossedEnter = (s, e) => {
          var sr = (SuppRes)s;
          if (sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1) return;
          var srNearest = new Lazy<SuppRes>(() => suppResNearest(sr));
          updateTradeCount(sr, srNearest.Value);
          if (enterCrossHandler(sr)) {
            setCloseLevels(true);
          }
        };
        #endregion
        _buyLevel.Crossed += crossedEnter;
        _sellLevel.Crossed += crossedEnter;
        #endregion
        #region ExitLevels
        Action<SuppRes> handleActiveExitLevel = (srExit) => {
          updateNeares(srExit);
          if (enterCrossHandler(srExit)) {
            setCloseLevels(true);
          }
        };
        EventHandler<SuppRes.CrossedEvetArgs> crossedExit = (s, e) => {
          var sr = (SuppRes)s;
          if (reverseStrategy.Value && Trades.Any(t => t.IsBuy != sr.IsSell)) {
            exitCrossHandler(sr);
            return;
          }
          if (sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1) return;
          if (sr.CanTrade) {
            if (sr.InManual) handleActiveExitLevel(sr);
            else crossedEnter(s, e);
          } else if (Trades.Any(t => t.IsBuy == sr.IsSell))
            exitCrossHandler(sr);
        };
        buyCloseLevel.Crossed += crossedExit;
        sellCloseLevel.Crossed += crossedExit;
        #endregion
        #endregion

        #region adjustExitLevels
        Action<double, double> adjustExitLevels = null;
        adjustExitLevels = (buyLevel, sellLevel) => {
          var tradesCount = Trades.Length;
          if (buyLevel.Min(sellLevel) < .5) {
            Log = new Exception(new { buyLevel, sellLevel } + "");
            return;
          }
          buyCloseLevel.SetPrice(exitPrice(false));
          sellCloseLevel.SetPrice(exitPrice(true));
          Action<SuppRes> setExitLevel = sr => {
            if (sr.IsGhost && sr.Rate.Between(SellLevel.Rate, BuyLevel.Rate)) {
              var enterLevel = GetTradeEnterBy(sr.IsBuy)(RateLast);
              var offset = PointSize / 10;
              var rate = sr.Rate;
              if (sr.IsBuy) {
                if (sr.Rate - enterLevel > offset) {
                  ghostLevelOffset.Value = enterLevel + offset - rate;
                }
              } else {
                if (enterLevel - sr.Rate > offset) {
                  ghostLevelOffset.Value = enterLevel - offset - rate;
                }
              }
            } else {
              sr.RateEx = crossLevelDefault(sr.IsSell);
              sr.ResetPricePosition();
            }
          };
          if (tradesCount == 0) {
            setExitLevel(buyCloseLevel);
            setExitLevel(sellCloseLevel);
          } else {
            //buyCloseLevel.SetPrice(r(false));
            //sellCloseLevel.SetPrice(r(true));
            if (!Trades.Any()) {
              adjustExitLevels(buyLevel, sellLevel);
              buyCloseLevel.ResetPricePosition();
              sellCloseLevel.ResetPricePosition();
            } else {
              if (isProfitOk()) CloseAtZero = true;
              var close0 = CloseAtZero || _trimAtZero || _trimToLotSize || MustExitOnReverse;
              var tpCloseInPips = close0 ? 0 : TakeProfitPips - CurrentLossInPips.Min(0);
              var tpColse = InPoints(tpCloseInPips.Min(TakeProfitPips * TakeProfitLimitRatio)).Min(TradingDistance);
              var ratesShort = RatesArray.Reverse<Rate>().Skip(1).Take(5).ToArray();
              var priceAvgMax = ratesShort.Max(GetTradeExitBy(true)) - PointSize / 10;
              var priceAvgMin = ratesShort.Min(GetTradeExitBy(false)) + PointSize / 10;

              if (buyCloseLevel.IsGhost)
                setExitLevel(buyCloseLevel);
              else if (buyCloseLevel.InManual) {
                if (buyCloseLevel.Rate <= priceAvgMax)
                  buyCloseLevel.Rate = priceAvgMax;
              } else if (Trades.HaveBuy()) {
                var signB = (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign();
                buyCloseLevel.RateEx = (buyLevel.Min(_buyLevelNetOpen()) + tpColse).Max(Trades.HaveBuy() ? priceAvgMax : double.NaN);
                if (signB != (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign())
                  buyCloseLevel.ResetPricePosition();
              } else buyCloseLevel.RateEx = crossLevelDefault(true);

              if (sellCloseLevel.IsGhost)
                setExitLevel(sellCloseLevel);
              else if (sellCloseLevel.InManual) {
                if (sellCloseLevel.Rate >= priceAvgMin)
                  sellCloseLevel.Rate = priceAvgMin;
              } else if (Trades.HaveSell()) {
                var sign = (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign();
                sellCloseLevel.RateEx = (sellLevel.Max(_sellLevelNetOpen()) - tpColse).Min(Trades.HaveSell() ? priceAvgMin : double.NaN);
                if (sign != (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign())
                  sellCloseLevel.ResetPricePosition();
              } else sellCloseLevel.RateEx = crossLevelDefault(false);
            }
          }
        };
        Action adjustExitLevels0 = () => adjustExitLevels(double.NaN, double.NaN);
        Action adjustExitLevels1 = () => {
          if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels);
          else adjustExitLevels(_buyLevel.Rate, _sellLevel.Rate);
        };
        #endregion

        #region adjustLevels
        var firstTime = true;
        var errorsCount = 0;
        #region Watchers
        var watcherSetCorridor = new ObservableValue<bool>(false, true);
        var watcherCanTrade = new ObservableValue<bool>(true, true);
        var watcherWaveTrade = new ObservableValue<bool>(false, true);
        var watcherWaveStart = new ObservableValue<DateTime>(DateTime.MinValue);
        var watcherAngleSign = new ObservableValue<int>(0);
        var watcherTradeCounter = new ObservableValue<bool>(false, true);
        var waveTradeOverTrigger = new ValueTrigger<Unit>(false);
        var watcherReverseStrategy = new ObservableValue<bool>(false);
        var triggerRsd = new ValueTrigger<Unit>(false);
        var triggerFractal = new ValueTrigger<DateTime>(false);
        var rsdStartDate = DateTime.MinValue;
        var corridorMoved = new ValueTrigger<double>(false);
        var positionTrigger = new ValueTrigger<Unit>(false);
        double corridorLevel = double.NaN;
        double corridorAngle = 0;
        DateTime tradeCloseDate = DateTime.MaxValue;
        DateTime corridorDate = ServerTime;
        var corridorHeightPrev = DateTime.MinValue;
        DB.sGetStats_Result[] stats = null;
        Func<string, double> getCorridorMax = weekDay => stats.Single(s => s.DayName == weekDay + "").Range.Value;
        var interpreter = new Interpreter();
        bool? isPriceUp = null;
        Func<bool> isReverseStrategy = () => _buyLevel.Rate < _sellLevel.Rate;

        var intTrigger = new ValueTrigger<int>(false);
        var doubleTrigger = new ValueTrigger<double>(false);

        #endregion

        #region Funcs
        Func<Rate, double> cp = CorridorPrice();
        Func<double> calcVariance = null;
        Func<double> calcMedian = null;
        WaveInfo waveToTrade = null;
        Func<Func<Rate, double>, double> getRateLast = (f) => f(RateLast) > 0 ? f(RateLast) : f(RatePrev);
        #region medianFunc
        Func<Store.MedianFunctions, Func<double>> medianFunc0 = (mf) => {
          switch (mf) {
            case Store.MedianFunctions.Void: return () => double.NaN;
            case Store.MedianFunctions.Regression: return () => CorridorStats.Coeffs.RegressionValue(0);
            case Store.MedianFunctions.Regression1: return () => CorridorStats.Coeffs.RegressionValue(CorridorStats.Rates.Count - 1);
            case Store.MedianFunctions.WaveShort: return () => (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
            case Store.MedianFunctions.WaveTrade: return () => (WaveTradeStart.RatesMax + WaveTradeStart.RatesMin) / 2;
            case Store.MedianFunctions.WaveStart: return () => CorridorPrice(WaveTradeStart.Rates.LastBC());
            case Store.MedianFunctions.WaveStart1: return () => {
              if (!WaveTradeStart.HasRates) return double.NaN;
              var skip = WaveTradeStart1.HasRates ? WaveTradeStart1.Rates.Count : 0;
              var wave = WaveTradeStart.Rates.Skip(skip).Reverse().Select(r => CorridorPrice(r)).ToArray();
              double h = WaveTradeStart.RatesMax, l = WaveTradeStart.RatesMin;
              var ret = wave.Where(r => r >= h || r <= l).DefaultIfEmpty(double.NaN).First();
              return ret;
              if (double.IsNaN(ret)) {
                h = wave.Max(); l = wave.Min();
                ret = wave.Where(r => r >= h || r <= l).DefaultIfEmpty(double.NaN).First();
              }
              return ret;
            };
            case MedianFunctions.Density: return () => CorridorByDensity(RatesArray, StDevByHeight);
          }
          throw new NotSupportedException(MedianFunction + " Median function is not supported.");
        };
        Func<Func<double>> medianFunc = () => medianFunc0(MedianFunction);
        #endregion
        #region varianceFunc
        Func<Func<double>> varianceFunc = () => {
          switch (VarianceFunction) {
            case Store.VarainceFunctions.Zero: return () => 0;
            case Store.VarainceFunctions.Rates: return () => RatesHeight;
            case Store.VarainceFunctions.Rates2: return () => RatesHeight / 2;
            case Store.VarainceFunctions.Rates3: return () => RatesHeight / 3;
            case Store.VarainceFunctions.Rates4: return () => RatesHeight / 4;
            case Store.VarainceFunctions.Wave: return () => WaveShort.RatesHeight * .45;
            case Store.VarainceFunctions.StDevSqrt: return () => Math.Sqrt(CorridorStats.StDevByPriceAvg * CorridorStats.StDevByHeight);
            case Store.VarainceFunctions.Price: return () => CorridorStats.StDevByPriceAvg;
            case Store.VarainceFunctions.Hight: return () => CorridorStats.StDevByHeight;
            case VarainceFunctions.Max: return () => CorridorStats.StDevByPriceAvg.Max(CorridorStats.StDevByHeight);
            case VarainceFunctions.Min: return () => CorridorStats.StDevByPriceAvg.Min(CorridorStats.StDevByHeight);
            case VarainceFunctions.Min2: return () => CorridorStats.StDevByPriceAvg.Min(CorridorStats.StDevByHeight)*1.8;
            case VarainceFunctions.Sum: return () => CorridorStats.StDevByPriceAvg + CorridorStats.StDevByHeight;
          }
          throw new NotSupportedException(VarianceFunction + " Variance function is not supported.");
        };
        #endregion
        Func<bool> runOnce = null;

        #region getLevels
        Func<object> notImplemented = () => { throw new NotImplementedException(); };
        var levelsSample = new { levelUp = 0.0, levelDown = 0.0 };
        Func<double, double, object> newLevels = (levelUp, levelDown) => new { levelUp, levelDown };
        Func<object> getLevels = notImplemented;
        #endregion

        #region SetTrendlines
        Func<double, double, Func<Rate[]>> setTrendLinesByParams = (h,l) => {
          if (CorridorStats == null || !CorridorStats.Rates.Any()) return ()=> new[] { new Rate(), new Rate() };
          var rates = new[] { RatesArray.LastBC(), CorridorStats.Rates.LastBC() };
          var regRates = getRegressionLeftRightRates();

          rates[0].PriceChartAsk = rates[0].PriceChartBid = double.NaN;
          rates[0].PriceAvg1 = regRates[1];
          rates[1].PriceAvg1 = regRates[0];

          rates[0].PriceAvg2 = rates[0].PriceAvg1 + h;
          rates[0].PriceAvg3 = rates[0].PriceAvg1 - l;
          rates[1].PriceAvg2 = rates[1].PriceAvg1 + h;
          rates[1].PriceAvg3 = rates[1].PriceAvg1 - l;
          return () => rates;
        };
        #endregion
        #endregion

        #region adjustEnterLevels
        Action adjustEnterLevels = () => {
          if (!WaveShort.HasRates) return;
          switch (TrailingDistanceFunction) {
            #region PriceAvg23
            case TrailingWaveMethod.PriceAvg23:
              #region firstTime
              if (firstTime) {
                Log = new Exception(new { CorrelationMinimum } + "");
                onCloseTradeLocal = t => {
                  if (t.PL >= -PriceSpreadAverageInPips) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              if (IsAutoStrategy && CurrentPrice.Average.Between(RateLast.PriceAvg3, RateLast.PriceAvg2)) {
                BuyLevel.RateEx = RateLast.PriceAvg2;
                SellLevel.RateEx = RateLast.PriceAvg3;
                _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.TradesCountEx = CorridorCrossesMaximum; });
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion

            #region Count
            case TrailingWaveMethod.Count:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
                Log = new Exception(new {
                  CorrelationMinimum,
                  TradingAngleRange,
                  WaveStDevRatio
                } + "");
              }
              #endregion
              {
                //if (false) {
                //  var weeks = 3;
                //  if (stats == null || stats[0].StartDateMin < ServerTime.Date.AddDays(-7 * (weeks + 1)))
                //    stats = GlobalStorage.UseForexContext(c => {
                //      return c.sGetStats(ServerTime.Date.AddDays(-7 * weeks), CorridorDistanceRatio.ToInt(), weeks).ToArray();
                //    });
                //}
                var hasCorridorChanged = _CorridorStartDateByScan > corridorHeightPrev.AddMinutes(BarPeriodInt * CorridorDistanceRatio / 5);
                corridorHeightPrev = _CorridorStartDateByScan;
                var isInside = false;
                if (CorridorAngleFromTangent() <= TradingAngleRange && CorridorStats.StartDate > RatesArray[0].StartDate) {
                  //var startDatePast = RateLast.StartDate.AddHours(-CorridorDistanceRatio);
                  var levelPrev = RatesInternal.ReverseIfNot().Skip(CorridorDistanceRatio.ToInt()).SkipWhile(r => r.StartDate.Hour != 23).First().PriceAvg;
                  var rates = setTrendLines(2);
                  var up = rates[0].PriceAvg2;
                  var down = rates[0].PriceAvg3;
                  var corridorRates = CorridorStats.Rates;
                  var upPeak = up - CorridorStats.RatesMax;
                  var downValley = CorridorStats.RatesMin - down;
                  var ud2 = (up - down) / 4;
                  var isBetween = levelPrev.Between(down, up) &&
                    (new[] { up, down }.Any(ud => ud.Between(_sellLevel.Rate, _buyLevel.Rate))
                    || _buySellLevels.Any(sr => sr.Rate.Between(down, up))
                    );
                  var height = (up - down);
                  var param = new { up, down, upPeak, downValley };
                  var heightOk = (bool)interpreter.Eval(Eval, new Parameter("a", param)); //height > (CorridorHeightMax = getCorridorMax(ServerTime.DayOfWeek + ""));
                  var sizeOk = /*(up - down) > height ||*/ heightOk;
                  isInside = hasCorridorChanged && isBetween && sizeOk;
                  _buyLevel.RateEx = up;
                  _sellLevel.RateEx = down;
                  if (hasCorridorChanged)
                    _buySellLevelsForEach(sr => sr.CanTradeEx = isInside);
                }
                adjustExitLevels0();
              }
              break;
            #endregion
            #region CountWithAngle
            case TrailingWaveMethod.CountWithAngle:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
              }
              #endregion
              if (CorridorAngleFromTangent().Abs().ToInt() <= TradingAngleRange) {
                var ratesCorridor = WaveShort.Rates.Skip(this.PriceCmaLevels * 2);
                _buyLevel.RateEx = ratesCorridor.Max(GetTradeEnterBy(true));
                _sellLevel.RateEx = ratesCorridor.Min(GetTradeEnterBy(false)); ;
                _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region Wavelette
            case TrailingWaveMethod.Wavelette:
              #region firstTime
              if (firstTime) {
                watcherCanTrade.SetValue(false);
              }
              #endregion
              {
                var waveletteOk = false;
                double wavelette2First = double.NaN, wavelette2Last = double.NaN;
                var ratesReversed = _rateArray.ReverseIfNot();
                var wavelette1 = ratesReversed.Wavelette(cp);
                var wavelette2 = ratesReversed.Skip(wavelette1.Count).ToArray().Wavelette(cp);
                if (wavelette2.Any()) {
                  wavelette2First = cp(wavelette2[0]);
                  wavelette2Last = cp(wavelette2.LastBC());
                  var wavelette2Height = wavelette2First.Abs(wavelette2Last);
                  waveletteOk = wavelette1.Count < 5 && wavelette2Height >= varianceFunc()();
                }
                if (watcherCanTrade.SetValue(waveletteOk).ChangedTo(true)) {
                  var isUp = wavelette2First > wavelette2Last;
                  {
                    var rates = WaveShort.Rates.Take(wavelette1.Count + wavelette2.Count);
                    var stDev = wavelette2.StDev(_priceAvg);
                    var high = isUp ? rates.Max(_priceAvg) : double.NaN;
                    var low = !isUp ? rates.Min(_priceAvg) : double.NaN;
                    _buyLevel.RateEx = high.IfNaN(low + stDev);
                    _sellLevel.RateEx = low.IfNaN(high - stDev);
                  }
                  #region Old
                  if (false) {
                    var sorted = wavelette2.OrderBy(_priceAvg);
                    var dateSecond = (isUp ? sorted.Last() : sorted.First()).StartDate;
                    var pricesSecond = WaveShort.Rates.TakeWhile(r => r.StartDate >= dateSecond);
                    var priceFirst = isUp ? pricesSecond.Min(_priceAvg) : pricesSecond.Max(_priceAvg);
                    var priceSecond = isUp ? pricesSecond.Max(_priceAvg) : pricesSecond.Min(_priceAvg);
                    var high = (isUp ? priceSecond : priceFirst) + PointSize;
                    var low = (isUp ? priceFirst : priceSecond) - PointSize;
                    _buyLevel.RateEx = high;
                    _sellLevel.RateEx = low;
                    var heightMin = Math.Sqrt(StDevByPriceAvg * StDevByHeight);
                    if (_buyLevel.Rate - _sellLevel.Rate < heightMin) {
                      var median = (_buyLevel.Rate + _sellLevel.Rate) / 2;
                      _buyLevel.RateEx = median + heightMin / 2;
                      _sellLevel.RateEx = median - heightMin / 2;
                    }
                  }
                  #endregion
                }
                _buySellLevelsForEach(sr => {
                  sr.CanTradeEx = IsAutoStrategy;
                  sr.TradesCount = CorridorCrossesMaximum.Max(0);
                });
                _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy && _buyLevel.Rate > _sellLevel.Rate);
                adjustExitLevels0();
              }
              break;
            #endregion
            #region Manual
            case TrailingWaveMethod.Manual:
              #region FirstTime
              if (firstTime) {
                LogTrades = false;
                DoNews = false;
                Log = new Exception(new { WaveStDevRatio, MedianFunction, DoNews } + "");
                onCloseTradeLocal = t => {
                  if (IsAutoStrategy)
                    buyCloseLevel.InManual = sellCloseLevel.InManual = false;
                };
                onOpenTradeLocal = t => { };
                if (ReverseStrategy) {
                  _buyLevel.Rate = _RatesMin;
                  _sellLevel.Rate = _RatesMax;
                }

                reverseStrategy = new ObservableValue<bool>(isReverseStrategy());
                reverseStrategy.ValueChanged += (s, e) => {
                  buyCloseLevel.InManual = sellCloseLevel.InManual = false;
                };
                if (ReverseStrategy && !reverseStrategy.Value) {
                  _buyLevel.Rate = _RatesMin;
                  _sellLevel.Rate = _RatesMax;
                }
                if (!ReverseStrategy && reverseStrategy.Value) {
                  _buyLevel.Rate = _RatesMax;
                  _sellLevel.Rate = _RatesMin;
                }
              }
              #endregion
              {
                double levelUp = double.NaN, levelDown = double.NaN;
                var getPrice = GetTradeEnterBy();
                var ratesByGap = WaveTradeStart.ResetRates(CorridorByVerticalLineLongestCross(CorridorStats.Rates.ToArray(), getPrice));
                if (MedianFunction == MedianFunctions.Void) {
                  if (ratesByGap != null) {
                    var level = getPrice(ratesByGap[0]);
                    var offset = ratesByGap.Average(r => level - getPrice(r));
                    var level1 = level + offset;
                    CenterOfMassBuy = levelUp = level.Max(level1);
                    CenterOfMassSell = levelDown = level.Min(level1);
                    var corridorOk = CorridorStats.Rates.Count >= CorridorDistanceRatio * WaveStDevRatio;
                    if (IsAutoStrategy) _buySellLevelsForEach(sr => sr.CanTradeEx = corridorOk);
                  }
                }
                reverseStrategy.Value = isReverseStrategy();
                if (reverseStrategy.Value) {
                  _buyLevel.RateEx = levelDown.IfNaN(CenterOfMassSell);
                  _sellLevel.RateEx = levelUp.IfNaN(CenterOfMassBuy);
                  var stopLoss = _buyLevel.Rate.Abs(_sellLevel.Rate);
                  if (Trades.HaveBuy() && !sellCloseLevel.InManual) {
                    sellCloseLevel.RateEx = Trades.NetOpen() - stopLoss;
                    sellCloseLevel.ResetPricePosition();
                    sellCloseLevel.InManual = true;
                  }
                  if (Trades.HaveSell() && !buyCloseLevel.InManual) {
                    buyCloseLevel.RateEx = Trades.NetOpen() + stopLoss;
                    buyCloseLevel.ResetPricePosition();
                    buyCloseLevel.InManual = true;
                  }
                } else {
                  _buyLevel.RateEx = levelUp.IfNaN(CenterOfMassBuy);
                  _sellLevel.RateEx = levelDown.IfNaN(CenterOfMassSell);
                }
              }
              adjustExitLevels0();
              break;
            #endregion
            #region Magnet
            case TrailingWaveMethod.MagnetFft:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
                Log = new Exception(new { CorridorHeightMax_crossesCount = CorridorHeightMax } + "");
                MagnetPrice = double.NaN;
                onCloseTradeLocal = t => {
                  if (t.PL > -PriceSpreadAverage) {
                    _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  }
                };
              }
              #endregion
              {
                var corridorRates = CorridorStats.Rates.Select(_priceAvg).ToArray();
                var point = InPoints(1) / 2;
                var crossesCount = CorridorHeightMax.ToInt();
                var edgesLevels = corridorRates.EdgeByRegression(point, 2, CorridorStats.StDevByHeight / 2);
                SetTrendLines = new SetTrendLinesDelegate(setTrendLinesByParams(edgesLevels.Max(), edgesLevels.Min().Abs()));
                var priceAvg1 = RateLast.PriceAvg1;
                var ratesMax = priceAvg1 + edgesLevels.Max();
                var ratesMin = priceAvg1 + edgesLevels.Min();
                CenterOfMassBuy = ratesMax.IfNaN(CenterOfMassBuy);// lowper.Max();
                CenterOfMassSell = ratesMin.IfNaN(CenterOfMassSell);// lowper.Min();
                //SetTrendLines 
                if (CorridorAngleFromTangent().Abs() > TradingAngleRange && !edgesLevels.Any(d => d.IsNaN())) {
                  _sellLevel.RateEx = CenterOfMassBuy;
                  _buyLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy) _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                }
                //if (IsAutoStrategy && edgesLevels.All(e => e.IsNaN())) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
              }
              {
                Func<double, IEnumerable<double>> rateSynceTrade = def => {
                  var d = Trades.Max(t => t.Time);
                  return RatesArray.ReverseIfNot().TakeWhile(r => r.StartDate >= d).Select(_priceAvg).DefaultIfEmpty(def);
                };
                var buyLevel = Trades.HaveBuy() ? rateSynceTrade(_buyLevel.Rate).Min() : _buyLevel.Rate;
                var sellLevel = Trades.HaveSell() ? rateSynceTrade(_sellLevel.Rate).Max() : _sellLevel.Rate;
                adjustExitLevels(buyLevel, sellLevel);
              }
              break;
            #endregion
            #region Magnet2
            case TrailingWaveMethod.Magnet2:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
                DoNews = false;
                var a = new { } + "";
                Log = new Exception(a);
                MagnetPrice = double.NaN;
              }
              #endregion
              {
                var evalParam = new { cdr = CorridorDistanceRatio.ToInt() };
                var corridorLength2 = (int)interpreter.Eval(Eval, new Parameter("a", evalParam));
                double level2 = 0;
                CorridorByVerticalLineCrosses2(RatesArray.ReverseIfNot(), _priceAvg, corridorLength2, out level2);

                CenterOfMassSell = MagnetPrice.Min(level2);
                CenterOfMassBuy = MagnetPrice.Max(level2);
                _buyLevel.RateEx = CenterOfMassBuy;
                _sellLevel.RateEx = CenterOfMassSell;
                var corridorOk = true;// _buyLevel.Rate - _sellLevel.Rate > StDevByHeight;
                _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy && corridorOk);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region StDev
            case TrailingWaveMethod.StDev:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum } + "");
                onCloseTradeLocal = t => { };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = getRateLast(r => r.PriceAvg2), l = getRateLast(r => r.PriceAvg3);
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                var levels = CorridorByStDev(ratesInner, StDevByHeight);
                double level = levels[0].Level, stDev = levels[0].StDev;
                var offset = stDev;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isCountOk = CorridorStats.Rates.Count >= RatesArray.Count * WaveStDevRatio;// levels[0].Count < StDevLevelsCountsAverage;
                bool ok = isCountOk && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum) &&
                  (!Trades.Any()
                  || Trades.HaveBuy() && level < Trades.NetClose() || Trades.HaveSell() && level > Trades.NetClose());
                var corridorJumped = corridorLevel.IfNaN(0).Abs(level) > stDev;

                CenterOfMassLevels.Clear();
                CenterOfMassLevels.AddRange(levels.Skip(1).Select(a => new[] { a.Level/* + a.StDev, a.Level - a.StDev*/ }).SelectMany(a => a));
                if (ok) {
                  corridorLevel = level;
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  var isPriceInside = CurrentPrice.Average.Between(_buyLevel.Rate, _sellLevel.Rate);
                  if (IsAutoStrategy && corridorJumped && !isPriceInside) {
                    _buySellLevelsForEach(sr => {
                      sr.CanTradeEx = false;
                      sr.ResetPricePosition();
                      if (Trades.Any()) CloseAtZero = true;
                    });
                  }
                  if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                }
                if (IsAutoStrategy && !isCountOk)
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev2
            case TrailingWaveMethod.StDev2:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = getRateLast(r => r.PriceAvg2), l = getRateLast(r => r.PriceAvg3);
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) Debugger.Break();
                //var levels = CorridorByStDev(ratesInner, StDevByHeight);
                var levels = StDevsByHeight(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                double level = levels[0].Level, stDev = levels[0].StDev;
                var offset = stDev * 3;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isCountOk = CorridorStats.Rates.Count >= RatesArray.Count * WaveStDevRatio;// levels[0].Count < StDevLevelsCountsAverage;
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                var angleOk = VoltageCurrent > GetVoltageAverage();
                bool ok = isCountOk 
                  && angleOk
                  && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                  && (!Trades.Any() || isPriceInside);

                CenterOfMassLevels.Clear();
                CenterOfMassLevels.AddRange(levels.Skip(1).Select(a => new[] { a.Level/* + a.StDev, a.Level - a.StDev*/ }).SelectMany(a => a));
                if (ok) {
                  corridorLevel = level;
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                }
                if (IsAutoStrategy && !(isCountOk && angleOk))
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev3
            case TrailingWaveMethod.StDev3:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) { Log = new Exception("StDev3: ratesInner is empty!"); break; }
                //var levels = CorridorByStDev(ratesInner, StDevByHeight);
                var levels = StDevsByHeight1(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                if (levels.Any()) {
                  double level = levels[0].Level, stDev = levels[0].StDev;
                  var rsd = ratesInner.Select(_priceAvg).Integral(60).AsParallel().Select(g => g.RsdNormalized()).Average();
                  var offset = stDev / rsd;
                  CenterOfMassBuy = level + offset;
                  CenterOfMassSell = level - offset;
                  var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                  var angleOk = !CorridorCorrelation.Abs().Between(0.15, WaveStDevRatio);// VoltageCurrent > GetVoltageHigh() && GetVoltageHigh() > WaveStDevRatio;
                  bool ok = angleOk
                    && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                    && (!Trades.Any() || isPriceInside);

                  CenterOfMassLevels.Clear();
                  CenterOfMassLevels.AddRange(levels.Skip(1).Select(a => new[] { a.Level/* + a.StDev, a.Level - a.StDev*/ }).SelectMany(a => a));
                  if (ok) {
                    corridorLevel = level;
                    _buyLevel.RateEx = CenterOfMassBuy;
                    _sellLevel.RateEx = CenterOfMassSell;
                    if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                      _buySellLevelsForEach(sr => {
                        sr.CanTradeEx = true;
                        sr.ResetPricePosition(); });
                  }
                  if (IsAutoStrategy && !(angleOk))
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev4
            case TrailingWaveMethod.StDev4:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { DoAdjustExitLevelByTradeTime, CorrelationMinimum } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) { Log = new Exception("StDev3: ratesInner is empty!"); break; }
                var levels = StDevsByHeight1(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                if (levels.Any()) {
                  double level = levels[0].Level, stDev = levels[0].StDev;
                  var offset = stDev * DistanceIterations;
                  CenterOfMassBuy = level + offset;
                  CenterOfMassSell = level - offset;
                  {
                    var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                    var isVoltageLow = VoltageCurrent < GetVoltageAverage();
                    bool okSetLevel = isVoltageLow
                      && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                      && (!Trades.Any() || isPriceInside);
                    if (okSetLevel) {
                      _buyLevel.RateEx = CenterOfMassBuy;
                      _sellLevel.RateEx = CenterOfMassSell;
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    }
                  }
                  {
                    var isPriceInside = CurrentPrice.Average.Between(_sellLevel.Rate, _buyLevel.Rate);
                    var isVoltageHigh = VoltageCurrent > GetVoltageHigh();
                    if (IsAutoStrategy && isVoltageHigh && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                      _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                  }
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev5
            case TrailingWaveMethod.StDev5:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.AsEnumerable().Reverse().ToArray();
                if (!ratesInner.Any()) { var exc = new Exception("ratesInner is empty."); if (errorsCount++ > 1000) throw exc; Log = exc; return; }
                var levels = StDevsByHeight(ratesInner, StDevByHeight).OrderBy(a => a.Count / a.StDev).Take(1).ToArray();
                double level = levels[0].Level, stDev = levels[0].StDev;
                var offset = stDev * DistanceIterations;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                bool ok = CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                  && isPriceInside;

                if (ok) {
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                }

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev6
            case TrailingWaveMethod.StDev6:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.AsEnumerable().Reverse().ToArray();
                if (!ratesInner.Any()) { var exc = new Exception("ratesInner is empty."); if (errorsCount++ > 1000) throw exc; Log = exc; return; }
                var levels = StDevsByHeight2(ratesInner, StDevByHeight).OrderBy(a => a.Count / a.StDev).Take(1).ToArray();
                double level = levels[0].Level, stDev = levels[0].StDev;
                var offset = stDev * DistanceIterations;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                bool ok = CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                  && isPriceInside;

                if (ok) {
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                }

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev7
            case TrailingWaveMethod.StDev7:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.AsEnumerable().Reverse().ToArray();
                if (!ratesInner.Any()) { var exc = new Exception("ratesInner is empty."); if (errorsCount++ > 1000) throw exc; Log = exc; return; }
                var levels = StDevsByHeight2(ratesInner, StDevByHeight).OrderBy(a => a.Count / a.StDev).Take(1).ToArray();
                double level = levels[0].Level;
                var offset = GetValueByTakeProfitFunction(TakeProfitFunction) / 2;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                bool ok = CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                  && isPriceInside;

                if (ok) {
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                }

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev31
            case TrailingWaveMethod.StDev31:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange, WaveStDevRatio = "0:1(" + WaveStDevRatio + ")", CorridorDistanceRatio } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                  CloseAtZero = false;
                };
                onOpenTradeLocal = t => {
                  CloseAtZero = false;
                };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) Debugger.Break();
                //var levels = CorridorByStDev(ratesInner, StDevByHeight);
                var levels = StDevsByHeight(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                double level = levels[0].Level, stDev = levels[0].StDev;
                var offset = stDev * DistanceIterations;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                var ratesArray = RatesArray.ReverseIfNot().SafeArray();
                var corrByWavesLength = CalcCorridorByMinWaveHeight(ratesArray, StDevByPriceAvg, 3);
                WaveShortLeft.ResetRates(ratesArray.CopyToArray(corrByWavesLength));
                var wavesLengthOk = corrByWavesLength < CorridorDistanceRatio;
                var angleOk = CorridorAngleFromTangent().Abs() <= TradingAngleRange;
                var corridorLengthOk = CorridorStats.Rates.Count.Div(RatesArray.Count) > WaveStDevRatio;
                bool ok = angleOk && wavesLengthOk && corridorLengthOk
                  && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                  && (!Trades.Any() || isPriceInside);

                if (ok) {
                  corridorLevel = level;
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                }
                if (IsAutoStrategy && Trades.Any() && !isTradingHourLocal())
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev32
            case TrailingWaveMethod.StDev32:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange, WaveStDevRatio = "0:1(" + WaveStDevRatio + ")", CorridorDistanceRatio } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                  CloseAtZero = false;
                };
                onOpenTradeLocal = t => {
                  CloseAtZero = false;
                };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) Debugger.Break();
                //var levels = CorridorByStDev(ratesInner, StDevByHeight);
                var levels = StDevsByHeight(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                double level = levels[0].Level, stDev = levels[0].StDev;
                var offset = stDev * DistanceIterations;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                var ratesArray = RatesArray.ReverseIfNot().SafeArray();
                var corrByWavesLength = CalcCorridorByMinWaveHeight(ratesArray, StDevByPriceAvg, 3);
                WaveShortLeft.ResetRates(ratesArray.CopyToArray(corrByWavesLength));
                var wavesLengthOk = corrByWavesLength < CorridorDistanceRatio;
                var angleOk = CorridorAngleFromTangent().Abs() <= TradingAngleRange;
                var corridorLengthOk = CorridorStats.Rates.Count.Div(RatesArray.Count) > WaveStDevRatio;
                bool ok = angleOk && wavesLengthOk && corridorLengthOk
                  && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                  && (!Trades.Any() || isPriceInside);

                if (ok) {
                  corridorLevel = level;
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => {
                      sr.TradesCountEx = CorridorCrossesMaximum;
                      sr.ResetPricePosition();
                    });
                }
                if (IsAutoStrategy && !isTradingHourLocal())
                  _buySellLevelsForEach(sr => sr.CanTradeEx = wavesLengthOk);

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev33
            case TrailingWaveMethod.StDev33:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange, WaveStDevRatio = "1-...(" + WaveStDevRatio + ")" } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                  CloseAtZero = false;
                };
                onOpenTradeLocal = t => {
                  CloseAtZero = false;
                };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) Debugger.Break();
                //var levels = CorridorByStDev(ratesInner, StDevByHeight);
                var levels = StDevsByHeight(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                double level = levels[0].Level, stDev = levels[0].StDev;
                var offset = stDev * DistanceIterations;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                var ratesArray = RatesArray.ReverseIfNot().SafeArray();
                var corrByWavesLength = CalcCorridorByMinWaveHeight(ratesArray, StDevByPriceAvg, 3);
                WaveShortLeft.ResetRates(ratesArray.CopyToArray(corrByWavesLength));
                var angleOk = CorridorAngleFromTangent().Abs() <= TradingAngleRange;
                var corridorLengthOk = corrByWavesLength.Div(CorridorStats.Rates.Count) > WaveStDevRatio;
                bool ok = angleOk && corridorLengthOk
                  && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                  && (!Trades.Any() || isPriceInside);
                if (ok) {
                  corridorLevel = level;
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                    _buySellLevelsForEach(sr => {
                      sr.CanTradeEx = true;
                      sr.TradesCountEx = CorridorCrossesMaximum;
                      sr.ResetPricePosition();
                    });
                }
                if (IsAutoStrategy && Trades.Any() && !isTradingHourLocal())
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDev34
            case TrailingWaveMethod.StDev34:
              #region FirstTime
              if (firstTime) {
                ScanCorridorBy = ScanCorridorFunction.StDevSplits2;
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { DoAdjustExitLevelByTradeTime, ScanCorridorBy } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > -PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                double h = WaveShortLeft.RatesMax, l = WaveShortLeft.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) { Log = new Exception("StDev3: ratesInner is empty!"); break; }
                //var levels = CorridorByStDev(ratesInner, StDevByHeight);
                var levels = StDevsByHeight(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                if (levels.Any()) {
                  double level = levels[0].Level, stDev = levels[0].StDev;
                  var offset = (stDev * DistanceIterations);
                  CenterOfMassBuy = level + offset;
                  CenterOfMassSell = level - offset;
                  var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                  bool ok = (!Trades.Any() || isPriceInside);

                  CenterOfMassLevels.Clear();
                  CenterOfMassLevels.AddRange(levels.Skip(1).Select(a => new[] { a.Level/* + a.StDev, a.Level - a.StDev*/ }).SelectMany(a => a));
                  if (ok) {
                    var corridorJumped = _buyLevel.Rate.Avg(_sellLevel.Rate).Abs(CenterOfMassBuy.Avg(CenterOfMassSell)) 
                      > _buyLevel.Rate.Abs(_sellLevel.Rate) / 2;
                    _buyLevel.RateEx = CenterOfMassBuy;
                    _sellLevel.RateEx = CenterOfMassSell;
                    if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                      _buySellLevelsForEach(sr => sr.CanTradeEx = !corridorJumped);
                    runOnce += () => {
                      _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                      return true;
                    };
                  }
                  if (IsAutoStrategy && Trades.Any() && !isTradingHourLocal())
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region LongCross
            case TrailingWaveMethod.LongCross:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime } + "");
                onCloseTradeLocal = t => { };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                var middle = CorridorStats.Coeffs.LineValue();
                var levels = CorridorStats.Rates.Select(GetTradeEnterBy()).ToArray().CrossedLevelsWithGap(InPoints(1));
                Func<Func<double,bool>,double> getLevel= f => levels.Where(t => f(t.Item1)).OrderByDescending(t => t.Item2).Select(t => t.Item1).DefaultIfEmpty(double.NaN).First();
                var levelUp = getLevel(l => l > middle);
                var levelDown = getLevel(l => l < middle);
                CenterOfMassBuy = levelUp.IfNaN(CenterOfMassBuy);
                CenterOfMassSell = levelDown.IfNaN(CenterOfMassSell);
                var isCountOk = CorridorStats.Rates.Count >= RatesArray.Count * WaveStDevRatio;// levels[0].Count < StDevLevelsCountsAverage;
                var bsHieght = CenterOfMassBuy.Abs(CenterOfMassSell);
                bool ok = isCountOk && bsHieght < CorridorStats.StDevByHeight * 2;
                if (ok) {
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy) {
                    _buySellLevelsForEach(sr => {
                      sr.CanTradeEx = true;
                    });
                  }
                }
                if (IsAutoStrategy && (!isCountOk || bsHieght * 2.5 > RatesHeight))
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

              }
              adjustExitLevels1();
              break;
            #endregion
            #region Rsd
            case TrailingWaveMethod.Rsd:
              #region firstTime
              if (firstTime) {
                LogTrades = !IsInVitualTrading;
                DoNews = !IsInVitualTrading;
                Log = new Exception(new {
                  CorridorDistanceRatio,
                  WaveStDevRatio_LenthRatio = WaveStDevRatio,
                  DistanceIterations_RsdRatio = DistanceIterations,
                  PolyOrder_FractalRatio = PolyOrder
                } + "");
                MagnetPrice = double.NaN;
                onCloseTradeLocal = t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  triggerRsd.Off();
                };
              }
              #endregion
              {
                var corridorRates = CorridorStats.Rates;
                var fractals = FractalsRsd(corridorRates, PolyOrder, _priceAvg, _priceAvg);
                if (fractals.Count == 2) {
                  CenterOfMassBuy = fractals[true].Average();
                  CenterOfMassSell = fractals[false].Average();
                  MagnetPrice = fractals.SelectMany(f => f.Value).Average();
                }
                var buySellHeight = CenterOfMassBuy - CenterOfMassSell;
                var lengthOk = WaveStDevRatio > 0 
                  ? CorridorStats.Rates.Count > CorridorDistanceRatio * WaveStDevRatio 
                  : CorridorStats.Rates.Count <= CorridorDistanceRatio;
                var angleOk = TradingAngleRange >= 0 ? CorridorAngle.Abs() > TradingAngleRange : CorridorAngle.Abs() < TradingAngleRange.Abs();
                var heightOk = buySellHeight < CorridorStats.HeightByRegression / 2;
                triggerRsd.Set(lengthOk && angleOk && heightOk, () => {
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  rsdStartDate = CurrentPrice.Time;
                  _buySellLevelsForEach(sr => sr.ResetPricePosition());
                  if (IsAutoStrategy) _buySellLevelsForEach(sr => sr.CanTradeEx = TradingAngleRange <= 0 || sr.IsBuy && CorridorAngle < 0 || sr.IsSell && CorridorAngle > 0);
                });
                triggerRsd.Off(lengthOk, () => {
                  //_buySellLevelsForEach(sr => sr.CanTradeEx = false);
                });
                if (IsAutoStrategy && (CurrentPrice.Time - rsdStartDate).TotalMinutes / BarPeriodInt > CorridorStats.Rates.Count) {
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  CloseTrades("Trades start date");
                }
              }
              adjustExitLevels1();
              break;
            #endregion
            #region Backdoor
            case TrailingWaveMethod.Backdoor:
              #region firstTime
              if (firstTime) {
                intTrigger.Set(false).Value = 0;
                doubleTrigger.Set(false).Value = 0;
                corridorDate = DateTime.MinValue;
                LogTrades = !IsInVitualTrading;
                DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, CorrelationMinimum, CorridorCrossesMaximum,DistanceIterations } + "");
                MagnetPrice = double.NaN;
                onCloseTradeLocal = t => {
                  if (t.PL >= -PriceSpreadAverageInPips) {
                    _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    isPriceUp = null;
                  }
                };
              }
              #endregion
              {
                Action onCorridor = () => {
                  if (IsAutoStrategy) _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.TradesCountEx = CorridorCrossesMaximum; });
                };
                Func<Func<Rate, double>, double> price = p => p(RateLast) > 0 ? p(RateLast) : p(RatePrev);
                var angleOk = CorridorAngleFromTangent().Abs() < 1;
                var buy = price(r => r.PriceAvg21);
                var sell = price(r => r.PriceAvg2);
                var tradeRangeOk = buy.Abs(sell) > InPoints(CorrelationMinimum) && buy.Min(sell) > 0;
                var corridorLenghOk = CorridorStats.Rates.Count.Div(CorridorDistanceRatio) > WaveStDevRatio;
                var volatilityOk = GetVoltageAverage() > DistanceIterations;
                if (angleOk && tradeRangeOk && corridorLenghOk && volatilityOk) {
                  if (CalculateLastPrice(RateLast, GetTradeEnterBy(true)).Between(sell, buy) && isPriceUp.GetValueOrDefault(false) != true) {
                    isPriceUp = true;
                    BuyLevel.RateEx = buy;
                    SellLevel.RateEx = sell;
                    onCorridor();
                  }
                  buy = price(r => r.PriceAvg3);
                  sell = price(r => r.PriceAvg31);
                  if (CalculateLastPrice(RateLast, GetTradeEnterBy(false)).Between(sell, buy) && isPriceUp.GetValueOrDefault(true) != false) {
                    isPriceUp = false;
                    BuyLevel.RateEx = buy;
                    SellLevel.RateEx = sell;
                    onCorridor();
                  }
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region FFT
            case TrailingWaveMethod.Fft:
              #region firstTime
              if (firstTime) {
                LogTrades = !IsInVitualTrading;
                DoNews = !IsInVitualTrading;
                Log = new Exception(new { DistanceIterations } + "");
                MagnetPrice = double.NaN;
                onCloseTradeLocal = t => { if (t.PL > 0)_buySellLevelsForEach(sr => sr.CanTradeEx = false); };
              }
              #endregion
              {
                var priceHigh = _priceAvg;
                var priceLow = _priceAvg;
                Fractals = RatesArray.Reverse<Rate>().ToArray().Fractals(FttMax * 2, priceHigh, priceLow, true);
                var fractal = Fractals.SelectMany(f => f).ToArray().FirstOrDefault();
                var fractalOk = fractal != null && fractal.StartDate.Between(CorridorStats.StartDate.AddMinutes(-10), CorridorStats.StartDate.AddMinutes(10));

                var voltsOk = GetVoltage(RateLast) > GetVoltageAverage() && GetVoltageAverage() > DistanceIterations;
                var tradeOk = fractalOk && voltsOk;
                triggerRsd.Set(tradeOk, () => {
                  CenterOfMassBuy = RateLast.PriceAvg2;
                  CenterOfMassSell = RateLast.PriceAvg3;
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy) _buySellLevelsForEach(sr => {
                    sr.ResetPricePosition();
                    sr.CanTradeEx = true;
                    sr.TradesCount = CorridorCrossesMaximum;
                    CloseAtZero = false;
                  });
                });
                triggerRsd.Off(tradeOk);
                if (GetVoltageAverage() < DistanceIterations)
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region Magnet
            case TrailingWaveMethod.Magnet:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
                Log = new Exception(new { CorrelationMinimum, WaveStDevRatio } + "");
                MagnetPrice = double.NaN;
              }
              #endregion
              if (CorridorAngleFromTangent().Abs().ToInt() <= TradingAngleRange && !double.IsNaN(MagnetPrice)) {
                Func<Rate, double> price = r => r.PriceCMALast;
                var rates = CorridorStats.Rates.Select(price);
                var ratesAboveBelow = rates.GroupBy(r => r.Sign(MagnetPrice)).ToArray();
                var ratesAbove_ = ratesAboveBelow.Where(g => g.Key == 1).SingleOrDefault();
                var ratesAbove = ratesAbove_ == null ? 0 : ratesAbove_.Average(r => r - MagnetPrice);
                var ratesBellow_ = ratesAboveBelow.Where(g => g.Key == -1).SingleOrDefault();
                var ratesBellow = ratesBellow_ == null ? 0 : ratesBellow_.Average(r => -r + MagnetPrice);
                MagnetPriceRatio = ratesAbove.Ratio(ratesBellow);
                if (ratesAbove.Min(ratesBellow) > 0 && MagnetPriceRatio > WaveStDevRatio) {
                  var offset = CorridorStats.StDevByHeight * CorrelationMinimum;// rates.Average(r => r.Abs(MagnetPrice)) * 2;
                  _buyLevel.RateEx = MagnetPrice + ratesAbove.ValueByPosition(0, ratesAbove + ratesBellow, 0, offset);
                  _sellLevel.RateEx = MagnetPrice - ratesBellow.ValueByPosition(0, ratesAbove + ratesBellow, 0, offset);
                  _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy);
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region Void
            case TrailingWaveMethod.Void:
              #region firstTime
              if (firstTime) {
                Log = new Exception(new { CorrelationMinimum } + "");
                onCloseTradeLocal = t => {
                  if (t.PL >= -PriceSpreadAverageInPips) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) { Log = new Exception("StDev3: ratesInner is empty!"); break; }
                //var levels = CorridorByStDev(ratesInner, StDevByHeight);
                var levels = StDevsByHeight1(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                if (levels.Any()) {
                  double level = levels[0].Level, stDev = levels[0].StDev;
                  var offset = stDev * DistanceIterations;
                  CenterOfMassBuy = level + offset;
                  CenterOfMassSell = level - offset;
                }
                var rates = RatesArray.ReverseIfNot();
                var rateLow = rates.Crosses(GetVoltageAverage(), GetVoltage).FirstOrDefault();
                var rateHigh = rates.Crosses(GetVoltageHigh(), GetVoltage).FirstOrDefault();
                bool? isPU = VoltageCurrent > GetVoltageHigh() ? true : VoltageCurrent < GetVoltageAverage() ? false : (bool?)null;
                  var height = StDevByHeight.Min(StDevByPriceAvg) / 2;
                Action setLevels = () => {
                  isPriceUp = isPU;
                  BuyLevel.RateEx = CenterOfMassBuy;
                  SellLevel.RateEx = CenterOfMassSell;
                  LineTimeMin = CurrentPrice.Time;
                  _buySellLevelsForEach(sr => sr.TradesCountEx = CorridorCrossesMaximum);
                };
                var corridorOk = CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum);
                var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                if (corridorOk && isPriceInside && isPU.HasValue)
                  setLevels();
                if (IsAutoStrategy)
                  _buySellLevelsForEach(sr => sr.CanTradeEx = isPU.GetValueOrDefault());
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region Void1
            case TrailingWaveMethod.Void1:
              #region firstTime
              if (firstTime) {
                Log = new Exception(new { CorrelationMinimum } + "");
                onCloseTradeLocal = t => {
                  if (t.PL >= -PriceSpreadAverageInPips) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) { Log = new Exception("StDev3: ratesInner is empty!"); break; }
                var levels = StDevsByHeight1(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                if (levels.Any()) {
                  double level = levels[0].Level, stDev = levels[0].StDev;
                  var offset = stDev * DistanceIterations;
                  CenterOfMassBuy = level + offset;
                  CenterOfMassSell = level - offset;
                }
                var corridorOk = this.VoltageCurrent * CorridorStats.StDevByHeightInPips > CorrelationMinimum;
                var isPriceInside = CalculateLastPrice(RateLast, GetTradeEnterBy()).Between(CenterOfMassSell, CenterOfMassBuy);
                if (IsAutoStrategy) {
                  if (!_buySellLevels.All(sr=>sr.CanTrade) && corridorOk && isTradingHourLocal()) {
                    BuyLevel.RateEx = CenterOfMassBuy;
                    SellLevel.RateEx = CenterOfMassSell;
                    _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                  }
                  if (VoltageCurrent < GetVoltageAverage() || !isTradingHourLocal())
                    _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDevRsd
            case TrailingWaveMethod.StDevRsd:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { RateLast.PriceAvg2 = 2; };
              }
              #endregion
              {
                Func<bool> calcAngleOk = () => TradingAngleRange >= 0
                  ? CorridorAngleFromTangent().Abs() >= TradingAngleRange : CorridorAngleFromTangent().Abs() < TradingAngleRange.Abs();
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) { Log = new Exception(TrailingDistanceFunction + ": ratesInner is empty!"); break; }
                var levels = StDevsByHeight1(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                if (levels.Any()) {
                  double level = levels[0].Level, stDev = levels[0].StDev;
                  var rsd = ratesInner.Select(_priceAvg).Integral(60).AsParallel().Select(g => g.RsdNormalized()).Average();
                  var offset = stDev / rsd;
                  CenterOfMassBuy = level + offset;
                  CenterOfMassSell = level - offset;
                  var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                  var angleOk = calcAngleOk() && VoltageCurrent < GetVoltageAverage();
                  bool ok = angleOk
                    && isTradingHourLocal()
                    && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                    && (!Trades.Any() || isPriceInside);

                  CenterOfMassLevels.Clear();
                  CenterOfMassLevels.AddRange(levels.Skip(1).Select(a => new[] { a.Level/* + a.StDev, a.Level - a.StDev*/ }).SelectMany(a => a));
                  if (ok) {
                    corridorLevel = level;
                    _buyLevel.RateEx = CenterOfMassBuy;
                    _sellLevel.RateEx = CenterOfMassSell;
                    if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                      _buySellLevelsForEach(sr => {
                        sr.CanTradeEx = true;
                        sr.ResetPricePosition();
                      });
                  }
                  if (IsAutoStrategy && !isTradingHourLocal())
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region StDevRsd1
            case TrailingWaveMethod.StDevRsd1:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime, CorrelationMinimum, TradingAngleRange } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => { RateLast.PriceAvg2 = 2; };
              }
              #endregion
              {
                Func<bool> calcAngleOk = () => TradingAngleRange >= 0
                  ? CorridorAngleFromTangent().Abs() >= TradingAngleRange : CorridorAngleFromTangent().Abs() < TradingAngleRange.Abs();
                double h = CorridorStats.RatesMax, l = CorridorStats.RatesMin;
                var ratesInner = RatesArray.Where(r => r.PriceAvg.Between(l, h)).Reverse().ToArray();
                if (!ratesInner.Any()) { Log = new Exception(TrailingDistanceFunction + ": ratesInner is empty!"); break; }
                var levels = StDevsByHeight1(ratesInner, StDevByHeight).OrderByDescending(a => a.Count / a.StDev).Take(1).ToArray();
                if (levels.Any()) {
                  double level = levels[0].Level, stDev = levels[0].StDev;
                  var rsd = ratesInner.Select(_priceAvg).Integral(60).AsParallel().Select(g => g.RsdNormalized()).Average();
                  var offset = stDev / rsd;
                  CenterOfMassBuy = level + offset;
                  CenterOfMassSell = level - offset;
                  var isPriceInside = CurrentPrice.Average.Between(CenterOfMassSell, CenterOfMassBuy);
                  var voltsAvg = GetVoltageAverage();
                  var angleOk = calcAngleOk() && WaveShort.Rates.Select(GetVoltage).Any(v => v < voltsAvg);
                  bool ok = angleOk
                    && isTradingHourLocal()
                    && CenterOfMassBuy.Abs(CenterOfMassSell) > InPoints(CorrelationMinimum)
                    && (!Trades.Any() || isPriceInside);

                  CenterOfMassLevels.Clear();
                  CenterOfMassLevels.AddRange(levels.Skip(1).Select(a => new[] { a.Level/* + a.StDev, a.Level - a.StDev*/ }).SelectMany(a => a));
                  if (ok) {
                    corridorLevel = level;
                    _buyLevel.RateEx = CenterOfMassBuy;
                    _sellLevel.RateEx = CenterOfMassSell;
                    if (IsAutoStrategy && isPriceInside && !_buySellLevels.All(sr => sr.CanTrade))
                      _buySellLevelsForEach(sr => {
                        sr.CanTradeEx = true;
                        sr.ResetPricePosition();
                      });
                  }
                  if (IsAutoStrategy && !isTradingHourLocal())
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region Ghost
            case TrailingWaveMethod.Ghost:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { ghostLevelOffset } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > -PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => {
                  buyCloseLevel.IsGhost = sellCloseLevel.IsGhost = false;
                };
                ghostLevelOffset.ValueChanged += (s, e) => SuppRes.ToList().ForEach(sr => sr.Rate += ghostLevelOffset.Value);

              }
              #endregion
              {
                if (IsAutoStrategy && !Trades.Any() && !SuppRes.Any(sr => sr.IsGhost) && CorridorAngleFromTangent().Abs() < TradingAngleRange) {
                  BuyLevel.RateEx = RateLast.PriceAvg2;
                  SellLevel.RateEx = RateLast.PriceAvg3;
                  var offset = BuyLevel.Rate.Abs(SellLevel.Rate) / CorrelationMinimum;
                  buyCloseLevel.Rate = BuyLevel.Rate - offset;
                  sellCloseLevel.Rate = SellLevel.Rate + offset;
                  buyCloseLevel.IsGhost = sellCloseLevel.IsGhost = true;
                  SuppRes.ToList().ForEach(sr => {
                    sr.CanTrade = true;
                    sr.ResetPricePosition();
                  });
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region Ghost2
            case TrailingWaveMethod.Ghost2:
              #region FirstTime
              if (firstTime) {
                LogTrades = DoNews = !IsInVitualTrading;
                Log = new Exception(new { ghostLevelOffset } + "");
                onCloseTradeLocal = t => {
                  if (t.PL > -PriceSpreadAverage) _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
                onOpenTradeLocal = t => {
                  buyCloseLevel.IsGhost = sellCloseLevel.IsGhost = false;
                };
                Func<SuppRes, double, bool> adjust = (sr, v) => v > 0 && (!sr.IsGhost && sr.IsBuy || sr.IsGhost && sr.IsSell)
                  || v < 0 && (!sr.IsGhost && sr.IsSell || sr.IsGhost && sr.IsBuy);
                ghostLevelOffset.ValueChanged += (s, e) => SuppRes.ToList().ForEach(sr =>
                  sr.Rate += adjust(sr, ghostLevelOffset.Value) ? ghostLevelOffset.Value : 0);
              }
              #endregion
              {
                if (IsAutoStrategy && !Trades.Any() && !SuppRes.Any(sr => sr.IsGhost) && CorridorAngleFromTangent().Abs() < TradingAngleRange) {
                  buyCloseLevel.RateEx = RateLast.PriceAvg2;
                  sellCloseLevel.RateEx = RateLast.PriceAvg3;
                  var offset = BuyLevel.Rate.Abs(SellLevel.Rate) / CorrelationMinimum;
                  BuyLevel.Rate = buyCloseLevel.Rate + offset;
                  SellLevel.Rate = sellCloseLevel.Rate - offset;
                  buyCloseLevel.IsGhost = sellCloseLevel.IsGhost = true;
                  SuppRes.ToList().ForEach(sr => {
                    sr.CanTrade = true;
                    sr.ResetPricePosition();
                  });
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            default: throw new Exception(TrailingDistanceFunction + " is not supported.");
          }
          if (firstTime) {
            firstTime = false;
            BarsCountCalc = null;
            ForEachSuppRes(sr => sr.ResetPricePosition());
            LogTrades = !IsInVitualTrading;
            DoNews = !IsInVitualTrading;
          }
        };
        #endregion
        #endregion

        #region On Trade Close
        _strategyExecuteOnTradeClose = t => {
          waveTradeOverTrigger.Off();
          if (!Trades.Any() && isCurrentGrossOk()) {
            exitCrossed = false;
            ForEachSuppRes(sr => sr.InManual = false);
            DistanceIterationsRealClear();
            if (!IsAutoStrategy)
              IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
            _buySellLevelsForEach(sr => sr.CanTrade = false);
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }

          if (isCurrentGrossOk() || exitCrossed) setCloseLevels(true);
          if (onCloseTradeLocal != null)
            onCloseTradeLocal(t);
          if (TurnOffOnProfit && t.PL >= PriceSpreadAverageInPips) Strategy = Strategy & ~Strategies.Auto;
          CloseAtZero = _trimAtZero = _trimToLotSize = exitCrossed = false;
        };
        #endregion

        #region On Trade Open
        _strategyExecuteOnTradeOpen = trade => {
          SuppRes.ForEach(sr => sr.ResetPricePosition());
          if (onOpenTradeLocal != null) onOpenTradeLocal(trade);
        };
        #endregion

        #region _adjustEnterLevels
        _adjustEnterLevels += () => adjustEnterLevels();
        _adjustEnterLevels += () => turnOff(() => _buySellLevelsForEach(sr => { if (IsAutoStrategy) sr.CanTradeEx = false; }));
        _adjustEnterLevels += () => exitFunc()();
        _adjustEnterLevels += () => {
          try {
            if (IsTradingActive) {
              Func<bool, double> enter = isBuy => CalculateLastPrice(RateLast, GetTradeEnterBy(isBuy));
              _buyLevel.SetPrice(enter(true));
              _sellLevel.SetPrice(enter(false));
              buyCloseLevel.SetPrice(exitPrice(false));
              sellCloseLevel.SetPrice(exitPrice(true));
            } else
              SuppRes.ForEach(sr => sr.ResetPricePosition());
          } catch (Exception exc) { Log = exc; }
        };
        _adjustEnterLevels += () => { if (runOnce != null && runOnce()) runOnce = null; };
        #endregion
      }

      #region if (!IsInVitualTrading) {
      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion
      #endregion

      #region ============ Run =============
      if (IsTradingActive)
        _adjustEnterLevels();
      #endregion
    }

    private void AdjustExitLevelsByTradeTime(Action<double, double> adjustExitLevels) {
      Func<double, IEnumerable<double>> rateSinceTrade = def => {
        var d = Trades.Max(t => t.Time);
        d = d - ServerTime.Subtract(d);
        return RatesArray.ReverseIfNot().TakeWhile(r => r.StartDate >= d).Select(_priceAvg).DefaultIfEmpty(def);
      };
      var buyLevel = Trades.HaveBuy() ? rateSinceTrade(_buyLevel.Rate).Min().Min(ExitByBuySellLevel ? _buyLevel.Rate : double.NaN) : _buyLevel.Rate;
      var sellLevel = Trades.HaveSell() ? rateSinceTrade(_sellLevel.Rate).Max().Max(ExitByBuySellLevel ? _sellLevel.Rate : double.NaN) : _sellLevel.Rate;
      adjustExitLevels(buyLevel, sellLevel);
    }

    public static IDictionary<bool, double[]> FractalsRsd(IList<Rate> rates, int fractalLength, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var prices = rates.Select(r => new { PriceHigh = priceHigh(r), PriceLow = priceLow(r) }).ToArray();
      var indexMiddle = fractalLength / 2;
      var zipped = prices.Zip(prices.Skip(1), (f, s) => new[] { f, s }.ToList());
      for (var i = 2; i < fractalLength; i++)
        zipped = zipped.Zip(prices.Skip(i), (z, v) => { z.Add(v); return z; });
      var zipped2 = zipped.Where(z => z.Count == fractalLength)
        .Select(z => new { max = z.Max(a => a.PriceHigh), min = z.Min(a => a.PriceLow), middle = z[indexMiddle] })
        .Select(a => new { price = a.middle, isUp = a.max == a.middle.PriceHigh, IsDpwm = a.middle.PriceLow == a.min })
        .ToArray();
      return zipped2
        .Where(a => a.isUp || a.IsDpwm)
        .GroupBy(a => a.isUp, a => a.isUp ? a.price.PriceHigh : a.price.PriceLow)
        .ToDictionary(g => g.Key, g => g.ToArray());
    }
    IList<Rate> CorridorByVerticalLineCrosses(IList<Rate> rates, int lengthMin, out double levelOut) {
      Func<double, Tuple<Rate, Rate>, bool> isOk = (l, t) => l.Between(t.Item1.PriceAvg, t.Item2.PriceAvg);
      IList<Tuple<Rate, Rate>> ratesFused = rates.Zip(rates.Skip(1), (f, s) => new Tuple<Rate, Rate>(f, s)).ToArray();
      var maxIntervals = new { span = TimeSpan.Zero, level = double.NaN}.IEnumerable().ToList();
      var height = rates.Take(lengthMin).Height();
      var levelMIn = rates.Take(lengthMin).Min(t => t.PriceAvg);
      var levels = Enumerable.Range(1, InPips(height).ToInt() - 1).Select(i => levelMIn + InPoints(i)).ToArray();
      levels.ForEach(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t)).Select(t => t.Item2).ToArray();
        if (ratesCrossed.Length > 3) {
          var ratesCrossesLinked = ratesCrossed.ToLinkedList();
          var span = TimeSpan.MaxValue;
          var corridorOk = false;
          //ratesCrossesLinked.ForEach(fs => 
          for(var node = ratesCrossesLinked.First.Next; node != null; node = node.Next) {
            var interval = node.Previous.Value.StartDate - node.Value.StartDate;
            if (interval > span) {
              while (!maxIntervals.Any() && node!=null && (rates[0].StartDate - node.Value.StartDate).Duration().TotalMinutes < lengthMin) {
                node = node.Next;
              }
              corridorOk = node != null;
            }
            if (node != null)
              span = rates[0].StartDate - node.Value.StartDate;
            if (corridorOk || node == null) break;
          };
          if (corridorOk)
            maxIntervals.Add(new { span = span, level });
        }
      });
      var maxInterval = maxIntervals.Where(mi => mi.span.Duration().TotalMinutes >= lengthMin)
        .OrderByDescending(a => a.span).FirstOrDefault();
      if (maxInterval == null) {
        levelOut = double.NaN;
        return null;
      } else
        try {
          levelOut = maxInterval.level;
          var dateStop = rates[0].StartDate.Subtract(maxInterval.span);
          return rates.TakeWhile(r => r.StartDate >= dateStop).ToArray();
        } catch (Exception exc) {
          throw;
        }
    }

    IList<Rate> CorridorByVerticalLineCrosses2(IList<Rate> ratesOriginal, Func<Rate, double> price, int lengthMin, out double levelOut) {
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, Rate, Rate, bool> isOk = (l, Item1, Item2) => l.Between(price(Item1), price(Item2));
      var ratesFused = rates.Zip(rates.Skip(1), (f, s) => new { f.index, Item1 = f.rate, Item2 = s.rate }).ToArray();
      var maxIntervals = new { length = 0, level = double.NaN }.IEnumerable().ToList();
      var height = rates.Take(lengthMin).Height(a => price(a.rate));
      var levelMIn = rates.Take(lengthMin).Min(t => price(t.rate));
      var levels = Enumerable.Range(1, InPips(height).ToInt() - 1).Select(i => levelMIn + InPoints(i)).ToArray();
      levels.ForEach(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t.Item1, t.Item2)).Select(a => new { a.index, rate = a.Item2 }).ToArray();
        if (ratesCrossed.Length > 3) {
          var span = int.MaxValue;
          var corridorOk = false;
          for (var i = 1; i < ratesCrossed.Count(); i++) {
            var node = ratesCrossed[i];
            var interval = node.index - ratesCrossed[i - 1].index;
            if (interval > span) {
              if (node.index < lengthMin) {
                node = ratesCrossed.Zip(ratesCrossed.Skip(1), (a, b) => new { length = b.index - a.index, item = b })
                  .Where(a => a.length < span && a.item.index > lengthMin)
                  .Select(a => a.item).FirstOrDefault();
              }
              corridorOk = node != null;
            }
            if (node != null)
              span = node.index;
            if (corridorOk || node == null) break;
          };
          if (corridorOk)
            maxIntervals.Add(new { length = span, level });
        }
      });
      var maxInterval = maxIntervals.Where(mi => mi.length >= lengthMin).OrderByDescending(a => a.length).FirstOrDefault();
      if (maxInterval == null) {
        levelOut = double.NaN;
        return null;
      } else
        try {
          levelOut = maxInterval.level;
          return ratesOriginal.Take(maxInterval.length).ToArray();
        } catch {
          throw;
        }
    }

    IList<Rate> CorridorByVerticalLineLongestCross(Rate[] ratesOriginal, Func<Rate, double> price) {
      var point = InPoints(1);
      Func<long, long, double> getHeight = (index1, index2) => {
        var rates1 = new Rate[index2 - index1 + 1];
        Array.Copy(ratesOriginal, index1, rates1, 0, rates1.Length);
        var price0 = price(rates1[0]);
        return rates1.Average(r => price0.Abs(price(r)) / point).Round(0);
      };
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, Rate, Rate, bool> isOk = (l, Item1, Item2) => l.Between(price(Item1), price(Item2));
      var ratesFused = rates.Zip(rates.Skip(1), (f, s) => new { f.index, Item1 = f.rate, Item2 = s.rate }).ToArray();
      var levelMin = ratesOriginal.Min(price);
      var levels = Enumerable.Range(1, InPips(ratesOriginal.Height(price)).ToInt() - 1)
        .Select(level => levelMin + level * point).ToArray().AsParallel();
      var ratesByLevels = levels.Select(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t.Item1, t.Item2)).Select(a => new { a.index, rate = a.Item2 }).ToArray();
        var h0 = 0.0;
        var d0 = 0;
        return ratesCrossed.Zip(ratesCrossed.Skip(1)
          , (prev, next) => new { prev, next, distance = d0 = next.index - prev.index, height = h0 = getHeight(prev.index, next.index), dToH = (d0 / h0).Round(1) });
      }).SelectMany(a => a.Where(b => b.height > 0))
      .Distinct((a, b) => a.prev.index == b.prev.index && a.distance == b.distance)
      .ToList();
      var distanceMin = ratesByLevels.AverageByIterations(a => a.distance, (a, d) => a > d, 2).Average(a => a.distance);
      Func<double, double, bool> comp = (a, b) => a <= b;
      //ratesByLevels = ratesByLevels.Where(a => a.distance >= distanceMin).ToList().SortByLambda((a, b) => comp(a.dToH, b.dToH));
      //var dToHMin = ratesByLevels.AverageByIterations(a => a.dToH, comp, 2).Average(a => a.dToH);
      var winner = ratesByLevels/*.TakeWhile(a => comp(a.dToH, dToHMin))*/.OrderByDescending(a => a.distance).FirstOrDefault();
      if (winner == null) return null;
      var ratesOut = new Rate[winner.next.index - winner.prev.index + 1];
      Array.Copy(ratesOriginal, winner.prev.index, ratesOut, 0, ratesOut.Length);
      return ratesOut;
    }

    IList<Rate> CorridorByVerticalLineLongestCross2(Rate[] ratesOriginal, int indexMax, Func<Rate, double> price) {
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      Func<long, long, double> getHeight = (index1, index2) => {
        var rates1 = new Rate[index2 - index1 + 1];
        Array.Copy(ratesOriginal, index1, rates1, 0, rates1.Length);
        var price0 = price(rates1[0]);
        return rates1.Average(r => price0.Abs(price(r)) / point).Round(0);
      };
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, Rate, Rate, bool> isOk = (l, Item1, Item2) => l.Between(price(Item1), price(Item2));
      var ratesFused = rates.Zip(rates.Skip(1), (f, s) => new { f.index, Item1 = f.rate, Item2 = s.rate }).ToArray();
      var levelMin = (ratesOriginal.Min(price) * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var levels = Enumerable.Range(0, InPips(ratesOriginal.Height(price)).ToInt())
        .Select(level => levelMin + level * point).ToArray();
      var ratesByLevels = levels.AsParallel().Select(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t.Item1, t.Item2)).Select(a => new { a.index, rate = a.Item2 }).ToArray();
        return ratesCrossed.Zip(ratesCrossed.Skip(1), (prev, next) => new { prev, next, distance = next.index - prev.index });
      }).SelectMany(a => a.Where(b => b.prev.index <= indexMax))
      .Distinct((a, b) => a.prev.index == b.prev.index && a.distance == b.distance)
      .ToList();
      var distanceMin = ratesByLevels.AverageByIterations(a => a.distance, (a, d) => a > d, 2).Average(a => a.distance);
      Func<double, double, bool> comp = (a, b) => a <= b;
      //ratesByLevels = ratesByLevels.Where(a => a.distance >= distanceMin).ToList().SortByLambda((a, b) => comp(a.dToH, b.dToH));
      //var dToHMin = ratesByLevels.AverageByIterations(a => a.dToH, comp, 2).Average(a => a.dToH);
      var winner = ratesByLevels/*.TakeWhile(a => comp(a.dToH, dToHMin))*/.OrderByDescending(a => a.distance).FirstOrDefault();
      if (winner == null) return null;
      var ratesOut = new Rate[winner.next.index - winner.prev.index + 1];
      Array.Copy(ratesOriginal, winner.prev.index, ratesOut, 0, ratesOut.Length);
      return ratesOut;
    }

    class CorridorByStDev_Results {
      public double Level { get; set; }
      public double StDev { get; set; }
      public int Count { get; set; }
      public override string ToString() {
        return new { Level, StDev, Count } + "";
      }
    }
    IList<CorridorByStDev_Results> StDevsByHeight(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Select(price).DefaultIfEmpty(double.NaN).Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips.Sub(halfInPips).Max(halfInPips))
        .Select(levelInner => levelMin + levelInner * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }
    IList<CorridorByStDev_Results> StDevsByHeight1(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Select(price).DefaultIfEmpty(double.NaN).Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt()*0;
      var rangeHeightInPips = ratesHeightInPips - halfInPips * 2;
      var levels = ParallelEnumerable.Range(halfInPips, rangeHeightInPips.Max(0))
        .Select(levelInner => levelMin + levelInner * point);
      var levelsByCount = levels.Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }

    IList<CorridorByStDev_Results> StDevsByHeight2(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Select(price).DefaultIfEmpty(double.NaN).Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height;
      var halfInPips = 0*InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips.Sub(halfInPips * 2).Max(halfInPips))
        .Select(levelInner => levelMin + levelInner * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }
    IList<CorridorByStDev_Results> StDevsByHeight3(IList<Rate> ratesReversed, double height) {
      var half = height / 2;
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var prices = ratesReversed.Select(price).DefaultIfEmpty(double.NaN).ToArray();
      var levelMin = (prices.Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var levelMax = (prices.Max() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var levels = Range.Double(levelMin + half * 2, levelMax - half * 2, point).ToArray();
      return StDevsByHeightBase(ratesReversed, half, price, levels);
    }

    private static IList<CorridorByStDev_Results> StDevsByHeightBase(IList<Rate> ratesReversed, double half, Func<Rate, double> price, double[] levels) {
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(price), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }

    IList<CorridorByStDev_Results> CorridorByStDev(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Min(price) * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips.Sub(halfInPips).Max(halfInPips))
        .Select(levelInner => levelMin + levelInner * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      StDevLevelsCountsAverage = levelsByCount.Select(a => (double)a.count).ToArray().AverageByIterations(2, true).DefaultIfEmpty(double.NaN).Average().ToInt();
      levelsByCount.Sort(a => -a.stDev);
      var levelsGrouped = levelsByCount//.Where(a => a.count >= StDevLevelsCountsAverage).ToArray()
        .GroupByLambda((a, b) => a.level.Abs(b.level) < height * 2)
        .Select(k => new { k.Key.level, k.Key.stDev,k.Key.count }).ToList();
      levelsGrouped.Sort(a => -a.stDev);
      return levelsGrouped.Select(a => new CorridorByStDev_Results() {
        Level = a.level,StDev = a.stDev,Count = a.count
      }).ToArray();
    }

    #region StDevLevelsRatio
    private double _StDevLevelsCountsAverage;
    public double StDevLevelsCountsAverage {
      get { return _StDevLevelsCountsAverage; }
      set {
        if (_StDevLevelsCountsAverage != value) {
          _StDevLevelsCountsAverage = value;
          OnPropertyChanged("StDevLevelsCountsAverage");
        }
      }
    }

    #endregion
    double CorridorByDensity(IList<Rate> ratesOriginal,double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesOriginal.Min(price) * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesOriginal.Height(price)).ToInt();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips - halfInPips)
        .Select(level => levelMin + level * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(level =>
        new { level, count = ratesOriginal.Count(r => isOk(level - half, level + half, r)) }
      ).ToList();
      return levelsByCount.Aggregate((p, n) => p.count > n.count ? p : n).level;
    }
    private double _buyLevelRate = double.NaN;
    private double _sellLevelRate = double.NaN;

    double _MagnetPriceRatio = double.NaN;

    public double MagnetPriceRatio {
      get { return _MagnetPriceRatio; }
      set {
        if (_MagnetPriceRatio == value) return;
        _MagnetPriceRatio = value;
        OnPropertyChanged(() => MagnetPriceRatio);
      }
    }

    List<double> _CenterOfMassLevels = new List<double>();

    public List<double> CenterOfMassLevels {
      get { return _CenterOfMassLevels; }
      private set { _CenterOfMassLevels = value; }
    }

    ILookup<bool, Rate> _Fractals = "".ToLookup(c => true, c => (Rate)null);
    public ILookup<bool, Rate> Fractals {
      get { return _Fractals; }
      set { _Fractals = value; }
    }

    public int FttMax { get; set; }

    public bool MustExitOnReverse { get; set; }
  }
}
