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
//using CURRENT_HEDGES = System.Collections.Generic.List<HedgeHog.Alice.Store.TradingMacro.HedgePosition<IBApi.Contract>>;
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
                      from hvhs in TradingMacroHedged(HVByBarPeriod, 0)
                      from hvh in hvhs
                      select hv / hvh);
          hvps.ForEach(hvp => SetVolts(MapHV(hvp), voltIndex));
        }
        return null;
      }
      double MapHV(double hv) => hv * 100;
      IEnumerable<(Rate[] ra, TradingMacro tm)> HedgedRates(IList<Rate> b) =>
        (from tm in TradingMacroHedged(0)
         from ra in tm.UseRatesInternal(ra => ra.SkipWhile(r => r.StartDate < b[0].StartDate).TakeWhile(r => r.StartDate <= b.Last().StartDate).ToArray())
         select (ra, tm)
         );
    }

    object _currentHedgePositionsLock = new object();
    IBApi.Contract _currentHedgeContract = null;
    public void SetCurrentHedgePosition<T>(IBApi.Contract contract, int quantity, T context) {
      lock(_currentHedgePositionsLock) {
        if(contract.Legs().First().c.Instrument != Pair)
          Debugger.Break();
        _currentHedgeContract = contract;
        var legs = contract.LegsForHedge(Pair).ToList();
        if(legs.Count != 2) {
          Log = new Exception($"{nameof(SetCurrentHedgePosition)}: {new { legsCount = legs.Count }}");
          return;
        }
        CurrentHedgePosition1 = legs[0].leg.Ratio;
        var p2 = CurrentHedgePosition2;
        CurrentHedgePosition2 = legs[1].leg.Ratio * (legs[1].leg.IsBuy ? 1 : -1);
        if(CurrentHedgePosition2 != 0 && TMCorrelation(0).Any(c => c.Sign() == CurrentHedgePosition2.Sign())) {
          //Debugger.Break();
          Log = new SoftException($"{nameof(OnSetCurrentHedgePosition)}:{new { CurrentHedgePosition2, mustHave = "Oposite sign as correlation" }} ");
        }
        CurrentHedgeQuantity = quantity;
        if(false && p2 != CurrentHedgePosition2)
          Log = new DebugLogException($"{nameof(SetCurrentHedgePosition)}:{new { Pair, contract = contract.ShortString, quantity, context }}");
      }
    }
    [TraderOnlyMember]
    public (int p1, int p2, int hedgeIndex) GetCurrentHedgePositions(bool adjustForQuantity = false) {
      if(!IsTrader) return TradingMacroTrader(tm => tm.GetCurrentHedgePositions(adjustForQuantity)).Single();
      lock(_currentHedgePositionsLock)
        return (
          CurrentHedgePosition1 * (adjustForQuantity ? CurrentHedgeQuantity : 1),
          CurrentHedgePosition2 * (adjustForQuantity ? CurrentHedgeQuantity : 1),
          0
          //CurrentHedgePosition1.Abs().Max(CurrentHedgePosition2.Abs()) * (adjustForQuantity ? CurrentHedgeQuantity : 1)
          );
    }
    int _currentHedgePosition1 = 0;
    public int CurrentHedgePosition1 {
      get => _currentHedgePosition1;
      set {
        if(_currentHedgePosition1 == value) return;
        _currentHedgePosition1 = value;
        OnPropertyChanged(nameof(CurrentHedgePosition1));
      }
    }
    int CurrentHedgeQuantity = 0;
    int CurrentHedgePosition2 {
      get => currentHedgePosition2; set {
        if(currentHedgePosition2 == value) return;
        currentHedgePosition2 = value;
        OnPropertyChanged(nameof(CurrentHedgePosition2));
      }
    }

    private int currentHedgePosition2 = 0;
    CorridorStatistics ShowVoltsByHedgePrice(int voltIndex, int hedgeIndex) {
      var chp = GetCurrentHedgePositions();
      if(UseCalc() && chp.p1 != 0) {
        if(true || GetVoltByIndex(voltIndex)(RatesArraySafe[0]).IsNaN())
          RunCalcHedgePrices(voltIndex, chp.p1, chp.p2, hedgeIndex);
        TradingMacroHedged(tmh => {
          var p = new[] { MakeParam(this, chp.p1), MakeParam(tmh, chp.p2) }.CalcHedgePrice();
          SetVolts(p, voltIndex, true);
        }, hedgeIndex);
      }
      return null;
      (double, int, double) MakeParam(TradingMacro tm, double pos) => ((tm.RatesArraySafe.LastBC()?.PriceAvg).GetValueOrDefault(double.NaN), tm.BaseUnitSize, pos);
    }

    private void RunCalcHedgePrices(int voltIndex, int p1, int p2, int hedgeIndex) {
      var sw = Stopwatch.StartNew();
      CalcHedgePrices(p1, p2, hedgeIndex).ForEach(t => {
        SetVoltByIndex(voltIndex)(t.rate, t.price);
      });
      Debug.WriteLine("RunCalcHedgePrices:" + sw.ElapsedMilliseconds + " ms");
    }

    private IEnumerable<(Rate rate, double price)> CalcHedgePrices(int p1, int p2, int hedgeIndex) =>
      new[] { p1, p2 }.GCD().With(gcd
        => UseRates(ra => TradingMacroHedged(tmh => tmh.UseRates(ram => CalcHedgePricesImpl(ra, tmh, ram, p1.Abs() / gcd, -p2.Abs() / gcd).ToList()), hedgeIndex))
           .Concat().Concat().Concat());
    private IEnumerable<(Rate rate, double price)> CalcHedgePricesImpl(List<Rate> ra, TradingMacro tmh, List<Rate> ram, double p1, double p2)
      => ra.Zip(r => r.StartDate, ram, r => r.StartDate, (r1, r2) => (r1, r2))
      .Select(t => {
        var p = (new[] { (t.r1.PriceAvg, BaseUnitSize, p1), (t.r2.PriceAvg, tmh.BaseUnitSize, p2) }).CalcHedgePrice();
        return (t.r1, p);
      });

    CorridorStatistics ShowVoltsByGrossVirtual(int voltIndex, int hedgeIndex) {
      var sw = Stopwatch.StartNew();
      try {
        var chp = GetHedgedTradePositionsOrDefualt();
        if(!UseCalc() || chp.p1 == 0 || chp.p2 == 0) return null;
        if(HedgeTradesVirtual.IsEmpty()) {
          var period = GetVoltCmaPeriodByIndex(voltIndex);
          var passes = GetVoltCmaPassesByIndex(voltIndex);
          var volts0 = GetHedgeGrosses(chp.p1, chp.p2, period, passes, hedgeIndex).ToArray();
          if(volts0.IsEmpty()) return null;
          var vMin = volts0.Min(v => v.v);
          var vMax = volts0.Max(v => v.v);
          var volts01 = volts0.Select(t => t.v - vMin).ToList();

          var volts = volts0.Select(t => new { t.rate, v = t.v - vMin }).Cma(v => v.v, period, passes, (r, v2) => new { r.rate.StartDate, r.v, v2 }).ToList();
          var sv = SetVoltByIndex(voltIndex);
          var sv2 = SetVoltVoltByIndex(voltIndex);
          UseRates(ra => ra.Zip(r => r.StartDate, volts, v => v.StartDate, (r, v) => (r, v.v, v.v2))
          //.Do(t => { if(start.IsNaN()) start = t.v; })
          //.AsParallel()
          .ForEach(t => {
            sv(t.r, t.v/* - start*/);
            sv2(t.r, t.v2/* - start*/);
          }));
          OnSetVoltsHighLowsByRegression(voltIndex);
        } else {
          var hedgedTrades = HedgeTradesVirtual
            .Where(ht => ht.Select(t => t.Pair).With(pairs => pairs.Contains(Pair) && TradingMacroHedged(hedgeIndex).Any(tm => pairs.Contains(tm.Pair))))
            .Concat()
            .OrderByDescending(t => t.Pair == Pair)
            .Buffer(2)
            .SelectMany(b => new[] { (tm: this, t: b[0]) }.Concat(TradingMacroHedged(tm => (tm, t: b[1]), hedgeIndex)))
            .ToArray();
          var tuples = (from ht in hedgedTrades
                        from x in ht.tm.UseRates(ra => ra.Select(r
                        => (d: r.StartDate
                        , t: (r, n: ht.t.CalcNetPL((ht.t.Close = ht.t.IsBuy ? r.BidLow : r.AskHigh) * ht.tm.BaseUnitSize)))))
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
        //Debug.WriteLine(s);
        _ShowVoltsByGrossVirtualElapsed = _ShowVoltsByGrossVirtualElapsed.Cma(10, sw.ElapsedMilliseconds);
        if(IsInVirtualTrading && !Debugger.IsAttached && _ShowVoltsByGrossVirtualElapsed > 100) {
          var s = $"{nameof(ShowVoltsByGrossVirtual)}[{this}]:{new { _ShowVoltsByGrossVirtualElapsed }}";
          Log = new Exception(s);
        }
      }
    }
    double _ShowVoltsByGrossVirtualElapsed = double.NaN;
    [TraderOnlyMember]
    int HedgeIndexByPair(string hp) => PairHedges.Select((h, i) => (h, i)).Where(t => t.h == hp).Select(t => t.i).SingleOrDefault();
    private (int p1, int p2, int hedgeIndex) GetHedgedTradePositionsOrDefualt() =>
      !IsTrader ? TradingMacroTrader(tm => tm.GetHedgedTradePositionsOrDefualt()).Single()
      : HedgedTrades()
      .Select(ht => (p: (int)ht.Position, ht.Pair))
      .Pairwise((p1, p2) => (p1.p * p1.p.Sign(), p2.p * p1.p.Sign(), HedgeIndexByPair(p2.Pair)))
      .DefaultIfEmpty(GetCurrentHedgePositions(true)).SingleOrDefault();

    class GetHedgePricesAsyncBuffer :AsyncBuffer<GetHedgePricesAsyncBuffer, Action> {
      public GetHedgePricesAsyncBuffer(Func<string> distinct) : base(1, TimeSpan.Zero, false, distinct) { }
      protected override Action PushImpl(Action a) {
        return a;
      }
    }
    GetHedgePricesAsyncBuffer _setHedgeGrossesAsyncBuffer;
    GetHedgePricesAsyncBuffer SetHedgeGrossesAsyncBuffer => _setHedgeGrossesAsyncBuffer
      ?? (_setHedgeGrossesAsyncBuffer = new GetHedgePricesAsyncBuffer(() => ServerTime.Second / 5 + ""));
    void OnSetExitGrossByHedgeGrossess(bool isSync = false) {
      if(!isSync)
        SetHedgeGrossesAsyncBuffer.Push(SetExitPriceByHedgeGrosses);
      else
        SetExitPriceByHedgeGrosses();
    }
    private void SetExitPriceByHedgeGrosses() {
      int hedgeIndex = 0;
      //var hedgedPositions = HedgedTrades().Select(t => t.IsBuy ? t.Lots : -t.Lots).Buffer(2)
      //.Select(b => (p1: b[0], p2: b[1], 0)).ToArray().With(a => a.Any() ? a.Single() : GetCurrentHedgePositions(true));
      var hh = GetHedgedTradePositionsOrDefualt();
      if(hh.p1 * hh.p2 == 0) return;
      ExitGrossByHedgePositionsCalc = GetExitPriceByHedgeGrosses(hh.p1, hh.p2, hh.hedgeIndex);
      var isBuy = hh.p1.Sign();
      ExitPriceByHedgePrices = CalcHedgeExitPrice(hh.p1, hh.p2, isBuy);
      /// Locals
      double CalcHedgeExitPrice(int p1, int p2, int isBuy) {
        var prices = CalcHedgePrices(p1, p2, hedgeIndex).Select(t => t.price).ToList();
        return prices.Linear().RegressionValue(prices.Count - 1);
      }
    }


    //double GetExitHedgePrice(int position1, int position 2) {
    //  CalcHedgePrices()
    //}

    static int bufferCountRatio = 3;
    public double GetExitPriceByHedgeGrosses(int position1, int position2, int hedgeIndex) {
      var prices0 = VoltageFunction == VoltageFunction.GrossV
        ? UseRates(ra => ra.Select(GetVoltage).ToList()).Single()
        : GetHedgeGrosses(position1, position2, 2, 2, hedgeIndex).Select(t => t.v).ToList();
      //return prices0.StandardDeviation().Round();
      return prices0.Height() * TakeProfitXRatio;
      var bufferCount = prices0.Count / bufferCountRatio;
      var bufferSkip = (prices0.Count * 0.05).ToInt();
      var prices1 = prices0.Buffer(bufferCount.Max(1), bufferSkip.Max(1)).Where(b => b.Count == bufferCount);
      var bySDReg = prices1.AsParallel().Select(prices => prices.StDevByRegressoin() * TakeProfitXRatio).DefaultIfEmpty(double.NaN).Average();
      return bySDReg.Round();
    }

    private IEnumerable<(Rate rate, double v)> GetHedgeGrosses(int position1, int position2, double cmaPeriod, int cmaPasses, int hedgeIndex)
      => UseRates(ra => TradingMacroHedged(tmh => tmh.UseRates(rah =>
      (from bu in new[] { BaseUnitSize }
       from buh in new[] { tmh.BaseUnitSize }
       from r1 in ra
       join r2 in rah on r1.StartDate equals r2.StartDate
       select (rate: r1, v: r1.PriceAvg * bu * position1 + r2.PriceAvg * buh * position2))), hedgeIndex)
      ).Concat().Concat().Concat()
      .Cma(r => r.v, cmaPeriod, cmaPasses, (r, v) => (r.rate, v));
    public enum HedgeCalcTypes { ByPos = 0, ByTV, ByHV, ByGross, ByGrosss, ByTR, ByPoss };
    private HedgeCalcTypes hedgeCalcType;
    [TraderOnlyMember]
    public HedgeCalcTypes HedgeCalcType {
      get => TradingMacroTrader(tm => tm.hedgeCalcType).SingleOrDefault();
      set {
        if(hedgeCalcType == value) return;
        hedgeCalcType = value;
        OnPropertyChanged(nameof(HedgeCalcType));
      }
    }
    private int hedgeCalcIndex;
    [TraderOnlyMember]
    public int HedgeCalcIndex {
      get => TradingMacroTrader(tm => tm.hedgeCalcIndex).SingleOrDefault();
      set { if(hedgeCalcIndex == value) return; hedgeCalcIndex = value; OnPropertyChanged(nameof(HedgeCalcIndex)); }
    }
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByHV(int count, int hedgeIndex) => CurrentHedgesByHV(count, DateTime.MaxValue, BarPeriodInt > 0, hedgeIndex);
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByHV(int hedgeIndex) => CurrentHedgesByHV(int.MaxValue, hedgeIndex);
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByHV(int count, DateTime end, bool isDaily, int hedgeIndex) {
      var hp = TradingMacroHedged(tm => tm.HistoricalVolatilityByPoints(count / tm.BarPeriodInt.Max(1), end, isDaily)
      .Where(hv => hv != 0)
      .Select(hv => (pair: tm.Pair, hv, cp: tm.CurrentPriceAvg(), m: (double)tm.BaseUnitSize)), hedgeIndex
      ).Concat().ToArray();
      var hh = (from h in HistoricalVolatilityByPoints(count / BarPeriodInt.Max(1), end, isDaily)
                .Where(hv => hv != 0)
                .Select(hv => (pair: Pair, hv, cp: CurrentPriceAvg(), m: (double)BaseUnitSize)
                ).Concat(hp)
                where hp.Any() && hp.All(x => !double.IsInfinity(x.hv))
                select (h.pair.ContractFactory(IsInVirtualTrading, h.m.ToInt()), new IBApi.Contract[0], h.cp, h.hv, h.m, h.pair + ":" + h.hv.Round(2))
                 ).ToArray();
      return TradesManagerStatic.HedgeRatioByValue(":", TMCorrelation(hedgeIndex).Single() > 0, hh).Select(HedgePosition.Create).ToList();//.SideEffect(_ => OnCurrentHedgesByHV(_));
    }
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByPositions(int hedgeIndex) {
      var hr = GetHedgeRatioByPrices(hedgeIndex);
      if(hr.IsNaNOrZero()) return new List<HedgePosition<IBApi.Contract>>();
      var r = hr.PositionsFromRatio();
      var corr = TMCorrelation(hedgeIndex).Single();
      return HedgePositionsByPositions(r.p1, r.p2 * corr, hedgeIndex);
    }
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByPositionsGross(int hedgeIndex) {
      var hr = GetHedgeRatioByGrosses(hedgeIndex);
      if(hr.IsNaNOrZero()) return new List<HedgePosition<IBApi.Contract>>();
      var r = hr.PositionsFromRatio();
      return HedgePositionsByPositions(r.p1, r.p2, hedgeIndex);
    }
    public List<HedgePosition<IBApi.Contract>> CurrentHedgesByTradingRatio(int hedgeIndex) {
      if(TradingRatioHedge.IsNaNOrZero()) return new List<HedgePosition<IBApi.Contract>>();
      var r = TradingRatioHedge/*.Div(TradingRatio)*/.PositionsFromRatio();
      var corr = TMCorrelation(hedgeIndex).Single();
      return HedgePositionsByPositions(r.p1, r.p2 * corr, hedgeIndex);
    }
    double _currentHedgeRatio;
    ActionAsyncBuffer SetCurrentHedgePositionAsyncBuffer = new ActionAsyncBuffer();
    void OnSetCurrentHedgePosition() {
      var hci = TradingMacroTrader(tm => tm.HedgeCalcIndex).Single();
      if(hci != PairIndex || !IsPairHedged) return;
      int hedgeIndex = 0;
      if(IsInVirtualTrading) a();
      else
        SetCurrentHedgePositionAsyncBuffer.Push(a);
      void a() {
        var tm = TradingMacroTrader().Single();
        var hct = tm.HedgeCalcType;
        var tr = tm.TradingRatio;
        IObservable<(IBApi.Contract contract, int quantity)> combo;
        List<HedgePosition<IBApi.Contract>> hh;
        switch(hct) {
          case HedgeCalcTypes.ByHV:
            hh = CurrentHedgesByHV(hedgeIndex);
            break;
          case HedgeCalcTypes.ByPos:
            hh = CurrentHedgesByPositions(hedgeIndex);
            break;
          case HedgeCalcTypes.ByGross:
            hh = CurrentHedgesByPositionsGross(hedgeIndex);
            break;
          case HedgeCalcTypes.ByTR:
            hh = CurrentHedgesByTradingRatio(hedgeIndex);
            break;
          case HedgeCalcTypes.ByTV:
            hh = new List<HedgePosition<IBApi.Contract>>();
            break;
          default: throw new Exception();
        }
        if(hh.Any()) {
          combo = IBApp.AccountManager.MakeHedgeComboSafe(tr.ToInt(), hh[0].contract, hh[1].contract, hh[0].ratio, hh[1].ratio, IsInVirtualTrading);
          combo.Subscribe(c => tm.SetCurrentHedgePosition(c.contract, c.quantity, hct));
        }
      }
    }
    private List<HedgePosition<IBApi.Contract>> HedgePositionsByPositions(double p1, double p2, int hedgeIndex) {
      var isBuy = TMCorrelation(hedgeIndex).Single() > 1;
      var h1 = (Pair.ContractFactory(IsInVirtualTrading, BaseUnitSize), p1, CurrentPriceAvg(), Pair, isBuy);
      var h2 = TradingMacroHedged(tm => (tm.Pair.ContractFactory(IsInVirtualTrading, tm.BaseUnitSize), p2, tm.CurrentPriceAvg(), tm.Pair, !isBuy), hedgeIndex);
      var hh = new[] { h1 }.Concat(h2).ToList();
      return hh.Select(HedgePosition.Create).ToList();
    }


    #region CalcHedgeRatioByPositions
    private double _HedgeRatioByPrices { get; set; } = double.NaN;
    private double _HedgeRatioByPrices2 { get; set; } = double.NaN;
    Action<double> SetHedgeRatioByPrices(int index) => index == 0 ? new Action<double>(p => _HedgeRatioByPrices = p) : p => _HedgeRatioByPrices2 = p;
    double GetHedgeRatioByPrices(int index) => index == 0 ? _HedgeRatioByPrices : _HedgeRatioByPrices2;
    private int[][] _hedgePositionMinMax2 = new[] { new int[] { 0, 200 }, new int[] { 0, 200 } };

    private double _HedgeRatioByGrosses { get; set; } = double.NaN;
    private double _HedgeRatioByGrosses2 { get; set; } = double.NaN;
    Action<double> SetHedgeRatioByGrosses(int index) => index == 0 ? new Action<double>(p => _HedgeRatioByGrosses = p) : p => _HedgeRatioByGrosses2 = p;
    double GetHedgeRatioByGrosses(int index) => index == 0 ? _HedgeRatioByGrosses : _HedgeRatioByGrosses2;
    private int[][] _hedgePositionGrossMinMax2 = new[] { new int[] { 0, 200 }, new int[] { 0, 200 } };

    void CalcHedgeRatioByPositions(int hedgeIndex) {
      if(!TradingMacroHedged(hedgeIndex).Any()) return;
      var pos1 = 100;// GetCurrentHedgePositions(true).max;
      if(pos1 == 0) pos1 = 100;
      var sw = Stopwatch.StartNew();
      var _hedgePositionMinMax = _hedgePositionMinMax2[hedgeIndex];
      var hrs = Enumerable.Range(_hedgePositionMinMax[0], _hedgePositionMinMax[1] - _hedgePositionMinMax[0])
        //.Select(p => pos1 - p)
        .Select(pos2 => new { pos1, pos2 })
        .AsParallel()
        .Select(p => CalcHedgeRatioByPositionsCorrelation(p.pos1, p.pos2, hedgeIndex))
        .ToArray()
        .OrderByDescending(hr => hr.corr)
        .ToArray();
      if(!hrs.Any(t => !t.corr.IsNaN())) return;
      _hedgePositionMinMax2[hedgeIndex] = hrs.Take(15).Select(t => t.pos2).MinMax().With(a => new[] { a[0] - 15, a[1] + 15 });
      hrs.Take(1).Where(hr => !hr.corr.IsNaNOrZero()).Select(hr => hr.pos2.Div(hr.pos1)).DefaultIfEmpty(double.NaN).ForEach(hr => SetHedgeRatioByPrices(hedgeIndex)(hr));
      sw.Stop();
      //Debug.Print($"{nameof(CalcHedgeRatioByPositions)}:{sw.Elapsed.TotalSeconds.AutoRound2(3)}sec");
    }
    void CalcHedgeRatioByGrosses(int hedgeIndex) {
      var pos1 = 100;// GetCurrentHedgePositions(true).max;
      if(pos1 == 0) pos1 = 100;
      var sw = Stopwatch.StartNew();
      var _hedgePositionMinMax = _hedgePositionGrossMinMax2[hedgeIndex];
      var hrs = Enumerable.Range(_hedgePositionMinMax[0], _hedgePositionMinMax[1] - _hedgePositionMinMax[0])
        //.Select(p => pos1 - p)
        .Select(pos2 => new { pos1, pos2 })
        .AsParallel()
        .Select(p => CalcHedgeRatioByPositionsGross(p.pos1, p.pos2, hedgeIndex))
        .ToArray()
        .OrderBy(hr => hr.gross)
        .ToArray();
      if(!hrs.Any(t => !t.gross.IsNaN())) return;
      _hedgePositionGrossMinMax2[hedgeIndex] = hrs.Take(15).Select(t => t.pos2).MinMax().With(a => new[] { a[0] - 15, a[1] + 15 });
      hrs.Take(1).Where(hr => !hr.gross.IsNaNOrZero()).Select(hr => hr.pos2.Div(hr.pos1)).DefaultIfEmpty(double.NaN).ForEach(hr => SetHedgeRatioByGrosses(hedgeIndex)(hr));
      sw.Stop();
      //Debug.Print($"{nameof(CalcHedgeRatioByPositions)}:{sw.Elapsed.TotalSeconds.AutoRound2(3)}sec");
    }
    (double gross, int pos1, int pos2) CalcHedgeRatioByPositionsGross(int pos1, int pos2, int hedgeIndex) {
      var sign = TMCorrelation(hedgeIndex).DefaultIfEmpty(1).Single() == 1 ? -1 : 1;
      var grosses = GetHedgeGrosses(pos1, pos2 * sign, 0, 0, hedgeIndex);
      var grossRange = grosses.Select(t => t.v).RelativeToHeightStandardDeviation();
      return (grossRange.Abs(), pos1, pos2);
    }
    private (double corr, int pos1, int pos2) CalcHedgeRatioByPositionsCorrelation(int pos1, int pos2, int hedgeIndex) {
      var getVolt = GetVoltByIndex(hedgeIndex == 1 ? 0 : 1);
      var hedgePrices = UseRates(ra => TradingMacroHedged(tmh => tmh.UseRates(rah =>
          from r1 in ra
          from corr in TMCorrelation(hedgeIndex)
          join r2 in rah on r1.StartDate equals r2.StartDate
          let t = (a1: r1.PriceAvg * BaseUnitSize * pos1, a2: r2.PriceAvg * tmh.BaseUnitSize * pos2, diff: getVolt(r1))
          where t.a1.IsNotNaN() && t.a2.IsNotNaN() && t.diff.IsNotNaN()
          select new { price = (t.a1 - t.a2 * corr), t.diff }
        ), hedgeIndex)).Concat().Concat().Concat().ToList();
      var corr = hedgePrices.IsEmpty() ? double.NaN : MathNet.Numerics.Statistics.Correlation.Pearson(hedgePrices.Select(x => x.price), hedgePrices.Select(x => x.diff));
      //var corr = hedgePrices.IsEmpty() ? double.NaN : hedgePrices.Average(x => x.price).Abs() / hedgePrices.Height(x => x.price);
      //var corr = hedgePrices.IsEmpty() ? double.NaN : 1 / hedgePrices.Select(x => x.price).ToArray().LinearSlope().Abs();
      return (corr, pos1, pos2);
    }
    ActionAsyncBuffer _CalcHedgeRatioByPositionsAsyncBuffer;
    ActionAsyncBuffer CalcHedgeRatioByPositionsAsyncBuffer => _CalcHedgeRatioByPositionsAsyncBuffer
      ?? (_CalcHedgeRatioByPositionsAsyncBuffer = IsInVirtualTrading
      ? new ActionAsyncBuffer()
      : new ActionAsyncBuffer(() => (BarPeriodInt > 10 ? ServerTime.Minute : ServerTime.Second) % 10 + ""));

    void OnCalcHedgeRatioByPositions(bool runSync = false) {
      if(runSync) CalcHedgeRatios();
      else CalcHedgeRatioByPositionsAsyncBuffer.Push(CalcHedgeRatios);
    }

    object _calcHedgeRatioByPositionsGate = new object();
    void CalcHedgeRatios() {
      lock(_calcHedgeRatioByPositionsGate) {
        CalcHedgeRatioByPositions(0); // Hedge 0
        CalcHedgeRatioByPositions(1); // Hedge 1
        if(false) {
          CalcHedgeRatioByGrosses(0);
          CalcHedgeRatioByGrosses(1);
        }
        OnSetCurrentHedgePosition();
      }
    }
    #endregion

    #region CurrentHedgesByHV Subject
    public IObservable<List<HedgePosition<IBApi.Contract>>> CurrentHedgesByHVObserver { get; private set; }
    object _CurrentHedgesByHVSubjectLocker = new object();
    ISubject<List<HedgePosition<IBApi.Contract>>> _CurrentHedgesByHVSubject;
    ISubject<List<HedgePosition<IBApi.Contract>>> CurrentHedgesByHVSubject {
      get {
        lock(_CurrentHedgesByHVSubjectLocker)
          if(_CurrentHedgesByHVSubject == null) {
            _CurrentHedgesByHVSubject = new Subject<List<HedgePosition<IBApi.Contract>>>();
            DateTime lastScan = DateTime.MinValue;
            CurrentHedgesByHVObserver = _CurrentHedgesByHVSubject.InitBufferedObservable((a, b) => {
              try {
                var az = a?.Zip(b, (p, n) => HedgePosition.Create((p.contract, p.ratio.Cma(10, n.ratio), n.price, n.context, n.isBuy)));
                var x = a?.Any() == false || DateTime.Now > lastScan.AddMinutes(1) ? b : az?.ToList();
                return x ?? new List<HedgePosition<IBApi.Contract>>();
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

    void OnCurrentHedgesByHV(List<HedgePosition<IBApi.Contract>> p) {
      CurrentHedgesByHVSubject.OnNext(p);
    }

    private class TraderOnlyMemberAttribute :Attribute {
    }
    #endregion

  }
}