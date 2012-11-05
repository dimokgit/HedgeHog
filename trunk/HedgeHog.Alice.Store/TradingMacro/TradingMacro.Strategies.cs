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


    private void StrategyEnterTrailer_01() {
      if (!RatesArray.Any() ) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGross = () => TradingStatistics.CurrentGross;
      Func<double> currentGrossInPips = () => TradesManager.MoneyAndLotToPips(currentGross(), LotSize, Pair);
      #endregion

      #region Init
      if ( _strategyExecuteOnTradeClose == null) {
        CorridorStats = null;
        WaveHigh = null;
        SuppResLevelsCount = 2;
        LastProfitStartDate = null;
        CmaLotSize = 0;
        ShowTrendLines = false;
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
            _buyLevel.TradesCount = _sellLevel.TradesCount = 1;
          }
          CloseAtZero = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
        return;
      }
      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion

      if (!CorridorStats.Rates.Any()) return;

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1();
      var sellCloseLevel = Resistance1();
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      if (!EnsureCorridorStopRate()) return;
      var waveQuickOk = WaveTradeStart.Rates.LastBC() >= CorridorStats.StopRate;
      bool canTrade = true;
      //var wave = TrailBuySellCorridor(buyCloseLevel, sellCloseLevel);

      if (!IsInVitualTrading && CorridorStats.StopRate != null) {
        CorridorStats.StopRate = RatesArray.FindBar(CorridorStats.StopRate.StartDate);
      }

      if (!CorridorStats.Rates.Any()) return;

      #region Exit
      double als = AllowedLotSizeCore(Trades);
      if (Trades.Lots() / als >= this.ProfitToLossExitRatio) {
        var lot = Trades.Lots() - als.ToInt();
        CloseTrades(lot);
        return;
      }
      if (IsEndOfWeek()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
      }
      if (IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (IsAutoStrategy) {
        if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
        if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
      }
      if (!CloseAtZero) {
        CloseAtZero = currentGross() > 0
          && (Trades.Lots() > LotSize || Trades.GrossInPips() * 2 > InPips(_buyLevel.Rate - _sellLevel.Rate) || !canTrade);
      }
      if (!canTrade)
        CloseTrades();
      #endregion

      #region Run

      var ratesForTrade = RatesArray.ReverseIfNot().TakeWhile(r => r.Distance <= WaveShort.Distance.IfNaN(WaveDistance)).OrderBy(r => r.PriceAvg).ToArray();
      MagnetPrice = ratesForTrade.Average(r => r.PriceAvg);
      if (MagnetPrice.Between(RateLast.PriceLow, RateLast.PriceHigh))
        _buyLevel.TradesCount = _sellLevel.TradesCount = 0;

      _buyLevel.Rate = ratesForTrade.LastBC().PriceAvg;
      _sellLevel.Rate = ratesForTrade[0].PriceAvg;
      if (IsAutoStrategy)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade && waveQuickOk;

      if (CorridorStartDate.HasValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = TradingMacro.WaveInfo.RateByDistance(RatesArray, WaveDistance) >= CorridorStats.StopRate &&  waveQuickOk;


      if (IsAutoStrategy || CorridorStopDate > DateTime.MinValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade && waveQuickOk;

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }

    private void StrategyEnterTrailer_02() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGross = () => TradingStatistics.CurrentGross;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
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
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            if (IsInVitualTrading && isManual())
              IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }
          if (CloseAtZero || isCurrentGrossOk() || !CloseOnProfitOnly)
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;
          CloseAtZero = false;
          _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
        return;
      }
      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion

      if (!CorridorStats.Rates.Any()) return;

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1();
      var sellCloseLevel = Resistance1();
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      if (!EnsureCorridorStopRate()) return;
      {
        var middle = (_RatesMax + _RatesMin) / 2;
        var corrHeight = RatesHeight / CorridorDistanceRatio;
        _CenterOfMassBuy = middle + corrHeight / 2;
        _CenterOfMassSell = middle - corrHeight / 2;
      }
      var waveTradeStartDate = WaveTradeStart.Rates.LastBC().StartDate;
      var waveQuickOk = true;// waveTradeStartDate >= CorridorStats.StopRate.StartDate;
      var corridorStartTimeSpan = CorridorStats.Rates.LastBC().StartDate - RateLast.StartDate;
      var ratesForTrade = RatesArray.SkipWhile(r => r.Distance > WaveShort.Distance.IfNaN(WaveDistance)).OrderBy(r => r.PriceAvg).ToArray();
      MagnetPrice = ratesForTrade.Sum(r => r.PriceAvg * r.PriceHeight) / ratesForTrade.Sum(r => r.PriceHeight);
      bool canTrade = isManual() || (
        (ratesForTrade.LastBC().PriceAvg - ratesForTrade[0].PriceAvg) < RatesHeight / 3
         && !MagnetPrice.Between(CenterOfMassSell, CenterOfMassBuy));
      //var wave = TrailBuySellCorridor(buyCloseLevel, sellCloseLevel);

      if (!IsInVitualTrading && CorridorStats.StopRate != null) {
        CorridorStats.StopRate = RatesArray.FindBar(CorridorStats.StopRate.StartDate);
      }

      if (!CorridorStats.Rates.Any()) return;

      #region Exit
      double als = AllowedLotSizeCore(Trades);
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
      if (!CloseAtZero) {
        var a = (LotSize / BaseUnitSize) * TradesManager.GetPipCost(Pair) * 100;
        var pipsOffset = currentLoss() / a;
        var tpOk = Trades.GrossInPips() * 2 > InPips(_buyLevel.Rate - _sellLevel.Rate);
        CloseAtZero = currentGrossInPips() > pipsOffset
          && (Trades.Lots() > LotSize || tpOk /*|| !canTrade*/);
        if (!CloseOnProfitOnly && Trades.GrossInPips() * PLToCorridorExitRatio > InPips(_buyLevel.Rate - _sellLevel.Rate))
          CloseAtZero = true;
      }
      #endregion

      #region Run

      ReArmLevelsTradeCount();
      if (!canTrade)
        _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;

      _buyLevel.Rate = ratesForTrade.LastBC().PriceAvg;
      _sellLevel.Rate = ratesForTrade[0].PriceAvg;
      if (IsAutoStrategy)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade && waveQuickOk;

      if (CorridorStartDate.HasValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = TradingMacro.WaveInfo.RateByDistance(RatesArray, WaveDistance) >= CorridorStats.StopRate && waveQuickOk;


      if (IsAutoStrategy || CorridorStopDate > DateTime.MinValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade && waveQuickOk;

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }


    private bool EnsureCorridorStopRate() {
      bool up;
      return EnsureCorridorStopRate(out up);
    }
    private bool EnsureCorridorStopRate(out bool up) {
      up = false;
      if (!CorridorStartDate.HasValue && CorridorStopDate == DateTime.MinValue) {
        //var waves = _waves.Where(w => w.MaxStDev() > StDevAverages.LastBC() / 2).ToArray();
        var waveIndex = _waves.TakeWhile(w => !CorridorStats.StartDate.Between(w.LastBC().StartDate, w[0].StartDate)).Count() + 1;
        var wave = waveIndex < StopRateWaveOffset ? WaveHigh : _waves[waveIndex - StopRateWaveOffset];
        var sd = wave.GetWaveStopRate(out up);
        //var sd = waveIndex < StopRateWaveOffset ? RatesArray.LastBC() : _waves[waveIndex - StopRateWaveOffset].First();
        //if (CorridorStats.StopRate == null || sd > CorridorStats.StopRate)
        CorridorStats.StopRate = sd;
        //CorridorStats.Rates.Reverse().Skip(1).SkipWhile(r => r.PriceStdDev > 0).SkipWhile(r => r.PriceStdDev == 0).SkipWhile(r => r.PriceStdDev > 0).SkipWhile(r => r.PriceStdDev == 0).SkipWhile(r => r.PriceStdDev > 0).DefaultIfEmpty(CorridorStats.Rates[0]).First();
      }
      if (CorridorStats.StopRate == null) {
        Log = new Exception("CorridorStats.StopRate is null.");
        return false;
      }

      var corridorDistance = Strategy.HasFlag(Strategies.FreeRoam) ? WaveDistanceForTrade : CorridorStats.Rates.LastBC().Distance - CorridorStats.StopRate.Distance;
      WaveTradeStart = new WaveInfo(this) { Rates = RatesArray.SkipWhile(r => r.Distance > corridorDistance)/*.TakeEx(-WaveLength * 2)*/.ToArray() };

      var waveTradeStartDate = WaveTradeStart.Rates.LastBC().StartDate;
      var corridorStartTimeSpan = CorridorStats.Rates.LastBC().StartDate - RateLast.StartDate;

      return true;
    }

    Models.ObservableValue<bool> _isCorridorDistanceOk = new Models.ObservableValue<bool>(false);

    private void StrategyEnterTrailerFreeRoaming() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGross = () => TradingStatistics.CurrentGross;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
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
        WaveShort.StartDateChanged += (s, e) => { 
          var length = (e.New-e.Old).Minutes/BarPeriodInt;
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
          }
          if (CloseAtZero || isCurrentGrossOk())
            _buyLevel.TradesCount = _sellLevel.TradesCount = 1;
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

      #region Set Trade Levels
      if (!EnsureCorridorStopRate()) return;
      //var ratesForTrade = RatesArray.SkipWhile(r => r.Distance > WaveShort.Distance.IfNaN(WaveDistance)).OrderBy(r => r.PriceAvg).ToArray();
      _buyLevel.Rate = TrailingWave.RatesMax;
      _sellLevel.Rate = TrailingWave.RatesMin;
      var canTrade = 
        (CorridorDistanceRatio == 0 
         || (CorridorDistanceRatio >= 1
            ? WaveShort.Rates.Count >= WaveTradeStart.Rates.Count * CorridorDistanceRatio
            : WaveShort.Rates.Count <= WaveTradeStart.Rates.Count * CorridorDistanceRatio)
        )
        && WaveShort.Rates.Count > WaveLength;
      if (IsInVitualTrading)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade;
      #endregion

      #region Sanity check
      if (!IsInVitualTrading && CorridorStats.StopRate != null) {
        CorridorStats.StopRate = RatesArray.FindBar(CorridorStats.StopRate.StartDate);
      }
      #endregion

      #region Exit
      double als = AllowedLotSizeCore(Trades);
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
      if (!CloseAtZero) {
        var a = (LotSize / BaseUnitSize) * TradesManager.GetPipCost(Pair) * 100;
        var pipsOffset = (currentLoss() / a);
        var tpOk = Trades.GrossInPips() * PLToCorridorExitRatio > InPips(_buyLevel.Rate - _sellLevel.Rate);
        CloseAtZero = currentGrossInPips() > pipsOffset.Max(-3)
          && (Trades.Lots() > LotSize || tpOk /*|| !canTrade*/);
        if ((!CloseOnProfitOnly || pipsOffset < -10) && tpOk)
          CloseAtZero = true;
      }
      #endregion

      #region Run

      ReArmLevelsTradeCount();
 
      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        var buyRate = Trades.HaveBuy() && Trades.Gross() < 0 ? WaveTradeStart.RatesMin : double.NaN;
        buyCloseLevel.Rate = buyRate.IfNaN((buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN));
        var sellRate = Trades.HaveSell() && Trades.Gross() < 0 ? WaveTradeStart.RatesMax : double.NaN;
        sellCloseLevel.Rate = sellRate.IfNaN((sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN));
      }
      #endregion
    }

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
      double als = AllowedLotSizeCore(Trades);
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

    private void StrategyEnterAfterWaver() {
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
          var rateWave = WaveShort.Rates.LastBC();
          var index = (RatesArray.IndexOf(rateWave)-WaveShort.Rates.Count/10.0).Max(0).ToInt();
          var tail = RatesArray.Skip(index).TakeWhile(r => r != rateWave).Select(r=>r.PriceAvg).ToArray();
          if (isUp.Value) {
            _sellLevel.Rate = WaveShort.RatesMin.Min(tail.Min());
          } else {
            _buyLevel.Rate = WaveShort.RatesMax.Max(tail.Max());
          }
          _buyLevel.CanTrade = _sellLevel.CanTrade = true;
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
      double als = AllowedLotSizeCore(Trades);
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
      if (WaveShort.Rates.Count < 10
          || RatesHeight < RatesHeightMinimum.Abs())
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;

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
      double als = AllowedLotSizeCore(Trades);
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
      double als = AllowedLotSizeCore(Trades);
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
      double als = AllowedLotSizeCore(Trades);
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
      double als = AllowedLotSizeCore(Trades);
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

    private void StrategyEnterDistancerManual() {
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
        if (_buyLevel.TradesCount == 0 && _sellLevel.TradesCount == -1) {
          _sellLevel.Rate = _buyLevel.Rate - RatesStDev;
          _buyLevel.TradesCount = -1;
        }
        if (_buyLevel.TradesCount == -1 && _sellLevel.TradesCount == 0) {
          _buyLevel.Rate = _sellLevel.Rate + RatesStDev;
          _sellLevel.TradesCount = -1;
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
            IsTradingActive = false;
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
      double als = AllowedLotSizeCore(Trades);
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
      if (!IsAutoStrategy && IsEndOfWeek() || !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        return;
      }
      if (Trades.Lots() / als > ProfitToLossExitRatio)
        _trimAtZero = true;
      #endregion

      #region Run
      //SetMassMaxMin();
      adjustLevels();

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero || _trimAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
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
      double als = AllowedLotSizeCore(Trades);
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

    private void StrategyEnterTrailer() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isManual = () => CorridorStartDate.HasValue || _isCorridorStopDateManual;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGross = () => TradingStatistics.CurrentGross;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
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
        _isCorridorDistanceOk.ValueChanged += (s, e) => {
          if (_isCorridorDistanceOk.Value)
            _buyLevel.TradesCount = _sellLevel.TradesCount = 0;
        };
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            if (IsInVitualTrading && isManual())
              IsTradingActive = false;
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            WaveShort.ClearDistance();
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }
          if (CloseAtZero || isCurrentGrossOk() || !CloseOnProfitOnly)
            _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;
          CloseAtZero = false;
          _trimAtZero = false;
          _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        };
        _strategyExecuteOnTradeOpen = () => { };
        return;
      }
      if (!IsInVitualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion

      if (!CorridorStats.Rates.Any()) return;

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1();
      var sellCloseLevel = Resistance1();
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      bool up;
      if (!EnsureCorridorStopRate(out up)) return;
      var countMin = _waves.Select(w => (double)w.Count).ToArray().AverageByIterations(-1).Average().ToInt();
      var distanceMin = new[] { RatesArray.LastBC(countMin), CorridorStats.StopRate }.Min().Distance;
      var ratesForTrade = RatesArray.SkipWhile(r => r.Distance > WaveShort.Distance.IfNaN(WaveDistance.Min(distanceMin))).OrderBy(r => r.PriceAvg).ToArray();

      MagnetPrice = ratesForTrade.Sum(r => r.PriceAvg / r.PriceHeight) / ratesForTrade.Sum(r => 1 / r.PriceHeight);
      _buyLevel.Rate = ratesForTrade.LastBC().PriceAvg;
      _sellLevel.Rate = ratesForTrade[0].PriceAvg;
      var tradeCorridor = _buyLevel.Rate - _sellLevel.Rate;
      
      bool canTrade = isManual() || (
        tradeCorridor.Between(CorridorStats.RatesStDev, RatesHeight / 3)
        && (_isCorridorDistanceOk.Value = (CorridorStats.StopRate.Distance > WaveDistance * CorridorDistanceRatio))
        );
      //var wave = TrailBuySellCorridor(buyCloseLevel, sellCloseLevel);

      if (!IsInVitualTrading && CorridorStats.StopRate != null) {
        CorridorStats.StopRate = RatesArray.FindBar(CorridorStats.StopRate.StartDate);
      }

      if (!CorridorStats.Rates.Any()) return;

      #region Exit
      double als = AllowedLotSizeCore(Trades);
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
      if (!CloseAtZero) {
        var a = (LotSize / BaseUnitSize) * TradesManager.GetPipCost(Pair) * 100;
        var pipsOffset = currentLoss() / a;
        var tpOk = Trades.GrossInPips() * 2 > InPips(_buyLevel.Rate - _sellLevel.Rate);
        CloseAtZero = currentGrossInPips() > pipsOffset
          && (Trades.Lots() > LotSize || tpOk /*|| !canTrade*/);
        if (!CloseOnProfitOnly && Trades.GrossInPips() * PLToCorridorExitRatio > InPips(_buyLevel.Rate - _sellLevel.Rate))
          CloseAtZero = true;
      }
      #endregion

      #region Run

      ReArmLevelsTradeCount();
      if (!canTrade)
        _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesMaximum;

      //if (up) {
      //  _buyLevel.Rate = _buyLevel.Rate.Max(CorridorStats.StopRate.PriceHigh);
      //  _sellLevel.Rate = _sellLevel.Rate.Min(_buyLevel.Rate - CorridorStats.RatesStDev);
      //}else {
      //  _sellLevel.Rate = _sellLevel.Rate.Min(CorridorStats.StopRate.PriceLow);
      //  _buyLevel.Rate = _buyLevel.Rate.Max(_sellLevel.Rate + CorridorStats.RatesStDev);
      //}

      if (IsAutoStrategy)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade;

      if (CorridorStartDate.HasValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = TradingMacro.WaveInfo.RateByDistance(RatesArray, WaveDistance) >= CorridorStats.StopRate;


      if (IsAutoStrategy || CorridorStopDate > DateTime.MinValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade;

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((CloseAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
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
    public double VoltageHight { get { return _VoltageHight; } set { _VoltageHight = value; } }
  }
}
