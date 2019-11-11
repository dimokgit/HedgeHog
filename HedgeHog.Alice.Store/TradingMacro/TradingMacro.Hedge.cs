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
      public static HedgePosition<TContract> Create<TContract>((TContract contract, double ratio, double price, string context, bool isBuy) t)
        => new HedgePosition<TContract>(t.contract, new TContract[0], t.ratio, t.price, t.context, t.isBuy);
      public static HedgePosition<TContract> Create<TContract>((TContract contract, TContract[] options, double ratio, double price, string context, bool isBuy) t)
        => new HedgePosition<TContract>(t.contract, t.options, t.ratio, t.price, t.context, t.isBuy);
      //public static double CalcHedgePrice<Contract>(IList<HedgePosition<Contract>> hps) => hps.Select(hp => (hp.price, multiplier: hp.contract.ComboMultiplier, positions: hp.contract.ratio)).ToArra().CalcHedgePrice();
    }
    public class HedgePosition<TContract> {
      public HedgePosition(TContract contract, TContract[] options, double ratio, double price, string context, bool isBuy) {
        this.contract = contract;
        this.options = options;
        this.ratio = ratio;
        this.price = price;
        this.context = context;
        this.isBuy = isBuy;
      }
      //    public static double CalcHedgePrice(this IList<(double price, int multiplier, double positions)> hedges) => CalcHedgePrice(hedges.Select(h => (h.price, (double)h.multiplier, h.positions)).ToArray());
      public TContract contract { get; }
      public TContract[] options { get; }
      public double ratio { get; }
      public double price { get; }
      public string context { get; }
      public bool isBuy { get; }

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
    IBApi.Contract _currentHedgeContract = null;
    public void SetCurrentHedgePosition<T>(IBApi.Contract contract, int quantity, T context) {
      lock(_currentHedgePositionsLock) {
        _currentHedgeContract = contract;
        var legs = contract.LegsForHedge(Pair).ToList();
        if(legs.Count != 2) {
          Log = new Exception($"{nameof(SetCurrentHedgePosition)}: {new { legsCount = legs.Count }}");
          return;
        }
        var hasOpposite = legs.Distinct(l => l.leg.Action).Count() == 2;
        CurrentHedgePosition1 = legs[0].leg.Ratio;
        var p2 = CurrentHedgePosition2;
        CurrentHedgePosition2 = legs[1].With(l => l.leg.Ratio * (hasOpposite ? -1 : 1));
        CurrentHedgeQuantity = quantity;
        if(p2 != CurrentHedgePosition2)
          Log = new DebugLogException($"{nameof(SetCurrentHedgePosition)}:{new { Pair, contract = contract.ShortString, quantity, context }}");
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
          RunCalcHedgePrices(voltIndex, chp.p1, chp.p2);
        TradingMacroHedged(tmh => {
          var p = new[] { MakeParam(this, chp.p1), MakeParam(tmh, chp.p2) }.CalcHedgePrice();
          SetVolts(p, voltIndex, true);
        });
      }
      return null;
      (double, int, double) MakeParam(TradingMacro tm, double pos) => ((tm.RatesArraySafe.LastBC()?.PriceAvg).GetValueOrDefault(double.NaN), tm.BaseUnitSize, pos);
    }

    private void RunCalcHedgePrices(int voltIndex, int p1, int p2) {
      var sw = Stopwatch.StartNew();
      CalcHedgePrices(p1, p2).ForEach(t => {
        SetVoltByIndex(voltIndex)(t.rate, t.price);
      });
      Debug.WriteLine("RunCalcHedgePrices:" + sw.ElapsedMilliseconds + " ms");
    }

    private IEnumerable<(Rate rate, double price)> CalcHedgePrices(int p1, int p2) =>
      new[] { p1, p2 }.GCD().With(gcd
        => UseRates(ra => TradingMacroHedged(tmh => tmh.UseRates(ram => CalcHedgePricesImpl(ra, tmh, ram, p1.Abs() / gcd, -p2.Abs() / gcd).ToList())))
           .Concat().Concat().Concat());
    private IEnumerable<(Rate rate, double price)> CalcHedgePricesImpl(List<Rate> ra, TradingMacro tmh, List<Rate> ram, double p1, double p2)
      => ra.Zip(r => r.StartDate, ram, r => r.StartDate, (r1, r2) => (r1, r2))
      .Select(t => {
        var p = (new[] { (t.r1.PriceAvg, BaseUnitSize, p1), (t.r2.PriceAvg, tmh.BaseUnitSize, p2) }).CalcHedgePrice();
        return (t.r1, p);
      });

    CorridorStatistics ShowVoltsByGrossVirtual(int voltIndex) {
      var sw = Stopwatch.StartNew();
      try {
        var chp = HedgedTrades().Select(ht => ht.Lots).Pairwise((p1, p2) => (p1, p2, 0)).DefaultIfEmpty(GetCurrentHedgePositions(true)).SingleOrDefault();
        if(!UseCalc() || chp.p1 == 0 || chp.p2 == 0) return null;
        if(HedgeTradesVirtual.IsEmpty()) {
          var period = GetVoltCmaPeriodByIndex(voltIndex);
          var passes = GetVoltCmaPassesByIndex(voltIndex);
          var volts = GetHedgeGrosses(chp.p1, chp.p2, period, passes).Cma(v => v.v, period, passes, (r, v2) => new { r.rate.StartDate, r.v, v2 });//.ToList();
          var start = double.NaN;
          var sv = SetVoltByIndex(voltIndex);
          var sv2 = SetVoltVoltByIndex(voltIndex);
          UseRates(ra => ra.Zip(r => r.StartDate, volts, v => v.StartDate, (r, v) => (r, v.v, v.v2))
          .Do(t => { if(start.IsNaN()) start = t.v; })
          .AsParallel()
          .ForAll(t => {
            sv(t.r, t.v - start);
            sv2(t.r, t.v2 - start);
          }));
          OnSetVoltsHighLowsByRegression(voltIndex);
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
      } finally {
        sw.Stop();
        var s = $"{MethodBase.GetCurrentMethod().Name}[{Pair}]:{new { sw.ElapsedMilliseconds }}";
        if(sw.ElapsedMilliseconds > 100)
          Log = new Exception(s);
        //Debug.WriteLine(s);
      }
    }

    class GetHedgePricesAsyncBuffer :AsyncBuffer<GetHedgePricesAsyncBuffer, Action> {
      public GetHedgePricesAsyncBuffer(Func<string> distinct) : base(1, TimeSpan.Zero, false, distinct) { }
      protected override Action PushImpl(Action a) {
        return a;
      }
    }
    GetHedgePricesAsyncBuffer _setHedgeGrossesAsyncBuffer;
    GetHedgePricesAsyncBuffer SetHedgeGrossesAsyncBuffer => _setHedgeGrossesAsyncBuffer
      ?? (_setHedgeGrossesAsyncBuffer = new GetHedgePricesAsyncBuffer(() => ServerTime.Minute + ""));
    void OnSetExitGrossByHedgeGrossess() => SetHedgeGrossesAsyncBuffer.Push(SetExitPriceByHedgeGrosses);
    private void SetExitPriceByHedgeGrosses() {
      //var hedgedPositions = HedgedTrades().Select(t => t.IsBuy ? t.Lots : -t.Lots).Buffer(2)
      //.Select(b => (p1: b[0], p2: b[1], 0)).ToArray().With(a => a.Any() ? a.Single() : GetCurrentHedgePositions(true));
      var hh = CurrentHedgesByPositions().Select(h => h.ratio).Buffer(2)
        .Select(b => TradesManagerStatic.CalcComboRatios(TradingRatio.ToInt(), b[0], -b[1] * HedgeCorrelation.Sign())).ToList();
      hh.ForEach(h => {
        ExitGrossByHedgePositionsCalc = GetExitPriceByHedgeGrosses(h.r1 * h.quantity, h.r2 * h.quantity);
        var isBuy = h.r1.Sign();
        ExitPriceByHedgePrices = CalcHedgeExitPrice(h.r1, h.r2, isBuy);
      });
      /// Locals
      double CalcHedgeExitPrice(int p1, int p2, int isBuy) {
        var prices = CalcHedgePrices(p1, p2).Select(t => t.price).ToList();
        return prices.Linear().RegressionValue(prices.Count - 1);
      }
    }


    //double GetExitHedgePrice(int position1, int position 2) {
    //  CalcHedgePrices()
    //}
    public double GetExitPriceByHedgeGrosses(int position1, int position2) {
      var prices0 = GetHedgeGrosses(position1, position2, 2, 2).Select(t => t.v).ToList();
      var bufferCount = prices0.Count / 3;
      var bufferSkip = (prices0.Count * 0.05).ToInt();
      var prices1 = prices0.Buffer(bufferCount.Max(1), bufferSkip.Max(1)).Where(b => b.Count == bufferCount);
      var bySDReg = prices1.AsParallel().Select(prices => prices.StDevByRegressoin() * 1.5).DefaultIfEmpty(double.NaN).Max();
      return bySDReg.Round();
    }

    private IEnumerable<(Rate rate, double v)> GetHedgeGrosses(int position1, int position2, double cmaPeriod, int cmaPasses)
      => UseRates(ra => TradingMacroHedged(tmh => tmh.UseRates(rah =>
      (from r1 in ra
       join r2 in rah on r1.StartDate equals r2.StartDate
       select (rate: r1, v: r1.PriceAvg * BaseUnitSize * position1 + r2.PriceAvg * tmh.BaseUnitSize * position2))))
      ).Concat().Concat().Concat()
      .Cma(r => r.v, cmaPeriod, cmaPasses, (r, v) => (r.rate, v));
    public enum HedgeCalcTypes { ByTV, ByHV, ByPos, ByTR };
    public HedgeCalcTypes HedgeCalcType { get; set; } = HedgeCalcTypes.ByHV;
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
                select (h.pair.ContractFactory(IsInVirtualTrading, h.m.ToInt()), new IBApi.Contract[0], h.cp, h.hv, h.m, h.pair + ":" + h.hv.Round(2))
                 ).ToArray();
      return TradesManagerStatic.HedgeRatioByValue(":", TMCorrelation().Single() > 0, hh).Select(HedgePosition.Create).ToList();//.SideEffect(_ => OnCurrentHedgesByHV(_));
    }
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByPositions() {
      if(HedgeRatioByPrices.IsNaNOrZero()) return new List<HedgePosition<IBApi.Contract>>();
      var r = HedgeRatioByPrices.PositionsFromRatio();
      return HedgePositionsByPositions(r.p1, r.p2);
    }
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByTradingRatio() {
      if(TradingRatioHedge.IsNaNOrZero()) return new List<HedgePosition<IBApi.Contract>>();
      var r = TradingRatioHedge.Div(TradingRatio).PositionsFromRatio();
      return HedgePositionsByPositions(r.p1, r.p2);
    }

    private CURRENT_HEDGES HedgePositionsByPositions(double p1, double p2) {
      var isBuy = TMCorrelation().Single() > 1;
      var h1 = (Pair.ContractFactory(IsInVirtualTrading, BaseUnitSize), p1, CurrentPriceAvg(), Pair, isBuy);
      var h2 = TradingMacroHedged(tm => (tm.Pair.ContractFactory(IsInVirtualTrading, tm.BaseUnitSize), p2, tm.CurrentPriceAvg(), tm.Pair, !isBuy));
      var hh = new[] { h1 }.Concat(h2).ToList();
      return hh.Select(HedgePosition.Create).ToList();
    }


    #region CalcHedgeRatioByPositions
    private double HedgeRatioByPrices { get; set; } = double.NaN;
    private int[] _hedgePositionMinMax = new int[] { 0, 1000 };
    void CalcHedgeRatioByPositions() {
      var pos1 = 100;// GetCurrentHedgePositions(true).max;
      if(pos1 == 0) pos1 = 100;
      var sw = Stopwatch.StartNew();
      var hrs = Enumerable.Range(_hedgePositionMinMax[0], _hedgePositionMinMax[1] - _hedgePositionMinMax[0])
        //.Select(p => pos1 - p)
        .Select(pos2 => new { pos1, pos2 })
        .AsParallel()
        .Select(p => CalcHedgeRatioByPositionsCorrelation(p.pos1, p.pos2))
        .ToArray()
        .OrderByDescending(hr => hr.corr)
        .ToArray();
      if(!hrs.Any(t => !t.corr.IsNaN())) return;
      _hedgePositionMinMax = hrs.Take((pos1 * 0.05).ToInt()).Select(t => t.pos2).MinMax().With(a => new[] { a[0] - 20, a[1] + 20 });
      hrs.Take(1).Where(hr => !hr.corr.IsNaNOrZero()).Select(hr => hr.pos2.Div(hr.pos1)).DefaultIfEmpty(double.NaN).ForEach(hr => HedgeRatioByPrices = hr);
      sw.Stop();
      //Debug.Print($"{nameof(CalcHedgeRatioByPositions)}:{sw.Elapsed.TotalSeconds.AutoRound2(3)}sec");
    }
    (double stDev, int pos1, int pos2) CalcHedgeRatioByPositionsStDev(int pos1, int pos2) {
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
      var hedgePrices = UseRates(ra => TradingMacroHedged(tmh => tmh.UseRates(rah =>
          from r1 in ra
          join r2 in rah on r1.StartDate equals r2.StartDate
          let t = (a1: r1.PriceAvg * BaseUnitSize * pos1, a2: r2.PriceAvg * tmh.BaseUnitSize * pos2, diff: GetVoltage2(r1))
          where t.a1.IsNotNaN() && t.a2.IsNotNaN() && t.diff.IsNotNaN()
          select new { price = (t.a1 - t.a2), t.diff }
        ))).Concat().Concat().Concat().ToList();
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
                var az = a?.Zip(b, (p, n) => HedgePosition.Create((p.contract, p.ratio.Cma(10, n.ratio), n.price, n.context,n.isBuy)));
                var x = a?.Any() == false || DateTime.Now > lastScan.AddMinutes(1) ? b : az?.ToList();
                return x ?? new CURRENT_HEDGES();
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

    void ExitGrossByHedgePositionsReset() => _exitGrossByHedgePositions = double.NaN;
    double _exitGrossByHedgePositions = double.NaN;
    public double ExitGrossByHedgePositions {
      get { return _exitGrossByHedgePositions.IfNaN(ExitGrossByHedgePositionsCalc); }
      set {
        _exitGrossByHedgePositions = value;
      }
    }
    public double ExitGrossByHedgePositionsCalc { get; private set; } = double.NaN;
    public double ExitPriceByHedgePrices { get; private set; }

    void OnCurrentHedgesByHV(CURRENT_HEDGES p) {
      CurrentHedgesByHVSubject.OnNext(p);
    }
    #endregion

  }
}