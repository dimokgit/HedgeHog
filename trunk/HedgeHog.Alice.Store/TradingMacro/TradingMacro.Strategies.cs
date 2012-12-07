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

    private IList<double> TrackBuySellCorridor(Store.SuppRes buyCloseLevel, Store.SuppRes sellCloseLevel) {
      var ratesShort = RatesArray.TakeEx(-5).Select(r => r.PriceAvg).OrderBy(d => d).ToArray();
      if ((_buyLevel.CanTrade || _sellLevel.CanTrade) && ratesShort.Any(min => min.Between(_sellLevel.Rate, _buyLevel.Rate))) {
        var newBuy = -_buyLevel.Rate + _buyLevel.Rate.Max(RatePrev.PriceAvg);
        var newSell = _sellLevel.Rate - _sellLevel.Rate.Min(RatePrev.PriceAvg);
        if (SymmetricalBuySell) {
          var point = InPoints(.1);
          if (_buyLevel.TradesCount <= CorridorCrossesCountMinimum && _sellLevel.TradesCount >= -1 && newBuy > point)
            newSell = newBuy;
          if (_sellLevel.TradesCount <= CorridorCrossesCountMinimum && _buyLevel.TradesCount >= -1 && newSell > point)
            newBuy = newSell;
        }
        if (_buyLevel.TradesCount == 1 && _sellLevel.TradesCount == 1 && SymmetricalBuySell) {
          var reArm = ((_buyLevel.Rate + _sellLevel.Rate) / 2).Between(RateLast.BidLow, RateLast.AskHigh);
          if (reArm) {
            var lastCross = RatesArray.ReverseIfNot().SkipWhile(r => r.PriceAvg > _sellLevel.Rate && r.PriceAvg < _buyLevel.Rate).First();
            if (lastCross.PriceAvg == _buyLevel.Rate && _buyLevel.TradesCount == 1)
              _buyLevel.TradesCount = 0;
            if (lastCross.PriceAvg == _sellLevel.Rate && _sellLevel.TradesCount == 1)
              _sellLevel.TradesCount = 0;
          }
        }
        //var lastTouch = CorridorStats.Rates.Where(r => MagnetPrice.Between(r.PriceLow, r.PriceHigh)).Last();
        //var corr = CorridorStats.Rates.TakeWhile(r => r >= lastTouch).Select(r => r.PriceAvg).OrderBy(r => r).ToArray();
        //_buyLevel.Rate = corr.LastByCount().Min(_buyLevel.Rate + newBuy);
        //_sellLevel.Rate = corr[0].Max(_sellLevel.Rate - newSell);
        _buyLevel.Rate = _buyLevel.Rate + newBuy;
        _sellLevel.Rate = _sellLevel.Rate - newSell;
        sellCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      }
      ConvertCloseLevelToOpenLevel(buyCloseLevel, sellCloseLevel, ratesShort);
      return ratesShort;
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
          CmaLotSize = 0;
          ResetSuppResesInManual();
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
    void ForEachSuppRes(Action<SuppRes> action) { SuppRes.ToList().ForEach(action); }
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
          CmaLotSize = 0;
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

    private void StrategyEnterUniversal() {
      if (!RatesArray.Any()) return;

      #region Local globals
      #region Loss/Gross
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      Func<bool> isCorridorFrozen = () => LotSizeByLossBuy >= MaxLotSize;
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
        #region Levels
        SuppResLevelsCount = 2;
        _buyLevel = Resistance0();
        _sellLevel = Support0();
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        _buyLevelRate = _sellLevelRate = double.NaN;
        var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
        Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
        var buyCloseLevel = Support1();
        var sellCloseLevel = Resistance1();
        Action<SuppRes> setCloseLevel = (sr) => {
          if (sr.InManual) return;
          sr.CanTrade = false;
          if (sr.TradesCount != 9) sr.TradesCount = 9;
        };
        ForEachSuppRes(sr => sr.InManual = false);
        ForEachSuppRes(sr => sr.ResetPricePosition());
        ForEachSuppRes(sr => sr.ClearCrossedHandlers());
        setCloseLevel(buyCloseLevel); setCloseLevel(sellCloseLevel);
        #endregion
        ShowTrendLines = false;
        _isSelfStrategy = true;

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
        var waveTradeOverTrigger = new ValueTrigger(false);
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
                var varaince = CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
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
              _adjustExitLevels(null,null);
              break;
            #endregion
            #region WaveMax1
            case TrailingWaveMethod.WaveMax1: 
              {
                if (firstTime)
                  _buySellLevelsForEach(sr => sr.CanTrade = false);
                var waveStDev = CorridorStats.StDevByPriceAvg.Max(CorridorStats.StDevByHeight);
                var median = (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                var varaince = waveStDev / MathExtensions.StDevRatioMax / 2;
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                SetTrendLines = () => {
                  var rates = new[] { RatesArray[0], RatesArray.LastBC() }.ToList();
                  rates.ForEach(r => {
                    r.PriceAvg1 = median;
                    r.PriceAvg2 = _CenterOfMassBuy + waveStDev;
                    r.PriceAvg3 = _CenterOfMassSell - waveStDev;
                  });
                  return rates.ToArray();
                };
                var corridorLength = CorridorDistanceRatio.Max(CorridorLength);
                var isSteady = !isCorridorFrozen()
                  && _CenterOfMassBuy < WaveTradeStart.RatesMax && _CenterOfMassSell > WaveTradeStart.RatesMin
                  && (LotSizeByLossBuy < MaxLotSize || WaveShort.Rates.Count > corridorLength && WaveTradeStart.Rates.Count > corridorLength);
                if (watcherWaveTrade.SetValue(isSteady).HasChangedTo) {
                  _buySellLevelsForEach(sr => sr.CanTrade = watcherWaveTrade.Value);
                  _buyLevel.Rate = WaveTradeStart.RatesMax.Max(RateLast.PriceAvg) + PointSize;
                  _sellLevel.Rate = WaveTradeStart.RatesMin.Min(RateLast.PriceAvg) - PointSize;
                  _buySellLevelsForEach(sr => { sr.CanTrade = true; sr.TradesCount = 0; });
                  waveTradeOverTrigger.Off();
                }
                if (CorridorCrossesMaximum < 0 && watcherTradeCounter.SetValue(_buyLevel.TradesCount.Max(_sellLevel.TradesCount) < CorridorCrossesMaximum).HasChangedTo)
                  NewThreadScheduler.Default.Schedule(() => _buyLevel.CanTrade = _sellLevel.CanTrade = false);
              }
              var isOver = false && _CenterOfMassBuy > WaveTradeStart.RatesMax && _CenterOfMassSell < WaveTradeStart.RatesMin && Trades.Any();
              if (waveTradeOverTrigger.Set(isOver)) {
                _adjustExitLevels(_CenterOfMassBuy, _CenterOfMassSell);
              } else
                _adjustExitLevels(null, null);
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

        #region Exit Trade
        _exitTrade = () => {
          double als = LotSizeByLossBuy;
          if (!isCorridorFrozen() &&( IsEndOfWeek() || !IsTradingHour())) {
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

        #region _adjustExitLevels
        _adjustExitLevels = (buyLevel,sellLevel) => {
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
                buyCloseLevel.RateEx = buyLevel.GetValueOrDefault((_buyLevelNetOpen().Min(_buyLevel.Rate) + tpColse))
                  .Max(Trades.HaveBuy() ? priceAvgMax : double.NaN);
              if (sellCloseLevel.InManual) {
                if (sellCloseLevel.Rate >= priceAvgMin)
                  sellCloseLevel.Rate = priceAvgMin;
              } else
                sellCloseLevel.RateEx = sellLevel.GetValueOrDefault((_sellLevelNetOpen().Max(_sellLevel.Rate) - tpColse))
                  .Min(Trades.HaveSell() ? priceAvgMin : double.NaN);
              try {
                buyCloseLevel.SetPrice(RateLast.PriceAvg);
                sellCloseLevel.SetPrice(RateLast.PriceAvg);
              } catch (ArgumentException exc) {
                Log = exc;
              }
            }
          }
        };
        #endregion

        #region On Trade Close
        _strategyExecuteOnTradeClose = t => {
          waveTradeOverTrigger.Off();
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
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
          CloseAtZero = _trimAtZero = false;
        };
        #endregion

        _strategyExecuteOnTradeOpen = () => { };
      }

      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion

      #region Run
      _adjustEnterLevels();
      _buyLevel.SetPrice(RateLast.PriceAvg);
      _sellLevel.SetPrice(RateLast.PriceAvg);
      _exitTrade();
      #endregion
    }

    private void ReArmLevelsTradeCount() {
      _CenterOfMassBuy = _buyLevel.Rate - WaveShort.RatesStDev;
      _CenterOfMassSell = _sellLevel.Rate + WaveShort.RatesStDev;
      if (RateLast.PriceAvg.Between(_CenterOfMassSell, _CenterOfMassBuy))
        _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
    }

    private void ReArmLevelsTradeCount_02() {
      if (MagnetPrice.Between(RateLast.PriceLow, RateLast.PriceHigh))
        _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
    }

    private void ReArmLevelsTradeCount_01() {
      if ((_buyLevel.Rate - RateLast.PriceAvg).Max(RateLast.PriceAvg - _sellLevel.Rate) > PriceSpreadAverage * 2)
        _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
    }


    private double[] GetWaveByStDev(double stDev) {
      var level = CorridorStats.Rates.SkipWhile(r => r.PriceStdDev < stDev)
        .SkipWhile(r => r.PriceStdDev > 0).SkipWhile(r => r.PriceStdDev == 0).SkipWhile(r => r.PriceStdDev > 0)
        .DefaultIfEmpty(CorridorStats.Rates.LastBC()).First();
      var levels = CorridorStats.Rates.TakeWhile(r => r > level).Select(r => r.PriceAvg).OrderBy(d => d).ToArray();
      return levels;
    }

    #region 09s

    WaveLast _waveLast = new WaveLast();
    class WaveLast {
      private Rate _rateStart = new Rate();
      private Rate _rateEnd = new Rate();
      private List<Rate> ratesOrdered;
      IList<Rate> _Rates;
      private double average;
      private DateTime startDate;
      public IList<Rate> Rates {
        get { return _Rates; }
        set {
          _Rates = value;
          this.IsUp = this.Rates.Select(r => r.PriceAvg).ToArray().Regress(1)[1] > 0;
        }
      }
      public bool IsHot { get; set; }
      public Rate High { get; set; }
      public Rate Low { get; set; }
      public bool IsUp { get; private set; }
      public Rate Sell {
        get {
          var a = this.Rates.ReverseIfNot();
          var b = a.TakeWhile(r => r.PriceAvg < this.High.PriceAvg).ToList();
          var c = b.OrderBy(r => r.PriceAvg).ToList();
          return c.DefaultIfEmpty(this.High).First();
        }
      }
      public Rate Buy {
        get {
          return this.Rates.ReverseIfNot().TakeWhile(r => r.PriceAvg > this.Low.PriceAvg)
            .OrderByDescending(r => r.PriceAvg).DefaultIfEmpty(this.Low).First();
        }
      }
      void SetWave(IList<Rate> rates) {
        var prices = rates.Select(r => r.PriceAvg).OrderBy(p => p).ToArray();
        var average = prices.Average();
        var mean = (prices.LastBC() - prices[0]) / 2 + prices[0];
        IsHot = IsUp && average < mean || !IsUp && average > mean;
      }
      public bool HasChanged(IList<Rate> wave, Func<Rate, Rate, bool> hasChanged = null) {
        if (wave == null || wave.Count == 0) return false;
        var changed = (hasChanged ?? HasChanged)(wave[0], wave.LastBC());
        if (changed) {
          this.Rates = wave.ToList();
          this.ratesOrdered = wave.OrderBy(r => r.PriceAvg).ToList();
          this.High = ratesOrdered.LastBC();
          this.Low = ratesOrdered[0];
        }
        return changed;
      }
      public bool HasChanged(Tuple<Rate, Rate, int> t) { return HasChanged(t.Item1, t.Item2); }
      public bool HasChanged(Rate rateStart, Rate rateEnd) {
        if ((rateStart.StartDate - this._rateStart.StartDate).Duration().TotalMinutes > 115 /*&& this._rateEnd.StartDate != rateEnd.StartDate*/) {
          this._rateStart = rateStart;
          this._rateEnd = rateEnd;
          return true;
        }
        return false;
      }
      public bool HasChanged4(IList<Rate> wave) {
        if (wave[0].StartDate - this.startDate > 60.FromMinutes()) {
          bool ret = Rates != null && Rates.Count > 0;
          this.Rates = wave.ToArray();
          this.startDate = wave[0].StartDate;
          SetWave(wave);
          return ret;
        }
        return false;
      }
      public bool HasChanged3(IList<Rate> wave, double price, double distance) {
        if (wave != null && !price.Between(this.average - distance, this.average + distance) && wave[0].StartDate > this.startDate) {
          this.Rates = wave.ToArray();
          this.average = price;
          this.startDate = wave[0].StartDate;
          return true;
        }
        return false;
      }
      public bool HasChanged(Rate rateStart) {
        if (rateStart.StartDate != this._rateStart.StartDate) {
          this._rateStart = rateStart;
          return true;
        }
        return false;
      }
      public bool HasChanged2(Rate rateStart, Rate rateEnd) {
        if (rateEnd.StartDate != this._rateEnd.StartDate) {
          this._rateStart = rateStart;
          this._rateEnd = rateEnd;
          return true;
        }
        return false;
      }
      public bool HasChanged1(Rate rateStart, Rate rateEnd) {
        if (rateStart.StartDate != this._rateStart.StartDate || this._rateEnd.StartDate != rateEnd.StartDate) {
          this._rateStart = rateStart;
          this._rateEnd = rateEnd;
          return true;
        }
        return false;
      }
    }
    #endregion

    double _VoltageAverage = double.NaN;
    public double VoltageAverage { get { return _VoltageAverage; } set { _VoltageAverage = value; } }

    double _VoltageHight = double.NaN;
    private double _buyLevelRate = double.NaN;
    private double _sellLevelRate = double.NaN;
    public double VoltageHight { get { return _VoltageHight; } set { _VoltageHight = value; } }
  }
}
