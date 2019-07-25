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
    public static IObservable<(Contract contract, double pl)> MakeComboHedgeFromPositions(IEnumerable<Position> positions) {
      var a = (from g in HedgedPositions(positions).Where(p => p.contract.IsFuture).ToArray()
               where g.Length == 2
               let pl = g.Sum(p => p.pl)
               from hc in g.Pairwise((o, t) => MakeHedgeCombo(1, o.contract, t.contract, o.position.Abs(), t.position.Abs()))
               select (hc.contract,pl)
               );
      return a;
      // where g.Key.IsFuture
      // select (g.ToArray(),
      //   MakeHedgeCombo(1, g.First().contract, g.Last().contract, g.First().position.Abs(), g.Last().position.Abs())
      //   )
      //).ToArray();
    }
    public static IObservable<(Contract contract, double open, double close, double pl, int position)> HedgedPositions(IEnumerable<Position> positions) =>
      (from p in positions.ToObservable()
       where p.contract.IsFuture
       from price in IBClientMaster.ReqPriceSafe(p.contract)
       let close = (p.position > 0 ? price.ask : price.bid) * p.contract.ComboMultiplier * p.position
       select (p.contract, p.open, close, close - p.open, p.position)
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
