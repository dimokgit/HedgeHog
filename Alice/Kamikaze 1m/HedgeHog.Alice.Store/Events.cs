﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace HedgeHog.Alice.Store {
  public class BackTestEventArgs : EventArgs {
    public DateTime StartDate { get; set; }
    public int MonthToTest { get; set; }
    public double Delay { get; set; }
    public bool Pause { get; set; }
    public bool StepBack { get; set; }
    public bool ClearTest { get; set; }
    public BackTestEventArgs() { }
    public BackTestEventArgs( DateTime startDate, int monthsToTest, double delay, bool pause, bool clearTest) {
      this.StartDate = startDate;
      this.MonthToTest = monthsToTest;
      this.Delay = delay;
      this.Pause = pause;
      this.ClearTest = clearTest;
    }
  }
  public class MasterTradeEventArgs : EventArgs {
    public Trade MasterTrade { get; set; }
    public MasterTradeEventArgs(Trade trade) {
      this.MasterTrade = trade;
    }
  }

  public class TradingStatisticsEventArgs : EventArgs {
    public TradingStatistics TradingStatistics { get; set; }
    public TradingStatisticsEventArgs(TradingStatistics tradingStatistics=null) {
      this.TradingStatistics = tradingStatistics;
    }
  }

}
