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
        _strategyExecuteOnTradeOpen = trade=> { };
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

        _strategyExecuteOnTradeOpen = trade => { };
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

    Func<Rate, double> _priceAvg = rate => rate.PriceAvg;

    private void StrategyEnterUniversal() {
      if (!RatesArray.Any()) return;

      #region ============ Local globals ============
      #region Loss/Gross
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => CurrentGrossInPips;
      Func<double> currentLoss = () => CurrentLoss;
      Func<double> currentGross = () => CurrentGross;
      Func<bool> isCorridorFrozen = () => LotSizeByLossBuy >= MaxLotSize;
      Func<bool> isProfitOk = () => false;
      #endregion
      _isSelfStrategy = true;
      Func<bool, double> crossLevelDefault = isBuy => isBuy ? _RatesMax + RatesHeight / 2 : _RatesMin - RatesHeight / 2;
      Action resetCloseAndTrim = () => CloseAtZero = _trimAtZero = _trimToLotSize = false;
      #endregion

      #region ============ Init =================

      if (_strategyExecuteOnTradeClose == null) {
        #region SetTrendLines
        Func<Func<Rate, double>, double, double[]> getStDevByPrice = (price, skpiRatio) => {
          var line = new double[CorridorStats.Rates.Count];
          CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          var hl = CorridorStats.Rates.Select((r, i) => price(r) - line[i]).Skip((CorridorStats.Rates.Count * skpiRatio).ToInt()).ToArray();
          var h = hl.Max() / 2;
          var l = hl.Min().Abs() / 2;
          return new[] { h, l };
        };
        Func<double[]> getStDev = () => {
          switch (CorridorHeightMethod) {
            case CorridorHeightMethods.ByMA: return getStDevByPrice(CorridorPrice,.9);
            case CorridorHeightMethods.ByPriceAvg: return getStDevByPrice(_priceAvg, 0);
          }
          throw new NotSupportedException("CorridorHeightMethods." + CorridorHeightMethod + " is not supported.");
        };
        SetTrendLines = () => {
          double h, l;
          if (getStDev == null)
            h = l = CorridorStats.StDevByHeight;
          else {
            var hl = getStDev();
            h = hl[0];
            l = hl[1];
          }
          if (CorridorStats == null || !CorridorStats.Rates.Any()) return new[] { new Rate(), new Rate() };
          var rates = new[] { CorridorStats.Rates[0], CorridorStats.Rates.LastBC() };
          var coeffs = CorridorStats.Coeffs;
          rates[0].PriceAvg1 = coeffs.RegressionValue(0);
          rates[0].PriceAvg2 = rates[0].PriceAvg1 + h * 2;
          rates[0].PriceAvg3 = rates[0].PriceAvg1 - l * 2;
          rates[1].PriceAvg1 = coeffs.RegressionValue(CorridorStats.Rates.Count - 1);
          rates[1].PriceAvg2 = rates[1].PriceAvg1 + h * 2;
          rates[1].PriceAvg3 = rates[1].PriceAvg1 - l * 2;
          return rates;
        };
        #endregion
        Action onEOW = () => { };
        #region Exit Funcs
        #region exitOnFriday
        Func<bool> exitOnFriday = () => {
          if (!SuppRes.Any(sr => sr.InManual)) {
            bool isEOW = !isCorridorFrozen() && IsAutoStrategy && (IsEndOfWeek() || !IsTradingHour());
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots());
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
              return true;
            }
          }
          return false;
        };
        #endregion
        Func<bool> exitByGrossTakeProfit = () => 
          Trades.Lots() >= LotSize * ProfitToLossExitRatio && TradesManager.MoneyAndLotToPips(-currentGross(), LotSize, Pair) <= TakeProfitPips;
        Func<bool> exitByLossGross = () => 
          Trades.Lots() >= LotSize * ProfitToLossExitRatio && currentLoss() < currentGross() * ProfitToLossExitRatio;// && LotSizeByLossBuy <= LotSize;
        #region exitVoid
        Action exitVoid = () => {
          if (exitOnFriday()) return;
        };
        #endregion
        #region exitByTakeProfit
        Action exitByTakeProfit = () => {
          double als = LotSizeByLossBuy;
          if (exitOnFriday()) return;
          if (exitByGrossTakeProfit())
            CloseTrades(Trades.Lots() - LotSize);
          else if (Trades.Lots() > LotSize && currentGross() > 0)
            _trimAtZero = true;
        };
        #endregion
        #region exitWave0
        Action exitWave0 = () => {
          double als = LotSizeByLossBuy;
          if (exitOnFriday()) return;
          if (exitByLossGross())
            CloseTrades(Trades.Lots() - (Trades.Lots() / ProfitToLossExitRatio).ToInt());
          else if (Trades.Lots() > LotSize && currentGross() > 0)
            _trimAtZero = true;
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
          if (exitOnFriday()) return;
          if (exitByLossGross())
            CloseTrades(Trades.Lots() - LotSizeByLossBuy);
          else {
            var lossInPips = TradesManagerStatic.MoneyAndLotToPips(CurrentLoss, LotSize, PipCost, BaseUnitSize);
            if (lossInPips < -RatesHeightInPips && CurrentGross*2 > CurrentLoss)
              CloseTrades();
          }
        };
        #endregion
        #region exitFunc
        Func<Action> exitFunc = () => {
          switch (ExitFunction) {
            case Store.ExitFunctions.Void: return exitVoid;
            case Store.ExitFunctions.Exit0: return exitWave0;
            case Store.ExitFunctions.GrossTP: return exitByTakeProfit;
            case Store.ExitFunctions.Exit: return exitWave;
            case Store.ExitFunctions.Exit1: return exitWave1;
            case Store.ExitFunctions.Exit2: return exitWave2;
          }
          throw new NotSupportedException(ExitFunction + " exit function is not supported.");
        };
        #endregion
        #endregion
        #region TurnOff Funcs
        Action<Action> turnOffByCorrelation1 = a => { if (CorridorCorrelation > CorrelationMinimum)a(); };
        Action<Action> turnOffByCorrelation = a => { if (CorridorCorrelation < CorrelationMinimum)a(); };
        Action<Action> turnOffByWaveHeight = a => { if (WaveShort.RatesHeight < RatesHeight * .75)a(); };
        Action<Action> turnOffByWaveShortLeft = a => { if (WaveShort.Rates.Count < WaveShortLeft.Rates.Count)a(); };
        Action<Action> turnOffByWaveShortAndLeft = a => { if (WaveShortLeft.Rates.Count < CorridorDistanceRatio && WaveShort.Rates.Count < WaveShortLeft.Rates.Count)a(); };
        Action<Action> turnOffByBuySellHeight = a => { if (_CenterOfMassBuy - _CenterOfMassSell < StDevByHeight.Max(StDevByPriceAvg))a(); };
        
        Action<Action> turnOff = a => {
          switch (TurnOffFunction) {
            case Store.TurnOffFunctions.Void: return;
            case Store.TurnOffFunctions.Correlation: turnOffByCorrelation(a); return;
            case Store.TurnOffFunctions.Variance: turnOffByCorrelation1(a); return;
            case Store.TurnOffFunctions.WaveHeight: turnOffByWaveHeight(a); return;
            case Store.TurnOffFunctions.WaveShortLeft: turnOffByWaveShortLeft(a); return;
            case Store.TurnOffFunctions.WaveShortAndLeft: turnOffByWaveShortAndLeft(a); return;
            case Store.TurnOffFunctions.BuySellHeight: turnOffByBuySellHeight(a); return;
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
          if (suppResCanTrade(suppRes)) {
            lot += AllowedLotSizeCore();
            suppRes.TradeDate = TradesManager.ServerTime;
          }
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
          SuppRes.ForEach(sr => sr.ResetPricePosition());
          srFrom.ResetPricePosition();
          var srTo = _buySellLevels.Single(sr => sr.IsSupport == srFrom.IsSupport);
          srTo.Rate = srFrom.Rate;
          srTo.ResetPricePosition();
          srTo.TradesCount = srFrom.TradesCount;
          SuppRes.ForEach(sr => sr.ResetPricePosition());
          buyCloseLevel.Rate = crossLevelDefault(true);
          sellCloseLevel.Rate = crossLevelDefault(false);
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
          if (sr.CanTrade) {
            if (sr.InManual) handleActiveExitLevel(sr);
            else crossedEnter(s, e);
          } else
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
          if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
            if (tradesCount == 0) {
              buyCloseLevel.RateEx = crossLevelDefault(true);
              buyCloseLevel.ResetPricePosition();
              sellCloseLevel.RateEx = crossLevelDefault(false);
              sellCloseLevel.ResetPricePosition();
            } else {
              buyCloseLevel.SetPrice(CorridorPrice(RateLast));
              sellCloseLevel.SetPrice(CorridorPrice(RateLast));
              if (!Trades.Any()) {
                adjustExitLevels(buyLevel, sellLevel);
                buyCloseLevel.ResetPricePosition();
                sellCloseLevel.ResetPricePosition();
                return;
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
          } else {
            buyCloseLevel.SetPrice(CorridorPrice(RateLast));
            sellCloseLevel.SetPrice(CorridorPrice(RateLast));

          }
        };
        Action adjustExitLevels0 = () => adjustExitLevels(double.NaN, double.NaN);
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
        var corridorMoved = new ValueTrigger(false);
        double corridorLevel = 0;
        DateTime corridorDate = TradesManager.ServerTime;
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
            case Store.VarainceFunctions.Wave: return () => WaveShort.RatesHeight * .45;
            case Store.VarainceFunctions.StDevSqrt: return () => Math.Sqrt(CorridorStats.StDevByPriceAvg * CorridorStats.StDevByHeight);
            case Store.VarainceFunctions.Price: return () => CorridorStats.StDevByPriceAvg * _waveStDevRatioSqrt / 2;
            case Store.VarainceFunctions.Hight: return () => CorridorStats.StDevByHeight * _waveStDevRatioSqrt / 2;
            case VarainceFunctions.Max: return () => CorridorStats.StDevByPriceAvg.Max(CorridorStats.StDevByHeight) * _waveStDevRatioSqrt / 2;
            case VarainceFunctions.Min: return () => CorridorStats.StDevByPriceAvg.Min(CorridorStats.StDevByHeight) * _waveStDevRatioSqrt / 2;
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
        Action runOnce = null;
        #endregion

        #region adjustEnterLevels
        Action adjustEnterLevels = () => {
          if (!WaveShort.HasRates) return;
          switch (TrailingDistanceFunction) {
            #region WaveTrade
            case TrailingWaveMethod.WaveTrade: {
                if (firstTime) { }
                setCloseLevels(false);
                setCenterOfMass(WaveShort);
                var rates = SetTrendLines().Select(r => r.PriceAvg1).OrderBy(p => p).ToArray();
                _buyLevel.RateEx = rates[0];
                _sellLevel.RateEx = rates[1];
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
                setCloseLevels(false);
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
            #region WaveMax3
            case TrailingWaveMethod.WaveMax3:
              #region firstTime
              if (firstTime) { }
              #endregion
              {
                var rates = SetTrendLines().Select(r => r.PriceAvg1).ToArray();
                _buyLevel.RateEx = rates[1] + varianceFunc()();
                _sellLevel.RateEx = rates[1] - varianceFunc()();
                _buySellLevelsForEach(sr => sr.CanTradeEx = true);
              }
              adjustExitLevels0();
              break;
            #endregion
            #region WaveCommon
            case TrailingWaveMethod.WaveCommon: {
                if (firstTime) {
                }
                setCloseLevels(false);
                cp = CorridorPrice();
                var median = medianFunc()();
                var varaince = varianceFunc()();
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
                if (isSteady == null || watcherWaveTrade.SetValue(isSteady()).HasChangedTo
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
            #region HorseShoe
            case TrailingWaveMethod.HorseShoe:
              #region firstTime
              if (firstTime) { }
              #endregion
              if(CorridorCorrelation < CorrelationMinimum)
              {
                var hl = getStDev();
                var m = medianFunc0(Store.MedianFunctions.Regression)();
                _buySellLevelsForEach(sr => sr.SetPrice(CorridorPrice(RateLast)));
                _sellLevel.RateEx = m + hl[0] * 2;
                _buyLevel.RateEx = m - hl[1] * 2;
                _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                new[] { buyCloseLevel, sellCloseLevel }.ForEach(sr => {
                  sr.RateEx = 0.1;
                  sr.ResetPricePosition();
                });
              }else
              {
                var hl = getStDev();
                var m = medianFunc0(Store.MedianFunctions.Regression1)();
                _buyLevel.RateEx = m + hl[0] * 2;
                _sellLevel.RateEx = m - hl[1] * 2;
                _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                adjustExitLevels0();
              }
              break;
            #endregion
            #region Sinus
            case TrailingWaveMethod.Sinus:
              #region firstTime
              if (firstTime) { }
              #endregion
              {
                if (CorridorCorrelation >= CorrelationMinimum) {
                  var hl = getStDev();
                  var m = medianFunc()();
                  _buyLevel.RateEx = m + hl[0] * 2;
                  _sellLevel.RateEx = m - hl[1] * 2;
                  _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                }
                adjustExitLevels0();
              }
              break;
            #endregion
            #region Count
            case TrailingWaveMethod.Count:
              #region firstTime
              if (firstTime) {
                watcherCanTrade.SetValue(false);
                _CenterOfMassBuy = _CenterOfMassSell = _RatesMin + RatesHeight / 2;
              }
              #endregion
              {
                _buyLevel.RateEx = _CenterOfMassBuy;
                _sellLevel.RateEx = _CenterOfMassSell;
                _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy);
                adjustExitLevels0();
              }
              break;
            #endregion
            #region CountWithAngle
            case TrailingWaveMethod.CountWithAngle:
              #region firstTime
              if (firstTime) {
                watcherCanTrade.SetValue(false);
              }
              #endregion
              {
                if (watcherCanTrade.SetValue(_crossesOk && corridorLevel.Abs(MagnetPrice) > varianceFunc()()).ChangedTo(true)) {
                  corridorLevel = MagnetPrice;
                  _buyLevel.RateEx = _CenterOfMassBuy;
                  _sellLevel.RateEx = _CenterOfMassSell;
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
              }
              #endregion
              {
                var coeffs = CorridorStats.Coeffs;
                var angleOk = coeffs[1].Angle(PointSize).Abs().ToInt() <= 0;
                var crossesOk = CorridorStats.CorridorCrossesCount >= WaveStDevRatio;
                var level = coeffs[0];
                if (watcherCanTrade.SetValue(angleOk && crossesOk && corridorLevel.Abs(level) > RatesHeight / 3).ChangedTo(true)) {
                  corridorLevel = level;
                  var median = medianFunc()();
                  var variance = varianceFunc()() * 2;
                  _buyLevel.RateEx = median + variance;
                  _sellLevel.RateEx = median - variance;
                  _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy);
                }
                var a = 7;
                var segment = RatesHeight / a;
                var bottom = _RatesMin + segment * (a/2.0).Floor();
                var top = bottom + segment;
                var tradeLevel = (_buyLevel.Rate + _sellLevel.Rate) / 2;
                if ((_buyLevel.CanTrade||_sellLevel.CanTrade) && IsAutoStrategy && tradeLevel.Between(bottom, top))
                  _buySellLevelsForEach(sr => sr.CanTradeEx = false);
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
                var wavelette1 = WaveShort.Rates.Wavelette(cp);
                var wavelette2 = WaveShort.Rates.Skip(wavelette1.Count).ToArray().Wavelette(cp);
                if (wavelette2.Any()) {
                  wavelette2First = cp(wavelette2[0]);
                  wavelette2Last = cp(wavelette2.LastBC());
                  var wavelette2Height = wavelette2First.Abs(wavelette2Last);
                  waveletteOk = wavelette1.Count < 5 && wavelette2Height >= StDevByHeight + StDevByPriceAvg;
                }
                if (watcherCanTrade.SetValue(waveletteOk).ChangedTo(true)) {
                  var isUp = wavelette2First > wavelette2Last;
                  var sorted = wavelette2.OrderBy(_priceAvg);
                  var dateSecond = (isUp ? sorted.Last() : sorted.First()).StartDate;
                  var pricesSecond = WaveShort.Rates.TakeWhile(r => r.StartDate >= dateSecond);
                  var priceFirst = isUp ? pricesSecond.Min(_priceAvg) : pricesSecond.Max(_priceAvg);
                  var priceSecond = isUp ? pricesSecond.Max(_priceAvg) : pricesSecond.Min(_priceAvg);
                  var high = (isUp ? priceSecond : priceFirst) + PointSize;
                  var low = (isUp ? priceFirst : priceSecond) - PointSize;
                  _buyLevel.RateEx = high;
                  _sellLevel.RateEx = low;
                  _buySellLevelsForEach(sr => {
                    sr.CanTradeEx = IsAutoStrategy;
                    sr.TradesCount = 1;
                  });
                }
                var heightMin = StDevByHeight.Min(StDevByPriceAvg);
                if (_buyLevel.Rate - _sellLevel.Rate < heightMin) {
                  var median = (_buyLevel.Rate + _sellLevel.Rate) / 2;
                  _buyLevel.RateEx = median + heightMin / 2;
                  _sellLevel.RateEx = median - heightMin / 2;
                }
                _buySellLevelsForEach(sr => sr.CanTradeEx = IsAutoStrategy && _buyLevel.Rate > _sellLevel.Rate);
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

        #region On Trade Open
        _strategyExecuteOnTradeOpen = trade => {
          SuppRes.ForEach(sr => sr.ResetPricePosition());
          if (onOpenTradeLocal != null) onOpenTradeLocal(trade);
        };
        #endregion

        #region _adjustEnterLevels
        _adjustEnterLevels += () => adjustEnterLevels();
        _adjustEnterLevels += () => { if (runOnce != null) { runOnce(); runOnce = null; } };
        _adjustEnterLevels += () => turnOff(() => _buySellLevelsForEach(sr => sr.CanTrade = false));
        _adjustEnterLevels += () => exitFunc()();
        _adjustEnterLevels += () => {
          try {
            var r = GetTradeEnterBy()(RateLast);
            if (!double.IsNaN(r)) {
              _buyLevel.SetPrice(r);
              _sellLevel.SetPrice(r);
            }
              //SuppRes.ForEach(sr => sr.SetPrice(r));
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
      _adjustEnterLevels();
      #endregion
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
