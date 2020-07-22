using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog;
using HedgeHog.Bars;
using System.Reactive.Concurrency;
using System.Threading;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using TL = HedgeHog.Bars.Rate.TrendLevels;
using static HedgeHog.IEnumerableCore;
using System.Collections.Concurrent;
using HedgeHog.Shared;
using System.Diagnostics;
using IBApi;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    Func<CorridorStatistics> GetShowVoltageFunction() {
      return GetShowVoltageFunction(VoltageFunction);
    }
    Func<CorridorStatistics> GetShowVoltageFunction(VoltageFunction voltageFunction, int voltIndex = 0) {
      var getVolts = GetVoltByIndex(voltIndex);
      var setVolts = SetVoltByIndex(voltIndex);
      var setVoltHigh = voltIndex == 0 ? new Action<double>(v => GetVoltageHigh = () => new[] { v }) : v => GetVoltage2High = () => new[] { v };
      var setVoltLow = voltIndex == 0 ? new Action<double>(v => GetVoltageAverage = () => new[] { v }) : v => GetVoltage2Low = () => new[] { v };

      switch(voltageFunction) {
        case HedgeHog.Alice.VoltageFunction.None:
          return ShowVoltsNone;
        case HedgeHog.Alice.VoltageFunction.t1:
          return () => {
            (voltIndex == 0 ? (Action)SetVoltsM1 : SetVoltsM1_2)();
            return null;
          };
        case HedgeHog.Alice.VoltageFunction.BSTip:
          return () => ShowVoltsByBSTip(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.StDev:
          return ShowVoltsByStDev;
        case HedgeHog.Alice.VoltageFunction.AvgLineRatio:
          return ShowVoltsByAvgLineRatio;
        case HedgeHog.Alice.VoltageFunction.Rsd:
          return ShowVoltsByRsd;
        case HedgeHog.Alice.VoltageFunction.TLH:
          return () => ShowVoltsByTLHn(1, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLH3:
          return () => ShowVoltsByTLHn(3, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLH2:
          return () => ShowVoltsByTLHn(2, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLHR:
          return () => ShowVoltsByTLHR(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLA:
          return () => ShowVoltsByTLA(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLAR:
          return () => ShowVoltsByTLAR(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.BBSD:
          return () => ShowVoltsByBBSD(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.PPM:
          return () => ShowVoltsByPPM(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.PPMB:
          return () => ShowVoltsByPPMB(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.PPMH:
          return ShowVoltsByPPMH;
        //case HedgeHog.Alice.VoltageFunction.AO:
        //  return ShowVoltsByAO;
        case HedgeHog.Alice.VoltageFunction.MPH:
          return ShowVoltsByMPH;
        case HedgeHog.Alice.VoltageFunction.PpmM1:
          return () => { SetVoltsByPpm(); return null; };
        case HedgeHog.Alice.VoltageFunction.Corr:
          return ShowVoltsByCorrelation;
        case HedgeHog.Alice.VoltageFunction.Straddle:
          return () => ShowVoltsByStraddle(sh => sh.delta, voltIndex);
        case HedgeHog.Alice.VoltageFunction.PutStrdl:
          return () => ShowVoltsByStraddleSpread();
        case HedgeHog.Alice.VoltageFunction.StraddleA:
          return () => ShowVoltsByStraddle(sh => sh.ask == 0 ? 0 : sh.ask / sh.delta - 1, voltIndex);
        case HedgeHog.Alice.VoltageFunction.Gross:
          return ShowVoltsByGross;
        case HedgeHog.Alice.VoltageFunction.GrossV:
          return () => ShowVoltsByGrossVirtual(voltIndex, 0);
        case HedgeHog.Alice.VoltageFunction.RatioDiff:
          return () => ShowVoltsByRatioDiff(voltIndex, 0);
        case HedgeHog.Alice.VoltageFunction.RatioDiff2:
          return () => ShowVoltsByRatioDiff(voltIndex, 1);
        case HedgeHog.Alice.VoltageFunction.StDevDiff:
          return () => ShowVoltsByStDevDiff(voltIndex);
        case HedgeHog.Alice.VoltageFunction.HV:
          return () => ShowVoltsByHV(voltIndex);
        case HedgeHog.Alice.VoltageFunction.HedgeRatio:
          return () => ShowVoltsByHedgeRatio(voltIndex);
        case HedgeHog.Alice.VoltageFunction.HedgePrice:
          return () => ShowVoltsByHedgePrice(voltIndex, 0);
        case HedgeHog.Alice.VoltageFunction.HVR:
          return () => ShowVoltsByHVR(voltIndex);
        case HedgeHog.Alice.VoltageFunction.StdRatio:
          return () => ShowVoltsByStdRatio(voltIndex);
        case VoltageFunction.StdOverPrice:
          return () => ShowVoltsByStdOverPrice(voltIndex);
        //StdOverCurrPriceRatio
        case HedgeHog.Alice.VoltageFunction.StdRatioLime:
          return () => ShowVoltsByStdRatioLime(voltIndex);
        case HedgeHog.Alice.VoltageFunction.Slope:
          return ShowVoltsBySlope;
      }
      throw new NotSupportedException(VoltageFunction + " not supported.");
    }

    CorridorStatistics ShowVoltsByVolume() {
      RatesArray.AsEnumerable().Reverse().TakeWhile(r => GetVoltage(r).IsNaN())
        .ForEach(rate => { SetVoltage(rate, rate.Volume); });
      return ShowVolts(RateLast.Volume, 2);
    }
    CorridorStatistics ShowVoltsByRsd() {
      if(UseCalc())
        return UseRates(rates => rates.ToArray(_priceAvg).StDevByRegressoin())
          .Select(rsd => ShowVolts(InPips(rsd), 2))
          .SingleOrDefault();
      return null;
    }
    CorridorStatistics ShowVoltsByPPM(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var useCalc = UseCalc();
      Func<Rate, double> price = _priceAvg;
      Func<IEnumerable<double>> calcVolt = ()
        => UseRates(rates
        => {
          var range0 = rates
          .BackwardsIterator()
          .Take(rates.Count.Div(4).ToInt())
          ;
          var range = range0
          .Pairwise()
          .Select(t => new { d = price(t.Item1).Abs(price(t.Item2)), ts = (t.Item1.StartDate - t.Item2.StartDate).TotalMinutes })
          .ToArray();
          var tsa = range.Average(x => x.ts).Max(BarPeriod <= BarsPeriodType.t1 ? 1 : BarPeriodInt) * 3;
          range = range.Where(x => x.ts < tsa).ToArray();
          var dMax = range
          .AverageByPercentage(x => x.d, (v, a) => v > a, 0.05)
          .Min(x => x.d);
          range = range.Where(x => x.d <= dMax).ToArray();
          var d = range.Sum(x => x.d);
          var ts = range.Sum(x => x.ts);
          return d / ts;
        }
        )
        .Where(ppm => ppm > 0)
        .Select(ppm => InPips(ppm));
      if(!useCalc)
        return GetLastVolt().Take(1).Select(v => ShowVolts(v, 2, getVolt, setVolt)).SingleOrDefault();

      return calcVolt()
        .Select(volt => ShowVolts(useCalc ? volt : GetLastVolt().DefaultIfEmpty(volt).Single(), 2, getVolt, setVolt))
        .SingleOrDefault();
    }

    Tuple<DateTime, DateTime, double> _tlBlue = new Tuple<DateTime, DateTime, double>(DateTime.MinValue, DateTime.MaxValue, double.NaN);

    CorridorStatistics ShowVoltsByPPMB(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var useCalc = UseCalc();

      Func<DateTime, DateTime, double[]> getmemoize = (ds, de) => _tlBlue.Item1 == ds && _tlBlue.Item2 == de ? new[] { _tlBlue.Item3 } : new double[0];
      Func<DateTime, DateTime, double, double> setmemoize = (ds, de, v) => { _tlBlue = Tuple.Create(ds, de, v); return v; };

      Func<IEnumerable<double>> calcVolt = ()
        => TLBlue.PPMB
        .IfEmpty(() => TLBlue.PPMB = TLBlue.Rates.Cma(_priceAvg, 1)
        .Distances().TakeLast(1)
        .Select(l => l / TLBlue.TimeSpan.TotalMinutes)
        .Where(ppm => ppm > 0)
        .Select(ppm => setmemoize(TLBlue.StartDate, TLBlue.EndDate, InPips(ppm)))
        .ToArray())
        .Take(1);
      if(!useCalc)
        return GetLastVolt().Concat(calcVolt()).Take(1).Select(v => ShowVolts(v, 2, getVolt, setVolt)).SingleOrDefault();

      return calcVolt()
        .Select(volt => ShowVolts(useCalc ? volt : GetLastVolt().DefaultIfEmpty(volt).Single(), 2, getVolt, setVolt))
        .SingleOrDefault();
    }
    bool UseCalc() => IsRatesLengthStable && TradingMacrosByPair(tm => tm.BarPeriod != BarsPeriodType.t1).All(tm => tm.IsRatesLengthStable);
    CorridorStatistics ShowVoltsByBBSD(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var useCalc = UseCalc();
      if(!useCalc)
        return GetLastVolt().Take(1).Select(v => ShowVolts(v, 2, getVolt, setVolt)).SingleOrDefault();
      return _boilingerStDev.Value?.Select(v => ShowVolts(v.Item1.Div(v.Item2).ToPercent(), 2, getVolt, setVolt)).SingleOrDefault();
    }

    CorridorStatistics ShowVoltsByUDB(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var useCalc = UseCalc();
      Func<DateTime, DateTime, double[]> getmemoize = (ds, de) => _tlBlue.Item1 == ds && _tlBlue.Item2 == de ? new[] { _tlBlue.Item3 } : new double[0];
      Func<DateTime, DateTime, double, double> setmemoize = (ds, de, v) => { _tlBlue = Tuple.Create(ds, de, v); return v; };
      Func<TL, IEnumerable<double>> calcVolt = tl
        => getmemoize(tl.StartDate, tl.EndDate)
        .Concat(() => tl.Rates.Cma(_priceAvg, 1)
        .Distances().TakeLast(1)
        .Select(l => l / tl.TimeSpan.TotalMinutes)
        .Where(ppm => ppm > 0)
        .Select(ppm => setmemoize(tl.StartDate, tl.EndDate, InPips(ppm))))
        .Take(1);
      if(!useCalc)
        return GetLastVolt().Concat(calcVolt(TLBlue)).Take(1).Select(v => ShowVolts(v, 2, getVolt, setVolt)).SingleOrDefault();

      return calcVolt(TLBlue)
        .Select(volt => ShowVolts(useCalc ? volt : GetLastVolt().DefaultIfEmpty(volt).Single(), 2, getVolt, setVolt))
        .SingleOrDefault();
    }


    CorridorStatistics ShowVoltsByPPMH() {
      var useCalc = UseCalc();
      if(!useCalc)
        return GetLastVolt()
          .Select(v => ShowVolts(v, 2))
          .SingleOrDefault();
      var calcVolt = MonoidsCore.ToFunc(() =>
       (from cmas in UseRates(rates => rates.Select(_priceAvg).ToArray())
        let ppms = cmas.Distances()
        .TakeLast(1)
        .ToArray(d => d / RatesDuration)
        let h = cmas.Height()
        let sd = cmas.StDevByRegressoin()
        let hsd = h / sd / 4
        from ppm in ppms
        select new { ppm, hsd })
        .Where(x => x.ppm > 0));

      calcVolt().ForEach(v => {
        SetVots(InPips(v.ppm), 2);
        SetVolts(v.hsd, GetVoltage2, SetVoltage2, 2);
      });
      return null;
    }

    CorridorStatistics ShowVoltsByAO() {
      var useCalc = UseCalc();

      if(!useCalc)
        return GetLastVolt()
          .Select(v => ShowVolts(v, 2))
          .SingleOrDefault();
      var map = MonoidsCore.ToFunc((IList<Rate>)null, 0.0, (rate, ma) => new { rate, ma });

      Func<Rate, double> select = BarPeriod >= BarsPeriodType.m1 ? _priceAvg : r => r.PriceCMALast;

      CalcSma(AoPeriodFast, AoPeriodSlow, @select, GetVoltage, SetVoltage).ForEach(v => SetVots(v, 2));
      CalcSma(AoPeriodFast2, AoPeriodSlow2, @select, GetVoltage2, SetVoltage2).ForEach(v => SetVolts(v, GetVoltage2, SetVoltage2, 2));

      return null;
    }

    CorridorStatistics ShowVoltsByMPH() {
      var useCalc = UseCalc();

      if(useCalc)
        SetVots(RatesDuration / InPips(_RatesHeight), 2);

      return null;
    }

    private bool IsRatesLengthStableGlobal() {
      return TradingMacrosByPair().All(tm => tm.IsRatesLengthStable);
    }

    CorridorStatistics ShowVoltsByTLHn(int tlCount, Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      Func<TL, double[]> tlMM = tl => tl.PriceMax.Concat(tl.PriceMin).ToArray();
      var tl3 = TrendLinesTrendsAll.OrderBy(tl => tl.EndDate).TakeLast(tlCount + 1).ToArray();
      if(!tl3.Any(tl => tl.IsEmpty) && tl3.Length == tlCount + 1 && IsRatesLengthStable) {
        Func<TL, DateTime[]> dateRange = tl => {
          var slack = (tl.TimeSpan.TotalMinutes * .1).FromMinutes();
          return new[] { tl.StartDate.Add(slack), tl.EndDate.Subtract(slack) };
        };
        var tl3MM = tl3.Select(tl => tlMM(tl)).ToArray();
        var overlap = (from tlmm1 in tl3MM.TakeLast(1)
                       from tlmm2 in tl3MM.Take(tlCount)
                       let ol = tlmm1.OverlapRatio(tlmm2)
                       select ol
                       ).ToArray();
        if(overlap.Any())
          ShowVolts(overlap.Average().ToPercent(), VoltAverageIterations, getVolt, setVolt);
      }
      return null;
    }
    CorridorStatistics ShowVoltsByTLHR(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      if(IsRatesLengthStable) {
        var tl3 = TrendLinesTrendsAll;
        if(!tl3.Any(tl => tl.IsEmpty)) {
          Func<TL, DateTime[]> dateRange = tl => {
            var slack = (tl.TimeSpan.TotalMinutes * .1).FromMinutes();
            return new[] { tl.StartDate.Add(slack), tl.EndDate.Subtract(slack) };
          };
          var dateOverlapOk = !tl3.Permutation().Any(t => dateRange(t.Item1).DoSetsOverlap(dateRange(t.Item2)));
          if(dateOverlapOk) {
            var v = tl3.SelectMany(tl => tl.PriceHeight).ToArray().Permutation().Average(t => t.Item1.ToPercent(t.Item2)).ToInt();
            ShowVolts(v, getVolt, setVolt);
          }
        }
      }
      return null;
    }
    CorridorStatistics ShowVoltsByTLA(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var useCalc = IsRatesLengthStable;
      if(useCalc)
        TLsAngle().ForEach(v => ShowVolts(v, 2, getVolt, setVolt));
      return null;
    }
    CorridorStatistics ShowVoltsByTLAR(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var useCalc = IsRatesLengthStable;
      if(useCalc)
        TLsAngleRatio().ForEach(v => ShowVolts(v, 2, getVolt, setVolt));
      return null;
    }
    CorridorStatistics ShowVoltsByBSTip(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var useCalc = IsRatesLengthStable;
      if(useCalc)
        BSTipOverlap(this).ForEach(v => ShowVolts(v.Item1, 2, getVolt, setVolt));
      return null;
    }

    public IEnumerable<int> TMCorrelation(int hedgeIndex) =>
      TradingMacroTrader(tm => tm.HedgeCorrelation).Where(i => i != 0)
      .IfEmpty(() => GetHedgeCorrelation(Pair, PairHedge))
      .IfEmpty(() => TradingMacroHedged(tmOther => TMCorrelationImpl((this, tmOther)), hedgeIndex).Concat().Take(1));

    public IEnumerable<int> TMCorrelation(TradingMacro tmOther)
        => TradingMacroTrader(tm => tm.HedgeCorrelation).Where(i => i != 0)
        .IfEmpty(() => GetHedgeCorrelation(Pair, tmOther.Pair))
        .IfEmpty(() => TMCorrelationImpl((this, tmOther))).Take(1);
    Func<(TradingMacro tmThis, TradingMacro tmOther), int[]> TMCorrelationImpl =
      new Func<(TradingMacro tmThis, TradingMacro tmOther), int[]>(t
         => {
           return (
           from tm1 in GetTMt(t.tmThis)
           from tm2 in GetTMt(t.tmOther)
           from corr in Hedging.TMCorrelation(tm1, tm2)
             //from tm1M1 in GetTM1(t.tmThis)
             //from tm2M1 in GetTM1(t.tmOther)
             //from corrM1 in t.corrs.Concat(Hedging.TMCorrelation(tm1M1, tm2M1)).Take(1)
             //where corr != 0 && corr == corrM1
           where corr != 0
           select corr
        )
        .ToArray();
           IEnumerable<TradingMacro> GetTMt(TradingMacro tm) => tm.BarPeriod == BarsPeriodType.t1 ? new[] { tm } : tm.TradingMacroTrader();
           IEnumerable<TradingMacro> GetTM1(TradingMacro tm) => tm.BarPeriod == BarsPeriodType.t1 ? tm.TradingMacroM1() : new[] { tm };
         }).MemoizeLast(t => t, i => i.Any());
    CorridorStatistics ShowVoltsByCorrelation() {
      if(UseCalc()) {
        (from voltRates in UseRates(ra => ra.Where(r => !GetVoltage2(r).IsNaNOrZero()).ToList().SideEffect(l => l.Reverse()))
         where voltRates.Count > BarsCountCalc * 0.9
         let bc = RatesArray.Count / 5
         from v in voltRates
           .Buffer(bc, 1)
           .Where(b => b.Count == bc)
           .TakeWhile(b => GetVoltage(b[0]).IsNaN())
           .Select(b => alglib.pearsoncorr2(b.ToArray(t => GetVoltage2(t)), b.ToArray(_priceAvg)).SideEffect(v => SetVoltage(b[0], v)))
           .Reverse()
           .Take(1)
         select v
         ).ForEach(v => ShowVolts(v, GetVoltage, SetVoltage));

      }
      return null;
    }
    CorridorStatistics ShowVoltsByGross() {
      ShowVolts(TradesManager.GetTrades().Net(), 0, GetVoltage2, SetVoltage2);
      return null;
    }
    CorridorStatistics ShowVoltsByStraddleSpread() {
      if(UseCalc())
        CurrentPut.ForEach(put => (
        from vl in GetVoltageAverage()
        from vh in GetVoltageHigh()
        where !vl.IsNaN() && !vh.IsNaN()
        select (vl.Percentage(vh) - put.marketPrice.ask.Percentage(put.marketPrice.bid)).ToPercent()
        ).ForEach(v => SetVolts(v, 1)));
      return null;
    }

    private IEnumerable<double[]> CurrentStraddleBidMinMax() => UseStraddleHistory(sh => sh.GetRange(RatesArray.Count.Min(sh.Count)).MinMax(t => t.bid));

    CorridorStatistics ShowVoltsByStraddle(Func<IBApp.MarketPrice, double> value, int voltIndex) {
      if(UseCalc()) {
        SetVoltsByStraddle()
          .TakeLast(1)
          .ForEach(s => SetVolts(s, voltIndex));
      }
      return null;
      IEnumerable<double> SetVoltsByStraddle() {
        (from r in RatesArray.Take(0)
         where GetVoltage(r).IsNaN()
         select r.StartDate)
        .ForEach(mergeDate =>
          UseStraddleHistory(straddleHistory => {
            var mergeShs = straddleHistory.SkipWhile(sh => sh.time < mergeDate).ToArray();
            RatesArray.SkipLast(1).Zip(r => r.StartDate, mergeShs, sh => sh.time, (r, sh) => (r, sh))
            .ForEach(t => SetVoltage(t.r, t.sh.bid));
          }));
        var volts = RatesArray.BackwardsIterator().TakeWhile(r => GetVoltByIndex(voltIndex)(r).IsNaN()).ToList();
        volts.Reverse();
        var startDate = volts.Select(r => r.StartDate).Take(1);
        return UseStraddleHistory(straddleHistory => {
          var straddles = (from sd in startDate
                           select straddleHistory.BackwardsIterator().TakeWhile(sh => sh.time >= sd)
                           ).Concat().ToList();
          straddles.Reverse();
          var shs = volts.Zip(r => r.StartDate.ToUniversalTime(), straddles, sh => sh.time, (r, sh) => (r, sh));
          return shs.SkipWhile(t => !GetVoltByIndex(voltIndex)(t.r).IsNaN())
            .Select(t => value(t.sh).SideEffect(v => SetVoltByIndex(voltIndex)(t.r, v)));
        }).Concat();
      }
    }


    CorridorStatistics ShowVoltsByHVR(int voltIndex) {
      var ok = UseCalc();
      var vhss = UseRates(GetHVs);
      var vhss2 = TradingMacroOther(Pair).Where(tm => tm.IsActive && tm.BarPeriod == BarPeriod).Select(tm => tm.UseRates(GetHVs2)).Concat().ToArray();
      (from vhs in vhss
       from vhs2 in vhss2
       from z in vhs.Zip(vhs2, (a, b) => (a, b))
       select z
       ).AsParallel().ForAll(t2 => SetVoltByIndex(voltIndex)(t2.a.r, t2.b / t2.a.hv));
      //vs2.Zip(vs2).ForEach(t => SetVolts(t.Item2 / t.Item1, voltIndex));
      return null;
      (Rate r, double hv)[] GetHVs(List<Rate> ra) => ra.Select(r => (r, hv: GetHV(r))).ToArray();
      double[] GetHVs2(List<Rate> ra) => ra.Select(GetHV).ToArray();
    }

    object _voltLocker = new object();
    CorridorStatistics ShowVoltsByHV(int voltIndex) {
      lock(_voltLocker) {
        if(UseCalc()) {
          var c = RatesArray.Count - 1;
          if(GetVoltByIndex(voltIndex)(RatesInternal[c]).IsNaN())
            UseRatesInternal(ri => ri.Buffer(c, 1).TakeWhile(b => b.Count == c).ForEach(b => {
              //var prices = b.Select(_priceAvg).ToList();
              SetMA(b);
              var hv = b.HistoricalVolatilityByPoint(r => r.PriceCMALast);
              //var stdr = prices.StandardDeviation();//.ToArray();//.StDevByRegressoin();
              SetVoltByIndex(voltIndex)(b.Last(), hv * 10000);
            }));
          var hvps = UseRates(ra => ra.HistoricalVolatilityByPoint(r => r.PriceCMALast));
          hvps.ForEach(hvp => SetVolts(hvp * 10000, voltIndex));
        }
        return null;
      }
    }

    private static double[] HVByBarPeriod(TradingMacro tm) => tm.UseRates(ra => tm.HVByBarPeriod(ra));

    public const int CURRENT_HEDGE_COUNT = 23 * 60 * 2;
    private double HVByBarPeriod(IList<Rate> b) => BarPeriodInt > 0 ? HistoricalVolatilityByPoints(b, CURRENT_HEDGE_COUNT / BarPeriodInt, DateTime.MaxValue, false) : HVByPoints(b);

    CorridorStatistics ShowVoltsBySlope() {
      if(UseCalc()) {
        var v = GetLastVolts(GetVoltage2).ToArray().With(vs => vs.Length > 0 ? vs.LinearSlope() : double.NaN);
        SetVolts(-v * 10000, 0);
      }
      return null;
    }
    CorridorStatistics ShowVoltsByStdRatio(int voltIndex) {
      if(UseCalc()) {
        var c = RatesArray.Count - 1;
        if(GetVoltByIndex(voltIndex)(RatesInternal[c]).IsNaN())
          UseRatesInternal(ri => ri.Buffer(c, 1).TakeWhile(b => b.Count == c).ForEach(b => {
            var std = b.StandardDeviation(_priceAvg);
            var stdr = b.Select(_priceAvg).ToArray().StDevByRegressoin();
            SetVoltByIndex(voltIndex)(b.Last(), std / stdr - 1);
          }));
        SetVolts(StDevByPriceAvg / StDevByHeight - 1, voltIndex);
      }
      return null;
    }
    CorridorStatistics ShowVoltsByStdRatioLime(int voltIndex) {
      if(UseCalc() && !TLLime.IsEmpty) {
        var a = TLPlum.PriceHeight.Select(h => TLLime.StDev / h);
        var b = TLLime.StDev / TLBlue.StDev;
        SetVolts(a.Concat(b.Yield()).Max(), voltIndex);
      }
      return null;
    }
    CorridorStatistics ShowVoltsByStdOverPrice(int voltIndex) {
      if(UseCalc()) {
        var c = RatesArray.Count - 1;
        if(GetVoltByIndex(voltIndex)(RatesInternal[c]).IsNaN())
          UseRatesInternal(ri => ri.Buffer(c, 1).TakeWhile(b => b.Count == c).ForEach(b => {
            var stdr = b.Select(_priceAvg).StandardDeviation();//.ToArray();//.StDevByRegressoin();
            SetVoltByIndex(voltIndex)(b.Last(), StdOverCurrPriceRatio(stdr, b.Last().PriceAvg));
          }));
        SetVolts(StdOverCurrPriceRatio(), voltIndex);
      }
      return null;
    }

    static double[] EMPTY_DOUBLE = new double[0];
    public IEnumerable<T> VoltageHigh<T>(Func<double, T> func) => GetVoltageHigh().Select(func);
    public Func<IList<double>> GetVoltageHigh = () => EMPTY_DOUBLE;
    public IEnumerable<T> VoltageAverage<T>(Func<double, T> func) => GetVoltageAverage().Select(func);
    public Func<IList<double>> GetVoltageAverage = () => EMPTY_DOUBLE;
    public Func<IList<double>> GetVoltageLow = () => EMPTY_DOUBLE;
    public Func<IList<double>> GetVoltage2High = () => EMPTY_DOUBLE;
    public Func<IList<double>> GetVoltage2Low = () => EMPTY_DOUBLE;

    public Func<Rate, double> GetVoltage = r => r.Voltage;
    public Func<Rate, double> GetVoltage01 = r => r.Voltage01;
    public Action<Rate, double> SetVoltage = (r, v) => r.Voltage = v;
    public Action<Rate, double> SetVoltage01 = (r, v) => r.Voltage01 = v;

    public Func<Rate, double> GetVoltage2 = r => r.Voltage2;
    public Func<Rate, double> GetVoltage21 = r => r.Voltage21;
    public Action<Rate, double> SetVoltage2 = (r, v) => r.Voltage2 = v;
    public Action<Rate, double> SetVoltage21 = (r, v) => r.Voltage21 = v;

    public Action<Rate, double> SetHV = (r, v) => r.CrossesDensity = v;
    public Func<Rate, double> GetHV = (r) => r.CrossesDensity;

    int VoltAverageIterationsByIndex(int index) => index == 0 ? VoltAverageIterations : VoltAverageIterations2;
    IEnumerable<double> GetLastVoltByIndex(int voltIndex) => voltIndex == 0 ? GetLastVolt() : GetLastVolt2();
    IEnumerable<double> GetLastVoltVoltByIndex(int voltIndex) => voltIndex == 0 ? GetLastVoltVolt() : GetLastVoltVolt2();
    Func<Rate, double> GetVoltVoltByIndex(int voltIndex) => GetVoltByIndex(voltIndex + 10);
    Func<Rate, double> GetVoltByIndex(int voltIndex) {
      switch(voltIndex) {
        case 0: return GetVoltage;
        case 10: return GetVoltage01;
        case 1: return GetVoltage2;
        case 11: return GetVoltage21;
        default: throw new Exception(new { NotSupprted = new { voltIndex } } + "");
      }
    }
    Action<Rate, double> SetVoltVoltByIndex(int voltIndex) => SetVoltByIndex(voltIndex, 10);
    Action<Rate, double> SetVoltByIndex(int voltIndex) => SetVoltByIndex(voltIndex, 0);
    Action<Rate, double> SetVoltByIndex(int voltIndex, int offset) {
      switch(voltIndex + offset) {
        case 0: return SetVoltage;
        case 10: return SetVoltage01;
        case 1: return SetVoltage2;
        case 11: return SetVoltage21;
        default: throw new Exception(new { NotSupprted = new { voltIndex, offset } } + "");
      }
    }
    Action<double> SetVoltHighByIndex(int voltIndex) => voltIndex == 0 ? new Action<double>(v => GetVoltageHigh = () => new[] { v }) : v => GetVoltage2High = () => new[] { v };
    Action<double> SetVoltLowByIndex(int voltIndex) => voltIndex == 0 ? new Action<double>(v => GetVoltageAverage = () => new[] { v }) : v => GetVoltage2Low = () => new[] { v };
    IEnumerable<double> GetVoltHighByIndex(int voltIndex) => voltIndex == 0 ? GetVoltageHigh() : GetVoltage2High();
    IEnumerable<double> GetVoltLowByIndex(int voltIndex) => voltIndex == 0 ? GetVoltageAverage() : GetVoltage2Low();

    Action<Rate, double> SetHedgePriceByIndex(int hedgeIndex) => hedgeIndex == 0 ? (r, h) => r.PriceHedge = h : new Action<Rate, double>((r, h) => r.PriceHedge2 = h);
    Func<Rate, double> GetHedgePriceByIndex(int hedgeIndex) => hedgeIndex == 0 ? r => r.PriceHedge : new Func<Rate, double>(r => r.PriceHedge2);

    double GetVoltCmaPeriodByIndex(int voltIndex) => voltIndex == 0 ? VoltCmaPeriod : VoltCmaPeriod2;
    int GetVoltCmaPassesByIndex(int voltIndex) => voltIndex == 0 ? VoltCmaPasses : VoltCmaPasses2;
    int GetVoltCmaWaveIterationsByIndex(int voltIndex) => voltIndex == 0 ? VoltCmaWaveIterations : VoltCmaWaveIterations2;

    CorridorStatistics ShowVoltsByStDevDiff(int voltIndex) {
      var v = TLLimeHeightRatioByAvg();
      if(!v.IsNaN()) SetVolts(voltIndex)(v.ToPercent());
      return null;
      IEnumerable<T> TrendsNotEmpty<T>(Func<TL, T> map, params TL[] trends) => trends.Where(tl => !tl.IsEmpty).Select(map);
      IEnumerable<double> Trends() => TrendsNotEmpty(tl => tl.StDev, TLBlue, TLPlum, TLRed, TLGreen).DefaultIfEmpty(double.NaN);
      double TLLimeHeightRatioByAvg() => TLLime.StDev / Trends().Average() - 1;
    }
    CorridorStatistics ShowVoltsByRatioDiff(int voltIndex, int hedgeIndex) {
      double GetPrice(Rate r) => r.PriceAvg;
      if(UseCalc()) {
        //var voltRates = UseRates(ra => ra.Where(r => !GetVoltage(r).IsNaNOrZero()).ToArray()).Concat().ToArray();
        var voltRates = ShowVoltsByRatioDiff_New(GetPrice, hedgeIndex)
          .ToArray();
        if(voltRates.IsEmpty()) return null;
        VoltsFullScaleMinMax = voltRates[0].With(vr => GetFullScaleMinMax(vr.r, vr.h, GetPrice)).ToArray();
        var voltMap = voltRates[0].With(vr => RatioMapDouble((vr.h, VoltsFullScaleMinMax)));
        var priceMap = voltRates[0].With(vr => RatioMap((vr.r, GetPrice, null)));

        double min = double.MaxValue, max = double.MinValue, priceMin = double.MaxValue, priceMax = double.MinValue;
        var period = GetVoltCmaPeriodByIndex(voltIndex);
        var passes = GetVoltCmaPassesByIndex(voltIndex);
        var rateVolts = priceMap
          .Zip(voltMap, (b, a) => {
            var v = b.t.v - a.v;
            return (b.t.r, v);
          })
          .Cma(t => t.v, period, passes, (t, v) => new { t.r, v })
          .ToArray();
        var volts = new double[0 * rateVolts.Length];
        var voltCounter = 0;
        //rateVolts = rateVolts.Cma(t => t.v, cmaPeriod, (t, cma) => (t.r, cma)).ToArray();
        rateVolts.Cma(t => t.v, period * passes, 0, (t, cma2) => {
          var v = t.v;
          priceMin = GetPrice(t.r).Min(priceMin);
          priceMax = GetPrice(t.r).Max(priceMax);
          SetVoltByIndex(voltIndex)(t.r, v);
          SetVoltVoltByIndex(voltIndex)(t.r, cma2);
          if(volts.Any()) {
            min = v.Min(min);
            max = v.Max(max);
            volts[voltCounter++] = v;
          }
        });
        if(volts.Any()) { // store this logic for other times
          min = volts.AverageByIterations(-VoltAverageIterationsByIndex(voltIndex)).DefaultIfEmpty(double.NaN).Average();
          max = volts.AverageByIterations(VoltAverageIterationsByIndex(voltIndex)).DefaultIfEmpty(double.NaN).Average();
        }
        min = new[] { min, max }.OrderBy(m => m.Abs()).Last();

        //SetVoltHighByIndex(voltIndex)(min.Abs());
        //SetVoltLowByIndex(voltIndex)(-min.Abs());
        //voltRates.Select(vr => vr.r).ForEach(ra => ra.Zip(voltMap, (r, h) => r.PriceHedge = PosByRatio(priceMin, priceMax, h.v)).Count());
        var volts2 = voltRates[0].r.Zip(voltMap, (r, h) => new { r, v = PosByRatio(priceMin, priceMax, h.v) })
          //.ToList()
          //.Cma(t => t.v, RatesHeightMin, (r, v) => (r.r, v))
          ;
        var shp = SetHedgePriceByIndex(hedgeIndex);
        UseRates(ra => ra.Zip(r => r.StartDate, volts2, v => v.r.StartDate, (r, v) => new { r, v.v }).ToList())
          .ForEach(v3 => v3.ForEach(t => shp(t.r, t.v)));

        OnSetVoltsHighLowsByRegression(voltIndex);

        OnCalcHedgeRatioByPositions();
        OnSetExitGrossByHedgeGrossess();
      }
      return null;

      double PosByRatio(double min, double max, double pos) {
        return min + (max - min) * pos;
      }
    }

    IEnumerable<(Rate[] r, double[] h)> ShowVoltsByRatioDiff_New(Func<Rate, double> map, int hedgeIndex) =>
      from xs in ZipHedgedRates(map, hedgeIndex)
      select (xs.Select(t => t.rate).ToArray(), xs.Select(t => t.ratio).ToArray());

    IEnumerable<IList<(Rate rate, double ratio)>> ZipHedgedRates(Func<Rate, double> map, int hedgeIndex) =>
      from tm2 in TradingMacroHedged(hedgeIndex)
      from corr in TMCorrelation(tm2)
      let m = corr == 1 ? map : new Func<Rate, double>((r) => 1 / map(r))
      from xs in UseRates(tm2, (ra, ra2) => ZipRateArrays((ra, ra2), m))
      select xs;

    Func<(List<Rate> ra, List<Rate> ra2), Func<Rate, double>, IList<(Rate rate, double ratio)>> ZipRateArrays = ZipRateArraysImpl2;//.MemoizeLast(t => t.ra.LastOrDefault().StartDate);
    private static Func<(List<Rate> ra, List<Rate> ra2, int corr), IList<(Rate rate, double)>> ZipRateArraysImpl =
      t => t.ra.Zip(
        r => r.StartDate
        , t.ra2.Select(r => Tuple.Create(r.StartDate, t.corr == 1 ? r.PriceAvg : 1 / r.PriceAvg))
        , r => r.Item1
        , (r, r2) => (r, r2.Item2)
        ).ToArray();
    private static Func<(List<Rate> ra, List<Rate> ra2), Func<Rate, double>, IList<(Rate rate, double)>> ZipRateArraysImpl2 =
      (t, m) => (from r in t.ra
                 join r2 in t.ra2 on r.StartDate equals r2.StartDate
                 select (r, m(r2))
               ).AsParallel().AsOrdered().ToList();


    static Func<(IList<Rate> source, Func<Rate, double> getter, double[] minMax), IEnumerable<(DateTime d, (double v, Rate r) t)>> RatioMapImpl => t => {
      var minMax = (t.minMax ?? new double[0]).Concat(t.source.MinMax(t.getter)).MinMax();
      return t.source.Select(r => (r.StartDate, (t.getter(r).PositionRatio(minMax), r)));
    };
    static Func<(IList<Rate> source, Func<Rate, double> getter, double[] minMax), IEnumerable<(DateTime d, (double v, Rate r) t)>> RatioMap
      = RatioMapImpl;//.MemoizeLast(t => t.source.BackwardsIterator().Take(1).Select(r=>(r.StartDate,t.getter(r))).FirstOrDefault());

    static Func<(IList<double> source, double[] minMax), IEnumerable<(double position, double value)>> RatioMapDoubleImpl = p => {
      var minMax = (p.minMax ?? new double[0]).Concat(p.source.MinMax()).MinMax();
      return p.source.Select(t => (t.PositionRatio(minMax), t));
    };
    Func<(IList<double> source, double[] minMax), IEnumerable<(double v, double r)>> RatioMapDouble
      = RatioMapDoubleImpl;//.MemoizeLast(t => (t.source.FirstOrDefault(), t.source.LastOrDefault()));

    private static IEnumerable<(double pos, double std)> CalcFullScaleShiftByStDev(IList<double> map, IList<double> mapH) {
      return Enumerable.Range(-99, 99 * 2).Select(i => i / 100.0).Select(pos => (pos, std: GetStDev(pos))).OrderBy(x => x.std);
      double GetStDev(double ofs) => map.Zip(mapH, (t1, t2) => t1 - (t2 + ofs)).Sum().Abs();
    }

    private static double[] GetFullScaleMinMax(IList<Rate> rates, IList<double> hedge, Func<Rate, double> map) {
      var linearPrice = rates.Linear(map).RegressionValue(rates.Count / 2);
      var priceMinMax = rates.MinMax(map);
      if(priceMinMax.Length < 2) return new double[0];
      var pricePos = linearPrice.PositionRatio(priceMinMax);

      var linearVolt = hedge.Linear().RegressionValue(hedge.Count / 2);
      var v = hedge.MinMax().Yield(mm => new { min = mm[0], max = mm[1] }).Single();
      var voltPos = linearVolt.PositionRatio(v.min, v.max);
      if(voltPos.IsNaN()) {// test stdev approach
        var rMap = RatioMapDoubleImpl((rates.ToArray(map), null)).ToArray(t => t.position);
        var hMap = RatioMapDoubleImpl((hedge, null)).ToArray(t => t.position);
        var fullScaleBySrDev = CalcFullScaleShiftByStDev(rMap, hMap).ToArray();
        var shiftByStDev = fullScaleBySrDev.First().pos;
        var shiftByLinear = linearPrice - linearVolt;
        var isOk = shiftByLinear.Ratio(shiftByStDev).Abs() > 0;
      }
      return new[] { voltMinNew(), voltMaxNew() };

      double voltMaxNew() => (linearVolt - v.min) / pricePos + v.min;
      double voltMinNew() => v.max - (v.max - linearVolt) / (1 - pricePos);
    }

    public static double MaxLevelByMinMiddlePos(double min, double middle, double pos) => (middle - min) / pos + min;
    public static double MinLevelByMaxMiddlePos(double max, double middle, double pos) => max - (max - middle) / (1 - pos);

    IEnumerable<BarBaseDate> GetContinious<TRate>(IEnumerable<TRate> source, BarsPeriodType periodType) where TRate : Rate {
      var e = source.GetEnumerator();
      if(!e.MoveNext()) yield break;

      var ts = TimeSpan.FromMinutes(((double)periodType).Max(1 / 60.0));
      var current = e.Current;
      yield return current;

      while(e.MoveNext()) {
        while(current.StartDate.Add(ts) < e.Current.StartDate) {
          current = current.Clone() as TRate;
          current.StartDate2 = current.StartDate2 + ts;
          yield return current;
        }
        yield return current = e.Current;
      }
    }


    static int LastTrendLineDurationPercentage2(IList<TL> tls, int skip) =>
      tls.TakeLast(skip).Select(tl => tl.TimeSpan.TotalMinutes).RootMeanPower().With(tlLast => {
        var tla = tls.SkipLast(1).Select(tl => tl.TimeSpan.TotalMinutes).SquareMeanRoot();
        return tlLast.Div(tla).ToPercent();
      });

    private Singleable<double> TLsAngle() {
      return new[] { TrendLinesTrendsAll }
        .Where(tls => tls.Any())
        .Where(tls => tls.All(TL.NotEmpty))
        .Select(a => a.AverageWeighted(tl => tl.Angle, tl => tl.Distance))
        .AsSingleable();
    }
    private Singleable<double> TLsAngleRatio() {
      return new[] { TrendLinesTrendsAll.Where(TL.NotEmpty) }
        .Where(tls => tls.Any())
        .Select(tls => tls.Select(tl => tl.Angle).ToList())
        .Select(a => a.Permutation((a1, a2) => (a1 - a2).Abs()).Average())
        .AsSingleable();
    }

    private IEnumerable<double> CalcSma(int periodFast, int periodSlow, Func<Rate, double> select, Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      return UseRatesInternal(rates => {
        var rates2 = rates.BackwardsIterator();

        var cmaFast = rates2.Buffer(periodFast, 1)
        .TakeWhile(b => getVolt(b[0]).IsNaN())
        .Select(b => b.Average(select))
        .ToArray();

        var cmaSlow = rates2.Buffer(periodSlow, 1)
        .TakeWhile(b => getVolt(b[0]).IsNaN())
        .Select(b => b.Average(select))
        .ToArray();

        return rates2.Zip(cmaFast.Zip(cmaSlow, (f, s) => f - s), (r, ao) => new { r, ao })
       .Do(x => setVolt(x.r, x.ao))
       .ToArray()
       .Take(1);
      })
        .SelectMany(v => v.Select(x => x.ao));
    }

    static double CalcVolatility(IList<Rate> rates, Func<Rate, double> getValue, Func<Rate, double> line) {
      return CalcVolatility(rates.ToArray(getValue), rates.ToArray(line));
    }
    static double CalcVolatility(IList<double> values, IList<double> line = null) {
      line = line ?? values.Regression(1, (c, l) => l);
      var threes = line.Buffer(3, 1).Where(b => b.Count == 3).ToArray();
      return values.Skip(1).SkipLast(1)
        .Zip(line.Skip(1).SkipLast(1), (z1, z2) => z1 - z2)
        .Zip(threes, (abs, b3) => (abs * MathExtensions.Offset(b3.Regress(1).LineSlope(), 0.01)).Abs())
        .ToArray()
        .AverageByIterations(-1)
        .StDev();
    }

    #region SetCentersOfMass Subject
    ActionAsyncBuffer _SetCentersOfMassAsyncBuffer;
    ActionAsyncBuffer SetCentersOfMassAsyncBuffer => _SetCentersOfMassAsyncBuffer ?? (_SetCentersOfMassAsyncBuffer = new ActionAsyncBuffer());
    object _SetCentersOfMassSubjectLocker = new object();
    ISubject<Action> _SetCentersOfMassSubject;
    ISubject<Action> SetCentersOfMassSubject {
      get {
        lock(_SetCentersOfMassSubjectLocker)
          if(_SetCentersOfMassSubject == null) {
            _SetCentersOfMassSubject = new Subject<Action>();
            _SetCentersOfMassSubject
              .DistinctUntilChanged(_ => TryServerTime().FirstOrDefault().Round(MathCore.RoundTo.Minute))
              .SubscribeToLatestOnBGThread(exc => Log = exc, ThreadPriority.Lowest);
            //.Latest().ToObservable(new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }))
            //.Subscribe(s => s(), exc => Log = exc);
          }
        return _SetCentersOfMassSubject;
      }
    }
    #endregion
    #region SetBarsCountCalc Subject
    object _SetBarsCountCalcSubjectLocker = new object();
    ISubject<Action> _SetBarsCountCalcSubject;
    ISubject<Action> SetBarsCountCalcSubject {
      get {
        lock(_SetBarsCountCalcSubjectLocker)
          if(_SetBarsCountCalcSubject == null) {
            IObservable<Action> o = null;
            _SetBarsCountCalcSubject = _SetBarsCountCalcSubject.InitBufferedObservable(ref o, exc => Log = exc);
            o
              //.ObserveOn(new EventLoopScheduler())
              .Subscribe(a => {
                a();
                RatesLengthLatch = false;
              });
          }
        return _SetBarsCountCalcSubject;
      }
    }
    void OnSetBarsCountCalc(Action p) {
      SetBarsCountCalcSubject.OnNext(p);
    }
    void OnSetBarsCountCalc(bool runSync = true) {
      if(runSync)
        GetRatesLengthFunction()();
      else
        OnSetBarsCountCalc(GetRatesLengthFunction());
    }

    Action GetRatesLengthFunction() {
      switch(RatesLengthBy) {
        case RatesLengthFunction.None:
          return () => { BarsCountCalc = IsAsleep ? 60 : BarsCount; };
        case RatesLengthFunction.DistanceMin:
          return ScanRatesLengthByDistanceMin;
        case RatesLengthFunction.DistanceMinSmth:
          return ScanRatesLengthByDistanceMinSmoothed;
        case RatesLengthFunction.DistanceMin0:
          return ScanRatesLengthByDistanceMin0;
        case RatesLengthFunction.TimeFrame:
          return ScanRatesLengthByTimeFrame;
        case RatesLengthFunction.StDevMin:
          return ScanRatesLengthByStDevReg;
        case RatesLengthFunction.MinHeight:
          return ScanRatesLengthByMinHeight;
        case RatesLengthFunction.MinBBSD:
          return ScanRatesLengthByBBSD;
        case RatesLengthFunction.M1Wave:
          return () => ScanRatesLengthByM1Wave(tm => tm.WaveRangeAvg);
        case RatesLengthFunction.M1WaveS:
          return () => ScanRatesLengthByM1Wave(tm => tm.WaveRangeSum);
        case RatesLengthFunction.M1WaveAvg:
          return () => ScanRatesLengthByM1WaveAvg(false, tm => new[] { tm.WaveRangeAvg });
        case RatesLengthFunction.M1WaveAvg2:
          return () => ScanRatesLengthByM1WaveAvg(true, tm => new[] { tm.WaveRangeAvg });
        case RatesLengthFunction.M1WaveAvg3:
          return () => ScanRatesLengthByM1WaveAvg(true, tm => new[] { tm.WaveRangeAvg, tm.WaveRangeSum });
        default:
          throw new NotImplementedException(new { RatesLengthFunction = RatesLengthBy, Error = "Not implemented" } + "");
      }
    }
    #endregion

    double _stDevUniformRatio = Math.Sqrt(12);
    void OnRatesArrayChaged_SetVoltsByRsd(double volt) {
      RatesArray.TakeWhile(r => GetVoltage(r).IsNaN()).ForEach(r => SetVoltage(r, volt));
      RatesArray.Reverse<Rate>().TakeWhile(r => GetVoltage(r).IsNaN()).ForEach(r => SetVoltage(r, volt));
    }
    Action OnRatesArrayChaged = () => { };
    IEnumerable<T> GetSenterOfMassStrip<T>(IList<double> rates, double height, int roundOffset, Func<double[], double, double, T> map) {
      var rates2 = rates.SafeArray();
      rates.CopyTo(rates2, 0);
      return rates2.BufferVertical2(r => RoundPrice(r, roundOffset), height, (b, t, c) => new { b, t, c })
        .OrderByDescending(a => a.c)
        .Take(1)
        .Select(a => map(rates2, a.t, a.b));
    }
    [WwwSetting]
    public double CoMStartHour { get; set; } = 5;
    [WwwSetting]
    public double CoMEndHour { get; set; } = 9.5;

    public List<(double[] upDown, DateTime[] dates, double r, bool isAH)> CurrentSpecialHours() {
      var bh = BeforeHours.SkipWhile(h => h.dates.Last().TimeOfDay < _beforeHourTime).Select(h => (h.upDown, h.dates, r: 1.0, isAH: false)).Take(1);
      var ah = AfterHours.SkipWhile(h => h.dates.Last().TimeOfDay < _afterHourTime).Select(h => (h.upDown, h.dates, r: 0.75, true)).Take(1);
      return bh.Concat(ah).Where(h => h.dates.Last() <= ServerTime).OrderByDescending(h => h.dates.Last()).Take(1).ToList();
    }
    public (double[] upDown, DateTime[] dates)[] BeforeHours = new (double[], DateTime[])[0];
    public (double[] upDown, DateTime[] dates)[] AfterHours = new (double[], DateTime[])[0];
    Action<DateTime> _SetBeforeHoursMemoize;
    private Action<DateTime> SetBeforeHours => _SetBeforeHoursMemoize ?? (_SetBeforeHoursMemoize = new Action<DateTime>(d => SetBeforeHoursImpl()).MemoizeLast());
    private void SetBeforeHoursImpl() {
      var startHour = CoMStartHour;
      var endHour = CoMEndHour;
      if(!TryServerTime(out var serverTime)) return;
      var timeRange = new[] { serverTime.Date.AddHours(startHour), serverTime.Date.AddHours(endHour) };
      var afterHours = new List<(double[] upDown, DateTime[] dates)>();
      while((timeRange = SetCenterOfMassByM1Hours(timeRange, t => {
        afterHours.Add(t);
        return t.dates;
      })).Any()) { };
      BeforeHours = afterHours.ToArray();
      SetAfterHours(15, 17, ah => AfterHours = ah);
    }
    private void SetAfterHours(double startHour, double endHour, Action<(double[] upDown, DateTime[] dates)[]> set) {
      if(!TryServerTime(out var serverTime)) return;
      var timeRange = new[] { serverTime.Date.AddHours(startHour), serverTime.Date.AddHours(endHour) };
      var afterHours = new List<(double[] upDown, DateTime[] dates)>();
      while((timeRange = SetCenterOfMassByM1Hours(timeRange, t => {
        afterHours.Add(t);
        return t.dates;
      })).Any()) { };
      set(afterHours.ToArray());
    }
    Func<DateTime, object> _SetCentersOfMassMemoize;
    private Func<DateTime, object> SetCentersOfMass => _SetCentersOfMassMemoize ?? (_SetCentersOfMassMemoize = new Func<DateTime, object>(d => { SetCentersOfMassImpl(); return null; }).MemoizeLast(d => d));
    private void SetCentersOfMassImpl() {
      var startHour = CoMStartHour;
      var endHour = CoMEndHour;
      if(!TryServerTime(out var serverTime)) return;
      var stripHoursGreen = new[] { serverTime.Date.AddHours(startHour), serverTime.Date.AddHours(endHour) };
      var stripHours = SetCenterOfMassByM1Hours(stripHoursGreen, t => {
        CenterOfMassBuy = t.upDown[1];
        CenterOfMassSell = t.upDown[0];
        return CenterOfMassDates = t.dates;
      });
      return;
      stripHours = SetCenterOfMassByM1Hours(stripHours, t => {
        CenterOfMassBuy2 = t.upDown[1];
        CenterOfMassSell2 = t.upDown[0];
        return CenterOfMass2Dates = t.dates;
      });

      stripHours = SetCenterOfMassByM1Hours(stripHours, (t) => {
        CenterOfMassBuy3 = t.upDown[1];
        CenterOfMassSell3 = t.upDown[0];
        return CenterOfMass3Dates = t.dates;
      });
      SetCenterOfMassByM1Hours(stripHours, (t) => {
        CenterOfMassBuy4 = t.upDown[1];
        CenterOfMassSell4 = t.upDown[0];
        return CenterOfMass4Dates = t.dates;
      });
    }

    private DateTime[] SetCenterOfMassByM1Hours(DateTime[] stripHours, Func<(double[] upDown, DateTime[] dates), DateTime[]> setCenterOfMass) {
      return stripHours.IsEmpty() ? new DateTime[0]
        : TradingMacroM1(tm =>
       from shs in Enumerable.Range(0, 5).Select(i => addDay(stripHours, -i))
       from mm in tm.UseRatesInternal(ri =>
       ri.BackwardsIterator()
       .SkipWhile(r => r.StartDate > shs[1])
       .TakeWhile(r => r.StartDate > shs[0])
       .MinMax(r => r.PriceAvg, r => r.PriceAvg)
       )
       where mm.All(Lib.IsNotNaN)
       select addDay(setCenterOfMass((mm, shs)), -1)
       )
      .Concat()
      .Take(1)
      .Concat()
      .ToArray()
      ;
      DateTime[] addDay(DateTime[] shrs, int i) {
        return shrs.Select(sh => sh.AddDays(i)).ToArray();
      }
    }


    int _integrationPeriod { get { return CorridorHeightMax.ToInt(); } }

    int _AoPeriodFast = 5;
    int _AoPeriodSlow = 35;
    int _AoPeriodFast2 = 15;
    int _AoPeriodSlow2 = 55;

    [WwwSetting(wwwSettingsAO)]
    public int AoPeriodFast {
      get {
        return _AoPeriodFast;
      }

      set {
        if(_AoPeriodFast == value)
          return;
        _AoPeriodFast = value;
        ResetVoltages();

      }
    }

    [WwwSetting(wwwSettingsAO)]
    public int AoPeriodSlow {
      get {
        return _AoPeriodSlow;
      }

      set {
        if(_AoPeriodSlow == value)
          return;
        _AoPeriodSlow = value;
        ResetVoltages();
      }
    }

    [WwwSetting(wwwSettingsAO)]
    public int AoPeriodFast2 {
      get {
        return _AoPeriodFast2;
      }

      set {
        if(_AoPeriodFast2 == value)
          return;
        _AoPeriodFast2 = value;
        ResetVoltages();
      }
    }

    [WwwSetting(wwwSettingsAO)]
    public int AoPeriodSlow2 {
      get {
        return _AoPeriodSlow2;
      }

      set {
        if(_AoPeriodSlow2 == value)
          return;
        _AoPeriodSlow2 = value;
        ResetVoltages();
      }
    }

    public bool IsVoltFullScale => false;
    public double[] VoltsFullScaleMinMax { get; private set; }
  }
}
