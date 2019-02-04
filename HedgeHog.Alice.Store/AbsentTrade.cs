using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  public abstract class UnKnownAliceBase : UnKnownBase {
    double _BalanceOnStop;
    public double BalanceOnStop {
      get { return _BalanceOnStop; }
      set { _BalanceOnStop = value; OnPropertyChanged("BalanceOnStop"); }
    }
    double _BalanceOnLimit;
    public double BalanceOnLimit {
      get { return _BalanceOnLimit; }
      set { _BalanceOnLimit = value; OnPropertyChanged("BalanceOnLimit"); }
    }
  }
  public class TradeUnKNown : UnKnownAliceBase {
    public Trade MasterTrade { get; set; }
    public bool AutoSync { get; set; }
    public bool SyncStop { get; set; }
    public bool SyncLimit { get; set; }
    public bool IsSyncPending { get; set; }
    public TradeStatistics TradeStats { get; set; }
    public Guid SessionId { get; set; }
    public string SessionInfo { get; set; }
    public TradeStatistics InitTradeStatistics(TradeStatistics tradesStatistics = null) {
      //if (tradesStatistics != null && this.TradeStats != null)
      //  throw new InvalidOperationException("TradeStats member is already set.");
      TradeStats = tradesStatistics ?? new TradeStatistics();
      return TradeStats;
    }
  }

  public class OrderUnKnown : UnKnownAliceBase {
    private double _NoLossLimit;
    public double NoLossLimit {
      get { return _NoLossLimit; }
      set {
        if (_NoLossLimit != value) {
          _NoLossLimit = value;
          OnPropertyChanged("NoLossLimit");
        }
      }
    }
    private double _PercentOnStop;
    public double PercentOnStop {
      get { return _PercentOnStop; }
      set {
        if (_PercentOnStop != value) {
          _PercentOnStop = value;
          OnPropertyChanged("PercentOnStop");
        }
      }
    }

    private double _PercentOnLimit;
    public double PercentOnLimit {
      get { return _PercentOnLimit; }
      set {
        if (_PercentOnLimit != value) {
          _PercentOnLimit = value;
          OnPropertyChanged("PercentOnLimit");
        }
      }
    }


  }
}
