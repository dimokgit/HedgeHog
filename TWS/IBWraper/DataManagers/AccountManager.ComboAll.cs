using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using static HedgeHog.MathCore;
namespace IBApp {
  public partial class AccountManager {
    public static (TPositions[] positions, (Contract contract, int positions) contract)[] MakeComboAll<TPositions>
      (IEnumerable<(Contract c, int p)> combosAll, IEnumerable<TPositions> positions, Func<TPositions, string, bool> filterByTradingClass) =>
      combosAll
      .Where(c => c.c.HasOptions)
      .GroupBy(combo => (combo.c.Symbol, combo.c.TradingClass, combo.c.Exchange, combo.c.Currency, combo.c.Expiration))
      .Where(g => g.Count() > 1)
      .Select(combos =>
        (
        positions.Where(p => filterByTradingClass(p, combos.Key.TradingClass)).ToArray(),
        MakeComboCache(combos.Key.Symbol, combos.Key.Exchange, combos.Key.Currency
          , CombosLegs(combos).OrderBy(c => c.ConId).ToArray())
        )
      )
      .ToArray();
    public static (TPositions[] positions, (Contract contract, int positions) contract)[] MakeUnderlyingComboAll<TPositions>(IEnumerable<(Contract c, int p)> combosAll, IEnumerable<TPositions> positions) {
      var isUnderCombo = combosAll.Count(c => c.c.IsOption) == 1 && combosAll.Count(c => !c.c.IsOption) == 1;
      var underCombos = combosAll
      .GroupBy(combo => combo.c.Symbol)
      .Where(g => g.Count() > 1)
      .Select(g => (option: g.Where(c => c.c.IsOption).ToArray(), under: g.Where(c => !c.c.IsOption).ToArray()))
      .Where(t => t.option.Length == 1 && t.under.Length == 1)
      .Select(combos => (positions.ToArray(), combos.under.Single()))
      .ToArray();
      return underCombos;
    }
    static ComboLeg ComboLeg((Contract c, int p) combo) =>
      new ComboLeg { ConId = combo.c.ConId, Ratio = combo.p.Abs(), Action = "BUY", Exchange = combo.c.Exchange };
    //new ComboLeg { ConId = combo.c.ConId, Ratio = combo.p.Abs(), Action = combo.p.Sign() > 0 ? "BUY" : "SELL", Exchange = combo.c.Exchange };
    static IList<ComboLeg> CombosLegs(IEnumerable<(Contract c, int p)> combos) =>
        (from combo in combos
         from leg in (combo.c.ComboLegs ?? new List<ComboLeg> { ComboLeg(combo) })
         group leg by leg.ConId into legConId
         select legConId.Select(leg
          => new ComboLeg { ConId = leg.ConId, Ratio = legConId.Sum(l => l.Ratio), Action = leg.Action, Exchange = leg.Exchange }).First()
         ).ToArray();

    static Func<string, string, string, IList<ComboLeg>, (Contract contract, int positions)> MakeComboCache
      = new Func<string, string, string, IList<ComboLeg>, (Contract contract, int positions)>(MakeCombo)
      .Memoize(t => (t.Item1, t.Item2, t.Item3, t.Item4.Select(l => $"{l.ConId}{l.Ratio}{l.Action}").Flatter("")));

    static (Contract contract, int positions) MakeCombo(string instrument, string exchange, string currency, IList<ComboLeg> comboLegs) {
      var positions = comboLegs.Select(l => l.Ratio).ToArray().GCD();
      comboLegs.ForEach(l => l.Ratio /= positions);
      var contract = new Contract() {
        Symbol = instrument,
        SecType = "BAG",
        Exchange = exchange,
        Currency = currency,
        ComboLegs = new List<ComboLeg>(comboLegs.OrderBy(l => l.ConId))
      }.AddToCache();
      return (contract, positions);
    }

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

    static IEnumerable<Contract> SortCombos(IEnumerable<Contract> options) =>
      (from c in options
       group c by c.Right into gc
       orderby gc.Key
       select gc.OrderByDescending(v => v.Strike)
       ).Concat();
  }
}
