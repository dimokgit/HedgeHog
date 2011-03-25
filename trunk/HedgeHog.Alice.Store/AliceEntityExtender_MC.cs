using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
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
using System.Windows.Threading;
using System.Windows.Input;
using Gala = GalaSoft.MvvmLight.Command;
using HedgeHog.DB;
using System.Data.Objects.DataClasses;
using System.Runtime.CompilerServices;

namespace HedgeHog.Alice.Store {
  public partial class AliceEntities {
    //~AliceEntities() {
    //  if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return;
    //  var newName = Path.Combine(
    //    Path.GetDirectoryName(Connection.DataSource),
    //    Path.GetFileNameWithoutExtension(Connection.DataSource)
    //    ) + ".backup" + Path.GetExtension(Connection.DataSource);
    //  if (File.Exists(newName)) File.Delete(newName);
    //  File.Copy(Connection.DataSource, newName);
    //}
  }
  public partial class AliceEntities {
    private object _saveChangesLock = new object();
    public override int SaveChanges(System.Data.Objects.SaveOptions options) {
      lock (_saveChangesLock) {
        try {
          InitGuidField<TradingAccount>(ta => ta.Id, (ta, g) => ta.Id = g);
          InitGuidField<TradingMacro>(ta => ta.UID, (ta, g) => ta.UID = g);
        } catch { }
        return base.SaveChanges(options);
      }
    }

