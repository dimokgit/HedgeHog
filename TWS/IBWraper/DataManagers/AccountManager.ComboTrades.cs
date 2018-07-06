using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using COMBO_TRADES_IMPL = System.Collections.Generic.IEnumerable<(IBApi.Contract contract, int position, double open, double openPrice, double takeProfit, int orderId)>;
using COMBO_TRADES = System.IObservable<(IBApi.Contract contract, int position, double open, double close, double pl, double underPrice, double strikeAvg, double openPrice, double closePrice, (double bid, double ask) price, double takeProfit, double profit, int orderId)>;
namespace IBApp {
  public partial class AccountManager {
    public static double priceFromProfit(double profit, double position, int multiplier, double open)
      => (profit + open) / position / multiplier;
    public COMBO_TRADES ComboTrades(double priceTimeoutInSeconds) {
      var combos = (
        from c in ComboTradesImpl().ToObservable()
        from underPrice in UnderPrice(c.contract).DefaultIfEmpty()
        from price in IbClient.ReqPriceSafe(c.contract, priceTimeoutInSeconds, true).DefaultIfEmpty().Take(1)
        let multiplier = c.contract.ComboMultiplier
        let closePrice = (c.position > 0 ? price.bid : price.ask)
        let close = (closePrice * c.position * multiplier).Round(4)
        let openPrice = c.open / c.position.Abs() / multiplier
        let isOk = openPrice == c.openPrice ? true : throw new Exception(new { calc = new { openPrice }, c.openPrice } + "")
        select (c: IbClient.SetContractSubscription(c.contract), c.position, c.open, close
        , pl: close - c.open, underPrice, strikeAvg: c.contract.ComboStrike()
        , openPrice
        , closePrice
        , price: (price.bid, price.ask)
        , c.takeProfit
        , profit: (c.takeProfit * c.position * multiplier - c.open).Round(2)
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
        var underSymbol = contract.Symbol + (contract.HasFutureOption ? "U8" : "");
        return (
        from symbol in IbClient.ReqContractDetailsCached(underSymbol)
        from underPrice in IbClient.ReqPriceSafe(symbol.Summary, priceTimeoutInSeconds, false)
        select underPrice.ask.Avg(underPrice.bid));
      }
    }

    public IEnumerable<(Contract contract, int position, double open, double openPrice, double takeProfit, int orderId)> ComboTradesImpl() {
      var positions = Positions.Where(p => p.position != 0).ToArray();
      var orders = OrderContractsInternal.Where(oc => !oc.isDone).ToArray();
      var combos = (
        from c in positions/*.ParseCombos(orders)*/.Do(c => IbClient.SetContractSubscription(c.contract))
        let order = orders.Where(oc => oc.contract.Key == c.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice)).FirstOrDefault()
        select (c.contract, c.position, c.open, c.open / c.position.Abs() / c.contract.ComboMultiplier, order.LmtPrice, order.OrderId)
        );
      var comboAll = ComboTradesAllImpl().ToArray();
      return combos.Concat(comboAll).Distinct(c => c.contract.Instrument);
    }
    public COMBO_TRADES_IMPL ComboTradesAllImpl() {
      var positions = Positions.Where(p => p.position != 0 && p.contract.IsOption).ToArray();
      //var expDate = positions.Select(p => p.contract.Expiration).DefaultIfEmpty().Min();
      //var positionsByExpiration = positions.Where(p => p.contract.Expiration == expDate).ToArray();
      var positionsByExpiration = positions.GroupBy(p => p.contract.Expiration);
      return positionsByExpiration.Select(g => ComboTradesAllImpl2(g.ToArray())).Concat();
    }

    private COMBO_TRADES_IMPL ComboTradesAllImpl2((Contract contract, int position, double open, double price, double pipCost)[] positions) =>
      (from ca in MakeComboAll(positions.Select(p => (p.contract, p.position)), positions, (p, tc) => p.contract.TradingClass == tc)
       let order = UseOrderContracts(orderContracts => orderContracts.Where(oc => !oc.isDone && oc.contract.Key == ca.contract.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice))).Concat().FirstOrDefault()
       let open = ca.positions.Sum(p => p.open)
       let openPrice = open / ca.contract.positions.Abs() / ca.contract.contract.ComboMultiplier
       select (ca.contract.contract, position: ca.contract.positions, open, openPrice, order.LmtPrice, order.OrderId));
  }
}
