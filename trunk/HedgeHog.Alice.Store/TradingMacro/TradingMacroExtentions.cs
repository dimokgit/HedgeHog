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
        LockPriceCmaPeriod(true);
        lock (_OpenTradeByMASubjectLocker) {
          if (OpenTradeByMASubject != null) {
            OpenTradeByMASubject.OnCompleted();
          }
        }
      }
    }
    void CreateOpenTradeByMASubject(bool isBuy, Action<Action> openTradeAction) {
      LockPriceCmaPeriod();
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
        Debug.WriteLine(Pair + "." + key + " is pending:" + PendingEntryOrders[key]);
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
        //this.CurrentLoss = 0;
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
          _TakeProfitPips = value;
          OnPropertyChanged("TakeProfitPips");
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
      if (e.PropertyName == Metadata.CorridorStatisticsMetadata.StartDate) {
        if (!IsGannAnglesManual) SetGannAngleOffset(cs);
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
        RatesStDev = _corridorBig.StDev;
      }
    }

    public bool HasCorridor { get { return CorridorStats.IsCurrent; } }
    private CorridorStatistics _CorridorStats;
    public CorridorStatistics CorridorStats {
      get { return _CorridorStats ?? new CorridorStatistics(); }
      set {
        var datePrev = _CorridorStats == null ? DateTime.MinValue : _CorridorStats.StartDate;
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
                , r => Strategy == Strategies.Breakout && false ? r.PriceAvg1 : MagnetPrice, (r, d) => r.PriceAvg1 = d
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
            SetLotSize();
            EntryOrdersAdjust();
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

    void LockPriceCmaPeriod(bool unLock = false) { _priceCmaPeriodLocked = unLock ? null : (double?)PriceCmaPeriodByStDevRatio; }
    double? _priceCmaPeriodLocked =null;

    public double PriceCmaPeriodByStDevRatio {
      get { 
        if(_priceCmaPeriodLocked.HasValue)
          _priceCmaPeriodLocked = _priceCmaPeriodLocked.Value.Max(CorridorStDevToRatesStDevRatio);
        return _priceCmaPeriodLocked.GetValueOrDefault(CorridorStDevToRatesStDevRatio).Max(PriceCmaPeriod.Max(1)); 
      }
    }


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
      if (!Trades.Any() && CurrentGross >= 0)
        SuppRes.ToList().ForEach(sr => { sr.CanTrade = false; sr.CorridorDate = CorridorStats.StartDate; });
    }

    private void RaisePositionsChanged() {
      OnPropertyChanged("PositionsSell");
      OnPropertyChanged("PositionsBuy");
      OnPropertyChanged("PipsPerPosition");
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
        RatesInternal.Clear();
        CurrentLoss = MinimumGross = HistoryMaximumLot = 0;
        SuppRes.ToList().ForEach(sr => { sr.CanTrade = true; sr.TradesCount = 0; });
        CorridorStartDate = null;
        CorridorStats = null;
        DisposeOpenTradeByMASubject();
        _waveRates.Clear();
        var currentPosition = -1;
        var indexCurrent = 0;
        while (!args.MustStop && indexCurrent < rates.Count) {
          if (currentPosition > 0 && currentPosition != args.CurrentPosition) {
            //cp= 100*(i-BarsCount)/(rates.Count-BarsCount)
            //cp*(rates.Count-BarsCount)/100 +BarsCount= i
            //i = cp*(rates.Count-BarsCount)+BarsCount
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
            TradesManager.RaisePriceChanged(Pair, RateLast);
            var d = Stopwatch.StartNew();
            if (rate != null) {
              args.CurrentPosition = currentPosition = (100.0 * (indexCurrent - BarsCount) / (rates.Count - BarsCount)).ToInt();
              var price = new Price(Pair, RateLast, TradesManager.ServerTime, TradesManager.GetPipSize(Pair), TradesManager.GetDigits(Pair), true);
              RunPriceChanged(new PriceChangedEventArgs(Pair, price, TradesManager.GetAccount(), new Trade[0]), null);
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
          _CorridorAngle = CalculateAngle(value);
          OnPropertyChanged(TradingMacroMetadata.CorridorAngle);
        }
      }
    }

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

    double SpreadForSuppRes { get { return Math.Max(SpreadShort, SpreadLong); } }

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
    private Store.SuppRes[] SupportsNotCurrent() {
      return SuppResNotCurrent(Supports);
    }

    private Store.SuppRes ResistanceLow() {
      return Resistances.OrderBy(s => s.Rate).First();
    }

    private Store.SuppRes ResistanceHigh() {
      return Resistances.OrderBy(s => s.Rate).Last();
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
        //MagnetPrice = CorridorStats.Rates.Average(r => r.PriceAvg);
        MagnetPrice = _levelCounts[0].Item2;
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
    public List<Rate> RatesArray { get { return _rateArray; } }
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
            if (rateLast != RateLast || rs != _ratesSpreadSum || !_rateArray.Any()) {
              _ratesSpreadSum = rs;
              RateLast = RatesInternal[RatesInternal.Count - 1];
              RatePrev = RatesInternal[RatesInternal.Count - 2];
              RatePrev1 = RatesInternal[RatesInternal.Count - 3];
              //_rateArray = GetRatesForStDev(GetRatesSafe()).ToArray();
              _rateArray = GetRatesSafe().ToList();

              #region Spread
              _RateDirection = _rateArray.Skip(_rateArray.Count() - 2).ToList();
              var ratesForSpread = BarPeriod == 0 ? _rateArray.GetMinuteTicks(1).OrderBars().ToList() : _rateArray;
              var spreadShort = ratesForSpread.Skip(ratesForSpread.Count() - 10).ToList().AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
              var spreadLong = ratesForSpread.AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
              var spreadForTrade = ratesForSpread.Select(r => r.Spread).ToList().AverageByIterations(2, true).Average();
              VolumeShort = 0;// ratesForSpread.Skip(ratesForSpread.Count() - 10).ToList().AverageByIterations(r => r.Volume, 2).Average(r => r.Volume);
              VolumeLong = 0;// ratesForSpread.AverageByIterations(r => r.Volume, 2).Average(r => r.Volume);
              SetShortLongSpreads(spreadShort, spreadLong, spreadForTrade);
              #endregion

              if (IsInVitualTrading)
                Trades.ToList().ForEach(t => t.UpdateByPrice(TradesManager, CurrentPrice));

              RatesHeight = _rateArray.Height();//CorridorStats.priceHigh, CorridorStats.priceLow);
              PriceSpreadAverage = _rateArray.Select(r => r.PriceSpread).Average();//.ToList().AverageByIterations(2).Average();

              if (!SuppRes.IsBuy(true).Any()) {
                AddSuppRes(PriceSpreadAverage.Value, false);
              }
              if (!SuppRes.IsBuy(false).Any()) {
                AddSuppRes(PriceSpreadAverage.Value, true);
              }

              OnPropertyChanged(TradingMacroMetadata.PriceCmaPeriodByStDevRatio);
              SetMA();
              if (false) _rateArray.ReverseIfNot().SetStDevPrice(GetPriceMA);
              SetMagnetPrice();
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

    public Rate[] RatesDirection { get; protected set; }
    IList<Rate> _RateDirection;
    public int RateDirection { get { return Math.Sign(_RateDirection[1].PriceAvg - _RateDirection[0].PriceAvg); } }
    public double InPips(double? d) {
      return TradesManager == null ? double.NaN : TradesManager.InPips(Pair, d);
    }

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
    public double? _SpreadShortLongRatioAverage;
    public double SpreadShortLongRatioAverage {
      get { return _SpreadShortLongRatioAverage.GetValueOrDefault(SpreadShortToLongRatio); }
      set {
        _SpreadShortLongRatioAverage = Lib.Cma(_SpreadShortLongRatioAverage, BarsCount / 10.0, value);
        OnPropertyChanged(Metadata.TradingMacroMetadata.SpreadShortLongRatioAverage);
      }
    }

    public bool IsSpreadShortLongRatioAverageOk {
      get { return SpreadShortToLongRatio > SpreadShortLongRatioAverage; }
    }

    public void SetShortLongSpreads(double spreadShort, double spreadLong,double spreadForTrade) {
      SpreadShort = spreadShort;
      SpreadLong = spreadLong;
      SpreadForTrade = spreadForTrade;
      if(RatesArray.Any())
        SpreadForCorridor = CalcSpreadForCorridor(RatesArray);
      SpreadShortLongRatioAverage = SpreadShortToLongRatio;
      OnPropertyChanged(TradingMacroMetadata.SpreadShort);
      OnPropertyChanged(TradingMacroMetadata.SpreadShortInPips);
      OnPropertyChanged(TradingMacroMetadata.SpreadLong);
      OnPropertyChanged(TradingMacroMetadata.SpreadLongInPips);
      OnPropertyChanged(TradingMacroMetadata.SpreadForCorridorInPips);
      OnPropertyChanged(TradingMacroMetadata.SpreadShortToLongRatio);
      OnPropertyChanged(TradingMacroMetadata.IsSpreadShortLongRatioAverageOk);
      OnPropertyChanged(TradingMacroMetadata.CorridorHeightToSpreadRatio);
      OnPropertyChanged(TradingMacroMetadata.CorridorHeight0ToSpreadRatio);
    }
    private double CalcSpreadForCorridor(ICollection<Rate> rates,int iterations = 3) {
      try {
        var spreads = rates.Select(r => r.AskHigh - r.BidLow).ToList();
        if (spreads.Count == 0) return double.NaN;
        var spreadLow = spreads.AverageByIterations(iterations, true);
        var spreadHight = spreads.AverageByIterations(iterations, false);
        if (spreadLow.Count == 0 && spreadHight.Count == 0)
          return CalcSpreadForCorridor(rates, iterations - 1);
        var sa = spreads.Except(spreadLow.Concat(spreadHight)).Average();
        var sstdev = 0;// spreads.StDev();
        return sa + sstdev;
      } catch (Exception exc) {
        Log = exc;
        return double.NaN;
      }
    }

    #region SpreadForTrade
    public double SpreadForTradeInPIps { get { return InPips(SpreadForTrade); } }
    private double _SpreadForTrade;
    public double SpreadForTrade {
      get { return _SpreadForTrade; }
      set {
        if (_SpreadForTrade != value) {
          _SpreadForTrade = value;
          OnPropertyChanged(TradingMacroMetadata.SpreadForTrade);
          OnPropertyChanged(TradingMacroMetadata.SpreadForTradeInPIps);
        }
      }
    }

    #endregion
    double _SpreadShort;
    public double SpreadShort {
      get { return _SpreadShort; }
      set { _SpreadShort = value; }
    }
    public double SpreadShortInPips { get { return InPips(SpreadShort); } }

    double _SpreadLong;
    public double SpreadLong {
      get { return _SpreadLong; }
      set { _SpreadLong = value; }
    }
    public double SpreadLongInPips { get { return InPips(SpreadLong); } }

    double SpreadMin { get { return Math.Min(SpreadLong, SpreadShort); } }
    double SpreadMax { get { return Math.Max(SpreadLong, SpreadShort); } }
    double SpreadForCorridor { get; set; }
    public double SpreadForCorridorInPips { get { return InPips(SpreadForCorridor); } }


    public double SpreadShortToLongRatio { get { return SpreadShort / SpreadLong; } }

    #endregion

    public double TradingDistanceInPips {
      get { return InPips(TradingDistance); }
    }
    public double TradingDistance {
      get {
        if (!HasRates) return double.NaN;
        var multiplier = TradingDistanceFunction == TradingMacroTakeProfitFunction.Spread ? Trades.Count().Max(1) : 1;
        return (GetValueByTakeProfitFunction(TradingDistanceFunction) * multiplier).Max(PriceSpreadAverage.GetValueOrDefault(double.NaN) * 3);
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
        switch (Strategy) {
          case Strategies.Breakout:
            return StrategyBreakout;
          case Strategies.None:
            return () => { };
        }
        throw new NotSupportedException("Strategy " + Strategy + " is not supported.");
      }
    }
    void RunStrategy() { StrategyAction(); }

    void StrategyBreakout() {
      StrategyEnterBreakout032();
      Action<bool, Func<double, bool>> openTrade = (isBuy, canTrade) => {
        var suppReses = EnsureActiveSuppReses().OrderBy(sr => sr.TradesCount).ToList();
        var minTradeCount = suppReses.Min(sr => sr.TradesCount);
        foreach (var suppRes in EnsureActiveSuppReses(isBuy)) {
          var level = suppRes.Rate;
          if (canTrade(level)) {
            if (suppRes.TradesCount == minTradeCount)
              suppReses.IsBuy(!isBuy).OrderBy(sr => (sr.Rate - suppRes.Rate).Abs()).Take(1).ToList()
                .ForEach(sr => sr.TradesCount = suppRes.TradesCount - 1);
            if (suppRes.TradesCount <= 0 && !HasTradesByDistance(isBuy)) {
              CheckPendingAction("OT", (pa) => {
                var lot = suppRes.CanTrade ? EntryOrderAllowedLot(isBuy) : Trades.IsBuy(!isBuy).Lots();
                if (lot > 0) {
                  pa();
                  TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
                }
              });
              return;
            }
          }
        }
      };
      if (SupportLow().Rate != ResistanceHigh().Rate && (RateLast.StartDate - RatePrev1.StartDate).TotalMinutes / BarPeriodInt <= 3) {
        var priceLast = CalculateLastPrice(RateLast, r => r.PriceAvg);
        var pricePrev = RatePrev1.PriceAvg;
        openTrade(true, level => priceLast > level && pricePrev <= level);
        openTrade(false, level => priceLast < level && pricePrev >= level);
      }
    }

    private void StrategyEnterBreakout01() {
      if (RateLast.PriceAvg > RateLast.PriceAvg21) {
        ResistanceLow().Rate = RateLast.PriceAvg21;
        SupportLow().Rate = RateLast.PriceAvg2;
      }
      if (RateLast.PriceAvg < RateLast.PriceAvg31) {
        SupportLow().Rate = RateLast.PriceAvg31;
        ResistanceLow().Rate = RateLast.PriceAvg3;
      }
    }
    private void StrategyEnterBreakout02() {
      if (RateLast.PriceAvg > RateLast.PriceAvg21) {
        ResistanceLow().Rate = RateLast.PriceAvg21 + CorridorStats.StDev;
        SupportLow().Rate = RateLast.PriceAvg21;
      }
      if (RateLast.PriceAvg < RateLast.PriceAvg31) {
        SupportLow().Rate = RateLast.PriceAvg31 - CorridorStats.StDev;
        ResistanceLow().Rate = RateLast.PriceAvg31;
      }
    }

    private void StrategyEnterBreakout03() {
      StrategyExitByGross();
      if (HasCorridor) {
        ResistanceHigh().Rate = RateLast.PriceAvg21;
        if (SuppResLevelsCount == 2)
          SupportHigh().Rate = RateLast.PriceAvg2;
        SupportLow().Rate = RateLast.PriceAvg31;
        if (SuppResLevelsCount == 2)
          ResistanceLow().Rate = RateLast.PriceAvg3;
      }
      var canTrade = IsCorridorStDevToRatesStDevRatioOk;
      SuppRes.ToList().ForEach(sr => sr.CanTrade = canTrade);
    }

    private void StrategyEnterBreakout031() {
      StrategyExitByGross031();
      if (HasCorridor) {
        var tradeCorridorHeightOk = RateLast.PriceAvg21 - RateLast.PriceAvg31 > SpreadForCorridor * 3;
        ResistanceHigh().Rate = RateLast.PriceAvg21;
        if (SuppResLevelsCount == 2)
          SupportHigh().Rate = tradeCorridorHeightOk ? RateLast.PriceAvg2 : double.MaxValue;
        SupportLow().Rate = RateLast.PriceAvg31;
        if (SuppResLevelsCount == 2)
          ResistanceLow().Rate = tradeCorridorHeightOk ? RateLast.PriceAvg3 : double.MinValue;
      }
      var canTrade = IsCorridorStDevToRatesStDevRatioOk;
      SuppRes.ToList().ForEach(sr => sr.CanTrade = canTrade);
    }

    private void StrategyEnterBreakout032() {
      StrategyExitByGross031();
      if (!IsInVitualTrading) return;
      if (CorridorStats.StartDate > ResistanceHigh().CorridorDate.AddMinutes(BarPeriodInt * 60 * 3) && CorridorStats.StartDate > RatesArray[0].StartDate.AddMinutes(BarPeriodInt * 30)) {
        var stDev = CorridorStats.StDev;
        var rateLast = CorridorStats.Rates.LastByCount();
        
        ResistanceHigh().Rate = rateLast.PriceAvg21 + stDev;
        SupportLow().Rate = rateLast.PriceAvg31 - stDev;
        ResistanceHigh().CanTrade = SupportLow().CanTrade = CanTradeByTradeCorridor();

        if (SuppResLevelsCount == 2) {
          SupportHigh().Rate = rateLast.PriceAvg21 + stDev;
          SupportHigh().CanTrade = false;
        }
        if (SuppResLevelsCount == 2) {
          ResistanceLow().Rate = rateLast.PriceAvg31 - stDev;
          ResistanceLow().CanTrade = false;
        }
        //SuppRes.ToList().ForEach(sr => sr.CanTrade = canMove);
      }
    }

    private bool CanTradeByTradeCorridor() {
      return (ResistanceHigh().Rate - SupportLow().Rate).Between(SpreadForCorridor*4,  RatesHeight / 3);
    }



    bool _canSell;
    public bool CanSell {
      get { return _canSell; }
      set { 
        _canSell = value;
        if (value) _canBuy = false;
      }
    }
    bool _canBuy;
    public bool CanBuy {
      get { return _canBuy; }
      set {
        _canBuy = value;
        if (value) _canSell = false;
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
    bool StrategyExitByGross() { return StrategyExitByGross(() => false); }
    bool StrategyExitByGross(Func<bool> or) {
      if (Trades.Any()) {
        var exitByProfit = CurrentGrossInPips >= SpreadForCorridorInPips.Max(TakeProfitPips / Trades.Positions(LotSize));
        if (exitByProfit || or()) {
          TradesManager.ClosePair(Pair, Trades[0].IsBuy);
          return true;
        }
        if( Trades.Lots() > LotSize && CurrentGross > 0)
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
          if(RatesArraySafe.Count>0)
            _RateDirection = RatesArray.Skip(_rateArray.Count() - 2).ToList();
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
      var td = TradingDistanceInPips.Max(trades.DistanceMaximum());
      return TakeProfitPips == 0 || double.IsNaN(TradingDistance)
        || (trades.Any() && trades.Max(t => t.PL) > -(td + PriceSpreadAverageInPips));
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
        #region Crosses
        {
          var startDate = ratesForCorridor.Take(ratesForCorridor.Count-30).OrderByDescending(r => r.Corridorness).First().StartDate;
          var startRate = ScanCrosses(reversed.TakeWhile(r => r.StartDate >= startDate).ToList()).OrderByDescending(l=>l.Item1).First().Item2;
          startDate = ratesForCorridor.Where(r => startRate.Between(r.BidLow, r.AskHigh)).Min(r => r.StartDate);//.Max(CorridorStats.StartDate);
          var ratesForCross = reversed.TakeWhile(r => r.StartDate >= startDate).ToList();
          var endDate = ratesForCross.Where(r => startRate.Between(r.BidLow, r.AskHigh)).Max(r => r.StartDate);
          var corridorRates = ratesForCross.Where(r => r.StartDate.Between(startDate, endDate)).ToList();
          _levelCounts = ScanCrosses(corridorRates);
          _levelCounts.Sort((t1, t2) => -t1.Item1.CompareTo(t2.Item1));
          var coeffs = Regression.Regress(corridorRates.ReverseIfNot().Select(r => r.PriceAvg).ToArray(), 1);
          var median = corridorRates.Average(r => r.PriceAvg);
          var stDev = corridorRates.Select(r => (CorridorGetHighPrice()(r) - median).Abs()).ToList().StDev();
          crossedCorridor = new CorridorStatistics(this, corridorRates, stDev, coeffs);
        }
        if (false) {
          var ratesForCross = ratesForCorridor.OrderBy(r => r.PriceAvg).ToList();
          _levelCounts = ScanCrosses(ratesForCross, ratesForCross[0].PriceAvg, ratesForCross[ratesForCross.Count - 1].PriceAvg);
          _levelCounts.Sort((t1, t2) => -t1.Item1.CompareTo(t2.Item1));
          var levelMaxCross = _levelCounts[0].Item2;
          var crossedRates = ratesForCross.Where(r => levelMaxCross.Between(r.BidAvg, r.AskAvg)).OrderBars().ToList();
          var rateStart = crossedRates[0];
          var rateEnd = crossedRates[crossedRates.Count - 1];
          var corridorRates = reversed.Where(r=>r.StartDate.Between(rateStart.StartDate,rateEnd.StartDate)).ToList();
          var stDev = corridorRates.Select(r => (CorridorGetHighPrice()(r) - levelMaxCross).Abs()).ToList().StDev();
          var coeffs = Regression.Regress(corridorRates.ReverseIfNot().Select(r => r.PriceAvg).ToArray(), 1);
          crossedCorridor = new CorridorStatistics(this, corridorRates,stDev,coeffs);
            //.ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod);
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
          csOld.Spread = CalcSpreadForCorridor(csOld.Rates);
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
        return pricePrev * (1 - ratio) + priceCurrent * ratio;
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
        case TradingMacroTakeProfitFunction.Corridor: tp = CorridorHeightByRegression; break;
        case TradingMacroTakeProfitFunction.Corridor0: tp = CorridorHeightByRegression0.Min(RatesStDev); break;
        case TradingMacroTakeProfitFunction.RatesHeight: tp = RatesHeight; break;
        case TradingMacroTakeProfitFunction.RatesStDev: tp = RatesStDev; break;
        case TradingMacroTakeProfitFunction.StDev: tp = CorridorStats.StDev; break;
        case TradingMacroTakeProfitFunction.Spread: tp = SpreadForCorridor; break;
        case TradingMacroTakeProfitFunction.Spread2: tp = SpreadForCorridor *2; break;
        case TradingMacroTakeProfitFunction.Spread3: tp = SpreadForCorridor * 3; break;
        case TradingMacroTakeProfitFunction.Spread4: tp = SpreadForCorridor * 4; break;
        case TradingMacroTakeProfitFunction.Corr0_CorrB0:
          tp = CorridorHeightByRegression0.Max(CorridorBig.HeightUpDown0); break;
      }
      return tp;
    }


    public double CommissionByTrade(Trade trade) { return TradesManager.CommissionByTrade(trade); }

    bool IsInVitualTrading { get { return TradesManager is VirtualTradesManager; } }
    private bool CanTrade() {
      return IsInVitualTrading || !IsInPlayback;
    }

    BroadcastBlock<PriceChangedEventArgs> _runPriceBroadcast;
    public BroadcastBlock<PriceChangedEventArgs> RunPriceBroadcast {
      get {
        if (_runPriceBroadcast == null) {
          _runPriceBroadcast = new BroadcastBlock<PriceChangedEventArgs>(u => u);
          _runPriceBroadcast.AsObservable()
            .Subscribe(u => RunPrice(u,Trades));
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

    public int AllowedLotSizeCore(ICollection<Trade> trades, double? takeProfitPips = null) {
      if (!HasRates) return 0;
      return StrategyLotSizeByLossAndDistance(trades);
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
            var useDefaultInterval = !DoStreatchRates || dontStreachRates || CorridorStats == null || CorridorStats.StartDate == DateTime.MinValue;
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
                if (false && DensityMin > -100)
                  DensityMin = ps.PriceStatistics(Pair).BidHighAskLowSpread;
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
        case TradingMacroMetadata.CurrentLoss:
          SetLotSize();
          break;
        case TradingMacroMetadata.DensityMin:
        case TradingMacroMetadata.CorridorCalcMethod:
        case TradingMacroMetadata.CorridorCrossHighLowMethod:
        case TradingMacroMetadata.CorridorCrossesCountMinimum:
        case TradingMacroMetadata.CorridorHighLowMethod:
        case TradingMacroMetadata.CorridorHeightMultiplier:
        case TradingMacroMetadata.CorridorStartDate:
        case TradingMacroMetadata.CorridorStDevRatioMin:
        case TradingMacroMetadata.CorridorStDevRatioMax:
        case TradingMacroMetadata.TradingAngleRange:
          CorridorStats = null;
          OnScanCorridor(RatesArray);
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
  }
}
