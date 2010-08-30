using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Store {
  public class BackTestEventArgs : EventArgs {
    public string Pair { get; set; }
    public DateTime StartDate { get; set; }
    public BackTestEventArgs(string pair, DateTime startDate) {
      this.Pair = pair;
      this.StartDate = startDate;
    }
  }
  public class MasterTradeEventArgs : EventArgs {
    public Trade MasterTrade { get; set; }
    public MasterTradeEventArgs(Trade trade) {
      this.MasterTrade = trade;
    }
  }
}
