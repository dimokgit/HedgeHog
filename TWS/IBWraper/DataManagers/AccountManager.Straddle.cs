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

    #region Make Straddles
    public IEnumerable<(string straddle, double netPL, double position)> TradeStraddles() {
      var straddles = (
        from trade in GetTrades()
        join c in Contract.Cache() on trade.Pair equals c.Key
        select new { c, trade } into g0
        group g0 by new { g0.c.Symbol, g0.c.Strike } into g
        from strdl in g.OrderBy(v => v.c.Instrument)
        .Buffer(2, 1)
        .Where(b => b.Count == 2)
        .Select(b => b.MashDiffs(x => x.c.Instrument))
        select (strdl.mash, strdl.source.Select(x => x.trade).Gross(), strdl.source.Max(s => s.trade.Position)));
      return straddles;
    }
    public IObservable<(string instrument, double bid, double ask, DateTime time, double delta, double strike)[]>
      CurrentStraddles(string symbol, int count) {
      (IBApi.Contract contract, double bid, double ask, DateTime time) priceEmpty = default;
      return (
        from price in IbClient.ReqPriceSafe(symbol, 1, true)
        from combo in MakeStraddle(symbol, price.bid.Avg(price.ask), 1, 4)
        from p in IbClient.TryGetPrice(combo.contract.Instrument).Select(p => (combo.contract, bid: p.Bid, ask: p.Ask, time: IbClient.ServerTime))
        .ToObservable()
        .Concat(Observable.Defer(() => ReqPrice(IbClient, combo)))
        .Take(1)
        from strike in combo.options.Take(1).Select(o => o.Strike)
        from up in IbClient.TryGetPrice(symbol).Select(p => p.Average)
        select (
          instrument: combo.contract.Instrument,
          p.bid,
          p.ask,
          p.time,//.ToString("HH:mm:ss"),
          delta: p.ask.Avg(p.bid) - (up - strike),
          strike
        )).ToArray()
        .Select(b => b
         .OrderBy(t => t.ask.Avg(t.bid))
         .Select((t, i) => (t, i))
         .OrderBy(t => t.i > 1)
         .ThenBy(t => t.t.ask.Avg(t.t.bid) / t.t.delta)
         .Select(t => t.t)
         .ToArray()
         );
      IObservable<(IBApi.Contract contract, double bid, double ask, DateTime time)> ReqPrice(IBClientCore ib, (IBApi.Contract contract, IList<IBApi.Contract> options) combo) {
        return ib.ReqPrices(combo.contract.SideEffect(Subscribe), 1, false)
          .DefaultIfEmpty(priceEmpty)
          .FirstAsync();
        void Subscribe(IBApi.Contract c) => ib.SetOfferSubscription(c, _ => { });
      }
    }

    public IObservable<(Contract contract, Contract[] options)> MakeStraddle
      (string symbol, double price, int expirationsCount, int count) =>
      IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true, false }, expirationsCount, count * 2)
      //.Take(count*2)
      .ToArray()
      .SelectMany(reqOptions => reqOptions.OrderBy(o => o.Strike.Abs(price))
      .GroupBy(c => new { c.Strike, c.LastTradeDateOrContractMonth })
      .ToDictionary(c => c.Key, c => c.ToArray())
      .Where(c => c.Value.Length == 2)
      .Select(options => (MakeStraddle(options.Value), options.Value)))
      .Take(count);

    public static Contract MakeStraddle(IList<Contract> contractOptions) =>
      MakeStraddleCache(contractOptions[0].Symbol, contractOptions[0].Exchange, contractOptions[0].Currency
        , contractOptions.OrderBy(c => c.Right).Select(c => c.ConId).ToArray());
    public IObservable<Contract> MakeStraddle(string symbol, IList<Contract> contractOptions)
      => IbClient.ReqContractDetailsCached(symbol)
      .Select(cd => cd.Summary)
      .Select(contract => MakeStraddleCache(contract.Instrument, contract.Exchange, contract.Currency, contractOptions.Sort().Select(o => o.ConId).ToArray()));

    static Func<string, string, string, IList<int>, Contract> MakeStraddleCache
      = new Func<string, string, string, IList<int>, Contract>(MakeStraddle).Memoize(t => (t.Item1, t.Item2, t.Item3, t.Item4.Flatter("")));

    static Contract MakeStraddle(string instrument, string exchange, string currency, IList<int> conIds) {
      if(conIds.Count != 2)
        throw new Exception($"MakeStraddle:{new { conIds }}");
      var c = new Contract() {
        Symbol = instrument,
        SecType = "BAG",
        Exchange = exchange,
        Currency = currency
      };
      var call = new ComboLeg() {
        ConId = conIds[0],
        Ratio = 1,
        Action = "BUY",
        Exchange = exchange
      };
      var put = new ComboLeg() {
        ConId = conIds[1],
        Ratio = 1,
        Action = "BUY",
        Exchange = exchange
      };
      c.ComboLegs = new List<ComboLeg> { call, put };
      return c;
    }
    #endregion


    #region Make Butterfly
    public IObservable<(Contract contract, Contract[] options)> MakeButterflies(string symbol, double price) =>
   IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true })
    .ToArray()
    //.Select(a => a.OrderBy(c => c.Strike).ToArray())
    .SelectMany(reqOptions =>
      reqOptions
      .Buffer(3, 1)
      .TakeWhile(b => b.Count == 3)
      .Select(options => MakeButterfly(symbol, options).Select(contract => (contract.AddToCache(), options.ToArray()))))
      .Merge();

    public IObservable<Contract> MakeButterfly(string symbol, IList<Contract> contractOptions)
  => IbClient.ReqContractDetailsCached(symbol)
  .Select(cd => cd.Summary)
  .Select(contract => MakeButterfly(contract.Instrument, contract.Exchange, contract.Currency, contractOptions.Select(o => o.ConId).ToArray()));
    public Contract MakeButterfly(Contract contract, IList<Contract> contractOptions)
      => MakeButterfly(contract.Instrument, contract.Exchange, contract.Currency, contractOptions.Select(o => o.ConId).ToArray());
    Contract MakeButterfly(string instrument, string exchange, string currency, IList<Contract> contractOptions)
      => MakeButterfly(instrument, exchange, currency, contractOptions.Select(o => o.ConId).ToArray());
    Contract MakeButterfly(string instrument, string exchange, string currency, IList<int> conIds) {
      if(conIds.Count != 3)
        throw new Exception($"{nameof(MakeButterfly)}:{new { conIds }}");
      //if(conIds.Zip(conIds.Skip(1), (p, n) => (p, n)).Any(t => t.p <= t.n)) throw new Exception($"Butterfly legs are out of order:{string.Join(",", conIds)}");
      var c = new Contract() {
        Symbol = instrument,
        SecType = "BAG",
        Exchange = exchange,
        Currency = currency
      };
      var left = new ComboLeg() {
        ConId = conIds[0],
        Ratio = 1,
        Action = "BUY",
        Exchange = exchange
      };
      var middle = new ComboLeg() {
        ConId = conIds[1],
        Ratio = 2,
        Action = "SELL",
        Exchange = exchange
      };
      var right = new ComboLeg() {
        ConId = conIds[2],
        Ratio = 1,
        Action = "BUY",
        Exchange = exchange
      };
      c.ComboLegs = new List<ComboLeg> { left, middle, right };
      return c;
    }
    #endregion

  }
  public static class AccountManagerMixins {
    #region Parse Combos
    public static IList<(Contract contract, int position, double open)> ParseCombos(this ICollection<(Contract c, int p, double o)> positions) {
      var trades = positions.ToList();
      List<(Contract c, int p, double o)> combos = new List<(Contract c, int p, double o)>();
      while(trades.Any()) {
        var trade = ResortPositions(trades).First();
        var match = FindStraddle(trades, trade).Concat(FindStraddle(trades, trade, true)).Take(1);
        if(match.IsEmpty()) {
          trades.Remove(trade);
        } else {
          var parsed = match.Select(ParseMatch).ToArray();
          combos.AddRange(parsed.Select(p => p.combo));
          trades = RePosition(trades, parsed).ToList();
        }
        //ver("Trades new:\n" + trades.Flatter("\n"));
      }
      return combos;
    }
    static ((Contract c, int p, double o) combo, (Contract c, int p, double o)[] rest) ParseMatch(((Contract c, int p, double o) leg1, (Contract c, int p, double o) leg2) m) {
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
    static IEnumerable<(Contract c, int p, double o)> RePosition(IList<(Contract c, int p, double o)> tradePositions, IEnumerable<((Contract c, int p, double o) combo, (Contract c, int p, double o)[] rest)> parsedPositions) =>
      from trade in tradePositions
      join c in parsedPositions.SelectMany((p => p.rest)) on trade.c.Key equals c.c.Key into tg
      from cc in tg.DefaultIfEmpty(trade)
      where cc.p != 0
      select cc;
    static IList<(Contract c, int p, double o)> ResortPositions(IEnumerable<(Contract c, int p, double o)> positions)
      => positions.OrderByDescending(t => t.p.Abs()).ToArray();
    static IEnumerable<((Contract c, int p, double o) leg1, (Contract c, int p, double o) leg2)> FindStraddle(IList<(Contract c, int p, double o)> positions, (Contract c, int p, double o) leg, bool wide = false) =>
      positions
      .Where(p => p.c.Symbol == leg.c.Symbol)
      .Where(p => wide || p.c.Strike == leg.c.Strike)
      .Where(p => p.c.Right != leg.c.Right)
      .Where(p => p.p.Sign() == leg.p.Sign())
      .Select(m => (m, leg));
    #endregion

  }
}
