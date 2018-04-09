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
    public static double priceFromProfit(double profit, OrdeContractHolder och, double open)
      => priceFromProfit(profit, och.order.TotalPosition(), och.contract.ComboMultiplier, open);
    public static double priceFromProfit(double profit, double position, int multiplier, double open)
      => (profit + open) / position.Abs() * open.Sign() / multiplier;
    public IObservable<(Contract contract, int position, double open, double close, double pl, double underPrice, double strikeAvg, double openPrice, double takeProfit, double profit, int orderId)>
      ComboTrades(double priceTimeoutInSeconds) {
      var combos = (
        from c in ComboTradesImpl()
        from underPrice in UnderPrice(c.contract).DefaultIfEmpty()
        from price in IbClient.ReqPriceSafe(c.contract, priceTimeoutInSeconds, true).DefaultIfEmpty().Take(1)
        let multiplier = c.contract.ComboMultiplier
        let position = c.position.Abs() * c.open.Sign()
        let close = ((c.position > 0 ? price.bid : price.ask) * position * multiplier).Round(4)
        select (c: IbClient.SetContractSubscription(c.contract), c.position, c.open, close
        , pl: close - c.open, underPrice, strikeAvg: c.contract.ComboStrike()
        , openPrice: c.open / c.position.Abs() / multiplier
        , c.takeProfit
        , profit: (c.takeProfit * position * multiplier - c.open).Round(2)
        , c.orderId
        )
        );
      return combos
        .ToArray()
        .SelectMany(cmbs => cmbs
          .OrderBy(c => c.c.Legs().Count())
          .ThenBy(c => c.c.IsOption)
          .ThenBy(c => c.c.Instrument)
         );
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
        from c in positions.ParseCombos(OrderContracts.Values).Do(c => IbClient.SetContractSubscription(c.contract))
        let order = OrderContracts.Values.Where(oc => !oc.isDone && oc.contract.Key == c.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice)).FirstOrDefault()
        select (c.contract, c.position, c.open, order.LmtPrice, order.OrderId)
        );
      var comboAll = (from ca in MakeComboAll(Positions.Select(p => (p.contract, p.position)), Positions, (p, tc) => p.contract.TradingClass == tc)
                      let order = OrderContracts.Values.Where(oc => !oc.isDone && oc.contract.Key == ca.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice)).FirstOrDefault()
                      select (ca.contract, ca.positions.Sum(p => p.open).Sign(), ca.positions.Sum(p => p.open), order.LmtPrice, order.OrderId)).ToArray();
      return combos.Concat(comboAll.ToObservable());
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
