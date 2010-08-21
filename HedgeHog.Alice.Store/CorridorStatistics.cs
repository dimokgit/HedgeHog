using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Client {
  public class CorridorStatistics:CorridorStatisticsBase{
    public Store.TradingMacro TradingMacro { get; set; }
    public CorridorStatistics(Store.TradingMacro tradingMacro) {
      this.TradingMacro = tradingMacro;
    }
    public bool IsCorridornessOk {
      get { return Corridornes <= TradingMacro.CorridornessMin; }
    }
    bool? _TradeSignal;
    public bool? TradeSignal {
      get {
        var fibInstant = CorridorFibInstant.Round(1);
        var fib = CorridorFib.Round(1);
        var fibAvg = CorridorFibAverage.Round(1);
        #region Trade Signals
        //Func<bool?> tradeSignal1 = () => {
        //  return fibAvg < -FibMinimum && fib > fibAvg /*&& fibInstant < fib*/ ? true :
        //    fibAvg > +FibMinimum && fib < fibAvg /*&& fibInstant > fib*/ ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal2 = () => {
        //  return fibInstant < -FibMinimum && fib > fibAvg && fibInstant < fib && fib < 0 ? true :
        //         fibInstant > +FibMinimum && fib < fibAvg && fibInstant > fib && fib > 0 ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal3 = () => {
        //  var isFibAvgOk = fibAvg.Abs() > FibMinimum / 2;
        //  return fib > fibAvg && fibInstant < fib && fibAvg < 0 && isFibAvgOk ? true :
        //         fib < fibAvg && fibInstant > fib && fibAvg > 0 && isFibAvgOk ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal4 = () => {
        //  var isFibAvgOk = fibAvg.Abs() >= FibMinimum && fib.Abs() <= FibMinimum;
        //  return fib > fibAvg && (fibInstant < 0 && fibAvg < 0) && isFibAvgOk ? true :
        //         fib < fibAvg && (fibInstant > 0 && fibAvg > 0) && isFibAvgOk ? false :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal5 = () => {
        //  if (TradingMacro.PriceCmaCounter < TradingMacro.TicksPerMinuteMaximun * 2) return null;
        //  //if (!TradingMacro.IsSpeedOk) return null;
        //  var pdp23 = TradingMacro.PriceCma23DiffernceInPips;
        //  var pdp = TradingMacro.PriceCmaDiffernceInPips;
        //  return PriceCmaDiffHigh > 0 && pdp < 0 && pdp23 < 0 ? false :
        //         PriceCmaDiffLow < 0 && pdp > 0 && pdp23 > 0 ? true :
        //    (bool?)null;
        //};
        //Func<bool?> tradeSignal6 = () => {
        //  if (!IsCorridorAvarageHeightOk) return null;
        //  //if (TradingMacro.PriceCmaCounter < TradingMacro.TicksPerMinuteMaximun * 2) return null;
        //  var pdhFirst = TradingMacro.PriceCmaDiffHighWalker.CmaArray.First();
        //  var pdhLast = TradingMacro.PriceCmaDiffHighWalker.CmaArray.Last();
        //  var pdlFirst = TradingMacro.PriceCmaDiffLowWalker.CmaArray.First();
        //  var pdlLast = TradingMacro.PriceCmaDiffLowWalker.CmaArray.Last();
        //  return (pdhFirst > 0 || pdhLast > 0) && pdhFirst <= pdhLast ? false :
        //         (pdlFirst < 0 || pdlLast < 0) && pdlFirst >= pdlLast ? true :
        //    (bool?)null;
        //};
        #endregion
        Func<bool?> tradeSignal7 = () => {
          if (!IsCorridorAvarageHeightOk) return null;
          var doSell = PriceCmaDiffHigh > 0;// || TradingMacro.BarHeight60 > 0 && PriceCmaDiffLow >= TradingMacro.BarHeight60;
          var doBuy = PriceCmaDiffLow < 0;// || TradingMacro.BarHeight60 > 0 && (-PriceCmaDiffHigh) >= TradingMacro.BarHeight60;
          return doSell == doBuy ? (bool?)null : doBuy;
        };
        var ts = tradeSignal7();
        if (ts != _TradeSignal)
          OnPropertyChanged("TradeSignal");
        _TradeSignal = ts;
        return _TradeSignal;
      }
    }

    double priceCmaForAverageHigh { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.PriceCurrent.Ask; } }
    double priceCmaForAverageLow { get { return TradingMacro.PriceCurrent == null ? 0 : TradingMacro.PriceCurrent.Bid; } }

    public double PriceCmaDiffHigh { get { return priceCmaForAverageHigh - AverageHigh; } }
    public double PriceCmaDiffLow { get { return priceCmaForAverageLow - AverageLow; } }

    public bool IsCorridorAvarageHeightOk {
      get {
        var addOn = PriceCmaDiffHigh > 0
          ? TradingMacro.PriceCmaDiffHighFirst + TradingMacro.PriceCmaDiffHighLast
          : PriceCmaDiffLow < 0 ? -TradingMacro.PriceCmaDiffLowFirst - TradingMacro.PriceCmaDiffLowLast
          : 0;
        return (AverageHeight + Math.Max(0, addOn)) / TradingMacro.BarHeight60 > 1;
      }
    }
  }
}
