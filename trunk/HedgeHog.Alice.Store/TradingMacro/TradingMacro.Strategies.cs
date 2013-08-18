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
      }
    }
    bool _trimAtZero;
    bool _trimToLotSize;

    #region Corridor Start/Stop
    public double StartStopDistance { get; set; }
    double GetDistanceByDates(DateTime start, DateTime end) {
      var a = RatesArray.FindBar(start);
      var b = RatesArray.FindBar(end);
      return a.Distance - b.Distance;
    }
    public double CorridorDistanceInPips { get { return InPips(CorridorDistance); } }
    public double CorridorDistance {
      get {
        if (CorridorStats.StopRate == null && CorridorStopDate == DateTime.MinValue) return 0;
        if (CorridorStats.StopRate == null)
          return GetDistanceByDates(CorridorStartDate.Value, CorridorStopDate);
        return CorridorStats.Rates.LastBC().Distance - CorridorStats.StopRate.Distance;
      }
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
          StrategyAction();
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
          if (_CorridorStopDate == value) return;
          _CorridorStopDate = value;
          if (value == RateLast.StartDate)
            CorridorStats.StopRate = RateLast;
          else {
            var index = RatesArray.IndexOf(new Rate() { StartDate = value });
            CorridorStats.StopRate = RatesArray.Reverse<Rate>().SkipWhile(r => r.StartDate > value).First();
            if (CorridorStats.StopRate != null)
              _CorridorStopDate = CorridorStats.StopRate.StartDate;
          }
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
      h = l = CorridorStats.StDevByHeight;
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
        TurnOffSuppRes(RatesArray.Select(r => r.PriceAvg).DefaultIfEmpty().Average());
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
          double[] hl = getStDev == null ? new[] { CorridorStats.StDevByHeight, CorridorStats.StDevByHeight } : getStDev();
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
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
        Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
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
          CloseTrades(lot, "exitCrossHandler:sr.IsSupport=" + sr.IsSupport);
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
          Func<bool, double> r = isBuy => CalculateLastPrice(RateLast, GetTradeExitBy(isBuy));
          if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
            if (tradesCount == 0) {
              buyCloseLevel.RateEx = crossLevelDefault(true);
              buyCloseLevel.ResetPricePosition();
              sellCloseLevel.RateEx = crossLevelDefault(false);
              sellCloseLevel.ResetPricePosition();
            } else {
              //buyCloseLevel.SetPrice(r(false));
              //sellCloseLevel.SetPrice(r(true));
              if (!Trades.Any()) {
                adjustExitLevels(buyLevel, sellLevel);
                buyCloseLevel.ResetPricePosition();
                sellCloseLevel.ResetPricePosition();
                return;
              } else {
                var close0 = CloseAtZero || _trimAtZero || _trimToLotSize || isProfitOk();
                var tpCloseInPips = close0 ? 0 : TakeProfitPips - CurrentLossInPips.Min(0);
                var tpColse = InPoints(tpCloseInPips.Min(TakeProfitPips * TakeProfitLimitRatio));
                var ratesShort = RatesArray.Skip(RatesArray.Count - PriceCmaLevels.Max(5)+1).ToArray();
                var priceAvgMax = ratesShort.Max(rate => GetTradeExitBy(true)(rate)) - PointSize / 100;
                var priceAvgMin = ratesShort.Min(rate => GetTradeExitBy(false)(rate)) + PointSize / 100;
                if (buyCloseLevel.InManual) {
                  if (buyCloseLevel.Rate <= priceAvgMax)
                    buyCloseLevel.Rate = priceAvgMax;
                } else {
                  var signB = (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign();
                  buyCloseLevel.RateEx = Trades.HaveBuy()
                    ? (buyLevel.Min(_buyLevelNetOpen()) + tpColse).Max( Trades.HaveBuy() ? priceAvgMax : double.NaN)
                    : crossLevelDefault(true);
                  if (signB != (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign())
                    buyCloseLevel.ResetPricePosition();
                }
                if (sellCloseLevel.InManual) {
                  if (sellCloseLevel.Rate >= priceAvgMin)
                    sellCloseLevel.Rate = priceAvgMin;
                } else {
                  var sign = (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign();
                  sellCloseLevel.RateEx = Trades.HaveSell()
                    ? (sellLevel.Max(_sellLevelNetOpen()) - tpColse).Min(Trades.HaveSell() ? priceAvgMin : double.NaN)
                    : crossLevelDefault(false);
                  if (sign != (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign())
                    sellCloseLevel.ResetPricePosition();
                }
              }
            }
          }
        };
        Action adjustExitLevels0 = () => adjustExitLevels(double.NaN, double.NaN);
        Action adjustExitLevels1 = () => adjustExitLevels(_buyLevel.Rate, _sellLevel.Rate);
        #endregion

        #region adjustLevels
        var firstTime = true;
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
        var corridorMoved = new ValueTrigger<bool?>(false);
        var positionTrigger = new ValueTrigger<Unit>(false);
        double corridorLevel = double.NaN;
        double corridorAngle = 0;
        DateTime tradeCloseDate = DateTime.MaxValue;
        DateTime corridorDate = ServerTime;
        var corridorHeightPrev = DateTime.MinValue;
        DB.sGetStats_Result[] stats = null;
        Func<string, double> getCorridorMax = weekDay => stats.Single(s => s.DayName == weekDay + "").Range.Value;
        var interpreter = new Interpreter();
        Func<bool> isReverseStrategy = () => _buyLevel.Rate < _sellLevel.Rate;

        #endregion

        #region Funcs
        Func<Rate, double> cp = CorridorPrice();
        Func<double> calcVariance = null;
        Func<double> calcMedian = null;
        WaveInfo waveToTrade = null;
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
                  if (t.PL > -PriceSpreadAverage ) {
                    _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  }
                };
              }
              #endregion
              {
                var corridorRates = CorridorStats.Rates.Select(_priceAvg).ToArray();
                var point = InPoints(1) / 2;
                var crossesCount = CorridorHeightMax.ToInt();
                var edgesLevels = corridorRates.EdgeByRegression(point,2, CorridorStats.StDevByHeight / 2);
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
                Log = new Exception(new { WaveStDevRatio } + "");
                onCloseTradeLocal = t => { };
                onOpenTradeLocal = t => { };
              }
              #endregion
              {
                var ratesInner = RatesArray.ReverseIfNot();
                var levels = CorridorByStDev(ratesInner, StDevByHeight);
                double level = levels[0].Level, stDev = levels[0].StDev;
                var isCountOk = levels[0].Count < StDevLevelsCountsAverage;
                bool ok = isCountOk &&
                  (!Trades.Any()
                  || Trades.HaveBuy() && level < Trades.NetClose() || Trades.HaveSell() && level > Trades.NetClose());
                var corridorJumped = corridorLevel.IfNaN(0).Abs(level) > stDev;
                var offset = stDev;
                CenterOfMassBuy = level + offset;
                CenterOfMassSell = level - offset;

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
                  PolyOrder_FractalRatio = PolyOrder,
                  CorrelationMinimum_HeightRatio = CorrelationMinimum
                } + "");
                MagnetPrice = double.NaN;
                onCloseTradeLocal = t => {
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                };
              }
              #endregion
              {
                var corridorRates = CorridorStats.Rates;
                var priceHigh = _priceAvg;
                var priceLow = _priceAvg;
                alglib.complex[] bins;
                corridorRates.Select(_priceAvg).FftFrequency(true, out bins);
                var polyOrder = bins.Skip(1).Select(MathExtensions.ComplexValue).OrderByDescending(v => v).Take(3).Sum().ToInt();
                Fractals = corridorRates.Fractals(polyOrder < 6 ? corridorRates.Count / polyOrder : polyOrder, priceHigh, priceLow);
                var voltage = GetVoltage(RateLast);
                var fractalsOk = Fractals.Select(f => f.Count()).Sum() == 0;
                triggerFractal.Set(fractalsOk, CurrentPrice.Time);
                var buySellHeight = CenterOfMassBuy - CenterOfMassSell;
                {
                  var lengthOk = CorridorStats.Rates.Count > CorridorDistanceRatio * WaveStDevRatio;
                  var angleOk = TradingAngleRange >= 0 ? CorridorAngle.Abs() > TradingAngleRange : CorridorAngle.Abs() < TradingAngleRange.Abs();
                  var heightOk = true;// buySellHeight < CorridorStats.HeightByRegression / CorrelationMinimum;
                  var voltageOk = voltage >= GetVoltageHigh();
                  var canTrade = lengthOk && angleOk && heightOk && voltageOk && triggerFractal.On;
                  triggerRsd.Set(canTrade, () => {
                    triggerFractal.Off();
                    var offset = CorridorStats.StDevByHeight;
                    CenterOfMassBuy = RateLast.PriceAvg1 + offset;
                    CenterOfMassSell = RateLast.PriceAvg1 - offset;
                    MagnetPrice = Fractals.SelectMany(f => f).Select(_priceAvg).DefaultIfEmpty(MagnetPrice).Average();
                    _buyLevel.RateEx = CenterOfMassBuy;
                    _sellLevel.RateEx = CenterOfMassSell;
                    rsdStartDate = CurrentPrice.Time;
                    //_buySellLevelsForEach(sr => sr.ResetPricePosition());
                    if (IsAutoStrategy) _buySellLevelsForEach(sr => sr.CanTradeEx = TradingAngleRange <= 0 || sr.IsBuy && CorridorAngle < 0 || sr.IsSell && CorridorAngle > 0);
                  });
                  triggerRsd.Off(canTrade);
                  if (!triggerFractal.Value.IsMin() && triggerFractal.Value.AddMinutes(BarsCount) < CurrentPrice.Time)
                    triggerFractal.Off();
                }
                if (IsAutoStrategy
                  && this.TakeProfitLimitRatio == 1
                  && (CurrentPrice.Time - rsdStartDate).TotalMinutes * BarPeriodInt > CorridorStats.Rates.Count) {
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  CloseTrades("Trades start date");
                }
              }
              adjustExitLevels1();
              break;
            #endregion
            #region Rsd2
            case TrailingWaveMethod.Rsd2:
              #region firstTime
              if (firstTime) {
                LogTrades = !IsInVitualTrading;
                DoNews = !IsInVitualTrading;
                Log = new Exception(new {
                  CorridorCrossesMaximum,
                  DistanceIterations_RsdRatio = DistanceIterations,
                  PolyOrder_FractalRatio = PolyOrder,
                  CorrelationMinimum_HeightRatio = CorrelationMinimum
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
                var priceHigh = _priceAvg;
                var priceLow = _priceAvg;
                Fractals = corridorRates.Fractals(PolyOrder > FttMax ? corridorRates.Count / PolyOrder : FttMax, priceHigh, priceLow);
                var fractalsMinCount = Fractals[true].Count().Min(Fractals[false].Count());
                var heightOk = (_buyLevel.Rate - _sellLevel.Rate) * CorrelationMinimum < RatesHeight;
                var fractalOk = fractalsMinCount >= PolyOrder - 1;
                var tradeOk = fractalOk && heightOk;
                if (fractalsMinCount > 0) {
                  CenterOfMassBuy = Fractals[true].Select(r => r.PriceHigh).First();
                  CenterOfMassSell = Fractals[false].Select(r => r.PriceLow).First();
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy) _buySellLevelsForEach(sr => {
                    sr.CanTradeEx = true;
                    sr.TradesCount = 0;
                    CloseAtZero = false;
                  });
                }
              }
              adjustExitLevels1();
              break;
            #endregion
            #region Tunnel
            case TrailingWaveMethod.Backdoor:
              #region firstTime
              if (firstTime) {
                corridorMoved.Set(false);
                corridorDate = DateTime.MinValue;
                LogTrades = !IsInVitualTrading;
                DoNews = !IsInVitualTrading;
                Log = new Exception(new { WaveStDevRatio, TradingAngleRange } + "");
                MagnetPrice = double.NaN;
                onCloseTradeLocal = t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                };
              }
              #endregion
              {
                var rates = RatesArray.SafeArray();
                var voltMinIndex = rates.AsParallel().Select((r, i) => new { v = GetVoltage(r), i }).OrderBy(a => a.v).First().i;
                var voltMinDate = rates[voltMinIndex].StartDate;
                var voltageOk = GetVoltage(RateLast).IfNaN(GetVoltage(RatePrev)) < GetVoltageHigh();
                corridorMoved.Set(_harmonics != null && voltageOk, () => {
                  corridorDate = CurrentPrice.Time;
                  var avg = CurrentPrice.Average;
                  var stDev = InPoints(_harmonics[0].Height);
                  var buy = avg + stDev;
                  var sell = avg - stDev;
                  _buyLevel.RateEx = buy;// RateLast.PriceAvg3 > 0 ? RateLast.PriceAvg3 : RatePrev.PriceAvg3;
                  _sellLevel.RateEx = sell;// RateLast.PriceAvg2 > 0 ? RateLast.PriceAvg2 : RatePrev.PriceAvg2;
                });
                if (IsAutoStrategy) _buySellLevelsForEach(sr =>
                  sr.CanTradeEx = corridorDate > CorridorStats.StartDate);
              }
              adjustExitLevels1();
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

            case TrailingWaveMethod.Magnet:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
                Log = new Exception(new { CorridorHeightMax_crossesCount = CorridorHeightMax } + "");
                MagnetPrice = double.NaN;
                onCloseTradeLocal = t => {
                  if (t.PL > -PriceSpreadAverage ) {
                    //_buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  }
                };
              }
              #endregion
              {
                var height = StDevByHeight;
                var spread = PriceSpreadAverage.GetValueOrDefault(0);
                var corridorRates = CorridorStats.Rates.Select(GetPriceMA()).ToArray();
                var avg = corridorRates.Average();
                var corridorRatesAvg = corridorRates.Average();
                var point = InPoints(1) / 2;
                var crossesCount = CorridorHeightMax.ToInt();
                var takeSkip = CorridorDistanceRatio.ToInt();
                var levelUp = avg + CorridorStats.StDevByHeight / 2;
                var edgeUp = corridorRates.Where(r => r >= levelUp).ToArray().Edge(point, crossesCount);
                var levelDown = avg - CorridorStats.StDevByHeight / 2;
                var edgeDown = corridorRates.Where(r => r <= levelDown).ToArray().Edge(point, crossesCount);
                Func<IList<HedgeHog.Lib.EdgeInfo>, double> getLevel = edges => edges == null ? double.NaN
                  : edges.AverageByIterations(e => {
                    if (e == null) Debugger.Break();
                    return e.SumAvg; 
                  }, (v, a) => v <= a, 3).OrderByDescending(e => e.DistAvg)
                  .Select(e => e.Level).DefaultIfEmpty(double.NaN).First();
                var edgesLevels = new[] { getLevel(edgeUp), getLevel(edgeDown) };
                var ratesMax = edgesLevels.Max();
                var ratesMin = edgesLevels.Min();
                var upper = new[] { ratesMax + spread, ratesMax - height };
                var lower = new[] { ratesMin - spread, ratesMin + height };
                var isUp = CurrentPrice.Average >= avg;
                var lowper = isUp ? upper : lower;
                CenterOfMassBuy = ratesMax.IfNaN(CenterOfMassBuy);// lowper.Max();
                CenterOfMassSell = ratesMin.IfNaN(CenterOfMassSell);// lowper.Min();
                MagnetPrice = corridorRatesAvg;
                if ( !edgesLevels.Any(d => d.IsNaN())) {
                  _buyLevel.RateEx = CenterOfMassBuy;
                  _sellLevel.RateEx = CenterOfMassSell;
                  if (IsAutoStrategy) _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                }
                if (false && IsAutoStrategy && edgesLevels.All(e => e.IsNaN())) {
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  CloseAtZero = true;
                }
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
            default: var exc = new Exception(TrailingDistanceFunction + " is not supported."); Log = exc; throw exc;
          }
          if (firstTime) {
            firstTime = false;
            ForEachSuppRes(sr => sr.ResetPricePosition());
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
        _adjustEnterLevels += () => { if (runOnce != null && runOnce()) runOnce = null; };
        _adjustEnterLevels += () => turnOff(() => _buySellLevelsForEach(sr => { if (IsAutoStrategy) sr.CanTradeEx = false; }));
        _adjustEnterLevels += () => exitFunc()();
        _adjustEnterLevels += () => {
          try {
            if (IsTradingActive) {
              Func<bool, double> r = isBuy => CalculateLastPrice(RateLast, GetTradeEnterBy(isBuy));
              _buyLevel.SetPrice(r(true));
              _sellLevel.SetPrice(r(false));
              buyCloseLevel.SetPrice(r(false));
              sellCloseLevel.SetPrice(r(true));
            } else
              SuppRes.ForEach(sr => sr.ResetPricePosition());
          } catch (Exception exc) { Log = exc; }
        };
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

    IList<Rate> CorridorByVerticalLineCrosses(IList<Rate> rates, int lengthMin,out double levelOut) {
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
      var maxIntervals = new { length = 0, level = double.NaN, index = 0 }.IEnumerable().ToConcurrentQueue();
      var height = rates.Take(lengthMin).Height(a => price(a.rate));
      var levelMIn = rates.Take(lengthMin).Min(t => price(t.rate));
      var levels = Enumerable.Range(1, InPips(height).ToInt() - 1).Select(i => levelMIn + InPoints(i)).AsParallel();
      Parallel.ForEach(levels, level => {
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
            maxIntervals.Enqueue(new { length = span, level, ratesCrossed.Last().index });
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
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips - halfInPips)
        .Select(levelInner => levelMin + levelInner * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      StDevLevelsCountsAverage = levelsByCount.Select(a => (double)a.count).ToArray().AverageByIterations(2, true).Average().ToInt();
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
  }
}
