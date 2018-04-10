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
    public static double priceFromProfit(double profit, double position, int multiplier, double open)
      => (profit + open) / position / multiplier;
    public IObservable<(Contract contract, int position, double open, double close, double pl, double underPrice, double strikeAvg, double openPrice, double takeProfit, double profit, int orderId)>
      ComboTrades(double priceTimeoutInSeconds) {
      var combos = (
        from c in ComboTradesImpl().ToObservable()
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
          .ThenByDescending(c => c.strikeAvg - c.underPrice)
          .ThenByDescending(c => c.c.Instrument)
         );
      IObservable<double> UnderPrice(Contract contract) {
        if(!contract.IsOption && !contract.IsCombo) return Observable.Return(0.0);
        return (
        from symbol in IbClient.ReqContractDetailsCached(contract.Symbol)
        from underPrice in IbClient.ReqPriceSafe(symbol.Summary, priceTimeoutInSeconds, false)
        select underPrice.ask.Avg(underPrice.bid));
      }
    }

    public IEnumerable<(Contract contract, int position, double open, double takeProfit, int orderId)> ComboTradesImpl() {
      var positions = Positions.Where(p => p.position != 0).ToArray();
      var combos = (
        from c in positions.ParseCombos(OrderContracts.Values).Do(c => IbClient.SetContractSubscription(c.contract))
        let order = OrderContracts.Values.Where(oc => !oc.isDone && oc.contract.Key == c.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice)).FirstOrDefault()
        select (c.contract, c.position, c.open, order.LmtPrice, order.OrderId)
        );
      var comboAll = (from ca in MakeComboAll(positions.Select(p => (p.contract, p.position)), positions, (p, tc) => p.contract.TradingClass == tc)
                      let order = OrderContracts.Values.Where(oc => !oc.isDone && oc.contract.Key == ca.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice)).FirstOrDefault()
                      select (ca.contract, ca.positions.Sum(p => p.open).Sign(), ca.positions.Sum(p => p.open), order.LmtPrice, order.OrderId)).ToArray();
      return combos.Concat(comboAll).Distinct(c => c.contract.Instrument);
    }
  }
}
