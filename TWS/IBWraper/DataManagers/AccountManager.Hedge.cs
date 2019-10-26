using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;

namespace IBApp {
  partial class AccountManager {
    public IObservable<ComboTrade> MakeComboHedgeFromPositions(IEnumerable<Position> positions) {
      var a = (from g in HedgedPositions(positions).OrderBy(p => p.position.contract.Instrument).ToArray()
               where g.Length == 2
               from hc in g.Pairwise((o, t) => MakeHedgeCombo(1, o.position.contract, t.position.contract, o.position.position.Abs(), t.position.position.Abs()))
               let quantity = hc.quantity * g.First().position.position.Sign()
               let isBuy = quantity > 0
               let pl = g.Sum(p => p.pl)
               let mul = g.Min(p => p.position.contract.ComboMultiplier) * quantity
               let openPrice = g.Sum(p => p.position.open) / mul
               let closePrice = g.Sum(p => p.close) / mul
               let order = OrderContractsInternal.OpenByContract(hc.contract).Where(oc => oc.order.IsBuy != isBuy).Take(1).ToList()
               let orderId = order.Select(o => o.order.OrderId).SingleOrDefault()
               let tp = order.Select(oc => oc.order.LmtAuxPrice).SingleOrDefault()
               let profit = (tp - openPrice) * mul
               let open = openPrice * mul
               select new ComboTrade(hc.contract, pl, openPrice, closePrice, quantity, tp, profit, open, orderId)
               );
      return a;
      // where g.Key.IsFuture
      // select (g.ToArray(),
      //   MakeHedgeCombo(1, g.First().contract, g.Last().contract, g.First().position.Abs(), g.Last().position.Abs())
      //   )
      //).ToArray();
    }
    public static IObservable<(Position position, double close, double pl, double closePrice)> HedgedPositions(IEnumerable<Position> positions) =>
      (from p in positions.ToObservable()
       where p.contract.IsFuture
       from price in p.contract.ReqPriceSafe()
       let closePrice = p.position > 0 ? price.bid : price.ask
       let close = closePrice * p.contract.ComboMultiplier * p.position
       select (p, close, close - p.open, closePrice)
      );

    public static IObservable<(Contract contract, int quantity)> MakeHedgeComboSafe(int quantity, Contract c1, Contract c2, double ratio1, double ratio2, bool isInTest) =>
      from cd1 in isInTest ? Observable.Return(c1.SetTestConId(isInTest, 0)) : c1.ReqContractDetailsCached().Select(cd => cd.Contract)
      from cd2 in isInTest ? Observable.Return(c2.SetTestConId(isInTest, 0)) : c2.ReqContractDetailsCached().Select(cd => cd.Contract)
      select MakeHedgeCombo(quantity, cd1, cd2, ratio1, ratio2).SideEffect(x => x.contract.SetTestConId(isInTest, 0));

    public static (Contract contract, int quantity) MakeHedgeCombo(int quantity, Contract c1, Contract c2, double ratio1, double ratio2) {
      if(c1.ConId == 0)
        throw new Exception($"ComboLeg contract1 has ConId = 0");
      if(c2.ConId == 0)
        throw new Exception($"ComboLeg contract2 has ConId = 0");
      int r1 = (ratio1 * quantity).ToInt();
      int r2 = (ratio2 * quantity).ToInt();
      if(r1 == int.MinValue || r2 == int.MinValue) {
        Debugger.Break();
      }
      var gcd = new[] { r1, r2 }.GCD();
      Contract contract = new Contract();
      var symbol = c1.IsFuture || c1.IsOption
        ? c1.Symbol.IfEmpty(Regex.Match(c1.LocalSymbol, "(.+).{2}$").Groups[1] + "").ThrowIf(contractSymbol => contractSymbol.IsNullOrWhiteSpace())
        : new[] { c1.Symbol, c2.Symbol }.OrderBy(s => s).Flatter(",");
      contract.Symbol = symbol;
      contract.SecType = "BAG";
      contract.Currency = "USD";
      contract.Exchange = "SMART";
      contract.TradingClass = "COMB";

      ComboLeg leg1 = new ComboLeg();
      leg1.ConId = c1.ConId;
      leg1.Ratio = r1 / gcd;
      leg1.Action = "BUY";
      leg1.Exchange = c1.Exchange;

      ComboLeg leg2 = new ComboLeg();
      leg2.ConId = c2.ConId;
      leg2.Ratio = r2 / gcd;
      leg2.Action = "SELL";
      leg2.Exchange = c2.Exchange;

      contract.ComboLegs = new List<ComboLeg>();
      contract.ComboLegs.Add(leg1);
      contract.ComboLegs.Add(leg2);

      var cache = Contract.Contracts.Any() ? contract.FromCache().Single() : contract;
      return (cache, gcd);
    }
  }

}
