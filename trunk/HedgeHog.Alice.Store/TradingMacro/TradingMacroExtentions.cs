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
              .ObserveOn(Scheduler.NewThread)
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
              .ObserveOn(Scheduler.ThreadPool)
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
              .SubscribeOn(Scheduler.TaskPool)
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
      public __openTradeInfo(Action<Action> action,bool isBuy) {
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
            .Do(oti => Log = new Exception("[" + Pair + "] OpenTradeByMASubject Queued: " + new { oti.isBuy}))
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
      if (Strategy == Strategies.Breakout) return true;
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
    MemoryCache _pendingEntryOrders;
    MemoryCache PendingEntryOrders {
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
    [MethodImpl(MethodImplOptions.Synchronized)]
    private bool CheckPendingKey(string key) {
      return !PendingEntryOrders.Contains(key);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void CheckPendingAction(string key, Action<Action> action = null) {
      if (CheckPendingKey(key)) {
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
      SuppRes.AssociationChanged += new CollectionChangeEventHandler(SuppRes_AssociationChanged);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<CloseAllTradesMessage>(this, a => {
        if (!IsInVitualTrading && IsActive && TradesManager != null) {
          TradesManager.ClosePair(Pair);
          CurrentLoss = 0;
        }
      });
    }
    ~TradingMacro() {
      var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
      if (fw != null && fw.IsLoggedIn)
        fw.DeleteOrders(fw.GetEntryOrders(Pair, true));
      else if (Strategy.HasFlag(Strategies.Hot))
        MessageBox.Show("Account is already logged off. Unable to close Entry Orders.");
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
      sr.Rate = a.OrderByDescending(t=>t.Item1).First().Item2;
      RaiseShowChart();
    }

    #region ScanCrosses
    private List<Tuple<int, double>> ScanCrosses(double levelStart, double levelEnd,double stepInPips = 1) {
      return ScanCrosses(RatesArray, levelStart, levelEnd,stepInPips);
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
      if (!IsInVitualTrading && !IsInPlayback)
        (sender as SuppRes).CrossesCount = GetCrossesCount(RatesArray, (sender as SuppRes).Rate);
      return;
      SetEntryOrdersBySuppResLevels();
    }
    #endregion

    static Guid _sessionId = Guid.NewGuid();
    public static Guid SessionId { get { return _sessionId; } }
    public void ResetSessionId() {
      _sessionId = Guid.NewGuid();
    }

    public string CompositeId { get { return Pair + "_" + PairIndex; } }

    public string CompositeName { get { return Pair + ":" + BarPeriod; } }
    partial void OnPairChanged() { OnPropertyChanged(TradingMacroMetadata.CompositeName); }
    partial void OnLimitBarChanged() { OnPropertyChanged(TradingMacroMetadata.CompositeName); }

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

    public int[] CorridorIterationsArray {
      get {
        try {
          return CorridorIterations.Split(',').Select(s => int.Parse(s)).ToArray();
        } catch (Exception) { return new int[] { }; }
      }
      set {
        OnPropertyChanged("CorridorIterationsArray");
      }
    }


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
        var datePrev = _CorridorStats == null ? DateTime.MinValue : _CorridorStats.StartDate;
        if (_CorridorStats != value && value != null)
          value.StartDateChanged += CorridorStats_StartDateChanged;
        _CorridorStats = value;
        if (_CorridorStats!=null)
          _CorridorStats.PeriodsJumped += CorridorStats_PeriodsJumped;
        ////CorridorStatsArray.ToList().ForEach(cs => cs.IsCurrent = cs == value);
        //lock (_rateArrayLocker) {
          RatesArray.ForEach(r => r.PriceAvg1 = r.PriceAvg2 = r.PriceAvg3 = r.PriceAvg02 = r.PriceAvg03 = r.PriceAvg21 = r.PriceAvg31 = double.NaN);
          if (value != null) {
            if (false && RatesArray.LastOrDefault() != _CorridorStats.Rates.FirstOrDefault()) {
              Log = new Exception(Pair + ": LastCorridorRate:" + _CorridorStats.Rates.FirstOrDefault() + ",LastRate:" + RatesArray.LastOrDefault());
              Task.Factory.StartNew(() => OnScanCorridor(RatesArray));
              return;
            }
            CorridorStats.Rates
              .SetCorridorPrices(CorridorStats.Coeffs
                , CorridorStats.HeightUp0, CorridorStats.HeightDown0
                , CorridorStats.HeightUp, CorridorStats.HeightDown
                , CorridorStats.HeightUp0*3, CorridorStats.HeightDown0*3
                , r => /*Strategy == Strategies.Breakout ? r.PriceAvg1 :*/ MagnetPrice, (r, d) => r.PriceAvg1 = d
                , (r, d) => r.PriceAvg02 = d, (r, d) => r.PriceAvg03 = d
                , (r, d) => r.PriceAvg2 = d, (r, d) => r.PriceAvg3 = d
                , (r, d) => r.PriceAvg21 = d, (r, d) => r.PriceAvg31 = d
              );
            CorridorAngle = CorridorStats.Slope;
            CalculateCorridorHeightToRatesHeight();
            CalculateSuppResLevels();
            var tp = CalculateTakeProfit();
            TakeProfitPips = InPips(tp);
            if( !Trades.Any())
              LimitRate = GetLimitRate(GetPriceMA(_CorridorStats.Rates[0]) < _CorridorStats.Rates[0].PriceAvg1);
            //if (tp < CurrentPrice.Spread * 3) CorridorStats.IsCurrent = false;
            OnOpenTradeByMA(RatesArray.LastOrDefault(r => r.PriceAvg1 > 0));
            if (false && !IsGannAnglesManual)
              SetGannAngleOffset(value);
            UpdateTradingGannAngleIndex();
          }
        //}

        #region PropertyChanged
        CalculateCorridorHeightToRatesHeight();
        RaiseShowChart();
        OnPropertyChanged(TradingMacroMetadata.CorridorStats);
        OnPropertyChanged(TradingMacroMetadata.HasCorridor);
        OnPropertyChanged(TradingMacroMetadata.CorridorHeightByRegressionInPips);
        OnPropertyChanged(TradingMacroMetadata.CorridorHeightByRegressionInPips0);
        OnPropertyChanged(TradingMacroMetadata.CorridorsRatio);
        #endregion
      }
    }

    event EventHandler<CorridorStatistics.StartDateEventArgs> CorridorStartDateChanged;
    void CorridorStats_StartDateChanged(object sender, CorridorStatistics.StartDateEventArgs e) {
      if (CorridorStartDateChanged != null) CorridorStartDateChanged(sender, e);
    }

    void CorridorStats_PeriodsJumped(object sender, EventArgs e) {
      if (false && HasCorridor)
        ForceOpenTrade = CorridorStats.Slope < 0;
    }
    double CalculateCorridorHeightToRatesHeight(CorridorStatistics cs) {
      return cs.HeightUpDown / RatesHeight;
    }
    void CalculateCorridorHeightToRatesHeight() {
      CorridorHeightToRatesHeightRatio = CalculateCorridorHeightToRatesHeight(CorridorStats);
    }

    private double _CorridorHeightToRatesHeightRatio;
    public double CorridorHeightToRatesHeightRatio {
      get { return _CorridorHeightToRatesHeightRatio; }
      set {
        if (_CorridorHeightToRatesHeightRatio != value) {
          _CorridorHeightToRatesHeightRatio = value;
          OnPropertyChanged(() => CorridorHeightToRatesHeightRatio);
        }
      }
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
      get { return Trades.Sum(t => t.GrossPL) - (TradesManager == null ? 0 : TradesManager.CommissionByTrades(Trades)); }
    }

    public double ExitOnNetAmount { get { return Math.Min(RangeRatioForTradeLimit, 0).Abs(); } }
    public bool DoExitOnCurrentNet { get { return RangeRatioForTradeLimit < 0; } }
    public double CurrentGross {
      get { return CurrentLoss + OpenTradesGross + Math.Min(RangeRatioForTradeLimit, 0); }
    }

    public double CurrentGrossInPips {
      get { return TradesManager == null ? double.NaN : TradesManager.MoneyAndLotToPips(CurrentGross, Trades.Length == 0 ? AllowedLotSizeCore(Trades) : Trades.NetLots().Abs(), Pair); }
    }

    public double CurrentLossInPips {
      get { return TradesManager == null ? double.NaN : TradesManager.MoneyAndLotToPips(CurrentLoss, Trades.Length == 0 ? AllowedLotSizeCore(Trades) : Trades.NetLots().Abs(), Pair); }
    }

    public int PositionsBuy { get { return Trades.IsBuy(true).Length; } }
    public int PositionsSell { get { return Trades.IsBuy(false).Length; } }

    public double PipsPerPosition {
      get {
        var trades = Trades.ToArray();
        return trades.Length < 2 ? (trades.Length == 1 ? trades[0].PL : 0) : InPips(trades.Max(t => t.Open) - trades.Min(t => t.Open)) / (trades.Length - 1); }
    }



    public double CorridorHeightByRegression0 { get { return CorridorStats == null ? double.NaN : CorridorStats.HeightUpDown0; } }
    public double CorridorHeightByRegression { get { return CorridorStats == null ? double.NaN : CorridorStats.HeightUpDown; } }
    public double CorridorHeightByRegressionInPips0 { get { return InPips(CorridorHeightByRegression0); } }
    public double CorridorHeightByRegressionInPips { get { return InPips(CorridorHeightByRegression); } }
    public double CorridorsRatio { get { return CorridorHeightByRegression / CorridorHeightByRegression0; } }


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
          return RangeRatioForTradeLimit < 0 ? -RangeRatioForTradeLimit : CalculateTakeProfit() * RangeRatioForTradeLimit;
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

    #region Corridor Ratios
    public double CorridorHeightToSpreadRatio { get { return CorridorStats.HeightUpDown / SpreadForCorridor; } }
    public double CorridorHeight0ToSpreadRatio { get { return CorridorStats.HeightUpDown0 / SpreadForCorridor; } }
    public double CorridorStDevToRatesStDevRatio { get { return CalcCorridorStDevToRatesStDevRatio(CorridorStats); } }
    public double CalcCorridorStDevToRatesStDevRatio(CorridorStatistics cs) { return (cs.StDev / RatesStDev).Round(2); }
    public bool CalcIsCorridorStDevToRatesStDevRatioOk(CorridorStatistics cs) { return cs.StDev / RatesStDev < .4; }
    public bool IsCorridorStDevToRatesStDevRatioOk { get { return CalcIsCorridorStDevToRatesStDevRatioOk(CorridorStats); } }
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
        return _tradeCloseHandler ?? (_tradeCloseHandler = new EventHandler<TradeEventArgs>(TradesManager_TradeClosed));
      }
    }

    EventHandler<TradeEventArgs> _TradeAddedHandler;
    EventHandler<TradeEventArgs> TradeAddedHandler {
      get {
        if (_TradeAddedHandler == null) _TradeAddedHandler = new EventHandler<TradeEventArgs>(TradesManager_TradeAddedGlobal);
        return _TradeAddedHandler;
      }
    }

    public double InPips(double? d) {
      return TradesManager == null ? double.NaN : TradesManager.InPips(Pair, d);
    }
    Func<ITradesManager> _TradesManager = () => null;
    public ITradesManager TradesManager { get { return _TradesManager(); } }
    public void SubscribeToTradeClosedEVent(Func<ITradesManager> getTradesManager) {
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
      if (_strategyExecuteOnTradeOpen != null) _strategyExecuteOnTradeOpen();
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
      if (Strategy == Strategies.Wave || !Trades.Any() && CurrentGross >= 0)
        SuppRes.ToList().ForEach(sr => { sr.CanTrade = false; });
      if (_strategyExecuteOnTradeClose != null) _strategyExecuteOnTradeClose(e.Trade);
    }

    private void RaisePositionsChanged() {
      OnPropertyChanged("PositionsSell");
      OnPropertyChanged("PositionsBuy");
      OnPropertyChanged("PipsPerPosition");
    }
    string _sessionInfo = "";
    public string SessionInfo {
      get {
        if (string.IsNullOrEmpty(_sessionInfo)) {
          var l = new List<string>();
          foreach(var p in GetType().GetProperties()){
            var ca = p.GetCustomAttributes(typeof(CategoryAttribute), false).FirstOrDefault() as CategoryAttribute;
            if (ca != null && ca.Category == categoryActive) {
              l.Add(p.Name + ":" + p.GetValue(this, null));
            }
          }
          _sessionInfo = string.Join(",", l);
        }
        return _sessionInfo;
      }
    }
    public void Replay(ReplayArguments args) {
      try {
        if (!IsInVitualTrading)
          UnSubscribeToTradeClosedEVent(TradesManager);
        SetPlayBackInfo(true, args.DateStart.GetValueOrDefault(), args.DelayInSeconds.FromSeconds());
        var framesBack = 3;
        var barsCountTotal = BarsCount * framesBack;
        var actionBlock = new ActionBlock<Action>(a=>a());
        Action<Order2GoAddIn.FXCoreWrapper.RateLoadingCallbackArgs<Rate>> cb= callBackArgs => PriceHistory.SaveTickCallBack(BarPeriodInt, Pair, o => Log = new Exception(o + ""), actionBlock, callBackArgs);
        var fw = GetFXWraper();
        if (fw != null)
          PriceHistory.AddTicks(fw, BarPeriodInt, Pair,args.DateStart.GetValueOrDefault(DateTime.Now.AddMinutes(-barsCountTotal * 2)), o => Log = new Exception(o + ""));
        //GetFXWraper().GetBarsBase<Rate>(Pair, BarPeriodInt, barsCountTotal, args.DateStart.GetValueOrDefault(TradesManagerStatic.FX_DATE_NOW), TradesManagerStatic.FX_DATE_NOW, new List<Rate>(), cb);
        var rates = args.DateStart.HasValue
          ? GlobalStorage.GetRateFromDB(Pair, args.DateStart.Value, int.MaxValue, BarPeriodInt)
          : GlobalStorage.GetRateFromDBBackward(Pair, RatesArraySafe.Last().StartDate, barsCountTotal, BarPeriodInt);
        if (args.MonthsToTest > 0)
          rates = rates.Where(r => r.StartDate <= args.DateStart.Value.AddMonths(args.MonthsToTest.ToInt())).ToList();
        #region Init stuff
        RatesInternal.Clear();
        _sessionInfo = "";
        CurrentLoss = MinimumGross = HistoryMaximumLot = 0;
        SuppRes.ToList().ForEach(sr => { sr.CanTrade = false; sr.TradesCount = 0; sr.CorridorDate = DateTime.MinValue; });
        CorridorStartDate = null;
        CorridorStats = null;
        DisposeOpenTradeByMASubject();
        _waveRates.Clear();
        _waveLast = new WaveLast();
        var currentPosition = -1;
        var indexCurrent = 0; 
        #endregion
        while (!args.MustStop && indexCurrent < rates.Count) {
          if (currentPosition > 0 && currentPosition != args.CurrentPosition) {
            var index = (args.CurrentPosition * (rates.Count - BarsCount) / 100.0 ).ToInt();
            RatesInternal.Clear();
            RatesInternal.AddRange(rates.Skip(index).Take(BarsCount-1));
          }
          Rate rate;
          if (args.StepBack) {
            args.InPause = true;
            rate = rates.Previous(RatesInternal[0]);
            if (rate != null) {
              RatesInternal.Insert(0, rate);
              RatesInternal.Remove(RatesInternal.Last());
              indexCurrent--;
            }
          } else {
            rate = rates[indexCurrent++];
            if (rate != null) RatesInternal.Add(rate);
            while (RatesInternal.Count > BarsCount)
              RatesInternal.RemoveAt(0);
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
              if (TradesManager.GetAccount().Equity < 25000)
                MessageBox.Show("Equity Alert!");
              Profitability = (TradesManager.GetAccount().Equity - 50000) / (RateLast.StartDate - args.DateStart.Value).TotalDays * 30;
            } else
              Log = new Exception("Replay:End");
            Thread.Sleep((args.DelayInSeconds - d.Elapsed.TotalSeconds).Max(0).FromSeconds());
            if (args.InPause) {
              args.StepBack = args.StepForward = false;
              Task.Factory.StartNew(() => {
                while (args.InPause && !args.StepBack && !args.StepForward && !args.MustStop)
                  Thread.Sleep(200);
              }).Wait();
            }
          }
        }
      } catch (Exception exc) {
        Log = exc;
      } finally {
        SetPlayBackInfo(false, args.DateStart.GetValueOrDefault(), args.DelayInSeconds.FromSeconds());
        DisposeOpenTradeByMASubject();
        args.MustStop = args.StepBack = args.StepBack = args.InPause = false;
        if (!IsInVitualTrading) {
          RatesInternal.Clear();
          SubscribeToTradeClosedEVent(_TradesManager);
          LoadRates();
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
          AddSuppRes(RatesArray.Average(r=>r.PriceAvg),isSupport);
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
    public void UpdateSuppRes(Guid uid, double rateNew) {
      var suppRes = SuppRes.ToArray().SingleOrDefault(sr => sr.UID == uid);
      if (suppRes == null)
        throw new InvalidOperationException("SuppRes UID:" + uid + " does not exist.");
      suppRes.Rate = rateNew;
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

    public double RatesStDevInPips { get { return InPips(RatesStDev); } }
    private double _RatesStDev = double.NaN;
    public double RatesStDev {
      get { return _RatesStDev; }
      set {
        if (_RatesStDev != value) {
          _RatesStDev = value;
          OnPropertyChanged(TradingMacroMetadata.RatesStDev);
          OnPropertyChanged(TradingMacroMetadata.RatesStDevInPips);
          OnPropertyChanged(TradingMacroMetadata.HeightToStDevRatio);
        }
      }
    }

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

    double LockPriceHigh(Rate rate) { return _stDevPriceLevelsHigh[StDevLevelLock](rate); }
    double LoadPriceHigh(Rate rate) { return _stDevPriceLevelsHigh[StDevLevelLoad](rate); }
    Func<Rate, double>[] _stDevPriceLevelsHigh = new Func<Rate, double>[] { 
      r => r.PriceAvg02, 
      r => r.PriceAvg2, 
      r => r.PriceAvg21 
    };
    double LockPriceLow(Rate rate) { return _stDevPriceLevelsLow[StDevLevelLock](rate); }
    double LoadPriceLow(Rate rate) { return _stDevPriceLevelsLow[StDevLevelLoad](rate); }
    Func<Rate, double>[] _stDevPriceLevelsLow = new Func<Rate, double>[] { 
      r => r.PriceAvg03, 
      r => r.PriceAvg3, 
      r => r.PriceAvg31 
    };


    [MethodImpl(MethodImplOptions.Synchronized)]
    void CalculateSuppResLevels() {

      if (IsSuppResManual || Strategy == Strategies.None) return;

      var levelsCount = SuppResLevelsCount;

      if (levelsCount >= 0) {
        #region Adjust SUppReses
        while (Resistances.Count() < levelsCount)
          AddResistance(0);
        while (Supports.Count() < levelsCount)
          AddSupport(0);

        while (Resistances.Count() > levelsCount)
          RemoveSuppRes(Resistances.Last());
        while (Supports.Count() > levelsCount)
          RemoveSuppRes(Supports.Last());
        #endregion
      }

      if (levelsCount == 0) return;

      var supportList = new LinkedList<SuppRes>(Supports);
      var support = supportList.First;
      var resistanceList = new LinkedList<SuppRes>(Resistances);
      var resistance = resistanceList.First;

      Func<LinkedListNode<SuppRes>> addSupportNode = () => supportList.AddLast(AddSupport(0));
      Func<LinkedListNode<SuppRes>> addResistanceNode = () => resistanceList.AddLast(AddResistance(0));

      if (levelsCount == 1 && Strategy.HasFlag(Strategies.Hot)) {
        if (resistance == null) resistance = addResistanceNode();
        if (support == null) support = addSupportNode();
        if (RateLast != null && RateLast.PriceAvg1 > 0) {
          try {
            support.Value.Rate = ReverseStrategy ? LockPriceLow(RateLast) : LockPriceHigh(RateLast);
          } catch {
            support.Value.Rate = ReverseStrategy ? LockPriceLow(RateLast) : LockPriceHigh(RateLast);
          }
          try {
            resistance.Value.Rate = ReverseStrategy ? LockPriceHigh(RateLast) : LockPriceLow(RateLast);
          } catch {
            resistance.Value.Rate = ReverseStrategy ? LockPriceHigh(RateLast) : LockPriceLow(RateLast);
          }
          return;
        } else {
          support.Value.Rate = resistance.Value.Rate = double.NaN;
          throw new InvalidDataException(Pair+": Last Rate is not proccesed.");
        }
      }
    }

    #region MagnetPrice
    private void SetMagnetPrice() {
      try {
        //var rates = RatesArray.Where(r => r.Volume > 0 && r.Spread > 0 && r.PriceStdDev>0).ToList();
        //MagnetPrice = rates.Sum(r => r.PriceAvg / r.Volume) / rates.Sum(r => 1.0 / r.Volume);
        //MagnetPrice = rates.Sum(r => r.PriceAvg * r.PriceStdDev * r.PriceStdDev) / rates.Sum(r => r.PriceStdDev * r.PriceStdDev);
        MagnetPrice = CorridorStats.Rates.Average(r => r.PriceAvg);
        //MagnetPrice = _levelCounts[0].Item2;
      } catch { }
    }
    private double _MagnetPrice;
    public double MagnetPrice {
      get { return _MagnetPrice; }
      set {
        if (_MagnetPrice != value) {
          _MagnetPrice = value;
          OnPropertyChanged("MagnetPrice");
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
        lock (_rateArrayLocker)
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
              RatesHeight = _rateArray.Height();//CorridorStats.priceHigh, CorridorStats.priceLow);
              PriceSpreadAverage = _rateArray.Select(r => r.PriceSpread).Average();//.ToList().AverageByIterations(2).Average();
              #endregion

              SpreadForCorridor = RatesArray.Spread();
              SetMA();
              _rateArray.ReverseIfNot().SetStDevPrices(GetPriceMA);
              RatesStDev = _rateArray.Max(r => r.PriceStdDev);
              StDevAverages.Clear();
              _rateArray.Select(r => r.PriceStdDev).ToList().AverageByIterations(StDevTresholdIterations, StDevAverages).Average();
              {
                Rate stDevRate = null;
                var stDev = 0;
                foreach(var r in _rateArray.ToArray().Reverse() ){
                  if (stDevRate == null && r.PriceStdDev <= StDevAverages[stDev])
                    continue;
                  if (stDevRate != null && r.PriceStdDev < stDevRate.PriceStdDev)
                    break;
                  stDevRate = r;
                }
                StDevAverages[stDev] = stDevRate.PriceStdDev;
              }
              var round = TradesManager.GetDigits(Pair);
              StDevAverage = StDevAverages[0];
              VolumeAverageHigh = _rateArray.Select(r => (double)r.Volume).ToList().AverageByIterations(VolumeTresholdIterations).Average();
              VolumeAverageLow = _rateArray.Select(r => (double)r.Volume).ToList().AverageByIterations(-VolumeTresholdIterations).Average();
              if (!IsInVitualTrading)
                SuppRes.ToList().ForEach(sr => sr.CrossesCount = GetCrossesCount(_rateArray, sr.Rate));
              OnScanCorridor(_rateArray);

              OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
            }
            return _rateArray;
          } catch (Exception exc) {
            Log = exc;
            return _rateArray;
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
    IEnumerable<Rate> GetRatesForChart(IEnumerable<Rate> rates) {
      return rates.Reverse().Take(BarsCount.Max(CorridorStats.Periods)).Reverse();
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

    private double _ProfitCounter;
    public double ProfitCounter {
      get { return _ProfitCounter; }
      set {
        if (_ProfitCounter != value) {
          _ProfitCounter = value;
          OnPropertyChanged("ProfitCounter");
        }
      }
    }


    public int _fibMin = 0;
    private double _AvarageLossInPips = double.NaN;
    public double AvarageLossInPips {
      get { return _AvarageLossInPips; }
      set {
        if (_AvarageLossInPips != value) {
          _AvarageLossInPips = value;
          OnPropertyChanged("AvarageLossInPips");
          //if (_fibMin == 0) _fibMin = FibMin.ToInt();
          //FibMin = Math.Max(_fibMin, _AvarageLossInPips);
        }
      }
    }

    Dictionary<string, Strategies> tradeStrategies = new Dictionary<string, Strategies>();
    Dictionary<Strategies, double[]> strategyScores = new Dictionary<Strategies, double[]>() { 
      { Strategies.Range, new double[]{initialScore,initialScore} }
      //,{ Strategies.Breakout, new double[]{initialScore,initialScore} },
      //,{ Strategies.Breakout, new double[]{initialScore,initialScore} },
      //{ Strategies.Brange, new double[]{initialScore,initialScore} } ,
      //{ Strategies.Correlation, new double[]{initialScore,initialScore} } 
    };
    public string StrategyScoresText {
      get {
        return string.Join(",", strategyScores.Where(sc => sc.Value.Sum() > 0).Select(sc =>
          string.Format("{3}:{0:n1}/{1:n1}={2:n1}", sc.Value[0], sc.Value[1], sc.Value[0] / (sc.Value[0] + sc.Value[1]) * 100, sc.Key))
          .ToArray());
      }
    }
    public double StrategyScore {
      get {
        return strategyScores[Strategy][0] / (strategyScores[Strategy][0] + (double)strategyScores[Strategy][1]);
      }
    }
    const int initialScore = 50;
    public void StrategyScoresReset() { strategyScores.Values.ToList().ForEach(ss => { ss[0] = ss[1] = initialScore; }); }
    public Trade LastTrade {
      get { return _lastTrade; }
      set {
        if (value == null) return;
        _lastTrade = value;
        return;
        if (value.Id == LastTrade.Id) {
          var id = LastTrade.Id + "";
          if (!string.IsNullOrWhiteSpace(id)) {
            Strategies tradeStrategy = tradeStrategies.ContainsKey(id) ? tradeStrategies[id] : Strategies.None;
            if (tradeStrategy != Strategies.None) {
              var strategyScore = strategyScores[tradeStrategy];
              if (strategyScores.ContainsKey(tradeStrategy)) {
                strategyScore[0] += (LastTrade.PL > 0 ? 1 : 0);
                strategyScore[1] += (LastTrade.PL > 0 ? 0 : 1);
                if (strategyScore.Min() > initialScore * 1.1) {
                  strategyScore[0] *= .9;
                  strategyScore[1] *= .9;
                }
              }
            }
          }
        } else {
          var strategy = Strategy & (Strategies.Breakout | Strategies.Range );
          tradeStrategies[value.Id + ""] = strategy;
          if (-LastTrade.PL > AvarageLossInPips / 10) AvarageLossInPips = Lib.Cma(AvarageLossInPips, 10, LastTrade.PL.Abs());

          ProfitCounter = CurrentLoss >= 0 ? 0 : ProfitCounter + (LastTrade.PL > 0 ? 1 : -1);
        }
        OnPropertyChanged("LastTrade");
        OnPropertyChanged("LastLotSize");
        OnPropertyChanged("StrategyScoresText");
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

    List<Trade> _trades = new List<Trade>();
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

    public double CorridorToRangeMinimumRatio { get { return 0; } }

    public static Strategies[] StrategiesToClose = new Strategies[] { Strategies.Brange };
    private Strategies _Strategy;
    [Category(categoryCorridor)]
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
    private double CalcSpreadForCorridor(ICollection<Rate> rates,int iterations = 3) {
      try {
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

    public double TradingDistanceInPips {
      get { return InPips(TradingDistance).Max(_tradingDistanceMax); }
    }
    public double TradingDistance {
      get {
        if (!HasRates) return double.NaN;
        return (GetValueByTakeProfitFunction(TradingDistanceFunction)).Max(PriceSpreadAverage.GetValueOrDefault(double.NaN) * 3);
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
      if (!HasRates || price.IsPlayback) return;
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
      return TradesManager.Round(Pair, price);
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
        var canDo = IsHotStrategy && HasCorridor && IsPriceSpreadOk;
        return canDo;
      }
    }
    private bool CanDoNetOrders {
      get {
        var canDo = IsHotStrategy && (HasCorridor || IsAutoStrategy);
        return canDo;
      }
    }

    private int EntryOrderAllowedLot(bool isBuy, double? takeProfitPips = null) {
      var lotByStats = (int)_tradingStatistics.AllowedLotMinimum;
      var lot = false && !_isInPipserMode && lotByStats > 0 ? lotByStats : AllowedLotSizeCore(Trades.IsBuy(isBuy), takeProfitPips);
      return lot + (TradesManager.IsHedged ? 0 : Trades.IsBuy(!isBuy).Lots());
    }


    static TradingMacro() {
    }

    void SetEntryOrdersBySuppResLevels() {
      if (TradesManager == null ) return;
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

    private Order2GoAddIn.FXCoreWrapper GetFXWraper(bool failTradesManager = true ) {
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
    void SetNetStopLimit( bool isBuy) {
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
            if(fw!=null)
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
            if (fw!=null && (RoundPrice(currentLimit) - LimitRate).Abs() > ps) {
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

    public class WaveInfo {
      public Rate Rate { get; set; }
      /// <summary>
      /// 1 - based
      /// </summary>
      public int Position { get; set; }
      public double Slope { get; set; }
      public double Direction { get { return Math.Sign(Slope); } }
      public WaveInfo(Rate rate,int position,double slope) {
        this.Rate = rate;
        this.Position = position;
        this.Slope = slope;
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
        if (list.Any() &&  wr.Direction == list.Last().Direction) {
          list[list.Count - 1] = wr;
        } else {
          var zeros = corridorRates.Take(wr.Position).Where(r => r.PriceStdDev == 0).ToList();
          list.Add(wr);
          i++;
        }
      }
      return list.Take(count).ToList();
    }
    private static WaveInfo GetWaveRate(IList<Rate> corridorRates, double spreadMinimum, Func<Rate,double> ratePrice, int startIndex = 0) {
      if (corridorRates.Count() - startIndex < 2) return null;
      var rates = corridorRates.Skip(startIndex).ToList().SetStDevPrice(ratePrice);
      var a = rates.Select((r, i) => new Tuple<Rate, int>(r, i+1)).ToList();
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
      var ratesOut =rates.Take(node.Value.Item2).OrderBy(r=>r.PriceAvg);
      var rateOut = slope > 0 ? ratesOut.Last() : ratesOut.First();
      var tupleOut = node.List.Single(n => n.Item1 == rateOut);
      return new WaveInfo(tupleOut.Item1, tupleOut.Item2 + startIndex, slope);
    }

    private IList<Rate> RatesForTakeProfit(Trade[] trades) {
      if (!HasRates) return new Rate[0];
      var lastTradeDate = trades.OrderByDescending(t => t.Time).Select(t=>t.Time).DefaultIfEmpty(RatesArraySafe[0].StartDate).First();//.Subtract(BarPeriodInt.FromMinutes());
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
        switch ((Strategy & ~Strategies.Auto) & ~Strategies.Hot) {
          case Strategies.LongWave:
            return StrategyEnterBreakout095;
          case Strategies.Breakout:
            return StrategyBreakout;
          case Strategies.Breakout_2Corrs:
            return StrategyBreakout2Corr;
          case Strategies.Range:
            return StrategyRange;
          case Strategies.Wave:
            return StrategyEnterWave01;
          case Strategies.None:
            return () => { };
        }
        switch (Strategy) {
          case Strategies.Breakout7:
            return StrategyEnterBreakout0712;
        }
        throw new NotSupportedException("Strategy " + Strategy + " is not supported.");
      }
    }
    void RunStrategy() { 
      StrategyAction();
      #region Trade Action
      Action<bool, Func<double, bool>, Func<double, bool>> openTrade = (isBuy, mastClose, canOpen) => {
        var suppReses = EnsureActiveSuppReses().OrderBy(sr => sr.TradesCount).ToList();
        var minTradeCount = suppReses.Min(sr => sr.TradesCount);
        foreach (var suppRes in EnsureActiveSuppReses(isBuy)) {
          var level = suppRes.Rate;
          if (mastClose(level)) {
            if (suppRes.CanTrade) {
              var srGroup = suppReses.Where(a => !a.IsGroupIdEmpty && a.GroupId == suppRes.GroupId).OrderBy(sr => sr.TradesCount).ToList();
              if (srGroup.Any()) {
                if (srGroup[0] == suppRes)
                  srGroup[1].TradesCount = suppRes.TradesCount - 1;
              } else if (suppRes.TradesCount == minTradeCount) {
                suppReses.IsBuy(!isBuy).Where(sr => sr.CanTrade)
                  .OrderBy(sr => (sr.Rate - suppRes.Rate).Abs()).Take(1).ToList()
                  .ForEach(sr => sr.TradesCount = suppRes.TradesCount - 1);
              }
            }
            var canTrade = suppRes.CanTrade && suppRes.TradesCount <= 0 && !HasTradesByDistance(isBuy);
            CheckPendingAction("OT", (pa) => {
              var lotClose = Trades.IsBuy(!isBuy).Lots();
              var lotOpen = canTrade && canOpen(level) ? AllowedLotSizeCore(Trades) : 0;
              var lot = lotClose + lotOpen;
              if (lot > 0) {
                pa();
                TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
              }
            });
          }
        }
      }; 
      #endregion
      #region Call Trade Action
      if (SuppRes.Any() && Strategy != Strategies.None && (RateLast.StartDate - RatePrev1.StartDate).TotalMinutes / BarPeriodInt <= 3) {
        double priceLast;
        double pricePrev;
        CalculatePriceLastAndPrev(out priceLast, out pricePrev);

        Func<double, bool> canBuy = level => (CurrentPrice.Ask - level).Abs() < SpreadForCorridor*2;
        Func<double, bool> mustCloseSell = level => priceLast > level && pricePrev <= level;
        openTrade(true, mustCloseSell, canBuy);

        Func<double, bool> canSell = level => (CurrentPrice.Bid - level).Abs() < SpreadForCorridor*2;
        Func<double, bool> mustCloseBuy = level => priceLast < level && pricePrev >= level;
        openTrade(false, mustCloseBuy, canSell);
      } 
      #endregion
    }

    private void OpenTradeWithReverse(bool isBuy) {
      CheckPendingAction("OT", (pa) => {
        var lotClose = Trades.IsBuy(!isBuy).Lots();
        var lotOpen = AllowedLotSizeCore(Trades);
        var lot = lotClose + lotOpen;
        if (lot > 0) {
          pa();
          TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
        }
      });
    }

    public void CalculatePriceLastAndPrev(out double priceLast, out double pricePrev) {
      priceLast = CalculateLastPrice(RatesArray.LastByCount(), r => r.PriceAvg);
      pricePrev = RatePrev1.PriceAvg;
    }

    bool _useTakeProfitMin = false;
    Action<Trade> _strategyExecuteOnTradeClose;
    Action _strategyExecuteOnTradeOpen;
    double _strategyAnglePrev = double.NaN;

    #region Old
    private void StrategyEnterBreakout034() {
      StrategyExitByGross();
      if (!IsInVitualTrading) return;

      var canTrade = (StDevAverage / RatesArray.Max(r => r.PriceStdDev)).Between(StDevAverateRatioMin, StDevAverateRatioMax);

      var stDev = CorridorStats.StDev;
      var rateLast = CorridorStats.Rates[0];
      var buyLevel = rateLast.PriceAvg21;
      var sellLevel = rateLast.PriceAvg31;
      var rs = Resistances.OrderBy(r => r.Rate).ToList();
      Enumerable.Range(0, rs.Count()).ToList().ForEach(r => rs[r].Rate = buyLevel + stDev * r);
      var ss = Supports.OrderByDescending(r => r.Rate).ToList();
      Enumerable.Range(0, ss.Count()).ToList().ForEach(r => ss[r].Rate = sellLevel - stDev * r);

      SuppRes.ToList().ForEach(sr => sr.CanTrade = canTrade);
    }
    private void StrategyEnterBreakout035() {
      StrategyExitByGross();
      if (!IsInVitualTrading) return;

      var canTrade = CorridorStats.StDev * 4 < RatesStDev;
      if (canTrade) {
        var stDev = CorridorStats.StDev;
        var rateLast = CorridorStats.Rates[0];
        var buyLevel = rateLast.PriceAvg21;
        var sellLevel = rateLast.PriceAvg31;
        var rs = Resistances.OrderBy(r => r.Rate).ToList();
        Enumerable.Range(0, rs.Count()).ToList().ForEach(r => rs[r].Rate = buyLevel + stDev * r);
        var ss = Supports.OrderByDescending(r => r.Rate).ToList();
        Enumerable.Range(0, ss.Count()).ToList().ForEach(r => ss[r].Rate = sellLevel - stDev * r);
        SuppRes.ToList().ForEach(sr => sr.CanTrade = true);
      }
    }
    private void StrategyEnterBreakout036() {
      if (!StrategyExitByGross())
        Trades.Where(t => t.PL > RatesHeightInPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      if (!IsInVitualTrading) return;

      var canTrade = (StDevAverage / RatesArray.Max(r => r.PriceStdDev)).Between(StDevAverateRatioMin, StDevAverateRatioMax);

      var stDev = CorridorStats.StDev;
      var rateLast = CorridorStats.Rates[0];
      var buyLevel = rateLast.PriceAvg21;
      var sellLevel = rateLast.PriceAvg31;
      var rs = Resistances.OrderBy(r => r.Rate).ToList();
      Enumerable.Range(0, rs.Count()).ToList().ForEach(r => rs[r].Rate = buyLevel + stDev * r);
      var ss = Supports.OrderByDescending(r => r.Rate).ToList();
      Enumerable.Range(0, ss.Count()).ToList().ForEach(r => ss[r].Rate = sellLevel - stDev * r);

      SuppRes.ToList().ForEach(sr => sr.CanTrade = canTrade);
    }
    private void StrategyEnterBreakout037() {
      StrategyExitByGross();
      if (!IsInVitualTrading) return;

      var bo = Strategy == Strategies.Breakout ? 1 : -1;
      var buyLevel = MagnetPrice + RatesArray[0].PriceStdDev * bo;
      var sellLevel = MagnetPrice - RatesArray[0].PriceStdDev * bo;
      var canTrade = (StDevAverage / RatesArray.Max(r => r.PriceStdDev)).Between(StDevAverateRatioMin, StDevAverateRatioMax);

      var stDev = CorridorStats.StDev;
      var rs = Resistances.OrderBy(r => r.Rate).ToList();
      Enumerable.Range(0, rs.Count()).ToList().ForEach(r => rs[r].Rate = buyLevel + stDev * r);
      var ss = Supports.OrderByDescending(r => r.Rate).ToList();
      Enumerable.Range(0, ss.Count()).ToList().ForEach(r => ss[r].Rate = sellLevel - stDev * r);

      SuppRes.ToList().ForEach(sr => sr.CanTrade = canTrade);
    }
    private void StrategyEnterBreakout038() {
      var stDev = RatesArray.Max(r => r.PriceStdDev);
      if (!StrategyExitByGross() && Trades.Any()) {
        var isBuy = Trades[0].IsBuy;
        var basePrice = isBuy ? CorridorStats.Rates.Min(r => r.PriceAvg) : CorridorStats.Rates.Max(r => r.PriceAvg);
        var priceDiff = isBuy ? RateLast.PriceAvg - basePrice : basePrice - RateLast.PriceAvg;
        if (priceDiff > stDev) TradesManager.ClosePair(Pair);
      }
      if (!IsInVitualTrading) return;

      var bo = Strategy == Strategies.Breakout ? 1 : -1;
      var buyLevel = MagnetPrice + stDev * bo;
      var sellLevel = MagnetPrice - stDev * bo;
      var canTrade = (StDevAverage / RatesArray.Max(r => r.PriceStdDev)).Between(StDevAverateRatioMin, StDevAverateRatioMax);

      var rs = Resistances.OrderBy(r => r.Rate).ToList();
      Enumerable.Range(0, rs.Count()).ToList().ForEach(r => rs[r].Rate = buyLevel + stDev * r);
      var ss = Supports.OrderByDescending(r => r.Rate).ToList();
      Enumerable.Range(0, ss.Count()).ToList().ForEach(r => ss[r].Rate = sellLevel - stDev * r);

      SuppRes.ToList().ForEach(sr => sr.CanTrade = canTrade);
    }
    private void StrategyEnterBreakout039() {
      if (_strategyExecuteOnTradeClose == null) {
        _strategyExecuteOnTradeClose = (t) => _useTakeProfitMin = false;
        _strategyExecuteOnTradeOpen = () => _useTakeProfitMin = true;
      }
      var stDev = RatesArray.Max(r => r.PriceStdDev);
      if (!StrategyExitByGross())
        Trades.Where(t => t.PL >= TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      if (!IsInVitualTrading) return;

      var stDevMax = RatesArray.Max(r => r.PriceStdDev).Max(CorridorStats.StDev * 4);
      var bl = CorridorStats.Rates.Min(r => r.PriceAvg) + stDevMax;
      var sl = CorridorStats.Rates.Max(r => r.PriceAvg) - stDevMax;
      var isBO = Strategy == Strategies.Breakout;
      var buyLevel = isBO ? bl : sl;
      var sellLevel = isBO ? sl : bl;

      ResistanceHigh().Rate = buyLevel;
      SupportLow().Rate = sellLevel;
      ResistanceHigh().CanTrade = SupportLow().CanTrade = true;

      if (SuppResLevelsCount == 2) {
        SupportHigh().Rate = buyLevel;
        SupportHigh().CanTrade = false;
      }
      if (SuppResLevelsCount == 2) {
        ResistanceLow().Rate = sellLevel;
        ResistanceLow().CanTrade = false;
      }
    }
    private void StrategyEnterBreakout040() {
      if (_strategyExecuteOnTradeClose == null) {
        _strategyExecuteOnTradeClose = (t) => _useTakeProfitMin = false;
        _strategyExecuteOnTradeOpen = () => _useTakeProfitMin = true;
      }
      if (!StrategyExitByGross())
        Trades.Where(t => t.PL >= TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      if (!IsInVitualTrading) return;

      var rates = CorridorStats.Rates;
      var ratesMin = rates.Min(r => r.PriceAvg);
      var stDevAverage = rates.Select(r => r.PriceStdDev).ToList().AverageByIterations(StDevTresholdIterations).Average();
      var stDevRatio = stDevAverage / rates.Max(r => r.PriceStdDev);

      var corrMin = CorridorStats.Rates.Min(r => r.PriceAvg);
      var corrHeight = CorridorStats.RatesHeight;
      var bl = corrMin + corrHeight * stDevRatio;
      var sl = corrMin + corrHeight * (1 - stDevRatio);
      var isBO = Strategy == Strategies.Breakout;
      var buyLevel = isBO ? bl : sl;
      var sellLevel = isBO ? sl : bl;

      ResistanceHigh().Rate = buyLevel;
      SupportLow().Rate = sellLevel;
      ResistanceHigh().CanTrade = SupportLow().CanTrade = true;

      if (SuppResLevelsCount == 2) {
        SupportHigh().Rate = buyLevel;
        SupportHigh().CanTrade = false;
      }
      if (SuppResLevelsCount == 2) {
        ResistanceLow().Rate = sellLevel;
        ResistanceLow().CanTrade = false;
      }
    }
    private void StrategyEnterBreakout041() {
      if (_strategyExecuteOnTradeClose == null) {
        #region Init SuppReses
        SuppResLevelsCount = 2;
        ResistanceHigh().GroupId = ResistanceLow().GroupId = Guid.NewGuid();
        SupportHigh().GroupId = SupportLow().GroupId = Guid.NewGuid();

        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;

          ResistanceHigh().TradesCount = 1;
          ResistanceLow().TradesCount = 2;

          SupportLow().TradesCount = 1;
          SupportHigh().TradesCount = 2;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      if (!StrategyExitByGross())
        Trades.Where(t => t.PL >= TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      if (!IsInVitualTrading) return;

      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(2).OrderBy(r => r.PriceAvg).ToList();
      var buyRate = rates2.LastByCount();
      var buyLevel = buyRate.PriceAvg;
      var sellRate = rates2[0];
      var sellLevel = sellRate.PriceAvg;

      var isInSell = sellRate > buyRate;
      var sellLevel1 = isInSell
        ? (rates.TakeWhile(r => r > sellRate).Max(r => r.PriceAvg) - SpreadForCorridor).Max(sellLevel + SpreadForCorridor * 3)
        : sellLevel + StDevAverage;
      var buyLevel1 = !isInSell
        ? (rates.TakeWhile(r => r > buyRate).Min(r => r.PriceAvg) + SpreadForCorridor).Min(buyLevel - SpreadForCorridor * 3)
        : buyLevel - StDevAverage;

      ResistanceHigh().Rate = buyLevel;
      ResistanceLow().Rate = buyLevel1;
      SupportLow().Rate = sellLevel;
      SupportHigh().Rate = sellLevel1;

      ResistanceHigh().CanTrade = SupportLow().CanTrade = false;
      ResistanceLow().CanTrade = SupportHigh().CanTrade = true;
    } 
    private void StrategyEnterBreakout042() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();
      if (!IsInVitualTrading) return;

      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(2).OrderBy(r => r.PriceAvg).ToList();
      var buyRate = rates2.LastByCount();
      var buyLevel = buyRate.PriceAvg;
      var sellRate = rates2[0];
      var sellLevel = sellRate.PriceAvg;

      ResistanceHigh().Rate = buyLevel + SpreadForCorridor;
      SupportLow().Rate = sellLevel - SpreadForCorridor;

      ResistanceHigh().CanTrade = SupportLow().CanTrade = true;
    }
    private void StrategyEnterBreakout0421() {// 0.6
      if (_strategyExecuteOnTradeClose == null) {
        #region Init SuppReses
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      StrategyExitByGross042();
      if (!IsInVitualTrading) return;

      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var buyRate = rates2.LastByCount();
      var buyLevel = buyRate.PriceAvg;
      var sellRate = rates2[0];
      var sellLevel = sellRate.PriceAvg;

      SupportHigh().Rate = buyLevel;// + SpreadForCorridor/2;
      ResistanceLow().Rate = sellLevel;// - SpreadForCorridor/2;

      ResistanceHigh().Rate = MagnetPrice + CorridorStats.StDev;
      SupportLow().Rate = MagnetPrice - CorridorStats.StDev;

      ResistanceLow().CanTrade = SupportHigh().CanTrade = false;
      ResistanceHigh().CanTrade = SupportLow().CanTrade = true;
    }
    private void StrategyEnterBreakout0422() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();
      if (!IsInVitualTrading) return;

      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(1);
      var buyRate = rates2.LastByCount();
      var buyCloseLevel = buyRate.PriceAvg - extreamOffset;
      var sellRate = rates2[0];
      var sellCloseLevel = sellRate.PriceAvg + extreamOffset;
      var buyLevel = MagnetPrice + CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var sellLevel = MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();

      SupportHigh().Rate = buyCloseLevel.Max((netOpen == 0 ? buyLevel : netOpen) - InPoints(this.CurrentLossInPips * .9));// + SpreadForCorridor/2;
      ResistanceLow().Rate = sellCloseLevel.Min((netOpen == 0 ? sellLevel : netOpen) + InPoints(this.CurrentLossInPips * .9));// - SpreadForCorridor/2;
      ResistanceLow().CanTrade = SupportHigh().CanTrade = false;


      var canBuy = SupportHigh().Rate - buyLevel > buyLevel - sellLevel;
      ResistanceHigh().Rate = buyLevel;
      ResistanceHigh().CanTrade = canBuy;

      var canSell = sellLevel - ResistanceLow().Rate > buyLevel - sellLevel;
      SupportLow().Rate = sellLevel;
      SupportLow().CanTrade = canSell;

    }
    private void StrategyEnterBreakout0423() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();

      var stDevAverageBig = _rateArray.Select(r => r.PriceStdDev).ToList().AverageByIterations(StDevTresholdIterations+1).Average();

      var ratesBig = RatesArray.SkipWhile(r => r.PriceStdDev > stDevAverageBig * StDevAverageLeewayRatio).OrderBy(r=>r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(1);
      var buyRate = ratesBig.LastByCount();
      var buyCloseLevel = buyRate.PriceAvg - extreamOffset;
      var sellRate = ratesBig[0];
      var sellCloseLevel = sellRate.PriceAvg + extreamOffset;
      var buyLevel = rates2.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellLevel = rates2[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();


      #region Close Levels
      var bcl = buyCloseLevel.Max((netOpen == 0 ? buyLevel : netOpen) - InPoints(this.CurrentLossInPips * .9));// + SpreadForCorridor/2;
      if (IsInVitualTrading || bcl > Support0().Rate)
        Support0().Rate = bcl;

      var scl = sellCloseLevel.Min((netOpen == 0 ? sellLevel : netOpen) + InPoints(this.CurrentLossInPips * .9));// - SpreadForCorridor/2;
      if (IsInVitualTrading || scl < Resistance0().Rate)
        Resistance0().Rate = scl;
      
      Resistance0().CanTrade = Support0().CanTrade = false; 
      #endregion

      if (CorridorStats.Spread > SpreadForCorridor && (CorridorAngle.Sign() != _strategyAnglePrev.Sign() || CorridorAngle.Abs().Round(0) <= 3) && IsInVitualTrading) {
        _strategyAnglePrev = CorridorAngle;
        Resistance1().Rate = buyLevel;
        Resistance1().CanTrade = true;

        Support1().Rate = sellLevel;
        Support1().CanTrade = true;
      }

    }
    private void StrategyEnterBreakout04231() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();

      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevAverageBig = _rateArray.Select(r => r.PriceStdDev).ToList().AverageByIterations(StDevTresholdIterations + 1).Average();
      var ratesBig = RatesArray.SkipWhile(r => r.PriceStdDev > stDevAverageBig * StDevAverageLeewayRatio).OrderBy(r => r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(1);
      var buyRate = ratesBig.LastByCount();
      var buyClosePrice = buyRate.PriceAvg - extreamOffset;
      var sellRate = ratesBig[0];
      var sellClosePrice = sellRate.PriceAvg + extreamOffset;
      var buyPrice = rates2.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellPrice = rates2[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();


      #region Suppres levels
      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion


      if (CorridorStats.Spread > SpreadForCorridor && (CorridorAngle.Sign() != _strategyAnglePrev.Sign() || CorridorAngle.Abs().Round(0) <= 3) && isAuto) {
        _strategyAnglePrev = CorridorAngle;
        buyLevel.Rate = buyPrice;
        sellLevel.Rate = sellPrice;
        buyLevel.CanTrade = sellLevel.CanTrade = true;
      }

      #region Close Levels
      var plAgjustment = InPoints(this.CurrentLossInPips.Min(0) * CurrentLossInPipsCloseAdjustment - TakeProfitPips);
      var bcl = buyClosePrice.Max(buyNetOpen - plAgjustment);// + SpreadForCorridor/2;
      if (isAuto || bcl > buyCloseLevel.Rate)
        buyCloseLevel.Rate = bcl;

      var scl = sellClosePrice.Min(sellNetOpen + plAgjustment);// - SpreadForCorridor/2;
      if (isAuto || scl < sellCloseLevel.Rate)
        sellCloseLevel.Rate = scl;

      buyCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      #endregion

    }
    private void StrategyEnterBreakout04232() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            ResistanceHigh().TradesCount = 0;
            SupportLow().TradesCount = 0;
          }
          SuppRes.ToList().ForEach(sr => sr.CanTrade = false);
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross04232();

      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var ratesBig = CorridorsRates[1].OrderBy(r => r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot().Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(1);
      var buyRate = ratesBig.LastByCount();
      var buyClosePrice = buyRate.PriceAvg - extreamOffset;
      var sellRate = ratesBig[0];
      var sellClosePrice = sellRate.PriceAvg + extreamOffset;
      var buyPrice = rates.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellPrice = rates[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();


      #region Suppres levels
      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var stDevOk = CorridorBigToSmallRatio > 0 ? StDevAverages[1] / StDevAverages[0] > CorridorBigToSmallRatio : StDevAverages[1] / StDevAverages[0] < CorridorBigToSmallRatio.Abs();
      var angleOk =  /*CorridorAngle.Sign() != _strategyAnglePrev.Sign();// ||*/ CorridorAngle.Abs().Round(0) <= this.TradingAngleRange;
      _strategyAnglePrev = CorridorAngle;
      var tradeHeightOk = false;// buyLevel.CanTrade && (buyPrice - sellPrice) < (buyLevel.Rate - sellLevel.Rate);
      if ((stDevOk && angleOk || tradeHeightOk) && isAuto) {
        buyLevel.Rate = buyPrice;
        sellLevel.Rate = sellPrice;
        buyLevel.CanTrade = sellLevel.CanTrade = true;
      }

      #region Close Levels
      var plAgjustment = InPoints(this.CurrentLossInPips.Min(0) * CurrentLossInPipsCloseAdjustment - TakeProfitPips);
      var bcl = buyClosePrice.Max(buyNetOpen - plAgjustment);// + SpreadForCorridor/2;
      if (isAuto || bcl > buyCloseLevel.Rate)
        buyCloseLevel.Rate = bcl;

      var scl = sellClosePrice.Min(sellNetOpen + plAgjustment);// - SpreadForCorridor/2;
      if (isAuto || scl < sellCloseLevel.Rate)
        sellCloseLevel.Rate = scl;

      buyCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      #endregion

    }

    bool _waitingForAngle = false;
    private void StrategyEnterBreakout04233() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            ResistanceHigh().TradesCount = 0;
            SupportLow().TradesCount = 0;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross04232();

      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var ratesBig = CorridorsRates[1].OrderBy(r => r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot().Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(1);
      var buyRate = ratesBig.LastByCount();
      var buyClosePrice = buyRate.PriceAvg - extreamOffset;
      var sellRate = ratesBig[0];
      var sellClosePrice = sellRate.PriceAvg + extreamOffset;
      var buyPrice = rates.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellPrice = rates[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();


      #region Suppres levels
      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var corridorOk = rates[0].PriceAvg == ratesBig[0].PriceAvg || ratesBig.LastByCount().PriceAvg == rates.LastByCount().PriceAvg;
      var stDevOk = CorridorBigToSmallRatio > 0 ? StDevAverages[2] / StDevAverages[1] > CorridorBigToSmallRatio : StDevAverages[2] / StDevAverages[1] < CorridorBigToSmallRatio.Abs();
      if (stDevOk) {
        var s = (RatesArray.Max(r => r.PriceStdDev) - RatesArray.Min(r => r.PriceStdDev)) / 2;
        stDevOk = StDevAverages[1] >= s;
      }
      var angleOk =  /*CorridorAngle.Sign() != _strategyAnglePrev.Sign();// ||*/ CorridorAngle.Abs().Round(0) <= this.TradingAngleRange;
      _strategyAnglePrev = CorridorAngle;
      var tradeHeightOk =  buyLevel.CanTrade && (buyPrice - sellPrice) > (buyLevel.Rate - sellLevel.Rate);
      if ((corridorOk && stDevOk && angleOk || tradeHeightOk) && isAuto) {
        buyLevel.Rate = buyPrice;
        sellLevel.Rate = sellPrice;
        buyLevel.CanTrade = sellLevel.CanTrade = true;
      }
      if(StDevAverages[1] / StDevAverages[0]< 1.5)
        buyLevel.CanTrade = sellLevel.CanTrade = false;

      #region Close Levels
      var plAgjustment = InPoints(this.CurrentLossInPips.Min(0) * CurrentLossInPipsCloseAdjustment - TakeProfitPips);
      var bcl = buyClosePrice.Max(buyNetOpen - plAgjustment);// + SpreadForCorridor/2;
      if (isAuto || bcl > buyCloseLevel.Rate)
        buyCloseLevel.Rate = bcl;

      var scl = sellClosePrice.Min(sellNetOpen + plAgjustment);// - SpreadForCorridor/2;
      if (isAuto || scl < sellCloseLevel.Rate)
        sellCloseLevel.Rate = scl;

      buyCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      #endregion

    }
    private void StrategyEnterBreakout0424() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();

      var stDevAverageBig = StDevAverages[1];

      var ratesBig = CorridorsRates.Last().OrderBy(r=>r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(ExtreamCloseOffset);
      var buyCloseLevel = ratesBig.LastByCount().PriceAvg - extreamOffset;
      var sellCloseLevel = ratesBig[0].PriceAvg + extreamOffset;
      var heightClose = buyCloseLevel - sellCloseLevel;
      var buyLevel = rates2.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellLevel = rates2[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var heightOpen = buyLevel - sellLevel;
      var netOpen = Trades.NetOpen();


      #region Close Levels
      var bcl = buyCloseLevel.Max((netOpen == 0 ? buyLevel : netOpen) - InPoints(CurrentLossInPips.Min(CurrentGrossInPips) * CurrentLossInPipsCloseAdjustment));// + SpreadForCorridor/2;
      if (IsInVitualTrading || bcl > Support0().Rate)
        Support0().Rate = bcl;

      var scl = sellCloseLevel.Min((netOpen == 0 ? sellLevel : netOpen) + InPoints(CurrentLossInPips.Min(CurrentGrossInPips) * CurrentLossInPipsCloseAdjustment));// - SpreadForCorridor/2;
      if (IsInVitualTrading || scl < Resistance0().Rate)
        Resistance0().Rate = scl;

      Resistance0().CanTrade = Support0().CanTrade = false;
      #endregion

      if (StDevAverages[1]>StDevAverages[0]*2 && CorridorAngle.Sign() != _strategyAnglePrev.Sign() && IsInVitualTrading) {
        _strategyAnglePrev = CorridorAngle;
        Resistance1().Rate = buyLevel;
        Resistance1().CanTrade = true;

        Support1().Rate = sellLevel;
        Support1().CanTrade = true;
      }

    }
    private void StrategyEnterBreakout0425() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();

      var stDevAverageBig = _rateArray.Select(r => r.PriceStdDev).ToList().AverageByIterations(StDevTresholdIterations + 1).Average();

      var ratesBig = RatesArray.SkipWhile(r => r.PriceStdDev > stDevAverageBig * StDevAverageLeewayRatio).OrderBy(r => r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(1);
      var buyRate = ratesBig.LastByCount();
      var buyCloseLevel = buyRate.PriceAvg - extreamOffset;
      var sellRate = ratesBig[0];
      var sellCloseLevel = sellRate.PriceAvg + extreamOffset;
      var buyLevel = rates2.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellLevel = rates2[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();


      #region Close Levels
      var bcl = buyCloseLevel.Max((netOpen == 0 ? buyLevel : netOpen) - InPoints(this.CurrentLossInPips * .9));// + SpreadForCorridor/2;
      if (IsInVitualTrading || bcl > Support0().Rate)
        Support0().Rate = bcl;

      var scl = sellCloseLevel.Min((netOpen == 0 ? sellLevel : netOpen) + InPoints(this.CurrentLossInPips * .9));// - SpreadForCorridor/2;
      if (IsInVitualTrading || scl < Resistance0().Rate)
        Resistance0().Rate = scl;

      Resistance0().CanTrade = Support0().CanTrade = false;
      #endregion

      var isSpreadOk = CorridorStats.Spread > SpreadForCorridor;
      var isAngleOk = (CorridorAngle.Sign() != _strategyAnglePrev.Sign() || CorridorAngle.Abs().Round(0) <= 3);
      var isHeightOk = buyLevel - sellLevel < SpreadForCorridor * 4;
      if ( isHeightOk && isSpreadOk && isAngleOk && IsInVitualTrading) {
        _strategyAnglePrev = CorridorAngle;
        Resistance1().Rate = buyLevel;
        Resistance1().CanTrade = true;

        Support1().Rate = sellLevel;
        Support1().CanTrade = true;
      }

    }
    private void StrategyEnterBreakout0426() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          SuppRes.ToList().ForEach(sr => sr.TradesCount = 0);
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();

      var stDevAverageBig = _rateArray.Select(r => r.PriceStdDev).ToList().AverageByIterations(StDevTresholdIterations + 1).Average();
      var ratesAverage = RatesArray.Average(r => r.PriceAvg);
      var up = CorridorAngle > 0;// MagnetPrice > ratesAverage;

      var ratesBig = RatesArray.SkipWhile(r => r.PriceStdDev > stDevAverageBig * StDevAverageLeewayRatio).OrderBy(r => r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = SpreadForCorridor;
      var buyRate = ratesBig.LastByCount();
      var buyClosePrice = buyRate.PriceAvg - InPoints(1);
      var sellRate = ratesBig[0];
      var sellClosePrice = sellRate.PriceAvg + InPoints(1);
      var buyPrice = rates2.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellPrice = rates2[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();

      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);

      if (up) {
        buyPrice = ratesBig.LastByCount().PriceAvg + extreamOffset;
        sellPrice = ratesBig.LastByCount().PriceAvg - extreamOffset;
      } else {
        buyPrice = ratesBig[0].PriceAvg + extreamOffset;
        sellPrice = ratesBig[0].PriceAvg - extreamOffset;

      }

      var isSpreadOk = CorridorStats.Spread > SpreadForCorridor;
      var isAngleOk = CorridorAngle.Sign() != _strategyAnglePrev.Sign();// || CorridorAngle.Abs().Round(0) <= 3);
      if (isSpreadOk && isAngleOk && IsInVitualTrading) {
        _strategyAnglePrev = CorridorAngle;
        buyLevel.Rate = buyPrice;
        sellLevel.Rate = sellPrice;
        buyLevel.CanTrade = sellLevel.CanTrade = true;
      }

      var buyCloseByLoss = (buyLevel.Rate + InPoints(TakeProfitPips)).Max(buyNetOpen - InPoints(this.CurrentLossInPips * CurrentLossInPipsCloseAdjustment));
      var sellCloseByLoss = (sellLevel.Rate - InPoints(TakeProfitPips)).Min(sellNetOpen + InPoints(this.CurrentLossInPips * CurrentLossInPipsCloseAdjustment));
      buyCloseLevel.Rate = buyCloseByLoss.Max(buyClosePrice);
      sellCloseLevel.Rate = sellCloseByLoss.Min(sellClosePrice);
      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
    }
    private void StrategyEnterBreakout048() {// 0.6
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          SuppRes.ToList().ForEach(sr => sr.TradesCount = 0);
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross042();

      var rates = CorridorStats.Rates.ReverseIfNot();
      var rates2 = rates.Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(ExtreamCloseOffset);
      var buyRate = rates2.LastByCount();
      var buyClosePrice = buyRate.PriceAvg - InPoints(1);
      var sellRate = rates2[0];
      var sellClosePrice = sellRate.PriceAvg + InPoints(1);
      var buyPrice = rates2.LastByCount().PriceAvg + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellPrice = rates2[0].PriceAvg - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen();

      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);

      buyLevel.Rate = buyPrice;
      sellLevel.Rate = sellPrice;

      buyCloseLevel.Rate = buyClosePrice;
      sellCloseLevel.Rate = sellClosePrice;

      var canOpen = CorridorStats.Spread > SpreadForCorridor;
      if (canOpen) {
        buyLevel.CanTrade = sellLevel.CanTrade = canOpen;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = canOpen;
      }
    }
    private void StrategyEnterBreakout043() {
      if (_strategyExecuteOnTradeClose == null) {
        #region Init SuppReses
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      StrategyExitByGross042();
      if (!IsInVitualTrading) return;

      var canBuy = CorridorAngle > 0;
      var tradeLevel = CorridorStats.Rates[0].PriceAvg1;
      var buyLevel = canBuy ? tradeLevel : MagnetPrice + InPoints(200);
      var sellLevel = !canBuy ? tradeLevel : MagnetPrice - InPoints(200);

      ResistanceHigh().Rate = buyLevel;
      SupportLow().Rate = sellLevel;

      ResistanceHigh().CanTrade = SupportLow().CanTrade = true;
    }
    private void StrategyEnterBreakout044() {
      Price.ClosePriceMode = ClosePriceMode.HighLow;
      if (_strategyExecuteOnTradeClose == null) {
        #region Init SuppReses
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      StrategyExitByGross042();
      //if (!IsInVitualTrading) return;

      var canBuy = CorridorAngle > 0;
      var tradeLevel = CorridorStats.Rates[0].PriceAvg1;
      var buyLevel = tradeLevel + SpreadForCorridor; //-StDevAverage;
      var sellLevel = tradeLevel - SpreadForCorridor; //+StDevAverage;

      ResistanceHigh().Rate = buyLevel;
      SupportLow().Rate = sellLevel;

      ResistanceHigh().CanTrade = true;
      SupportLow().CanTrade = true;
    }
    private void StrategyEnterBreakout0441() {
      Price.ClosePriceMode = ClosePriceMode.HighLow;
      if (_strategyExecuteOnTradeClose == null) {
        #region Init SuppReses
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      if (!StrategyExitByGross042() && !RateLast.Volume.Between(VolumeAverageLow, VolumeAverageHigh) && Trades.Any()) {
        var lot = 0.Max(Trades.Lots() - AllowedLotSizeCore(Trades));
        if (lot > 0)
          TradesManager.ClosePair(Pair, Trades[0].IsBuy, lot);
      }
      if (this.IsHotStrategy) return;

      var canBuy = CorridorAngle > 0;
      var tradeLevel = CorridorStats.Rates[0].PriceAvg1;
      var buyLevel = tradeLevel + SpreadForCorridor; //-StDevAverage;
      var sellLevel = tradeLevel - SpreadForCorridor; //+StDevAverage;

      ResistanceHigh().Rate = buyLevel;
      SupportLow().Rate = sellLevel;

      var canTradeByTime = TradesManager.ServerTime.Hour.Between(5, 15);
      ResistanceHigh().CanTrade = canTradeByTime;
      SupportLow().CanTrade = canTradeByTime;
    }
    private void StrategyEnterBreakout045() {
      Price.ClosePriceMode = ClosePriceMode.HighLow;
      if (_strategyExecuteOnTradeClose == null) {
        #region Init SuppReses
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      StrategyExitByGross042();
      //if (!IsInVitualTrading) return;

      var canBuy = CorridorAngle > 0;
      var tradeLevel = CorridorStats.Rates[0].PriceAvg1;
      var offset = CorridorStats.StDev;
      var buyLevel = tradeLevel + offset; //-StDevAverage;
      var sellLevel = tradeLevel - offset; //+StDevAverage;

      ResistanceHigh().Rate = buyLevel;
      SupportLow().Rate = sellLevel;

      ResistanceHigh().CanTrade = true;
      SupportLow().CanTrade = true;
    }
    private void StrategyEnterBreakout046() {
      Price.ClosePriceMode = ClosePriceMode.HighLow;
      if (_strategyExecuteOnTradeClose == null) {
        #region Init SuppReses
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      StrategyExitByGross042();
      //if (!IsInVitualTrading) return;
      var rates = CorridorStats.Rates.Skip(2);
      Func<double> levelHigh = () => rates.Max(this.CorridorCrossGetHighPrice());
      Func<double> levelLow = () => rates.Min(this.CorridorCrossGetLowPrice());
      var canBuy = CorridorAngle > 0;
      var tradeLevel = CorridorStats.Rates[0].PriceAvg1;
      var offset = CorridorStats.StDev;
      var buyLevel = canBuy ? levelHigh() : tradeLevel + offset; //-StDevAverage;
      var sellLevel = !canBuy ? levelLow() : tradeLevel - offset; //+StDevAverage;

      ResistanceHigh().Rate = tradeLevel + InPoints(1);
      SupportLow().Rate = tradeLevel - InPoints(1);

      ResistanceHigh().CanTrade = true;
      SupportLow().CanTrade = true;
    }
    private void StrategyEnterBreakout047() {
      Action setLevels = () => {
        var rates = CorridorStats.Rates.Skip(2);
        var levelHigh = rates.Max(this.CorridorCrossGetHighPrice());
        var levelLow = rates.Min(this.CorridorCrossGetLowPrice());
        var tradeLevel = CorridorStats.Rates[0].PriceAvg1;
        var offset = SpreadForCorridor;
        var buyLevel = levelHigh;
        var sellLevel = levelLow;

        ResistanceHigh().Rate = levelHigh + offset / 2;
        SupportHigh().Rate = levelHigh - offset / 2;

        ResistanceLow().Rate = levelLow + offset / 2;
        SupportLow().Rate = levelLow - offset / 2;
      };
      Price.ClosePriceMode = ClosePriceMode.HighLow;
      if (_strategyExecuteOnTradeClose == null) {
        _corridorDirectionChanged += (s, e) => setLevels();
        #region Init SuppReses
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          _useTakeProfitMin = false;
          ResistanceHigh().TradesCount = 0;
          SupportLow().TradesCount = 0;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
        #endregion
      }
      StrategyExitByGross042();
      if (!IsInVitualTrading) return;
      if (CorridorAngle.Abs() < 4)
        setLevels();
      SuppRes.ToList().ForEach(sr => sr.CanTrade = true);
    }
    #endregion

    #region New
    private void StrategyEnterBreakout050() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            ResistanceHigh().TradesCount = 0;
            SupportLow().TradesCount = 0;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross04232();

      #region Set Levels
      var up = CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var ratesBig = CorridorsRates[1].OrderBy(r => r.PriceAvg).ToList();
      var rates = CorridorStats.Rates.ReverseIfNot().Skip(1).OrderBy(r => r.PriceAvg).ToList();
      var extreamOffset = InPoints(1);
      var tradeRate = up ? ratesBig.LastByCount().PriceAvg.Max(rates.LastByCount().PriceAvg) : ratesBig[0].PriceAvg.Min(rates[0].PriceAvg);
      var buyRate = ratesBig.LastByCount();
      var buyClosePrice = buyRate.PriceAvg - extreamOffset;
      var sellRate = ratesBig[0];
      var sellClosePrice = sellRate.PriceAvg + extreamOffset;
      var buyPrice = tradeRate + extreamOffset;//.Min(SpreadForCorridor * 2);
      var sellPrice = tradeRate - extreamOffset;// MagnetPrice - CorridorStats.StDev;//.Min(SpreadForCorridor * 2);
      var netOpen = Trades.NetOpen(); 
      #endregion

      #region Suppres levels
      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      if ( _strategyAnglePrev.Abs().Max(13) * 1 < CorridorAngle.Abs() && CorridorAngle.Abs() >=70)
        _waitingForAngle = true;
      _strategyAnglePrev = CorridorAngle;
      var angleOk =  _waitingForAngle && CorridorAngle.Abs().Round(0) <= this.TradingAngleRange;
      var tradeHeightOk = buyLevel.CanTrade && (buyPrice - sellPrice) > (buyLevel.Rate - sellLevel.Rate);
      if ((angleOk || tradeHeightOk) && isAuto) {
        buyLevel.Rate = buyPrice;
        sellLevel.Rate = sellPrice;
        buyLevel.CanTrade = sellLevel.CanTrade = true;
        _waitingForAngle = false;
        //if (angleOk) OpenTradeWithReverse(!up);
      }

      #region Close Levels
      var plAgjustment = InPoints(this.CurrentLossInPips.Min(0) * CurrentLossInPipsCloseAdjustment - TakeProfitPips);
      var bcl = buyClosePrice.Max(buyNetOpen - plAgjustment);// + SpreadForCorridor/2;
      if (isAuto || bcl > buyCloseLevel.Rate)
        buyCloseLevel.Rate = bcl;

      var scl = sellClosePrice.Min(sellNetOpen + plAgjustment);// - SpreadForCorridor/2;
      if (isAuto || scl < sellCloseLevel.Rate)
        sellCloseLevel.Rate = scl;

      buyCloseLevel.CanTrade = buyCloseLevel.CanTrade = false;
      #endregion

      if (!IsInVitualTrading)
        this.RaiseShowChart();

    }
    private void StrategyEnterBreakout060() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 0;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross060();

      #region Set Levels
      var up = CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      #endregion

      var angleOk = (_strategyAnglePrev.Abs() - CorridorAngle.Abs()).Abs() > TradingAngleRange && CorridorAngle.Abs() > TradingAngleRange;
      _strategyAnglePrev = CorridorAngle;
      if (angleOk && isAuto && !HasTradesByDistance(up)) {
        OpenTradeWithReverse(up);
      }
    }
    private void StrategyEnterBreakout061() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 0;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion
      StrategyExitByGross060();

      #region Set Levels
      var up = CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      #endregion

      var angleOk = (_strategyAnglePrev.Abs() - CorridorAngle.Abs()).Abs() > TradingAngleRange
        && CorridorAngle.Abs().Round(0) > TradingAngleRange
        && _strategyAnglePrev.Abs().Round(0) < 5;
      _strategyAnglePrev = CorridorAngle;
      if (angleOk && isAuto && !HasTradesByDistance(up)) {
        OpenTradeWithReverse(up);
      }
    }

    bool _waitBuyClose = false;
    bool _waitSellClose = false;

    private void StrategyEnterBreakout062() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      #region SetLevels
      Action<bool> setLevels = (bool isBuy) => {

        #region Close Trades
        if (!Trades.Any())
          _waitBuyClose = _waitSellClose = false;
        else {
          if (RateLast.PriceAvg >= buyCloseLevel.Rate)
            _waitBuyClose = true;
          if (RateLast.PriceAvg <= sellCloseLevel.Rate)
            _waitSellClose = true;
          if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
            || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
            ) {
            TradesManager.ClosePair(Pair);
            return;
          }
        }
        #endregion

        Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.Skip(1).TakeWhile(p)
          .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
        var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
        var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
        if (isBuy) {
          buyLevel.Rate = RateLast.PriceAvg03.Min(ratesDown[0].IfNaN(double.MaxValue));
          buyLevel.CanTrade = true;
          buyCloseLevel.Rate = RateLast.PriceAvg02.Max(ratesUp.LastByCount().IfNaN(double.MinValue));
          buyCloseLevel.CanTrade = true;
        } else {
          sellLevel.Rate = RateLast.PriceAvg02.Max(ratesUp.LastByCount().IfNaN(double.MinValue));
          sellLevel.CanTrade = true;
          sellCloseLevel.Rate = RateLast.PriceAvg03.Min(ratesDown[0].IfNaN(double.MaxValue));
          sellCloseLevel.CanTrade = true;
        }
      }; 
      #endregion

      StrategyExitByGross061();

      #region Set Levels
      var up = CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      #endregion

      #region Run
      var angleOk = (_strategyAnglePrev.Abs() - CorridorAngle.Abs()).Abs() > TradingAngleRange
    && CorridorAngle.Abs().Round(0) > TradingAngleRange
    && _strategyAnglePrev.Abs().Round(0) < 5;
      _strategyAnglePrev = CorridorAngle;
      var canTrade = SuppRes.Any(sr => sr.CanTrade);
      if (angleOk && !HasTradesByDistance(up) && isAuto) {
        TradesManager.ClosePair(Pair, !up);
        setLevels(up);
      } else if (Trades.Any())
        setLevels(Trades[0].IsBuy);
      else if (canTrade)
        setLevels(up);
      else if (!canTrade)
        SuppRes.ToList().ForEach(sr => sr.Rate = 0); 
      #endregion
    }
    private void StrategyEnterBreakout063() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      #region SetLevels
      Action<bool> setLevels = (bool isBuy) => {

        #region Close Trades
        if (!Trades.Any())
          _waitBuyClose = _waitSellClose = false;
        else {
          if (RateLast.PriceAvg >= buyCloseLevel.Rate)
            _waitBuyClose = true;
          if (RateLast.PriceAvg <= sellCloseLevel.Rate)
            _waitSellClose = true;
          if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
            || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
            ) {
            TradesManager.ClosePair(Pair);
            return;
          }
        }
        #endregion

        Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
          .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
        var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
        var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
        var tp = InPoints(TakeProfitPips);
        if (isBuy) {
          buyLevel.Rate = RateLast.PriceAvg03.Min(ratesDown[0], RateLast.PriceAvg1 - tp);
          buyCloseLevel.Rate = RateLast.PriceAvg02.Max(ratesUp.LastByCount(), buyNetOpen + tp);
        } else {
          sellLevel.Rate = RateLast.PriceAvg02.Max(ratesUp.LastByCount(), RateLast.PriceAvg1 + tp);
          sellCloseLevel.Rate = RateLast.PriceAvg03.Min(ratesDown[0], sellNetOpen - tp);
        }
      };
      #endregion

      StrategyExitByGross061();

      #region Run
      var up = CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      var canTrade = angleOk && corridorDateOk && stDevOk;
      SuppRes.ToList().ForEach(sr => sr.CanTrade = canTrade);
      setLevels(up);
      #endregion
    }

    double _tradingDistanceMax = 0;
    private void StrategyEnterBreakout064() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = ()=> Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      #region SetLevels
      Action<bool> setLevels = (bool isBuy) => {

        #region Close Trades
        if (!Trades.Any())
          _waitBuyClose = _waitSellClose = false;
        else {
          if (RateLast.PriceAvg >= buyCloseLevel.Rate)
            _waitBuyClose = true;
          if (RateLast.PriceAvg <= sellCloseLevel.Rate)
            _waitSellClose = true;
        }
        #endregion

        if (isBuy) {
          buyLevel.Rate = RateLast.PriceAvg03.Min( RateLast.PriceAvg1 - tp);
          buyCloseLevel.Rate = RateLast.PriceAvg02.Max(buyNetOpen() + tpColse);
        } else {
          sellLevel.Rate = RateLast.PriceAvg02.Max(RateLast.PriceAvg1 + tp);
          sellCloseLevel.Rate = RateLast.PriceAvg03.Min(sellNetOpen() - tpColse);
        }
      };
      #endregion

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      var basePrice = MagnetPrice;
      if (Trades.Any()) {
        if (up) {
          buyCloseLevel.CanTrade = false;
          sellLevel.Rate = MagnetPrice - tp;
          sellLevel.CanTrade = canTrade;
          buyLevel.Rate = sellCloseLevel.Rate = 0.0001;
        } else {
          sellCloseLevel.CanTrade = false;
          buyLevel.Rate = MagnetPrice + tp;
          buyLevel.CanTrade = canTrade;
          sellLevel.Rate = buyCloseLevel.Rate = 0.0001;
        }
      } else {
        buyLevel.Rate = MagnetPrice + tp;
        sellLevel.Rate = MagnetPrice - tp;
        buyLevel.CanTrade = sellLevel.CanTrade = canTrade;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
        buyCloseLevel.Rate = sellCloseLevel.Rate = 0.0001;
      }
      buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse);
      sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse);
      #endregion
    }

    private void StrategyEnterBreakout070() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
      #region SetLevels
      Action<bool> setLevels = (bool isBuy) => {

        #region Close Trades
        if (!Trades.Any())
          _waitBuyClose = _waitSellClose = false;
        else {
          if (RateLast.PriceAvg >= buyCloseLevel.Rate)
            _waitBuyClose = true;
          if (RateLast.PriceAvg <= sellCloseLevel.Rate)
            _waitSellClose = true;
        }
        #endregion

        if (isBuy) {
          buyLevel.Rate = RateLast.PriceAvg03.Min(RateLast.PriceAvg1 - tp);
          buyCloseLevel.Rate = RateLast.PriceAvg02.Max(buyNetOpen() + tpColse);
        } else {
          sellLevel.Rate = RateLast.PriceAvg02.Max(RateLast.PriceAvg1 + tp);
          sellCloseLevel.Rate = RateLast.PriceAvg03.Min(sellNetOpen() - tpColse);
        }
      };
      #endregion

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      var basePrice = MagnetPrice;
      if (Trades.Any()) {
        if (up) {
          buyCloseLevel.CanTrade = false;
          sellLevel.Rate = MagnetPrice - tp;
          sellLevel.CanTrade = canTrade;
          buyLevel.Rate = sellCloseLevel.Rate = 0.0001;
        } else {
          sellCloseLevel.CanTrade = false;
          buyLevel.Rate = MagnetPrice + tp;
          buyLevel.CanTrade = canTrade;
          sellLevel.Rate = buyCloseLevel.Rate = 0.0001;
        }
      } else {
        buyLevel.Rate = MagnetPrice + tp;
        sellLevel.Rate = MagnetPrice - tp;
        buyLevel.CanTrade = sellLevel.CanTrade = canTrade;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
        buyCloseLevel.Rate = sellCloseLevel.Rate = 0.0001;
      }
      buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse);
      sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse);
      #endregion
    }

    #region CanBuy(Sell)
    bool _canBuy;
    public bool CanBuy {
      get { return _canBuy; }
      set {
        if (_canBuy == value) return;
        _canBuy = value;
        if (value) CanSell = false;
        OnPropertyChanged("CanBuy");
      }
    }
    bool _canSell;
    public bool CanSell {
      get { return _canSell; }
      set {
        if (_canSell == value) return;
        _canSell = value;
        if (value) CanBuy = false;
        OnPropertyChanged("CanSell");
      }
    }
    #endregion

    bool _stDevOk {
      get {
        return StDevAverages.Count < 2
          ? true : CorridorBigToSmallRatio > 0 
          ? StDevAverages[1] / StDevAverages[0] > CorridorBigToSmallRatio 
          : StDevAverages[1] / StDevAverages[0] < CorridorBigToSmallRatio.Abs();
      }
    }

    private void StrategyEnterBreakout071() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          CanSell = CanBuy = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
          CanSell = CanBuy = false;
        };
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

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      var basePrice = MagnetPrice;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> sellLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg > MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> buyLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg < MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderBy(d => d).First();
      if (isAuto) {
        if (Trades.Any()) {
          if (up) {
            _sellLevel.Rate = MagnetPrice - tp;
            _sellLevel.CanTrade = canTrade;
          } else {
            _buyLevel.Rate = MagnetPrice + tp;
            _buyLevel.CanTrade = canTrade;
          }
        } else {
          if (RateLast.PriceAvg > _buyLevel.Rate)
            CanBuy = true;
          if (RateLast.PriceAvg < _sellLevel.Rate)
            CanSell = true;
          _buyLevel.Rate = MagnetPrice + (CanBuy ? -tradeLevelByMP(false) : tp);
          _sellLevel.Rate = MagnetPrice - (CanSell ? -tradeLevelByMP(true) : tp);
        }

        _buyLevel.CanTrade = CanBuy;
        _sellLevel.CanTrade = CanSell;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      }
      if (Strategy.HasFlag(Strategies.Breakout)) {
        var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 1.1;
        buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse.Max(minCloseOffset));
        sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse.Max(minCloseOffset));
      }
      #endregion
    }

    private void StrategyEnterBreakout0710() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          CanSell = CanBuy = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
          CanSell = CanBuy = false;
        };
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

      //if (CorridorStats.Rates.Count < 360) return;

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> sellLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg > MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> buyLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg < MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderBy(d => d).First();
      if (isAuto) {
        if (Trades.Any()) {
          if(_stDevOk)
            if (up) {
              _sellLevel.Rate = MagnetPrice - tp;
              _sellLevel.CanTrade = canTrade;
            } else {
              _buyLevel.Rate = MagnetPrice + tp;
              _buyLevel.CanTrade = canTrade;
            }
        } else if (_stDevOk) {
          if (RateLast.PriceAvg > _buyLevel.Rate)
            CanBuy = true;
          if (RateLast.PriceAvg < _sellLevel.Rate)
            CanSell = true;
          _buyLevel.Rate = MagnetPrice + (CanBuy ? -tradeLevelByMP(false) : tp);
          _sellLevel.Rate = MagnetPrice - (CanSell ? -tradeLevelByMP(true) : tp);
        }

        _buyLevel.CanTrade = _stDevOk && CanBuy;
        _sellLevel.CanTrade = _stDevOk && CanSell;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      }
      if (Strategy.HasFlag(Strategies.Breakout)) {
        var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 1.1;
        buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse.Max(minCloseOffset));
        sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse.Max(minCloseOffset));
      }
      #endregion
    }

    private void StrategyEnterBreakout0711() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          CanSell = CanBuy = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
          CanSell = CanBuy = false;
        };
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

      //if (CorridorStats.Rates.Count < 360) return;

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> sellLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg > MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> buyLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg < MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderBy(d => d).First();
      if (isAuto) {
        if (Trades.Any()) {
          if (_stDevOk)
            if (up) {
              _sellLevel.Rate = MagnetPrice - tp;
              _sellLevel.CanTrade = canTrade;
            } else {
              _buyLevel.Rate = MagnetPrice + tp;
              _buyLevel.CanTrade = canTrade;
            }
        } else if (_stDevOk) {
          if (RateLast.PriceAvg > _buyLevel.Rate)
            CanBuy = true;
          if (RateLast.PriceAvg < _sellLevel.Rate)
            CanSell = true;
          _buyLevel.Rate = MagnetPrice + (CanBuy ? -tradeLevelByMP(false) : tp);
          _sellLevel.Rate = MagnetPrice - (CanSell ? -tradeLevelByMP(true) : tp);
        } else CanSell = CanBuy = false;

        _buyLevel.CanTrade = _stDevOk && CanBuy;
        _sellLevel.CanTrade = _stDevOk && CanSell;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      }
      if (Strategy.HasFlag(Strategies.Breakout)) {
        var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 1.1;
        buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse.Max(minCloseOffset));
        sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse.Max(minCloseOffset));
      }
      #endregion
    }

    void initExeuteOnTradeCloseOpen(Action action = null) {
      if (_strategyExecuteOnTradeClose == null) {
        if (action != null) action();
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          CanSell = CanBuy = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
          CanSell = CanBuy = false;
        };
      }
    }

    private void StrategyEnterBreakout0712() {
      _useTakeProfitMin = false;
      initExeuteOnTradeCloseOpen();

      #region Suppres levels
      _buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(_buyLevel.Rate);
      _sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(_sellLevel.Rate);
      #endregion

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      if (isAuto && corridorOk) {
        if (Trades.Any()) {
          if (_stDevOk)
            if (up) {
              _sellLevel.Rate = (Trades.NetOpen() - RatesStDev).Min(CorridorStats.Rates.Skip(5).OrderBy(r => r.PriceAvg).First().PriceAvg);
              _sellLevel.CanTrade = canTrade;
            } else {
              _buyLevel.Rate = (Trades.NetOpen() + RatesStDev).Max(CorridorStats.Rates.Skip(5).OrderByDescending(r => r.PriceAvg).First().PriceAvg);
              _buyLevel.CanTrade = canTrade;
            }
        } else if (_stDevOk) {
          if (RateLast.PriceAvg > _buyLevel.Rate)
            CanBuy = true;
          if (RateLast.PriceAvg < _sellLevel.Rate)
            CanSell = true;
          _buyLevel.Rate = MagnetPrice + (CanBuy ? -tradeLevelByMP(false) : tp);
          _sellLevel.Rate = MagnetPrice - (CanSell ? -tradeLevelByMP(true) : tp);
        } else CanSell = CanBuy = false;

        _buyLevel.CanTrade = _stDevOk && CanBuy;
        _sellLevel.CanTrade = _stDevOk && CanSell;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      }
      if (Strategy.HasFlag(Strategies.Breakout)) {
        var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0;
        buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse.Max(minCloseOffset));
        sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse.Max(minCloseOffset));
      }
      #endregion
    }


    private void StrategyEnterBreakout072() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          CanSell = CanBuy = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
          CanSell = CanBuy = false;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      if (StrategyExitByGross061()) return;

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      var basePrice = MagnetPrice;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> sellLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg > MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> buyLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg < MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderBy(d => d).First();
      var buyRate = RateLast.PriceAvg21;
      var sellRate = RateLast.PriceAvg31;
      if (Trades.Any()) {
        if (up) {
          sellLevel.Rate = buyNetOpen() - tp;
        } else {
          buyLevel.Rate = sellNetOpen() + tp;
        }
      } else {
        Action a = () => buyLevel.Rate = CanBuy ? MagnetPrice - tradeLevelByMP(false) : buyRate;
        a();
        if (RateLast.PriceAvg > buyLevel.Rate) {
          CanBuy = true;
          a();
        }
        a = () => sellLevel.Rate = CanSell ? MagnetPrice + tradeLevelByMP(true) : sellRate;
        a();
        if (RateLast.PriceAvg < sellLevel.Rate) {
          CanSell = true;
          a();
        }
      }

      buyLevel.CanTrade = CanBuy;
      sellLevel.CanTrade = CanSell;
      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;

      buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse);
      sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse);

      #endregion
    }
    private void StrategyEnterBreakout073() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          CanSell = CanBuy = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
          CanSell = CanBuy = false;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      if (StrategyExitByGross061()) return;

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      var canTrade = RatesStDev >= SpreadForCorridor * 4;
      var basePrice = MagnetPrice;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> sellLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg > MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> buyLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg < MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderBy(d => d).First();
      var buyRate = RateLast.PriceAvg21;
      var sellRate = RateLast.PriceAvg31;
      if (Trades.Any()) {
        if (up) {
          sellLevel.Rate = buyNetOpen() - tp;
        } else {
          buyLevel.Rate = sellNetOpen() + tp;
        }
      } else {
        Action a = () => buyLevel.Rate = CanBuy ? MagnetPrice : buyRate;
        a();
        if (RateLast.PriceAvg < sellRate) {
          CanBuy = true;
          a();
        }
        a = () => sellLevel.Rate = CanSell ? MagnetPrice : sellRate;
        a();
        if (RateLast.PriceAvg > buyRate) {
          CanSell = true;
          a();
        }
      }

      buyLevel.CanTrade = CanBuy;
      sellLevel.CanTrade = CanSell;
      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;

      buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse);
      sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse);

      #endregion
    }
    private void StrategyEnterBreakout074() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          if (CurrentGross > 0)
            CanBuy = CanSell = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      if (StrategyExitByGross061()) return;

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      var canTrade = tp >= SpreadForCorridor * 4;
      var basePrice = MagnetPrice;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> sellLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg > MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      Func<double> buyLevelByMP = () => CorridorStats.Rates.TakeWhile(r => r.PriceAvg < MagnetPrice).Select(r => r.PriceAvg - MagnetPrice).DefaultIfEmpty(0).OrderBy(d => d).First();
      var buyRate = RateLast.PriceAvg21;
      var sellRate = RateLast.PriceAvg31;
      if (false && Trades.Any()) {
        if (up) {
          sellLevel.Rate = buyNetOpen() - tp;
        } else {
          buyLevel.Rate = sellNetOpen() + tp;
        }
      } else {
        Action a = () => {
          if (CanBuy) {
            buyLevel.Rate = MagnetPrice;
            sellLevel.Rate = sellRate;
          } else if (CanSell) {
            buyLevel.Rate = buyRate;
            sellLevel.Rate = MagnetPrice;
          } else {
            buyLevel.Rate = buyRate;
            sellLevel.Rate = sellRate;
          }
        };
        a();
        if (RateLast.PriceAvg < sellRate) {
          CanBuy = true;
          a();
        }
        if (RateLast.PriceAvg > buyRate) {
          CanSell = true;
          a();
        }
      }

      if (CanBuy || CanSell)
        buyLevel.CanTrade = sellLevel.CanTrade = canTrade;
      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;

      buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse);
      sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse);

      #endregion
    }
    private void StrategyEnterBreakout075() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = Resistance0();
      var buyCloseLevel = Support1();
      Func<double> buyNetOpen = () => Trades.IsBuy(true).NetOpen(buyLevel.Rate);
      var sellLevel = Support0();
      var sellCloseLevel = Resistance1();
      Func<double> sellNetOpen = () => Trades.IsBuy(false).NetOpen(sellLevel.Rate);
      #endregion

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      if (StrategyExitByGross061()) return;

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = CorridorStats.StDev / SpreadForCorridor > CorridorStDevToSpreadMin;
      var canTrade = angleOk && stDevOk;
      var basePrice = RateLast.PriceAvg1;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > basePrice : r.PriceAvg < basePrice)
        .Select(r => (r.PriceAvg - basePrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      var buyRate = RateLast.PriceAvg21;
      var sellRate = RateLast.PriceAvg31;
      if (Trades.Any()) {
        if (up) {
          sellLevel.Rate = buyNetOpen() - tp;
        } else {
          buyLevel.Rate = sellNetOpen() + tp;
        }
      } else {
        Action a = () => buyLevel.Rate = CanBuy ? basePrice : buyRate;
        a();
        if (RateLast.PriceAvg < sellRate) {
          CanBuy = true;
          a();
        }
        a = () => sellLevel.Rate = CanSell ? basePrice : sellRate;
        a();
        if (RateLast.PriceAvg > buyRate) {
          CanSell = true;
          a();
        }
      }

      if (CanBuy)
        buyLevel.CanTrade = canTrade && CorridorAngle > 0;
      else buyLevel.CanTrade = false;
      if (CanSell)
        sellLevel.CanTrade = canTrade && CorridorAngle < 0;
      else sellLevel.CanTrade = false;
      buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;

      buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse);
      sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse);

      #endregion
    }

    #region 08s
    DateTime _corridorStartDateLast;
    bool _isCorridorHot = false;
    double _tradeLifespan = double.NaN;
    private void StrategyEnterBreakout08() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;

      {
        if (_strategyExecuteOnTradeClose == null) {
          SuppResLevelsCount = 2;
          _strategyExecuteOnTradeClose = (t) => {
            if (!Trades.IsBuy(t.IsBuy).Any()) {
              _useTakeProfitMin = false;
              _waitBuyClose = _waitSellClose = false;
              _tradingDistanceMax = 0;
            }
            CanSell = CanBuy = false;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          };
          _strategyExecuteOnTradeOpen = () => {
            _useTakeProfitMin = true;
            CanSell = CanBuy = false;
          };
        }
      }
      #endregion

      #region isCorridorHot
      {
        if (_corridorStartDateLast == DateTime.MinValue)
          _corridorStartDateLast = CorridorStats.StartDate;
        var ch = CorridorStats.Rates.Count < RatesArraySafe.Count * .0625
          && (CorridorStats.StartDate - _corridorStartDateLast).TotalMinutes > RatesArraySafe.Count * this.BarPeriodInt / 3;
        if (ch) {
          _isCorridorHot = true;
        }
        _corridorStartDateLast = CorridorStats.StartDate;
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

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      #region Exit trade
      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();
      #endregion

      #region Run
      double _0 = 0.00001;
      Rate rateHigh = CorridorStats.Rates.OrderByDescending(r => r.AskHigh).First();
      Rate rateLow = CorridorStats.Rates.OrderBy(r => r.BidLow).First();
      var corridorOk = CorridorStats.Rates.Count > 360 && StDevAverages[0] < SpreadForCorridor * 2;
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();

      if (isAuto && _isCorridorHot) {
        _isCorridorHot = false;
        _buyLevel.CanTrade = !up;
        _sellLevel.CanTrade = up;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      }
      if (Strategy.HasFlag(Strategies.Breakout)) {
        _sellLevel.Rate = _sellLevel.CanTrade ? CurrentPrice.Bid.Max(MagnetPrice + tradeLevelByMP(true).Max(tp)) : _0;
        _buyLevel.Rate = _buyLevel.CanTrade ? CurrentPrice.Ask.Min(MagnetPrice - tradeLevelByMP(false).Max(tp)) : _0;
        buyCloseLevel.Rate = Trades.HaveBuy() ? (MagnetPrice + tradeLevelByMP(true)).Min(buyNetOpen() + tpColse).Max(CurrentPrice.Bid) : _0;
        sellCloseLevel.Rate = Trades.HaveSell() ? (MagnetPrice - tradeLevelByMP(false)).Max(sellNetOpen() - tpColse).Min(CurrentPrice.Ask) : _0;
      }
      #endregion
    }

    private void StrategyEnterBreakout081() {

      #region Init
      if (CorridorStats.Rates.Count == 0) return;

      {
        if (_strategyExecuteOnTradeClose == null) {
          SuppResLevelsCount = 2;
          _strategyExecuteOnTradeClose = (t) => {
            if (!Trades.IsBuy(t.IsBuy).Any()) {
              _useTakeProfitMin = false;
              _waitBuyClose = _waitSellClose = false;
              _tradingDistanceMax = 0;
            }
            CanSell = CanBuy = false;
            _buyLevel.CanTrade = _sellLevel.CanTrade = false;
          };
          _strategyExecuteOnTradeOpen = () => {
            _useTakeProfitMin = true;
            CanSell = CanBuy = false;
          };
        }
      }
      #endregion

      #region isCorridorHot
      {
        if (_corridorStartDateLast == DateTime.MinValue)
          _corridorStartDateLast = CorridorStats.StartDate;
        var ch = (CorridorStats.StartDate - _corridorStartDateLast).TotalMinutes > CorridorStats.Rates.Count * 5;
        if (ch && !Trades.Any() && CheckPendingKey("OT")) {
          _tradeLifespan = CorridorStats.Rates.Count;
          _isCorridorHot = true;
        }
        _corridorStartDateLast = CorridorStats.StartDate;
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

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      #region Exit trade
      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();
      #endregion

      #region Run
      double _0 = 0.00001;
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

      if (isAuto && _isCorridorHot) {
        _isCorridorHot = false;

        _sellLevel.Rate = up ? MagnetPrice - tradeLevelByMP(false) : rateLow().BidLow;
        _buyLevel.Rate = up ? rateHigh().AskHigh: MagnetPrice + tradeLevelByMP(true);

        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        _buyLevel.TradesCount = up ? 0 : 1;
        _sellLevel.TradesCount = up ? 1 : 0;

        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      }
      if (Strategy.HasFlag(Strategies.Breakout)) {
        var barsInTrade = (TradesManager.ServerTime - Trades.Select(t => t.Time).OrderBy(t => t).FirstOrDefault()).TotalMinutes / BarPeriodInt;
        var timeOffset = (_tradeLifespan / (_tradeLifespan + barsInTrade));
        var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 0.1;
        buyCloseLevel.Rate = (buyNetOpen() + minCloseOffset.Max(tpColse * timeOffset)).Max(RateLast.PriceAvg, RatePrev.PriceAvg);
        sellCloseLevel.Rate = (sellNetOpen() - minCloseOffset.Max(tpColse * timeOffset)).Min(RateLast.PriceAvg, RatePrev.PriceAvg);
      }
      #endregion
    }
    #endregion

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
      if (_waveLong.Item3 >= WaveLengthMin) {
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
      var canTrade = isAngleOk && isLotSizeOk;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      Func<bool, double> tradeLevelByMP = (above) => CorridorStats.Rates.TakeWhile(r => above ? r.PriceAvg > MagnetPrice : r.PriceAvg < MagnetPrice)
        .Select(r => (r.PriceAvg - MagnetPrice).Abs()).DefaultIfEmpty(0).OrderByDescending(d => d).First();
      if (WaveLengthMin > 0 && canTrade && _waveLong.Item3 >= WaveLengthMin && _waveLast.HasChanged(_waveLong)) {
        var up = Trades.Any() ? Trades[0].IsBuy : _waveLast.IsUp;
        var ratesFirst = CorridorsRates[0]
          .SkipWhile(r => r.StartDate < _waveLong.Item1.StartDate).TakeWhile(r => r.StartDate <= _waveLong.Item2.StartDate).ToList();
        var bl = ratesFirst.Max(r => r.AskAvg);
        var sl = ratesFirst.Min(r => r.BidAvg);
        if (bl - sl < SpreadForCorridor * 8) {
          _buyLevel.Rate = bl;
          _sellLevel.Rate = sl;
          if (isAuto) {
            _buyLevel.CanTrade = !TradeByRateDirection || up;
            _sellLevel.CanTrade = !TradeByRateDirection || !up;
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
      if (WaveLengthMin > 0 && canTrade) {
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
      var waveIndex = _waves.IndexOf(_waveHigh);
      if (canTrade && _waves.Count - waveIndex <= 3 && _waveLast.HasChanged(ratesFirst)) {
        var rate1 = _waves[0.Max(waveIndex - 1)];
        var bl = ratesFirst.Max(r => r.AskHigh).Max(rate1.Max(r=>r.AskHigh));
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

    private void StrategyEnterBreakout095() {

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
      var waveIndex = _waves.IndexOf(_waveHigh);
      if (canTrade && _waves.Count - waveIndex <= 3 && _waveLast.HasChanged(_waveHigh)) {
        var rate1 = _waves[0.Max(waveIndex - 1)];
        _buyLevelRate = _waveHigh.Max(r => r.AskHigh).Max(rate1.Max(r => r.AskHigh));
        _sellLevelRate = _waveHigh.Min(r => r.BidLow).Min(rate1.Min(r => r.BidLow));
        _buyLevel.Rate = _buyLevelRate;
        _sellLevel.Rate = _sellLevelRate;
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
      public bool IsUp { get { return _rateEnd.PriceAvg > _rateStart.PriceAvg; } }
      public bool HasChanged(IList<Rate> wave) { return HasChanged(wave[0], wave.LastByCount()); }
      public bool HasChanged(Tuple<Rate, Rate, int> t) { return HasChanged(t.Item1, t.Item2); }
      public bool HasChanged(Rate rateStart, Rate rateEnd) {
        if ((rateStart.StartDate - this._rateStart.StartDate).TotalMinutes > 15 /*&& this._rateEnd.StartDate != rateEnd.StartDate*/) {
          this._rateStart = rateStart;
          this._rateEnd = rateEnd;
          return true;
        }
        return false;
      }
    }
    #endregion

    private void StrategyBreakout2Corr() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 2;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
          CanSell = CanBuy = false;
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
          CanSell = CanBuy = false;
        };
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

      var tp = CalculateTakeProfit();// InPoints(TakeProfitPips);
      var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
        .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
      var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
      var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
      var isAuto = IsInPlayback || IsHotStrategy||IsAutoStrategy;
      if (isAuto) {
        _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
        var corridorAverageLast = MagnetPrice;
        var corridorAveragePrev = CorridorsRates[1].Average(r => r.PriceAvg);
        _buyLevel.Rate = corridorAverageLast.Max(corridorAveragePrev);
        _sellLevel.Rate = corridorAverageLast.Min(corridorAveragePrev);
        _buyLevel.CanTrade = _sellLevel.CanTrade = true;
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = false;
      }
      if (IsBreakpoutStrategy) {
        var minCloseOffset = (_buyLevel.Rate - _sellLevel.Rate).Abs() * 1.1;
        buyCloseLevel.Rate = ratesUp.LastByCount().Max(buyNetOpen() + tpColse.Max(minCloseOffset));
        sellCloseLevel.Rate = ratesDown[0].Min(sellNetOpen() - tpColse.Max(minCloseOffset));
      }
      #endregion
    }

    #endregion
    //
    void StrategyBreakout() {
      StrategyEnterBreakout081();
    }

    void StrategyRange() {
      StrategyBreakout();
    }


    private void StrategyEnterRange034() {
      if (!StrategyExitByGross032())
        Trades.Where(t => t.PL > TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      if (!IsInVitualTrading) return;
      Func<double, double, bool> canTradeByTradeCorridor = (level1, level2) => {
        return (level1 - level2).Abs().Between(SpreadForCorridor * 2, RatesHeight / 2);
      };

      var stDev = this.CalculateTakeProfit();
      var sellLevel = MagnetPrice + stDev;
      var buyLevel = MagnetPrice - stDev;

      var rs = Resistances.OrderByDescending(r => r.Rate).ToList();
      Enumerable.Range(0, rs.Count()).ToList().ForEach(r => rs[r].Rate = buyLevel - stDev * r);
      var ss = Supports.OrderBy(r => r.Rate).ToList();
      Enumerable.Range(0, ss.Count()).ToList().ForEach(r => ss[r].Rate = sellLevel + stDev * r);
      SuppRes.ToList().ForEach(sr => sr.CanTrade = true);

    }

    private void StrategyEnterRange035() {
      if (!StrategyExitByGross1()) {
        Trades.Where(t => t.PL > TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      }
      if (!IsInVitualTrading) return;
      Func<double, double, bool> canTradeByTradeCorridor = (level1, level2) => {
        return (level1 - level2).Abs().Between(SpreadForCorridor * 2, RatesHeight / 2);
      };

      var stDev = this.CalculateTakeProfit();
      var sellLevel = MagnetPrice + stDev;
      var buyLevel = MagnetPrice - stDev;

      var rs = Resistances.OrderByDescending(r => r.Rate).ToList();
      Enumerable.Range(0, rs.Count()).ToList().ForEach(r => rs[r].Rate = buyLevel - stDev * r);
      var ss = Supports.OrderBy(r => r.Rate).ToList();
      Enumerable.Range(0, ss.Count()).ToList().ForEach(r => ss[r].Rate = sellLevel + stDev * r);
      SuppRes.ToList().ForEach(sr => sr.CanTrade = true);

    }
    private void StrategyEnterRange036() {
      if (!StrategyExitByGross1()) {
        Trades.Where(t => t.PL > TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      }
      if (!IsInVitualTrading) return;

      var stDev = this.CalculateTakeProfit();
      var sellLevel = MagnetPrice + stDev;
      var buyLevel = MagnetPrice - stDev;

      var canBuy = !Trades.IsBuy(false).Any();
      var rs = Resistances.OrderByDescending(r => r.Rate).ToList();
      Enumerable.Range(0, rs.Count()).ToList().ForEach(r => {
        rs[r].Rate = canBuy ? buyLevel - stDev * r : InPoints(1)*300-MagnetPrice;
        rs[r].CanTrade = canBuy;
      });

      var canSell = !Trades.IsBuy(true).Any();
      var ss = Supports.OrderBy(r => r.Rate).ToList();
      Enumerable.Range(0, ss.Count()).ToList().ForEach(r => {
        ss[r].Rate = canSell ? sellLevel + stDev * r : InPoints(1) * 300 + MagnetPrice;
        ss[r].CanTrade = canSell;
      });
    }

    private void StrategyEnterRange064() {
      #region Init SuppReses
      if (_strategyExecuteOnTradeClose == null) {
        SuppResLevelsCount = 1;
        _strategyExecuteOnTradeClose = (t) => {
          if (!Trades.IsBuy(t.IsBuy).Any()) {
            _useTakeProfitMin = false;
            _waitBuyClose = _waitSellClose = false;
            _tradingDistanceMax = 0;
          }
        };
        _strategyExecuteOnTradeOpen = () => {
          _useTakeProfitMin = true;
        };
      }
      #endregion

      #region Suppres levels
      var buyLevel = ResistanceHigh();
      var buyCloseLevel = SupportHigh();
      var buyNetOpen = Trades.IsBuy(true).NetOpen(RateLast.PriceAvg1);
      var sellLevel = SupportLow();
      var sellCloseLevel = ResistanceLow();
      var sellNetOpen = Trades.IsBuy(false).NetOpen(RateLast.PriceAvg1);
      #endregion

      #region SetLevels
      Action<bool> setLevels = (bool isBuy) => {

        #region Close Trades
        if (!Trades.Any())
          _waitBuyClose = _waitSellClose = false;
        else {
          if (RateLast.PriceAvg >= buyCloseLevel.Rate)
            _waitBuyClose = true;
          if (RateLast.PriceAvg <= sellCloseLevel.Rate)
            _waitSellClose = true;
        }
        #endregion

        Func<Func<Rate, bool>, List<double>> getRates = p => CorridorStats.Rates.TakeWhile(p)
          .Select(r => r.PriceAvg).DefaultIfEmpty(double.NaN).OrderBy(d => d).ToList();
        var ratesUp = getRates(r => r.PriceAvg > r.PriceAvg1);
        var ratesDown = getRates(r => r.PriceAvg < r.PriceAvg1);
        var tp = InPoints(TakeProfitPips);
        var tpColse = InPoints(TakeProfitPips - (Trades.Any() ? CurrentLossInPips : 0));
        if (isBuy) {
          buyLevel.Rate = RateLast.PriceAvg03.Min(ratesDown[0], RateLast.PriceAvg1 - tp);
          buyCloseLevel.Rate = ratesUp.LastByCount().Max(RateLast.PriceAvg02.Max(buyNetOpen + tpColse));
        } else {
          sellLevel.Rate = RateLast.PriceAvg02.Max(ratesUp.LastByCount(), RateLast.PriceAvg1 + tp);
          sellLevel.CanTrade = true;
          sellCloseLevel.Rate = ratesDown[0].Min(RateLast.PriceAvg03.Min(sellNetOpen - tpColse));
        }
      };
      #endregion

      if (_waitBuyClose && Trades.IsBuy(true).Any() && RateLast.PriceAvg < buyCloseLevel.Rate
        || _waitSellClose && Trades.IsBuy(false).Any() && RateLast.PriceAvg > sellCloseLevel.Rate
        ) {
        TradesManager.ClosePair(Pair);
        return;
      }
      StrategyExitByGross061();

      #region Run
      _tradingDistanceMax = _tradingDistanceMax.Max(TradingDistanceInPips);
      var up = Trades.Any() ? Trades[0].IsBuy : CorridorAngle > 0;
      var isAuto = IsInVitualTrading || IsAutoStrategy;
      var stDevMean = RatesArray.Select(r => r.PriceStdDev).Mean();
      var angleOk = CorridorAngle.Abs().Round(0) < TradingAngleRange;
      var stDevOk = StDevAverage.Between(stDevMean * CorridorStDevRatioMin, stDevMean * CorridorStDevRatioMax);
      var corridorDateOk = (RatesArray.LastByCount().StartDate - CorridorStats.StartDate).TotalMinutes.Abs() * 2 < (RatesArray.LastByCount().StartDate - RatesArray[0].StartDate).TotalMinutes.Abs();
      SuppRes.ToList().ForEach(sr => sr.CanTrade = true);
      setLevels(up);
      if (Trades.IsBuy(true).Any())
        buyCloseLevel.CanTrade = false;
      else if (Trades.IsBuy(false).Any())
        sellCloseLevel.CanTrade = false;
      else
        buyCloseLevel.CanTrade = sellCloseLevel.CanTrade = true;

      #endregion
    }

    private void StrategyEnterWave() {
      if (!StrategyExitByGross1())
        Trades.Where(t => t.PL > TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      if (!IsInVitualTrading) return;
      var reversed = RatesArray.ReverseIfNot();
      var node = new LinkedList<Rate>(reversed).First.Next;
      var waveRates = new List<Rate>();
      for (; node.Next != null && waveRates.Count < 2; node = node.Next)
        if (node.Value.PriceStdDev > node.Previous.Value.PriceStdDev.Max(node.Next.Value.PriceStdDev))
          waveRates.Add(node.Value);
      if (waveRates.Count == 2) {
        StDevAverage = reversed.Select(r => r.PriceStdDev).ToList().AverageByIterations(StDevTresholdIterations).Average();
        var canTrade = waveRates[0].PriceStdDev > StDevAverage
          && reversed.TakeWhile(r => r.StartDate >= waveRates[1].StartDate).Height() > TradingDistance
          && waveRates[0].PriceStdDev > waveRates[1].PriceStdDev;
        if (canTrade) {
          var rtd = reversed.TakeWhile(r => r.StartDate > waveRates[0].StartDate).OrderByDescending(r => r.PriceAvg).ToList();
          bool? isBuy =
              rtd[0].StartDate < rtd.LastByCount().StartDate && RateLast.PriceAvg < GetPriceMA(RateLast) ? true
            : rtd[0].StartDate > rtd.LastByCount().StartDate && RateLast.PriceAvg > GetPriceMA(RateLast) ? false
            : (bool?)null;
          if (isBuy.HasValue) {
            var sr = SuppRes.IsBuy(isBuy.Value)[0];
            {
              var level = !sr.CanTrade ? RateLast.PriceAvg
                : isBuy.Value && RateLast.PriceAvg < sr.Rate ? RateLast.PriceAvg
                : !isBuy.Value && RateLast.PriceAvg > sr.Rate ? RateLast.PriceAvg
                : sr.Rate;
              if (sr.Rate != level) {
                sr.Rate = level;
                sr.CanTrade = true;
                SuppRes.IsBuy(!isBuy.Value)[0].Rate = MagnetPrice + (isBuy.Value ? 1 : -1) * RatesHeight * 3;
              }
            }
          }
        }
      }
    }

    private void StrategyEnterWave01() {
      if( !StrategyExitWave() && !this.CloseOnProfitOnly )
        Trades.Where(t => t.PL > TakeProfitPips).ToList().ForEach(t => TradesManager.CloseTrade(t));
      if (!IsInVitualTrading) return;
      var reversed = RatesArray.ReverseIfNot();
      var node = new LinkedList<Rate>(reversed).First.Next;
      var waveRates = new List<Rate>();
      for (; node.Next != null && waveRates.Count < 2; node = node.Next)
        if (node.Value.PriceStdDev > node.Previous.Value.PriceStdDev.Max(node.Next.Value.PriceStdDev))
          waveRates.Add(node.Value);
      if (waveRates.Count == 2) {
        var canTrade = waveRates[0].PriceStdDev > StDevAverage
          && reversed.TakeWhile(r => r.StartDate >= waveRates[1].StartDate).Height() > CalculateTakeProfit()
          && waveRates[0].PriceStdDev > waveRates[1].PriceStdDev;
        if (canTrade) {
          var rtd = reversed.TakeWhile(r => r.StartDate > waveRates[0].StartDate).OrderByDescending(r => r.PriceAvg).ToList();
          bool? isBuy =
              rtd[0].StartDate < rtd.LastByCount().StartDate && RateLast.PriceAvg < GetPriceMA(RateLast) ? true
            : rtd[0].StartDate > rtd.LastByCount().StartDate && RateLast.PriceAvg > GetPriceMA(RateLast) ? false
            : (bool?)null;
          if (isBuy.HasValue) {
            if (CurrentGross >0 || !Trades.IsBuy(!isBuy.Value).Any()) {
              var sr = SuppRes.IsBuy(isBuy.Value)[0];
              {
                var level = !sr.CanTrade ? RateLast.PriceAvg
                  : isBuy.Value && RateLast.PriceAvg < sr.Rate ? RateLast.PriceAvg
                  : !isBuy.Value && RateLast.PriceAvg > sr.Rate ? RateLast.PriceAvg
                  : sr.Rate;
                if (sr.Rate != level) {
                  sr.Rate = level;
                  sr.CanTrade = true;
                  SuppRes.IsBuy(!isBuy.Value)[0].Rate = MagnetPrice + (isBuy.Value ? 1 : -1) * RatesHeight;
                }
              }
            }
          }
        }
      }
    }

    private void TurnOffSuppRes(double level = double.NaN) {
      var rate = double.IsNaN(level) ? SuppRes.Average(sr => sr.Rate) : level;
      foreach (var sr in SuppRes)
        sr.Rate = rate;
    }

    bool StrategyExitByGross031() {
      if (StrategyExitByGross()) return true;
      if (Trades.Any()) {
        var al = AllowedLotSize(Trades, !Trades[0].IsBuy);
        if (Trades.Lots() >= al * 2)
          TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - al);
      }
      return false;
    }
    bool StrategyExitByGross032() {
      if (StrategyExitByGross()) return true;
      if (Trades.Any()) {
          var al = AllowedLotSize(Trades, !Trades[0].IsBuy);
          if (Trades.Lots() >= al * 2)
            TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - al);
      }
      return false;
    }
    bool StrategyExitByGross() { return StrategyExitByGross(() => false); }
    bool StrategyExitByGross042() {
      if (Trades.Lots() > LotSize && CurrentGrossInPips > 0) {
        TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - LotSize);
        return true;
      }
      return false;
    }
    bool StrategyExitByGross04232() {
      if (Trades.Lots() > LotSize && CurrentGrossInPips > 0) {
        TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - LotSize);
        return true;
      }
      double als = AllowedLotSizeCore(Trades);
      if (Trades.Lots() / als >= this.ProfitToLossExitRatio) {
        TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - als.ToInt());
        return true;
      }
      if (CurrentGross < ResetOnBalance) {
        EventHandler<TradeEventArgs> tc = null;
        tc = new EventHandler<TradeEventArgs>((s, e) => {
          CurrentLoss = 0;
          TradesManager.TradeClosed -= tc;
        });
        TradesManager.TradeClosed += tc;
        TradesManager.ClosePair(Pair);
      }
      var tradeDate = Trades.Select(t => t.Time).DefaultIfEmpty(DateTime.MaxValue).OrderBy(t => t).First();
      if (CurrentGross > 0 && CorridorAngle.Abs() < TradingAngleRange && tradeDate < TradesManager.ServerTime.AddDays(-1)) {
        TradesManager.ClosePair(Pair);
        return true;
      }
      if (Trades.Gross() > 0 && CorridorAngle.Abs() < 5 && tradeDate < TradesManager.ServerTime.AddDays(-1)) {
        TradesManager.ClosePair(Pair);
        return true;
      }
      return false;
    }
    bool StrategyExitByGross060() {
      if (Trades.Lots() > LotSize && CurrentGrossInPips > 0) {
        TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - LotSize);
        return true;
      }
      double als = AllowedLotSizeCore(Trades);
      if (Trades.Lots() / als >= this.ProfitToLossExitRatio) {
        TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - als.ToInt());
        return true;
      }
      if (CurrentGrossInPips >= TakeProfitPips && CorridorAngle.Abs().Round(0) < 3) {
        TradesManager.ClosePair(Pair);
        return true;
      }
      return false;
    }

    bool _false() { return false; }
    bool StrategyExitByGross061( Func<bool> doSell = null) {
      doSell = doSell ?? _false;
      if (Trades.Lots() > LotSize && (CurrentGrossInPips > 0 || doSell())) {
        CheckPendingAction("CT", pa => {
          pa();
          var lot = Trades.Lots() - LotSize;
          Log = new Exception(string.Format("{0}:Closing {1} from {2} in {3}", Pair, lot, Trades.Lots(), MethodBase.GetCurrentMethod().Name));
          if (!TradesManager.ClosePair(Pair, Trades[0].IsBuy, lot))
            ReleasePendingAction("CT");
        });
        return true;
      }
      double als = AllowedLotSizeCore(Trades);
      if (Trades.Lots() / als >= this.ProfitToLossExitRatio) {
        var lot = Trades.Lots() - als.ToInt();
        Log = new Exception(string.Format("{0}:Closing {1} from {2} in {3}", Pair, lot, Trades.Lots(), MethodBase.GetCurrentMethod().Name));
        TradesManager.ClosePair(Pair, Trades[0].IsBuy, lot);
        return true;
      }
      return false;
    }

    bool StrategyExitByGross(Func<bool> or) {
      if (Trades.Any()) {
        var exitByProfit = CurrentGrossInPips >= SpreadForCorridorInPips.Max(TakeProfitPips / Trades.Positions(LotSize));
        if (exitByProfit || or()) {
          TradesManager.ClosePair(Pair, Trades[0].IsBuy);
          return true;
        }
        if (Trades.Lots() > LotSize && CurrentGross > 0)
          TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - LotSize);
      }
      return false;
    }

    bool StrategyExitWave() {
      if (Trades.Any()) {
        var exitByProfit = CurrentGrossInPips >= TakeProfitPips / Trades.Positions(LotSize);
        if (exitByProfit) {
          TradesManager.ClosePair(Pair, Trades[0].IsBuy);
          return true;
        }
      }
      return false;
    }
    bool StrategyExitByGross1() {
      if (Trades.Any()) {
        var exitByProfit = CurrentGrossInPips >= TakeProfitPips / Trades.Positions(LotSize);
        if (exitByProfit) {
          TradesManager.ClosePair(Pair, Trades[0].IsBuy);
          return true;
        }
          if (Trades.Lots() > LotSize && CurrentGross > 0)
          TradesManager.ClosePair(Pair, Trades[0].IsBuy, Trades.Lots() - LotSize);
      }
      return false;
    }

    bool HasCrossed() {
      var magnetDirection = CalculateLastPrice(RateLast, GetPriceMA) > MagnetPrice;
      var crossed = _magnetDirtection.HasValue && magnetDirection != _magnetDirtection;
      _magnetDirtection = magnetDirection;
      return crossed;
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
    private SuppRes[] EnsureActiveSuppReses(bool isBuy,bool doTrades = false) {
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
      return TakeProfitPips == 0 || double.IsNaN(TradingDistance)
        || (trades.Any() && CurrentGrossInPips.Min(0).Abs() < (td + PriceSpreadAverageInPips));
        //|| (trades.Any() && trades.Max(t => t.PL) > -(td + PriceSpreadAverageInPips));
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
        timeSpanDict.Add("Other", sw.ElapsedMilliseconds);
        if (sw.Elapsed > TimeSpan.FromSeconds(LoadRatesSecondsWarning)) {
          var s = string.Join(Environment.NewLine, timeSpanDict.Select(kv => " " + kv.Key + ":" + kv.Value));
          Log = new Exception(string.Format("{0}[{2}]:{1:n}ms{3}{4}",
            MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, Pair, Environment.NewLine, s));
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
        case Store.MovingAverageType.Cma:
          return r => r.PriceCMALast;
        case Store.MovingAverageType.Trima:
          return r => r.PriceTrima;
        default:
          throw new NotSupportedException(new { MovingAverageType }.ToString());
      }
    }
    private void SetMA() {
      switch (MovingAverageType) {
        case Store.MovingAverageType.Cma:
          RatesArray.SetCma(PriceCmaPeriod, PriceCmaLevels); break;
        case Store.MovingAverageType.Trima:
          RatesArray.SetTrima(PriceCmaPeriod); break;
      }
    }

    bool IsCorridorLengthOk { get { return CalculateCorridorLengthOk(); } }
    bool CalculateCorridorLengthOk(CorridorStatistics cs = null) {
      return (cs ?? CorridorStats).Rates.Count <= RatesArray.Count / (double)CorridorMinimumLengthRatio;
    }
    Func<double, double, double> _max = (d1, d2) => Math.Max(d1, d2);
    Func<double, double, double> _min = (d1, d2) => Math.Min(d1, d2);
    public void ScanCorridor(IList<Rate> ratesForCorridor) {
      try {
        if (!IsActive || !isLoggedIn || !HasRates /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        var showChart = CorridorStats == null || CorridorStats.Periods == 0;
        #region Prepare Corridor
        var periodsStart = CorridorStartDate == null
          ? (BarsCount * CorridorLengthMinimum).Max(5).ToInt() : ratesForCorridor.Count(r => r.StartDate >= CorridorStartDate.Value);
        if (periodsStart == 1) return;
        var periodsLength = CorridorStartDate.HasValue ? 1 : CorridorStats.Periods > 0 ? ratesForCorridor.Count(r => r.StartDate >= CorridorStats.StartDate) - periodsStart + 1 : int.MaxValue;// periodsStart;
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
        if (ratesForCorridor.Count == 0) {
          #region
          var startDate = ratesForCorridor.Take(ratesForCorridor.Count - 30).OrderByDescending(r => r.PriceStdDev).First().StartDate;
          var startRate = ScanCrosses(reversed.TakeWhile(r => r.StartDate >= startDate).ToList()).OrderByDescending(l => l.Item1).First().Item2;
          startDate = ratesForCorridor.Where(r => startRate.Between(r.BidLow, r.AskHigh)).Min(r => r.StartDate);//.Max(CorridorStats.StartDate);
          var ratesForCross = reversed.TakeWhile(r => r.StartDate >= startDate).ToList();
          var endDate = ratesForCross.Where(r => startRate.Between(r.BidLow, r.AskHigh)).Max(r => r.StartDate);
          var corridorRates = ratesForCross.Where(r => r.StartDate.Between(startDate, endDate)).ToList();
          MagnetPrice = corridorRates.Average(r => r.PriceAvg);
          calcCorridor(corridorRates, MagnetPrice); 
          #endregion
        } else if (ratesForCorridor.Count == 44) {
          #region
          var rates = ratesForCorridor.SkipWhile(r => r.PriceStdDev > StDevAverage * StDevAverageLeewayRatio).Reverse().ToList();
          MagnetPrice = rates.Average(r => r.PriceAvg);

          if (rates.Count < 2) return;
          var coeffs = Regression.Regress(rates.ReverseIfNot().Select(r => r.PriceAvg).ToArray(), 1);
          var stDev = rates.Select(r => (CorridorGetHighPrice()(r) - MagnetPrice).Abs()).ToList().AverageByIterations(1, true).StDev();
          crossedCorridor = new CorridorStatistics(this, rates, stDev, coeffs);
          #endregion
        } else if (Strategy != Strategies.LongWave) {
          #region
          CorridorsRates.Clear();
          foreach (var sda in StDevAverages.OrderByDescending(d => d))
            CorridorsRates.Insert(0, CorridorsRates.DefaultIfEmpty(ratesForCorridor).First().Reverse().TakeWhile(r => r.PriceStdDev <= sda * StDevAverageLeewayRatio).Reverse().ToList());
          if (CorridorStats.Rates.Count > 0)
            CorridorsRates[0] = CorridorsRates[0].Skip((CorridorsRates[0].Count - (CorridorStats.Rates.Count + 1)).Max(0)).ToList();
          MagnetPrice = CorridorsRates[0].Average(r => r.PriceAvg);
          if (CorridorsRates[0].Count < 2) return;
          crossedCorridor = CorridorsRates[0].ReverseIfNot().ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
          #endregion
        } else if (Strategy == Strategies.LongWave || Strategy == Strategies.Breakout7) {
          _waves = ratesForCorridor.Partition(r => r.PriceStdDev != 0).Where(l => l.Count > 1).ToList();
          var waves1 = _waves.Select(w => new { w, s = w.Sum(r => r.PriceStdDev * r.PriceStdDev) }).ToList();
          var waves2 = waves1.AverageByIterations(w => w.s, (v, avg) => v >= avg, this.StDevTresholdIterations);

          //_waveHigh = _waves.Reverse().SkipWhile(w => w.Max(r => r.PriceStdDev) < StDevAverages[0]).First();
          _waveHigh = waves2.Last().w;

          var bigBarStart = _waveHigh[0].StartDate.AddMinutes(-15 * BarPeriodInt).Max(CorridorStats.StartDate.AddMinutes(-15 * BarPeriodInt));// _bigWave.Item1.StartDate.Min(_bigWave.Item2.StartDate);// peaks.AverageByIterations(r => r.PriceStdDev, DensityMin).OrderBarsDescending().First();
          var b = RatesArray.Where(r => r.StartDate >= bigBarStart).ToList();
          if (b.Count < 2) return;
          CorridorsRates.Clear();
          CorridorsRates.Add(b);
          crossedCorridor = CorridorsRates[0].ReverseIfNot().ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
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

        #region Waves
        if (false && !CorridorStartDate.HasValue) {
          var waveRates = GetWaveRates(reversed, 4);
          if (!_waveRates.Any() || waveRates[0].Rate.StartDate > _waveRates[0].Rate.StartDate) {
            _waveRates.Clear();
            _waveRates.AddRange(waveRates);
          }
          for (var w = 110; w < waveRates.Count; w++) {
            if (_waveRates.Count < w + 1)
              _waveRates.Add(waveRates[w]);
            else
              if (_waveRates[w].Rate < waveRates[w].Rate || _waveRates[w].Direction != waveRates[w].Direction)
                _waveRates[w] = waveRates[w];
            if (_rateArray.IndexOf(_waveRates[w].Rate) == -1) {
              var rate = RatesArray.Find(r => r.StartDate == _waveRates[w].Rate.StartDate);
              if (rate == null) _waveRates.RemoveAt(w);
              else _waveRates[w].Rate = rate;
            }
          }
        }
        #endregion
        #region Corridorness
        if (false) {
          var ratesForWave = new List<Tuple<double, Rate>>();
          ratesForCorridor.Take(ratesForCorridor.Count - 30).Aggregate(double.NaN, (ma, r) => {
            var cma = ma.Cma(100, r.Corridorness);
            ratesForWave.Add(new Tuple<double, Rate>(cma, r));
            return cma;
          });
          var corridornessRate = ratesForWave.OrderByDescending(t => t.Item1).First().Item2;
          corridornessRate = ratesForCorridor
            .Where(r => r.StartDate.Between(corridornessRate.StartDate.AddMinutes(-BarPeriodInt * 30), corridornessRate.StartDate.AddMinutes(BarPeriodInt * 30)))
            .OrderByDescending(r => r.Corridorness).First();
          //var stDevRate = ratesForCorridor.SkipWhile(r=>r.StartDate<CorridorStats.StartDate).OrderByDescending(r => r.PriceStdDev).First();
          setPeriods(corridornessRate.StartDate);
        }
        #endregion

        var corridornesses = crossedCorridor != null
          ? new[] { crossedCorridor }.ToList()
          : ratesForCorridor.GetCorridornesses(priceHigh, priceLow, periodsStart, periodsLength, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod, cs => {
            return false;
          }).Select(c => c.Value).ToList();
        if(true){
          var coeffs = Regression.Regress(ratesForCorridor.ReverseIfNot().Select(r => r.PriceAvg).ToArray(), 1);
          var median = ratesForCorridor.Average(r => r.PriceAvg);
          var stDev = ratesForCorridor.Select(r => (CorridorGetHighPrice()(r) - median).Abs()).ToList().StDev();
          CorridorBig = new CorridorStatistics(this, ratesForCorridor, stDev, coeffs);
        }else
          CorridorBig = ratesForCorridor.ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);// corridornesses.LastOrDefault() ?? CorridorBig;
        #endregion
        #region Update Corridor
        if (corridornesses.Any()) {
          var rateLast = ratesForCorridor.Last();
          var cc = corridornesses
            //.OrderBy(cs => InPips(cs.Slope.Abs().Angle()).ToInt())
           .ToList();
          crossedCorridor = cc/*.Where(cs => IsCorridorOk(cs))*/.FirstOrDefault();
          var csCurr = crossedCorridor ?? cc.FirstOrDefault();
          var ok = IsCorridorOk(csCurr) || !CorridorStats.Rates.Any();
          var csOld = CorridorStats;
          var wasShifted = false;
          if (!ok)
            csCurr = ratesForCorridor.SkipWhile(r => r.StartDate < CorridorStats.StartDate).ToList().ReverseIfNot()
              .ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
          else if (false && csCurr.StDev < SpreadForCorridor) {
            csCurr = ratesForCorridor.GetCorridornesses(priceHigh, priceLow, periodsStart, int.MaxValue, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod, cs => {
              return cs.StDev > SpreadForCorridor;
            }).Select(c => c.Value).DefaultIfEmpty(csCurr).Last();
            wasShifted = true;
          }
          csOld.Init(csCurr, PointSize);
          csOld.Spread = csOld.Rates.Spread();
          CorridorStats = csOld;
          CorridorStats.IsCurrent = wasShifted || IsCorridorOk(CorridorStats);// ok;// crossedCorridor != null;
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

    public double CalculateLastPrice(Rate rate, Func<Rate, double> price) {
      try {
        if (TradesManager.IsInTest || IsInPlayback) return price(rate);
        var secondsPerBar = BarPeriodInt * 60;
        var secondsCurrent = (TradesManager.ServerTime - rate.StartDate).TotalSeconds;
        var ratio = secondsCurrent / secondsPerBar;
        var ratePrev = RatesArray.Previous(rate);
        var priceCurrent = price(rate);
        var pricePrev = price(ratePrev);
        return pricePrev * (1 - ratio).Max(0) + priceCurrent * ratio.Min(1);
      } catch (Exception exc) {
        Log = exc;
        return double.NaN;
      }
    }

    public double CorridorCrossHighPrice(Rate rate, Func<Rate, double> getPrice = null) {
      return CalculateLastPrice(rate, getPrice ?? CorridorCrossGetHighPrice());
    }
    public Func<Rate, double> CorridorCrossGetHighPrice() {
      return CorridorHighPrice(CorridorCrossHighLowMethod);
    }
    public Func<Rate, double> CorridorGetHighPrice() {
      return CorridorHighPrice(CorridorHighLowMethod);
    }
    private Func<Rate, double> CorridorHighPrice(CorridorHighLowMethod corridorHighLowMethod) {
      switch (corridorHighLowMethod) {
        case CorridorHighLowMethod.AskHighBidLow: return r => r.AskHigh;
        case CorridorHighLowMethod.AskLowBidHigh: return r => r.AskLow;
        case CorridorHighLowMethod.BidHighAskLow: return r => r.BidHigh;
        case CorridorHighLowMethod.BidLowAskHigh: return r => r.BidLow;
        case CorridorHighLowMethod.Average: return r => r.PriceAvg;
        case CorridorHighLowMethod.AskBidByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.AskHigh : r.BidLow;
        case CorridorHighLowMethod.BidAskByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.BidHigh : r.AskLow;
        case CorridorHighLowMethod.PriceByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.PriceHigh : r.PriceLow;
        case CorridorHighLowMethod.PriceMA: return r => GetPriceMA(r);
      }
      throw new NotSupportedException(new { corridorHighLowMethod } + "");
    }

    public double CorridorCrossLowPrice(Rate rate, Func<Rate, double> getPrice = null) {
      return CalculateLastPrice(rate, getPrice ?? CorridorCrossGetLowPrice());
    }
    public Func<Rate, double> CorridorCrossGetLowPrice() {
      return CorridorLowPrice(CorridorCrossHighLowMethod);
    }
    public Func<Rate, double> CorridorGetLowPrice() {
      return CorridorLowPrice(CorridorHighLowMethod);
    }
    private Func<Rate, double> CorridorLowPrice(CorridorHighLowMethod corridorHighLowMethod) {
      switch (corridorHighLowMethod) {
        case CorridorHighLowMethod.AskHighBidLow: return r => r.BidLow;
        case CorridorHighLowMethod.AskLowBidHigh: return r => r.BidHigh;
        case CorridorHighLowMethod.BidHighAskLow: return r => r.AskLow;
        case CorridorHighLowMethod.BidLowAskHigh: return r => r.AskHigh;
        case CorridorHighLowMethod.Average: return r => r.PriceAvg;
        case CorridorHighLowMethod.AskBidByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.AskHigh : r.BidLow;
        case CorridorHighLowMethod.BidAskByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.BidHigh : r.AskLow;
        case CorridorHighLowMethod.PriceByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.PriceHigh : r.PriceLow;
        case CorridorHighLowMethod.PriceMA: return r => GetPriceMA(r);
      }
      throw new NotSupportedException(new { corridorHighLowMethod } + "");
    }

    private bool IsCorridorOk(CorridorStatistics cs) {
      return true;
      if (!cs.Rates.Any()) return false;
      var rates = cs.Rates.OrderBy(r => r.PriceAvg).ToList();
      var rateLow = rates[0];
      var rateHigh = rates[rates.Count-1];
      var rateAverage = rates.Average(r => r.PriceAvg);
      var stDev3 = cs.StDev * 3;
      return
        CorridorCrossGetLowPrice()(rateLow) < rateAverage - stDev3 &&
        CorridorCrossGetHighPrice()(rateHigh) > rateAverage + stDev3
        //&& CalcIsCorridorStDevToRatesStDevRatioOk(cs)
        //&& cs.StDev.Between(SpreadForCorridor * .75, SpreadForCorridor * 1.5)
        ;
      var priceHigh = CorridorGetHighPrice()(rates[rates.Count - 1]);
      return IsCorridorOk(cs, CorridorCrossesCountMinimum);
    }
    private bool IsCorridorOk(CorridorStatistics cs, double corridorCrossesCountMinimum) {
      var spreadRatio = cs.StDev / cs.Spread;
      var spreadOk = CorridorStDevToSpreadMin >= 0 ? spreadRatio > CorridorStDevToSpreadMin : spreadRatio < CorridorStDevToSpreadMin.Abs();
      var lengthOk = CalculateCorridorLengthOk(cs);
      var angleOk = cs.Slope.Angle(PointSize).Abs() < TradingAngleRange;
      return spreadOk && lengthOk && angleOk && (Strategy != Strategies.Breakout || cs.HeightUpDown < cs.Spread * 5);
        var crossesCount = cs.CorridorCrossesCount;
        var isCorCountOk = IsCorridorCountOk(crossesCount, corridorCrossesCountMinimum);
      var corridorHeightToRatesHeight = CalculateCorridorHeightToRatesHeight(cs);
      var isCorridorOk = isCorCountOk;
        //&& CalcCorridorStDevToRatesStDevRatio(cs).Between(corridorStDevRatioMin, corridorStDevRatioMax);
      return isCorridorOk;
    }

    #region IsCorridorCountOk
    private bool IsCorridorCountOk() {
      return IsCorridorCountOk(CorridorStats, CorridorCrossesCountMinimum);
    }
    private bool IsCorridorCountOk(CorridorStatistics cs) {
      return IsCorridorCountOk(cs, CorridorCrossesCountMinimum);
    }
    private static bool IsCorridorCountOk(CorridorStatistics cs, double corridorCrossesCountMinimum) {
      return IsCorridorCountOk(cs.CorridorCrossesCount, corridorCrossesCountMinimum);
    }
    private static bool IsCorridorCountOk(int crossesCount, double corridorCrossesCountMinimum) {
      return double.IsNaN(corridorCrossesCountMinimum) || crossesCount <= corridorCrossesCountMinimum;
    }
    #endregion

    #region CorridorCrossesCount
    class __rateCross {
      public Rate rate { get; set; }
      public bool isUp { get; set; }
      public __rateCross(Rate rate,bool isUp) {
        this.rate = rate;
        this.isUp = isUp;
      }
    }
    private int CorridorCrossesCount0(CorridorStatistics corridornes) {
      return CorridorCrossesCount(corridornes, corridornes.priceHigh, corridornes.priceLow, c => c.HeightUp0, c => c.HeightDown0);
    }
    private int CorridorCrossesCount(CorridorStatistics corridornes) {
      return CorridorCrossesCount(corridornes, corridornes.priceHigh, corridornes.priceLow, c => c.HeightUp, c => c.HeightDown);
    }
    private int CorridorCrossesCount(CorridorStatistics corridornes, Func<Rate, double> getPriceHigh, Func<Rate, double> getPriceLow, Func<CorridorStatistics, double> heightUp, Func<CorridorStatistics, double> heightDown) {
      var rates = corridornes.Rates;
      double[] coeffs = corridornes.Coeffs;

      var rateByIndex = rates.Select((r, i) => new { index = i, rate = r }).Skip(3).ToList();
      var crossPriceHigh = CorridorCrossGetHighPrice();
      var crossUps = rateByIndex
        .Where(rbi => crossPriceHigh(rbi.rate) >= corridornes.priceLine[rbi.index] + heightUp(corridornes))
        .Select(rbi => new __rateCross(rbi.rate, true)).ToList();
      var crossPriceLow = CorridorCrossGetLowPrice();
      var crossDowns = rateByIndex
        .Where(rbi => crossPriceLow(rbi.rate) <= corridornes.priceLine[rbi.index] - heightDown(corridornes))
        .Select(rbi => new __rateCross(rbi.rate, false)).ToList();
      if (crossDowns.Any() || crossUps.Any()) {
        var crosses = crossUps.Concat(crossDowns).OrderByDescending(r => r.rate.StartDate).ToList();
        var crossesList = new List<__rateCross>();
        crossesList.Add(crosses[0]);
        crosses.Aggregate((rp, rn) => {
          if (rp.isUp != rn.isUp) {
            crossesList.Add(rn);
            //corridornes.LegInfos.Add(new CorridorStatistics.LegInfo(rp.rate, rn.rate, BarPeriodInt.FromMinutes()));
          }
          return rn;
        });
        return crossesList.Count;
      }
      return 0;
    }

    #endregion

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
      var tp = 0.0;
      switch (function) {
        case TradingMacroTakeProfitFunction.Corridor0: tp = CorridorHeightByRegression0; break;
        case TradingMacroTakeProfitFunction.Corridor: tp = CorridorHeightByRegression; break;
        case TradingMacroTakeProfitFunction.Corridor1: tp = CorridorStats.StDev * 6; break;
        case TradingMacroTakeProfitFunction.RatesHeight: tp = RatesHeight; break;
        case TradingMacroTakeProfitFunction.RatesHeight_2: tp = RatesHeight / 2; break;
        case TradingMacroTakeProfitFunction.RatesStDev: tp = RatesStDev; break;
        case TradingMacroTakeProfitFunction.SpreadX: tp = SpreadForCorridor * TakeProfitMultiplier; break;
        case TradingMacroTakeProfitFunction.Spread: tp = SpreadForCorridor; break;
        case TradingMacroTakeProfitFunction.Spread2: tp = SpreadForCorridor *2; break;
        case TradingMacroTakeProfitFunction.Spread3: tp = SpreadForCorridor * 3; break;
        case TradingMacroTakeProfitFunction.Spread4: tp = SpreadForCorridor * 4; break;
        case TradingMacroTakeProfitFunction.CorridorPrev: tp = CorridorsRates[1].Height(); break;
        case TradingMacroTakeProfitFunction.BuySellLevels:
          if (_buyLevel == null || _sellLevel == null) return double.NaN;
          tp = ((_buyLevelRate - _sellLevelRate).Abs() + PriceSpreadAverage.GetValueOrDefault(SpreadForCorridor) * 2); break;
        default:
          throw new NotImplementedException(new { function } + "");
      }
      return tp;
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
          Action<PriceChangedEventArgs> a = u => RunPrice(u,Trades);
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
        var minGross = CurrentLoss + trades.Sum(t => t.GrossPL);// +tm.RunningBalance;
        if (MinimumGross > minGross) MinimumGross = minGross;
        CurrentLossPercent = CurrentGross / account.Balance;
        BalanceOnStop = account.Balance + StopAmount.GetValueOrDefault();
        BalanceOnLimit = account.Balance + LimitAmount.GetValueOrDefault();
        SetTradesStatistics(price, trades);
        SetLotSize();
        RunStrategy();
      } catch (Exception exc) { Log = exc; }
      if (sw.Elapsed > TimeSpan.FromSeconds(5))
        Log = new Exception("RunPrice(" + Pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
      //Debug.WriteLine("RunPrice[{1}]:{0} ms", sw.Elapsed.TotalMilliseconds, pair);
    }

    #region LotSize
    public void SetLotSize(Account account=null) {
      if (TradesManager == null) return;
      if (account == null) account = TradesManager.GetAccount();
      if (account == null) return;
      Trade[] trades = Trades;
      LotSize = TradingRatio <= 0 ? 0 : TradingRatio >= 1 ? (TradingRatio * 1000).ToInt()
        : TradesManagerStatic.GetLotstoTrade(account.Balance, TradesManager.Leverage(Pair), TradingRatio, TradesManager.MinimumQuantity);
      LotSizePercent = LotSize / account.Balance / TradesManager.Leverage(Pair);
      LotSizeByLossBuy = AllowedLotSize(trades, true);
      LotSizeByLossSell = AllowedLotSize(trades, false);
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

    static int LotSizeByLoss(ITradesManager tradesManager, double loss, int baseLotSize, double lotMultiplier) {
      return tradesManager.GetLotSize(-(loss / (baseLotSize / 100.0) / lotMultiplier - 0) * baseLotSize, true);
    }
    int LotSizeByLoss() {
      return LotSizeByLoss(TradesManager, CurrentGross, LotSize, TradingDistanceInPips / 100);
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


    public int AllowedLotSizeCore(ICollection<Trade> trades, double? takeProfitPips = null) {
      if (!HasRates) return 0;
      return LotSizeByLoss().Max(LotSize).Min(MaxLotByTakeProfitRatio.ToInt() * LotSize);
      if (MaxLotSize == LotSize) return LotSize;
      var calcLot = CalculateLot(trades,takeProfitPips);
      if (DoAdjustTimeframeByAllowedLot && calcLot > MaxLotSize && Strategy.HasFlag(Strategies.Hot)) {
        while (CalculateLot(Trades, takeProfitPips) > MaxLotSize) {
          var nextLimitBar = Enum.GetValues(typeof(BarsPeriodType)).Cast<int>().Where(bp => bp > (int)BarPeriod).Min();
          BarPeriod = (BarsPeriodType)nextLimitBar;
          RatesInternal.Clear();
          LoadRates();
        }
      }
      var prev = 1;// trades.Select(t => t.Lots * 2 / LotSize).OrderBy(l => l).DefaultIfEmpty(1).Last();
      return prev * Math.Min(MaxLotSize, calcLot);
    }
    public int AllowedLotSize(ICollection<Trade> trades, bool isBuy, double? takeProfitPips = null) {
      return AllowedLotSizeCore(trades.IsBuy(isBuy),takeProfitPips);
    }

    private int CalculateLot(ICollection<Trade> trades, double? takeProfitPips = null) {
      if (Strategy.HasFlag(Strategies.Hot) && takeProfitPips!=null && trades.Count == 0) return LotSize;
      Func<int, int> returnLot = d => Math.Max(LotSize, d);
      if (FreezeStopType == Freezing.Freez)
        return returnLot(trades.Sum(t => t.Lots) * 2);
      var tpInPips = (IsHotStrategy && CloseOnOpen ? TradingStatistics.TakeProfitPips : InPips(CalculateTakeProfit()));
      var tp = CalculateTakeProfitInDollars(tpInPips);// tpInPips * LotSize / 10000;
      var currentGross = IsHotStrategy && CloseOnOpen ? TradingStatistics.CurrentGrossAverage : TradingStatistics.CurrentGrossAverage;
      if (double.IsNaN(currentGross)) return 0;
      return returnLot(CalculateLotCore(currentGross - tp, takeProfitPips.GetValueOrDefault(tpInPips)));
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
                startDate = CorridorStats.StartDate.AddMinutes(-(int)BarPeriod * intervalToAdd);
                var periodsByStartDate = RatesInternal.Count(r => r.StartDate >= startDate) + intervalToAdd;
                periodsBack = periodsBack.Max(periodsByStartDate);
              }
            }
            RatesInternal.RemoveAll(r => !r.IsHistory);
            if (RatesInternal.Count != RatesInternal.Distinct().Count()) {
              var ri = RatesInternal.ToList();
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
    protected void OnPropertyChanged(Expression<Func<object>> property) {
      OnPropertyChanged(Lib.GetLambda(property));
    }
    protected override void OnPropertyChanged(string property) {
      base.OnPropertyChanged(property);
      OnPropertyChangedCore(property);
      //OnPropertyChangedQueue.Add(this, property);
    }
    public void OnPropertyChangedCore(string property) {
      if (EntityState == System.Data.EntityState.Detached) return;
      //_propertyChangedTaskDispencer.RunOrEnqueue(property, () => {
      switch (property) {
        case TradingMacroMetadata.TradingDistanceFunction:
        case TradingMacroMetadata.CurrentLoss:
          _tradingDistanceMax = 0;
          SetLotSize();
          break;
        case TradingMacroMetadata.WaveLengthMin:
        case TradingMacroMetadata.CorridorCalcMethod:
        case TradingMacroMetadata.CorridorCrossHighLowMethod:
        case TradingMacroMetadata.CorridorCrossesCountMinimum:
        case TradingMacroMetadata.CorridorHighLowMethod:
        case TradingMacroMetadata.CorridorStartDate:
        case TradingMacroMetadata.CorridorStDevRatioMin:
        case TradingMacroMetadata.CorridorStDevRatioMax:
        case TradingMacroMetadata.TradingAngleRange:
        case TradingMacroMetadata.StDevAverageLeewayRatio:
        case TradingMacroMetadata.StDevTresholdIterations:
          RatesArray = null;
          CorridorStats = null;
          OnScanCorridor(RatesArraySafe);
          break;
        case TradingMacroMetadata.Pair:
          _pointSize = double.NaN;
          goto case TradingMacroMetadata.CorridorBarMinutes;
        case TradingMacroMetadata.BarsCount:
        case TradingMacroMetadata.CorridorBarMinutes:
        case TradingMacroMetadata.LimitBar:
          CorridorStats = null;
          CorridorStartDate = null;
          Strategy = Strategies.None;
          RatesInternal.Clear();
          OnLoadRates();
          break;
        case TradingMacroMetadata.RatesInternal:
          RatesArraySafe.Count();
          OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
          break;
        case TradingMacroMetadata.Strategy:
          DisposeOpenTradeByMASubject();
          _tradingDistanceMax = 0;
          goto case TradingMacroMetadata.IsSuppResManual;
        case TradingMacroMetadata.IsSuppResManual:
        case TradingMacroMetadata.TakeProfitFunction:
          OnScanCorridor(RatesArray);
          goto case TradingMacroMetadata.RangeRatioForTradeLimit;
        case TradingMacroMetadata.RangeRatioForTradeLimit:
        case TradingMacroMetadata.RangeRatioForTradeStop:
        case TradingMacroMetadata.IsColdOnTrades:
        case TradingMacroMetadata.IsPriceSpreadOk:
          SetEntryOrdersBySuppResLevels();
          break;
        case TradingMacroMetadata.MovingAverageType:
        case TradingMacroMetadata.PriceCmaPeriod:
        case TradingMacroMetadata.PriceCmaLevels:
          SetMA();
          RaiseShowChart();
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
        if(_priceSpreadAverage == spread)return;
        _priceSpreadAverage = spread;
        OnPropertyChanged(() => PriceSpreadAverage);
        SetPriceSpreadOk();
      }
    }
    partial void OnLimitBarChanging(int newLimitBar) {
      if (newLimitBar == (int)BarPeriod) return;
      OnLoadRates();
    }
    Strategies[] _exceptionStrategies = new[] { Strategies.Massa, Strategies.Hot };
    partial void OnCorridorBarMinutesChanging(int value) {
      if (value == CorridorBarMinutes) return;
      if (!_exceptionStrategies.Any(s => Strategy.HasFlag(s)))
        Strategy = Strategies.None;
      OnLoadRates();
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

    void DefferedRun<T>(T value, double delayInSeconds, Action<T> run) {
      Observable.Return(value).Throttle(TimeSpan.FromSeconds(delayInSeconds)).SubscribeOnDispatcher().Subscribe(run, exc => Log = exc);
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
          OnPropertyChanged(TradingMacroMetadata.StDevToCorridorHeight0Real);
          OnPropertyChanged(TradingMacroMetadata.IsStDevOk);
          OnPropertyChanged(TradingMacroMetadata.TrendNessRatio);
        }
      }
    }
    public double TakeProfitDistanceInPips { get { return InPips(TakeProfitDistance); } }

    public double StDevToCorridorHeight0Real { get { return RatesStDev / CorridorStats.HeightUpDown0; } }
    public bool IsStDevOk { get { return StDevToCorridorHeight0Real <= CorridorStDevRatioMin; } }


    public double RatesStDevToRatesHeightRatio { get { return RatesStDev / RatesHeight; } }
    public double HeightToStDevRatio { get { return RatesHeight / RatesStDev; } }

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

    public double RatesHeight { get; set; }
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

    private List<double> _StDevAverages = new List<double>();
    public List<double> StDevAverages {
      get { return _StDevAverages; }
      set { _StDevAverages = value; }
    }
    public double StDevAverage { get; set; }
    public double StDevAverageInPips { get { return InPips(StDevAverage); } }


    public double VolumeAverageHigh { get; set; }

    public double VolumeAverageLow { get; set; }

    private List<List<Rate>> _CorridorsRates = new List<List<Rate>>();
    private Store.SuppRes _buyLevel;
    private Store.SuppRes _sellLevel;
    private IList<IList<Rate>> _waves;
    private IList<Rate> _waveHigh;
    private double _buyLevelRate;
    private double _sellLevelRate;
    public List<List<Rate>> CorridorsRates {
      get { return _CorridorsRates; }
      set { _CorridorsRates = value; }
    }

    public Tuple<Rate, Rate, int> _waveLong { get; set; }
  }
}
