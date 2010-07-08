using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using HedgeHog.Shared;

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
    public double Limit {
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



    #region CorridorFib
    private double _buyStopByCorridor;
    public double BuyStopByCorridor {
      get { return _buyStopByCorridor; }
      protected set {
        _buyStopByCorridor = value;
        OnPropertyChanged("BuyStopByCorridor");
      }
    }

    private double _sellStopByCorridor;
    public double SellStopByCorridor {
      get { return _sellStopByCorridor; }
      protected set {
        _sellStopByCorridor = value;
        OnPropertyChanged("SellStopByCorridor");
      }
    }

    private double _CorridorFibInstant;
    public double CorridorFibInstant {
      get { return _CorridorFibInstant; }
      set {
        if (_CorridorFibInstant != value) {
          _CorridorFibInstant = value;
          CorridorFib = value;
          OnPropertyChanged("CorridorFibInstant");
        }
      }
    }

    private double _CorridorFib;
    public double CorridorFib {
      get { return _CorridorFib; }
      set {
        if (value != 0 && _CorridorFib != value) {
          //_CorridorFib = Lib.CMA(_CorridorFib, 0, TicksPerMinuteMinimum, Math.Min(99, value.Abs()) * Math.Sign(value));
          _CorridorFib = Lib.CMA(_CorridorFib, 0, TicksPerMinuteMaximun, value);
          CorridorFibAverage = _CorridorFib;
          OnPropertyChanged("CorridorFib");
        }
      }
    }

    private double _CorridorFibAverage;
    public double CorridorFibAverage {
      get { return _CorridorFibAverage; }
      set {
        if (value != 0 && _CorridorFibAverage != value) {
          _CorridorFibAverage = Lib.CMA(_CorridorFibAverage, 0, TicksPerMinuteMaximun, value);
          OnPropertyChanged("CorridorFibAverage");
        }
      }
    }

    Queue<double> bigFibAverage = new Queue<double>(new[]{100.0});
    public void SetCorridorFib(double buyStop, double sellStop) {
      BuyStopByCorridor = buyStop;
      SellStopByCorridor = sellStop;
      CorridorFibInstant = 
        BuyStopByCorridor == 0 ? -bigFibAverage.Average() 
        : SellStopByCorridor == 0 ? bigFibAverage.Average() 
        : Fibonacci.FibRatioSign(BuyStopByCorridor, SellStopByCorridor);
      if (CorridorFibInstant > 100 && CorridorFibInstant != bigFibAverage.Last()) {
        if (bigFibAverage.Count > 20) bigFibAverage.Dequeue();
        bigFibAverage.Enqueue(CorridorFibInstant);
      }
    }
    #endregion

    #region Corridor Stats
    private CorridorStatistics _CorridorStatsForTradeDistance;
    public CorridorStatistics CorridorStatsForTradeDistance {
      get { return _CorridorStatsForTradeDistance; }
      set {
        _CorridorStatsForTradeDistance = value;
        OnPropertyChanged("CorridorStatsForTradeDistance");
      }
    }


    private HedgeHog.Bars.CorridorStatistics _CorridorStats;
    public HedgeHog.Bars.CorridorStatistics CorridorStats {
      get { return _CorridorStats; }
      set {
        _CorridorStats = value;
        Corridornes = CorridorCalcMethod == Models.CorridorCalculationMethod.Density ? _CorridorStats.Density : 1 / _CorridorStats.Density;
        OnPropertyChanged("CorridorStats");
      }
    }


    double _corridornes;
    public double Corridornes {
      get { return _corridornes; }
      set {
        if (_corridornes == value) return;
        _corridornes = value;
        OnPropertyChanged("Corridornes");
        OnPropertyChanged("MinutesBack");
        OnPropertyChanged("IsCorridornessOk");
      }
    }

    public bool IsCorridornessOk {
      get { return Corridornes <= CorridornessMin; }
    }
    #endregion

    public void SetCorrelation(string currency, double correlation) {
      if (Currency1 == currency) Correlation1 = correlation;
      if (Currency2 == currency) Correlation2 = correlation;
    }

    public string Currency1 { get { return Pair.Split('/')[0]; } }
    public string Currency2 { get { return Pair.Split('/')[1]; } }

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
    public int TicksPerMinuteMaximun { get { return new double[] { TicksPerMinute, TicksPerMinuteAverage, TicksPerMinuteInstant }.Max().ToInt(); } }
    public int TicksPerMinuteMinimum { get { return new double[] { TicksPerMinute, TicksPerMinuteAverage, TicksPerMinuteInstant }.Min().ToInt(); } }
    private int _TicksPerMinuteInstant;
    public int TicksPerMinuteInstant {
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
      set {
        TicksPerMinuteInstant = value.ToInt();
        _TicksPerMinute = Lib.CMA(_TicksPerMinute, 0, Math.Max(1, TicksPerMinute.ToInt()), value);
        TicksPerMinuteAverage = _TicksPerMinute;
        OnPropertyChanged("TicksPerMinute");
        OnPropertyChanged("TicksPerMinuteMaximun");
        OnPropertyChanged("TicksPerMinuteMinimum");
      }
    }

    private double _TicksPerMinuteAverage;
    public double TicksPerMinuteAverage {
      get { return _TicksPerMinuteAverage; }
      set {
        _TicksPerMinuteAverage = Lib.CMA(_TicksPerMinuteAverage, 0, Math.Max(1, TicksPerMinuteAverage.ToInt()), value);
        OnPropertyChanged("TicksPerMinuteAverage");
      }
    }
    #endregion

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

    private int _CorridorIterationsTrade;
    public int CorridorIterationsTrade {
      get { return _CorridorIterationsTrade; }
      set {
        if (_CorridorIterationsTrade != value) {
          _CorridorIterationsTrade = value;
          OnPropertyChanged("CorridorIterationsTrade");
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

    private bool? _TradeSignal;
    public bool? TradeSignal {
      get { return _TradeSignal; }
      set {
        if (_TradeSignal != value) {
          _TradeSignal = value;
          OnPropertyChanged("TradeSignal");
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

    public int CorridorIterationsCalc_ { get { return Positions == 1 ? CorridorIterationsOut : CorridorIterationsIn; } }
  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { StDev = 1, Density = 2 }
}
