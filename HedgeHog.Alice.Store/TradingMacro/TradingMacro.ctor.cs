using HedgeHog.Bars;
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

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    public TradingMacro() {
      GroupRates = MonoidsCore.ToFunc((IList<Rate> rates) => GroupRatesImpl(rates, GroupRatesCount)).MemoizeLast(r => r.Last().StartDate);
      this.ObservableForProperty(tm => tm.Pair, false, false)
        .Where(_ => !IsInVirtualTrading)
        .Select(oc => oc.Value)
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
          _Rates.Clear();
          if(!oc.prev.IsNullOrWhiteSpace() && TradesManager != null) {
            TradesManager.CoreFX.SetOfferSubscription(Pair);
            OnLoadRates();
          }
          _pendingEntryOrders = null;
          OnPropertyChanged(nameof(CompositeName));
          SubscribeToEntryOrderRelatedEvents();
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
        ).Subscribe(_ => UseRatesInternal(ri => ri.SideEffect(__ => { Log = new Exception($"{Pair}: InternalRates cleared."); }).Clear()));
      this.WhenAnyValue(
        tm => tm.RatesMinutesMin,
        tm => tm.BarsCount,
        tm => tm.BarsCountMax,
        tm => tm.PairHedge,
        tm => tm.RatesLengthBy,
        tm => tm.HedgeCorrelation,
        (v1, rls, v3, ph, rlb, hc) => true).Subscribe(_ => SyncHedgedPair());
      this.WhenAnyValue(
        tm => tm.PairHedge
        )
        .Subscribe(_ => TradingMacrosByPair(tm => tm != this).ForEach(tm => tm.PairHedge = _));

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
          UseRatesInternal(ri => ri.Clear());
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