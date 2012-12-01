﻿using System;
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

    private void StrategyEnterDeviator() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      Func<bool> isRatesRatioOk = () => RatesStDevToRatesHeightRatio < CorridorDistanceRatio;
      Action<bool?> setLevels = isUp => {
        if (isUp.HasValue) {
          if (WaveShort.RatesStDev > SpreadForCorridor) {
            WaveShort.IsUp = null;
          } else {
            var corridorHeight = RatesStDev;
            if (isUp.Value) {
              _buyLevel.Rate = WaveShort.RatesMax;
              _buyLevel.TradesCount = CorridorCrossesMaximum;
              _sellLevel.Rate = _buyLevel.Rate - corridorHeight;
              _sellLevel.TradesCount = -1;
            } else {
              _sellLevel.Rate = WaveShort.RatesMin;
              _sellLevel.TradesCount = CorridorCrossesMaximum;
              _buyLevel.Rate = _sellLevel.Rate + corridorHeight;
              _buyLevel.TradesCount = -1;
            }
            _buyLevel.CanTrade = _sellLevel.CanTrade = true;
          }
        }
      };
      Action resetLevels = () => {
        if (_buyLevel.TradesCount == CorridorCrossesMaximum) {
          setLevels(true);
        }
        if (_sellLevel.TradesCount == CorridorCrossesMaximum) {
          setLevels(false);
        }
      };
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        CorridorStats = null;
        WaveHigh = null;
        SuppResLevelsCount = 2;
        LastProfitStartDate = null;
        CmaLotSize = 0;
        ShowTrendLines = false;
        WaveShort.LengthCma = double.NaN;
        WaveShort.ClearEvents();
        WaveShort.IsUpChanged += (s, e) => {
          setLevels(WaveShort.IsUp);
        };
        _isCorridorDistanceOk.ValueChanged += (s, e) => {
          if (_isCorridorDistanceOk.Value)
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
        };
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (IsInVitualTrading && isManual())
              IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          }
          if (CloseAtZero || isCurrentGrossOk())
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;
          CloseAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
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
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion


      #region Sanity check
      if (!IsInVitualTrading && CorridorStats.StopRate != null) {
        CorridorStats.StopRate = RatesArray.FindBar(CorridorStats.StopRate.StartDate);
      }
      #endregion

      #region Exit
      double als = AllowedLotSizeCore();
      if (IsTradingActive) {
        if (Trades.Lots() / als >= this.ProfitToLossExitRatio) {
          var lot = Trades.Lots() - als.ToInt();
          CloseTrades(lot);
          return;
        }
        if (IsAutoStrategy) {
          if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
          if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
        }
      }
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (!CloseAtZero && !CloseOnProfitOnly) {
        var a = (LotSize / BaseUnitSize) * TradesManager.GetPipCost(Pair) * 100;
        var pipsOffset = (currentLoss() / a);
        var tpOk = TakeProfitFunction == TradingMacroTakeProfitFunction.BuySellLevels 
          && Trades.GrossInPips() * PLToCorridorExitRatio > InPips(_buyLevel.Rate - _sellLevel.Rate);
        CloseAtZero = currentGrossInPips() > pipsOffset.Max(-3)
          && (Trades.Lots() > LotSize * 2 || tpOk /*|| !canTrade*/);
      }
      #endregion

      #region Run
      //EnsureCorridorStopRate();
      //resetLevels();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }

    #region Mass Min/Max
    double _massMin = double.NaN;
    double _massMax = double.NaN;
    double _MassLevelMax = double.NaN;
    public double MassLevelMax { get { return _MassLevelMax; } set { _MassLevelMax = value; } }
    double _MassLevelMin = double.NaN;
    public double MassLevelMin { get { return _MassLevelMin; } set { _MassLevelMin = value; } }
    void SetMassMaxMin() {
      //if (WaveAverageIteration == 0) return true;
      var masses = RatesArray.SkipWhile(r => !r.Mass.HasValue).Select(r => r.Mass.Value).OrderBy(m => m).ToArray();
      _massMin = masses[0];
      _massMax = masses.LastBC();
      var massAverage = masses.Average();
      var massStDev = masses.StDev();
      MassLevelMax = massAverage + massStDev * WaveAverageIteration.Max(1);
      MassLevelMin = massAverage - massStDev * WaveAverageIteration.Max(1);
    }
    #endregion

    private void StrategyEnterDistancer() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      IList<Rate> _massRates = null;
      Func<IList<Rate>> massRates = () => { return _massRates ?? (_massRates = RatesArray.LastBCs(10).ToArray()); };
      Func<bool> isMassOk = () => massRates().Max(r => r.Mass.GetValueOrDefault(double.NaN)) > MassLevelMax;
      Action adjustLevels = () => {
        SetTimeFrameStats();
        if (RatesHeight < RatesHeightMinimum.Abs() && _buyLevel.TradesCount.Max(_sellLevel.TradesCount) < CorridorCrossesMaximum) {
          _buyLevel.Rate = _RatesMax;
          _sellLevel.Rate = _RatesMin;
          _buyLevel.CanTrade = _sellLevel.CanTrade = true;
          _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;
        } else {
          if (_buyLevel.Rate > _RatesMax) {
            _buyLevel.Rate = _RatesMax;
            _sellLevel.Rate = _RatesMin;
          }
          _buyLevel.Rate = _buyLevel.Rate.Min(_RatesMax);
          if (_sellLevel.Rate < _RatesMin) {
            _sellLevel.Rate = _RatesMin;
            _buyLevel.Rate = _RatesMax;
          }
          if (RatesHeightMinimum == RatesHeightMinimumOff && _buyLevel.Rate != _sellLevel.Rate && _buyLevel.TradesCount.Max(_sellLevel.TradesCount) < CorridorCrossesMaximum)
            _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        }
      };
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        ReloadPairStats();
        //if (!TimeFrameStats.Any()) throw new ApplicationException("Timeframe statisticks for " + new { Pair, BarPeriod, BarsCount } + " are not found.");
        if (false && !IsInVitualTrading) {
          var historyRates = TradesManager.GetBarsFromHistory(Pair, BarPeriodInt, TradesManager.ServerTime.AddDays(-3), DateTime.Now);
          var heights = new List<double>();
          var frame = BarsCount / 2;
          for (var i = 0; i <= BarsCount-frame ; i++) {
            heights.Add(historyRates.Skip(i).Take(frame).ToArray().Height());
          }
          var avg = heights.Average();
          RatesHeightMinimum = avg;
        }
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _buyLevel.CanTradeChanged += (s, e) => { if (!_buyLevel.CanTrade)  _buyLevel.TradesCount = 0; };
        _sellLevel.CanTradeChanged += (s, e) => { if (!_sellLevel.CanTrade)  _sellLevel.TradesCount = 0; };
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (IsInVitualTrading && isManual())
              IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          }
          if (CloseAtZero || isCurrentGrossOk())
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
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
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      double als = AllowedLotSizeCore();
      if (IsTradingActive) {
        if (Trades.Lots() > LotSize) {
          if (!CloseAtZero && Trades.Lots() / als >= this.ProfitToLossExitRatio)
            _trimAtZero = true;
          if (currentGrossInPips() > 0)
            _trimAtZero = true;
        }
        if (IsAutoStrategy) {
          if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
          if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
        }
      }
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (isCurrentGrossOk() && Trades.Lots() > LotSize)
        _trimAtZero = true;
      else {
        if (!CloseAtZero && !CloseOnProfitOnly) {
          var a = (LotSize / BaseUnitSize) * TradesManager.GetPipCost(Pair) * 100;
          var pipsOffset = (currentLoss() / a);
          var tpOk = TakeProfitFunction == TradingMacroTakeProfitFunction.BuySellLevels
            && Trades.GrossInPips() * PLToCorridorExitRatio > InPips(_buyLevel.Rate - _sellLevel.Rate);
          CloseAtZero = currentGrossInPips() > pipsOffset.Max(-3)
            && (Trades.Lots() > LotSize * 2 || tpOk /*|| !canTrade*/);
        }
      }
      #endregion

      #region Run
      //SetMassMaxMin();
      adjustLevels();
      if ((_buyLevel.CanTrade || _sellLevel.CanTrade) && IsBlackoutTime || (RatesHeightMinimum!= RatesHeightMinimumOff && RatesHeight > RatesHeightMinimum.Abs() * 2))
        _buyLevel.TradesCount = _sellLevel.TradesCount = 1;


      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }

    ObservableValue<double> _corridorDirection;
    private void StrategyEnterDistancerAndAHalf() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      IList<Rate> _massRates = null;
      Func<IList<Rate>> massRates = () => { return _massRates ?? (_massRates = RatesArray.LastBCs(10).ToArray()); };
      Func<bool> isMassOk = () => massRates().Max(r => r.Mass.GetValueOrDefault(double.NaN)) > MassLevelMax;
      Action adjustLevels = () => {
        var isExpending = WaveShort.RatesHeight > WaveShortLeft.RatesHeight;
        if (this.IsAutoStrategy || isExpending) {
          _buyLevel.Rate = WaveShort.RatesMax;
          _sellLevel.Rate = WaveShort.RatesMin;
          _buyLevel.CanTrade = _sellLevel.CanTrade = isExpending;
        }
      };
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        ReloadPairStats();
        //if (!TimeFrameStats.Any()) throw new ApplicationException("Timeframe statisticks for " + new { Pair, BarPeriod, BarsCount } + " are not found.");
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _corridorDirection = new ObservableValue<double>(0);
        _corridorDirection.ValueChanged += (s, e) => { adjustLevels(); };
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (IsInVitualTrading && isManual())
              IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          }
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
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
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      double als = AllowedLotSizeCore();
      if (IsTradingActive) {
        if (IsAutoStrategy) {
          if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
          if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
        }
      }
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (Trades.Lots() / als > ProfitToLossExitRatio)
        _trimAtZero = true;
      #endregion

      #region Run
      //SetMassMaxMin();
      adjustLevels();
      //_corridorDirection.Value = WaveShort.Rates.Select(r => r.PriceAvg).ToArray().Regress(1)[1].Sign();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }

    private void StrategyEnterDistancerAndAQuater() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      IList<Rate> _massRates = null;
      Func<IList<Rate>> massRates = () => { return _massRates ?? (_massRates = RatesArray.LastBCs(10).ToArray()); };
      Func<bool> isMassOk = () => massRates().Max(r => r.Mass.GetValueOrDefault(double.NaN)) > MassLevelMax;
      Action adjustLevels = () => {
        var isExpending = WaveShort.RatesHeight > WaveShortLeft.RatesHeight;
        if (this.IsAutoStrategy || isExpending) {
          var avg = WaveTradeStart.Rates.Average(r => r.PriceAvg);
          _buyLevel.Rate = avg + RatesStDev / 2;
          _sellLevel.Rate = avg - RatesStDev / 2;
          _buyLevel.CanTrade = _sellLevel.CanTrade = isExpending;
        }
      };
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        ReloadPairStats();
        //if (!TimeFrameStats.Any()) throw new ApplicationException("Timeframe statisticks for " + new { Pair, BarPeriod, BarsCount } + " are not found.");
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _corridorDirection = new ObservableValue<double>(0);
        _corridorDirection.ValueChanged += (s, e) => { adjustLevels(); };
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (IsInVitualTrading && isManual())
              IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          }
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
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
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      double als = AllowedLotSizeCore();
      if (IsTradingActive) {
        if (IsAutoStrategy) {
          if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
          if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
        }
      }
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (Trades.Lots() / als > ProfitToLossExitRatio)
        _trimAtZero = true;
      #endregion

      #region Run
      //SetMassMaxMin();
      //adjustLevels();
      _corridorDirection.Value = WaveTradeStart.Rates.Select(r => r.PriceAvg).ToArray().Regress(1)[1].Sign();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }

    ObservableValue<int> _areCorridorsAlligned;
    ObservableValue<double> _mustTrim;
    private void StrategyEnterAverager() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      IList<Rate> _massRates = null;
      Action adjustLevels = () => {
        var offset = (RatesStDev * 2 - WaveTradeStart.RatesHeight).Max(0) / 2;
        _buyLevel.Rate = WaveTradeStart.RatesMax + offset;
        _sellLevel.Rate = WaveTradeStart.RatesMin - offset;
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        _buyLevel.TradesCount = _sellLevel.TradesCount = 1;
      };
      Action adjustLevels1 = () => {
        if (_buyLevel.TradesCount > _sellLevel.TradesCount) {
          _sellLevel.Rate = WaveTradeStart.RatesMin;
          _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        }
        if (_buyLevel.TradesCount < _sellLevel.TradesCount) {
          _buyLevel.Rate = WaveTradeStart.RatesMax;
          _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        }

      };
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        ReloadPairStats();
        //if (!TimeFrameStats.Any()) throw new ApplicationException("Timeframe statisticks for " + new { Pair, BarPeriod, BarsCount } + " are not found.");
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _areCorridorsAlligned = new ObservableValue<int>(0);
        _areCorridorsAlligned.ValueChanged += (s, e) => adjustLevels();
        _mustTrim = new ObservableValue<double>(0);
        _mustTrim.ValueChanged += (s, e) => adjustLevels();
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if(!IsAutoStrategy) IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          }
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
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
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      double als = AllowedLotSizeCore();
      if (IsEndOfWeek()) {
        if (Trades.Length > 0)
          CloseAtZero = true;
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      }
      if (IsTradingActive) {
        if (IsAutoStrategy) {
          if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
          if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
        }
      }
      if (!CloseOnProfitOnly && Trades.Lots() / als >= ProfitToLossExitRatio) {
        _trimAtZero = true;
      }
      if (RateLast.Spread+RatePrev.Spread > PriceSpreadAverage * 10)
        _buyLevel.TradesCount = _sellLevel.TradesCount = 1;
      #endregion

      #region Run
      if (WaveShort.RatesHeight < WaveShortLeft.RatesHeight) {
        _CenterOfMassBuy = WaveShort.Rates.Skip(WaveTradeStart.Rates.Count).Average(r=>r.PriceAvg);
        _CenterOfMassSell = WaveTradeStart.Rates.Average(r=>r.PriceAvg);
        _areCorridorsAlligned.Value = (_CenterOfMassBuy - _CenterOfMassSell).Sign().ToInt();
        //_tradesCountTotal.Value = _buyLevel.TradesCount + _sellLevel.TradesCount;
      }
      adjustLevels1();
      if (_buyLevel.TradesCount.Min(_sellLevel.TradesCount).Abs() > CorridorCrossesMaximum)
        CloseAtZero = true;

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }

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
        ReloadPairStats();
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

    ObservableValue<double> _leftRightWaveAvg = new ObservableValue<double>(0);
    private void StrategyEnterBreaker() {
      if (!RatesArray.Any()) return;

      #region Local globals
      #region Others
      if (SuppResLevelsCount != 2) SuppResLevelsCount = 2;
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      IList<Rate> _massRates = null;
      Func<IList<Rate>> massRates = () => { return _massRates ?? (_massRates = RatesArray.LastBCs(10).ToArray()); };
      Func<bool> isMassOk = () => massRates().Max(r => r.Mass.GetValueOrDefault(double.NaN)) > MassLevelMax;
      #endregion
      #region adjustLevels
      Action adjustLevels = () => {
        var wave = WaveShort.Rates.Skip(WaveTradeStart.Rates.Count);
        if (TrailingDistanceFunction == TrailingWaveMethod.WaveTrade) {
          if (Trades.HaveBuy()) {
            _buyLevel.Rate = wave.Max(r => r.PriceAvg);
            _sellLevel.Rate = WaveTradeStart.RatesMin;
          }
          if (Trades.HaveSell()) {
            _sellLevel.Rate = wave.Min(r => r.PriceAvg);
            _buyLevel.Rate = WaveTradeStart.RatesMax;
          }
          if (IsAutoStrategy && !Trades.Any()) {
            _buyLevel.Rate = wave.Max(r => r.PriceAvg);
            _sellLevel.Rate = wave.Min(r => r.PriceAvg);
          }
        } else if (TrailingDistanceFunction == TrailingWaveMethod.WaveShort) {
          double waveMax = wave.Max(r => r.PriceAvg), waveMin = wave.Min(r => r.PriceAvg);
          var isHot = (waveMax - waveMin) > RatesHeight * CorridorDistanceRatio;
          if (isHot)
            _buyLevel.CanTrade = _sellLevel.CanTrade = true;
          if (isHot) {
            _buyLevel.Rate = waveMax;
            _sellLevel.Rate = waveMin;
          }
        } else if (TrailingDistanceFunction == TrailingWaveMethod.WaveLeft) {
          double avgLeft = WaveShortLeft.Rates.Average(r=>r.PriceAvg), avgRight = WaveShort.Rates.Average(r=>r.PriceAvg);
          _leftRightWaveAvg.Value = (avgLeft - avgRight).Sign();
          _CenterOfMassBuy = avgLeft; _CenterOfMassSell = avgRight;
          var isHot = _leftRightWaveAvg.HasChanged;
          if (isHot) {
            _leftRightWaveAvg.HasChanged = false;
            _buyLevel.Rate = WaveTradeStart.RatesMax;
            _sellLevel.Rate = WaveTradeStart.RatesMin;
          }
          _buyLevel.CanTrade = _sellLevel.CanTrade = true;// (_buyLevel.Rate - _sellLevel.Rate) >= RatesStDev + WaveShort.RatesStDev;
        } else if (TrailingDistanceFunction == TrailingWaveMethod.WaveMax) {
          double waveMax = wave.Max(r => r.PriceAvg), waveMin = wave.Min(r => r.PriceAvg);
          var isHot = (waveMax - waveMin) > RatesHeight * CorridorDistanceRatio;
          if (isHot)
            _buyLevel.CanTrade = _sellLevel.CanTrade = true;
          else
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
          var tradesCount = _buyLevel.TradesCount.Min(_sellLevel.TradesCount);
          if (isHot && tradesCount == 0) {
            _buyLevel.Rate = waveMin;
            _sellLevel.Rate = waveMax;
          }
        } else {
          var offset = TradingDistanceFunction == TradingMacroTakeProfitFunction.BuySellLevels ? RatesStDev : TradingDistance;
          if (_buyLevel.TradesCount == 0 && _sellLevel.TradesCount == -1) {
            _sellLevel.Rate = _buyLevel.Rate - offset;
            _buyLevel.TradesCount = -1;
          }
          if (_buyLevel.TradesCount == -1 && _sellLevel.TradesCount == 0) {
            _buyLevel.Rate = _sellLevel.Rate + offset;
            _sellLevel.TradesCount = -1;
          }
        }
      };
      #endregion
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        ReloadPairStats();
        ShowTrendLines = false;
        _buyLevel.CanTrade = _sellLevel.CanTrade = IsAutoStrategy;
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (!IsAutoStrategy) {
              IsTradingActive = false;
            }
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
        return;
      }
      this.TradesManager.TradeAdded += (s, e) => {
        if (_buyLevel.Rate <= _sellLevel.Rate)
          if (e.Trade.IsBuy) _sellLevel.Rate = _buyLevel.Rate - TradingDistance;
          else _buyLevel.Rate = _sellLevel.Rate + TradingDistance;
      };
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
      if (Trades.Lots() / als > ProfitToLossExitRatio || als == LotSize && Trades.Lots() > LotSize)
        _trimAtZero = true;
      #endregion

      #region Run
      //SetMassMaxMin();
      adjustLevels();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        if (!Trades.Any()) {
          buyCloseLevel.Rate = _RatesMin - RatesStDev;
          sellCloseLevel.Rate = _RatesMax + RatesStDev;
        } else {
          var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
          buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
          sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
        }
      }
      #endregion
    }

    IEnumerable<Rate> _waveShortMiddle { get { return WaveShort.Rates.Skip(WaveTradeStart.Rates.Count); } }

    void SetBuySellLevels(bool isBuy,double waveMax,double waveMin) {
      if (isBuy) {
        _buyLevelRate = waveMax;
        _sellLevelRate = WaveTradeStart.RatesMin;
      } else { 
        _sellLevelRate = waveMin;
        _buyLevelRate = WaveTradeStart.RatesMax;
      }

    }

    public void OpenTrade(bool isBuy, int lot) {
      CheckPendingAction("OT", (pa) => {
        if (lot > 0) {
          pa();
          TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
        }
      });
    }
    void ForEachSuppRes(Action<SuppRes> action) { SuppRes.ToList().ForEach(action); }
    ObservableValue<double> _isCorridorOk = new ObservableValue<double>(0);
    ObservableValue<bool> _isCorridorAreaOk = new ObservableValue<bool>(false);
    ObservableValue<double> _corridorArea = new ObservableValue<double>(0);

    private void StrategyEnterStDevCombo() {
      if (!RatesArray.Any() || !WaveShort.HasRates) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
      Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
      var buyCloseLevel = Support1();
      var sellCloseLevel = Resistance1();

      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      IList<Rate> _massRates = null;
      Func<IList<Rate>> massRates = () => { return _massRates ?? (_massRates = RatesArray.LastBCs(10).ToArray()); };
      Func<bool> isMassOk = () => massRates().Max(r => r.Mass.GetValueOrDefault(double.NaN)) > MassLevelMax;
      Func<double> ratesMedian = () => (WaveTradeStart.RatesMax + WaveTradeStart.RatesMin) / 2;

      #region adjustLevels
      Action adjustLevels = () => {
        var pricePosition = RateLast.PriceAvg.PositionByMiddle(WaveTradeStart.RatesMax, WaveTradeStart.RatesMin);
        var wave = WaveShort.Rates.Skip(WaveTradeStart.Rates.Count);
        var shortAvg = wave.PriceAvg();
        var shortStDevCombo = StDevByHeight + StDevByPriceAvg;
        _CenterOfMassBuy = shortAvg + shortStDevCombo / 2;
        _CenterOfMassSell = shortAvg - shortStDevCombo / 2;
        var waveMax = _CenterOfMassBuy;
        var waveMin = _CenterOfMassSell;
        var waveHeight = waveMax - waveMin;
        SetBuySellLevels(pricePosition > 0.5, waveMax, waveMin);
        if (TrailingDistanceFunction == TrailingWaveMethod.WaveTrade) {
          _isCorridorAreaOk.Value = (_corridorArea.Value - ratesMedian()).Abs() > TradingDistance;
          if (Trades.HaveBuy()) {
            _buyLevel.RateEx = waveMax;
            _sellLevel.RateEx = WaveTradeStart.RatesMin;
          } else if (Trades.HaveSell()) {
            _sellLevel.RateEx = waveMin;
            _buyLevel.RateEx = WaveTradeStart.RatesMax;
          } else if (HasPendingEntryOrders) {
            _buyLevel.RateEx = _buyLevelRate;
            _sellLevel.RateEx = _sellLevelRate;
          } else if (IsAutoStrategy && !Trades.Any()) {
            _buyLevel.RateEx = waveMax;
            _sellLevel.RateEx = waveMin;
          }

        } else if (TrailingDistanceFunction == TrailingWaveMethod.WaveShort) {
          if (IsAutoStrategy || Trades.Any()) {
            _buyLevel.RateEx = wave.Max(r => r.PriceAvg);
            _sellLevel.RateEx = wave.Min(r => r.PriceAvg);
          }
          _buyLevel.CanTrade = _sellLevel.CanTrade = (_buyLevel.Rate - _sellLevel.Rate) > RatesHeight * CorridorDistanceRatio;
        } else {
          var offset = TradingDistanceFunction == TradingMacroTakeProfitFunction.BuySellLevels ? RatesStDev : TradingDistance;
          if (_buyLevel.TradesCount == 0 && _sellLevel.TradesCount == -1) {
            _sellLevel.RateEx = _buyLevel.Rate - offset;
            _buyLevel.TradesCount = -1;
          }
          if (_buyLevel.TradesCount == -1 && _sellLevel.TradesCount == 0) {
            _buyLevel.RateEx = _sellLevel.Rate + offset;
            _sellLevel.TradesCount = -1;
          }
        }
      };
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
        ReloadPairStats();
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _buyLevelRate = _sellLevelRate = double.NaN;
        _isSelfStrategy = true;
        ForEachSuppRes(sr => sr.InManual = false);
        ForEachSuppRes(sr => sr.ResetPricePosition());
        ForEachSuppRes(sr => sr.ClearCrossedHandlers());

        _buyLevel.CanTrade = _sellLevel.CanTrade = IsAutoStrategy;
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
        buyCloseLevel.Crossed += (s, e) => { if (e.Direction == -1) exitCrossHandler(); };
        sellCloseLevel.Crossed += (s, e) => { if (e.Direction == 1) exitCrossHandler(); };
        EventHandler<EventArgs> tc = (s, e) => {
          if (CorridorCrossesMaximum > 0 && _buyLevel.TradesCount.Min(_sellLevel.TradesCount) < -CorridorCrossesMaximum) {
            _isCorridorOk.Value = ratesMedian();
            _buySellLevelsForEach(sr => sr.TradesCount = CorridorCrossesMaximum);
          }
        };
        _buySellLevelsForEach(sr => sr.TradesCountChanged += tc);
        _isCorridorAreaOk = new ObservableValue<bool>(false);
        _isCorridorAreaOk.ValueChanged += (s, e) => {
          if (_isCorridorAreaOk.Value) {
            _corridorArea.Value = ratesMedian();
            _buySellLevelsForEach(sr => { sr.TradesCount = 0; sr.CanTrade = true; });
          }
        };
        WaveShort.ClearDistance();
        CloseAtZero = _trimAtZero;
        _strategyExecuteOnTradeClose = t => {
          ForEachSuppRes(sr => sr.InManual = false);
          CmaLotSize = 0;
          _isCorridorOk.Value = ratesMedian();
          _isCorridorOk.HasChanged = false;
          if (isCurrentGrossOk()) {
            if (!IsAutoStrategy) {
              IsTradingActive = false;
            }
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
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

      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      if (IsAutoStrategy) {
        if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()
            || CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) {
          CloseTrades();
          return;
        }
      }
      double als = AllowedLotSizeCore();
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (!IsAutoStrategy && IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (Trades.Lots() / als > ProfitToLossExitRatio || als == LotSize && Trades.Lots() > LotSize)
        _trimAtZero = true;
      #endregion

      #region Run
      _buySellLevelsForEach(sr => sr.SetPrice(RateLast.PriceAvg));
      adjustLevels();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        if (!Trades.Any()) {
          buyCloseLevel.Rate = _RatesMin - RatesHeight;
          buyCloseLevel.ResetPricePosition();
          sellCloseLevel.Rate = _RatesMax + RatesHeight;
          sellCloseLevel.ResetPricePosition();
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
          buyCloseLevel.SetPrice(RateLast.PriceAvg);
          sellCloseLevel.SetPrice(RateLast.PriceAvg);
        }
      }
      return;
      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        if (!Trades.Any()) {
          buyCloseLevel.Rate = _RatesMin - RatesStDev;
          sellCloseLevel.Rate = _RatesMax + RatesStDev;
        } else {
          var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
          buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
          sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
        }
      }
      #endregion
    }

    private void StrategyEnterMiddleMan() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      var _buySellLevels = new[] { _buyLevel, _sellLevel }.ToList();
      Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
      var buyCloseLevel = Support1();
      var sellCloseLevel = Resistance1();

      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      IList<Rate> _massRates = null;
      Func<IList<Rate>> massRates = () => { return _massRates ?? (_massRates = RatesArray.LastBCs(10).ToArray()); };
      Func<bool> isMassOk = () => massRates().Max(r => r.Mass.GetValueOrDefault(double.NaN)) > MassLevelMax;
      Func<double> ratesMedian = () => (WaveTradeStart.RatesMax + WaveTradeStart.RatesMin) / 2;

      Func<IEnumerable<double>> priceHikes = () => WaveTradeStart.Rates.PriceHikes().DefaultIfEmpty(double.NaN);

      Func<double> priceHike = () => {
        var ph = 0.0;
        WaveShort.Rates.Take(5).Aggregate((p, n) => { ph = ph.Max((p.PriceAvg - n.PriceAvg).Abs()); return n; });
        return ph;
      };
      #region adjustLevels
      Action adjustLevels = () => {
        if (!WaveTradeStart.HasRates) return;
        var pricePosition = RateLast.PriceAvg.PositionByMiddle(WaveTradeStart.RatesMax, WaveTradeStart.RatesMin);
        var wave = WaveShort.Rates.Skip(WaveTradeStart.Rates.Count);
        var waveMax = wave.PriceMax();
        var waveMin = wave.PriceMin();
        var waveHeight = waveMax-waveMin;
        var isExpanded = waveMax <= WaveTradeStart.RatesMax && waveMin >= WaveTradeStart.RatesMin || WaveTradeStart.RatesHeight > waveHeight * 1.5;
        SetBuySellLevels(pricePosition > 0.5, waveMax, waveMin);
        if (TrailingDistanceFunction == TrailingWaveMethod.WaveTrade) {
          if (Trades.HaveBuy()) {
            _buyLevel.RateEx = waveMax;
            _sellLevel.RateEx = WaveTradeStart.RatesMin;
          } else if (Trades.HaveSell()) {
            _sellLevel.RateEx = waveMin;
            _buyLevel.RateEx = WaveTradeStart.RatesMax;
          } else if (HasPendingEntryOrders) {
            _buyLevel.RateEx = _buyLevelRate;
            _sellLevel.RateEx = _sellLevelRate;
          } else if (IsAutoStrategy && !Trades.Any()) {
            _buyLevel.RateEx = waveMax;
            _sellLevel.RateEx = waveMin;
          }

          var stDevByHeight = CorridorStats.StDevByHeight;
          var stDevByPriceAvg = CorridorStats.StDevByPriceAvg;
          var isInCorridor = stDevByHeight > stDevByPriceAvg;
          var isOutOfArea = (_isCorridorOk.Value - ratesMedian()).Abs() > TradingDistance;
          if ((isExpanded) /*&& isOutOfArea*/)
            _isCorridorOk.Value = ratesMedian();
          if (_isCorridorOk.HasChanged) {
            _isCorridorOk.HasChanged = false;
            if (IsAutoStrategy)
              _buySellLevelsForEach(sr => sr.CanTrade = true);
            _buySellLevelsForEach(sr => sr.TradesCount = 0);
          }
          if (stDevByHeight > stDevByPriceAvg)
            _buySellLevelsForEach(sr => sr.TradesCount = CorridorCrossesMaximum);
          if (stDevByHeight > stDevByPriceAvg || CorridorCrossesMaximum > 0 && _buyLevel.TradesCount.Min(_sellLevel.TradesCount) < -CorridorCrossesMaximum)
            _buySellLevelsForEach(sr => sr.TradesCount = CorridorCrossesMaximum);
        } else if (TrailingDistanceFunction == TrailingWaveMethod.WaveLeft) {
          var tradeOk = WaveAverageIteration > 0
            ? WaveShort.Rates.Count / WaveTradeStart.Rates.Count >= WaveAverageIteration
            : WaveShort.Rates.Count / WaveTradeStart.Rates.Count <= WaveAverageIteration.Abs();
          if (tradeOk) {
            _buyLevel.RateEx = WaveTradeStart.RatesMax;
            _sellLevel.RateEx = WaveTradeStart.RatesMin;
          }
          _buySellLevelsForEach(sr => { if (!sr.InManual) sr.CanTrade = tradeOk; });

          //var start = TradingDistance;
          //var tradeOk = (_buyLevel.Rate - _sellLevel.Rate).Between(start/WaveAverageIteration, start * WaveAverageIteration) && WaveShort.Rates.Count - WaveTradeStart.Rates.Count >= CorridorDistanceRatio;
        } else if (TrailingDistanceFunction == TrailingWaveMethod.WaveRight) {
          if (WaveTradeStart.RatesHeight < PointSize && WaveShort.RatesHeight < RatesStDev) {
            _buyLevel.RateEx = WaveShort.RatesMax;
            _sellLevel.RateEx = WaveShort.RatesMin;
            _buySellLevelsForEach(sr => { if (!sr.InManual) sr.CanTrade = true; });
          }
        } else if (TrailingDistanceFunction == TrailingWaveMethod.WaveShort) {
          if (!isExpanded) {
            _buyLevel.RateEx = waveMax;
            _sellLevel.RateEx = waveMin;
            _buySellLevelsForEach(sr => { if (!sr.InManual) sr.CanTrade = true; });
          }
          if ((WaveShortLeft.HasRates ? WaveShortLeft.RatesHeight : double.NaN).Max(_buyLevel.Rate - _sellLevel.Rate) > TradingDistance * 2)
            _buySellLevelsForEach(sr => { if (!sr.InManual) sr.CanTrade = false; });
          //_buyLevel.CanTrade = _sellLevel.CanTrade = (_buyLevel.Rate - _sellLevel.Rate) > RatesHeight * CorridorDistanceRatio;
        } else {
          var offset = TradingDistanceFunction == TradingMacroTakeProfitFunction.BuySellLevels ? RatesStDev : TradingDistance;
          if (_buyLevel.TradesCount == 0 && _sellLevel.TradesCount == -1) {
            _sellLevel.RateEx = _buyLevel.Rate - offset;
            _buyLevel.TradesCount = -1;
          }
          if (_buyLevel.TradesCount == -1 && _sellLevel.TradesCount == 0) {
            _buyLevel.RateEx = _sellLevel.Rate + offset;
            _sellLevel.TradesCount = -1;
          }
        }
      };
      #endregion
      #endregion

      #region Init
      #region SuppRes Event Handlers
      Func<SuppRes, bool> suppResCanTrade = (sr) => sr.CanTrade && sr.TradesCount <= 0 && !HasTradesByDistance(sr.IsBuy);
      Action<SuppRes, bool> enterCrossHandler = (suppRes, isBuy) => {
        if (!IsTradingActive) return;
        var lot = Trades.IsBuy(!isBuy).Lots();
        if (suppResCanTrade(suppRes)) {
          var ha = priceHikes().Average();
          if (ha > SpreadForCorridor) {
            lot += AllowedLotSizeCore();
          } else
            _buySellLevelsForEach(sr => sr.ResetPricePosition());
        }
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
        ReloadPairStats();
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _buyLevelRate = _sellLevelRate = double.NaN;
        _isSelfStrategy = true;
        ForEachSuppRes(sr => sr.InManual = false);
        ForEachSuppRes(sr => sr.ResetPricePosition());
        ForEachSuppRes(sr => sr.ClearCrossedHandlers());

        _buyLevel.CanTrade = _sellLevel.CanTrade = IsAutoStrategy;
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
        WaveShort.ClearDistance();
        CloseAtZero = _trimAtZero;
        var turnOffOnProfit = TrailingDistanceFunction == TrailingWaveMethod.WaveRight;
        _strategyExecuteOnTradeClose = t => {
          ForEachSuppRes(sr => sr.InManual = false);
          CmaLotSize = 0;
          _isCorridorOk.Value = ratesMedian();
          _isCorridorOk.HasChanged = false;
          if (isCurrentGrossOk()) {
            if (!IsAutoStrategy) 
              IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
            if (turnOffOnProfit)
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
      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      if (!CorridorStats.Rates.Any()) return;
      #endregion

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      if (buyCloseLevel.TradesCount != 9 || sellCloseLevel.TradesCount != 9)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      Action exitAction = () => {
        if (IsAutoStrategy) {
          if (RateLast.PriceAvg > _buyLevel.Rate && Trades.HaveSell()
              || RateLast.PriceAvg < _sellLevel.Rate && Trades.HaveBuy()) {
            CloseTrades();
            return;
          }
        }
        double als = AllowedLotSizeCore();
        if (IsEndOfWeek()) {
          if (Trades.Any())
            CloseTrades(Trades.Lots());
        }
        if (!IsAutoStrategy && IsEndOfWeek() || !IsTradingHour()) {
          _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          return;
        }
        var howFarFromSell = Trades.HaveBuy() ? RateLast.PriceAvg - _sellLevel.Rate : double.NaN;
        var howFarFromBuy = Trades.HaveSell() ? -RateLast.PriceAvg + _buyLevel.Rate : double.NaN;
        var isFarFromClose = howFarFromSell.Max(howFarFromBuy) > TradingDistance * 2;
        if (CurrentGross > 0 && isFarFromClose)
          CloseAtZero = true;
        else if (Trades.Lots() / als > ProfitToLossExitRatio
                || (CurrentGross > 0 ||isFarFromClose) && als == LotSize && Trades.Lots() > LotSize
          )
          _trimAtZero = true;
      };
      #endregion

      #region Run
      //SetMassMaxMin();
      _buyLevel.SetPrice(RateLast.PriceAvg);
      _sellLevel.SetPrice(RateLast.PriceAvg);
      adjustLevels();
      exitAction();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        if (!Trades.Any()) {
          buyCloseLevel.RateEx = _RatesMin - RatesHeight;
          buyCloseLevel.ResetPricePosition();
          sellCloseLevel.RateEx = _RatesMax + RatesHeight;
          sellCloseLevel.ResetPricePosition();
        } else {
          var ph = priceHikes().DefaultIfEmpty(double.NaN).Average(d => d);
          var tpRatioBySpread = ph.Max(SpreadForCorridor) / SpreadForCorridor.Min(ph);
          var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : (TakeProfitPips / tpRatioBySpread) + CurrentLossInPips.Min(0).Abs()));
          var priceAvgMax = RateLast.PriceAvg.Max(RatePrev.PriceAvg) - PointSize;
          var priceAvgMin = RateLast.PriceAvg.Min(RatePrev.PriceAvg) + PointSize;
          if (buyCloseLevel.InManual) {
            if (buyCloseLevel.Rate <= priceAvgMax)
              buyCloseLevel.Rate = priceAvgMax;
          } else
            buyCloseLevel.RateEx = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? priceAvgMax : double.NaN);
          if (sellCloseLevel.InManual) {
            if (sellCloseLevel.Rate >= priceAvgMin)
              sellCloseLevel.Rate = priceAvgMin;
          } else
            sellCloseLevel.RateEx = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? priceAvgMin : double.NaN);
          try {
            buyCloseLevel.SetPrice(RateLast.PriceAvg);
            sellCloseLevel.SetPrice(RateLast.PriceAvg);
          } catch (ArgumentException exc) {
            Log = exc;
          }
        }
      }
      return;
      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        if (!Trades.Any()) {
          buyCloseLevel.Rate = _RatesMin - RatesStDev;
          sellCloseLevel.Rate = _RatesMax + RatesStDev;
        } else {
          var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
          buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
          sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
        }
      }
      #endregion
    }

    double _buyLevelNetOpen() { return Trades.IsBuy(true).NetOpen(_buyLevel.Rate); }
    double _sellLevelNetOpen() { return Trades.IsBuy(false).NetOpen(_sellLevel.Rate); }
    Action _adjustLevels = () => { throw new NotImplementedException(); };
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
        var watcherSetCorridor = new ObservableValue<bool>(false, true);
        var watcherCanTrade = new ObservableValue<bool>(true, true);
        var watcherWaveTrade = new ObservableValue<bool>(false, true);
        var watcherWaveStart = new ObservableValue<DateTime>(DateTime.MinValue);
        var corridorLengthOld = 0.0;
        _adjustLevels = () => {
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
                } 
              }
              break;
            #endregion
            #region WaveMin
            case TrailingWaveMethod.WaveMin: {
                if (firstTime)
                  _buySellLevelsForEach(sr => sr.CanTrade = false);
                var canTradeHeight = (CorridorStats.StDevByPriceAvg + CorridorStats.StDevByHeight)*2;// Math.Sqrt(StDevByHeight * StDevByPriceAvg);
                var canTradeRatio = 1 + Fibonacci.FibRatioSign(WaveShort.Rates.Count, CorridorLength);
                if (watcherWaveTrade.SetValue(WaveTradeStart.RatesHeight > canTradeHeight * canTradeRatio).HasChanged) {
                  _buySellLevelsForEach(sr => sr.CanTrade = watcherWaveTrade.Value);
                } else if (!watcherWaveTrade.Value || corridorLengthOld < CorridorLength) {
                  _buyLevel.Rate = WaveTradeStart.RatesMax.Max(RateLast.PriceAvg) + PointSize;
                  _sellLevel.Rate = WaveTradeStart.RatesMin.Min(RateLast.PriceAvg) - PointSize;
                }
                corridorLengthOld = CorridorLength;
                var median = (WaveShort.RatesMax + WaveShort.RatesMin) / 2;
                var varaince = CorridorStats.StDevByPriceAvg + CorridorStats.StDevByHeight;
                _CenterOfMassBuy = median + varaince;
                _CenterOfMassSell = median - varaince;
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

        #region On Trade Close
        _strategyExecuteOnTradeClose = t => {
          ForEachSuppRes(sr => sr.InManual = false);
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (!IsAutoStrategy)
              IsTradingActive = false;
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;
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
      _adjustLevels();
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

    private void StrategyEnterMinimalist() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      Func<bool> isRatesRatioOk = () => RatesStDevToRatesHeightRatio < CorridorDistanceRatio;
      Func<double[]> tail = () => {
        var rateWave = WaveShort.Rates.LastBC();
        var index = (RatesArray.IndexOf(rateWave) - WaveShort.Rates.Count / 10.0).Max(0).ToInt();
        return RatesArray.Skip(index).TakeWhile(r => r != rateWave).Select(r => r.PriceAvg).ToArray();
      };
      Action<bool?> setLevels = isUp => {
        if (isUp.HasValue) {
          if (isUp.Value) {
            _sellLevel.Rate = WaveShort.RatesMin;
          } else {
            _buyLevel.Rate = WaveShort.RatesMax;
          }
          _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        }
      };
      Action adjustLevels = () => {
        if (RatesHeight < RatesHeightMinimum.Abs()) {
          _buyLevel.Rate = _RatesMax;
          _sellLevel.Rate = _RatesMin;
        } else {
          _buyLevel.Rate = _buyLevel.Rate.Min(_RatesMax);
          _sellLevel.Rate = _sellLevel.Rate.Max(_RatesMin);
        }

      };
      #endregion

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        CorridorStats = null;
        WaveHigh = null;
        SuppResLevelsCount = 2;
        LastProfitStartDate = null;
        CmaLotSize = 0;
        ShowTrendLines = false;
        WaveShort.LengthCma = double.NaN;
        WaveShort.ClearEvents();
        WaveShort.IsUpChanged += (s, e) => {
          setLevels(WaveShort.IsUp);
        };
        _isCorridorDistanceOk.ValueChanged += (s, e) => {
          if (_isCorridorDistanceOk.Value)
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
        };
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (IsInVitualTrading && isManual())
              IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          }
          if (CloseAtZero || isCurrentGrossOk())
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;
          CloseAtZero = _trimAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
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
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Exit
      double als = AllowedLotSizeCore();
      if (IsTradingActive) {
        if (!CloseAtZero && Trades.Lots() / als >= this.ProfitToLossExitRatio) 
          _trimAtZero = true;
        if (IsAutoStrategy) {
          if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
          if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
        }
      }
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (!CloseAtZero && !CloseOnProfitOnly) {
        var a = (LotSize / BaseUnitSize) * TradesManager.GetPipCost(Pair) * 100;
        var pipsOffset = (currentLoss() / a);
        var tpOk = TakeProfitFunction == TradingMacroTakeProfitFunction.BuySellLevels
          && Trades.GrossInPips() * PLToCorridorExitRatio > InPips(_buyLevel.Rate - _sellLevel.Rate);
        CloseAtZero = currentGrossInPips() > pipsOffset.Max(-3)
          && (Trades.Lots() > LotSize * 2 || tpOk /*|| !canTrade*/);
      }
      #endregion

      #region Run
      //EnsureCorridorStopRate();
      if ( WaveShort.Rates.Count < 10
          || IsBlackoutTime
          )
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      adjustLevels();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
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