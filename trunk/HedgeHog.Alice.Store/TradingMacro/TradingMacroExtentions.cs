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
using HedgeHog.Shared.Messages;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    #region Subjects
    static TimeSpan THROTTLE_INTERVAL = TimeSpan.FromSeconds(1);

    #region LoadRates Broadcast
    BroadcastBlock<TradingMacro> _LoadRatesBroadcast;
    public BroadcastBlock<TradingMacro> LoadRatesBroadcast {
      [MethodImpl(MethodImplOptions.Synchronized)]
      get {
        if (_LoadRatesBroadcast == null) {
          _LoadRatesBroadcast = new BroadcastBlock<TradingMacro>(tm => tm);
          _LoadRatesBroadcast.AsObservable()
            .Subscribe(tm => tm.LoadRates());
        }
        return _LoadRatesBroadcast;
      }
    }
    public void OnLoadRates_() {
      LoadRatesBroadcast.SendAsync(this);
    }

    //          var bb = new BroadcastBlock<string>(s => { /*Debug.WriteLine("b:" + s);*/ return s; });
    //      bb.AsObservable().Buffer(1.FromSeconds()).Where(s=>s.Any()).Subscribe(s => {
    ////        Debug.WriteLine(DateTime.Now.ToString("mm:ss.fff") + Environment.NewLine + string.Join("\t" + Environment.NewLine, s));
    //        Debug.WriteLine(DateTime.Now.ToString("mm:ss.fff") + Environment.NewLine + "\t" + s.Count);
    //      });

    #endregion

    #region LoadRates Subject
    static object _LoadRatesSubjectLocker = new object();
    static ISubject<TradingMacro> _LoadRatesSubject;
    static ISubject<TradingMacro> LoadRatesSubject {
      get {
        lock (_LoadRatesSubjectLocker)
          if (_LoadRatesSubject == null) {
            _LoadRatesSubject = new Subject<TradingMacro>();
            _LoadRatesSubject
              .ObserveOn(NewThreadScheduler.Default)
              .Subscribe(tm => tm.LoadRates());
          }
        return _LoadRatesSubject;
      }
    }

    static ActionBlock<TradingMacro> _loadRatesAction = new ActionBlock<TradingMacro>(tm => tm.LoadRates());
    public void OnLoadRates() {
      _loadRatesAction.SendAsync(this);
      //LoadRatesSubject.OnNext(this);
    }
    #endregion


    #region EntryOrdersAdjust Subject
    static object _EntryOrdersAdjustSubjectLocker = new object();
    static ISubject<TradingMacro> _EntryOrdersAdjustSubject;
    static ISubject<TradingMacro> EntryOrdersAdjustSubject {
      get {
        lock (_EntryOrdersAdjustSubjectLocker)
          if (_EntryOrdersAdjustSubject == null) {
            _EntryOrdersAdjustSubject = new Subject<TradingMacro>();
            _EntryOrdersAdjustSubject
              .ObserveOn(Scheduler.Default)
              .Buffer(THROTTLE_INTERVAL)
              .Subscribe(tml => {
                tml.GroupBy(tm => tm.Pair).ToList().ForEach(tm => {
                  tm.Last().EntryOrdersAdjust();
                });
              });
            //.GroupByUntil(g => g.Pair, g => Observable.Timer(THROTTLE_INTERVAL))
            //.SelectMany(o => o.TakeLast(1))
            //.Subscribe(tm => tm.EntryOrdersAdjust());
          }
        return _EntryOrdersAdjustSubject;
      }
    }
    void OnEntryOrdersAdjust() {
      if (IsInVitualTrading) return;
      if (TradesManager.IsInTest || IsInPlayback)
        EntryOrdersAdjust();
      else
        EntryOrdersAdjustSubject.OnNext(this);
    }
    #endregion


    #region DeleteOrder Subject
    static object _DeleteOrderSubjectLocker = new object();
    static ISubject<string> _DeleteOrderSubject;
    ISubject<string> DeleteOrderSubject {
      get {
        lock (_DeleteOrderSubjectLocker)
          if (_DeleteOrderSubject == null) {
            _DeleteOrderSubject = new Subject<string>();
            _DeleteOrderSubject
              .DistinctUntilChanged()
              .Subscribe(s => {
                try {
                  GetFXWraper().DeleteOrder(s);
                } catch (Exception exc) { Log = exc; }
              }, exc => Log = exc);
          }
        return _DeleteOrderSubject;
      }
    }
    protected void OnDeletingOrder(string orderId) {
      DeleteOrderSubject.OnNext(orderId);
    }
    #endregion


    #region SetNet Subject
    static object _setNetSubjectLocker = new object();
    static ISubject<TradingMacro> _SettingStopLimits;
    static ISubject<TradingMacro> SettingStopLimits {
      get {
        lock (_setNetSubjectLocker)
          if (_SettingStopLimits == null) {
            _SettingStopLimits = new Subject<TradingMacro>();
            _SettingStopLimits
              .Do(tm => { if (tm == null)Debugger.Break(); })
              .GroupByUntil(tm => tm, g => Observable.Timer(THROTTLE_INTERVAL))
              .Select(g => g.TakeLast(1))
              .Subscribe(g => g.Subscribe(tm => {
                if (tm == null) GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(new Exception("SettingStopLimits: TradingMacro is null."));
                else
                  tm.SetNetStopLimit();
              }, exc => GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc))
              , exc => GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc), () => Debugger.Break())
              ;
          }
        return _SettingStopLimits;
      }
    }
    protected void OnSettingStopLimits() {
      if (TradesManager.IsInTest || IsInPlayback)
        SetNetStopLimit();
      else {
        var f = new Action<TradingMacro>(SettingStopLimits.OnNext);
        Observable.FromAsyncPattern<TradingMacro>(f.BeginInvoke, f.EndInvoke)(this);
      }
    }
    #endregion


    #region CreateEntryOrder Subject
    class CreateEntryOrderHelper {
      public string Pair { get; set; }
      public bool IsBuy { get; set; }
      public int Amount { get; set; }
      public double Rate { get; set; }
      public CreateEntryOrderHelper(string pair, bool isbuy, int amount, double rate) {
        this.Pair = pair;
        this.IsBuy = isbuy;
        this.Amount = amount;
        this.Rate = rate;
      }
    }
    static ISubject<CreateEntryOrderHelper> _CreateEntryOrderSubject;

    ISubject<CreateEntryOrderHelper> CreateEntryOrderSubject {
      get {
        if (_CreateEntryOrderSubject == null) {
          _CreateEntryOrderSubject = new Subject<CreateEntryOrderHelper>();
          _CreateEntryOrderSubject
            .GroupByUntil(g => new { g.Pair, g.IsBuy }, g => Observable.Timer(THROTTLE_INTERVAL))
              .SelectMany(o => o.TakeLast(1))
              .SubscribeOn(Scheduler.Default)
              .Subscribe(s => {
                try {
                  CheckPendingAction("EO", (pa) => { pa(); GetFXWraper().CreateEntryOrder(s.Pair, s.IsBuy, s.Amount, s.Rate, 0, 0); });
                } catch (Exception exc) {
                  Log = exc;
                }
              });
        }
        return _CreateEntryOrderSubject;
      }
    }

    void OnCreateEntryOrder(bool isBuy, int amount, double rate) {
      CreateEntryOrderSubject.OnNext(new CreateEntryOrderHelper(Pair, isBuy, amount, rate));
    }
    #endregion


    #region ScanCorridor Broadcast
    void OnScanCorridor(IList<Rate> rates) { ScanCorridor(rates); }
    #endregion

    #region OpenTrade Subject
    class __openTradeInfo {
      public Action<Action> action { get; set; }
      public bool isBuy { get; set; }
      public __openTradeInfo(Action<Action> action, bool isBuy) {
        this.action = action;
        this.isBuy = isBuy;
      }
    }
    TimeSpan OPEN_TRADE_THROTTLE_INTERVAL = 5.FromSeconds();

    #region OpenTrade Broadcast
    BroadcastBlock<__openTradeInfo> _OpenTradeBroadcast;
    BroadcastBlock<__openTradeInfo> OpenTradeBroadcast {
      [MethodImpl(MethodImplOptions.Synchronized)]
      get {
        if (_OpenTradeBroadcast == null) {
          _OpenTradeBroadcast = new BroadcastBlock<__openTradeInfo>(s => s);
          _OpenTradeBroadcast.AsObservable()
            .Throttle(OPEN_TRADE_THROTTLE_INTERVAL)
            .Do(oti => Log = new Exception("[" + Pair + "] OpenTradeByMASubject Queued: " + new { oti.isBuy }))
            .Subscribe(oti => CreateOpenTradeByMASubject(oti.isBuy, oti.action), exc => { Log = exc; });
        }
        return _OpenTradeBroadcast;
      }
    }
    Action<Rate> virtualOpenTrade = null;
    void OnOpenTradeBroadcast(Action<Action> p, bool isBuy) {
      if (IsInVitualTrading) {
        virtualOpenTrade = rate => {
          if (!CanTradeByMAFilter(rate, isBuy)) return;
          virtualOpenTrade = null;
          CheckPendingAction("OT", p);
        };
        virtualOpenTrade(RateLast);
        //CreateOpenTradeByMASubject(isBuy, p);
      } else
        OpenTradeBroadcast.SendAsync(new __openTradeInfo(p, isBuy));
    }
    Action<Rate> virtualCloseTrade = null;
    void OnCloseTradeBroadcast(Action a, bool isBuy) {
      virtualCloseTrade = rate => {
        virtualCloseTrade = null;
      };
    }
    #endregion
    #endregion

    #region OpenTradeByMA Subject
    public bool IsOpenTradeByMASubjectNull { get { return OpenTradeByMASubject == null && virtualOpenTrade == null; } }
    object _OpenTradeByMASubjectLocker = new object();
    ISubject<Rate> _OpenTradeByMASubject;
    public ISubject<Rate> OpenTradeByMASubject {
      get { return _OpenTradeByMASubject; }
      set {
        if (_OpenTradeByMASubject == value) return;
        _OpenTradeByMASubject = value;
        OnPropertyChanged(() => OpenTradeByMASubject);
        OnPropertyChanged(() => IsOpenTradeByMASubjectNull);
        if (value == null && !IsInVitualTrading)
          Log = new Exception("[" + Pair + "] OpenTradeByMASubject disposed.");
        RaiseShowChart();
      }
    }
    bool _isInPipserMode { get { return TakeProfitFunction == TradingMacroTakeProfitFunction.Spread; } }
    bool CanTradeByMAFilter(Rate rate, bool isBuy) {
      if (!_isInPipserMode) {
        if (!HasCorridor) DisposeOpenTradeByMASubject();
        var suppRes = EnsureActiveSuppReses(isBuy).SingleOrDefault();
        if (suppRes == null) return false;
      } else
        if (HasTradesByDistance(isBuy)) return false;
      return PriceCmaPeriod > 10
        ? (isBuy ? rate.PriceAvg > GetPriceMA(rate) : rate.PriceAvg < GetPriceMA(rate))
        : (isBuy ? CorridorCrossLowPrice(rate) > LoadPriceLow(rate) : CorridorCrossHighPrice(rate) < LoadPriceHigh(rate));
    }
    void DisposeOpenTradeByMASubject() {
      if (IsInVitualTrading)
        virtualOpenTrade = null;
      else {
        lock (_OpenTradeByMASubjectLocker) {
          if (OpenTradeByMASubject != null) {
            OpenTradeByMASubject.OnCompleted();
          }
        }
      }
    }
    void CreateOpenTradeByMASubject(bool isBuy, Action<Action> openTradeAction) {
      lock (_OpenTradeByMASubjectLocker)
        DisposeOpenTradeByMASubject();
      if (OpenTradeByMASubject == null) {
        OpenTradeByMASubject = new Subject<Rate>();
        OpenTradeByMASubject
          //.Timeout(DateTimeOffset.Now.AddMinutes(BarPeriodInt * 10))
          .Where(r => CanTradeByMAFilter(r, isBuy))
          .Take(1)
          .Subscribe(s => {
            CheckPendingAction("OT", openTradeAction);
          }
          , exc => {
            OpenTradeByMASubject = null;
          }
          , () => {
            OpenTradeByMASubject = null;
          });
      }
    }
    void OnOpenTradeByMA(Rate p) {
      if (IsInVitualTrading) {
        if (virtualOpenTrade != null)
          virtualOpenTrade(p);
      } else {
        if (p != null && OpenTradeByMASubject != null)
          try {
            OpenTradeByMASubject.OnNext(p);
          } catch (Exception exc) { Log = exc; }
      }
    }
    #endregion

    #endregion

    #region Pending Action
    bool HasPendingEntryOrders { get { return PendingEntryOrders.Count() > 0; } }
    static MemoryCache _pendingEntryOrders;
    MemoryCache PendingEntryOrders {
      [MethodImpl(MethodImplOptions.Synchronized)]
      get {
        if (_pendingEntryOrders == null)
          _pendingEntryOrders = new MemoryCache(Pair);
        return _pendingEntryOrders;
      }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void ReleasePendingAction(string key) {
      if (PendingEntryOrders.Contains(key)) {
        PendingEntryOrders.Remove(key);
        Debug.WriteLine("Pending[" + Pair + "] " + key + " released.");
      }
    }
    private bool HasPendingOrders() { return PendingEntryOrders.Any(); }
    private bool HasPendingKey(string key) { return !CheckPendingKey(key); }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private bool CheckPendingKey(string key) {
      return !PendingEntryOrders.Contains(key);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void CheckPendingAction(string key, Action<Action> action = null) {
      if (!HasPendingOrders()) {
        if (action != null) {
          try {
            Action a = () => {
              var cip = new CacheItemPolicy() { AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(1), RemovedCallback = ce => { if (!IsInVitualTrading) Log = new Exception(ce.CacheItem.Key + "[" + Pair + "] expired."); } };
              PendingEntryOrders.Add(key, DateTimeOffset.Now, cip);
            };
            action(a);
          } catch (Exception exc) {
            ReleasePendingAction(key);
            Log = exc;
          }
        }
      } else {
        Debug.WriteLine(Pair + "." + key + " is pending:" + PendingEntryOrders[key] + " in " + Lib.CallingMethod());
      }
    }
    #endregion

    #region Events

    event EventHandler ShowChartEvent;
    public event EventHandler ShowChart {
      add {
        if (ShowChartEvent == null || !ShowChartEvent.GetInvocationList().Contains(value))
          ShowChartEvent += value;
      }
      remove {
        ShowChartEvent -= value;
      }
    }
    void RaiseShowChart() {
      if (ShowChartEvent != null) ShowChartEvent(this, EventArgs.Empty);
    }

    #endregion

    #region ctor
    public TradingMacro() {
      _waveShort = new WaveInfo(this);
      WaveShort.DistanceChanged += (s, e) => {
        OnPropertyChanged(() => WaveShortDistance);
        OnPropertyChanged(() => WaveShortDistanceInPips);
        _broadcastCorridorDateChanged();
      };
      if(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher!=null)
        _processCorridorDatesChange = DataFlowProcessors.CreateYieldingActionOnDispatcher();
      SuppRes.AssociationChanged += new CollectionChangeEventHandler(SuppRes_AssociationChanged);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<RequestPairForHistoryMessage>(this
        , a => {
          Debugger.Break();
          a.Pairs.Add(new Tuple<string, int>(this.Pair, this.BarPeriodInt));
        });
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<CloseAllTradesMessage>(this, a => {
        if (IsActive && TradesManager != null && Trades.Any())
          CloseAtZero = true;
      });
    }
    ~TradingMacro() {
      var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
      if (fw != null && fw.IsLoggedIn)
        fw.DeleteOrders(fw.GetEntryOrders(Pair, true));
    }
    #endregion

    #region SuppRes Event Handlers
    void SuppRes_AssociationChanged(object sender, CollectionChangeEventArgs e) {
      switch (e.Action) {
        case CollectionChangeAction.Add:
          ((Store.SuppRes)e.Element).RateChanged += SuppRes_RateChanged;
          ((Store.SuppRes)e.Element).Scan += SuppRes_Scan;
          ((Store.SuppRes)e.Element).IsActiveChanged += SuppRes_IsActiveChanged;
          ((Store.SuppRes)e.Element).EntryOrderIdChanged += SuppRes_EntryOrderIdChanged;
          break;
        case CollectionChangeAction.Refresh:
          ((EntityCollection<SuppRes>)sender).ToList()
            .ForEach(sr => {
              sr.RateChanged += SuppRes_RateChanged;
              sr.Scan += SuppRes_Scan;
              sr.IsActiveChanged += SuppRes_IsActiveChanged;
              sr.EntryOrderIdChanged += SuppRes_EntryOrderIdChanged;
            });
          break;
        case CollectionChangeAction.Remove:
          ((Store.SuppRes)e.Element).RateChanged -= SuppRes_RateChanged;
          ((Store.SuppRes)e.Element).Scan -= SuppRes_Scan;
          ((Store.SuppRes)e.Element).IsActiveChanged -= SuppRes_IsActiveChanged;
          ((Store.SuppRes)e.Element).EntryOrderIdChanged -= SuppRes_EntryOrderIdChanged;
          break;
      }
      SetEntryOrdersBySuppResLevels();
    }

    void SuppRes_Scan(object sender, EventArgs e) {
      if (IsInVitualTrading) return;
      var sr = (sender as SuppRes);
      var rate = sr.Rate;
      var range = 15;
      var a = ScanCrosses(rate - PointSize * range, rate + PointSize * range);
      sr.Rate = a.OrderByDescending(t => t.Item1).First().Item2;
      RaiseShowChart();
    }

    #region ScanCrosses
    private List<Tuple<int, double>> ScanCrosses(double levelStart, double levelEnd, double stepInPips = 1) {
      return ScanCrosses(RatesArray, levelStart, levelEnd, stepInPips);
    }
    private List<Tuple<int, double>> ScanCrosses(IList<Rate> rates, double stepInPips = 1) {
      if (!rates.Any()) return new List<Tuple<int, double>>();
      return ScanCrosses(rates, rates.Min(r => r.PriceAvg), rates.Max(r => r.PriceAvg), stepInPips);
    }
    private List<Tuple<int, double>> ScanCrosses(IList<Rate> rates, double levelStart, double levelEnd, double stepInPips = 1) {
      var step = PointSize * stepInPips;
      var steps = new List<double>();
      for (; levelStart <= levelEnd; levelStart += step)
        steps.Add(levelStart);
      return Partitioner.Create(steps).AsParallel().Select(s => new Tuple<int, double>(GetCrossesCount(rates, s), s)).ToList();
    }
    #endregion

    void SuppRes_EntryOrderIdChanged(object sender, SuppRes.EntryOrderIdEventArgs e) {
      var fw = GetFXWraper();
      if (!string.IsNullOrWhiteSpace(e.OldId) && fw != null)
        try {
          OnDeletingOrder(e.OldId);
          //fw.DeleteOrder(e.OldId);
        } catch (Exception exc) {
          Log = exc;
        }
    }

    void SuppRes_IsActiveChanged(object sender, EventArgs e) {
      try {
        var suppRes = (SuppRes)sender;
        var fw = GetFXWraper();
        if (fw != null && !suppRes.IsActive) {
          SetEntryOrdersBySuppResLevels();
          fw.GetEntryOrders(Pair, true).IsBuy(suppRes.IsBuy).ToList()
            .ForEach(o => fw.DeleteOrder(o.OrderID));
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void SuppRes_RateChanged(object sender, EventArgs e) {
      return;
      SetEntryOrdersBySuppResLevels();
    }
    #endregion

    public Guid SessionIdSuper { get; set; }
    static Guid _sessionId = Guid.NewGuid();
    public static Guid SessionId { get { return _sessionId; } }
    public void ResetSessionId() { ResetSessionId(Guid.Empty); }
    public void ResetSessionId(Guid superId ) {
      _sessionId = Guid.NewGuid();
      GlobalStorage.UseForexContext(c => {
        c.t_Session.AddObject(new DB.t_Session() { Uid = _sessionId, SuperUid = superId, Timestamp = DateTime.Now });
        c.SaveChanges();
      });
    }

    public string CompositeId { get { return Pair + "_" + PairIndex; } }

    public string CompositeName { get { return Pair + ":" + BarPeriod; } }

    void ReloadPairStats() {
      GlobalStorage.UseForexContext(f => {
        this.TimeFrameStats = f.s_GetBarStats(Pair, BarPeriodInt, BarsCount, DateTime.Parse("2008-01-01")).ToArray();
      });
    }

    #region MonthStatistics
    class MonthStatistics : Models.ModelBase {
      #region MonthLow
      private DateTime _MonthLow;
      public DateTime MonthLow {
        get { return _MonthLow; }
        set {
          if (_MonthLow != value) {
            _MonthLow = value;
            _stats = null;
          }
        }
      }
      #endregion
      #region MonthHigh
      private DateTime _MonthHigh;
      public DateTime MonthHigh {
        get { return _MonthHigh; }
        set {
          if (_MonthHigh != value) {
            _MonthHigh = value;
            _stats = null;
          }
        }
      }
      #endregion
      #region Hour
      private int _Hour;
      public int Hour {
        get { return _Hour; }
        set {
          if (_Hour != value) {
            _Hour = value;
            _heightMin = double.NaN;
          }
        }
      }

      #endregion
      readonly IList<DB.s_GetBarStats_Result> _dbStats;
      public MonthStatistics(IList<DB.s_GetBarStats_Result> dbStats) {
        _dbStats = dbStats;
      }
      IList<DB.s_GetBarStats_Result> _stats;
      IList<DB.s_GetBarStats_Result> GetStats(DateTime date) {
        this.MonthLow = date.Round(MathExtensions.RoundTo.Day).AddDays(-3);
        this.MonthHigh = date.Round(MathExtensions.RoundTo.Day);
        return _stats ?? (_stats = _dbStats.Where(s => s.StopDateMonth.Value.Between(MonthLow, MonthHigh)).ToArray());
      }
      double _heightMin = double.NaN;
      public double GetHeightMin(DateTime date) {
        Hour = date.Hour;
        var hourHigh = (24 + Hour + 2) % 24;
        var hourLow = (24 + Hour - 2) % 24;
        Func<int,bool> compare = (hour) => {
          if (hourLow < hourHigh) return hour.Between(hourLow, hourHigh);
          return !hour.Between(hourLow, hourHigh);
        };
        if (double.IsNaN(_heightMin)) {
          var a = GetStats(date).Where(s => s.StopDateMonth < date.Date || s.StopDateHour < Hour);
          a = a.Where(s => compare(s.StopDateHour.Value));
          var b = a.Select(s => s.BarsHeightAvg.Value).DefaultIfEmpty(double.NaN).ToArray();
          var avg = b.Average();
          var stDev = b.StDev();
          var d = b.Where(v => v < avg + stDev).Average();
          _heightMin = d;// a.Select(s => s.BarsHeightAvg).DefaultIfEmpty(double.NaN).Average().Value;
        }
        return _heightMin;
      }
    }
    MonthStatistics _MonthStats;
    #endregion

    partial void OnPairChanged() {
      _inPips = null;
      _pointSize = double.NaN;
      _pipCost = double.NaN;
      _BaseUnitSize = 0;
      GlobalStorage.UseForexContext(f => {
        this._blackoutTimes = f.v_BlackoutTime.ToArray();
      });
      OnPropertyChanged(TradingMacroMetadata.CompositeName);
    }
    partial void OnLimitBarChanged() { OnPropertyChanged(TradingMacroMetadata.CompositeName); }

    public bool IsBlackoutTime {
      get {
        var BlackoutHoursTimeframe = 0;
        if (BlackoutHoursTimeframe == 0) return false;
        var r = _blackoutTimes.Any(b => RateLast.StartDate.Between(b.Time.AddHours(-BlackoutHoursTimeframe), b.Time));
        return r;
        //return _blackoutTimes.Where(b => RateLast.StartDate.Between(b.TimeStart.Value.LocalDateTime, b.TimeStop.LocalDateTime)).Any();
      }
    }

    #region LotSize
    int _lotSize;
    public int LotSize {
      get { return _lotSize; }
      set {
        if (_lotSize == value) return;
        _lotSize = value;
        OnPropertyChanged("LotSize");
      }
    }

    private double _LotSizePercent;
    public double LotSizePercent {
      get { return _LotSizePercent; }
      set {
        if (_LotSizePercent != value) {
          _LotSizePercent = value;
          OnPropertyChanged("LotSizePercent");
        }
      }
    }

    private int _LotSizeByLossBuy;
    public int LotSizeByLossBuy {
      get { return _LotSizeByLossBuy; }
      set {
        if (_LotSizeByLossBuy != value) {
          _LotSizeByLossBuy = value;
          OnPropertyChanged("LotSizeByLossBuy");
        }
      }
    }
    private int _LotSizeByLossSell;
    public int LotSizeByLossSell {
      get { return _LotSizeByLossSell; }
      set {
        if (_LotSizeByLossSell != value) {
          _LotSizeByLossSell = value;
          OnPropertyChanged("LotSizeByLossSell");
        }
      }
    }
    int _currentLot;
    public int CurrentLot {
      get { return _currentLot; }
      set {
        if (_currentLot == value) return;
        _currentLot = value;
        OnPropertyChanged("CurrentLot");
      }
    }
    #endregion

    double TakeProfitInDollars { get { return TakeProfitPips * LotSize / 10000; } }
    private double _TakeProfitPips;
    public double TakeProfitPips {
      get { return _TakeProfitPips; }
      set {
        if (_TakeProfitPips != value) {
          if (!_useTakeProfitMin || value < _TakeProfitPips) {
            _TakeProfitPips = value;
            OnPropertyChanged("TakeProfitPips");
          }
        }
      }
    }
    #region Corridor Stats



    public IEnumerable<CorridorStatistics> GetCorridorStats() { return CorridorStatsArray.OrderBy(cs => cs.Iterations); }
    public CorridorStatistics GetCorridorStats(int iterations) {
      if (iterations <= 0) return CorridorStatsArray.OrderBy(c => c.Iterations).Take(-iterations + 1).Last();
      var cs = CorridorStatsArray.Where(c => c.Iterations == iterations).SingleOrDefault();
      if (cs == null) {
        GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher
          .Invoke(new Action(() => {
            CorridorStatsArray.Add(new CorridorStatistics(this));
          }));
        CorridorStatsArray.Last().Iterations = iterations;
        return CorridorStatsArray.Last();
      }
      return cs;
    }

    partial void OnGannAnglesOffsetChanged() {
      if (RatesArraySafe.Count() > 0) {
        var rateLast = RatesArraySafe.Last();
        if (CorridorStats != null) {
          SetGannAngles();
          var slope = CorridorStats.Slope;
          Predicate<double> filter = ga => slope < 0 ? rateLast.PriceAvg > ga : rateLast.PriceAvg < ga;
          var index = GetGannAngleIndex(GannAngleActive);// GetGannIndex(rateLast, slope);
          if (index >= 0)
            GannAngleActive = index;
          //else
          //  Debugger.Break();
        }
      }
      OnPropertyChanged(Metadata.TradingMacroMetadata.GannAnglesOffset_);
    }

    private static int GetGannIndex(Rate rateLast, double slope) {
      var gann = slope > 0
        ? rateLast.GannPrices.Where(ga => rateLast.PriceAvg > ga).DefaultIfEmpty().Max()
        : rateLast.GannPrices.Where(ga => rateLast.PriceAvg < ga).DefaultIfEmpty().Min();
      var index = rateLast.GannPrices.ToList().IndexOf(gann);
      return index;
    }

    partial void OnGannAnglesChanged() {
      _gannAngles = GannAnglesList.FromString(GannAngles).Where(a => a.IsOn).Select(a => a.Value).ToList();
      OnPropertyChanged("GannAngles_");
      return;
      _gannAngles = GannAngles.Split(',')
        .Select(a => (double)System.Linq.Dynamic.DynamicExpression.ParseLambda(new ParameterExpression[0], typeof(double), a).Compile().DynamicInvoke())
        .ToList();
    }
    List<double> _gannAngles;
    public List<double> GannAnglesArray { get { return _gannAngles; } }

    public double Slope { get { return CorridorStats == null ? 0 : CorridorStats.Slope; } }
    public int GetGannAngleIndex(int indexOld) {
      return -1;
      var ratesForGann = ((IList<Rate>)SetGannAngles()).Reverse().ToList();
      if (Slope != 0 && ratesForGann.Count > 0) {
        var testList = new List<Tuple<Rate, Rate>>();
        ratesForGann.Aggregate((rp, rn) => {
          testList.Add(new Tuple<Rate, Rate>(rp, rn));
          return rn;
        });
        Func<Rate, Rate, int, bool> cross1 = (r1, r2, gannIndex) => {
          var gannLow = Math.Min(r1.GannPrices[gannIndex], r2.GannPrices[gannIndex]);
          var gannHigh = Math.Max(r1.GannPrices[gannIndex], r2.GannPrices[gannIndex]);
          var ask = Math.Max(r1.PriceHigh, r2.PriceHigh);
          var bid = Math.Min(r1.PriceLow, r2.PriceLow);
          return gannLow < ask && gannHigh > bid;
        };
        Func<Rate, Rate, int> cross2 = (rp, rn) =>
          rn.GannPrices.Select((gp, i) =>
            new { i, cross = cross1(rp, rn, i) })
            .Where(a => a.cross).DefaultIfEmpty(new { i = -1, cross = false }).Last().i;
        Predicate<Tuple<Rate, Rate>> cross3 = t => cross2(t.Item1, t.Item2) >= 0;
        var rateCross = testList.Find(cross3);
        if (rateCross != null && (_rateGannCurrentLast == null || _rateGannCurrentLast < rateCross.Item2)) {
          _rateGannCurrentLast = rateCross.Item2;
          if (rateCross != null) return cross2(rateCross.Item1, rateCross.Item2);
        }
        return indexOld;
      }
      return -1;
    }

    void cs_PropertyChanged(object sender, PropertyChangedEventArgs e) {
      var cs = (sender as CorridorStatistics);
      switch (e.PropertyName) {
        case Metadata.CorridorStatisticsMetadata.StartDate:
          if (!IsGannAnglesManual) SetGannAngleOffset(cs);
          break;
      }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private void SetGannAngleOffset(CorridorStatistics cs) {
      GannAnglesOffset = cs.Slope.Abs() / GannAngle1x1;
    }
    private ObservableCollection<CorridorStatistics> _CorridorStatsArray;
    public ObservableCollection<CorridorStatistics> CorridorStatsArray {
      get {
        if (_CorridorStatsArray == null) {
          _CorridorStatsArray = new ObservableCollection<CorridorStatistics>();
          _CorridorStatsArray.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(CorridorStatsArray_CollectionChanged);
        }
        //  _CorridorStatsArray = new CorridorStatistics[] { new CorridorStatistics(this), new CorridorStatistics(this), new CorridorStatistics(this) };
        return _CorridorStatsArray;
      }
      set {
        if (_CorridorStatsArray != value) {
          _CorridorStatsArray = value;
          OnPropertyChanged("CorridorStatsArray");
        }
      }
    }

    void CorridorStatsArray_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
      if (e.Action == NotifyCollectionChangedAction.Add)
        (e.NewItems[0] as CorridorStatistics).PropertyChanged += cs_PropertyChanged;
    }

    CorridorStatistics _corridorBig;
    public CorridorStatistics CorridorBig {
      get { return _corridorBig ?? new CorridorStatistics(); }
      set {
        if (_corridorBig == value) return;
        _corridorBig = value;
      }
    }



    public bool HasCorridor { get { return CorridorStats.IsCurrent; } }
    private CorridorStatistics _CorridorStats;
    public CorridorStatistics CorridorStats {
      get { return _CorridorStats ?? new CorridorStatistics(); }
      set {
        _CorridorStats = value;
        if (_CorridorStats != null) {
          _CorridorStats.PeriodsJumped += CorridorStats_PeriodsJumped;
        }
        if (value != null) {
          var doRegressionLevels = true;// Strategy.HasFlag(Strategies.Trailer01);
          if (doRegressionLevels) {
            if (SetTrendLines != null)
              SetTrendLines();
            else {
              RatesArray.ForEach(r => r.PriceAvg1 = r.PriceAvg2 = r.PriceAvg3 = r.PriceAvg02 = r.PriceAvg03 = r.PriceAvg21 = r.PriceAvg31 = double.NaN);
              CorridorStats.Rates
                .SetCorridorPrices(CorridorStats.Coeffs
                  , CorridorStats.HeightUp0, CorridorStats.HeightDown0
                  , CorridorStats.HeightUp, CorridorStats.HeightDown
                  , CorridorStats.HeightUp0 * 3, CorridorStats.HeightDown0 * 3
                  , r => doRegressionLevels ? r.PriceAvg1 : MagnetPrice, (r, d) => r.PriceAvg1 = d
                  , (r, d) => r.PriceAvg02 = d, (r, d) => r.PriceAvg03 = d
                  , (r, d) => r.PriceAvg2 = d, (r, d) => r.PriceAvg3 = d
                  , (r, d) => r.PriceAvg21 = d, (r, d) => r.PriceAvg31 = d
                );
            }
            OnOpenTradeByMA(RatesArray.LastOrDefault(r => r.PriceAvg1 > 0));
          }
          CorridorAngle = CorridorStats.Slope;
          var tp = CalculateTakeProfit();
          TakeProfitPips = InPips(tp);
          if (!Trades.Any())
            LimitRate = GetLimitRate(GetPriceMA(_CorridorStats.Rates[0]) < _CorridorStats.Rates[0].PriceAvg1);
          //if (tp < CurrentPrice.Spread * 3) CorridorStats.IsCurrent = false;
          if (false && !IsGannAnglesManual)
            SetGannAngleOffset(value);
          UpdateTradingGannAngleIndex();
        }
        //}

        #region PropertyChanged
        RaiseShowChart();
        OnPropertyChanged(TradingMacroMetadata.CorridorStats);
        OnPropertyChanged(TradingMacroMetadata.HasCorridor);
        #endregion
      }
    }

    void CorridorStats_PeriodsJumped(object sender, EventArgs e) {
      if (false && HasCorridor)
        ForceOpenTrade = CorridorStats.Slope < 0;
    }
    public void UpdateTradingGannAngleIndex() {
      if (CorridorStats == null) return;
      int newIndex = GetGannAngleIndex(GannAngleActive);
      if (true || newIndex > GannAngleActive)
        GannAngleActive = newIndex;
    }

    private int GetGannAngleIndex_() {
      var rateLast = RatesArraySafe.Last();
      Predicate<double> filter = ga => CorridorStats.Slope > 0 ? rateLast.PriceAvg < ga : rateLast.PriceAvg > ga;
      return rateLast.GannPrices.ToList().FindLastIndex(filter);
    }

    public List<Rate> SetGannAngles() {
      return new List<Rate>();
      if (true || CorridorStats == null) return new List<Rate>();
      RatesArraySafe.ToList().ForEach(r => Enumerable.Range(0, GannAnglesArray.Count).ToList()
        .ForEach(i => { if (r.GannPrices.Length > i) r.GannPrices[i] = 0; }));
      var ratesForGann = RatesArraySafe.SkipWhile(r => r.StartDate < this.GannAnglesAnchorDate.GetValueOrDefault(CorridorStats.StartDate)).ToList();
      var rateStart = this.GannAnglesAnchorDate.GetValueOrDefault(new Func<DateTime>(() => {
        var rateStop = Slope > 0 ? ratesForGann.OrderBy(r => r.PriceAvg).LastOrDefault() : ratesForGann.OrderBy(r => r.PriceAvg).FirstOrDefault();
        if (rateStop == null) return DateTime.MinValue;
        var ratesForStart = ratesForGann.Where(r => r < rateStop);
        if (ratesForStart.Count() == 0) ratesForStart = ratesForGann;
        return (CorridorStats.Slope > 0 ? ratesForStart.OrderBy(CorridorStats.priceLow).First() : ratesForStart.OrderBy(CorridorStats.priceHigh).Last()).StartDate;
      })());
      ratesForGann = ratesForGann.Where(r => r.StartDate >= rateStart).OrderBars().ToList();
      if (ratesForGann.Count == 0) return new List<Rate>();
      //var interseption = Slope > 0 ? Math.Min(ratesForGann[0].PriceAvg3, ratesForGann[0].PriceLow) : Math.Max(ratesForGann[0].PriceAvg2, ratesForGann[0].PriceHigh);
      var interseption = Slope > 0 ? ratesForGann[0].PriceLow : ratesForGann[0].PriceHigh;
      Enumerable.Range(0, ratesForGann.Count()).AsParallel().ForAll(i => {
        var rate = ratesForGann[i];
        if (rate.GannPrices.Length != GannAnglesArray.Count) rate.GannPrices = new double[GannAnglesArray.Count];
        for (var j = 0; j < GannAnglesArray.Count; j++) {
          double tangent = GannAnglesArray[j] * PointSize * GannAnglesOffset.GetValueOrDefault();
          var coeffs = new[] { interseption, Math.Sign(CorridorStats.Slope) * tangent };
          rate.GannPrices[j] = coeffs.RegressionValue(i);
        }
      });
      for (var i = 0; i < ratesForGann.Count(); i++) {
      }
      return ratesForGann;
    }
    public double GannAngle1x1 { get { return PointSize; } }
    private int _GannAngleActive = -1;
    /// <summary>
    /// Index of active Gann Angle
    /// </summary>
    public int GannAngleActive {
      get { return _GannAngleActive; }
      set { _GannAngleActive = value; }
    }
    double GannPriceForTrade() { return GannPriceForTrade(GetLastRateWithGannAngle()); }
    double GannPriceForTrade(Rate rateLast) {
      if (GannAngleActive >= 0 && rateLast.GannPrices.Length > GannAngleActive && GannAngleActive.Between(0, GannAnglesArray.Count - 1))
        return rateLast.GannPrices[GannAngleActive];
      return double.NaN;
    }

    //Dimok: Need to implement FindTrendAngle
    void FindTrendAngle(ICollection<Rate> rates) {


    }
    #endregion

    #region Correlations
    public double Correlation_P;
    public double Correlation_R;

    public double Correlation {
      get {
        return (Correlation_P + Correlation_R) / 2;
        return new double[] { Correlation_P, Correlation_R }.OrderBy(c => c.Abs()).First();
      }
    }

    public void SetCorrelation(string currency, double correlation) {
      if (Currency1 == currency) Correlation1 = correlation;
      if (Currency2 == currency) Correlation2 = correlation;
    }

    public string Currency1 { get { return (Pair + "").Split('/').DefaultIfEmpty("").ToArray()[0]; } }
    public string Currency2 { get { return (Pair + "").Split('/').Skip(1).DefaultIfEmpty("").ToArray()[0]; } }

    private double _Correlation1;
    public double Correlation1 {
      get { return _Correlation1; }
      set {
        if (_Correlation1 != value) {
          _Correlation1 = value;
          OnPropertyChanged("Correlation1");
        }
      }
    }

    private double _Correlation2;
    public double Correlation2 {
      get { return _Correlation2; }
      set {
        if (_Correlation2 != value) {
          _Correlation2 = value;
          OnPropertyChanged("Correlation2");
        }
      }
    }
    #endregion

    #region Overlap
    public int OverlapTotal { get { return Overlap.ToInt() + Overlap5; } }

    double _overlap;
    public double Overlap {
      get { return _overlap; }
      set {
        if (_overlap == value) return;
        _overlap = value;
        OnPropertyChanged("Overlap");
        OnPropertyChanged("OverlapTotal");
      }
    }

    int _overlap5;
    public int Overlap5 {
      get { return _overlap5; }
      set {
        if (_overlap5 == value) return;
        _overlap5 = value;
        OnPropertyChanged(TradingMacroMetadata.Overlap5);
        OnPropertyChanged(TradingMacroMetadata.OverlapTotal);
      }
    }
    #endregion

    #region TicksPerMinute
    public bool IsTicksPerMinuteOk {
      get {
        return Math.Max(TicksPerMinuteInstant, TicksPerMinute) < TicksPerMinuteAverage;
      }
    }
    public int TicksPerMinuteMaximun { get { return new double[] { TicksPerMinute, TicksPerMinuteAverage, TicksPerMinuteInstant }.Max().ToInt(); } }
    public int TicksPerMinuteMinimum { get { return new double[] { TicksPerMinute, TicksPerMinuteAverage, TicksPerMinuteInstant }.Min().ToInt(); } }
    public double TicksPerMinuteInstant { get { return PriceQueue.TickPerMinute(.25); } }
    public double TicksPerMinute { get { return PriceQueue.TickPerMinute(.5); } }
    public double TicksPerMinuteAverage { get { return PriceQueue.TickPerMinute(1); } }

    int priceQueueCount = 600;
    public class TicksPerPeriod {
      Queue<Price> priceStackByPair = new Queue<Price>();
      int maxCount;
      public TicksPerPeriod(int maxCount) {
        this.maxCount = maxCount;
      }
      private IEnumerable<Price> GetQueue(double period) {
        lock (priceStackByPair) {
          if (period <= 1) period = (priceStackByPair.Count * period).ToInt();
          return priceStackByPair.Take(period.ToInt());
        }
      }
      public void Add(Price price, DateTime serverTime) {
        lock (priceStackByPair) {
          var queue = priceStackByPair;
          if ((price.Time - serverTime).Duration() < TimeSpan.FromMinutes(1)) {
            if (queue.Count > maxCount) queue.Dequeue();
            queue.Enqueue(price);
          }
        }
      }
      public double TickPerMinute(double period) {
        return TickPerMinute(GetQueue(period));
      }

      public DateTime LastTickTime() {
        lock (priceStackByPair) {
          return priceStackByPair.Count == 0 ? DateTime.MaxValue : priceStackByPair.Max(p => p.Time);
        }
      }
      private static double TickPerMinute(IEnumerable<Price> queue) {
        if (queue.Count() < 10) return 10;
        var totalMinutes = (queue.Max(p => p.Time) - queue.Min(p => p.Time)).TotalMinutes;
        return queue.Count() / Math.Max(1, totalMinutes);
      }
      public double Speed(double period) {
        return Speed(GetQueue(period));
      }
      public static double Speed(IEnumerable<Price> queue) {
        if (queue.Count() < 2) return 0;
        var distance = 0.0;
        for (var i = 1; i < queue.Count(); i++)
          distance += (queue.ElementAt(i).Average - queue.ElementAt(i - 1).Average).Abs();
        var totalMinutes = (queue.Max(p => p.Time) - queue.Min(p => p.Time)).TotalMinutes;
        return totalMinutes == 0 ? 0 : distance / totalMinutes;
      }
    }

    TicksPerPeriod _PriceQueue;
    public TicksPerPeriod PriceQueue {
      get {
        if (_PriceQueue == null) _PriceQueue = new TicksPerPeriod(priceQueueCount);
        return _PriceQueue;
      }
    }
    public void TicksPerMinuteSet(Price price, DateTime serverTime) {
      PriceQueue.Add(price, serverTime);
      OnPropertyChanged(TradingMacroMetadata.TicksPerMinuteInstant);
      OnPropertyChanged(TradingMacroMetadata.TicksPerMinute);
      OnPropertyChanged(TradingMacroMetadata.TicksPerMinuteAverage);
      OnPropertyChanged(TradingMacroMetadata.TicksPerMinuteMaximun);
      OnPropertyChanged(TradingMacroMetadata.TicksPerMinuteMinimum);
      OnPropertyChanged(TradingMacroMetadata.IsTicksPerMinuteOk);
      OnPropertyChanged(TradingMacroMetadata.PipsPerMinute);
      OnPropertyChanged(TradingMacroMetadata.PipsPerMinuteCmaFirst);
      OnPropertyChanged(TradingMacroMetadata.PipsPerMinuteCmaLast);
      OnPropertyChanged(TradingMacroMetadata.CurrentGross);
      OnPropertyChanged(TradingMacroMetadata.CurrentGrossInPips);
      OnPropertyChanged(TradingMacroMetadata.OpenTradesGross);
      OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
    }
    #endregion

    public double PipsPerMinute { get { return InPips(PriceQueue.Speed(.25)); } }
    public double PipsPerMinuteCmaFirst { get { return InPips(PriceQueue.Speed(.5)); } }
    public double PipsPerMinuteCmaLast { get { return InPips(PriceQueue.Speed(1)); } }

    public double OpenTradesGross {
      get { return Trades.Gross() - (TradesManager == null ? 0 : TradesManager.CommissionByTrades(Trades)); }
    }

    public double CurrentGross {
      get { return CurrentLoss + OpenTradesGross; }
    }

    public double CurrentGrossInPips {
      get { return TradesManager == null ? double.NaN : TradesManager.MoneyAndLotToPips(CurrentGross, Trades.Length == 0 ? AllowedLotSizeCore() : Trades.NetLots().Abs(), Pair); }
    }

    public double CurrentLossInPips {
      get { return TradesManager == null ? double.NaN : TradesManager.MoneyAndLotToPips(CurrentLoss, Trades.Length == 0 ? AllowedLotSizeCore() : Trades.NetLots().Abs(), Pair); }
    }

    public int PositionsBuy { get { return Trades.IsBuy(true).Length; } }
    public int PositionsSell { get { return Trades.IsBuy(false).Length; } }

    public double PipsPerPosition {
      get {
        var trades = Trades.ToArray();
        return trades.Length < 2 ? (trades.Length == 1 ? trades[0].PL : 0) : InPips(trades.Max(t => t.Open) - trades.Min(t => t.Open)) / (trades.Length - 1);
      }
    }

    private int _HistoricalGrossPL;
    public int HistoricalGrossPL {
      get { return _HistoricalGrossPL; }
      set {
        if (_HistoricalGrossPL != value) {
          _HistoricalGrossPL = value;
          OnPropertyChanged("HistoricalGrossPL");
        }
      }
    }

    public bool IsTradingHours {
      get {
        return true /*Trades.Length > 0 || RatesArraySafe.StartDate.TimeOfDay.Hours.Between(3, 10)*/;
      }
    }

    struct TradeSignal {
      public double OpenPrice { get; set; }
      public double ClosePrice { get; set; }
      public bool IsActive { get; set; }
    }

    public double CalculateCloseProfitInPips() {
      return InPips(CalculateCloseProfit());
    }
    public double CalculateCloseProfit() {
      switch (Strategy) {
        default:
          return CalculateTakeProfit();
      }
    }
    public double CalculateCloseLossInPips() {
      return InPips(CalculateCloseLoss());
    }
    public double CalculateCloseLoss() {
      switch (Strategy) {
        default:
          return RangeRatioForTradeStop < 0 ? RangeRatioForTradeStop : -CalculateTakeProfit() * RangeRatioForTradeStop;
      }
    }

    #region Last Rate
    private Rate GetLastRateWithGannAngle() {
      return GetLastRate(RatesArraySafe.SkipWhile(r => r.GannPrices.Length == 0).TakeWhile(r => r.GannPrices.Length > 0).ToList());
    }
    private Rate GetLastRate() { return GetLastRate(RatesArraySafe); }
    private Rate GetLastRate(ICollection<Rate> rates) {
      if (!rates.Any()) return null;
      var rateLast = rates.Skip(rates.Count - 2)
        .LastOrDefault(LastRateFilter);
      return rateLast ?? rates.Last();
    }

    private bool LastRateFilter(Rate r) {
      return r.StartDate <= CurrentPrice.Time - TimeSpan.FromMinutes((int)BarPeriod);
    }
    #endregion

    #region Price Funcs

    static Func<Rate, double> gannPriceHigh = rate => rate.PriceAvg;
    static Func<Rate, double> gannPriceLow = rate => rate.PriceAvg;

    static Func<Rate, double> suppResPriceHigh = rate => rate.PriceHigh;
    static Func<Rate, double> suppResPriceLow = rate => rate.PriceLow;
    #endregion

    static object _tradesFromReportLock = new object();
    static List<Trade> _tradesFromReport;
    List<Trade> tradesFromReport {
      get {
        lock (_tradesFromReportLock) {
          if (_tradesFromReport == null)
            _tradesFromReport = GetFXWraper().GetTradesFromReport(DateTime.Now.AddDays(-7), DateTime.Now);
        }
        return _tradesFromReport;
      }
    }
    #region TradesManager 'n Stuff
    private IDisposable _priceChangedSubscribsion;
    public IDisposable PriceChangedSubscribsion {
      get { return _priceChangedSubscribsion; }
      set {
        if (_priceChangedSubscribsion == value) return;
        if (_priceChangedSubscribsion != null) _priceChangedSubscribsion.Dispose();
        _priceChangedSubscribsion = value;
      }
    }
    EventHandler<TradeEventArgs> _tradeCloseHandler;
    EventHandler<TradeEventArgs> TradeCloseHandler {
      get {
        return _tradeCloseHandler ?? (_tradeCloseHandler = TradesManager_TradeClosed);
      }
    }

    EventHandler<TradeEventArgs> _TradeAddedHandler;
    EventHandler<TradeEventArgs> TradeAddedHandler {
      get {
        return _TradeAddedHandler ?? (_TradeAddedHandler = TradesManager_TradeAddedGlobal);
      }
    }

    delegate double InPipsDelegate(string pair, double? price);
    InPipsDelegate _inPips;
    public double InPips(double? d) {
      if (_inPips == null)
        _inPips = TradesManager.InPips;
      return _inPips == null ? double.NaN : _inPips(Pair, d);
    }

    private const int RatesHeightMinimumOff = 0;
    Func<ITradesManager> _TradesManager = () => null;
    public ITradesManager TradesManager { get { return _TradesManager(); } }
    public void SubscribeToTradeClosedEVent(Func<ITradesManager> getTradesManager) {
      _inPips = null;
      this._TradesManager = getTradesManager;
      this.TradesManager.TradeClosed += TradeCloseHandler;
      this.TradesManager.TradeAdded += TradeAddedHandler;
      var fw = GetFXWraper();
      if (fw != null && !IsInPlayback) {
        //_priceChangedSubscribsion = Observable.FromEventPattern<EventHandler<PriceChangedEventArgs>
        //  , PriceChangedEventArgs>(h => h, h => _TradesManager().PriceChanged += h, h => _TradesManager().PriceChanged -= h)
        //  .Where(pce=>pce.EventArgs.Pair == Pair)
        //  .Sample((0.1).FromSeconds())
        //  .Subscribe(pce => RunPriceChanged(pce.EventArgs, null), exc => Log = exc);
        fw.PriceChangedBroadcast.AsObservable()
          .Where(pce => pce.Pair == Pair)
          .Do(pce => {
            CurrentPrice = pce.Price;
            if (!TradesManager.IsInTest && !IsInPlayback)
              AddCurrentTick(pce.Price);
            TicksPerMinuteSet(pce.Price, TradesManager.ServerTime);
            OnPropertyChanged(TradingMacroMetadata.PipsPerPosition);
          })
          .Subscribe(pce => RunPriceChanged(pce, null), exc => Log = exc, () => Log = new Exception(Pair + " got terminated."));

        fw.CoreFX.LoggingOff += CoreFX_LoggingOffEvent;
        fw.OrderAdded += TradesManager_OrderAdded;
        fw.OrderChanged += TradesManager_OrderChanged;
        fw.OrderRemoved += TradesManager_OrderRemoved;
        if (isLoggedIn) {
          RunningBalance = tradesFromReport.ByPair(Pair).Sum(t => t.NetPL);
          CalcTakeProfitDistance();
        }
      }
      RaisePositionsChanged();
    }

    void TradesManager_OrderChanged(object sender, OrderEventArgs e) {
      if (!IsMyOrder(e.Order) || !e.Order.IsNetOrder) return;
      CalcTakeProfitDistance();
    }

    void CoreFX_LoggingOffEvent(object sender, Order2GoAddIn.LoggedInEventArgs e) {
      var fw = GetFXWraper();
      if (fw == null) return;
      fw.GetEntryOrders(Pair, true).ToList().ForEach(o => fw.DeleteOrder(o.OrderID, false));
      SetNetStopLimit();
    }

    void TradesManager_OrderAdded(object sender, OrderEventArgs e) {
      if (!IsMyOrder(e.Order)) return;
      if (e.Order.IsEntryOrder) {
        EnsureActiveSuppReses();
        try {
          var key = "EO";
          ReleasePendingAction(key);
        } catch (Exception exc) {
          Log = exc;
        }
      }
      try {
        TakeProfitDistance = CalcTakeProfitDistance();
        var order = e.Order;
        var fw = GetFXWraper();
        if (fw != null && !order.IsNetOrder) {
          var orders = GetEntryOrders();
          orders.IsBuy(true).OrderBy(o => o.OrderID).Skip(1)
            .Concat(orders.IsBuy(false).OrderBy(o => o.OrderID).Skip(1))
            .ToList().ForEach(o => OnDeletingOrder(o.OrderID));
        }
        SetEntryOrdersBySuppResLevels();
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void TradesManager_OrderRemoved(Order order) {
      if (!IsMyOrder(order)) return;
      EnsureActiveSuppReses();
      SuppRes.Where(sr => sr.EntryOrderId == order.OrderID).ToList().ForEach(sr => sr.EntryOrderId = Store.SuppRes.RemovedOrderTag);
      SetEntryOrdersBySuppResLevels();
    }

    void TradesManager_TradeAddedGlobal(object sender, TradeEventArgs e) {
      if (!IsMyTrade(e.Trade)) return;
      DisposeOpenTradeByMASubject();
      ReleasePendingAction("OT");
      EnsureActiveSuppReses();
      SetEntryOrdersBySuppResLevels();
      RaisePositionsChanged();
      if (_strategyExecuteOnTradeOpen != null) _strategyExecuteOnTradeOpen(e.Trade);
    }

    bool IsMyTrade(Trade trade) { return trade.Pair == Pair; }
    bool IsMyOrder(Order order) { return order.Pair == Pair; }
    public void UnSubscribeToTradeClosedEVent(ITradesManager tradesManager) {
      PriceChangedSubscribsion = null;
      if (this.TradesManager != null) {
        this.TradesManager.TradeClosed -= TradeCloseHandler;
        this.TradesManager.TradeAdded -= TradeAddedHandler;
      }
      if (tradesManager != null) {
        tradesManager.TradeClosed -= TradeCloseHandler;
        tradesManager.TradeAdded -= TradeAddedHandler;
      }
    }
    void TradesManager_TradeClosed(object sender, TradeEventArgs e) {
      if (!IsMyTrade(e.Trade)) return;
      DisposeOpenTradeByMASubject();
      CurrentLot = Trades.Sum(t => t.Lots);
      EnsureActiveSuppReses();
      SetEntryOrdersBySuppResLevels();
      RaisePositionsChanged();
      RaiseShowChart();
      ReleasePendingAction("OT");
      ReleasePendingAction("CT");
      if (_strategyExecuteOnTradeClose != null) _strategyExecuteOnTradeClose(e.Trade);
      if (IsInVitualTrading)
        GlobalStorage.UseForexContext(c => {
          var session = c.t_Session.Single(s => s.Uid == SessionId);
          session.MaximumLot = HistoryMaximumLot;
          session.MinimumGross = this.MinimumGross;
          session.Profitability = Profitability;
          c.SaveChanges();
        });
    }

    private void RaisePositionsChanged() {
      OnPropertyChanged("PositionsSell");
      OnPropertyChanged("PositionsBuy");
      OnPropertyChanged("PipsPerPosition");
    }
    string[] sessionInfoCategories = new[] { categoryActive, categorySession, categoryActiveFuncs };
    string _sessionInfo = "";
    public string SessionInfo {
      get {
        if (string.IsNullOrEmpty(_sessionInfo)) {
          var l = new List<string>();
          foreach (var p in GetType().GetProperties()) {
            var ca = p.GetCustomAttributes(typeof(CategoryAttribute), false).FirstOrDefault() as CategoryAttribute;
            if (ca != null && sessionInfoCategories.Contains(ca.Category)) {
              l.Add(p.Name + ":" + p.GetValue(this, null));
            }
          }
          _sessionInfo = string.Join(",", l);
        }
        return _sessionInfo;
      }
    }
    bool HasShutdownStarted { get { return ((bool)GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(new Func<bool>(() => GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.HasShutdownStarted), new object[0])); } }

    public void Replay(ReplayArguments args) {
      Action<RepayPauseMessage> pra = m => args.InPause = !args.InPause;
      Action<RepayBackMessage> sba = m => args.StepBack = true;
      Action<RepayForwardMessage> sfa = m => args.StepForward = true;
      try {
        if (!IsInVitualTrading)
          UnSubscribeToTradeClosedEVent(TradesManager);
        SetPlayBackInfo(true, args.DateStart.GetValueOrDefault(), args.DelayInSeconds.FromSeconds());
        var framesBack = 3;
        var barsCountTotal = BarsCount * framesBack;
        var actionBlock = new ActionBlock<Action>(a => a());
        Action<Order2GoAddIn.FXCoreWrapper.RateLoadingCallbackArgs<Rate>> cb = callBackArgs => PriceHistory.SaveTickCallBack(BarPeriodInt, Pair, o => Log = new Exception(o + ""), actionBlock, callBackArgs);
        var fw = GetFXWraper();
        if (fw != null)
          PriceHistory.AddTicks(fw, BarPeriodInt, Pair, args.DateStart.GetValueOrDefault(DateTime.Now.AddMinutes(-barsCountTotal * 2)), o => Log = new Exception(o + ""));
        //GetFXWraper().GetBarsBase<Rate>(Pair, BarPeriodInt, barsCountTotal, args.DateStart.GetValueOrDefault(TradesManagerStatic.FX_DATE_NOW), TradesManagerStatic.FX_DATE_NOW, new List<Rate>(), cb);
        var moreMinutes = (args.DateStart.Value.DayOfWeek == DayOfWeek.Monday ? 17*60+24*60 : args.DateStart.Value.DayOfWeek == DayOfWeek.Saturday ? 1440 : 0) ;
        var rates = args.DateStart.HasValue
          ? GlobalStorage.GetRateFromDB(Pair, args.DateStart.Value.AddMinutes(-BarsCount * BarPeriodInt - 2880 * 1.5), int.MaxValue, BarPeriodInt)
          : GlobalStorage.GetRateFromDBBackward(Pair, RatesArraySafe.Last().StartDate, barsCountTotal, BarPeriodInt);
        var rateStart = rates.SkipWhile(r => r.StartDate < args.DateStart.Value).First();
        var rateStartIndex = rates.IndexOf(rateStart);
        var rateIndexStart = (rateStartIndex - BarsCount).Max(0);
        rates.RemoveRange(0, rateIndexStart);
        var dateStop = args.MonthsToTest > 0 ? args.DateStart.Value.AddDays(args.MonthsToTest * 30.5) : DateTime.MaxValue;
        if (args.MonthsToTest > 0) {
          //rates = rates.Where(r => r.StartDate <= args.DateStart.Value.AddDays(args.MonthsToTest*30.5)).ToList();
          if ((rates[0].StartDate - rates.Last().StartDate).Duration().TotalDays < args.MonthsToTest * 30) {
            args.ResetSuperSession();
            return;
          }
        }
        #region Init stuff
        RatesInternal.Clear();
        RateLast = null;
        WaveDistanceForTrade = double.NaN;
        WaveLength = 0;
        _waves = null;
        _sessionInfo = "";
        _buyLevelRate = _sellLevelRate = double.NaN;
        _isSelfStrategy = false;
        WaveShort.Reset();
        CloseAtZero = _trimAtZero = false;
        CurrentLoss = MinimumGross = HistoryMaximumLot = 0;
        ForEachSuppRes(sr => {
          sr.CanTrade = false; 
          sr.TradesCount = 0;
          sr.InManual = false;
          sr.CorridorDate = DateTime.MinValue;
        });
        if (CorridorStartDate != null) CorridorStartDate = null;
        if (CorridorStats != null) CorridorStats = null;
        WaveHigh = null;
        LastProfitStartDate = null;
        DisposeOpenTradeByMASubject();
        _waveRates.Clear();
        _strategyExecuteOnTradeClose = null;
        var currentPosition = -1;
        var indexCurrent = 0;
        #endregion
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Register(this, pra);
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Register(this, sba);
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Register(this, sfa);
        var vm = (VirtualTradesManager)TradesManager;
        var tms = args.TradingMacros.Cast<TradingMacro>().ToArray();
        Func<Rate[]> getLastRates = () => vm.RatesByPair().Where(kv => kv.Key != Pair).Select(kv => kv.Value.LastBC()).ToArray();
        while (!args.MustStop && indexCurrent < rates.Count && Strategy != Strategies.None) {
          while (!args.IsMyTurn(this)) {
            //Task.Factory.StartNew(() => {
            //  while (!args.IsMyTurn(this) && !args.MustStop)
                Thread.Sleep(0);
            //}).Wait();
          }
          if (tms.Length == 1 && currentPosition > 0 && currentPosition != args.CurrentPosition) {
            var index = (args.CurrentPosition * (rates.Count - BarsCount) / 100.0).ToInt();
            RatesInternal.Clear();
            RatesInternal.AddRange(rates.Skip(index).Take(BarsCount - 1));
          }
          Rate rate;
          try {
            if (args.StepBack) {
              args.InPause = true;
              rate = rates.Previous(RatesInternal[0]);
              if (rate != null) {
                RatesInternal.Insert(0, rate);
                RatesInternal.Remove(RatesInternal.Last());
                indexCurrent--;
              }
            } else {
              if (RateLast != null && tms.Length > 1) {
                var a = tms.Select(tm => tm.RatesInternal.LastByCountOrDefault(new Rate()).StartDate).ToArray();
                var dateMin = a.Min();
                if ((dateMin - a.Max()).Duration().TotalMinutes > 30) {
                  Log = new Exception("MaxTime-MinTime>30mins");
                }
                if (RateLast.StartDate > dateMin)
                  continue;
              }
              rate = rates[indexCurrent++];
              if (rate != null)
                if (RatesInternal.Count == 0 || rate > RatesInternal.LastBC())
                  RatesInternal.Add(rate);
                else {
                  Debugger.Break();
                }
              while (RatesInternal.Count > BarsCount 
                  && (!DoStreatchRates || (CorridorStats.Rates.Count == 0 || RatesInternal[0] < CorridorStats.Rates.LastBC())))
                RatesInternal.RemoveAt(0);
            }
            if (rate.StartDate > dateStop) {
              if (CurrentGross > 0) {
                CloseTrades();
                break;
              }
            }
            if (RatesArraySafe.Count < BarsCount)
              TurnOffSuppRes(RatesInternal.Select(r => r.PriceAvg).DefaultIfEmpty().Average());
            else {
              LastRatePullTime = RateLast.StartDate;
              //TradesManager.RaisePriceChanged(Pair, RateLast);
              var d = Stopwatch.StartNew();
              if (rate != null) {
                args.CurrentPosition = currentPosition = (100.0 * (indexCurrent - BarsCount) / (rates.Count - BarsCount)).ToInt();

                var price = new Price(Pair, RateLast, TradesManager.ServerTime, TradesManager.GetPipSize(Pair), TradesManager.GetDigits(Pair), true);
                TradesManager.RaisePriceChanged(Pair, RateLast);
                //RunPriceChanged(new PriceChangedEventArgs(Pair, price, TradesManager.GetAccount(), new Trade[0]), null);
                ReplayEvents();
                if (TradesManager.GetAccount().Equity < 25000) {
                  Log = new Exception("Equity Alert: " + TradesManager.GetAccount().Equity);
                  args.MustStop = true;
                }
                Profitability = (TradesManager.GetAccount().Equity - 50000) / (RateLast.StartDate - args.DateStart.Value).TotalDays * 30.5;
              } else
                Log = new Exception("Replay:End");
              ReplayCancelationToken.ThrowIfCancellationRequested();
              Thread.Sleep((args.DelayInSeconds - d.Elapsed.TotalSeconds).Max(0).FromSeconds());
              Func<bool> inPause = () => args.InPause || !IsTradingActive;
              if (inPause()) {
                args.StepBack = args.StepForward = false;
                Task.Factory.StartNew(() => {
                  while (inPause() && !args.StepBack && !args.StepForward && !args.MustStop)
                    Thread.Sleep(200);
                }).Wait();
              }
            }
          } finally {
            args.NextTradingMacro();
          }
        }
      } catch (Exception exc) {
        Log = exc;
      } finally {
        try {
          args.SessionStats.ProfitToLossRatio = ProfitabilityRatio;
          TradesManager.ClosePair(Pair);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Unregister(this, pra);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Unregister(this, sba);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Unregister(this, sfa);
          SetPlayBackInfo(false, args.DateStart.GetValueOrDefault(), args.DelayInSeconds.FromSeconds());
          DisposeOpenTradeByMASubject();
          args.MustStop = args.StepBack = args.StepBack = args.InPause = false;
          if (!IsInVitualTrading) {
            RatesInternal.Clear();
            SubscribeToTradeClosedEVent(_TradesManager);
            LoadRates();
          }
        } catch (Exception exc) {
          Log = exc;
        }
      }
    }

    private void ReplayEvents() {
      OnPropertyChanged(TradingMacroMetadata.CurrentGross);
      OnPropertyChanged(TradingMacroMetadata.CurrentGrossInPips);
      OnPropertyChanged(TradingMacroMetadata.OpenTradesGross);
      OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
    }


    #endregion

    #region TradesStatistics
    protected Dictionary<string, TradeStatistics> TradeStatisticsDictionary = new Dictionary<string, TradeStatistics>();
    public void SetTradesStatistics(Price price, Trade[] trades) {
      foreach (var trade in trades)
        SetTradeStatistics(price, trade);
    }
    public TradeStatistics SetTradeStatistics(Price price, Trade trade) {
      if (!TradeStatisticsDictionary.ContainsKey(trade.Id))
        TradeStatisticsDictionary.Add(trade.Id, new TradeStatistics());
      var ts = TradeStatisticsDictionary[trade.Id];
      if (false) {
        if (!trade.Buy && ts.Resistanse == 0 && HasCorridor)
          ts.Resistanse = CorridorRates.OrderBars().Max(CorridorStats.priceHigh);
        if (trade.Buy && ts.Support == 0 && HasCorridor)
          ts.Support = CorridorRates.OrderBars().Min(CorridorStats.priceLow);
      }
      return ts;
    }

    private IEnumerable<Rate> CorridorRates {
      get {
        return RatesArraySafe.Where(r => r.StartDate >= CorridorStats.StartDate);
      }
    }
    private IEnumerable<Rate> GannAngleRates {
      get {
        return RatesArraySafe.SkipWhile(r => r.GannPrice1x1 == 0);
      }
    }
    public TradeStatistics GetTradeStatistics(Trade trade) {
      return TradeStatisticsDictionary.ContainsKey(trade.Id) ? TradeStatisticsDictionary[trade.Id] : null;
    }
    #endregion

    int _PriceCmaDirection;

    public int PriceCmaDirection {
      get { return _PriceCmaDirection; }
      set { _PriceCmaDirection = value; }
    }

    private double _CorridorAngle;
    public double CorridorAngle {
      get { return _CorridorAngle; }
      set {
        if (PointSize != 0) {
          var ca = CalculateAngle(value);
          if (Math.Sign(ca) != Math.Sign(_CorridorAngle) && _corridorDirectionChanged != null)
            _corridorDirectionChanged(this, EventArgs.Empty);
          _CorridorAngle = ca;
          OnPropertyChanged(TradingMacroMetadata.CorridorAngle);
        }
      }
    }
    event EventHandler _corridorDirectionChanged;
    private double CalculateAngle(double value) {
      return value.Angle(PointSize);
    }

    #region SuppReses

    void AdjustSuppResCount() {
      foreach (var isSupport in new[] { false, true }) {
        while (SuppRes.Where(sr => sr.IsSupport == isSupport).Count() > SuppResLevelsCount)
          RemoveSuppRes(SuppRes.Where(sr => sr.IsSupport == isSupport).Last());
        while (SuppRes.Where(sr => sr.IsSupport == isSupport).Count() < SuppResLevelsCount)
          AddSuppRes(RatesArray.Average(r => r.PriceAvg), isSupport);
      }
      RaiseShowChart();
    }

    private bool IsEntityStateOk {
      get {
        return EntityState != System.Data.EntityState.Detached && EntityState != System.Data.EntityState.Deleted;
      }
    }
    const double suppResDefault = double.NaN;

    public void SuppResResetAllTradeCounts(int tradesCount = 0) { SuppResResetTradeCounts(SuppRes, tradesCount); }
    public static void SuppResResetTradeCounts(IEnumerable<SuppRes> suppReses, double tradesCount = 0) {
      if (tradesCount < 0)
        suppReses.ToList().ForEach(sr => sr.TradesCount = Math.Max(Store.SuppRes.TradesCountMinimum, sr.TradesCount + tradesCount));
      else suppReses.ToList().ForEach(sr => sr.TradesCount = Math.Max(Store.SuppRes.TradesCountMinimum, tradesCount));
    }

    private Store.SuppRes SupportLow() {
      return Supports.OrderBy(s => s.Rate).First();
    }

    private Store.SuppRes SupportHigh() {
      return Supports.OrderBy(s => s.Rate).Last();
    }
    private Store.SuppRes Support0() {
      return SupportByPosition(0);
    }
    private Store.SuppRes Support1() {
      return SupportByPosition(1);
    }
    private Store.SuppRes SupportByPosition(int position) {
      return SuppRes.Where(sr => sr.IsSupport).Skip(position).First();
    }
    private Store.SuppRes[] SupportsNotCurrent() {
      return SuppResNotCurrent(Supports);
    }

    private Store.SuppRes ResistanceLow() {
      return Resistances.OrderBy(s => s.Rate).First();
    }

    private Store.SuppRes ResistanceHigh() {
      return Resistances.OrderBy(s => s.Rate).Last();
    }
    private Store.SuppRes Resistance0() {
      return ResistanceByPosition(0);
    }
    private Store.SuppRes Resistance1() {
      return ResistanceByPosition(1);
    }

    private Store.SuppRes ResistanceByPosition(int position) {
      return SuppRes.Where(sr => !sr.IsSupport).Skip(position).First();
    }

    private Store.SuppRes[] ResistancesNotCurrent() {
      return SuppResNotCurrent(Resistances);
    }
    private Store.SuppRes[] SuppResNotCurrent(SuppRes[] suppReses) {
      return suppReses.OrderBy(s => (s.Rate - CurrentPrice.Ask).Abs()).Skip(1).ToArray();
    }

    private SuppRes[] IndexSuppReses(SuppRes[] suppReses) {
      if (!IsActive) return suppReses;
      if (suppReses.Any(a => a.Index == 0)) {
        var index = 1;
        suppReses.OrderByDescending(a => a.Rate).ToList().ForEach(a => {
          a.Index = index++;
        });
        return suppReses;
        if (Trades.Length > 0) {
          var trade = Trades.OrderBy(t => t.Time).Last();
          var lots = (Trades.Sum(t => t.Lots) + LotSize) / LotSize;
          var lot = lots / 2;
          var rem = lots % 2;
          var tcBuy = lot + (trade.Buy ? rem : 0);
          var tcSell = lot + (!trade.Buy ? rem : 0);
          if (tcBuy > 0) SuppResResetTradeCounts(Resistances, tcBuy);
          if (tcSell > 0) SuppResResetTradeCounts(Supports, tcSell);
        }
      }
      return suppReses;
    }
    #endregion

    #region Supports/Resistances
    #region Add
    public SuppRes AddSupport(double rate) { return AddSuppRes(rate, true); }
    public SuppRes AddResistance(double rate) { return AddSuppRes(rate, false); }
    public SuppRes AddBuySellRate(double rate, bool isBuy) { return AddSuppRes(rate, !isBuy); }
    public SuppRes AddSuppRes(double rate, bool isSupport) {
      try {
        var srs = (isSupport ? Supports : Resistances);
        var index = srs.Select(a => a.Index).DefaultIfEmpty(0).Max() + 1;
        var sr = new SuppRes { Rate = rate, IsSupport = isSupport, TradingMacroID = UID, UID = Guid.NewGuid(), TradingMacro = this, Index = index, TradesCount = srs.Select(a => a.TradesCount).DefaultIfEmpty().Max() };
        GlobalStorage.UseAliceContext(c => c.SuppRes.AddObject(sr));
        GlobalStorage.UseAliceContext(c => c.SaveChanges());
        return sr;
      } catch (Exception exc) {
        Log = exc;
        return null;
      } finally {
        SetEntryOrdersBySuppResLevels();
      }
    }
    #endregion
    #region Update
    /// <summary>
    /// Used for manual (drag) position change
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="rateNew"></param>
    public void UpdateSuppRes(Guid uid, double rateNew) {
      var suppRes = SuppRes.ToArray().SingleOrDefault(sr => sr.UID == uid);
      if (suppRes == null)
        throw new InvalidOperationException("SuppRes UID:" + uid + " does not exist.");
      suppRes.Rate = rateNew;
      suppRes.InManual = true;
    }

    #endregion
    #region Remove
    public void RemoveSuppRes(Guid uid) {
      try {
        var suppRes = SuppRes.SingleOrDefault(sr => sr.UID == uid);
        RemoveSuppRes(suppRes);
      } catch (Exception exc) {
        Log = exc;
      } finally {
        SetEntryOrdersBySuppResLevels();
      }
    }

    private void RemoveSuppRes(Store.SuppRes suppRes) {
      if (suppRes != null) {
        SuppRes.Remove(suppRes);
        GlobalStorage.UseAliceContext(c => c.DeleteObject(suppRes));
      }
    }
    #endregion

    partial void OnSupportPriceStoreChanging(double? value) {
      //if (value.GetValueOrDefault() > 0)
      //  Application.Current.Dispatcher.BeginInvoke(new Action(() => {
      //    if (!SuppRes.Any(sr => sr.Rate == value.GetValueOrDefault()))
      //      AddSupport(value.GetValueOrDefault());
      //  }));
    }
    partial void OnResistancePriceStoreChanging(double? value) {
      //if (value.GetValueOrDefault() > 0)
      //  Application.Current.Dispatcher.BeginInvoke(new Action(() => {
      //    if (!SuppRes.Any(sr => sr.Rate == value.GetValueOrDefault()))
      //      AddResistance(value.GetValueOrDefault());
      //  }));
    }

    private static string GetSuppResRateErrorMessage(double rate) {
      return "Rate " + rate + " is not unique in " + Metadata.AliceEntitiesMetadata.SuppRes + " table";
    }
    object supportsLocker = new object();
    public SuppRes[] Supports {
      get {
        lock (supportsLocker) {
          return IndexSuppReses(SuppRes.Where(sr => sr.IsSupport).OrderBy(a => a.Rate).ToArray());
        }
      }
    }
    object resistancesLocker = new object();
    public SuppRes[] Resistances {
      get {
        lock (resistancesLocker)
          return IndexSuppReses(SuppRes.Where(sr => !sr.IsSupport).OrderBy(a => a.Rate).ToArray());
      }
    }


    public double[] SuppResPrices(bool isSupport) {
      return SuppRes.Where(sr => sr.IsSupport == isSupport).Select(sr => sr.Rate).ToArray();
    }
    public double[] SupportPrices { get { return Supports.Select(sr => sr.Rate).ToArray(); } }
    public double[] ResistancePrices { get { return Resistances.Select(sr => sr.Rate).ToArray(); } }
    #endregion

    static Func<Rate, double> centerOfMassBuy = r => r.PriceHigh;
    static Func<Rate, double> centerOfMassSell = r => r.PriceLow;

    public Rate[][] CentersOfMass { get; set; }
    double _CenterOfMassSell = double.NaN;
    public double CenterOfMassSell {
      get {
        return !double.IsNaN(_CenterOfMassSell) ? _CenterOfMassSell : centerOfMassSell(CenterOfMass);
      }
    }
    double _CenterOfMassBuy = double.NaN;
    public double CenterOfMassBuy {
      get { return !double.IsNaN(_CenterOfMassBuy) ? _CenterOfMassBuy : centerOfMassBuy(CenterOfMass); }
    }
    private Rate _CenterOfMass = new Rate();
    public Rate CenterOfMass {
      get { return _CenterOfMass; }
      set {
        if (_CenterOfMass != value) {
          _CenterOfMass = value;
          OnPropertyChanged(TradingMacroMetadata.CenterOfMass);
        }
      }
    }

    public double SuppResMinimumDistance { get { return CurrentPrice.Spread * 2; } }

    double LockPriceHigh(Rate rate) { return _stDevPriceLevelsHigh[1](rate); }
    double LoadPriceHigh(Rate rate) { return _stDevPriceLevelsHigh[1](rate); }
    Func<Rate, double>[] _stDevPriceLevelsHigh = new Func<Rate, double>[] { 
      r => r.PriceAvg02, 
      r => r.PriceAvg2, 
      r => r.PriceAvg21 
    };
    double LockPriceLow(Rate rate) { return _stDevPriceLevelsLow[1](rate); }
    double LoadPriceLow(Rate rate) { return _stDevPriceLevelsLow[1](rate); }
    Func<Rate, double>[] _stDevPriceLevelsLow = new Func<Rate, double>[] { 
      r => r.PriceAvg03, 
      r => r.PriceAvg3, 
      r => r.PriceAvg31 
    };

    #region MagnetPrice
    private double CalcMagnetPrice(IList<Rate> rates = null) {
      return (rates ?? CorridorStats.Rates).Average(r => r.PriceAvg);
      //return (double)((rates ?? CorridorStats.Rates).Sum(r => (decimal)r.PriceAvg / (decimal)(r.Spread * r.Spread)) / (rates ?? CorridorStats.Rates).Sum(r => 1 / (decimal)(r.Spread * r.Spread)));
      return (double)((rates ?? CorridorStats.Rates).Sum(r => r.PriceAvg / r.Spread) / (rates ?? CorridorStats.Rates).Sum(r => 1 / r.Spread));
    }
    private void SetMagnetPrice(IList<Rate> rates = null) {
      try {
        //var rates = RatesArray.Where(r => r.Volume > 0 && r.Spread > 0 && r.PriceStdDev>0).ToList();
        //MagnetPrice = rates.Sum(r => r.PriceAvg / r.Volume) / rates.Sum(r => 1.0 / r.Volume);
        //MagnetPrice = rates.Sum(r => r.PriceAvg * r.PriceStdDev * r.PriceStdDev) / rates.Sum(r => r.PriceStdDev * r.PriceStdDev);
        MagnetPrice = CalcMagnetPrice(rates);
        //MagnetPrice = _levelCounts[0].Item2;
      } catch { }
    }
    private double _MagnetPrice = double.NaN;
    public double MagnetPrice {
      get { return _MagnetPrice; }
      set {
        if (_MagnetPrice != value) {
          _MagnetPrice = value;
          OnPropertyChanged("MagnetPrice");
        }
      }
    }
    private double _MagnetPricePosition;
    /// <summary>
    /// Position relatively mean
    /// </summary>
    public double MagnetPricePosition {
      get { return _MagnetPricePosition; }
      set {
        if (_MagnetPricePosition != value) {
          _MagnetPricePosition = value;
          OnPropertyChanged("MagnetPricePosition");
        }
      }
    }
    #endregion

    class RatesAreNotReadyException : Exception { }
    Schedulers.BackgroundWorkerDispenser<string> backgroundWorkers = new Schedulers.BackgroundWorkerDispenser<string>();

    Rate _rateLast;
    public Rate RateLast {
      get { return _rateLast; }
      set {
        if (_rateLast != value) {
          _rateLast = value;
          OnPropertyChanged("RateLast");
        }
      }
    }

    Rate _ratePrev;
    public Rate RatePrev {
      get { return _ratePrev; }
      set {
        if (_ratePrev == value) return;
        _ratePrev = value;
        OnPropertyChanged("RateLast");
      }
    }

    #region RatePrev1
    private Rate _RatePrev1;
    public Rate RatePrev1 {
      get { return _RatePrev1; }
      set {
        if (_RatePrev1 != value) {
          _RatePrev1 = value;
          OnPropertyChanged("RatePrev1");
        }
      }
    }

    #endregion
    object _rateArrayLocker = new object();
    List<Rate> _rateArray = new List<Rate>();
    public List<Rate> RatesArray {
      get { return _rateArray; }
      set { _rateArray = value == null ? new List<Rate>() : value; }
    }
    double _ratesSpreadSum;
    public List<Rate> RatesArraySafe {
      get {
        if (!Monitor.TryEnter(_rateArrayLocker, 1000)) {
          Log = new Exception("_rateArrayLocker was busy for more then 1 second.");
          throw new TimeoutException();
        }
        try {
          if (RatesInternal.Count < Math.Max(1, BarsCount)) {
            //Log = new RatesAreNotReadyException();
            return new List<Rate>();
          }
          var rateLast = RatesInternal.Last();
          var rs = rateLast.AskHigh - rateLast.BidLow;
          if (rateLast != RateLast || rs != _ratesSpreadSum || _rateArray == null || !_rateArray.Any()) {
            _ratesSpreadSum = rs;
            #region Quick Stuff
            RateLast = RatesInternal[RatesInternal.Count - 1];
            RatePrev = RatesInternal[RatesInternal.Count - 2];
            RatePrev1 = RatesInternal[RatesInternal.Count - 3];
            _rateArray = GetRatesSafe().ToList();
            if (IsInVitualTrading)
              Trades.ToList().ForEach(t => t.UpdateByPrice(TradesManager, CurrentPrice));
            RatesHeight = _rateArray.Height(r => r.PriceAvg, out _RatesMin, out _RatesMax);//CorridorStats.priceHigh, CorridorStats.priceLow);
            PriceSpreadAverage = _rateArray.Average(r => r.PriceSpread);//.ToList().AverageByIterations(2).Average();
            #endregion

            SpreadForCorridor = RatesArray.Spread();

            SetMA();
            var cp = CorridorPrice();
            var corridor = _rateArray.ScanCorridorWithAngle(cp, cp, TimeSpan.Zero, PointSize, CorridorCalcMethod);
            RatesStDev = corridor.StDev;
            StDevByPriceAvg = corridor.StDevs[CorridorCalculationMethod.PriceAverage];
            StDevByHeight = corridor.StDevs.ContainsKey(CorridorCalculationMethod.Height) ? corridor.StDevs[CorridorCalculationMethod.Height] : double.NaN;
            Angle = corridor.Slope.Angle(PointSize);
            OnScanCorridor(_rateArray);
            OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
            OnPropertyChanged(() => RatesStDevToRatesHeightRatio);
            OnPropertyChanged(() => SpreadForCorridorInPips);
          }
          return _rateArray;
        } catch (Exception exc) {
          Log = exc;
          return _rateArray;
        } finally {
          Monitor.Exit(_rateArrayLocker);
        }
      }
    }

    int GetCrossesCount(IList<Rate> rates, double level) {
      return rates.Count(r => level.Between(r.BidLow, r.AskHigh));
    }

    public bool HasRates { get { return _rateArray.Any(); } }
    private List<Rate> GetRatesSafe() {
      return _limitBarToRateProvider == (int)BarPeriod ? RatesInternal : RatesInternal.GetMinuteTicks((int)BarPeriod, false, false).ToList();
    }
    IEnumerable<Rate> GetRatesForStDev(IEnumerable<Rate> rates) {
      return rates.Reverse().Take(BarsCount).Reverse();
    }
    List<Rate> _Rates = new List<Rate>();
    public List<Rate> RatesInternal {
      get {
        return _Rates;
      }
    }
    /// <summary>
    /// Returns instant deep copy of Rates
    /// </summary>
    /// <returns></returns>
    ICollection<Rate> RatesCopy() { return RatesArraySafe.Select(r => r.Clone() as Rate).ToList(); }

    public double InPoints(double d) {
      return TradesManager == null ? double.NaN : TradesManager.InPoints(Pair, d);
    }

    double _pointSize = double.NaN;
    public double PointSize {
      get {
        if (double.IsNaN(_pointSize))
          _pointSize = TradesManager == null ? double.NaN : TradesManager.GetPipSize(Pair);
        return _pointSize;
      }
    }

    double _pipCost = double.NaN;
    public double PipCost {
      get {
        if (double.IsNaN(_pipCost))
          _pipCost = TradesManager.GetPipCost(Pair);
        return _pipCost;
      }
    }

    double _HeightFib;

    public double HeightFib {
      get { return _HeightFib; }
      set {
        if (_HeightFib == value) return;
        _HeightFib = value;
        OnPropertyChanged("HeightFib");
      }
    }

    Trade _lastTrade = new Trade();

    public Trade LastTrade {
      get { return _lastTrade; }
      set {
        if (value == null) return;
        _lastTrade = value;
        OnPropertyChanged("LastTrade");
        OnPropertyChanged("LastLotSize");
      }
    }

    public int LastLotSize {
      get { return Math.Max(LotSize, LastTrade.Lots); }
    }
    public int MaxLotSize {
      get {
        return MaxLotByTakeProfitRatio.ToInt() * LotSize;
      }
    }

    private double _Profitability;
    public double Profitability {
      get { return _Profitability; }
      set {
        if (_Profitability != value) {
          _Profitability = value;
          OnPropertyChanged(Metadata.TradingMacroMetadata.Profitability);
          OnPropertyChanged(Metadata.TradingMacroMetadata.ProfitabilityRatio);
        }
      }
    }

    public double ProfitabilityRatio {
      get { return Profitability / MinimumGross; }
    }

    private double _RunningBalance;
    public double RunningBalance {
      get { return _RunningBalance; }
      set {
        if (_RunningBalance != value) {
          _RunningBalance = value;
          OnPropertyChanged("RunningBalance");
        }
      }
    }
    private double _MinimumGross;
    [Category(categorySession)]
    public double MinimumGross {
      get { return _MinimumGross; }
      set {
        if (_MinimumGross != value) {
          _MinimumGross = value;
          OnPropertyChanged("MinimumGross");
        }
      }
    }

    private int _HistoryMinimumPL;
    public int HistoryMinimumPL {
      get { return _HistoryMinimumPL; }
      set {
        if (_HistoryMinimumPL != value) {
          _HistoryMinimumPL = value;
          OnPropertyChanged("HistoryMinimumPL");
        }
      }
    }

    private int _HistoryMaximumLot;
    public int HistoryMaximumLot {
      get { return _HistoryMaximumLot; }
      set {
        if (_HistoryMaximumLot != value) {
          _HistoryMaximumLot = value;
          OnPropertyChanged("HistoryMaximumLot");
        }
      }
    }

    TradeDirections _TradeDirection = TradeDirections.None;
    public TradeDirections TradeDirection {
      get { return _TradeDirection; }
      set {
        _TradeDirection = value;
        OnPropertyChanged("TradeDirection");
      }
    }
    public int GetTradesGlobalCount() {
      return TradesManager.GetTrades().Select(t => t.Pair).Distinct().Count();
    }
    int _tradesCount = 0;
    public Trade[] Trades {
      get {
        Trade[] trades = TradesManager == null ? new Trade[0] : TradesManager.GetTrades(Pair);/* _trades.ToArray();*/
        if (_tradesCount != trades.Length) {
          OnTradesCountChanging(trades.Length, _tradesCount);
          _tradesCount = trades.Length;
        }
        return trades;
      }
      //set {
      //  _trades.Clear();
      //  _trades.AddRange(value);
      //  if (value.Length > 0) ResetLock();
      //}
    }

    private void OnTradesCountChanging(int countNew, int countOld) {
      //new Action(() => BarPeriod = countNew > 0 ? 1 : 5).BeginInvoke(a => { }, null);
    }


    private Strategies _Strategy;
    [Category(categorySession)]
    public Strategies Strategy {
      get {
        return _Strategy;
      }
      set {
        if (_Strategy != value) {
          _Strategy = value;
          OnPropertyChanged(TradingMacroMetadata.Strategy);
        }
      }
    }
    private bool _ShowPopup;
    public bool ShowPopup {
      get { return _ShowPopup; }
      set {
        _ShowPopup = value;
        OnPropertyChanged(TradingMacroMetadata.ShowPopup);
      }
    }
    private string _PopupText;
    public string PopupText {
      get { return _PopupText; }
      set {
        if (_PopupText != value) {
          _PopupText = value;
          ShowPopup = value != "";
          OnPropertyChanged(TradingMacroMetadata.PopupText);
        }
      }
    }

    #region Spread
    private double CalcSpreadForCorridor(IList<Rate> rates, int iterations = 1) {
      try {
        return rates.Spread(iterations);
        var spreads = rates.Select(r => r.AskHigh - r.BidLow).ToList();
        if (spreads.Count == 0) return double.NaN;
        var spreadLow = spreads.AverageByIterations(iterations, true);
        var spreadHight = spreads.AverageByIterations(iterations, false);
        if (spreadLow.Count == 0 && spreadHight.Count == 0)
          return CalcSpreadForCorridor(rates, iterations - 1);
        var sa = spreads.Except(spreadLow.Concat(spreadHight)).DefaultIfEmpty(spreads.Average()).Average();
        var sstdev = 0;// spreads.StDev();
        return sa + sstdev;
      } catch (Exception exc) {
        Log = exc;
        return double.NaN;
      }
    }

    double SpreadForCorridor { get; set; }
    public double SpreadForCorridorInPips { get { return InPips(SpreadForCorridor); } }

    #endregion

    public double TradingDistanceInPips { get { return InPips(_tradingDistance).Max(_tradingDistanceMax); } }
    double _tradingDistance = double.NaN;
    public double TradingDistance {
      get {
        if (!HasRates) return double.NaN;
        _tradingDistance = GetValueByTakeProfitFunction(TradingDistanceFunction);
        if (CorridorFollowsPrice) {
          var td1 = AllowedLotSizeCore(TradingDistanceInPips)
            .ValueByPosition(LotSize, LotSize * MaxLotByTakeProfitRatio, _tradingDistance, RatesHeight / _ratesHeightAdjustmentForAls)
            .Min(RatesHeight / _ratesHeightAdjustmentForAls);
          if (td1 > 0) {
            if ((td1 - _tradingDistance).Abs() / _tradingDistance > .1) _tradingDistance = td1;
          } else {
            td1 = _tradingDistance;
          }
        }
        return _tradingDistance;// td;
      }
    }

    IWave<Rate> TrailingWave {
      get {
        if (!HasRates) return null;
        switch (TrailingDistanceFunction) {
          case TrailingWaveMethod.WaveShort: return WaveShort;
          case TrailingWaveMethod.WaveTrade: return WaveTradeStart;
          case TrailingWaveMethod.WaveMax: return WaveTradeStart.Rates.Count > WaveShort.Rates.Count ? (IWave<Rate>)WaveTradeStart : (IWave<Rate>)WaveShort;
          case TrailingWaveMethod.WaveMin: return WaveTradeStart.Rates.Count < WaveShort.Rates.Count ? (IWave<Rate>)WaveTradeStart : (IWave<Rate>)WaveShort;
        }
        throw new NotSupportedException("TrailingRates of type " + TrailingDistanceFunction + " is not supported.");
      }
    }

    Playback _Playback = new Playback();
    public void SetPlayBackInfo(bool play, DateTime startDate, TimeSpan delay) {
      _Playback.Play = play;
      _Playback.StartDate = startDate;
      _Playback.Delay = delay;
    }
    public bool IsInPlayback { get { return _Playback.Play; } }

    enum workers { LoadRates, ScanCorridor, RunPrice };
    Schedulers.BackgroundWorkerDispenser<workers> bgWorkers = new Schedulers.BackgroundWorkerDispenser<workers>();

    void AddCurrentTick(Price price) {
      if (!RatesInternal.Any() || !HasRates || price.IsPlayback) return;
      if (!Monitor.TryEnter(_Rates)) return;
      try {
        var isTick = RatesInternal.First() is Tick;
        if (BarPeriod == 0) {
          RatesInternal.Add(isTick ? new Tick(price, 0, false) : new Rate(price, false));
        } else {
          if (price.Time > RatesInternal.Last().StartDate.AddMinutes((int)BarPeriod)) {
            RatesInternal.Add(isTick ? new Tick(price, 0, false) : new Rate(RatesInternal.Last().StartDate.AddMinutes((int)BarPeriod), price.Ask, price.Bid, false));
          } else RatesInternal.Last().AddTick(price.Time, price.Ask, price.Bid);
        }
      } finally {
        Monitor.Exit(_Rates);
      }
    }


    double RoundPrice(double price) {
      return TradesManager == null ? double.NaN : TradesManager.Round(Pair, price);
    }

    private bool _isPriceSpreadOk;
    public bool IsPriceSpreadOk {
      get { return _isPriceSpreadOk; }
      set {
        if (_isPriceSpreadOk == value) return;
        _isPriceSpreadOk = value;
        OnPropertyChanged(() => IsPriceSpreadOk);
      }
    }
    public void SetPriceSpreadOk() {
      IsPriceSpreadOk = this.CurrentPrice.Spread < this.PriceSpreadAverage * 1.2;
    }
    private bool CanDoEntryOrders {
      get {
        var canDo = IsAutoStrategy && HasCorridor && IsPriceSpreadOk;
        return canDo;
      }
    }
    private bool CanDoNetOrders {
      get {
        var canDo = IsAutoStrategy && (HasCorridor || IsAutoStrategy);
        return canDo;
      }
    }

    private int EntryOrderAllowedLot(bool isBuy, double? takeProfitPips = null) {
      var lotByStats = (int)_tradingStatistics.AllowedLotMinimum;
      var lot = false && !_isInPipserMode && lotByStats > 0 ? lotByStats : AllowedLotSizeCore();
      return lot + (TradesManager.IsHedged ? 0 : Trades.IsBuy(!isBuy).Lots());
    }


    static TradingMacro() {
    }

    void SetEntryOrdersBySuppResLevels() {
      if (TradesManager == null) return;
      if (!isLoggedIn) return;
      try {
        if (CanDoEntryOrders)
          OnEntryOrdersAdjust();
        else
          GetEntryOrders().ToList().ForEach(o => OnDeletingOrder(o.OrderID));
        if (CanDoNetOrders)
          OnSettingStopLimits();
      } catch (Exception exc) {
        Log = exc;
      }
    }

    private Order2GoAddIn.FXCoreWrapper GetFXWraper(bool failTradesManager = true) {
      if (TradesManager == null)
        FailTradesManager();
      return TradesManager as Order2GoAddIn.FXCoreWrapper;
    }

    internal void SetNetStopLimit() {
      try {
        SetNetStopLimit(true);
        SetNetStopLimit(false);
      } catch (Exception exc) { Log = exc; }
    }

    private static void FailTradesManager() {
      Debug.Fail("TradesManager is null", (new NullReferenceException()) + "");
    }

    public double CalcTakeProfitDistance(bool inPips = false) {
      if (Trades.Length == 0) return double.NaN;
      var fw = GetFXWraper();
      if (fw == null) return double.NaN;
      var netOrder = fw.GetNetLimitOrder(Trades.LastTrade());
      if (netOrder == null) return double.NaN;
      var netOpen = Trades.NetOpen();
      var ret = !netOrder.IsBuy ? netOrder.Rate - netOpen : netOpen - netOrder.Rate;
      return inPips ? InPips(ret) : ret;
    }
    void SetNetStopLimit(bool isBuy) {
      Order2GoAddIn.FXCoreWrapper fw = GetFXWraper();
      if (CloseOnOpen) return;
      var ps = TradesManager.GetPipSize(Pair) / 2;
      var trades = Trades.IsBuy(isBuy);
      if (trades.Length == 0) return;
      var tradeLast = trades.OrderBy(t => t.Id).Last();
      foreach (var trade in trades) {
        var currentLimit = trade.Limit;
        if (fw != null && currentLimit == 0) {
          var netLimitOrder = fw.GetNetLimitOrder(trade);
          if (netLimitOrder != null) currentLimit = netLimitOrder.Rate;
        }
        var rateLast = RatesArraySafe.LastOrDefault(r => r.PriceAvg1 > 0);
        if (rateLast == null) return;
        var netOpen = tradeLast.Open;// trades.NetOpen();
        if (CloseOnOpen) {
          if (currentLimit != 0)
            if (fw != null)
              fw.FixOrderSetLimit(trade.Id, 0, "");
        } else {
          LimitRate = GetLimitRate(isBuy);
          if (LimitRate > 0) {
            Func<double> bid = () => !IsInVitualTrading ? CurrentPrice.Bid : RateLast.BidHigh;
            Func<double> ask = () => !IsInVitualTrading ? CurrentPrice.Ask : RateLast.AskLow;
            if (isBuy && LimitRate - ps <= bid() || !isBuy && LimitRate + ps >= ask()) {
              DisposeOpenTradeByMASubject();
              TradesManager.ClosePair(Pair);
            }
            if (fw != null && (RoundPrice(currentLimit) - LimitRate).Abs() > ps) {
              fw.FixOrderSetLimit(trade.Id, LimitRate, "");
            }
          }
          continue;
          var stopByCorridor = rateLast == null || !ReverseStrategy ? 0 : !isBuy ? rateLast.PriceAvg02 : rateLast.PriceAvg03;
          var sl = RoundPrice((trade.IsBuy ? 1 : -1) * CalculateCloseLoss());
          var stopRate = RoundPrice(ReverseStrategy ? stopByCorridor : netOpen + PriceSpreadToAdd(isBuy) + sl);
          if (stopRate > 0) {
            if (isBuy && stopRate >= CurrentPrice.Bid || !isBuy && stopRate <= CurrentPrice.Ask)
              fw.ClosePair(Pair);
            var currentStop = trade.Stop;
            if (currentStop == 0) {
              var netStopOrder = fw.GetNetStopOrder(trade);
              if (netStopOrder != null) currentStop = netStopOrder.Rate;
            }
            if ((stopRate - RoundPrice(currentStop)).Abs() > ps)
              fw.FixOrderSetStop(trade.Id, stopRate, "");
          }
        }
      }
    }

    private double GetLimitRate(bool isBuy) {
      try {
        if (!HasRates) return double.NaN;
        //var rate = CorridorStats.Rates.First(r => r.PriceAvg1.Between(r.AskHigh, r.BidLow));
        //var profit = CorridorStats.StDev;
        //var rates = RatesArray.Where(r => r >= rate);
        //return isBuy ? rates.Min(r => r.AskHigh) + profit : rates.Max(r => r.BidLow) - profit;
        //return this.RateLast.PriceAvg1;
        var useNet = _isInPipserMode;
        Trade[] trades = Trades.IsBuy(isBuy);
        var closeProfit = CalculateCloseProfit() / (useNet && this.TakeProfitFunction != TradingMacroTakeProfitFunction.Spread ? trades.Count() : 1);
        var basePrice = useNet ? trades.NetOpen() : trades.OrderByDescending(t => t.Id).Select(t => t.Open).LastOrDefault();
        return basePrice + (isBuy ? 1 : -1) * closeProfit;

        if (!_waveRates.Any()) return double.NaN;
        var rateAndIndex = _waveRates[0];
        var rate = rateAndIndex.Rate;
        var index = rateAndIndex.Position;
        var ratesForStDevLimit = CorridorStats.Rates.Take(index).ToArray();// RatesForTakeProfit(trades);
        var stDevProfit = CorridorStats.StDev * 2;
        var limitByStDev = isBuy ? ratesForStDevLimit.Min(CorridorGetLowPrice()) + stDevProfit : ratesForStDevLimit.Max(CorridorGetHighPrice()) - stDevProfit;
        var limitByNet = Trades.NetOpen(limitByStDev) + (isBuy ? closeProfit : -closeProfit);
        var limit = isBuy ? limitByStDev.Min(limitByNet) : limitByStDev.Max(limitByNet);
        return RoundPrice(CloseOnProfitOnly || ReverseStrategy
          ? trades.LastTrade().Open + PriceSpreadToAdd(isBuy) + (isBuy ? 1 : -1) * closeProfit
          : limit
        );
      } catch (Exception exc) {
        Log = exc;
        return double.NaN;
      }
    }

    private IList<WaveInfo> GetWaveRates(IList<Rate> corridorRates, int count) {
      var list = new List<WaveInfo>(count);
      var i = 0;
      var spreadMinimum = CalcSpreadForCorridor(corridorRates);
      while (i <= count) {
        var wr = GetWaveRate(corridorRates, spreadMinimum, GetPriceMA, i == 0 ? 0 : list[i - 1].Position);
        if (wr == null) break;
        if (list.Any())
          if (wr.Position == list.Last().Position) break;
        if (list.Any() && wr.Direction == list.Last().Direction) {
          list[list.Count - 1] = wr;
        } else {
          var zeros = corridorRates.Take(wr.Position).Where(r => r.PriceStdDev == 0).ToList();
          list.Add(wr);
          i++;
        }
      }
      return list.Take(count).ToList();
    }
    private static WaveInfo GetWaveRate(IList<Rate> corridorRates, double spreadMinimum, Func<Rate, double> ratePrice, int startIndex = 0) {
      if (corridorRates.Count() - startIndex < 2) return null;
      var rates = corridorRates.Skip(startIndex).ToList().SetStDevPrice(ratePrice);
      var a = rates.Select((r, i) => new Tuple<Rate, int>(r, i + 1)).ToList();
      int sign = 0, nextSign = 0;
      var node = new LinkedList<Tuple<Rate, int>>(a).First;
      var b = false;
      while (node.Next != null) {
        var rate = node.Value.Item1;
        var rateNext = node.Next.Value.Item1;
        var nextHeight = rates.Take(node.Next.Value.Item2).ToList().Height(ratePrice);
        if (nextHeight > spreadMinimum) {
          nextSign = Math.Sign(node.Value.Item1.PriceAvg - node.Next.Value.Item1.PriceAvg);// Math.Sign(ratePrice(rateNext) - rateNext.PriceAvg);
          sign = node.Previous == null ? nextSign : Math.Sign(node.Previous.Value.Item1.PriceAvg - node.Value.Item1.PriceAvg);// Math.Sign(ratePrice(rate) - rate.PriceAvg);
          var nodeHeight = rates.Take(node.Value.Item2).ToList().Height(ratePrice);
          if (!b)
            b = node.Value.Item1.PriceStdDev > node.Next.Value.Item1.PriceStdDev;
          if (b && (true || nextSign != sign)) {
            //node = node.Next;
            break;
          }
        }
        node = node.Next;
      }
      var slope = Regression.Regress(rates.Take(node.Value.Item2).Select(ratePrice).ToArray(), 1)[1];
      var ratesOut = rates.Take(node.Value.Item2).OrderBy(r => r.PriceAvg);
      var rateOut = slope > 0 ? ratesOut.Last() : ratesOut.First();
      var tupleOut = node.List.Single(n => n.Item1 == rateOut);
      return new WaveInfo(tupleOut.Item1, tupleOut.Item2 + startIndex, slope);
    }

    private IList<Rate> RatesForTakeProfit(Trade[] trades) {
      if (!HasRates) return new Rate[0];
      var lastTradeDate = trades.OrderByDescending(t => t.Time).Select(t => t.Time).DefaultIfEmpty(RatesArraySafe[0].StartDate).First();//.Subtract(BarPeriodInt.FromMinutes());
      var firstDate = lastTradeDate.Subtract(TradesManager.ServerTime - lastTradeDate);
      return RatesArraySafe.SkipWhile(r => r.StartDate < lastTradeDate).DefaultIfEmpty(RateLast).ToList();
    }

    private double PriceSpreadToAdd(bool isBuy) {
      return (isBuy ? 1 : -1) * PriceSpreadAverage.GetValueOrDefault(double.NaN);
    }



    bool? _magnetDirtection;
    DateTime? _corridorTradeDate;
    internal void EntryOrdersAdjust() {
      try {

        RunStrategy();
        return;
        foreach (var suppres in EnsureActiveSuppReses()) {
          var isBuy = suppres.IsBuy;
          var rate = RoundPrice(suppres.Rate);// + (ReverseStrategy ? 0 : PriceSpreadToAdd(isBuy)));
          var allowedLot = EntryOrderAllowedLot(isBuy);
          var canBuy = isBuy && CorridorCrossLowPrice(RateLast) <= rate;// && (MagnetPrice + CorridorStats.StDev) > orderedRates.Last();
          var canSell = !isBuy && CorridorCrossHighPrice(RateLast) >= rate;// && (MagnetPrice-CorridorStats.StDev)<orderedRates.First();
          if (ForceOpenTrade.HasValue || canBuy || canSell) {
            if (ForceOpenTrade.HasValue) isBuy = ForceOpenTrade.Value;
            if (CheckPendingKey("OT") && EnsureActiveSuppReses().Contains(suppres)) {
              DisposeOpenTradeByMASubject();
              TouchDownDateTime = RatesArray.Last().StartDate;
              TradesManager.ClosePair(Pair);
              if (!HasTradesByDistance(isBuy)) {
                Action<Action> a = (pa) => {
                  if (CanTrade()) {
                    if (TradesManager.IsHedged)
                      TradesManager.ClosePair(Pair, !isBuy);
                    Price price = IsInVitualTrading ? new Price(Pair, RateLast, TradesManager.ServerTime, TradesManager.GetPipSize(Pair), TradesManager.GetDigits(Pair), true) : null;
                    var lot = EntryOrderAllowedLot(isBuy);
                    if (lot > 0) {
                      pa();
                      TradesManager.OpenTrade(Pair, isBuy, lot, 0.0, 0.0, "", price);
                    }
                  } else
                    ReleasePendingAction("OT");
                };
                OnOpenTradeBroadcast(a, isBuy);
              }
              ForceOpenTrade = null;
            }
          }
          continue;
          Order2GoAddIn.FXCoreWrapper fw = GetFXWraper();
          if (fw == null || (!ForceOpenTrade.HasValue && !CanDoEntryOrders)) return;
          if (false) {
            var orders = GetEntryOrders(isBuy).OrderBy(o => (o.Rate - suppres.Rate).Abs()).ToList();
            orders.Skip(1).ToList().ForEach(o => {
              OnDeletingOrder(o.OrderID);
              orders.Remove(o);
            });
            var order = orders.FirstOrDefault();//, suppres.EntryOrderId);
            if (order == null) {
              OnCreateEntryOrder(isBuy, allowedLot, rate);
            } else {
              if ((RoundPrice(order.Rate) - rate).Abs() > PointSize / 2)
                fw.ChangeOrderRate(order, rate);
              if (order.AmountK != allowedLot / 1000)
                fw.ChangeOrderAmount(order, allowedLot);
            }
          }
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    Action StrategyAction {
      get {
        switch ((Strategy & ~Strategies.Auto)) {
          case Strategies.Hot:
            return StrategyEnterDistancerManual;
          case Strategies.Ender:
            return StrategyEnterEnder;
          case Strategies.Universal:
            return StrategyEnterUniversal;
          case Strategies.None:
            return () => { };
        }
        throw new NotSupportedException("Strategy " + Strategy + " is not supported.");
      }
    }
    bool _isSelfStrategy = false;
    void RunStrategy() {
      StrategyAction();
      if (!IsTradingActive || _isSelfStrategy) {
        return;
      }
      #region Trade Action
      Action<bool, Func<double, bool>, Func<double, bool>> openTrade = (isBuy, mastClose, canOpen) => {
        var suppReses = EnsureActiveSuppReses().OrderBy(sr => sr.TradesCount).ToList();
        var minTradeCount = suppReses.Min(sr => sr.TradesCount);
        Func<SuppRes, bool> isEnterSuppRes = sr => sr == _buyLevel || sr == _sellLevel;
        foreach (var suppRes in EnsureActiveSuppReses(isBuy)) {
          var level = suppRes.Rate;
          if (mastClose(level)) {
            if (isEnterSuppRes(suppRes)) {
              var srGroup = suppReses.Where(a => !a.IsGroupIdEmpty && a.GroupId == suppRes.GroupId).OrderBy(sr => sr.TradesCount).ToList();
              if (srGroup.Any()) {
                if (srGroup[0] == suppRes)
                  srGroup[1].TradesCount = suppRes.TradesCount - 1;
              } else if (suppRes.TradesCount == minTradeCount) {
                suppReses.IsBuy(!isBuy).Where(sr => sr.TradesCount < 9 && sr.CanTrade)
                  .OrderBy(sr => (sr.Rate - suppRes.Rate).Abs()).Take(1).ToList()
                  .ForEach(sr => sr.TradesCount = suppRes.TradesCount - 1);
              }
            }

            var canTrade = suppRes.CanTrade && suppRes.TradesCount <= 0 && !HasTradesByDistance(isBuy);
            CheckPendingAction("OT", (pa) => {
              var als = AllowedLotSizeCore();
              if (_trimAtZero) {
                var lot = Trades.Lots() - als;
                if (lot > 0)
                  CloseTrades(lot);
                 _trimAtZero = false;
              } else {
                var lotOpen = canTrade && canOpen(level) ? als : 0;
                var lotClose = Trades.IsBuy(!isBuy).Lots();
                var lot = lotClose + lotOpen;
                if (lot > 0) {
                  pa();
                  TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
                }
              }
            });
          }
        }
      };
      #endregion
      #region Call Trade Action
      if (RatesArray.Any() && SuppRes.Any() && Strategy != Strategies.None && (RateLast.StartDate - RatePrev1.StartDate).TotalMinutes / BarPeriodInt <= 3) {
        double priceLast;
        double pricePrev;
        CalculatePriceLastAndPrev(out priceLast, out pricePrev);

        Func<double, bool> canBuy = level => true;// (CurrentPrice.Ask - level).Abs() < SpreadForCorridor * 2;
        Func<double, bool> mustCloseSell = level => priceLast >= level && pricePrev.Min(RatePrev1.PriceAvg) <= level;
        openTrade(true, mustCloseSell, canBuy);

        Func<double, bool> canSell = level => true;// (CurrentPrice.Bid - level).Abs() < SpreadForCorridor * 2;
        Func<double, bool> mustCloseBuy = level => priceLast <= level && pricePrev.Max(RatePrev1.PriceAvg) >= level;
        openTrade(false, mustCloseBuy, canSell);
      }
      #endregion
    }
    double? _buyPriceToLevelSign;
    double? _sellPriceToLevelSign;
    delegate double GetPriceLastForTradeLevelDelegate();
    delegate double GetPricePrevForTradeLevelDelegate();


    private void OpenTradeWithReverse(bool isBuy) {
      CheckPendingAction("OT", (pa) => {
        var lotClose = Trades.IsBuy(!isBuy).Lots();
        var lotOpen = AllowedLotSizeCore();
        var lot = lotClose + lotOpen;
        if (lot > 0) {
          pa();
          TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
        }
      });
    }

    public void CalculatePriceLastAndPrev(out double priceLast, out double pricePrev) {
      priceLast = CalculateLastPrice(RatesArray.LastBC(), r => r.PriceAvg);
      pricePrev = RatePrev1.PriceAvg;
    }

    bool _useTakeProfitMin = false;
    Action<Trade> _strategyExecuteOnTradeClose;
    Action<Trade> _strategyExecuteOnTradeOpen;
    double _tradingDistanceMax = 0;

    #region New

    void initExeuteOnTradeCloseOpen(Action action = null) {
      if (_strategyExecuteOnTradeClose == null) {
        if (action != null) action();
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _tradingDistanceMax = 0;
          }
        };
        _strategyExecuteOnTradeOpen = trade => {
          _useTakeProfitMin = true;
        };
      }
    }


    #region 08s
    DateTime _corridorStartDateLast;
    bool _isCorridorHot = false;
    double _tradeLifespan = double.NaN;
    #endregion

    #endregion

    private void TurnOffSuppRes(double level = double.NaN) {
      var rate = double.IsNaN(level) ? SuppRes.Average(sr => sr.Rate) : level;
      foreach (var sr in SuppRes)
        sr.RateEx = rate;
    }

    public void TrimTrades() { CloseTrades(Trades.Lots() - LotSize); }
    public void CloseTrades() { CloseTrades(Trades.Lots()); }
    private void CloseTrades(int lot) {
      if (!Trades.Any()) return;
      if (HasPendingKey("CT")) return;
      if (lot > 0)
        CheckPendingAction("CT", pa => {
          pa();
          if(LogTRades)
            Log = new Exception(string.Format("{0}:Closing {1} from {2} in {3}", Pair, lot, Trades.Lots(), new StackFrame(3).GetMethod().Name));
          if (!TradesManager.ClosePair(Pair, Trades[0].IsBuy, lot))
            ReleasePendingAction("CT");
        });
    }

    #region GetEntryOrders
    private Order[] GetEntryOrders() {
      Order2GoAddIn.FXCoreWrapper fw = GetFXWraper();
      return fw == null ? new Order[0] : fw.GetEntryOrders(Pair);
    }
    private Order[] GetEntryOrders(bool isBuy) {
      return GetEntryOrders().IsBuy(isBuy);
    }
    #endregion

    Schedulers.TaskTimer _runPriceChangedTasker = new Schedulers.TaskTimer(100);
    Schedulers.TaskTimer _runPriceTasker = new Schedulers.TaskTimer(100);
    public void RunPriceChanged(PriceChangedEventArgs e, Action<TradingMacro> doAfterScanCorridor) {
      if (TradesManager != null) {
        try {
          RunPriceChangedTask(e, doAfterScanCorridor);
        } catch (Exception exc) {
          Log = exc;
        }
      }
    }

    private SuppRes[] EnsureActiveSuppReses() {
      return EnsureActiveSuppReses(true).Concat(EnsureActiveSuppReses(false)).OrderBy(sr => sr.Rate).ToArray();
    }
    private SuppRes[] EnsureActiveSuppReses(bool isBuy, bool doTrades = false) {
      var hasTrades = doTrades && HasTradesByDistance(Trades.IsBuy(isBuy));
      var isActiveCommon = true;// !IsCold && IsHotStrategy && HasCorridor;
      var isActiveByBuy = true;//rateLast == null ? true : !isBuy ? rateLast.PriceAvg < rateLast.PriceAvg1 : rateLast.PriceAvg > rateLast.PriceAvg1;
      SuppRes.IsBuy(isBuy).ToList().ForEach(sr => sr.IsActive = ForceOpenTrade.HasValue || !hasTrades && isActiveByBuy && isActiveCommon);
      return SuppRes.Active(isBuy);
    }

    private bool HasTradesByDistance(bool isBuy) {
      return HasTradesByDistance(Trades.IsBuy(isBuy));
    }
    private bool HasTradesByDistance(Trade[] trades) {
      if (MaximumPositions <= trades.Positions(LotSize)) return true;
      var td = TradingDistanceInPips;//.Max(trades.DistanceMaximum());
      return TakeProfitPips == 0 || double.IsNaN(TradingDistance) || HasTradesByDistanceDelegate(trades, td);
    }

    delegate bool HasTradesByDistanceCustom(Trade[] trades, double tradingDistanceInPips);
    HasTradesByDistanceCustom _hasTradesByDistanceDelegate;
    private HasTradesByDistanceCustom HasTradesByDistanceDelegate {
      get { return _hasTradesByDistanceDelegate ?? HasTradesByDistanceAndPL; }
      set { _hasTradesByDistanceDelegate = value; }
    }
    private bool HasTradesByDistanceAndPL(Trade[] trades, double tradeDistanceInPips) {
      return (trades.Any() && trades.Max(t => t.PL) > -(tradeDistanceInPips + PriceSpreadAverageInPips));
    }

    private bool HasTradesByDistanceAndCurrentGross(Trade[] trades, double tradeDistanceInPips) {
      return (trades.Any() && CurrentGrossInPips.Min(0).Abs() < (tradeDistanceInPips + PriceSpreadAverageInPips));
    }
    static double? _runPriceMillisecondsAverage;
    public void RunPriceChangedTask(PriceChangedEventArgs e, Action<TradingMacro> doAfterScanCorridor) {
      try {
        if (TradesManager == null) return;
        Stopwatch sw = Stopwatch.StartNew();
        var timeSpanDict = new Dictionary<string, long>();
        Price price = e.Price;
        #region LoadRates
        if (!TradesManager.IsInTest && !IsInPlayback
          && (!RatesInternal.Any() || LastRatePullTime.AddMinutes(1.0.Max((double)BarPeriod / 2)) <= TradesManager.ServerTime)) {
          LastRatePullTime = TradesManager.ServerTime;
          OnLoadRates();
          timeSpanDict.Add("LoadRates", sw.ElapsedMilliseconds);
          _runPriceMillisecondsAverage = Lib.Cma(_runPriceMillisecondsAverage, BarsCount, sw.ElapsedMilliseconds);
        }
        #endregion
        OnRunPriceBroadcast(e);
        if (doAfterScanCorridor != null) doAfterScanCorridor.BeginInvoke(this, ar => { }, null);
        #region Timing
        if (!IsInVitualTrading) {
          timeSpanDict.Add("Other", sw.ElapsedMilliseconds);
          if (sw.Elapsed > TimeSpan.FromSeconds(LoadRatesSecondsWarning)) {
            var s = string.Join(Environment.NewLine, timeSpanDict.Select(kv => " " + kv.Key + ":" + kv.Value));
            Log = new Exception(string.Format("{0}[{2}]:{1:n}ms{3}{4}",
              MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, Pair, Environment.NewLine, s));
          }
        }
        #endregion
      } catch (Exception exc) {
        Log = exc;
      }
    }
    public double GetPriceMA(Rate rate) {
      var ma = GetPriceMA()(rate);
      if (ma <= 0) {
        var msg = "Price MA must be more than Zero!";
        Debug.Fail(msg);
        throw new InvalidDataException(msg);
      }
      return ma;
    }
    public Func<Rate, double> GetPriceMA() {
      switch (MovingAverageType) {
        case Store.MovingAverageType.RegressByMA:
        case Store.MovingAverageType.Regression:
        case Store.MovingAverageType.Cma:
          return r => r.PriceCMALast;
        case Store.MovingAverageType.Trima:
          return r => r.PriceTrima;
        default:
          throw new NotSupportedException(new { MovingAverageType }.ToString());
      }
    }
    double _sqrt2 = 1.5;// Math.Sqrt(1.5);

    Func<Rate,Rate, double> GetMovingAverageValueFunction() {
      switch (MovingAverageValue) {
        case MovingAverageValues.PriceMove: return (p, r) => (r.PriceAvg-p.PriceAvg).Abs();
        case MovingAverageValues.PriceAverage: return (p, r) => r.PriceAvg;
        case MovingAverageValues.PriceSpread: return (p, r) => r.PriceSpread;
        case MovingAverageValues.Volume: return (p, r) => r.Volume;
      }
      throw new InvalidEnumArgumentException(MovingAverageValue + " is not supported.");
    }
    private void SetMA(int? period = null) {
      switch (MovingAverageType) {
        case Store.MovingAverageType.RegressByMA:
          RatesArray.SetCma(GetMovingAverageValueFunction(), 3, 3);
          RatesArraySafe.SetRegressionPrice(PriceCmaPeriod, r => r.PriceCMALast, (r, d) => r.PriceCMALast = d);
          break;
        case Store.MovingAverageType.Regression:
          Action<Rate,double> a = (r,d)=>r.PriceCMALast = d;
          RatesArraySafe.SetRegressionPrice(PriceCmaPeriod, _priceAvg, a);
          break;
        case Store.MovingAverageType.Cma:
          if (period.HasValue)
            RatesArray.SetCma(period.Value, period.Value);
          else if (PriceCmaPeriod > 0) {
            //RatesArray.SetCma((p, r) => r.PriceAvg - p.PriceAvg, r => {
            //  if(r.PriceCMAOther == null)r.PriceCMAOther = new List<double>();
            //  return r.PriceCMAOther;
            //}, PriceCmaPeriod + CmaOffset, PriceCmaLevels + CmaOffset.ToInt());
            RatesArray.SetCma(GetMovingAverageValueFunction(), PriceCmaPeriod, PriceCmaLevels);
          }
          break;
        case Store.MovingAverageType.Trima:
          RatesArray.SetTrima(PriceCmaPeriod); break;
      }
    }

    Func<double, double, double> _max = (d1, d2) => Math.Max(d1, d2);
    Func<double, double, double> _min = (d1, d2) => Math.Min(d1, d2);
    public void ScanCorridor(IList<Rate> ratesForCorridor) {
      try {
        if (!IsActive || !isLoggedIn || !HasRates /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        var showChart = CorridorStats == null || CorridorStats.Rates.Count == 0;
        #region Prepare Corridor
        var periodsStart = CorridorStartDate == null
          ? (BarsCount * CorridorLengthMinimum).Max(5).ToInt() : ratesForCorridor.Count(r => r.StartDate >= CorridorStartDate.Value);
        if (periodsStart == 1) return;
        var periodsLength = CorridorStartDate.HasValue ? 1 : CorridorStats.Rates.Any() ? ratesForCorridor.Count(r => r.StartDate >= CorridorStats.StartDate) - periodsStart + 1 : int.MaxValue;// periodsStart;
        Action<DateTime> setPeriods = startDate => {
          periodsStart = ratesForCorridor.Count(r => r.StartDate >= (startDate).Max(CorridorStats.StartDate));
          periodsLength = 1;
        };
        CorridorStatistics crossedCorridor = null;
        Func<Rate, double> priceHigh = CorridorGetHighPrice();
        Func<Rate, double> priceLow = CorridorGetLowPrice();
        var reversed = ratesForCorridor.ReverseIfNot();
        Action<List<Rate>, double> calcCorridor = (corridorRates, median) => {
          Parallel.Invoke(
            () => {
              _levelCounts = ScanCrosses(corridorRates);
              _levelCounts.Sort((t1, t2) => -t1.Item1.CompareTo(t2.Item1));
            },
            () => {
              var coeffs = Regression.Regress(corridorRates.ReverseIfNot().Select(r => r.PriceAvg).ToArray(), 1);
              var stDev = corridorRates.Select(r => (CorridorGetHighPrice()(r) - median).Abs()).ToList().StDev();
              crossedCorridor = new CorridorStatistics(this, corridorRates, stDev, coeffs);
            });
        };

        #region Crosses
        crossedCorridor = GetScanCorridorFunction(ScanCorridorBy)(ratesForCorridor, priceHigh, priceLow);
        if (crossedCorridor == null) {
          Log = new Exception("No corridor for you.");
          return;
        }
        #region Old
        #endregion
        if (false) {
          #region
          var ratesForCross = ratesForCorridor.OrderBy(r => r.PriceAvg).ToList();
          _levelCounts = ScanCrosses(ratesForCross, ratesForCross[0].PriceAvg, ratesForCross[ratesForCross.Count - 1].PriceAvg);
          _levelCounts.Sort((t1, t2) => -t1.Item1.CompareTo(t2.Item1));
          var levelMaxCross = _levelCounts[0].Item2;
          var crossedRates = ratesForCross.Where(r => levelMaxCross.Between(r.BidAvg, r.AskAvg)).OrderBars().ToList();
          var rateStart = crossedRates[0];
          var rateEnd = crossedRates[crossedRates.Count - 1];
          var corridorRates = reversed.Where(r => r.StartDate.Between(rateStart.StartDate, rateEnd.StartDate)).ToList();
          var stDev = corridorRates.Select(r => (CorridorGetHighPrice()(r) - levelMaxCross).Abs()).ToList().StDev();
          var coeffs = Regression.Regress(corridorRates.ReverseIfNot().Select(r => r.PriceAvg).ToArray(), 1);
          crossedCorridor = new CorridorStatistics(this, corridorRates, stDev, coeffs);
          //.ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod); 
          #endregion
        }
        #endregion

        #region Corridorness
        #endregion

        var corridornesses = crossedCorridor != null
          ? new[] { crossedCorridor }.ToList()
          : ratesForCorridor.GetCorridornesses(priceHigh, priceLow, periodsStart, periodsLength, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod, cs => {
            return false;
          }).Select(c => c.Value).ToList();
        if (false) {
          if (true) {
            var coeffs = Regression.Regress(ratesForCorridor.ReverseIfNot().Select(r => r.PriceAvg).ToArray(), 1);
            var median = ratesForCorridor.Average(r => r.PriceAvg);
            var stDev = ratesForCorridor.Select(r => (CorridorGetHighPrice()(r) - median).Abs()).ToList().StDev();
            CorridorBig = new CorridorStatistics(this, ratesForCorridor, stDev, coeffs);
          } else
            CorridorBig = ratesForCorridor.ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);// corridornesses.LastOrDefault() ?? CorridorBig;
        }
        #endregion
        #region Update Corridor
        if (corridornesses.Any()) {
          var rateLast = ratesForCorridor.Last();
          var cc = corridornesses
            //.OrderBy(cs => InPips(cs.Slope.Abs().Angle()).ToInt())
           .ToList();
          crossedCorridor = cc/*.Where(cs => IsCorridorOk(cs))*/.FirstOrDefault();
          var csCurr = crossedCorridor ?? cc.FirstOrDefault();
          var csOld = CorridorStats;
          csOld.Init(csCurr, PointSize);
          csOld.Spread = double.NaN;
          CorridorStats = csOld;
          CorridorStats.IsCurrent = true;// ok;// crossedCorridor != null;
        } else {
          throw new Exception("No corridors found for current range.");
        }
        #endregion
        PopupText = "";
        if (showChart) RaiseShowChart();
      } catch (Exception exc) {
        Log = exc;
        //PopupText = exc.Message;
      }
      //Debug.WriteLine("{0}[{2}]:{1:n1}ms @ {3:mm:ss.fff}", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, Pair,DateTime.Now);
    }

    delegate CorridorStatistics ScanCorridorDelegate(IList<Rate> ratesForCorridor, Func<Rate, double> priceHigh, Func<Rate, double> priceLow);

    private List<Rate> RatesForTrades() {
      return GetRatesSafe().Where(LastRateFilter).ToList();
    }

    private double CalculateTakeProfitInDollars(double? takeProfitInPips = null) {
      return takeProfitInPips.GetValueOrDefault(CalculateTakeProfit()) * LotSize / 10000;
    }
    public double CalculateTakeProfitInPips(bool dontAdjust = false) {
      return InPips(CalculateTakeProfit(dontAdjust));
    }
    double CalculateTakeProfit(bool dontAdjust = false) {
      var tp = 0.0;
      tp = GetValueByTakeProfitFunction(TakeProfitFunction);
      return dontAdjust ? tp : tp.Max((PriceSpreadAverage.GetValueOrDefault(double.NaN) + InPoints(CommissionByTrade(null))) * 2);
    }

    private double GetValueByTakeProfitFunction(TradingMacroTakeProfitFunction function) {
      var tp = double.NaN;
      switch (function) {
        case TradingMacroTakeProfitFunction.CorridorStDev: tp = CorridorStats.StDevByHeight + CorridorStats.StDevByPriceAvg; break;
        case TradingMacroTakeProfitFunction.CorridorHeight: tp = CorridorStats.RatesHeight; break;
        case TradingMacroTakeProfitFunction.RatesHeight: tp = RatesHeight; break;
        case TradingMacroTakeProfitFunction.WaveShort: tp = WaveShort.RatesHeight; break;
        case TradingMacroTakeProfitFunction.WaveShortStDev: tp = WaveShort.RatesStDev; break;
        case TradingMacroTakeProfitFunction.WaveTradeStart: tp = WaveTradeStart.RatesHeight - (WaveTradeStart1.HasRates ? WaveTradeStart1.RatesHeight : 0); break;
        case TradingMacroTakeProfitFunction.WaveTradeStartStDev: tp = WaveTradeStart.RatesStDev; break;
        case TradingMacroTakeProfitFunction.RatesHeight_2: tp = RatesHeight / 2; break;
        case TradingMacroTakeProfitFunction.RatesStDev: tp = RatesStDev; break;
        case TradingMacroTakeProfitFunction.RatesStDevMin: tp = StDevByHeight.Min(StDevByPriceAvg, Math.Sqrt(CorridorStats.StDevByHeight * CorridorStats.StDevByPriceAvg) * 4); break;
        case TradingMacroTakeProfitFunction.Spread: return SpreadForCorridor;
        case TradingMacroTakeProfitFunction.PriceSpread: return PriceSpreadAverage.GetValueOrDefault(double.NaN);
        case TradingMacroTakeProfitFunction.BuySellLevels:
          tp = _buyLevelRate - _sellLevelRate;
          if (double.IsNaN(tp)) {
            if (_buyLevel == null || _sellLevel == null) return double.NaN;
            tp = (_buyLevel.Rate - _sellLevel.Rate).Abs();
          }
          tp -= PriceSpreadAverage.GetValueOrDefault(double.NaN) * 2;
          break;
        default:
          throw new NotImplementedException(new { function } + "");
      }
      return tp;
    }

    ScanCorridorDelegate GetScanCorridorFunction(ScanCorridorFunction function) {
      switch (function) {
        case ScanCorridorFunction.WaveStDevHeight: return ScanCorridorByStDevHeight;
        case ScanCorridorFunction.WaveDistance42: return ScanCorridorByDistance42;
        case ScanCorridorFunction.WaveDistance43: return ScanCorridorByDistance43;
        case ScanCorridorFunction.DayDistance: return ScanCorridorByDayDistance;
        case ScanCorridorFunction.Regression: return ScanCorridorByRegression;
        case ScanCorridorFunction.Parabola: return ScanCorridorByParabola;
        case ScanCorridorFunction.Sinus: return ScanCorridorBySinus;
        case ScanCorridorFunction.Sinus1: return ScanCorridorBySinus_1;
        case ScanCorridorFunction.Crosses: return ScanCorridorByCrosses;
        case ScanCorridorFunction.StDev: return ScanCorridorByStDev;
      }
      throw new NotSupportedException(function + "");
    }

    public double CommissionByTrade(Trade trade) { return TradesManager.CommissionByTrade(trade); }

    bool IsInVitualTrading { get { return TradesManager is VirtualTradesManager; } }
    private bool CanTrade() {
      return IsInVitualTrading || !IsInPlayback;
    }

    ITargetBlock<PriceChangedEventArgs> _runPriceBroadcast;
    public ITargetBlock<PriceChangedEventArgs> RunPriceBroadcast {
      get {
        if (_runPriceBroadcast == null) {
          Action<PriceChangedEventArgs> a = u => RunPrice(u, Trades);
          _runPriceBroadcast = a.CreateYieldingTargetBlock();
        }
        return _runPriceBroadcast;
      }
    }
    void OnRunPriceBroadcast(PriceChangedEventArgs pce) {
      if (TradesManager.IsInTest || IsInPlayback) {
        CurrentPrice = pce.Price;
        RunPrice(pce, Trades);
      } else RunPriceBroadcast.SendAsync(pce);
    }

    private void RunPrice(PriceChangedEventArgs e, Trade[] trades) {
      Price price = e.Price;
      Account account = e.Account;
      var sw = Stopwatch.StartNew();
      try {
        CalcTakeProfitDistance();
        if (!price.IsReal) price = TradesManager.GetPrice(Pair);
        var minGross = CurrentLoss + trades.Gross();// +tm.RunningBalance;
        if (MinimumGross > minGross) MinimumGross = minGross;
        CurrentLossPercent = CurrentGross / account.Balance;
        BalanceOnStop = account.Balance + StopAmount.GetValueOrDefault();
        BalanceOnLimit = account.Balance + LimitAmount.GetValueOrDefault();
        SetTradesStatistics(price, trades);
        SetLotSize();
        try {
          RunStrategy();
        } catch (Exception exc) {
          if (IsInVitualTrading)
            Strategy = Strategies.None;
          throw;
        }
      } catch (Exception exc) { Log = exc; }
      if (!IsInVitualTrading && sw.Elapsed > TimeSpan.FromSeconds(5))
        Log = new Exception("RunPrice(" + Pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
      //Debug.WriteLine("RunPrice[{1}]:{0} ms", sw.Elapsed.TotalMilliseconds, pair);
    }

    #region LotSize
    int _BaseUnitSize = 0;
    public int BaseUnitSize { get { return _BaseUnitSize > 0 ? _BaseUnitSize : _BaseUnitSize = TradesManager.GetBaseUnitSize(Pair); } }
    public void SetLotSize(Account account = null) {
      if (TradesManager == null) return;
      if (account == null) account = TradesManager.GetAccount();
      if (account == null) return;
      Trade[] trades = Trades;
      LotSize = TradingRatio <= 0 ? 0 : TradingRatio >= 1 ? (TradingRatio * BaseUnitSize).ToInt()
        : TradesManagerStatic.GetLotstoTrade(account.Balance, TradesManager.Leverage(Pair), TradingRatio, BaseUnitSize);
      LotSizePercent = LotSize / account.Balance / TradesManager.Leverage(Pair);
      var td = TradingDistance;
      LotSizeByLossBuy = AllowedLotSizeCore();
      LotSizeByLossSell = AllowedLotSizeCore();
      //Math.Max(tm.LotSize, FXW.GetLotSize(Math.Ceiling(tm.CurrentLossPercent.Abs() / tm.LotSizePercent) * tm.LotSize, fw.MinimumQuantity));
      var stopAmount = 0.0;
      var limitAmount = 0.0;
      foreach (var trade in trades.ByPair(Pair)) {
        stopAmount += trade.StopAmount;
        limitAmount += trade.LimitAmount;
      }
      StopAmount = stopAmount;
      LimitAmount = limitAmount;
    }

    int LotSizeByLoss(ITradesManager tradesManager, double loss, int baseLotSize, double lotMultiplierInPips) {
      var bus = tradesManager.GetBaseUnitSize(Pair);
      return TradesManagerStatic.GetLotSize(-(loss / lotMultiplierInPips) * bus / TradesManager.GetPipCost(Pair), bus, true);
    }
    int LotSizeByLoss(double? lotMultiplierInPips = null) {
      var currentGross = this.TradingStatistics.CurrentGross;
      var lotSize = LotSizeByLoss(TradesManager,currentGross , LotSize, lotMultiplierInPips ?? TradingDistanceInPips);
      return lotMultiplierInPips.HasValue || lotSize <= MaxLotSize ? lotSize : LotSizeByLoss(TradesManager, currentGross, LotSize, RatesHeightInPips/_ratesHeightAdjustmentForAls);
    }

    int StrategyLotSizeByLossAndDistance(ICollection<Trade> trades) {
      var lotSize = trades.Select(t => t.Lots).DefaultIfEmpty(LotSizeByLoss()).Min()
        .Max(LotSize).Min((MaxLotByTakeProfitRatio * LotSize).ToInt());
      var lastLotSize = trades.Select(t => t.Lots).DefaultIfEmpty(lotSize).Max();
      var pl = trades.Select(t => (t.Close - t.Open).Abs()).OrderBy(d => d).LastOrDefault();
      var multilier = (Math.Floor(pl / CorridorStats.HeightUpDown) + 1).Min(MaxLotByTakeProfitRatio).ToInt();
      return (lotSize * multilier).Min(lastLotSize + lotSize);
    }

    private int LotSizeByDistance(ICollection<Trade> trades) {
      var pl = trades.Select(t => (t.Close - t.Open).Abs()).OrderBy(d => d).LastOrDefault();
      var multilier = (Math.Floor(pl / RatesHeight) + 1).Min(MaxLotByTakeProfitRatio).ToInt();
      return LotSize.Max(LotSize * multilier);
    }


    public int AllowedLotSizeCore( double? lotMultiplierInPips = null) {
      if (!HasRates) return 0;
      return LotSizeByLoss(lotMultiplierInPips).Max(LotSize);//.Min(MaxLotByTakeProfitRatio.ToInt() * LotSize);
    }
    private int CalculateLotCore(double totalGross, double? takeProfitPips = null) {
      //return TradesManager.MoneyAndPipsToLot(Math.Min(0, totalGross).Abs(), takeProfitPips.GetValueOrDefault(TakeProfitPips), Pair);
      return TradesManager.MoneyAndPipsToLot(Math.Min(0, totalGross).Abs(), takeProfitPips.GetValueOrDefault(TradingStatistics.TakeProfitPips), Pair);
    }
    #endregion

    #region Commands


    ICommand _GannAnglesResetCommand;
    public ICommand GannAnglesResetCommand {
      get {
        if (_GannAnglesResetCommand == null) {
          _GannAnglesResetCommand = new Gala.RelayCommand(GannAnglesReset, () => true);
        }

        return _GannAnglesResetCommand;
      }
    }
    void GannAnglesReset() {
      GannAnglesList.Reset();
    }


    ICommand _GannAnglesUnSelectAllCommand;
    public ICommand GannAnglesUnSelectAllCommand {
      get {
        if (_GannAnglesUnSelectAllCommand == null) {
          _GannAnglesUnSelectAllCommand = new Gala.RelayCommand(GannAnglesUnSelectAll, () => true);
        }

        return _GannAnglesUnSelectAllCommand;
      }
    }
    void GannAnglesUnSelectAll() {
      GannAnglesList.Angles.ToList().ForEach(a => a.IsOn = false);
    }


    ICommand _GannAnglesSelectAllCommand;
    public ICommand GannAnglesSelectAllCommand {
      get {
        if (_GannAnglesSelectAllCommand == null) {
          _GannAnglesSelectAllCommand = new Gala.RelayCommand(GannAnglesSelectAll, () => true);
        }
        return _GannAnglesSelectAllCommand;
      }
    }
    void GannAnglesSelectAll() {
      GannAnglesList.Angles.ToList().ForEach(a => a.IsOn = true);
    }

    #endregion


    #region PriceBars
    protected class PriceBarsDuplex {
      public PriceBar[] Long { get; set; }
      public PriceBar[] Short { get; set; }
      public PriceBar[] GetPriceBars(bool isLong) { return isLong ? Long : Short; }
    }
    protected PriceBarsDuplex PriceBars = new PriceBarsDuplex();
    protected void SetPriceBars(bool isLong, PriceBar[] priceBars) {
      if (isLong) PriceBars.Long = priceBars;
      else PriceBars.Short = priceBars;
    }
    protected PriceBar[] FetchPriceBars(int rowOffset, bool reversePower) {
      return FetchPriceBars(rowOffset, reversePower, DateTime.MinValue);
    }
    protected PriceBar[] FetchPriceBars(int rowOffset, bool reversePower, DateTime dateStart) {
      var isLong = dateStart == DateTime.MinValue;
      var rs = RatesArraySafe.Where(r => r.StartDate >= dateStart).GroupTicksToRates();
      var ratesForDensity = (reversePower ? rs.OrderBarsDescending() : rs.OrderBars()).ToList();
      SetPriceBars(isLong, ratesForDensity.GetPriceBars(TradesManager.GetPipSize(Pair), rowOffset));
      return GetPriceBars(isLong);
    }
    public PriceBar[] GetPriceBars(bool isLong) {
      return PriceBars.GetPriceBars(isLong) ?? new PriceBar[0];
    }
    #endregion


    private Exception _Log;
    public Exception Log {
      get { return _Log; }
      set {
        if (_Log != value) {
          _Log = value;
          OnPropertyChanged("Log");
        }
      }
    }

    bool isLoggedIn { get { return TradesManager != null && TradesManager.IsLoggedIn; } }

    int _limitBarToRateProvider {
      get {
        return (int)BarPeriod;// Enum.GetValues(typeof(BarsPeriodTypeFXCM)).Cast<int>().Where(i => i <= (int)BarPeriod).Max();
      }
    }
    public void LoadRates(bool dontStreachRates = false) {
      try {
        if (TradesManager != null && !TradesManager.IsInTest && !IsInPlayback && isLoggedIn && !IsInVitualTrading) {
          InfoTooltip = "Loading Rates";
          lock (_Rates) {
            Debug.WriteLine("LoadRates[{0}:{2}] @ {1:HH:mm:ss}", Pair, TradesManager.ServerTime, (BarsPeriodType)BarPeriod);
            var sw = Stopwatch.StartNew();
            var serverTime = TradesManager.ServerTime;
            var periodsBack = BarsCount;
            var useDefaultInterval = /*!DoStreatchRates || dontStreachRates ||*/ CorridorStats == null || CorridorStats.StartDate == DateTime.MinValue;
            var startDate = TradesManagerStatic.FX_DATE_NOW;
            if (!useDefaultInterval) {
              var intervalToAdd = Math.Max(5, RatesInternal.Count / 10);
              if (CorridorStartDate.HasValue)
                startDate = CorridorStartDate.Value;
              else if (CorridorStats == null)
                startDate = TradesManagerStatic.FX_DATE_NOW;
              else {
                startDate = CorridorStats.StartDate;//.AddMinutes(-(int)BarPeriod * intervalToAdd);
                var periodsByStartDate = RatesInternal.Count(r => r.StartDate >= startDate) + intervalToAdd;
                periodsBack = periodsBack.Max(periodsByStartDate);
              }
            }
            RatesInternal.RemoveAll(r => !r.IsHistory);
            if (RatesInternal.Count != RatesInternal.Distinct().Count()) {
              var ri = RatesInternal.Distinct().ToList();
              RatesInternal.Clear();
              RatesInternal.AddRange(ri);
            }

            bool wereRatesPulled = false;
            using (var ps = new PriceService.PriceServiceClient()) {
              try {
                //var ratesPulled = ps.FillPrice(Pair, RatesInternal.Select(r => r.StartDate).DefaultIfEmpty().Max());
                //RatesInternal.AddRange(ratesPulled);
                //wereRatesPulled = true;
                var DensityMin = true ? 0 : ps.PriceStatistics(Pair).BidHighAskLowSpread;
              } catch (Exception exc) {
                Log = exc;
              } finally {
                if (!wereRatesPulled)
                  RatesLoader.LoadRates(TradesManager, Pair, _limitBarToRateProvider, periodsBack, startDate, TradesManagerStatic.FX_DATE_NOW, RatesInternal);
              }
            }
            OnPropertyChanged(Metadata.TradingMacroMetadata.RatesInternal);
            if (sw.Elapsed > TimeSpan.FromSeconds(LoadRatesSecondsWarning))
              Debug.WriteLine("LoadRates[" + Pair + ":{1}] - {0:n1} sec", sw.Elapsed.TotalSeconds, (BarsPeriodType)BarPeriod);
            LastRatePullTime = TradesManager.ServerTime;
          }
          {
            RatesArraySafe.SavePairCsv(Pair);
          }
          //if (!HasCorridor) ScanCorridor();
        }
      } catch (Exception exc) {
        Log = exc;
      } finally {
        InfoTooltip = "";
      }
    }

    #region Overrides

    class OnPropertyChangedDispatcher : BlockingConsumerBase<Tuple<TradingMacro, string>> {
      public OnPropertyChangedDispatcher() : base(t => t.Item1.OnPropertyChangedCore(t.Item2)) { }
      public void Add(TradingMacro tm, string propertyName) {
        Add(new Tuple<TradingMacro, string>(tm, propertyName), (t1, t2) => t1.Item1 == t2.Item1 && t1.Item2 == t2.Item2);
      }
    }
    static OnPropertyChangedDispatcher OnPropertyChangedQueue = new OnPropertyChangedDispatcher();

    protected ConcurrentDictionary<Expression<Func<object>>, string> _propertyExpressionDictionary = new ConcurrentDictionary<Expression<Func<object>>, string>();

    protected void OnPropertyChanged(Expression<Func<object>> property) {
      //var propertyString = _propertyExpressionDictionary.GetOrAdd(property, pe => Lib.GetLambda(property));
      OnPropertyChanged(Lib.GetLambda(property));
      //OnPropertyChanged(Lib.GetLambda(property));
    }
    protected override void OnPropertyChanged(string property) {
      base.OnPropertyChanged(property);
      OnPropertyChangedCore(property);
      //OnPropertyChangedQueue.Add(this, property);
    }

    ITargetBlock<Action> _processCorridorDatesChange;
    ITargetBlock<Action> processCorridorDatesChange {
      get {
        return _processCorridorDatesChange;
      }
    }


    int _broadcastCounter;
    BroadcastBlock<Action<int>> _broadcastCorridorDatesChange;
    BroadcastBlock<Action<int>> broadcastCorridorDatesChange {
      get {
        if (_broadcastCorridorDatesChange == null) {
          _broadcastCorridorDatesChange = DataFlowProcessors.SubscribeToBroadcastBlock(() => _broadcastCounter);
        }
        return _broadcastCorridorDatesChange;
      }
    }


    public void OnPropertyChangedCore(string property) {
      if (EntityState == System.Data.EntityState.Detached) return;
      //_propertyChangedTaskDispencer.RunOrEnqueue(property, () => {
      switch (property) {
        case TradingMacroMetadata.IsTradingActive:
          SuppRes.ToList().ForEach(sr => sr.ResetPricePosition());
          break;
        case TradingMacroMetadata.TradingDistanceFunction:
        case TradingMacroMetadata.CurrentLoss:
          _tradingDistanceMax = 0;
          SetLotSize();
          break;
        case TradingMacroMetadata.Pair:
          goto case TradingMacroMetadata.CorridorBarMinutes;
        case TradingMacroMetadata.BarsCount:
        case TradingMacroMetadata.CorridorBarMinutes:
        case TradingMacroMetadata.LimitBar:
          CorridorStats = null;
          CorridorStartDate = null;
          //Strategy = Strategies.None;
          if (!IsInVitualTrading) {
            RatesInternal.Clear();
            OnLoadRates();
          }
          break;
        case TradingMacroMetadata.RatesInternal:
          RatesArraySafe.Count();
          OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
          break;
        case TradingMacroMetadata.Strategy:
          _strategyExecuteOnTradeClose = null;
          _strategyExecuteOnTradeOpen = null;
          CloseAtZero = false;
          DisposeOpenTradeByMASubject();
          _tradingDistanceMax = 0;
          goto case TradingMacroMetadata.IsSuppResManual;
        case TradingMacroMetadata.IsSuppResManual:
        case TradingMacroMetadata.TakeProfitFunction:
        case TradingMacroMetadata.CorridorDistanceRatio:
          OnScanCorridor(RatesArray);
          goto case TradingMacroMetadata.RangeRatioForTradeLimit;
        case TradingMacroMetadata.RangeRatioForTradeLimit:
        case TradingMacroMetadata.RangeRatioForTradeStop:
        case TradingMacroMetadata.IsColdOnTrades:
        case TradingMacroMetadata.IsPriceSpreadOk:
          SetEntryOrdersBySuppResLevels();
          break;
        case TradingMacroMetadata.CorridorCalcMethod:
        case TradingMacroMetadata.CorridorCrossHighLowMethod:
        case TradingMacroMetadata.CorridorCrossesCountMinimum:
        case TradingMacroMetadata.CorridorHighLowMethod:
        case TradingMacroMetadata.CorridorStDevRatioMax:
        case TradingMacroMetadata.TradingAngleRange:
        case TradingMacroMetadata.StDevAverageLeewayRatio:
        case TradingMacroMetadata.StDevTresholdIterations:
        case TradingMacroMetadata.MovingAverageType:
        case TradingMacroMetadata.PriceCmaPeriod:
        case TradingMacroMetadata.PriceCmaLevels:
        case TradingMacroMetadata.PolyOrder:
          try {
            if (RatesArray.Any()) {
              RatesArray.Clear();
              RatesArraySafe.Count();
            }
          } catch (Exception exc) {
            Log = exc;
          }
          break;
        case TradingMacroMetadata.SuppResLevelsCount_:
          AdjustSuppResCount();
          break;
      }
      //}, exc => Log = exc);
    }

    double PriceSpreadAverageInPips { get { return InPips(PriceSpreadAverage); } }
    double? _priceSpreadAverage;
    public double? PriceSpreadAverage {
      get { return _priceSpreadAverage; }
      set {
        var spread = RoundPrice(value.Value);
        if (_priceSpreadAverage == spread) return;
        _priceSpreadAverage = spread;
        OnPropertyChanged(() => PriceSpreadAverage);
        SetPriceSpreadOk();
      }
    }
    partial void OnLimitBarChanging(int newLimitBar) {
      if (newLimitBar == (int)BarPeriod) return;
      OnLoadRates();
    }
    Strategies[] _exceptionStrategies = new[] { Strategies.Auto};
    partial void OnCorridorBarMinutesChanging(int value) {
      if (value == CorridorBarMinutes) return;
      if (!IsInVitualTrading) {
        if (!_exceptionStrategies.Any(s => Strategy.HasFlag(s)))
          Strategy = Strategies.None;
        OnLoadRates();
      }
    }
    #endregion

    RatesLoader _ratesLoader;
    internal RatesLoader RatesLoader {
      get {
        if (_ratesLoader == null) _ratesLoader = new RatesLoader();
        return _ratesLoader;
      }
    }

    public void HideInfoTootipAsync(double delayInSeconds = 0) {
      ShowInfoTootipAsync("", delayInSeconds);
    }

    IDisposable DefferedRun<T>(T value, double delayInSeconds, Action<T> run) {
      return Observable.Return(value).Throttle(TimeSpan.FromSeconds(delayInSeconds)).SubscribeOnDispatcher().Subscribe(run, exc => Log = exc);
    }
    void DefferedRun(double delayInSeconds, Action run) {
      Observable.Return(0).Throttle(TimeSpan.FromSeconds(delayInSeconds)).SubscribeOnDispatcher().Subscribe(i => { run(); }, exc => Log = exc);
    }
    public void ShowInfoTootipAsync(string text = "", double delayInSeconds = 0) {
      DefferedRun(text, delayInSeconds, t => InfoTooltip = t);
      //new Scheduler(Application.Current.Dispatcher, TimeSpan.FromSeconds(Math.Max(.01, delayInSeconds))).Command = () => InfoTooltip = text;
    }

    private Rate _rateGannCurrentLast;
    public Rate RateGannCurrentLast {
      get { return _rateGannCurrentLast; }
      set { _rateGannCurrentLast = value; }
    }
    private string _InfoTooltip;
    public string InfoTooltip {
      get { return _InfoTooltip; }
      set {
        _InfoTooltip = value;
        OnPropertyChanged(TradingMacroMetadata.InfoTooltip);
      }
    }

    private double _TakeProfitDistance;
    public double TakeProfitDistance {
      get { return _TakeProfitDistance; }
      set {
        if (_TakeProfitDistance != value) {
          _TakeProfitDistance = value;
          OnPropertyChanged(TradingMacroMetadata.TakeProfitDistance);
          OnPropertyChanged(TradingMacroMetadata.TakeProfitDistanceInPips);
          OnPropertyChanged(TradingMacroMetadata.TrendNessRatio);
        }
      }
    }
    public double TakeProfitDistanceInPips { get { return InPips(TakeProfitDistance); } }



    public double RatesStDevToRatesHeightRatio { get { return RatesHeight / RatesStDev; } }

    public double TrendNessRatio {
      get {
        if (!RatesArraySafe.Any()) return double.NaN;
        var pipSize = TradesManager.GetPipSize(Pair);
        var count = 0.0;
        for (var d = -CorridorStats.HeightDown0; d <= CorridorStats.HeightUp0; d += pipSize) {
          count = count.Max(RatesArraySafe.Count(r => (r.PriceAvg1 + d).Between(r.PriceLow, r.PriceHigh)));
        }
        return count / RatesArraySafe.Count();
      }
    }

    private double _limitRate;

    public double LimitRate {
      get { return _limitRate; }
      set {
        _limitRate = value;
        OnPropertyChanged(() => LimitRate);
      }
    }

    double _RatesHeight;
    public double RatesHeight {
      get { return _RatesHeight; }
      set {
        if (_RatesHeight == value) return;
        _RatesHeight = value;
        OnPropertyChanged(() => RatesHeightInPips);
      }
    }
    public double RatesHeightInPips { get { return InPips(RatesHeight); } }
    private bool CanOpenTradeByDirection(bool isBuy) {
      if (isBuy && TradeDirection == TradeDirections.Down) return false;
      if (!isBuy && TradeDirection == TradeDirections.Up) return false;
      return true;
    }

    public DateTime TouchDownDateTime { get; set; }

    private IList<WaveInfo> _waveRates = new List<WaveInfo>();
    private List<Tuple<int, double>> _levelCounts;
    public IList<WaveInfo> WaveRates {
      get { return _waveRates; }
      set { _waveRates = value; }
    }

    public double VolumeAverageLow { get; set; }

    private List<List<Rate>> _CorridorsRates = new List<List<Rate>>();
    private Store.SuppRes _buyLevel;
    private Store.SuppRes _sellLevel;
    private IList<IList<Rate>> _waves;

    public IList<IList<Rate>> Waves {
      get { return _waves; }
      set { _waves = value; }
    }
    private IList<Rate> _WaveHigh;
    public IList<Rate> WaveHigh {
      get { return _WaveHigh; }
      set {
        if (_WaveHigh != value) {
          _WaveHigh = value;
          //_waveHighOnOff = new ValueWithOnOff<IList<Rate>>(value);
        }
      }
    }

    ValueWithOnOff<IList<Rate>> _waveHighOnOff;
    private List<IList<Rate>> _wavesBig;
    public List<IList<Rate>> WavesBig {
      get { return _wavesBig; }
      set { _wavesBig = value; }
    }

    private double _waveAverage;
    public double WaveAverage {
      get { return _waveAverage; }
      set {
        if (_waveAverage == value) return;
        _waveAverage = value;
        OnPropertyChanged("WaveAverage");
        OnPropertyChanged("WaveAverageInPips");
      }
    }

    public double WaveAverageInPips { get { return InPips(WaveAverage); } }

    class ValueWithOnOff<T> : Models.ModelBase {
      public T Value { get; set; }
      public bool IsOn { get; set; }
      public ValueWithOnOff(T value) {
        this.Value = value;
        this.TurnOn();
      }
      public void TurnOn() { IsOn = true; }
      public void TurnOff() { IsOn = false; }
    }
    private bool _isWaveOk;

    interface IWave<TBar> where TBar : BarBase {
      IList<TBar> Rates { get; set; }
      double RatesMax { get;  }
      double RatesMin { get;  }
    }
    public class WaveInfo : Models.ModelBase,IWave<Rate> {
      #region Distance
      public Rate DistanceRate { get; set; }
      double _Distance = double.NaN;

      public double Distance {
        get { return _Distance; }
        set {
          if (_Distance == value) return;
          _Distance = value;
          RaiseDistanceChanged();
        }
      }
      public bool HasDistance { get { return !double.IsNaN(Distance); } }
      public double ClearDistance() { return Distance = double.NaN; }
      public Rate SetRateByDistance(IList<Rate> rates) {
        if (!this.HasDistance) return null;
        return DistanceRate = rates.ReverseIfNot().SkipWhile(r => r.Distance < this.Distance).FirstOrDefault();
      }
      public void SetRatesByDistance(IList<Rate> rates,int countMinimum = 30) {
        if (!this.HasDistance) return;
        this.Rates = RatesByDistance(rates, Distance).ToArray();
        if (this.Rates.Count < countMinimum)
          this.Rates = rates.Take(countMinimum).ToArray();
      }
      public static Rate RateByDistance(IList<Rate> rates, double distance) {
        return rates.ReverseIfNot().SkipWhile(r => r.Distance < distance).First();
      }
      public static IEnumerable<Rate> RatesByDistance(IList<Rate> rates, double distance) {
        return rates.ReverseIfNot().TakeWhile(r => r.Distance <= distance);
      }
      #endregion


      public double RatesHeight { get { return _Rates == null ? double.NaN : RatesMax - RatesMin; } }
      public double RatesHeightInPips { get { return _tradingMacro.InPips(RatesHeight); } }

      #region RatesMax,RatesMinRatesStDev
      double _RatesMax = double.NaN;
      public double RatesMax {
        get {
          if (double.IsNaN(_RatesMax) && HasRates)
            _RatesMax = Rates.Max(_tradingMacro.CorridorPrice());
          return _RatesMax;
        }
        set {
          _RatesMax = value;
        }
      }
      double _RatesMin = double.NaN;
      public double RatesMin {
        get {
          if (double.IsNaN(_RatesMin) && HasRates)
            _RatesMin = Rates.Min(_tradingMacro.CorridorPrice());
          return _RatesMin;
        }
        set {
          _RatesMin = value;
        }
      }
      public double RatesStDevPrev { get; set; }
      private double _RatesStDev = double.NaN;
      public double RatesStDev {
        get {
          if (double.IsNaN(_RatesStDev) && HasRates && _Rates.Count > 1) {
            var corridor = Rates.ScanCorridorWithAngle(_tradingMacro.CorridorPrice, _tradingMacro.CorridorPrice, TimeSpan.Zero, _tradingMacro.PointSize, _tradingMacro.CorridorCalcMethod);
            //var stDevs = Rates.Shrink(_tradingMacro.CorridorPrice, 5).ToArray().ScanWaveWithAngle(v => v, _tradingMacro.PointSize, _tradingMacro.CorridorCalcMethod);
            _RatesStDev = corridor.StDev;
            _Angle = corridor.Slope.Angle(_tradingMacro.PointSize);
          }
          return _RatesStDev; 
        }
        set {
          if (double.IsNaN(value)) RatesStDevPrev = _RatesStDev;
          _RatesStDev = value;
        }
      }
      public double RatesStDevInPips { get { return _tradingMacro.InPips(RatesStDev); } }
      #endregion
      public WaveInfo(TradingMacro tradingMacro) {
        this._tradingMacro = tradingMacro;
      }
      public WaveInfo(TradingMacro tradingMacro,IList<Rate> rates):this(tradingMacro) {
        this.Rates = rates;
      }

      public bool HasRates { get { return _Rates != null && _Rates.Any(); } }
      IList<Rate> _Rates;
      private TradingMacro _tradingMacro;
      public IList<Rate> Rates {
        get {
          if (_Rates == null || !_Rates.Any())
            throw new NullReferenceException();
          return _Rates;
        }
        set {
          var isUp = value == null || value.Count == 0  ? null : (bool?)(value.LastBC().PriceAvg < value[0].PriceAvg);
          if (value == null || !HasRates || isUp != IsUp || Rates.LastBC() == value.LastBC())
            _Rates = value;
          else {
            var newRates = value.TakeWhile(r => r != Rates[0]);
            _Rates = newRates.Concat(_Rates).ToArray();
          }
          RatesMax = double.NaN;
          RatesMin = double.NaN;
          RatesStDev = double.NaN;
          Angle = double.NaN;
          if (_Rates != null && _Rates.Any()) {
            StartDate = _Rates.LastBC().StartDate;
            IsUp = isUp;
          }
          RaisePropertyChanged("Rates");
          RaisePropertyChanged("RatesStDev");
          RaisePropertyChanged("RatesStDevInPips");
          RaisePropertyChanged("RatesHeightInPips");
        }
      }
      public void Reset() {
        ClearDistance();
        ClearEvents();
        Rates = null;
      }

      #region Events
      public void ClearEvents() {
        if (DistanceChangedEvent != null)
          foreach (var handler in DistanceChangedEvent.GetInvocationList().Cast<EventHandler<EventArgs>>())
            DistanceChangedEvent -= handler;
        if (StartDateChangedEvent != null)
          foreach (var handler in StartDateChangedEvent.GetInvocationList().Cast<EventHandler<NewOldEventArgs<DateTime>>>())
            StartDateChangedEvent -= handler;
        if (IsUpChangedEvent!= null)
          foreach (var handler in IsUpChangedEvent.GetInvocationList().Cast<EventHandler<NewOldEventArgs<bool?>>>())
            IsUpChangedEvent -= handler;
      }
      #region DistanceChanged Event
      event EventHandler<EventArgs> DistanceChangedEvent;
      public event EventHandler<EventArgs> DistanceChanged {
        add {
          if (DistanceChangedEvent == null || !DistanceChangedEvent.GetInvocationList().Contains(value))
            DistanceChangedEvent += value;
        }
        remove {
          DistanceChangedEvent -= value;
        }
      }
      protected void RaiseDistanceChanged() {
        if (DistanceChangedEvent != null) DistanceChangedEvent(this, new EventArgs());
      }
      #endregion

      #region StartDateChanged Event
      event EventHandler<NewOldEventArgs<DateTime>> StartDateChangedEvent;
      public event EventHandler<NewOldEventArgs<DateTime>> StartDateChanged {
        add {
          if (StartDateChangedEvent == null || !StartDateChangedEvent.GetInvocationList().Contains(value))
            StartDateChangedEvent += value;
        }
        remove {
          StartDateChangedEvent -= value;
        }
      }
      protected void RaiseStartDateChanged(DateTime now,DateTime then) {
        if (StartDateChangedEvent != null) StartDateChangedEvent(this, new NewOldEventArgs<DateTime>(now,then));
      }
      #endregion

      #region IsUpChanged Event
      event EventHandler<NewOldEventArgs<bool?>> IsUpChangedEvent;
      public event EventHandler<NewOldEventArgs<bool?>> IsUpChanged {
        add {
          if (IsUpChangedEvent == null || !IsUpChangedEvent.GetInvocationList().Contains(value))
            IsUpChangedEvent += value;
        }
        remove {
          IsUpChangedEvent -= value;
        }
      }
      protected void RaiseIsUpChanged(bool? now,bool? then) {
        if (IsUpChangedEvent != null) IsUpChangedEvent(this, new NewOldEventArgs<bool?>(now, then));
      }
      #endregion

      #endregion

      public Rate Rate { get; set; }
      /// <summary>
      /// 1 - based
      /// </summary>
      public int Position { get; set; }
      public double Slope { get; set; }
      public double Direction { get { return Math.Sign(Slope); } }
      public WaveInfo(Rate rate, int position, double slope) {
        this.Rate = rate;
        this.Position = position;
        this.Slope = slope;
      }



      #region StartDate
      private DateTime _StartDate;
      public DateTime StartDate {
        get { return _StartDate; }
        set {
          if (_StartDate != value) {
            var then = _StartDate;
            _StartDate = value;
            RaisePropertyChanged("StartDate");
            RaiseStartDateChanged(_StartDate,then);
          }
        }
      }

      #endregion

      private double _Angle = double.NaN;
      public double Angle {
        get {
          if (double.IsNaN(_Angle))
            RatesStDev = RatesStDev;
          return _Angle;
        }
        set { _Angle = value; }
      }

      #region IsUp
      private Lazy<bool?> _getUp = new Lazy<bool?>();
      private bool? _IsUp;
      public bool? IsUp {
        get { return IsUpChangedEvent == null ? _getUp.Value : _IsUp; }
        set {
          if (_IsUp != value) {
            if (!HasRates || _Rates.Count < 2) {
              _IsUp = null;
              return;
            }
            _getUp = new Lazy<bool?>(() => HasRates ? _Rates.Select(r => r.PriceAvg).ToArray().Regress(1)[1] < 0 : (bool?)null, true);
            if (IsUpChangedEvent == null)
              return;
            else {
              throw new NotImplementedException("IsUp property of WaviInfo class must be tested with IsUpChangedEvent != null.");
              var isUp = _getUp.Value;
              if (_IsUp == isUp) return;
              _IsUp = isUp;
              RatesStDevPrev = _RatesStDev;
              RaisePropertyChanged("IsUp");
              RaiseIsUpChanged(_IsUp, !_IsUp);
            }
          }
        }
      }

      #endregion
    }

    public class WaveWithHeight<TBar>:Models.ModelBase,IWave<TBar> where TBar:BarBase {
      public double RatesHeight { get; set; }
      double _min = double.NaN;
      public double RatesMin { get { return _min; } set { _min = value; } }
      double _max = double.NaN;
      public double RatesMax { get { return _max; } set { _max = value; } }
      IList<TBar> _bars = new TBar[0];
      #region StDev
      private double _StDev = double.NaN;
      public double StDev {
        get { return _StDev; }
        set {
          if (_StDev != value) {
            _StDev = value;
            RaisePropertyChanged("StDev");
          }
        }
      }
      #endregion
      public IList<TBar> Rates {
        get { return _bars; }
        set { 
          _bars = value;
          RatesHeight = value.Height(out _min, out _max);
          RaisePropertyChanged("Rates");
        }
      }
      public WaveWithHeight(IList<TBar> bars  ) {
        this.Rates = bars;
        this.StDev = bars.StDev(r => r.PriceAvg);
      }
    }
    private WaveInfo _waveTradeStart;
    public WaveInfo WaveTradeStart {
      get { return _waveTradeStart ?? (_waveTradeStart = new WaveInfo(this)); }
      set { _waveTradeStart = value; }
    }
    private WaveInfo _waveTradeStart1;
    public WaveInfo WaveTradeStart1 {
      get { return _waveTradeStart1 ?? (_waveTradeStart1 = new WaveInfo(this)); }
      set { _waveTradeStart1 = value; }
    }
    public List<List<Rate>> CorridorsRates {
      get { return _CorridorsRates; }
      set { _CorridorsRates = value; }
    }

    public Tuple<Rate, Rate, int> _waveLong { get; set; }

    public double _RatesMax = 0;

    public double _RatesMin = 0;
    private bool _isStrategyRunning;

    #region IsTradingActive
    private bool _IsTradingActive = true;
    
    private static TaskScheduler _currentDispatcher;

    public bool IsTradingActive {
      get { return _IsTradingActive; }
      set {
        if (_IsTradingActive != value) {
          _IsTradingActive = value;
          OnPropertyChanged(() => IsTradingActive);
        }
      }
    }

    #endregion

    #region WaveLength
    private int _WaveLength;
    public int WaveLength {
      get {
        if (_WaveLength == 0) throw new ArgumentOutOfRangeException("WaveLength");
        return _WaveLength;
      }
      set {
        if (_WaveLength != value) {
          _WaveLength = value;
          OnPropertyChanged("WaveLength");
        }
      }
    }
    #endregion
    public double WaveDistanceInPips { get { return InPips(WaveDistance); } }

    IEnumerable<SuppRes> _suppResesForBulk() { return SuppRes.Where(sr => !sr.IsExitOnly || sr.InManual); }
    public void ResetSuppResesInManual() {
      ResetSuppResesInManual(!_suppResesForBulk().Any(sr => sr.InManual));
    }
    public void ResetSuppResesInManual(bool isManual) {
      _suppResesForBulk().ForEach(sr => sr.InManual = isManual);
    }
    public void SetCanTrade(bool canTrade) {
      _suppResesForBulk().ToList().ForEach(sr => sr.CanTrade = canTrade);
    }
    public void ToggleCanTrade() {
      var srs = _suppResesForBulk().ToList();
      var canTrade = !srs.Any(sr => sr.CanTrade);
      srs.ForEach(sr => sr.CanTrade = canTrade);
    }
    public void SetTradeCount(int tradeCount) {
      _suppResesForBulk().ForEach(sr => sr.TradesCount = tradeCount);
    }

    #region StDevByPriceAvg
    private double _StDevByPriceAvg;
    public double StDevByPriceAvg {
      get { return _StDevByPriceAvg; }
      set {
        if (_StDevByPriceAvg != value) {
          _StDevByPriceAvg = value;
          OnPropertyChanged("StDevByPriceAvg");
          OnPropertyChanged("StDevByPriceAvgInPips");
        }
      }
    }
    public double StDevByPriceAvgInPips { get { return InPips(StDevByPriceAvg); } }
    #endregion

    #region StDevByHeight
    private double _StDevByHeight;
    public double StDevByHeight {
      get { return _StDevByHeight; }
      set {
        if (_StDevByHeight != value) {
          _StDevByHeight = value;
          OnPropertyChanged("StDevByHeight");
          OnPropertyChanged("StDevByHeightInPips");
        }
      }
    }
    public double StDevByHeightInPips { get { return InPips(StDevByHeight); } }
    #endregion

    public double CorridorStDevSqrt { get { return Math.Pow(CorridorStats.StDevByPriceAvg * CorridorStats.StDevByHeight, .52); } }
    public double CorridorStDevSqrtInPips { get { return InPips(CorridorStDevSqrt); } }
    public double RatesStDevInPips { get { return InPips(RatesStDev); } }
    private double _RatesStDev;
    public double RatesStDev {
      get { return _RatesStDev; }
      set {
        if (_RatesStDev == value) return;
        _RatesStDev = value;
        OnPropertyChanged("RatesStDev");
        OnPropertyChanged("RatesStDevInPips");
      }
    }

    double _WaveDistanceForTrade = double.NaN;

    public double WaveDistanceForTrade {
      get {
        if (double.IsNaN(_WaveDistanceForTrade)) throw new ArgumentOutOfRangeException("WaveDistanceForTrade");
        return _WaveDistanceForTrade;
      }
      set {
        if (_WaveDistanceForTrade == value) return;
        _WaveDistanceForTrade = value;
      }
    }
    double _WaveDistance;
    /// <summary>
    /// Distance for WaveShort
    /// </summary>
    public double WaveDistance { get { return _WaveDistance; }
      set {
        if (_WaveDistance == value) return;
        _WaveDistance = value;
        OnPropertyChanged(() => WaveDistance);
        OnPropertyChanged(() => WaveDistanceInPips);
        OnPropertyChanged(() => WaveShortDistanceInPips);
      }
    }
    public double WaveShortDistance { get { return WaveShort.Distance.IfNaN(WaveDistance); } }
    public double WaveShortDistanceInPips { get { return InPips(WaveShortDistance); } }

    double _RatesStDevAdjusted = double.NaN;
    private DB.v_BlackoutTime[] _blackoutTimes;

    private IList<DB.s_GetBarStats_Result> _TimeFrameStats;
    private double _ratesHeightAdjustmentForAls = 2;

    public IList<DB.s_GetBarStats_Result> TimeFrameStats {
      get { return _TimeFrameStats; }
      set {
        if (_TimeFrameStats == value) return;
        _TimeFrameStats = value;
        _MonthStats = new MonthStatistics(value);
      }
    }

    public double RatesStDevAdjusted {
      get { return _RatesStDevAdjusted; }
      set {
        if (_RatesStDevAdjusted == value) return;
        _RatesStDevAdjusted = value;
        OnPropertyChanged("RatesStDevAdjusted");
        OnPropertyChanged("RatesStDevAdjustedInPips");
      }
    }
    public double RatesStDevAdjustedInPips { get { return InPips(RatesStDevAdjusted); } }
  }
  public static class WaveInfoExtentions {
    public static Dictionary<CorridorCalculationMethod, double> ScanWaveWithAngle<T>(this IList<T> rates, Func<T, double> price , double pointSize, CorridorCalculationMethod corridorMethod) {
      return rates.ScanWaveWithAngle(price, price, price, pointSize, corridorMethod);
    }
    public static Dictionary<CorridorCalculationMethod, double> ScanWaveWithAngle<T>(this IList<T> rates, Func<T, double> price, Func<T, double> priceHigh, Func<T, double> priceLow, double pointSize, CorridorCalculationMethod corridorMethod) {
      try {
        #region Funcs
        double[] linePrices = new double[rates.Count()];
        Func<int, double> priceLine = index => linePrices[index];
        Action<int, double> lineSet = (index, d) => linePrices[index] = d;
        var coeffs = rates.SetRegressionPrice(1, price, lineSet);
        var sineOffset = Math.Sin(Math.PI / 2 - coeffs[1] / pointSize);
        Func<T, int, double> heightHigh = (rate, index) => (priceHigh(rate) - priceLine(index)) * sineOffset;
        Func<T, int, double> heightLow = (rate, index) => (priceLine(index) - priceLow(rate)) * sineOffset;
        #endregion
        #region Locals
        var lineLow = new LineInfo(new Rate[0], 0, 0);
        var lineHigh = new LineInfo(new Rate[0], 0, 0);
        #endregion

        var stDevDict = new Dictionary<CorridorCalculationMethod, double>();
        if (corridorMethod == CorridorCalculationMethod.Minimum || corridorMethod == CorridorCalculationMethod.Maximum) {
          stDevDict.Add(CorridorCalculationMethod.HeightUD, rates.Select(heightHigh).Union(rates.Select(heightLow)).ToList().StDevP());
          stDevDict.Add(CorridorCalculationMethod.Height, rates.Select((r, i) => heightHigh(r, i).Abs() + heightLow(r, i).Abs()).ToList().StDevP());
          if (corridorMethod == CorridorCalculationMethod.Minimum)
            stDevDict.Add(CorridorCalculationMethod.Price, rates.GetPriceForStats(price, priceLine, priceHigh, priceLow).ToList().StDevP());
          else
            stDevDict.Add(CorridorCalculationMethod.PriceAverage, rates.StDev(price));
        } else
          switch (corridorMethod) {
            case CorridorCalculationMethod.Minimum:
              stDevDict.Add(CorridorCalculationMethod.Minimum, stDevDict.Values.Min()); break;
            case CorridorCalculationMethod.Maximum:
              stDevDict.Add(CorridorCalculationMethod.Maximum, stDevDict.Values.Max()); break;
            case CorridorCalculationMethod.Height:
              stDevDict.Add(CorridorCalculationMethod.Height, rates.Select((r, i) => heightHigh(r, i).Abs() + heightLow(r, i).Abs()).ToList().StDevP()); break;
            case CorridorCalculationMethod.HeightUD:
              stDevDict.Add(CorridorCalculationMethod.HeightUD, rates.Select(heightHigh).Union(rates.Select(heightLow)).ToList().StDevP()); break;
            case CorridorCalculationMethod.Price:
              stDevDict.Add(CorridorCalculationMethod.Price, rates.GetPriceForStats(price, priceLine, priceHigh, priceLow).ToList().StDevP()); break;
            default:
              throw new NotSupportedException(new { corridorMethod } + "");
          }
        stDevDict.Add(CorridorCalculationMethod.PriceAverage, rates.StDev(price));
        return stDevDict;
      } catch (Exception exc) {
        Debug.WriteLine(exc);
        throw;
      }
    }

  }
}
