using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Store {
  public class BackTestEventArgs : EventArgs {
    public string Pair { get; set; }
    public DateTime StartDate { get; set; }
    public int MonthToTest { get; set; }
    public double Delay { get; set; }
    public bool Pause { get; set; }
    public bool StepBack { get; set; }
    public BackTestEventArgs() { }
    public BackTestEventArgs(string pair, DateTime startDate, int monthsToTest, double delay,bool pause) {
      this.Pair = pair;
      this.StartDate = startDate;
      this.MonthToTest = monthsToTest;
      this.Delay = delay;
      this.Pause = pause;
    }
  }
  public class MasterTradeEventArgs : EventArgs {
    public Trade MasterTrade { get; set; }
    public MasterTradeEventArgs(Trade trade) {
      this.MasterTrade = trade;
    }
  }
}
