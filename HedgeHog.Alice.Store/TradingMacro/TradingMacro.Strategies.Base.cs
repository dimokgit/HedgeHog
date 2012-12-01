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


    private void StrategyEnterMiddleManBase() {
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
          if (IsAutoStrategy || Trades.Any()) {
            _buyLevel.Rate = wave.Max(r => r.PriceAvg);
            _sellLevel.Rate = wave.Min(r => r.PriceAvg);
          }
          _buyLevel.CanTrade = _sellLevel.CanTrade = (_buyLevel.Rate - _sellLevel.Rate) > RatesHeight * CorridorDistanceRatio;
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

      #region Init
      if (_strategyExecuteOnTradeClose == null) {
        ReloadPairStats();
        SuppResLevelsCount = 2;
        ShowTrendLines = false;
        _buyLevel.CanTrade = _sellLevel.CanTrade = IsAutoStrategy;
        _strategyExecuteOnTradeClose = t => {
          CmaLotSize = 0;
          if (isCurrentGrossOk()) {
            if (!IsAutoStrategy) {
              IsTradingActive = false;
              _buyLevel.CanTrade = _sellLevel.CanTrade = false;
            }
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

  }
}
