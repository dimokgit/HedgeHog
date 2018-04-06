using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IBApp {
  public partial class AccountManager {
    public static double priceFromProfit(double profit, int position, int multiplier, double open)
      => (profit + open) / position / multiplier;
    public IObservable<(Contract contract, int position, double open, double close, double pl, double underPrice, double strikeAvg, double openPrice, double takeProfit, double profit, int orderId)>
      ComboTrades(double priceTimeoutInSeconds) {
      var combos = (
        from c in ComboTradesImpl()
        from underPrice in UnderPrice(c.contract).DefaultIfEmpty()
        from price in IbClient.ReqPriceSafe(c.contract, priceTimeoutInSeconds, true).DefaultIfEmpty().Take(1)
        let multiplier = c.contract.ComboMultiplier
        let close = (c.position > 0 ? price.bid : price.ask) * c.position * multiplier
        select (IbClient.SetContractSubscription(c.contract), c.position, c.open, close
        , pl: close - c.open, underPrice, strikeAvg: c.contract.ComboStrike()
        , openPrice: c.open / c.position / multiplier
        , c.takeProfit
        , profit: c.takeProfit * c.position * multiplier - c.open
        , c.orderId
        )
        );
      return combos;
      IObservable<double> UnderPrice(Contract contract) {
        if(!contract.IsOption && !contract.IsCombo) return Observable.Return(0.0);
        return (
        from symbol in IbClient.ReqContractDetailsCached(contract.Symbol)
        from underPrice in IbClient.ReqPriceSafe(symbol.Summary, priceTimeoutInSeconds, false)
        select underPrice.ask.Avg(underPrice.bid));
      }
    }

    public IObservable<(Contract contract, int position, double open, double takeProfit, int orderId)> ComboTradesImpl() {
      var positionsArray = (from p in Positions.ToObservable()
                            from cd in IbClient.ReqContractDetailsCached(p.contract)
                            select (contract: cd.Summary, p.position, p.open)
                       ).ToArray();
      var combos = (
        from positions in positionsArray
        from c in positions.ParseCombos().Do(c => IbClient.SetContractSubscription(c.contract))
        let order = OrderContracts.Values.Where(oc => !oc.isDone && oc.contract.Key == c.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice)).FirstOrDefault()
        select (c.contract, c.position, c.open, order.LmtPrice, order.OrderId)
        );
      return combos;
      return combos.GroupJoin(
        OrderContracts.Values.ToObservable(),
        combo => combo.contract.Key.ToObservable(),
        oc => oc.contract.Key.ToObservable(),
        (combo, ocs) => ocs
          .Select(oc => (combo.contract, combo.position, combo.open, takeProfit: oc.order.LmtPrice, oc.order.OrderId))
          .DefaultIfEmpty((combo.contract, combo.position, combo.open, takeProfit: 0.0, 0))
        ).Merge()
        ;
    }
  }
}