    private void InitGuidField<TEntity>(Func<TEntity, Guid> getField, Action<TEntity, Guid> setField) {
      var d = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Added)
        .Select(o => o.Entity).OfType<TEntity>().Where(e => getField(e) == new Guid()).ToList();
      d.ForEach(e => setField(e, Guid.NewGuid()));
    }
  }

  public partial class TradeHistory {
    public double NetPL { get { return GrossPL - Commission; } }
  }

  public partial class OrderTemplate {
  }

  public partial class TradingMacro {

    public TradingMacro() {
      SuppRes.AssociationChanged += new CollectionChangeEventHandler(SuppRes_AssociationChanged);
    }
    ~TradingMacro() {
      var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
      if (fw != null && fw.IsLoggedIn)
        fw.GetEntryOrders(Pair).ToList().ForEach(o => fw.DeleteOrder(o));
      else if (Strategy == Strategies.Hot)
        MessageBox.Show("Account is already logged off. Unable to close Entry Orders.");
    }

    void SuppRes_AssociationChanged(object sender, CollectionChangeEventArgs e) {
      switch (e.Action) {
        case CollectionChangeAction.Add:
          ((Store.SuppRes)e.Element).RateChanged += SuppRes_RateChanged;
          ((Store.SuppRes)e.Element).IsActiveChanged += SuppRes_IsActiveChanged;
          break;
        case CollectionChangeAction.Refresh:
          ((EntityCollection<SuppRes>)sender).ToList()
            .ForEach(sr => {
              sr.RateChanged += SuppRes_RateChanged;
              sr.IsActiveChanged += SuppRes_IsActiveChanged;
            });
          break;
        case CollectionChangeAction.Remove:
          ((Store.SuppRes)e.Element).RateChanged -= SuppRes_RateChanged;
          break;
      }
      SetEntryOrdersBySuppResLevels();
    }

    void SuppRes_IsActiveChanged(object sender, EventArgs e) {
      return;
      try {
        var suppRes = (SuppRes)sender;
        var fw = GetFXWraper();
        if (fw != null && !suppRes.IsActive && !string.IsNullOrWhiteSpace(suppRes.EntryOrderId))
          fw.DeleteOrder(suppRes.EntryOrderId);
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void SuppRes_RateChanged(object sender, EventArgs e) {
      //SetEntryOrdersBySuppResLevels();
    }
    static Guid _sessionId = Guid.NewGuid();
    public static Guid SessionId { get { return _sessionId; } }
    public void ResetSessionId() {
      _sessionId = Guid.NewGuid();
    }

    public string CompositeId { get { return Pair + "_" + PairIndex; } }

    public string CompositeName { get { return Pair + ":" + LimitBar; } }
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

    private CorridorStatistics _CorridorStats;
    public CorridorStatistics CorridorStats {
      get { return _CorridorStats; }
      set {
        var datePrev = _CorridorStats == null ? DateTime.MinValue : _CorridorStats.StartDate;
        _CorridorStats = value;
        CorridorStatsArray.ToList().ForEach(cs => cs.IsCurrent = cs == value);

        RatesArraySafe.ToList().ForEach(r => r.PriceAvg1 = r.PriceAvg2 = r.PriceAvg3 = 0);
        if (value != null) {
          var corridorRates = RatesArraySafe.Skip(RatesArraySafe.Count() - CorridorStats.Periods).ToArray();
          var tangent = corridorRates
            .SetCorridorPrices(CorridorStats.HeightUp0, CorridorStats.HeightDown0, CorridorStats.HeightUp, CorridorStats.HeightDown,
            r => r.PriceAvg, r => r.PriceAvg1, (r, d) => r.PriceAvg1 = d
            , (r, d) => r.PriceAvg02 = d, (r, d) => r.PriceAvg03 = d
            , (r, d) => r.PriceAvg2 = d, (r, d) => r.PriceAvg3 = d
            )[1];

          CorridorAngle = tangent;
          if (!IsGannAnglesManual)
            SetGannAngleOffset(value);
          UpdateTradingGannAngleIndex();
        }

        #region PropertyChanged
        OnPropertyChanged("CorridorHeightByRegressionInPips");
        OnPropertyChanged("CorridorHeightByRegressionInPips0");
        OnPropertyChanged("CorridorToRangeRatio");
        OnPropertyChanged("CorridorsRatio");

        OnPropertyChanged("CorridorStats");
        OnPropertyChanged("OpenSignal");
        #endregion
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
      if (false && CorridorStats == null) return new Rate[0];
      RatesArraySafe.ToList().ForEach(r => Enumerable.Range(0, GannAnglesArray.Length).ToList()
        .ForEach(i => { if (r.GannPrices.Length > i) r.GannPrices[i] = 0; }));
      var ratesForGann = RatesArraySafe.SkipWhile(r => r.StartDate < this.GannAnglesAnchorDate.GetValueOrDefault(CorridorStats.StartDate)).ToArray();
      var rateStart = this.GannAnglesAnchorDate.GetValueOrDefault(new Func<DateTime>(() => {
        var rateStop = Slope > 0 ? ratesForGann.OrderBy(r => r.PriceAvg).LastOrDefault() : ratesForGann.OrderBy(r => r.PriceAvg).FirstOrDefault();
        if (rateStop == null) return DateTime.MinValue;
        var ratesForStart = ratesForGann.Where(r => r < rateStop);
        if (ratesForStart.Count() == 0) ratesForStart = ratesForGann;
        return (CorridorStats.Slope > 0 ? ratesForStart.OrderBy(r => r.BidLow).First() : ratesForStart.OrderBy(r => r.AskHigh).Last()).StartDate;
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


    DateTime _lastRatePullTime;
    public DateTime LastRatePullTime {
      get { return _lastRatePullTime; }
      set {
        if (_lastRatePullTime == value) return;
        _lastRatePullTime = value;
        OnPropertyChanged(TradingMacroMetadata.LastRatePullTime);
      }
    }

    public double AngleInRadians { get { return Math.Atan(Angle) * (180 / Math.PI); } }
    double _angle;
    public double Angle {
      get { return _angle; }
      set {
        if (_angle == value) return;
        _angle = value;
        OnPropertyChanged("Angle"); OnPropertyChanged("AngleInRadians");
      }
    }

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
        OnPropertyChanged("Overlap5");
        OnPropertyChanged("OverlapTotal");
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
      //if (PointSize == 0) PointSize = pointSize;
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
      OnPropertyChanged(TradingMacroMetadata.IsSpeedOk);
      OnPropertyChanged(TradingMacroMetadata.CurrentGross);
      OnPropertyChanged(TradingMacroMetadata.CurrentGrossInPips);
      OnPropertyChanged(TradingMacroMetadata.OpenTradesGross);
      OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
    }
    #endregion

    public double PipsPerMinute { get { return InPips(PriceQueue.Speed(.25)); } }
    public double PipsPerMinuteCmaFirst { get { return InPips(PriceQueue.Speed(.5)); } }
    public double PipsPerMinuteCmaLast { get { return InPips(PriceQueue.Speed(1)); } }

    public bool IsSpeedOk { get { return PipsPerMinute < Math.Max(PipsPerMinuteCmaFirst, PipsPerMinuteCmaLast); } }

    bool _PendingSell;
    public bool PendingSell {
      get { return _PendingSell; }
      set {
        if (_PendingSell == value) return;
        _PendingSell = value;
        OnPropertyChanged("PendingSell");
      }
    }

    bool _PendingBuy;
    public bool PendingBuy {
      get { return _PendingBuy; }
      set {
        if (_PendingBuy == value) return;
        _PendingBuy = value;
        OnPropertyChanged("PendingBuy");
      }
    }


    Price _currentPrice;
    public Price CurrentPrice {
      get { return _currentPrice; }
      set { _currentPrice = value; OnPropertyChanged("CurrentPrice"); }
    }

    double _balanceOnStop;
    public double BalanceOnStop {
      get { return _balanceOnStop; }
      set {
        if (_balanceOnStop == value) return;
        _balanceOnStop = value;
        OnPropertyChanged("BalanceOnStop");
      }
    }

    double _balanceOnLimit;
    public double BalanceOnLimit {
      get { return _balanceOnLimit; }
      set {
        if (_balanceOnLimit == value) return;
        _balanceOnLimit = value;
        OnPropertyChanged("BalanceOnLimit");
      }
    }

    public double OpenTradesGross {
      get { return Trades.Sum(t => t.GrossPL) - (TradesManager==null?0: TradesManager.CommissionByTrades(Trades)); }
    }

    public double ExitOnNetAmount { get { return Math.Min(RangeRatioForTradeLimit, 0).Abs(); } }
    public bool DoExitOnCurrentNet { get { return RangeRatioForTradeLimit < 0; } }
    public double CurrentGross {
      get { return CurrentLoss + OpenTradesGross + Math.Min(RangeRatioForTradeLimit, 0); }
    }

    public double CurrentGrossInPips {
      get { return TradesManager == null ? double.NaN: TradesManager.MoneyAndLotToPips( CurrentGross, Trades.Length == 0?AllowedLotSizeCore(Trades): Trades.NetLots().Abs(), Pair); }
    }

    double? _StopAmount;
    public double? StopAmount {
      get { return _StopAmount; }
      set {
        if (_StopAmount == value) return;
        _StopAmount = value;
        OnPropertyChanged("StopAmount");
      }
    }
    double? _LimitAmount;
    public double? LimitAmount {
      get { return _LimitAmount; }
      set {
        if (_LimitAmount == value) return;
        _LimitAmount = value;
        OnPropertyChanged("LimitAmount");
      }
    }

    double? _netInPips;
    public double? NetInPips {
      get { return _netInPips; }
      set {
        if (_netInPips == value) return;
        _netInPips = value;
        OnPropertyChanged("NetInPips");
      }
    }

    private double _SlackInPips;
    public double SlackInPips {
      get { return _SlackInPips; }
      set {
        if (_SlackInPips != value) {
          _SlackInPips = value;
          OnPropertyChanged("SlackInPips");
        }
      }
    }

    private double _CurrentLossPercent;
    public double CurrentLossPercent {
      get { return _CurrentLossPercent; }
      set {
        if (_CurrentLossPercent != value) {
          _CurrentLossPercent = value;
          OnPropertyChanged("CurrentLossPercent");
        }
      }
    }


    public Freezing FreezeType {
      get { return (Freezing)this.FreezLimit; }
      set {
        if (this.FreezLimit != (int)value) {
          this.FreezLimit = (int)value;
          OnPropertyChanged("FreezeType");
        }
      }
    }

    public Freezing FreezeStopType {
      get { return (Freezing)this.FreezeStop; }
      set {
        if (this.FreezeStop != (int)value) {
          this.FreezeStop = (int)value;
          OnPropertyChanged("FreezeStopType");
        }
      }
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

    public int PositionsBuy {
      get { return Trades.IsBuy(true).Length; }
    }

    public int PositionsSell {
      get { return Trades.IsBuy(false).Length; }
    }

    public double PipsPerPosition {
      get { return Trades.Length < 2 ? 0 : InPips(Trades.Max(t => t.Open) - Trades.Min(t => t.Open)) / (Trades.Length - 1); }
    }


    private double _TradeDistanceInPips;
    double TradeDistanceInPips {
      get { return _TradeDistanceInPips; }
      set {
        if (_TradeDistanceInPips != value) {
          _TradeDistanceInPips = value;
          OnPropertyChanged("TradeDistanceInPips");
        }
      }
    }

    private int _CalculatedLotSize;
    public int CalculatedLotSize {
      get { return _CalculatedLotSize; }
      set {
        if (_CalculatedLotSize != value) {
          _CalculatedLotSize = value;
          OnPropertyChanged("CalculatedLotSize");
        }
      }
    }

    private double _BarHeightHigh;
    public double BarHeightHigh {
      get { return _BarHeightHigh; }
      set {
        if (_BarHeightHigh != value) {
          _BarHeightHigh = value;
          OnPropertyChanged("BarHeightHigh");
        }
      }
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
            Func<Rate,double> corridor = rate=> GannPriceForTrade(rate);// GetCurrentCorridor();
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
            return GetSignal(CrossOverSignal(GetPriceHigh(rateLast), GetPriceHigh(ratePrev), GetPriceLow(rateLast), GetPriceLow(ratePrev),
                                   rateLast.PriceAvg2, ratePrev.PriceAvg2) ??
                             CrossOverSignal(GetPriceHigh(rateLast), GetPriceHigh(ratePrev), GetPriceLow(rateLast), GetPriceLow(ratePrev),
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
      return r.StartDate <= CurrentPrice.Time - TimeSpan.FromMinutes(LimitBar);
    }
    static Func<Rate, double> gannPriceHigh = rate => rate.PriceAvg;
    static Func<Rate, double> gannPriceLow = rate => rate.PriceAvg;

    static Func<Rate, double> suppResPriceHigh = rate => rate.PriceHigh;
    static Func<Rate, double> suppResPriceLow = rate => rate.PriceLow;

    public double CorridorHeightToSpreadRatio { get { return CorridorStats.HeightUpDown / SpreadLong; } }
    public bool? CloseSignal {
      get {
        if (CorridorStats == null || CloseOnOpen) return null;
        switch (Strategy) {
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


    #region TradesManager 'n Stuff
    Func<ITradesManager> _TradesManager = () => null;
    ITradesManager TradesManager { get { return _TradesManager(); } }
    public void SubscribeToTradeClosedEVent(Func<ITradesManager> getTradesManager) {
      this._TradesManager = getTradesManager;
      this.TradesManager.TradeClosed -= TradesManager_TradeClosed;
      this.TradesManager.TradeClosed += TradesManager_TradeClosed;
      this.TradesManager.TradeAdded -= TradesManager_TradeAddedGlobal;
      this.TradesManager.TradeAdded += TradesManager_TradeAddedGlobal;
      RaisePositionsChanged();
    }

    void TradesManager_TradeAddedGlobal(object sender, TradeEventArgs e) {
      if (!IsMyTrade(e.Trade)) return;
      SuppRes.IsBuy(e.Trade.IsBuy).ToList().ForEach(sr => sr.IsActive = false);
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
        var tm = sender as ITradesManager;
        tm.TradeAdded -= TradesManager_TradeAdded;
        Trade trade = e.Trade;
        if (Strategy == Strategies.SuppRes) {
          var suppResSpreadMultiplier = 0;
          var offsetBySpread = SpreadShort * suppResSpreadMultiplier;
          //if (trade.Buy && trade.Open < ResistancePrice) SupportPrice = trade.Open - offsetBySpread;
          //if (!trade.Buy && trade.Open > SupportPrice) ResistancePrice = trade.Open + offsetBySpread;
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }
    bool IsMyTrade(Trade trade) { return trade.Pair == Pair; }
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
      var hasSameDirectionTrades = TradesManager.GetTradesInternal(Pair).IsBuy(e.Trade.IsBuy).Length > 0;
      SuppRes.IsBuy(e.Trade.IsBuy).ToList().ForEach(sr => sr.IsActive = !hasSameDirectionTrades);
      SetEntryOrdersBySuppResLevels();
      RaisePositionsChanged();
      if (Strategy == Strategies.None) return;
      var trade = e.Trade;
      ResetLock();
      if (trade.Buy) BuyWhenReady = false;
      else SellWhenReady = false;

      switch (Strategy) {
        case Strategies.SuppRes:
          if (trade.PL > 0) {
            if (trade.Buy) IsSellLock = true;
            else IsBuyLock = true;
          }
          break;
        case Strategies.Range:
        case Strategies.Massa:
          break;
      }
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
      if (!trade.Buy && ts.Resistanse == 0)
        ts.Resistanse = CorridorRates.OrderBars().Max(r => r.AskHigh);
      if (trade.Buy && ts.Support == 0)
        ts.Support = CorridorRates.OrderBars().Min(r => r.BidLow);
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
          _CorridorAngle = value.Angle() / PointSize;
          OnPropertyChanged("CorridorAngle");
        }
      }
    }



    private static Func<Rate, double> _GetBidLow = r => r.BidLow;
    private static Func<Rate, double> _GetPriceLow = r => r.PriceLow;
    public Func<Rate, double> GetPriceLow { get { return IsHot? _GetBidLow: _GetPriceLow; } }
    private static Func<Rate, double> _GetAskHigh = r => r.AskHigh;
    private static Func<Rate, double> _GetPriceHigh = r => r.PriceHigh;
    public Func<Rate, double> GetPriceHigh { get { return IsHot?_GetAskHigh: _GetPriceHigh; } }

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
          AddSupport(RatesArraySafe.Min(GetPriceLow));
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
          AddResistance(RatesArraySafe.Max(GetPriceHigh));
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
    public SuppRes AddResistance(double rate) {return AddSuppRes(rate, false); }
    public SuppRes AddBuySellRate(double rate, bool isBuy) { return AddSuppRes(rate, !isBuy); }
    public SuppRes AddSuppRes(double rate, bool isSupport) {
      try {
        var srs = (isSupport ? Supports : Resistances);
        var index = srs.Select(a => a.Index).DefaultIfEmpty(0).Max() + 1;
        var sr = new SuppRes { Rate = rate, IsSupport = isSupport, TradingMacroID = UID, UID = Guid.NewGuid(), TradingMacro = this, Index = index, TradesCount = srs.Max(a => a.TradesCount) };
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
          return IndexSuppReses(SuppRes.Where(sr => !sr.IsSupport).OrderBy(a=>a.Rate).ToArray());
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

    private double _RatesStDev = double.MaxValue;
    public double RatesStDev {
      get { return _RatesStDev; }
      set {
        if (_RatesStDev != value) {
          _RatesStDev = value;
          CorridorStDevRatio.Add(value, BarsCount / 10);
          OnPropertyChanged(TradingMacroMetadata.RatesStDev);
          OnPropertyChanged(Metadata.TradingMacroMetadata.IsCorridorStDevRatioOk);
          OnPropertyChanged(Metadata.TradingMacroMetadata.CorridorStDevRatioPrevInPips);
          OnPropertyChanged(Metadata.TradingMacroMetadata.CorridorStDevRatioLastInPips);
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

    private Lib.CmaWalker _corridorStDevRatio = new Lib.CmaWalker(2);
    public double CorridorStDevRatioPrevInPips { get { return InPips(CorridorStDevRatio.Prev); } }
    public double CorridorStDevRatioLastInPips { get { return InPips(CorridorStDevRatio.Last); } }
    public Lib.CmaWalker CorridorStDevRatio { get { return _corridorStDevRatio; } }
    public bool IsCorridorStDevRatioOk { get { return CorridorStDevRatio.Difference > 0; } }
    public double SuppResMinimumDistance { get { return CurrentPrice.Spread * 2; } }

    double[] StDevLevels = new double[0];
    bool _doCenterOfMass = true;
    bool _areSuppResesActive { get { return true; } }
    void CalculateSuppResLevels() {

      if (IsSuppResManual || Strategy == Strategies.None) return;

      var iterations = IterationsForSuppResLevels;
      var levelsCount = SuppResLevelsCount;

      if (levelsCount>=0) {
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

      if (levelsCount == 1 && Strategy == Strategies.Hot) {
        if (resistance == null) resistance = addResistanceNode();
        if (support == null) support = addSupportNode();
        var rateLast = RatesArraySafe.LastOrDefault(r => r.PriceAvg1 > 0);
        if (rateLast != null) {
          support.Value.Rate = ReverseStrategy ? rateLast.PriceAvg3 : rateLast.PriceAvg2;
          resistance.Value.Rate = ReverseStrategy ? rateLast.PriceAvg2 : rateLast.PriceAvg3;
          return;
        }


        var ratesForHot = CorridorRates.ToArray();
        var average = MagnetPrice = ratesForHot.Average(r => r.PriceAvg);
        var stDev = ratesForHot.StDev(r => r.PriceAvg) * 2;
        resistance.Value.Rate = average - stDev;
        //if (!Trades.HaveSell())
        support.Value.Rate = average + stDev;
        return;
      }
      if (Strategy == Strategies.Hot) {
        var ratesForHot = CorridorRates.ToArray();
        if (ratesForHot.Count() > 0) {
          var average = ratesForHot.Average(r => r.PriceAvg);
          var askHigh = ratesForHot.Max(r => r.AskHigh);
          var bidLow = ratesForHot.Min(r => r.BidLow);
          var rateLastHot = RatesArraySafe.LastOrDefault();
          if (rateLastHot != null && rateLastHot.PriceAvg1 > 0) {
            var canChangeUp = true;// Slope > 0 && rateLastHot.AskHigh > rateLastHot.PriceAvg2;
            var canChangeDown = true;// Slope < 0 && rateLastHot.BidLow < rateLastHot.PriceAvg3;

            if (resistance == null) resistance = addResistanceNode();
            if (support == null) support = addSupportNode();
            if (canChangeUp || resistance.Value.Rate == support.Value.Rate) {
              //if (!Trades.HaveBuy())
              resistance.Value.Rate = askHigh + SuppResMinimumDistance;
              //if (!Trades.HaveSell())
              support.Value.Rate = askHigh - SuppResMinimumDistance;
            }
            resistance = resistance.Next;
            support = support.Next;

            if (resistance == null) resistance = addResistanceNode();
            if (support == null) support = addSupportNode();
            if (canChangeDown || resistance.Value.Rate == support.Value.Rate) {
              //if (!Trades.HaveBuy())
              resistance.Value.Rate = bidLow + SuppResMinimumDistance;
              //if (!Trades.HaveSell())
              support.Value.Rate = bidLow - SuppResMinimumDistance;
            }
          }
        }
        return;
      }

      var rateAverage = rates.Average(r => r.PriceAvg); //CenterOfMassBuy;
      #region com
      Func<ICollection<Rate>, bool, Rate> com = (ratesForCom, up) => {
        switch (LevelType_) {
          case Store.LevelType.Magnet:
            var rs = ratesForCom.FindRatesByPrice(ratesForCom.Average(r => r.PriceAvg));
            return up ? rs.OrderBy(r => r.PriceAvg).Last() : rs.OrderBy(r => r.PriceAvg).First();
            return ratesForCom.CalculateMagnetLevel(up);
          case Store.LevelType.CenterOfMass:
            return ratesForCom.CenterOfMass(up);
          default: throw new InvalidEnumArgumentException(LevelType_ + " level type is not supported.");
        }
      };
      #endregion


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
          _doCenterOfMass = false;

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
          _doCenterOfMass = true;
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
    BackgroundWorkerDispenser<string> backgroundWorkers = new BackgroundWorkerDispenser<string>();
    List<Rate> _Rates = new List<Rate>();
    public Rate[] RatesArray { get { return Rates.ToArray(); } }
    public Rate[] RatesArraySafe {
      get {
        if (Rates.Count < BarsCount) {
          //Log = new RatesAreNotReadyException();
          return new Rate[0];
        }
        return RatesArray;
      }
    }
    public List<Rate> Rates {
      get { return _Rates; }
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
        if( _pointSize == double.NaN )
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
    public Trade[] Trades {
      get {
        if (TradesManager == null) {
          Log = new Exception("Warning: TradesManager is null");
          return new Trade[0];
        }
        return TradesManager.GetTrades(Pair);/* _trades.ToArray();*/ 
      }
      //set {
      //  _trades.Clear();
      //  _trades.AddRange(value);
      //  if (value.Length > 0) ResetLock();
      //}
    }

    public double CorridorToRangeMinimumRatio { get { return 0; } }

    public static Strategies[] StrategiesToClose = new Strategies[] { Strategies.Brange };
    private Strategies _Strategy;
    [Category(categoryCorridor)]
    public Strategies Strategy {
      get {
        //if (Trades.Length > 0) return _Strategy;
        if ((_Strategy & Strategies.Auto) == Strategies.None) return _Strategy;
        OnPropertyChanged(TradingMacroMetadata.Strategy);
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

    double _RangeCorridorHeight;
    public double Correlation_P;
    public double Correlation_R;

    public double Correlation {
      get {
        return (Correlation_P + Correlation_R) / 2;
        return new double[] { Correlation_P, Correlation_R }.OrderBy(c => c.Abs()).First();
      }
    }

    public double RangeCorridorHeight {
      get { return _RangeCorridorHeight; }
      set {
        _RangeCorridorHeight = value;
        OnPropertyChanged("RangeCorridorHeight");
      }
    }


    //public double BigCorridorHeight { get; set; }

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

    public void SetShortLomgSpreads(double spreadShort, double spreadLong) {
      _SpreadShort = spreadShort;
      _SpreadLong = spreadLong;
      SpreadShortLongRatioAverage = SpreadShortToLongRatio;
      OnPropertyChanged("SpreadShort");
      OnPropertyChanged("SpreadShortInPips");
      OnPropertyChanged("SpreadLong");
      OnPropertyChanged("SpreadLongInPips");
      OnPropertyChanged("SpreadShortToLongRatio");
      OnPropertyChanged("IsSpreadShortToLongRatioOk");
      OnPropertyChanged(Metadata.TradingMacroMetadata.IsSpreadShortLongRatioAverageOk);
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

    public double SpreadShortToLongRatio { get { return SpreadShort / SpreadLong; } }

    public bool IsSpreadShortToLongRatioOk { get { return SpreadShortToLongRatio > SpreadShortToLongTreshold; } }


    public double TradingDistanceInPips {
      get { return InPips(TradingDistance); }
    }
    private double _TradingDistance;
    public double TradingDistance {
      get {
        if (Strategy == Strategies.Vilner) {
          var srs = SuppRes.OrderBy(sr => sr.Rate).ToArray();
          return srs.Last().Rate - srs.First().Rate;
        }
        if (RatesArraySafe.Count() == 0) return double.NaN;
        return Strategy == Strategies.SuppRes ? 10
          : Math.Max(_TradingDistance, Math.Max(RatesArraySafe.Height(),CorridorHeightByRegression*2));
      }
      set {
        if (_TradingDistance != value) {
          _TradingDistance = value;
        }
      }
    }


    public bool IsCharterMinimized { get; set; }

    private bool _ShowProperties;
    public bool ShowProperties {
      get { return _ShowProperties; }
      set {
        if (_ShowProperties != value) {
          _ShowProperties = value;
          OnPropertyChanged("ShowProperties");
        }
      }
    }

    public Playback Playback;
    public void SetPlayBackInfo(bool play, DateTime startDate, TimeSpan delay) {
      Playback.Play = play;
      Playback.StartDate = startDate;
      Playback.Delay = delay;
    }
    public bool IsInPlayback { get { return Playback.Play; } }

    ThreadScheduler ScanCorridorScheduler = new ThreadScheduler();

    enum workers { LoadRates, ScanCorridor, RunPrice };
    BackgroundWorkerDispenser<workers> bgWorkers = new BackgroundWorkerDispenser<workers>();

    void AddCurrentTick(Price price) {
      if (!HasRates || price.IsPlayback) return;
      var isTick = Rates.First() is Tick;
      if (LimitBar == 0) {
        Rates.Add(isTick? new Tick(price, 0, false) : new Rate(price, false));
      } else {
        if (price.Time > Rates.Last().StartDate.AddMinutes(LimitBar)) {
          Rates.Add(isTick ? new Tick(price, 0, false) : new Rate(Rates.Last().StartDate.AddMinutes(LimitBar), price.Ask, price.Bid, false));
        } else Rates.Last().AddTick(price.Time, price.Ask, price.Bid);
      }
      SetRatesStDev();
    }


    private static bool IsSuppResBuy(SuppRes suppres) {
      return !suppres.IsSupport;
    }

    double RoundPrice(double price) {
      return TradesManager.Round(Pair, price);
    }

    private bool CanDoEntryOrders {
      get {
        var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
        if( fw == null )return false;
        var canDo = Strategy == Strategies.Hot;
        if (!canDo)
          fw.GetEntryOrders(Pair).ToList().ForEach(o => fw.DeleteOrder(o.OrderID));
        return canDo;
      }
    }

    private int EntryOrderAllowedLot(bool isBuy) {
      return AllowedLotSizeCore(Trades) + Trades.IsBuy(!isBuy).Lots().ToInt();
    }

    Tasker entryOrdersTasker = new Tasker("EntryOrders");
    void SetEntryOrdersBySuppResLevels(bool runSync = false) {
      try {
        if (!CanDoEntryOrders) return;
        var fw = GetFXWraper();
        if (fw == null || Strategy != Strategies.Hot) return;
        if (!runSync) {
          entryOrdersTasker.RunOrEnqueue(() => SetEntryOrdersBySuppResLevels(true), exc => Log = exc);
        } else {
          var entryOrders = fw.GetEntryOrders(Pair);
          entryOrders = EntryOrdersAddRemove(fw, entryOrders, Supports.Active(), false);
          entryOrders = EntryOrdersAddRemove(fw, entryOrders, Resistances.Active(), true);
          EntryOrdersAdjust(entryOrders);
          SetStopLimits();
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    private Order2GoAddIn.FXCoreWrapper GetFXWraper() { return TradesManager as Order2GoAddIn.FXCoreWrapper; }

    void SetStopLimits() {
      SetStopLimit(true);
      SetStopLimit(false);
    }
    double _limitLast;
    double _stopLast;
    void SetStopLimit(bool isBuy) {
      var fw = GetFXWraper();
      if(fw == null || !CanDoEntryOrders)return;
      Trades.IsBuy(isBuy).ToList().ForEach(trade => {
        var tp = (trade.IsBuy ? 1 : -1) * CalculateCloseProfit();
        if (RoundPrice(tp) != RoundPrice(_limitLast)) {
          fw.FixOrderSetLimit(trade.Id, tp, "", false);
          _limitLast = tp;
        }
        var sl = (trade.IsBuy ? 1 : -1) * CalculateCloseLoss();
        if (RoundPrice(sl) != RoundPrice(_stopLast)) {
          fw.FixOrderSetStop(trade.Id, sl, "", false);
          _stopLast = sl;
        }
      });
    }

    void EntryOrdersAdjust(Order[] entryOrders) {
      EntryOrdersAdjust(Supports.Active().FirstOrDefault(), entryOrders);
      EntryOrdersAdjust(Resistances.Active().FirstOrDefault(), entryOrders);
    }
    void EntryOrdersAdjust(SuppRes suppres, Order[] entryOrders) {
      if (!CanDoEntryOrders || suppres == null) return;
      try {
        var fw = TradesManager as Order2GoAddIn.FXCoreWrapper;
        if (fw == null) return;
        var suppReses = GetActiveSuppReses(suppres.IsSupport ? Supports : Resistances);
        var isBuy = IsSuppResBuy(suppres);
        entryOrders = entryOrders.IsBuy(isBuy);
        if (entryOrders.Length == 0) return;
        for (var i = 0; i < suppReses.Length; i++) {
          if (RoundPrice(entryOrders[i].Rate) != RoundPrice(suppReses[i].Rate))
            fw.ChangeOrderRate(entryOrders[i], suppReses[i].Rate);
          var allowedLot = EntryOrderAllowedLot(isBuy);
          if (entryOrders[i].AmountK != allowedLot / 1000)
            fw.ChangeOrderAmount(entryOrders[i], allowedLot);
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private Order[] EntryOrdersAddRemove(Order2GoAddIn.FXCoreWrapper fw, Order[] entryOrders, Store.SuppRes[] suppReses, bool isBuy) {
      foreach (var suppRes in suppReses.Active()) {
        var order = fw.GetOrders(Pair).SingleOrDefault(o => o.OrderID == suppRes.EntryOrderId);
        if (order == null) {
          var allowedLot = EntryOrderAllowedLot(isBuy);
          SetActiveSuppReses();
          if(suppRes.IsActive)
            suppRes.EntryOrderId = fw.CreateEntryOrder(Pair, isBuy, allowedLot, suppRes.Rate, 0, 0);
          if (string.IsNullOrWhiteSpace(suppRes.EntryOrderId))
            throw new Exception("Entry order was not created for Pair:" + Pair + ",Rate:" + suppRes.Rate);
          entryOrders = fw.GetOrders(Pair);
        }
      }
      foreach (var order in entryOrders)
        if (!SuppRes.Active().Any(sr => sr.EntryOrderId == order.OrderID))
          fw.DeleteOrder(order);
      return entryOrders;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private Order[] EntryOrdersAddRemove_Old(Order2GoAddIn.FXCoreWrapper fw, Order[] entryOrders, Store.SuppRes[] suppReses, bool isBuy) {
      if (!CanDoEntryOrders) return entryOrders;
      var deleteSkip = 0;
      var renewOrders = false;
      suppReses = suppReses.Where(sr => !SuppResPrices(!sr.IsSupport).Contains(sr.Rate)).ToArray();
      if (Trades.IsBuy(isBuy).Length == 0) {
        GetActiveSuppReses(suppReses)
          .Skip(entryOrders.IsBuy(isBuy).Length).ToList().ForEach(sr => {
            var allowedLot = EntryOrderAllowedLot(isBuy);
            //if (entryOrders.IsBuy(isBuy).Length == 0)
            var price = CurrentPrice;
            if (false && isBuy && (price.Ask-sr.Rate).Between(0,price.Spread)) {
              Log = new Exception("OpenTrade insted of CreateEntryOrder");
              fw.OpenTrade(Pair, isBuy, allowedLot, 0, 0, "", null);
              fw.GetTradesInternal(Pair);
              return;
            }
            if (false && !isBuy && (sr.Rate- price.Bid).Between(0,price.Spread)) {
              Log = new Exception("OpenTrade insted of CreateEntryOrder");
              fw.OpenTrade(Pair, isBuy, allowedLot, 0, 0, "", null);
              fw.GetTradesInternal(Pair);
              return;
            }
            fw.CreateEntryOrder(Pair, isBuy, allowedLot, sr.Rate, 0, 0);
            renewOrders = true;
            //else
            //  fw.ChangeOrderRate(entryOrders.IsBuy(isBuy).First(), sr.Rate);
            //entryOrders.IsBuy(isBuy).OrderBy(o => o.OrderID).Skip(1).ToList().ForEach(o => fw.DeleteOrder(o.OrderID));
          });
        deleteSkip = suppReses.Length;
      }
      entryOrders.IsBuy(isBuy).Skip(deleteSkip).ToList().ForEach(o =>{ fw.DeleteOrder(o.OrderID);renewOrders = true;});
      return renewOrders ? fw.GetEntryOrders(Pair, true) : entryOrders;
    }

    private SuppRes[] GetActiveSuppReses(Store.SuppRes[] suppReses) {
      if (suppReses.Length == 0) return new Store.SuppRes[0];
      var isBuy = IsSuppResBuy(suppReses[0]);
      var trades = Trades.IsBuy(isBuy).DefaultIfEmpty(new Trade());
      var tradeOpen = isBuy?trades.Min(t=>t.Open):trades.Max(t=>t.Open);
      return suppReses.Where(sr => !SuppResPrices(!sr.IsSupport).Contains(sr.Rate)).ToArray().Active();
        //.Where(sr=> tradeOpen==0 ||
        //            isBuy && sr.Rate< tradeOpen-SpreadMin ||
        //            !isBuy && sr.Rate> tradeOpen+SpreadMin
        //).ToArray();
    }

    TasksDispenser<TradingMacro> afterScanTaskDispenser = new TasksDispenser<TradingMacro>();
    Tasker _runPriceChangedTasker = new Tasker("RunPriceChange");
    Tasker _runPriceTasker = new Tasker("RunPrice");
    Tasker _scanCorridorTasker = new Tasker("ScanCorridor");
    public void RunPriceChanged(PriceChangedEventArgs e, Action<TradingMacro> doAfterScanCorridor) {
      CurrentPrice = e.Price;
      SetActiveSuppReses();
      if (!IsInPlayback)
        AddCurrentTick(e.Price);
      if (HasRates) {
        _RateDirection = RatesArraySafe.Skip(RatesArraySafe.Count() - 2).ToArray();
        SetLotSize(e.Account);
      }
      TicksPerMinuteSet(e.Price, TradesManager.ServerTime);
      _runPriceChangedTasker.RunOrEnqueue(() => RunPriceChangedTask(e, doAfterScanCorridor),null);
    }

    private void SetActiveSuppReses() {
      var hasBuys = Trades.IsBuy(true).Length > 0;
      SuppRes.IsBuy(true).ToList().ForEach(sr => sr.IsActive = !hasBuys);
      var hasSells = Trades.IsBuy(false).Length > 0;
      SuppRes.IsBuy(false).ToList().ForEach(sr => sr.IsActive = !hasSells);
    }
    public void RunPriceChangedTask(PriceChangedEventArgs e, Action<TradingMacro> doAfterScanCorridor) {
      try {
        if (TradesManager == null) return;
        Stopwatch sw = Stopwatch.StartNew();
        Price price = e.Price;
        if (RatesArraySafe.Count() == 0 || LastRatePullTime.AddSeconds(Math.Max(1, LimitBar) * 60 / 3) <= TradesManager.ServerTime)
          LoadRatesAsync();
        if (RatesArraySafe.Count() < BarsCount) return;
        var lastCmaIndex = RatesArraySafe.ToList().FindLastIndex(r => r.PriceCMA != null) - 1;
        var lastCma = lastCmaIndex < 1 ? new double?[3] : Rates[lastCmaIndex].PriceCMA.Select(c => new double?(c)).ToArray();
        RatesArraySafe.Skip(Math.Max(0, lastCmaIndex)).ToArray().SetCMA(PriceCmaPeriod, lastCma[0], lastCma[1], lastCma[2]);
        _runPriceTasker.RunOrEnqueue(() => RunPrice(e.Price, e.Account, Trades), exc => Log = exc);
        if (doAfterScanCorridor != null) doAfterScanCorridor(this);
        if (sw.Elapsed > TimeSpan.FromSeconds(1)) {
          Log = new Exception(string.Format("{0}:{1:n}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds));
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    Tasker _loadRatesTasker = new Tasker("LoadRates");
    public void LoadRatesAsync() {
      if (IsInPlayback) 
        LoadRates();
      else
        _loadRatesTasker.RunOrEnqueue(() => LoadRates(), exc => Log = exc);
    }

    public void ScanCorridor() {
      try {
        if (RatesArraySafe.Count() == 0 /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        RatesStDev = RatesArraySafe.StDev(r => r.PriceAvg);
        if (false && !IsTradingHours) return;
        #region Prepare Corridor
        var ratesForSpread = LimitBar == 0 ? RatesArraySafe.GetMinuteTicks(1).OrderBars().ToArray() : RatesArraySafe;
        var spreadShort = ratesForSpread.Skip(ratesForSpread.Count() - 10).ToArray().AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
        var spreadLong = ratesForSpread.AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
        SetShortLomgSpreads(spreadShort, spreadLong);
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
        var periodsStart = CorridorStartDate == null ? ratesForCorridor.Count() / 10 : ratesForCorridor.Count(r => r.StartDate >= CorridorStartDate.Value);
        if (periodsStart == 1) return;
        var periodsLength = ratesForCorridor.Count();// periodsStart;

        CorridorStatistics crossedCorridor = null;
        var heightUpDownLevel = SpreadShort > SpreadLong ? RatesStDev : SpreadMax;
        Predicate<CorridorStatistics> exitCondition = cs => {
          if (crossedCorridor==null && cs.HeightUpDown0 >= heightUpDownLevel && CorridorCrossesCount(cs,r=>r.PriceHigh,r=>r.PriceLow) > 2) crossedCorridor = cs;
          return crossedCorridor != null && cs.Slope.Abs() < crossedCorridor.Slope.Abs();
        };

        var corridornesses = ratesForCorridor.GetCorridornesses(r=>r.PriceHigh, r=>r.PriceLow, periodsStart, periodsLength, IterationsForCorridorHeights,exitCondition, false)
          //.Where(c => tradesManager.InPips(tm.Pair, c.Value.HeightUpDown) > 0)
          .Select(c => c.Value).ToArray();
        //var corridorBig = ratesForCorridor.ScanCorridorWithAngle(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, IterationsForCorridorHeights, false);
        //if (corridorBig != null)
        //  BigCorridorHeight = corridorBig.HeightUpDown;
        #endregion
        #region Update Corridor
        if (corridornesses.Count() > 0) {
          //corridornesses = corridornesses.Where(c => c.HeightDown + c.HeightUp >= RatesStDev).ToArray();
          //var theCorridor = corridornesses.Last();// GetCorridorByCrosses(corridornesses) ?? corridornesses[0];
          var csCurr = corridornesses.Reverse().Take(2).Last();// corridornesses.Where(c => Math.Sign(c.Slope) == Math.Sign(theCorridor.Slope)).OrderBy(c => c.Slope.Abs()).Last();
          var cs = GetCorridorStats(csCurr.Iterations);
          cs.Init(csCurr.Density, csCurr.Coeffs, csCurr.HeightUp0, csCurr.HeightDown0, csCurr.HeightUp, csCurr.HeightDown, csCurr.LineHigh, csCurr.LineLow, csCurr.Periods, csCurr.EndDate, csCurr.StartDate, csCurr.Iterations);
          RangeCorridorHeight = corridornesses.Last().HeightUpDown;
          CorridorStats = GetCorridorStats().Last();
          TakeProfitPips = InPips(CalculateTakeProfit());

        } else {
          throw new Exception("No corridors found for current range.");
        }
        #endregion
        PopupText = "";
      } catch (Exception exc) {
        PopupText = exc.Message;
      } finally {
        CorridorStats = GetCorridorStats().LastOrDefault();
      }
    }

    private int CorridorCrossesCount(CorridorStatistics corridornes,Func<Rate,double>getPriceHigh,Func<Rate,double>getPriceLow) {
      var rates = RatesArraySafe.Where(r => r.StartDate >= corridornes.StartDate).ToArray();
      var coeffs = Lib.Regress(rates.Select(r => r.PriceAvg).ToArray(), 1);
      var rateByIndex = rates.Select((r, i) => new { index = i, rate = r }).ToArray();
      var crossUps = rateByIndex.AsParallel().Where(rbi => getPriceHigh(rbi.rate) >= coeffs.RegressionValue(rbi.index) + corridornes.HeightUp)
        .Select(rbi => new { rate = rbi.rate, isUp = true }).ToArray();
      var crossDowns = rateByIndex.AsParallel().Where(rbi => getPriceLow(rbi.rate) <= coeffs.RegressionValue(rbi.index) - corridornes.HeightDown)
        .Select(rbi => new { rate = rbi.rate, isUp = false }).ToArray();
      if (crossDowns.Length > 0 && crossUps.Length > 0) {
        var crosses = crossUps.Concat(crossDowns).OrderByDescending(r => r.rate.StartDate).ToArray();
        var crossCount = 0;
        crosses.AsParallel().Aggregate((rp, rn) => {
          if (rp.isUp != rn.isUp) crossCount++;
          return rn;
        });
        return crossCount;
      }
      return 0;
    }

    private Rate[] RatesForTrades() {
      var ratesForCorridor = RatesArraySafe.Where(LastRateFilter).ToArray();
      return ratesForCorridor;
    }

    double _takeProfitPrevious = 0;
    private double CalculateTakeProfitInDollars() { return CalculateTakeProfit() * LotSize / 10000; }
    private double CalculateTakeProfit() {
      var tp = 0.0;
      switch (TakeProfitFunction) {
        case TradingMacroTakeProfitFunction.Corridor: tp = CorridorHeightByRegression; break;
        case TradingMacroTakeProfitFunction.Corridor0: tp = CorridorHeightByRegression0; break;
        case TradingMacroTakeProfitFunction.RatesHeight: tp = RatesArraySafe.Height(); break;
        case TradingMacroTakeProfitFunction.RatesStDev: tp = RatesStDev; break;
        case TradingMacroTakeProfitFunction.Corr_RStd:
          tp = Math.Max(CorridorHeightByRegression, RatesStDev); break;
      }
      tp = new double[] { CurrentPrice.Spread+ InPoints(CommissionByTrade(new Trade(){Lots=LotSize}))*3, tp,CorridorHeightByRegression0 }.Max();
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

    void OnTakeProfitChanged() {
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


    public void SetLotSize(Account account) {
      if (TradesManager == null) return;
      Trade[] trades = account.Trades;
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

    int _allowedLotSize;
    public int AllowedLotSizeCore(ICollection<Trade> trades) {
      if (RatesArraySafe.Count() == 0) return 0;
      var calcLot = CalculateLot(trades);
      if (DoAdjustTimeframeByAllowedLot && calcLot > MaxLotSize && Strategy == Strategies.Hot) {
        if (CalculateLot(Trades) > MaxLotSize) {
          var nextLimitBar = Enum.GetValues(typeof(BarsPeriodType)).Cast<int>().Where(bp => bp > LimitBar).Min();
          LimitBar = nextLimitBar;
        }
      }
      var als = Math.Min(MaxLotSize,calcLot);
      if (als != _allowedLotSize)
        OnPropertyChanged("AllowedLotSize");
      return _allowedLotSize = als;
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
      return returnLot(CalculateLotCore(CurrentGross-InPips(CalculateTakeProfit())));
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
      ratesForDensity.Index();
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
            CentersOfMass = rates.Reverse().Take((rates.Count() / PowerVolatilityMinimum).ToInt()).ToArray().Overlaps(IterationsForSuppResLevels);
            CenterOfMass = CentersOfMass.CenterOfMass() ?? CenterOfMass;
          }
          CalculateSuppResLevels();
        }, e => Log = e);
      }
    }
    public void LoadRates(bool dontStreachRates = false) {
      try {
        if (!IsInPlayback && isLoggedIn) {
          InfoTooltip = "Loading Rates";
          Debug.WriteLine("LoadRates[{0}:{2}] @ {1:HH:mm:ss}", Pair, TradesManager.ServerTime, (BarsPeriodType)LimitBar);
          var sw = Stopwatch.StartNew();
          var serverTime = TradesManager.ServerTime;
          var startDate = !DoStreatchRates || dontStreachRates || CorridorStats == null || CorridorStats.StartDate == DateTime.MinValue 
            ? TradesManagerStatic.FX_DATE_NOW
            : CorridorStartDate.GetValueOrDefault(CorridorStats == null ? TradesManagerStatic.FX_DATE_NOW : CorridorStats.StartDate.AddMinutes(-LimitBar * 5));
          RatesLoader.LoadRates(TradesManager, Pair, LimitBar, BarsCount, startDate, TradesManagerStatic.FX_DATE_NOW, Rates);
          Rates.SetCMA(PriceCmaPeriod);
          if (sw.Elapsed > TimeSpan.FromSeconds(1))
            Debug.WriteLine("LoadRates[" + Pair + ":{1}] - {0:n1} sec", sw.Elapsed.TotalSeconds, (BarsPeriodType)LimitBar);
          LastRatePullTime = TradesManager.ServerTime;
          _scanCorridorTasker.RunOrEnqueue(ScanCorridor, exc => Log = exc);
          OnPropertyChanged(Metadata.TradingMacroMetadata.Rates);
        }
      } catch (Exception exc) {
        Log = exc;
      } finally {
        InfoTooltip = "";
      }
    }

    #region Overrides

    TaskerDispenser<string> _propertyChangedTaskDispencer = new TaskerDispenser<string>();
    protected override void OnPropertyChanged(string property) {
      //_propertyChangedTaskDispencer.RunOrEnqueue(property, () => {
        switch (property) {
          case TradingMacroMetadata.RatesStDev:
            _scanCorridorTasker.RunOrEnqueue(() => {
              ScanCorridor();
              CalculateLevels();
            }, exc => Log = exc);
            break;
          case TradingMacroMetadata.Pair:
            _pointSize = double.NaN;
            goto case TradingMacroMetadata.CorridorBarMinutes;
          case TradingMacroMetadata.CorridorBarMinutes:
          case TradingMacroMetadata.LimitBar:
            CorridorStats = null;
            CorridorStartDate = null;
            if (!_exceptionStrategies.Contains(Strategy))
              Strategy = Strategies.None;
            Rates.Clear();
            goto case TradingMacroMetadata.Strategy;
          case TradingMacroMetadata.Rates:
            SetRatesStDev();
            OnPropertyChanged(TradingMacroMetadata.TradingDistanceInPips);
            break;
          case TradingMacroMetadata.IsSuppResManual:
          case TradingMacroMetadata.TakeProfitFunction:
          case TradingMacroMetadata.Strategy:
            CorridorStDevRatio.Clear();
            CalculateLevels();
            break;
          case TradingMacroMetadata.RangeRatioForTradeLimit:
          case TradingMacroMetadata.RangeRatioForTradeStop:
            SetEntryOrdersBySuppResLevels();
            break;
          case TradingMacroMetadata.AllowedLotSize:
            break;
        }
      //}, exc => Log = exc);
      base.OnPropertyChanged(property);
    }

    double _ratesSumm;
    private void SetRatesStDev() {
      var rs = RatesArraySafe.Sum(r => r.PriceAvg);
      if (rs != _ratesSumm) {
        _ratesSumm = rs;
        RatesStDev = RatesArraySafe.StDev(r => r.PriceAvg);
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
      if (newLimitBar == LimitBar) return;
      Application.Current.Dispatcher.BeginInvoke(new Action(() => LoadRates(true)));
    }
    Strategies[] _exceptionStrategies = new[] { Strategies.Massa,Strategies.Hot };
    partial void OnCorridorBarMinutesChanging(int value) {
      if (value == CorridorBarMinutes) return;
      if (!_exceptionStrategies.Contains(Strategy))
        Strategy = Strategies.None;
      Application.Current.Dispatcher.BeginInvoke(new Action(() => LoadRates()));
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
    public void ShowInfoTootipAsync(string text = "", double delayInSeconds = 0) {
      new Scheduler(Application.Current.Dispatcher, TimeSpan.FromSeconds(Math.Max(.01, delayInSeconds))).Command = () => InfoTooltip = text;
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
  }
  public enum TradingMacroTakeProfitFunction { Corridor = 1, Corridor0 = 2, RatesHeight = 4, RatesStDev = 8, Corr_RStd = 9 }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { StDev = 1, Density = 2 }
  [Flags]
  public enum LevelType { CenterOfMass = 1, Magnet = 2, CoM_Magnet = CenterOfMass | Magnet }
  [Flags]
  public enum Strategies {
    None = 0, Breakout = 1, Range = 2, Stop = 4, Auto = 8,
    Breakout_A = Breakout + Auto, Range_A = Range + Auto, Massa = 16, Reverse = 32, Momentum_R = Massa + Reverse,
    Gann = 64, Brange = 128, SuppRes = 256, Vilner = 512, Hot = 1024
  }
  public struct Playback {
    public bool Play;
    public DateTime StartDate;
    public TimeSpan Delay;
    public Playback(bool play, DateTime startDate, TimeSpan delay) {
      this.Play = play;
      this.StartDate = startDate;
      this.Delay = delay;
    }
  }
  public class GannAngle : Models.ModelBase {
    public double Price { get; set; }
    public double Time { get; set; }
    public double Value { get { return Price / Time; } }
    public bool IsDefault { get; set; }
    private bool _IsOn;
    #region IsOn
    public bool IsOn {
      get { return _IsOn; }
      set {
        if (_IsOn != value) {
          _IsOn = value;
          OnPropertyChanged("IsOn");
        }
      }
    }
    #endregion

    public GannAngle(double price, double time, bool isDefault) {
      this.Price = price;
      this.Time = time;
      this.IsDefault = isDefault;
    }
    public override string ToString() {
      return string.Format("{0}/{1}={2:n3}", Price, Time, Value);
    }
  }
  public class GannAngles : Models.ModelBase {
    int _Angle1x1Index = -1;
    public int Angle1x1Index {
      get { return _Angle1x1Index; }
      set { _Angle1x1Index = value; }
    }
    GannAngle[] _Angles = new[]{
     new GannAngle(8,1,true),
     new GannAngle(7,1,false),
     new GannAngle(6,1,false),
     new GannAngle(5,1,false),
     new GannAngle(4,1,true),
     new GannAngle(3,1,true),
     new GannAngle(2,1,true),
     new GannAngle(1.618,1,false),
     new GannAngle(1.382,1,false),
     new GannAngle(1.236,1,false),
     new GannAngle(1,1,true),
     new GannAngle(1,1.236,false),
     new GannAngle(1,1.382,false),
     new GannAngle(1,1.618,false),
     new GannAngle(1,2,true),
     new GannAngle(1,3,true),
     new GannAngle(1,4,true),
     new GannAngle(1,5,false),
     new GannAngle(1,6,false),
     new GannAngle(1,7,false),
     new GannAngle(1,8,true)
    };

    public GannAngle[] Angles {
      get { return _Angles; }
      set { _Angles = value; }
    }

    public void Reset() {
      Angles.ToList().ForEach(a => a.IsOn = a.IsDefault);
    }

    public GannAngle[] ActiveAngles { get { return Angles.Where(a => a.IsOn).ToArray(); } }

    public GannAngles(string priceTimeValues)
      : this() {
      FromString(priceTimeValues);
    }
    public GannAngles() {
      Angles.ToList().ForEach(angle => angle.PropertyChanged += (o, p) => {
        if (ActiveAngles.Length == 0)
          Get1x1().IsOn = true;
        else {
          Angle1x1Index = GetAngle1x1Index();
          OnPropertyChanged("Angles");
        }
      });
    }
    private GannAngle Get1x1() { return Angles.Where(a => a.Price == a.Time).Single(); }
    public int GetAngle1x1Index() { return ActiveAngles.ToList().FindIndex(a => a.Price == a.Time); }

    public GannAngle[] FromString(string priceTimeValues) {
      var ptv = priceTimeValues.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Split('/').Select(v1 => double.Parse(v1)).ToArray()).ToArray();
      var aaa = (from v in ptv
                 join a in Angles on new { Price = v[0], Time = v.Length > 1 ? v[1] : 1 } equals new { a.Price, a.Time }
                 select a).ToList();
      aaa.ForEach(a => a.IsOn = true);
      return Angles;
    }

    public override string ToString() {
      return string.Join(",", ActiveAngles.Select(a => string.Format("{0}/{1}", a.Price, a.Time)));
    }
  }
}
