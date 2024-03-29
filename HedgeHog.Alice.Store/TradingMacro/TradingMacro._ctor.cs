﻿using HedgeHog.Bars;
using HedgeHog.Shared;
using HedgeHog.Shared.Messages;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using HedgeHog;
using System.Collections.Concurrent;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    private Func<(string Pair, BarsPeriodType BarPeriod, DateTime LastStartDate)> StartDateKey() => () => (Pair, BarPeriod, LastStartDate);

    private static Func<T> MemoizedCurryStat<T>(TradingMacro tm, Func<TradingMacro, T> f) => MemoizedCurryStat(tm, f, tm2 => (tm2.Pair, tm.BarPeriod, tm.LastStartDate));
    private static Func<T> MemoizedCurryStat<T, K>(TradingMacro tm, Func<TradingMacro, T> f, Func<TradingMacro, K> key) {
      var m = f.MemoizeLast(key);
      return () => m(tm);
    }

    private Func<T1, T2, T3, T> MemoizedCurry<T1, T2, T3, T>(Func<T1, T2, T3, T> f) => MemoizedCurry(f, StartDateKey());
    private Func<T1, T2, T3, T> MemoizedCurry<T1, T2, T3, T, K>(Func<T1, T2, T3, T> f, Func<K> key) {
      var m = f.MemoizeLast(key);
      return (t1, t2, t3) => m(t1, t2, t3);
    }

    private Func<T> MemoizedCurry<T>(Func<T> f, Predicate<T> test) => MemoizedCurry(f, StartDateKey(), test);
    private Func<T> MemoizedCurry<T, K>(Func<T> f, Func<K> key, Predicate<T> test) {
      var m = f.MemoizeLast(key, test);
      return () => m();
    }
    //private Action<T> MemoizedCurry<T>(Action<T> f) => MemoizedCurry(f, StartDateKey);
    private Action<T> MemoizedCurry<T, K>(Action<T> f, Func<T, K> key) {
      var m = f.MemoizeLast(key);
      return p => m(p);
    }
    Action<SuppRes> _tradeLevelChanged;
    Action<(SuppRes level, SuppRes.CrossedEvetArgs crossed)> _tradeLevelCrossed;
    IBApp.IBWraper ibWraper => TradesManager as IBApp.IBWraper;
    public ConcurrentDictionary<bool, (IBApi.Contract contract, double level)> StrategyBS = new ConcurrentDictionary<bool, (IBApi.Contract contract, double level)>();
    private void OnOrderEntryLevel(IEnumerable<(double Rate, double TradingRatio, bool IsCall)> tls)
      => ibWraper.AccountManager.OrderEnrtyLevel.OnNext(tls.Select(tl
        => (Pair, tl.Rate, tl.IsCall, tl.TradingRatio.ToInt(), TLLime.PriceAvg2.Abs(TLLime.PriceAvg3))).ToArray());
    public readonly Func<double> HistoricalVolatilityAnnualized2;
    public readonly Func<double> HistoricalVolatilityAnnualized3;
    public TradingMacro() {
      #region Memoizes
      HistoricalVolatilityAnnualized2 = MemoizedCurry(HistoricalVolatilityAnnualized2Impl, r => r != 0);
      HistoricalVolatilityAnnualized3 = MemoizedCurry(HistoricalVolatilityAnnualized3Impl, r => r != 0);
      #endregion
      GroupRates = MonoidsCore.ToFunc((IList<Rate> rates) => GroupRatesImpl(rates, GroupRatesCount)).MemoizeLast(r => r.Last().StartDate);

      var tls = (from sr in Observable.FromEvent<Action<SuppRes>, SuppRes>(h => _tradeLevelChanged += h, h => _tradeLevelChanged -= h)
                 where IsTradingActive && !IsInVirtualTrading && !sr.IsExitOnly
                 group sr by sr.IsBuy into g
                 from kv in g.Scan((p, n) => p.Rate.Ratio(n.Rate) > 1.001 ? p : n).DistinctUntilChanged(tl => (tl.Rate.RoundBySample(MinTick), tl.PricePosition))
                 select kv)
        .Select(tl => new[] { (tl.Rate, TradingRatio, !tl.IsBuy) }.Where(_ => tl.IsBuy && BuyLevel.CanTrade || tl.IsSell && SellLevel.CanTrade))
        .Publish().RefCount();
      tls.Subscribe(tl =>
       OnOrderEntryLevel(tl));

      Observable.FromEvent<(SuppRes level, SuppRes.CrossedEvetArgs crossed)>(h => _tradeLevelCrossed += h, h => _tradeLevelCrossed -= h)
        .Subscribe(tl => new Exception(new { TradeLevelCrossed = new { tl.level.IsBuy, tl.crossed.Direction } } + ""), () => { });

      this.ObservableForProperty(tm => tm.Pair, false, false)
        .Where(_ => TradesManager != null && !IsInVirtualTrading)
        .Select(oc => oc.GetValue())
        .Scan((prev: "", curr: ""), (prev, curr) => (prev.curr, curr))
        .Where(pair => !pair.curr.IsNullOrWhiteSpace())
        .Throttle(1.FromSeconds())
        .ObserveOn(Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher)
        .Subscribe(oc => {
          _inPips = null;
          _pointSize = double.NaN;
          _BaseUnitSize = 0;
          _mmr = 0;
          LoadActiveSettings();
          ClearInternalRates();
          if(!oc.prev.IsNullOrWhiteSpace() && TradesManager != null) {
            TradesManager.CoreFX.SetSymbolSubscription(Pair, () => OnLoadRates());
          }
          _pendingEntryOrders = null;
          OnPropertyChanged(nameof(CompositeName));
          TradingMacrosByPair(oc.prev).Where(tm => tm.BarPeriod > BarsPeriodType.t1)
          .ForEach(tm => tm.Pair = oc.curr);
        });

      this.WhenAnyValue(
        tm => tm.CorridorSDRatio,
        tm => tm.IsRatesLengthStable,
        tm => tm.TrendBlue,
        tm => tm.TrendRed,
        tm => tm.TrendPlum,
        tm => tm.TrendGreen,
        tm => tm.TrendLime,
        tm => tm.TimeFrameTreshold,
        tm => tm.CorridorCalcMethod,
        (v1, rls, v3, v4, v5, v6, v7, v8, v9) => new { v1, rls, v3, v4, v5, v6, v7, v8, v9 }
        )
        .Where(x => !IsAsleep && x.rls)
        .Subscribe(_ => {
          _mustResetAllTrendLevels = true;
          OnScanCorridor(RatesArray, () => { }, false);
        });
      this.WhenAnyValue(
        tm => tm.RatesMinutesMin,
        tm => tm.BarsCountMax,
        (rmm, bcm) => new { rmm, bcm }
        ).Subscribe(_ => ClearInternalRates());
      this.WhenAnyValue(
        tm => tm.RatesMinutesMin,
        tm => tm.BarsCount,
        tm => tm.BarsCountMax,
        tm => tm.BarPeriod,
        tm => tm.PairHedge,
        tm => tm.RatesLengthBy,
        tm => tm.HedgeCorrelation,
        tm => tm.CmaPasses,
        tm => tm.PriceCmaLevels,
        tm => tm.MovingAverageType,
        (v1, rls, v3, bp, ph, rlb, hc, cp, pcl, mat) => true
        )
        .Where(_ => !Pair.IsNullOrEmpty())
        .Throttle(1.FromSeconds())
        .Subscribe(_ => SyncHedgedPair());
      this.WhenAnyValue(
        tm => tm.PairHedge
        )
        .Where(_ => !Pair.IsNullOrEmpty())
        .Throttle(1.FromSeconds())
        .Subscribe(_ => TradingMacrosByPair(tm => tm != this).ForEach(tm => tm.PairHedge = _));
      this.WhenAnyValue(tm => tm.VoltageFunction)
        .Subscribe(_ => {
          ResetVoltage();
          if(_ == VoltageFunction.Straddle) {
            SyncStraddleHistoryT1(this);
            Log = new Exception($"{nameof(SyncStraddleHistoryT1)}[{this}] - done");
          }
        });
      (this).WhenAnyValue(tm => tm.VoltageFunction2)
        .Subscribe(_ => {
          ResetVoltage2();
          if(_ == VoltageFunction.Straddle) {
            TradingMacroM1(SyncStraddleHistoryM1);
            Log = new Exception($"{nameof(SyncStraddleHistoryM1)}[{this}] - done");
          }
        });
      var hedgeVoltFuns = new[] { VoltageFunction.HedgePrice, VoltageFunction.GrossV, VoltageFunction.HedgeRatio, };
      this.WhenAnyValue(tm => tm.Strategy)
        .Subscribe(s => {
        if(s.IsHedge()) {
          VoltageFunction2 = VoltageFunction.RatioDiff;
          VoltageFunction = VoltageFunction.GrossV;
          Log = new Exception(new { Set = new { VoltageFunction, VoltageFunction2 } } +"");
        }
      });
      this.WhenAnyValue(tm => tm.CurrentHedgePosition1
      , tm => tm.CurrentHedgePosition2
      , tm => tm.HedgeCalcIndex
      , tm => tm.HedgeCalcType
      , tm => tm.TradingRatio
      , tm => tm.TakeProfitXRatio
      , tm => tm.TradingRatioHedge
      ).Select(_ => true)
      .Merge((this).WhenAnyValue(tm => tm.HedgeQuantity).Select(_ => true))
      .Throttle(1.FromSeconds())
      .SubscribeToLatestOnBGThread(_ => {
        Log = new Exception("Hedge setting changed.");
        TradingMacroTrader(tm => tm.CalcHedgeRatios());
        TradingMacrosByPair().ForEach(tm => tm.OnCalcHedgeRatioByPositions(true));

        if(hedgeVoltFuns.Contains(VoltageFunction))
          TradingMacrosByPair(tm => tm.GetShowVoltageFunction()());
        if(hedgeVoltFuns.Contains(VoltageFunction2))
          TradingMacrosByPair(tm => tm.GetShowVoltageFunction(VoltageFunction2, 1)());
        if(Strategy.IsHedge())
          OnSetExitGrossByHedgeGrossess(true);
      });
      MessageBus.Current.Listen<ConnectionRestoredMessage>().Subscribe(_ => ClearInternalRates());
      _newsCaster.CountdownSubject
        .Where(nc => IsActive && Strategy != Strategies.None && nc.AutoTrade && nc.Countdown <= _newsCaster.AutoTradeOffset)
        .Subscribe(nc => {
          try {
            if(!RatesArray.Any())
              return;
            var height = CorridorStats.StDevByHeight;
            if(CurrentPrice.Average > MagnetPrice) {
              BuyLevel.Rate = MagnetPrice + height;
              SellLevel.Rate = MagnetPrice;
            } else {
              BuyLevel.Rate = MagnetPrice;
              SellLevel.Rate = MagnetPrice - height;
            }
            new[] { BuyLevel, SellLevel }.ForEach(sr => {
              sr.ResetPricePosition();
              sr.CanTrade = true;
              //sr.InManual = true;
            });
            DispatcherScheduler.Current.Schedule(5.FromSeconds(), () => nc.AutoTrade = false);
          } catch(Exception exc) { Log = exc; }
        });
      _waveShort = new WaveInfo(this);
      WaveShort.DistanceChanged += (s, e) => {
        OnPropertyChanged(() => WaveShortDistance);
        OnPropertyChanged(() => WaveShortDistanceInPips);
        _broadcastCorridorDateChanged();
      };
      //SuppRes.AssociationChanged += new CollectionChangeEventHandler(SuppRes_AssociationChanged);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<RequestPairForHistoryMessage>(this
        , a => {
          Debugger.Break();
          a.Pairs.Add(new Tuple<string, int>(this.Pair, this.BarPeriodInt));
        });
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<CloseAllTradesMessage<TradingMacro>>(this, a => {
        if(a.Sender.YieldNotNull().Any(tm => tm.Pair == Pair))
          return;
        if(IsActive && TradesManager != null) {
          if(Trades.Any())
            CloseTrading("CloseAllTradesMessage sent by " + a.Sender.Pair);
          a.OnClose(this);
        }
      });
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<TradeLineChangedMessage>(this, a => {
        if(a.Target == this && _strategyOnTradeLineChanged != null)
          _strategyOnTradeLineChanged(a);
      });
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<ShowSnapshotMatchMessage>(this, m => {
        if(SnapshotArguments.IsTarget && !m.StopPropagation) {
          m.StopPropagation = true;
          SnapshotArguments.DateStart = m.DateStart;
          SnapshotArguments.DateEnd = null;
          SnapshotArguments.IsTarget = false;
          SnapshotArguments.Label = m.Correlation.ToString("n2");
          //if (BarsCount != m.BarCount) BarsCount = m.BarCount;
          if(BarPeriodInt != m.BarPeriod)
            BarPeriod = (BarsPeriodType)m.BarPeriod;
          ClearInternalRates();
          RatesArray.Clear();
          CorridorStartDate = null;
          ShowSnaphot(m.DateStart, m.DateEnd);
          Scheduler.Default.Schedule(1.FromSeconds(), () => {
            try {
              CorridorStartDate = m.DateStart;
              CorridorStopDate = DateTime.MinValue;// RatesArray.SkipWhile(r => r.StartDate < CorridorStartDate).Skip(m.DateEnd - 1).First().StartDate;
            } catch(Exception exc) {
              Log = exc;
            }
          });
          Scheduler.Default.Schedule(10.FromSeconds(), () => SnapshotArguments.IsTarget = true);
        }
      });
      //MessageBus.Current.Listen<AppExitMessage>().Subscribe(_ => SaveActiveSettings());
    }
    private void ClearInternalRates() =>
      UseRatesInternal(ri => ri.SideEffect(__ => { Log = new Exception($"{this}: InternalRates cleared."); }).Clear());
    private void ResetVoltage() => UseRates(ra => {
      ra.ForEach(r => SetVoltage(r, double.NaN));
      GetVoltageHigh = GetVoltageAverage = GetVoltageLow = () => EMPTY_DOUBLE;
    });
    private void ResetVoltage2() => UseRates(ra => {
      ra.ForEach(r => SetVoltage2(r, double.NaN));
      GetVoltage2High = GetVoltage2Low = () => EMPTY_DOUBLE;
    });

    ~TradingMacro() {
      if(string.IsNullOrWhiteSpace(Pair))
        return;
      if(_TradesManager != null) {
        if(!IsInVirtualTrading && TradesManager != null && TradesManager.IsLoggedIn)
          TradesManager.DeleteOrders(Pair);
      } else {
        Log = new Exception(new { _TradesManager } + "");
      }
    }
  }
}