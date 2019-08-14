using HedgeHog.Bars;
using HedgeHog.Shared;
using HedgeHog.Shared.Messages;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using HedgeHog;
using System.Collections.Concurrent;
using System.Reactive.Subjects;
using IBApp;
using CURRENT_HEDGES = System.Collections.Generic.List<(IBApi.Contract contract, double ratio, double price, string context)>;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    IEnumerable<(TradingMacro tm, TradingMacro tmh)> GetHedgedTradingMacros(string pair) {
      return from tm2 in TradingMacroHedged()
             select (this, tm2);
    }
    public static List<IList<Trade>> HedgeTradesVirtual { get; set; } = new List<IList<Trade>>();

    DateTime _zeroHedgeDate;
    CorridorStatistics ShowVoltsByGrossVirtual(int voltIndex) {
      if(!UseCalc()) return null;
      if(HedgeTradesVirtual.IsEmpty()) {
        var zeroHedge = UseRates(ra =>
        (from r1 in ra.SkipWhile(r => GetVoltage2(r).IsNaN()).DistinctUntilChanged(r => GetVoltage2(r).Sign()).Take(2).TakeLast(1).Select(r => new { r.StartDate, PriceAvg = r.PriceAvg * BaseUnitSize * CurrentHedgePosition1 })
         from r2 in TradingMacroHedged(tmh => tmh.UseRates(rah => rah.SkipWhile(r => r.StartDate < r1.StartDate).Take(1).Select(r => r.PriceAvg * tmh.BaseUnitSize * CurrentHedgePosition2).ToArray()).Concat()).Concat()
         select (date: r1.StartDate, a1: r1.PriceAvg, a2: r2)
         ).ToArray()).Concat();

        if(false && zeroHedge.Any(zh => zh.date == _zeroHedgeDate))
          // Fill last
          UseRates(ra =>
            TradingMacroHedged(tmh => (
            from start in zeroHedge.Select(t => t.a1 - t.a2)
            from x2 in tmh.UseRates(rah => ra.BackwardsIterator().Take(2).Zip(r => r.StartDate, rah.BackwardsIterator().Take(2), r => r.StartDate, (r1, r2) => (rate: r1, a1: r1.PriceAvg * BaseUnitSize * CurrentHedgePosition1, a2: r2.PriceAvg * tmh.BaseUnitSize * CurrentHedgePosition2)))
            from x1 in x2.Reverse().TakeLast(1)
            select (x1.rate, v: (x1.a1 - x1.a2) - start)
            ))
            .ToArray()).Concat().Concat()
            .ForEach(t => SetVolts(voltIndex)(t.v));
        else {
          // Refiil
          //var xxx=
          UseRates(ra =>
          TradingMacroHedged(tmh =>
          from start in zeroHedge.Do(zh => _zeroHedgeDate = zh.date).Select(t => t.a1 - t.a2)
          from x2 in tmh.UseRates(rah => ra.Zip(r => r.StartDate, rah, r => r.StartDate, (r1, r2) => (rate: r1, a1: r1.PriceAvg * BaseUnitSize * CurrentHedgePosition1, a2: r2.PriceAvg * tmh.BaseUnitSize * CurrentHedgePosition2)))
          from x1 in x2
          select (x1.rate, v: (x1.a1 - x1.a2) - start)
          )).Concat().Concat()
          .Scan((rate: (Rate)null, v: double.NaN), (a, t) => (t.rate, v: a.v.Cma(10, t.v)))
          //.AsParallel()
          .ForEach(t => SetVoltByIndex(voltIndex)(t.rate, t.v));
          SetVoltsHighLows(voltIndex);
        }
      } else {
        var hedgedTrades = HedgeTradesVirtual
          .Where(ht => ht.Select(t => t.Pair).With(pairs => pairs.Contains(Pair) && TradingMacroHedged().Any(tm => pairs.Contains(tm.Pair))))
          .Concat()
          .OrderByDescending(t => t.Pair == Pair)
          .Buffer(2)
          .SelectMany(b => new[] { (tm: this, t: b[0]) }.Concat(TradingMacroHedged(tm => (tm, t: b[1]))))
          .ToArray();
        var tuples = (from ht in hedgedTrades
                      from x in ht.tm.UseRates(ra => ra.Select(r
                      => (d: r.StartDate
                      , t: (r, n: ht.t.CalcNetPL2((ht.t.Close = ht.t.IsBuy ? r.BidLow : r.AskHigh) * ht.tm.BaseUnitSize)))))
                      select x.ToArray()
        ).ToArray();
        tuples.Buffer(2)
        .SelectMany(b => b[0].Zip(b[1], (t1, t2) => { return (t1.t.n + t2.t.n).SideEffect(n => SetVoltByIndex(voltIndex)(t1.t.r, n)); }))
        .TakeLast(1)
        .ForEach(SetVolts(voltIndex));
      }
      return null;
    }

    #region MaxHedgeProfit Subject
    object _maxHedgeProfitSubjectLocker = new object();
    ISubject<(DateTime d, Action a)> _MaxHedgeProfitSubject;
    ISubject<(DateTime d, Action a)> MaxHedgeProfitSubject {
      get {
        lock(_maxHedgeProfitSubjectLocker)
          if(_MaxHedgeProfitSubject == null) {
            _MaxHedgeProfitSubject = new Subject<(DateTime d, Action a)>();
            _MaxHedgeProfitSubject
              .DistinctUntilChanged(d => d.d.Round(MathCore.RoundTo.Hour))
              .Subscribe(s => s.a(), exc => { });
          }
        return _MaxHedgeProfitSubject;
      }
    }
    void OnMaxHedgeProfit(DateTime d, Action a) => MaxHedgeProfitSubject.OnNext((d, a));
    #endregion

    public CURRENT_HEDGES CurrentHedgesByHV(int count) => CurrentHedgesByHV(count, DateTime.MaxValue, false);
    public CURRENT_HEDGES CurrentHedgesByHV() => CurrentHedgesByHV(int.MaxValue, DateTime.MaxValue, false);
    public CURRENT_HEDGES CurrentHedgesByHV(int count, DateTime end, bool isDaily) {
      var hp = TradingMacroHedged(tm => tm.HistoricalVolatilityByPoints(count / tm.BarPeriodInt.Max(1), end, isDaily).Select(hv => (pair: tm.Pair, hv, cp: tm.CurrentPriceAvg(), m: (double)tm.BaseUnitSize))).Concat().ToArray();
      var hh = (from h in HistoricalVolatilityByPoints(count / BarPeriodInt.Max(1), end, isDaily).Select(hv => (pair: Pair, hv, cp: CurrentPriceAvg(), m: (double)BaseUnitSize)).Concat(hp)
                where hp.Any() && hp.All(x => !double.IsInfinity(x.hv))
                select (h.pair.ContractFactory(), h.cp, h.hv, h.m, h.pair + ":" + h.hv.Round(2))
                 ).ToArray();
      return TradesManagerStatic.HedgeRatioByValue(":", hh).ToList().SideEffect(_ => OnCurrentHedgesByHV(_));
    }

    #region CurrentHedgesByHV Subject
    public IObservable<CURRENT_HEDGES> CurrentHedgesByHVObserver { get; private set; }
    object _CurrentHedgesByHVSubjectLocker = new object();
    ISubject<CURRENT_HEDGES> _CurrentHedgesByHVSubject;
    ISubject<CURRENT_HEDGES> CurrentHedgesByHVSubject {
      get {
        lock(_CurrentHedgesByHVSubjectLocker)
          if(_CurrentHedgesByHVSubject == null) {
            _CurrentHedgesByHVSubject = new Subject<CURRENT_HEDGES>();
            DateTime lastScan = DateTime.MinValue;
            CurrentHedgesByHVObserver = _CurrentHedgesByHVSubject.InitBufferedObservable((a, b) => {
              try {
                return a?.Any() == false || DateTime.Now > lastScan.AddMinutes(1) ? b
                  : a.Zip(b, (p, n) => (p.contract, p.ratio.Cma(10, n.ratio), n.price, n.context)).ToList();
              } catch(Exception exc) {
                Log = exc;
                return new CURRENT_HEDGES();
              } finally {
                lastScan = DateTime.Now;
              }
            }, e => { });
          }
        return _CurrentHedgesByHVSubject;
      }
    }
    void OnCurrentHedgesByHV(CURRENT_HEDGES p) {
      CurrentHedgesByHVSubject.OnNext(p);
    }
    #endregion


    /*
    public IObservable<(Contract contract, double ratio, double price, string context)[]> CurrentHedgesByHV((string pair, double hv)[] hedges) {
      var o = (from h in hedges.ToObservable()
               from cd in IbClient.ReqContractDetailsCached(h.pair)
               from p in IbClient.ReqPriceSafe(cd.Contract)
               let hh=(cd.Contract,p.ask.Avg(p.bid),h.hv,(double)cd.Contract.ComboMultiplier, h.pair+":"+h.hv.Round(2))
               select hh).ToArray();
      var o2 = (from hh in o select TradesManagerStatic.HedgeRatioByValue(":", hh));
      return o2;
    }
     */
  }
}