﻿using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using CURRENT_OPTIONS = System.Collections.Generic.IList<(string instrument, double bid, double ask, System.DateTime time, double delta, double strikeAvg, double underPrice, (double up, double dn) breakEven, IBApi.Contract option, double deltaBid, double deltaAsk)>;
using CURRENT_ROLLOVERS = System.IObservable<(IBApp.ComboTrade trade, IBApi.Contract roll, int days, double bid, double ppw, double amount, double dpw, double perc, double delta)>;
namespace IBApp {
  public partial class AccountManager {

    #region Make Straddles
    public IObservable<(string instrument, double bid, double ask, DateTime time, double delta, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo, double deltaBid, double deltaAsk)[]>
      CurrentStraddles(string symbol, double strikeLevel, int expirationDaysSkip, int count, int gap) {
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from price in IbClient.ReqPriceSafe(cd.Contract, 5, false).Select(p => p.ask.Avg(p.bid))
        from combo in MakeStraddles(symbol, strikeLevel.IfNaN(price), expirationDaysSkip, 1, count, gap)
        from p in IbClient.ReqPriceSafe(combo.contract, 2, true).DefaultIfEmpty()
        select CurrentComboInfo(price, combo, p)).ToArray()
        .Select(b => b
         .OrderBy(t => t.ask.Avg(t.bid))
         .Select((t, i) => ((t, i)))
         .OrderBy(t => t.i > 1)
         .ThenBy(t => t.t.ask.Avg(t.t.bid) / t.t.delta)
         .ThenByDescending(t => t.t.delta)
         .Select(t => t.t)
         .ToArray()
         );
    }

    private static (string instrument, double bid, double ask, DateTime time, double delta, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo, double deltaBid, double deltaAsk)
      CurrentComboInfo(double underPrice, (Contract contract, Contract[] options) combo, (double bid, double ask, DateTime time, double delta) p) {
      var strikeAvg = combo.options.Average(o => o.Strike);
      double pa = p.ask.Avg(p.bid);
      var iv = combo.options.Sum(o => o.IntrinsicValue(underPrice));
      return (
              instrument: combo.contract.Instrument,
              p.bid,
              p.ask,
              p.time,//.ToString("HH:mm:ss"),
              delta: pa - iv,
              strikeAvg,
              underPrice,
              breakEven: (up: strikeAvg + pa, dn: strikeAvg - pa),
              combo,
              deltaBid: p.bid - iv,
              deltaAsk: p.ask - iv
            );
    }

    public IObservable<(string instrument, double bid, double ask, DateTime time, double delta, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo)[]>
      CurrentStraddles(string symbol, int expirationDaysSkip, int count, int gap) {
      (IBApi.Contract contract, double bid, double ask, DateTime time) priceEmpty = default;
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from underPrice in IbClient.ReqPriceSafe(cd.Contract, 5, false).Select(p => p.bid)
        from combo in MakeStraddles(symbol, underPrice, expirationDaysSkip, 1, count, gap)
        from p in IbClient.ReqPriceSafe(combo.contract, 2, true).DefaultIfEmpty()
        let strikeAvg = combo.options.Average(o => o.Strike)
        select (
          instrument: combo.contract.Instrument,
          p.bid,
          p.ask,
          p.time,//.ToString("HH:mm:ss"),
          delta: combo.options.Sum(o => o.ExtrinsicValue(p.bid.Avg(p.ask), underPrice)),
          strikeAvg,
          underPrice,
          breakEven: (up: strikeAvg + underPrice, dn: strikeAvg - underPrice),
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
    public IObservable<(Contract contract, Contract[] options)> MakeStraddles
      (string symbol, double price, int expirationDaysSkip, int expirationsCount, int count, int gap) =>
      IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true, false }, expirationDaysSkip, expirationsCount, count * 2 + gap * 2)
      //.Take(count*2)
      .ToArray()
      .SelectMany(reqOptions => {
        var strikes = reqOptions.OrderBy(o => o.Strike.Abs(price)).Take(count * 2 + gap * 2).OrderBy(c => c.Strike).ToArray();
        var calls = strikes.Where(c => c.Right == "C");
        var puts = strikes.Where(c => c.Right == "P");
        return calls.Skip(gap).Zip(puts, (call, put) => new[] { call, put })
          .Select(cp => (MakeStraddle(cp), cp))
          .OrderBy(cp => cp.cp.Average(c => c.Strike).Abs(price));
      })
      .Take(count);

