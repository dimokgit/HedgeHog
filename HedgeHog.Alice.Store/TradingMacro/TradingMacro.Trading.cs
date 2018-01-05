using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using TM_HEDGE = System.Nullable<(HedgeHog.Alice.Store.TradingMacro tm, string Pair, double HV, double HVP, double TradeRatio, double TradeRatioM1, double TradeAmount, double MMR, int Lot, double Pip, bool IsBuy, bool IsPrime, double HVPR, double HVPM1R)>;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    public IEnumerable<TM_HEDGE> HedgeBuySell(bool isBuy) =>
      (TradesManager?.GetAccount()?.Equity * TradingRatio).YieldNotNull(equity => {
        var hbs = (from tmh in GetHedgedTradingMacros(Pair)
                   from corr in tmh.tm.TMCorrelation(tmh.tmh)
                   let t = new[] { (tmh.tm, isBuy), (tmh.tmh, corr > 0 ? !isBuy : isBuy) }
                   from x in Hedging.CalcTradeAmount(t, equity.Value)
                   let lot = x.tm.GetLotsToTrade(x.tradeAmount, 1, 1)
                   select (
                   tm: x.tm,
                   Pair: x.tm.Pair,
                   HV: x.hv,
                   HVP: x.hvp,
                   TradeRatio: x.tradingRatio * 100,
                   TradeRatioM1: x.tradingRatioM1 * 100,
                   TradeAmount: x.tradeAmount,
                   MMR: x.mmr,
                   Lot: lot,
                   Pip: x.tm.PipAmountByLot(lot),
                   IsBuy: x.buy,
                   IsPrime: x.tm.Pair.ToLower() == Pair.ToLower(),
                   HVPR: (x.hvpr * 100).AutoRound2(3),
                   HVPM1R: (x.hvpM1r * 100).AutoRound2(3)
                   ));
        return hbs.Select(t => new TM_HEDGE(t));
      }).Concat();

    public void OpenHedgedTrades(bool isBuy, bool closeOnly, string reason) {
      if(!IsInVirtualTrading && TradesManager.GetTrades().Any() && !closeOnly)
        AdjustHedgedTrades(isBuy, reason);
      else {
        var hbs = HedgeBuySell(isBuy)
        .Select(x => x.Value)
        .OrderByDescending(tm => tm.Pair == Pair)
        .ToArray();

        if(hbs.Where(bs => !TradesManager.TryGetPrice(bs.Pair).Any(p => p.IsShortable))
          .Do(bs => Log = new Exception(bs.Pair + " is not shortable")).Any())
          return;

        hbs.ForEach(t => {
          var lotToClose = t.tm.Trades.IsBuy(!t.IsBuy).Sum(tr => tr.Lots);
          var lotToOpen = !closeOnly ? t.Lot : 0;
          t.tm.OpenTrade(t.IsBuy, lotToOpen + lotToClose, reason + ": hedge open");
        });
        if(TradesManager.GetTrades().Length == 1) {
          TradesManager.CloseAllTrades();
        }
      }
    }

    public void AdjustHedgedTrades(bool isBuy, string reason) {
      var exc = new Exception($"{nameof(AdjustHedgedTrades)}: there is no hadged trades to adjust.");
      var hbs = HedgeBuySell(isBuy)
        .Select(x => x.Value)
        .OrderByDescending(tm => tm.Pair == Pair)
        .Select(t => (t.Pair, Position: t.IsBuy ? t.Lot : -t.Lot))
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
      .ForEach(t => t.tm.OpenTrade(t.pos > 0, t.pos.Abs().ToInt(), reason + ":" + nameof(AdjustHedgedTrades)));

      Exception ExcNoTrader(string pair) => new Exception(new { pair, error = "No trader found'" } + "");
    }

    object _tradeLock = new object();
    public void OpenTrade(bool isBuy, int lot, string reason) {
      lock(_tradeLock) {
        var key = lot - Trades.Lots(t => t.IsBuy != isBuy) > 0 ? OT : CT;
        CheckPendingAction(key, (pa) => {
          if(lot > 0) {
            pa();
            LogTradingAction(string.Format("{0}[{1}]: {2} {3} from {4} by [{5}]", Pair, BarPeriod, isBuy ? "Buying" : "Selling", lot, new StackFrame(3).GetMethod().Name, reason));
            TradesManager.OpenTrade(Pair, isBuy, lot, 0, 0, "", CurrentPrice);
          }
        });
      }
    }

    public void CloseTrades(string reason) { CloseTrades(Trades.Lots(), reason); }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private void CloseTrades(int lot, string reason) {
      lock(_tradeLock) {
        if(!IsTrader || !Trades.Any() || HasPendingKey(CT))
          return;
        if(lot > 0)
          CheckPendingAction(CT, pa => {
            LogTradingAction(string.Format("{0}[{1}]: Closing {2} from {3} in {4} from {5}]", Pair, BarPeriod, lot, Trades.Lots(), new StackFrame(3).GetMethod().Name, reason));
            pa();
            if(!TradesManager.ClosePair(Pair, Trades[0].IsBuy, lot))
              ReleasePendingAction(CT);
          });
      }
    }
  }
}
