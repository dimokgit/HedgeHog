﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using HedgeHog.Shared;
using System.Collections.ObjectModel;

namespace HedgeHog.Alice.Client.Models {
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
        InitGuidField<Models.TradingAccount>(ta => ta.Id, (ta, g) => ta.Id = g);
        InitGuidField<Models.TradingMacro>(ta => ta.UID, (ta, g) => ta.UID = g);
      } catch { }
      return base.SaveChanges(options);
    }

    private void InitGuidField<TEntity>(Func<TEntity,Guid> getField, Action<TEntity,Guid> setField) {
      var d = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Added)
        .Select(o => o.Entity).OfType<TEntity>().Where(e =>getField(e)  == new Guid()).ToList();
      d.ForEach(e => setField(e, Guid.NewGuid()));
    }
  }

  public partial class ClosedTrade {
    public double NetPL { get { return GrossPL - Commission; } }
  }

  public partial class TradingMacro {

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

    double _Limit;
    double Limit {
      get { return _Limit; }
      set {
        if (_Limit == value) return;
        _Limit = value;
        OnPropertyChanged("Limit");
        OnPropertyChanged("TakeProfitPips");
      }
    }

    //public double TakeProfitPips { get { return CorridorRatio == 0 ? 0 : Limit / CorridorRatio; } }
    private double _TakeProfitPips;
    public double TakeProfitPips {
      get { return _TakeProfitPips; }
      set {
        if (_TakeProfitPips != value) {
          _TakeProfitPips = value;
          OnPropertyChanged("TakeProfitPips");
          OnPropertyChanged("IsTakeProfitPipsMinimumOk");
        }
      }
    }



    #region Corridor Stats

    private int[] _CorridorIterationsArray;
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
      if (iterations <= 0) return CorridorStatsArray.OrderBy(c => c.Iterations).Take(-iterations+1).Last();
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
        return _CorridorStatsArray; }
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
      }
    }

    Trade[] emptyTrades = new Trade[] { };
    public Trade[] CloseTrades(Trade[] trades) {
      if (CorridorStats == null) return emptyTrades;
      if (PriceCmaCounter < TicksPerMinuteMaximun * 2) return emptyTrades;

      Func<bool, Trade[]> plTrades = buy => trades.Where(t => t.IsBuy == buy && t.PL > 0).ToArray();
      var isBuy = PriceCmaDiffernceInPips < 0;
      var ts = trades.Where(t => t.IsBuy == isBuy && t.PL > 0).ToArray();
      return ts.Length > 1 ? ts : emptyTrades;
    }
    public bool? CloseTrades_ {
      get {
        var csTS = CorridorStatsArray.FirstOrDefault(cs => cs.Height > CorridorHeightMinimum && cs.TradeSignal.HasValue);
        return csTS == null ? null : csTS.TradeSignal;
      }
    }
    #endregion

    public void SetCorrelation(string currency, double correlation) {
      if (Currency1 == currency) Correlation1 = correlation;
      if (Currency2 == currency) Correlation2 = correlation;
    }

    public string Currency1 { get { return (Pair+"").Split('/').DefaultIfEmpty("").ToArray()[0]; } }
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
        if (period <= 1) period = (priceStackByPair.Count * period).ToInt();
        return priceStackByPair.Take(period.ToInt());
      }
      public void Add(Price price, DateTime serverTime) {
        var queue = priceStackByPair;
        if ((price.Time - serverTime).Duration() < TimeSpan.FromMinutes(1)) {
          if (queue.Count > maxCount) queue.Dequeue();
          queue.Enqueue(price);
        }
      }
      public double TickPerMinute(double period) {
        return TickPerMinute(GetQueue(period));
      }

      public DateTime LastTickTime() { return priceStackByPair.Count == 0 ? DateTime.MaxValue : priceStackByPair.Max(p=>p.Time); }
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
    public void TicksPerMinuteSet(Price price, DateTime serverTime, Func<double, double> inPips) {
      if (_InPips == null) _InPips = inPips;
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
      OnPropertyChanged("PriceCmaDiffHighFirstInPips");
      OnPropertyChanged("PriceCmaDiffHighLastInPips");

      OnPropertyChanged("PriceCmaDiffLowInPips");
      OnPropertyChanged("PriceCmaDiffLowFirstInPips");
      OnPropertyChanged("PriceCmaDiffLowLastInPips");

      OnPropertyChanged("CorridorAverageHeightInPips");
    }
    #endregion

    public double PipsPerMinute { get { return InPips == null?0: InPips(PriceQueue.Speed(.25)); } }
    public double PipsPerMinuteCmaFirst { get { return InPips == null ? 0 : InPips(PriceQueue.Speed(.5)); } }
    public double PipsPerMinuteCmaLast { get { return InPips == null ? 0 : InPips(PriceQueue.Speed(1)); } }

    public bool IsSpeedOk { get { return PipsPerMinute < Math.Max(PipsPerMinuteCmaFirst, PipsPerMinuteCmaLast); } }

    public double CorridorAverageHeightInPips { get { return CorridorStats == null?0: InPips(CorridorStats.AverageHeight); } }
    public double CorridorAverageHeight { get { return CorridorStats == null ? 0 : CorridorStats.AverageHeight; } }
    public double PriceCmaDiffHigh { get { return CorridorStats == null ? 0 : CorridorStats.PriceCmaDiffHigh; } }
    public double PriceCmaDiffHighInPips { get { return InPips(PriceCmaDiffHigh); } }
    public double PriceCmaDiffLow { get { return CorridorStats == null ? 0 : CorridorStats.PriceCmaDiffLow; } }
    public double PriceCmaDiffLowInPips { get { return InPips(PriceCmaDiffLow); } }

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

    private int _Positions;
    public int Positions {
      get { return _Positions; }
      set {
        if (_Positions != value) {
          _Positions = value;
          OnPropertyChanged("Positions");
          OnPropertyChanged("CorridorIterationsCalc");
        }
      }
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

    public double CorridorFibMax(int index) { return double.Parse(corridorFibMax[index]); }
    public string[] corridorFibMax {
      get {
        var ms = FibMax.Split(',');
        return new[] { ms.Take(1).First(), ms.Take(2).Last(), ms.Take(3).Last(), ms.Take(4).Last() };
      }
    }

    double _BarHeight60;
    public double BarHeight60 {
      get { return _BarHeight60; }
      set { 
        _BarHeight60 = value;
        OnPropertyChanged("BarHeight60InPips");
        OnPropertyChanged("IsCorridorAvarageHeightOk");
      }
    }
    public double BarHeight60InPips { get { return InPips(BarHeight60); } }

    public double TakeProfitPipsMinimum { get { return FibMin; } }
    public bool IsTakeProfitPipsMinimumOk { get { return CorridorStats == null ? false : TakeProfitPips >= TakeProfitPipsMinimum; } }

    public bool IsCorridorAvarageHeightOk { get { return CorridorStats == null ? false : CorridorStats.IsCorridorAvarageHeightOk; } }

    public double CorridorHeightMinimum { get; set; }

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

    private double _PriceCma;
    public double PriceCma {
      get { return PriceCmaWalker.CmaArray[0].Value; }
    }
    private double _PriceCma1;
    public double PriceCma1 {
      get { return _PriceCma1; }
      set {
        if (_PriceCma1 != value) {
          _PriceCma1 = value;
          OnPropertyChanged("PriceCma1");
        }
      }
    }

    private double _PriceCma2;
    public double PriceCma2 {
      get { return _PriceCma2; }
      set {
        _PriceCma2 = value;
        OnPropertyChanged("PriceCma2");
      }
    }

    private double _PriceCma3;
    public double PriceCma3 {
      get { return _PriceCma3; }
      set {
        if (_PriceCma3 != value) {
          _PriceCma3 = value;
          OnPropertyChanged("PriceCma3");
          OnPropertyChanged("PriceCmaDiffernceInPips");
          OnPropertyChanged("PriceCma1DiffernceInPips");
          OnPropertyChanged("PriceCma23DiffernceInPips");
        }
      }
    }

    bool? _TradeSignal;

    public bool? TradeSignal {
      get {
        return CorridorStats == null?null: CorridorStats.TradeSignal;
      }
    }

    public double PriceCmaDiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCmaWalker.CmaDiff().FirstOrDefault()), 2); } }
    //public double PriceCma1DiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCma1 - PriceCma2), 2); } }
    public double PriceCma1DiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCmaWalker.CmaDiff().Skip(1).DefaultIfEmpty(PriceCmaWalker.CmaDiff().FirstOrDefault()).First()), 2); } }
    //public double PriceCma23DiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCma2 - PriceCma3), 2); } }
    //public double PriceCma23DiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCmaWalker.FromEnd(1) - PriceCmaWalker.FromEnd(0)), 2); } }
    public double PriceCma23DiffernceInPips { get { return Math.Round(InPips(PriceCmaWalker.CmaDiff().LastOrDefault()), 2); } }

    public double PriceCmaDiffHighFirstInPips { get { return InPips(PriceCmaDiffHighFirst); } }
    public double PriceCmaDiffHighLastInPips { get { return InPips(PriceCmaDiffHighLast); } }
    public double PriceCmaDiffHighFirst { get { return PriceCmaDiffHighWalker.CmaArray.FirstOrDefault().GetValueOrDefault(); } }
    public double PriceCmaDiffHighLast { get { return PriceCmaDiffHighWalker.CmaArray.LastOrDefault().GetValueOrDefault(); } }

    public double PriceCmaDiffLowFirstInPips { get { return InPips(PriceCmaDiffLowFirst); } }
    public double PriceCmaDiffLowLastInPips { get { return InPips(PriceCmaDiffLowLast); } }
    public double PriceCmaDiffLowFirst { get { return PriceCmaDiffLowWalker.CmaArray.FirstOrDefault().GetValueOrDefault(); } }
    public double PriceCmaDiffLowLast { get { return PriceCmaDiffLowWalker.CmaArray.LastOrDefault().GetValueOrDefault(); } }

    int _priceCmaCounter;
    public int PriceCmaCounter {
      get { return _priceCmaCounter; }
      protected set { _priceCmaCounter = value; }
    }

    public int PriceCmaLevel { get { return CorridorIterationsIn; } }
    HedgeHog.Lib.CmaWalker _PriceCmaWalker;
    public HedgeHog.Lib.CmaWalker PriceCmaWalker {
      get {
        if (_PriceCmaWalker == null) _PriceCmaWalker = new HedgeHog.Lib.CmaWalker(PriceCmaLevel);
        return _PriceCmaWalker;
      }
    }

    HedgeHog.Lib.CmaWalker _PriceCmaDiffHighWalker;
    public HedgeHog.Lib.CmaWalker PriceCmaDiffHighWalker {
      get {
        if (_PriceCmaDiffHighWalker == null) _PriceCmaDiffHighWalker = new HedgeHog.Lib.CmaWalker(PriceCmaLevel);
        return _PriceCmaDiffHighWalker;
      }
    }
    HedgeHog.Lib.CmaWalker _PriceCmaDiffLowWalker;
    public HedgeHog.Lib.CmaWalker PriceCmaDiffLowWalker {
      get {
        if (_PriceCmaDiffLowWalker == null) _PriceCmaDiffLowWalker = new HedgeHog.Lib.CmaWalker(PriceCmaLevel);
        return _PriceCmaDiffLowWalker;
      }
    }

    public Price PriceCurrent { get; set; }

    int _PriceCmaDirection;

    public int PriceCmaDirection {
      get { return _PriceCmaDirection; }
      set { _PriceCmaDirection = value; }
    }
    public void SetPriceCma(Price price) {
      //var dir = price.AskChangeDirection + price.BidChangeDirection;
      //if(dir == PriceCmaDirection ) return;
      //PriceCmaDirection = dir;
      PriceCurrent = price;
      PriceCmaCounter++;
      if (PriceDigits == 0) PriceDigits = price.Digits;
      var cmaperiod = TicksPerMinuteInstant;
      PriceCmaWalker.Add(price.Average, cmaperiod);
      PriceCmaDiffHighWalker.Add(PriceCmaDiffHigh, cmaperiod);
      PriceCmaDiffLowWalker.Add(PriceCmaDiffLow, cmaperiod);
      OnPropertyChanged("TradeSignal");
      OnPropertyChanged("PriceCmaDiffernceInPips");
      OnPropertyChanged("PriceCma1DiffernceInPips");
      OnPropertyChanged("PriceCma23DiffernceInPips");
      //PriceCma = Lib.CMA(PriceCma, 0, cmaperiod , price.Average);
      //PriceCma1 = Lib.CMA(PriceCma1, 0, cmaperiod, PriceCma);
      //PriceCma2 = Lib.CMA(PriceCma2, 0, cmaperiod, PriceCma1);
      //PriceCma3 = Lib.CMA(PriceCma3, 0, cmaperiod, PriceCma2);
    }
    public int PriceDigits { get; set; }
    public string PriceDigitsFormat { get { return "n" + (PriceDigits-1); } }
    public string PriceDigitsFormat2 { get { return "n" + PriceDigits; } }

    Func<double, double> _InPips;
    public Func<double, double> InPips {
      get { return _InPips == null ? d => 0 : _InPips; }
      set { _InPips = value; }
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

    int _LastLotSize;
    public int LastLotSize {
      get { return _LastLotSize; }
      set {
        if (_LastLotSize == value) return;
        _LastLotSize = value;
        OnPropertyChanged("LastLotSize");
      }
    }
  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { StDev = 1, Density = 2 }
}
