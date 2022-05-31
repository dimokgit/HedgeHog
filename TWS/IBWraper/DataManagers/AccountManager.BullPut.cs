using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IBApp {
  public partial class AccountManager {
    public IObservable<(string instrument, double bid, double ask, DateTime time, double delta, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo)[]>
  CurrentBullPuts(string symbol, double strikeLevel, int expirationDaysSkip, int count, int gap) {
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from price in cd.Contract.ReqPriceSafe().Select(p => p.ask.Avg(p.bid))
        from combo in MakeBullPuts(symbol, strikeLevel.IfNaN(price), expirationDaysSkip, count, gap)
        from p in combo.contract.ReqPriceSafe().DefaultIfEmpty()
        let strikeAvg = combo.options.Average(o => o.Strike)
        select (
          instrument: combo.contract.Instrument,
          p.bid,
          p.ask,
          p.time,//.ToString("HH:mm:ss"),
          delta: p.ask.Avg(p.bid) - combo.options.Sum(o => o.IntrinsicValue(price)),
          strikeAvg,
          price,
          breakEven: (up: strikeAvg + price, dn: strikeAvg - price),
          combo
        )).ToArray()
        .Select(b => b
         .OrderByDescending(t => t.delta)
         //.Select((t, i) => (t, i))
         //.OrderBy(t => t.i > 1)
         //.ThenBy(t => t.t.ask.Avg(t.t.bid) / t.t.delta)
         //.ThenByDescending(t => t.t.delta)
         //.Select(t => t.t)
         .ToArray()
         );
    }

    public IObservable<(Contract contract, Contract[] options)> MakeBullPuts
    (string symbol, double price, int expirationDaysSkip,  int count, int gap) =>
      IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true, false }, expirationDaysSkip,  (count + gap + 1) * 2, c => true)
      .Where(c => c.IsPut)
      //.Take(count*2)
      .ToArray()
      .SelectMany(reqOptions => {
        var puts = reqOptions.OrderBy(o => o.Strike.Abs(price)).Take((count + gap + 1) * 2).OrderByDescending(c => c.Strike).ToArray();
        return puts.Zip(puts.Skip(gap + 1), (sell, buy) => new[] { sell, buy })
          .Select(cp => (MakeBullPut(cp), cp))
          .OrderBy(cp => cp.cp.Average(c => c.Strike).Abs(price));
      })
      .Take(count);

    public static Contract MakeBullPut(IList<Contract> contractOptions) =>
      MakeBullPutCache(contractOptions[0].Symbol, contractOptions[0].Exchange, contractOptions[0].Currency
        , contractOptions.Select(c => c.ConId).ToArray());
    static Func<string, string, string, IList<int>, Contract> MakeBullPutCache
      = new Func<string, string, string, IList<int>, Contract>(MakeBullPut)
      .Memoize(t => (t.Item1, t.Item2, t.Item3, t.Item4.Flatter("")));
    static Contract MakeBullPut(string instrument, string exchange, string currency, IList<int> conIds) {
      if(conIds.Count != 2)
        throw new Exception($"{nameof(MakeBullPut)}:{new { conIds }}");
      var c = new Contract() {
        Symbol = instrument,
        SecType = "BAG",
        Exchange = exchange,
        Currency = currency
      };
      var call = new ComboLeg() {
        ConId = conIds[0],
        Ratio = 1,
        Action = "SELL",
        Exchange = exchange
      };
      var put = new ComboLeg() {
        ConId = conIds[1],
        Ratio = 1,
        Action = "BUY",
        Exchange = exchange
      };
      c.ComboLegs = new List<ComboLeg> { call, put };
      return c.AddToCache();
    }

    public static HedgeCombo MakeUnderCombo(IEnumerable<ComboTrade> combos, IEnumerable<string> selection) {
      var ct = combos.Where(ct => selection.Contains(ct.contract.Instrument));
      return MakeUnderCombo(ct);
    }
    public static HedgeCombo MakeUnderCombo(IEnumerable<ComboTrade> combos) {
      var cmbs = combos.Select(p => (p.contract, Position: p.position)).OrderBy(p => p.contract.IsOption).ToArray();
      return AccountManager.MakeUnderCombo(1, cmbs[0].contract, cmbs[1].contract, cmbs[0].Position, cmbs[1].Position);

    }
    public static HedgeCombo MakeUnderCombo(int quantity, Contract c1, Contract c2, double ratio1, double ratio2) {
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
      contract.Exchange = c1.IsFuture || c1.IsFutureOption ? c1.Exchange : EXCHAGE;
      //contract.TradingClass = "COMB";

      ComboLeg leg1 = new ComboLeg();
      leg1.ConId = c1.ConId;
      leg1.Ratio = r1 / gcd;
      leg1.Action = ratio1 > 0 ? "BUY" : "SELL";
      leg1.Exchange = contract.Exchange;

      ComboLeg leg2 = new ComboLeg();
      leg2.ConId = c2.ConId;
      leg2.Ratio = r2 / gcd;
      string action = ratio2 > 0 ? "BUY" : "SELL";
      leg2.Action = action;
      leg2.Exchange = contract.Exchange;

      contract.ComboLegs = new List<ComboLeg>();
      contract.ComboLegs.Add(leg1);
      contract.ComboLegs.Add(leg2);

      var cache = Contract.Contracts.Any() ? contract.FromCache().Single() : contract;
      return (cache, gcd, GetComboMultiplier(cache));
    }
    IObservable<(Contract currentContract, HedgeCombo rollContract, ComboTrade currentTrade)> CreateRoll(string currentSymbol, int quantity, string rollSymbol, int rollQuantity) =>
      (from cd in IbClient.ReqContractDetailsCached(currentSymbol)
       let cc = cd.Contract.ThrowIf(contract => !contract.IsOption)
       from rcd in IbClient.ReqContractDetailsCached(rollSymbol)
       from uc in cc.UnderContract
       from ct in ComboTrades(5)
       where ct.contract.ConId == cc.ConId
       select (cc, MakeRollContract(cc, quantity, rcd.Contract, rollQuantity, uc), ct));

    IObservable<(Contract currentContract, HedgeCombo rollContract, ComboTrade currentTrade)> CreateRoll(Contract cc, int quantity
      , Contract rc, int rollQuantity) =>
      (from uc in cc.UnderContract.ToObservable()
       from ct in ComboTrades(5)
       where ct.contract.ConId == cc.ConId
       select (cc, MakeRollContract(cc, quantity, rc, rollQuantity, uc), ct));

    static HedgeCombo MakeRollContract(Contract current, int quantity, Contract roll, int rollQuantity, Contract under) {
      var gcd = new[] { quantity, rollQuantity }.GCD();
      var c = new Contract() {
        Symbol = under.Symbol,
        SecType = "BAG",
        Exchange = current.Exchange,
        Currency = current.Currency
      };
      var call = new ComboLeg() {
        ConId = current.ConId,
        Ratio = quantity / gcd,
        Action = "BUY",
        Exchange = current.Exchange
      };
      var put = new ComboLeg() {
        ConId = roll.ConId,
        Ratio = rollQuantity / gcd,
        Action = "SELL",
        Exchange = roll.Exchange
      };
      c.ComboLegs = new List<ComboLeg> { call, put };
      return (c.AddToCache(), gcd, GetComboMultiplier(c));
    }

  }
}

