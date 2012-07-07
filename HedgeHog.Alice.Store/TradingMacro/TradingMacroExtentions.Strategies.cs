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

    bool IsTradingHour() {
      return IsTradingHour(TradesManager.ServerTime);
    }
    private void StrategyEnterRange093() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        ScanCorridorCustom = ScanCorridorBySeaHourse;
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
      if (_buyLevel!=null && !IsTradingHour()) {
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
        if (CurrentGross > 0) CloseTrades(Trades.Lots());
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
      var distance = StDevAverages.TakeEx(-2).First();
      if (isAuto && IsTradingHour() &&  _isWaveOk && _waveLast.HasChanged3(WaveHigh, MagnetPrice,distance) ) {
        var up = _waveLast.Rates[0].PriceAvg < _waveLast.Rates.LastByCount().PriceAvg;
        var rateNode = new LinkedList<Rate>(_waveLast.Rates).First;
        while (rateNode != null) {
          if (Math.Sign(rateNode.Value.PriceAvg - MagnetPrice) != Math.Sign(rateNode.Next.Value.PriceAvg - MagnetPrice))
            break;
          rateNode = rateNode.Next;
        }
        var ratesOpen = _waveLast.Rates.SkipWhile(r => r < rateNode.Value).OrderBy(r => r.PriceAvg).ToArray();
        _buyLevel.Rate = up ? RatePrev.PriceAvg : MagnetPrice;
        _sellLevel.Rate = !up ? RatePrev.PriceAvg : MagnetPrice;
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

    private void StrategyWaveCorridor() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        ScanCorridorCustom = ScanCorridorByWave;
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
      var wave = WaveHigh;
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var distance = StDevAverages.TakeEx(-2).First();
      //SetMagnetPrice(wave);
      if (isAuto && _waveLast.HasChanged3(wave, MagnetPrice, distance)) {
        _buyLevel.Rate = _CenterOfMassBuy.Max(RateLast.PriceHigh);
        _sellLevel.Rate = _CenterOfMassSell.Min(RateLast.PriceLow);
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
      } else if (Trades.Any()) {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
      }
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    class TradeConditionerWaveCorridor {
      public bool IsNoLoss { get; set; }
      public void Clear() {
        IsNoLoss = false;
      }
    }
    TradeConditionerWaveCorridor _tcWaveCorridor = new TradeConditionerWaveCorridor();
    private void StrategyWaveCorridor010() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (ScanCorridorCustom != ScanCorridorByWaveAbsolute) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        _tcWaveCorridor.Clear();
        ScanCorridorCustom = ScanCorridorByWaveAbsolute;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentGross >= 0) {
            if( t.IsBuy) _buyLevel.CanTrade = false;
            else  _sellLevel.CanTrade = false;
          }
          if(!Trades.IsBuy(t.IsBuy).Any())
            _tcWaveCorridor.Clear();
        };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      #region Exit
      if (!IsTradingHour()) {
        if(Trades.Any())
          CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      }
      if (StrategyExitByGross061()) {
        _tcWaveCorridor.IsNoLoss = true;
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
      var wave = WaveHigh;
      var gross = Trades.Any() ? CurrentLossInPips.Abs() : 0;
      var tpColse = InPoints(TakeProfitPips.Max(gross));
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var distance = StDevAverages.TakeEx(-2).First();
      //SetMagnetPrice(wave);
      if (isAuto && IsTradingHour() && IsWaveFlagOk(wave) && _waveLast.HasChanged3(wave, MagnetPrice, 0)) {
        var up = wave.Select(r => r.PriceAvg).ToArray().Regress(1)[1] > 0;
        if (!up) {
          _buyLevel.Rate = wave[0].PriceAvg + SpreadForCorridor;// _CenterOfMassBuy.Min(RateLast.PriceAvg);//.Max(RateLast.PriceHigh);
        }
        if (up) {
          _sellLevel.Rate = wave[0].PriceAvg - SpreadForCorridor;//.Min(RateLast.PriceLow);
        }
        _buyLevel.CanTrade = true;
        _sellLevel.CanTrade = true;
        if (CurrentPrice.Ask > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
        if (CurrentPrice.Bid < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
      } else if (Trades.Any()) {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
      }
      //if ((IsWaveFlagOk(wave) || IsWaveClubOk(wave)))
      //  _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      if (_tcWaveCorridor != null && _tcWaveCorridor.IsNoLoss)
        tpColse = 0;
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    private void StrategyEnterWaveStDevCorridor() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (ScanCorridorCustom != ScanCorridorByWaveAbsolute) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        _tcWaveCorridor.Clear();
        ScanCorridorCustom = ScanCorridorByWaveAbsolute;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          //_useTakeProfitMin = false;
          if (CurrentGross >= 0) {
            if (t.IsBuy) _buyLevel.CanTrade = false;
            else _sellLevel.CanTrade = false;
          }
          if (!Trades.IsBuy(t.IsBuy).Any())
            _tcWaveCorridor.Clear();
        };
        _strategyExecuteOnTradeOpen = () => {
          //_useTakeProfitMin = true;
        };
      }
      #endregion

      #region Exit
      if (!IsTradingHour()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
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
      RateLast.PriceRlw = (this.RatesHeight / this.WaveAverage).Round(1);
      var wave = WaveHigh;
      var gross = CurrentLossInPips.Min(0).Abs();
      var tpColse = InPoints(TakeProfitPips + gross);
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var distance = StDevAverages.TakeEx(-2).First();
      //SetMagnetPrice(wave);
      if (isAuto) {
        _buyLevel.Rate = MagnetPrice + this.InPoints(TakeProfitPips);
        _sellLevel.Rate = MagnetPrice - this.InPoints(TakeProfitPips);
        if (CurrentPrice.Ask > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
        if (CurrentPrice.Bid < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = !MagnetPrice.Between(_CenterOfMassBuy, _CenterOfMassSell);
      } else if (CorridorFollowsPrice && RatePrev.PriceAvg.Between(_sellLevel.Rate, _buyLevel.Rate)) {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
      }
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    private void StrategyEnterAutoPilot_1() {

      #region Init
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Action resetLevels = () => {
        _buyLevel.Rate = MagnetPrice + WaveAverage;
        _sellLevel.Rate = MagnetPrice - WaveAverage;
        _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesCountMinimum;
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
      };
      if (CorridorStats.Rates.Count == 0) return;
      if (ScanCorridorCustom != ScanCorridorByWaveAbsolute || _strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        _tcWaveCorridor.Clear();
        ScanCorridorCustom = ScanCorridorByWaveAbsolute;
        SuppResLevelsCount = 2;
        resetLevels();
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentLossInPips > -InPips(SpreadForCorridor)) 
            resetLevels();
        };
        _strategyExecuteOnTradeOpen = () => {
          var psa = RatesArraySafe.TakeEx(-5).Average(r => r.PriceSpread);
          var offest = PriceSpreadAverage.GetValueOrDefault().Max(psa) + WaveAverage;
          if (Trades.HaveBuy() /*&& _buyLevel.TradesCount == 0*/)
            _sellLevel.Rate = _buyLevel.Rate - offest;
          if (Trades.HaveSell() /*&& _sellLevel.TradesCount == 0*/)
            _buyLevel.Rate = _sellLevel.Rate + offest;
        };
        return;
      }
      #endregion

      #region Exit
      StrategyExitByGross061();
      if (!IsTradingHour()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      }
      #endregion

      #region Suppres levels
      var reset = _buyLevel == null;
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      if (reset) resetLevels();

      var buyCloseLevel = Support1(); buyCloseLevel.TradesCount = 9;
      var sellCloseLevel = Resistance1(); sellCloseLevel.TradesCount = 9;
      #endregion

      #region Run
      var gross = CurrentLossInPips.Min(0).Abs();
      var tpColse = InPoints(TakeProfitPips + gross);
      var distance = StDevAverages.TakeEx(-2).First();
      var isRange = _sellLevel.Rate > _buyLevel.Rate;
      //if (!Trades.Any() && _waveLast.HasChanged3(CorridorStats.Rates, MagnetPrice, CorridorStats.StDev * 2)) {
        resetLevels();
      //}else 
        if (CorridorFollowsPrice || Trades.Any()) {
        if (!isRange) {
          var a = RatesArraySafe.TakeEx(-5).ToArray();
          if (a.Length > 0) {
            if (!Trades.HaveSell() && a.Min(r => r.BidLow) <= _buyLevel.Rate)
              _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
            if (!Trades.HaveBuy() && a.Max(r => r.AskHigh) >= _sellLevel.Rate)
              _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
            if (Trades.HaveBuy()) {
              _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
              _sellLevel.Rate = RatePrev.PriceAvg.Min(_buyLevel.Rate - TradingDistance - PriceSpreadAverage.GetValueOrDefault());
            }
            if (Trades.HaveSell()) {
              _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
              _buyLevel.Rate = RatePrev.PriceAvg.Max(_sellLevel.Rate + TradingDistance + PriceSpreadAverage.GetValueOrDefault());
            }
          }
        } else {
          _buyLevel.Rate = _buyLevel.Rate.Min(RatePrev.PriceAvg);
          _sellLevel.Rate = _sellLevel.Rate.Max(RatePrev.PriceAvg);
        }
        //Func< double> getLevelRate = () => RatePrev.PriceCMALast;
        //if (RatePrev.BidLow <= _buyLevel.Rate)
        //  _buyLevel.Rate = _buyLevel.Rate.Max(getLevelRate());
        //if(_sellLevel.TradesCount == 0 && _sellLevel.Rate > _buyLevel.Rate && RatePrev.BidLow <= _sellLevel.Rate)
        //  _sellLevel.Rate = _sellLevel.Rate.Max(getLevelRate());

        //if(RatePrev.AskHigh >= _sellLevel.Rate  )
        //  _sellLevel.Rate = _sellLevel.Rate.Min(getLevelRate());
        //if (_buyLevel.TradesCount == 0 && _sellLevel.Rate > _buyLevel.Rate && RatePrev.AskHigh >= _buyLevel.Rate)
        //  _buyLevel.Rate = _buyLevel.Rate.Min(getLevelRate());
      }
      if (!isRange) {
        buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      } else {
        if (Trades.IsBuy(true).Gross() >= 0) {
          buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        } else if (Trades.IsBuy(false).Gross() >= 0) {
          sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
        } else {
          var stopLoss = InPoints(TakeProfitPips) + SpreadForCorridor;
          buyCloseLevel.Rate = buyNetOpen() - stopLoss;
          sellCloseLevel.Rate = sellNetOpen() + stopLoss;
        }
      }
      #endregion
    }

    private void StrategyEnterAutoPilot_LongRunner() {

      #region Init
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Action resetLevels = () => {
        var waveLength = _waves.Average(w => w.Count).ToInt().Min(CorridorStats.Rates.Count - 1);
        var rates = CorridorStats.Rates.Skip(waveLength).ToArray();
        _buyLevel.Rate = rates.Max(r => r.PriceHigh);
        _sellLevel.Rate = rates.Min(r => r.PriceLow);
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
      };
      if (CorridorStats.Rates.Count == 0) return;
      if (ScanCorridorCustom != ScanCorridorByWaveAbsolute || _strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        _tcWaveCorridor.Clear();
        ScanCorridorCustom = ScanCorridorByWaveAbsolute;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentLoss >= 0)
            _buyLevel.TradesCount = _sellLevel.TradesCount = 1;
        };
        _strategyExecuteOnTradeOpen = () => { };
        return;
      }
      #endregion

      #region Exit
      if (!StrategyExitByTotalGross())
        StrategyExitByGross061();

      if (!IsTradingHour()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      }
      if (CurrentPrice.Ask > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
      if (CurrentPrice.Bid < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
      #endregion

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1(); buyCloseLevel.TradesCount = 9;
      var sellCloseLevel = Resistance1(); sellCloseLevel.TradesCount = 9;
      #endregion

      #region Run
      if (_waveLast.HasChanged4(WaveHigh))
        resetLevels();
      var gross = CurrentLossInPips.Min(0).Abs().Max(TradingStatistics.CurrentLossInPips.Min(0).Abs() / TradingStatistics.TradingMacros.Count);
      var limitRatio = 1;// Math.Log((CorridorStats.Angle.Abs()).Max(0.000001), RangeRatioForTradeLimit);
      var tpColse = InPoints(TakeProfitPips * limitRatio + gross + gross * limitRatio.Min(0));
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    private void StrategyEnterBounce() {

      #region Init
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Action resetLevels = () => {
        _buyLevel.Rate =  RateLast.PriceAvg02;
        _sellLevel.Rate = RateLast.PriceAvg03;
      };
      if (CorridorStats.Rates.Count == 0) return;
      if (ScanCorridorCustom != ScanCorridorByWaveAbsolute || _strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        _tcWaveCorridor.Clear();
        ScanCorridorCustom = ScanCorridorByWaveAbsolute;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => { };
        _strategyExecuteOnTradeOpen = () => { };
        return;
      }
      #endregion

      #region Exit
      if (!StrategyExitByTotalGross())
        StrategyExitByGross061();

      if (!IsTradingHour()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      }
      if (CurrentPrice.Bid > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
      if (CurrentPrice.Ask < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
      #endregion

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1(); buyCloseLevel.TradesCount = 9;
      var sellCloseLevel = Resistance1(); sellCloseLevel.TradesCount = 9;
      #endregion

      #region Run
      if (RateLast.PriceAvg1 > _RatesMax || _sellLevel.CanTrade && _sellLevel.Rate < RateLast.PriceAvg1) {
        _buyLevel.Rate = RateLast.PriceAvg2.Max(_buyLevel.Rate, CorridorStats.RatesMax);
        _buyLevel.CanTrade = false;
        _sellLevel.Rate = RateLast.PriceAvg1.Max(_sellLevel.Rate, RatePrev.PriceAvg);
        _sellLevel.CanTrade = true;
      }
      if (RateLast.PriceAvg1 < _RatesMin || _buyLevel.CanTrade && _buyLevel.Rate > RateLast.PriceAvg1) {
        _buyLevel.Rate = RateLast.PriceAvg1.Min(_buyLevel.Rate, RatePrev.PriceAvg);
        _buyLevel.CanTrade = true;
        _sellLevel.Rate = RateLast.PriceAvg3.Min(_sellLevel.Rate, CorridorStats.RatesMin);
        _sellLevel.CanTrade = false;
      }
      if (_buyLevel.CanTrade && RateLast.PriceAvg3 > CorridorStats.RatesMin && RateLast.PriceAvg > RateLast.PriceAvg1) {
        _buyLevel.CanTrade = false;
      }
      if (_sellLevel.CanTrade && RateLast.PriceAvg2 < CorridorStats.RatesMax && RateLast.PriceAvg < RateLast.PriceAvg1) {
        _sellLevel.CanTrade = false;
      }
      //resetLevels();
      var gross = CurrentLossInPips.Min(0).Abs().Max(TradingStatistics.CurrentLossInPips.Min(0).Abs() / TradingStatistics.TradingMacros.Count);
      var limitRatio = 1;// Math.Log((CorridorStats.Angle.Abs()).Max(0.000001), RangeRatioForTradeLimit);
      var tpColse = InPoints(TakeProfitPips * limitRatio + gross + gross * limitRatio.Min(0));
      buyCloseLevel.Rate = (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
      sellCloseLevel.Rate = (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
      #endregion
    }

    void _buySellLevel_TradesCountChanging(object sender, SuppRes.TradesCountChangingEventArgs e) {
      if (e.NewValue != 0) return;
      var changingLevel = sender == _buyLevel?_buyLevel:_sellLevel;
      var otherLevel = sender != _buyLevel?_buyLevel:_sellLevel;
      if (otherLevel.TradesCount > 0) return;
      _buyLevel.Rate = RateLast.AskHigh;
      _sellLevel.Rate = RateLast.BidLow;
    }

    private void StrategyEnterBouncer() {

      #region Init
      _buyLevel = Resistance0();
      _sellLevel = Support0();
      Action resetLevels = () => {
        _buyLevel.Rate = RateLast.AskHigh;
        _sellLevel.Rate = RateLast.BidLow;
        _buyLevel.TradesCount = _sellLevel.TradesCount = CorridorCrossesCountMinimum;
      };
      if (CorridorStats.Rates.Count == 0) return;
      if (ScanCorridorCustom != ScanCorridorByWaveAbsolute || _strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        _tcWaveCorridor.Clear();
        ScanCorridorCustom = ScanCorridorByWaveAbsolute;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!_buyLevel.CanTrade)
            resetLevels();
        };
        _strategyExecuteOnTradeOpen = () => { };
        _buyLevel.TradesCountChanging += new EventHandler<Store.SuppRes.TradesCountChangingEventArgs>(_buySellLevel_TradesCountChanging);
        _sellLevel.TradesCountChanging += new EventHandler<Store.SuppRes.TradesCountChangingEventArgs>(_buySellLevel_TradesCountChanging);
        return;
      }
      #endregion

      #region Exit
      if (!StrategyExitByTotalGross())
        TrimTradesOnBreakEven();

      if (!IsTradingHour()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      }
      #endregion

      #region Suppres levels
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);

      var buyCloseLevel = Support1(); 
      var sellCloseLevel = Resistance1();
      if (!buyCloseLevel.CanTrade && !sellCloseLevel.CanTrade)
        buyCloseLevel.TradesCount = sellCloseLevel.TradesCount = 9;

      #endregion

      #region Run
      double als = AllowedLotSizeCore(Trades);
      var canReset = Trades.Lots() >= LotSize * this.ProfitToLossExitRatio && als == LotSize;
      if (canReset) TrimTrades();

      if (false) {
        if (Strategy.HasFlag(Strategies.Auto) && (canReset || _waveLast.HasChanged4(WaveHigh))) {
          _sellLevel.CanTrade = _buyLevel.CanTrade = true;
          _buyLevel.Rate = CorridorStats.RatesMax + SpreadForCorridor;
          _sellLevel.Rate = CorridorStats.RatesMax - SpreadForCorridor;
          _buyLevel.TradesCount = CorridorCrossesCountMinimum;
          _sellLevel.TradesCount = CorridorCrossesCountMinimum + (SymmetricalBuySell ? 1 : 0);

          sellCloseLevel.CanTrade = buyCloseLevel.CanTrade = true;

          sellCloseLevel.Rate = CorridorStats.RatesMin + SpreadForCorridor;
          buyCloseLevel.Rate = CorridorStats.RatesMin - SpreadForCorridor;
          buyCloseLevel.TradesCount = CorridorCrossesCountMinimum;
          sellCloseLevel.TradesCount = CorridorCrossesCountMinimum + (SymmetricalBuySell ? 1 : 0);
        }
      }
      var ratesShort = CorridorStats.Rates.Take(5).Select(r=>r.PriceAvg).OrderBy(d=>d).ToArray();
      
      if (_buyLevel.CanTrade && _sellLevel.CanTrade && ratesShort.Any(min=> min.Between(_sellLevel.Rate, _buyLevel.Rate))) {
        var newBuy = -_buyLevel.Rate + _buyLevel.Rate.Max(RatePrev.PriceAvg); 
        var newSell = _sellLevel.Rate - _sellLevel.Rate.Min(RatePrev.PriceAvg);
        if (_buyLevel.TradesCount == 1 || _sellLevel.TradesCount == 1) {
          var reArm = ((_buyLevel.Rate + _sellLevel.Rate) / 2).Between(RateLast.BidLow, RateLast.AskHigh);
          if (reArm) {
            var lastCross = CorridorStats.Rates.SkipWhile(r => r.PriceAvg > _sellLevel.Rate && r.PriceAvg < _buyLevel.Rate).First();
            if (lastCross.PriceAvg == _buyLevel.Rate && _buyLevel.TradesCount == 1)
              _buyLevel.TradesCount = 0;
            if (lastCross.PriceAvg == _sellLevel.Rate && _sellLevel.TradesCount == 1)
              _sellLevel.TradesCount = 0;
          }
        }
        if (SymmetricalBuySell) {
          var point = InPoints(.1);
          if (_buyLevel.TradesCount <= CorridorCrossesCountMinimum && newBuy > point)
            newSell = newBuy;
          if (_sellLevel.TradesCount <= CorridorCrossesCountMinimum && newSell > point)
            newBuy = newSell;
        }
        _buyLevel.Rate += newBuy;
        _sellLevel.Rate -= newSell;
        sellCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      }
      if (buyCloseLevel.CanTrade && ratesShort.Any(min => min.Between(buyCloseLevel.Rate, sellCloseLevel.Rate))) {
        _buyLevel.Rate = sellCloseLevel.Rate;
        _buyLevel.TradesCount = sellCloseLevel.TradesCount;
        _sellLevel.Rate = buyCloseLevel.Rate;
        _sellLevel.TradesCount = buyCloseLevel.TradesCount;
        sellCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      }
      if (!sellCloseLevel.CanTrade && !buyCloseLevel.CanTrade) {
        var gross = 0.0;
        if (StreatchTakeProfit) {
          var tradesGlobal = GetTradesGlobalCount();
          gross = tradesGlobal < 2 ? CurrentLossInPips.Min(0).Abs() : CurrentLossInPips.Min(0).Abs().Max(TradingStatistics.CurrentLossInPips.Min(0).Abs() / tradesGlobal);
        }
        var tpColse = InPoints(TakeProfitPips + gross);
        var newBuy = -buyCloseLevel.Rate + (buyNetOpen() + tpColse).Max(Trades.HaveBuy() ? RateLast.PriceAvg.Max(RatePrev.PriceAvg) : double.NaN);
        var newSell = sellCloseLevel.Rate - (sellNetOpen() - tpColse).Min(Trades.HaveSell() ? RateLast.PriceAvg.Min(RatePrev.PriceAvg) : double.NaN);
        buyCloseLevel.Rate += newBuy;
        sellCloseLevel.Rate -= newSell;
      }
      #endregion
    }

    private void StrategyRomanCandle() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;
      if (_strategyExecuteOnTradeClose == null) {
        WaveQuick = null;
        WaveHigh = null;
        _waveLast = new WaveLast();
        _tcWaveCorridor.Clear();
        ScanCorridorCustom = ScanCorridorByWave;
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (CurrentGross >= 0) {
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          }
          if (!Trades.IsBuy(t.IsBuy).Any())
            _tcWaveCorridor.Clear();
        };
        _strategyExecuteOnTradeOpen = () => { };
      }
      #endregion

      #region Exit
      if (!IsTradingHour()) {
        if (Trades.Any())
          CloseTrades(Trades.Lots());
        _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      }
      if (StrategyExitByGross061()) {
        _tcWaveCorridor.IsNoLoss = true;
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
      var wave = WaveHigh;
      var gross = Trades.Any() ? CurrentLossInPips.Abs() : 0;
      var tpColse = InPoints(TakeProfitPips.Max(gross));
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var distance = StDevAverages.TakeEx(-2).First();
      //SetMagnetPrice(wave);
      if (isAuto && IsTradingHour() && !(IsWaveFlagOk(wave) || IsWaveClubOk(wave)) && _waveLast.HasChanged3(wave, MagnetPrice, 0)) {
        var up = wave.Select(r => r.PriceAvg).ToArray().Regress(1)[1] > 0;
        _buyLevel.Rate = wave.Max(r => r.AskHigh);// _CenterOfMassBuy.Min(RateLast.PriceAvg);//.Max(RateLast.PriceHigh);
        _sellLevel.Rate = wave.Min(r => r.BidLow);// _CenterOfMassSell.Max(RateLast.PriceAvg);//.Min(RateLast.PriceLow);
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        if (CurrentPrice.Ask > _buyLevel.Rate && Trades.HaveSell()) CloseTrades(Trades.Lots());
        if (CurrentPrice.Bid < _sellLevel.Rate && Trades.HaveBuy()) CloseTrades(Trades.Lots());
      } else if (Trades.Any()) {
        _buyLevel.Rate = _buyLevel.Rate.Max(RatePrev.PriceAvg);
        _sellLevel.Rate = _sellLevel.Rate.Min(RatePrev.PriceAvg);
      }
      //if ((IsWaveFlagOk(wave) || IsWaveClubOk(wave)))
      //  _buyLevel.CanTrade = _sellLevel.CanTrade = false;
      if (_tcWaveCorridor != null && _tcWaveCorridor.IsNoLoss)
        tpColse = 0;
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
        TrimTrades();
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
        var prices = rates.Select(r=>r.PriceAvg).OrderBy(p=>p).ToArray();
        var average = prices.Average();
        var mean = (prices.LastByCount() - prices[0]) / 2 + prices[0];
        IsHot = IsUp && average < mean || !IsUp && average > mean;
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
