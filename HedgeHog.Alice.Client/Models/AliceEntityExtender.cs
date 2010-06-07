using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
        OnPropertyChanged("TradeAmount");
      }
    }

    double _Limit;
    public double Limit {
      get { return _Limit; }
      set {
        if (_Limit == value) return;
        _Limit = value;
        OnPropertyChanged("Limit");
      }
    }

    private double _buyStopByCorridor;

    public double BuyStopByCorridor {
      get { return _buyStopByCorridor; }
      set {
        if (_buyStopByCorridor != value) {
          _buyStopByCorridor = value;
          OnPropertyChanged("BuyStopByCorridor");
        }
      }
    }

    private double _sellStopByCorridor;

    public double SellStopByCorridor {
      get { return _sellStopByCorridor; }
      set {
        if (_sellStopByCorridor != value) {
          _sellStopByCorridor = value;
          OnPropertyChanged("SellStopByCorridor");
        }
      }
    }



    int _currentLot;
    public int CurrentLot {
      get { return _currentLot; }
      set {
        if (_currentLot == value) return;
        _currentLot = value; OnPropertyChanged("CurrentLot");
      }
    }

    public int TradeAmount {
      get { return LotSize * Lots; }
    }

    double _corridornes;

    public double Corridornes {
      get { return _corridornes; }
      set {
        if (_corridornes == value) return;
        _corridornes = value; OnPropertyChanged("Corridornes");
      }
    }

    int _corridorMinutes;

    public int CorridorMinutes {
      get { return _corridorMinutes; }
      set {
        if (_corridorMinutes == value) return;
        _corridorMinutes = value; OnPropertyChanged("CorridorMinutes");
      }
    }

    bool _freezStop;
    public bool FreezStop {
      get { return _freezStop; }
      set {
        if (_freezStop == value) return;
        _freezStop = value; OnPropertyChanged("FreezStop");
      }
    }

    DateTime _lastRateTime;
    public DateTime LastRateTime {
      get { return _lastRateTime; }
      set {
        if (_lastRateTime == value) return;
        _lastRateTime = value;
        OnPropertyChanged("LastRateTime"); }
    }

    public double AngleInRadians { get { return Math.Atan(Angle) * (180 / Math.PI); } }
    double _angle;
    public double Angle {
      get { return _angle; }
      set {
        if (_angle == value) return;
        _angle = value;
        OnPropertyChanged("Angle"); OnPropertyChanged("AngleInRadians"); }
    }

    double _overlap;
    public double Overlap {
      get { return _overlap; }
      set {
        if (_overlap == value) return;
        _overlap = value; 
        OnPropertyChanged("Overlap"); }
    }

    int _overlap5;
    public int Overlap5 {
      get { return _overlap5; }
      set {
        if (_overlap5 == value) return;
        _overlap5 = value; OnPropertyChanged("Overlap5");
      }
    }

    bool _PendingSell;
    public bool PendingSell {
      get { return _PendingSell; }
      set {
        if (_PendingSell == value) return;
        _PendingSell = value; 
        OnPropertyChanged("PendingSell"); }
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
        OnPropertyChanged("BalanceOnStop"); }
    }

    double _balanceOnLimit;
    public double BalanceOnLimit {
      get { return _balanceOnLimit; }
      set {
        if (_balanceOnLimit == value) return;
        _balanceOnLimit = value; 
        OnPropertyChanged("BalanceOnLimit"); }
    }

    double? _net;
    public double? Net { get { return _net; } 
      set {
        if (_net == value) return;
        _net = value; OnPropertyChanged("Net"); } }

    double? _StopAmount;
    public double? StopAmount {
      get { return _StopAmount; }
      set {
        if (_StopAmount == value) return;
        _StopAmount = value; 
        OnPropertyChanged("StopAmount"); }
    }
    double? _LimitAmount;
    public double? LimitAmount {
      get { return _LimitAmount; }
      set {
        if (_LimitAmount == value) return;
        _LimitAmount = value; 
        OnPropertyChanged("LimitAmount"); }
    }

    double? _netInPips;
    public double? NetInPips { get { return _netInPips; } 
      set {
        if (_netInPips == value) return;
        _netInPips = value; 
        OnPropertyChanged("NetInPips"); } }
  }
}
