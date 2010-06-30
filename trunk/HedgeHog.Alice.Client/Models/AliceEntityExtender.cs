using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

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
    public double TakeProfitPips {
      get { return CorridorRatio == 0 ? 0 : Limit / CorridorRatio; }
    }


    private double _buyStopByCorridor;
    public double BuyStopByCorridor {
      get { return _buyStopByCorridor; }
      protected set {
        if (_buyStopByCorridor != value) {
          _buyStopByCorridor = value;
          OnPropertyChanged("BuyStopByCorridor");
        }
      }
    }

    private double _sellStopByCorridor;

    public double SellStopByCorridor {
      get { return _sellStopByCorridor; }
      protected set {
        if (_sellStopByCorridor != value) {
          _sellStopByCorridor = value;
          OnPropertyChanged("SellStopByCorridor");
        }
      }
    }

    int _corrodorCmaPeriodHigh = 10;
    int _corrodorCmaPeriodLow = 10;
    private double _CorridorFibAverage;
    public double CorridorFibAverage {
      get { return _CorridorFibAverage; }
      set {
        if (value != 0 && _CorridorFibAverage != value) {
          _CorridorFibAverage = Lib.CMA(_CorridorFibAverage, double.MinValue, _corrodorCmaPeriodHigh, value);
          OnPropertyChanged("CorridorFibAverage");
        }
      }
    }

    private double _CorridorFib;
    public double CorridorFib {
      get { return _CorridorFib; }
      set {
        if (value!= 0 && _CorridorFib != value) {
          _CorridorFib = Lib.CMA(_CorridorFib, double.MinValue, _corrodorCmaPeriodLow, Math.Min(99, value.Abs()) * Math.Sign(value));
          CorridorFibAverage = _CorridorFib;
          OnPropertyChanged("CorridorFib");
        }
      }
    }



    public void SetCorridorFib(double buyStop, double sellStop) {
      BuyStopByCorridor = buyStop;
      SellStopByCorridor = sellStop;
      CorridorFib = BuyStopByCorridor == 0 || SellStopByCorridor == 0 ? 0 : Fibonacci.FibRatioSign(BuyStopByCorridor, SellStopByCorridor);
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

    private HedgeHog.Bars.CorridorStatistics _CorridorStats;
    public HedgeHog.Bars.CorridorStatistics CorridorStats {
      get { return _CorridorStats; }
      set {
        if (_CorridorStats != value) {
          _CorridorStats = value;
          Corridornes = CorridorCalcMethod == Models.CorridorCalculationMethod.Density ? _CorridorStats.Density : 1 / _CorridorStats.Density;
          OnPropertyChanged("CorridorStats");
        }
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

    private double _TicksPerMinute;
    public double TicksPerMinute {
      get { return _TicksPerMinute; }
      set {
        if (_TicksPerMinute != value) {
          _TicksPerMinute = value;
          TicksPerMinuteAverage = value;
          OnPropertyChanged("TicksPerMinute");
        }
      }
    }

    private double _TicksPerMinuteAverage;
    public double TicksPerMinuteAverage {
      get { return _TicksPerMinuteAverage; }
      set {
        _TicksPerMinuteAverage = Lib.CMA(_TicksPerMinuteAverage, 0, Math.Max(1, value.ToInt()), value);
        OnPropertyChanged("TicksPerMinuteAverage");
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


    public int CorridorIterationsCalc { get { return Positions == 1 ? CorridorIterationsOut : CorridorIterationsIn; } }
  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { StDev = 1, Density = 2 }
}
