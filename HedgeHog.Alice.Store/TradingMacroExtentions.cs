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

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    static System.Web.Caching.Cache _pendingEntryOrders = HttpRuntime.Cache;

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

    public TradingMacro() {
      SuppRes.AssociationChanged += new CollectionChangeEventHandler(SuppRes_AssociationChanged);
    }
    ~TradingMacro() {
      var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
      if (fw != null && fw.IsLoggedIn)
        fw.DeleteOrders(fw.GetEntryOrders(Pair, true));
      else if (Strategy.HasFlag(Strategies.Hot))
        MessageBox.Show("Account is already logged off. Unable to close Entry Orders.");
    }

    void SuppRes_AssociationChanged(object sender, CollectionChangeEventArgs e) {
      switch (e.Action) {
        case CollectionChangeAction.Add:
          ((Store.SuppRes)e.Element).RateChanged += SuppRes_RateChanged;
          ((Store.SuppRes)e.Element).IsActiveChanged += SuppRes_IsActiveChanged;
          ((Store.SuppRes)e.Element).EntryOrderIdChanged += SuppRes_EntryOrderIdChanged;
          break;
        case CollectionChangeAction.Refresh:
          ((EntityCollection<SuppRes>)sender).ToList()
            .ForEach(sr => {
              sr.RateChanged += SuppRes_RateChanged;
              sr.IsActiveChanged += SuppRes_IsActiveChanged;
              sr.EntryOrderIdChanged += SuppRes_EntryOrderIdChanged;
            });
          break;
        case CollectionChangeAction.Remove:
          ((Store.SuppRes)e.Element).RateChanged -= SuppRes_RateChanged;
          ((Store.SuppRes)e.Element).IsActiveChanged -= SuppRes_IsActiveChanged;
          ((Store.SuppRes)e.Element).EntryOrderIdChanged -= SuppRes_EntryOrderIdChanged;
          break;
      }
      SetEntryOrdersBySuppResLevels();
    }

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
      SetEntryOrdersBySuppResLevels();
    }
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
          SetEntryOrdersBySuppResLevels();
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
          SetEntryOrdersBySuppResLevels();
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
          OnTakeProfitChanged();
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
      _gannAngles = GannAnglesList.FromString(GannAngles).Where(a => a.IsOn).Select(a => a.Value).ToArray();
      OnPropertyChanged("GannAngles_");
      return;
      _gannAngles = GannAngles.Split(',')
        .Select(a => (double)System.Linq.Dynamic.DynamicExpression.ParseLambda(new ParameterExpression[0], typeof(double), a).Compile().DynamicInvoke())
        .ToArray();
    }
    double[] _gannAngles;
    public double[] GannAnglesArray { get { return _gannAngles; } }

    public double Slope { get { return CorridorStats == null ? 0 : CorridorStats.Slope; } }
    public int GetGannAngleIndex(int indexOld) {
      var ratesForGann = SetGannAngles().Reverse().ToList();
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
        OnScanCorridor();
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

    public bool HasCorridor { get { return CorridorStats.IsCurrent; } }
    readonly CorridorStatistics _corridorStatsEmpty = new CorridorStatistics();
    private CorridorStatistics _CorridorStats;
    public CorridorStatistics CorridorStats {
      get { return _CorridorStats ?? _corridorStatsEmpty; }
      set {
        var datePrev = _CorridorStats == null ? DateTime.MinValue : _CorridorStats.StartDate;
        _CorridorStats = value;
        //CorridorStatsArray.ToList().ForEach(cs => cs.IsCurrent = cs == value);
        lock (_rateArrayLocker) {
          RatesArraySafe.ToList().ForEach(r => r.PriceAvg1 = r.PriceAvg2 = r.PriceAvg3 = r.PriceAvg02 = r.PriceAvg03 = 0);
          if (value != null) {
            CorridorStats.Rates
              .SetCorridorPrices(CorridorStats.Coeffs, CorridorStats.HeightUp0, CorridorStats.HeightDown0, CorridorStats.HeightUp, CorridorStats.HeightDown,
                r => r.PriceAvg, r => r.PriceAvg1, (r, d) => r.PriceAvg1 = d
                , (r, d) => r.PriceAvg02 = d, (r, d) => r.PriceAvg03 = d
                , (r, d) => r.PriceAvg2 = d, (r, d) => r.PriceAvg3 = d
              );

            CorridorAngle = CorridorStats.Slope;
            CalculateCorridorHeightToRatesHeight();
            if (false && !IsGannAnglesManual)
              SetGannAngleOffset(value);
            UpdateTradingGannAngleIndex();
          }
        }

        #region PropertyChanged
        RaiseShowChart();
        OnPropertyChanged(TradingMacroMetadata.CorridorStats);
        OnPropertyChanged(TradingMacroMetadata.HasCorridor);
        OnPropertyChanged(TradingMacroMetadata.CorridorHeightByRegressionInPips);
        OnPropertyChanged(TradingMacroMetadata.CorridorHeightByRegressionInPips0);
        OnPropertyChanged(TradingMacroMetadata.CorridorsRatio);
        OnPropertyChanged(TradingMacroMetadata.OpenSignal);
        #endregion
      }
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

    public Rate[] SetGannAngles() {
      if (true || CorridorStats == null) return new Rate[0];
      RatesArraySafe.ToList().ForEach(r => Enumerable.Range(0, GannAnglesArray.Length).ToList()
        .ForEach(i => { if (r.GannPrices.Length > i) r.GannPrices[i] = 0; }));
      var ratesForGann = RatesArraySafe.SkipWhile(r => r.StartDate < this.GannAnglesAnchorDate.GetValueOrDefault(CorridorStats.StartDate)).ToArray();
      var rateStart = this.GannAnglesAnchorDate.GetValueOrDefault(new Func<DateTime>(() => {
        var rateStop = Slope > 0 ? ratesForGann.OrderBy(r => r.PriceAvg).LastOrDefault() : ratesForGann.OrderBy(r => r.PriceAvg).FirstOrDefault();
        if (rateStop == null) return DateTime.MinValue;
        var ratesForStart = ratesForGann.Where(r => r < rateStop);
        if (ratesForStart.Count() == 0) ratesForStart = ratesForGann;
        return (CorridorStats.Slope > 0 ? ratesForStart.OrderBy(CorridorStats.priceLow).First() : ratesForStart.OrderBy(CorridorStats.priceHigh).Last()).StartDate;
      })());
      ratesForGann = ratesForGann.Where(r => r.StartDate >= rateStart).OrderBars().ToArray();
      if (ratesForGann.Length == 0) return new Rate[0];
      //var interseption = Slope > 0 ? Math.Min(ratesForGann[0].PriceAvg3, ratesForGann[0].PriceLow) : Math.Max(ratesForGann[0].PriceAvg2, ratesForGann[0].PriceHigh);
      var interseption = Slope > 0 ? ratesForGann[0].PriceLow : ratesForGann[0].PriceHigh;
      Enumerable.Range(0, ratesForGann.Count()).AsParallel().ForAll(i => {
        var rate = ratesForGann[i];
        if (rate.GannPrices.Length != GannAnglesArray.Length) rate.GannPrices = new double[GannAnglesArray.Length];
        for (var j = 0; j < GannAnglesArray.Length; j++) {
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
      if (GannAngleActive >= 0 && rateLast.GannPrices.Length > GannAngleActive && GannAngleActive.Between(0, GannAnglesArray.Length - 1))
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

    public CorridorCalculationMethod CorridorCalcMethod {
      get { return (CorridorCalculationMethod)this.CorridorMethod; }
      set {
        if (this.CorridorMethod != (int)value) {
          this.CorridorMethod = (int)value;
          OnPropertyChanged("CorridorCalcMethod");
        }
      }
    }

    public int PositionsBuy { get { return Trades.IsBuy(true).Length; } }
    public int PositionsSell { get { return Trades.IsBuy(false).Length; } }

    public double PipsPerPosition {
      get { return Trades.Length < 2 ? 0 : InPips(Trades.Max(t => t.Open) - Trades.Min(t => t.Open)) / (Trades.Length - 1); }
    }



    public double CorridorHeightByRegression0 { get { return CorridorStats == null ? 0 : CorridorStats.HeightUpDown0; } }
    public double CorridorHeightByRegression { get { return CorridorStats == null ? 0 : CorridorStats.HeightUpDown; } }
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

    public void ResetTradeReady() { BuyWhenReady = SellWhenReady = false; }
    bool _buyWhenReady;
    public bool BuyWhenReady {
      get { return _buyWhenReady; }
      set {
        if (_buyWhenReady == value) return;
        _buyWhenReady = value;
        if (value) SellWhenReady = false;
      }
    }
    bool _sellWhenReady;
    public bool SellWhenReady {
      get { return _sellWhenReady; }
      set {
        if (_sellWhenReady == value) return;
        _sellWhenReady = value;
        if (value) BuyWhenReady = false;
      }
    }

    public void DisableTrading() {
      switch (Strategy) {
        case Strategies.SuppRes:
        case Strategies.Massa:
          if (TradingRatio > 0) TradingRatio = -TradingRatio;
          break;
      }
    }
    public void EnableTrading() {
      if (TradingRatio < 0) TradingRatio = -TradingRatio;
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
    bool? GetSignal(bool? signal) { return !signal.HasValue ? null : ReverseStrategy ? !signal : signal; }

    static Func<Rate, double>[] foos = new[] { new Func<Rate, double>(r => r.PriceAvg2), new Func<Rate, double>(r => r.PriceAvg3) };
    private Func<Rate, double> GetCurrentCorridor() {
      foreach (var rate in RatesArraySafe.Reverse())
        foreach (var foo in foos)
          if (foo(rate).Between(rate.PriceLow, rate.PriceHigh)) return foo;
      return null;
    }

    bool tradeOnCrossOnce;
    public bool? OpenSignal {
      get {
        if (CorridorStats == null || RatesArraySafe.Count() == 0) return null;
        var slope = CorridorStats.Slope;
        var rates = RatesArraySafe.ToList();
        var rateLast = Strategy == Strategies.Gann ? GetLastRateWithGannAngle() : GetLastRate();
        var lastIndex = rates.IndexOf(rateLast);
        var ratePrev = rates[lastIndex - 1];
        var ratePrev2 = rates[lastIndex - 2];
        var ratePrev3 = rates[lastIndex - 3];
        var ratePrev4 = rates[lastIndex - 4];
        rates = rates.TakeWhile(r => r <= rateLast).ToList();
        bool? ret = GetSuppResSignal(rateLast, ratePrev);
        switch (Strategy) {
          case Strategies.Vilner:
            ret = GetSuppResSignal(rateLast, ratePrev);
            if (ret.HasValue) return ret;
            return GetSignal(GetSuppResSignal(rateLast, ratePrev2));
          case Strategies.Massa:
            var retMassa = GetSuppResSignal(rateLast, ratePrev) ??
                   GetSuppResSignal(rateLast, ratePrev2) ??
                   GetSuppResSignal(rateLast, ratePrev3);
            if (tradeOnCrossOnce && retMassa.HasValue)
              tradeOnCrossOnce = false;
            return retMassa;
            if (!retMassa.HasValue) return null;
            if (retMassa == true && CenterOfMass.PriceAvg <= MagnetPrice) return true;
            if (retMassa == false && CenterOfMass.PriceAvg >= MagnetPrice) return false;
            return null;
            if (IsResistanceCurentHigh() && rateLast.PriceAvg >= ResistanceCurrent().Rate) return true;
            if (IsSupportCurentLow() && rateLast.PriceAvg <= SupportCurrent().Rate) return false;
            var ratesReversed = RatesArraySafe.Reverse().ToArray();
            var resistanceHigh = ResistanceHigh().Rate;
            var upRate = ratesReversed.FirstOrDefault(r => r.PriceHigh >= resistanceHigh) ?? rates.High();
            var supportLow = SupportLow().Rate;
            var downRate = ratesReversed.FirstOrDefault(r => r.PriceLow <= supportLow) ?? rates.Low();
            var up = downRate.StartDate > upRate.StartDate;
            if (up && IsResistanceCurentLow() && rateLast.PriceAvg > ResistanceCurrent().Rate) return true;
            if (!up && IsSupportCurentHigh() && rateLast.PriceAvg < SupportCurrent().Rate) return false;
            return null;
            return GetSuppResSignal(rateLast, ratePrev);
          case Strategies.SuppRes:
            return GetSuppResSignal(rateLast, ratePrev);
          case Strategies.Gann:
            var gannPrice = GannPriceForTrade();
            if (!double.IsNaN(gannPrice)) {
              if (rateLast.PriceLow > gannPrice) return true;
              if (rateLast.PriceHigh < gannPrice) return false;
              return null;
              if (gannPriceLow(rateLast) > gannPrice && ratePrev.PriceLow < gannPrice) return GetSignal(true);
              if (gannPriceHigh(rateLast) < gannPrice && ratePrev.PriceHigh > gannPrice) return GetSignal(false);
            }
            return null;
          case Strategies.Range:
            //if (CorridorAngle > 0 && rateLast.PriceHigh > rateLast.PriceAvg1) return false;
            //if (CorridorAngle > 0 && ratePrev.PriceHigh > rateLast.PriceAvg1) return false;
            //if (CorridorAngle < 0 && rateLast.PriceLow < rateLast.PriceAvg3) return true;
            //if (CorridorAngle < 0 && ratePrev.PriceLow < rateLast.PriceAvg3) return true;
            Func<Rate, double> corridor = rate => GannPriceForTrade(rate);// GetCurrentCorridor();
            if (corridor == null) {
              var corridorObject = new[] { 
              //new {name = "PriceAvg1", price = new Func<Rate, double>(r => r.PriceAvg1), distance = (rateLast.PriceAvg - rateLast.PriceAvg1).Abs() } ,
              //new {name = "PriceAvg02",  price = new Func<Rate, double>(r => r.PriceAvg02), distance = (rateLast.PriceAvg - rateLast.PriceAvg02).Abs() } ,
              //new { name = "PriceAvg03", price = new Func<Rate, double>(r => r.PriceAvg03), distance = (rateLast.PriceAvg - rateLast.PriceAvg03).Abs() } ,
              new { name = "PriceAvg2", price = new Func<Rate, double>(r => r.PriceAvg2), distance = (rateLast.PriceAvg - rateLast.PriceAvg2).Abs() } ,
              new { name = "PriceAvg3", price = new Func<Rate, double>(r => r.PriceAvg3), distance = (rateLast.PriceAvg - rateLast.PriceAvg3).Abs() } 
              }.OrderBy(a => a.distance).First();
              corridor = corridorObject.price;
            }
            ret = GetRangeSignal(rateLast, ratePrev, corridor)
              ?? GetRangeSignal(rateLast, ratePrev2, corridor)
              ?? GetRangeSignal(rateLast, ratePrev3, corridor)
              ?? GetRangeSignal(rateLast, ratePrev4, corridor);
            //if (ret.HasValue && rateLast.PriceAvg.Between(rateLast.PriceAvg2,rateLast.PriceAvg3)) {
            //  if (ret == true && CorridorAngle < 0 ) return null;
            //  if (ret == false && CorridorAngle > 0 ) return null;
            //}
            return ret;
            return GetSignal(CrossOverSignal(CorridorStats.priceHigh(rateLast), CorridorStats.priceHigh(ratePrev), CorridorStats.priceLow(rateLast), CorridorStats.priceLow(ratePrev),
                                   rateLast.PriceAvg2, ratePrev.PriceAvg2) ??
                             CrossOverSignal(CorridorStats.priceHigh(rateLast), CorridorStats.priceHigh(ratePrev), CorridorStats.priceLow(rateLast), CorridorStats.priceLow(ratePrev),
                                    rateLast.PriceAvg3, ratePrev.PriceAvg3)
                   );
        }
        return null;
      }
    }

    private bool? GetRangeSignal(Rate rateLast, Rate ratePrev, Func<Rate, double> level) {
      bool? signal = null;
      if (!TradeOnCrossOnly)
        return GetSignal(
        rateLast.PriceLow > level(rateLast) ? true
        : rateLast.PriceHigh < level(rateLast) ? false
        : (bool?)null
        );
      if (CrossUp(rateLast.PriceLow, ratePrev.PriceLow, level(rateLast), level(ratePrev)))
        signal = true;
      if (CrossDown(rateLast.PriceHigh, ratePrev.PriceHigh, level(rateLast), level(ratePrev)))
        signal = false;
      return GetSignal(signal);
    }
    private bool? GetSuppResSignal(Rate rateLast, Rate ratePrev) {
      bool? signal = null;
      Func<Rate, double> priceLow = r => ResistancePrice == SupportPrice ? r.PriceLow : r.PriceAvg;
      Func<Rate, double> priceHigh = r => ResistancePrice == SupportPrice ? r.PriceHigh : r.PriceAvg;
      if (!IsSuppResManual && !TradeOnCrossOnly && !tradeOnCrossOnce)
        return GetSignal(
        priceLow(rateLast) > ResistancePrice /*&& CurrentPrice.Average - ResistancePrice < SpreadForSuppRes*/ ? true
        : priceHigh(rateLast) < SupportPrice /*&& SupportPrice - CurrentPrice.Average < SpreadForSuppRes*/ ? false
        : (bool?)null
        );
      //return null;
      if (CrossUp(priceLow(rateLast), ratePrev.PriceLow, ResistancePrice, ResistancePrice))
        signal = true;
      if (CrossDown(priceHigh(rateLast), ratePrev.PriceHigh, SupportPrice, SupportPrice))
        signal = false;
      return GetSignal(signal);
      return GetSignal(CrossOverSignal(suppResPriceHigh(rateLast), suppResPriceHigh(ratePrev), suppResPriceLow(rateLast), suppResPriceLow(ratePrev),
                             ResistancePrice, ResistancePrice) ??
                       CrossOverSignal(suppResPriceHigh(rateLast), suppResPriceHigh(ratePrev), suppResPriceLow(rateLast), suppResPriceLow(ratePrev),
                              SupportPrice, SupportPrice)
             );
    }

    private bool? CrossOverSignal(double priceBuyLast, double priceBuyPrev, double priceSellLast, double priceSellPrev, double tresholdPriceLast, double tresholdPricePrev) {
      if (CrossUp(priceBuyLast, priceBuyPrev, tresholdPriceLast, tresholdPricePrev)) return true;
      if (CrossDown(priceSellLast, priceSellPrev, tresholdPriceLast, tresholdPricePrev)) return false;
      return null;
    }

    private static bool CrossDown(double priceSellLast, double priceSellPrev, double tresholdPriceLast, double tresholdPricePrev) {
      return priceSellLast < tresholdPriceLast && priceSellPrev > tresholdPricePrev;
    }

    private static bool CrossUp(double priceBuyLast, double priceBuyPrev, double tresholdPriceLast, double tresholdPricePrev) {
      return priceBuyLast > tresholdPriceLast && priceBuyPrev < tresholdPricePrev;
    }

    private bool? CrossOverSignal(Rate rateLast, Rate ratePrev, double tresholdPriceLast, double tresholdPricePrev, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      if (priceHigh(rateLast) > tresholdPriceLast && priceHigh(ratePrev) < tresholdPricePrev) return true;
      if (priceLow(rateLast) < tresholdPriceLast && priceLow(ratePrev) > tresholdPricePrev) return false;
      return null;
    }

    bool HasCrossedUp(double priceCurrent, double pricePrevious, double treshold) {
      return priceCurrent > treshold && pricePrevious < treshold;
    }
    bool HasCrossedDown(double priceCurrent, double pricePrevious, double treshold) {
      return priceCurrent < treshold && pricePrevious > treshold;
    }

    private Rate GetLastRateWithGannAngle() {
      return GetLastRate(RatesArraySafe.SkipWhile(r => r.GannPrices.Length == 0).TakeWhile(r => r.GannPrices.Length > 0).ToArray());
    }
    private Rate GetLastRate() { return GetLastRate(RatesArraySafe); }
    private Rate GetLastRate(ICollection<Rate> rates) {
      if (rates.Count == 0) return null;
      var rateLast = rates.Skip(rates.Count - 2)
        .LastOrDefault(LastRateFilter);
      return rateLast ?? rates.Last();
    }

    private bool LastRateFilter(Rate r) {
      return r.StartDate <= CurrentPrice.Time - TimeSpan.FromMinutes((int)BarPeriod);
    }
    static Func<Rate, double> gannPriceHigh = rate => rate.PriceAvg;
    static Func<Rate, double> gannPriceLow = rate => rate.PriceAvg;

    static Func<Rate, double> suppResPriceHigh = rate => rate.PriceHigh;
    static Func<Rate, double> suppResPriceLow = rate => rate.PriceLow;

    public double CorridorHeightToSpreadRatio { get { return CorridorStats.HeightUpDown / SpreadForCorridor; } }
    public double CorridorHeight0ToSpreadRatio { get { return CorridorStats.HeightUpDown0 / SpreadForCorridor; } }
    public bool? CloseSignal {
      get {
        if (CorridorStats == null || CloseOnOpen) return null;
        Strategies strategy = Strategy ^ Strategies.Auto ^ Strategies.Stop;
        switch (strategy) {
          case Strategies.Vilner:
            var buys = Trades.IsBuy(true);
            if (buys.GrossInPips() > TradingDistanceInPips// / (buys.Length*2)
              //|| buys.Length > 3 && buys.GrossInPips()>0
              ) return GetSignal(true);
            var sells = Trades.IsBuy(false);
            if (sells.GrossInPips() > TradingDistanceInPips// / (sells.Length*2)
              //|| sells.Length > 3 && sells.GrossInPips() > 0
              ) return GetSignal(false);
            return null;
          case Strategies.Hot:
            if (CurrentGross > TakeProfitPips)
              TradesManager.ClosePair(Pair);
            return null;
          case Strategies.Massa:
            if (CurrentGross >= TakeProfitPips) {
              tradeOnCrossOnce = true;
              TradesManager.ClosePair(Pair);
              return null;
            }
            {
              var allowedLot = AllowedLotSizeCore(Trades);
              var currentLot = Trades.Lots().ToInt();
              if (currentLot / allowedLot >= 7) {
                tradeOnCrossOnce = true;
                TradesManager.ClosePair(Pair, Trades[0].Buy, currentLot - allowedLot);
                return null;
              }
              if (currentLot > LotSize * 2 && allowedLot == LotSize) {
                tradeOnCrossOnce = true;
                TradesManager.ClosePair(Pair, Trades[0].Buy, currentLot - allowedLot);
                return null;
              }
              if (!IsSuppResManual) {
                var trade = Trades.LastOrDefault();
                if (Trades.GrossInPips() < -InPips(SpreadForSuppRes * 3)) {
                  tradeOnCrossOnce = true;
                  TradesManager.ClosePair(Pair);
                  return null;
                }
              }
            }
            return !OpenSignal;
            if (TradesManager.IsHedged) {
              var bs = Trades.IsBuy(true);
              var ss = Trades.IsBuy(false);
              //if (bs.Select(t => t.PL).DefaultIfEmpty().Max() > TakeProfitPips * RangeRatioForTradeLimit) return true;
              //if (ss.Select(t => t.PL).DefaultIfEmpty().Max() > TakeProfitPips * RangeRatioForTradeLimit) return false;
              if (bs.Length == 1 && bs.GrossInPips() > TakeProfitPips) return true;
              if (ss.Length == 1 && ss.GrossInPips() > TakeProfitPips) return false;
              var takeProfitByLots = TakeProfitPips / (bs.Sum(t => t.Lots) / LotSize);
              if (bs.Length > 1 && bs.GrossInPips() > takeProfitByLots) return true;
              if (ss.Length > 1 && ss.GrossInPips() > takeProfitByLots) return false;
              var lots = "".Select(a => new { lots = 0, count = 0, IsBuy = false }).Where(a => a.count > 0).ToList();
              lots.AddRange(new[] { new { lots = bs.Sum(t => t.Lots), count = bs.Length, IsBuy = true }, new { lots = ss.Sum(t => t.Lots), count = ss.Length, IsBuy = true } });
              var lotMin = lots.Min(t => t.lots);
              var lotMax = lots.Max(t => t.lots);
              var countMin = lots.Min(t => t.count);
              var countMax = lots.Max(t => t.count);
              var b = lots.OrderBy(l => l.lots).First().IsBuy;
              if (countMin > 2 && countMax > 3/*&& (double)lotMax / lotMin >= 2*/) {
                TradesManager.ClosePair(Pair, true);//, lotMin - (b ? LotSize : 0));
                TradesManager.ClosePair(Pair, false);//, lotMin- (!b ? LotSize : 0));
              }
            }
            return null;
            if (Trades.Length > 0 && Trades.GrossInPips() < InPips(SupportLow().Rate - SupportHigh().Rate) / 20)
              return Trades.First().Buy;
            var comm = Trades.Select(t => TradesManager.CommissionByTrade(t)).Sum();
            var currentLoss = CloseOnProfitOnly ? CurrentLoss - comm : 0;
            if (CurrentGross >= currentLoss / 2 && Trades.GrossInPips() >= InPips(RatesStDev * RangeRatioForTradeLimit)) return Trades.First().Buy;
            if (CurrentGross >= currentLoss / 2 && Trades.GrossInPips() >= InPips(RatesStDev * RangeRatioForTradeLimit * 2)) return Trades.First().Buy;
            if (Trades.Sum(t => t.Lots) >= LotSize * 10 && CurrentGross >= 0) return Trades.First().Buy;
            return null;// GetSignal(!OpenSignal);
            if (!CloseOnProfit || Trades.Length == 0) return null;
            var close = CurrentGross >= TakeProfitPips * LotSize / 10000.0;
            if (close) {
              DisableTrading();
              return Trades.First().Buy;
            }
            return null;
          //case Strategies.Massa:
          //  var distance = Math.Min(SpreadLong, SpreadShort);
          //  if (PriceCmaDiffLow < 0) return false;
          //  if (PriceCmaDiffHigh > 0 ) return true;
          //  return null;
          case Strategies.Gann: return !OpenSignal;
          case Strategies.SuppRes: return !OpenSignal;
          case Strategies.Range:
            if (Trades.GrossInPips() > TakeProfitPips)
              TradesManager.ClosePair(Pair);
            return !OpenSignal;
        }
        return null;
      }
    }

    public void ResetLock() { IsBuyLock = IsSellLock = false; }
    bool _isBuyLock;
    public bool IsBuyLock {
      get { return _isBuyLock; }
      set {
        _isBuyLock = value;
        if (value) IsSellLock = false;
        OnPropertyChanged("IsBuyLock");
      }
    }
    bool _isSellLock;
    public bool IsSellLock {
      get { return _isSellLock; }
      set {
        _isSellLock = value;
        if (value) IsBuyLock = false;
        OnPropertyChanged("IsSellLock");
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
    Func<ITradesManager> _TradesManager = () => null;
    public ITradesManager TradesManager { get { return _TradesManager(); } }
    public void SubscribeToTradeClosedEVent(Func<ITradesManager> getTradesManager) {
      this._TradesManager = getTradesManager;
      this.TradesManager.TradeClosed += TradesManager_TradeClosed;
      this.TradesManager.TradeAdded += TradesManager_TradeAddedGlobal;
      var fw = GetFXWraper();
      if (fw != null) {
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
          lock (_pendingEntryOrders) {
            var po = _pendingEntryOrders["EO"];
            if (po != null) {
              _pendingEntryOrders.Remove("EO");
              Debug.WriteLine("Pending Entry Order released:" + po);
            }
          }
        } catch (Exception exc) {
          Log = exc;
        }
      }
      try {
        CalcTakeProfitDistance();
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
      EnsureActiveSuppReses();
      SetEntryOrdersBySuppResLevels();
      RaisePositionsChanged();
      try {
        if (false && IsSpreadShortToLongRatioOk) {
          if (e.Trade.IsBuy) {
            //ResistanceCurrent().TradesCount++;
            Resistances.ToList().ForEach(r => r.TradesCount++/* = (int)Math.Round(Math.Max(1, r.TradesCount) * MaxLotByTakeProfitRatio)*/);
          } else {
            //SupportCurrent().TradesCount++;
            Supports.ToList().ForEach(r => r.TradesCount++/* = (int)Math.Round(Math.Max(1, r.TradesCount) * MaxLotByTakeProfitRatio)*/);
          }
        }
        //SuppResResetInactiveTradeCounts();
      } catch (Exception exc) {
        Log = exc;
      }
    }
    public void AddTradeAddedHandler() {
      TradesManager.TradeAdded += TradesManager_TradeAdded;
    }

    void TradesManager_TradeAdded(object sender, TradeEventArgs e) {
      if (!IsMyTrade(e.Trade)) return;
      try {
        EnsureActiveSuppReses();
        var tm = sender as ITradesManager;
        tm.TradeAdded -= TradesManager_TradeAdded;
        Trade trade = e.Trade;
        if (Strategy == Strategies.SuppRes) {
          var suppResSpreadMultiplier = 0;
          var offsetBySpread = SpreadShort * suppResSpreadMultiplier;
          //if (trade.Buy && trade.Open < ResistancePrice) SupportPrice = trade.Open - offsetBySpread;
          //if (!trade.Buy && trade.Open > SupportPrice) ResistancePrice = trade.Open + offsetBySpread;
        }
        RaiseShowChart();
      } catch (Exception exc) {
        Log = exc;
      }
    }
    bool IsMyTrade(Trade trade) { return trade.Pair == Pair; }
    bool IsMyOrder(Order order) { return order.Pair == Pair; }
    public void UnSubscribeToTradeClosedEVent(ITradesManager tradesManager) {
      if (this.TradesManager != null) {
        this.TradesManager.TradeClosed -= TradesManager_TradeClosed;
        this.TradesManager.TradeAdded -= TradesManager_TradeAdded;
        this.TradesManager.TradeAdded -= TradesManager_TradeAddedGlobal;
      }
      if (tradesManager != null) {
        tradesManager.TradeClosed -= TradesManager_TradeClosed;
        tradesManager.TradeAdded -= TradesManager_TradeAdded;
        tradesManager.TradeAdded -= TradesManager_TradeAddedGlobal;
      }
    }
    void TradesManager_TradeClosed(object sender, TradeEventArgs e) {
      if (!IsMyTrade(e.Trade)) return;
      EnsureActiveSuppReses();
      SetEntryOrdersBySuppResLevels();
      RaisePositionsChanged();
      RaiseShowChart();
    }

    private void RaisePositionsChanged() {
      OnPropertyChanged("PositionsSell");
      OnPropertyChanged("PipsPerPosition");
      OnPropertyChanged("PositionsBuy");
      OnPropertyChanged("PipsPerPosition");
    }
    #endregion

    protected Dictionary<string, TradeStatistics> TradeStatisticsDictionary = new Dictionary<string, TradeStatistics>();
    public void SetTradesStatistics(Price price, Trade[] trades) {
      foreach (var trade in trades)
        SetTradeStatistics(price, trade);
    }
    public TradeStatistics SetTradeStatistics(Price price, Trade trade) {
      if (!TradeStatisticsDictionary.ContainsKey(trade.Id))
        TradeStatisticsDictionary.Add(trade.Id, new TradeStatistics());
      var ts = TradeStatisticsDictionary[trade.Id];
      if (!trade.Buy && ts.Resistanse == 0 && HasCorridor)
        ts.Resistanse = CorridorRates.OrderBars().Max(CorridorStats.priceHigh);
      if (trade.Buy && ts.Support == 0 && HasCorridor)
        ts.Support = CorridorRates.OrderBars().Min(CorridorStats.priceLow);
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

    bool IsCorridorAngleOk { get { return CorridorAngle.Abs() <= TradingAngleRange; } }

    double SpreadForSuppRes { get { return Math.Max(SpreadShort, SpreadLong); } }

    private bool IsEntityStateOk {
      get {
        return EntityState != System.Data.EntityState.Detached && EntityState != System.Data.EntityState.Deleted;
      }
    }
    const double suppResDefault = double.NaN;
    public double SupportPrice {
      get {
        if (!IsEntityStateOk || !SuppRes.IsLoaded) return suppResDefault;
        if (Supports.Length == 0)
          AddSupport(RatesArraySafe.Min(CorridorStats.priceLow));
        return SupportCurrent().Rate;
      }
    }
    private void SupportsResetInactiveTradeCounts() {
      SuppResResetInactiveTradesCounts(Supports);
    }


    public double ResistancePrice {
      get {
        if (!IsEntityStateOk || !SuppRes.IsLoaded) return suppResDefault;
        if (Resistances.Length == 0)
          AddResistance(RatesArraySafe.Max(CorridorStats.priceHigh));
        return ResistanceCurrent().Rate;
      }
    }

    private void ResistancesResetInactiveTradeCounts() {
      SuppResResetInactiveTradesCounts(Resistances);
    }

    private void SuppResResetInactiveTradesCounts(SuppRes[] suppReses) {
      var current = SuppResCurrent(suppReses);
      SuppResResetTradeCounts(suppReses.Where(r => r != current));
    }

    public void SuppResResetInactiveTradeCounts() {
      ResistancesResetInactiveTradeCounts();
      SupportsResetInactiveTradeCounts();
    }
    public void SuppResResetAllTradeCounts(int tradesCount = 0) { SuppResResetTradeCounts(SuppRes, tradesCount); }
    public static void SuppResResetTradeCounts(IEnumerable<SuppRes> suppReses, double tradesCount = 0) {
      if (tradesCount < 0)
        suppReses.ToList().ForEach(sr => sr.TradesCount = Math.Max(Store.SuppRes.TradesCountMinimum, sr.TradesCount + tradesCount));
      else suppReses.ToList().ForEach(sr => sr.TradesCount = Math.Max(Store.SuppRes.TradesCountMinimum, tradesCount));
    }

    private bool IsSupportCurentLow() {
      return SupportLow() == SupportCurrent();
    }

    private Store.SuppRes SupportLow() {
      return Supports.OrderBy(s => s.Rate).First();
    }
    private bool IsSupportCurentHigh() {
      return SupportHigh() == SupportCurrent();
    }

    private Store.SuppRes SupportHigh() {
      return Supports.OrderBy(s => s.Rate).Last();
    }
    private Store.SuppRes SupportCurrent() {
      return SuppResCurrent(Supports);
    }
    private Store.SuppRes[] SupportsNotCurrent() {
      return SuppResNotCurrent(Supports);
    }
    private bool IsResistanceCurentLow() {
      return ResistsnceLow() == ResistanceCurrent();
    }

    private Store.SuppRes ResistsnceLow() {
      return Resistances.OrderBy(s => s.Rate).First();
    }
    private bool IsResistanceCurentHigh() {
      return ResistanceHigh() == ResistanceCurrent();
    }

    private Store.SuppRes ResistanceHigh() {
      return Resistances.OrderBy(s => s.Rate).Last();
    }
    private Store.SuppRes ResistanceCurrent() {
      return SuppResCurrent(Resistances);
    }
    private Store.SuppRes[] ResistancesNotCurrent() {
      return SuppResNotCurrent(Resistances);
    }
    private Store.SuppRes[] SuppResNotCurrent(SuppRes[] suppReses) {
      return suppReses.OrderBy(s => (s.Rate - CurrentPrice.Ask).Abs()).Skip(1).ToArray();
    }
    private Store.SuppRes SuppResCurrent(SuppRes[] suppReses) {
      foreach (var rate in RatesArraySafe.Reverse())
        foreach (var sr in suppReses)
          if (sr.Rate.Between(rate.PriceLow, rate.PriceHigh)) return sr;
      return suppReses.OrderBy(s => (s.Rate - CurrentPrice.Ask).Abs()).First();
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

    Dictionary<SuppRes, List<int>> maxTradeCounts = new Dictionary<SuppRes, List<int>>();


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
        GlobalStorage.Context.SuppRes.AddObject(sr);
        GlobalStorage.Context.SaveChanges();
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
        GlobalStorage.Context.DeleteObject(suppRes);
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
      get { return !double.IsNaN(_CenterOfMassBuy) ? _CenterOfMassBuy : centerOfMassSell(CenterOfMass); }
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

    double[] StDevLevels = new double[0];
    bool _doCenterOfMass = true;
    bool _areSuppResesActive { get { return true; } }
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
      var rates = RatesArraySafe.Where(LastRateFilter).ToArray();
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
        var rateLast = RatesArraySafe.LastOrDefault(r => r.PriceAvg1 > 0);
        if (rateLast != null) {
          support.Value.Rate = ReverseStrategy ? rateLast.PriceAvg3 : rateLast.PriceAvg2;
          resistance.Value.Rate = ReverseStrategy ? rateLast.PriceAvg2 : rateLast.PriceAvg3;
          MagnetPrice = RatesArraySafe.Sum(r => r.PriceAvg * r.Volume) / RatesArraySafe.Sum(r => r.Volume);
          return;
        }

        throw new InvalidOperationException("Unsupported SuppResLevelsCount:" + SuppResLevelsCount);

        var ratesForHot = CorridorRates.ToArray();
        if (ratesForHot.Length > 0) {
          var average = MagnetPrice = ratesForHot.Average(r => r.PriceAvg);
          var stDev = ratesForHot.StDev(r => r.PriceAvg) * 2;
          resistance.Value.Rate = average - stDev;
          //if (!Trades.HaveSell())
          support.Value.Rate = average + stDev;
        }
        return;
      }

      var rateAverage = rates.Average(r => r.PriceAvg); //CenterOfMassBuy;

      //var fibLevels = Fibonacci.Levels(levelHigh/*.PriceAvg*/, levelLow/*.PriceAvg*/);
      var rateStDevUp = rates.Where(r => r.PriceAvg > rateAverage).ToArray().StDev(r => r.PriceAvg);
      var rateStDevDown = rates.Where(r => r.PriceAvg < rateAverage).ToArray().StDev(r => r.PriceAvg);

      switch (levelsCount) {
        case 6:
          if (StDevLevels.Length > 2) {
            var ratio = StDevLevels[1];
            var rate = rateAverage + rateStDevUp * ratio;
            resistance.Value.Rate = 0;//.PriceAvg;//fibLevels[3];// levelsCount >= 2 ? GetPriceHigh(level) - 0.0001 : GetPriceLow(level);
            support.Value.Rate = rate;

            resistance = resistance.Next;
            support = support.Next;

            rate = rateAverage - rateStDevDown * ratio;
            resistance.Value.Rate = rate;//.PriceAvg; //fibLevels[6];// levelsCount >= 2 ? GetPriceLow(level) + .0001 : GetPriceHigh(level);
            support.Value.Rate = 0;

            resistance = resistance.Next;
            support = support.Next;
          }
          goto case 4;
        case 5: {
            var ratio = StDevLevels.Last();
            resistance.Value.Rate = rateAverage + ratio * (rateStDevUp - rateStDevDown) / 2;
            support.Value.Rate = resistance.Value.Rate;

            resistance = resistance.Next;
            support = support.Next;
          }
          goto case 4;
        case 4:
          if (StDevLevels.Length > 1) {
            var ratio = StDevLevels.Last();
            resistance.Value.Rate = rateAverage + rateStDevUp * ratio;//.PriceAvg;//fibLevels[3];// levelsCount >= 2 ? GetPriceHigh(level) - 0.0001 : GetPriceLow(level);
            support.Value.Rate = resistance.Value.Rate;

            resistance = resistance.Next;
            support = support.Next;

            resistance.Value.Rate = rateAverage - rateStDevDown * ratio;//.PriceAvg; //fibLevels[6];// levelsCount >= 2 ? GetPriceLow(level) + .0001 : GetPriceHigh(level);
            support.Value.Rate = resistance.Value.Rate;

            resistance = resistance.Next;
            support = support.Next;
          }
          goto case 2;
        case 3: {
            var ratio = StDevLevels[0];
            resistance.Value.Rate = rateAverage + ratio * (rateStDevUp - rateStDevDown) / 2;
            support.Value.Rate = resistance.Value.Rate;

            resistance = resistance.Next;
            support = support.Next;
          }
          goto case 2;
        case 20: {
            var com1 = CentersOfMass.CentersOfMass();
            var rateHigh = com1.Max(r => r.PriceHigh);
            var rateLow = com1.Min(r => r.PriceLow);
            resistance.Value.Rate = rateHigh;//.PriceAvg;//fibLevels[3];// levelsCount >= 2 ? GetPriceHigh(level) - 0.0001 : GetPriceLow(level);
            support.Value.Rate = rateHigh;

            resistance = resistance.Next;
            support = support.Next;

            resistance.Value.Rate = rateLow;//.PriceAvg; //fibLevels[6];// levelsCount >= 2 ? GetPriceLow(level) + .0001 : GetPriceHigh(level);
            support.Value.Rate = rateLow;
          }
          return;
        case 2: {
            var com1 = CentersOfMass.CentersOfMass();
            var rateHigh = com1.Max(r => r.PriceHigh);
            var rateLow = com1.Min(r => r.PriceLow);
            var offset1 = RatesStDev * (CorridorRatioForBreakout);// + Trades.Lots() / 1000000);
            resistance.Value.Rate = CenterOfMass.PriceAvg + offset1;
            support.Value.Rate = CenterOfMass.PriceAvg + offset1;

            resistance = resistance.Next;
            support = support.Next;

            resistance.Value.Rate = CenterOfMass.PriceAvg - offset1;
            support.Value.Rate = CenterOfMass.PriceAvg - offset1;
          }
          return;
        case 7:

          resistance.Value.Rate = rateAverage + RatesStDev * 2;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage + RatesStDev;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage - RatesStDev;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage - RatesStDev * 2;
          support.Value.Rate = resistance.Value.Rate;
          return;
        default:
          break;
      }

      #region Trash
      /*
      if (levelsCount == 3) {
        resistance = resistance.Next;
        support = support.Next;
        resistance.Value.Rate = CenterOfMass.PriceAvg; ;
        support.Value.Rate = resistance.Value.Rate;
      } else if (levelsCount >= 4) {
        resistance = resistance.Next;
        support = support.Next;
        resistance.Value.Rate = fibLevels[3];
        support.Value.Rate = resistance.Value.Rate;

        resistance = resistance.Next;
        support = support.Next;
        resistance.Value.Rate = fibLevels[6];
        support.Value.Rate = resistance.Value.Rate;
        if (levelsCount == 5) {
          resistance = resistance.Next;
          support = support.Next;
          resistance.Value.Rate = MagnetPrice;
          support.Value.Rate = resistance.Value.Rate;
        } else if (levelsCount == 6) {
          resistance = resistance.Next;
          support = support.Next;
          resistance.Value.Rate = fibLevels[4];
          support.Value.Rate = resistance.Value.Rate;

          resistance = resistance.Next;
          support = support.Next;
          resistance.Value.Rate = fibLevels[5];
          support.Value.Rate = resistance.Value.Rate;
        }

      } else if (levelsCount == 5) {
        var h6 = rates.Height() / 6;
        var priceHigh = rates.Max(r => r.PriceHigh);
        var priceLow = rates.Min(r => r.PriceLow);

        var level = CentersOfMass.Where(cms => cms.All(r => r.PriceAvg.Between(priceHigh - h6 * 3, priceHigh - h6))).OrderBy(a => a.Length).Last().OrderBy(r => r.Spread).First();
        resistance = resistance.Next;
        support = support.Next;
        resistance.Value.Rate = GetPriceLow(level);
        support.Value.Rate = resistance.Value.Rate;

        level = CentersOfMass.Where(cms => cms.All(r => r.PriceAvg.Between(priceLow + h6, priceLow + h6 * 3))).OrderBy(a => a.Length).Last().OrderBy(r => r.Spread).First();
        resistance = resistance.Next;
        support = support.Next;
        resistance.Value.Rate = GetPriceHigh(level);
        support.Value.Rate = resistance.Value.Rate;

      } else if (levelsCount > 2) {
        var priceAverage = rates.Average(r => r.PriceAvg);
        var up = CenterOfMass.PriceAvg > priceAverage;

        resistance = resistance.Next;
        support = support.Next;

        resistance.Value.Rate = MagnetPrice;
        support.Value.Rate = resistance.Value.Rate;
        if (levelsCount > 3) {
          resistance = resistance.Next;
          support = support.Next;
          var priceShift = (up ? CenterOfMass.PriceAvg - rates.Min(r => r.PriceAvg) : rates.Max(r => r.PriceAvg) - CenterOfMass.PriceAvg) / 3.0;
          var priceStart = up ? CenterOfMass.PriceAvg - priceShift : CenterOfMass.PriceAvg + priceShift;
          var priceEnd = up ? CenterOfMass.PriceAvg - priceShift * 2 : CenterOfMass.PriceAvg + priceShift * 2;
          var cm = CentersOfMass.Where(cms => cms.All(r => r.PriceAvg.Between(priceStart, priceEnd))).OrderBy(a => a.Length).Last().OrderBy(r => r.Spread).First();
          resistance.Value.Rate = GetPriceHigh(cm);
          support.Value.Rate = resistance.Value.Rate;
        }
      }
       * */
      #endregion
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

    class RatesAreNotReadyException : Exception { }
    Schedulers.BackgroundWorkerDispenser<string> backgroundWorkers = new Schedulers.BackgroundWorkerDispenser<string>();
    Rate _rateFirst, _rateLast;

    public Rate RateLast {
      get { return _rateLast; }
      set {
        if (_rateLast != value) {
          _rateLast = value;
          OnPropertyChanged("RateLast");
        }
      }
    }

    object _rateArrayLocker = new object();
    Rate[] _rateArray = new Rate[0];
    public Rate[] RatesArraySafe {
      get {
        lock (_rateArrayLocker)
          try {
            if (RatesInternal.Count < Math.Max(1, BarsCount)) {
              //Log = new RatesAreNotReadyException();
              return new Rate[0];
            }
            if (RatesInternal[0] != _rateFirst || RatesInternal[RatesInternal.Count - 1] != RateLast || _rateArray.Length == 0) {
              _rateFirst = RatesInternal[0];
              RateLast = RatesInternal[RatesInternal.Count - 1];
              //_rateArray = GetRatesForStDev(GetRatesSafe()).ToArray();
              _rateArray = GetRatesSafe();
              RatesHeight = _rateArray.Height();//CorridorStats.priceHigh, CorridorStats.priceLow);
              SetRatesStDev(_rateArray);
              CalcTakeProfitDistance();
              CalculateCorridorHeightToRatesHeight();
            }
            return _rateArray;
          } catch (Exception exc) {
            Log = exc;
            return _rateArray;
          }
      }
    }

    private Rate[] GetRatesSafe() {
      return _limitBarToRateProvider == (int)BarPeriod ? RatesInternal.ToArray() : RatesInternal.GetMinuteTicks((int)BarPeriod, false, false);
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
    public Rate[] RatesCopy() { return RatesArraySafe.Select(r => r.Clone() as Rate).ToArray(); }

    public Rate[] RatesDirection { get; protected set; }
    Rate[] _RateDirection;
    public int RateDirection { get { return Math.Sign(_RateDirection[1].PriceAvg - _RateDirection[0].PriceAvg); } }
    private bool HasRates { get { return RatesArraySafe.Length > 0; } }
    public double InPips(double? d) {
      return TradesManager == null ? double.NaN : TradesManager.InPips(Pair, d);
    }

    double InPoints(double d) {
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
    private double _AvarageLossInPips;
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
          var strategy = Strategy & (Strategies.Breakout | Strategies.Range | Strategies.SuppRes);
          if (strategy == Strategies.SuppRes) strategy = Strategies.Range;
          tradeStrategies[value.Id + ""] = strategy;
          if (-LastTrade.PL > AvarageLossInPips / 10) AvarageLossInPips = Lib.CMA(AvarageLossInPips, 0, 10, LastTrade.PL.Abs());

          ProfitCounter = CurrentLoss >= 0 ? 0 : ProfitCounter + (LastTrade.PL > 0 ? 1 : -1);
          _lastTrade = value;
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

    private double _PowerAverage;
    public double PowerAverage {
      get { return _PowerAverage; }
      set {
        if (_PowerAverage != value) {
          _PowerAverage = value;
          OnPropertyChanged(TradingMacroMetadata.PowerAverage);
        }
      }
    }

    #region Spread
    public double? _SpreadShortLongRatioAverage;
    public double SpreadShortLongRatioAverage {
      get { return _SpreadShortLongRatioAverage.GetValueOrDefault(SpreadShortToLongRatio); }
      set {
        _SpreadShortLongRatioAverage = Lib.CMA(_SpreadShortLongRatioAverage, BarsCount / 10.0, value);
        OnPropertyChanged(Metadata.TradingMacroMetadata.SpreadShortLongRatioAverage);
      }
    }

    public bool IsSpreadShortLongRatioAverageOk {
      get { return SpreadShortToLongRatio > SpreadShortLongRatioAverage; }
    }

    public void SetShortLongSpreads(double spreadShort, double spreadLong) {
      _SpreadShort = spreadShort;
      _SpreadLong = spreadLong;
      SpreadForCorridor = CalcSpreadForCorridor(RatesArraySafe);
      SpreadShortLongRatioAverage = SpreadShortToLongRatio;
      OnPropertyChanged(TradingMacroMetadata.SpreadShort);
      OnPropertyChanged(TradingMacroMetadata.SpreadShortInPips);
      OnPropertyChanged(TradingMacroMetadata.SpreadLong);
      OnPropertyChanged(TradingMacroMetadata.SpreadLongInPips);
      OnPropertyChanged(TradingMacroMetadata.SpreadForCorridorInPips);
      OnPropertyChanged(TradingMacroMetadata.SpreadShortToLongRatio);
      OnPropertyChanged(TradingMacroMetadata.IsSpreadShortToLongRatioOk);
      OnPropertyChanged(TradingMacroMetadata.IsSpreadShortLongRatioAverageOk);
      OnPropertyChanged(TradingMacroMetadata.CorridorHeightToSpreadRatio);
      OnPropertyChanged(TradingMacroMetadata.CorridorHeight0ToSpreadRatio);
    }
    private static double CalcSpreadForCorridor(Rate[] rates) {
      var spreads = rates.Select(r => r.Spread).ToArray();
      var spreadLow = spreads.AverageByIterations(3, true);
      var spreadHight = spreads.AverageByIterations(3, false);
      var sa = spreads.Except(spreadLow.Concat(spreadHight)).Average();
      var sstdev = spreads.StDev();
      return sa + sstdev;
    }

    double _SpreadShort;
    public double SpreadShort {
      get { return _SpreadShort; }
    }
    public double SpreadShortInPips { get { return InPips(SpreadShort); } }

    double _SpreadLong;
    public double SpreadLong {
      get { return _SpreadLong; }
    }
    public double SpreadLongInPips { get { return InPips(SpreadLong); } }

    double SpreadMin { get { return Math.Min(SpreadLong, SpreadShort); } }
    double SpreadMax { get { return Math.Max(SpreadLong, SpreadShort); } }
    double SpreadForCorridor { get; set; }
    public double SpreadForCorridorInPips { get { return InPips(SpreadForCorridor); } }


    public double SpreadShortToLongRatio { get { return SpreadShort / SpreadLong; } }

    public bool IsSpreadShortToLongRatioOk { get { return SpreadShortToLongRatio > SpreadShortToLongTreshold; } }

    #endregion

    public double TradingDistanceInPips {
      get { return InPips(TradingDistance); }
    }
    public double TradingDistance {
      get {
        if (Strategy == Strategies.Vilner) {
          var srs = SuppRes.OrderBy(sr => sr.Rate).ToArray();
          return srs.Last().Rate - srs.First().Rate;
        }
        if (RatesArraySafe.Count() == 0) return double.NaN;
        return Strategy == Strategies.SuppRes ? 10
          : Math.Max(RatesHeight, CorridorHeightByRegression * 0);
      }
    }

    public Playback Playback;
    public void SetPlayBackInfo(bool play, DateTime startDate, TimeSpan delay) {
      Playback.Play = play;
      Playback.StartDate = startDate;
      Playback.Delay = delay;
    }
    public bool IsInPlayback { get { return Playback.Play; } }

    enum workers { LoadRates, ScanCorridor, RunPrice };
    Schedulers.BackgroundWorkerDispenser<workers> bgWorkers = new Schedulers.BackgroundWorkerDispenser<workers>();

    void AddCurrentTick(Price priceOuter) {
      new Action<Price>(price => {
        if (!HasRates || price.IsPlayback) return;
        lock (_Rates) {
          var isTick = RatesInternal.First() is Tick;
          if (BarPeriod == 0) {
            RatesInternal.Add(isTick ? new Tick(price, 0, false) : new Rate(price, false));
          } else {
            if (price.Time > RatesInternal.Last().StartDate.AddMinutes((int)BarPeriod)) {
              RatesInternal.Add(isTick ? new Tick(price, 0, false) : new Rate(RatesInternal.Last().StartDate.AddMinutes((int)BarPeriod), price.Ask, price.Bid, false));
            } else RatesInternal.Last().AddTick(price.Time, price.Ask, price.Bid);
          }
        }
      }).BeginInvoke(priceOuter, ia => { }, null);
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
        var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
        if (fw == null) return false;
        var canDo = IsHotStrategy && HasCorridor && IsPriceSpreadOk;
        return canDo;
      }
    }
    private bool CanDoNetOrders {
      get {
        var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
        if (fw == null) return false;
        var canDo = IsHotStrategy && (HasCorridor || IsAutoStrategy);
        return canDo;
      }
    }

    private int EntryOrderAllowedLot(bool isBuy) {
      return AllowedLotSizeCore(Trades) + Trades.IsBuy(!isBuy).Lots().ToInt();
    }


    static TradingMacro() {
    }

    #region Subjects
    static TimeSpan THROTTLE_INTERVAL = TimeSpan.FromSeconds(1);

    #region LoadRates Subject
    static object _LoadRatesSubjectLocker = new object();
    static ISubject<TradingMacro> _LoadRatesSubject;
    static ISubject<TradingMacro> LoadRatesSubject {
      get {
        lock (_LoadRatesSubjectLocker)
          if (_LoadRatesSubject == null) {
            _LoadRatesSubject = new Subject<TradingMacro>();
            _LoadRatesSubject
              .Buffer(THROTTLE_INTERVAL)
              .SubscribeOn(Scheduler.NewThread)
              //.ObserveOn(Scheduler.NewThread)
              .Subscribe(tml => {
                tml.GroupBy(tm => tm.Pair).ToList().ForEach(tm => {
                  tm.Last().LoadRates();
                });
              });
          }
        return _LoadRatesSubject;
      }
    }

    public void OnLoadRates() {
      var f = new Action<TradingMacro>(LoadRatesSubject.OnNext);
      Observable.FromAsyncPattern<TradingMacro>(f.BeginInvoke,f.EndInvoke)(this);
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
              //.Do(tm => Debug.WriteLine(tm.Pair + "[I]:SettingStopLimits @" + DateTime.Now.ToString("mm:ss.fff")))

              .Buffer(THROTTLE_INTERVAL)
              //.GroupByUntil(g => g.Pair, g => Observable.Timer(THROTTLE_INTERVAL))
              //.SelectMany(o=>o.TakeLast(1))

              //.Do(tm=>Debug.WriteLine(tm.Pair+"[O]:SettingStopLimits @"+DateTime.Now.ToString("mm:ss.fff")))
              //.SubscribeOn(Scheduler.TaskPool)
              .Subscribe(tml => {
                tml.GroupBy(tm => tm.Pair).ToList().ForEach(tm => {
                  tm.Last().SetNetStopLimit();
                });
              }, exc => Debugger.Break(), () => Debugger.Break())
              ;
          }
        return _SettingStopLimits;
      }
    }
    protected void OnSettingStopLimits() {
      var f = new Action<TradingMacro>(SettingStopLimits.OnNext);
      Observable.FromAsyncPattern<TradingMacro>(f.BeginInvoke, f.EndInvoke)(this);
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
                  lock (_pendingEntryOrders) {
                    var po = _pendingEntryOrders["EO"];
                    if (po == null || DateTime.Now - ((DateTime)po) > TimeSpan.FromMinutes(1)) {
                      GetFXWraper().CreateEntryOrder(s.Pair, s.IsBuy, s.Amount, s.Rate, 0, 0);
                      _pendingEntryOrders.Insert("EO", DateTime.Now, null, Cache.NoAbsoluteExpiration, TimeSpan.FromMinutes(1), CacheItemPriority.Default, null);
                    } else {
                      
                      Debug.WriteLine("Entry Order is pending:" + _pendingEntryOrders["EO"]);
                    }
                  }
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
    #endregion

    void SetEntryOrdersBySuppResLevels() {
      if (TradesManager == null || GetFXWraper(false) == null) return;
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
      Order2GoAddIn.FXCoreWrapper fw = GetFXWraper();
      if (fw != null)
        try {
          SetNetStopLimit(fw, true);
          SetNetStopLimit(fw, false);
        } catch (Exception exc) { Log = exc; }
    }

    private static void FailTradesManager() {
      Debug.Fail("TradesManager is null", (new NullReferenceException()) + "");
    }

    void CalcTakeProfitDistance() {
      if (Trades.Length == 0) return;
      var netOrder = GetFXWraper().GetNetLimitOrder(Trades[0]);
      if (netOrder == null) return;
      var netOpen = Trades.NetOpen();
      TakeProfitDistance = (!netOrder.IsBuy ? netOrder.Rate - netOpen : netOpen - netOrder.Rate);
    }
    void SetNetStopLimit(Order2GoAddIn.FXCoreWrapper fw, bool isBuy) {
      if (fw == null /*|| !IsHot*/) return;
      var ps = fw.GetPipSize(Pair) / 2;
      var trades = Trades.IsBuy(isBuy);
      if (trades.Length == 0) return;
      var spreadToAdd = PriceSpreadToAdd(isBuy);
      var lastTradeDate = trades.OrderByDescending(t => t.Time).First().Time;
      var ratesForStDevLimit = RatesArraySafe.Reverse().TakeWhile(r=>r.StartDate >=lastTradeDate).ToArray();
      foreach (var trade in trades) {
        var currentLimit = trade.Limit;
        if (currentLimit == 0) {
          var netLimitOrder = fw.GetNetLimitOrder(trade);
          if (netLimitOrder != null) currentLimit = netLimitOrder.Rate;
        }
        var rateLast = RatesArraySafe.LastOrDefault(r => r.PriceAvg1 > 0);
        if (rateLast == null) return;
        var netOpen = trades.NetOpen();
        if (CloseOnOpen && currentLimit > 0) {
          fw.FixOrderSetLimit(trade.Id, 0, "");
        } else {
          var cp = (rateLast.PriceAvg2 - rateLast.PriceAvg3) * .5;
          var limitByCorridor = ReverseStrategy ? 0 : isBuy ? rateLast.PriceAvg3 + cp : rateLast.PriceAvg2 - cp;
          var stDev = CorridorStats.HeightUpDown0.Max(RatesStDev);
          var limitByStDev = isBuy ? ratesForStDevLimit.Min(r => r.PriceAvg) + stDev : ratesForStDevLimit.Max(r => r.PriceAvg) - stDev;
          var tp = RoundPrice((trade.IsBuy ? 1 : -1) *
            (CalculateCloseProfit()
            + (ReverseStrategy ? fw.InPoints(Pair, fw.MoneyAndLotToPips(-CurrentLoss, AllowedLotSize(Trades, isBuy), Pair)) : 0)));
          if (double.IsNaN(tp)) return;

          var limitByTakeProfit = netOpen + spreadToAdd + tp;
          var limitRate = RoundPrice(ReverseStrategy ? limitByTakeProfit
            : limitByStDev//isBuy ? limitByCorridor.Min(limitByTakeProfit/*, limitByStDev*/) : limitByCorridor.Max(limitByTakeProfit/*, limitByStDev*/)
          );
          if (limitRate > 0) {
            if (isBuy && limitRate <= CurrentPrice.Bid || !isBuy && limitRate >= CurrentPrice.Ask)
              fw.ClosePair(Pair);
            if ((RoundPrice(currentLimit) - limitRate).Abs() > ps) {
              fw.FixOrderSetLimit(trade.Id, limitRate, "");
            }
          }
        }

        var stopByCorridor = rateLast == null || !ReverseStrategy ? 0 : !isBuy ? rateLast.PriceAvg02 : rateLast.PriceAvg03;
        var sl = RoundPrice((trade.IsBuy ? 1 : -1) * CalculateCloseLoss());
        var stopRate = RoundPrice(ReverseStrategy ? stopByCorridor : netOpen + spreadToAdd + sl);
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

    private double PriceSpreadToAdd(bool isBuy) {
      return (isBuy ? 1 : -1) * PriceSpreadAverage.GetValueOrDefault(0);
    }

    internal void EntryOrdersAdjust() {
      try {
        Order2GoAddIn.FXCoreWrapper fw = GetFXWraper();
        if (fw == null || !CanDoEntryOrders) return;
        //var eoDelete = (from eo in GetEntryOrders(fw)
        //               join sr in EnsureActiveSuppReses() on eo.OrderID equals sr.EntryOrderId
        //               into srGroup
        //               from srItem in srGroup.DefaultIfEmpty()
        //               where srItem == null
        //               select eo).ToList();
        //eoDelete.ForEach(eo => fw.DeleteOrder(eo));
        foreach (var suppres in EnsureActiveSuppReses()) {
          var isBuy = suppres.IsBuy;
          var allowedLot = EntryOrderAllowedLot(isBuy);
          var rate = RoundPrice(suppres.Rate);// + (ReverseStrategy ? 0 : PriceSpreadToAdd(isBuy)));
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
      } catch (Exception exc) {
        Log = exc;
      }
    }


    #region GetEntryOrders
    private Order[] GetEntryOrders() {
      Order2GoAddIn.FXCoreWrapper fw = GetFXWraper();
      if (fw == null) throw new NullReferenceException("FXWraper");
      return fw.GetEntryOrders(Pair);
    }
    private Order[] GetEntryOrders(bool isBuy) {
      return GetEntryOrders().IsBuy(isBuy);
    }
    private Order GetEntryOrder(string orderId) {
      return GetEntryOrders().OrderById(orderId);
    }
    #endregion

    Schedulers.TaskTimer _runPriceChangedTasker = new Schedulers.TaskTimer(100);
    Schedulers.TaskTimer _runPriceTasker = new Schedulers.TaskTimer(100);
    public void RunPriceChanged(PriceChangedEventArgs e, Action<TradingMacro> doAfterScanCorridor) {
      try {
        if (TradesManager == null) return;
        CurrentPrice = e.Price;
        if (!TradesManager.IsInTest)
          AddCurrentTick(e.Price);
        if (HasRates) {
          _RateDirection = RatesArraySafe.Skip(RatesArraySafe.Count() - 2).ToArray();
        }
        TicksPerMinuteSet(e.Price, TradesManager.ServerTime);
        //_runPriceChangedTasker.Action = () =>
          RunPriceChangedTask(e, doAfterScanCorridor);
      } catch (Exception exc) {
        Log = exc;
      }
    }

    private SuppRes[] EnsureActiveSuppReses() {
      return EnsureActiveSuppReses(true).Concat(EnsureActiveSuppReses(false)).OrderBy(sr => sr.Rate).ToArray();
    }
    private SuppRes[] EnsureActiveSuppReses(bool isBuy) {
      var hasTrades = HasTradesByDistance(Trades.IsBuy(isBuy));
      var isActiveCommon = !IsCold && IsHotStrategy && (ReverseStrategy || HasCorridor);
      var rateLast = RatesArraySafe.LastOrDefault();
      var isActiveByBuy = true;//rateLast == null ? true : !isBuy ? rateLast.PriceAvg < rateLast.PriceAvg1 : rateLast.PriceAvg > rateLast.PriceAvg1;
      SuppRes.IsBuy(isBuy).ToList().ForEach(sr => sr.IsActive = !hasTrades && isActiveByBuy && isActiveCommon);
      return SuppRes.Active(isBuy);
    }

    private bool HasTradesByDistance(bool isBuy) {
      return HasTradesByDistance(Trades.IsBuy(isBuy));
    }
    private bool HasTradesByDistance(Trade[] trades) {
      return trades.Count(t => t.PL > -RatesHeightInPips.Max(InPips(CorridorStats.HeightUpDown))) > 0;
    }
    static double? _runPriceMillisecondsAverage;
    public void RunPriceChangedTask(PriceChangedEventArgs e, Action<TradingMacro> doAfterScanCorridor) {
      try {
        if (TradesManager == null) return;
        Stopwatch sw = Stopwatch.StartNew();
        var timeSpanDict = new Dictionary<string, long>();
        Price price = e.Price;
        if (!TradesManager.IsInTest 
          && (RatesArraySafe.Count() == 0 || LastRatePullTime.AddMinutes(1.0.Max((double)BarPeriod / 2)) <= TradesManager.ServerTime)) {
          LastRatePullTime = TradesManager.ServerTime;
          OnLoadRates();
          timeSpanDict.Add("LoadRates", sw.ElapsedMilliseconds);
          _runPriceMillisecondsAverage = Lib.CMA(_runPriceMillisecondsAverage, BarsCount, sw.ElapsedMilliseconds);
        }
        if (RatesArraySafe.Count() < BarsCount) return;
        var lastCmaIndex = RatesArraySafe.ToList().FindLastIndex(r => r.PriceCMA != null) - 1;
        var lastCma = lastCmaIndex < 1 ? new double?[3] : RatesArraySafe[lastCmaIndex].PriceCMA.Select(c => new double?(c)).ToArray();
        RatesArraySafe.Skip(Math.Max(0, lastCmaIndex)).ToArray().SetCMA(PriceCmaPeriod, lastCma[0], lastCma[1], lastCma[2]);
        _runPriceTasker.Action = () =>
          RunPrice(e.Price, e.Account, Trades);
        if (doAfterScanCorridor != null) doAfterScanCorridor.BeginInvoke(this, ar => { }, null);
        timeSpanDict.Add("Other", sw.ElapsedMilliseconds);
        if (sw.Elapsed > TimeSpan.FromSeconds(LoadRatesSecondsWarning)) {
          var s = string.Join(Environment.NewLine, timeSpanDict.Select(kv => " " + kv.Key + ":" + kv.Value));
          Log = new Exception(string.Format("{0}[{2}]:{1:n}ms{3}{4}",
            MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, Pair, Environment.NewLine, s));
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    #region ScanCorridorSubject
    static object _ScanCorridorSubjectLocker = new object();
    static ISubject<TradingMacro> _ScanCorridorSubject;
    static ISubject<TradingMacro> ScanCorridorSubject {
      get {
        lock (_ScanCorridorSubjectLocker)
          if (_ScanCorridorSubject == null) {
            _ScanCorridorSubject = new Subject<TradingMacro>();
            _ScanCorridorSubject
              .Buffer(THROTTLE_INTERVAL)
              .Subscribe(tml => {
                tml.GroupBy(tm => tm.Pair).ToList().ForEach(tm => {
                  tm.Last().ScanCorridor();
                });
              }, exc => Debugger.Break(), () => Debugger.Break());
          }
        return _ScanCorridorSubject;
      }
    }
    void OnScanCorridor() {
      var f = new Action<TradingMacro>(ScanCorridorSubject.OnNext);
      Observable.FromAsyncPattern<TradingMacro>(f.BeginInvoke, f.EndInvoke)(this);
    }
    #endregion

    public void ScanCorridor(Action action = null) {
      try {
        if (RatesArraySafe.Count() == 0 /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        var showChart = CorridorStats == null || CorridorStats.Periods == 0;
        #region Prepare Corridor
        var ratesForSpread = BarPeriod == 0 ? RatesArraySafe.GetMinuteTicks(1).OrderBars().ToArray() : RatesArraySafe;
        var spreadShort = ratesForSpread.Skip(ratesForSpread.Count() - 10).ToArray().AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
        var spreadLong = ratesForSpread.AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
        VolumeShort = ratesForSpread.Skip(ratesForSpread.Count() - 10).ToArray().AverageByIterations(r => r.Volume, 2).Average(r => r.Volume);
        VolumeLong = ratesForSpread.AverageByIterations(r => r.Volume, 2).Average(r => r.Volume);
        SetShortLongSpreads(spreadShort, spreadLong);
        var spread = TradesManager.InPips(Pair, Math.Max(spreadLong, spreadShort));
        var priceBars = FetchPriceBars(PowerRowOffset, ReversePower).OrderByDescending(pb => pb.StartDate).ToArray();
        var powerBars = priceBars.Select(pb => pb.Power).ToArray();
        double powerAverage;
        var priceBarsForCorridor = priceBars.AverageByIterations(pb => pb.Power, IterationsForPower, out powerAverage);
        PowerAverage = powerAverage;
        priceBarsForCorridor = priceBarsForCorridor.Where(pb => pb.Power > powerAverage).OrderBars().ToArray();
        var priceBarsIntervals = priceBarsForCorridor.Select((r, i) => new Tuple<int, PriceBar>(i, r)).ToArray().GetIntervals(2);
        var powerBar = !TradeByFirstWave.HasValue ? priceBarsForCorridor.OrderBy(pb => pb.Power).LastOrDefault()
          : (TradeByFirstWave.Value ? priceBarsIntervals.First() : priceBarsIntervals.Last()).OrderByDescending(pb => pb.Power).First();//.OrderBy(pb => pb.Power).Last();
        if (powerBar == null) return;
        var startDate = CorridorStartDate.GetValueOrDefault(//powerBar.StartDate);
          new[] { powerBar.StartDate, CorridorStats == null ? DateTime.MinValue : CorridorStats.StartDate }.Max());
        var ratesForCorridor = RatesForTrades();
        var periodsStart = CorridorStartDate == null
          ? CorridorCrossesCountMinimum == 0 ? BarsCount : BarsCount / 10
          : ratesForCorridor.Count(r => r.StartDate >= CorridorStartDate.Value);
        if (periodsStart == 1) return;
        var periodsLength = CorridorStartDate.HasValue ? 1 : ratesForCorridor.Count();// periodsStart;

        CorridorStatistics crossedCorridor = null;
        var heightByPriceSpread = this.PriceSpreadAverage.GetValueOrDefault(double.NaN) * CorridorHeightMultiplier * 4;
        CorridorHeightMinimum = heightByPriceSpread.Max(SpreadForCorridor * CorridorHeightMultiplier);
        Predicate<CorridorStatistics> exitCondition = cs => {
          return false;

          var isCorridorOk = IsCorridorOk(cs);
          if (crossedCorridor == null && isCorridorOk) crossedCorridor = cs;
          return crossedCorridor != null;
        };

        var corridornesses = ratesForCorridor.GetCorridornesses(r => r.BidHigh, r => r.AskLow, periodsStart, periodsLength,((int)BarPeriod).FromMinutes(),PointSize, cs => {
          cs.CorridorCrossesCount = CorridorCrossesCount(cs);
          return exitCondition(cs);
        })
          //.Where(c => tradesManager.InPips(tm.Pair, c.Value.HeightUpDown) > 0)
          .Select(c => c.Value).ToArray();
        //var corridorBig = ratesForCorridor.ScanCorridorWithAngle(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, IterationsForCorridorHeights, false);
        //if (corridorBig != null)
        //  BigCorridorHeight = corridorBig.HeightUpDown;
        #endregion
        #region Update Corridor
        if (corridornesses.Count() > 0) {
          var rateLast = ratesForCorridor.Last();
          var cc = corridornesses
            .Where(cs=>IsCorridorOk(cs,double.NaN,double.NaN,CorridorHeightMultiplier,0))
            .Select(cs => {
              var levelUp = cs.Coeffs[0] + cs.HeightUp;
              var levelDown = cs.Coeffs[0] - cs.HeightDown;
              var distanceUp = (rateLast.PriceHigh - levelUp).Abs();
              var distancedown = (rateLast.PriceLow - levelDown).Abs();
              var distance = distanceUp.Min(distancedown);
              return new { cs, distance, LegsAngleStDevR = cs.LegsAngleAverage };
            })
          .OrderByDescending(cs => cs.cs.CorridorCrossesCount)
          .ThenByDescending(cs => cs.cs.HeightUpDown)
          //.ThenBy(cs => double.IsNaN(cs.LegsAngleStDevR) ? double.MaxValue : cs.LegsAngleStDevR)
          //.ThenBy(cs => cs.distance)
          .Select(cs => cs.cs)
          .ToArray();

          var cc0 = cc;//.Where(cs => cs.LegsAngleStDevR <= .25).ToArray();
          var cc1 = cc0.Where(IsCorridorOk).ToArray();

          crossedCorridor = cc1.Where(cs => IsCorridorCountOk(cs)).FirstOrDefault();
          var csCurr = crossedCorridor ?? cc1.FirstOrDefault() ?? cc0.FirstOrDefault() ?? cc.FirstOrDefault() ?? corridornesses.Last();
          var csOld = CorridorStats;
          csOld.Init(csCurr,PointSize);
          csOld.IsCurrent = crossedCorridor != null;
          CorridorStats = csOld;
          TakeProfitPips = InPips(CalculateTakeProfit());
          CalculateLevels();
        } else {
          throw new Exception("No corridors found for current range.");
        }
        #endregion
        PopupText = "";
        if (showChart) RaiseShowChart();
      } catch (Exception exc) {
        Log = exc;
        //PopupText = exc.Message;
      } finally {
        if (action != null)
          action();
      }
    }

    private bool IsCorridorOk(CorridorStatistics cs) {
      return IsCorridorOk(cs, CorridorCrossesCountMinimum, TradingAngleRange, CorridorHeightMultiplier,StDevToCorridorHeight0);
    }
    private bool IsCorridorOk(CorridorStatistics cs
      , double corridorCrossesCountMinimum, double tradingAngleRange, double corridorHeightMultiplier, double stDevToCorridorHeight0) {
        //if (cs.LegInfos.Count == 0 && corridorCrossesCountMinimum > 0) return false;
        if (cs.HeightUpDown0 < CurrentPrice.Spread * 2) return false;
        var crossesCount = CorridorCrossesCount(cs);
        var isCorCountOk = IsCorridorCountOk(crossesCount, corridorCrossesCountMinimum);
        var isCorridorAngleOk = !(corridorCrossesCountMinimum > 0)
        || (corridorCrossesCountMinimum == 0 && crossesCount == 0)
        || double.IsNaN(tradingAngleRange)
        || cs.LegInfos.Count == 0 
        || cs.LegInfos.Average(a=>a.Angle.Abs()) >= tradingAngleRange;// corridorAngle.Abs() <= tradingAngleRange;
      cs.Spread = cs.Rates.Average(r=>r.Spread);
      var corridorHeightToRatesHeight = CalculateCorridorHeightToRatesHeight(cs);
      var isCorridorOk = isCorCountOk 
        && isCorridorAngleOk
        && corridorHeightToRatesHeight >= corridorHeightMultiplier
        && cs.HeightUpDown0ToSpreadRatio >= stDevToCorridorHeight0;
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
    private int CorridorCrossesCount(CorridorStatistics corridornes) {
      return CorridorCrossesCount(corridornes, corridornes.priceHigh, corridornes.priceLow);
    }
    private int CorridorCrossesCount(CorridorStatistics corridornes, Func<Rate, double> getPriceHigh, Func<Rate, double> getPriceLow) {
      Rate[] rates = corridornes.Rates;
      double[] coeffs = corridornes.Coeffs;
      var interval = TimeSpan.FromMinutes((int)BarPeriod);


      var rateByIndex = rates.Select((r, i) => new { index = i, rate = r }).ToArray();
      var crossUps = rateByIndex.Where(rbi => getPriceHigh(rbi.rate) >= corridornes.priceLine[rbi.index] + corridornes.HeightUp)
        .Select(rbi => new { rate = rbi.rate, isUp = true }).AsParallel().ToArray();
      var crossDowns = rateByIndex.Where(rbi => getPriceLow(rbi.rate) <= corridornes.priceLine[rbi.index] - corridornes.HeightDown)
        .Select(rbi => new { rate = rbi.rate, isUp = false }).AsParallel().ToArray();
      if (crossDowns.Length > 0 || crossUps.Length > 0) {
        var crosses = crossUps.Concat(crossDowns).OrderByDescending(r => r.rate.StartDate).ToArray();
        var crossesList = "".Select(s => new { rate = (Rate)null, isUp = false }).Take(0).ToList();
        crossesList.Add(crosses[0]);
        crosses.Aggregate((rp, rn) => {
          if (rp.isUp != rn.isUp) {
            crossesList.Add(rn);
            corridornes.LegInfos.Add(new CorridorStatistics.LegInfo(rp.rate,rn.rate,interval));
          }
          return rn;
        });
        return crossesList.Count;
      }
      return 0;
    }

    #endregion

    private Rate[] RatesForTrades() {
      return GetRatesSafe().Where(LastRateFilter).ToArray();
    }

    double _takeProfitPrevious = 0;
    private double CalculateTakeProfitInDollars() { return CalculateTakeProfit() * LotSize / 10000; }
    private double CalculateTakeProfit() {
      var tp = 0.0;
      switch (TakeProfitFunction) {
        case TradingMacroTakeProfitFunction.Corridor: tp = CorridorHeightByRegression; break;
        case TradingMacroTakeProfitFunction.Corridor0: tp = CorridorHeightByRegression0; break;
        case TradingMacroTakeProfitFunction.RatesHeight: tp = RatesHeight; break;
        case TradingMacroTakeProfitFunction.RatesStDev: tp = RatesStDev; break;
        case TradingMacroTakeProfitFunction.Corr0_RStd:
          tp = Math.Max(CorridorHeightByRegression0, RatesStDev); break;
      }
      tp = new double[] { CurrentPrice.Spread + InPoints(CommissionByTrade(new Trade() { Lots = LotSize })) * 3, tp }.Max();
      var raiseTakeProfitChanged = InPips(tp).ToInt() != InPips(_takeProfitPrevious).ToInt();
      _takeProfitPrevious = tp;
      //if( raiseTakeProfitChanged)        OnTakeProfitChanged();
      return tp;
      switch (Strategy) {
        case Strategies.Vilner:
          return TradingDistance;
        default: return CorridorHeightByRegression;
      }
    }

    class TakeProfitChangedDispatcher : BlockingConsumerBase<TradingMacro> {
      public TakeProfitChangedDispatcher() : base(tm => tm.OnTakeProfitChangedCore()) { }
    }
    static TakeProfitChangedDispatcher TakeProfitChangedQueue = new TakeProfitChangedDispatcher();

    public void OnTakeProfitChanged() {
      TakeProfitChangedQueue.Add(this);
    }
    public void OnTakeProfitChangedCore() {
      SetLotSize(TradesManager.GetAccount());
      SetEntryOrdersBySuppResLevels();
    }

    void ScanTrendLine() {
      var ratesForGann = GannAngleRates.ToArray();
    }
    public Func<Trade, double> CommissionByTrade = trade => 0.7;

    private bool CanTrade() {
      return RatesArraySafe.Count() > 0;
    }

    private void RunPrice(Price price, Account account, Trade[] trades) {
      var sw = Stopwatch.StartNew();
      try {
        if (!CanTrade()) return;
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


    public void SetLotSize(Account account=null) {
      if (TradesManager == null) return;
      if (account == null) account = TradesManager.GetAccount();
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

    int GetLotSizeByTrades(ICollection<Trade> trades) {
      return TradesManagerStatic.GetLotSize(LotSize * GetLotSizeByRatio(trades.Count + 1, 1.7), TradesManager.MinimumQuantity);
    }
    double GetLotSizeByRatio(int tradeNumber, double ratio) {
      var lotSize = 1.0;
      while (--tradeNumber > 0) {
        lotSize *= ratio;
      }
      return lotSize;
    }

    public int AllowedLotSizeCore(ICollection<Trade> trades) {
      if (RatesArraySafe.Count() == 0) return 0;
      var calcLot = CalculateLot(trades);
      if (DoAdjustTimeframeByAllowedLot && calcLot > MaxLotSize && Strategy.HasFlag(Strategies.Hot)) {
        while (CalculateLot(Trades) > MaxLotSize) {
          var nextLimitBar = Enum.GetValues(typeof(BarsPeriodType)).Cast<int>().Where(bp => bp > (int)BarPeriod).Min();
          BarPeriod = (BarsPeriodType)nextLimitBar;
          RatesInternal.Clear();
          LoadRates();
        }
      }
      return Math.Min(MaxLotSize, calcLot);
    }
    public int AllowedLotSize(ICollection<Trade> trades, bool isBuy) {
      return AllowedLotSizeCore(trades.IsBuy(isBuy));
      if (Strategy == Strategies.Massa || Strategy == Strategies.Range) {
        var tc = GetTradesCountFromSuppRes(isBuy);
        if (tc > MaximumPositions) {
          SuppResResetAllTradeCounts();
          tc = 0;
        }
        return TradesManagerStatic.GetLotSize(LotSize * Math.Max(1, tc), TradesManager.MinimumQuantity);
      }
    }

    private double GetTradesCountFromSuppRes(bool isBuy) {
      var tc = isBuy ? ResistanceCurrent().TradesCount : SupportCurrent().TradesCount;
      return tc;
    }

    private int CalculateLot(ICollection<Trade> trades) {
      Func<int, int> returnLot = d => Math.Max(LotSize, d);
      if (FreezeStopType == Freezing.Freez)
        return returnLot(trades.Sum(t => t.Lots) * 2);
      return returnLot(CalculateLotCore(CurrentGross - InPips(CalculateTakeProfit() * LotSize / 10000)));
    }
    private int CalculateLotCore(double totalGross) {
      return TradesManager.MoneyAndPipsToLot(Math.Min(0, totalGross).Abs(), TakeProfitPips, Pair);
    }

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
      var ratesForDensity = (reversePower ? rs.OrderBarsDescending() : rs.OrderBars()).ToArray();
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

    void CalculateLevels() {
      if (RatesArraySafe.Count() > 0) {
        backgroundWorkers.Run("CenterOfMass", TradesManager.IsInTest, () => {
          Thread.CurrentThread.Priority = ThreadPriority.Lowest;
          var rates = RatesArraySafe;
          if (_doCenterOfMass) {
            CentersOfMass = rates.ToArray().Overlaps(IterationsForCenterOfMass);
            CenterOfMass = CentersOfMass.CenterOfMass() ?? CenterOfMass;
          }
          CalculateSuppResLevels();
        }, e => Log = e);
      }
    }
    int _limitBarToRateProvider {
      get {
        return (int)BarPeriod;// Enum.GetValues(typeof(BarsPeriodTypeFXCM)).Cast<int>().Where(i => i <= (int)BarPeriod).Max();
      }
    }
    public void LoadRates(bool dontStreachRates = false) {
      try {
        if (TradesManager != null && !TradesManager.IsInTest && isLoggedIn) {
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
              var ri = RatesInternal.ToArray();
              RatesInternal.Clear();
              RatesInternal.AddRange(ri);
            }

            bool wereRatesPulled = false;
            using (var ps = new PriceService.PriceServiceClient()) {
              try {
                //var ratesPulled = ps.FillPrice(Pair, RatesInternal.Select(r => r.StartDate).DefaultIfEmpty().Max());
                //RatesInternal.AddRange(ratesPulled);
                //wereRatesPulled = true;
              } catch (Exception exc) {
                Log = exc;
              } finally {
                if (!wereRatesPulled)
                  RatesLoader.LoadRates(TradesManager, Pair, _limitBarToRateProvider, periodsBack, startDate, TradesManagerStatic.FX_DATE_NOW, RatesInternal);
              }
            }
            OnPropertyChanged(Metadata.TradingMacroMetadata.RatesInternal);
            RatesArraySafe.SetCMA(PriceCmaPeriod);
            if (sw.Elapsed > TimeSpan.FromSeconds(LoadRatesSecondsWarning))
              Debug.WriteLine("LoadRates[" + Pair + ":{1}] - {0:n1} sec", sw.Elapsed.TotalSeconds, (BarsPeriodType)BarPeriod);
            LastRatePullTime = TradesManager.ServerTime;
          }
          {
            var path = AppDomain.CurrentDomain.BaseDirectory + "\\CSV";
            Directory.CreateDirectory(path);
            File.WriteAllText(path + "\\" + Pair.Replace("/", "") + ".csv", RatesArraySafe.Csv());
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
      //_propertyChangedTaskDispencer.RunOrEnqueue(property, () => {
      switch (property) {
        case TradingMacroMetadata.CurrentLoss:
          SetLotSize();
          break;
        case TradingMacroMetadata.TradingAngleRange:
        case TradingMacroMetadata.IterationsForCenterOfMass:
        case TradingMacroMetadata.CorridorHeightMultiplier:
        case TradingMacroMetadata.CorridorCrossesCountMinimum:
        case TradingMacroMetadata.StDevToSpreadRatio:
        case TradingMacroMetadata.RatesStDev:
          OnScanCorridor();
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
          OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
          break;
        case TradingMacroMetadata.IsSuppResManual:
        case TradingMacroMetadata.TakeProfitFunction:
        case TradingMacroMetadata.Strategy:
          OnScanCorridor();
          goto case TradingMacroMetadata.RangeRatioForTradeLimit;
        case TradingMacroMetadata.RangeRatioForTradeLimit:
        case TradingMacroMetadata.RangeRatioForTradeStop:
        case TradingMacroMetadata.IsColdOnTrades:
        case TradingMacroMetadata.IsPriceSpreadOk:
          SetEntryOrdersBySuppResLevels();
          break;
      }
      //}, exc => Log = exc);
    }

    double _ratesSumm;
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
    private void SetRatesStDev(Rate[] rates) {
      rates = GetRatesForStDev(rates).ToArray();
      var rs = rates.Sum(r => r.PriceAvg);
      if (rs != _ratesSumm) {
        _ratesSumm = rs;
        RatesStDev = rates.StDev(r => r.PriceAvg);
        PriceSpreadAverage = rates.Select(r => (r.AskHigh - r.BidHigh + r.AskLow - r.BidLow) / 2)
          .ToArray().AverageByIterations(2).Average();
      }
    }
    partial void OnFibMaxChanged() {
      StDevLevels = FibMax.Split(',').Select(s => double.Parse(s)).ToArray();
    }
    partial void OnCurrentLossChanging(double value) {
      return;
      if (Strategy == Strategies.Massa && CurrentLoss < 0 && value < 0) {
        var power = StDevLevels[0];
        var ratio = Math.Min(power, Math.Pow(-value, 1 / power) / Math.Pow(-CurrentLoss, 1 / power));
        (LastTrade.Buy ? Resistances : Supports).ToList().ForEach(r => r.TradesCount = Math.Max(Store.SuppRes.TradesCountMinimum, r.TradesCount * ratio));
      }
    }
    partial void OnCurrentLossChanged() {
      if (Strategy == Strategies.Massa) {
        if (CurrentLoss >= 0) SuppResResetAllTradeCounts();
        OnPropertyChanged("CurrentNet");
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
    public bool IsStDevOk { get { return StDevToCorridorHeight0Real <= StDevToCorridorHeight0; } }


    public double RatesStDevToRatesHeightRatio { get { return RatesStDev / RatesHeight; } }
    public double HeightToStDevRatio { get { return RatesHeight / RatesStDev; } }

    public double TrendNessRatio {
      get {
        if (RatesArraySafe.Length == 0) return double.NaN;
        var pipSize = TradesManager.GetPipSize(Pair);
        var count = 0.0;
        for (var d = -CorridorStats.HeightDown0; d <= CorridorStats.HeightUp0; d += pipSize) {
          count = count.Max(RatesArraySafe.Count(r => (r.PriceAvg1 + d).Between(r.PriceLow, r.PriceHigh)));
        }
        return count / RatesArraySafe.Length;
      }
    }

    private double _CorridorHeightMinimum;
    public double CorridorHeightMinimum {
      get { return _CorridorHeightMinimum; }
      set {
        if (_CorridorHeightMinimum != value) {
          _CorridorHeightMinimum = value;
          OnPropertyChanged(TradingMacroMetadata.CorridorHeightMinimum);
        }
      }
    }


    public double RatesHeight { get; set; }
    public double RatesHeightInPips { get { return InPips(RatesHeight); } }
  }
}
