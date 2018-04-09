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
    public static (TPositions[] positions, Contract contract)[] MakeComboAll<TPositions>(IEnumerable<(Contract c, int p)> combosAll, IEnumerable<TPositions> positions, Func<TPositions, string, bool> filterByTradingClass) =>
      combosAll
      .Where(c => c.c.IsOption || c.c.IsCombo)
      .GroupBy(combo => (combo.c.Symbol, combo.c.TradingClass, combo.c.Exchange, combo.c.Currency))
      .Where(g => g.Count() > 1)
      .Select(combos =>
        (
        positions.Where(p => filterByTradingClass(p, combos.Key.TradingClass)).ToArray(),
        MakeComboCache(combos.Key.Symbol, combos.Key.Exchange, combos.Key.Currency
          , CombosLegs(combos).OrderBy(c => c.ConId).ToArray())
        )
      )
      .ToArray();

    static ComboLeg ComboLeg((Contract c, int p) combo) =>
      new ComboLeg { ConId = combo.c.ConId, Ratio = combo.p.Abs(), Action = combo.p.Sign() > 0 ? "BUY" : "SELL", Exchange = combo.c.Exchange };
    static IList<ComboLeg> CombosLegs(IEnumerable<(Contract c, int p)> combos) =>
        (from combo in combos
         from leg in (combo.c.ComboLegs ?? new List<ComboLeg> { ComboLeg(combo) }).Do(l => l.Ratio *= combo.p.Abs())
         group leg by leg.ConId into legConId
         select legConId.Select(leg
          => new ComboLeg { ConId = leg.ConId, Ratio = legConId.Sum(l => l.Ratio), Action = leg.Action, Exchange = leg.Exchange }).First()
         ).ToArray();

    static Func<string, string, string, IList<ComboLeg>, Contract> MakeComboCache
      = new Func<string, string, string, IList<ComboLeg>, Contract>(MakeCombo)
      .Memoize(t => (t.Item1, t.Item2, t.Item3, t.Item4.Select(l => $"{l.ConId}{l.Ratio}{l.Action}").Flatter("")));

    static Contract MakeCombo(string instrument, string exchange, string currency, IList<ComboLeg> comboLegs) =>
       new Contract() {
         Symbol = instrument,
         SecType = "BAG",
         Exchange = exchange,
         Currency = currency,
         ComboLegs = new List<ComboLeg>(comboLegs.OrderBy(l => l.ConId))
       }.AddToCache();

    static IEnumerable<Contract> SortCombos(IEnumerable<Contract> options) =>
      (from c in options
       group c by c.Right into gc
       orderby gc.Key
       select gc.OrderByDescending(v => v.Strike)
       ).Concat();
  }
}
