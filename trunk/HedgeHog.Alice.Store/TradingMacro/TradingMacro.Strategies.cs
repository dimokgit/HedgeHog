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

    bool _closeAtZero;

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
      broadcastCorridorDatesChange.SendAsync(u => {
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
      });
    }
    partial void OnCorridorStartDateChanging(DateTime? value) {
      if (value == CorridorStartDate) return;
      _broadcastCorridorDateChanged();
    }

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
      if (ScanCorridorCustom != ScanCorridorByWaveRelative || _strategyExecuteOnTradeClose == null) {
        ScanCorridorCustom = ScanCorridorByWaveRelative;
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
          _closeAtZero = false;
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
      var waveQuickOk = WaveTradeStart.LastBC() >= CorridorStats.StopRate;
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
      if (!_closeAtZero) {
        _closeAtZero = currentGross() > 0
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
        var tpColse = InPoints((_closeAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
    }

    private bool EnsureCorridorStopRate() {
      if (!CorridorStartDate.HasValue && CorridorStopDate == DateTime.MinValue) {
        var waveIndex = _waves.TakeWhile(w => !CorridorStats.StartDate.Between(w.LastBC().StartDate, w[0].StartDate)).Count() + 1;
        var sd = waveIndex < StopRateWaveOffset ? RatesArray.LastBC() : _waves[waveIndex - StopRateWaveOffset].First();
        //if (CorridorStats.StopRate == null || sd > CorridorStats.StopRate)
        CorridorStats.StopRate = sd;
        //CorridorStats.Rates.Reverse().Skip(1).SkipWhile(r => r.PriceStdDev > 0).SkipWhile(r => r.PriceStdDev == 0).SkipWhile(r => r.PriceStdDev > 0).SkipWhile(r => r.PriceStdDev == 0).SkipWhile(r => r.PriceStdDev > 0).DefaultIfEmpty(CorridorStats.Rates[0]).First();
      }
      if (CorridorStats.StopRate == null) {
        Log = new Exception("CorridorStats.StopRate is null.");
        return false;
      }

      var corridorDistance = CorridorStats.Rates.LastBC().Distance - CorridorStats.StopRate.Distance;
      WaveTradeStart = RatesArray.ReverseIfNot().TakeWhile(r => r.Distance <= corridorDistance)/*.TakeEx(-WaveLength * 2)*/.ToArray();
      return true;
    }

    private void StrategyEnterTrailer() {
      if (!RatesArray.Any()) return;

      #region Local globals
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      Func<double> currentGross = () => TradingStatistics.CurrentGross;
      Func<double> currentGrossInPips = () => TradingStatistics.CurrentGrossInPips;
      Func<double> currentLoss = () => TradingStatistics.CurrentLoss;
      #endregion

      #region Init
      if (ScanCorridorCustom != ScanCorridorByWaveRelative || _strategyExecuteOnTradeClose == null) {
        ScanCorridorCustom = ScanCorridorByWaveRelative;
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
          _closeAtZero = false;
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
      var waveQuickOk = WaveTradeStart.LastBC() >= CorridorStats.StopRate;
      bool canTrade = CorridorStats.Rates.LastBC().Distance <= WaveTradeStart.LastBC().Distance * CorridorDistanceRatio;
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
      if (!_closeAtZero) {
        var a = (LotSize / BaseUnitSize) * TradesManager.GetPipCost(Pair) * 100;
        var pipsOffset = currentLoss() / a;
        _closeAtZero = currentGrossInPips() > pipsOffset
          && (Trades.Lots() > LotSize || Trades.GrossInPips() * 2 > InPips(_buyLevel.Rate - _sellLevel.Rate) || !canTrade);
      }
      #endregion

      #region Run

      var ratesForTrade = RatesArray.ReverseIfNot().TakeWhile(r => r.Distance <= WaveShort.Distance.IfNaN(WaveDistance)).OrderBy(r => r.PriceAvg).ToArray();
      MagnetPrice = WaveTradeStart.Middle();
      if (MagnetPrice.Between(RateLast.PriceLow, RateLast.PriceHigh))
        _buyLevel.TradesCount = _sellLevel.TradesCount = 0;

      _buyLevel.Rate = ratesForTrade.LastBC().PriceAvg;
      _sellLevel.Rate = ratesForTrade[0].PriceAvg;
      if (IsAutoStrategy)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade && waveQuickOk;

      if (CorridorStartDate.HasValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = TradingMacro.WaveInfo.RateByDistance(RatesArray, WaveDistance) >= CorridorStats.StopRate && waveQuickOk;


      if (IsAutoStrategy || CorridorStopDate > DateTime.MinValue)
        _buyLevel.CanTrade = _sellLevel.CanTrade = canTrade && waveQuickOk;

      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var tpColse = InPoints((_closeAtZero ? 0 : TakeProfitPips + CurrentLossInPips.Min(0).Abs()));
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      }
      #endregion
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
