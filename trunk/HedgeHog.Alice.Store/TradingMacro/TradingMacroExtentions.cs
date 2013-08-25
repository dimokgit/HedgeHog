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
using HedgeHog.Shared.Messages;
using Hardcodet.Util.Dependencies;
using ChainedObserver;
using HedgeHog.UI;
using System.ComponentModel.Composition;
using ReactiveUI;
using HedgeHog.NewsCaster;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    #region Subjects
    static TimeSpan THROTTLE_INTERVAL = TimeSpan.FromSeconds(1);

    static ActionBlock<TradingMacro> _loadRatesAction = new ActionBlock<TradingMacro>(tm => tm.LoadRates());
    public void OnLoadRates() {
      broadcastLoadRates.Post(u => LoadRates());
      //_loadRatesAction.SendAsync(this);
    }

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

    #region Snapshot control
    SnapshotArguments _SnapshotArguments;

    public SnapshotArguments SnapshotArguments {
      get {
        if (_SnapshotArguments == null) {
          _SnapshotArguments = new SnapshotArguments();
          _SnapshotArguments.ShowSnapshot += (s, e) => ShowSnaphot(SnapshotArguments.DateStart, SnapshotArguments.DateEnd);
          _SnapshotArguments.AdvanceSnapshot += (s, e) => AdvanceSnapshot(SnapshotArguments.DateStart, SnapshotArguments.DateEnd, false);
          _SnapshotArguments.DescendSnapshot += (s, e) => AdvanceSnapshot(SnapshotArguments.DateStart, SnapshotArguments.DateEnd, true);
          _SnapshotArguments.MatchSnapshotRange += (s, e) => MatchSnapshotRange();
        }
        return _SnapshotArguments;
      }
    }
    void MatchSnapshotRange() {
      var dateStart = new DateTimeOffset(CorridorStartDate.GetValueOrDefault(CorridorStats.StartDate));
      var dateEnd = new DateTimeOffset(CorridorStopDate.IfMin(RatesArraySafe.LastBC().StartDate));
      var dateRangeStart = dateStart.AddDays(-1);
      var dateRangeEnd = dateStart.AddDays(1);
      Func<Rate, double> price = r => r.CrossesDensity;// r.PriceCMALast;
      var ratesSample = RatesArray.SkipWhile(r => r.StartDate2 < dateStart).TakeWhile(r => r.StartDate2 <= dateEnd).Select(price).ToArray();
      var interval = ratesSample.Count();
      var heightSample = ratesSample.Height();
      var hourStart = dateStart.AddHours(-1).UtcDateTime.Hour;
      var hourEnd = dateStart.AddHours(1).UtcDateTime.Hour;
      Func<DateTimeOffset, bool> isHourOk = d => hourEnd > hourStart ? d.UtcDateTime.Hour.Between(hourStart, hourEnd) : !d.UtcDateTime.Hour.Between(hourStart, hourEnd);
      Func<DateTimeOffset, bool> isDateOk = d => d.DayOfWeek == dateStart.DayOfWeek && isHourOk(d);
      var dateLoadStart = dateStart.AddYears(-1);
      var ratesHistory = GlobalStorage.GetRateFromDB(Pair, dateLoadStart.DateTime, int.MaxValue, BarPeriodInt);
      ratesHistory.Take(interval).ForEach(r => r.CrossesDensity = 0);
      ratesHistory.SetCma((p, r) => r.PriceAvg, PriceCmaLevels, PriceCmaLevels);
      Enumerable.Range(0, ratesHistory.Count() - interval).AsParallel().ForAll(i => {
        try {
          var range = new Rate[interval];
          ratesHistory.CopyTo(i, range, 0, interval);
          var cd = range.Select(_priceAvg).CrossesInMiddle(range.Select(GetPriceMA())).Count / (double)interval;
          range.LastBC().CrossesDensity = cd;
        } catch (Exception exc) {
          Debugger.Break();
        }
      });
      var priceHistory = ratesHistory.Select(price).ToList();
      var correlations = new ConcurrentDictionary<int, double>();
      Enumerable.Range(0, ratesHistory.Count() - interval).AsParallel().ForAll(i => {
        if (!ratesHistory[i].StartDate2.Between(dateRangeStart, dateRangeEnd)) {
          var range = new double[interval];
          priceHistory.CopyTo(i, range, 0, interval);
          if (isDateOk(ratesHistory[i].StartDate2) /*&& heightSample.Ratio(range.Height()) < 1.1*/)
            correlations.TryAdd(i, AlgLib.correlation.spearmanrankcorrelation(range, ratesSample, range.Length));
        }
      });
      var sorted = correlations.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
      Func<KeyValuePair<int, double>, KeyValuePair<int, double>, double, bool> compare = (d1, d2, d) => (d1.Key - d2.Key).Abs() <= d;
      var maxCorr1 = sorted.GroupBy(c => c, new ClosenessComparer<KeyValuePair<int, double>>(60, compare)).ToArray();
      var maxCorr2 = maxCorr1.Select(cg => cg.Key).ToArray();
      var avgIterations = 5;
      var maxCorr3 = maxCorr2.Take(avgIterations);
      maxCorr3.ForEach(kv => {
        var startDate = ratesHistory[kv.Key].StartDate2;
        try {
          var ds = startDate.Date + dateStart.TimeOfDay;
          var de = ds + (dateEnd - dateStart);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(
            new ShowSnapshotMatchMessage(ds, de/*, BarsCount*/, BarPeriodInt, kv.Value));
        } catch (Exception exc) {
          Log = new Exception(new { startDate } + "", exc);
        }
      });

    }
    void AdvanceSnapshot(DateTime? dateStart, DateTime? dateEnd, bool goBack = false) {
      var minutes = ((goBack ? -1 : 1) * BarsCount * BarPeriodInt / 10).FromMinutes();
      if (SnapshotArguments.DateEnd != null)
        SnapshotArguments.DateEnd += minutes;
      else SnapshotArguments.DateStart += minutes;
      ShowSnaphot(SnapshotArguments.DateStart, SnapshotArguments.DateEnd);
    }
    IDisposable _scheduledSnapshot;
    void ShowSnaphot(DateTime? dateStart, DateTime? dateEnd) {
      var message = new List<string>();
      if (TradesManager == null) message.Add("TradesManager is null");
      if (dateStart == null && dateEnd == null) message.Add("SnapshotArguments.Date(Start and End) are null.");
      //if (dateStart != null && dateEnd != null) message.Add("SnapshotArguments.Date(Start or End) must be null.");
      if (message.Any()) {
        Log = new Exception(string.Join("\n", message));
        return;
      }
      RatesArray.Clear();
      RatesInternal.Clear();
      try {
        var rates = dateStart.HasValue && dateEnd.HasValue
          ? GlobalStorage.GetRateFromDBByDateRange(Pair, dateStart.Value, dateEnd.Value, BarPeriodInt).ToArray()
          : dateStart.HasValue
          ? GlobalStorage.GetRateFromDB(Pair, dateStart.Value, BarsCount, BarPeriodInt).ToArray()
          : GlobalStorage.GetRateFromDBBackward(Pair, dateEnd.Value, BarsCount, BarPeriodInt).ToArray();
        if (dateStart.HasValue && dateEnd.HasValue)
          _CorridorBarMinutes = rates.Count();
        RatesInternal.AddRange(rates);
        while (RatesInternal.Count < BarsCount)
          RatesInternal.Add(RatesInternal.LastBC());
        if (CorridorStartDate.HasValue && !CorridorStartDate.Value.Between(RatesInternal[0].StartDate, RatesInternal.LastBC().StartDate))
          CorridorStartDate = null;
        Action doMatch = () => { };
        var doRunMatch = dateEnd.HasValue && !dateStart.HasValue && RatesInternal.LastBC().StartDate < dateEnd;
        if (doRunMatch) {
          Enumerable.Range(0, CorridorDistanceRatio.ToInt()).ForEach(i => {
            RatesInternal.RemoveAt(0);
            var rate = RatesInternal.LastBC().Clone() as Rate;
            rate.StartDate = rate.StartDate.AddMinutes(BarPeriodInt);
            rate.StartDate2 = rate.StartDate2.AddMinutes(BarPeriodInt);
            RatesInternal.Add(rate);
          });
          doMatch = MatchSnapshotRange;
          try {
            if (_scheduledSnapshot != null) _scheduledSnapshot.Dispose();
          } catch (Exception exc) { Log = exc; }
          _scheduledSnapshot = Scheduler.Default.Schedule(BarPeriodInt.FromMinutes(), () => {
            SnapshotArguments.RaiseShowSnapshot();
          });
        }
        Scheduler.Default.Schedule(() => {
          RaiseShowChart();
          doMatch();
        });
      } catch (Exception exc) {
        Log = exc;
      }
    }
    #endregion
    #region ctor
    [Import]
    static NewsCasterModel _newsCaster { get { return NewsCasterModel.Default; } }
    public TradingMacro() {
      _newsCaster.CountdownSubject
        .Where(nc => IsActive && Strategy!= Strategies.None && nc.AutoTrade && nc.Countdown <= _newsCaster.AutoTradeOffset)
        .Subscribe(nc => {
          try {
            if (!RatesArray.Any()) return;
            var height = CorridorStats.StDevByHeight;
            if (CurrentPrice.Average > MagnetPrice) {
              _buyLevel.Rate = MagnetPrice + height;
              _sellLevel.Rate = MagnetPrice;
            } else {
              _buyLevel.Rate = MagnetPrice;
              _sellLevel.Rate = MagnetPrice - height;
            }
            new[] { _buyLevel, _sellLevel }.ForEach(sr => {
              sr.ResetPricePosition();
              sr.CanTrade = true;
              //sr.InManual = true;
            });
            DispatcherScheduler.Current.Schedule(5.FromSeconds(), () => nc.AutoTrade = false);
          } catch (Exception exc) { Log = exc; }
        });
      _waveShort = new WaveInfo(this);
      WaveShort.DistanceChanged += (s, e) => {
        OnPropertyChanged(() => WaveShortDistance);
        OnPropertyChanged(() => WaveShortDistanceInPips);
        _broadcastCorridorDateChanged();
      };
      if (GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher != null)
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
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<TradeLineChangedMessage>(this, a => {
        if (a.Target == this && _strategyOnTradeLineChanged != null)
          _strategyOnTradeLineChanged(a);
      });
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<ShowSnapshotMatchMessage>(this, m => {
        if (SnapshotArguments.IsTarget && !m.StopPropagation) {
          m.StopPropagation = true;
          SnapshotArguments.DateStart = m.DateStart;
          SnapshotArguments.DateEnd = null;
          SnapshotArguments.IsTarget = false;
          SnapshotArguments.Label = m.Correlation.ToString("n2");
          //if (BarsCount != m.BarCount) BarsCount = m.BarCount;
          if (BarPeriodInt != m.BarPeriod)
            BarPeriod = (BarsPeriodType)m.BarPeriod;
          RatesInternal.Clear();
          RatesArray.Clear();
          CorridorStartDate = null;
          ShowSnaphot(m.DateStart, m.DateEnd);
          Scheduler.Default.Schedule(1.FromSeconds(), () => {
            try {
              CorridorStartDate = m.DateStart;
              CorridorStopDate = DateTime.MinValue;// RatesArray.SkipWhile(r => r.StartDate < CorridorStartDate).Skip(m.DateEnd - 1).First().StartDate;
            } catch (Exception exc) {
              Log = exc;
            }
          });
          Scheduler.Default.Schedule(10.FromSeconds(), () => SnapshotArguments.IsTarget = true);
        }
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
    public void ResetSessionId(Guid superId) {
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
        Func<int, bool> compare = (hour) => {
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
            else throw new InvalidOperationException();
          }
          CorridorAngle = CorridorStats.Slope;
          var tp = CalculateTakeProfit();
          TakeProfitPips = InPips(tp);
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
          return -CalculateTakeProfit();
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
      var digits = TradesManager.GetDigits(Pair);
      if (fw != null && !IsInPlayback) {
        PriceChangedSubscribsion = Observable.FromEventPattern<EventHandler<PriceChangedEventArgs>
          , PriceChangedEventArgs>(h => h, h => _TradesManager().PriceChanged += h, h => _TradesManager().PriceChanged -= h)
          .Where(pce => pce.EventArgs.Pair == Pair)
          //.Sample((0.1).FromSeconds())
          .DistinctUntilChanged(pce => pce.EventArgs.Price.Average.Round(digits))
          .Do(pce => {
            try {
              CurrentPrice = pce.EventArgs.Price;
              if (!TradesManager.IsInTest && !IsInPlayback)
                AddCurrentTick(pce.EventArgs.Price);
              TicksPerMinuteSet(pce.EventArgs.Price, ServerTime);
              OnPropertyChanged(TradingMacroMetadata.PipsPerPosition);
            } catch (Exception exc) { Log = exc; }
          })
          .Latest().ToObservable(ThreadPoolScheduler.Instance)
          .Subscribe(pce => RunPriceChanged(pce.EventArgs, null), exc => MessageBox.Show(exc + ""), () => Log = new Exception(Pair + " got terminated."));
        if (false)
          fw.PriceChangedBroadcast.AsObservable()
            .Where(pce => pce.Pair == Pair)
            .Do(pce => {
              CurrentPrice = pce.Price;
              if (!TradesManager.IsInTest && !IsInPlayback)
                AddCurrentTick(pce.Price);
              TicksPerMinuteSet(pce.Price, ServerTime);
              OnPropertyChanged(TradingMacroMetadata.PipsPerPosition);
            })
            .Latest().ToObservable(ThreadPoolScheduler.Instance)
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
      ReleasePendingAction("OT");
      EnsureActiveSuppReses();
      SetEntryOrdersBySuppResLevels();
      RaisePositionsChanged();
      if (_strategyExecuteOnTradeOpen != null) _strategyExecuteOnTradeOpen(e.Trade);
    }

    bool IsMyTrade(Trade trade) { return trade.Pair == Pair; }
    bool IsMyOrder(Order order) { return order.Pair == Pair; }
    public void UnSubscribeToTradeClosedEVent(ITradesManager tradesManager) {
      if (PriceChangedSubscribsion != null) PriceChangedSubscribsion.Dispose();
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
      if (!IsMyTrade(e.Trade) || HistoryMaximumLot == 0) return;
      CurrentLot = Trades.Sum(t => t.Lots);
      EnsureActiveSuppReses();
      SetEntryOrdersBySuppResLevels();
      RaisePositionsChanged();
      RaiseShowChart();
      ReleasePendingAction("OT");
      ReleasePendingAction("CT");
      if (_strategyExecuteOnTradeClose != null) _strategyExecuteOnTradeClose(e.Trade);
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
          l.Add("TestFileName:" + TestFileName);
          _sessionInfo = string.Join(",", l);
        }
        return _sessionInfo;
      }
    }
    bool HasShutdownStarted { get { return ((bool)GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(new Func<bool>(() => GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.HasShutdownStarted), new object[0])); } }

    object _replayLocker = new object();
    public void Replay(ReplayArguments args) {
      Func<TradingMacro[]> tms = () => args.TradingMacros.Cast<TradingMacro>().ToArray();
      Action<RepayPauseMessage> pra = m => args.InPause = !args.InPause;
      Action<RepayBackMessage> sba = m => args.StepBack = true;
      Action<RepayForwardMessage> sfa = m => args.StepForward = true;
      var tc = new EventHandler<TradeEventArgs>((sender, e) => {
        GlobalStorage.UseForexContext(c => {
          var session = c.t_Session.Single(s => s.Uid == SessionId);
          session.MaximumLot = HistoryMaximumLot;
          session.MinimumGross = this.MinimumGross;
          session.Profitability = Profitability;
          session.DateMin = e.Trade.TimeClose;
          if (session.DateMin == null) session.DateMin = e.Trade.Time;
          c.SaveChanges();
        });
      });

      TradesManager.TradeClosed += tc;
      try {
        if (tms().Length == 1 || BarPeriod == tms().Min(tm => tm.BarPeriod))
          lock (_replayLocker) {
            GalaSoft.MvvmLight.Messaging.Messenger.Default.Register(this, pra);
            GalaSoft.MvvmLight.Messaging.Messenger.Default.Register(this, sba);
            GalaSoft.MvvmLight.Messaging.Messenger.Default.Register(this, sfa);
            args.MustStop = false;
          }
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
        var moreMinutes = (args.DateStart.Value.DayOfWeek == DayOfWeek.Monday ? 17 * 60 + 24 * 60 : args.DateStart.Value.DayOfWeek == DayOfWeek.Saturday ? 1440 : 0);
        var internalRateCount = 1440 * 8;
        var rates = GlobalStorage.GetRateFromDB(Pair, args.DateStart.Value.AddMinutes(-internalRateCount * BarPeriodInt), int.MaxValue, BarPeriodInt);
        //var rateStart = rates.SkipWhile(r => r.StartDate < args.DateStart.Value).First();
        //var rateStartIndex = rates.IndexOf(rateStart);
        //var rateIndexStart = (rateStartIndex - BarsCount).Max(0);
        //rates.RemoveRange(0, rateIndexStart);
        var dateStop = args.MonthsToTest > 0 ? args.DateStart.Value.AddDays(args.MonthsToTest * 30.5) : DateTime.MaxValue;
        if (args.MonthsToTest > 0) {
          //rates = rates.Where(r => r.StartDate <= args.DateStart.Value.AddDays(args.MonthsToTest*30.5)).ToList();
          if ((rates[0].StartDate - rates.Last().StartDate).Duration().TotalDays < args.MonthsToTest * 30) {
            args.ResetSuperSession();
            return;
          }
        }
        #region Init stuff
        CorridorStats.Rates = null;
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
        CurrentLoss = HistoryMaximumLot = 0;
        ResetMinimumGross();
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
        _waveRates.Clear();
        _strategyExecuteOnTradeClose = null;
        _strategyOnTradeLineChanged = null;
        MagnetPrice = double.NaN;
        var currentPosition = -1;
        var indexCurrent = 0;
        #endregion
        var vm = (VirtualTradesManager)TradesManager;
        Func<Rate[]> getLastRates = () => vm.RatesByPair().Where(kv => kv.Key != Pair).Select(kv => kv.Value.LastBC()).ToArray();
        if (!rates.Any()) throw new Exception("No rates were dowloaded fot Pair:{0}, Bars:{1}".Formater(Pair, BarPeriod));
        while (!args.MustStop && indexCurrent < rates.Count && Strategy != Strategies.None) {
          while (!args.IsMyTurn(this)) {
            //Task.Factory.StartNew(() => {
            //  while (!args.IsMyTurn(this) && !args.MustStop)
            Thread.Sleep(0);
            if (args.MustStop) return;
            //}).Wait();
          }
          if (tms().Length == 1 && currentPosition > 0 && currentPosition != args.CurrentPosition) {
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
                RatesArraySafe.Count();
                indexCurrent--;
              }
            } else {
              if (RateLast != null && tms().Length > 1) {
                var a = tms().Select(tm => tm.RatesInternal.LastByCountOrDefault(new Rate()).StartDate).ToArray();
                var dateMin = a.Min();
                if ((dateMin - a.Max()).Duration().TotalMinutes > BarPeriodInt * 60) {
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
              while (RatesInternal.Count > (BarsCount * 10).Max(1440 * 3)
                  && (!DoStreatchRates || (CorridorStats.Rates.Count == 0 || RatesInternal[0] < CorridorStats.Rates.LastBC())))
                RatesInternal.RemoveAt(0);
            }
            if (rate.StartDate > dateStop) {
              //if (CurrentGross > 0) {
                CloseTrades("Replay break due dateStop.");
                break;
              //}
            }
            if (RatesInternal.LastBC().StartDate < args.DateStart.Value) {
              continue;
            } else if (RatesArraySafe.LastBC().StartDate < args.DateStart.Value) {
              continue;
            } else {
              LastRatePullTime = RateLast.StartDate;
              //TradesManager.RaisePriceChanged(Pair, RateLast);
              var d = Stopwatch.StartNew();
              if (rate != null) {
                args.CurrentPosition = currentPosition = (100.0 * (indexCurrent - BarsCount) / (rates.Count - BarsCount)).ToInt();

                var price = new Price(Pair, RateLast, ServerTime, TradesManager.GetPipSize(Pair), TradesManager.GetDigits(Pair), true);
                TradesManager.RaisePriceChanged(Pair, BarPeriodInt, new Price(Pair, rate, ServerTime, TradesManager.GetPipSize(Pair), TradesManager.GetDigits(Pair), true));

                //RunPriceChanged(new PriceChangedEventArgs(Pair, price, TradesManager.GetAccount(), new Trade[0]), null);
                ReplayEvents();
                if (TradesManager.GetAccount().PipsToMC < 0) {
                  Log = new Exception("Equity Alert: " + TradesManager.GetAccount().Equity);
                  CloseTrades("Equity Alert: " + TradesManager.GetAccount().Equity);
                }
                Profitability = (args.GetOriginalBalance() - 50000) / (RateLast.StartDate - args.DateStart.Value).TotalDays * 30.5;
              } else
                Log = new Exception("Replay:End");
              ReplayCancelationToken.ThrowIfCancellationRequested();
              Thread.Sleep((args.DelayInSeconds - d.Elapsed.TotalSeconds).Max(0).FromSeconds());
              Func<bool> inPause = () => args.InPause || !IsTradingActive;
              if (inPause()) {
                args.StepBack = args.StepForward = false;
                Task.Factory.StartNew(() => {
                  while (inPause() && !args.StepBack && !args.StepForward && !args.MustStop)
                    Thread.Sleep(100);
                }).Wait();
              }
            }
          } finally {
            args.NextTradingMacro();
          }
        }
        Log = new Exception("Replay for Pair:{0}[{1}] done.".Formater(Pair, BarPeriod));
      } catch (Exception exc) {
        Log = exc;
      } finally {
        try {
          args.TradingMacros.Remove(this);
          args.MustStop = true;
          args.SessionStats.ProfitToLossRatio = ProfitabilityRatio;
          TradesManager.ClosePair(Pair);
          TradesManager.TradeClosed -= tc;
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Unregister(this, pra);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Unregister(this, sba);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Unregister(this, sfa);
          SetPlayBackInfo(false, args.DateStart.GetValueOrDefault(), args.DelayInSeconds.FromSeconds());
          args.StepBack = args.StepBack = args.InPause = false;
          if (!IsInVitualTrading) {
            RatesInternal.Clear();
            SubscribeToTradeClosedEVent(_TradesManager);
            LoadRates();
          }
        } catch (Exception exc) {
          Log = exc;
          MessageBox.Show(exc.ToString(), "Replay");
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
      return value.Angle(BarPeriodInt, PointSize);
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
      var a = Supports.OrderBy(s => s.Rate).FirstOrDefault();
      if (a == null) throw new Exception("There is no Support.");
      return a;
    }

    private Store.SuppRes SupportHigh() {
      var a = Supports.OrderBy(s => s.Rate).LastOrDefault();
      if (a == null) throw new Exception("There is no Support.");
      return a;
    }
    private Store.SuppRes Support0() {
      return SupportByPosition(0);
    }
    private Store.SuppRes Support1() {
      return SupportByPosition(1);
    }
    private Store.SuppRes SupportByPosition(int position) {
      var s = SuppRes.Where(sr => sr.IsSupport).Skip(position).FirstOrDefault();
      if (s == null) throw new Exception("There is no Support as position {0}".Formater(position));
      return s;
    }
    private Store.SuppRes[] SupportsNotCurrent() {
      return SuppResNotCurrent(Supports);
    }

    private Store.SuppRes ResistanceLow() {
      var a = Resistances.OrderBy(s => s.Rate).FirstOrDefault();
      if (a == null) throw new Exception("There is no Resistance.");
      return a;

    }

    private Store.SuppRes ResistanceHigh() {
      var a = Resistances.OrderBy(s => s.Rate).LastOrDefault();
      if (a == null) throw new Exception("There is no Restiance.");
      return a;
    }
    private Store.SuppRes Resistance0() {
      return ResistanceByPosition(0);
    }
    private Store.SuppRes Resistance1() {
      return ResistanceByPosition(1);
    }

    private Store.SuppRes ResistanceByPosition(int position) {
      var s = SuppRes.Where(sr => !sr.IsSupport).Skip(position).FirstOrDefault();
      if (s == null) throw new Exception("There is no Restiance.");
      return s;
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

    double _CenterOfMassSell = double.NaN;
    public double CenterOfMassSell {
      get { return _CenterOfMassSell; }
      private set { _CenterOfMassSell = value; }
    }
    double _CenterOfMassBuy = double.NaN;
    public double CenterOfMassBuy {
      get { return _CenterOfMassBuy; }
      private set { _CenterOfMassBuy = value; }
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
          if (!SnapshotArguments.IsTarget && RatesInternal.Count < Math.Max(1, BarsCount)) {
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
            var corridor = _rateArray.ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
            RatesStDev = corridor.StDev;
            StDevByPriceAvg = corridor.StDevs[CorridorCalculationMethod.PriceAverage];
            StDevByHeight = corridor.StDevs.ContainsKey(CorridorCalculationMethod.Height) ? corridor.StDevs[CorridorCalculationMethod.Height] : double.NaN;
            Angle = corridor.Slope.Angle(BarPeriodInt, PointSize);
            OnPropertyChanged(() => RatesRsd);
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
    private IEnumerable<Rate> GetRatesSafe() {
      Func<IEnumerable<Rate>> a = () => {
        var startDate = CorridorStartDate ?? (CorridorStats.Rates.Count > 0 ? CorridorStats.Rates.LastBC().StartDate : (DateTime?)null);
        var countByDate = startDate.HasValue && DoStreatchRates ? RatesInternal.Count(r => r.StartDate >= startDate) : 0;
        return RatesInternal.Skip((RatesInternal.Count - (countByDate * 1).Max(BarsCount)).Max(0));
      };
      return _limitBarToRateProvider == (int)BarPeriod ? a() : RatesInternal.GetMinuteTicks((int)BarPeriod, false, false);
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
    void ResetMinimumGross() { MinimumGross = double.NaN; }
    private double _MinimumGross = double.NaN;
    [Category(categorySession)]
    public double MinimumGross {
      get { return _MinimumGross; }
      set {
        if (_MinimumGross.IsNaN() || value.IsNaN() || _MinimumGross > value) {
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

    public double TradingDistanceInPips {
      get {
        try {
          return InPips(_tradingDistance).Max(_tradingDistanceMax);
        } catch {
          return double.NaN;
        }
      }
    }
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

    Playback _Playback = new Playback();
    public void SetPlayBackInfo(bool play, DateTime startDate, TimeSpan delay) {
      _Playback.Play = play;
      _Playback.StartDate = startDate;
      _Playback.Delay = delay;
    }
    public bool IsInPlayback { get { return _Playback.Play || (SnapshotArguments.DateStart ?? SnapshotArguments.DateEnd) != null || SnapshotArguments.IsTarget; } }

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
    private bool _CanDoEntryOrders = true;
    [Category(categoryTrading)]
    [DisplayName("Can Do Entry Orders")]
    public bool CanDoEntryOrders {
      get { return _CanDoEntryOrders; }
      set { _CanDoEntryOrders = value; }
    }
    private bool CanDoNetOrders {
      get {
        var canDo = false && IsAutoStrategy && (HasCorridor || IsAutoStrategy);
        return canDo;
      }
    }

    static TradingMacro() { }

    void SetEntryOrdersBySuppResLevels() {
      if (TradesManager == null) return;
      if (!isLoggedIn) return;
      try {
        if (!CanDoEntryOrders)
          GetEntryOrders().ToList().ForEach(o => OnDeletingOrder(o.OrderID));
      } catch (Exception exc) {
        Log = exc;
      }
    }

    private Order2GoAddIn.FXCoreWrapper GetFXWraper(bool failTradesManager = true) {
      if (TradesManager == null)
        FailTradesManager();
      return TradesManager as Order2GoAddIn.FXCoreWrapper;
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

    bool? _magnetDirtection;
    DateTime? _corridorTradeDate;

    Action StrategyAction {
      get {
        switch ((Strategy & ~Strategies.Auto)) {
          case Strategies.Hot:
            return StrategyEnterUniversal;
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
    Action<TradeLineChangedMessage> _strategyOnTradeLineChanged;
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
    #endregion

    private void TurnOffSuppRes(double level = double.NaN) {
      var rate = double.IsNaN(level) ? SuppRes.Average(sr => sr.Rate) : level;
      foreach (var sr in SuppRes)
        sr.RateEx = rate;
    }
    public void OpenTrade(bool isBuy, int lot, string reason) {
      CheckPendingAction("OT", (pa) => {
        if (lot > 0) {
          pa();
          if (LogTrades)
            Log = new Exception(string.Format("{0}[{1}]: {2} {3} from {4} by [{5}]", Pair, BarPeriod, isBuy ? "Buying" : "Selling", lot, new StackFrame(3).GetMethod().Name, reason));
          TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", null);
        }
      });
    }

    public void TrimTrades(string reason) { CloseTrades(Trades.Lots() - LotSize, reason); }
    public void CloseTrades(string reason) { CloseTrades(Trades.Lots(), reason); }
    private void CloseTrades(int lot, string reason) {
      if (!Trades.Any()) return;
      if (HasPendingKey("CT")) return;
      if (lot > 0)
        CheckPendingAction("CT", pa => {
          pa();
          if (LogTrades)
            Log = new Exception(string.Format("{0}[{1}]: Closing {2} from {3} in {4} from {5}]"
              , Pair, BarPeriod, lot, Trades.Lots(), new StackFrame(3).GetMethod().Name, reason));
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
          && (!RatesInternal.Any() || LastRatePullTime.AddMinutes(1.0.Max((double)BarPeriod / 2)) <= ServerTime)) {
          LastRatePullTime = ServerTime;
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
      return GetPriceMA(MovingAverageType);
    }
    private static Func<Rate, double> GetPriceMA(MovingAverageType movingAverageType) {
      switch (movingAverageType) {
        case Store.MovingAverageType.RegressByMA:
        case Store.MovingAverageType.Regression:
        case Store.MovingAverageType.Cma:
          return r => r.PriceCMALast;
        case Store.MovingAverageType.Trima:
          return r => r.PriceTrima;
        default:
          throw new NotSupportedException(new { movingAverageType }.ToString());
      }
    }
    double _sqrt2 = 1.5;// Math.Sqrt(1.5);

    private void SetMA(int? period = null) {
      switch (MovingAverageType) {
        case Store.MovingAverageType.RegressByMA:
          RatesArray.SetCma((p, r) => r.PriceAvg, 3, 3);
          RatesArraySafe.SetRegressionPrice1(PriceCmaLevels, r => r.PriceCMALast, (r, d) => r.PriceCMALast = d);
          break;
        case Store.MovingAverageType.Regression:
          Action<Rate, double> a = (r, d) => r.PriceCMALast = d;
          RatesArraySafe.SetRegressionPrice1(PriceCmaLevels, _priceAvg, a);
          break;
        case Store.MovingAverageType.Cma:
          if (period.HasValue)
            RatesArray.SetCma(period.Value, period.Value);
          else if (PriceCmaLevels > 0) {
            //RatesArray.SetCma((p, r) => r.PriceAvg - p.PriceAvg, r => {
            //  if(r.PriceCMAOther == null)r.PriceCMAOther = new List<double>();
            //  return r.PriceCMAOther;
            //}, PriceCmaPeriod + CmaOffset, PriceCmaLevels + CmaOffset.ToInt());
            RatesArray.SetCma((p, r) => r.PriceAvg, PriceCmaLevels, PriceCmaLevels);
          }
          break;
        case Store.MovingAverageType.Trima:
          RatesArray.SetTrima(PriceCmaLevels); break;
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
              var coeffs = corridorRates.ReverseIfNot().Select(r => r.PriceAvg).ToArray().Regress(1);
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
          var coeffs = corridorRates.ReverseIfNot().Select(r => r.PriceAvg).ToArray().Regress(1);
          crossedCorridor = new CorridorStatistics(this, corridorRates, stDev, coeffs);
          //.ScanCorridorWithAngle(priceHigh, priceLow, ((int)BarPeriod).FromMinutes(), PointSize, CorridorCalcMethod); 
          #endregion
        }
        #endregion

        #region Corridorness
        #endregion

        var corridornesses = new[] { crossedCorridor }.ToList();
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
        case TradingMacroTakeProfitFunction.CorridorHeight_BS: 
          return GetValueByTakeProfitFunction(TradingMacroTakeProfitFunction.CorridorHeight).Min(GetValueByTakeProfitFunction(TradingMacroTakeProfitFunction.BuySellLevels));
        case TradingMacroTakeProfitFunction.CorridorStDevMin: tp = CorridorStats.StDevByHeight.Min(CorridorStats.StDevByPriceAvg); break;
        case TradingMacroTakeProfitFunction.CorridorStDevMax: tp = CorridorStats.StDevByHeight.Max(CorridorStats.StDevByPriceAvg); break;
        case TradingMacroTakeProfitFunction.CorridorHeight: tp = CorridorStats.StDevByHeight * 4; break;
        case TradingMacroTakeProfitFunction.RatesHeight: tp = RatesHeight; break;
        case TradingMacroTakeProfitFunction.WaveShort: tp = WaveShort.RatesHeight; break;
        case TradingMacroTakeProfitFunction.WaveShortStDev: tp = WaveShort.RatesStDev; break;
        case TradingMacroTakeProfitFunction.WaveTradeStart: tp = WaveTradeStart.RatesHeight - (WaveTradeStart1.HasRates ? WaveTradeStart1.RatesHeight : 0); break;
        case TradingMacroTakeProfitFunction.WaveTradeStartStDev: tp = WaveTradeStart.RatesStDev; break;
        case TradingMacroTakeProfitFunction.RatesHeight_2: tp = RatesHeight / 2; break;
        case TradingMacroTakeProfitFunction.RatesHeight_3: tp = RatesHeight / 3; break;
        case TradingMacroTakeProfitFunction.RatesHeight_4: tp = RatesHeight / 4; break;
        case TradingMacroTakeProfitFunction.RatesHeight_5: tp = RatesHeight / 5; break;
        case TradingMacroTakeProfitFunction.RatesStDevMax: tp = StDevByHeight.Max(StDevByPriceAvg); break;
        case TradingMacroTakeProfitFunction.RatesStDevMin: tp = StDevByHeight.Min(StDevByPriceAvg); break;
        case TradingMacroTakeProfitFunction.Spread: return SpreadForCorridor;
        case TradingMacroTakeProfitFunction.PriceSpread: return PriceSpreadAverage.GetValueOrDefault(double.NaN);
        case TradingMacroTakeProfitFunction.Harmonic:
          if (_harmonics == null || !_harmonics.Any()) throw new InvalidOperationException("Function " + function + " is using _harmonics that is empty.");
          return InPoints(_harmonics.Select(h => h.Height).OrderByDescending(h => h).Take(2).Average());
        case TradingMacroTakeProfitFunction.BuySellLevels2:
        case TradingMacroTakeProfitFunction.BuySellLevels:
          tp = _buyLevelRate - _sellLevelRate;
          if (double.IsNaN(tp)) {
            if (_buyLevel == null || _sellLevel == null) return double.NaN;
            tp = (_buyLevel.Rate - _sellLevel.Rate).Abs();
          }
          tp += PriceSpreadAverage.GetValueOrDefault(double.NaN) * (function == TradingMacroTakeProfitFunction.BuySellLevels2 ? 2 : 1);
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
        case ScanCorridorFunction.StDevAngle: return ScanCorridorByStDevAndAngle;
        case ScanCorridorFunction.Simple: return ScanCorridorSimple;
        case ScanCorridorFunction.Height: return ScanCorridorByHeight;
        case ScanCorridorFunction.Time: return ScanCorridorByTimeMinAndAngleMax;
        case ScanCorridorFunction.Rsd: return ScanCorridorByRsdFft;
        case ScanCorridorFunction.Ftt: return ScanCorridorByFft;
        case ScanCorridorFunction.TimeFrame: return ScanCorridorByTimeFrameAndAngle;
        case ScanCorridorFunction.StDevSimple1Cross: return ScanCorridorSimpleWithOneCross;
        case ScanCorridorFunction.StDevBalance: return ScanCorridorByStDevBalance;
        case ScanCorridorFunction.StDevUDCross: return ScanCorridorStDevUpDown;
        case ScanCorridorFunction.Balance: return ScanCorridorByBalance;
        case ScanCorridorFunction.HorizontalProbe: return ScanCorridorByHorizontalLineCrosses;
        case ScanCorridorFunction.HorizontalProbe2: return ScanCorridorByHorizontalLineCrosses3;
        case ScanCorridorFunction.Void: return ScanCorridorVoid;
      }
      throw new NotSupportedException(function + "");
    }

    public double CommissionByTrade(Trade trade) { return TradesManager.CommissionByTrade(trade); }

    public bool IsInVitualTrading { get { return TradesManager is VirtualTradesManager; } }
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
      RunPrice(pce, Trades);
      //if (TradesManager.IsInTest || IsInPlayback) {
      //  CurrentPrice = pce.Price;
      //  RunPrice(pce, Trades);
      //} else RunPriceBroadcast.SendAsync(pce);
    }

    #region GeneralPurpose Subject
    object _GeneralPurposeSubjectLocker = new object();
    ISubject<Action> _GeneralPurposeSubject;
    ISubject<Action> GeneralPurposeSubject {
      get {
        lock (_GeneralPurposeSubjectLocker)
          if (_GeneralPurposeSubject == null) {
            _GeneralPurposeSubject = new Subject<Action>();
            _GeneralPurposeSubject
              .Latest().ToObservable(new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }))
              .Subscribe(s => s(), exc => Log = exc);
          }
        return _GeneralPurposeSubject;
      }
    }
    void OnGeneralPurpose(Action p) {
      GeneralPurposeSubject.OnNext(p);
    }
    #endregion


    ReactiveUI.ReactiveCollection<NewsEvent> _newEventsCurrent = new ReactiveCollection<NewsEvent>();
    public ReactiveUI.ReactiveCollection<NewsEvent> NewEventsCurrent { get { return _newEventsCurrent; } }
    private void RunPrice(PriceChangedEventArgs e, Trade[] trades) {
      Price price = e.Price;
      Account account = e.Account;
      var sw = Stopwatch.StartNew();
      try {
        CalcTakeProfitDistance();
        if (!price.IsReal) price = TradesManager.GetPrice(Pair);
        MinimumGross = CurrentGross;
        CurrentLossPercent = CurrentGross / account.Balance;
        BalanceOnStop = account.Balance + StopAmount.GetValueOrDefault();
        BalanceOnLimit = account.Balance + LimitAmount.GetValueOrDefault();
        SetTradesStatistics(price, trades);
        if (DoNews && RatesArray.Any())
          OnGeneralPurpose(() => {
            var dateStart = RatesArray[0].StartDate;
            var dateEnd = RatesArray.LastBC().StartDate.AddHours(120);
            var newsEventsCurrent = NewsCasterModel.SavedNews.AsParallel().Where(ne => ne.Time.DateTime.Between(dateStart, dateEnd)).ToArray();
            NewEventsCurrent.Except(newsEventsCurrent).ToList().ForEach(ne => NewEventsCurrent.Remove(ne));
            NewEventsCurrent.AddRange(newsEventsCurrent.Except(NewEventsCurrent).ToArray());
          });
        SetLotSize();
        try {
          RunStrategy();
        } catch (Exception exc) {
          Log = exc;
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
      try {
        LotSize = TradingRatioByPMC ? CalcLotSizeByPMC(account)
          : TradingRatio <= 0 ? 0 : TradingRatio >= 1 ? (TradingRatio * BaseUnitSize).ToInt()
          : TradesManagerStatic.GetLotstoTrade(account.Balance, TradesManager.Leverage(Pair), TradingRatio, BaseUnitSize);
      } catch (Exception exc) { throw new SetLotSizeException("", exc); }
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

    private int MaxPipsToPMC() { return (TakeProfitPips * 2).ToInt(); }

    private int CalcLotSizeByPMC(Account account) {
      return TradingStatistics.TradingMacros == null ? 0
        : TradesManagerStatic.GetLotSize((TradesManagerStatic.LotToMarginCall(MaxPipsToPMC(), account.Equity, BaseUnitSize, PipCost, TradesManager.GetOffer(Pair).MMR)) / TradingStatistics.TradingMacros.Count, BaseUnitSize);
    }

    int LotSizeByLoss(ITradesManager tradesManager, double loss, int baseLotSize, double lotMultiplierInPips) {
      var bus = tradesManager.GetBaseUnitSize(Pair);
      return TradesManagerStatic.GetLotSize(-(loss / lotMultiplierInPips) * bus / TradesManager.GetPipCost(Pair), bus, true);
    }
    int LotSizeByLoss(double? lotMultiplierInPips = null) {
      var currentGross = this.TradingStatistics.CurrentGross;
      var lotSize = LotSizeByLoss(TradesManager, currentGross, LotSize,
        lotMultiplierInPips ?? (TradingDistanceInPips * 2));
      return lotMultiplierInPips.HasValue || lotSize <= MaxLotSize || !CorridorFollowsPrice ? lotSize.Min(MaxLotSize) : LotSizeByLoss(TradesManager, currentGross, LotSize, RatesHeightInPips / _ratesHeightAdjustmentForAls).Max(MaxLotSize);
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


    public int AllowedLotSizeCore(double? lotMultiplierInPips = null) {
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
            Debug.WriteLine("LoadRates[{0}:{2}] @ {1:HH:mm:ss}", Pair, ServerTime, (BarsPeriodType)BarPeriod);
            var sw = Stopwatch.StartNew();
            var serverTime = ServerTime;
            var periodsBack = (BarsCount * 5).Max(1440 * 5);
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
            LastRatePullTime = ServerTime;
            Action a = () => {
              try {
                Store.PriceHistory.AddTicks(TradesManager as Order2GoAddIn.FXCoreWrapper, BarPeriodInt, Pair, DateTime.MinValue, obj => { if (DoLogSaveRates) Log = new Exception(obj + ""); });
              } catch (Exception exc) { Log = exc; }
            };
            Scheduler.Default.Schedule(a);
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

    BroadcastBlock<Action<Unit>> _broadcastLoadRates;
    BroadcastBlock<Action<Unit>> broadcastLoadRates {
      get {
        if (_broadcastLoadRates == null) {
          _broadcastLoadRates = DataFlowProcessors.SubscribeToBroadcastBlock();
        }
        return _broadcastLoadRates;
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
        case TradingMacroMetadata.TradingAngleRange:
        case TradingMacroMetadata.StDevAverageLeewayRatio:
        case TradingMacroMetadata.StDevTresholdIterations:
        case TradingMacroMetadata.MovingAverageType:
        case TradingMacroMetadata.PriceCmaLevels:
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
    Strategies[] _exceptionStrategies = new[] { Strategies.Auto };
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
    private double CorridorAngleFromTangent() {
      return AngleFromTangent(CorridorStats.Coeffs.LineSlope());
    }

    private double AngleFromTangent(double tangent) {
      return tangent.Angle(BarPeriodInt, PointSize);
    }


    public double VolumeAverageLow { get; set; }

    private List<List<Rate>> _CorridorsRates = new List<List<Rate>>();
    private Store.SuppRes _buyLevel;

    public Store.SuppRes BuyLevel {
      get { return _buyLevel; }
      set {
        if (_buyLevel == value) return;
        _buyLevel = value;
        OnPropertyChanged(() => BuyLevel);
      }
    }
    private Store.SuppRes _sellLevel;

    public Store.SuppRes SellLevel {
      get { return _sellLevel; }
      set {
        if (_sellLevel == value) return;
        _sellLevel = value;
        OnPropertyChanged(() => SellLevel);
      }
    }
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
      double RatesMax { get; }
      double RatesMin { get; }
    }
    public class WaveInfo : Models.ModelBase, IWave<Rate> {
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
      public void SetRatesByDistance(IList<Rate> rates, int countMinimum = 30) {
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
            var corridor = Rates.ScanCorridorWithAngle(_tradingMacro.CorridorGetHighPrice(), _tradingMacro.CorridorGetLowPrice(), TimeSpan.Zero, _tradingMacro.PointSize, _tradingMacro.CorridorCalcMethod);
            //var stDevs = Rates.Shrink(_tradingMacro.CorridorPrice, 5).ToArray().ScanWaveWithAngle(v => v, _tradingMacro.PointSize, _tradingMacro.CorridorCalcMethod);
            _RatesStDev = corridor.StDev;
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
      public WaveInfo(TradingMacro tradingMacro, IList<Rate> rates)
        : this(tradingMacro) {
        this.Rates = rates;
      }

      public bool HasRates { get { return _Rates != null && _Rates.Any(); } }
      IList<Rate> _Rates;
      private TradingMacro _tradingMacro;
      public IList<Rate> ResetRates(IList<Rate> rates) {
        Rates = null;
        return Rates = rates;
      }
      public IList<Rate> Rates {
        get {
          if (_Rates == null || !_Rates.Any())
            throw new NullReferenceException();
          return _Rates;
        }
        set {
          var isUp = value == null || value.Count == 0 ? null : (bool?)(value.LastBC().PriceAvg < value[0].PriceAvg);
          if (value == null || !HasRates || isUp != IsUp || Rates.LastBC() == value.LastBC())
            _Rates = value;
          else {
            var newRates = value.TakeWhile(r => r != Rates[0]);
            _Rates = newRates.Concat(_Rates).ToArray();
          }
          RatesMax = double.NaN;
          RatesMin = double.NaN;
          RatesStDev = double.NaN;
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
        if (IsUpChangedEvent != null)
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
      protected void RaiseStartDateChanged(DateTime now, DateTime then) {
        if (StartDateChangedEvent != null) StartDateChangedEvent(this, new NewOldEventArgs<DateTime>(now, then));
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
      protected void RaiseIsUpChanged(bool? now, bool? then) {
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
            RaiseStartDateChanged(_StartDate, then);
          }
        }
      }

      #endregion

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

    public class WaveWithHeight<TBar> : Models.ModelBase, IWave<TBar> where TBar : BarBase {
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
      public WaveWithHeight(IList<TBar> bars) {
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
          SuppRes.ForEach(sr => sr.ResetPricePosition());
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
      _suppResesForBulk().ToList().ForEach(sr => sr.InManual = isManual);
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

    #region RatesRsd
    public double RatesRsd { get { return RatesHeight / StDevByPriceAvg; } }
    #endregion

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
    public double WaveDistance {
      get { return _WaveDistance; }
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
    List<LambdaBinding> _tradingStateLambdaBindings = new List<LambdaBinding>();
    bool __tradingStateLambdaBinding;
    public bool TradingState {
      get {
        if (!__tradingStateLambdaBinding) {
          __tradingStateLambdaBinding = true;
          try {
            _tradingStateLambdaBindings.AddRange(new[]{ 
              LambdaBinding.BindOneWay(() => BuyLevel.CanTradeEx, () => TradingState, (s) => false),
              LambdaBinding.BindOneWay(() => SellLevel.CanTradeEx, () => TradingState, (s) => false),
              LambdaBinding.BindOneWay(() => Strategy, () => TradingState, (s) => false),
              LambdaBinding.BindOneWay(() => IsTradingActive, () => TradingState, (s) => false)
            });
          } catch (Exception exc) {
            Log = exc;
          }
        }
        return GetTradingState();
      }
      set {
        OnPropertyChanged(() => TradingState);
      }
    }

    private bool GetTradingState() {
      return Strategy != Strategies.None && IsTradingActive
       && (_buyLevel != null && _buyLevel.CanTrade || _sellLevel != null && _sellLevel.CanTrade);
    }

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
