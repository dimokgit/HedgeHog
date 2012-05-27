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

    private void StrategyEnterRange09() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        ScanCorridorCustom = ScanCorridorByQuickWave09;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentGross >= 0) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            _waveHighOnOff.TurnOff();
            _tradeConditioner911.Clear();
          }
        };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      #region Exit
      var quickIndex = _waves.Reverse().TakeWhile(w => w[0] > WaveQuick[0]).Count();
      var inSpike = _waves.TakeEx(-2).Any(w => w[0].PriceStdDev > StDevAverages.TakeEx(-2).First());
      if (inSpike) {
        if (CurrentGrossInPips > TakeProfitPips)
          CloseTrades(Trades.Lots() - LotSize);
        if (CurrentGross > 0)
          CloseTrades(Trades.Lots() - LotSize);
      }
      #endregion

      #region Suppres levels
      _buyLevel = Resistance0();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1(); buyCloseLevel.TradesCount = 9;
      var sellCloseLevel = Resistance1(); sellCloseLevel.TradesCount = 9;
      #endregion

      #region Run
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      if (isAuto && quickIndex.Between(1, 3) && _waveLast.HasChanged(WaveQuick, _waveLast.HasChanged2)) {
          _buyLevel.Rate = RateLast.AskHigh;
          _sellLevel.Rate = RateLast.BidLow;
        var up = WaveQuick[0].PriceAvg < WaveQuick.LastByCount().PriceAvg;
        var ratesForClose = _waves.TakeEx(-(quickIndex + 2));
        _buyLevel.Rate = _buyLevel.Rate.Max(WaveQuick.LastByCount().AskHigh, RateLast.AskHigh);
        _sellLevel.Rate = _sellLevel.Rate.Min(WaveQuick.LastByCount().BidLow, RateLast.BidLow);
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        _buyLevel.TradesCount = !up ? 0 : CorridorCrossesCountMinimum;
        _sellLevel.TradesCount = up ? 0 : CorridorCrossesCountMinimum;
      } else if (Trades.Any()) {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
      }
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    private void StrategyEnterRange091() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        ScanCorridorCustom = ScanCorridorByQuickWave09;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentGross >= 0) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            _waveHighOnOff.TurnOff();
            _tradeConditioner911.Clear();
          }
        };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      #region Exit
      var quickIndex = _waves.Reverse().TakeWhile(w => w[0] > WaveQuick[0]).Count();
      var inSpike = _waves.TakeEx(-2).Any(w => w[0].PriceStdDev > StDevAverages.TakeEx(-2).First());
      if (inSpike) {
        if (CurrentGrossInPips > TakeProfitPips)
          CloseTrades(Trades.Lots() - LotSize);
        if (CurrentGross > 0)
          CloseTrades(Trades.Lots() - LotSize);
      }
      StrategyExitByGross061();
      #endregion

      #region Suppres levels
      _buyLevel = Resistance0();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1(); buyCloseLevel.TradesCount = 9;
      var sellCloseLevel = Resistance1(); sellCloseLevel.TradesCount = 9;
      #endregion

      #region Run
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      if (isAuto && quickIndex.Between(1, 3) && _waveLast.HasChanged(WaveQuick, _waveLast.HasChanged2)) {
        var up = WaveQuick[0].PriceAvg < WaveQuick.LastByCount().PriceAvg;
        SetMagnetPrice();
        var rateNode = new LinkedList<Rate>(_waveLast.Rates).First;
        while (rateNode != null) {
          if (Math.Sign(rateNode.Value.PriceAvg - MagnetPrice) != Math.Sign(rateNode.Next.Value.PriceAvg - MagnetPrice))
            break;
          rateNode = rateNode.Next;
        }
        var ratesOpen = _waveLast.Rates.SkipWhile(r => r < rateNode.Value).OrderBy(r => r.PriceAvg).ToArray();
        _buyLevel.Rate = up ? ratesOpen.LastByCount().PriceHigh : MagnetPrice;
        _sellLevel.Rate = !up ? ratesOpen[0].PriceLow : MagnetPrice;
        _buyLevel.Rate = _buyLevel.Rate.Max(WaveQuick.LastByCount().AskHigh, RateLast.AskHigh);
        _sellLevel.Rate = _sellLevel.Rate.Min(WaveQuick.LastByCount().BidLow, RateLast.BidLow);
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        _buyLevel.TradesCount = !up ? 0 : CorridorCrossesCountMinimum;
        _sellLevel.TradesCount = up ? 0 : CorridorCrossesCountMinimum;
      } else if (Trades.Any()) {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
      }
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    private void StrategyEnterRange092() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        ScanCorridorCustom = ScanCorridorByBigWave09;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentGross >= 0) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            _waveHighOnOff.TurnOff();
            _tradeConditioner911.Clear();
          }
        };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      #region Exit
      var inSpike = _waves.TakeEx(-2).Any(w => w[0].PriceStdDev > StDevAverages.TakeEx(-2).First());
      if (inSpike) {
        if (CurrentGrossInPips > TakeProfitPips)
          CloseTrades(Trades.Lots() - LotSize);
        if (CurrentGross > 0)
          CloseTrades(Trades.Lots() - LotSize);
      }
      StrategyExitByGross061();
      #endregion

      #region Suppres levels
      _buyLevel = Resistance0();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1(); buyCloseLevel.TradesCount = 9;
      var sellCloseLevel = Resistance1(); sellCloseLevel.TradesCount = 9;
      #endregion

      #region Run
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var waveIndex = _waves.Reverse().TakeWhile(w => w[0] > WaveHigh[0]).Count();
      if (isAuto && waveIndex.Between(1, 3) && _waveLast.HasChanged(WaveHigh, _waveLast.HasChanged2)) {
        var up = _waveLast.Rates[0].PriceAvg < _waveLast.Rates.LastByCount().PriceAvg;
        SetMagnetPrice();
        var rateNode = new LinkedList<Rate>(_waveLast.Rates).First;
        while (rateNode != null) {
          if (Math.Sign(rateNode.Value.PriceAvg - MagnetPrice) != Math.Sign(rateNode.Next.Value.PriceAvg - MagnetPrice))
            break;
          rateNode = rateNode.Next;
        }
        var ratesOpen = _waveLast.Rates.SkipWhile(r => r < rateNode.Value).OrderBy(r => r.PriceAvg).ToArray();
        _buyLevel.Rate = up ? ratesOpen.LastByCount().PriceHigh : MagnetPrice;
        _sellLevel.Rate = !up ? ratesOpen[0].PriceLow : MagnetPrice;
        _buyLevel.Rate = _buyLevel.Rate.Max(_waveLast.Rates.LastByCount().AskHigh, RateLast.AskHigh);
        _sellLevel.Rate = _sellLevel.Rate.Min(_waveLast.Rates.LastByCount().BidLow, RateLast.BidLow);
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        _buyLevel.TradesCount = !up ? 0 : CorridorCrossesCountMinimum;
        _sellLevel.TradesCount = up ? 0 : CorridorCrossesCountMinimum;
      } else if (Trades.Any()) {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
      }
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    #region 09s
    private void StrategyEnterBreakout090() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => { };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      StrategyExitByGross061();

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));


      #region Run
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      if (_waveLong.Item3 >= 60) {
        var rateFirst = CorridorStats.Rates.LastByCount().StartDate;
        var ratesFirst = CorridorsRates[0]
          .SkipWhile(r => r.StartDate < _waveLong.Item1.StartDate).TakeWhile(r => r.StartDate <= _waveLong.Item2.StartDate).ToList();//.TakeWhile(r => r.PriceStdDev > 0).ToList();// RatesArray.Where(r => r.StartDate.Between(rateFirst.AddMinutes(-BarPeriodInt * 15), rateFirst.AddMinutes(BarPeriodInt * 15))).ToList();
        _sellLevel.Rate = ratesFirst.Min(r => r.BidAvg);//.Min(CorridorStats.Rates.Min(r => r.PriceAvg) + PointSize);
        _buyLevel.Rate = ratesFirst.Max(r => r.AskAvg);//.Max(CorridorStats.Rates.Max(r => r.PriceAvg) - PointSize);
        if (isAuto) {
          if (_sellLevel.CorridorDate != _waveLong.Item2.StartDate) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = true;// CorridorsRates[0][0].PriceStdDev > SpreadForCorridor * 2;
            _buyLevel.CorridorDate = _sellLevel.CorridorDate = _waveLong.Item2.StartDate;
          }
        }
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(RateLast.PriceAvg, RatePrev.PriceAvg);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(RateLast.PriceAvg, RatePrev.PriceAvg);
      #endregion
    }

    private void StrategyEnterBreakout091() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        ScanCorridorCustom = ScanCorridorByWaveArea91;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentGross >= 0) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            _waveHighOnOff.TurnOff();
          }
        };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      #region Close Trades
      StrategyExitByGross061();
      if (_waveQuick != null && _waves.IndexOf(_waveQuick) > _waves.Count - 2 && this.LotSizeByLossBuy.Max(LotSizeByLossSell) == LotSize)
        CloseExcessiveTrades();
      #endregion

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      #region Run
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAngleOk = this.TradingAngleRange == 0 ? true : this.TradingAngleRange > 0 ? CorridorStats.Angle.Abs() <= this.TradingAngleRange : CorridorStats.Angle.Abs() >= this.TradingAngleRange.Abs();
      var isLotSizeOk = !Trades.Any() || Trades.Lots() < MaxLotByTakeProfitRatio * LotSize;
      var canTrade = isAngleOk && isLotSizeOk;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var waveIndex = _waves.IndexOf(WaveHigh);
      var waveQuickIndex = _waves.IndexOf(WaveQuick);
      /*if (waveIndex < _waves.Count - 3)*/
      {
        var waveChanged = _waveLast.HasChanged(WaveHigh, _waveLast.HasChanged1);
        var up = Trades.Any() ? Trades[0].IsBuy : _waveLast.IsUp;
        var ratesForLevels = RatesArray.ReverseIfNot().Skip(1).TakeWhile(r => r != WaveHigh[0]).ToArray();//.TakeWhile(r => r != (_waveLast.IsUp ? _waveLast.High : _waveLast.Low));
        var bl = ratesForLevels.Max(r => r.AskAvg);
        var sl = ratesForLevels.Min(r => r.BidAvg);
        if (bl - sl < SpreadForCorridor * 80) {
          _buyLevel.Rate = bl;
          _sellLevel.Rate = sl;
          if (isAuto) {
            _buyLevel.CanTrade = _waveHighOnOff.IsOn && _waveQuick[0] > WaveHigh[0] && (!TradeByRateDirection || up);
            _sellLevel.CanTrade = _waveHighOnOff.IsOn && _waveQuick[0] > WaveHigh[0] && (!TradeByRateDirection || !up);
          }
        }
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(RateLast.PriceAvg, RatePrev.PriceAvg);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(RateLast.PriceAvg, RatePrev.PriceAvg);
      #endregion
    }

    class TradeConditioner911 {
      public bool ByWaveAvgMax { get; set; }
      public bool ByWaveAvgMin { get; set; }
      public bool CanTrade { get { return ByWaveAvgMax || ByWaveAvgMin; } }
      public void Clear() {
        ByWaveAvgMax = false;
        ByWaveAvgMin = false;
      }
    }
    TradeConditioner911 _tradeConditioner911 = new TradeConditioner911();
    private void StrategyEnterBreakout0911() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        ScanCorridorCustom = ScanCorridorByWaveArea911;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentGross >= 0) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            _waveHighOnOff.TurnOff();
            _tradeConditioner911.Clear();
          }
        };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      #region Exit
      if (CurrentGrossInPips > TakeProfitPips) {
        var quickIndex = _waves.Reverse().TakeWhile(w => w[0] > WaveQuick[0]).Count();
        if (quickIndex < 2)
          CloseTrades(Trades.Lots());
      }
      
      #endregion

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      #region Run
      _tradeConditioner911.ByWaveAvgMax = true;// WaveAverage <= StDevAverages.TakeEx(-2).Sum();
      //if (WaveAverage > StDevAverages.TakeEx(-2).First()*2) _tradeConditioner911.ByWaveAvgMin = true;
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAngleOk = this.TradingAngleRange == 0 ? true : this.TradingAngleRange > 0 ? CorridorStats.Angle.Abs() <= this.TradingAngleRange : CorridorStats.Angle.Abs() >= this.TradingAngleRange.Abs();
      var isLotSizeOk = !Trades.Any() || Trades.Lots() < MaxLotByTakeProfitRatio * LotSize;
      var canTrade = isAngleOk && isLotSizeOk;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      //var waveEnd = _waves.SkipWhile(w => w[0] < WaveHigh.LastByCount()).SkipWhile(w => w[0].PriceStdDev < StDevAverages.Last()).FirstOrDefault();
      if (isAuto) {
        var waveChanged = _waveLast.HasChanged(WaveHigh, _waveLast.HasChanged1);
        var up = Trades.Any() ? Trades[0].IsBuy : _waveLast.IsUp;
        var rateStart = WaveHigh[0].StartDate;
        var ratesForLevels = RatesArray.SkipWhile(r => r.StartDate < rateStart).Reverse().Skip(1).ToArray();
        var bl = ratesForLevels.Max(r => r.AskAvg);//.Max(CurrentGross < 0 ? _buyLevel.Rate : double.NaN);
        var sl = ratesForLevels.Min(r => r.BidAvg);//.Min(CurrentGross < 0 ? _sellLevel.Rate : double.NaN);
        if (bl - sl < SpreadForCorridor * 80) {
          _buyLevel.Rate = bl;
          _sellLevel.Rate = sl;
          {
            _buyLevel.CanTrade = _tradeConditioner911.CanTrade && (!TradeByRateDirection || up);
            _sellLevel.CanTrade = _tradeConditioner911.CanTrade && (!TradeByRateDirection || !up);
          }
        }
      } else {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.AskAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.BidAvg);
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(RateLast.PriceAvg, RatePrev.PriceAvg);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(RateLast.PriceAvg, RatePrev.PriceAvg);
      #endregion
    }

    private void StrategyEnterBreakout092() {//2880/BuySellLevels/BuySellLevels/StDevIterations:2/Cma:4  650/3300

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => { };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      StrategyExitByGross061();

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));


      #region Run
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAngleOk = this.TradingAngleRange == 0 ? true : this.TradingAngleRange > 0 ? CorridorStats.Angle.Abs() <= this.TradingAngleRange : CorridorStats.Angle.Abs() >= this.TradingAngleRange.Abs();
      var isLotSizeOk = !Trades.Any() || Trades.Lots() < MaxLotByTakeProfitRatio * LotSize;
      var canTrade = isAngleOk && isLotSizeOk && StDevAverages[0] > SpreadForCorridor;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      if (canTrade) {
        var ratesFirst = _waves.Reverse().SkipWhile(w => w.Max(r => r.PriceStdDev) < StDevAverages[0]).First();
        var bl = ratesFirst.Max(r => r.AskHigh);
        var sl = ratesFirst.Min(r => r.BidLow);
        if (bl - sl > 0) {
          _buyLevel.Rate = bl;
          _sellLevel.Rate = sl;
          if (isAuto) {
            _buyLevel.CanTrade = true;
            _sellLevel.CanTrade = true;
          }
        }
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(RateLast.PriceAvg, RatePrev.PriceAvg);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(RateLast.PriceAvg, RatePrev.PriceAvg);
      #endregion
    }

    private void StrategyEnterBreakout093() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => { };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      StrategyExitByGross061(() => Trades.Lots() > LotSize * 10 && CurrentGrossInPips >= RatesStDevInPips);

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));


      #region Run
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAngleOk = this.TradingAngleRange == 0 ? true : this.TradingAngleRange > 0 ? CorridorStats.Angle.Abs() <= this.TradingAngleRange : CorridorStats.Angle.Abs() >= this.TradingAngleRange.Abs();
      var isLotSizeOk = !Trades.Any() || Trades.Lots() < MaxLotByTakeProfitRatio * LotSize;
      var canTrade = isAngleOk && isLotSizeOk && StDevAverages[0] > SpreadForCorridor;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      var ratesFirst = _waves.Reverse().SkipWhile(w => w.Max(r => r.PriceStdDev) < StDevAverages[0]).First();
      if (canTrade && _waveLast.HasChanged(ratesFirst)) {
        var bl = ratesFirst.Max(r => r.AskHigh);
        var sl = ratesFirst.Min(r => r.BidLow);
        _buyLevel.Rate = bl;
        _sellLevel.Rate = sl;
        if (isAuto) {
          _buyLevel.CanTrade = true;
          _sellLevel.CanTrade = true;
        }
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(RateLast.PriceAvg, RatePrev.PriceAvg);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(RateLast.PriceAvg, RatePrev.PriceAvg);
      #endregion
    }

    private void StrategyEnterBreakout094() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => { };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      StrategyExitByGross061(() => Trades.Lots() > LotSize * 10 && CurrentGrossInPips >= RatesStDevInPips);

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));


      #region Run
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAngleOk = this.TradingAngleRange == 0 ? true : this.TradingAngleRange > 0 ? CorridorStats.Angle.Abs() <= this.TradingAngleRange : CorridorStats.Angle.Abs() >= this.TradingAngleRange.Abs();
      var isLotSizeOk = !Trades.Any() || Trades.Lots() < MaxLotByTakeProfitRatio * LotSize;
      var canTrade = isAngleOk && isLotSizeOk && StDevAverages[0] > SpreadForCorridor;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      var ratesFirst = _waves.Reverse().SkipWhile(w => w.Max(r => r.PriceStdDev) < StDevAverages[0]).First();
      var waveIndex = _waves.IndexOf(WaveHigh);
      if (canTrade && _waves.Count - waveIndex <= 3 && _waveLast.HasChanged(ratesFirst)) {
        var rate1 = _waves[0.Max(waveIndex - 1)];
        var bl = ratesFirst.Max(r => r.AskHigh).Max(rate1.Max(r => r.AskHigh));
        var sl = ratesFirst.Min(r => r.BidLow).Min(rate1.Min(r => r.BidLow));
        _buyLevel.Rate = bl;
        _sellLevel.Rate = sl;
        if (isAuto) {
          _buyLevel.CanTrade = true;
          _sellLevel.CanTrade = true;
        }
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(RateLast.PriceAvg, RatePrev.PriceAvg);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(RateLast.PriceAvg, RatePrev.PriceAvg);
      #endregion
    }

    private void StrategyEnterBreakout095() {// B5D0A43A9801

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => { };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      StrategyExitByGross061(() => Trades.Lots() > LotSize * 10 && CurrentGrossInPips >= RatesStDevInPips);

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));


      #region Run
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAngleOk = this.TradingAngleRange == 0 ? true : this.TradingAngleRange > 0 ? CorridorStats.Angle.Abs() <= this.TradingAngleRange : CorridorStats.Angle.Abs() >= this.TradingAngleRange.Abs();
      var isLotSizeOk = !Trades.Any() || Trades.Lots() < MaxLotByTakeProfitRatio * LotSize;
      var canTrade = isAngleOk && isLotSizeOk && StDevAverages[0] > SpreadForCorridor;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var waveIndex = _waves.IndexOf(WaveHigh);
      if (canTrade && _waves.Count - waveIndex <= 3 && _waveLast.HasChanged(WaveHigh)) {
        var rate1 = _waves[0.Max(waveIndex - 1)];
        _buyLevel.Rate = WaveHigh.Max(r => r.AskHigh).Max(rate1.Max(r => r.AskHigh));
        _sellLevel.Rate = WaveHigh.Min(r => r.BidLow).Min(rate1.Min(r => r.BidLow));
        if (isAuto) {
          _buyLevel.CanTrade = true;
          _sellLevel.CanTrade = true;
        }
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(!Trades.Any() ? double.NaN : RateLast.PriceAvg.Max(RatePrev.PriceAvg));
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(!Trades.Any() ? double.NaN : RateLast.PriceAvg.Min(RatePrev.PriceAvg));
      #endregion
    }

    private void StrategyEnterBreakout096() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => { };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      StrategyExitByGross061(() => Trades.Lots() > LotSize * 10 && CurrentGrossInPips >= RatesStDevInPips);

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));


      #region Run
      Func<Rate> rateHigh = () => CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Func<Rate> rateLow = () => CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAngleOk = this.TradingAngleRange == 0 ? true : this.TradingAngleRange > 0 ? CorridorStats.Angle.Abs() <= this.TradingAngleRange : CorridorStats.Angle.Abs() >= this.TradingAngleRange.Abs();
      var isLotSizeOk = !Trades.Any() || Trades.Lots() < MaxLotByTakeProfitRatio * LotSize;
      var canTrade = isAngleOk && isLotSizeOk && StDevAverages[0] > SpreadForCorridor;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      var waveIndex = _waves.IndexOf(WaveHigh);
      var stDevMin = StDevAverages.TakeEx(-1).First();
      var rate1 = _waves.Skip(waveIndex + 1).SkipWhile(w => w[0].PriceStdDev > stDevMin).FirstOrDefault();
      var waveIndex1 = rate1 != null ? _waves.IndexOf(rate1) : 0;
      var wave2 = waveIndex == 0 || waveIndex1 == 0 ? null
        : _waves.Skip(waveIndex).Take(waveIndex1 - waveIndex + 1).SelectMany(w => w).OrderBars().ToArray();
      if (canTrade && _waves.Count - waveIndex <= 3 && _waveLast.HasChanged(wave2, _waveLast.HasChanged1)) {
        _buyLevel.Rate = (_waveLast.IsUp ? wave2.Max(r => r.AskHigh) : rate1.Max(r => r.AskHigh));
        _sellLevel.Rate = (!_waveLast.IsUp ? wave2.Min(r => r.BidLow) : rate1.Min(r => r.BidLow));
        if (isAuto) {
          _buyLevel.CanTrade = true;
          _sellLevel.CanTrade = true;
        }
      }
      var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
      var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
      var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(!Trades.Any() ? double.NaN : RateLast.PriceAvg.Max(RatePrev.PriceAvg));
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(!Trades.Any() ? double.NaN : RateLast.PriceAvg.Min(RatePrev.PriceAvg));
      #endregion
    }

    WaveLast _waveLast = new WaveLast();
    class WaveLast {
      private Rate _rateStart = new Rate();
      private Rate _rateEnd = new Rate();
      private List<Rate> ratesOrdered;
      IList<Rate> _Rates;
      public IList<Rate> Rates {
        get { return _Rates; }
        set {
          _Rates = value;
          this.IsUp = this.Rates.Select(r => r.PriceAvg).ToArray().Regress(1)[1] > 0;
        }
      }
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
      public bool HasChanged(IList<Rate> wave, Func<Rate, Rate, bool> hasChanged = null) {
        if (wave == null || wave.Count == 0) return false;
        var changed = (hasChanged ?? HasChanged)(wave[0], wave.LastByCount());
        if (changed) {
          this.Rates = wave.ToList();
          this.ratesOrdered = wave.OrderBy(r => r.PriceAvg).ToList();
          this.High = ratesOrdered.LastByCount();
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
  }
}
