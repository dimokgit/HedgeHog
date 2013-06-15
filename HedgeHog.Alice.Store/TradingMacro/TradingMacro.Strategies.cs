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
      Func<bool, double> crossLevelDefault = isBuy => isBuy ? _RatesMax + RatesHeight  : _RatesMin - RatesHeight;
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
        Action<Action> turnOffByBuySellHeight = a => { if (_CenterOfMassBuy - _CenterOfMassSell < StDevByHeight.Max(StDevByPriceAvg))a(); };
        Action<Action> turnOff = a => {
          switch (TurnOffFunction) {
            case Store.TurnOffFunctions.Void: return;
            case Store.TurnOffFunctions.WaveHeight: turnOffByWaveHeight(a); return;
            case Store.TurnOffFunctions.WaveShortLeft: turnOffByWaveShortLeft(a); return;
            case Store.TurnOffFunctions.WaveShortAndLeft: turnOffByWaveShortAndLeft(a); return;
            case Store.TurnOffFunctions.BuySellHeight: turnOffByBuySellHeight(a); return;
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
        Func<bool> isProfitOk = () => Trades.HaveBuy() && CurrentPrice.Bid > buyCloseLevel.Rate ||
          Trades.HaveSell() && CurrentPrice.Ask < sellCloseLevel.Rate;
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
          OpenTrade(isBuy, lot, "enterCrossHandler:" + new { suppRes.IsBuy, suppRes.IsExitOnly });
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
          if (sr.CanTrade) updateTradeCount(sr, srNearest.Value);
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

        #region _adjustExitLevels
        Action<double, double> adjustExitLevels = null;
        adjustExitLevels = (buyLevel, sellLevel) => {
          var tradesCount = Trades.Length;
          if (buyLevel.Min(sellLevel) < .5)
            Debugger.Break();
          Func<bool, double> r = isBuy => CalculateLastPrice(RateLast, GetTradeExitBy(isBuy));
          if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
            if (tradesCount == 0) {
              buyCloseLevel.RateEx = crossLevelDefault(true);
              buyCloseLevel.ResetPricePosition();
              sellCloseLevel.RateEx = crossLevelDefault(false);
              sellCloseLevel.ResetPricePosition();
            } else {
              buyCloseLevel.SetPrice(r(false));
              sellCloseLevel.SetPrice(r(true));
              if (!Trades.Any()) {
                adjustExitLevels(buyLevel, sellLevel);
                buyCloseLevel.ResetPricePosition();
                sellCloseLevel.ResetPricePosition();
                return;
              } else {
                var close0 = CloseAtZero || _trimAtZero || _trimToLotSize || isProfitOk();
                var tpCloseInPips = (close0 ? 0 : TakeProfitPips + (CloseOnProfitOnly ? CurrentLossInPips.Min(0).Abs() : 0));
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
          } else {
            buyCloseLevel.SetPrice(r(false));
            sellCloseLevel.SetPrice(r(true));

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
        var waveTradeOverTrigger = new ValueTrigger(false);
        var watcherReverseStrategy = new ObservableValue<bool>(false);
        var triggerAngle = new ValueTrigger(false);
        var corridorMoved = new ValueTrigger(false);
        var densityTrigger = new ValueTrigger(false);
        double corridorLevel = double.NaN;
        double corridorAngle = 0;
        DateTime tradeCloseDate = DateTime.MaxValue;
        DateTime corridorDate = ServerTime;
        var corridorHeightPrev = DateTime.MinValue;
        DB.sGetStats_Result[] stats = null;
        Func<string, double> getCorridorMax = weekDay => stats.Single(s => s.DayName == weekDay + "").Range.Value;
        var interpreter = new Interpreter();

        #endregion

        #region Funcs
        Func<Rate, double> cp = CorridorPrice();
        Func<double> calcVariance = null;
        Func<double> calcMedian = null;
        Func<bool> isSteady = () => _CenterOfMassBuy < WaveTradeStart.RatesMax && _CenterOfMassSell > WaveTradeStart.RatesMin;
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
        #region setCenterOfMass
        Action<WaveInfo> setCenterOfMass =  w=> {
          var m = (w.RatesMax + w.RatesMin) / 2;
          var v = w.RatesHeight / 2;
          _CenterOfMassBuy = m + v;
          _CenterOfMassSell = m - v;

        };
        #endregion
        Func<bool> runOnce = null;

        #region getLevels
        Func<object> notImplemented = () => { throw new NotImplementedException(); };
        var levelsSample = new { levelUp = 0.0, levelDown = 0.0 };
        Func<double, double, object> newLevels = (levelUp, levelDown) => new { levelUp, levelDown };
        Func<object> getLevels = notImplemented;
        #endregion
        #endregion

        #region adjustEnterLevels
        Action adjustEnterLevels = () => {
          if (!WaveShort.HasRates) return;
          if (DoShowInactiveCorridor)
            SetCentersOfMassByHour(3, 120);
          switch (TrailingDistanceFunction) {
            #region Old
            #region CountWithAngle
            case TrailingWaveMethod.CountWithAngle:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
              }
              #endregion
              {
                if (CorridorAngleFromTangent().Abs().ToInt() <= TradingAngleRange) {
                  var ratesCorridor = WaveShort.Rates.Skip(this.PriceCmaLevels * 2);
                  _buyLevel.RateEx = ratesCorridor.Max(GetTradeEnterBy(true));
                  _sellLevel.RateEx = ratesCorridor.Min(GetTradeEnterBy(false)); ;
                  _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy);
                }
                adjustExitLevels0();
              }
              break;
            #endregion
            #region Count0
            case TrailingWaveMethod.Count0:
              #region firstTime
              if (firstTime) {
                watcherCanTrade.SetValue(false);
                VarianceFunction = VarainceFunctions.Zero;
                MedianFunction = MedianFunctions.Void;
              }
              #endregion
              {
                var sector = RatesHeight / WaveStDevRatio;
                _CenterOfMassBuy = _RatesMax - sector;
                _CenterOfMassSell = _RatesMin + sector;
                if (SuppRes.Any(sr => sr.InManual)) break;
                var coeffs = CorridorStats.Coeffs;
                var angleOk = coeffs[1].Angle(BarPeriodInt, PointSize).Abs().ToInt() <= this.TradingAngleRange;
                var countMin = (WaveShortLeft.HasRates ? WaveShortLeft.Rates.Count : CorridorDistanceRatio).Max(CorridorDistanceRatio);
                var crossesOk = WaveShort.Rates.Count > countMin;
                var high = WaveShort.RatesMax;
                var low = WaveShort.RatesMin;
                var level = (high + low) / 2;
                var levelOk = corridorLevel.Abs(level) > varianceFunc()();
                var sectorOk = !level.Between(_CenterOfMassSell, _CenterOfMassBuy);
                if (watcherCanTrade.SetValue(angleOk && crossesOk && levelOk && sectorOk).ChangedTo(true)) {
                  corridorLevel = level;
                  _buyLevel.RateEx = high;
                  _sellLevel.RateEx = low;
                }
                _buySellLevelsForEach(sr =>
                  sr.CanTradeEx = IsAutoStrategy
                  && WaveShort.HasRates
                  && WaveShort.Rates.Count < CorridorDistanceRatio
                  && _buyLevel.Rate > _sellLevel.Rate);
                adjustExitLevels0();
              }
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
              #region firstTime
              if (firstTime) {
                Log = new Exception(new {
                  CorrelationMinimum_timeFrame = CorrelationMinimum,
                  DistanceIterations_hour = DistanceIterations
                } + "");
              }
              #endregion
              {
                SetCentersOfMassByHour(3,120);
                adjustExitLevels0();
              }
              break;
            #endregion
            #endregion
            #region TradeHour
            case TrailingWaveMethod.TradeHour1:
              if (firstTime) {
                getLevels = () => newLevels(CorridorStats.RatesMax, CorridorStats.RatesMin);
              }
              goto case TrailingWaveMethod.TradeHour;
            case TrailingWaveMethod.TradeHour:
              if (firstTime) {
                if (getLevels == notImplemented)
                  getLevels = () => {
                    var rates = setTrendLines(2);
                    return newLevels(rates[0].PriceAvg2, rates[0].PriceAvg3);
                  };
                isTradingHourLocal = () => true;
                onCloseTradeLocal = trade => {
                    triggerAngle.Off();
                    if (!IsTradingTime()) _buySellLevelsForEach(sr => sr.CanTrade = false);
                    return;
                    if (CurrentGross < 0)
                      if (trade.IsBuy)
                        _buyLevel.RateEx = trade.Close - InPoints(TakeProfitPips);
                      else
                        _sellLevel.RateEx = trade.Close + InPoints(TakeProfitPips);
                };
              }{
                var hourStart = DistanceIterations.ToInt();
                if (hourStart >= 0) {
                  var hourEnd = (hourStart + CorrelationMinimum.ToInt()) % 24;
                  var tradingHours = string.Format("{0:00}:00-{1:00}:00", hourStart, hourEnd);
                  TradingHoursRange = tradingHours;
                }
                var gl = Lib.Cast(getLevels(), () => levelsSample);
                var canTrade = IsAutoStrategy && !_buySellLevels.Any(sr => sr.InManual);
                if (canTrade) {
                  var angle = CorridorStats.Slope.Angle(BarPeriodInt, PointSize).Abs();
                  var isAngleOk = angle <= TradingAngleRange;
                  triggerAngle.Set(isAngleOk && IsTradingTime(), () => {
                      _buyLevel.RateEx =  gl.levelUp;
                      _sellLevel.RateEx = gl.levelDown;
                      _buySellLevelsForEach(sr => {
                        sr.ResetPricePosition();
                        sr.CanTrade = true;
                        sr.TradesCount = CorridorCrossesMaximum.Abs();
                      });
                  });
                  if (!isAngleOk) triggerAngle.Off();
                }
              }
              if (StreatchTakeProfit) adjustExitLevels0(); else adjustExitLevels1();
              break;
            #endregion
            #region ByHour
            case TrailingWaveMethod.ByHour:
              #region firstTime
              if (firstTime) {
                LogTrades = false;
                Log = new Exception(new {
                  CorridorDistanceRatio,
                  DistanceIterations
                } + "");
              }
              #endregion
              {
                CalcNarrowRange();
                _buyLevel.RateEx = _CenterOfMassBuy;
                _sellLevel.RateEx = _CenterOfMassSell;
                _buySellLevelsForEach(sr => sr.CanTradeEx = _buyLevel.Rate - _sellLevel.Rate < CorridorStats.HeightByRegression);
                adjustExitLevels0();
              }
              break;
            #endregion

            default: var exc = new Exception(TrailingDistanceFunction + " is not supported."); Log = exc; throw exc;
          }
          if (!CloseOnProfitOnly && Trades.GrossInPips() > TakeProfitPips
            //|| currentGrossInPips() >= PriceSpreadAverage && WaveTradeStart.Rates.Take(5).ToArray().PriceHikes().Max() < SpreadForCorridor
            )
            CloseAtZero = true;
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

    private void SetCentersOfMassByHour(int hour,int timeFrame) {
      var levels = RatesInternal.ReverseIfNot().Skip(CorridorDistanceRatio.ToInt())
        .SkipWhile(r => r.StartDate.ToUniversalTime().Hour != hour).Take(timeFrame).OrderBy(_priceAvg).ToArray();
      _CenterOfMassBuy = levels.LastBC().PriceAvg;
      _CenterOfMassSell = levels[0].PriceAvg;
    }

    private void CalcNarrowRange() {
      var reversed = RatesInternal.ReverseIfNot();
      var rangeLevels = new Rate[2][];
      var corridorLength = CorridorDistanceRatio.ToInt();
      {
        var ratesPast = reversed.Take(corridorLength).ToArray();
        var rangeLength = DistanceIterations.ToInt();
        var ranges = Enumerable.Range(0, ratesPast.Length - rangeLength).Select(i => {
          var range = new Rate[rangeLength];
          Array.Copy(ratesPast, i, range, 0, rangeLength);
          return range;
        });
        var rangeSmall = ranges.OrderBy(range => range.Height()).First();
        rangeLevels[0] = rangeSmall;
      }
      {
        var ratesPast = reversed
          .Skip(corridorLength*2)
          .Take(corridorLength).ToArray();
        var rangeLength = DistanceIterations.ToInt();
        var ranges = Enumerable.Range(0, ratesPast.Length - rangeLength).Select(i => {
          var range = new Rate[rangeLength];
          Array.Copy(ratesPast, i, range, 0, rangeLength);
          return range;
        });
        var rangeSmall = ranges.OrderBy(range => range.Height()).First();
        rangeLevels[1] = rangeSmall;
      }
      rangeLevels = rangeLevels.OrderBy(r => r.Average(_priceAvg)).ToArray();
      _CenterOfMassBuy = rangeLevels[1].Max(_priceAvg);
      _CenterOfMassSell = rangeLevels[0].Min(_priceAvg);
    }

    private void StrategyEnterRegression() {
    }

    double _VoltageAverage = double.NaN;
    public double VoltageAverage { get { return _VoltageAverage; } set { _VoltageAverage = value; } }

    double _VoltageHight = double.NaN;
    private double _buyLevelRate = double.NaN;
    private double _sellLevelRate = double.NaN;
    public double VoltageHight { get { return _VoltageHight; } set { _VoltageHight = value; } }
  }
}
