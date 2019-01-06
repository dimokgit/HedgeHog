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
using COMBO_TRADES = System.IObservable<IBApp.ComboTrade>;
namespace IBApp {
  public class ComboTrade {
    public bool IsVirtual { get; private set; }
    public double Change => closePrice * position.Sign() - openPrice;
    public ComboTrade(Contract contract) {
      this.contract = contract;
      position = 1;
      IsVirtual = true;
    }
    public ComboTrade(IBApi.Contract contract, int position, double open, double close, double pl, double change, double underPrice
      , double strikeAvg, double openPrice, double closePrice, (double bid, double ask) price, double takeProfit, double profit
      , double pmc, double mcUnder, int orderId) {
      this.contract = contract;
      this.position = position;
      this.open = open;
      this.close = close;
      this.pl = pl;
      this.change = change;
      this.underPrice = underPrice;
      this.strikeAvg = strikeAvg;
      this.openPrice = openPrice;
      this.closePrice = closePrice;
      this.price = price;
      this.takeProfit = takeProfit;
      this.profit = profit;
      this.pmc = pmc;
      this.mcUnder = mcUnder;
      this.orderId = orderId;
    }
    public override string ToString() => new {
      this.contract,
      this.position,
      this.open,
      this.close,
      this.pl,
      this.change,
      this.underPrice,
      this.strikeAvg,
      this.openPrice,
      this.closePrice,
      this.price,
      this.takeProfit,
      this.profit,
      this.pmc,
      this.mcUnder,
      this.orderId
    } + "";
    public Contract contract { get; }
    public int position { get; }
    public double open { get; }
    public double close { get; }
    public double pl { get; }
    public double change { get; }
    public double underPrice { get; }
    public double strikeAvg { get; }
    public double openPrice { get; }
    public double closePrice { get; }
    public (double bid, double ask) price { get; }
    public double takeProfit { get; }
    public double profit { get; }
    public double pmc { get; }
    public double mcUnder { get; }
    public int orderId { get; }
  }
  public partial class AccountManager {
    public static double priceFromProfit(double profit, double position, int multiplier, double open)
      => (profit + open) / position / multiplier;
    public COMBO_TRADES ComboTrades(double priceTimeoutInSeconds) {
      var combos = (
        from c in ComboTradesImpl().ToObservable()
        from underPrice in UnderPrice(c.contract, priceTimeoutInSeconds).DefaultIfEmpty()
        from price in IbClient.ReqPriceSafe(c.contract, priceTimeoutInSeconds, true).DefaultIfEmpty().Take(1)
        let multiplier = c.contract.ComboMultiplier
        let closePrice = (c.position > 0 ? price.bid : price.ask)
        let close = (closePrice * c.position * multiplier).Round(4)
        let delta = price.delta != 0 ? price.delta.Abs() : 1
        let pmc = Account.ExcessLiquidity / (multiplier * c.position.Abs()) / (delta.Between(-1, 0) ? delta : 0).Abs()
        let mcUnder = c.position > 0 ? 0
        : c.contract.IsCall ? underPrice + pmc : c.contract.IsPut ? underPrice - pmc : 0
        let pl = close - c.open
        let change = pl / c.position.Abs() / multiplier
        select new ComboTrade(
         IbClient.SetContractSubscription(c.contract)
        , c.position
        , c.open
        , close
        , pl
        , change
        , underPrice
        , strikeAvg: c.contract.ComboStrike()
        , c.openPrice
        , closePrice
        , price: (price.bid, price.ask)
        , c.takeProfit
        , profit: (c.takeProfit * c.position * multiplier - c.open).Round(2)
        , pmc
        , mcUnder
        , c.orderId
        )
        );
      return combos
        .ToArray()
        .SelectMany(cmbs => cmbs
          .OrderBy(c => c.contract.Legs().Count())
          .ThenBy(c => c.contract.IsOption)
          .ThenByDescending(c => c.strikeAvg - c.underPrice)
          .ThenByDescending(c => c.contract.Instrument)
         );
    }

    IObservable<double> UnderPrice(Contract contract,double priceTimeoutInSeconds) {
      var cds = contract.FromDetailsCache().Concat(contract.Legs().SelectMany(l => l.c.FromDetailsCache()))
        .Select(c=>c.Contract.IsOption?c.UnderSymbol:c.Contract.LocalSymbol).Take(1);
      return (
        from underSymbol in cds.ToObservable()
        from u in IbClient.ReqContractDetailsCached(underSymbol)
        from underPrice in IbClient.ReqPriceSafe(u.Contract, priceTimeoutInSeconds, false)
        select underPrice.ask.Avg(underPrice.bid)
        ).Take(1);
    }
    public IEnumerable<(Contract contract, int position, double open, double openPrice, double takeProfit, int orderId)> ComboTradesImpl() {
      var positions = Positions.Where(p => p.position != 0).ToArray();
      var orders = OrderContractsInternal.Where(oc => !oc.isDone).ToArray();
      var combos = (
        from c in positions/*.ParseCombos(orders)*/.Do(c => IbClient.SetContractSubscription(c.contract))
        let order = orders.Where(oc => oc.isSubmitted && oc.contract.Key == c.contract.Key).Select(oc => (oc.order.OrderId, oc.order.LmtPrice)).FirstOrDefault()
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
    //public COMBO_TRADES_IMPL ComboTradesUnder() {
    //  var positions = Positions.Where(p => p.position != 0).ToArray();
    //  //var expDate = positions.Select(p => p.contract.Expiration).DefaultIfEmpty().Min();
    //  //var positionsByExpiration = positions.Where(p => p.contract.Expiration == expDate).ToArray();
    //  return MakeUnderlyingComboAll(positions.Select(p => (p.contract, p.position)), positions);
    //}

  }
}
