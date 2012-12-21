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

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    bool IsTradingHour() { return IsTradingHour(TradesManager.ServerTime); }

    private bool IsEndOfWeek() {
      return TradesManager.ServerTime.DayOfWeek == DayOfWeek.Friday && TradesManager.ServerTime.ToUniversalTime().TimeOfDay > TimeSpan.Parse("2" + (TradesManager.ServerTime.IsDaylightSavingTime() ? "0" : "1") + ":45");
    }

    private void ConvertCloseLevelToOpenLevel(Store.SuppRes buyCloseLevel, Store.SuppRes sellCloseLevel, double[] ratesShort) {
      if ((buyCloseLevel.CanTrade || sellCloseLevel.CanTrade) && ratesShort.Any(min => min.Between(buyCloseLevel.Rate, sellCloseLevel.Rate))) {
        _buyLevel.Rate = sellCloseLevel.Rate;
        if (sellCloseLevel.TradesCount != 9) _buyLevel.TradesCount = sellCloseLevel.TradesCount;
        _sellLevel.Rate = buyCloseLevel.Rate;
        if (buyCloseLevel.TradesCount != 9) _sellLevel.TradesCount = buyCloseLevel.TradesCount;
        _buyLevel.CanTrade = sellCloseLevel.CanTrade;
        _sellLevel.CanTrade = buyCloseLevel.CanTrade;
        sellCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      }
    }

    private IList<double> TrailBuySellCorridor(Store.SuppRes buyCloseLevel, Store.SuppRes sellCloseLevel) {
      var ratesShort = RatesArray.TakeEx(-5).Select(r => r.PriceAvg).OrderBy(d => d).ToArray();
      var ratesWave = RatesArray.TakeEx(-WaveLength * 2).ToArray();
      if (ratesShort.Any(min => min.Between(_sellLevel.Rate, _buyLevel.Rate))) {
        var newBuy = -_buyLevel.Rate + _buyLevel.Rate.Max(RatePrev.PriceAvg);
        var newSell = _sellLevel.Rate - _sellLevel.Rate.Min(RatePrev.PriceAvg);
        var point = InPoints(.1);
        if (newBuy > point)
          newSell = -newBuy;
        if (newSell > point)
          newBuy = -newSell;
        if (IsAutoStrategy) {
          var offset = ratesWave.Height();
          if (newBuy > point) {
            _buyLevel.Rate = _buyLevel.Rate + newBuy;
            _sellLevel.Rate = _buyLevel.Rate - offset;
          }
          if (newSell > point) {
            _sellLevel.Rate = _sellLevel.Rate - newSell;
            _buyLevel.Rate = _sellLevel.Rate + offset;
          }
        } else {
          _buyLevel.Rate = _buyLevel.Rate + newBuy;
          _sellLevel.Rate = _sellLevel.Rate - newSell;
        }
        sellCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      }
      ConvertCloseLevelToOpenLevel(buyCloseLevel, sellCloseLevel, ratesShort);
      return ratesWave.Select(r=>r.PriceAvg).ToArray();
    }

    private void TrailBuySellCorridorByWaveDistanceAndHeight(IList<Rate> wave) {
      var mid = wave.Average(r => r.PriceAvg);
      var offset = wave.Height() / 2;
      _buyLevel.Rate = mid + offset;
      _sellLevel.Rate = mid - offset;
    }

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
      if (IsInVitualTrading) a(0);
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
        if (value == DateTime.MinValue) {
          _CorridorStopDate = value;
        } else {
          value = value.Min(RateLast.StartDate).Max(CorridorStats.StartDate.Add((BarPeriodInt * 2).FromMinutes()));
          if (_CorridorStopDate == value) return;
          _CorridorStopDate = value;
          if (value == DateTime.MinValue)
            CorridorStats.StopRate = null;
          else {
            var index = RatesArray.IndexOf(new Rate() { StartDate = value });
            CorridorStats.StopRate = RatesArray.GetRange(index - 5, 10).OrderByDescending(r => r.PriceHigh - r.PriceLow).First();
            _CorridorStopDate = CorridorStats.StopRate.StartDate;
            StartStopDistance = CorridorStats.Rates.LastBC().Distance - CorridorStats.StopRate.Distance;
          }
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


    private void StrategyEnterDistancerManual() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _buyLevel.CanTrade = _sellLevel.CanTrade = IsAutoStrategy;
        _strategyExecuteOnTradeClose = t => {
          ResetSuppResesInManual(false);
          if (isCurrentGrossOk()) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
            IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }
          CloseAtZero = _trimAtZero = false;
        };
        this.TradesManager.TradeAdded += (s, e) => {
          if (_buyLevel.Rate <= _sellLevel.Rate)
            if (e.Trade.IsBuy) _sellLevel.Rate = WaveTradeStart.Rates.PriceMin(60) - WaveTradeStart.RatesStDev;
            else _buyLevel.Rate = WaveTradeStart.Rates.PriceMax(60) + WaveTradeStart.RatesStDev;
          if (false && _buyLevel.TradesCount.Min(_sellLevel.TradesCount) >= 0) {
            var wave = _waveMiddle;
            _buyLevel.Rate = wave.Max(r => r.PriceAvg);
            _sellLevel.Rate = wave.Min(r => r.PriceAvg);
          }
        };

        return;
      }
      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      if (!CorridorStats.Rates.Any()) return;
      #endregion

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1();
      var sellCloseLevel = Resistance1();
      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      double als = AllowedLotSizeCore();
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (!IsAutoStrategy && IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (Trades.Lots() / als > ProfitToLossExitRatio)
        _trimAtZero = true;
      #endregion

      #region Run

      if (_buyLevel.Rate < _sellLevel.Rate) {
        if (_buyLevel.Rate > RateLast.PriceAvg)
          _buyLevel.Rate = RateLast.PriceAvg + PointSize;
        if (_sellLevel.Rate < RateLast.PriceAvg)
          _sellLevel.Rate = RateLast.PriceAvg - PointSize;
      }

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        if (!Trades.Any()) {
          buyCloseLevel.Rate = _RatesMin - RatesHeight;
          sellCloseLevel.Rate = _RatesMax + RatesHeight;
        } else {
          var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
          var priceAvgMax = RateLast.PriceAvg.Max(RatePrev.PriceAvg) - PointSize;
          var priceAvgMin = RateLast.PriceAvg.Min(RatePrev.PriceAvg) + PointSize;
          if (buyCloseLevel.InManual) {
            if (buyCloseLevel.Rate <= priceAvgMax)
              buyCloseLevel.Rate = priceAvgMax;
          } else
            buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? priceAvgMax : double.NaN);
          if (sellCloseLevel.InManual) {
            if (sellCloseLevel.Rate >= priceAvgMin)
              sellCloseLevel.Rate = priceAvgMin;
          } else
            sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? priceAvgMin : double.NaN);
        }
      }
      #endregion
    }

    public void OpenTrade(bool isBuy, int lot) {
      CheckPendingAction("OT", (pa) => {
        if (lot > 0) {
          pa();
          TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
        }
      });
    }
    public delegate Rate[] SetTrendLinesDelegate();
    Rate[] _setTrendLineDefault() { return RatesArray.ToArray(); }
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
    private void StrategyEnterEnder() {
      if (!RatesArray.Any()) return;

      #region Local globals
      #region Levels
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
      Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
      var buyCloseLevel = Support1();
      var sellCloseLevel = Resistance1();
      #endregion
      #region Loss/Gross
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      #endregion
      #endregion

      #region Init
      #region SuppRes Event Handlers
      Func<SuppRes, bool> suppResCanTrade = (sr) => sr.CanTrade && sr.TradesCount <= 0 && !HasTradesByDistance(sr.IsBuy);
      Action<SuppRes, bool> enterCrossHandler = (suppRes, isBuy) => {
        if (!IsTradingActive) return;
        var lot = Trades.IsBuy(!isBuy).Lots();
        if (suppResCanTrade(suppRes))
          lot += AllowedLotSizeCore();
        OpenTrade(isBuy, lot);
      };
      Action exitCrossHandler = () => {
        if (!IsTradingActive) return;
        var lot = Trades.Lots() - (_trimAtZero ? AllowedLotSizeCore() : 0);
        _trimAtZero = false;
        CloseTrades(lot);
      };
      #endregion
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _buyLevelRate = _sellLevelRate = double.NaN;
        _isSelfStrategy = true;
        ForEachSuppRes(sr => sr.InManual = false);
        ForEachSuppRes(sr => sr.ResetPricePosition());
        ForEachSuppRes(sr => sr.ClearCrossedHandlers());

        var turnOffOnProfit = TrailingDistanceFunction == TrailingWaveMethod.WaveRight;
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        #region Buy/Sell Crossed handlers
        _buyLevel.Crossed += (s, e) => {
          if (e.Direction == -1) return;
          if (_sellLevel.CanTrade || _buyLevel.CanTrade)
            _sellLevel.TradesCount = _buyLevel.TradesCount - 1;
          enterCrossHandler((SuppRes)s, true);
        };
        _sellLevel.Crossed += (s, e) => {
          if (e.Direction == 1) return;
          if (_sellLevel.CanTrade || _buyLevel.CanTrade)
            _buyLevel.TradesCount = _sellLevel.TradesCount - 1;
          enterCrossHandler((SuppRes)s, false);
        };
        buyCloseLevel.Crossed += (s, e) => {
          if (e.Direction == -1) exitCrossHandler();
        };
        sellCloseLevel.Crossed += (s, e) => {
          if (e.Direction == 1) exitCrossHandler();
        };
        #endregion
        #region adjustLevels
        var firstTime = true;
        #region Watchers
        var watcherSetCorridor = new ObservableValue<bool>(false, true);
        var watcherCanTrade = new ObservableValue<bool>(true, true);
        var watcherWaveTrade = new ObservableValue<bool>(false, true);
        var watcherWaveStart = new ObservableValue<DateTime>(DateTime.MinValue);
        var watcherTradeCounter = new ObservableValue<bool>(false, true);
        #endregion
        #region _adjustLevels
        _adjustEnterLevels = () => {
          if (!WaveTradeStart.HasRates) return;
          switch (TrailingDistanceFunction) {
            #region WaveMax
            case TrailingWaveMethod.WaveMax: {
                if (firstTime)
                  _buySellLevelsForEach(sr => sr.CanTrade = false);
                var median = (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                var varaince = CorridorStats.StDevByPriceAvg.Max(CorridorStats.StDevByHeight) * 2;
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                var isSteady = _CenterOfMassBuy < WaveTradeStart.RatesMax && _CenterOfMassSell > WaveTradeStart.RatesMin;
                if (watcherWaveTrade.SetValue(isSteady).HasChangedTo) {
                  _buySellLevelsForEach(sr => sr.CanTrade = watcherWaveTrade.Value);
                  _buyLevel.Rate = WaveTradeStart.RatesMax.Max(RateLast.PriceAvg) + PointSize;
                  _sellLevel.Rate = WaveTradeStart.RatesMin.Min(RateLast.PriceAvg) - PointSize;
                  _buySellLevelsForEach(sr => { sr.CanTrade = true; sr.TradesCount = 0; });
                }
                if (CorridorCrossesMaximum < 0 && watcherTradeCounter.SetValue(_buyLevel.TradesCount.Max(_sellLevel.TradesCount) < CorridorCrossesMaximum).HasChangedTo)
                  NewThreadScheduler.Default.Schedule(() => _buyLevel.CanTrade = _sellLevel.CanTrade = false);
              }
              break;
            #endregion
            #region WaveMax1
            case TrailingWaveMethod.WaveMax1: {
                if (firstTime)
                  _buySellLevelsForEach(sr => sr.CanTrade = false);
                var median = (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                var waveStDev = CorridorStats.StDevByPriceAvg.Max(CorridorStats.StDevByHeight);
                var varaince = waveStDev / MathExtensions.StDevRatioMax / 2;
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                var isSteady = _CenterOfMassBuy < WaveTradeStart.RatesMax && _CenterOfMassSell > WaveTradeStart.RatesMin;
                if (watcherWaveTrade.SetValue(isSteady).HasChangedTo) {
                  _buySellLevelsForEach(sr => sr.CanTrade = watcherWaveTrade.Value);
                  _buyLevel.Rate = WaveTradeStart.RatesMax.Max(RateLast.PriceAvg) + PointSize;
                  _sellLevel.Rate = WaveTradeStart.RatesMin.Min(RateLast.PriceAvg) - PointSize;
                  _buySellLevelsForEach(sr => { sr.CanTrade = true; sr.TradesCount = 0; });
                }
                if (CorridorCrossesMaximum < 0 && watcherTradeCounter.SetValue(_buyLevel.TradesCount.Max(_sellLevel.TradesCount) < CorridorCrossesMaximum).HasChangedTo)
                  NewThreadScheduler.Default.Schedule(() => _buyLevel.CanTrade = _sellLevel.CanTrade = false);
              }
              break;
            #endregion
            #region WaveAuto
            case TrailingWaveMethod.WaveAuto: {
                var waveTradeOk = WaveShort.Rates.Count - WaveTradeStart.Rates.Count > CorridorLength && CorridorLength < CorridorDistanceRatio;
                var waveShortOk = WaveShort.HasRates && CorridorStats.StDevByHeightInPips.ToInt() >= CorridorStats.StDevByPriceAvgInPips.ToInt();
                if (watcherSetCorridor.SetValue(waveTradeOk && waveShortOk).HasChangedTo) {
                  _buyLevel.RateEx = WaveShort.RatesMax;
                  _sellLevel.RateEx = WaveShort.RatesMin;
                }
                if (watcherCanTrade.SetValue(waveTradeOk).HasChanged)
                  _buySellLevelsForEach(sr => { if (!sr.InManual) { sr.CanTrade = watcherCanTrade.Value; sr.TradesCount = 0; } });
              }
              break;
            #endregion
            #region WaveRight
            case TrailingWaveMethod.WaveRight: {
                var waveTradeOk = WaveShort.Rates.Count - WaveTradeStart.Rates.Count > CorridorDistanceRatio;
                var ratesOk = StDevByHeightInPips.ToInt() > StDevByPriceAvgInPips.ToInt();
                //var waveShortOk = WaveShort.HasRates && WaveShort.Rates.Count > 30 && WaveShort.RatesHeight.ToInt() < RatesStDev.ToInt();
                watcherSetCorridor.Value = waveTradeOk && ratesOk;// && waveShortOk;
                if (watcherSetCorridor.HasChangedTo) {
                  _buyLevel.RateEx = WaveShort.RatesMax;
                  _sellLevel.RateEx = WaveShort.RatesMin;
                  _buySellLevelsForEach(sr => { if (!sr.InManual) { sr.CanTrade = true; sr.TradesCount = 0; } });
                }
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
          ForEachSuppRes(sr => sr.InManual = false);
          if (isCurrentGrossOk()) {
            if (!IsAutoStrategy)
              IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum.Max(0);
            _buySellLevelsForEach(sr => sr.CanTrade = false);
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
        return;
      }
        #endregion
      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      if (!CorridorStats.Rates.Any()) return;
      #endregion

      #region Suppres levels
      Action<SuppRes> setCloseLevel = (sr) => {
        if (sr.InManual) return;
        sr.CanTrade = false;
        if (sr.TradesCount != 9) sr.TradesCount = 9;
      };
      setCloseLevel(buyCloseLevel); setCloseLevel(sellCloseLevel);
      #endregion

      #region Exit
      Action exitAction = () => {
        double als = AllowedLotSizeCore();
        if (IsEndOfWeek() || !IsTradingHour()) {
          if (Trades.Any())
            CloseTrades(Trades.Lots());
          _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          return;
        }
        if (Trades.Lots() / als > ProfitToLossExitRatio
            || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
          )
          _trimAtZero = true;
      };
      #endregion

      #region Run
      _adjustEnterLevels();
      _buyLevel.SetPrice(RateLast.PriceAvg);
      _sellLevel.SetPrice(RateLast.PriceAvg);
      exitAction();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        if (!Trades.Any()) {
          buyCloseLevel.RateEx = _RatesMin - RatesHeight;
          buyCloseLevel.ResetPricePosition();
          sellCloseLevel.RateEx = _RatesMax + RatesHeight;
          sellCloseLevel.ResetPricePosition();
        } else {
          //var phH = WaveTradeStart.Rates.Take(5).ToArray().PriceHikes().DefaultIfEmpty(SpreadForCorridor).Max();
          var tpRatioBySpread = 1;// +Math.Log(phH.Ratio(SpreadForCorridor), 1.5);
          var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : (TakeProfitPips / tpRatioBySpread) + (CloseOnProfitOnly ? CurrentLossInPips.Min(0).Abs() : 0)));
          var priceAvgMax = RateLast.PriceAvg.Max(RatePrev.PriceAvg) - PointSize;
          var priceAvgMin = RateLast.PriceAvg.Min(RatePrev.PriceAvg) + PointSize;
          if (buyCloseLevel.InManual) {
            if (buyCloseLevel.Rate <= priceAvgMax)
              buyCloseLevel.Rate = priceAvgMax;
          } else
            buyCloseLevel.RateEx = (_buyLevelNetOpen().Min(_buyLevel.Rate) + tpColse).Max(Trades.HaveBuy() ? priceAvgMax : double.NaN);
          if (sellCloseLevel.InManual) {
            if (sellCloseLevel.Rate >= priceAvgMin)
              sellCloseLevel.Rate = priceAvgMin;
          } else
            sellCloseLevel.RateEx = (_sellLevelNetOpen().Max(_sellLevel.Rate) - tpColse).Min(Trades.HaveSell() ? priceAvgMin : double.NaN);
          try {
            buyCloseLevel.SetPrice(RateLast.PriceAvg);
            sellCloseLevel.SetPrice(RateLast.PriceAvg);
          } catch (ArgumentException exc) {
            Log = exc;
          }
        }
      }
      #endregion
    }

    private void StrategyEnterUniversal_GOOD() {
      if (!RatesArray.Any()) return;

      #region ============ Local globals ============
      #region Loss/Gross
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => CurrentGrossInPips;
      Func<double> currentLoss = () => CurrentLoss;
      Func<bool> isCorridorFrozen = () => LotSizeByLossBuy >= MaxLotSize;
      Func<bool> isProfitOk = () => false;
      #endregion
      ShowTrendLines = false;
      _isSelfStrategy = true;
      Func<bool, double> crossLevelDefault = isBuy => isBuy ? _RatesMax + RatesHeight / 5 : _RatesMin - RatesHeight / 5;
      Action resetCloseAndTrim = () => CloseAtZero = _trimAtZero = _trimToLotSize = false;
      #endregion

      #region ============ Init =================

      if (_strategyExecuteOnTradeClose == null) {
        Action onEOW = () => { };
        #region Exit Funcs
        #region exitWave
        Action exitWave = () => {
          double als = LotSizeByLossBuy;
          if (!SuppRes.Any(sr => sr.InManual)) {
            var st = TradesManager.ServerTime;
            var isFirstWednesday = st.DayOfWeek == DayOfWeek.Wednesday && st.Month > st.AddDays(-7).Month;
            var isFirstThursday = st.DayOfWeek == DayOfWeek.Thursday && st.Month > st.AddDays(-7).Month;
            bool isBadTime = TradesManager.ServerTime.DayOfWeek == DayOfWeek.Sunday || isFirstWednesday;
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            bool isBlackSunday = st.DayOfWeek == DayOfWeek.Sunday && CurrentLoss < 0;
            if (isEOW || isFirstWednesday || isFirstThursday || isBlackSunday) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              return;
            }
          }
          if (Trades.Lots() / als >= ProfitToLossExitRatio
              || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
            )
            _trimAtZero = true;
        };
        #endregion
        #region exitWave1
        Action exitWave1 = () => {
          double als = LotSizeByLossBuy;
          if (!SuppRes.Any(sr => sr.InManual)) {
            var st = TradesManager.ServerTime;
            var isFirstWednesday = st.DayOfWeek == DayOfWeek.Wednesday && st.Month > st.AddDays(-7).Month;
            var isFirstThursday = st.DayOfWeek == DayOfWeek.Thursday && st.Month > st.AddDays(-7).Month;
            bool isBadTime = TradesManager.ServerTime.DayOfWeek == DayOfWeek.Sunday || isFirstWednesday;
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            bool isBlackSunday = st.DayOfWeek == DayOfWeek.Sunday && CurrentLoss < 0;
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              return;
            }
          }
          var exitByLossGross = Trades.Lots() > LotSize && currentLoss() < CurrentGross * ProfitToLossExitRatio && als <= LotSize;
          if (exitByLossGross
              || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
            )
            _trimAtZero = true;
        };
        #endregion
        #region exitWave2
        Action exitWave2 = () => {
          double als = LotSizeByLossBuy;
          if (!SuppRes.Any(sr => sr.InManual)) {
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              resetCloseAndTrim();
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              onEOW();
              return;
            }
          }
          var exitByLossGross = Trades.Lots() > LotSize && currentLoss() < CurrentGross * ProfitToLossExitRatio;
          if (exitByLossGross
              || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
            )
            _trimToLotSize = true;
        };
        #endregion
        #endregion

        bool exitCrossed = false;
        Action<Trade> onCloseTradeLocal = null;

        #region Levels
        SuppResLevelsCount = 2;
        SuppRes.ForEach(sr => sr.IsExitOnly = false);
        var buyCloseLevel = Support1(); buyCloseLevel.IsExitOnly = true;
        var sellCloseLevel = Resistance1(); sellCloseLevel.IsExitOnly = true;
        _buyLevel = Resistance0();
        _sellLevel = Support0();
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
        Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
        Action<SuppRes,bool> setCloseLevel = (sr,overWrite) => {
          if (!overWrite && sr.InManual) return;
          sr.InManual = false;
          sr.CanTrade = false;
          if (sr.TradesCount != 9) sr.TradesCount = 9;
        };
        Action<bool> setCloseLevels = (overWrite) => { setCloseLevel(buyCloseLevel,overWrite); setCloseLevel(sellCloseLevel,overWrite); };
        ForEachSuppRes(sr => sr.InManual = false);
        ForEachSuppRes(sr => sr.ResetPricePosition());
        ForEachSuppRes(sr => sr.ClearCrossedHandlers());
        setCloseLevels(true);
        #region updateTradeCount
        Action<SuppRes, SuppRes> updateTradeCount = (supRes, other) => {
          if (supRes.TradesCount <= other.TradesCount) other.TradesCount = supRes.TradesCount - 1;
        };
        Action<SuppRes> updateNeares = supRes => {
          var other = _suppResesForBulk().Where(sr => sr.IsSupport != supRes.IsSupport).OrderBy(sr => (sr.Rate - supRes.Rate).Abs()).First();
          updateTradeCount(supRes, other);
        };
        #endregion
        Func<SuppRes, bool> suppResCanTrade = (sr) => sr.CanTrade && sr.TradesCount <= 0 && !HasTradesByDistance(sr.IsBuy);
        #endregion

        #region SuppRes Event Handlers
        #region enterCrossHandler
        Action<SuppRes, bool> enterCrossHandler = (suppRes, isBuy) => {
          if (!IsTradingActive) return;

          if (suppRes.TradesCount <= 0) {
            if (suppRes.IsBuy) {
              if (buyCloseLevel.CanTrade) {
                if (suppRes != _sellLevel) {
                  _sellLevel.TradesCount = buyCloseLevel.TradesCount;
                  _sellLevel.Rate = buyCloseLevel.Rate;
                  _sellLevel.ResetPricePosition();
                }
              }
            } else {
              if (sellCloseLevel.CanTrade) {
                if (suppRes != _buyLevel) {
                  _buyLevel.TradesCount = sellCloseLevel.TradesCount;
                  _buyLevel.Rate = sellCloseLevel.Rate;
                  _buyLevel.ResetPricePosition();
                }
              }
            }
            setCloseLevels(true);
          }

          var lot = Trades.IsBuy(!isBuy).Lots();
          if (suppResCanTrade(suppRes))
            lot += AllowedLotSizeCore();
          OpenTrade(isBuy, lot);
        };
        #endregion
        #endregion

        #region Buy/Sell Crossed handlers

        #region exitCrossHandler
        Action<SuppRes> exitCrossHandler = (sr) => {
          if (!IsTradingActive) return;
          if (sr.CanTrade) {
            updateNeares(sr);
            if (sr.TradesCount > 0) return;
            if (sr.IsSell) {
              _sellLevel.Rate = sr.Rate;
              sr.ResetPricePosition();
              sr.Rate = crossLevelDefault(true);
              enterCrossHandler(_sellLevel, false);
              sr.ResetPricePosition();
            } else {
              _buyLevel.Rate = sr.Rate;
              sr.ResetPricePosition();
              sr.Rate = crossLevelDefault(false);
              sr.ResetPricePosition();
              enterCrossHandler(_buyLevel, false);
            }
            setCloseLevels(true);
          } else {
            exitCrossed = true;
            var lot = Trades.Lots() - (_trimToLotSize ? LotSize.Max(Trades.Lots() / 2) : _trimAtZero ? AllowedLotSizeCore() : 0);
            resetCloseAndTrim();
            CloseTrades(lot);
          }
        };
        #endregion

        #region Crossed Events
        _buyLevel.Crossed += (s, e) => {
          if (e.Direction == -1) return;
          if (_buySellLevels.Any(sr => sr.CanTrade))
            updateNeares((SuppRes)s);
          enterCrossHandler((SuppRes)s, true);
        };
        _sellLevel.Crossed += (s, e) => {
          if (e.Direction == 1) return;
          if (_buySellLevels.Any(sr => sr.CanTrade))
            updateNeares((SuppRes)s);
          enterCrossHandler((SuppRes)s, false);
        };
        buyCloseLevel.Crossed += (s, e) => {
          if (e.Direction == -1) {
            exitCrossHandler((SuppRes)s);
          }
        };
        sellCloseLevel.Crossed += (s, e) => {
          if (e.Direction == 1) {
            exitCrossHandler((SuppRes)s);
          }
        };
        #endregion
        #endregion

        #region adjustLevels
        var firstTime = true;
        #region Watchers
        var watcherSetCorridor = new ObservableValue<bool>(false, true);
        var watcherCanTrade = new ObservableValue<bool>(true, true);
        var watcherWaveTrade = new ObservableValue<bool>(false, true);
        var watcherWaveStart = new ObservableValue<DateTime>(DateTime.MinValue);
        var watcherTradeCounter = new ObservableValue<bool>(false, true);
        var waveTradeOverTrigger = new ValueTrigger(false);
        var corridorMoved = new ValueTrigger(false);
        double corridorLevel = 0;
        Func<Rate, double> cp;
        Func<double> calcVariance = null;
        Func<double> calcMedial = null;
        WaveInfo waveToTrade = null;
        #endregion

        #region _adjustExitLevels
        Action<double,double> adjustExitLevels = (buyLevel, sellLevel) => {
          if (buyLevel.Min(sellLevel) < .5)
            Debugger.Break();
          if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
            if (!Trades.Any()) {
              buyCloseLevel.RateEx = crossLevelDefault(true);
              buyCloseLevel.ResetPricePosition();
              sellCloseLevel.RateEx = crossLevelDefault(false);
              sellCloseLevel.ResetPricePosition();
            } else {
              //var phH = WaveTradeStart.Rates.Take(5).ToArray().PriceHikes().DefaultIfEmpty(SpreadForCorridor).Max();
              var tpRatioBySpread = 1;// +Math.Log(phH.Ratio(SpreadForCorridor), 1.5);
              var tpColse = InPoints((CloseAtZero || _trimAtZero || _trimToLotSize || isProfitOk() ? 0 : (TakeProfitPips / tpRatioBySpread) + (CloseOnProfitOnly ? CurrentLossInPips.Min(0).Abs() : 0)));
              var priceAvgMax = WaveShort.Rates.Take(PriceCmaPeriod.Max(5)).Max(r => CorridorPrice(r)) - PointSize / 100;
              var priceAvgMin = WaveShort.Rates.Take(PriceCmaPeriod.Max(5)).Min(r => CorridorPrice(r)) + PointSize / 100;
              if (buyCloseLevel.InManual) {
                if (buyCloseLevel.Rate <= priceAvgMax)
                  buyCloseLevel.Rate = priceAvgMax;
              } else
                buyCloseLevel.RateEx = (buyLevel.Min(_buyLevelNetOpen()) + tpColse).Max(Trades.HaveBuy() ? priceAvgMax : double.NaN);
              if (sellCloseLevel.InManual) {
                if (sellCloseLevel.Rate >= priceAvgMin)
                  sellCloseLevel.Rate = priceAvgMin;
              } else
                sellCloseLevel.RateEx = (sellLevel.Max(_sellLevelNetOpen()) - tpColse).Min(Trades.HaveSell() ? priceAvgMin : double.NaN);
            }
          }
          try {
            buyCloseLevel.SetPrice(CorridorPrice(RateLast));
            sellCloseLevel.SetPrice(CorridorPrice(RateLast));
          } catch (Exception exc) {
            Log = exc;
          }
        };
        #endregion

        #region _adjustLevels
        _adjustEnterLevels = () => {
          if (!WaveTradeStart.HasRates) return;
          setCloseLevels(false);
          switch (TrailingDistanceFunction) {
            #region WaveMax
            case TrailingWaveMethod.WaveMax: {
                if (firstTime) {
                  _buySellLevelsForEach(sr => sr.CanTrade = false);
                  _exitTrade = exitWave;
                  calcMedial = () => (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                  calcVariance = () => CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
                }
                cp = CorridorPrice();
                var median = (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                var varaince = CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                var isSteady = _CenterOfMassBuy < WaveTradeStart.RatesMax && _CenterOfMassSell > WaveTradeStart.RatesMin;
                if (watcherWaveTrade.SetValue(isSteady).HasChangedTo) {
                  _buyLevel.RateEx = WaveTradeStart.RatesMax.Max(cp(RateLast)) + PointSize;
                  _sellLevel.RateEx = WaveTradeStart.RatesMin.Min(cp(RateLast)) - PointSize;
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = watcherWaveTrade.Value; sr.TradesCountEx = 0; });
                }
                var isNotSteady = _CenterOfMassBuy > WaveShort.RatesMax && _CenterOfMassSell < WaveShort.RatesMin;
                if (CorridorCrossesMaximum < 0 && watcherTradeCounter.SetValue(_buyLevel.TradesCount.Max(_sellLevel.TradesCount) < CorridorCrossesMaximum).HasChangedTo)
                  NewThreadScheduler.Default.Schedule(() => _buyLevel.CanTrade = _sellLevel.CanTrade = false);
              }
              adjustExitLevels(_buyLevel.Rate, _sellLevel.Rate);
              break;
            #endregion
            #region WaveMax1
            case TrailingWaveMethod.WaveMax1:
              #region firstTime
              if (firstTime) {
                _buySellLevelsForEach(sr => sr.CanTrade = false);
                _exitTrade = exitWave;
                waveToTrade = WaveTradeStart;
                calcMedial = () => (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                calcVariance = () => CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
                onEOW += () => corridorLevel = 0;
                onEOW += () => corridorMoved.Off();
              }
              #endregion
              goto case TrailingWaveMethod.WaveCommon;
            #endregion
            #region WaveCommon
            case TrailingWaveMethod.WaveCommon: {
                if (firstTime) {
                }
                cp = CorridorPrice();
                var median = calcMedial();
                var varaince = calcVariance();
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                var isSteady = _CenterOfMassBuy < WaveTradeStart.RatesMax && _CenterOfMassSell > WaveTradeStart.RatesMin;
                if (watcherWaveTrade.SetValue(isSteady).HasChangedTo 
                  && corridorMoved.Set((corridorLevel - median).Abs() > StDevByPriceAvg, () => corridorLevel = median).On) {
                  corridorMoved.Off();
                  _buyLevel.RateEx = waveToTrade.RatesMax.Max(cp(RateLast)) + PointSize;
                  _sellLevel.RateEx = waveToTrade.RatesMin.Min(cp(RateLast)) - PointSize;
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = watcherWaveTrade.Value; sr.TradesCountEx = 0; });
                }
              }
              adjustExitLevels(_buyLevel.Rate, _sellLevel.Rate);
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
          if (isCurrentGrossOk()) {
            exitCrossed = false;
            ForEachSuppRes(sr => sr.InManual = false);
            DistanceIterationsRealClear();
            if (!IsAutoStrategy)
              IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum.Max(0);
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

        _strategyExecuteOnTradeOpen = () => { };
      }

      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion

      #region ============ Run =============
      _adjustEnterLevels();
      _buyLevel.SetPrice(CorridorPrice(RateLast));
      _sellLevel.SetPrice(CorridorPrice(RateLast));
      _exitTrade();
      #endregion
    }

    private void StrategyEnterUniversal() {
      if (!RatesArray.Any()) return;

      #region ============ Local globals ============
      #region Loss/Gross
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => CurrentGrossInPips;
      Func<double> currentLoss = () => CurrentLoss;
      Func<bool> isCorridorFrozen = () => LotSizeByLossBuy >= MaxLotSize;
      Func<bool> isProfitOk = () => false;
      #endregion
      ShowTrendLines = false;
      _isSelfStrategy = true;
      Func<bool, double> crossLevelDefault = isBuy => isBuy ? _RatesMax + RatesHeight / 10 : _RatesMin - RatesHeight / 10;
      Action resetCloseAndTrim = () => CloseAtZero = _trimAtZero = _trimToLotSize = false;
      #endregion

      #region ============ Init =================

      if (_strategyExecuteOnTradeClose == null) {
        Action onEOW = () => { };
        #region Exit Funcs
        Func<bool> exitByLossGross = () => Trades.Lots() >= LotSize * ProfitToLossExitRatio && currentLoss() < CurrentGross * ProfitToLossExitRatio;// && LotSizeByLossBuy <= LotSize;
        #region exitWave0
        Action exitWave0 = () => {
          double als = LotSizeByLossBuy;
          if (!SuppRes.Any(sr => sr.InManual)) {
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              return;
            }
          }
          if (exitByLossGross())
            CloseAtZero = true;
          else if (Trades.Lots() / als >= ProfitToLossExitRatio
                     || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
                   )
            _trimAtZero = true;
        };
        #endregion
        #region exitWave
        Action exitWave = () => {
          double als = LotSizeByLossBuy;
          if (!SuppRes.Any(sr => sr.InManual)) {
            var st = TradesManager.ServerTime;
            var isFirstWednesday = st.DayOfWeek == DayOfWeek.Wednesday && st.Month > st.AddDays(-7).Month;
            var isFirstThursday = st.DayOfWeek == DayOfWeek.Thursday && st.Month > st.AddDays(-7).Month;
            bool isBadTime = TradesManager.ServerTime.DayOfWeek == DayOfWeek.Sunday || isFirstWednesday;
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            bool isBlackSunday = st.DayOfWeek == DayOfWeek.Sunday && CurrentLoss < 0;
            if (isEOW || isFirstWednesday || isFirstThursday || isBlackSunday) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              return;
            }
          }
          if (Trades.Lots() / als >= ProfitToLossExitRatio
              || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
            )
            _trimAtZero = true;
        };
        #endregion
        #region exitWave1
        Action exitWave1 = () => {
          double als = LotSizeByLossBuy;
          if (!SuppRes.Any(sr => sr.InManual)) {
            var st = TradesManager.ServerTime;
            var isFirstWednesday = st.DayOfWeek == DayOfWeek.Wednesday && st.Month > st.AddDays(-7).Month;
            var isFirstThursday = st.DayOfWeek == DayOfWeek.Thursday && st.Month > st.AddDays(-7).Month;
            bool isBadTime = TradesManager.ServerTime.DayOfWeek == DayOfWeek.Sunday || isFirstWednesday;
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            bool isBlackSunday = st.DayOfWeek == DayOfWeek.Sunday && CurrentLoss < 0;
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              return;
            }
          }
          if (exitByLossGross()
              || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
            )
            _trimAtZero = true;
        };
        #endregion
        #region exitWave2
        Action exitWave2 = () => {
          double als = LotSizeByLossBuy;
          if (!SuppRes.Any(sr => sr.InManual)) {
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              resetCloseAndTrim();
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              onEOW();
              return;
            }
          }
          if (exitByLossGross()
              || !CloseOnProfitOnly && CurrentGross > 0 && als == LotSize && Trades.Lots() > LotSize
            )
            _trimToLotSize = true;
        };
        #endregion
        Func<Action> exitFunc = () => {
          switch (ExitFunction) {
            case Store.ExitFunction.Exit0: return exitWave0;
            case Store.ExitFunction.Exit: return exitWave;
            case Store.ExitFunction.Exit1: return exitWave1;
            case Store.ExitFunction.Exit2: return exitWave2;
          }
          throw new NotSupportedException(ExitFunction + " exit function is not supported.");
        };
        #endregion

        if (_adjustEnterLevels != null)
          _adjustEnterLevels.GetInvocationList().Cast<Action>().ForEach(d => _adjustEnterLevels -= d);
        bool exitCrossed = false;
        Action<Trade> onCloseTradeLocal = null;

        #region Levels
        SuppResLevelsCount = 2;
        SuppRes.ForEach(sr => sr.IsExitOnly = false);
        var buyCloseLevel = Support1(); buyCloseLevel.IsExitOnly = true;
        var sellCloseLevel = Resistance1(); sellCloseLevel.IsExitOnly = true;
        _buyLevel = Resistance0();
        _sellLevel = Support0();
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
        Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
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
        Func<SuppRes, bool> suppResCanTrade = (sr) => sr.CanTrade && sr.TradesCount <= 0 && !HasTradesByDistance(sr.IsBuy);
        #endregion

        #region SuppRes Event Handlers
        #region enterCrossHandler
        Func<SuppRes, bool> enterCrossHandler = (suppRes) => {
          if (!IsTradingActive) return false;
          var isBuy = suppRes.IsBuy;
          var lot = Trades.IsBuy(!isBuy).Lots();
          var canTrade = suppResCanTrade(suppRes);
          if (suppResCanTrade(suppRes))
            lot += AllowedLotSizeCore();
          OpenTrade(isBuy, lot);
          return canTrade;
        };
        #endregion
        #region exitCrossHandler
        Action<SuppRes> exitCrossHandler = (sr) => {
          if (!IsTradingActive) return;
          exitCrossed = true;
          var lot = Trades.Lots() - (_trimToLotSize ? LotSize.Max(Trades.Lots() / 2) : _trimAtZero ? AllowedLotSizeCore() : 0);
          resetCloseAndTrim();
          CloseTrades(lot);
        };
        #endregion
        #endregion

        #region Crossed Events
        #region copySupRess
        Action<SuppRes> copySupRess = (srFrom) => {
          srFrom.ResetPricePosition();
          var srTo = _buySellLevels.Single(sr => sr.IsSupport == srFrom.IsSupport);
          srTo.Rate = srFrom.Rate;
          srTo.ResetPricePosition();
          srTo.TradesCount = srFrom.TradesCount;
        };
        #endregion
        #region Enter Levels
        #region crossedEnter
        EventHandler<SuppRes.CrossedEvetArgs> crossedEnter = (s, e) => {
          var sr = (SuppRes)s;
          if (sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1) return;
          var srNearest = new Lazy<SuppRes>(() => suppResNearest(sr));
          if (sr.CanTrade) updateTradeCount(sr, srNearest.Value);
          if (enterCrossHandler(sr)) {
            if (srNearest.Value.IsExitOnly) copySupRess(srNearest.Value);
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
            copySupRess(srExit);
            setCloseLevels(true);
          }
        };
        EventHandler<SuppRes.CrossedEvetArgs> crossedExit = (s, e) => {
          var sr = (SuppRes)s;
          if (sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1) return;
          if (sr.CanTrade)
            handleActiveExitLevel(sr);
          else
            exitCrossHandler(sr);
        };
        buyCloseLevel.Crossed += crossedExit;
        sellCloseLevel.Crossed += crossedExit;
        #endregion
        #endregion

        #region adjustLevels
        var firstTime = true;
        #region Watchers
        var watcherSetCorridor = new ObservableValue<bool>(false, true);
        var watcherCanTrade = new ObservableValue<bool>(true, true);
        var watcherWaveTrade = new ObservableValue<bool>(false, true);
        var watcherWaveStart = new ObservableValue<DateTime>(DateTime.MinValue);
        var watcherTradeCounter = new ObservableValue<bool>(false, true);
        var waveTradeOverTrigger = new ValueTrigger(false);
        var corridorMoved = new ValueTrigger(false);
        double corridorLevel = 0;
        #endregion
        #region Funcs
        Func<Rate, double> cp = CorridorPrice();
        Func<double> calcVariance = null;
        Func<double> calcMedian = null;
        Func<bool> isSteady = () => _CenterOfMassBuy < WaveTradeStart.RatesMax && _CenterOfMassSell > WaveTradeStart.RatesMin;
        WaveInfo waveToTrade = null;
        Func<Func<double>> medianFunc = () => {
          switch (MedianFunction) {
            case Store.MedianFunction.WaveShort: return () => (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
            case Store.MedianFunction.WaveTrade: return () => (WaveTradeStart.RatesMax + WaveTradeStart.RatesMin) / 2;
          }
          throw new NotSupportedException(MedianFunction + " Median function is not supported.");
        };
        Func<Func<double>> varianceFunc = () => {
          switch (VarianceFunction) {
            case Store.VarainceFunction.Price: return () => CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
            case Store.VarainceFunction.Hight: return () => CorridorStats.StDevByHeight * _waveStDevRatioSqrt / 2;
            case VarainceFunction.Max: return () => CorridorStats.StDevByPriceAvg.Max(CorridorStats.StDevByHeight) * _waveStDevRatioSqrt / 2;
            case VarainceFunction.Min: return () => CorridorStats.StDevByPriceAvg.Min(CorridorStats.StDevByHeight) * _waveStDevRatioSqrt / 2;
            case VarainceFunction.Sum: return () => CorridorStats.StDevByPriceAvg + CorridorStats.StDevByHeight;
          }
          throw new NotSupportedException(VarianceFunction + " Variance function is not supported.");
        };
        #endregion

        #region _adjustExitLevels
        Action<double, double> adjustExitLevels = (buyLevel, sellLevel) => {
          if (buyLevel.Min(sellLevel) < .5)
            Debugger.Break();
          if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
            if (!Trades.Any()) {
              buyCloseLevel.RateEx = crossLevelDefault(true);
              buyCloseLevel.ResetPricePosition();
              sellCloseLevel.RateEx = crossLevelDefault(false);
              sellCloseLevel.ResetPricePosition();
            } else {
              //var phH = WaveTradeStart.Rates.Take(5).ToArray().PriceHikes().DefaultIfEmpty(SpreadForCorridor).Max();
              var tpRatioBySpread = 1;// +Math.Log(phH.Ratio(SpreadForCorridor), 1.5);
              var tpColse = InPoints((CloseAtZero || _trimAtZero || _trimToLotSize || isProfitOk() ? 0 : (TakeProfitPips / tpRatioBySpread) + (CloseOnProfitOnly ? CurrentLossInPips.Min(0).Abs() : 0)));
              var priceAvgMax = WaveShort.Rates.Take(PriceCmaPeriod.Max(5)).Max(r => CorridorPrice(r)) - PointSize / 100;
              var priceAvgMin = WaveShort.Rates.Take(PriceCmaPeriod.Max(5)).Min(r => CorridorPrice(r)) + PointSize / 100;
              if (buyCloseLevel.InManual) {
                if (buyCloseLevel.Rate <= priceAvgMax)
                  buyCloseLevel.Rate = priceAvgMax;
              } else
                buyCloseLevel.RateEx = (buyLevel.Min(_buyLevelNetOpen()) + tpColse).Max(Trades.HaveBuy() ? priceAvgMax : double.NaN);
              if (sellCloseLevel.InManual) {
                if (sellCloseLevel.Rate >= priceAvgMin)
                  sellCloseLevel.Rate = priceAvgMin;
              } else
                sellCloseLevel.RateEx = (sellLevel.Max(_sellLevelNetOpen()) - tpColse).Min(Trades.HaveSell() ? priceAvgMin : double.NaN);
            }
          }
          try {
            buyCloseLevel.SetPrice(CorridorPrice(RateLast));
            sellCloseLevel.SetPrice(CorridorPrice(RateLast));
          } catch (Exception exc) {
            Log = exc;
          }
        };
        Action adjustExitLevels0 = () => adjustExitLevels(double.NaN, double.NaN);
        #endregion

        #region _adjustLevels
        _adjustEnterLevels += () => {
          if (!WaveTradeStart.HasRates) return;
          setCloseLevels(false);
          switch (TrailingDistanceFunction) {
            #region WaveTrade
            case TrailingWaveMethod.WaveTrade: {
                if (firstTime) { }
                var median = medianFunc()();
                var varaince = varianceFunc()();
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                _buyLevel.RateEx = _CenterOfMassBuy;
                _sellLevel.RateEx = _CenterOfMassSell;
                _buySellLevelsForEach(sr => sr.CanTrade = true);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region WaveMax
            case TrailingWaveMethod.WaveMax: {
                if (firstTime) {
                  _buySellLevelsForEach(sr => sr.CanTrade = false);
                  calcMedian = () => (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                  calcVariance = () => CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
                }
                cp = CorridorPrice();
                var median = (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                var varaince = CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                if (watcherWaveTrade.SetValue(isSteady()).HasChangedTo) {
                  _buyLevel.RateEx = WaveTradeStart.RatesMax.Max(cp(RateLast)) + PointSize;
                  _sellLevel.RateEx = WaveTradeStart.RatesMin.Min(cp(RateLast)) - PointSize;
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = watcherWaveTrade.Value; sr.TradesCountEx = 0; });
                }
                var isNotSteady = _CenterOfMassBuy > WaveShort.RatesMax && _CenterOfMassSell < WaveShort.RatesMin;
                if (CorridorCrossesMaximum < 0 && watcherTradeCounter.SetValue(_buyLevel.TradesCount.Max(_sellLevel.TradesCount) < CorridorCrossesMaximum).HasChangedTo)
                  NewThreadScheduler.Default.Schedule(() => _buyLevel.CanTrade = _sellLevel.CanTrade = false);
              }
              adjustExitLevels(_buyLevel.Rate, _sellLevel.Rate);
              break;
            #endregion
            #region WaveMax1
            case TrailingWaveMethod.WaveMax1:
              #region firstTime
              if (firstTime) {
                _buySellLevelsForEach(sr => sr.CanTrade = false);
                waveToTrade = WaveTradeStart;
                calcMedian = () => (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                calcVariance = () => CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
                onEOW += () => corridorLevel = 0;
                onEOW += () => corridorMoved.Off();
              }
              #endregion
              goto case TrailingWaveMethod.WaveCommon;
            #endregion
            #region WaveMax2
            case TrailingWaveMethod.WaveMax2:
              #region firstTime
              if (firstTime) {
                _buySellLevelsForEach(sr => sr.CanTrade = false);
                waveToTrade = WaveTradeStart;
                isSteady = () => WaveTradeStart.Rates.TouchDowns(_CenterOfMassBuy, _CenterOfMassSell, cp).Count() >= 3;
                onEOW += () => corridorLevel = 0;
                onEOW += () => corridorMoved.Off();
              }
              #endregion
              goto case TrailingWaveMethod.WaveCommon;
            #endregion
            #region WaveCommon
            case TrailingWaveMethod.WaveCommon: {
                if (firstTime) {
                }
                cp = CorridorPrice();
                var median = medianFunc()();
                var varaince = varianceFunc()();
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                if (watcherWaveTrade.SetValue(isSteady()).HasChangedTo
                  //&& corridorMoved.Set((corridorLevel - median).Abs() > StDevByPriceAvg, () => corridorLevel = median).On
                  ) {
                  corridorMoved.Off();
                  _buyLevel.RateEx = waveToTrade.RatesMax.Max(cp(RateLast)) + PointSize;
                  _sellLevel.RateEx = waveToTrade.RatesMin.Min(cp(RateLast)) - PointSize;
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = watcherWaveTrade.Value; sr.TradesCountEx = CorridorCrossesMaximum.Abs(); });
                }
              }
              adjustExitLevels0();
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
        _adjustEnterLevels += () => exitFunc()();
        _adjustEnterLevels += () => _buyLevel.SetPrice(CorridorPrice(RateLast));
        _adjustEnterLevels += () => _sellLevel.SetPrice(CorridorPrice(RateLast));

        #endregion
        #endregion

        #region On Trade Close
        _strategyExecuteOnTradeClose = t => {
          waveTradeOverTrigger.Off();
          if (isCurrentGrossOk()) {
            exitCrossed = false;
            ForEachSuppRes(sr => sr.InManual = false);
            DistanceIterationsRealClear();
            if (!IsAutoStrategy)
              IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum.Max(0);
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

        _strategyExecuteOnTradeOpen = () => { };
      }

      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion

      #region ============ Run =============
      _adjustEnterLevels();
      #endregion
    }

    double _VoltageAverage = double.NaN;
    public double VoltageAverage { get { return _VoltageAverage; } set { _VoltageAverage = value; } }

    double _VoltageHight = double.NaN;
    private double _buyLevelRate = double.NaN;
    private double _sellLevelRate = double.NaN;
    public double VoltageHight { get { return _VoltageHight; } set { _VoltageHight = value; } }
  }
}
