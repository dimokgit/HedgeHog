using HedgeHog;
using System;
using System.Linq;
using System.Reactive.Linq;

namespace IBApp {
  public partial class AccountManager {
    const string STRATEGY_PREFIX = "TM:";
    void WireOrderEntryLevels() {
      var reqs = (from oels in OrderEnrtyLevel
                  from oel in oels
                  from orders in OptionsByBS(oel.instrument, oel.level, oel.isBuy)
                  select (orders, oel.quantity, oel.isBuy)
                 );
      var b = reqs.Where(r => r.isBuy).DistinctUntilChanged(_ => DateTime.Now.Round(MathCore.RoundTo.Second));
      var s = reqs.Where(r => !r.isBuy).DistinctUntilChanged(_ => DateTime.Now.Round(MathCore.RoundTo.Second));
      (from r in b.Merge(s)
       from t in r.orders
       from under in t.option.UnderContract
       let contract = t.option
       let level = t.level
       let profit = 5
       from hasPos in Positions.Where(p
       => p.position != 0
       && p.contract.UnderContract.SequenceEqual(contract.UnderContract)
       && p.contract.IsCall == contract.IsCall
       && p.contract.Strike.Abs(contract.Strike) <= 5
       ).Select(_ => true).DefaultIfEmpty()
       where !hasPos
       let openCond = under.PriceCondition(level.Round(2), contract.IsPut)
       let tpCond = under.PriceCondition((level + profit * (contract.IsCall ? 1 : -1)).Round(2), contract.IsCall)
       let strategyRef = STRATEGY_PREFIX + t.option.Right
       from hc in OrderContractsInternal.Values.Where(h => h.order.OrderRef == strategyRef).Select(h => h.order.OrderId).DefaultIfEmpty()
       from co in hc != 0 ? CancelOrder(hc) : Observable.Return(new ErrorMessages<OrderContractHolder>(default, default))
       where co.error.reqId == 0 || !co.error.HasError
       from ot in OpenTrade(t.option, r.quantity, 0, 0, true, DateTime.Now.AddMinutes(10), default, openCond, tpCond, strategyRef)
       select ot
       ).Subscribe();
    }
  }
}
