using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using static IBApp.AccountManager;

namespace IBApp {
  partial class AccountManager {
    public static IObservable<ComboTrade> MakeComboHedgeFromPositions(IEnumerable<Position> positions) {
      var a = (from g in HedgedPositions(positions).Where(p => p.position.contract.IsFuture).ToArray()
               where g.Length == 2
               let pl = g.Sum(p => p.pl)
               let openPrice = g.Sum(p => p.position.price * p.position.position)
               let closePrice = g.Sum(p => p.closePrice * p.position.position)
               from hc in g.Pairwise((o, t) => MakeHedgeCombo(1, o.position.contract, t.position.contract, o.position.position.Abs(), t.position.position.Abs()))
               select new ComboTrade(hc.contract, pl, openPrice, closePrice, hc.quantity * g.First().position.position.Sign())
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
       from price in IBClientMaster.ReqPriceSafe(p.contract)
       let closePrice = (p.position > 0 ? price.ask : price.bid)
       let close = closePrice * p.contract.ComboMultiplier * p.position
       select (p, close, close - p.open, closePrice)
      );

    public static (Contract contract, int quantity) MakeHedgeCombo(int quantity, Contract c1, Contract c2, double ratio1, double ratio2) {
      int r1 = (ratio1 * quantity).ToInt();
      int r2 = (ratio2 * quantity).ToInt();
      var gcd = new[] { r1, r2 }.GCD();
      Contract contract = new Contract();
      contract.Symbol = c1.Symbol;
      contract.SecType = "BAG";
      contract.Currency = "USD";
      contract.Exchange = "SMART";

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

      //EXEND
      return (contract, gcd);
    }
  }

}
