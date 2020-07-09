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
    public IObservable<MarketPrice> ReqPriceSafe(Contract contract, double timeoutInSeconds, bool useErrorHandler, double defaultPrice) =>
      ReqPriceSafe(contract, timeoutInSeconds).DefaultIfEmpty((defaultPrice, defaultPrice, DateTime.MinValue, 0, double.NaN));
    public IObservable<MarketPrice> ReqPriceEmpty() => Observable.Return((MarketPrice)(0.0, 0.0, DateTime.MinValue, 0.0, double.NaN));
    static object _ReqPriceSafeLocker = new object();

    static IScheduler esReqPrice = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqPrice" });
    public IObservable<MarketPrice> ReqPriceSafe(Contract contract, double timeoutInSeconds = 5, [CallerMemberName] string Caller = "") {
      var cache = contract.FromCache().DefaultIfEmpty(contract).Single();
      var title = Common.CallerChain(Caller);
      lock(_ReqPriceSafeLocker) {
        if(cache.IsBag)
          return ReqPriceBag(cache, timeoutInSeconds);
        if(!cache.IsHedgeCombo && cache.IsOptionsCombo)
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
      MarketPrice MakePrice(HedgeHog.Shared.Price p) => (p.Bid, p.Ask, p.Time, p.GreekDelta, p.GreekTheta);
      bool IsPriceReady(HedgeHog.Shared.Price p) => p.IsAskSet && p.IsBidSet;
    }


    public IObservable<(double bid, double ask, DateTime time)> ReqPriceComboSafe_New(Contract combo, double timeoutInSeconds) {
      double ask((Contract option, ComboLeg leg) cl, MarketPrice price) => (cl.leg.Action == "BUY" ? 1 : -1) * price.ask;
      double bidCalc((Contract option, ComboLeg leg) cl, MarketPrice price) => (cl.leg.Action == "BUY" ? 1 : -1) * price.bid;
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
    public IObservable<MarketPrice> ReqPriceComboSafe(Contract combo, double timeoutInSeconds, [CallerMemberName] string Caller = "") {
      //Trace($"{nameof(ReqPriceComboSafe)}:{combo} <= {Caller}");
      double ask((Contract option, ComboLeg leg) cl, MarketPrice price) => cl.leg.Action == "BUY" ? price.ask : -price.bid;
      double bid((Contract option, ComboLeg leg) cl, MarketPrice price) => cl.leg.Action == "BUY" ? price.bid : -price.ask;
      var x = combo.LegsEx()
        .ToObservable()
        .SelectMany(cl => ReqPriceSafe(cl.contract, timeoutInSeconds).Select(price => (cl, price)).Take(1))
        .ToArray()
        .Where(a => a.Any())
        .Select(t => (MarketPrice)(
        t.Sum(t2 => bid(t2.cl, t2.price) * t2.cl.leg.Ratio),
        t.Sum(t2 => ask(t2.cl, t2.price) * t2.cl.leg.Ratio),
        t.Max(t2 => t2.price.time),
        t.Max(t2 => t2.price.delta),
        t.Average(t2 => t2.price.theta)
        ));
      return x;
    }
    public IObservable<MarketPrice> ReqPriceBag(Contract combo, double timeoutInSeconds, [CallerMemberName] string Caller = "") {
      string title() => $"{nameof(ReqPriceBag)}: {new { combo, key = combo._key() }}";
      var legs = combo.LegsEx().ToList();
      if(legs.Count < 2) Trace($"{title()} is no a combo");
      var x0 = (from l in legs.ToObservable()
                from p in l.contract.ReqPriceSafe(timeoutInSeconds).DefaultIfEmpty()
                  //               from ab in new[] { (price.bid, multiplier: l.c.ComboMultiplier, positions: l.r), (price.ask, multiplier: l.c.ComboMultiplier, positions: l.r) }
                select new { c = l.contract, p, r = (double)l.leg.Ratio * (l.leg.IsBuy ? 1 : -1), l.leg.IsBuy }
               )
               .ToList()
               .Do(a => {
                 if(a.Count < legs.Count) {
                   TraceError($"{title()} - yelded prices for only {a.Count} legs");
                 }
               })
               //.Where(a => a.Length == 2)
               .Select(a => {
                 var bid = a.Select(x => (price: x.IsBuy ? x.p.bid : x.p.ask, multiplier: x.c.ComboMultiplier, positions: x.r)).ToArray().CalcHedgePrice();
                 var ask = a.Select(x => (price: x.IsBuy ? x.p.ask : x.p.bid, multiplier: x.c.ComboMultiplier, positions: x.r)).ToArray().CalcHedgePrice();
                 if(bid == 0 || ask == 0)
                   TraceError($"{title()}: {new { ask, bid }}");
                 return (MarketPrice)(bid, ask, a.Max(b => b.p.time), 1.0, double.NaN);
               })
               ;
      return x0.Where(p => p.ask != 0 && p.bid != 0);
    }
  }

  public struct MarketPrice {
    public double bid;
    public double ask;
    public DateTime time;
    public double delta;
    public double theta;

    public MarketPrice(double bid, double ask, DateTime time, double delta, double theta) {
      this.bid = bid;
      this.ask = ask;
      this.time = time;
      this.delta = delta;
      this.theta = theta;
    }

    public override bool Equals(object obj)
      => obj is MarketPrice other && bid == other.bid && ask == other.ask
      && time == other.time && delta == other.delta && theta.IfNaN(0) == other.theta.IfNaN(0);

    public override int GetHashCode() {
      var hashCode = 1697963223;
      hashCode = hashCode * -1521134295 + bid.GetHashCode();
      hashCode = hashCode * -1521134295 + ask.GetHashCode();
      hashCode = hashCode * -1521134295 + time.GetHashCode();
      hashCode = hashCode * -1521134295 + delta.GetHashCode();
      hashCode = hashCode * -1521134295 + theta.GetHashCode();
      return hashCode;
    }

    public void Deconstruct(out double bid, out double ask, out DateTime time, out double delta, out double theta) {
      bid = this.bid;
      ask = this.ask;
      time = this.time;
      delta = this.delta;
      theta = this.theta;
    }

    public static implicit operator (double bid, double ask, DateTime time, double delta, double theta)(MarketPrice value)
      => (value.bid, value.ask, value.time, value.delta, value.theta);
    public static implicit operator MarketPrice((double bid, double ask, DateTime time, double delta, double theta) value)
      => new MarketPrice(value.bid, value.ask, value.time, value.delta, value.theta);

    public static bool operator ==(MarketPrice left, MarketPrice right) {
      return left.Equals(right);
    }

    public static bool operator !=(MarketPrice left, MarketPrice right) {
      return !(left == right);
    }
  }
}