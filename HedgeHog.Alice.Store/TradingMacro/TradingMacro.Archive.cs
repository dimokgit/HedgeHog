﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    public double CalculateLastPrice(Rate rate, Func<Rate, double> price) {
      try {
        if (TradesManager.IsInTest || IsInPlayback) return price(rate);
        var secondsPerBar = BarPeriodInt * 60;
        var secondsCurrent = (TradesManager.ServerTime - rate.StartDate).TotalSeconds;
        var ratio = secondsCurrent / secondsPerBar;
        var ratePrev = RatesArray.Previous(rate);
        var priceCurrent = price(rate);
        var pricePrev = price(ratePrev);
        return pricePrev * (1 - ratio).Max(0) + priceCurrent * ratio.Min(1);
      } catch (Exception exc) {
        Log = exc;
        return double.NaN;
      }
    }

    public double CorridorCrossHighPrice(Rate rate, Func<Rate, double> getPrice = null) {
      return CalculateLastPrice(rate, getPrice ?? CorridorCrossGetHighPrice());
    }
    public Func<Rate, double> CorridorCrossGetHighPrice() {
      return CorridorHighPrice(CorridorCrossHighLowMethod);
    }
    public Func<Rate, double> CorridorGetHighPrice() {
      return CorridorHighPrice(CorridorHighLowMethod);
    }
    private Func<Rate, double> CorridorHighPrice(CorridorHighLowMethod corridorHighLowMethod) {
      switch (corridorHighLowMethod) {
        case CorridorHighLowMethod.AskHighBidLow: return r => r.AskHigh;
        case CorridorHighLowMethod.AskLowBidHigh: return r => r.AskLow;
        case CorridorHighLowMethod.BidHighAskLow: return r => r.BidHigh;
        case CorridorHighLowMethod.BidLowAskHigh: return r => r.BidLow;
        case CorridorHighLowMethod.Average: return r => r.PriceAvg;
        case CorridorHighLowMethod.AskBidByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.AskHigh : r.BidLow;
        case CorridorHighLowMethod.BidAskByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.BidHigh : r.AskLow;
        case CorridorHighLowMethod.PriceByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.PriceHigh : r.PriceLow;
        case CorridorHighLowMethod.PriceMA: return r => GetPriceMA(r);
      }
      throw new NotSupportedException(new { corridorHighLowMethod } + "");
    }

    public double CorridorCrossLowPrice(Rate rate, Func<Rate, double> getPrice = null) {
      return CalculateLastPrice(rate, getPrice ?? CorridorCrossGetLowPrice());
    }
    public Func<Rate, double> CorridorCrossGetLowPrice() {
      return CorridorLowPrice(CorridorCrossHighLowMethod);
    }
    public Func<Rate, double> CorridorGetLowPrice() {
      return CorridorLowPrice(CorridorHighLowMethod);
    }
    private Func<Rate, double> CorridorLowPrice(CorridorHighLowMethod corridorHighLowMethod) {
      switch (corridorHighLowMethod) {
        case CorridorHighLowMethod.AskHighBidLow: return r => r.BidLow;
        case CorridorHighLowMethod.AskLowBidHigh: return r => r.BidHigh;
        case CorridorHighLowMethod.BidHighAskLow: return r => r.AskLow;
        case CorridorHighLowMethod.BidLowAskHigh: return r => r.AskHigh;
        case CorridorHighLowMethod.Average: return r => r.PriceAvg;
        case CorridorHighLowMethod.AskBidByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.AskHigh : r.BidLow;
        case CorridorHighLowMethod.BidAskByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.BidHigh : r.AskLow;
        case CorridorHighLowMethod.PriceByMA: return r => r.PriceAvg > GetPriceMA()(r) ? r.PriceHigh : r.PriceLow;
        case CorridorHighLowMethod.PriceMA: return r => GetPriceMA(r);
      }
      throw new NotSupportedException(new { corridorHighLowMethod } + "");
    }

    private bool IsCorridorOk(CorridorStatistics cs) {
      return true;
    }

    #region IsCorridorCountOk
    private bool IsCorridorCountOk() {
      return IsCorridorCountOk(CorridorStats, CorridorCrossesCountMinimum);
    }
    private bool IsCorridorCountOk(CorridorStatistics cs) {
      return IsCorridorCountOk(cs, CorridorCrossesCountMinimum);
    }
    private static bool IsCorridorCountOk(CorridorStatistics cs, double corridorCrossesCountMinimum) {
      return IsCorridorCountOk(cs.CorridorCrossesCount, corridorCrossesCountMinimum);
    }
    private static bool IsCorridorCountOk(int crossesCount, double corridorCrossesCountMinimum) {
      return double.IsNaN(corridorCrossesCountMinimum) || crossesCount <= corridorCrossesCountMinimum;
    }
    #endregion

    #region CorridorCrossesCount
    class __rateCross {
      public Rate rate { get; set; }
      public bool isUp { get; set; }
      public __rateCross(Rate rate, bool isUp) {
        this.rate = rate;
        this.isUp = isUp;
      }
    }
    private int CorridorCrossesCount0(CorridorStatistics corridornes) {
      return CorridorCrossesCount(corridornes, corridornes.priceHigh, corridornes.priceLow, c => c.HeightUp0, c => c.HeightDown0);
    }
    private int CorridorCrossesCount(CorridorStatistics corridornes) {
      return CorridorCrossesCount(corridornes, corridornes.priceHigh, corridornes.priceLow, c => c.HeightUp, c => c.HeightDown);
    }
    private int CorridorCrossesCount(CorridorStatistics corridornes, Func<Rate, double> getPriceHigh, Func<Rate, double> getPriceLow, Func<CorridorStatistics, double> heightUp, Func<CorridorStatistics, double> heightDown) {
      var rates = corridornes.Rates;
      double[] coeffs = corridornes.Coeffs;

      var rateByIndex = rates.Select((r, i) => new { index = i, rate = r }).Skip(3).ToList();
      var crossPriceHigh = CorridorCrossGetHighPrice();
      var crossUps = rateByIndex
        .Where(rbi => crossPriceHigh(rbi.rate) >= corridornes.priceLine[rbi.index] + heightUp(corridornes))
        .Select(rbi => new __rateCross(rbi.rate, true)).ToList();
      var crossPriceLow = CorridorCrossGetLowPrice();
      var crossDowns = rateByIndex
        .Where(rbi => crossPriceLow(rbi.rate) <= corridornes.priceLine[rbi.index] - heightDown(corridornes))
        .Select(rbi => new __rateCross(rbi.rate, false)).ToList();
      if (crossDowns.Any() || crossUps.Any()) {
        var crosses = crossUps.Concat(crossDowns).OrderByDescending(r => r.rate.StartDate).ToList();
        var crossesList = new List<__rateCross>();
        crossesList.Add(crosses[0]);
        crosses.Aggregate((rp, rn) => {
          if (rp.isUp != rn.isUp) {
            crossesList.Add(rn);
            //corridornes.LegInfos.Add(new CorridorStatistics.LegInfo(rp.rate, rn.rate, BarPeriodInt.FromMinutes()));
          }
          return rn;
        });
        return crossesList.Count;
      }
      return 0;
    }

    #endregion
  }
}