    public static Contract MakeStraddle(IList<Contract> contractOptions) =>
      MakeStraddleCache(contractOptions[0].Symbol, contractOptions[0].Exchange, contractOptions[0].Currency
        , contractOptions.OrderBy(c => c.Right).Select(c => c.ConId).ToArray());
    public IObservable<Contract> MakeStraddle(string symbol, IList<Contract> contractOptions)
      => IbClient.ReqContractDetailsCached(symbol)
      .Select(cd => cd.Contract)
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
      return c.AddToCache();
    }
    #endregion

    #region Make Butterfly
    public IObservable<(Contract contract, Contract[] options)> MakeButterflies(string symbol, double price) =>
   IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true }, 1, 1, 4)
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
  .Select(cd => cd.Contract)
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

    #region Options
    //public IObservable<CURRENT_OPTIONS> CurrentOptions(string symbol, double strikeLevel, int expirationDaysSkip, int count) =>
    public IObservable<CURRENT_OPTIONS> CurrentOptions(string symbol, double strikeLevel, int expirationDaysSkip, int count, Func<Contract, bool> filter) =>
      (from cd in IbClient.ReqContractDetailsCached(symbol)
       where count > 0
       from price in IbClient.ReqPriceSafe(cd.Contract, 5, false).Select(p => p.ask.Avg(p.bid))
       from option in MakeOptions(symbol, strikeLevel.IfNaNOrZero(price), expirationDaysSkip, 1, count * 2)
       where filter(option)
       from p in IbClient.ReqPriceSafe(option, 2, true).DefaultIfEmpty()
       let pa = p.ask.Avg(p.bid)
       select (
         instrument: option.Instrument,
         p.bid,
         p.ask,
         p.time,//.ToString("HH:mm:ss"),
         delta: option.ExtrinsicValue(pa, price),
         option.Strike,
         price,
         breakEven: (up: option.Strike + pa, dn: option.Strike - pa),
         option,
         deltaBid: option.ExtrinsicValue(p.bid, price),
         deltaAsk: option.ExtrinsicValue(p.ask, price)
       ))
      .ToArray()
      .Select(b => b
      .OrderBy(t => t.ask.Avg(t.bid))
      .Select((t, i) => (t, i))
      .OrderBy(t => t.i > 3)
      .ThenBy(t => t.t.ask.Avg(t.t.bid) / t.t.delta)
      .ThenBy(t => t.t.option.Right)
      .Select(t => t.t)
      .ToArray());

    public IObservable<Contract> MakeOptions
      (string symbol, double price, int expirationDaysSkip, int expirationsCount, int count) =>
      IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true, false }, expirationDaysSkip, expirationsCount, count * 2)
      //.Take(count*2)
      .ToArray()
      .SelectMany(reqOptions => {
        return reqOptions.OrderBy(o => o.Strike.Abs(price)).Take(count * 2).ToArray();
      })
      .Take(count);
    #endregion

    static string _spy(string text) => $"{DateTime.Now:mm:ss.f}: {text} -- ";
    static string VXXFix(string symbol) => symbol == "VXX" ? "VXXB" : symbol;
    public IObservable<Contract[]> CurrentRollOvers(string symbol, bool useStraddle, int strikes, int weeks) =>
      (from cd in IbClient.ReqContractDetailsCached(symbol)
       where strikes > 0 && weeks > 0
       let contract = cd.Contract
       from underContract in cd.Contract.IsOption
         ? IbClient.ReqContractDetailsCached(VXXFix(cd.UnderSymbol)).Select(ucd => ucd.Contract)
         : Observable.Return(cd.Contract)
       let underSymbol = underContract.LocalSymbol
       let expiration = contract.IsOption ? contract.Expiration : DateTime.Now.Date.AddDays(0)
       from allStkExp in IbClient.ReqStrikesAndExpirations(underSymbol)//.Spy(_spy("AllStrikesAndExpirations"))
       let exps = allStkExp.expirations.Where(ex => ex > expiration && ex <= expiration.AddDays(weeks * 7)).OrderBy(ex => ex).ToArray()
       from under in IbClient.ReqContractDetailsCached(underSymbol)//.Spy(_spy("ReqContractDetailsCached"))
       from price in IbClient.ReqPriceSafe(under.Contract, 2000, true)//.Spy(_spy("ReqPriceSafe"))
       let priceAvg = price.ask.Avg(price.bid)
       let strikes2 = allStkExp.strikes.OrderBy(strike => strike.Abs(priceAvg)).ToArray()
       from strikeFirst in strikes2.Take(1)
       let strikes3 = strikes2.Where(strike => !contract.IsOption || (contract.IsCall && strike >= strikeFirst) || (contract.IsPut && strike <= strikeFirst)).Take(strikes).ToArray()
       from strike in strikes3
       from exp in exps
       from cds in IbClient.ReqOptionChainOldCache(underSymbol, exp, strike)//.Spy(_spy("ReqOptionChainOldCache 2"))
       from cdRoll in cds
       where !contract.IsOption || useStraddle || contract.IsCall == cdRoll.IsCall
       group cdRoll by (cdRoll.Strike, cdRoll.LastTradeDateOrContractMonth) into g
       from sr in g.ToArray()
       select sr.Length == 1 ? sr.Single() : MakeStraddle(sr)
      ).ToArray()
      .Select(a => a.OrderBy(c => c.Expiration).ThenBy(c => c.Strike).ToArray());//.Spy(_spy("CurrentRollOvers"));

    public CURRENT_ROLLOVERS CurrentRollOver(string symbol, bool useStraddle, int strikesCount, int weeks) =>
       from yes in Observable.Return(!symbol.IsNullOrEmpty())
       where yes
       from cd in IbClient.ReqContractDetailsCached(symbol)
       from trade in ComboTrades(1).DefaultIfEmpty(new ComboTrade(cd.Contract))
       from cro in CurrentRollOverImpl(trade, cd.Contract, useStraddle, strikesCount, weeks)
       select cro;

    public CURRENT_ROLLOVERS CurrentRollOver(string underSymbol, bool? isCall, DateTime expiration, int strikesCount, int weeks) =>
      from contract in IbClient.ReqContractDetailsCached(underSymbol)
      from roll in CurrentRollOverImpl2(new ComboTrade(contract.Contract), isCall, expiration, strikesCount, weeks)
      select roll;

    CURRENT_ROLLOVERS CurrentRollOverImpl(ComboTrade trade, Contract contractFilter, bool useStraddle, int strikesCount, int weeks) =>
      from yes in Observable.Return(trade.contract == contractFilter && strikesCount > 0 && weeks > 0)
      where yes
      let symbol = contractFilter.LocalSymbol
      let IsCall = useStraddle ? (bool?)null : contractFilter.IsCall
      let Expiration = contractFilter.IsOption ? contractFilter.Expiration : IbClient.ServerTime
      from roll in CurrentRollOverImpl2(trade, IsCall, Expiration, strikesCount, weeks)
      select roll;

    public CURRENT_ROLLOVERS CurrentRollOverImpl2(ComboTrade trade, bool? IsCall, DateTime Expiration, int strikesCount, int weeks) =>
      from yes in Observable.Return(strikesCount > 0 && weeks > 0)
      where yes
      let symbol = trade.contract.LocalSymbol
      from rolls in CurrentRollOvers(symbol, !trade.contract.IsOption || !IsCall.HasValue, strikesCount, weeks)
      from roll in rolls
      let strikeSign = (trade.contract.IsCall ? -1 : trade.contract.IsPut ? 1 : 0) * trade.position.Sign()
      let strikeDelta = (roll.ComboStrike() - trade.underPrice) * strikeSign
      from up in UnderPrice(trade.contract, 3)
      from rp in IbClient.ReqPriceSafe(roll, 3, false)
      let bid = roll.ExtrinsicValue(rp.bid, up.bid)
      where !trade.contract.IsOption || bid > strikeDelta.Max(-trade.change)
      let days = (roll.Expiration - Expiration).TotalDays.Floor()
      let workDays = Expiration.GetWorkingDays(roll.Expiration)
      let amount = bid * trade.position.Abs() * (trade.contract.IsOption ? roll.ComboMultiplier : 1)
      let w = 5.0 / workDays
      let perc = bid / up.bid
      select (trade, roll, days, bid, ppw: (bid * w).AutoRound2(2), amount, amount * w, (perc * 100).AutoRound2(3), delta: rp.delta.Round(1));
  }
}
