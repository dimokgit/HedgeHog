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
    public IObservable<(string instrument, double bid, double ask, DateTime time, double delta, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo)[]>
  CurrentBullPuts(string symbol, double strikeLevel, int expirationDaysSkip, int count, int gap) {
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from price in IbClient.ReqPriceSafe(cd.Contract).Select(p => p.ask.Avg(p.bid))
        from combo in MakeBullPuts(symbol, strikeLevel.IfNaN(price), expirationDaysSkip, 1, count, gap)
        from p in IbClient.ReqPriceSafe(combo.contract).DefaultIfEmpty()
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
    (string symbol, double price, int expirationDaysSkip, int expirationsCount, int count, int gap) =>
      IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true, false }, expirationDaysSkip, expirationsCount, (count + gap + 1) * 2)
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

    IObservable<(Contract currentContract, Contract rollContract, ComboTrade currentTrade)> CreateRoll(string currentSymbol, string rollSymbol) =>
      (from cd in IbClient.ReqContractDetailsCached(currentSymbol)
       let cc = cd.Contract.ThrowIf(contract => !contract.IsOption)
       from rcd in IbClient.ReqContractDetailsCached(rollSymbol)
       from uc in cc.UnderContract
       from ct in ComboTrades(5)
       where ct.contract.ConId == cc.ConId
       select (cc, MakeRollContract(cc, rcd.Contract, uc), ct));

    static Contract MakeRollContract(Contract current, Contract roll, Contract under) {
      var c = new Contract() {
        Symbol = under.Symbol,
        SecType = "BAG",
        Exchange = current.Exchange,
        Currency = current.Currency
      };
      var call = new ComboLeg() {
        ConId = current.ConId,
        Ratio = 1,
        Action = "BUY",
        Exchange = current.Exchange
      };
      var put = new ComboLeg() {
        ConId = roll.ConId,
        Ratio = 1,
        Action = "SELL",
        Exchange = roll.Exchange
      };
      c.ComboLegs = new List<ComboLeg> { call, put };
      return c.AddToCache();
    }

  }
}

