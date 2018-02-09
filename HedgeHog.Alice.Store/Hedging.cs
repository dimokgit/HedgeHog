using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public static class Hedging {
    public static double HedgedTriplet(this (double All, double Up, double Down) t, bool buy) => buy ? t.Up : t.Down;
    public static double HedgedTriplet(this (int All, int Up, int Down) t, bool buy) => buy ? t.Up : t.Down;
    public static double HedgedPip(this (double All, double Up, double Down) pip, bool buy) => buy ? pip.Up : pip.Down;
    public static double HedgedTradeAmount(this (double All, double Up, double Down) ta, bool buy) => buy ? ta.Up : ta.Down;
    public static int HedgedLot(this (int All, int Up, int Down) lot, bool buy) => buy ? lot.Up : lot.Down;
    public static int HedgedLotAll(this (int All, int Up, int Down) lot, bool buy) => buy ? lot.All : lot.All;
    public static int HedgedPosition(this (int All, int Up, int Down) lot, bool buy) => lot.HedgedLot(buy) * (buy ? 1 : -1);
    public static int HedgedPositionAll(this (int All, int Up, int Down) lot,bool buy) => lot.HedgedLotAll(buy) * (buy ? 1 : -1);
    public static IEnumerable<int> TMCorrelation(TradingMacro tm1, TradingMacro tm2) =>
      from corrs in tm1.UseRates(ra1 => tm2.UseRates(ra2 => alglib.pearsoncorr2(ra1.ToArray(r => r.PriceAvg), ra2.ToArray(r => r.PriceAvg), ra1.Count.Min(ra2.Count))))
      from corr in corrs
      where corr != 0
      select corr > 0 ? 1 : -1;

    public static IEnumerable<(TradingMacro tm, bool buy
      , double tradeAmountAll, double tradeAmountUp, double tradeAmountDown
      , double tradingRatioAll, double tradingRatioM1All
      , double tradingRatioUp, double tradingRatioM1Up
      , double tradingRatioDown, double tradingRatioM1Down
      , double hvpr, double hv, double hvp, double mmr, double hvpM1r)>
      CalcTradeAmount(IList<(TradingMacro tm, bool buy)> tms, double equity) {
      var minMaxes = (from tm in tms
                      from tmM1 in tm.tm.TradingMacroM1()
                      from hv in tm.tm.HistoricalVolatility()
                      from hvM1 in tmM1.HistoricalVolatility()
                      from hvUp in tm.tm.HistoricalVolatilityUp()
                      from hvM1Up in tmM1.HistoricalVolatilityUp()
                      from hvDown in tm.tm.HistoricalVolatilityDown()
                      from hvM1Down in tmM1.HistoricalVolatilityDown()
                      from hvp in tm.tm.HistoricalVolatilityByPips()
                      from hvpM1 in tmM1.HistoricalVolatilityByPips()
                      let mmr = TradesManagerStatic.GetMMR(tm.tm.Pair, tm.buy)
                      orderby mmr descending
                      select new { tm.tm, tradeMax = equity / mmr, tm.buy, hv, hvM1, hvUp, hvM1Up, hvDown, hvM1Down, hvp, hvpM1, mmr }
                      )
                      .Pairwise((min, max) => new {
                        min, max,
                        hvr = min.hv / max.hv,
                        hvM1r = min.hvM1 / max.hvM1,
                        hvrUp = min.hvUp / max.hvUp,
                        hvM1rUp = min.hvM1Up / max.hvM1Up,
                        hvrDown = min.hvDown / max.hvDown,
                        hvM1rDown = min.hvM1Down / max.hvM1Down
                      })
                      .ToArray();
      var ctas = minMaxes.SelectMany(mm => {
        //var hvr = mm.hvr.Avg(mm.hvM1r);
        var hvr = mm.hvr;
        var maxTradeAll = mm.max.tradeMax.Min(mm.min.tradeMax * hvr);
        var minTradeAll = mm.min.tradeMax.Min(maxTradeAll / hvr);

        //var hvrUp = mm.hvrUp.Avg(mm.hvM1rUp);
        var hvrUp = mm.hvrUp;
        var maxTradeUp = mm.max.tradeMax.Min(mm.min.tradeMax * hvrUp);
        var minTradeUp = mm.min.tradeMax.Min(maxTradeUp / hvrUp);

        //var hvrDown = mm.hvrDown.Avg(mm.hvM1rDown);
        var hvrDown = mm.hvrDown;
        var maxTradeDown = mm.max.tradeMax.Min(mm.min.tradeMax * hvrDown);
        var minTradeDown = mm.min.tradeMax.Min(maxTradeDown / hvrDown);

        var hvs = mm.min.hv + mm.max.hv;
        var hvsUp = mm.min.hvUp + mm.max.hvUp;
        var hvsDown = mm.min.hvDown + mm.max.hvDown;

        var hvM1s = mm.min.hvM1 + mm.max.hvM1;
        var hvM1sUp = mm.min.hvM1Up + mm.max.hvM1Up;
        var hvM1sDown = mm.min.hvM1Down + mm.max.hvM1Down;

        var hvps = mm.min.hvp + mm.max.hvp;
        var hvpM1s = mm.min.hvpM1 + mm.max.hvpM1;
        return new[] {
          (mm.min.tm, mm.min.buy
          , minTradeAll, minTradeUp, minTradeDown
          ,1 - mm.min.hv / hvs,1 - mm.min.hvM1 / hvM1s,1 - mm.min.hvUp / hvsUp
          ,1 - mm.min.hvM1Up / hvM1sUp
          ,1 - mm.min.hvDown / hvsDown,1 - mm.min.hvM1Down / hvM1sDown
          ,mm.min.hvp / hvps,mm.min.hv,mm.min.hvp,mm.min.mmr,mm.min.hvpM1 / hvpM1s),
          (mm.max.tm, mm.max.buy
          ,maxTradeAll,maxTradeUp,maxTradeDown
          ,1 - mm.max.hv / hvs,1 - mm.max.hvM1 / hvM1s
          ,1 - mm.max.hvUp / hvsUp,1 - mm.max.hvM1Up / hvM1sUp
          ,1 - mm.max.hvDown / hvsDown,1 - mm.max.hvM1Down / hvM1sDown
          ,mm.max.hvp / hvps,mm.max.hv,mm.max.hvp,mm.max.mmr,mm.max.hvpM1 / hvpM1s)
          };
      });

      return from tm in tms
             join cta in ctas on tm equals (cta.tm, cta.buy)
             select cta;
    }
  }
}
