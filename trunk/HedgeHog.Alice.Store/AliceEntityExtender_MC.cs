﻿using System;
using System.Collections.Generic;
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

  public partial class TradingMacro {
    public TradingMacro() {
      this.SuppRes.AssociationChanged += new CollectionChangeEventHandler(SuppRes_AssociationChanged);
    }

    void SuppRes_AssociationChanged(object sender, CollectionChangeEventArgs e) {
      var ec = sender as EntityCollection<SuppRes>;
      foreach(var sr in ec)
        sr.PropertyChanged += new PropertyChangedEventHandler(SuppRes_PropertyChanged);
    }

    void SuppRes_PropertyChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == Metadata.SuppResMetadata.Rate) {
        OnPropertyChanged(Metadata.TradingMacroMetadata.SuppResHeight);
        OnPropertyChanged(Metadata.TradingMacroMetadata.IsSuppResHeightOk);
      }
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

    private int _LotSizeByLoss;
    public int LotSizeByLoss {
      get { return _LotSizeByLoss; }
      set {
        if (_LotSizeByLoss != value) {
          _LotSizeByLoss = value;
          OnPropertyChanged("LotSizeByLoss");
          OnPropertyChanged("TakeProfitPipsMinimum");
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
        OnPropertyChanged("TakeProfitPipsMinimum");
      }
    }
    #endregion

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
      if (Rates.Count > 0) {
        var rateLast = Rates.Last();
        if (CorridorStats != null) {
          SetGannAngles();
          var slope = CorridorStats.Slope;
          Predicate<double> filter = ga => slope < 0 ? rateLast.PriceAvg > ga : rateLast.PriceAvg < ga;
          var index = GetGannAngleIndex();// GetGannIndex(rateLast, slope);
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
      _gannAngles = GannAnglesList.FromString(GannAngles).Where(a=>a.IsOn).Select(a=>a.Value).ToArray();
      OnPropertyChanged("GannAngles_");
      return;
      _gannAngles = GannAngles.Split(',')
        .Select(a => (double)System.Linq.Dynamic.DynamicExpression.ParseLambda(new ParameterExpression[0], typeof(double), a).Compile().DynamicInvoke())
        .ToArray();
    }
    double[] _gannAngles;
    public double[] GannAnglesArray { get { return _gannAngles; } }

    public double Slope { get { return CorridorStats == null ? 0 : CorridorStats.Slope; } }
    public int GetGannAngleIndex() {
      if (Slope != 0) {
        var ratesForGann = SetGannAngles().Reverse().ToList();
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
        if( rateCross != null) return cross2(rateCross.Item1, rateCross.Item2);
      }
      return -1;
    }

    void cs_PropertyChanged(object sender, PropertyChangedEventArgs e) {
      var cs = (sender as CorridorStatistics);
      if (e.PropertyName == Metadata.CorridorStatisticsMetadata.StartDate) {
        if(!IsGannAnglesManual) SetGannAngleOffset(cs);
      }
    }

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
      if( e.Action == NotifyCollectionChangedAction.Add )
        (e.NewItems[0] as CorridorStatistics).PropertyChanged += cs_PropertyChanged;
    }

    private CorridorStatistics _CorridorStats;
    public CorridorStatistics CorridorStats {
      get { return _CorridorStats; }
      set {
        var datePrev = _CorridorStats == null ? DateTime.MinValue : _CorridorStats.StartDate;
        _CorridorStats = value;
        CorridorStatsArray.ToList().ForEach(cs => cs.IsCurrent = cs == value);

        Rates.ToList().ForEach(r => r.PriceAvg1 = r.PriceAvg2 = r.PriceAvg3 = 0);
        if (value != null) {
          var corridorRates = Rates.Skip(Rates.Count - CorridorStats.Periods).ToArray();
          var tangent = corridorRates
            .SetCorridorPrices(CorridorStats.HeightUp0, CorridorStats.HeightDown0, CorridorStats.HeightUp, CorridorStats.HeightDown,
            r => r.PriceAvg, r => r.PriceAvg1, (r, d) => r.PriceAvg1 = d
            , (r, d) => r.PriceAvg02 = d, (r, d) => r.PriceAvg03 = d
            , (r, d) => r.PriceAvg2 = d, (r, d) => r.PriceAvg3 = d
            )[1];

          CorridorAngle = tangent;
          if( !IsGannAnglesManual)
            SetGannAngleOffset(value);
          UpdateTradingGannAngleIndex();
        }

        #region PropertyChanged
        OnPropertyChanged("CorridorThinness");
        OnPropertyChanged("CorridorHeightsRatio");
        OnPropertyChanged("CorridorHeightByRegressionInPips");
        OnPropertyChanged("CorridorHeightByRegressionInPips0");
        OnPropertyChanged("CorridorToRangeRatio");
        OnPropertyChanged("CorridorsRatio");

        OnPropertyChanged("CorridorStats");
        OnPropertyChanged("PriceCmaDiffHighInPips");
        OnPropertyChanged("PriceCmaDiffLowInPips");
        OnPropertyChanged("OpenSignal");
        #endregion
      }
    }
    public void UpdateTradingGannAngleIndex() {
      if (CorridorStats == null) return;
      int newIndex = GetGannAngleIndex();
      if (true || newIndex > GannAngleActive)
        GannAngleActive = newIndex;
    }

    private int GetGannAngleIndex_() {
      var rateLast = Rates.Last();
      Predicate<double> filter = ga => CorridorStats.Slope > 0 ? rateLast.PriceAvg < ga : rateLast.PriceAvg > ga;
      return rateLast.GannPrices.ToList().FindLastIndex(filter);
    }

    public Rate[] SetGannAngles() {
      if (CorridorStats == null) return new Rate[0];
      Rates.ToList().ForEach(r => Enumerable.Range(0, GannAnglesArray.Length).ToList()
        .ForEach(i => { if (r.GannPrices.Length > i) r.GannPrices[i] = 0; }));
      var ratesForGann = Rates.SkipWhile(r => r.StartDate < this.GannAnglesAnchorDate.GetValueOrDefault(CorridorStats.StartDate)).ToArray();
      var rateStart = this.GannAnglesAnchorDate.GetValueOrDefault(new Func<DateTime>(() => {
        var rateStop = Slope > 0 ? ratesForGann.OrderBy(r => r.PriceAvg).Last() : ratesForGann.OrderBy(r => r.PriceAvg).First();
        var ratesForStart = ratesForGann.Where(r => r < rateStop);
        if( ratesForStart.Count() == 0)ratesForStart = ratesForGann;
        return (CorridorStats.Slope > 0 ? ratesForStart.OrderBy(r => r.BidLow).First() : ratesForStart.OrderBy(r => r.AskHigh).Last()).StartDate;
      })());
      ratesForGann = ratesForGann.Where(r => r.StartDate >= rateStart).OrderBars().ToArray();
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
      if( GannAngleActive>=0 && rateLast.GannPrices.Length>GannAngleActive && GannAngleActive.Between(0, GannAnglesArray.Length - 1) )
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
    public void TicksPerMinuteSet(Price price, DateTime serverTime, Func<double?, double> inPips, double pointSize) {
      if (_InPips == null) _InPips = inPips;
      //if (PointSize == 0) PointSize = pointSize;
      PriceQueue.Add(price, serverTime);
      OnPropertyChanged("TicksPerMinuteInstant");
      OnPropertyChanged("TicksPerMinute");
      OnPropertyChanged("TicksPerMinuteAverage");
      OnPropertyChanged("TicksPerMinuteMaximun");
      OnPropertyChanged("TicksPerMinuteMinimum");
      OnPropertyChanged("IsTicksPerMinuteOk");
      OnPropertyChanged("PipsPerMinute");
      OnPropertyChanged("PipsPerMinuteCmaFirst");
      OnPropertyChanged("PipsPerMinuteCmaLast");
      OnPropertyChanged("IsSpeedOk");

      OnPropertyChanged("PriceCmaDiffHighInPips");
      OnPropertyChanged("PriceCmaDiffLowInPips");
    }
    #endregion

    public double PipsPerMinute { get { return InPips == null ? 0 : InPips(PriceQueue.Speed(.25)); } }
    public double PipsPerMinuteCmaFirst { get { return InPips == null ? 0 : InPips(PriceQueue.Speed(.5)); } }
    public double PipsPerMinuteCmaLast { get { return InPips == null ? 0 : InPips(PriceQueue.Speed(1)); } }

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

    double? _OpenTradesGross;
    public double? OpenTradesGross {
      get { return _OpenTradesGross; }
      set {
        if (_OpenTradesGross == value) return;
        _OpenTradesGross = value;
        OnPropertyChanged("CurrentNet");
        OnPropertyChanged("OpenTradesGross");
      }
    }

    public bool DoExitOnCurrentNet { get { return RangeRatioForTradeLimit < 0; } }
    public double CurrentNet {
      get { return CurrentLoss + OpenTradesGross.GetValueOrDefault() + Math.Min(RangeRatioForTradeLimit,0); }
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

    private int _PositionsBuy;
    public int PositionsBuy {
      get { return _PositionsBuy; }
      set {
        if (_PositionsBuy != value) {
          _PositionsBuy = value;
          OnPropertyChanged("PositionsBuy");
          OnPropertyChanged("PipsPerPosition");
        }
      }
    }

    private int _PositionsSell;
    public int PositionsSell {
      get { return _PositionsSell; }
      set {
        if (_PositionsSell != value) {
          _PositionsSell = value;
          OnPropertyChanged("PositionsSell");
          OnPropertyChanged("PipsPerPosition");
        }
      }
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

    public double CorridorFibMax(int index) { return 1; }

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

    public int BarsCount {
      get { return CorridorBarMinutes; }
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
        return true ||/*Trades.Length > 0 ||*/ RateLast.StartDate.TimeOfDay.Hours.Between(3, 10);
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
        if( value )SellWhenReady = false; 
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
          return RangeRatioForTradeLimit < 0 ? -RangeRatioForTradeLimit : CorridorHeightByRegression * RangeRatioForTradeLimit;
      }
    }
    bool? GetSignal(bool? signal) { return !signal.HasValue ? null : ReverseStrategy ? !signal : signal; }

    public bool? OpenSignal {
      get {
        if (CorridorStats == null) return null;
        var slope = CorridorStats.Slope;
        var rateLast = Strategy == Strategies.Gann ? GetLastRateWithGannAngle() : GetLastRate();
        var lastIndex = Rates.IndexOf(rateLast);
        var ratePrev = Rates[lastIndex - 1];
        var ratePrev2 = Rates[lastIndex - 2];
        var ratePrev3 = Rates[lastIndex - 3];
        var ratePrev4 = Rates[lastIndex - 4];
        var rates = Rates.TakeWhile(r => r <= rateLast).ToArray();
        bool? ret = GetSuppResSignal(rateLast, ratePrev);
        switch (Strategy) {
          case Strategies.Vilner:
            ret = GetSuppResSignal(rateLast, ratePrev);
            if (ret.HasValue) return ret;
            return GetSignal(GetSuppResSignal(rateLast, ratePrev2));
          case Strategies.Massa:
            return GetSuppResSignal(rateLast, ratePrev) ??
                   GetSuppResSignal(rateLast, ratePrev2) ??
                   GetSuppResSignal(rateLast, ratePrev3);
            if (IsResistanceCurentHigh() && rateLast.PriceAvg >= ResistanceCurrent().Rate) return true;
            if (IsSupportCurentLow() && rateLast.PriceAvg<= SupportCurrent().Rate ) return false;
            var ratesReversed = Rates.ToArray().Reverse().ToArray();
            var resistanceHigh = ResistanceHigh().Rate;
            var upRate = ratesReversed.FirstOrDefault(r => r.PriceHigh >= resistanceHigh) ?? rates.High();
            var supportLow = SupportLow().Rate;
            var downRate = ratesReversed.FirstOrDefault(r => r.PriceLow <= supportLow) ?? rates.Low();
            var up =  downRate.StartDate > upRate.StartDate;
            if (up && IsResistanceCurentLow() && rateLast.PriceAvg > ResistanceCurrent().Rate) return true;
            if (!up && IsSupportCurentHigh() && rateLast.PriceAvg < SupportCurrent().Rate ) return false;
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
            var corridorObject = new[] { 
              new {name = "PriceAvg1", price = new Func<Rate, double>(r => r.PriceAvg1), distance = (rateLast.PriceAvg - rateLast.PriceAvg1).Abs() } ,
              new {name = "PriceAvg02",  price = new Func<Rate, double>(r => r.PriceAvg02), distance = (rateLast.PriceAvg - rateLast.PriceAvg02).Abs() } ,
              new { name = "PriceAvg03", price = new Func<Rate, double>(r => r.PriceAvg03), distance = (rateLast.PriceAvg - rateLast.PriceAvg03).Abs() } ,
              new { name = "PriceAvg2", price = new Func<Rate, double>(r => r.PriceAvg2), distance = (rateLast.PriceAvg - rateLast.PriceAvg2).Abs() } ,
              new { name = "PriceAvg3", price = new Func<Rate, double>(r => r.PriceAvg3), distance = (rateLast.PriceAvg - rateLast.PriceAvg3).Abs() } 
            }.OrderBy(a=>a.distance).First();
            var corridor = corridorObject.price;
            return GetRangeSignal(rateLast, ratePrev, corridor) 
              ?? GetRangeSignal(rateLast, ratePrev2, corridor)
              ?? GetRangeSignal(rateLast, ratePrev3, corridor)
              ?? GetRangeSignal(rateLast, ratePrev4, corridor);
            return GetSignal(CrossOverSignal(GetPriceHigh(rateLast), GetPriceHigh(ratePrev), GetPriceLow(rateLast), GetPriceLow(ratePrev),
                                   rateLast.PriceAvg2, ratePrev.PriceAvg2) ??
                             CrossOverSignal(GetPriceHigh(rateLast), GetPriceHigh(ratePrev), GetPriceLow(rateLast), GetPriceLow(ratePrev),
                                    rateLast.PriceAvg3, ratePrev.PriceAvg3)
                   );
        }
        var os = CorridorStats.OpenSignal;
        if( !os.HasValue )return null;
        if (false && Strategy == Strategies.Range) {
          if (os.Value && RateDirection < 0) return null;
          if (!os.Value && RateDirection > 0) return null;
        }
        return os;
      }
    }

    private bool? GetRangeSignal(Rate rateLast, Rate ratePrev, Func<Rate, double> level) {
      bool? signal = null;
      if (CrossUp(rateLast.PriceAvg, ratePrev.PriceLow, level(rateLast), level(ratePrev)))
        signal = true;
      if (CrossDown(rateLast.PriceAvg, ratePrev.PriceHigh, level(rateLast), level(ratePrev)))
        signal = false;
      return GetSignal(signal);
    }
    private bool? GetSuppResSignal(Rate rateLast, Rate ratePrev) {
      bool? signal = null;
      if (!TradeOnLevelCrossOnly)
        return GetSignal(
        suppResPriceHigh(rateLast) > ResistancePrice ? true
        : suppResPriceHigh(rateLast) < SupportPrice ? false
        : (bool?)null
        );
      //return null;
        if (CrossUp(suppResPriceLow(rateLast), ratePrev.PriceLow, ResistancePrice, ResistancePrice))
          signal = true;
        if (CrossDown(suppResPriceHigh(rateLast), ratePrev.PriceHigh, SupportPrice, SupportPrice)) 
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
      return GetLastRate(Rates.ToArray().SkipWhile(r => r.GannPrices.Length == 0).TakeWhile(r => r.GannPrices.Length > 0).ToArray());
    }
    private Rate GetLastRate() { return GetLastRate(Rates.ToArray()); }
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

    static Func<Rate, double> suppResPriceHigh = rate => rate.PriceAvg;
    static Func<Rate, double> suppResPriceLow = rate => rate.PriceAvg;


    public double? PriceCmaDiffHigh {
      get {
        if (CorridorStats == null) return double.NaN;
        switch (Strategy) {
          case Strategies.Massa:
            var rateMass = GetLastRate();
            return CenterOfMassSell - rateMass.PriceHigh;
          case Strategies.Gann:
            var rateGann = GetLastRateWithGannAngle();
            return rateGann == null ? double.NaN : GannPriceForTrade(rateGann) - gannPriceHigh(rateGann);
          case Strategies.SuppRes:
            var rateSuppRes = GetLastRate();
            return rateSuppRes == null ? double.NaN : SupportPrice - suppResPriceLow(rateSuppRes);
          default:
            return CorridorStats.PriceCmaDiffHigh;
        }
      }
    }
    public double? PriceCmaDiffHighInPips { get { return InPips(PriceCmaDiffHigh); } }
    public double? PriceCmaDiffLow {
      get {
        if( CorridorStats == null)return double.NaN;
        switch (Strategy) {
          case Strategies.Massa:
            var rateMass = GetLastRate();
            return CenterOfMassBuy - rateMass.PriceLow;
          case Strategies.Gann:
            var rateGann = GetLastRateWithGannAngle();
            return rateGann == null ? double.NaN : GannPriceForTrade(rateGann) - gannPriceLow(rateGann);
          case Strategies.SuppRes:
            var rateSuppRes = GetLastRate();
            return rateSuppRes == null ? double.NaN : ResistancePrice - suppResPriceHigh(rateSuppRes);
          default:
            return CorridorStats.PriceCmaDiffLow;
        }
      }
    }
    public double? PriceCmaDiffLowInPips { get { return InPips(PriceCmaDiffLow); } }

    public double SuppResHeight { get { return HeightBySuppRes(); } }
    public double RatesStDevInPips { get { return InPips(RatesStDev); } }
    public double SuppResHeightInPips { get { return InPips(SuppResHeight); } }
    public double SuppResHeightToRatesStDevRatio { get { return SuppResHeight/RatesStDev; } }
    public bool IsSuppResHeightOk { get { return SuppResHeightToRatesStDevRatio > PowerVolatilityMinimum; } }
    public bool CanTrade {
      get {
        return IsSuppResHeightOk;
      }
    }

    public double CorridorHeightToSpreadRatio { get { return CorridorStats.HeightUpDown / SpreadLong; } }
    public bool? CloseSignal {
      get {
        if (CorridorStats == null || CloseOnOpen) return null;
        switch (Strategy) {
          case Strategies.Vilner:
            var buys = Trades.IsBuy(true);
            if (buys.GrossInPips() > TradingDistance// / (buys.Length*2)
                //|| buys.Length > 3 && buys.GrossInPips()>0
              ) return GetSignal(true);
            var sells = Trades.IsBuy(false);
            if (sells.GrossInPips() > TradingDistance// / (sells.Length*2)
                //|| sells.Length > 3 && sells.GrossInPips() > 0
              ) return GetSignal(false);
            return null;
          case Strategies.Massa:
            if(Trades.Length>0) {
              if (CurrentNet > 0 && (TradeOnLevelCrossOnly || Trades.Sum(t => t.Lots) > LotSize) && (SupportPrice - Rates.Last().PriceAvg).Abs() > ratesStDev) return Trades.First().Buy;
              return !OpenSignal;
              if (CurrentNet>0 && Trades.GrossInPips() > InPips(ratesStDev)) return Trades.First().Buy;
              var comm = Trades.Select(t => TradesManager.CommissionByTrade(t)).Sum();
              var currentLoss = CurrentLoss - comm;
              if (CurrentLoss < 0 && CurrentNet >= currentLoss.Abs() / RangeRatioForTradeLimit) return Trades.First().Buy;
              //if (RunningBalance > 0 && CurrentLoss.Abs() > RunningBalance * 0.7) { CurrentLoss = 0; SuppResResetAllTradeCounts(); return Trades.First().Buy; } 
              return null;
            }
            if (Trades.Length > 0 && Trades.GrossInPips() < InPips(SupportLow().Rate - SupportHigh().Rate) / 20)
              return Trades.First().Buy; {
              var comm = Trades.Select(t => TradesManager.CommissionByTrade(t)).Sum();
              var currentLoss = CloseOnProfitOnly ? CurrentLoss - comm : 0;
              if (CurrentNet >= currentLoss / 2 && Trades.GrossInPips() >= InPips(ratesStDev * RangeRatioForTradeLimit)) return Trades.First().Buy;
              if (CurrentNet >= currentLoss / 2 && Trades.GrossInPips() >= InPips(ratesStDev * RangeRatioForTradeLimit * 2)) return Trades.First().Buy;
              if (Trades.Sum(t => t.Lots) >= LotSize * 10 && CurrentNet >= 0) return Trades.First().Buy;
              return null;// GetSignal(!OpenSignal);
            }
            if (!CloseOnProfit || Trades.Length == 0) return null;
            var close = CurrentNet >= TakeProfitPips * LotSize / 10000.0;
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
          case Strategies.Range: return !OpenSignal;
            if (RateLast.PriceAvg.Between(RateLast.PriceAvg2, RateLast.PriceAvg3))
              ResetLock();
            break;
        }
        return CorridorStats.CloseSignal;
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
      TradesManager.TradeClosed += TradesManager_TradeClosed;
      this.TradesManager.TradeAdded -= TradesManager_TradeAddedGlobal;
      TradesManager.TradeAdded += TradesManager_TradeAddedGlobal;
    }

    void TradesManager_TradeAddedGlobal(object sender, TradeEventArgs e) {
      if (!IsMyTrade(e.Trade)) return;
      PositionsBuy = Trades.Count(t => t.Buy);
      PositionsSell = Trades.Count(t => !t.Buy);
      try {
        var step = IsSuppResHeightOk ? MaxLotByTakeProfitRatio : 1;
          if (e.Trade.IsBuy) {
            //ResistanceCurrent().TradesCount++;
            Resistances.ToList().ForEach(r => r.TradesCount *= step);
          } else {
            //SupportCurrent().TradesCount++;
            Supports.ToList().ForEach(r => r.TradesCount *= step);
          }
        //SuppResResetInactiveTradeCounts();
      } catch (Exception exc) {
        Log = exc;
      }
    }
    public void AddTradeAddedHandler(){
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
      PositionsBuy = Trades.Count(t => t.Buy);
      PositionsSell = Trades.Count(t => !t.Buy);
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
        case Strategies.Massa: return;
          if (!ReverseStrategy && trade.PL > 0) {
            var ratio = 1- trade.GrossPL / CurrentLoss.Abs();
            if (trade.Buy) Resistances.ToList().ForEach(r => r.TradesCount *= ratio);
            else Supports.ToList().ForEach(s => s.TradesCount *= ratio);
          }
          break;
      }
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
        return Rates.Where(r => r.StartDate >= CorridorStats.StartDate);
      }
    }
    private IEnumerable<Rate> GannAngleRates {
      get {
        return Rates.SkipWhile(r => r.GannPrice1x1 == 0);
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


    public double CorridorThinness { get { return CorridorStats == null ? 4 : CorridorStats.Thinness; } }

    private static Func<Rate, double> _GetPriceLow = r => r.PriceLow;
    public static Func<Rate, double> GetPriceLow { get { return _GetPriceLow; } }
    private static Func<Rate, double> _GetPriceHigh = r => r.PriceHigh;
    public static Func<Rate, double> GetPriceHigh { get { return _GetPriceHigh; } }

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
          AddSupport(Rates.Min(GetPriceLow));
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
          AddResistance(Rates.Max(GetPriceHigh));
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
    public void SuppResResetAllTradeCounts(int tradesCount = 0) { SuppResResetTradeCounts(SuppRes,tradesCount); }
    public static void SuppResResetTradeCounts(IEnumerable<SuppRes> suppReses,int tradesCount = 0) {
      if (tradesCount < 0)
        suppReses.ToList().ForEach(sr => sr.TradesCount = Math.Max(0,sr.TradesCount + tradesCount));
      else suppReses.ToList().ForEach(sr => sr.TradesCount = tradesCount);
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
    Rate lastSuppres = new Rate();
    private Store.SuppRes SuppResCurrent(SuppRes[] suppReses) {
      foreach(var rate in Rates.ToArray().Reverse())
        foreach(var sr in suppReses)
          if( sr.Rate.Between(rate.PriceLow,rate.PriceHigh) )return sr;
      return suppReses.OrderBy(s => (s.Rate - CurrentPrice.Ask).Abs()).First();
    }

    private SuppRes[] IndexSuppReses(SuppRes[] suppReses) {
      if (!IsActive) return suppReses;
      if (suppReses.Any(a => a.Index == 0)) {
        var index = 1;
        suppReses.OrderByDescending(a => a.Rate).ToList().ForEach(a => { 
          a.Index = index++;
        });
        if (Trades.Length > 0) {
          var trade = Trades.OrderBy(t => t.Time).Last();
          var lots = (Trades.Sum(t => t.Lots)+LotSize) / LotSize;
          var lot = lots/2;
          var rem = lots % 2;
          var tcBuy = lot + (trade.Buy ? rem : 0);
          var tcSell = lot + (!trade.Buy ? rem : 0);
          if (tcBuy > 0) SuppResResetTradeCounts(Resistances,tcBuy);
          if (tcSell > 0) SuppResResetTradeCounts(Supports, tcSell);
        }
      }
      return suppReses;
    }

    #region Supports/Resistances
    #region Add
    public void AddSupport(double rate) { AddSuppRes(rate, true); }
    public void AddResistance(double rate) { AddSuppRes(rate, false); }
    public SuppRes AddBuySellRate(double rate, bool isBuy) { return AddSuppRes(rate, !isBuy); }
    public SuppRes AddSuppRes(double rate, bool isSupport) {
      var srs = (isSupport ? Supports : Resistances);
      var index = srs.Select(a => a.Index).DefaultIfEmpty(0).Max() + 1;
      var sr = new SuppRes { Rate = rate, IsSupport = isSupport, TradingMacroID = UID, UID = Guid.NewGuid(), TradingMacro = this,Index = index,TradesCount = srs.Max(a=>a.TradesCount) };
      GlobalStorage.Context.SuppRes.AddObject(sr);
      GlobalStorage.Context.SaveChanges();
      return sr;
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
      var suppRes = SuppRes.SingleOrDefault(sr => sr.UID == uid);
      RemoveSuppRes(suppRes);
    }

    private static void RemoveSuppRes(Store.SuppRes suppRes) {
      if (suppRes != null)
        //SuppRes.Remove(suppRes);
        GlobalStorage.Context.DeleteObject(suppRes);
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
      return "Rate " + rate + " is not unique in "+Metadata.AliceEntitiesMetadata.SuppRes+" table";
    }
    object supportsLocker = new object();
    public SuppRes[] Supports {
      get {
        lock (supportsLocker) {
          return IndexSuppReses(SuppRes.Where(sr => sr.IsSupport).ToArray());
        }
      }
    }
    object resistancesLocker = new object();
    public SuppRes[] Resistances {
      get {
        lock (resistancesLocker)
          return IndexSuppReses(SuppRes.Where(sr => !sr.IsSupport).ToArray());
      }
    }


    public double[] SupportPrices { get { return Supports.Select(sr => sr.Rate).ToArray(); } }
    public double[] ResistancePrices { get { return Resistances.Select(sr => sr.Rate).ToArray(); } }
    #endregion

    static Func<Rate, double> centerOfMassBuy = r => r.PriceHigh;
    static Func<Rate, double> centerOfMassSell = r => r.PriceLow;

    double ratesStDev = double.MaxValue;

    public double RatesStDev {
      get { return ratesStDev; }
      set {
        if (ratesStDev == value) return;
        ratesStDev = value;
        OnPropertyChanged(Metadata.TradingMacroMetadata.RatesStDev);
        OnPropertyChanged(Metadata.TradingMacroMetadata.RatesStDevInPips);
        OnPropertyChanged(Metadata.TradingMacroMetadata.SuppResHeight);
        OnPropertyChanged(Metadata.TradingMacroMetadata.SuppResHeightInPips);
        OnPropertyChanged(Metadata.TradingMacroMetadata.IsSuppResHeightOk);
        OnPropertyChanged(Metadata.TradingMacroMetadata.SuppResHeightToRatesStDevRatio);
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
          OnPropertyChanged("CenterOfMass");
        }
        //if (Strategy == Strategies.Massa)
      }
    }

    private Lib.CmaWalker _corridorStDevRatio = new Lib.CmaWalker(1);
    public Lib.CmaWalker CorridorStDevRatio { get { return _corridorStDevRatio; } }
    public bool IsCorridorStDevRatioOk { get { return CorridorStDevRatio.Difference > 0; } }

    double[] StDevLevels = new double[0];
    bool _doCenterOfMass = true;
    double _tradeLevelLast;
    void CalculateSuppResLevels() {
      var iterations = IterationsForSuppResLevels;
      var levelsCount = SuppResLevelsCount;
      var suppResCount = Math.Max(levelsCount, StDevLevels.Length);

      #region Adjust SUppReses
      if( levelsCount!=1)
        AdjustSuppResCount(levelsCount);
      #endregion

      if (IsSuppResManual) return;

      var rates = Rates.ToArray().Where(LastRateFilter).ToArray();

      #region com
      Func<ICollection<Rate>, bool, Rate> com = (ratesForCom, up) => {
        switch (LevelType_) {
          case Store.LevelType.Magnet:
            var rs = ratesForCom.FindRatesByPrice(ratesForCom.Average(r=>r.PriceAvg));
            return up ? rs.OrderBy(r => r.PriceAvg).Last() : rs.OrderBy(r => r.PriceAvg).First();
            return ratesForCom.CalculateMagnetLevel(up);
          case Store.LevelType.CenterOfMass:
            return ratesForCom.CenterOfMass(up);
          default: throw new InvalidEnumArgumentException(LevelType_ + " level type is not supported.");
        }
      };
      #endregion

      var support = new LinkedList<SuppRes>(Supports).First;
      var resistance = new LinkedList<SuppRes>(Resistances).First;


      //var fibLevels = Fibonacci.Levels(levelHigh/*.PriceAvg*/, levelLow/*.PriceAvg*/);
      var rateAverage = rates.Average(r => r.PriceAvg); //CenterOfMassBuy;
      var rateStDev = rates.StDev(r => r.PriceAvg);
      CorridorStDevRatio.Add(rateStDev/rateAverage, CorridorBarMinutes / 10);
      OnPropertyChanged(Metadata.TradingMacroMetadata.IsCorridorStDevRatioOk);
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
            var ratio = StDevLevels.Last() ;
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
            resistance.Value.Rate = rateAverage +  ratio*(rateStDevUp - rateStDevDown)/2;
            support.Value.Rate = resistance.Value.Rate;

            resistance = resistance.Next;
            support = support.Next;
          }
          goto case 2;
        case 2: {
          _doCenterOfMass = false;
          var rs2 = rates.ToArray().Reverse().Take((rates.Count() / StDevLevels[0]).ToInt()).ToArray();
            var centersOfMass = rs2.Overlaps(2);//cr.Skip(cr.Length - (cr.Length/PowerVolatilityMinimum).ToInt()).ToArray().Overlaps();
            var com2 = centersOfMass.CentersOfMass().OrderBars().ToArray();
            var rls = rates.Where(com2.First(), com2.Last());
            var rateHigh = rls.Max(r => r.PriceHigh);
            var rateLow = rls.Min(r => r.PriceLow);
            //CorridorStDevRatio.Add(rates.Select(r=>r.PriceAvg).Where(a=>a.Between(rateLow,rateHigh)).ToArray().StDevRatio(), CorridorBarMinutes / 10);
            resistance.Value.Rate = rateHigh;//.PriceAvg;//fibLevels[3];// levelsCount >= 2 ? GetPriceHigh(level) - 0.0001 : GetPriceLow(level);
            support.Value.Rate = resistance.Value.Rate;

            resistance = resistance.Next;
            support = support.Next;

            resistance.Value.Rate = rateLow;//.PriceAvg; //fibLevels[6];// levelsCount >= 2 ? GetPriceLow(level) + .0001 : GetPriceHigh(level);
            support.Value.Rate = resistance.Value.Rate;
        }
          return;
        case 1:
          _doCenterOfMass = false;
          if (StDevLevels.Length == 1) {
            AdjustSuppResCount(2);
            int partsCount = StDevLevels[0].ToInt();
            var partLength = Math.Ceiling(rates.Count()/(double)partsCount).ToInt();
              var centersOfMass = rates.Take(partLength*(partsCount-1)).ToArray().Overlaps();//cr.Skip(cr.Length - (cr.Length/PowerVolatilityMinimum).ToInt()).ToArray().Overlaps();
              var com1 = centersOfMass.CentersOfMass();
              var tradeLevel = centersOfMass.CenterOfMass().PriceAvg;

              resistance.Value.Rate = tradeLevel;// rates.Last().PriceAvg < rateAverage ? com1.Min(r => r.PriceLow) : com1.Min(r => r.PriceHigh);
              support.Value.Rate = resistance.Value.Rate;

              resistance = resistance.Next;
              support = support.Next;

              var rs1 = rates.Skip(partLength * (partsCount - 1)).ToArray();
              RatesStDev = rs1.StDev(r => r.PriceAvg);
              centersOfMass = rs1.Overlaps();//cr.Skip(cr.Length - (cr.Length/PowerVolatilityMinimum).ToInt()).ToArray().Overlaps();
            com1 = centersOfMass.CentersOfMass();
            tradeLevel = centersOfMass.CenterOfMass().PriceAvg;

              resistance.Value.Rate = tradeLevel;// rates.Last().PriceAvg < rateAverage ? com1.Min(r => r.PriceLow) : com1.Min(r => r.PriceHigh);
              support.Value.Rate = resistance.Value.Rate;

              resistance = resistance.Next;
              support = support.Next;
          } else {
            AdjustSuppResCount(StDevLevels.Length);
            foreach (var stDevLevel in StDevLevels) {
              var centersOfMass = rates.ToArray().Reverse().Take((rates.Count() / stDevLevel).ToInt()).ToArray().Overlaps();//cr.Skip(cr.Length - (cr.Length/PowerVolatilityMinimum).ToInt()).ToArray().Overlaps();
              var com1 = centersOfMass.CentersOfMass();
              var tradeLevel = centersOfMass.CenterOfMass().PriceAvg;

              resistance.Value.Rate = tradeLevel;// rates.Last().PriceAvg < rateAverage ? com1.Min(r => r.PriceLow) : com1.Min(r => r.PriceHigh);
              support.Value.Rate = resistance.Value.Rate;

              resistance = resistance.Next;
              support = support.Next;
            }
          }
          return;
        case 7:
          _doCenterOfMass = false;

          resistance.Value.Rate = rateAverage + rateStDev * 2;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage + rateStDev;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage - rateStDev;
          support.Value.Rate = resistance.Value.Rate;
          resistance = resistance.Next;
          support = support.Next;

          resistance.Value.Rate = rateAverage - rateStDev * 2;
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

    private void AdjustSuppResCount(int suppResCount) {
      while (Resistances.Count() < suppResCount)
        AddResistance(0);
      while (Supports.Count() < suppResCount)
        AddSupport(0);

      while (Resistances.Count() > suppResCount)
        RemoveSuppRes(Resistances.Last());
      while (Supports.Count() > suppResCount)
        RemoveSuppRes(Supports.Last());
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


    BackgroundWorkerDispenser<string> backgroundWorkers = new BackgroundWorkerDispenser<string>();
    List<Rate> _Rates = new List<Rate>();
    public List<Rate> Rates {
      get { return _Rates; }
    }
    private Rate _RateLast;
    public Rate RateLast {
      get { return _RateLast; }
      set {
        if (_RateLast != value) {
          _RateLast = value;
          OnPropertyChanged("RateLast");
        }
      }
    }
    /// <summary>
    /// Returns instant deep copy of Rates
    /// </summary>
    /// <returns></returns>
    public Rate[] RatesCopy() { return Rates.Select(r => r.Clone() as Rate).ToArray(); }

    private Rate _RatePreLast;
    public Rate RatePreLast {
      get { return _RatePreLast; }
      set {
        if (_RatePreLast != value) {
          _RatePreLast = value;
          OnPropertyChanged("RatePreLast");
        }
      }
    }
    Rate[] _RatesLast = new Rate[0];
    public Rate[] RatesLast {
      get { return _RatesLast; }
      protected set { _RatesLast = value; }
    }
    public Rate[] RatesDirection { get; protected set; }
    public double RateLastAsk { get { return RatesLast.Select(r => r.AskHigh).DefaultIfEmpty(double.NaN).Max(); } }
    public double RateLastBid { get { return RatesLast.Select(r => r.BidLow).DefaultIfEmpty(double.NaN).Min(); } }
    Rate[] _RateDirection;
    double distanceOld;
    public int RateDirection { get { return Math.Sign(_RateDirection[1].PriceAvg - _RateDirection[0].PriceAvg); } }
    public void SetPriceCma(Price price) {
      try {
        if (/*(Strategy == Strategies.Massa || !TradesManager.IsInTest) &&*/ Rates.Count > 0 && (LimitBar == 0 || Rates.Count < 15000)) {
          var distanceNew = LimitBar == 0 ? 0 : Rates.Where(r => r != null).Sum(r => r.Spread);
          if (LimitBar == 0 || distanceOld != distanceNew || CenterOfMass.StartDate == DateTime.MinValue)
            backgroundWorkers.Run("CenterOfMass", true, () => {
              Thread.CurrentThread.Priority = ThreadPriority.Lowest;
              var rates = new List<Rate>(Rates);
              if (_doCenterOfMass) {
                var cr = CorridorRates.ToArray();
                CentersOfMass = cr.Skip(cr.Length - (cr.Length/PowerVolatilityMinimum).ToInt()).ToArray().Overlaps();
                CenterOfMass = CentersOfMass.CenterOfMass() ?? CenterOfMass;
              }
              MagnetPrice = rates.Average(r => r.PriceAvg);
              CalculateSuppResLevels();
            }, e => Log = e);
          distanceOld = distanceNew;
        }
        RatesLast = Rates.Skip(Rates.Count - 3).ToArray();
        RateLast = RatesLast.DefaultIfEmpty(new Rate()).Last();
        RatePreLast = Rates.Skip(Rates.Count - 2).DefaultIfEmpty(new Rate()).First();
        _RateDirection = Rates.Skip(Rates.Count - 2).ToArray();
      } catch (Exception exc) {
        Log = exc;
      }
    }

    Func<double?, double> _InPips;
    public Func<double?, double> InPips {
      get { return _InPips == null ? d => 0 : _InPips; }
      set { _InPips = value; }
    }

    public double PointSize {
      get {
        if (TradesManager == null) throw new NullReferenceException("TradesManager instance must be set before using PointSize property.");
        return TradesManager.GetPipSize(Pair);
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
          string.Format("{3}:{0:n1}/{1:n1}={2:n1}", sc.Value[0], sc.Value[1], sc.Value[0] / (sc.Value[0] + sc.Value[1])*100, sc.Key))
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
            if (tradeStrategy !=  Strategies.None) {
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
          var strategy = Strategy & (Strategies.Breakout | Strategies.Range| Strategies.SuppRes);
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
    public int MaxLotSize(IEnumerable<Trade> trades) {
      if (CloseOnProfitOnly) {
        if (trades.Any(t => t.Buy) && trades.Any(t => !t.Buy)) return 0;
        return trades.Sum(t => t.Lots) + LotSize;
      }
      if (Strategy == Strategies.Massa || Strategy == Strategies.Vilner) return MaxLotByTakeProfitRatio.ToInt() * LotSize;
      return (Strategy == Strategies.Range && StrategyScore < .47) ? LotSize
        : Math.Min(LastLotSize + LotSize, MaxLotByTakeProfitRatio.ToInt() * LotSize);
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
      get { return TradesManager.GetTrades(Pair);/* _trades.ToArray();*/ }
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
        OnPropertyChanged("Strategy");
        return _Strategy;
      }
      set {
        if (_Strategy != value) {
          _Strategy = value;
          OnPropertyChanged("Strategy");
        }
      }
    }
    private bool _ShowPopup;
    public bool ShowPopup {
      get { return _ShowPopup; }
      set {
        _ShowPopup = value;
        OnPropertyChanged("ShowPopup");
      }
    }
    private string _PopupText;
    public string PopupText {
      get { return _PopupText; }
      set {
        if (_PopupText != value) {
          _PopupText = value;
          ShowPopup = value != "";
          OnPropertyChanged("PopupText");
        }
      }
    }

    private double _PowerAverage;
    public double PowerAverage {
      get { return _PowerAverage; }
      set {
        if (_PowerAverage != value) {
          _PowerAverage = value;
          OnPropertyChanged("PowerAverage");
          OnPropertyChanged("IsPowerAverageOk");
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


    public double BigCorridorHeight { get; set; }

    public double? _SpreadShortLongRatioAverage;
    public double SpreadShortLongRatioAverage {
      get { return _SpreadShortLongRatioAverage.GetValueOrDefault(SpreadShortToLongRatio); }
      set {
        _SpreadShortLongRatioAverage = Lib.CMA(_SpreadShortLongRatioAverage, CorridorBarMinutes / 10.0, value);
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

    public double SpreadShortToLongRatio { get { return SpreadShort / SpreadLong; } }

    public bool IsSpreadShortToLongRatioOk { get { return SpreadShortToLongRatio > SpreadShortToLongTreshold; } }

    
    private double _TradingDistance;
    public double TradingDistance {
      get {
        if (Strategy == Strategies.Vilner) {
          return InPips(HeightBySuppRes());
        }
        var rates = new List<Rate>(Rates);
        return Strategy == Strategies.SuppRes ? 10
          : Math.Max(_TradingDistance, rates.Count == 0 ? 0 : Math.Max(10,InPips(rates.Height())));
      }
      set {
        if (_TradingDistance != value) {
          _TradingDistance = value;
        }
      }
    }

    private double HeightBySuppRes() {
      var srs = SuppRes.OrderBy(sr => sr.Rate).ToArray();
      return srs.Last().Rate - srs.First().Rate;
    }
    private double SuppResHeightMinimum() {
      var srCross = from sr1 in Supports
                    from sr2 in Supports
                    where sr1 != sr2
                    let diff = (sr1.Rate - sr2.Rate).Abs()
                    orderby diff
                    select diff;
      return srCross.First();
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
      if (Rates.Count == 0 || price.IsPlayback) return;
      if (LimitBar == 0) {
        Rates.Add(Rates.First() is Tick ? new Tick(price, 0, false) : new Rate(price, false));
      } else {
        var priceTime = price.Time.Round(LimitBar);
        if (priceTime > Rates.Last().StartDate)
          Rates.Add(Rates.First() is Tick ? new Tick(price, 0, false) : new Rate(priceTime, price.Ask, price.Bid, false));
        else Rates.Last().AddTick(priceTime, price.Ask, price.Bid);
      }
    }

    TasksDispenser<TradingMacro> afterScanTaskDispenser = new TasksDispenser<TradingMacro>();
    public void RunPriceChanged(PriceChangedEventArgs e,Action<TradingMacro> doAfterScanCorridor) {
      try {
        Stopwatch sw = Stopwatch.StartNew();
        Price price = e.Price;
        CurrentPrice = price;
        if (Rates.Count == 0 || LastRatePullTime.AddSeconds(Math.Max(1, LimitBar) * 60 / 3) <= TradesManager.ServerTime)
          LoadRatesAsync();
        SetLotSize(e.Account);
        SetPriceCma(price);
        TicksPerMinuteSet(price, TradesManager.ServerTime, d => TradesManager.InPips(Pair, d), TradesManager.GetPipSize(Pair));
        if (!IsInPlayback)
          AddCurrentTick(price);
        var lastCmaIndex = Rates.FindLastIndex(r => r.PriceCMA != null) - 1;
        var lastCma = lastCmaIndex < 1 ? new double?[3] : Rates[lastCmaIndex].PriceCMA.Select(c => new double?(c)).ToArray();
        Rates.Skip(Math.Max(0, lastCmaIndex)).ToArray().SetCMA(PriceCmaPeriod, lastCma[0], lastCma[1], lastCma[2]);
        //bgWorkers.Run(workers.ScanCorridor, IsInPlayback, () => {
          ScanCorridor();
        //}, evt => Log = evt);
        //afterScanTaskDispenser.Run(this, () => { 
        doAfterScanCorridor(this);
        //}, exc => Log = exc);
        bgWorkers.Run(workers.RunPrice, IsInPlayback, () => RunPrice(e.Price, e.Account, Trades), evt => Log = evt);
        if (sw.Elapsed > TimeSpan.FromSeconds(1)) {
          Log = new Exception(string.Format("{0}:{1:n}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds));
        }
        OnPropertyChanged("TradingDistance");
      } catch (Exception exc) {
        Log = exc;
      }
    }

    static Action emptyAction = ()=>{};
    public void LoadRatesAsync(Action afterDone = null) {
      bgWorkers.Run(workers.LoadRates, IsInPlayback, () => {
        LoadRates();
        SetPriceCma(CurrentPrice);
        (afterDone ?? emptyAction)();
      }, evt => Log = evt);
    }

    public void ScanCorridor() {
      try {
        if (Rates.Count == 0 /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        if (false && !IsTradingHours) return;
        #region Prepare Corridor
        var ratesForSpread = LimitBar == 0 ? Rates.GetMinuteTicks(1).OrderBars().ToArray() : Rates.ToArray();
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
        var periodsLength = 1;
        var periodsStart = Rates.Count(r => r.StartDate >= startDate);
        if (periodsStart == 1) return;
        var corridornesses = Rates.GetCorridornesses(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, periodsStart, periodsLength, IterationsForCorridorHeights, false)
          //.Where(c => tradesManager.InPips(tm.Pair, c.Value.HeightUpDown) > 0)
          .Select(c => c.Value).ToArray();
        var corridorBig = Rates.ScanCorridorWithAngle(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, IterationsForCorridorHeights, false);
        if (corridorBig != null)
          BigCorridorHeight = corridorBig.HeightUpDown;
        #endregion
        #region Update Corridor
        if (corridornesses.Count() > 0) {
          foreach (int i in CorridorIterationsArray) {
            //var a = corridornesses.Where(filter).Select(c => new { c.StartDate, c.Corridornes }).OrderBy(c => c.Corridornes).ToArray();
            var csCurr = corridornesses.OrderBy(c => c.Corridornes).First();
            var cs = GetCorridorStats(csCurr.Iterations);
            cs.Init(csCurr.Density, csCurr.Slope, csCurr.HeightUp0, csCurr.HeightDown0, csCurr.HeightUp, csCurr.HeightDown, csCurr.LineHigh, csCurr.LineLow, csCurr.Periods, csCurr.EndDate, csCurr.StartDate, csCurr.Iterations);
            cs.FibMinimum = CorridorFibMax(i - 1);
            cs.InPips = d => TradesManager.InPips(Pair, d);
            //SetCorrelations(tm, rates, cs, priceBars);
          }
          TakeProfitPips = CalculateTakeProfit();
          RangeCorridorHeight = corridornesses.Last().HeightUpDown;
          CorridorStats = GetCorridorStats().Last();

        } else {
          throw new Exception("No corridors found for current range.");
        }
        #endregion
        PopupText = "";
      } catch (Exception exc) {
        PopupText = exc.Message;
      } finally {
        CorridorStats = GetCorridorStats().Last();
      }
    }

    private double CalculateTakeProfit() {
      switch (Strategy) {
        case Strategies.Vilner:
          return TradingDistance;
        default: return CorridorHeightByRegression;
      }
    }

    void ScanTrendLine() {
      var ratesForGann = GannAngleRates.ToArray();
    }
    public Func<Trade,double> CommissionByTrade = trade=> 0.7;

    private void RunPrice(Price price,Account account, Trade[] trades) {
      var sw = Stopwatch.StartNew();
      try {
        if (Rates.Count == 0) return;
        if (!price.IsReal) price = TradesManager.GetPrice(Pair);
        var minGross = CurrentLoss + trades.Sum(t => t.GrossPL);// +tm.RunningBalance;
        if (MinimumGross > minGross) MinimumGross = minGross;
        OpenTradesGross = trades.Length > 0 ? trades.Sum(t => t.GrossPL)- TradesManager.CommissionByTrades(trades) : (double?)null;
        CurrentLossPercent = (CurrentLoss + OpenTradesGross.GetValueOrDefault()) / account.Balance;
        BalanceOnStop = account.Balance + StopAmount.GetValueOrDefault();
        BalanceOnLimit = account.Balance + LimitAmount.GetValueOrDefault();
        SetTradesStatistics(price, trades);
      } catch (Exception exc) { Log = exc; }
      if (sw.Elapsed > TimeSpan.FromSeconds(5))
        Log = new Exception("RunPrice(" + Pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
      //Debug.WriteLine("RunPrice[{1}]:{0} ms", sw.Elapsed.TotalMilliseconds, pair);
    }


    public void SetLotSize(Account account) {
      Trade[] trades = account.Trades;
      LotSize = TradingRatio <= 0 ? 0 : TradingRatio >= 1 ? (TradingRatio * 1000).ToInt()
        : TradesManagerStatic.GetLotstoTrade(account.Balance, TradesManager.Leverage(Pair), TradingRatio, TradesManager.MinimumQuantity);
      LotSizePercent = LotSize / account.Balance / TradesManager.Leverage(Pair);
      LotSizeByLoss = AllowedLotSize(trades);
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


    public int AllowedLotSize(ICollection<Trade> trades) {
      if (Strategy == Strategies.Massa || Strategy == Strategies.Range) {
        var tc = (ResistanceCurrent().TradesCount + SupportCurrent().TradesCount).ToInt();
        if (tc > MaximumPositions) {
          SuppResResetAllTradeCounts();
          tc = 0;
        }
        return LotSize * Math.Max(1, tc /**  (IsCorridorStDevRatioOk ? 1 : 0)*/);
      }
      return Math.Min(MaxLotSize(trades)/* - trades.Sum(t=>t.Lots)*/, Math.Max(LotSize, CalculateLot( trades)));
    }

    private int CalculateLot(ICollection<Trade> trades) {
      Func<int, int> returnLot = d => Math.Max(LotSize, d);
      if (FreezeStopType == Freezing.Freez)
        return returnLot(trades.Sum(t => t.Lots) * 2);
      return returnLot(CalculateLotCore(CurrentLoss + trades.Sum(t => t.GrossPL)));
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
    protected void SetPriceBars( bool isLong, PriceBar[] priceBars) {
      if (isLong) PriceBars.Long = priceBars;
      else PriceBars.Short = priceBars;
    }
    protected PriceBar[] FetchPriceBars(int rowOffset, bool reversePower) {
      return FetchPriceBars(rowOffset, reversePower, DateTime.MinValue);
    }
    protected PriceBar[] FetchPriceBars(int rowOffset, bool reversePower, DateTime dateStart) {
      var isLong = dateStart == DateTime.MinValue;
      var rs = Rates.Where(r => r.StartDate >= dateStart).GroupTicksToRates();
      var ratesForDensity = (reversePower ? rs.OrderBarsDescending() : rs.OrderBars()).ToArray();
      ratesForDensity.Index();
      SetPriceBars( isLong, ratesForDensity.GetPriceBars(TradesManager.GetPipSize(Pair), rowOffset));
      return GetPriceBars(isLong);
    }
    public PriceBar[] GetPriceBars( bool isLong) {
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
    public void LoadRates(bool dontStreachRates = false) {
      try {
        if (!IsInPlayback && isLoggedIn) {
          InfoTooltip = "Loading Rates";
          Debug.WriteLine("LoadRates[{0}:{2}] @ {1:HH:mm:ss}", Pair, TradesManager.ServerTime, (BarsPeriodType)LimitBar);
          var sw = Stopwatch.StartNew();
          var serverTime = TradesManager.ServerTime;
          var startDate = !DoStreatchRates || dontStreachRates ? TradesManagerStatic.FX_DATE_NOW
            : CorridorStartDate.GetValueOrDefault(CorridorStats == null ? TradesManagerStatic.FX_DATE_NOW : CorridorStats.StartDate.AddMinutes(-LimitBar * 5));
          RatesLoader.LoadRates(TradesManager, Pair, LimitBar, BarsCount, startDate, TradesManagerStatic.FX_DATE_NOW, Rates);
          Rates.SetCMA(PriceCmaPeriod);
          if (sw.Elapsed > TimeSpan.FromSeconds(1))
            Debug.WriteLine("LoadRates[" + Pair + ":{1}] - {0:n1} sec", sw.Elapsed.TotalSeconds, (BarsPeriodType)LimitBar);
          LastRatePullTime = TradesManager.ServerTime;
          ScanCorridor();
          OnPropertyChanged("Rates");
        }
      } catch (Exception exc) {
        Log = exc;
      } finally {
        InfoTooltip = "";
      }
    }

    #region Overrides
    
    partial void OnFibMaxChanged() {
      StDevLevels = FibMax.Split(',').Select(s => double.Parse(s)).ToArray();
    }
    partial void OnCurrentLossChanged() {
      if (CurrentLoss >= 0) SuppResResetAllTradeCounts();
      OnPropertyChanged("CurrentNet");
    }
    partial void OnLimitBarChanging(int newLimitBar) {
      if (newLimitBar == LimitBar) return;
      CorridorStartDate = null;
      Strategy = Strategies.None;
      Rates.Clear();
      Application.Current.Dispatcher.BeginInvoke(new Action(() => LoadRates(true)));
    }
    partial void OnCorridorBarMinutesChanging(int value) {
      if (value == CorridorBarMinutes) return;
      if (Strategy != Strategies.Massa)
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

    private string _InfoTooltip;
    public string InfoTooltip {
      get { return _InfoTooltip; }
      set {
        _InfoTooltip = value;
        OnPropertyChanged(TradingMacroMetadata.InfoTooltip);
      }
    }
  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { StDev = 1, Density = 2 }
  [Flags]
  public enum LevelType { CenterOfMass = 1, Magnet= 2, CoM_Magnet = CenterOfMass | Magnet }
  [Flags]
  public enum Strategies {
    None = 0, Breakout = 1, Range = 2, Stop = 4, Auto = 8,
    Breakout_A = Breakout + Auto, Range_A = Range + Auto, Massa = 16, Reverse = 32, Momentum_R = Massa + Reverse,
    Gann = 64, Brange = 128,SuppRes = 256,Vilner = 512
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
  public class GannAngle :Models.ModelBase{
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

    public GannAngle(double price,double time, bool isDefault) {
      this.Price = price;
      this.Time = time;
      this.IsDefault = isDefault;
    }
    public override string ToString() {
      return string.Format("{0}/{1}={2:n3}", Price, Time, Value);
    }
  }
  public class GannAngles:Models.ModelBase {
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

    public GannAngles(string priceTimeValues) : this() {
        FromString(priceTimeValues);
    }
    public GannAngles() {
      Angles.ToList().ForEach(angle => angle.PropertyChanged += (o,p) => {
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
