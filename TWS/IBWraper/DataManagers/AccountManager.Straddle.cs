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
using CURRENT_OPTIONS = System.Collections.Generic.IList<IBApp.CurrentCombo>;
using CURRENT_ROLLOVERS = System.IObservable<(IBApp.ComboTrade trade, IBApi.Contract roll, int days, double bid, double ppw, double amount, double dpw, double perc, double delta)>;
using CURRENT_HEDGES = System.Collections.Generic.List<(IBApi.Contract contract, IBApi.Contract[] options, double ratio, double price, string context, bool isBuy)>;
using System.Reactive.Concurrency;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Reactive.Subjects;
using System.Reactive;

namespace IBApp {
  public partial class AccountManager {

    #region Make Straddles
    public IObservable<CurrentCombo[]>
      CurrentStraddles(string symbol, double strikeLevel, (int expirationDaysSkip, DateTime expirationDate) exp, int count, int gap) {
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from price in cd.Contract.ReqPriceSafe(5).Select(p => p.ask.Avg(p.bid))
        from combo in MakeStraddles(symbol, strikeLevel.IfNaNOrZero(price), count, exp.expirationDate.IsMin() ? exp.expirationDaysSkip.CalcExpirationDate() : exp.expirationDate, gap)
        from p in combo.contract.ReqPriceSafe().DefaultIfEmpty()
        select CurrentComboInfo(price, combo, p)
        ).ToArray();
    }
    public IObservable<CurrentCombo[]>
      CurrentStraddles(string symbol, double strikeLevel, (int expirationDaysSkip, DateTime expirationDate) exp, int count, double gap) {
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from price in cd.Contract.ReqPriceSafe(5).Select(p => p.ask.Avg(p.bid))
        from combo in MakeStraddles(symbol, strikeLevel.IfNaNOrZero(price), count, exp.expirationDate.IsMin() ? exp.expirationDaysSkip.CalcExpirationDate() : exp.expirationDate, gap)
        from p in combo.contract.ReqPriceSafe().DefaultIfEmpty()
        select CurrentComboInfo(price, combo, p)
        ).ToArray();
    }

    public IObservable<CurrentCombo[]>
      StraddleFromContracts(IList<Contract> contracts) {
      return (
        from cd in IbClient.ReqContractDetailsCached(contracts[0].Symbol)
        from price in cd.Contract.ReqPriceSafe(5).Select(p => p.ask.Avg(p.bid))
        from combo in new[] { (contract: contracts.MakeStraddle(), options: contracts.ToArray()) }
        from p in combo.contract.ReqPriceSafe().DefaultIfEmpty()
        select CurrentComboInfo(price, combo, p)
        ).ToArray();
    }
    public IObservable<CurrentCombo[]>
      CurrentStraddles_Old(string symbol, double strikeLevel, (int expirationDaysSkip, DateTime expirationDate) exp, int count, int gap) {
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from price in cd.Contract.ReqPriceSafe(5).Select(p => p.ask.Avg(p.bid))
        from combo in MakeStraddles(symbol, strikeLevel.IfNaNOrZero(price), exp, count, gap)
        from p in combo.contract.ReqPriceSafe().DefaultIfEmpty()
        select CurrentComboInfo(price, combo, p)).ToArray()
        .Select(b => b
         .OrderBy(t => t.marketPrice.ask.Avg(t.marketPrice.bid))
         .Select((t, i) => ((t, i)))
         .OrderBy(t => t.i > 1)
         .ThenBy(t => t.t.marketPrice.ask.Avg(t.t.marketPrice.bid) / t.t.deltaAsk.Avg(t.t.deltaBid))
         .ThenByDescending(t => t.t.deltaAsk.Avg(t.t.deltaBid))
         .Select(t => t.t)
         .ToArray()
         );
    }
    private static CurrentCombo
      CurrentComboInfo(double underPrice, (Contract contract, Contract[] options) combo, MarketPrice p) {
      var strikeAvg = combo.options.Average(o => o.Strike);
      double pa = p.ask.Avg(p.bid);
      var iv = combo.options.Sum(o => o.IntrinsicValue(underPrice));
      //p.delta = pa - iv;
      return new CurrentCombo(
              combo.contract.Instrument,
              p,
              strikeAvg,
              underPrice,
              (up: strikeAvg + pa, dn: strikeAvg - pa),
              combo,
              p.bid - iv,
              p.ask - iv
            );
    }

    public IObservable<(string instrument, double bid, double ask, DateTime time, double delta, double strikeAvg, double underPrice, (double up, double dn) breakEven, (Contract contract, Contract[] options) combo)[]>
      CurrentStraddles(string symbol, (int expirationDaysSkip, DateTime expirationDate) exp, int count, int gap) {
      return (
        from cd in IbClient.ReqContractDetailsCached(symbol)
        from underPrice in cd.Contract.ReqPriceSafe(5).Select(p => p.bid)
        from combo in MakeStraddles(symbol, underPrice, exp, count, gap)
        from p in combo.contract.ReqPriceSafe(2).DefaultIfEmpty()
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

    public static IObservable<(Contract contract, Contract[] options)> MakeStraddles(string symbol, int count, int expirationDaysSkip, int gap) =>
      MakeStraddles(symbol, count, expirationDaysSkip.CalcExpirationDate(), gap);

    public static IObservable<(Contract contract, Contract[] options)> MakeStraddles(string symbol, int count, DateTime expDate, int gap) =>
      from cd in symbol.ReqContractDetailsCached()
      from p in cd.ReqPriceSafe().Select(p => p.avg)
      from s in MakeStraddles(symbol, p, count, expDate, gap)
      select s;

    public static IObservable<(Contract contract, Contract[] options)> MakeStraddles(string symbol, double underPrice, int count, DateTime expDate, double gap) {
      return MakeStraddlesBest(symbol, underPrice, expDate, gap);
    }
      public static IObservable<(Contract contract, Contract[] options)> MakeStraddles(string symbol, double underPrice, int count, DateTime expDate, int gap) {
        return MakeStraddlesBest(symbol, underPrice, expDate, gap / 1000.0);
        return (
        from chains in DataManager.IBClientMaster.ReqOptionChainOldCache(symbol, expDate).ToArray()
        from chain in chains
        where chain.Any()
        from d in chain.Select(c => c.Strike - underPrice).MinBy(d => d.Abs()).Take(1)
        from up in chain.Where(c => c.IsCall).OrderBy(c => c.Strike.Abs(underPrice)).Take(count).Select(c => c.Strike - d)
        from g in Enumerable.Range(0, gap + 1)
        from pair in BuildStraddlePair(chain, up, g)
        let cp = new[] { pair.call, pair.put }
        select (cp.MakeStraddle(), cp)
       );
      IEnumerable<(Contract call, Contract put)> BuildStraddlePair(Contract[] chain, double p, int gap) {
        var a0 = chain
                  .GroupBy(x => x.Strike)
                  .OrderBy(x => x.Key.Abs(p))
                  .Take(gap + 1)
                  .SelectMany(g => g)
                  .ToList();
        var calls = a0.Where(c => c.IsCall).MaxBy(g => g.Strike);
        var puts = a0.Where(c => !c.IsCall).MinBy(g => g.Strike);
        return calls.Zip(puts, (call, put) => (call, put));
        //return a0
        //          .SelectMany(g => g.OrderBy(b => b.IsCall).Buffer(2).Select(b => new { call = b[1], put = b[0] }))
        //          .TakeLast(2)
        //          .OrderBy(x => x.call.Strike)
        //          .Buffer(2)
        //          .Select(b => (b.Last().call, b[0].put));
      }
    }
    public static IObservable<(Contract contract, Contract[] options)> MakeStraddlesBest(string symbol, double underPrice, DateTime expDate, double gap) =>
        from chains in DataManager.IBClientMaster.ReqOptionChainOldCache(symbol, expDate).ToArray()
        from chain in chains
        from strile in chain.Where(c => c.IsCall).OrderBy(c => c.Strike.Abs(underPrice)).Take(3).Select(c => c.Strike)
        from straddle in MakeStraddle(symbol, strile, 1, expDate, gap)
        select straddle;
    public static IObservable<(Contract contract, Contract[] options)> MakeStraddle(string symbol, double underPrice, int count, DateTime expDate, double gap) {
      return (
        from chains in DataManager.IBClientMaster.ReqOptionChainOldCache(symbol, expDate).ToArray()
        from chain in chains
        where chain.Any()
        let slc = StrikeLevel(underPrice, gap)
        let slp = StrikeLevel(underPrice, -gap)
        from call in chain.Where(c => c.IsCall).OrderBy(c => c.Strike).Where(c => c.Strike <= slc).TakeLast(1)
        from cprice in call.ReqPriceSafe().Do(mp => call.Price = mp.avg)
        from put in chain.Where(c => !c.IsCall).OrderBy(c => -c.Strike).Where(c => c.Strike >= slp).TakeLast(1)
        from pprice in put.ReqPriceSafe().Do(mp => put.Price = mp.avg)
        let cp = new[] { call, put }
        select (cp.MakeStraddle(), cp)
       );
    }

    public IObservable<(Contract contract, Contract[] options)> MakeStraddles
      (string symbol, double price, (int expirationDaysSkip, DateTime expirationDate) exp, int count, int gap) =>
      IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true, false }, exp, count * 2 + gap * 3, c => true)
      //.Take(count*2)
      .ToArray()
      .SelectMany(reqOptions => {
        var strikes = reqOptions.OrderBy(o => o.Strike.Abs(price)).Take(count * 2 + gap * 2).OrderBy(c => c.Strike).ToArray();
        var calls = strikes.Where(c => c.Right == "C").ToArray();
        var puts = strikes.Where(c => c.Right == "P").ToArray();
        var cps = from c in calls
                  join p in puts on new { c.Strike, c.Expiration } equals new { p.Strike, p.Expiration }
                  select new { c, p };
        calls = cps.Select(cp => cp.c).ToArray();
        puts = cps.Select(cp => cp.p).ToArray();
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
      var uniDir = Contract.FromCache(conIds[0]).Zip(Contract.FromCache(conIds[1]), (c1, c2) => c1.Right != c2.Right).Single();
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
        Action = uniDir ? "BUY" : "SELL",
        Exchange = exchange
      };
      c.ComboLegs = new List<ComboLeg> { call, put };
      return c.AddToCache();
    }
    #endregion

    #region Make Butterfly
    public IObservable<(Contract contract, Contract[] options)> MakeButterflies(string symbol, double price) =>
   IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true }, (1, DateTime.MinValue), 4, c => true)
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

    public IObservable<CURRENT_HEDGES> CurrentHedges(string h1, string h2, int tvDays, bool posCorr) => CurrentHedges(h1, h2, " ", c => c.ShortWithDate, tvDays, posCorr);
    public IObservable<CURRENT_HEDGES> CurrentHedges(string h1, string h2, string mashDivider, Func<Contract, string> context, int tvDays, bool posCorr) =>
        (from es in CurrentTimeValue(h1, tvDays)
         from nq in CurrentTimeValue(h2, tvDays)
         let options = es.Concat(nq).Where(t => t.delta > 0).ToArray()
         where options.Length == es.Length + nq.Length
         let hh = options.Select(c => (c.contract, new[] { c.option }, c.underPrice, c.delta, (double)c.contract.ComboMultiplier, context(c.option))).ToArray()
         select es.Any() && nq.Any() ? TradesManagerStatic.HedgeRatioByValue(mashDivider, posCorr, hh).ToList() : new CURRENT_HEDGES());
    public IObservable<(Contract option, Contract contract, double underPrice, double bid, double ask, double delta)[]> CurrentTimeValue(string symbol, int tvDays) {
      return (
        from u in IbClient.ReqContractDetailsCached(symbol).Take(0)
        from up in u.Contract.ReqPriceSafe()
        let nextFriday = DateTime.Now.AddDays(tvDays).GetNextWeekday(DayOfWeek.Friday)
        from cs in CurrentOptions(symbol, double.NaN, (-1, nextFriday), 6, c => c.Expiration.DayOfWeek == DayOfWeek.Friday)
        let calls = cs.Where(c => c.option.IsCall).OrderByDescending(c => c.marketPrice.delta).Take(2)
        let puts = cs.Where(c => c.option.IsPut).OrderByDescending(c => c.marketPrice.delta).Take(2)
        select calls.Concat(puts).Select(o => (o.option, u.Contract, o.underPrice, o.marketPrice.bid, o.marketPrice.ask, o.marketPrice.delta)).ToArray());
    }
    //public IObservable<CURRENT_OPTIONS> CurrentOptions(string symbol, double strikeLevel, int expirationDaysSkip, int count) =>
    static IScheduler esCurrOptions = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "CurrOptions" });
    static IObservable<CURRENT_OPTIONS> _CurrentOptionsGate = Observable.Empty<CURRENT_OPTIONS>();
    object _currOptLock = new object();
    public static double StrikeLevel(double currentPrice, double strikeLevel) =>
      strikeLevel.IsNotSetOrZero() ? currentPrice
      : !strikeLevel.Between(-1, 1) ? strikeLevel
      : currentPrice * (1 + strikeLevel);
    public IObservable<CURRENT_OPTIONS> CurrentOptions(string symbol, double strikeLevel, (int expirationDaysSkip, DateTime expirationDate) exp, int count, Func<Contract, bool> filter, [CallerMemberName] string Caller = "") {
      lock(_currOptLock) {
        TraceDebug0($"{nameof(CurrentOptions)} from {Caller}");
        return (
          //from w in _CurrentOptionsGate.DefaultIfEmpty().Take(1)
          from cd in IbClient.ReqContractDetailsCached(symbol)
          where count > 0
          from ucd in IbClient.ReqFutureChainCached(symbol, exp.expirationDate)
          from price in ucd.ReqPriceSafe(5).Select(p => p.ask.Avg(p.bid))
          from options in MakeOptions(symbol, StrikeLevel(price, strikeLevel), exp, count, filter).ToArray()
          from option in options.Skip((options.Length - 12).Max(0)).ToObservable()//.RateLimit(10, TaskPoolScheduler.Default)
          from p in option.ReqPriceSafe(10, Common.CallerChain("Current Option")).DefaultIfEmpty()
          let pa = p.ask.Avg(p.bid)
          select (CurrentCombo)(
            instrument: option.Instrument,
            p,
            option.Strike,
            price,
            breakEven: (up: option.Strike + pa, dn: option.Strike - pa),
            option,
            deltaBid: option.ExtrinsicValue(p.bid, price),
            deltaAsk: option.ExtrinsicValue(p.ask, price)
          ))
        .ToArray()
        .Select(b => b
        .OrderBy(t => t.marketPrice.ask.Avg(t.marketPrice.bid))
        .Select((t, i) => (t, i))
        .OrderBy(t => t.i > 3)
        .ThenBy(t => t.t.marketPrice.ask.Avg(t.t.marketPrice.bid) / t.t.deltaAsk.Avg(t.t.deltaBid))
        .ThenBy(t => t.t.option.Right)
        .Select(t => t.t)
        .ToArray())
        .Publish()
        .RefCount()
        //.SideEffect(_ => _CurrentOptionsGate = _)
        ;
      }
    }
    public IObservable<Contract> MakeOptions
      (string symbol, double price, (int expirationDaysSkip, DateTime expirationDate) exp, int count, Func<Contract, bool> filter) =>
      IbClient.ReqCurrentOptionsAsync(symbol, price, new[] { true, false }, exp, count, filter)
      //.Take(count*2)
      .ToArray()
      .SelectMany(reqOptions => {
        return reqOptions.OrderBy(o => o.Strike.Abs(price)).Take(count).ToArray();
      })
      .Take(count);
    #endregion

    static string _spy(string text) => $"{DateTime.Now:mm:ss.f}: {text} -- ";
    public IObservable<Contract[]> CurrentRollOvers(string symbol, bool? isCall, int strikes, int weeks) =>
      (from cd in IbClient.ReqContractDetailsCached(symbol)
       where strikes > 0 && weeks > 0
       let contract = cd.Contract
       from underContract in cd.Contract.IsOption
         ? IbClient.ReqContractDetailsCached(cd.UnderSymbol).Select(ucd => ucd.Contract)
         : Observable.Return(cd.Contract)
       let underSymbol = underContract.LocalSymbol
       from under in IbClient.ReqContractDetailsCached(underSymbol)//.Spy(_spy("ReqContractDetailsCached"))
       from price in under.Contract.ReqPriceSafe()//.Spy(_spy("ReqPriceSafe"))
       let expiration = contract.IsOption ? contract.Expiration : DateTime.Now.Date.AddDays(0)
       from allStkExp in IbClient.ReqStrikesAndExpirations(underSymbol)//.Spy(_spy("AllStrikesAndExpirations"))
       let exps = allStkExp.expirations.Where(ex => ex > expiration && ex <= expiration.AddDays(weeks * 7)).OrderBy(ex => ex).ToArray()
       let priceAvg = price.ask.Avg(price.bid)
       let strikes2 = allStkExp.strikes.OrderBy(strike => strike.Abs(priceAvg)).ToArray()
       from strikeFirst in strikes2.Take(1)
       let strikes3 = strikes2.Where(strike => !contract.IsOption || (contract.IsCall && strike >= strikeFirst) || (contract.IsPut && strike <= strikeFirst)).Take(strikes).ToArray()
       from strike in strikes3
       from exp in exps
       from cds in IbClient.ReqOptionChainOldCache(underSymbol, exp, strike)//.Spy(_spy("ReqOptionChainOldCache 2"))
       from cdRoll in cds
       where !isCall.HasValue || isCall == cdRoll.IsCall
       group cdRoll by (cdRoll.Strike, cdRoll.LastTradeDateOrContractMonth) into g
       from sr in g.ToArray()
       select sr.Length == 1 ? sr.Single() : MakeStraddle(sr)
      ).ToArray()
      .Select(a => a.OrderBy(c => c.Expiration).ThenBy(c => c.Strike).ToArray());//.Spy(_spy("CurrentRollOvers"));

    public IObservable<Contract[]> CurrentRollOversByUnder(Contract underContract, bool isCall, DateTime expiration, double strikeMinMax, int strikesCount, int weeks) =>
      (from yes in Observable.Return(strikesCount > 0 && weeks > 0)
       where yes
       from price in underContract.ReqPriceSafe()//.Spy(_spy("ReqPriceSafe"))
       from allStkExp in IbClient.ReqStrikesAndExpirations(underContract.LocalSymbol)//.Spy(_spy("AllStrikesAndExpirations"))
       let exps = allStkExp.expirations.Where(ex => ex > expiration && ex <= expiration.AddDays(weeks * 7)).OrderBy(ex => ex).ToArray()
       let priceAvg = price.ask.Avg(price.bid)
       let strikes2 = allStkExp.strikes.OrderBy(strike => strike.Abs(strikeMinMax)).ToArray()
       from strikeFirst in strikes2.Take(1)
       let strikes3 = strikes2.Where(strike => (isCall && strike >= strikeMinMax) || (!isCall && strike <= strikeMinMax)).Take(strikesCount).ToArray()
       from strike in strikes3
       from exp in exps
       from cds in IbClient.ReqOptionChainOldCache(underContract.LocalSymbol, exp, strike)//.Spy(_spy("ReqOptionChainOldCache 2"))
       from cdRoll in cds
       where isCall == cdRoll.IsCall
       group cdRoll by (cdRoll.Strike, cdRoll.LastTradeDateOrContractMonth) into g
       from sr in g.ToArray()
       select sr.Length == 1 ? sr.Single() : MakeStraddle(sr)
      ).ToArray()
      .Select(a => a.OrderBy(c => c.Expiration).ThenBy(c => (isCall ? -1 : 1) * c.Strike).ToArray());//.Spy(_spy("CurrentRollOvers"));

    public CURRENT_ROLLOVERS CurrentRollOver(string symbol, bool useStraddle, int strikesCount, int weeks, int creditIdDollars) =>
       from yes in Observable.Return(!symbol.IsNullOrEmpty())
       where yes
       from cd in IbClient.ReqContractDetailsCached(symbol)
       from trades in ComboTrades(1).DefaultIfEmpty(new ComboTrade(cd.Contract)).ToArray()
       from trade in trades
       from cro in CurrentRollOverImpl(trade, cd.Contract, useStraddle, strikesCount, weeks, trades, creditIdDollars)
       select cro;

    CURRENT_ROLLOVERS CurrentRollOverImpl(ComboTrade trade, Contract contractFilter, bool useStraddle, int strikesCount, int weeks, ComboTrade[] trades, int creditIdDollars) =>
      from yes in Observable.Return(trade.contract == contractFilter && strikesCount > 0 && weeks > 0)
      where yes
      let symbol = contractFilter.LocalSymbol
      let IsCall = useStraddle ? (bool?)null : contractFilter.IsOption ? contractFilter.IsCall : trade.position > 0
      let Expiration = contractFilter.IsOption ? contractFilter.Expiration : IbClient.ServerTime.Date
      from roll in CurrentRollOverImpl2(trade, IsCall, Expiration, strikesCount, weeks, trades, creditIdDollars)
      select roll;

    public CURRENT_ROLLOVERS CurrentRollOverImpl2(ComboTrade trade, bool? isCall, DateTime expiration, int strikesCount, int weeks, ComboTrade[] trades, int creditIdDollars) =>
      from yes in Observable.Return(strikesCount > 0 && weeks > 0)
      where yes
      from up in UnderPrice(trade.contract, 3)
      let symbol = trade.contract.LocalSymbol
      from rolls in CurrentRollOvers(symbol, isCall, strikesCount, weeks)
      from roll in rolls//.SideEffect(_ => TraceDebug(rolls.Select(r => new { roll = r.Instrument }).ToTextOrTable("Rolls:")))
      let strikeSign = trade.contract.IsOption ? (trade.contract.IsCall ? -1 : trade.contract.IsPut ? 1 : 0) * trade.position.Sign() : 0
      let strikeDelta = (roll.ComboStrike() - trade.underPrice) * strikeSign
      from rp in roll.ReqPriceSafe()
      let bid = roll.ExtrinsicValue(rp.bid, up.bid)
      let coveredChange = trade.CoveredOption(trades).Select(ct => ct.change).Sum() + creditIdDollars
      let change = -trade.change - coveredChange
      where trade.contract.IsOption && bid > strikeDelta.Max(-trade.change) || bid > change
      let days = (roll.Expiration - expiration).TotalDays.Floor()//.SideEffect(_ => TraceDebug(new { roll, roll.Expiration, expiration, days = _ }))
      let workDays = expiration.GetWorkingDays(roll.Expiration)
      let amount = bid * trade.position.Abs() * roll.ComboMultiplier
      let w = 5.0 / workDays
      let perc = bid / up.bid
      select (trade, roll, days, bid, ppw: (bid * w).AutoRound2(2), amount, amount * w, (perc * 100).AutoRound2(3), delta: rp.delta.Round(1));

    public IObservable<RollOver> CurrentRollOverByUnder(string under, int quantity, int strikesCount, int weeks, int creditIdDollars)
      => under.ReqContractDetailsCached().SelectMany(cd
        => CurrentRollOverByUnder(cd.Contract, quantity, strikesCount, weeks, creditIdDollars));
    static double _extRatio = 0.3;
    public IObservable<RollOver> CurrentRollOverByUnder(Contract under, int quantity, int strikesCount, int weeks, int creditIdDollars) =>
      from yes in Observable.Return(strikesCount > 0 && weeks > 0)
      where yes
      from allTrades in ComboTrades(1).Where(ct => ct.contract.UnderContract.Any(c => c == under)).ToArray()
      where allTrades.Any()
      let exp = allTrades.Min(t => t.contract.Expiration)
      let tradesLost = allTrades.Where(t => t.contract.Expiration == exp && t.contract.IsOption && t.change <= 0 && !t.IsBuy).ToArray()
      from isCall in tradesLost.Select(t => t.contract.IsCall).Distinct()
      from up in under.ReqPriceSafe()
      let strikeMinMax = isCall ? allTrades.Min(ct => ct.contract.Strike) : allTrades.Max(ct => ct.contract.Strike)
      from rolls in CurrentRollOversByUnder(under, isCall, exp, strikeMinMax, strikesCount, weeks)
      from roll in rolls//.SideEffect(_ => TraceDebug(rolls.Select(r => new { roll = r.Instrument }).ToTextOrTable("Rolls:")))
      let mul = roll.ComboMultiplier * quantity
      let strikeSign = 1
      let strikeDeltaAmount = (roll.ComboStrike() - up.avg) * strikeSign * mul * 0
      from rp in roll.ReqPriceSafe()
      let ev = roll.ExtrinsicValue(rp.bid, up.bid)
      where ev / rp.bid >= _extRatio
      let bid = rp.bid// roll.ExtrinsicValue(rp.bid, up.bid)
      let bidAmount = bid * mul
      let tradesChangeAmount = tradesLost.Select(ct => ct.pl).Sum()
      let changeAmount = -tradesChangeAmount - creditIdDollars
      where bidAmount > strikeDeltaAmount.Max(changeAmount)
      let days = (roll.Expiration - exp).TotalDays.Floor()//.SideEffect(_ => TraceDebug(new { roll, roll.Expiration, expiration, days = _ }))
      let workDays = exp.GetWorkingDays(roll.Expiration).Max(1)
      let w = 5.0 / workDays
      let perc = MathExtensions.AnnualRate(Account.Equity, Account.Equity + bidAmount, DateTime.Now, roll.Expiration).rate// bid / up.bid
      select new RollOver(under, roll, days, bid, (bid * w).AutoRound2(2), bidAmount, bidAmount * w, (perc * 100).AutoRound2(3), rp.delta.Round(1));
  }
  static class CombosMixins {
    #region Parse Combos
    public static IList<(Contract contract, int position, double open)> ParseCombos
      (this ICollection<(Contract c, int p, double o)> positions, ICollection<AccountManager.OrderContractHolder> openOrders) {
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
                            from op in gpos.Select(gp => (gp.c, gp.p, gp.o)).ToArray().ParseCombos(new AccountManager.OrderContractHolder[0])
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
