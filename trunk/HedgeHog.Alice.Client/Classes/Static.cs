using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Client {
  public class Static {
    public static double GetEntryOrderLimit(ITradesManager tm, Trade[] trades,int lot, bool addProfit, double currentLoss) {
      if (trades.Length == 0) return 0;
      var lotOld = trades.Sum(t => t.Lots);
      currentLoss = currentLoss.Abs();
      var profitInPips = 0.0;
      if (addProfit) {
        if (currentLoss == 0)
          currentLoss = Math.Max(0, trades.Sum(t => t.LimitAmount));
        else
          profitInPips = Math.Max(
            trades.Select(t => t.StopInPips).Where(s => s < 0).OrderBy(s => s).FirstOrDefault().Abs(),
            trades.Select(t => t.LimitInPips).Where(l => l > 0).OrderBy(s => s).FirstOrDefault().Abs()
            ) * lotOld / lot;
      }
      var stopLoss = currentLoss - trades.Sum(t => t.StopAmount);
      return TradesManagedStatic.MoneyAndLotToPips(tm, stopLoss, lot, trades[0].Pair).Abs() + profitInPips;// +curentlimit;
    }
  }
}
