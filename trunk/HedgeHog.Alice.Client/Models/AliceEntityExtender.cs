using System;
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
        }
      }
    }



    #region Corridor Stats

    private int[] _CorridorIterationsArray;
    public int[] CorridorIterationsArray {
      get { return CorridorIterations.Split(',').Select(s => int.Parse(s)).ToArray(); }
      set {
        OnPropertyChanged("CorridorIterationsArray");
      }
    }


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

    public bool? CloseTrades {
      get {
        var csTS = CorridorStatsArray.FirstOrDefault(cs => cs.Height > CorridorHeighMinimum && cs.TradeSignal.HasValue);
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
    private double _TicksPerMinuteInstant;
    public double TicksPerMinuteInstant {
      get { return _TicksPerMinuteInstant; }
      set {
        if (_TicksPerMinuteInstant != value) {
          _TicksPerMinuteInstant = value;
          OnPropertyChanged("TicksPerMinuteInstant");
        }
      }
    }

    private double _TicksPerMinute;
    public double TicksPerMinute {
      get { return _TicksPerMinute; }
      protected set {
        _TicksPerMinute = value;
        OnPropertyChanged("TicksPerMinute");
      }
    }

    private double _TicksPerMinuteAverage;
    public double TicksPerMinuteAverage {
      get { return _TicksPerMinuteAverage; }
      protected set {
        _TicksPerMinuteAverage = value;
        OnPropertyChanged("TicksPerMinuteAverage");
      }
    }
    public void TicksPerMinuteSet(double ticksPerMinuteInst, double ticksPerMinuteFast, double ticksPerMinuteSlow) {
      TicksPerMinuteInstant = ticksPerMinuteInst;
      TicksPerMinute = ticksPerMinuteFast;
      TicksPerMinuteAverage = ticksPerMinuteSlow;
      OnPropertyChanged("TicksPerMinuteMaximun");
      OnPropertyChanged("TicksPerMinuteMinimum");
      OnPropertyChanged("IsTicksPerMinuteOk");
    }
    #endregion

    private double _BarHeight60InPips;
    public double BarHeight60InPips {
      get { return _BarHeight60InPips; }
      set {
        if (_BarHeight60InPips != value) {
          _BarHeight60InPips = value;
          OnPropertyChanged("BarHeight60InPips");
        }
      }
    }


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

    private double _TradeDistance;
    public double TradeDistance {
      get { return _TradeDistance; }
      set {
        if (_TradeDistance != value) {
          _TradeDistance = value;
          OnPropertyChanged("TradeDistance");
        }
      }
    }

    class TradeHistory {
      public DateTime Time { get; set; }
      public Trade Trade { get; set; }
      public TradeHistory() { }
      public TradeHistory(DateTime time, Trade trade) {
        this.Time = time;
        this.Trade = trade;
      }
    }

    Queue<TradeHistory> tradesQueue = new Queue<TradeHistory>();
    public double CorridorFibMax(int index) { return double.Parse(corridorFibMax[index]); }
    public string[] corridorFibMax {
      get {
        var ms = FibMax.Split(',');
        return new[] { ms.Take(1).First(), ms.Take(2).Last(), ms.Take(3).Last(), ms.Take(4).Last() };
      }
    }

    public void TradesToHistory_Clear() {
      tradesQueue.Clear();
    }
    public void TradesToHistory_Add(Trade[] trades) {
      var now = DateTime.Now;
      while (tradesQueue.Count() > 0 && (now - tradesQueue.Peek().Time).Duration() > TimeSpan.FromMinutes(Overlap))
        tradesQueue.Dequeue();
      foreach (var trade in trades)
        tradesQueue.Enqueue(new TradeHistory(now, trade));
    }

    public Trade[] MaxPLTrade(bool isBuy) {
      return tradesQueue.Where(t => t.Trade.IsBuy == isBuy && t.Trade.PL > 0).Select(th=>th.Trade).ToArray();
    }

    public double BarHeight60 { get; set; }


    public double CorridorHeighMinimum { get; set; }

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
      get { return _PriceCma; }
      set {
        _PriceCma = value;
        OnPropertyChanged("PriceCma");
      }
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


    public double PriceCmaDiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCma - PriceCma2), 2); } }
    public double PriceCma1DiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCma1 - PriceCma2), 2); } }
    public double PriceCma23DiffernceInPips { get { return InPips == null ? 0 : Math.Round(InPips(PriceCma2 - PriceCma3), 2); } }

    int _priceCmaCounter;

    public int PriceCmaCounter {
      get { return _priceCmaCounter; }
      protected set { _priceCmaCounter = value; }
    }
    public void SetPriceCma(Price price) {
      PriceCmaCounter++;
      if (PriceDigits == 0) PriceDigits = price.Digits;
      PriceCma = Lib.CMA(PriceCma, 0, TicksPerMinuteMaximun, price.Average);
      PriceCma1 = Lib.CMA(PriceCma1, 0, TicksPerMinuteMaximun, PriceCma);
      PriceCma2 = Lib.CMA(PriceCma2, 0, TicksPerMinuteMaximun, PriceCma1);
      PriceCma3 = Lib.CMA(PriceCma3, 0, TicksPerMinuteMaximun, PriceCma2);
    }
    public int PriceDigits { get; set; }
    public string PriceDigitsFormat { get { return "n" + (PriceDigits-1); } }
    public string PriceDigitsFormat2 { get { return "n" + PriceDigits; } }

    Func<double, double> _InPips = null;
    public Func<double, double> InPips {
      get { return _InPips; }
      set { _InPips = value; }
    }

  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { StDev = 1, Density = 2 }
}
