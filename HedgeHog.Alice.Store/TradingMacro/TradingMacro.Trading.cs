using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using TM_HEDGE = System.Collections.Generic.IEnumerable<(HedgeHog.Alice.Store.TradingMacro tm, string Pair, double HV, double HVP
  , (double All, double Up, double Down) TradeRatio
  , (double All, double Up, double Down) TradeRatioM1
  , (double All, double Up, double Down) TradeAmount
  , double MMR
  , (int All, int Up, int Down) Lot
  , (double All, double Up, double Down) Pip
  , bool IsBuy, bool IsPrime, double HVPR, double HVPM1R)>;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    public TM_HEDGE HedgeBuySell(bool isBuy) =>
      (TradesManager?.GetAccount()?.Equity * TradingRatio / 2).YieldNotNull(equity => {
        var hbss = (from tmh in GetHedgedTradingMacros(Pair)
                    from corr in tmh.tm.TMCorrelation(tmh.tmh)
                    let t = new[] { (tmh.tm, isBuy), (tmh.tmh, corr > 0 ? !isBuy : isBuy) }
                    from x in Hedging.CalcTradeAmount(t, equity.Value)
                    let lotAll = x.tm.GetLotsToTrade(x.tradeAmountAll, 1, 1)
                    let lotUp = x.tm.GetLotsToTrade(x.tradeAmountUp, 1, 1)
                    let lotDown = x.tm.GetLotsToTrade(x.tradeAmountDown, 1, 1)
                    select (
                    tm: x.tm,
                    Pair: x.tm.Pair,
                    HV: x.hv,
                    HVP: x.hvp,
                    TradeRatio: (x.tradingRatioAll * 100, x.tradingRatioUp * 100, x.tradingRatioDown * 100),
                    TradeRatioM1: (x.tradingRatioM1All * 100, x.tradingRatioM1Up * 100, x.tradingRatioM1Down * 100),
                    TradeAmount: (x.tradeAmountAll, x.tradeAmountUp, x.tradeAmountDown),
                    MMR: x.mmr,
                    Lot: (lotAll, lotUp, lotDown),
                    Pip: (x.tm.PipAmountByLot(lotAll), x.tm.PipAmountByLot(lotUp), x.tm.PipAmountByLot(lotDown)),
                    IsBuy: x.buy,
                    IsPrime: x.tm.Pair.ToLower() == Pair.ToLower(),
                    HVPR: (x.hvpr * 100).AutoRound2(3),
                    HVPM1R: (x.hvpM1r * 100).AutoRound2(3)
                    ));
        return hbss;
      }).Concat();

    public void AdjustHedgedTrades(bool isBuy, string reason) {
      var exc = new Exception($"{nameof(AdjustHedgedTrades)}: there is no hadged trades to adjust.");
      var hbs = HedgeBuySell(isBuy)
        .OrderByDescending(tm => tm.Pair == Pair)
        .Select(t => (t.Pair, Position: t.Lot.HedgedPositionAll(t.IsBuy)))
        .ToArray();

      (from ht in hbs
       join trd in TradesManager.GetTrades().Select(t => (t.Pair, t.Position)) on ht.Pair equals trd.Pair into gj
       from trade in gj.DefaultIfEmpty((ht.Pair, 0))
       from tm in TradingMacroTrader(trade.Pair).IfEmpty(() => throw ExcNoTrader(trade.Pair))
       orderby tm.Pair != Pair
       let pos = ht.Position - trade.Position
       select (tm, pos)
      )
      .IfEmpty(() => throw exc)
      .ForEach(t => t.tm.OpenTrade(t.pos > 0, t.pos.Abs().ToInt(), null, reason + ":" + nameof(AdjustHedgedTrades)));

      Exception ExcNoTrader(string pair) => new Exception(new { pair, error = "No trader found'" } + "");
    }

    object _tradeLock = new object();
    public void OpenTrade(bool isBuy, int lot, Price price, string reason) {
      lock(_tradeLock) {
        var key = lot - Trades.Lots(t => t.IsBuy != isBuy) > 0 ? OT : CT;
        CheckPendingAction(key, (pa) => {
          if(lot > 0) {
            pa();
            LogTradingAction(string.Format("{0}[{1}]: {2} {3} from {4} by [{5}]", Pair, BarPeriod, isBuy ? "Buying" : "Selling", lot, new StackFrame(3).GetMethod().Name, reason));
            TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", price);
          }
        });
      }
    }

    public void CloseTrades(Price price, string reason) { CloseTrades(Trades.Lots(), price, reason); }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private void CloseTrades(int lot, Price price, string reason) {
      lock(_tradeLock) {
        if(!IsTrader || !Trades.Any() || HasPendingKey(CT))
          return;
        if(lot > 0)
          CheckPendingAction(CT, pa => {
            LogTradingAction(string.Format("{0}[{1}]: Closing {2} from {3} in {4} from {5}]", Pair, BarPeriod, lot, Trades.Lots(), new StackFrame(3).GetMethod().Name, reason));
            pa();
            if(!TradesManager.ClosePair(Pair, Trades[0].IsBuy, lot, price))
              ReleasePendingAction(CT);
          });
      }
    }
  }
}
