using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.DB {
  public partial class ForexEntities {
    //public TradeDirections GetTradeDirection_(DateTime today, string pair, int maPeriod, out DateTime dateClose) {
    //  today = today.AddDays(-1);
    //  var bars = this.t_Bar.Where(b => b.Pair == pair && b.Period == 24 && b.StartDate <= today).OrderByDescending(b => b.StartDate).Take(maPeriod + 1).ToArray();
    //  int outBegIdx, outNBElement;
    //  double[] outRealBig = new double[20];
    //  double[] outRealSmall = new double[20];
    //  Func<t_Bar, double> value = b => new[] { b.AskOpen + b.BidOpen + b.AskClose + b.BidClose }.Average();
    //  var barValues = bars.OrderBy(b => b.StartDate).Select(b => value(b)).ToArray();
    //  TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealBig);
    //  barValues = barValues.Skip((barValues.Length * .75).ToInt()).ToArray();
    //  TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealSmall);
    //  var lastBar = bars.OrderBy(b => b.StartDate).Last();
    //  dateClose = lastBar.StartDate.AddDays(1);
    //  return value(lastBar) > outRealBig[0] && value(lastBar) > outRealSmall[0]
    //    ? TradeDirections.Up
    //    : value(lastBar) < outRealBig[0] && value(lastBar) < outRealSmall[0]
    //    ? TradeDirections.Down : TradeDirections.None;
    //}
    //public TradeDirections GetTradeDirection(DateTime today, string pair, int maPeriod, out DateTime dateClose) {
    //  var period = 60;
    //  var bars = this.BarsByMinutes(pair, (byte)period, today, 24, maPeriod).ToArray();
    //  int outBegIdx, outNBElement;
    //  double[] outRealBig = new double[20];
    //  double[] outRealSmall = new double[20];
    //  Func<BarsByMinutes_Result, double> value = b => new[] { b.AskOpen + b.BidOpen + b.AskClose + b.BidClose }.Average().Value;
    //  var barValues = bars.OrderBy(b => b.DateOpen).Select(b => value(b)).ToArray();
    //  TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealBig);
    //  barValues = barValues.Skip((barValues.Length * .75).ToInt()).ToArray();
    //  TicTacTec.TA.Library.Core.Trima(0, barValues.Count() - 1, barValues, barValues.Count(), out outBegIdx, out outNBElement, outRealSmall);
    //  var lastBar = bars.OrderBy(b => b.DateOpen).Last();
    //  dateClose = lastBar.DateClose.Value.AddMinutes(period);
    //  return value(lastBar) > outRealBig[0] && value(lastBar) > outRealSmall[0]
    //    ? TradeDirections.Up
    //    : value(lastBar) < outRealBig[0] && value(lastBar) < outRealSmall[0]
    //    ? TradeDirections.Down : TradeDirections.None;
    //}

  }
}
