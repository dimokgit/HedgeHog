using System;
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
    public override int SaveChanges(System.Data.Objects.SaveOptions options) {
      try {
        InitGuidField<TradingAccount>(ta => ta.Id, (ta, g) => ta.Id = g);
        InitGuidField<TradingMacro>(ta => ta.UID, (ta, g) => ta.UID = g);
      } catch { }
      return base.SaveChanges(options);
    }

    private void InitGuidField<TEntity>(Func<TEntity, Guid> getField, Action<TEntity, Guid> setField) {
      var d = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Added)
        .Select(o => o.Entity).OfType<TEntity>().Where(e => getField(e) == new Guid()).ToList();
      d.ForEach(e => setField(e, Guid.NewGuid()));
    }
  }

  public partial class ForexEntities{
    public TradeDirections GetTradeDirection_(DateTime today,string pair,int maPeriod,out DateTime dateClose) {
      today = today.AddDays(-1);
      var bars = this.t_Bar.Where(b => b.Pair == pair && b.Period == 24 && b.StartDate <= today).OrderByDescending(b=>b.StartDate).Take(maPeriod+1).ToArray();
      int outBegIdx, outNBElement;
      double[] outRealBig = new double[20];
      double[] outRealSmall = new double[20];
      Func<t_Bar, double> value = b => new[] { b.AskOpen + b.BidOpen + b.AskClose + b.BidClose }.Average();
      var barValues = bars.OrderBy(b => b.StartDate).Select(b => value(b)).ToArray();
      TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealBig);
      barValues = barValues.Skip((barValues.Length * .75).ToInt()).ToArray();
      TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealSmall);
      var lastBar = bars.OrderBy(b => b.StartDate).Last();
      dateClose = lastBar.StartDate.AddDays(1);
      return value(lastBar) > outRealBig[0] && value(lastBar) > outRealSmall[0]
        ? TradeDirections.Up
        : value(lastBar) < outRealBig[0] && value(lastBar) < outRealSmall[0]
        ? TradeDirections.Down : TradeDirections.None;
    }
    public TradeDirections GetTradeDirection(DateTime today, string pair, int maPeriod, out DateTime dateClose) {
      var period = 60;
      var bars = this.BarsByMinutes(pair, (byte)period, today, 24, maPeriod).ToArray();
      int outBegIdx, outNBElement;
      double[] outRealBig = new double[20];
      double[] outRealSmall = new double[20];
      Func<BarsByMinutes_Result, double> value = b => new[] { b.AskOpen + b.BidOpen + b.AskClose + b.BidClose }.Average().Value;
      var barValues = bars.OrderBy(b => b.DateOpen).Select(b => value(b)).ToArray();
      TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealBig);
      barValues = barValues.Skip((barValues.Length * .75).ToInt()).ToArray();
      TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealSmall);
      var lastBar = bars.OrderBy(b => b.DateOpen).Last();
      dateClose = lastBar.DateClose.Value.AddMinutes(period);
      return value(lastBar) > outRealBig[0] && value(lastBar) > outRealSmall[0]
        ? TradeDirections.Up
        : value(lastBar) < outRealBig[0] && value(lastBar) < outRealSmall[0]
        ? TradeDirections.Down : TradeDirections.None;
    }

  }
  public partial class TradeHistory {
    public double NetPL { get { return GrossPL - Commission; } }
  }

  public partial class OrderTemplate {
  }

  public partial class TradingMacro {
    static Guid _sessionId = Guid.NewGuid();
    public static Guid SessionId { get { return _sessionId; } }
    public void ResetSessionId() {
      _sessionId = Guid.NewGuid();
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
        } catch (Exception exc) { return new int[] { }; }
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
      if (e.PropertyName == Metadata.CorridorStatisticsMetadata.StartDate && (SupportPrice == 0 && ResistancePrice ==0 || !IsSuppResManual)) {

        SetGannAngleOffset(cs);

        var rates = Rates.Where(r=>r.StartDate >= cs.StartDate).OrderBy(r => r.PriceAvg).ToArray();
        var rateMax = rates.Last();
        var rateMin = rates.First();
        if (false) {
          if (cs.Slope > 0) Support = rateMin.Clone() as Rate;
          if (cs.Slope < 0) Resistance = rateMax.Clone() as Rate;
          return;
        }
        if (cs.Slope > 0 && (SupportPrice == 0 || GetSupportPrice(rateMin) < SupportPrice)) {
          if( SupportPrice>0) Resistance = Support;
          Support = rateMin.Clone() as Rate;
        }
        if (cs.Slope < 0 &&( ResistancePrice == 0 || GetResistancePrice(rateMax) > ResistancePrice)) {
          if (ResistancePrice > 0) Support = Resistance;
          Resistance = rateMax.Clone() as Rate;
        }
        if (SupportPrice == 0) Support = Resistance;
        if (ResistancePrice == 0) Resistance = Support;
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
      var ratesForGann = Rates.SkipWhile(r=>r.StartDate< CorridorStats.StartDate).ToArray();
      var rateStart = CorridorStats.Slope > 0 ? ratesForGann.OrderBy(r => r.BidLow).First() : ratesForGann.OrderBy(r => r.AskHigh).Last();
      ratesForGann = ratesForGann.Where(r => r >= rateStart).OrderBars().ToArray();
      var interseption = Slope > 0 ? ratesForGann[0].PriceLow: ratesForGann[0].PriceHigh;
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
      set {
        _GannAngleActive = Math.Max(value, GannAngleIndexMinimum);
      }
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


    DateTime _lastRateTime;
    public DateTime LastRateTime {
      get { return _lastRateTime; }
      set {
        if (_lastRateTime == value) return;
        _lastRateTime = value;
        OnPropertyChanged("LastRateTime");
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

    double? _net;
    public double? Net {
      get { return _net; }
      set {
        if (_net == value) return;
        _net = value; OnPropertyChanged("Net");
      }
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

    private double _PipsPerPosition;
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

    string lastTradeId = "";

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

    TradeSignal BuySignal;
    TradeSignal SellSignal;

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
    bool GetSignal(bool signal) { return ReverseStrategy ? !signal : signal; }

    public bool? OpenSignal {
      get {
        if (CorridorStats == null) return null;
        var slope = CorridorStats.Slope;
        if (Strategy == Strategies.SuppRes) {
          if (CloseSignal.HasValue) return null;
          if (RateLast.BidLow > ResistancePrice) SellWhenReady = true;
          if (RateLast.AskHigh< SupportPrice) BuyWhenReady = true;
          if (Math.Min(SupportPrice, ResistancePrice) == 0) return null;
          var margin = SuppResArray.Height() / 10;
          if (!IsSellLock && RateLast.BidLow.Between(ResistancePrice + margin, double.MaxValue)) return GetSignal(true);
          if (!IsBuyLock && RateLast.AskHigh.Between(double.MinValue, SupportPrice-margin)) return GetSignal(false);
          if (BuyWhenReady && RateLast.BidLow > SupportPrice + margin) return GetSignal(true);
          if (SellWhenReady && RateLast.AskHigh< ResistancePrice - margin)  return GetSignal(false);
          return null;
        }
        if (Strategy == Strategies.Momentum && CorridorStats.HeightUpDownInPips > 0) {
          if (CorridorStats.HeightUpDown / SpreadLong < CorridorHeightToSpreadRatioLow) {
            if (CorridorStats.Slope > 0) {
              SellWhenReady = true;
              return true;
            }
            if (CorridorStats.Slope < 0) {
              BuyWhenReady = true;
              return false;
            }
          }
          if (CorridorHeightToSpreadRatio > CorridorHeightToSpreadRatioHigh) {
            if (SellWhenReady && CorridorStats.Slope > 0 && (!TradeByRateDirection || RateDirection > 0)) return false;
            if (BuyWhenReady && CorridorStats.Slope < 0 && (!TradeByRateDirection || RateDirection < 0)) return true;
          }
          return null;
        }
        if (Strategy == Strategies.Gann) {
          var rateLast = GetLastRateWithGannAngle();
          var lastIndex = Rates.IndexOf(rateLast);
          var ratePrev = Rates[lastIndex-1];
          var gannPrice = GannPriceForTrade();
          if (!double.IsNaN(gannPrice)) {
            if (gannPriceLow(rateLast) > gannPrice && ratePrev.PriceLow < gannPrice) return true;
            if (gannPriceHigh(rateLast) < gannPrice && ratePrev.PriceHigh > gannPrice) return false;
          }
          return null;
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

    private Rate GetLastRateWithGannAngle() {
      return GetLastRate(Rates.SkipWhile(r => r.GannPrices.Length == 0).TakeWhile(r => r.GannPrices.Length > 0).ToArray());
    }
    private Rate GetLastRate() { return GetLastRate(Rates); }
    private Rate GetLastRate(ICollection<Rate> rates) {
      if (rates.Count == 0) return null;
      var rateLast = rates.Skip(rates.Count - 2)
        .LastOrDefault(r => r.StartDate <= CurrentPrice.Time - TimeSpan.FromMinutes(LimitBar / 2.0));
      return rateLast ?? rates.Last();
    }
    static Func<Rate, double> gannPriceHigh = rate => rate.PriceAvg;
    static Func<Rate, double> gannPriceLow = rate => rate.PriceAvg;

    public double? PriceCmaDiffHigh {
      get {
        if (CorridorStats == null) return double.NaN;
        if (Strategy != Strategies.Gann)return CorridorStats.PriceCmaDiffHigh;
        var rateLast = GetLastRateWithGannAngle();
        return rateLast == null ? double.NaN : GannPriceForTrade(rateLast) - gannPriceHigh(rateLast);
      }
    }
    public double? PriceCmaDiffHighInPips { get { return InPips(PriceCmaDiffHigh); } }
    public double? PriceCmaDiffLow {
      get {
        if( CorridorStats == null)return double.NaN;
        if( Strategy != Strategies.Gann)return CorridorStats.PriceCmaDiffLow;
        var rateLast = GetLastRateWithGannAngle();
        return rateLast == null ? double.NaN : GannPriceForTrade(rateLast) - gannPriceLow(rateLast);
      }
    }
    public double? PriceCmaDiffLowInPips { get { return InPips(PriceCmaDiffLow); } }

    public double CorridorHeightToSpreadRatio { get { return CorridorStats.HeightUpDown / SpreadLong; } }
    public bool? CloseSignal {
      get {
        if (CorridorStats == null || CloseOnOpen) return null;
        switch (Strategy) {
          case Strategies.Gann: return !OpenSignal;
          case Strategies.SuppRes:
            if (!string.IsNullOrWhiteSpace(LastTrade.Id)) {
              if (ResetLockForSuppRes(RateLast) && (IsBuyLock || IsSellLock)) ResetLock();
              var lastTrade = Trades.DefaultIfEmpty(LastTrade).Last();
              if (CloseSuppRes(RateLast)) return lastTrade.Buy;
              if (!CloseOnProfit && CurrentLoss < 0 && Trades.GrossInPips() > -CurrentLoss * 2) return lastTrade.Buy;
            }
            return null;
          case Strategies.Range:
            if (RateLast.PriceAvg.Between(RateLast.PriceAvg2, RateLast.PriceAvg3))
              ResetLock();
            break;
        }
        return CorridorStats.CloseSignal;
      }
    }

    private bool CloseSuppRes(Rate rate) {
      if (Trades.Length > 10) {
        if (LastTrade.Buy && LastTrade.Buy == CorridorStats.OpenRange(0, 100)) {
          IsSellLock = true;
          return true;
        }
        if (!LastTrade.Buy && !LastTrade.Buy == !CorridorStats.OpenRange(0, 100)) {
          IsBuyLock = true;
          return true;
        }
      }
      // Close if price is back inside Corridor
      return rate.BidLow > SupportPriceClose && rate.AskHigh < ResistancePriceClose;
    }
    private bool ResetLockForSuppRes(Rate rate) {
      return rate.AskHigh >= SupportPrice && rate.BidLow <= ResistancePrice;
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


    ITradesManager TradesManager;
    public void SubscribeToTradeClosedEVent(ITradesManager tradesManager) {
      tradesManager.TradeClosed += OnTradeClosed;
      this.TradesManager = tradesManager;
    }
    void OnTradeClosed(object sender, TradeEventArgs e) {
      if (Strategy == Strategies.None) return;
      var trade = e.Trade;
      ResetLock();
      if (trade.Buy) BuyWhenReady = false;
      else SellWhenReady = false;

      if (Strategy == Strategies.SuppRes && trade.PL > 0) {
        if (trade.Buy) IsSellLock = true;
        else IsBuyLock = true;
      }
    }

    protected Dictionary<string, TradeStatistics> TradeStatisticsDictionary = new Dictionary<string, TradeStatistics>();
    public void SetTradesStatistics(Price price, Trade[] trades) {
      foreach (var trade in trades)
        SetTradeStatistics(price, trade);
    }
    public TradeStatistics SetTradeStatistics(Price price, Trade trade) {
      if (!TradeStatisticsDictionary.ContainsKey(trade.Id)) 
        TradeStatisticsDictionary.Add(trade.Id, new TradeStatistics());
      var ts = TradeStatisticsDictionary[trade.Id];
      ts.PLMaximum = trade.PL;
      ts.CorridorHeight = SuppResArray.Height();
      ts.CorridorsRatio = CorridorsRatio;
      Func<double> getMargin = () => (SupportPrice > 0 && ResistancePrice > 0 ? SuppResArray : CorridorRates.ToArray()).Height() / 10;
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

    Rate[] SuppResArray { get { return new[] { Support, Resistance }; } }
    double SpreadForSuppRes { get { return Math.Max(SpreadShort, SpreadLong); } }

    private double GetSupportPrice(Rate support) { return support.BidLow == 0 ? 0 : support.BidLow; }
    public double SupportPrice {
      get { return GetSupportPrice(Support); }
      set {
        var rate = GetRateByPrice(value);
        Support = rate ?? Rates.OrderBy(r=>r.PriceLow).ThenBy(r=>r.Spread).First();
      }
    }
    public double SupportPriceClose { get { return SupportPrice; } }
    Rate _Support = new Rate();
    public Rate Support {
      get {
        if (_Support.PriceAvg == 0 && Rates.Count() > 0) {
          var rate = Rates.SingleOrDefault(r => r.StartDate == SupportDate.GetValueOrDefault());
          if (rate != null) _Support = rate;
          else SupportPrice = Rates.Min(r => r.PriceAvg);
        }
        return _Support; 
      }
      set { 
        _Support = value;
        SupportDate = value.StartDate;
      }
    }


    public double ResistancePrice { 
      get { return GetResistancePrice(Resistance); }
      set {
        var rate = GetRateByPrice(value);
        Resistance = rate ?? Rates.OrderByDescending(r => r.PriceLow).ThenBy(r => r.Spread).First(); ;
      }
    }

    private Rate GetRateByPrice(double price) {
      var rate = Rates.Where(r => price.Between(r.PriceLow, r.PriceHigh)).OrderBy(r => r.Spread).FirstOrDefault();
      return rate;
    }

    private double GetResistancePrice(Rate resistance) { return resistance.AskHigh == 0 ? 0 : resistance.AskHigh; }
    public double ResistancePriceClose { get { return ResistancePrice; } }
    Rate _Resistance = new Rate();
    public Rate Resistance {
      get {

        if (_Resistance.PriceAvg == 0 && Rates.Count() > 0) {
          var rate = Rates.SingleOrDefault(r => r.StartDate == ResistanceDate.GetValueOrDefault());
          if (rate != null) _Resistance = rate;
          else ResistancePrice = Rates.Max(r => r.PriceAvg);
        }

        return _Resistance; 
      }
      set { 
        _Resistance = value;
        ResistanceDate = value.StartDate;
      }
    }

    private Rate _CenterOfMass = new Rate();
    public Rate CenterOfMass {
      get { return _CenterOfMass; }
      set {
        if (_CenterOfMass != value) {
          _CenterOfMass = value;
          OnPropertyChanged("CenterOfMass");
        }
      }
    }

    static Rate CalculateCenterOfMass(ICollection<Rate> rates) {
      return rates.AsParallel().Select(rate => rates.Where(r => r.OverlapsWith(rate) != OverlapType.None).ToArray()).ToArray()
        .OrderBy(rm => rm.Length).Last().OrderBy(r => r.Spread).First();
      var ratesMass = new List<Rate[]>();
      foreach (var rate in rates) {
        var mass = rates.Where(r => r.OverlapsWith(rate) != OverlapType.None).ToArray();
        ratesMass.Add(mass);
      }

      return ratesMass.OrderBy(rm => rm.Length).Last().OrderBy(r => r.Spread).First();
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
    public Rate[] RatesLast { get; protected set; }
    public Rate[] RatesDirection { get; protected set; }
    public double RateLastAsk { get { return RatesLast.Max(r => r.AskHigh); } }
    public double RateLastBid { get { return RatesLast.Min(r => r.BidLow); } }
    Rate[] _RateDirection;
    double distanceOld;
    public int RateDirection { get { return Math.Sign(_RateDirection[1].PriceAvg - _RateDirection[0].PriceAvg); } }
    public void SetPriceCma(Price price) {
      LastRateTime = Rates.DefaultIfEmpty(new Rate()).Max(r => r.StartDate);
      if (!TradesManager.IsInTest) {
        var distanceNew = Rates.Where(r => r != null).Sum(r => r.Spread);
        if (distanceOld != distanceNew || CenterOfMass.StartDate == DateTime.MinValue)
          backgroundWorkers.Run("CenterOfMass", () => {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            CenterOfMass = CalculateCenterOfMass(Rates);
          });
        distanceOld = distanceNew;
      }
      RatesLast = Rates.Skip(Rates.Count - 3).ToArray();
      RateLast = RatesLast.DefaultIfEmpty(new Rate()).Last();
      RatePreLast = Rates.Skip(Rates.Count - 2).DefaultIfEmpty(new Rate()).First();
      _RateDirection = Rates.Skip(Rates.Count - 2).ToArray();
    }

    Func<double?, double> _InPips;
    public Func<double?, double> InPips {
      get { return _InPips == null ? d => 0 : _InPips; }
      set { _InPips = value; }
    }

    double _PointSize;

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
    public int MaxLotSize(IEnumerable<Trade>trades) {
      if (CloseOnProfitOnly) {
        if (trades.Any(t => t.Buy) && trades.Any(t => !t.Buy)) return 0;
        return trades.Sum(t => t.Lots) + LotSize;
      }
        return (Strategy == Strategies.Range && StrategyScore < .47) ? LotSize 
          : Math.Min(LastLotSize + LotSize, MaxLotByTakeProfitRatio.ToInt() * LotSize);
    }

    private double _Profitability;
    public double Profitability {
      get { return _Profitability; }
      set {
        if (_Profitability != value) {
          _Profitability = value;
          OnPropertyChanged("Profitability");
        }
      }
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

    Trade[] _trades = new Trade[0];
    TradeDirections _TradeDirection = TradeDirections.None;
    public TradeDirections TradeDirection {
      get { return _TradeDirection; }
      set {
        _TradeDirection = value;
        OnPropertyChanged("TradeDirection");
      }
    }

    public Trade[] Trades {
      get { return _trades; }
      set {
        _trades = value;
        PositionsBuy = value.Count(t => t.Buy);
        PositionsSell = value.Count(t => !t.Buy);
        if (value.Length > 0) ResetLock();
      }
    }
    [ReadOnly(true)]
    [DesignOnly(true)]
    public double CorridorToRangeRatio {
      get { try { return CorridorHeightByRegression / BigCorridorHeight; } catch { return 0; } }
    }

    public double CorridorToRangeMinimumRatio { get { return 0; } }

    [DisplayName("Corridor Height Multiplier")]
    [Category(categoryTrading)]
    [Description("Ex: CorrUp = PriceRegr + Up + corrHeight*X")]
    public double CorridorHeightMultiplier {
      get { return CorridornessMin; }
      set { CorridornessMin = value; }
    }

    [DisplayName("Reverse Strategy")]
    [Category(categoryTrading)]
    public bool ReverseStrategy_ {
      get { return ReverseStrategy; }
      set { ReverseStrategy = value; }
    }


    [DisplayName("Close All On Profit")]
    [Category(categoryTrading)]
    [Description("Ex: if(trade.PL > Profit) ClosePair()")]
    public bool CloseAllOnProfit_ {
      get { return CloseAllOnProfit; }
      set { CloseAllOnProfit = value; }
    }

    [ReadOnly(true)]
    public bool IsCorridorToRangeRatioOk { get { return CorridorToRangeRatio > CorridorToRangeMinimumRatio; } }

    const string categoryCorridor = "Corridor";
    const string categoryTrading = "Trading";

    [Category(categoryCorridor)]
    [DisplayName("Ratio For Breakout")]
    public double CorridorRatioForBreakout_ {
      get { return CorridorRatioForBreakout; }
      set { CorridorRatioForBreakout = value; }
    }
    [Category(categoryCorridor)]
    [DisplayName("Ratio For Range")]
    [Description("Minimum Ratio to use Range strategy.")]
    public double CorridorRatioForRange_ {
      get { return CorridorRatioForRange; }
      set { CorridorRatioForRange = value; }
    }

    [Category(categoryCorridor)]
    [DisplayName("Reverse Power")]
    [Description("Calc power from rates.OrderBarsDescending().")]
    public bool ReversePower_ {
      get { return ReversePower; }
      set { ReversePower = value; }
    }


    [Category(categoryTrading)]
    [DisplayName("Correlation Treshold")]
    [Description("Ex: if(Corr >  X) return sell")]
    public double CorrelationTreshold_ {
      get { return CorrelationTreshold; }
      set { CorrelationTreshold = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Range Ratio For TradeLimit")]
    [Description("Ex:Exit when PL > Range * X")]
    public double RangeRatioForTradeLimit_ {
      get { return RangeRatioForTradeLimit; }
      set { RangeRatioForTradeLimit = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Range Ratio For TradeStop")]
    [Description("Ex:Exit when PL < -Range * X")]
    public double RangeRatioForTradeStop_ {
      get { return RangeRatioForTradeStop; }
      set { RangeRatioForTradeStop = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade By Angle")]
    public bool TradeByAngle_ {
      get { return TradeByAngle; }
      set { TradeByAngle = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade And Angle Are Synced")]
    public bool TradeAndAngleSynced_ {
      get { return TradeAndAngleSynced; }
      set { TradeAndAngleSynced = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade By First Wave")]
    [Description("If not - will trade by last wave")]
    public bool? TradeByFirstWave_ {
      get { return TradeByFirstWave; }
      set { TradeByFirstWave = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade By Power Average")]
    public bool TradeByPowerAverage_ {
      get { return TradeByPowerAverage; }
      set { TradeByPowerAverage = value; }
    }

    [Category(categoryTrading)]
    [DisplayName("Trade By Power Volatility")]
    public bool TradeByPowerVolatilty_ {
      get { return TradeByPowerVolatilty; }
      set { TradeByPowerVolatilty = value; }
    }



    [Category(categoryCorridor)]
    [DisplayName("Corridor Height By Spread Ratio")]
    [Description("Ex: Height > Spread * X")]
    public double CorridorHeightBySpreadRatio_ {
      get { return CorridorHeightBySpreadRatio; }
      set { CorridorHeightBySpreadRatio = value; }
    }

    public static Strategies[] StrategiesToClose = new Strategies[] { Strategies.Brange };
    private Strategies _Strategy;
    [Category(categoryTrading)]
    public Strategies Strategy {
      get {
        //if (Trades.Length > 0) return _Strategy;
        if ((_Strategy & Strategies.Auto) == Strategies.None) return _Strategy;
        var s = CorridorToRangeRatio <= CorridorRatioForBreakout ? Strategies.Breakout_A : CorridorToRangeRatio >= CorridorRatioForRange ? Strategies.Range_A : _Strategy;
        if (s == _Strategy) return _Strategy;
        _Strategy = s;
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

    public bool IsPowerAverageOk { get { return !TradeByPowerAverage || PowerCurrent > PowerAverage; } }
    public bool IsPowerOk { get { return IsPowerAverageOk && IsPowerVolatilityOk; } }

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

    private double _PowerCurrent;
    public double PowerCurrent {
      get { return _PowerCurrent; }
      set {
        if (_PowerCurrent != value) {
          _PowerCurrent = value;
          OnPropertyChanged("PowerCurrent");
          OnPropertyChanged("IsPowerOk");
        }
      }
    }
    Lib.CmaWalker powerVolatilityWalker = new Lib.CmaWalker(1);
    public bool IsPowerVolatilityOk { get { return !TradeByPowerVolatilty || PowerVolatility <= PowerVolatilityMinimum; } }
    double _PowerVolatility;
    public double PowerVolatility {
      get { return _PowerVolatility; }
      set { 
        _PowerVolatility = value;
        powerVolatilityWalker.Add(value, 10);
        //if (IsPowerVolatilityOk && TradeDirection != TradeDirections.None) TradeDirection = CorridorAngle > 0 ? TradeDirections.Up : TradeDirections.Down;
        OnPropertyChanged("PowerVolatility");
        OnPropertyChanged("IsPowerVolatilityOk");
        OnPropertyChanged("IsPowerOk");
      }
    }

    [DisplayName("Streach Trading Distance")]
    [Category(categoryTrading)]
    [Description("Ex: PL < tradingDistance * (X ? trades.Length:1)")]
    public bool StreachTradingDistance_ {
      get { return StreachTradingDistance; }
      set { StreachTradingDistance = value; }
    }

    [DisplayName("Close On Open Only")]
    [Category(categoryTrading)]
    [Description("Close position only when opposite opens.")]
    public bool CloseOnOpen_ {
      get { return CloseOnOpen; }
      set { CloseOnOpen = value; }
    }

    [DisplayName("Close On Profit")]
    [Category(categoryTrading)]
    [Description("Ex: if( PL > Limit) CloseTrade()")]
    public bool CloseOnProfit_ {
      get { return CloseOnProfit; }
      set { CloseOnProfit = value; }
    }

    [DisplayName("Close On Profit Only")]
    [Category(categoryTrading)]
    [Description("Ex: if( PL > Limit) OpenTrade()")]
    public bool CloseOnProfitOnly_ {
      get { return CloseOnProfitOnly; }
      set { CloseOnProfitOnly = value; }
    }

    [DisplayName("Power Volatility Minimum")]
    [Category(categoryTrading)]
    [Description("Ex: CanTrade = Power > (Power-Avg)/StDev")]
    public double PowerVolatilityMinimum_ {
      get { return PowerVolatilityMinimum; }
      set { PowerVolatilityMinimum = value; }
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

    private double _TradingDistance;
    public double SpreadShort;
    public double SpreadLong;
    public double SpreadLongInPips { get { return InPips(SpreadLong); } }

    public double TradingDistance {
      get { return _TradingDistance; }
      set {
        if (_TradingDistance != value) {
          _TradingDistance = value;
          OnPropertyChanged("TradingDistance");
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
      if (Rates.Count == 0 || price.IsPlayback) return;
      if (LimitBar == 0) {
        Rates.Add(new Rate(price, false));
      } else {
        var priceTime = price.Time.Round(LimitBar);
        if (priceTime > Rates.Last().StartDate)
          Rates.Add(new Rate(priceTime, price.Ask, price.Bid, false));
        else Rates.Last().AddTick(priceTime, price.Ask, price.Bid);
      }
    }

    public void RunPriceChanged(PriceChangedEventArgs e,Action<TradingMacro> doAfterScanCorridor) {
      Stopwatch sw = Stopwatch.StartNew();
      Price price = e.Price;
      CurrentPrice = price;
      Trade[] trades = e.Trades;
      if (Rates.Count == 0 || LastRateTime.AddSeconds(Math.Max(1, LimitBar) * 60 / 3) <= Rates.Last().StartDate)
        LoadRatesAsync();
      SetPriceCma(price);
      TicksPerMinuteSet(price, TradesManager.ServerTime, d => TradesManager.InPips(Pair, d), TradesManager.GetPipSize(Pair));
      if( !IsInPlayback )
        AddCurrentTick(price);
      var lastCmaIndex = Rates.FindLastIndex(r => r.PriceCMA != null)-1;
      var lastCma = lastCmaIndex < 1?new double?[3]:Rates[lastCmaIndex].PriceCMA.Select(c=>new double?(c)).ToArray();
      Rates.Skip(Math.Max(0, lastCmaIndex)).ToArray().SetCMA(PriceCmaPeriod, lastCma[0], lastCma[1], lastCma[2]);
      bgWorkers.Run(workers.ScanCorridor, IsInPlayback, () => {
        ScanCorridor();
        doAfterScanCorridor(this);
      }, evt => Log = evt);
      bgWorkers.Run(workers.RunPrice, IsInPlayback, () => RunPrice(e.Price, e.Account, trades.ByPair(Pair)), evt => Log = evt);
      if (sw.Elapsed > TimeSpan.FromSeconds(1)) {
        Log = new Exception(string.Format("{0}:{1:n}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds));
      }
    }

    static Action emptyAction = ()=>{};
    public void LoadRatesAsync(Action afterDone = null) {
      bgWorkers.Run(workers.LoadRates, IsInPlayback, () => { LoadRates(); (afterDone ?? emptyAction)(); }, evt => Log = evt);
    }

    public void ScanCorridor() {
      try {
        if (Rates.Count == 0 /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        if (false && !IsTradingHours) return;
        #region Prepare Corridor
        var ratesForSpread = LimitBar == 0 ? Rates.GetMinuteTicks(1).OrderBars().ToArray() : Rates.ToArray();
        var spreadShort = SpreadShort = ratesForSpread.Skip(ratesForSpread.Count() - 10).ToArray().AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
        var spreadLong = SpreadLong = ratesForSpread.AverageByIterations(r => r.Spread, 2).Average(r => r.Spread);
        var spread = TradesManager.InPips(Pair, Math.Max(spreadLong, spreadShort));
        var priceBars = FetchPriceBars(PowerRowOffset, ReversePower).OrderByDescending(pb => pb.StartDate).ToArray();
        PowerCurrent = priceBars[0].Power;
        PowerVolatility = 0;
        var powerBars = priceBars.Select(pb => pb.Power).ToArray();
        var corridorMinimum = CorridorHeightBySpreadRatio * (spread + CommissionByTrade(new Trade() { Lots = 10000 }));
        Func<CorridorStatistics, double> heightToMinimum = cs => corridorMinimum / TradesManager.InPips(Pair, cs.HeightUpDown);
        Func<CorridorStatistics, bool> filter = cs => heightToMinimum(cs) < 1;
        double powerAverage;
        var priceBarsForCorridor = priceBars.AverageByIterations(pb => pb.Power, IterationsForPower, out powerAverage);
        PowerAverage = powerAverage;
        priceBarsForCorridor = priceBarsForCorridor.Where(pb => pb.Power > powerAverage).OrderBars().ToArray();
        var priceBarsIntervals = priceBarsForCorridor.Select((r,i)=>new Tuple<int,PriceBar>(i,r)).ToArray().GetIntervals(2);
        var powerBar = !TradeByFirstWave.HasValue ? priceBarsForCorridor.OrderBy(pb => pb.Power).Last()
          : (TradeByFirstWave.Value ? priceBarsIntervals.First() : priceBarsIntervals.Last()).OrderByDescending(pb=>pb.Power).First();//.OrderBy(pb => pb.Power).Last();
        var startDate = CorridorStartDate.GetValueOrDefault(//powerBar.StartDate);
          new[] { powerBar.StartDate, CorridorStats == null ? DateTime.MinValue : CorridorStats.StartDate }.Max());
        var periodsLength = 1;
        var periodsStart = Rates.Count(r => r.StartDate >= startDate);
        var corridornesses = Rates.GetCorridornesses(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, periodsStart, periodsLength, IterationsForCorridorHeights, false)
          //.Where(c => tradesManager.InPips(tm.Pair, c.Value.HeightUpDown) > 0)
          .Select(c => c.Value).ToArray();
        var corridorBig = Rates.ScanCorridorWithAngle(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, IterationsForCorridorHeights, false);
        BigCorridorHeight = corridorBig.HeightUpDown;
        #endregion
        #region Update Corridor
        if (corridornesses.Count() > 0) {
          foreach (int i in CorridorIterationsArray) {
            //var a = corridornesses.Where(filter).Select(c => new { c.StartDate, c.Corridornes }).OrderBy(c => c.Corridornes).ToArray();
            var csCurr = corridornesses.OrderBy(c => c.Corridornes).First();
            var cs = GetCorridorStats(csCurr.Iterations);
            cs.Init(csCurr.Density, csCurr.Slope, csCurr.HeightUp0, csCurr.HeightDown0, csCurr.HeightUp, csCurr.HeightDown, csCurr.LineHigh, csCurr.LineLow, csCurr.Periods, csCurr.EndDate, csCurr.StartDate, csCurr.Iterations);
            if (!filter(cs)) {
              var d = heightToMinimum(cs);
              cs.HeightUp *= d;
              cs.HeightDown *= d;
            }
            cs.FibMinimum = CorridorFibMax(i - 1);
            cs.InPips = d => TradesManager.InPips(Pair, d);
            //SetCorrelations(tm, rates, cs, priceBars);
          }
          TakeProfitPips = CorridorHeightByRegressionInPips;
          RangeCorridorHeight = corridornesses.Last().HeightUpDown;
          CorridorStats = GetCorridorStats().Last();

        } else {
          throw new Exception("No corridors found for current range.");
        }
        #endregion
        PopupText = "";
      } catch (Exception exc) {
        PopupText = exc.Message;
      }
    }

    void ScanTrendLine() {
      var ratesForGann = GannAngleRates.ToArray();
    }
    public Func<Trade,double> CommissionByTrade = trade=> 0.7;

    private bool CanTrade() {
      return Rates.Count() > 0;
    }

    private void RunPrice(Price price,Account account, Trade[] trades) {
      var sw = Stopwatch.StartNew();
      try {
        if (!CanTrade()) return;
        if (!price.IsReal) price = TradesManager.GetPrice(Pair);
        var minGross = CurrentLoss + trades.Sum(t => t.GrossPL);// +tm.RunningBalance;
        if (MinimumGross > minGross) MinimumGross = minGross;
        Net = trades.Length > 0 ? trades.Sum(t => t.GrossPL) : (double?)null;
        CurrentLossPercent = (CurrentLoss + Net.GetValueOrDefault()) / account.Balance;
        BalanceOnStop = account.Balance + StopAmount.GetValueOrDefault();
        BalanceOnLimit = account.Balance + LimitAmount.GetValueOrDefault();
        SetLotSize(account);
        SetTradesStatistics(price, trades);
      } catch (Exception exc) { Log = exc; }
      if (sw.Elapsed > TimeSpan.FromSeconds(5))
        Log = new Exception("RunPrice(" + Pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
      //Debug.WriteLine("RunPrice[{1}]:{0} ms", sw.Elapsed.TotalMilliseconds, pair);
    }


    public void SetLotSize(Account account) {
      Trade[] trades = account.Trades;
      LotSize = TradingRatio >= 1 ? (TradingRatio * 1000).ToInt()
        : TradesManagedStatic.GetLotstoTrade(account.Balance, TradesManager.Leverage(Pair), TradingRatio, TradesManager.MinimumQuantity);
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


    private int AllowedLotSize(ICollection<Trade> trades) {
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


    private object _Log;
    public object Log {
      get { return _Log; }
      set {
        if (_Log != value) {
          _Log = value;
          OnPropertyChanged("Log");
        }
      }
    }

    bool isLoggedIn { get { return TradesManager != null && TradesManager.IsLoggedIn; } }
    public void LoadRates() {
      try {
        var a = GannAnglesArray;
        if (!IsInPlayback && isLoggedIn) {
          InfoTooltip = "Loading Rates";
          Debug.WriteLine("LoadRates[{0}:{2}] @ {1:HH:mm:ss}", Pair, TradesManager.ServerTime, (BarsPeriodType)LimitBar);
          var sw = Stopwatch.StartNew();
          var serverTime = TradesManager.ServerTime;
          var ratesStartDate = Rates.Select(r => r.StartDate).DefaultIfEmpty().Min();
          RatesLoader.LoadRates(TradesManager, Pair, LimitBar, BarsCount, CorridorStartDate.GetValueOrDefault(TradesManagedStatic.FX_DATE_NOW), TradesManagedStatic.FX_DATE_NOW, Rates);
          Rates.SetCMA(PriceCmaPeriod);
          if (sw.Elapsed > TimeSpan.FromSeconds(1))
            Debug.WriteLine("LoadRates[" + Pair + ":{1}] - {0:n1} sec", sw.Elapsed.TotalSeconds, (BarsPeriodType)LimitBar);
          ScanCorridor();
        }
        LastRateTime = Rates.Select(r => r.StartDate).DefaultIfEmpty().Max();
      } catch (Exception exc) {
        Log = exc;
      } finally {
        InfoTooltip = "";
      }
    }



    partial void OnLimitBarChanging(int newLimitBar) {
      if (newLimitBar * LimitBar == 0){
        CorridorStartDate = null;
      }
      Strategy = Strategies.None;
      Rates.Clear();
      Application.Current.Dispatcher.BeginInvoke(new Action(() => LoadRates()));
    }
    partial void OnCorridorBarMinutesChanged() {
      Strategy = Strategies.None;
      LoadRates();
    }

    RatesLoader _ratesLoader;

    internal RatesLoader RatesLoader {
      get {
        if (_ratesLoader == null) _ratesLoader = new RatesLoader();
        return _ratesLoader; 
      }
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
  public enum TradeDirections { None,Up, Down }
  [Flags]
  public enum Strategies {
    None = 0, Breakout = 1, Range = 2, Stop = 4, Auto = 8,
    Breakout_A = Breakout + Auto, Range_A = Range + Auto, Momentum = 16, Reverse = 32, Momentum_R = Momentum + Reverse,
    Gann = 64, Brange = 128,SuppRes = 256
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
}
