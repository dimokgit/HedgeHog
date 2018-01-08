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

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    Func<CorridorStatistics> GetShowVoltageFunction() {
      return GetShowVoltageFunction(VoltageFunction);
    }
    Func<CorridorStatistics> GetShowVoltageFunction(VoltageFunction voltageFunction, int voltIndex = 0) {
      var getVolts = voltIndex == 0 ? GetVoltage : GetVoltage2;
      var setVolts = voltIndex == 0 ? SetVoltage : SetVoltage2;
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
        case HedgeHog.Alice.VoltageFunction.TLW2:
          return () => ShowVoltsByTLWn(2, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLW3:
          return () => ShowVoltsByTLWn(3, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLW4:
          return () => ShowVoltsByTLWn(4, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.BBSD:
          return () => ShowVoltsByBBSD(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.PPM:
          return () => ShowVoltsByPPM(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.PPMB:
          return () => ShowVoltsByPPMB(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLsTimeAvg:
          return () => ShowVoltsByTLsTimeAvg(true, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLsTimeMax:
          return () => ShowVoltsByTLsTimeAvg(false, getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.RiskReward:
          return () => ShowVoltsByRiskReward(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLDur:
          return () => ShowVoltsByTLDuration(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.TLDur2:
          return () => ShowVoltsByTLDuration2(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.PPMH:
          return ShowVoltsByPPMH;
        case HedgeHog.Alice.VoltageFunction.TFH:
          return voltIndex == 0
            ? (Func<CorridorStatistics>)ShowVoltsByFrameAverage
            : () => ShowVoltsByFrameAverage(GetVoltage2, SetVoltage2);
        //case HedgeHog.Alice.VoltageFunction.AO:
        //  return ShowVoltsByAO;
        case HedgeHog.Alice.VoltageFunction.MPH:
          return ShowVoltsByMPH;
        case HedgeHog.Alice.VoltageFunction.PpmM1:
          return () => { SetVoltsByPpm(); return null; };
        case HedgeHog.Alice.VoltageFunction.BPA1:
          return ShowVoltsByBPA1;
        case HedgeHog.Alice.VoltageFunction.Pair:
          return () => ShowVoltsByPair(getVolts, setVolts);
        case HedgeHog.Alice.VoltageFunction.Corr:
          return ShowVoltsByCorrelation;
        case HedgeHog.Alice.VoltageFunction.Gross:
          return ShowVoltsByGross;
        case HedgeHog.Alice.VoltageFunction.GrossV:
          return ShowVoltsByGrossVirtual;
        case HedgeHog.Alice.VoltageFunction.RatioDiff:
          return () => ShowVoltsByRatioDiff(voltIndex);
        case HedgeHog.Alice.VoltageFunction.VoltDrv:
          if(voltIndex == 0)
            throw new Exception($"{VoltageFunction.VoltDrv} can only be used as second voltage.");
          return ShowVoltsByVoltsDerivative;
        case HedgeHog.Alice.VoltageFunction.HVP:
          return () => ShowVoltsByHVP(getVolts, setVolts);
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

    CorridorStatistics ShowVoltsByFrameAverage() {
      return ShowVoltsByFrameAverage(GetVoltage, SetVoltage);
    }
    CorridorStatistics ShowVoltsByFrameAverage(Func<Rate, double> getVolt = null, Action<Rate, double> setVolt = null) {
      var useCalc = IsRatesLengthStable;
      if(!useCalc)
        return GetLastVolt(getVolt)
          .Select(v => ShowVolts(v, 2, getVolt, setVolt))
          .SingleOrDefault();
      var calcVolt = RatesHeightByFrameAverage(new[] { this }, 0.0625);
      SetVolts(InPips(calcVolt), getVolt, setVolt, 2);
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
    CorridorStatistics ShowVoltsByTLsTimeAvg(bool useMin, Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      return IsRatesLengthStableGlobal()
        ? TLTimeAvg(useMin).Select(v => ShowVolts(v, 2, getVolt, setVolt)).SingleOrDefault()
        : null;
    }
    CorridorStatistics ShowVoltsByRiskReward(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      return IsRatesLengthStableGlobal()
        ? RiskRewardRatio().YieldIf(Lib.IsNotNaN).Select(v => ShowVolts(v.ToPercent(), 2, getVolt, setVolt)).SingleOrDefault()
        : null;
    }
    CorridorStatistics ShowVoltsByTLDuration(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      return IsRatesLengthStableGlobal()
        ? LastTrendLineDurationPercentage().Select(v => ShowVolts(v, 2, getVolt, setVolt)).SingleOrDefault()
        : null;
    }
    CorridorStatistics ShowVoltsByTLDuration2(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      return IsRatesLengthStableGlobal()
        ? LastTrendLineDurationPercentage2(TrendLinesByDate, 2).With(v => ShowVolts(v, 2, getVolt, setVolt))
        : null;
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
    CorridorStatistics ShowVoltsByTLWn(int tlCount, Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      var tl3 = TrendLinesTrendsAll.OrderBy(tl => tl.EndDate).TakeLast(tlCount).ToArray();
      var dateMin = tl3.Min(tl => tl.StartDate);
      if(!tl3.Any(tl => tl.IsEmpty) && tl3.Length == tlCount && IsRatesLengthStable) {
        Func<TL, double[]> dateRange = tl => new[] { tl.StartDate, tl.EndDate }.ToArray(d => (d - dateMin).TotalMinutes);
        //var dateOverlapOk = !tl3.Permutation().Any(t => dateRange(t.Item1).DoSetsOverlap(TLsOverlap - 1, dateRange(t.Item2)));
        //if(dateOverlapOk) {
        var dateZero = tl3[0].StartDate;
        Func<DateTime, double> date0 = d => d.Subtract(dateZero).TotalMinutes;
        Func<TL, double[]> tlTMs = tl => new[] { date0(tl.StartDate), date0(tl.EndDate) };
        var tl3MM = tl3.Select(tl => tlTMs(tl)).ToArray();
        var overlap = tl3MM.Pairwise((tm1, tm2) => tm1.OverlapRatio(tm2)).Average().ToPercent();
        ShowVolts(overlap, VoltAverageIterations, getVolt, setVolt);
        //}
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

    CorridorStatistics ShowVoltsByPair(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      if(UseCalc()) {
        var nonVoltRates = UseRatesInternal(ri => ri
          .BackwardsIterator()
          .SkipWhile(r => !getVolt(r).IsNaNOrZero()).ToArray())
          .Concat()
          .Reverse()
          .ToArray();
        var voltDate = nonVoltRates
          .Select(r => r.StartDate)
          .DefaultIfEmpty(DateTime.MinValue)
          .First();
        var tm2 = TradingMacroHedged();
        var tmRates2 = (from tm in tm2
                        from tmRates in tm.UseRatesInternal(ri => ri.BackwardsIterator().TakeWhile(r => r.StartDate >= voltDate).ToArray())
                        from tmRate in tmRates
                        select tmRate)
                        .Reverse()
                        .ToArray();
        var corrs = tm2.SelectMany(tm => TMCorrelation(tm));
        corrs.ForEach(corr =>
        nonVoltRates.Zip(
          r => r.StartDate
          , tmRates2.Select(x => Tuple.Create(x.StartDate, corr == 1 ? x.PriceAvg : 1 / x.PriceAvg))
          , (r, t) => setVolt(r, t.Item2)));
        CalcVoltsFullScaleShift();
      }
      return null;
    }

    public IEnumerable<int> TMCorrelation(TradingMacro tmOther)
      => GetHedgeCorrelation(Pair, tmOther.Pair).Concat(TMCorrelationImpl((this, tmOther))).Take(1)
      .IfEmpty(() => Log = new Exception(new { pairThis = Pair, pairOther = tmOther.Pair, correlation = "is empty" } + ""));
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
    public static class Hedging {
      public static IEnumerable<int> TMCorrelation(TradingMacro tm1, TradingMacro tm2) =>
        from corrs in tm1.UseRates(ra1 => tm2.UseRates(ra2 => alglib.pearsoncorr2(ra1.ToArray(r => r.PriceAvg), ra2.ToArray(r => r.PriceAvg), ra1.Count.Min(ra2.Count))))
        from corr in corrs
        where corr != 0
        select corr > 0 ? 1 : -1;

      public static IEnumerable<(TradingMacro tm, bool buy
        , double tradeAmountAll, double tradeAmountUp, double tradeAmountDown
        , double tradingRatioAll, double tradingRatioM1All
        , double tradingRatioUp, double tradingRatioM1Up
        , double tradingRatioDown, double tradingRatioM1Down
        , double hvpr, double hv, double hvp, double mmr, double hvpM1r)>
        CalcTradeAmount(IList<(TradingMacro tm, bool buy)> tms, double equity) {
        var minMaxes = (from tm in tms
                        from tmM1 in tm.tm.TradingMacroM1()
                        from hv in tm.tm.HistoricalVolatility()
                        from hvM1 in tmM1.HistoricalVolatility()
                        from hvUp in tm.tm.HistoricalVolatilityUp()
                        from hvM1Up in tmM1.HistoricalVolatilityUp()
                        from hvDown in tm.tm.HistoricalVolatilityDown()
                        from hvM1Down in tmM1.HistoricalVolatilityDown()
                        from hvp in tm.tm.HistoricalVolatilityByPips()
                        from hvpM1 in tmM1.HistoricalVolatilityByPips()
                        let mmr = TradesManagerStatic.GetMMR(tm.tm.Pair, tm.buy)
                        orderby mmr descending
                        select new { tm.tm, tradeMax = equity / mmr, tm.buy, hv, hvM1, hvUp, hvM1Up, hvDown, hvM1Down, hvp, hvpM1, mmr }
                        )
                        .Pairwise((min, max) => new { min, max,
                          hvr = min.hv / max.hv,
                          hvM1r = min.hvM1 / max.hvM1,
                          hvrUp = min.hvUp / max.hvUp,
                          hvM1rUp = min.hvM1Up / max.hvM1Up,
                          hvrDown = min.hvDown / max.hvDown,
                          hvM1rDown = min.hvM1Down / max.hvM1Down
                        })
                        .ToArray();
        var ctas = minMaxes.SelectMany(mm => {
          var hvr = mm.hvr.Avg(mm.hvM1r);
          var maxTradeAll = mm.max.tradeMax.Min(mm.min.tradeMax * hvr);
          var minTradeAll = mm.min.tradeMax.Min(maxTradeAll / mm.hvr);

          var hvrUp = mm.hvrUp.Avg(mm.hvM1rUp);
          var maxTradeUp = mm.max.tradeMax.Min(mm.min.tradeMax * hvrUp);
          var minTradeUp = mm.min.tradeMax.Min(maxTradeUp / hvrUp);

          var hvrDown = mm.hvrDown.Avg(mm.hvM1rDown);
          var maxTradeDown = mm.max.tradeMax.Min(mm.min.tradeMax * hvrDown);
          var minTradeDown = mm.min.tradeMax.Min(maxTradeDown / hvrDown);

          var hvs = mm.min.hv + mm.max.hv;
          var hvsUp = mm.min.hvUp + mm.max.hvUp;
          var hvsDown = mm.min.hvDown + mm.max.hvDown;

          var hvM1s = mm.min.hvM1 + mm.max.hvM1;
          var hvM1sUp = mm.min.hvM1Up + mm.max.hvM1Up;
          var hvM1sDown = mm.min.hvM1Down + mm.max.hvM1Down;

          var hvps = mm.min.hvp + mm.max.hvp;
          var hvpM1s = mm.min.hvpM1 + mm.max.hvpM1;
          return new[] {
          (mm.min.tm, mm.min.buy
          , minTradeAll, minTradeUp, minTradeDown
          ,1 - mm.min.hv / hvs,1 - mm.min.hvM1 / hvM1s,1 - mm.min.hvUp / hvsUp
          ,1 - mm.min.hvM1Up / hvM1sUp
          ,1 - mm.min.hvDown / hvsDown,1 - mm.min.hvM1Down / hvM1sDown
          ,mm.min.hvp / hvps,mm.min.hv,mm.min.hvp,mm.min.mmr,mm.min.hvpM1 / hvpM1s),
          (mm.max.tm, mm.max.buy
          ,maxTradeAll,maxTradeUp,maxTradeDown
          ,1 - mm.max.hv / hvs,1 - mm.max.hvM1 / hvM1s
          ,1 - mm.max.hvUp / hvsUp,1 - mm.max.hvM1Up / hvM1sUp
          ,1 - mm.max.hvDown / hvsDown,1 - mm.max.hvM1Down / hvM1sDown
          ,mm.max.hvp / hvps,mm.max.hv,mm.max.hvp,mm.max.mmr,mm.max.hvpM1 / hvpM1s)
          };
        });

        return from tm in tms
               join cta in ctas on tm equals (cta.tm, cta.buy)
               select cta;
      }
    }
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
      ShowVolts(TradesManager.GetTrades().Net2(), 0, GetVoltage2, SetVoltage2);
      return null;
    }
    CorridorStatistics ShowVoltsByHVP(Func<Rate, double> getVolt, Action<Rate, double> setVolt) {
      if(UseCalc())
        HistoricalVolatility().ForEach(hvp => ShowVolts(hvp, getVolt, setVolt));
      return null;
    }
    CorridorStatistics ShowVoltsByVoltsDerivative() {
      if(UseCalc()) {
        var voltses = (from volts in UseRates(ra => ra.Select(r => (r, v: GetVoltage(r))).Where(t => t.v.IsNotNaN()).ToArray())
                       let height = volts.MinMax(v => v.v).Aggregate((min, max) => min.Abs(max))
                       from t in volts.Cma(t => t.v, RatesHeightMin, RatesHeightMin.ToInt() * 2, (v, cma) => (v.r, v: ((v.v - cma) / height * 100)))
                       select t
        ).Select(t => {
          SetVoltage2(t.r, t.v);
          return t.v;
        })
        .ToArray();
        {
          var min = voltses.AverageByIterations(-VoltAverageIterations2).DefaultIfEmpty(double.NaN).Average();
          var max = voltses.AverageByIterations(VoltAverageIterations2).DefaultIfEmpty(double.NaN).Average();
          min = new[] { min, max }.OrderBy(m => m.Abs()).Last();
          GetVoltage2High = () => new[] { min.Abs() };
          GetVoltage2Low = () => new[] { -min.Abs() };
        }
      }
      return null;
    }

    IEnumerable<(TradingMacro tm, TradingMacro tmh)> GetHedgedTradingMacros(string pair) {
      return from tm2 in TradingMacroHedged()
             select (this, tm2);
    }
    public static List<IList<Trade>> HedgeTradesVirtual { get; set; } = new List<IList<Trade>>();

    CorridorStatistics ShowVoltsByGrossVirtual() {
      var hedgedTrades = HedgeTradesVirtual
        .Where(ht => ht.Select(t => t.Pair).With(pairs => pairs.Contains(Pair) && TradingMacroHedged().Any(tm => pairs.Contains(tm.Pair))))
        .Concat()
        .OrderByDescending(t => t.Pair == Pair)
        .Buffer(2)
        .SelectMany(b => new[] { (tm: this, t: b[0]) }.Concat(TradingMacroHedged(tm => (tm, t: b[1]))))
        .ToArray();
      var tuples = (from ht in hedgedTrades
                    from x in ht.tm.UseRates(ra => ra.Select(r => (d: r.StartDate, t: (r, n: ht.t.CalcNetPL2(ht.t.Close = ht.t.IsBuy ? r.BidLow : r.AskHigh)))))
                    select x.ToArray()
      ).ToArray();
      tuples.Buffer(2)
      .ForEach(b => b[0].Zip(b[1], (t1, t2) => SetVoltage2(t1.t.r, t1.t.n + t2.t.n)));

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
              .DistinctUntilChanged(d => d.d.Round(MathCore.RoundTo.Minute))
              .Subscribe(s => s.a(), exc => { });
          }
        return _MaxHedgeProfitSubject;
      }
    }
    void OnMaxHedgeProfit(DateTime d, Action a) => MaxHedgeProfitSubject.OnNext((d, a));
    #endregion

    int VoltAverageIterationsByIndex(int index) => index == 0 ? VoltAverageIterations : VoltAverageIterations;
    IEnumerable<double> GetLastVoltByIndex(int voltIndex) => voltIndex == 0 ? GetLastVolt() : GetLastVolt2();
    Func<Rate, double> GetVoltByIndex(int voltIndex) => voltIndex == 0 ? GetVoltage : GetVoltage2;
    Action<Rate, double> SetVoltByIndex(int voltIndex) => voltIndex == 0 ? SetVoltage : SetVoltage2;
    Action<double> SetVoltHighByIndex(int voltIndex) => voltIndex == 0 ? new Action<double>(v => GetVoltageHigh = () => new[] { v }) : v => GetVoltage2High = () => new[] { v };
    Action<double> SetVoltLowByIndex(int voltIndex) => voltIndex == 0 ? new Action<double>(v => GetVoltageAverage = () => new[] { v }) : v => GetVoltage2Low = () => new[] { v };
    IEnumerable<double> GetVoltHighByIndex(int voltIndex) => voltIndex == 0 ? GetVoltageHigh() : GetVoltage2High();
    IEnumerable<double> GetVoltLowByIndex(int voltIndex) => voltIndex == 0 ? GetVoltageAverage() : GetVoltage2Low();

    CorridorStatistics ShowVoltsByRatioDiff(int voltIndex) {
      if(UseCalc()) {
        //var voltRates = UseRates(ra => ra.Where(r => !GetVoltage(r).IsNaNOrZero()).ToArray()).Concat().ToArray();
        var voltRates = ShowVoltsByRatioDiff_New()
          .ToArray();
        VoltsFullScaleMinMax = voltRates.SelectMany(vr => GetFullScaleMinMax(vr.r, vr.h)).ToArray();
        OnMaxHedgeProfit(ServerTime, () =>
         MaxHedgeProfit = new[] {
          CalcMaxHedgeProfit()
          .Concat(MaxHedgeProfit)
          .DefaultIfEmpty()
          .Aggregate((p, n) => p.Zip(n, (p1, p2) => (p1.profit.Cma(10, p2.profit), p1.buy)).ToArray())
         }.Where(x => x != null).ToArray()
        );

        var voltMap = voltRates.SelectMany(vr => RatioMapDouble((vr.h, VoltsFullScaleMinMax)));
        var priceMap = voltRates.SelectMany(vr => RatioMap((vr.r, _priceAvg, null)));

        double min = double.MaxValue, max = double.MinValue, priceMin = double.MaxValue, priceMax = double.MinValue;
        var rateVolts = priceMap
          .Zip(voltMap, (b, a) => {
            var v = b.t.v - a.v;
            return (b.t.r, v);
          }).ToArray();
        var volts = new double[rateVolts.Length];
        var voltCounter = 0;
        var cmaPeriod = RatesHeightMin.ToInt();
        //rateVolts = rateVolts.Cma(t => t.v, cmaPeriod, (t, cma) => (t.r, cma)).ToArray();
        rateVolts.Cma(t => t.v, cmaPeriod, cmaPeriod, (t, cma) => {
          var v = /*t.v - */cma;
          min = v.Min(min);
          max = v.Max(max);
          priceMin = t.r.PriceAvg.Min(priceMin);
          priceMax = t.r.PriceAvg.Max(priceMax);
          SetVoltByIndex(voltIndex)(t.r, v);
          volts[voltCounter++] = v;
        });
        if(volts.Any()) { // store this logic for other times
          min = volts.AverageByIterations(-VoltAverageIterationsByIndex(voltIndex)).DefaultIfEmpty(double.NaN).Average();
          max = volts.AverageByIterations(VoltAverageIterationsByIndex(voltIndex)).DefaultIfEmpty(double.NaN).Average();
        }
        min = new[] { min, max }.OrderBy(m => m.Abs()).Last();

        SetVoltHighByIndex(voltIndex)(min.Abs());
        SetVoltLowByIndex(voltIndex)(-min.Abs());
        voltRates.Select(vr => vr.r).ForEach(ra => ra.Zip(voltMap, (r, h) => r.PriceHedge = PosByRatio(priceMin, priceMax, h.v)).Count());
      }
      return null;

      double PosByRatio(double min, double max, double pos) {
        return min + (max - min) * pos;
      }
    }

    IEnumerable<(Rate[] r, double[] h)> ShowVoltsByRatioDiff_New() => ShowVoltsByRatioDiff_New(t => { });
    IEnumerable<(Rate[] r, double[] h)> ShowVoltsByRatioDiff_New(Action<(Rate rate, double ratio)> action) =>
      from xs in ZipHedgedRates()
      select (xs.Do(action).Select(t => t.rate).ToArray(), xs.Select(t => t.ratio).ToArray());

    IEnumerable<IList<(Rate rate, double ratio)>> ZipHedgedRates() =>
      from tm2 in TradingMacroHedged()
      from corr in TMCorrelation(tm2)
      from xs in UseRates(tm2, (ra, ra2) => ZipRateArrays((ra, ra2, corr)))
      select xs;

    Func<(List<Rate> ra, List<Rate> ra2, int corr), IList<(Rate rate, double ratio)>> ZipRateArrays = ZipRateArraysImpl;//.MemoizeLast(t => t.ra.LastOrDefault().StartDate);
    private static Func<(List<Rate> ra, List<Rate> ra2, int corr), IList<(Rate rate, double)>> ZipRateArraysImpl =
      t => t.ra.Zip(
        r => r.StartDate
        , t.ra2.Select(r => Tuple.Create(r.StartDate, t.corr == 1 ? r.PriceAvg : 1 / r.PriceAvg))
        , r => r.Item1
        , (r, r2) => (r, r2.Item2)
        ).ToArray();


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

    void CalcVoltsFullScaleShift() {
      if(IsVoltFullScale && UseCalc()) {
        var voltRates = UseRates(ra => ra.Where(r => !GetVoltage(r).IsNaNOrZero()).ToArray())
          .Concat()
          .ToArray();
        if(voltRates.Length > BarsCountCalc * 0.9) {
          if(false) VoltsFullScaleMinMax = GetFullScaleMinMax(voltRates, GetVoltage);
          //MaxHedgeProfit = CalcMaxHedgeProfit().Concat(MaxHedgeProfit).Aggregate((p,n)=>p.Zip(n,(p1,p2)=>(p1.buy,p1.profit.Cma(10,p2.profit)))  });
          if(false)
            CalcVoltsFullScaleShiftByStDev(this);
        }
      }
    }
    IEnumerable<(double profit, bool buy)[]> CalcMaxHedgeProfit() {
      if(UseCalc())
        foreach(var hp in CalcMaxHedgeProfitMem(this))
          yield return hp;
    }

    static IEnumerable<(double pos, double std)> CalcVoltsFullScaleShiftByStDev(TradingMacro tmThis) {
      foreach(var mapH in tmThis.TradingMacroHedged(tm => tm.UseRates(ra => RatioMap((ra, r => 1 / _priceAvg(r), null))).Concat().ToArray(t => t.t.v))) {
        foreach(var map in tmThis.UseRates(ra => RatioMap((ra, _priceAvg, null)).ToArray(t => t.t.v))) {
          var stDevs = CalcFullScaleShiftByStDev(mapH, map);
          //Debug.WriteLine(new { stDevs.First().pos, stDevs.First().std, Pair, PairIndex });
          yield return stDevs.First();
        }
      }
    }

    private static IEnumerable<(double pos, double std)> CalcFullScaleShiftByStDev(IList<double> map, IList<double> mapH) {
      return Enumerable.Range(-99, 99 * 2).Select(i => i / 100.0).Select(pos => (pos, std: GetStDev(pos))).OrderBy(x => x.std);
      double GetStDev(double ofs) => map.Zip(mapH, (t1, t2) => t1 - (t2 + ofs)).Sum().Abs();
    }

    private static double[] GetFullScaleMinMax(IList<Rate> voltRates, Func<Rate, double> rateMap) {
      var linearPrice = voltRates.Linear(_priceAvg).RegressionValue(voltRates.Count / 2);
      var priceMinMax = voltRates.MinMax(_priceAvg);
      var pricePos = linearPrice.PositionRatio(priceMinMax);

      var linearVolt = voltRates.Linear(rateMap).RegressionValue(voltRates.Count / 2);
      var v = voltRates.MinMax(rateMap).Yield(mm => new { min = mm[0], max = mm[1] }).Single();
      var voltPos = linearVolt.PositionRatio(v.min, v.max);

      return new[] { voltMinNew(), voltMaxNew() };

      double voltMaxNew() => (linearVolt - v.min) / pricePos + v.min;
      double voltMinNew() => v.max - (v.max - linearVolt) / (1 - pricePos);
    }
    private static double[] GetFullScaleMinMax(IList<Rate> rates, IList<double> hedge) {
      var linearPrice = rates.Linear(_priceAvg).RegressionValue(rates.Count / 2);
      var priceMinMax = rates.MinMax(_priceAvg);
      var pricePos = linearPrice.PositionRatio(priceMinMax);

      var linearVolt = hedge.Linear().RegressionValue(hedge.Count / 2);
      var v = hedge.MinMax().Yield(mm => new { min = mm[0], max = mm[1] }).Single();
      var voltPos = linearVolt.PositionRatio(v.min, v.max);
      if(voltPos.IsNaN()) {// test stdev approach
        var rMap = RatioMapDoubleImpl((rates.ToArray(_priceAvg), null)).ToArray(t => t.position);
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

    static Func<TradingMacro, IEnumerable<(double profit, bool buy)[]>> CalcMaxHedgeProfitMem =
      new Func<TradingMacro, IEnumerable<(double profit, bool buy)[]>>(CalcMaxHedgeProfitImpl);
    //.MemoizeLast(tm => tm.RatesArray.BackwardsIterator(r => r.StartDate.Round(MathCore.RoundTo.Minute)).LastOrDefault());
    private static IEnumerable<(double profit, bool buy)[]> CalcMaxHedgeProfitImpl(TradingMacro tm) {
      Func<int, Func<Rate, double>> getOther = corr => corr == 1 ? new Func<Rate, double>(r => r.PriceAvg) : r => 1 / r.PriceAvg;
      return from tm2 in tm.TradingMacroHedged()
             from corrs in tm.TMCorrelation(tm2)
             let getter = getOther(corrs)
             from tmMap in tm.UseRates(ra => RatioMap((ra, _priceAvg, null))).ToArray()
             from tmMap2 in tm2.UseRates(ra => RatioMap((ra, getter, tm.VoltsFullScaleMinMax))).ToArray()
             let mm = tmMap2.Zip(tmMap, (r, t) => new { r = t.t.r, r2 = r.t.r, d = t.t.v - r.t.v }).MinMaxBy(t => t.d)
             let htb = tm.HedgeBuySell(true).Select(t => new { lots = t.Value.tm.GetLotsToTrade(t.Value.TradeAmount, 1, 1) }).ToArray()
             let hts = tm.HedgeBuySell(false).Select(t => new { lots = t.Value.tm.GetLotsToTrade(t.Value.TradeAmount, 1, 1) }).ToArray()
             where mm.Any() && hts.Any() && htb.Any()
             let t = new[] {
             (profit:(htb[0].lots * mm[1].r.BidLow + (-corrs) * htb[1].lots * mm[1].r2.BidLow) -
                    (htb[0].lots * mm[0].r.AskHigh + (-corrs) * htb[1].lots * mm[0].r2.AskHigh),
                    buy:true),
             (profit:(hts[0].lots * mm[1].r.BidLow + (-corrs) * hts[1].lots * mm[1].r2.BidLow) -
                    (hts[0].lots * mm[0].r.AskHigh + (-corrs) * hts[1].lots * mm[0].r2.AskHigh),
                    buy:false),
             }
             where t.All(p => p.profit > 0)
             select t;
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

    IEnumerable<int> LastTrendLineDurationPercentage() => LastTrendLineDurationPercentage(TrendLinesByDate);
    static IEnumerable<int> LastTrendLineDurationPercentage(IList<TL> tls) =>
      from tlLast in tls.TakeLast(1)
      let tla = tls.SkipLast(1).Select(tl => tl.TimeSpan.TotalMinutes).SquareMeanRoot()
      select tlLast.TimeSpan.TotalMinutes.Div(tla).ToPercent();

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

    public (double[] upDown, DateTime[] dates)[] BeforeHours = new(double[], DateTime[])[0];
    private void SetBeforeHours() {
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
    }
    private void SetCentersOfMass() {
      var startHour = CoMStartHour;
      var endHour = CoMEndHour;
      if(!TryServerTime(out var serverTime)) return;
      var stripHoursGreen = new[] { serverTime.Date.AddHours(startHour), serverTime.Date.AddHours(endHour) };
      var stripHours = SetCenterOfMassByM1Hours(stripHoursGreen, t => {
        CenterOfMassBuy = t.upDown[1];
        CenterOfMassSell = t.upDown[0];
        return CenterOfMassDates = t.dates;
      });
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

      return;
      var height = ScanCorridorByStDevAndAngleHeightMin();
      GetSenterOfMassStrip(RatesArray.ToArray(_priceAvg), height, 0, (rates, t, b) => new { rates = UseVoltage ? rates : null, t, b })
        .ForEach(a => {
          CenterOfMassBuy = a.t;
          CenterOfMassSell = a.b;
          if(UseVoltage) {
            var frameLength = VoltsFrameLength;
            double waveCount = a.rates
              .Buffer(frameLength, 1)
              .Where(b => b.Count == frameLength)
              .Select(b => b.LinearSlope().Sign())
              .DistinctUntilChanged()
              .Count();
            //var prices = RatesArray.Select(_priceAvg).Buffer(RatesArray.Count / 2).ToArray();
            //var ratio = prices[1].StDev() / prices[0].StDev();// RatesHeight / StDevByPriceAvg;
            RatesArray.Where(r => GetVoltage(r).IsNaN()).ForEach(r => SetVoltage(r, RatesArray.Count / waveCount / frameLength));
          }
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
       .MinMax(r => r.BidLow, r => r.AskHigh)
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

    public bool IsVoltFullScale => VoltageFunction == VoltageFunction.Pair;
    public double[] VoltsFullScaleMinMax { get; private set; }
    public IEnumerable<(double profit, bool buy)[]> MaxHedgeProfit { get; private set; } = new[] { new[] { (0.0, false) }.Take(0).ToArray() }.Take(0);
  }
}
