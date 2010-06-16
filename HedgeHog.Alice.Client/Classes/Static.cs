using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Client {
  public class Static {
    public static double GetEntryOrderLimit(FXW fw, Trade[] trades, bool addProfit, double currentLoss) {
      currentLoss = currentLoss.Abs();
      var profitInPips = 0.0;
      if (addProfit) {
        if (currentLoss == 0)
          currentLoss = Math.Max(0, trades.Sum(t => t.LimitAmount));
        else
          profitInPips = Math.Max(
            trades.Select(t => t.StopInPips).Where(s => s < 0).OrderBy(s => s).FirstOrDefault().Abs(),
            trades.Select(t => t.LimitInPips).Where(l => l > 0).OrderBy(s => s).FirstOrDefault().Abs()
            ) / 2;
      }
      var stopLoss = currentLoss - trades.Sum(t => t.StopAmount);
      var lots = trades.Sum(t => t.Lots) * 2;
      return fw.MoneyAndLotToPips(stopLoss, lots, trades[0].Pair).Abs() + profitInPips;// +curentlimit;
    }
  }
}
