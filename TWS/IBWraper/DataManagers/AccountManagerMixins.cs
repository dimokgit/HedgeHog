using HedgeHog;
using IBApi;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using static IBApp.AccountManager;

namespace IBApp {
  public static class AccountManagerMixins {
    public static IEnumerable<OrdeContractHolder> FindChildren(this IList<OrdeContractHolder> orders, Order order) =>
      from o in orders
      where o.order.ParentId == order.OrderId
      select o;

    #region Parse Combos
    public static IList<(Contract contract, int position, double open)> ParseCombos
      (this ICollection<(Contract c, int p, double o)> positions, ICollection<AccountManager.OrdeContractHolder> openOrders) {
      var cartasian = (from combo in positions.CartesianProductSelf()
                       select combo.MashDiffs(c => c.c.Instrument)).ToArray();
      var matches = (from oc in openOrders
                     join mashedCombo in cartasian on oc.contract.Instrument equals mashedCombo.mash
                     let p = -oc.order.TotalPosition().ToInt()
                     select (combo: oc.contract, positions: oc.contract.Legs().Select(c => (c, p)).ToArray(), oc.order.OrderId)
                     ).ToArray();
      var legs = matches.SelectMany(m => m.positions).ToArray();
      var update = (from pos in positions
                    join leg in legs on new { pos.c.Key, ps = pos.p.Sign() } equals new { leg.c.c.Key, ps = leg.p.Sign() }
                    select (p: pos, q: leg.p)
                    ).ToArray();

      var legs2 = matches.SelectMany(m => m.positions.Select(p => (p, m.OrderId))).ToArray();
      var orderPositions = (from pos in positions
                            join leg in legs2 on pos.c.Key equals leg.p.c.c.Key
                            let pos2 = (pos.c, leg.p.p, pos.o, leg.OrderId)
                            group pos2 by pos2.OrderId into gpos
                            from op in gpos.Select(gp => (gp.c, gp.p, gp.o)).ToArray().ParseCombos(new AccountManager.OrdeContractHolder[0])
                            select op
                            ).ToArray();
      positions = (from pos in positions
                   join u in update on pos.c.Key equals u.p.c.Key into gpos
                   from t in gpos.DefaultIfEmpty((p: pos, q: 0))
                   let t2 = (t.p.c, p: t.p.p - t.q, t.p.o)
                   where t2.p != 0
                   select t2
                   ).ToArray();

      ;
      //(from match in matches)
      var tradesAll = ResortPositions(positions).ToList();
      var combos = new List<(Contract c, int p, double o)>();
      var singles = new List<(Contract c, int p, double o)>();
      var trades = tradesAll.GroupBy(t => (t.c.Strike, t.c.LastTradeDateOrContractMonth))
        .OrderByDescending(t => t.Count())
        .SelectMany(g => g.ToArray()).ToList();
      while(trades.Any()) {
        var trade = trades.First();
        var match = FindStraddle(trades, trade).Concat(FindStraddle(trades, trade, true)).Take(1);
        if(match.IsEmpty()) {
          trades.Remove(trade);
          singles.Add(trade);
        } else {
          var parsed = match.Select(ParseMatch).ToArray();
          combos.AddRange(parsed.Select(p => p.combo));
          trades = RePosition(trades, parsed).ToList();
        }
        //ver("Trades new:\n" + trades.Flatter("\n"));
      }
      var xc = matches.SelectMany(m => m.positions);
      return orderPositions.Concat(combos).Concat(singles).ToArray();
    }
    static ((Contract c, int p, double o) combo, (Contract c, int p, double o)[] rest)
      ParseMatch(((Contract c, int p, double o) leg1, (Contract c, int p, double o) leg2) m) {
      //HandleMessage2($"Match:{m}");
      var posMin = m.leg1.p.Abs().Min(m.leg2.p.Abs()) * m.leg1.p.Sign();
      var legsCombo = new[] { m.leg1.c, m.leg2.c };
      var straddle = AccountManager.MakeStraddle(legsCombo).AddToCache();
      var combo = (c: straddle, p: posMin, o: m.leg1.o + m.leg2.o);
      var rest = new[] { (m.leg1.c, p: m.leg1.p - posMin, m.leg1.o), (m.leg2.c, p: m.leg2.p - posMin, m.leg2.o) };
      //HandleMessage2("combo:\n" + combo);
      //HandleMessage2("rest:\n" + rest.Flatter("\n"));
      return (combo, rest);
    }
    static IEnumerable<(Contract c, int p, double o)>
      RePosition(IList<(Contract c, int p, double o)> tradePositions, IEnumerable<((Contract c, int p, double o) combo, (Contract c, int p, double o)[] rest)> parsedPositions) =>
      from trade in tradePositions
      join c in parsedPositions.SelectMany((p => p.rest)) on trade.c.Key equals c.c.Key into tg
      from cc in tg.DefaultIfEmpty(trade)
      where cc.p != 0
      select cc;
    static List<(Contract c, int p, double o)>
      ResortPositions(this IEnumerable<(Contract c, int p, double o)> positions)
      => positions.OrderByDescending(t => t.p.Abs()).ToList();
    static IEnumerable<((Contract c, int p, double o) leg1, (Contract c, int p, double o) leg2)>
      FindStraddle(IList<(Contract c, int p, double o)> positions, (Contract c, int p, double o) leg, bool wide = false) =>
      positions
      .Where(p => p.c.Symbol == leg.c.Symbol)
      .Where(p => wide || p.c.Strike == leg.c.Strike)
      .Where(p => p.c.Right != leg.c.Right)
      .Where(p => p.p.Sign() == leg.p.Sign())
      .Select(m => (m, leg));
    #endregion

  }
}
