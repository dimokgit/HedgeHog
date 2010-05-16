using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Client.TradeExtenssions {
  public class TradeUnKNown {
    public bool AutoSync { get; set; }
    public bool IsSyncPending { get; set; }
  }
  public static class TradeExtenssions {
    public static Trade InitUnKnown(this Trade trade, DateTime serverTime) {
      var uk = new TradeUnKNown();
      uk.AutoSync = Math.Abs(trade.PL) < 3 || (serverTime - trade.Time) < TimeSpan.FromSeconds(5);
      trade.UnKnown = uk;
      return trade;
    }
    public static TradeUnKNown GetUnKnown(this Trade trade) { return trade.UnKnown as TradeUnKNown; }
    public static string MasterTradeId(this Trade trade) { return trade.Remark.Remark; }
    public static bool IsPending(this Trade trade) { return trade.Id == trade.MasterTradeId(); }
  }
}
