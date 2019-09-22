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
//using CURRENT_HEDGES = System.Collections.Generic.List<(IBApi.Contract contract, double ratio, double price, string context)>;
using CURRENT_HEDGES = System.Collections.Generic.List<HedgeHog.Alice.Store.TradingMacro.HedgePosition<IBApi.Contract>>;
using System.Reflection;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    public static class HedgePosition {
      public static HedgePosition<TContract> Create<TContract>((TContract contract, double ratio, double price, string context) t)
        => new HedgePosition<TContract>(t.contract, t.ratio, t.price, t.context);
    }
    public class HedgePosition<TContract> {
      public HedgePosition(TContract contract, double ratio, double price, string context) {
        this.contract = contract;
        this.ratio = ratio;
        this.price = price;
        this.context = context;
      }

      public TContract contract { get; }
      public double ratio { get; }
      public double price { get; }
      public string context { get; }

      public override string ToString() => new { contract, ratio, price, context } + "";
    }
    IEnumerable<(TradingMacro tm, TradingMacro tmh)> GetHedgedTradingMacros(string pair) {
      return from tm2 in TradingMacroHedged()
             select (this, tm2);
    }
    public static List<IList<Trade>> HedgeTradesVirtual { get; set; } = new List<IList<Trade>>();

    CorridorStatistics ShowVoltsByHedgeRatio(int voltIndex) {
      lock(_voltLocker) {
        if(UseCalc()) {
          var c = RatesArray.Count;
          if(GetVoltByIndex(voltIndex)(RatesInternal[c - 1]).IsNaN())
            UseRatesInternal(ri => ri.Buffer(c, 1).TakeWhile(b => b.Count == c).ToArray()).Concat().ForEach(b => {
              var hp = HedgedRates(b);
              var hvs = hp.Select(h => HVByBarPeriod(b) / h.tm.HVByBarPeriod(h.ra));
              //var stdr = prices.StandardDeviation();//.ToArray();//.StDevByRegressoin();
              hvs.ForEach(hv => SetVoltByIndex(voltIndex)(b.Last(), MapHV(hv)));
            });
          var hvps = (from hv in HVByBarPeriod(this)
                      from hvhs in TradingMacroHedged(HVByBarPeriod)
                      from hvh in hvhs
                      select hv / hvh);
          hvps.ForEach(hvp => SetVolts(MapHV(hvp), voltIndex));
        }
        return null;
      }
      double MapHV(double hv) => hv * 100;
      IEnumerable<(Rate[] ra, TradingMacro tm)> HedgedRates(IList<Rate> b) =>
        (from tm in TradingMacroHedged()
         from ra in tm.UseRatesInternal(ra => ra.SkipWhile(r => r.StartDate < b[0].StartDate).TakeWhile(r => r.StartDate <= b.Last().StartDate).ToArray())
         select (ra, tm)
         );
    }

    object _currentHedgePositionsLock = new object();
    public void SetCurrentHedgePosition(IBApi.Contract contract, int quantity) {
      lock(_currentHedgePositionsLock) {
        var legs = contract.ComboLegs;
        CurrentHedgePosition1 = legs[0].With(l => l.Ratio * (l.IsBuy ? 1 : -1));
        var p2 = CurrentHedgePosition2;
        CurrentHedgePosition2 = legs[1].With(l => l.Ratio * (l.IsBuy ? 1 : -1));
        CurrentHedgeQuantity = quantity;
        if(p2 != CurrentHedgePosition2)
          Log = new DebugLogException(new { Pair, contract = contract.ShortString, quantity } + "");
      }
    }
    public (int p1, int p2, int max) GetCurrentHedgePositions(bool adjustForQuantity = false) {
      lock(_currentHedgePositionsLock)
        return (
          CurrentHedgePosition1 * (adjustForQuantity ? CurrentHedgeQuantity : 1),
          CurrentHedgePosition2 * (adjustForQuantity ? CurrentHedgeQuantity : 1),
          CurrentHedgePosition1.Abs().Max(CurrentHedgePosition2.Abs()) * (adjustForQuantity ? CurrentHedgeQuantity : 1)
          );
    }
    int CurrentHedgePosition1 = 0;
    int CurrentHedgeQuantity = 0;
    int CurrentHedgePosition2 {
      get => currentHedgePosition2; set {
        if(currentHedgePosition2 == value) return;
        currentHedgePosition2 = value;
        OnPropertyChanged(nameof(CurrentHedgePosition2));
      }
    }

    private int currentHedgePosition2 = 0;
    CorridorStatistics ShowVoltsByHedgePrice(int voltIndex) {
      var chp = GetCurrentHedgePositions();
      if(UseCalc() && chp.p1 != 0) {
        if(true || GetVoltByIndex(voltIndex)(RatesArraySafe[0]).IsNaN())
          UseRates(ra => TradingMacroHedged(tmh => tmh.UseRates(ram => RunCalcHedgePrice(voltIndex, ra, tmh, ram, chp.p1, chp.p2))));
        TradingMacroHedged(tmh => {
          var p = new[] { MakeParam(this, chp.p1), MakeParam(tmh, chp.p2) }.CalcHedgePrice();
          SetVolts(p, voltIndex);
        });
      }
      return null;
      (double, int, double) MakeParam(TradingMacro tm, double pos) => ((tm.RatesArraySafe.LastBC()?.PriceAvg).GetValueOrDefault(double.NaN), tm.BaseUnitSize, pos);
    }

    private void RunCalcHedgePrice(int voltIndex, List<Rate> ra, TradingMacro tmh, List<Rate> ram, double p1, double p2)
      => ra.Zip(r => r.StartDate, ram, r => r.StartDate, (r1, r2) => (r1, r2))
      .ForEach(t => {
        var p = (new[] { (t.r1.PriceAvg, BaseUnitSize, p1), (t.r2.PriceAvg, tmh.BaseUnitSize, p2) }).CalcHedgePrice();
        SetVoltByIndex(voltIndex)(t.r1, p);
      });

    DateTime _zeroHedgeDate;
    CorridorStatistics ShowVoltsByGrossVirtual(int voltIndex) {
      var chp = GetCurrentHedgePositions(true);
      if(!UseCalc()) return null;
      if(HedgeTradesVirtual.IsEmpty()) {
        var zeroHedge = UseRates(ra =>
        (from r1 in ra.SkipWhile(r => GetVoltage2(r).IsNaN()).DistinctUntilChanged(r => GetVoltage2(r).Sign()).Take(2).TakeLast(1).Select(r => new { r.StartDate, PriceAvg = r.PriceAvg * BaseUnitSize * chp.p1 })
         from r2 in TradingMacroHedged(tmh => tmh.UseRates(rah => rah.SkipWhile(r => r.StartDate < r1.StartDate).Take(1).Select(r => r.PriceAvg * tmh.BaseUnitSize * chp.p2).ToArray()).Concat()).Concat()
         select (date: r1.StartDate, a1: r1.PriceAvg, a2: r2)
         ).ToArray()).Concat();

        if(false && zeroHedge.Any(zh => zh.date == _zeroHedgeDate))
          // Fill last
          UseRates(ra =>
            TradingMacroHedged(tmh => (
            from start in zeroHedge.Select(t => t.a1 + t.a2)
            from x2 in tmh.UseRates(rah => ra.BackwardsIterator().Take(2).Zip(r => r.StartDate, rah.BackwardsIterator().Take(2), r => r.StartDate, (r1, r2) => (rate: r1, a1: r1.PriceAvg * BaseUnitSize * chp.p1, a2: r2.PriceAvg * tmh.BaseUnitSize * chp.p2)))
            from x1 in x2.Reverse().TakeLast(1)
            select (x1.rate, v: (x1.a1 + x1.a2) - start)
            ))
            .ToArray()).Concat().Concat()
            .ForEach(t => SetVolts(voltIndex)(t.v));
        else {
          // Refiil
          //var xxx=
          UseRates(ra =>
          TradingMacroHedged(tmh =>
          from start in zeroHedge.Do(zh => _zeroHedgeDate = zh.date).Select(t => t.a1 + t.a2)
          from x2 in tmh.UseRates(rah => ra.Zip(r => r.StartDate, rah, r => r.StartDate, (r1, r2) => (rate: r1, a1: r1.PriceAvg * BaseUnitSize * chp.p1, a2: r2.PriceAvg * tmh.BaseUnitSize * chp.p2)))
          from x1 in x2
          select (x1.rate, v: (x1.a1 + x1.a2) - start)
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

    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByHV(int count) => CurrentHedgesByHV(count, DateTime.MaxValue, BarPeriodInt > 0);
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByHV() => CurrentHedgesByHV(int.MaxValue);
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByHV(int count, DateTime end, bool isDaily) {
      var hp = TradingMacroHedged(tm => tm.HistoricalVolatilityByPoints(count / tm.BarPeriodInt.Max(1), end, isDaily)
      .Where(hv => hv != 0)
      .Select(hv => (pair: tm.Pair, hv, cp: tm.CurrentPriceAvg(), m: (double)tm.BaseUnitSize))
      ).Concat().ToArray();
      var hh = (from h in HistoricalVolatilityByPoints(count / BarPeriodInt.Max(1), end, isDaily)
                .Where(hv => hv != 0)
                .Select(hv => (pair: Pair, hv, cp: CurrentPriceAvg(), m: (double)BaseUnitSize)
                ).Concat(hp)
                where hp.Any() && hp.All(x => !double.IsInfinity(x.hv))
                select (h.pair.ContractFactory(IsInVirtualTrading), h.cp, h.hv, h.m, h.pair + ":" + h.hv.Round(2))
                 ).ToArray();
      return TradesManagerStatic.HedgeRatioByValue(":", hh).Select(HedgePosition.Create).ToList().SideEffect(_ => OnCurrentHedgesByHV(_));
    }
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByPositions() {
      OnCalcHedgeRatioByPositions();
      var r = HedgeRatioByPrices.PositionsFromRatio();
      var h1 = (Pair.ContractFactory(IsInVirtualTrading), r.p1, CurrentPriceAvg(), Pair);
      if(HedgeRatioByPrices.IsNaNOrZero()) return new List<HedgePosition<IBApi.Contract>>();
      var h2 = TradingMacroHedged(tm => (tm.Pair.ContractFactory(IsInVirtualTrading), r.p2, tm.CurrentPriceAvg(), tm.Pair));
      var hh = new[] { h1 }.Concat(h2).ToList();
      return hh.Select(HedgePosition.Create).ToList();
    }


    #region CalcHedgeRatioByPositions
    private double HedgeRatioByPrices { get; set; } = double.NaN;
    void CalcHedgeRatioByPositions() {
      var pos1 = 100;// GetCurrentHedgePositions(true).max;
      if(pos1 == 0) pos1 = 100;
      var sw = Stopwatch.StartNew();
      var hrs = Enumerable.Range(0, pos1 - 1)
        .Select(p => pos1 - p)
        .SelectMany(pos2 => new[] { CalcHedgeRatioByPositionsCorrelation(pos1, pos2), CalcHedgeRatioByPositionsCorrelation(pos2, pos1) })
        .OrderByDescending(hr => hr.corr)
        .ToArray();
      hrs.Take(1).Where(hr => !hr.corr.IsNaNOrZero()).Select(hr => hr.pos2.Div(hr.pos1)).DefaultIfEmpty(double.NaN).ForEach(hr => HedgeRatioByPrices = hr);
      Debug.WriteLine($"{nameof(CalcHedgeRatioByPositions)}:{sw.Elapsed.TotalSeconds.AutoRound2(3)}sec");
    }
    (double stDev, int pos1, int pos2) CalcHedgeRatioByPositions(int pos1, int pos2) {
      var hedgePrices = UseRates(ra => TradingMacroHedged(tmh =>
        from x2 in tmh.UseRates(rah => ra.Zip(r => r.StartDate, rah, r => r.StartDate, (r1, r2) => (a1: r1.PriceAvg * BaseUnitSize * pos1, a2: r2.PriceAvg * tmh.BaseUnitSize * pos2)))
        from x1 in x2
        where x1.a1.IsNotNaN() && x1.a2.IsNotNaN()
        select (x1.a1 - x1.a2).Abs()
        )).Concat().Concat().Cma(2);
      ;
      var stDev = hedgePrices.Height(d => d);
      return (stDev, pos1, pos2);
    }
    (double corr, int pos1, int pos2) CalcHedgeRatioByPositionsCorrelation(int pos1, int pos2) {
      var hedgePrices = UseRates(ra => TradingMacroHedged(tmh =>
        from x2 in tmh.UseRates(rah
          => ra.Zip(r => r.StartDate, rah, r => r.StartDate, (r1, r2)
          => (a1: r1.PriceAvg * BaseUnitSize * pos1, a2: r2.PriceAvg * tmh.BaseUnitSize * pos2, diff: GetVoltage2(r1))))
        from x1 in x2
        where x1.a1.IsNotNaN() && x1.a2.IsNotNaN() && x1.diff.IsNotNaN()
        select new { price = (x1.a1 - x1.a2), x1.diff }
        )).Concat().Concat().ToList();
      var corr = hedgePrices.IsEmpty() ? double.NaN : MathNet.Numerics.Statistics.Correlation.Pearson(hedgePrices.Select(x => x.price), hedgePrices.Select(x => x.diff));
      return (corr, pos1, pos2);
    }
    ActionAsyncBuffer _CalcHedgeRatioByPositionsAsyncBuffer;
    ActionAsyncBuffer CalcHedgeRatioByPositionsAsyncBuffer => _CalcHedgeRatioByPositionsAsyncBuffer
      ?? (_CalcHedgeRatioByPositionsAsyncBuffer = new ActionAsyncBuffer(() => ServerTime.Minute + ""));
    void OnCalcHedgeRatioByPositions() => CalcHedgeRatioByPositionsAsyncBuffer.Push(CalcHedgeRatioByPositions);
    #endregion

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
                var az = a.Zip(b, (p, n) => HedgePosition.Create((p.contract, p.ratio.Cma(10, n.ratio), n.price, n.context)));
                var x = a?.Any() == false || DateTime.Now > lastScan.AddMinutes(1) ? b : az.ToList();
                return x;
              } catch(Exception exc) {
                Log = exc;
                return new List<HedgePosition<IBApi.Contract>>();
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

  }
}