using DynamicExpresso;
using HedgeHog.Bars;
using HedgeHog.Models;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks.Dataflow;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive.Disposables;
using HedgeHog.Shared.Messages;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    bool IsTradingHour() { return IsTradingHour(ServerTime); }
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
      if (!RatesArray.Any() || !CorridorStats.Rates.Any()) return;
      Action<int> a = u => {
        try {
          //Debug.WriteLine("broadcastCorridorDatesChange.Proc:{0:n0},Start:{1},Stop:{2}", u, CorridorStartDate, CorridorStopDate);
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
    void SetCorridorStopDate(Rate rate) {
      CorridorStats.StopRate = rate;
      _CorridorStopDate = CorridorStats.StopRate == null || rate == null ? DateTime.MinValue : CorridorStats.StopRate.StartDate;
    }
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
            var index = RatesArray.IndexOf(new Rate() { StartDate2 = new DateTimeOffset(value) });
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
    void ForEachSuppRes(params Action<SuppRes>[] actions) {
      (from sr in SuppRes
       from action in actions
       select new { sr, action }
      ).ForEach(a => a.action(a.sr));
    }
    double _buyLevelNetOpen() { return Trades.IsBuy(true).NetOpen(_buyLevel.Rate); }
    double _sellLevelNetOpen() { return Trades.IsBuy(false).NetOpen(_sellLevel.Rate); }
    Action _adjustEnterLevels = () => { throw new NotImplementedException(); };
    Action<double?, double?> _adjustExitLevels = (buyLevel, selLevel) => { throw new NotImplementedException(); };
    Action _exitTrade = () => { throw new NotImplementedException(); };

    Func<Rate, double> _priceAvg = rate => rate.PriceAvg;
    void SetCorridorStartDateAsync(DateTime? date) {
      Scheduler.CurrentThread.Schedule(TimeSpan.FromMilliseconds(1), () => CorridorStartDate = date);
    }
    double CurrentEnterPrice(bool? isBuy) { return CalculateLastPrice(GetTradeEnterBy(isBuy)); }
    double CurrentExitPrice(bool? isBuy) { return CalculateLastPrice(GetTradeExitBy(isBuy)); }

    private void StrategyEnterUniversal()
    {
      if (!RatesArray.Any()) return;

      #region ============ Local globals ============
      #region Loss/Gross
      Func<double> currentGrossInPips = () => CurrentGrossInPipTotal;
      Func<double> currentLoss = () => CurrentLoss;
      Func<double> currentGross = () => CurrentGross;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      #endregion
      _isSelfStrategy = true;
      var reverseStrategy = new ObservableValue<bool>(false);
      var _takeProfitLimitRatioLocal = double.NaN;
      Func<double> takeProfitLimitRatio = () => _takeProfitLimitRatioLocal.IfNaN(TakeProfitLimitRatio);
      Func<SuppRes, bool> isBuyR = sr => reverseStrategy.Value ? !sr.IsBuy : sr.IsBuy;
      Func<SuppRes, bool> isSellR = sr => reverseStrategy.Value ? !sr.IsSell : sr.IsSell;
      Func<bool> calcAngleOk = () => TradingAngleRange >= 0
        ? CorridorAngleFromTangent().Abs() >= TradingAngleRange : CorridorAngleFromTangent().Abs() < TradingAngleRange.Abs();
      Action resetCloseAndTrim = () => CloseAtZero = _trimAtZero = _trimToLotSize = false;
      Func<bool, double> enter = isBuy => CalculateLastPrice(RateLast, GetTradeEnterBy(isBuy));
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
          return new[] { h / 2, l / 2 };
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
            case CorridorHeightMethods.ByMA: return getStDevByAverage(CorridorPrice, .1);
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
        Action onEOW = () => { };
        #region Exit Funcs
        #region exitOnFriday
        Func<bool> exitOnFriday = () => {
          if (!SuppRes.Any(sr => sr.InManual)) {
            bool isEOW = IsAutoStrategy && IsEndOfWeek();
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots(), "exitOnFriday");
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
          if (CurrentGrossInPips >= _harmonics[0].Height)
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
        #region exitByJumpOut
        Action exitByJumpOut = () => {
          if (CorridorAngleFromTangent().Abs() < 1 && (ServerTime - Trades.Last().Time).TotalHours < 2) return;
          var revs = RatesArray.Reverse<Rate>().Take(5).Select(_priceAvg);
          if (Trades.HaveBuy() && revs.Max() > RateLast.PriceAvg2
          || Trades.HaveSell() && revs.Min() < RateLast.PriceAvg3)
            CloseTrades("exitByJumpOut");
        };
        #endregion
        #region exitFunc
        Func<Action> exitFunc = () => {
          if (!Trades.Any()) return () => { };
          switch (ExitFunction) {
            case Store.ExitFunctions.Void: return exitVoid;
            case Store.ExitFunctions.Friday: return () => exitOnFriday();
            case Store.ExitFunctions.GrossTP: return exitByTakeProfit;
            case Store.ExitFunctions.JumpOut: return exitByJumpOut;
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
        Action<double, Action> turnOffIfCorridorInMiddle_ = (sections, a) => {
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
        BuyCloseLevel = BuyCloseSupResLevel();
        SellCloseLevel = SellCloseSupResLevel();
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
        Func<SuppRes, SuppRes> suppResNearest = supRes => _suppResesForBulk().Where(sr => sr.IsSupport != supRes.IsSupport).OrderBy(sr => (sr.Rate - supRes.Rate).Abs()).First();
        Action<bool> setCloseLevels = (overWrite) => setCloseLevel(BuyCloseLevel, overWrite);
        setCloseLevels += (overWrite) => setCloseLevel(SellCloseLevel, overWrite);
        ForEachSuppRes(sr => {
          if (IsInVitualTrading) sr.InManual = false;
          sr.ResetPricePosition();
          sr.ClearCrossedHandlers();
        });
        setCloseLevels(true);
        #region updateTradeCount
        Action<SuppRes, SuppRes> updateTradeCount = (supRes, other) => {
          if (supRes.TradesCount <= other.TradesCount) other.TradesCount = supRes.TradesCount - 1;
        };
        Func<SuppRes, SuppRes> updateNeares = supRes => {
          var other = suppResNearest(supRes);
          updateTradeCount(supRes, other);
          return other;
        };
        #endregion
        Func<bool, bool> onCanTradeLocal = canTrade => canTrade;
        Func<SuppRes, bool> canTradeLocal = sr => {
          var ratio = CanTradeLocalRatio;
          var corr = BuyLevel.Rate.Abs(SellLevel.Rate);
          var tradeDist = (sr.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (SellLevel.Rate - CurrentPrice.Bid));
          var tradeRange = ((sr.IsBuy ? (CurrentPrice.Ask - SellLevel.Rate) : (BuyLevel.Rate - CurrentPrice.Bid))) / corr;
          var canTrade = new { canTrade = tradeRange < ratio || tradeDist <= PriceSpreadAverage, tradeRange, tradeDist, ratio };
          //if (!canTrade) 
          //Log = new Exception(canTrade + "");
          return onCanTradeLocal(canTrade.canTrade);
        };
        Func<bool> isTradingHourLocal = () => IsTradingHour() && IsTradingDay();
        Func<SuppRes, bool> suppResCanTrade = (sr) =>
          isTradingHourLocal() &&
          sr.CanTrade &&
          sr.TradesCount <= 0 &&
          !HasTradesByDistance(isBuyR(sr)) &&
          canTradeLocal(sr);
        Func<bool> isProfitOk = () => Trades.HaveBuy() && RateLast.BidHigh > BuyCloseLevel.Rate ||
          Trades.HaveSell() && RateLast.AskLow < SellCloseLevel.Rate;
        #endregion

        #region SuppRes Event Handlers
        #region enterCrossHandler
        Func<SuppRes, bool> enterCrossHandler = (suppRes) => {
          if (!IsTradingActive || (reverseStrategy.Value && !suppRes.CanTrade) || CanDoEntryOrders) return false;
          var isBuy = isBuyR(suppRes);
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
          if (TradingStatistics.TradingMacros.Count > 1 && (
            CurrentGrossInPipTotal > PriceSpreadAverageInPips || CurrentGrossInPipTotal >= _tradingStatistics.GrossToExitInPips)
            )
            CloseTrading(new { exitCrossHandler = "", CurrentGrossInPipTotal, _tradingStatistics.GrossToExitInPips } + "");
          else
            CloseTrades(lot, "exitCrossHandler:" + new { sr.IsBuy, sr.IsExitOnly, CloseAtZero });
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
          //if (reverseStrategy.Value && Trades.Any(t => t.IsBuy != sr.IsSell)) {
          //  exitCrossHandler(sr);
          //  return;
          //}
          if (sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1) return;
          if (sr.CanTrade) {
            if (sr.InManual) handleActiveExitLevel(sr);
            else crossedEnter(s, e);
          } else if (Trades.Any(t => t.IsBuy == sr.IsSell))
            exitCrossHandler(sr);
        };
        BuyCloseLevel.Crossed += crossedExit;
        SellCloseLevel.Crossed += crossedExit;
        #endregion
        #endregion

        #region adjustExitLevels
        Action<double, double> adjustExitLevels = AdjustCloseLevels();
        Action adjustExitLevels0 = () => adjustExitLevels(double.NaN, double.NaN);
        Action adjustExitLevels1 = () => {
          if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels);
          else adjustExitLevels(_buyLevel.Rate, _sellLevel.Rate);
        };
        #endregion

        #region adjustLevels
        var firstTime = true;
        #region Watchers
        var watcherCanTrade = new ObservableValue<bool>(true, true);
        var watcherCanTrade2 = new ObservableValue<bool>(true, true);
        var triggerRsd = new ValueTrigger<Unit>(false);
        var rsdStartDate = DateTime.MinValue;
        double corridorLevel = double.NaN;
        DateTime corridorDate = ServerTime;
        var corridorHeightPrev = DateTime.MinValue;
        var interpreter = new Interpreter();
        bool? isPriceUp = null;
        Func<bool> isReverseStrategy = () => _buyLevel.Rate < _sellLevel.Rate;
        IList<MathExtensions.Extream<Rate>> extreamsSaved = null;

        var intTrigger = new ValueTrigger<int>(false);
        var doubleTrigger = new ValueTrigger<double>(false);
        var corridorMovedTrigger = new ValueTrigger<ValueTrigger<bool>>(false) { Value = new ValueTrigger<bool>(false) };
        var dateTrigger = new ValueTrigger<DateTime>(false);
        object any = null;
        object[] bag = null;
        var canTradeSubject = new { canTrade = false, ifCan = new Action(() => { }) }.SubjectFuctory();
        #region Workflow tuple factories
        Func<List<object>> emptyWFContext = () => new List<object>();
        Func<List<object>, Tuple<int, List<object>>> tupleNext = e => Tuple.Create(1, e ?? new List<object>());
        Func<object, Tuple<int, List<object>>> tupleNextSingle = e => tupleNext(new List<object> { e });
        Func<Tuple<int, List<object>>> tupleNextEmpty = () => tupleNext(emptyWFContext());
        Func<List<object>, Tuple<int, List<object>>> tupleStay = e => Tuple.Create(0, e ?? new List<object>());
        Func<object, Tuple<int, List<object>>> tupleStaySingle = e => tupleStay(new[] { e }.ToList());
        Func<Tuple<int, List<object>>> tupleStayEmpty = () => tupleStay(emptyWFContext());
        Func<List<object>, Tuple<int, List<object>>> tuplePrev = e => Tuple.Create(-1, e ?? new List<object>());
        Func<List<object>, Tuple<int, List<object>>> tupleCancel = e => Tuple.Create(int.MaxValue / 2, e ?? new List<object>());
        Func<Tuple<int, List<object>>> tupleCancelEmpty = () => tupleCancel(emptyWFContext());
        #endregion
        Func<bool> cancelWorkflow = () => CloseAtZero;
        var workflowSubject = new Subject<IList<Func<List<object>, Tuple<int, List<object>>>>>();
        var workFlowObservable = workflowSubject
          .Scan(new { i = 0, o = emptyWFContext(), c = cancelWorkflow }, (i, wf) => {
            if (i.i >= wf.Count || i.c() || i.o.OfType<WF.MustExit>().Any(me=>me())) {
              i.o.OfType<WF.OnExit>().ForEach(a => a());
              i.o.Clear();
              i = new { i = 0, o = i.o, i.c };
            }
            var o = wf[i.i](i.o);// Side effect
            try {
              return new { i = (i.i + o.Item1).Max(0), o = o.Item2, i.c };
            } finally {
              if (o.Item1 != 0) workflowSubject.Repeat(1);
            }
          });

        #endregion

        
        #region Funcs
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
            case VarainceFunctions.Min2: return () => CorridorStats.StDevByPriceAvg.Min(CorridorStats.StDevByHeight) * 1.8;
            case VarainceFunctions.Sum: return () => CorridorStats.StDevByPriceAvg + CorridorStats.StDevByHeight;
          }
          throw new NotSupportedException(VarianceFunction + " Variance function is not supported.");
        };
        #endregion
        Func<bool> runOnce = null;

        #region SetTrendlines
        Func<double, double, Func<Rate[]>> setTrendLinesByParams = (h, l) => {
          if (CorridorStats == null || !CorridorStats.Rates.Any()) return () => new[] { new Rate(), new Rate() };
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
        #region initTradeRangeShift
        Action initTradeRangeShift = () => {
          Func<Trade, IEnumerable<Tuple<SuppRes, double>>> ootl_ = (trade) =>
            (from offset in (trade.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (CurrentPrice.Bid - SellLevel.Rate)).Yield()
             from sr in new[] { BuyLevel, SellLevel }
             select Tuple.Create(sr, offset));
          Action<Trade> ootl = null;
          ootl = trade => {
            ootl_(trade).ForEach(tpl => tpl.Item1.Rate += tpl.Item2);
            onOpenTradeLocal -= ootl;
          };
          onCanTradeLocal = canTrade => {
            if (!canTrade) {
              if (!onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                onOpenTradeLocal += ootl;
              }
            } else if (onOpenTradeLocal != null && onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
              onOpenTradeLocal -= ootl;
            }
            return true;
          };
        };
        #endregion
        #endregion
        

        #region adjustEnterLevels
        Action adjustEnterLevels = () => {
          if (!WaveShort.HasRates) return;
          switch (TrailingDistanceFunction) {
            #region DistAvgMin
            case TrailingWaveMethod.DistAvgMin:
              #region firstTime
              if (firstTime) {
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                var corridorRates = WaveShort.Rates;
                var buyLevel = CenterOfMassBuy;
                var sellLevel = CenterOfMassSell;
                if (!Trades.Any() || CurrentPrice.Average.Between(sellLevel, buyLevel)) {
                  var rates = RatesArray.Select(_priceAvg).Where(r => r.Between(CenterOfMassSell, CenterOfMassBuy)).ToArray();
                  var median = double.NaN;
                  var offset = rates.StDev(out median) * 2;
                  if (median > 0) {
                    BuyLevel.RateEx = median + offset;
                    SellLevel.RateEx = median - offset;
                  }
                }

                try {
                  var goTrade = GetVoltage(corridorRates.Last()) < GetVoltageHigh().Avg(GetVoltageAverage());
                  corridorMovedTrigger.Set(goTrade
                    , (vt) => {
                      CloseAtZero = false;
                      _buySellLevelsForEach(sr => { sr.CanTradeEx = goTrade && IsAutoStrategy; sr.ResetPricePosition(); });
                      corridorMovedTrigger.Value.Off();
                    }, corridorMovedTrigger.Value);
                  corridorMovedTrigger.Value.Set(!goTrade || !isTradingHourLocal(), (vt) => {
                    corridorMovedTrigger.Off();
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; CloseAtZero = Trades.Any(); });
                  });
                } catch (Exception exc) {
                  Log = exc;
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region DistAvgMax
            case TrailingWaveMethod.DistAvgMax:
              #region firstTime
              if (firstTime) {
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                _distanceHeightRatioExtreamPredicate = (e, i) => i == 0 && e.Slope < 0;
                Action setStopDate = () => {
                  CorridorStats.StopRate = RateLast;
                  _CorridorStopDate = CorridorStats.StopRate.StartDate;
                };
                var angleOk = calcAngleOk();
                var startDate = CorridorStats.StartDate;
                if (angleOk) {
                  if (CorridorStopDate.IsMin()) setStopDate();
                  else {
                    var cs = RatesArray.Reverse<Rate>().TakeWhile(r => r.StartDate >= startDate).ToArray()
                      .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
                    if (cs.StDevMin < CorridorStats.StDevMin && cs.Slope.Abs() <= CorridorStats.Slope.Abs())
                      setStopDate();
                  }
                }
                BuyLevel.RateEx = RateLast.PriceAvg2;
                SellLevel.RateEx = RateLast.PriceAvg3;
                dateTrigger.Set(angleOk && dateTrigger.Value < startDate, (vt) => {
                  if (IsAutoStrategy)
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; });
                  vt.Off();
                }, startDate);
                if (!angleOk && IsAutoStrategy)
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; dateTrigger.Value = default(DateTime); });

              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region DistAvgMinMax
            case TrailingWaveMethod.DistAvgMinMax:
              #region firstTime
              if (firstTime) {
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                {
                  Action setStopDate = () => {
                    CorridorStats.StopRate = RateLast;
                    _CorridorStopDate = CorridorStats.StopRate.StartDate;
                  };
                  var angleOk = calcAngleOk();
                  var startDate = CorridorStats.StartDate;
                  if (angleOk) {
                    if (CorridorStopDate.IsMin()) setStopDate();
                    else {
                      var cs = RatesArray.Reverse<Rate>().TakeWhile(r => r.StartDate >= startDate).ToArray()
                        .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
                      if (cs.StDevMin < CorridorStats.StDevMin && cs.Slope.Abs() <= CorridorStats.Slope.Abs())
                        setStopDate();
                    }
                  }
                }
                var isCorridorOld = CorridorStopDate.IfMax(DateTime.MaxValue) < RatesArray.LastBC(CorridorDistance).StartDate;
                var corridorRates = WaveShort.Rates;
                var buyLevel = CenterOfMassBuy;
                var sellLevel = CenterOfMassSell;
                if (!Trades.Any() || CurrentPrice.Average.Between(sellLevel, buyLevel)) {
                  var rates = RatesArray.Select(_priceAvg).Where(r => r.Between(CenterOfMassSell, CenterOfMassBuy)).ToArray();
                  var median = double.NaN;
                  var offset = rates.StDev(out median) * 2;
                  if (median > 0) {
                    BuyLevel.RateEx = median + offset;
                    SellLevel.RateEx = median - offset;
                  }
                }

                try {
                  var goTrade = GetVoltage(corridorRates.Last()) < GetVoltageHigh().Avg(GetVoltageAverage());
                  corridorMovedTrigger.Set(goTrade
                    , (vt) => {
                      CloseAtZero = false;
                      _buySellLevelsForEach(sr => { sr.CanTradeEx = goTrade && IsAutoStrategy; sr.ResetPricePosition(); });
                      corridorMovedTrigger.Value.Off();
                    }, corridorMovedTrigger.Value);
                  corridorMovedTrigger.Value.Set(!goTrade || !isTradingHourLocal() || isCorridorOld, (vt) => {
                    corridorMovedTrigger.Off();
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; CloseAtZero = Trades.Any(); });
                  });
                } catch (Exception exc) {
                  Log = exc;
                }
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region DistAvg
            case TrailingWaveMethod.DistAvg:
              #region firstTime
              if (firstTime) {
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                _distanceHeightRatioExtreamPredicate = (e, i) => i == 0 && e.Slope < 0;
                Action setStopDate = () => {
                  CorridorStats.StopRate = RateLast;
                  _CorridorStopDate = CorridorStats.StopRate.StartDate;
                };
                var angleOk = calcAngleOk();
                var startDate = CorridorStats.StartDate;
                if (angleOk) {
                  if (CorridorStopDate.IsMin()) setStopDate();
                  else {
                    var cs = RatesArray.Reverse<Rate>().TakeWhile(r => r.StartDate >= startDate).ToArray()
                      .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
                    if (cs.StDevMin < CorridorStats.StDevMin && cs.Slope.Abs() <= CorridorStats.Slope.Abs())
                      setStopDate();
                  }
                }
                BuyLevel.RateEx = RateLast.PriceAvg2;
                SellLevel.RateEx = RateLast.PriceAvg3;
                dateTrigger.Set(angleOk, (vt) => _buySellLevelsForEach(sr => sr.CanTradeEx = true));
                dateTrigger.Off(angleOk);
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region DistAvgLT
            case TrailingWaveMethod.DistAvgLT:
              #region firstTime
              if (firstTime) {
                watcherCanTrade.Value = false;
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                Func<double[], double[]> getLengthByAngle = ratesRev => {
                  var start = 60;
                  while (start < ratesRev.Length) {
                    if (AngleFromTangent(ratesRev.CopyToArray(start++).Regress(1).LineSlope()).Abs().ToInt() == 0)
                      break;
                  }
                  ratesRev = ratesRev.CopyToArray(start - 1);
                  return new[] { ratesRev.Max(), ratesRev.Min() };
                };
                GeneralPurposeSubject.OnNext(() => {
                  try {
                    var tail = CorridorDistance.Div(10).ToInt();
                    var range = extreamsSaved == null ? RatesInternal.Count
                      : (RatesArray.Count - RatesArray.IndexOf(extreamsSaved[0].Element) + tail).Min(CorridorDistance + tail);
                    var ratesReversed = UseRatesInternal(ri =>
                      ri.SafeArray().CopyToArray(ri.Count - range, range).Reverse().ToArray());
                    var extreamsAll = ratesReversed.Extreams(GetVoltage, CorridorDistance);
                    var extreams = extreamsAll.Where(e => e.Slope < 0).Take(2).ToArray();
                    if (range > RatesArray.Count && extreams.Length < 2)
                      throw new Exception("Range {0} is to short for two valleys.".Formater(RatesArray.Count));
                    Func<MathExtensions.Extream<Rate>, double> getLevel = e => ratesReversed.Skip(e.Index).Take(CorridorDistance).Average(_priceAvg);
                    var levels = extreams.Select(e => getLevel(e)).OrderBy(d => d).ToArray();
                    if (levels.Length == 1)
                      levels = new[] { levels[0], levels[0] > CenterOfMassBuy ? CenterOfMassBuy : CenterOfMassSell }
                        .OrderBy(d => d).ToArray();
                    if (levels.Length == 2) {
                      CenterOfMassBuy = levels[1];
                      CenterOfMassSell = levels[0];
                      extreamsSaved = extreamsAll;
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    }
                  } catch (Exception exc) {
                    Log = exc;
                  }
                });
                var angleOk = calcAngleOk();
                var buy = RateLast.PriceAvg2;
                var sell = RateLast.PriceAvg3;
                var inRange = buy.Avg(sell).Between(CenterOfMassSell, CenterOfMassBuy);
                var canTrade = angleOk && inRange;
                watcherCanTrade.SetValue(canTrade, true, () => {
                  BuyLevel.RateEx = buy;
                  SellLevel.RateEx = sell;
                  _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                });
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region DistAvgLT2
            case TrailingWaveMethod.DistAvgLT2:
              #region firstTime
              if (firstTime) {
                any = new Queue<double[]>();
                watcherCanTrade.Value = false;
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                var ranges = (Queue<double[]>)any;
                Func<double[], double[]> getLengthByAngle = ratesRev => {
                  var start = 60;
                  while (start < ratesRev.Length) {
                    if (AngleFromTangent(ratesRev.CopyToArray(start++).Regress(1).LineSlope()).Abs().ToInt() == 0)
                      break;
                  }
                  ratesRev = ratesRev.CopyToArray(start - 1);
                  return new[] { ratesRev.Max(), ratesRev.Min() };
                };
                GeneralPurposeSubject.OnNext(() => {
                  try {
                    var tail = CorridorDistance.Div(10).ToInt();
                    var range = extreamsSaved == null ? RatesInternal.Count
                      : (RatesArray.Count - RatesArray.IndexOf(extreamsSaved[0].Element) + tail).Min(CorridorDistance + tail);
                    var ratesReversed = UseRatesInternal(ri =>
                      ri.SafeArray().CopyToArray(ri.Count - range, range).Reverse().ToArray());
                    var extreamsAll = ratesReversed.Extreams(GetVoltage, CorridorDistance);
                    var extreams = extreamsAll.Where(e => e.Slope < 0).Take(2).ToArray();
                    if (range > RatesArray.Count && extreams.Length < 2)
                      throw new Exception("Range {0} is to short for two valleys.".Formater(RatesArray.Count));
                    Func<IList<double>, double[]> minMax = rates => new[] { rates.Min(), rates.Max() };
                    Func<MathExtensions.Extream<Rate>, double[]> getMinMax = e =>
                      minMax(ratesReversed.SkipWhile(r => r > e.Element0).Take(CorridorDistance / 2).ToArray(_priceAvg));
                    var levels = extreams.Select(e => getMinMax(e)).ToList();
                    while (ranges.Count + levels.Count > 2) ranges.Dequeue();
                    levels.ToArray(l => {
                      ranges.Enqueue(l);
                      return ranges.First();
                    }).Take(1).ForEach(lh => {
                      CenterOfMassBuy = lh[1];
                      CenterOfMassSell = lh[0];
                      extreamsSaved = extreamsAll;
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    });
                  } catch (Exception exc) {
                    Log = exc;
                  }
                });
                var angleOk = calcAngleOk();
                var inRange = CalculateLastPrice(GetTradeEnterBy(null)).Between(CenterOfMassSell, CenterOfMassBuy);
                var canTrade = angleOk && inRange;
                watcherCanTrade.SetValue(canTrade, true, () => {
                  BuyLevel.RateEx = CenterOfMassBuy;
                  SellLevel.RateEx = CenterOfMassSell;
                  _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                });
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region DistAvgLT3
            case TrailingWaveMethod.DistAvgLT3:
              #region firstTime
              if (firstTime) {
                bag = new object[] { new Queue<double[]>(), new Queue<MathExtensions.Extream<Rate>>(), false };
                watcherCanTrade.Value = false;
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
              }
              #endregion
              {
                var ranges = (Queue<double[]>)bag[0];
                var extreamsQueue = (Queue<MathExtensions.Extream<Rate>>)bag[1];
                var isBusy = (bool)bag[2];
                Func<double[], double[]> getLengthByAngle = ratesRev => {
                  var start = 60;
                  while (start < ratesRev.Length) {
                    if (AngleFromTangent(ratesRev.CopyToArray(start++).Regress(1).LineSlope()).Abs().ToInt() == 0)
                      break;
                  }
                  ratesRev = ratesRev.CopyToArray(start - 1);
                  return new[] { ratesRev.Max(), ratesRev.Min() };
                };
                GeneralPurposeSubject.OnNext(() => {
                  Action debug = () => { if (Debugger.IsAttached) Debugger.Break(); else Debugger.Launch(); };
                  try {
                    if (isBusy) throw new Exception("I am busy.");
                    isBusy = true;
                    var tail = CorridorDistance.Div(10).ToInt();
                    var range = !extreamsQueue.Any() ? RatesInternal.Count
                      : (RatesArray.Count - RatesArray.IndexOf(extreamsQueue.Last().Element) + tail).Min(CorridorDistance + tail);
                    var ratesReversed = UseRatesInternal(ri =>
                      ri.SafeArray().CopyToArray(ri.Count - range, range).Reverse().ToArray());
                    {
                      Func<IList<double>, double[]> minMax = rates => new[] { rates.Min(), rates.Max() };
                      var extreamsAll = ratesReversed.Extreams(GetVoltage, CorridorDistance).Reverse().ToArray();
                      extreamsQueue.IfEmpty(
                        (eq) => extreamsAll.ForEach(eq.Enqueue),
                        (eq) => extreamsAll
                          .SkipWhile(e => eq.Last().Slope.Sign() == e.Slope.Sign())
                          .ForEach(eq.Enqueue)
                      )
                      .ToMaybe2(() => (MathExtensions.Extream<Rate>)null)
                      .Do2((eq) => eq.Original.Skip(10).ToList().ForEach(eq1 => eq.Original.Dequeue()))
                      .Reverse().SkipWhile(e => e.Slope > 0).Take(4).Reverse().ToArray()
                      .AsMaybe().Do(extreams => {
                        if (range > RatesArray.Count && extreams.SafeArray().Length < 2)
                          throw new Exception("Range {0} is to short for two valleys.".Formater(BarsCountCalc));
                      }, exc => { })
                      .Do(extreams =>
                        UseRatesInternal(ri =>
                          extreams.Where(e => e.Slope > 0).Zip(extreams.Where(e => e.Slope < 0), (eu, ed) => new { eu, ed })
                            .Select(eud =>
                              minMax(ri
                              .SkipWhile(r => r <= eud.eu.Element0)
                              .TakeWhile(r => r < eud.ed.Element0)
                              .ToArray(_priceAvg)))
                            .ToList()
                        ).AsMaybe()
                      ).Do(levels => {
                        while (ranges.Count + levels.SafeList().Count > 2) ranges.Dequeue();
                        levels.ToArray(l => {
                          ranges.Enqueue(l);
                          return ranges.First();
                        }).Take(1).ForEach(lh => {
                          CenterOfMassBuy = lh[1];
                          CenterOfMassSell = lh[0];
                          _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        });
                      }, null);
                      //extreamsQueue.Zip(extreamsQueue.Skip(1), (ep, en) => new { ep, en }).Where(epn => epn.ep.Slope.Sign() == epn.en.Slope.Sign()).ForEach(epn => debug());
                    }
                  } catch (Exception exc) {
                    Log = exc;
                  } finally {
                    isBusy = false;
                  }
                });
                var angleOk = calcAngleOk();
                var inRange = CalculateLastPrice(GetTradeEnterBy(null)).Between(CenterOfMassSell, CenterOfMassBuy);
                var canTrade = angleOk && inRange;
                watcherCanTrade.SetValue(canTrade, true, () => {
                  BuyLevel.RateEx = CenterOfMassBuy;
                  SellLevel.RateEx = CenterOfMassSell;
                  _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                });
              }
              if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
              break;
            #endregion
            #region DistAvgLT31
            case TrailingWaveMethod.DistAvgLT31:
              #region firstTime
              if (firstTime) {
                bag = new object[] { new Queue<double[]>(), new Queue<MathExtensions.Extream<Rate>>(), false, new List<double[]>() };
                watcherCanTrade.Value = false;
                onCloseTradeLocal = t => {
                  if (t.PL >= TakeProfitPips / 2) {
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                    if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                  }
                };
                {
                  Func<Trade, IEnumerable<Tuple<SuppRes, double>>> ootl_ = (trade) =>
                    (from offset in (trade.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (CurrentPrice.Bid - SellLevel.Rate)).Yield()
                     from sr in new[] { BuyLevel, SellLevel }
                     select Tuple.Create(sr, offset));
                  Action<Trade> ootl = null;
                  ootl = trade => {
                    ootl_(trade).ForEach(tpl => tpl.Item1.Rate += tpl.Item2);
                    onOpenTradeLocal -= ootl;
                  };
                  onCanTradeLocal = canTrade => {
                    if (!canTrade) {
                      if (!onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                        onOpenTradeLocal += ootl;
                      }
                    } else if (onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                      onOpenTradeLocal -= ootl;
                    }
                    return true;
                  };
                }
              }
              #endregion
              {
                var ranges = (Queue<double[]>)bag[0];
                var extreamsQueue = (Queue<MathExtensions.Extream<Rate>>)bag[1];
                var isBusy = (bool)bag[2];
                var valleyLevels = (List<double[]>)bag[3];
                Func<double[], double[]> getLengthByAngle = ratesRev => {
                  var start = 60;
                  while (start < ratesRev.Length) {
                    if (AngleFromTangent(ratesRev.CopyToArray(start++).Regress(1).LineSlope()).Abs().ToInt() == 0)
                      break;
                  }
                  ratesRev = ratesRev.CopyToArray(start - 1);
                  return new[] { ratesRev.Max(), ratesRev.Min() };
                };

                var angleOk = calcAngleOk();
                Func<double[], bool> setRates = minMax => angleOk && CalculateLastPrice(GetTradeEnterBy(null)).Between(minMax[0], minMax[1]);
                var canTrade = valleyLevels.Any(setRates);
                watcherCanTrade.SetValue(canTrade, true, () => {
                  var minMax = valleyLevels.Last();
                  BuyLevel.RateEx = minMax[1];
                  SellLevel.RateEx = minMax[0];
                  _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                });

                GeneralPurposeSubject.OnNext(() => {
                  Action debug = () => { if (Debugger.IsAttached) Debugger.Break(); else Debugger.Launch(); };
                  try {
                    if (isBusy) throw new Exception("I am busy.");
                    isBusy = true;
                    var tail = CorridorDistance.Div(2).ToInt();
                    var range = !extreamsQueue.Any() ? RatesInternal.Count
                      : (RatesArray.Count - RatesArray.IndexOf(extreamsQueue.Last().Element) + tail).Min(CorridorDistance + tail);
                    var ratesReversed = UseRatesInternal(ri =>
                      ri.SafeArray().CopyToArray(ri.Count - range, range).Reverse().ToArray());
                    Func<IList<double>, double[]> minMax = rates => new[] { rates.Min(), rates.Max() };
                    var extreamsAll = ratesReversed.Extreams(GetVoltage, CorridorDistance).Reverse().ToArray();
                    Func<double, double, bool> isSameSlope = (slope1, slope2) => slope1.Sign() == slope2.Sign();
                    Func<double> getLastSlope = () => extreamsQueue.Select(eq => eq.Slope).LastOrDefault();
                    var lastSlope = getLastSlope();
                    extreamsAll
                      .SkipWhile(e => isSameSlope(lastSlope, e.Slope))
                      .Select(ex => {
                        extreamsQueue.Enqueue(ex);
                        return ex;
                      }).ToArray().Take(1).ForEach(_1 => {
                        var levelsCount = 3;
                        extreamsQueue.Skip(10).ToList().ForEach(eq1 => extreamsQueue.Dequeue());
                        extreamsQueue
                          .Reverse().SkipWhile(e => e.Slope > 0).Take(levelsCount * 2).Reverse().ToArray()
                          .Yield()
                          .Select(extreams => {
                            if (range > RatesArray.Count && extreams.Length < levelsCount)
                              throw new Exception("Range {0} is too short for {1} valleys.".Formater(BarsCountCalc, levelsCount));
                            return UseRatesInternal(ri =>
                              extreams.Where(e => e.Slope > 0).Zip(extreams.Where(e => e.Slope < 0), (eu, ed) => new { eu, ed })
                                .ToArray(eud =>
                                  minMax(ri
                                  .SkipWhile(r => r <= eud.eu.Element0)
                                  .TakeWhile(r => r < eud.ed.Element0)
                                  .ToArray(_priceAvg)))
                            );
                          })
                          .Select(levels => {
                            while (ranges.Count + levels.SafeList().Count > levelsCount) ranges.Dequeue();
                            var skipToFirst = levelsCount - 2;
                            levels.ToArray(l => {
                              ranges.Enqueue(l);
                              return l;
                            })
                            .Take(1)
                            .Select(l => ranges.Skip(skipToFirst).First())
                            .ForEach(lh => {
                              CenterOfMassBuy = lh[1];
                              CenterOfMassSell = lh[0];
                              _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                            });
                            return new { levels, skipToFirst };
                          })
                          .ForEach(_ => {
                            valleyLevels.Clear();
                            valleyLevels.AddRange(_.levels.Skip(_.skipToFirst));
                          });
                      });
                    extreamsQueue.Zip(extreamsQueue.Skip(1), (ep, en) => new { ep, en }).Where(epn => epn.ep.Slope.Sign() == epn.en.Slope.Sign()).ForEach(epn => debug());
                  } catch (Exception exc) {
                    Log = exc;
                  } finally {
                    isBusy = false;
                  }
                });
              }
              adjustExitLevels0();
              break;
            #endregion
            #region TillFlat
            case TrailingWaveMethod.TillFlat: {
                #region firstTime
                var hotValue = new { _ = new Subject<Func<bool, DateTime>>() };
                Action setTradeRange = () => { if (Debugger.IsAttached)Debugger.Break(); else Debugger.Launch(); };
                if (firstTime) {
                  CanDoEntryOrders = false;
                  hotValue.Caster(out any)()._.DistinctUntilChanged(a => a(false)).Subscribe(a => a(true));
                  Log = new Exception(new { TradingAngleRange, WaveStDevRatio } + "");
                  watcherCanTrade.Value = false;
                  onCloseTradeLocal = t => {
                    if (t.PL >= PriceSpreadAverageInPips) {
                      _buySellLevelsForEach(sr => {
                        sr.CanTradeEx = false;
                        sr.TradesCountEx = CorridorCrossesMaximum;
                        GetEntryOrders(sr.IsBuy).ForEach(OnDeletingOrder);
                      });
                      if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                    }
                  };
                  initTradeRangeShift();
                }
                #endregion
                {
                  var corridorTimeOK = RateLast.StartDate.Hour.Between(17, 23);
                  var angleOk = calcAngleOk();
                  var canTrade = angleOk && corridorTimeOK;
                  if (canTrade) hotValue.Caster(any)._.OnNext(
                    (doRun) => {
                      if (doRun) {
                        var rateLast = RatesArray.GetRange(RatesArray.Count - 3, 3).AsEnumerable().Reverse().SkipWhile(r => r.PriceAvg2 == 0).First();
                        CorridorStats.Rates.SkipWhile(r => r.PriceAvg2 == 0).Take(1)
                          .ForEach(rate => {
                            BuyLevel.RateEx = rateLast.PriceAvg2;
                            SellLevel.RateEx = rateLast.PriceAvg3;
                            _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                          });
                      }
                      return CorridorStats.StartDate;
                    });
                  if (ServerTime.Hour == 8 && CurrentGrossInPipTotal > InPips(BuyLevel.Rate.Abs(SellLevel.Rate) / 2))
                    CloseTrading("After 8 o'clock profit.");
                }
              }
              adjustExitLevels0();
              break;
            #endregion

            #region TimeFrame2
            case TrailingWaveMethod.TimeFrame2: {
                #region firstTime
                if (firstTime) {
                  HarmonicMin = int.MaxValue;
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (t.PL > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  #endregion
                  initTradeRangeShift();
                }
                #endregion
                {
                  #region Set locals
                  var corridorRates = CorridorStats.Rates;
                  var lastPrice = CurrentEnterPrice(null);
                  Func<bool> corridorOk = () => //corridorRates.Count >= CorridorDistance &&
                    CorridorStopDate.Between(RateLast.StartDate.AddMinutes(-2 * BarPeriodInt), RateLast.StartDate);
                  var corridorLevels = new[] { corridorRates[0], corridorRates.Last() }
                    .Buffer(2)
                    .Select(b => new { up = b.Max(r => r.PriceAvg2), down = b.Min(r => r.PriceAvg3) });
                  var points = InPoints(1) * 5;
                  var tlAnon = new { up = 0.0, down = 0.0 };
                  var tlWithAngleAnon = new { tlAnon.up, tlAnon.down, angle = 0 };
                  var tradeLevels = new[] { 
                    corridorLevels
                    .Where(r=>IsAutoStrategy && corridorOk()) //&& (CorridorStats.RatesMax>= (_RatesMax-points) || CorridorStats.RatesMin<= (_RatesMin+points))
                    ,new {up=BuyLevel.Rate,down=SellLevel.Rate}.Yield()
                    .Where(tl =>!IsAutoStrategy && lastPrice.Between(tl.down, tl.up)                      )
                  }.SelectMany(a => a).ToArray();
                  var angleOk = calcAngleOk();
                  #endregion
                  var canTrade = angleOk && isTradingHourLocal() && tradeLevels.Any();

                  #region setTradeLevels
                  Action<double, double> setTradeLevels = (up, down) => {
                    var h = up.Abs(down);
                    _buySellLevelsForEach(sr => sr.ResetPricePosition());
                    if (lastPrice > up) {
                      BuyLevel.RateEx = up + h * 2;
                      BuyLevel.CanTradeEx = true;
                      BuyLevel.TradesCountEx = CorridorCrossesMaximum + 1;
                      SellLevel.RateEx = up + h;
                      SellLevel.CanTradeEx = true;
                      SellLevel.TradesCountEx = CorridorCrossesMaximum;
                    } else {
                      BuyLevel.RateEx = down - h;
                      BuyLevel.CanTradeEx = true;
                      BuyLevel.TradesCountEx = CorridorCrossesMaximum;
                      SellLevel.RateEx = down - h * 2;
                      SellLevel.CanTradeEx = true;
                      SellLevel.TradesCountEx = CorridorCrossesMaximum + 1;
                    }
                    _buySellLevelsForEach(sr => sr.ResetPricePosition());
                  };
                  Action<double, double> setTradeLevels2 = (up, down) => {
                    _buySellLevelsForEach(sr => sr.ResetPricePosition());
                    BuyLevel.RateEx = up;
                    SellLevel.RateEx = down;
                    _buySellLevelsForEach(sr => {
                      if (!sr.CanTrade) sr.TradesCountEx = CorridorCrossesMaximum;
                      sr.CanTradeEx = true;
                      sr.ResetPricePosition();
                    });
                  };
                  #endregion
                  #region shortWaveAngle
                  var shortWave = tlWithAngleAnon.ToFunc(() => {
                    var rates = RatesArray.TakeLast(75).ToArray(_priceAvg);
                    var coeffs = rates.Regress(1);
                    var height2 = rates.HeightByRegressoin2(coeffs);
                    var mean = rates.Max().Avg(rates.Min());
                    return new { up = mean + height2 / 2, down = mean - height2 / 2, angle = coeffs.LineSlope().Sign() };
                  });
                  #endregion

                  #region Trading workflow
                  Action sizeTradingCorridor = () => {
                    var height = CorridorStats.StDevByPriceAvg;
                    var median = BuyLevel.Rate.Avg(SellLevel.Rate);
                    BuyLevel.RateEx = median + height / 2;
                    SellLevel.RateEx = median - height / 2;
                  };
                  Func<double, int> slideTradingCorridor = enterPrice => {
                    Func<SuppRes, Func<SuppRes, bool>, double, bool> setBSLevel = (sr, ok, offset) => {
                      if (sr.TradesCount > 0 && ok(sr)) {
                        //var offset = sr.RateEx - enterPrice;
                        _buySellLevelsForEach(sr2 => sr2.RateEx -= offset);
                        return true;
                      }
                      return false;
                    };
                    sizeTradingCorridor();
                    var point = InPoints(1);
                    if (setBSLevel(BuyLevel, sr => enterPrice > sr.Rate + point, -RateLast.AskHigh + BuyLevel.Rate)) return 1;
                    if (setBSLevel(SellLevel, sr => enterPrice < sr.Rate - point, SellLevel.Rate - RateLast.BidLow)) return -1;
                    return 0;
                  };

                  #region wf
                  var wf = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ =>{WorkflowStep = "1.Wait corridor to start"+new{caz=CloseAtZero};
                      if( canTrade ){
                        if (CloseAtZero) CloseAtZero = false;
                        return tupleNextSingle(tradeLevels.First());
                      }
                      return tupleStay(null);
                    },
                    _tl=>{WorkflowStep = "2.Wait corridor to finish";
                      var tl = tradeLevels.Take(1)
                        .Concat(_tl.Select(tl0 => tlAnon.Caster(tl0)))
                        .MinBy(tl0 => tl0.up.Abs(tl0.down)).First();
                      CenterOfMassSell = tl.up;
                      CenterOfMassBuy = tl.down;
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      return (canTrade ? tupleStaySingle : tupleNextSingle)(tl);
                    },
                    _tl => {WorkflowStep = "3.Wait price jumps out";
                      if (canTrade) return tupleCancelEmpty();
                      var tl = tlAnon.Caster(_tl.First());
                      var h = tl.up.Abs(tl.down) / 3;
                      return (lastPrice.Between(tl.down - h, tl.up + h) ? tupleStay : tupleNext)(_tl);
                    },
                    _tl => {WorkflowStep = "4.Wait price jumps in";
                      if (IsAutoStrategy && canTrade) return tupleCancelEmpty();
                      var tl = tlAnon.Caster(_tl.First());
                      return !lastPrice.Between(tl.down, tl.up) ? tupleStay(_tl) : tupleNext(_tl);
                    },
                    _tla =>{WorkflowStep = "5.Wait for flat short wave" ;
                      var tla = tlAnon.Caster(_tla.First());
                      setTradeLevels2(tla.up, tla.down);
                      return tupleNext(_tla);

                      if (canTrade){
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        return tupleCancelEmpty();
                      }
                      var tlaOld = tlWithAngleAnon.Caster(_tla.First());
                      var tlaNew = shortWave();
                      var isFlat = tlaOld.angle != 0 && tlaOld.angle != tlaNew.angle;
                      return (isFlat ? tupleNextSingle : tupleStaySingle)(tlaNew);
                    },
                    _tla =>{
                      //var tla = tlWithAngleAnon.Caster(_tla.First());
                      //setTradeLevels2(tla.up, tla.down);
                      return tupleNextSingle(ServerTime.AddMinutes(
                        (_isCorridorStopDateManual ? CorridorStats.Rates.Count : CorridorDistance) * BarPeriodInt));
                    }
                    ,//5. Move trade corridor up/down until canTrade ot Trades.Any()
                    _date =>{
                      return tupleNext(_date);

                      var date = (DateTime)_date.First();
                      WorkflowStep = "6.Adjusting till " + string.Format("{0:HH:mm}", date);
                      if (ServerTime > date) return tupleNext(_date);
                      if (canTrade) return tupleCancelEmpty();
                      if (Trades.Any()) return tupleNext(_date);
                      Action<SuppRes, Func<SuppRes, bool>> setBSLevel = (sr, ok) => {
                        if (sr.TradesCount>0 && ok(sr)){
                          var offset = sr.RateEx - lastPrice;
                          _buySellLevelsForEach(sr2 => sr2.RateEx -= offset);
                        }
                      };
                      var point = InPoints(1);
                      setBSLevel(BuyLevel, sr => lastPrice > sr.Rate + point);
                      setBSLevel(SellLevel, sr => lastPrice < sr.Rate - point);
                      return tupleStay(_date);
                    },
                    _date =>{
                      if (IsAutoStrategy && canTrade) return tupleCancelEmpty();
                      var date = (DateTime)_date.First();
                      WorkflowStep = "7.In Trading till " + string.Format("{0:HH:mm}", date);
                      if(ServerTime < date )return tupleStaySingle(date);
                      _buySellLevelsForEach(sr=>sr.CanTradeEx = false);
                      var newHeight = BuyLevel.Rate.Abs(SellLevel.Rate) / 2;
                      if (Trades.HaveBuy()) {
                        BuyLevel.RateEx = lastPrice;
                        SellLevel.RateEx = BuyLevel.Rate - newHeight;
                      }
                      if (Trades.HaveSell()) {
                        SellLevel.RateEx = lastPrice;
                        BuyLevel.RateEx = SellLevel.Rate + newHeight;
                      }

                      return tupleCancelEmpty();
                    }
                  };
                  #endregion

                  #region wfManual
                  var udDownCountAnon = new { uc = 0.0, dc = 0.0 };
                  var rateLast = corridorRates.SkipWhile(r => r.PriceAvg21.IsZeroOrNaN()).Take(1).ToArray();
                  var rateFirst = corridorRates.Where(r => !r.PriceAvg21.IsZeroOrNaN()).TakeLast(1).ToArray();
                  var cep = CurrentEnterPrice(null);
                  var canTrade2 =
                    rateFirst.Where(rf => angleOk && (rf.BidLow < rf.PriceAvg31 || rf.AskHigh > rf.PriceAvg21))
                    .Select(_ =>
                     new[]{ 1.YieldIf(rateLast.Any(rl => cep > rl.PriceAvg21) /*&& rateFirst.Any(rf=>rf.BidLow<rf.PriceAvg31)*/),
                          (-1).YieldIf(rateLast.Any(rl => cep < rl.PriceAvg31) /*&& rateFirst.Any(rf=>rf.AskHigh>rf.PriceAvg21)*/),
                          0.Yield()
                     }.SelectMany(a => a).First()
                     ).FirstOrDefault();
                  #region onProfit
                  Action<List<object>> onProfit = ti => {
                    Action<Trade> otc = t => { if (t.PL > 0) ti.Add(true); };
                    Action<Trade> oto = null;
                    oto = t => {
                      if (_buySellLevels.Max(sr => sr.TradesCount) < -1) {
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        onOpenTradeLocal -= oto;
                        ti.Add(true);
                      }
                    };
                    onCloseTradeLocal += otc;
                    onOpenTradeLocal += oto;
                    ti.Add(new WF.OnExit(() => {
                      onCloseTradeLocal -= otc;
                      onOpenTradeLocal -= oto;
                    }));
                  };
                  #endregion
                  #region setBsLevels
                  Action setBsLevels = () => {
                    rateLast.Select(rl => rl.PriceAvg21 - rl.PriceAvg2).ForEach(height => {
                      var ratesShort = RatesArray.TakeLast(30);
                      var corrHeight = CorridorStats.StDevByPriceAvg;
                      if (canTrade2 > 0) {
                        BuyLevel.RateEx = ratesShort.Max(_priceAvg);
                        BuyLevel.CanTradeEx = !Trades.Any();
                        SellLevel.CanTradeEx = !Trades.Any();
                        SellLevel.RateEx = BuyLevel.Rate - corrHeight;
                      } else if (canTrade2 < 0) {
                        SellLevel.CanTradeEx = !Trades.Any();
                        SellLevel.RateEx = ratesShort.Min(_priceAvg);
                        BuyLevel.RateEx = SellLevel.Rate + corrHeight;
                        BuyLevel.CanTradeEx = !Trades.Any();
                      }
                    });
                  };
                  #endregion
                  Func<IEnumerable<object>, bool> mustExit = ti => ti.TakeLast(1).OfType<bool>().Any(b => b);
                  var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ =>{WorkflowStep = "1.Wait start"+new{caz=CloseAtZero};
                    _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      setBsLevels();
                      return (isTradingHourLocal() && canTrade2 != 0)
                        ? tupleNextEmpty()
                        : tupleStayEmpty();
                    },
                    _ti =>{
                      _buySellLevelsForEach(sr => sr.TradesCountEx = 0);
                      CloseAtZero = false;
                      setBsLevels();
                      onProfit(_ti);
                      return tupleNext(_ti);
                    },
                    _ti =>{ WorkflowStep = "2.Finalize.";
                      if (mustExit(_ti) || (!Trades.Any() && !angleOk)) 
                        return tupleCancel(_ti);
                      if (canTrade2 == 0) //==>
                        return tupleNext(_ti);
                      return tupleStay(_ti);//<==
                    },
                    _ti =>{ WorkflowStep = "3.In Trading.";
                      return (((!Trades.Any() && !isTradingHourLocal()) || mustExit(_ti) || canTrade2 != 0)
                        ? tupleNext
                        : tupleStay)(_ti);
                    }
                  };
                  #endregion

                  #endregion
                  workflowSubject.OnNext(wfManual);
                  Action ifCan = () => {
                    var b = tradeLevels.First().up;
                    var s = tradeLevels.First().down;
                    if (_buySellLevels.All(sr => !sr.CanTrade)
                      || InPips(b.Avg(s).Abs(BuyLevel.Rate.Avg(SellLevel.Rate))) > 5
                      || b.Abs(s) < BuyLevel.Rate.Abs(SellLevel.Rate)) {
                      _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                      BuyLevel.RateEx = b;
                      SellLevel.RateEx = s;
                    }
                  };
                  //if (IsAutoStrategy && BuyLevel.Rate.Abs(SellLevel.Rate) > RatesHeight / 3)
                  //  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  //canTradeSubject.OnNext(new { canTrade, ifCan });
                }
              }
              adjustExitLevels0();
              break;
            #endregion

            #region Spike
            case TrailingWaveMethod.Spike: {
                #region firstTime
                if (firstTime) {
                  HarmonicMin = int.MaxValue;
                  canTradeSubject
                    .DistinctUntilChanged(a => a.canTrade)
                    .Where(a => a.canTrade)
                    .Subscribe(a => a.ifCan(), exc => Log = exc, () => { Debugger.Break(); });
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (CurrentGrossInPipTotal > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                {
                  #region Set locals
                  var corridorRates = CorridorStats.Rates;
                  var lastPrice = CurrentEnterPrice(null);
                  var angleOk = calcAngleOk();
                  #endregion

                  #region Trading workflow

                  #region wfManual
                  var rateLast = corridorRates.SkipWhile(r => r.PriceAvg21.IsZeroOrNaN()).Take(1).ToArray();
                  var rateFirst = corridorRates.Where(r => !r.PriceAvg21.IsZeroOrNaN()).TakeLast(1).ToArray();
                  var cep = CurrentEnterPrice(null);
                  var corridorOk = corridorRates.Count > CorridorDistance;
                  var canTrade2 =
                    rateFirst.Where(rf => angleOk
                      && (rf.AskHigh > rf.PriceAvg3 && rf.BidLow < rf.PriceAvg2))
                    .Select(_ =>
                     new[]{ 1.YieldIf(rateLast.Any(rl => cep > rl.PriceAvg21) /*&& rateFirst.Any(rf=>rf.BidLow<rf.PriceAvg31)*/),
                          (-1).YieldIf(rateLast.Any(rl => cep < rl.PriceAvg31) /*&& rateFirst.Any(rf=>rf.AskHigh>rf.PriceAvg21)*/),
                          0.Yield()
                     }.SelectMany(a => a).First()
                     ).FirstOrDefault();
                  #region onProfit
                  Action<List<object>> onProfit = ti => {
                    Action<Trade> otc = t => {
                      if (t.PL > 0) {
                        ti.Add(true);
                      }
                    };
                    Action<Trade> oto = async t => {
                      if (_buySellLevels.Min(sr => sr.TradesCount) < -0) {
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        ti.Add(true);
                      };
                    };
                    onCloseTradeLocal += otc;
                    onOpenTradeLocal += oto;
                    ti.Add(new WF.OnExit(() => {
                      onCloseTradeLocal -= otc;
                      onOpenTradeLocal -= oto;
                    }));
                  };
                  #endregion
                  #region setBsLevels
                  Func<bool> setBSLevels = () => {
                    if (canTrade2 == 0) return false;
                    rateLast.ForEach(_ => {
                      var ratesShort = RatesArray.TakeLast(30);
                      var corrHeight = CorridorStats.StDevByPriceAvg;
                      if (canTrade2 > 0) {
                        BuyLevel.RateEx = rateLast.First().PriceAvg21;
                        SellLevel.RateEx = rateLast.First().PriceAvg3;
                        if (!Trades.Any()) BuyLevel.CanTradeEx = true;
                        SellLevel.CanTradeEx = false;
                      } else {
                        SellLevel.RateEx = rateLast.First().PriceAvg31;
                        BuyLevel.RateEx = rateLast.First().PriceAvg2;
                        if (!Trades.Any()) SellLevel.CanTradeEx = true;
                        BuyLevel.CanTradeEx = false;
                      }
                    });
                    return true;
                  };
                  #endregion
                  Func<IEnumerable<object>, bool> mustExit = ti =>
                    ti.TakeLast(1).OfType<bool>().Any(b => b) ||
                    ti.OfType<WF.StartDate>().Any(st => (ServerTime - st()).TotalMinutes > BarsCount * BarPeriodInt);
                  var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{WorkflowStep = "1.Wait start"+new{caz=CloseAtZero};
                      if (isTradingHourLocal() && setBSLevels()){
                        _buySellLevelsForEach(sr => sr.TradesCountEx = 0);
                        CloseAtZero = false;
                        onProfit(_ti);
                        var startDate = ServerTime;
                        _ti.Add(new WF.StartDate(() => startDate));
                        return tupleNext(_ti);
                      }
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      return tupleStayEmpty();
                    },
                    _ti =>{ WorkflowStep = "2.Finalize.";
                      if (mustExit(_ti)) return tupleCancel(_ti);
                      if (canTrade2 == 0) return tupleNext(_ti);
                      return tupleStay(_ti);//<==
                    },
                    _ti =>{ WorkflowStep = "3.In Trading.";
                      return (((!Trades.Any() && !isTradingHourLocal()) || mustExit(_ti) || canTrade2 != 0)
                        ? tupleNext
                        : tupleStay)(_ti);
                    }
                  };
                  #endregion
                  #endregion
                  workflowSubject.OnNext(wfManual);
                }
              }
              adjustExitLevels0();
              break;
            #endregion
            #region Spike2
            case TrailingWaveMethod.Spike2: {
                #region firstTime
                if (firstTime) {
                  Log = new Exception(new { TrailingDistanceFunction, WaveStDevRatio } + "");
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (CurrentGrossInPipTotal > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                {
                  #region Set locals
                  var corridorRates = CorridorStats.Rates;
                  var lastPrice = CurrentEnterPrice(null);
                  var rateLast = corridorRates.Reverse().SkipWhile(r => r.PriceAvg1.IsZeroOrNaN()).Take(1).Memoize();
                  var corridorOk = corridorRates.Count < CorridorDistance;
                  Action<bool, bool> setCanTrade = (condition, on) => {
                    if (condition) {
                      BuyLevel.CanTradeEx = on && CorridorStats.Slope > 0;
                      SellLevel.CanTradeEx = on && CorridorStats.Slope < 0;
                    }
                  };
                  #endregion

                  #region Trading workflow

                  #region wfManual
                  var offset = StDevByPriceAvg;
                  var isUp = rateLast.Any(rl=>rl.PriceAvg2.Avg(rl.PriceAvg3) > rl.PriceAvg1);
                  var bsLevels = rateLast.Select(rl => new {
                    u = CenterOfMassBuy = isUp? rl.PriceAvg1:rl.PriceAvg21,
                    d = CenterOfMassSell = !isUp ? rl.PriceAvg1 : rl.PriceAvg31
                  });
                  Func<Rate, bool> calcVOK0 = rl => !rl.PriceAvg.Between(rl.PriceAvg3, rl.PriceAvg2);
                  Func<Rate, bool> calcVOK = r => (r.PriceAvg2 - r.PriceAvg1).Ratio(r.PriceAvg1 - r.PriceAvg3) > WaveStDevRatio;
                  var volatilityOk = rateLast.Where(calcVOK);
                  Action setBSHeight = () => BuyLevel.RateEx.Avg(SellLevel.Rate)
                    .Yield(mean => new { mean, offset = offset })
                    .Do(a => {
                      BuyLevel.RateEx = a.mean + a.offset;
                      SellLevel.RateEx = a.mean - a.offset;
                    }).Any();
                  var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{WorkflowStep = "1.Wait start";
                    bsLevels.ForEach(ud => {
                      var canTrade = new[] { corridorOk, lastPrice.Between(ud.d, ud.u), volatilityOk.Any() };
                      canTrade.Where(b => !b).Take(1).IfEmpty(_ => {
                        BuyLevel.RateEx = ud.u;
                        SellLevel.RateEx = ud.d;
                        setCanTrade(IsAutoStrategy, true);
                      }, _ => setCanTrade(!volatilityOk.Any() || !isTradingHourLocal(), false)
                      ).Any();
                      //setBSHeight();
                    });
                      return tupleStay(_ti);
                    }
                  };
                  #endregion
                  #endregion
                  workflowSubject.OnNext(wfManual);
                }
              }
              adjustExitLevels0();
              break;
            #endregion
            #region FrameAngle
            case TrailingWaveMethod.FrameAngle: {
                #region firstTime
                if (firstTime) {
                  Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    if (CurrentGrossInPipTotal > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                var point = InPoints(1);
                var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
                var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait start";
                    var slopeCurrent = CorridorStats.Slope.Sign();
                    var slope = _ti.OfType<int>().FirstOrDefault(slopeCurrent);
                    if (corridorOk && slopeCurrent != slope) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                    }
                    return tupleStaySingle(slopeCurrent);
                    }};
                workflowSubject.OnNext(wfManual);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region FrameAngle2
            case TrailingWaveMethod.FrameAngle2: {
                #region firstTime
                if (firstTime) {
                  Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    if (CurrentGrossInPipTotal > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      BroadcastCloseAllTrades();
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                var point = InPoints(1);
                var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
                var isBlackout = new Lazy<bool>(() => IsBlackout(RateLast.StartDate));
                if (isBlackout.Value) {
                  if (Trades.Any()) BroadcastCloseAllTrades();
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                }
                var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait start";
                    var slopeCurrent = CorridorStats.Slope.Sign();
                    var slope = _ti.OfType<int>().FirstOrDefault(slopeCurrent);
                    if (corridorOk && (calcAngleOk() || slopeCurrent != slope)) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                        return tupleNextEmpty();
                    }
                    return tupleStaySingle(slopeCurrent);
                    },_ti =>{ WorkflowStep = "2 Wait Finish";
                    if (!corridorOk) return tupleNext(_ti);
                    if (CorridorStats.RatesHeight + point * 2 < BuyLevel.Rate.Abs(SellLevel.Rate)) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                    }
                        return tupleStay(_ti);
                    }};
                workflowSubject.OnNext(wfManual);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region FrameAngle3
            case TrailingWaveMethod.FrameAngle3: {
                #region firstTime
                if (firstTime) {
                  Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    if (CurrentGrossInPipTotal > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      BroadcastCloseAllTrades();
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                var point = InPoints(1);
                var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
                var corridorOk2 = CorridorStats.Rates.Count > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
                var isBlackout = new Lazy<bool>(() => IsBlackout(RateLast.StartDate));
                if (isBlackout.Value) {
                  if (Trades.Any()) BroadcastCloseAllTrades();
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                }
                var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait start";
                    var slopeCurrent = CorridorStats.Slope.Sign();
                    var slope = _ti.OfType<int>().FirstOrDefault(slopeCurrent);
                    if (corridorOk && (calcAngleOk() || slopeCurrent != slope)) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                        return tupleNextEmpty();
                    }
                    return tupleStaySingle(slopeCurrent);
                    },_ti =>{ WorkflowStep = "2 Wait Finish";
                    if (!corridorOk2) return tupleNext(_ti);
                    if (CorridorStats.RatesHeight + point * 2 < BuyLevel.Rate.Abs(SellLevel.Rate)) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                    }
                        return tupleStay(_ti);
                    }};
                workflowSubject.OnNext(wfManual);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region FrameAngle4
            case TrailingWaveMethod.FrameAngle4: {
                #region firstTime
                if (firstTime) {
                  Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    if (CurrentGrossInPipTotal > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      BroadcastCloseAllTrades();
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                var point = InPoints(1);
                var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
                var corridorOk2 = CorridorStats.Rates.Count > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
                var isBlackout = new Lazy<bool>(() => IsBlackout(RateLast.StartDate));
                if (isBlackout.Value) {
                  if (Trades.Any()) BroadcastCloseAllTrades();
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                }
                var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait start";
                    var slopeCurrent = CorridorStats.Slope.Sign();
                    var slope = _ti.OfType<int>().FirstOrDefault(slopeCurrent);
                    var up = CorridorStats.RatesMax + point;
                    var down = CorridorStats.RatesMin - point;
                    if (corridorOk && (calcAngleOk() || slopeCurrent != slope)) {
                        BuyLevel.RateEx = up;
                        SellLevel.RateEx = down;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                        return tupleNextEmpty();
                    }else if (up.Abs(down) < BuyLevel.Rate.Abs(SellLevel.Rate)) {
                        BuyLevel.RateEx = up;
                        SellLevel.RateEx = down;
                    }
                    return tupleStaySingle(slopeCurrent);
                    }};
                workflowSubject.OnNext(wfManual);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region Spike3
            case TrailingWaveMethod.Spike3: {
                #region firstTime
                if (firstTime) {
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (CurrentGrossInPipTotal > 0) {
                      if (!IsInVitualTrading) IsTradingActive = false;
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                {
                  #region Set locals
                  var cep = CurrentEnterPrice(null);
                  var corridorRates = CorridorStats.Rates;
                  var lastPrice = CurrentEnterPrice(null);
                  var rateFirst = corridorRates.SkipWhile(r => r.PriceAvg1.IsZeroOrNaN()).Take(1).Memoize();
                  var corridorOk = corridorRates.Count > CorridorDistance
                    && rateFirst.Any(rf => !rf.PriceAvg1.Between(CorridorStats.RatesMin, CorridorStats.RatesMax));
                  Action<bool, bool> setCanTrade = (condition, on) => { if (condition) _buySellLevelsForEach(sr => sr.CanTradeEx = on); };
                  #endregion

                  #region wfManual
                  var startDateAnon = new { date = DateTime.MinValue };
                  var startDateCtor = startDateAnon.ToFunc(DateTime.MinValue, date => new { date });
                  #region onProfit
                  Func<IEnumerable<object>, bool> mustExit = ti =>
                    ti.TakeLast(1).OfType<bool>().Any(b => b) ||
                    ti.OfType(startDateAnon).Any(st => (ServerTime - st.date).TotalMinutes > BarsCount * BarPeriodInt);
                  Action<List<object>> onProfit = ti => {
                    Action<Trade> otc = t => {
                      if (t.PL > 0) {
                        setCanTrade(true, false);
                        ti.Add(true);
                      }
                    };
                    Action<Trade> oto = t => {
                      if (_buySellLevels.Min(sr => sr.TradesCount) < -1) {
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        //ti.Add(true);
                      };
                    };
                    onCloseTradeLocal += otc;
                    onOpenTradeLocal += oto;
                    ti.Add(new WF.OnExit(() => {
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      onCloseTradeLocal -= otc;
                      onOpenTradeLocal -= oto;
                    }));
                  };
                  #endregion
                  var isUp = CorridorStats.Slope > 0;
                  var bsLevels = rateFirst
                    .Where(_ => corridorOk)
                    .Select(rf => new { rf, isUp})
                    .Select(a => new {
                      u = new { rate = a.isUp ? _RatesMin : a.rf.PriceAvg1, canTrade = !a.isUp },
                      d = new { rate = a.isUp? a.rf.PriceAvg1: _RatesMax, canTrade = a.isUp },
                      a.isUp
                    });
                  var setBSLevels = bsLevels
                    .Select(ud => ((Func<double,double,bool>)null).ToAction((isReset) => {
                    if (isReset(ud.u.rate,ud.d.rate)) {
                      BuyLevel.RateEx = ud.u.rate; BuyLevel.CanTrade = ud.u.canTrade;
                      SellLevel.RateEx = ud.d.rate; SellLevel.CanTrade = ud.d.canTrade;
                    }
                    _buySellLevelsForEach(sr => sr.TradesCountEx = 0);
                  }));
                  var wfAnon =  new { buy = true, sell = true } ;
                  Func<double, double, bool> mustChange = (u, d) => u.Abs(d) < BuyLevel.Rate.Abs(SellLevel.Rate);
                  Action<bool, Action<Rate>, Action> setCloseRate = (isBuy, setRate, setRateDefault) => Trades.IsBuy(isBuy).Take(1)
                    .IfEmpty(setRateDefault)
                    .ForEach(_ => rateFirst.ForEach(setRate));
                  Action setCloseRates = () => {
                    setCloseRate(true, rf => { BuyCloseLevel.RateEx = rf.PriceAvg2; SellCloseLevel.RateEx = this.CrossLevelDefault(false); }, () => BuyCloseLevel.RateEx = CrossLevelDefault(true));
                    setCloseRate(false, rf => { SellCloseLevel.RateEx = rf.PriceAvg3; BuyCloseLevel.RateEx = this.CrossLevelDefault(true); }, () => SellCloseLevel.RateEx = CrossLevelDefault(false));
                  };
                  var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{WorkflowStep = "1 Wait start";
                      if (setBSLevels.Do(a => a((u, d) => true)).Any()) {
                        _buySellLevelsForEach(sr => { sr.ResetPricePosition(); sr.TradesCountEx = 0; });
                         return tupleNext(_ti);
                      }
                      setCloseRates();
                      setCanTrade(!isTradingHourLocal(), false);
                      return tupleStay(_ti);
                    },_ti=>{WorkflowStep = "1.1 Set Stuff";
                      onProfit(_ti);
                      _ti.Add(startDateCtor(ServerTime));
                      return tupleNext(_ti);
                    },_ti=>{WorkflowStep = "2 Wait";
                      if (mustExit(_ti)) return tupleCancel(_ti);
                      if (setBSLevels.Do(a=>a((u, d) => true)).Any()) 
                        return tupleStay(_ti);
                      return tupleNext(_ti);
                    },_ti=>{WorkflowStep = "3 Trade";
                      if (setBSLevels.Any() || mustExit(_ti)) return tupleCancel(_ti);
                      setCloseRates();
                      return tupleStay(_ti);
                    }

                  };
                  #endregion
                  workflowSubject.OnNext(wfManual);
                }
              }
              //adjustExitLevels0();
              break;
            #endregion
            #region Spike4
            case TrailingWaveMethod.Spike4: {
                #region firstTime
                if (firstTime) {
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (t.GrossPL > -PriceSpreadAverage)
                      if (!IsAutoStrategy) {
                        IsTradingActive = false;
                        CorridorStartDate = null;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      }
                    if (CurrentGrossInPipTotal > -PriceSpreadAverage) {
                      _buySellLevelsForEach(sr => sr.TradesCountEx = this.CorridorCrossesMaximum);
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                {
                  #region Set locals
                  var corridorRates = CorridorStats.Rates;
                  var lastPrice = CurrentEnterPrice(null);
                  var rateFirst = corridorRates.SkipWhile(r => r.PriceAvg1.IsZeroOrNaN()).Take(1).Memoize();
                  var corridorOk = corridorRates.Count > CorridorDistance;
                  Func<bool, bool, bool> setCanTrade = (condition, on) => { if (condition) _buySellLevelsForEach(sr => sr.CanTradeEx = on); return condition; };
                  #endregion

                  #region wfManual
                  #region onTrade
                  var tradesCountAnon = new { v = 0.ToBox() };
                  var slope_a = new { slope_a = 0 };
                  var slope_c = slope_a.ToFunc(0, slope => new { slope_a = slope });
                  Func<List<object>, List<object>> onTrade = ti => {
                    Action<Trade> oto = t => {
                      var tc = ti.OfType(tradesCountAnon).Single();
                      if (tc.v > 0) {
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        CorridorStartDate = null;
                        ti.Add(new WF.MustExit(() => true));
                      };
                      tc.v.Value++;
                    };
                    ti.Add(tradesCountAnon);
                    ti.Add(new WF.DateStart(CorridorStartDate.Value, (s, v) => ti.Add(new WF.MustExit(() => true))));
                    ti.Add(slope_c(CorridorStats.Slope.Sign()));
                    onOpenTradeLocal += oto;
                    ti.Add(new WF.OnExit(() => {
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      onOpenTradeLocal -= oto;
                    }));
                    return ti;
                  };
                  Func<IEnumerable<object>, bool> mustExit = ti => ti.TakeLast(1).OfType<bool>().Any(b => b);
                  Action resetStartDate = () => {
                    if (_buySellLevels.Any(sr=>sr.CanTrade) &&(
                      corridorRates.Count >= RatesArray.Count
                      || BuyLevel.Rate.Abs(SellLevel.Rate) > RatesHeight / 2))
                      CorridorStartDate = null;
                  };
                  #endregion
                  #region Set Levels
                  var bsLevels = rateFirst
                    .Select(rf => {
                      var rates = corridorRates.Skip(1).Select(_priceAvg).ToArray();
                      return new {
                        buy = new { rate = rates.Max() },
                        sell = new { rate = rates.Min() }
                      };
                    });
                  var setBSLevels = bsLevels
                    .Select(ud => new Action(() => {
                      BuyLevel.RateEx = ud.buy.rate;
                      SellLevel.RateEx = ud.sell.rate;
                    }));
                  #endregion
                  Func<bool> isWFActive = () => CorridorStartDate.HasValue && corridorRates.Count < RatesArray.Count;
                  Func<bool> quickie = () => AngleFromTangent(corridorRates.Take(30).Select(_priceAvg).ToArray().Regress(1).LineSlope()).Abs() > TradingAngleRange;
                  var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{WorkflowStep = "1 Wait start";
                      resetStartDate();
                      setBSLevels.Do(a => a()).Any();
                      if (isWFActive() && isTradingHourLocal())
                        return tupleNext(onTrade(_ti));
                      return tupleStay(_ti);
                    },_ti=>{WorkflowStep = "2 Wait Flat";
                      if(!isWFActive())return tupleCancel(_ti);
                      _ti.OfType<WF.DateStart>().Single().Value = CorridorStartDate.GetValueOrDefault();
                      setBSLevels.Do(a => a()).Any();
                      resetStartDate();
                      if(CorridorStats.Slope.Sign() == _ti.OfType(slope_a).Single().slope_a)
                        return tupleStay(_ti);
                      setCanTrade(true, true);
                      return tupleNext(_ti);
                    },_ti=>{WorkflowStep = "3 Trading";
                      resetStartDate();
                      if(!isWFActive())return tupleCancel(_ti);
                      _ti.OfType<WF.DateStart>().Single().Value = CorridorStartDate.GetValueOrDefault();
                      return tupleStay(_ti);
                    }
                  };
                  #endregion
                  workflowSubject.OnNext(wfManual);
                }
              }
              adjustExitLevels0();
              break;
            #endregion
            #region ManualRange
            case TrailingWaveMethod.ManualRange: {
                #region firstTime
                if (firstTime) {
                  workFlowObservable.Subscribe();
                  #region onCloseTradeLocal
                  onCloseTradeLocal += t => {
                    if (t.GrossPL > -PriceSpreadAverage)
                      if (!IsAutoStrategy) {
                        IsTradingActive = false;
                        CorridorStartDate = null;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      }
                    if (CurrentGrossInPipTotal > -PriceSpreadAverage) {
                      _buySellLevelsForEach(sr => sr.TradesCountEx = this.CorridorCrossesMaximum);
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  #endregion
                  //initTradeRangeShift();
                }
                #endregion
                {
                  #region Set locals
                  var corridorRates = CorridorStats.Rates;
                  var lastPrice = CurrentEnterPrice(null);
                  var rateFirst = corridorRates.SkipWhile(r => r.PriceAvg1.IsZeroOrNaN()).Take(1).Memoize();
                  var corridorOk = corridorRates.Count > CorridorDistance;
                  Func<bool, bool, bool> setCanTrade = (condition, on) => { if (condition) _buySellLevelsForEach(sr => sr.CanTradeEx = on); return condition; };
                  #endregion

                  #region wfManual
                  #region onTrade
                  var tradesCountAnon = new { v = 0.ToBox() };
                  var slope_a = new { slope_a = 0 };
                  var slope_c = slope_a.ToFunc(0, slope => new { slope_a = slope });
                  Func<bool> mustExit = () => BuyLevel.Rate.Abs(SellLevel.Rate) > InPoints(CorridorHeightMax) || !isTradingHourLocal();
                  Func<List<object>, List<object>> onTrade = ti => {
                    Action<Trade> oto = t => {
                      var tc = ti.OfType(tradesCountAnon).Single();
                      if (tc.v > 0) {
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        CorridorStartDate = null;
                        ti.Add(new WF.MustExit(() => true));
                      };
                      tc.v.Value++;
                    };
                    ti.Add(tradesCountAnon);
                    ti.Add(new WF.MustExit(mustExit));
                    ti.Add(slope_c(CorridorStats.Slope.Sign()));
                    onOpenTradeLocal += oto;
                    ti.Add(new WF.OnExit(() => {
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      onOpenTradeLocal -= oto;
                    }));
                    return ti;
                  };
                  #endregion
                  #region Set Levels
                  var bsLevels = rateFirst
                    .Select(rf => new {
                      buy = new { rate = rf.PriceAvg2 },
                      sell = new { rate = rf.PriceAvg3 }
                    });
                  var setBSLevels = bsLevels
                    .Select(ud => new Action(() => {
                      BuyLevel.RateEx = ud.buy.rate;
                      SellLevel.RateEx = ud.sell.rate;
                    }));
                  #endregion
                  var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{WorkflowStep = "1 Wait start";
                      setBSLevels.Do(a => a()).Any();
                      if (!mustExit())
                        return tupleNext(onTrade(_ti));
                      return tupleStay(_ti);
                    },_ti=>{WorkflowStep = "2 Wait Flat";
                      setBSLevels.Do(a => a()).Any();
                      if(CorridorStats.Slope.Sign() == _ti.OfType(slope_a).Single().slope_a)
                        return tupleStay(_ti);
                      setCanTrade(true, true);
                      return tupleNext(_ti);
                    },_ti=>{WorkflowStep = "3 Trading";
                      return tupleStay(_ti);
                    }
                  };
                  #endregion
                  workflowSubject.OnNext(wfManual);
                }
              }
              adjustExitLevels0();
              break;
            #endregion

            #region ElliottWave
            case TrailingWaveMethod.ElliottWave: {
                #region firstTime
                if (firstTime) {
                  canTradeSubject
                    .Where(a => a.canTrade)
                    .Subscribe(a => a.ifCan(), exc => Log = exc, () => { Debugger.Break(); });

                  onCloseTradeLocal += t => {
                    if (!IsAutoStrategy && CurrentGrossInPipTotal > -PriceSpreadAverageInPips) {
                      IsTradingActive = false;
                      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
                    }
                  };
                  initTradeRangeShift();
                }
                #endregion
                {
                  GeneralPurposeSubject.OnNext(() => CalcFractals(RatesArray));
                  var lastPrice = CalculateLastPrice(GetTradeEnterBy(null));
                  var waveTimes = FractalTimes.OrderByDescending(d => d).ToArray();
                  Func<DateTime, DateTime, IEnumerable<Rate>> ratesByDateRange = (dateMin, dateMax) =>
                    RatesArray.SkipWhile(r => r.StartDate < dateMin).TakeWhile(r => r.StartDate <= dateMax);
                  var waves = (from dates in waveTimes.Zip(waveTimes.Skip(1), (d1, d2) => new { min = d1.Min(d2), max = d1.Max(d2) })
                               select ratesByDateRange(dates.min, dates.max).ToArray()
                                     ).ToArray();
                  var waveLengthAvg = waves.OrderByDescending(wave => wave.Length).Take(2);
                  Func<Rate, double> corridorPrice = _priceAvg;
                  var waveOk = (from wave1 in waves.Skip(1).Take(1)
                                where wave1.Height() >= waveLengthAvg.Average(wave => wave.Height())
                                && waves[0].Height() < wave1.Height() / 3
                                let dates = new { dateMax = waveTimes[0], dateMin = waveTimes[1] }
                                let rates = ratesByDateRange(dates.dateMin, dates.dateMax)
                                let levels = new { min = rates.Min(corridorPrice), max = rates.Max(corridorPrice) }
                                let height = levels.max - levels.min
                                let ratesShort = RatesArray.TakeLast(10).ToArray()
                                let levelUp = ratesShort.Average(GetTradeEnterBy(true)).Max(lastPrice, levels.max)
                                let levelDown = ratesShort.Average(GetTradeEnterBy(false)).Min(lastPrice, levels.min)
                                let corridors = new[] { 
                                  new {min=levelUp,max = levelUp+height},
                                  new {min=levelDown-height,max = levelDown}
                                }
                                from corridor in corridors
                                where lastPrice.Between(corridor.min, corridor.max)
                                select corridor
                               ).ToArray();
                  var canTrade = isTradingHourLocal() && waveOk.Any();
                  Action ifCan = () => {
                    _buySellLevelsForEach(sr => {
                      sr.CanTradeEx = true;
                    });
                    BuyLevel.RateEx = waveOk[0].max;
                    SellLevel.RateEx = waveOk[0].min;
                  };
                  canTradeSubject.OnNext(new { canTrade, ifCan });
                }
              }
              adjustExitLevels0();
              break;
            #endregion

            #region BigGap
            case TrailingWaveMethod.BigGap: {
                #region firstTime
                var hotValue = new { _ = new Subject<DateTime>() };
                if (firstTime) {
                  any = hotValue;
                  hotValue.Caster(any)._
                    .DistinctUntilChanged()
                    .Window(2)
                    .Subscribe(w => {
                      w.LastAsync().Zip(w.FirstAsync(), (dl, df) => dl > df)
                        .Where(off => off && !CurrentPrice.Average.Between(SellLevel.Rate, BuyLevel.Rate))
                        .Subscribe(_ => {
                          _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        });
                    });
                  Log = new Exception(new { TradingAngleRange, WaveStDevRatio } + "");
                  onCloseTradeLocal = t => {
                    if (t.PL >= TakeProfitPips / 2) {
                      _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                      if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                    }
                  };
                  {
                    if (ScanCorridorBy != ScanCorridorFunction.BigGap) {
                      ScanCorridorBy = ScanCorridorFunction.BigGap;
                      Log = new Exception(new { ScanCorridorBy, ScanCorridor = "changed" } + "");
                    }
                  }
                  {
                    Func<Trade, IEnumerable<Tuple<SuppRes, double>>> ootl_ = (trade) =>
                      (from offset in (trade.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (CurrentPrice.Bid - SellLevel.Rate)).Yield()
                       from sr in new[] { BuyLevel, SellLevel }
                       select Tuple.Create(sr, offset));
                    Action<Trade> ootl = null;
                    ootl = trade => {
                      ootl_(trade).ForEach(tpl => tpl.Item1.Rate += tpl.Item2);
                      onOpenTradeLocal -= ootl;
                    };
                    onCanTradeLocal = canTrade => {
                      if (!canTrade) {
                        if (!onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                          onOpenTradeLocal += ootl;
                        }
                      } else if (onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                        onOpenTradeLocal -= ootl;
                      }
                      return true;
                    };
                  }
                }
                #endregion
                {
                  var angleOk = calcAngleOk();
                  Func<Rate, double> priceRange = rate => rate.PriceAvg2 - rate.PriceAvg3;
                  var anyLevels = new { up = new double[0], down = new double[0] };
                  var levels = anyLevels.Caster((Rate rate) => {
                    var pr = priceRange(rate);
                    Func<double, double[]> priceLevels = middle => new[] { middle + pr / 2, middle - pr / 2 };
                    var date = CorridorStats.StartDate;
                    var ratesMinMax = new { min = CorridorStats.RatesMin, max = CorridorStats.RatesMax };
                    return new {
                      up = new[] { rate.PriceAvg2, rate.PriceAvg1 },//priceLevels(ratesMinMax.max),
                      down = new[] { rate.PriceAvg1, rate.PriceAvg3 }// priceLevels(ratesMinMax.min)
                    };
                  });
                  var lengthOk = CorridorStats.Rates.Count > CorridorDistance * WaveStDevRatio;
                  var canTrade = angleOk && lengthOk;
                  Func<Rate, IEnumerable<double[]>> tradeCorridorOk = rate =>
                    from ls in levels(rate).Yield().SelectMany(l => new[] { l.up, l.down })
                    from te in new[] { GetTradeEnterBy(true), GetTradeEnterBy(false) }
                    where te(rate).Between(ls[1], ls[0])
                    select ls;
                  //new[] { GetTradeEnterBy(true), GetTradeEnterBy(false) }.All(p => p(RateLast).Between(rate.PriceAvg3, rate.PriceAvg2));
                  (from rate in (RatesArray.GetRange(RatesArray.Count - 3, 3)
                    .TakeWhile(_ => canTrade)
                    .Reverse().SkipWhile(r => r.PriceAvg2 == 0).Take(1)
                     )
                   from level in tradeCorridorOk(rate).OrderBy(ls => ls.Average().Abs(CurrentPrice.Average)).Take(1)
                   where level != null
                   select level
                  )
                  .Where(level => !Trades.Any()
                    || level.Average().Abs(_buySellLevels.Average(sr => sr.Rate)) > _buyLevel.Rate.Abs(_sellLevel.Rate) + level.Height())
                  .ForEach(level => {
                    BuyLevel.RateEx = level[0];
                    SellLevel.RateEx = level[1];
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                  });
                  hotValue.Caster(any)._.OnNext(CorridorStats.StartDate);
                }
                adjustExitLevels0();
                break;
              }
            #endregion
            #region BigGap2
            case TrailingWaveMethod.BigGap2: {
                #region firstTime
                var hotValue = new { _ = new Subject<DateTime>() };
                if (firstTime) {
                  any = hotValue;
                  hotValue.Caster(any)._
                    .DistinctUntilChanged()
                    .Window(2)
                    .Subscribe(w => {
                      w.LastAsync().Zip(w.FirstAsync(), (dl, df) => dl > df)
                        .Where(off => off && !CurrentPrice.Average.Between(SellLevel.Rate, BuyLevel.Rate))
                        .Subscribe(_ => {
                          _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                        });
                    });
                  Log = new Exception(new { TradingAngleRange, WaveStDevRatio } + "");
                  onCloseTradeLocal = t => {
                    if (t.PL >= TakeProfitPips / 2) {
                      _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                      if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                    }
                  };
                  {
                    if (ScanCorridorBy != ScanCorridorFunction.BigGap2) {
                      ScanCorridorBy = ScanCorridorFunction.BigGap2;
                      Log = new Exception(new { ScanCorridorBy, ScanCorridor = "changed" } + "");
                    }
                  }
                  {
                    Func<Trade, IEnumerable<Tuple<SuppRes, double>>> ootl_ = (trade) =>
                      (from offset in (trade.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (CurrentPrice.Bid - SellLevel.Rate)).Yield()
                       from sr in new[] { BuyLevel, SellLevel }
                       select Tuple.Create(sr, offset));
                    Action<Trade> ootl = null;
                    ootl = trade => {
                      ootl_(trade).ForEach(tpl => tpl.Item1.Rate += tpl.Item2);
                      onOpenTradeLocal -= ootl;
                    };
                    onCanTradeLocal = canTrade => {
                      if (!canTrade) {
                        if (!onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                          onOpenTradeLocal += ootl;
                        }
                      } else if (onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                        onOpenTradeLocal -= ootl;
                      }
                      return true;
                    };
                  }
                }
                #endregion
                {
                  var angleOk = calcAngleOk();
                  Func<Rate, double> priceRange = rate => rate.PriceAvg2 - rate.PriceAvg3;
                  var anyLevels = new { up = new double[0], down = new double[0] };
                  var levels = anyLevels.Caster((Rate rate) => {
                    var pr = priceRange(rate);
                    Func<double, double[]> priceLevels = middle => new[] { middle + pr / 2, middle - pr / 2 };
                    var date = CorridorStats.StartDate;
                    var ratesMinMax = new { min = CorridorStats.RatesMin, max = CorridorStats.RatesMax };
                    return new {
                      up = new[] { rate.PriceAvg2, rate.PriceAvg3 },//priceLevels(ratesMinMax.max),
                      down = new[] { rate.PriceAvg2, rate.PriceAvg3 }// priceLevels(ratesMinMax.min)
                    };
                  });
                  var lengthOk = CorridorStats.Rates.Count > CorridorDistance * WaveStDevRatio;
                  var canTrade = angleOk && lengthOk && isTradingHourLocal();
                  Func<Rate, IEnumerable<double[]>> tradeCorridorOk = rate =>
                    from ls in levels(rate).Yield().SelectMany(l => new[] { l.up, l.down })
                    from te in new[] { GetTradeEnterBy(true), GetTradeEnterBy(false) }
                    where te(rate).Between(ls[1], ls[0])
                    select ls;
                  //new[] { GetTradeEnterBy(true), GetTradeEnterBy(false) }.All(p => p(RateLast).Between(rate.PriceAvg3, rate.PriceAvg2));
                  (from rate in (RatesArray.GetRange(RatesArray.Count - 3, 3)
                    .TakeWhile(_ => canTrade)
                    .Reverse().SkipWhile(r => r.PriceAvg2 == 0).Take(1)
                     )
                   from level in tradeCorridorOk(rate).OrderBy(ls => ls.Average().Abs(CurrentPrice.Average)).Take(1)
                   where level != null
                   select level
                  )
                  .Where(level => !Trades.Any()
                    || level.Average().Abs(_buySellLevels.Average(sr => sr.Rate)) > _buyLevel.Rate.Abs(_sellLevel.Rate) + level.Height())
                  .ForEach(level => {
                    BuyLevel.RateEx = level[0];
                    SellLevel.RateEx = level[1];
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                  });
                  hotValue.Caster(any)._.OnNext(CorridorStats.StartDate);
                }
                adjustExitLevels0();
                break;
              }
            #endregion
            #region BigGap21
            case TrailingWaveMethod.BigGap21: {
                #region firstTime
                var hotValue = new { _ = new Subject<Func<bool, DateTime>>() };
                Action setTradeRange = () => { if (Debugger.IsAttached)Debugger.Break(); else Debugger.Launch(); };
                if (firstTime) {
                  any = hotValue;
                  hotValue.Caster(any)._
                    .DistinctUntilChanged(a => a(false))
                    .Subscribe(a => a(true));
                  Log = new Exception(new { TradingAngleRange, WaveStDevRatio, CorrelationMinimum } + "");
                  onCloseTradeLocal = t => {
                    if (t.PL >= TakeProfitPips / 2) {
                      _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = CorridorCrossesMaximum; });
                      if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                    }
                  };
                  {
                    if (ScanCorridorBy != ScanCorridorFunction.BigGap2) {
                      ScanCorridorBy = ScanCorridorFunction.BigGap2;
                      Log = new Exception(new { ScanCorridorBy, ScanCorridor = "changed" } + "");
                    }
                  }
                  {
                    Func<Trade, IEnumerable<Tuple<SuppRes, double>>> ootl_ = (trade) =>
                      (from offset in (trade.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (CurrentPrice.Bid - SellLevel.Rate)).Yield()
                       from sr in new[] { BuyLevel, SellLevel }
                       select Tuple.Create(sr, offset));
                    Action<Trade> ootl = null;
                    ootl = trade => {
                      ootl_(trade).ForEach(tpl => tpl.Item1.Rate += tpl.Item2);
                      onOpenTradeLocal -= ootl;
                    };
                    onCanTradeLocal = canTrade => {
                      if (!canTrade) {
                        if (!onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                          onOpenTradeLocal += ootl;
                        }
                      } else if (onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                        onOpenTradeLocal -= ootl;
                      }
                      return true;
                    };
                  }
                }
                #endregion
                {
                  var angleOk = calcAngleOk();
                  Func<Rate, double> priceRange = rate => rate.PriceAvg2 - rate.PriceAvg3;
                  var anyLevels = new { up = new double[0], down = new double[0] };
                  var getLevels = anyLevels.Caster((Rate rate) => {
                    var pr = priceRange(rate);
                    Func<double, double[]> priceLevels = middle => new[] { middle + pr / 2, middle - pr / 2 };
                    var date = CorridorStats.StartDate;
                    var ratesMinMax = new { min = CorridorStats.RatesMin, max = CorridorStats.RatesMax };
                    return new {
                      up = new[] { rate.PriceAvg2, rate.PriceAvg3 },//priceLevels(ratesMinMax.max),
                      down = new[] { rate.PriceAvg2, rate.PriceAvg3 }// priceLevels(ratesMinMax.min)
                    };
                  });
                  var rateLast = RatesArray.GetRange(RatesArray.Count - 3, 3).AsEnumerable().Reverse().SkipWhile(r => r.PriceAvg2 == 0).First();
                  var lengthOk = CorridorStats.Rates.Count > CorridorDistance * WaveStDevRatio;
                  Func<Rate, IEnumerable<double[]>> tradeCorridorOk = rate =>
                    from ls in getLevels(rate).Yield().SelectMany(l => new[] { l.up, l.down })
                    from te in new[] { GetTradeEnterBy(true), GetTradeEnterBy(false) }
                    where te(rate).Between(ls[1], ls[0])
                    select ls;
                  var levels = tradeCorridorOk(rateLast).OrderBy(ls => ls.Average().Abs(CurrentPrice.Average)).Take(1);
                  var levelsOk = levels.Where(level => !Trades.Any()
                      || level.Average().Abs(_buySellLevels.Average(sr => sr.Rate)) > _buyLevel.Rate.Abs(_sellLevel.Rate) + level.Height()
                      );
                  var canTrade = angleOk && lengthOk && isTradingHourLocal() && levelsOk.Any();
                  if (canTrade) {
                    hotValue.Caster(any)._.OnNext((doRun) => {
                      if (doRun) levels
                        .ForEach(level => {
                          BuyLevel.RateEx = level[0];
                          SellLevel.RateEx = level[1];
                          _buySellLevelsForEach(sr => { sr.CanTradeEx = true; sr.ResetPricePosition(); });
                        });
                      return CorridorStats.StartDate;
                    });
                  }
                }
                adjustExitLevels0();
                break;
              }
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
                var levels = CorridorStats.Rates.Select(_priceAvg).ToArray().CrossedLevelsWithGap(InPoints(1));
                Func<Func<double, bool>, double> getLevel = f => levels.Where(t => f(t.Item1)).OrderByDescending(t => t.Item2).Select(t => t.Item1).DefaultIfEmpty(double.NaN).First();
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
            default: throw new Exception(TrailingDistanceFunction + " is not supported.");
          }
          if (firstTime) {
            firstTime = false;
            ResetBarsCountCalc();
            ForEachSuppRes(sr => sr.ResetPricePosition());
            LogTrades = !IsInVitualTrading;
          }
        };
        #endregion
        #endregion

        
        
        #region On Trade Close
        _strategyExecuteOnTradeClose = t => {
          if (!Trades.Any() && isCurrentGrossOk()) {
            ForEachSuppRes(sr => sr.CanTrade = sr.InManual = false);
            DistanceIterationsRealClear();
            if (!IsAutoStrategy)
              IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }

          if (isCurrentGrossOk() || exitCrossed) setCloseLevels(true);
          if (onCloseTradeLocal != null)
            onCloseTradeLocal(t);
          if (TurnOffOnProfit && t.PL >= PriceSpreadAverageInPips) {
            Strategy = Strategy & ~Strategies.Auto;
          }
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
              _buyLevel.SetPrice(enter(true));
              _sellLevel.SetPrice(enter(false));
              BuyCloseLevel.SetPrice(CurrentExitPrice(true));
              SellCloseLevel.SetPrice(CurrentExitPrice(false));
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

    private static void BroadcastCloseAllTrades() {
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CloseAllTradesMessage>(null);
    }
    class WF {
      public delegate void OnExit();
      public delegate bool MustExit();
      public delegate DateTime StartDate();
      public class DateStart : Lib.Box<DateTime> {
        public DateStart(DateTime value,EventHandler<DateTime>eventHandler) : base(value,eventHandler) { }
        public DateStart(DateTime value) : base(value) { }
      }
    }
    #region WorkflowStep
    private string _WorkflowStep;
    public string WorkflowStep {
      get { return _WorkflowStep; }
      set {
        if (_WorkflowStep != value) {
          _WorkflowStep = value;
          OnPropertyChanged("WorkflowStep");
        }
      }
    }

    #endregion
    #region HarmonicMinuteMin
    private int _HarmonicMinuteMin;
    [Category(categoryActive)]
    [DisplayName("HarmonicMinuteMin")]
    public int HarmonicMinuteMin {
      get { return _HarmonicMinuteMin; }
      set {
        if (_HarmonicMinuteMin != value) {
          _HarmonicMinuteMin = value;
          OnPropertyChanged("HarmonicMinuteMin");
        }
      }
    }

    #endregion
    #region Timeframe2CorridorLengthRatio
    private int _Timeframe2CorridorLengthRatio = 2;
    [Category(categoryActive)]
    [DisplayName("TF Ratio")]
    [Description("OK = CorridorRatesCount > CorridorDistance * Timeframe2CorridorLengthRatio")]
    public int Timeframe2CorridorLengthRatio {
      get { return _Timeframe2CorridorLengthRatio; }
      set {
        if (_Timeframe2CorridorLengthRatio != value) {
          _Timeframe2CorridorLengthRatio = value;
          OnPropertyChanged("Timeframe2CorridorLengthRatio");
        }
      }
    }

    #endregion
    private void CloseTrading(string reason) {
      Log = new Exception("Closing Trading:" + reason);
      TradingStatistics.TradingMacros.ForEach(tm => {
        TradesManager.ClosePair(tm.Pair);
        tm.SuppRes.ForEach(sr1 => sr1.CanTrade = false);
      });
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
      var maxIntervals = new { span = TimeSpan.Zero, level = double.NaN }.IEnumerable().ToList();
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
          for (var node = ratesCrossesLinked.First.Next; node != null; node = node.Next) {
            var interval = node.Previous.Value.StartDate - node.Value.StartDate;
            if (interval > span) {
              while (!maxIntervals.Any() && node != null && (rates[0].StartDate - node.Value.StartDate).Duration().TotalMinutes < lengthMin) {
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
      var halfInPips = InPips(half).ToInt() * 0;
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
      var halfInPips = 0 * InPips(half).ToInt();
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
        .Select(k => new { k.Key.level, k.Key.stDev, k.Key.count }).ToList();
      levelsGrouped.Sort(a => -a.stDev);
      return levelsGrouped.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
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
    double CorridorByDensity(IList<Rate> ratesOriginal, double height) {
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

    IEnumerable<DateTime> _fractalTimes = new DateTime[0];
    public IEnumerable<DateTime> FractalTimes {
      get { return _fractalTimes; }
      set { _fractalTimes = value; }
    }
    public int FttMax { get; set; }

    public bool MustExitOnReverse { get; set; }
  }
}
