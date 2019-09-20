using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IBApp {
  public partial class MarketDataManager {
    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceSafe(Contract contract, double timeoutInSeconds, bool useErrorHandler, double defaultPrice) =>
      ReqPriceSafe(contract, timeoutInSeconds).DefaultIfEmpty((defaultPrice, defaultPrice, DateTime.MinValue, 0));
    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceEmpty() => Observable.Return((0.0, 0.0, DateTime.MinValue, 0.0));
    static object _ReqPriceSafeLocker = new object();

    static IScheduler esReqPrice = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqPrice" });
    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceSafe(Contract contract, double timeoutInSeconds = 5, [CallerMemberName] string Caller = "") {
      var cache = contract.FromCache().DefaultIfEmpty(contract).Single();
      var title = Common.CallerChain(Caller);
      lock(_ReqPriceSafeLocker) {
        if(cache.IsBag)
          return ReqPriceBag(cache, timeoutInSeconds);
        if(!cache.IsFuturesCombo && cache.IsCombo)
          return ReqPriceComboSafe(cache, timeoutInSeconds);

        var p0 = GetPrice(cache, title).Where(IsPriceReady).Select(MakePrice).ToArray();
        if(p0.Any()) return p0.ToObservable();

        var o = Observable.Interval(0.5.FromSeconds(), esReqPrice)//.Spy($"{nameof(ReqPriceSafe)} Timer", TraceDebug)
          .SelectMany(_ => {
            return GetPrice(cache, title + "<=Timer").Where(IsPriceReady).Select(MakePrice);
          })
          .Take(1)
          .TakeUntil(DateTimeOffset.Now.AddSeconds(timeoutInSeconds), esReqPrice)
          .ToArray()
          .Do(a => a.IsEmpty().IfTrue(() => {
            if(cache.ReqMktDataId.IsDefault()) {
              TraceError($"{title}: {new { cache, cache.ReqMktDataId }}");
              AddRequestSync(cache);
            }
            TraceError($"{title}: {cache} - price timeout - {timeoutInSeconds} seconds");
          }))
          .SelectMany(p => p)
        ;

        var or = Observable.Interval(0.2.FromSeconds(), esReqPrice)
          .SelectMany(_ => activeRequests.Values.Where(ar => ar.contract == cache).Select(ar => ar.contract))
          .Take(1)
          .TakeUntil(DateTimeOffset.Now.AddSeconds(timeoutInSeconds * 10), esReqPrice)
          .ToArray()
          .SelectMany(_ => o);

        return or;
      }
      /// Locals
      (double bid, double ask, DateTime time, double delta) MakePrice(HedgeHog.Shared.Price p) => (p.Bid, p.Ask, p.Time, p.GreekDelta);
      bool IsPriceReady(HedgeHog.Shared.Price p) => p.IsAskSet && p.IsBidSet;
    }


    public IObservable<(double bid, double ask, DateTime time)> ReqPriceComboSafe_New(Contract combo, double timeoutInSeconds) {
      double ask((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double delta) price) => (cl.leg.Action == "BUY" ? 1 : -1) * price.ask;
      double bidCalc((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double delta) price) => (cl.leg.Action == "BUY" ? 1 : -1) * price.bid;
      var x = combo.LegsEx()
        .ToObservable()
        .SelectMany(cl => ReqPriceSafe(cl.contract, timeoutInSeconds).Select(price => (cl, price)).Take(1))
        .ToArray()
        .Where(a => a.Any())
        .Select(t => {
          var bids = t.Select(t2 => bidCalc(t2.cl, t2.price) * t2.cl.leg.Ratio).ToArray();
          var bid = bids.Sum();
          return (
           bid,
           t.Sum(t2 => ask(t2.cl, t2.price) * t2.cl.leg.Ratio),
           t.Min(t2 => t2.price.time)
           );
        });
      return x;
    }
    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceComboSafe(Contract combo, double timeoutInSeconds, [CallerMemberName] string Caller = "") {
      //Trace($"{nameof(ReqPriceComboSafe)}:{combo} <= {Caller}");
      double ask((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double) price) => cl.leg.Action == "BUY" ? price.ask : -price.bid;
      double bid((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double) price) => cl.leg.Action == "BUY" ? price.bid : -price.ask;
      var x = combo.LegsEx()
        .ToObservable()
        .SelectMany(cl => ReqPriceSafe(cl.contract, timeoutInSeconds).Select(price => (cl, price)).Take(1))
        .ToArray()
        .Where(a => a.Any())
        .Select(t => (
        t.Sum(t2 => bid(t2.cl, t2.price) * t2.cl.leg.Ratio),
        t.Sum(t2 => ask(t2.cl, t2.price) * t2.cl.leg.Ratio),
        t.Max(t2 => t2.price.time),
        t.Max(t2 => t2.price.delta)
        ));
      return x;
    }
    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceBag(Contract combo, double timeoutInSeconds, [CallerMemberName] string Caller = "") {
      var x0 = (from l in combo.LegsEx().ToObservable()
                from p in l.contract.ReqPriceSafe(timeoutInSeconds)
                  //               from ab in new[] { (price.bid, multiplier: l.c.ComboMultiplier, positions: l.r), (price.ask, multiplier: l.c.ComboMultiplier, positions: l.r) }
                select new { c = l.contract, p, r = l.leg.Ratio * (l.leg.IsBuy ? 1 : -1), l.leg.IsBuy }
               )
               .ToArray()
               .Select(a => {
                 var bid = a.Select(x => (price: x.IsBuy ? x.p.bid : x.p.ask, multiplier: x.c.ComboMultiplier, positions: x.r)).ToArray().CalcHedgePrice();
                 var ask = a.Select(x => (price: x.IsBuy ? x.p.ask : x.p.bid, multiplier: x.c.ComboMultiplier, positions: x.r)).ToArray().CalcHedgePrice();
                 return (bid, ask, a.Max(b => b.p.time), 1.0);
               })
               ;
      return x0;
    }
  }
}