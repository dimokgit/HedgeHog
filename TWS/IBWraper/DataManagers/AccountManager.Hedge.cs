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
    static string[] _hedgeExcepts = new[] { "ES", "MES" };
    public IObservable<ComboTrade> MakeComboHedgeFromPositions(IEnumerable<Position> positions, IList<string> instruments = null) {
      var a = (from g in SelectedPositions(positions, instruments).Concat(HedgedPositions(positions))//.OrderBy(p => p.position.contract.Instrument)
               where g.Length == 2
               let o = g[0]
               let t = g[1]
               where /*g.Any(c => c.position.contract.IsOption) || */g.Select(p => p.position.contract.Symbol).Any(s => !_hedgeExcepts.Contains(s))
               let hc = MakeHedgeCombo(1, o.position.contract, t.position.contract, o.position.position.Abs(), -o.position.position.Sign() * t.position.position)
               let quantity = hc.quantity * g.First().position.position.Sign()
               let isBuy = quantity > 0
               let pl = g.Sum(p => p.pl)
               let mul = hc.multiplier// g.Select(x => x.position.contract.ComboMultiplier).ToArray().multiplier() * quantity
               let openPrice = g.Sum(p => p.position.open) / mul
               let closePrice = g.Sum(p => p.close) / mul
               let order = OrderContractsInternal.Items.OpenByContract(hc.contract).Where(oc => oc.order.IsBuy != isBuy).Take(1).ToList()
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
    public static IEnumerable<Position> IsPartOfStraddle(Position option, IEnumerable<Position> positions) =>
      positions.Where(p => p.contract.IsOption && option.contract.IsOption
      && option.IsBuy == p.IsBuy
      && p.contract.Expiration == option.contract.Expiration
      && p.contract.IsCall == option.contract.IsPut
      && p.contract.UnderContract.SequenceEqual(option.contract.UnderContract));
    public static IEnumerable<Position> CoveredOption(Position under, IEnumerable<Position> positions) =>
      positions
      .Where(p => !IsPartOfStraddle(p, positions).Any() && p.contract.IsOption && under.IsBuy == p.contract.IsCall && !p.IsBuy && p.contract.Expiration <= DateTime.Today.AddDays(1) && p.contract.UnderContract.Any(u => u.Key == under.contract.Key));

    public static IObservable<(Position position, double close, double pl, double closePrice)[]>
      HedgedPositions(IEnumerable<Position> positions) {
      return (from p0 in positions.Sort().ToObservable()
              let cp = (p0.position.Abs() /*+ CoveredOption(p0, positions).Sum(p => p.position)*/) * p0.position.Sign()
              let p = p0.position == cp ? p0 : new Position(p0.contract, cp, p0.averageCost)
              //where p.contract.IsFuture || p.contract.IsStock// || p.contract.IsOption
              where p.position != 0 && !p.contract.IsOption
              from price in p.contract.ReqPriceSafe()
              let closePrice = p.position > 0 ? price.bid : price.ask
              let close = closePrice * p.contract.ComboMultiplier * p.position
              let t = (p, close, close - p.open, closePrice)
              group t by t.p.contract.SecType into g
              from a in g/*.OrderBy(p => p.p.contract.Instrument)*/.ToArray()
              where a.Distinct(t => t.p.contract.Symbol).Count() == 2
              select a
      );
    }
    public static IObservable<(Position position, double close, double pl, double closePrice)[]>
      SelectedPositions(IEnumerable<Position> positions, IList<string> instruments) {
      return (from p0 in positions.Sort().OrderBy(p => p.contract.IsOption).ToObservable()
              let cp = (p0.position.Abs() /*+ CoveredOption(p0, positions).Sum(p => p.position)*/) * p0.position.Sign()
              let p = p0.position == cp ? p0 : new Position(p0.contract, cp, p0.averageCost)
              //where p.contract.IsFuture || p.contract.IsStock// || p.contract.IsOption
              where instruments != null && instruments.Contains(p.contract.Instrument) && p.position != 0
              from price in p.contract.ReqPriceSafe()
              let closePrice = p.position > 0 ? price.bid : price.ask
              let close = closePrice * p.contract.ComboMultiplier * p.position
              let t = (p, close, close - p.open, closePrice)
              group t by 1 into g
              from a in g/*.OrderBy(p => p.p.contract.Instrument)*/.ToArray()
              where a.Count() >= 2
              select a
      );
    }
    public static IObservable<HedgeCombo> MakeHedgeComboSafe(int quantity, string s1, string s2, double ratio1, double ratio2, bool isInTest) =>
      from c1 in s1.ContractFactory().ReqContractDetailsCached().Select(cd => cd.Contract)
      from c2 in s2.ContractFactory().ReqContractDetailsCached().Select(cd => cd.Contract)
      from h in MakeHedgeComboSafe(quantity, c1, c2, ratio1, ratio2, isInTest)
      select h;
    public static IObservable<HedgeCombo> MakeHedgeComboSafe(int quantity, Contract c1, Contract c2, double ratio1, double ratio2, bool isInTest) =>
      from cd1 in isInTest ? Observable.Return(c1.SetTestConId(isInTest, 0)) : c1.ReqContractDetailsCached().Select(cd => cd.Contract)
      from cd2 in isInTest ? Observable.Return(c2.SetTestConId(isInTest, 0)) : c2.ReqContractDetailsCached().Select(cd => cd.Contract)
      select MakeHedgeCombo(quantity, cd1, cd2, ratio1, ratio2).SideEffect(x => x.contract.SetTestConId(isInTest, 0));

    public static HedgeCombo MakeHedgeCombo(int quantity, Contract c1, Contract c2, double ratio1, double ratio2) {
      if(!c1.IsOption && c2.IsOption) return MakeUnderCombo(quantity, c1, c2, ratio1, ratio2);
      if(c1.ConId == 0)
        throw new Exception($"ComboLeg contract1 has ConId = 0");
      if(c2.ConId == 0)
        throw new Exception($"ComboLeg contract2 has ConId = 0");
      int r1 = (ratio1 * quantity).ToInt().Max(1);
      int r2 = (ratio2.Abs() * quantity).ToInt().Max(1);
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
      if(c1.PrimaryExch == c1.PrimaryExch)
        contract.PrimaryExch = c1.PrimaryExch;
      const string EXCHAGE = "SMART";
      contract.Exchange = EXCHAGE;
      contract.TradingClass = "COMB";

      ComboLeg leg1 = new ComboLeg();
      leg1.ConId = c1.ConId;
      leg1.Ratio = r1 / gcd;
      leg1.Action = "BUY";
      leg1.Exchange = c1.IsFuture ? c1.PrimaryExch.IfEmpty(c1.Exchange) : EXCHAGE;

      ComboLeg leg2 = new ComboLeg();
      leg2.ConId = c2.ConId;
      leg2.Ratio = r2 / gcd;
      string action = ratio2 < 0 ? "BUY" : c1.IsCall == c2.IsCall ? "SELL" : "BUY";
      leg2.Action = action;
      leg2.Exchange = c2.IsFuture ? c2.PrimaryExch.IfEmpty(c2.Exchange) : EXCHAGE;

      contract.ComboLegs = new List<ComboLeg>();
      contract.ComboLegs.Add(leg1);
      contract.ComboLegs.Add(leg2);

      var cache = Contract.Contracts.Any() ? contract.FromCache().Single() : contract;
      return (cache, gcd, GetComboMultiplier(cache));
    }

    public static HedgeCombo MakeStockCombo(double amount, IList<(Contract contract, double price)> legs) {
      const string EXCHAGE = "SMART";
      Contract contract = new Contract();

      var c1 = legs.First().contract;
      var symbol = c1.IsFuture || c1.IsOption
        ? c1.Symbol.IfEmpty(Regex.Match(c1.LocalSymbol, "(.+).{2}$").Groups[1] + "").ThrowIf(contractSymbol => contractSymbol.IsNullOrWhiteSpace())
        : legs.Select(l => l.contract.Symbol).OrderBy(s => s).Flatter(",");
      contract.Symbol = symbol;
      contract.SecType = "BAG";
      contract.Currency = "USD";
      contract.PrimaryExch = EXCHAGE;
      contract.Exchange = EXCHAGE;
      contract.TradingClass = "COMB";

      var comboLegs = legs.Select(l => {
        var c = l.contract;
        ComboLeg leg1 = new ComboLeg();
        leg1.ConId = c.ConId;
        leg1.Ratio = (amount / l.price).ToInt();
        leg1.Action = "BUY";
        leg1.Exchange = c.IsFuture ? c.PrimaryExch.IfEmpty(c.Exchange) : EXCHAGE;
        return leg1;
      });
      var gdc = comboLegs.Select(l => l.Ratio).ToArray().GCD();
      comboLegs.ForEach(l => l.Ratio /= gdc);
      contract.ComboLegs = new List<ComboLeg>();
      contract.ComboLegs.AddRange(comboLegs);

      var cache = Contract.Contracts.Any() ? contract.FromCache().Single() : contract;
      return (cache, gdc, GetComboMultiplier(cache));
    }

    private static double GetComboMultiplier(Contract cache) => cache.HedgeComboPrimary((m1, m2)
      => LogMessage.Send(new { AccountManager = new { GetComboMultiplier = new { NotFound = new { m1, m2 } } } })).Single().ComboMultiplier;
  }

  public struct HedgeCombo {
    public Contract contract;
    public int quantity;
    public double multiplier;

    public HedgeCombo(Contract contract, int quantity, double multiplier) {
      this.contract = contract;
      this.quantity = quantity;
      this.multiplier = multiplier;
    }

    public override bool Equals(object obj) => obj is HedgeCombo other && EqualityComparer<Contract>.Default.Equals(contract, other.contract) && quantity == other.quantity;

    public override int GetHashCode() {
      int hashCode = -1882738349;
      hashCode = hashCode * -1521134295 + EqualityComparer<Contract>.Default.GetHashCode(contract);
      hashCode = hashCode * -1521134295 + quantity.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out Contract contract, out int quantity, out double multiplier) {
      contract = this.contract;
      quantity = this.quantity;
      multiplier = this.multiplier;
    }

    public static implicit operator (Contract contract, int quantity, double multiplier)(HedgeCombo value) => (value.contract, value.quantity, value.multiplier);
    public static implicit operator HedgeCombo((Contract contract, int quantity, double multiplier) value) => new HedgeCombo(value.contract, value.quantity, value.multiplier);
  }
}
