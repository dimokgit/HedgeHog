using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using HedgeHog.Shared;
using System.Collections.ObjectModel;
using System.ComponentModel;

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
    private ObservableCollection<CorridorStatistics> _CorridorStatsArray = new ObservableCollection<CorridorStatistics>();
    public ObservableCollection<CorridorStatistics> CorridorStatsArray {
      get {
        //if( _CorridorStatsArray == null)
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

    private CorridorStatistics _CorridorStats;
    public CorridorStatistics CorridorStats {
      get { return _CorridorStats; }
      set {
        _CorridorStats = value;
        CorridorStatsArray.ToList().ForEach(cs => cs.IsCurrent = cs == value);
        OnPropertyChanged("CorridorStats");
        OnPropertyChanged("PriceCmaDiffHighInPips");
        OnPropertyChanged("PriceCmaDiffLowInPips");
      }
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
      if (PointSize == 0) PointSize = pointSize;
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

    public double? PriceCmaDiffHigh { get { return CorridorStats == null ? 0 : CorridorStats.PriceCmaDiffHigh; } }
    public double? PriceCmaDiffHighInPips { get { return InPips(PriceCmaDiffHigh); } }
    public double? PriceCmaDiffLow { get { return CorridorStats == null ? 0 : CorridorStats.PriceCmaDiffLow; } }
    public double? PriceCmaDiffLowInPips { get { return InPips(PriceCmaDiffLow); } }

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


    double _currentPrice;
    public double CurrentPrice {
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
        return true ||/*Trades.Length > 0 ||*/ RateLast.StartDate.TimeOfDay.Hours.Between(0, 16);
      }
    }


    public bool? OpenSignal {
      get {
        if (CorridorStats == null) return null;
        if (Strategy == Strategies.Correlation) {
          if (CorridorAngle > 0) return true;
          if (CorridorAngle < 0) return false;
          return null;
          if (Correlation > +CorrelationTreshold) TradeDirection = TradeDirections.Down;
          if (Correlation < -CorrelationTreshold) TradeDirection = TradeDirections.Up;
        }

        if (Strategy == Strategies.Momentum)
          if (IsPowerVolatilityOk)
            return CorridorStats.PriceCmaDiffHigh > RateLast.PriceAvg1 ? false 
              : CorridorStats.PriceCmaDiffLow < RateLast.PriceAvg1 ? true : (bool?)null;
          else return null;
        if (Strategy == Strategies.OverPower)
          if (IsPowerAverageOk) return CorridorAngle < 0;
          else return null;
        var os = CorridorStats.OpenSignal;
        if( !os.HasValue )return null;
        if (Strategy == Strategies.Range) {
          if (os.Value && RateDirection < 0) return null;
          if (!os.Value && RateDirection > 0) return null;
        }
        return os;
      }
    }
    public bool? CloseSignal {
      get {
        return CorridorStats == null ? null : CorridorStats.CloseSignal;
      }
    }

    public Price PriceCurrent { get; set; }

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

    public double CorridorHeightsRatio { get { return Fibonacci.FibRatioSign(CorridorStats.HeightHigh, CorridorStats.HeightLow); } }

    [DisplayName("Iterations For Power")]
    [Description("Number of Iterations to calculate power for wave")]
    [Category(categoryCorridor)]
    public int IterationsForPower {
      get { return CorridorIterationsIn; } 
      set { CorridorIterationsIn = value; } 
    }

    [DisplayName("Iterations For Corridor Heights")]
    [Description("Ex: highs.AverageByIteration(N)")]
    [Category(categoryCorridor)]
    public int IterationsForCorridorHeights {
      get { return CorridorIterationsOut; }
      set { CorridorIterationsOut = value; }
    }


    [DisplayName("Power Row Offset")]
    [Description("Ex: Speed = Spread / (row + X)")]
    [Category(categoryCorridor)]
    public int PowerRowOffset_ {
      get { return PowerRowOffset; }
      set { PowerRowOffset = value; }
    }


    public double CorridorThinness { get { return CorridorStats == null ? 4 : CorridorStats.Thinness; } }

    private static Func<Rate, double> _GetPriceLow = r => r.AskLow;
    public static Func<Rate, double> GetPriceLow { get { return _GetPriceLow; } }
    private static Func<Rate, double> _GetPriceHigh = r => r.BidHigh;
    public static Func<Rate, double> GetPriceHigh { get { return _GetPriceHigh; } }

    List<Rate> _Rates;
    public List<Rate> Rates {
      get { return _Rates; }
      set {
        _Rates = value;
        if (CorridorStats != null && CorridorStats.Periods > 0) {
          Rates.ToList().ForEach(r => r.PriceAvg1 = r.PriceAvg2 = r.PriceAvg3 = 0);
          CorridorAngle = Rates.Skip(Rates.Count - CorridorStats.Periods)
            .SetCorridorPrices(CorridorStats.HeightUp, CorridorStats.HeightDown, 
            r => r.PriceAvg, r => r.PriceAvg1, (r, d) => r.PriceAvg1 = d, (r, d) => r.PriceAvg2 = d, (r, d) => r.PriceAvg3 = d)[1];
          OnPropertyChanged("CorridorThinness");
          OnPropertyChanged("CorridorHeightsRatio");
          OnPropertyChanged("CorridorHeightByRegressionInPips");
          OnPropertyChanged("CorridorHeightByRegressionInPips0");
          OnPropertyChanged("CorridorToRangeRatio");
        }
        //var dateLast = value.Last().StartDate.AddMinutes(-4);
        RatesLast = value.Skip(value.Count - 3).ToArray();// value.ToArray().SkipWhile(r => r.StartDate < dateLast).ToArray();
        RateLast = RatesLast.DefaultIfEmpty(new Rate()).Last();
        _RateDirection = Rates.Skip(Rates.Count - 2).ToArray();
      }
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

    public Rate[] RatesLast { get; protected set; }
    public Rate[] RatesDirection { get; protected set; }
    public double RateLastAsk { get { return RatesLast.Max(r => r.AskHigh); } }
    public double RateLastBid { get { return RatesLast.Min(r => r.BidLow); } }
    Rate[] _RateDirection;
    public int RateDirection { get { return Math.Sign(_RateDirection[1].PriceAvg - _RateDirection[0].PriceAvg); } }
    public void SetPriceCma(Price price, List<Rate> rates, int calculatedLotSize) {
      //var dir = price.AskChangeDirection + price.BidChangeDirection;
      //if(dir == PriceCmaDirection ) return;
      //PriceCmaDirection = dir;
      CalculatedLotSize = calculatedLotSize;
      Rates = rates;
      PriceCurrent = price;
      if (PriceDigits == 0) PriceDigits = price.Digits;
      var cmaperiod = TicksPerMinuteInstant;
      OnPropertyChanged("OpenSignal");
      //PriceCma = Lib.CMA(PriceCma, 0, cmaperiod , price.Average);
      //PriceCma1 = Lib.CMA(PriceCma1, 0, cmaperiod, PriceCma);
      //PriceCma2 = Lib.CMA(PriceCma2, 0, cmaperiod, PriceCma1);
      //PriceCma3 = Lib.CMA(PriceCma3, 0, cmaperiod, PriceCma2);
    }
    public int PriceDigits { get; set; }
    public string PriceDigitsFormat { get { return "n" + (PriceDigits - 1); } }
    public string PriceDigitsFormat2 { get { return "n" + PriceDigits; } }

    Func<double?, double> _InPips;
    public Func<double?, double> InPips {
      get { return _InPips == null ? d => 0 : _InPips; }
      set { _InPips = value; }
    }
    public double PointSize { get; set; }

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
    Dictionary<Strategies, int[]> strategyScores = new Dictionary<Strategies, int[]>() { 
      { Strategies.Breakout, new int[2] }, { Strategies.Range, new int[2] }, { Strategies.Brange, new int[2] } , { Strategies.Correlation, new int[2] } };
    public string StrategyScoresText {
      get {
        return string.Join(",", strategyScores.Where(sc=>sc.Value.Sum()>0).Select(sc => sc.Key + ":" +
          sc.Value[0] + "/" + sc.Value[1] + "=" + ((double)sc.Value[0] / (sc.Value[0] + sc.Value[1])).ToString("n2")).ToArray());
      }
    }
    public void StrategyScoresReset() { strategyScores.Values.ToList().ForEach(ss => { ss[0] = ss[1] = 0; }); }
    public Trade LastTrade {
      get { return _lastTrade; }
      set {
        if (value == null) return;
        if (value.Id == LastTrade.Id) {
          var id = LastTrade.Id + "";
          if (!string.IsNullOrWhiteSpace(id)) {
            Strategies tradeStrategy = tradeStrategies[id];
            if (strategyScores.ContainsKey(tradeStrategy)) {
              strategyScores[tradeStrategy][0] = strategyScores[tradeStrategy][0] + (LastTrade.PL > 0 ? 1 : 0);
              strategyScores[tradeStrategy][1] = strategyScores[tradeStrategy][1] + (LastTrade.PL > 0 ? 0 : 1);
            }
          }
        } else {
          var strategy = Strategy & (Strategies.Breakout | Strategies.Range | Strategies.Brange | Strategies.Correlation);
          tradeStrategies[value.Id + ""] = strategy;
          if (-LastTrade.PL > AvarageLossInPips / 10) AvarageLossInPips = Lib.CMA(AvarageLossInPips, 0, 10, LastTrade.PL.Abs());

          ProfitCounter = CurrentLoss >= 0 ? 0 : ProfitCounter + (LastTrade.PL > 0 ? 1 : -1);

          _lastTrade = value;
          if (CorridorStats != null) {
            var tu = _lastTrade.InitUnKnown<TradeUnKNown>();
            tu.TradeStats = new TradeStatistics() {
              SessionId = SessionId
            };
          }
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
      if (true) {
        if (trades.Any(t => t.Buy) && trades.Any(t => !t.Buy)) return 0;
        return trades.Sum(t => t.Lots) + LotSize;
      }
      return Math.Min(LastLotSize + LotSize, MaxLotByTakeProfitRatio.ToInt() * LotSize);
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
        if (value.Length > 0) CorridorStats.ResetLock();
      }
    }
    [ReadOnly(true)]
    [DesignOnly(true)]
    public double CorridorToRangeRatio {
      get { try { return CorridorHeightByRegression / BigCorridorHeight; } catch { return 0; } }
    }

    [DisplayName("Corridor To Range Minimum Ratio")]
    [Category("Corridor")]
    public double CorridorToRangeMinimumRatio {
      get { return CorridornessMin; }
      set { CorridornessMin = value; }
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
    public bool IsPowerVolatilityOk {
      get {
        return !TradeByPowerVolatilty ||
          (PowerCurrent > PowerVolatility /*&& powerVolatilityWalker.Diff(PowerVolatility) <= 0*/);
      }
    }
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
  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { StDev = 1, Density = 2 }
  public enum TradeDirections { None,Up, Down }
  [Flags]
  public enum Strategies {
    None = 0, Breakout = 1, Range = 2, Stop = 4, Auto = 8,
    Breakout_A = Breakout + Auto, Range_A = Range + Auto, Momentum = 16, Reverse = 32, Momentum_R = Momentum + Reverse,
    OverPower = 64, Brange = 128,Correlation = 256
  }
}
