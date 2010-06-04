using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;

namespace HedgeHog.Alice.Client.TradeExtenssions {
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
    public string ErrorMessage { get; set; }
  }
  public class TradeUnKNown : UnKnownAliceBase {
    public Trade MasterTrade { get; set; }
    public bool AutoSync { get; set; }
    public bool SyncStop { get; set; }
    public bool SyncLimit { get; set; }
    public bool IsSyncPending { get; set; }

  }

  public class OrderUnKnown : UnKnownAliceBase {
  }
  public static class TradeExtenssions {
    public static Trade InitUnKnown(this Trade trade, DateTime serverTime) {
      var uk = new TradeUnKNown();
      uk.AutoSync = Math.Abs(trade.PL) <= Config.PipsDifferenceToSync || (serverTime - trade.Time) < TimeSpan.FromSeconds(Config.SecondsDifferenceToSync);
      trade.UnKnown = uk;
      return trade;
    }
    public static TradeUnKNown GetUnKnown(this Trade trade) {
      if (trade.UnKnown == null) trade.InitUnKnown(DateTime.MinValue);
      return trade.UnKnown as TradeUnKNown;
    }
    public static string MasterTradeId(this Trade trade) { return trade.Remark.Remark; }
    public static bool IsPending(this Trade trade) { return trade.Id == trade.MasterTradeId(); }
    public static string ErrorMessage(this Trade trade) { return trade.GetUnKnown().ErrorMessage; }

    public static OrderUnKnown GetUnKnown(this Order order) {
      return order.UnKnown as OrderUnKnown;
    }
  }
}
