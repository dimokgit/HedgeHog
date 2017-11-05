using HedgeHog.Bars;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Reactive.Linq;
using TL = HedgeHog.Bars.Rate.TrendLevels;
using static HedgeHog.IEnumerableCore;
using static HedgeHog.MonoidsCore;
using static HedgeHog.Core.JsonExtensions;
using System.Runtime.CompilerServices;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {

    #region TradeConditions
    public delegate TradeDirections TradeConditionDelegate();
    public delegate TradeDirections TradeConditionDelegateHide();
    #region Trade Condition Helers
    private bool IsCanTradeExpired(TimeSpan slack) {
      return BuySellLevels.Max(sr => sr.DateCanTrade) < ServerTime.Subtract(slack);
    }
    bool IsCurrentPriceInside(params double[] levels) {
      return new[] { CurrentEnterPrice(true), CurrentEnterPrice(false) }.All(cp => cp.Between(levels[0], levels[1]));
    }
    private bool IsCurrentPriceInsideTradeLevels {
      get {
        return new[] { CurrentEnterPrice(true), CurrentEnterPrice(false) }.All(cp => cp.Between(SellLevel.Rate, BuyLevel.Rate));
      }
    }
    private bool IsCurrentPriceInsideTradeLevels2 {
      get {
        Func<double, double[], bool> isIn = (v, levels) => {
          var h = levels.Height() / 4;
          return v.Between(levels.Min() + h, levels.Max() - h);
        };
        return new[] { CurrentEnterPrice(true), CurrentEnterPrice(false) }.All(cp => isIn(cp, new[] { SellLevel.Rate, BuyLevel.Rate }));
      }
    }
    private bool IsCurrentPriceInsideTradeLevels3(double slack) {
      return
        !BuyLevel.CanTrade || CurrentEnterPrice(true) - slack < BuyLevel.Rate &&
        !SellLevel.CanTrade || CurrentEnterPrice(false) + slack > SellLevel.Rate;
    }
    private bool IsCurrentPriceInsideBlueStrip { get { return IsCurrentPriceInside(CenterOfMassSell, CenterOfMassBuy); } }
    #endregion
    #region TradeDirection Helpers
    TradeDirections TradeDirectionBoth(bool ok) { return ok ? TradeDirections.Both : TradeDirections.None; }
    TradeConditionDelegate TradeDirectionEither(Func<bool> ok) { return () => ok() ? TradeDirections.Up : TradeDirections.Down; }
    TradeDirections IsTradeConditionOk(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, TradeDirections> condition) {
      return TradingMacroOther(tmPredicate).Take(1).Select(condition).DefaultIfEmpty(TradeDirections.Both).First();
    }
    #endregion

    double _tipRatioCurrent = double.NaN;

    #region TLS
    public TradeConditionDelegateHide ElliotOk {
      get {
        //TradingMacroTrader(tm => Log = new Exception(new { FreshOk = new { tm.WavesRsdPerc } } + "")).FirstOrDefault();
        var tlWaves = ToFunc((TradingMacro tm) => tm.TrendLinesTrendsAll
         .OrderBy(tl => tl.EndDate)
         .TakeLast(tm.TrendLinesTrendsAll.Length.Div(2).Floor())
         .Pairwise((tl1, tl2) => new { tl1, tl2 })
         .AsSingleable());
        var tlPrimes = ToFunc((TradingMacro tm) => tm.TrendLinesTrendsAll.TakeLast(2));
        var ok = ToFunc(() => from tm in TradingMacroTrender()
                              from wave in tlWaves(tm)
                              let prime = tlPrimes(tm)
                              select prime.Contains(wave.tl1) && !prime.Contains(wave.tl2));
        return () => ok().Select(b => TradeDirectionByBool(b)).SingleOrDefault();
      }
    }

    public TradeConditionDelegate Volt2Ok {
      get {
        return () => {
          var tms = TradingMacroM1(tmh=>new[] { this, tmh }).Concat().Select(GetTD).ToArray();
          return tms.Aggregate(TradeDirections.Both, (a, td) => a & td);
        };
        TradeDirections GetTD(TradingMacro tm) {
          var vrMin = tm.VoltRange_20;
          var vrMax = tm.VoltRange_21;
          var lv = tm.GetLastVolt(GetVoltage2).ToArray();
          var down = lv.Where(volt => volt >= vrMax && tm.GetVoltage2High().Any(v => volt >= v)).Select(_ => TradeDirections.Down);
          var up = lv.Where(volt => volt <= vrMin && tm.GetVoltage2Low().Any(v => volt <= v)).Select(_ => TradeDirections.Up);
          return down.Concat(up).SingleOrDefault();
        }
      }
    }


    #region Fresh
    public TradeConditionDelegate FreshOk {
      get {
        TradingMacroTrader(tm => Log = new Exception(new { FreshOk = new { tm.WavesRsdPerc } } + ""));
        Func<Singleable<TL>> tls = () => TradingMacroTrender(tm =>
          tm.TradeTrendLines
          .Take(1)
          .Where(tl => !tl.IsEmpty && IsTLFresh(tm, tl, tm.WavesRsdPerc / 100.0))
          //.Where(tl => IsTLFresh(tm, tl))
          )
        .Concat()
        .AsSingleable();
        return () => tls()
          .Select(_ => TradeDirections.Both)
          .SingleOrDefault();
      }
    }
    public TradeConditionDelegate FrshTrdOk {
      get {
        TradingMacroTrader(tm => Log = new Exception(new { FrshTrdOk = new { tm.WavesRsdPerc } } + ""));
        Func<Singleable<TL>> tls = () => TradingMacroTrender(tm =>
          tm.TradeTrendLines
          .OrderByDescending(tl => tl.EndDate)
          .Where(tl => !tl.IsEmpty)
          .IfEmpty(() => { if(!IsAsleep) throw new Exception(nameof(TrendLevelByTradeLevel) + "() returned empty handed."); })
          .Where(tl => IsTLFresh(tm, tl, tm.WavesRsdPerc / 100.0)))
          .Take(1)
          .Concat()
          .AsSingleable();
        return () => tls()
          .Select(_ => TradeDirections.Both)
          .SingleOrDefault();
      }
    }
    public TradeConditionDelegate BSOk {
      get {
        return () => TradeDirectionByBool(BuyLevel.Rate.Abs(SellLevel.Rate) <= StDevByPriceAvg);
      }
    }

    [TradeConditionAsleep]
    public TradeConditionDelegate THOk {
      get {
        return () => TradeDirectionByBool(IsTradingHour());
      }
    }

    double RiskRewardDenominator => (TradeOpenActionsHaveWrapCorridor ? HeightForWrapToCorridor(false) : BuyLevel.Rate.Abs(SellLevel.Rate));
    double RiskRewardRatio() {
      return CalculateTakeProfit() / RiskRewardDenominator;
    }
    public TradeConditionDelegate RsRwOk {
      get {
        return () => TradeDirectionByTreshold(RiskRewardRatio() * 100, RiskRewardThresh);
      }
    }
    #endregion

    #region TLs
    public TradeConditionDelegate SlowOk {
      get {
        var ok = ToFunc(() => (from tm in TradingMacroTrender()
                               from tlLast in tm.TrendLinesByDate.TakeLast(1)
                               from tlSlow in tm.TrendLinesTrendsAll.OrderByDescending(tl => tl.TimeSpan.TotalMinutes).Take(1)
                               select new { tlSlow, ok = tlLast == tlSlow, tls = tm.TrendLinesTrendsAll }
           ));
        return () => ok()
      .Do(x => setSelected(x.tls, x.tlSlow))
      .Select(x => TradeDirectionByBool(x.ok))
      .SingleOrDefault();
      }
    }
    static string[] _minTradeLevelSuffix = new[] { "Min", "Down" };
    static string[] _maxTradeLevelSuffix = new[] { "Max", "Up" };
    bool isReverse => _minTradeLevelSuffix.Any(ml => (LevelBuyBy + "").EndsWith(ml)) && _maxTradeLevelSuffix.Any(ml => (LevelSellBy + "").EndsWith(ml));
    [TradeConditionSetCorridor]
    public TradeConditionDelegate TLSOk {
      get {
        var ok = TLSImpl(100);
        return () => ok();
      }
    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegateHide TLS2Ok {
      get {
        var ok = TLSImpl(3);
        return () => ok();
      }
    }

    static void setSelected(IEnumerable<TL> tls, TL tlSelected) => tls.ForEach(tl0 => tl0.IsSelected = tlSelected == tl0);

    private TradeConditionDelegate TLSImpl(int skip = 0) {
      var ok = ToFunc(() => (from tm in TradingMacroTrender()
                             from x in tm.TrendLinesByDate.Select((tl, i) => new { tl, i })
                             orderby x.tl.TimeSpan.TotalMinutes descending
                             select new { x.tl, x.i, tm }
         ));
      return () => ok()
      .Take(1)
      .Do(x => SetBSfromTL(x.tl, isReverse))
      .Do(x => setSelected(x.tm.TrendLinesTrendsAll, x.tl))
      .Select(x => TradeDirectionByBool(x.i <= skip))
      .SingleOrDefault();
    }

    [TradeConditionSetCorridor]
    public TradeConditionDelegate TLLOk {
      get {
        var ok = TTLImpl(0);
        return () => ok();
      }
    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegate TLL2Ok {
      get {
        var ok = TTLImpl(1);
        return () => ok();
      }
    }

    private TradeConditionDelegate TTLImpl(int skip) {
      Func<TL[], TL[]> tls1 = tls => tls.Where(tl => !tl.IsEmpty).OrderByDescending(tl => tl.EndDate).Skip(skip).Take(1).ToArray();
      Func<TL, DateTime[]> dateRange = tl => {
        var slack = (tl.TimeSpan.TotalMinutes * .1).FromMinutes();
        return new[] { tl.StartDate.Add(slack), tl.EndDate.Subtract(slack) };
      };
      //Func<TL, bool> tlOk = tl => tl.Color != TrendLevelsPreset.Blue + "";
      return () => (from tm in TradingMacroTrender()
                    let tls = tm.TrendLinesTrendsAll.Where(tl => !tl.IsEmpty).ToArray()
                    let dateOverlapOk = !tls.Permutation().Any(t => dateRange(t.Item1).DoSetsOverlap(dateRange(t.Item2)))
                    where dateOverlapOk
                    from tl in tls1(tls)
                    select new { tls, tl }
                    )
                    .Do(x => SetBSfromTL(x.tl, isReverse))//, x.tls, isReverse()))
                    .Do(x => setSelected(x.tls, x.tl))
                    .Select(_ => TradeDirections.Both)
                    .DefaultIfEmpty()
                    .Single();
    }

    public TradeConditionDelegateHide TLHOk {
      get {
        TradingMacroTrader(tm => Log = new Exception(new { TLHOk = new { tm.TipRatio } } + ""));
        Func<IEnumerable<double>, IEnumerable<double>> abs = (rs) => rs.Scan((d1, d2) => InPips(d1.Abs(d2)));
        Func<IList<double>, IEnumerable<double>, bool> testInside = (outer, inner) => inner.All(d => d.Between(outer[0], outer[1]));
        Func<TL, IList<double>> priceMinMax = tl => tl.PriceMin.Concat(tl.PriceMax).ToArray();
        Func<TradingMacro, IEnumerable<TL>[]> trendFlats = tm => new[] { tm.TrendLinesFlat/*.Permutation(3)*/.Reverse() };
        var ok = MonoidsCore.ToFunc((TradingMacro tm, IEnumerable<TL> flats) => {
          //var flats = trendFlats(tm);
          var isInside = flats.Pairwise((tl1, tl2) => testInside(priceMinMax(tl1), priceMinMax(tl2))).All(b => b);
          if(!isInside)
            return new { td = TradeDirections.None, flat = flats.Last() };
          var perms = flats.ToArray().Permutation((tl1, tl2) => new { absMin = abs(tl1.PriceMin.Concat(tl2.PriceMin)), absMax = abs(tl1.PriceMax.Concat(tl2.PriceMax)) }).ToArray();
          var pipTres = InPips(tm.RatesHeight * tm.TipRatio);
          var upAvg = perms.SelectMany(p => p.absMax).DefaultIfEmpty(double.NaN).Distinct().Average();
          var downAvg = perms.SelectMany(p => p.absMin).DefaultIfEmpty(double.NaN).Distinct().Average();
          var upOk = upAvg <= pipTres ? TradeDirections.Both : TradeDirections.None;
          var downOk = downAvg <= pipTres ? TradeDirections.Both : TradeDirections.None;
          var useLast = GetTradeLevelsPreset().Any(tlp => tlp == TradeLevelsPreset.Lime);
          return new { td = upOk | downOk, flat = flats.FirstOrLast(useLast).Single() };
        });
        return () => TradingMacroTrender(tm => trendFlats(tm).Select(f => ok(tm, f)))
        .SelectMany(x => x)
        .Where(x => x.td.HasAny())
        //.Do(x => {
        //  x.flat.PriceMax.ForEach(r => BuyLevel.RateEx = r);
        //  x.flat.PriceMin.ForEach(r => SellLevel.RateEx = r);
        //})
        .Select(x => x.td)
        .DefaultIfEmpty()
        .Aggregate((td1, td2) => td1 | td2);

      }
    }

    public TradeConditionDelegateHide TLH2Ok {
      get {
        TradingMacroTrader(tm => Log = new Exception(new { TLH2Ok = new { tm.TipRatio } } + ""));
        Func<IEnumerable<double>, IEnumerable<double>> abs = (rs) => rs.Scan((d1, d2) => InPips(d1.Abs(d2)));
        Func<IList<double>, IEnumerable<double>, bool> testInside = (outer, inner) => inner.All(d => d.Between(outer[0], outer[1]));
        Func<TL, IList<double>> priceMinMax = tl => tl.PriceMin.Concat(tl.PriceMax).ToArray();
        Func<TradingMacro, IEnumerable<TL>[]> trendFlats = tm => new[] { tm.TrendLinesFlat.Reverse()/*.Permutation(3)*/ };
        var ok = MonoidsCore.ToFunc((TradingMacro)null, (IEnumerable<TL>)null, (tm, flats) => {
          //var flats = trendFlats(tm);
          var isInside = flats.Pairwise((tl1, tl2) => testInside(priceMinMax(tl1), priceMinMax(tl2))).All(b => b);
          if(!isInside)
            return TradeDirections.None;
          ;
          var perms = flats.ToArray().Permutation((tl1, tl2) => new { absMin = abs(tl1.PriceMin.Concat(tl2.PriceMin)), absMax = abs(tl1.PriceMax.Concat(tl2.PriceMax)) }).ToArray();
          var pipTres = InPips(tm.RatesHeight * tm.TipRatio);
          var upAvg = perms.SelectMany(p => p.absMax).DefaultIfEmpty(double.NaN).Distinct().Average();
          var downAvg = perms.SelectMany(p => p.absMin).DefaultIfEmpty(double.NaN).Distinct().Average();
          var upOk = upAvg <= pipTres;
          var downOk = downAvg <= pipTres;
          return upOk && downOk ? TradeDirections.Both : TradeDirections.None;
        });
        return () => TradingMacroTrender(tm => trendFlats(tm).Select(f => ok(tm, f)))
        .SelectMany(x => x)
        .DefaultIfEmpty()
        .Aggregate((td1, td2) => td1 | td2);

      }
    }

    bool IsTLFresh(TL tl, double percentage = 1) {
      return IsTLFresh(this, tl, percentage);
    }
    //
    static bool IsTLFresh(TradingMacro tm, TL tl) {
      return IsTLFresh(tm, tl, tm.WavesRsdPerc / 100.0);
    }
    static bool IsTLFresh(TradingMacro tm, TL tl, double percentage = 1) {
      var index = tm.RatesArray.Count - (tl.Count * percentage.Abs()).ToInt() - 1;
      if(tl.IsEmpty || index >= tm.RatesArray.Count || index < 0)
        return false;
      var rateDate = tm.RatesArray[index].StartDate;
      return percentage >= 0 ? tl.EndDate >= rateDate : tl.EndDate <= rateDate;

    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegateHide TLHTCOk {
      get {
        Func<TradingMacro, IEnumerable<TL>[]> trendFlats = tm => new[] { tm.TrendLinesFlat/*.Permutation(3)*/.Reverse() };
        var ok = MonoidsCore.ToFunc((TradingMacro)null, (tm) =>
          from tlRed in tm.TrendLinesFlat.TakeLast(1)
          from tlBig in tm.TrendLinesFlat.SkipLast(1).OrderByDescending(tl => tl.EndDate).ThenByDescending(tl => tl.Count).Take(1)
          let isBigOutRed = tlBig.StartDate > tlRed.EndDate
          select new { tlBig, td = isBigOutRed && IsTLFresh(tm, tlRed) ? TradeDirections.Both : TradeDirections.None }
          );
        return () => TradingMacroTrender(tm => ok(tm))
        .SelectMany(x => x)
        .Do(x => {
          x.tlBig.RateMax.ForEach(max => BuyLevel.RateEx = max.AskHigh);
          x.tlBig.RateMin.ForEach(min => SellLevel.RateEx = min.BidLow);
        })
        .DefaultIfEmpty()
        .Select(x => x.td)
        .Aggregate((td1, td2) => td1 | td2);
      }
    }

    public TradeConditionDelegateHide TLH3Ok {
      get {
        Func<TradingMacro, IEnumerable<TL>[]> trendFlats = tm => new[] { tm.TrendLinesFlat.OrderByDescending(tl => tl.Count)/*.Permutation(3)*/.ToArray() };
        var ok = MonoidsCore.ToFunc((TradingMacro)null, (tm) => {
          var flats = tm.TrendLinesFlat;
          return from tlBig in flats.TakeLast(1)
                 from tlLast in flats.SkipLast(1).MaxByOrEmpty(tl => tl.EndDate)
                 let rateDate = tm.RatesArray[tm.RatesArray.Count - tlBig.Count / 2].StartDate
                 let bigTimeSpan = (tlBig.EndDate - tlBig.StartDate).TotalMinutes
                 let tlBigIsLast = tlBig.EndDate > tlLast.EndDate.AddMinutes(bigTimeSpan / 10)
                 let tlBigIsFresh = tlBig.EndDate > rateDate
                 select tlBigIsLast && tlBigIsFresh ? TradeDirections.Both : TradeDirections.None;
        });
        return () => TradingMacroTrender(tm => ok(tm))
        .SelectMany(x => x)
        .DefaultIfEmpty()
        .Aggregate((td1, td2) => td1 | td2);

      }
    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegateHide TLH4Ok {
      get {
        var ok = MonoidsCore.ToFunc((TradingMacro tm, TL flat1, TL flat2) => {
          var offset = flat2.TimeSpan.TotalMinutes.Max(flat1.TimeSpan.TotalMinutes) * 0.5;
          var dateRange = new[] { flat1.EndDate.AddMinutes(-offset), flat1.EndDate.AddMinutes(offset) };
          var isLatesFlat = tm.TrendLinesFlat.OrderByDescending(tl => tl.EndDate).Take(1).Any(tl => tl == flat2);
          var td = flat2.StDev > flat1.StDev &&
          IsTLFresh(tm, flat2) &&
          flat2.StartDate.Between(dateRange[0], dateRange[1])
          ? TradeDirections.Both
          : TradeDirections.None;
          return new { tl = flat2, td };
        });
        return () => TradingMacroTrender(tm => tm.TrendLinesFlat
        .CartesianProductSelf()
        .Select(flats => new { flats, tm }))
        .SelectMany(x => x.Select(y => ok(y.tm, y.flats[0], y.flats[1])))
        .Where(x => x.td.HasAny())
        .OrderByDescending(x => x.tl.EndDate)
        .Take(1)
        .Do(x => SetBSfromTL(x.tl))
        .Select(x => x.td)
        .DefaultIfEmpty()
        .Single();

      }
    }

    void SetBSfromTL(TL tl, bool isReversed = false) {
      var rateLevels = TradeLevelByTrendLine(tl).SelectMany(lbs => lbs.Select(lb => TradeLevelFuncs[lb]())).ToArray();
      BuyLevel.RateEx = rateLevels[isReversed ? 1 : 0];
      SellLevel.RateEx = rateLevels[isReversed ? 0 : 1];
    }
    void SetBSfromTLs(TL tl, IEnumerable<TL> tls, bool reverse) {
      var rateLevels = TradeLevelByTrendLine(tl).SelectMany(lbs => lbs.Select(lb => TradeLevelFuncs[lb]())).ToArray();
      var half = tls.Select(tlo => TradeLevelByTrendLine(tlo).SelectMany(lbs => lbs.Select(lb => TradeLevelFuncs[lb]())).ToArray())
        .OrderBy(mm => mm.Height())
        .TakeLast(1)
        .Concat()
        .ToArray()
        .Height() / 2;
      var mean = rateLevels.Average();
      var sign = reverse ? -1 : 1;
      BuyLevel.RateEx = mean + half * sign;
      SellLevel.RateEx = mean - half * sign;
    }
    void SetBSfromTL(Func<TL, double> buy, Func<TL, double> sell) {
      TrendLevelByTradeLevel()
        .IfEmpty(() => new Exception($"{nameof(TrendLevelByTradeLevel)} returned zero Trend Lines."))
        .ForEach(tl => {
          SellLevel.RateEx = sell(tl);
          BuyLevel.RateEx = buy(tl);
        });
    }

    public TradeConditionDelegate TLFOk {
      get {
        Func<TL, DateTime[]> tlDates = tl => (tl.TimeSpan.TotalMinutes / 10).With(t => new[] { tl.StartDate.AddMinutes(-t), tl.EndDate.AddMinutes(t) });
        return () => (from tm in TradingMacroTrender()
                      from tr in TradingMacroTrader()

                      where tm.TrendLinesTrendsAll.All(TL.NotEmpty)
                      let flats = tm.TrendLinesTrendsAll

                      from tlMin in flats.MinByOrEmpty(tl => tl.StartDate).Take(1)
                      where tlMin.StartDate > tr.LastTrade.TimeClose

                      from tlFirst in flats.Take(1)
                      where IsTLFresh(tm, tlFirst, 0.5)

                      from sd in flats.TakeLast(1).Select(tlLast => tlDates(tlLast))
                      select flats.SkipLast(1).Select(tlDates).All(se => se.Any(tld => tld.Between(sd[0], sd[1])))
                      )
                      .Where(ok => ok)
                      .Select(_ => TradeDirections.Both)
                      .DefaultIfEmpty()
                      .Aggregate((td1, td2) => td1 | td2);
      }
    }
    static TradeLevelBy[][] _levelBysForTrendLines = new[] {
        new[] { TradeLevelBy.LimeMax, TradeLevelBy.LimeMin},
        new[] { TradeLevelBy.GreenMax, TradeLevelBy.GreenMin },
        new[] { TradeLevelBy.PlumMax, TradeLevelBy.PlumMin },
        new[] { TradeLevelBy.RedMax, TradeLevelBy.RedMin },
        new[] { TradeLevelBy.BlueMax, TradeLevelBy.BlueMin }
      };

    private static Singleable<TradeLevelBy[]> TradeLevelByTrendLine(TL tl) {
      return _levelBysForTrendLines
        .Select((lbs, i) => new { i, ok = HasTradeLevelBy(lbs, tl.Color) })
        .SkipWhile(x => !x.ok)
        .Take(1)
        .Select(x => _levelBysForTrendLines[x.i])
        .AsSingleable();
    }

    private Singleable<TL> TrendLevelByTradeLevel() {
      var trends = new[] { TLLime, TLGreen, TLPlum, TLRed, TLBlue };
      return _levelBysForTrendLines
        .Select((lbs, i) => new { i, ok = HasTradeLevelBy(lbs) })
        .SkipWhile(x => !x.ok)
        .Take(1)
        .Select(x => trends[x.i])
        .AsSingleable();
    }
    private static bool HasTradeLevelBy(IList<TradeLevelBy> tradeLevelBys, string color) {
      var lbs = new[] { "Max", "Min" }.Select(lb => EnumUtils.Parse<TradeLevelBy>(color + lb));
      return HasTradeLevelBy(tradeLevelBys, lbs);
    }
    private bool HasTradeLevelBy(IList<TradeLevelBy> tradeLevelBys) {
      return HasTradeLevelBy(tradeLevelBys, new[] { LevelBuyBy, LevelSellBy });
    }
    private static bool HasTradeLevelBy(IList<TradeLevelBy> tradeLevelBys, IEnumerable<TradeLevelBy> levelBys) {
      return (from tl in tradeLevelBys
              join bs in levelBys on tl equals bs
              select bs).Count() == 2;
    }

    [TradeConditionCanSetCorridor]
    public TradeConditionDelegate TLF2Ok {
      get {
        TradingMacroTrader(tm => Log = new Exception(new { TLF2Ok = new { tm.WavesRsdPerc } } + ""));
        //Action<SuppRes> setBuy = (sr) => tl => sr.RateEx = tl.PriceAvg3;
        Func<SuppRes, Action<TL>> setSell = (sr) => tl => sr.RateEx = tl.PriceAvg2;
        return () => (from tm in TradingMacroTrender()
                      from tr in TradingMacroTrader()
                        // No empty TLs
                      let flats = tm.TrendLinesTrendsAll
                      // No recent trade
                      from tlMin in flats.SkipLast(1).OrderBy(tl => tl.StartDate).Take(1)
                      where tlMin.StartDate > tr.LastTrade.TimeClose
                      // All lined up forwards
                      where flats.SkipLast(1).Pairwise().All(t => t.Item1.EndDate > t.Item2.EndDate)
                      from tlSmall in flats.Take(1)
                        // First TL is fresh
                      where IsTLFresh(tm, tlSmall, WavesRsdPerc / 100.0)
                      // Small is outside Big
                      from bigMM in flats.TakeLast(1).SelectMany(tl => tl.PriceMin.Concat(tl.PriceMax).Pairwise())
                      where tlSmall.PriceMax.Concat(tlSmall.PriceMin).All(p => !p.Between(bigMM))

                      let td =
                        (bigMM.Map((min, _) => tlSmall.PriceMin.Any(p => p < min)) ? TradeDirections.Up : TradeDirections.None) |
                        (bigMM.Map((_, max) => tlSmall.PriceMax.Any(p => p > max)) ? TradeDirections.Down : TradeDirections.None)
                      select td
                      )
                      .AsSingleable()
                      //.Do(x => SetBSfromTL( tl => tl.PriceAvg3, tl => tl.PriceAvg2))
                      .LastOrDefault();
      }
    }

    [TradeConditionCanSetCorridor]
    public TradeConditionDelegate TLF3Ok {
      get {
        TradingMacroTrader(tm => Log = new Exception(new { TLF3Ok = new { tm.WavesRsdPerc } } + ""));
        //Action<SuppRes> setBuy = (sr) => tl => sr.RateEx = tl.PriceAvg3;
        Func<SuppRes, Action<TL>> setSell = (sr) => tl => sr.RateEx = tl.PriceAvg2;
        return () => (from tm in TradingMacroTrender()
                      from tr in TradingMacroTrader()
                        // No empty TLs
                      where tm.TrendLinesTrendsAll.All(TL.NotEmpty)
                      let flats = tm.TrendLinesTrendsAll
                      // No recent trade
                      //from tlMin in flats.SkipLast(1).OrderBy(tl => tl.StartDate).Take(1)
                      //where tlMin.StartDate > tr.LastTrade.TimeClose
                      // All lined up forwards
                      where flats.SkipLast(1).Pairwise().All(t => t.Item1.EndDate > t.Item2.EndDate)
                      // First TL is fresh
                      where flats.Take(1).Any(tl => IsTLFresh(tm, tl, WavesRsdPerc / 100.0))
                      from tlSmall in flats.Take(1)
                      from tlBig in flats.TakeLast(1)
                        // Small is outside Big
                      from bigMM in tlBig.PriceMin.Concat(tlBig.PriceMax).Pairwise((min, max) => new { min, max })
                      where !tlSmall.PriceMax.Concat(tlSmall.PriceMin).Average().Between(bigMM.min, bigMM.max)
                      // Lime is wider Blue
                      where tlSmall.PriceAvg2 - tlSmall.PriceAvg3 > tlBig.PriceAvg2 - tlBig.PriceAvg3

                      let td =
                        ((tlSmall.PriceMin.Any(p => p < bigMM.min)) ? TradeDirections.Up : TradeDirections.None) |
                        ((tlSmall.PriceMax.Any(p => p > bigMM.max)) ? TradeDirections.Down : TradeDirections.None)
                      select td
                      )
                      .AsSingleable()
                      //.Do(x => SetBSfromTL( tl => tl.PriceAvg3, tl => tl.PriceAvg2))
                      .LastOrDefault();
      }
    }
    #endregion

    #region All Forward
    public TradeConditionDelegate AFOk {
      get {
        Func<TradingMacro, IEnumerable<TL>> tlsForward = tm => tm.TrendLinesTrendsAll;
        return () => AllTLsForwardOk(tlsForward);
      }
    }
    public TradeConditionDelegate AFDOk {
      get {
        Func<TradingMacro, TL[]> tlsForward = tm => tm.TrendLinesTrendsAll;
        Func<TL[], TL[]> tlsFirstLast = tls => tls.MinMaxBy(tl => tl.StartDate.Ticks);
        Func<TL[], Func<TL, IEnumerable<double>>, Func<double, double, bool>, TradeDirections, IEnumerable<TradeDirections>> tdOk
          = (tls, getter, comp, td)
          => tls
          .Pairwise(TLMinMaxOk(getter, comp))
          .Concat()
          .Where(b => b)
          .Select(_ => td);
        Func<TL[], IEnumerable<TradeDirections>> tdOkMin = tls => tdOk(tls, tl => tl.PriceMin, (first, last) => first > last, TradeDirections.Up);
        Func<TL[], IEnumerable<TradeDirections>> tdOkMax = tls => tdOk(tls, tl => tl.PriceMax, (first, last) => first < last, TradeDirections.Down);
        Func<TradingMacro, IEnumerable<TradeDirections>> okTM = tm => tlsFirstLast(tlsForward(tm)).With(tls => tdOkMax(tls).Concat(tdOkMin(tls)));
        Func<IEnumerable<TradeDirections>> ok = () => TradingMacroTrender(okTM).Concat();
        return () => AllTLsForwardOk(tlsForward).HasAny()
        ? ok().Aggregate(TradeDirections.None, (a, td) => a | td)
        : TradeDirections.None;
      }
    }

    private static Func<TL, TL, IEnumerable<bool>> TLMinMaxOk(Func<TL, IEnumerable<double>> price, Func<double, double, bool> comp) {
      return (tl1, tl2) => TLMinMaxOk(tl1, tl2, price, comp);
    }
    private static IEnumerable<bool> TLMinMaxOk(TL tl1, TL tl2, Func<TL, IEnumerable<double>> price, Func<double, double, bool> comp) {
      return price(tl1).Concat(price(tl2)).Pairwise(comp);
    }

    private TradeDirections AllTLsForwardOk(Func<TradingMacro, IEnumerable<TL>> tlsForward) {
      return (from tm in TradingMacroTrender()
              where tm.TrendLinesTrendsAll.All(TL.NotEmpty)
              where TLsAllForward(tlsForward(tm))
              select TradeDirections.Both
                    )
                    .AsSingleable()
                    .LastOrDefault();
    }

    private static bool TLsAllForward(IEnumerable<TL> tls) {
      return tls.Pairwise().All(t => t.Item1.StartDate >= t.Item2.EndDate);
    }
    #endregion

    #endregion

    #region Edges
    public TradeConditionDelegate RPGOk {
      get {
        TradingMacroTrader(tm => Log = new Exception(new { TLF3Ok = new { tm.WavesRsdPerc } } + ""));
        Func<TL, DateTime> endDate = tl => tl.EndDate.AddMinutes(-tl.TimeSpan.TotalMinutes / 3);
        return () => (from tm in TradingMacroTrender()
                      from tr in TradingMacroTrader()
                      where TLPlum.StartDate > endDate(TLRed)
                      where TLGreen.StartDate > endDate(TLPlum)
                      select TradeDirections.Both
                      )
                      .AsSingleable()
                      .LastOrDefault();
      }
    }

    public TradeConditionDelegateHide EdgesOk {
      get {
        return () => {
          var edges = UseRates(rates => rates.Select(_priceAvg).ToArray().EdgeByStDev(InPoints(0.1), 0))
          .SelectMany(x => x.ToArray())
          .ToArray();
          CenterOfMassBuy = edges[0].Item1;
          CenterOfMassSell = edges.Last().Item1;
          return TradeDirections.Both;
        };
      }
    }
    class SetEdgeLinesAsyncBuffer :AsyncBuffer<SetEdgeLinesAsyncBuffer, Action> {
      public SetEdgeLinesAsyncBuffer() : base() {

      }
      protected override Action PushImpl(Action context) {
        return context;
      }
    }
    Lazy<SetEdgeLinesAsyncBuffer> _setEdgeLinesAsyncBuffer = Lazy.Create(() => new SetEdgeLinesAsyncBuffer());

    public TradeConditionDelegateHide EdgesAOk {
      get {
        return () => {
          return UseRates(rates => rates.Select(_priceAvg).ToArray())
          .Select(rates => {
            _setEdgeLinesAsyncBuffer.Value.Push(() => SetAvgLines(rates));
            return TradeDirections.Both;
          })
          .SingleOrDefault();
        };
      }
    }

    private void SetAvgLines(IList<double> rates) {
      var edges = rates.EdgeByAverage(InPoints(1)).ToArray();
      var minLevel1 = edges[0].Item1;
      Func<Func<double, bool>, IEnumerable<Tuple<double, double>>> superEdge = predicate => rates
         .Where(predicate).ToArray()
         .EdgeByAverage(InPoints(0.1));
      AvgLineMax = superEdge(e => e > minLevel1).First().Item1;
      AvgLineMin = superEdge(e => e < minLevel1).First().Item1;
      AvgLineAvg = edges[0].Item2;
      var fibs = Fibonacci.Levels(AvgLineMax, AvgLineMin);
      CenterOfMassBuy = fibs.Skip(1).First();
      CenterOfMassSell = fibs.TakeLast(2).First();
    }
    #endregion

    #region Wave Conditions
    int _bigWaveIndex = 0;
    [Category(categoryTrading)]
    public int BigWaveIndex {
      get {
        return _bigWaveIndex;
      }

      set {
        if(value < 0)
          throw new Exception("BigWaveIndex must bre >= 0");
        _bigWaveIndex = value;
        OnPropertyChanged(() => BigWaveIndex);
      }
    }
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide BigWaveOk_Old {
      get {
        var ors = new Func<WaveRange, double>[] { wr => wr.DistanceCma };
        Func<WaveRange, TradingMacro, bool> predicate = (wr, tm) =>
          //wr.Angle.Abs().ToInt() >= tm.WaveRangeAvg.Angle.ToInt() && 
          ors.Any(or => or(wr) >= or(tm.WaveRangeAvg));
        return () => {
          var twoWavess = TradingMacroOther().SelectMany(tm => tm.WaveRanges)
            .Take(BigWaveIndex + 1)
            .Buffer(BigWaveIndex + 1)
            .Where(b => b.SkipLast(1).DefaultIfEmpty(b.Last()).Max(w => w.DistanceCma) <= b.Last().DistanceCma);
          var x = twoWavess.Select(_ => TradeDirections.Both).DefaultIfEmpty(TradeDirections.None).Single();
          return IsWaveOk(predicate, BigWaveIndex) & x;
        };
      }
    }

    public TradeConditionDelegate InWaveOk {
      get {
        return () => IsWaveOk2((wr, tm) => RatesArray[0].StartDate < wr.StartDate, 1);
      }
    }
    public TradeConditionDelegate BigWaveOk {
      get {
        var ok = MonoidsCore.ToFunc(() =>
          TradingMacroM1(tm => {
            var waveFirst = tm.WaveRangesWithTail.Take(1);
            var wavesUp = tm.WaveRanges.Skip(1).OrderByDescending(wr => wr.Max).Take(1).ToArray();
            var wavesDown = tm.WaveRanges.Skip(1).OrderBy(wr => wr.Min).Take(2).ToArray();
            Func<IList<Rate>, Func<Rate, double>, double> aoMax = (rates, getVolt) => rates.Max(getVolt);
            Func<IList<Rate>, Func<Rate, double>, double> aoMin = (rates, getVolt) => rates.Min(getVolt);
            Func<WaveRange, WaveRange, bool> slopeOk = (wr1, wr2) => wr1.Slope.Sign() == wr2.Slope.Sign();
            Func<WaveRange, Func<IList<double>, bool>, bool> aoDown = (wr, cmp) => wr.Range.BackwardsIterator(GetVoltage).SkipWhile(Lib.IsNaN).Buffer(2).Take(1).Any(cmp);
            var upOk = waveFirst.Zip(wavesUp, (f, u) => aoDown(f, b => b[0] < b[1]) && slopeOk(f, u) && f.Max > u.Max
              && aoMax(f.Range, GetVoltage) < aoMax(u.Range, GetVoltage)
              && aoMax(f.Range, GetVoltage2) < aoMax(u.Range, GetVoltage2)
              ).Any(b => b);
            var downOk = waveFirst.Zip(wavesDown, (f, d) => aoDown(f, b => b[1] < b[0]) && slopeOk(f, d) && f.Min < d.Min
              && aoMin(f.Range, GetVoltage) > aoMin(d.Range, GetVoltage)
              && aoMin(f.Range, GetVoltage2) > aoMin(d.Range, GetVoltage2)
              ).Any(b => b);
            return new { upOk, downOk };
          }));
        return () => ok().Select(x => x.upOk ? TradeDirections.Down : x.downOk ? TradeDirections.Up : TradeDirections.None).SingleOrDefault();
      }
    }
    public TradeConditionDelegateHide CalmOk {
      get {
        return () => AreWavesOk((wr, tm) =>
        wr.Distance < tm.WaveRangeAvg.Distance && wr.TotalMinutes < tm.WaveRangeAvg.TotalMinutes, 0, BigWaveIndex);
      }
    }
    public TradeConditionDelegateHide Calm2Ok {
      get {
        return () => (
          from calm in new[] { CalmOk() }
          where calm.HasAny()
          from tm in TradingMacroOther().Take(1)
          where tm.WaveRangeSum != null
          let wrs = tm.WaveRanges.SkipWhile(wr => wr.IsEmpty)
          let sum = wrs.Take(BigWaveIndex).Sum(wr => wr.Distance)
          let distMin = tm.WaveRangeSum.Distance
          select calm & TradeDirectionByBool(sum >= distMin))
          .SingleOrDefault();
        //.Take(BigWaveIndex);
      }
    }
    [TradeConditionAsleep]
    public TradeConditionDelegateHide Calm3Ok {
      get {
        return () => (
          from tm in TradingMacroOther().Take(1)
          where tm.WaveRangeSum != null
          let wrSum = tm.WaveRangeSum
          let wrs = tm.WaveRanges.SkipWhile(wr => wr.IsEmpty).Take(BigWaveIndex)
          let distOk = wrs.All(wr => wr.Distance < wrSum.Distance)
          let distSum = wrs.Select(wr => wr.Distance).DefaultIfEmpty().Sum()
          let ppmAvg = wrs.Select(wr => wr.PipsPerMinute).DefaultIfEmpty().Average()
          select TradeDirectionByBool(distOk && distSum >= wrSum.Distance && ppmAvg >= wrSum.PipsPerMinute))
          .SingleOrDefault();
        //.Take(BigWaveIndex);
      }
    }
    [TradeConditionAsleep]
    public TradeConditionDelegateHide Calm4Ok {
      get {
        return () => Calm3Impl((wr, tm) => wr.Distance > tm.WaveRangeAvg.Distance);
      }
    }
    public TradeDirections Calm3Impl(Func<WaveRange, TradingMacro, bool> condDist) {
      return (
        from tm in TradingMacroOther()
        where tm.WaveRangeSum != null
        let wrSum = tm.WaveRangeSum
        let wrs = tm.WaveRanges.SkipWhile(wr => wr.IsEmpty).Take(BigWaveIndex)
        let distOk = wrs.All(wr => condDist(wr, tm))
        let distSum = wrs.Select(wr => wr.Distance).DefaultIfEmpty().Sum()
        let ppmAvg = wrs.Select(wr => wr.PipsPerMinute).DefaultIfEmpty().Average()
        select TradeDirectionByBool(distOk && distSum >= wrSum.Distance && ppmAvg >= wrSum.PipsPerMinute))
        .FirstOrDefault();
      //.Take(BigWaveIndex);
    }
    [TradeConditionAsleep]
    public TradeConditionDelegateHide WvDistAOk {
      get {
        return () => TradeDirectionByBool(CalmImpl(
          (wrs, tm) => wrs.Sum(wr => wr.Distance) > tm.WaveRangeSum.Distance,
          (wr, tm) => wr.Distance < tm.WaveRangeAvg.Distance));
      }
    }
    [TradeConditionAsleep]
    public TradeConditionDelegateHide WvDistSOk {
      get {
        return () => TradeDirectionByBool(CalmImpl(
          (wrs, tm) => wrs.Sum(wr => wr.Distance) > tm.WaveRangeSum.Distance,
          (wr, tm) => wr.Distance < tm.WaveRangeSum.Distance));
      }
    }
    [TradeConditionAsleep]
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide WvAngAOk {
      get {
        return () => TradeDirectionByBool(
          TradingMacroOther().Take(1).SelectMany(tm =>
            tm.WaveRanges.Take(2).Where(wr => wr.Angle.Abs() > tm.WaveRangeAvg.Angle * 3)).IsEmpty());
      }
    }
    [TradeConditionAsleep]
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide WvSDOk {
      get {
        Func<bool> ok = () => (
         from tm in TradingMacroOther().Take(1)
         where tm.WaveRangeAvg != null && tm.WaveRangeSum != null
         let wr = tm.WaveRanges.SkipWhile(wr => wr.IsEmpty).ToList()
         let b = wr.Buffer(wr.Count / 2).ToList()
         where b.Count == 2
         select b[0].Average(w => w.PipsPerMinute) >= b[1].Average(w => w.PipsPerMinute)
         ).SingleOrDefault();
        return () => TradeDirectionByBool(ok());
      }
    }
    public bool CalmImpl(params Func<WaveRange, TradingMacro, bool>[] conds) {
      return CalmImpl(BigWaveIndex, conds);
    }
    public bool CalmImpl(int wavesCount, params Func<WaveRange, TradingMacro, bool>[] conds) {
      return (
        from tm in TradingMacroOther().Take(1)
        where tm.WaveRangeAvg != null && tm.WaveRangeSum != null
        from wr in tm.WaveRanges.SkipWhile(wr => wr.IsEmpty).Take(wavesCount)
        from cond in conds
        select cond(wr, tm)
        )
        .All(b => b);
    }
    public bool CalmImpl(Func<IEnumerable<WaveRange>, TradingMacro, bool> condTM, params Func<WaveRange, TradingMacro, bool>[] conds) {
      return CalmImpl(condTM, BigWaveIndex, conds);
    }
    public bool CalmImpl(Func<IEnumerable<WaveRange>, TradingMacro, bool> condTM, int wavesCount, params Func<WaveRange, TradingMacro, bool>[] conds) {
      var waves = (
        from tm in TradingMacroOther().Take(1)
        where tm.WaveRangeAvg != null && tm.WaveRangeSum != null
        from wr in tm.WaveRanges.SkipWhile(wr => wr.IsEmpty)
        from cond in conds
        where cond(wr, tm)
        select new { wr, tm }
        ).ToList();
      var tmRes = waves.Select(x => x.tm).Take(1).Select(tm2 => condTM(waves.Select(x => x.wr), tm2));
      return waves.Count >= wavesCount && tmRes.SingleOrDefault();
    }
    private TradeDirections IsWaveOk(Func<WaveRange, TradingMacro, bool> predicate, int index) {
      return TradingMacroOther()
      .Take(1)
      .SelectMany(tm => tm.WaveRanges
      .SkipWhile(wr => wr.IsEmpty)
      .Skip(index)
      .Take(1)
      .Where(wr => predicate(wr, tm))
      .Select(wr => wr.Slope > 0 ? TradeDirections.Down : TradeDirections.Up)
      )
      .DefaultIfEmpty(TradeDirections.None)
      .Single();
    }
    private TradeDirections IsWaveOk2(Func<WaveRange, TradingMacro, bool> predicate, int index) {
      return TradingMacroOther()
      .Take(1)
      .SelectMany(tm => tm.WaveRanges
      .SkipWhile(wr => wr.IsEmpty)
      .Skip(index)
      .Take(1)
      .Where(wr => predicate(wr, tm))
      .Select(wr => TradeDirections.Both)
      )
      .DefaultIfEmpty(TradeDirections.None)
      .Single();
    }
    private TradeDirections IsWaveOk(Func<WaveRange, TradingMacro, bool> predicate, int index, int count) {
      return TradingMacroOther()
      .Take(1)
      .SelectMany(tm => tm.WaveRanges
      .SkipWhile(wr => wr.IsEmpty)
      .Skip(index)
      .Take(count)
      .Where(wr => predicate(wr, tm))
      .Select(wr => TradeDirections.Both)
      )
      .DefaultIfEmpty(TradeDirections.None)
      .First();
    }
    private TradeDirections AreWavesOk(Func<WaveRange, TradingMacro, bool> predicate, int index, int count) {
      return TradingMacroOther()
      .Take(1)
      .Select(tm => tm.WaveRanges
      .SkipWhile(wr => wr.IsEmpty)
      .Skip(index)
      .Take(count)
      .All(wr => predicate(wr, tm))
      )
      .Select(ok => TradeDirectionByBool(ok))
      .DefaultIfEmpty(TradeDirections.None)
      .First();
    }
    #endregion

    #region Trade Corridor and Directions conditions
    #region Tip COnditions

    [TradeConditionTurnOff]
    public TradeConditionDelegate TipOk {
      get {
        Func<TradingMacro, double> tipRatioTres = tm => tm.TipRatio;
        TradingMacroTrader(tm => Log = new Exception(new { TipOk = new { tm.TipRatio } } + ""));
        Func<TradingMacro, double, Func<double>, TradeDirections> ok = (tm, tradeLevel, ratesMM) => {
          var tip = (ratesMM() - tradeLevel).Abs();
          var tipRatio = tip / tm.RatesHeight;
          return tipRatio <= tipRatioTres(tm)
            ? TradeDirections.Both
            : TradeDirections.None;
        };
        var ratesMinMax = MonoidsCore.ToFunc((TradingMacro tm) => new Func<double>[] { () => tm.RatesMax, () => tm.RatesMin });
        Func<TradingMacro, IEnumerable<double>> tradeLevels = tm
          => new[] {
            tm.TrendLinesFlat.SelectMany(tl => tl.PriceMax).AverageOrNaN(),
            tm.TrendLinesFlat.SelectMany(tl => tl.PriceMin).AverageOrNaN() };
        Func<TradeDirections> ok2 = () => (from tm in TradingMacroTrender()
                                           from bs in tradeLevels(tm)
                                           from mm in ratesMinMax(tm)
                                           select new { bs, mm, tm }
                                           ).Aggregate(TradeDirections.None, (td, a) => td | ok(a.tm, a.bs, a.mm));
        return () => ok2();
      }
    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegateHide TipFlatOk {
      get {
        Func<TradingMacro, double> tipRatioTres = tm => tm.TipRatio;
        TradingMacroTrader(tm => Log = new Exception(new { TipOk = new { tm.TipRatio } } + ""));
        Func<TradingMacro, double, Func<double>, TradeDirections> ok = (tm, tradeLevel, extream) => {
          var tip = (extream() - tradeLevel).Abs();
          var tipRatio = tip / tm.RatesHeight;
          return tipRatio <= tipRatioTres(tm)
            ? TradeDirections.Both
            : TradeDirections.None;
        };
        var extreams = MonoidsCore.ToFunc((TradingMacro tm) => new Func<double>[] { () => tm.RatesMax, () => tm.RatesMin });
        var tradeLevels = MonoidsCore.ToFunc((TradingMacro)null, (tm) => {
          var flatses = tm.TrendLinesFlat.OrderByDescending(tl => tl.Count).Permutation(3);
          return flatses.Select(flats => new[] { new { flats, avg = flats.SelectMany(tl => tl.PriceMax).DefaultIfEmpty(double.NaN).Average() }, new { flats, avg = flats.SelectMany(tl => tl.PriceMin).DefaultIfEmpty(double.NaN).Average() } });
        });

        Func<TradeDirections> ok2 = () => (from tm in TradingMacroTrender()
                                           from bss in tradeLevels(tm)
                                           from bs in bss
                                           from ex in extreams(tm)
                                           select new { bs.flats, td = ok(tm, bs.avg, ex) }
                                           ).Where(x => x.td.HasAny())
                                           .TakeLast(1)
                                           .Do(x => {
                                             var useLast = GetTradeLevelsPreset().Any(tlp => tlp == TradeLevelsPreset.Lime);
                                             var flat = x.flats.FirstOrLast(useLast).Single();
                                             flat.PriceMax.ForEach(r => BuyLevel.RateEx = r);
                                             flat.PriceMin.ForEach(r => SellLevel.RateEx = r);
                                           })
                                           .Select(x => x.td)
                                           .LastOrDefault();
        return () => ok2();
      }
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegateHide Tip2Ok {
      get {
        return () => {
          _tipRatioCurrent = RatesHeightCma / BuyLevel.Rate.Abs(SellLevel.Rate);
          var td = IsTresholdAbsOk(_tipRatioCurrent, TipRatio)
            ? TradeDirections.Both
            : TradeDirections.None;
          var bsl = new[] { BuyLevel, SellLevel };
          if(/*bsl.Any(sr => sr.CanTrade) && */Trades.Length == 0)
            SetTradeCorridorToMinHeight();
          return td;
        };
      }
    }
    public void SetTradeCorridorToMinHeight() {
      if(CanTriggerTradeDirection()) {
        var bsl = new[] { BuyLevel, SellLevel };
        var buy = GetTradeLevel(true, double.NaN);
        var sell = GetTradeLevel(false, double.NaN);
        var zip = bsl.Zip(new[] { buy, sell }, (sr, bs) => new { sr, bs });
        zip.Where(x => !x.sr.InManual)
          .ForEach(x => {
            x.sr.InManual = true;
            x.sr.ResetPricePosition();
            x.sr.Rate = x.bs;
          });
        var bsHeight = BuyLevel.Rate.Abs(SellLevel.Rate);
        var tlHeight = buy.Abs(sell);
        var tlAvg = buy.Avg(sell);
        //var currentPrice = new[] { CurrentEnterPrice(true), CurrentEnterPrice(false) };
        var canSetLevel = (!tlAvg.Between(SellLevel.Rate, BuyLevel.Rate) || tlHeight < bsHeight);
        if(canSetLevel)
          lock(_rateLocker) {
            zip.ForEach(x => {
              var rate = x.bs;
              var rateJump = InPips(rate.Abs(x.sr.Rate));
              var reset = rateJump > 1;
              if(reset)
                x.sr.ResetPricePosition();
              x.sr.Rate = rate;
              if(reset)
                x.sr.ResetPricePosition();
            });
          }
      }
    }

    public TradeConditionDelegateHide Tip3Ok {
      get {
        return () => {
          var bsl = new[] { BuyLevel, SellLevel };
          if(!HaveTrades())
            SetTradeCorridorToMinHeight2();
          _tipRatioCurrent = RatesHeightCma / BuyLevel.Rate.Abs(SellLevel.Rate);
          return TradeDirectionByBool(IsCurrentPriceInsideTradeLevels && IsTresholdAbsOk(_tipRatioCurrent, TipRatio));
        };
      }
    }

    #region BSTip
    public TradeConditionDelegate BSTipOk {
      get {
        var ok = BSTipImpl(r => r);
        return () => ok();
      }
    }
    public TradeConditionDelegate BSTipROk {
      get {
        var ok = BSTipImpl(r => !r);
        return () => ok();
      }
    }
    public TradeConditionDelegate BSTipNOk {
      get {
        var ok = BSTipNImpl();
        return () => ok();
      }
    }

    private TradeConditionDelegate BSTipImpl(Func<bool, bool> order) {
      Func<TradingMacro, double> tipRatioTres = tm => tm.TipRatio;
      TradingMacroTrader(tm => Log = new Exception(new { TipOk = new { tm.TipRatio } } + ""));
      Func<TradingMacro, Tuple<double, SuppRes>, bool> isTreshOk = (tm, t) => IsTresholdAbsOk(_tipRatioCurrent = t.Item1, tipRatioTres(tm));
      return () =>
        (from tm in TradingMacroTrender()
         from t in BSTipOverlap(tm)
         where isTreshOk(tm, t)
         select tm.TipRatio > 0 ? TradeDirections.Both : order(t.Item2.IsBuy) ? TradeDirections.Up : TradeDirections.Down
        )
        .SingleOrDefault();
    }
    private TradeConditionDelegate BSTipNImpl() {
      Func<TradingMacro, double> tipRatioTres = tm => tm.TipRatio;
      TradingMacroTrader(tm => Log = new Exception(new { TipOk = new { tm.TipRatio } } + ""));
      Func<TradingMacro, Tuple<double, SuppRes>, bool> isTreshOk = (tm, t) => IsTresholdAbsOk(_tipRatioCurrent = t.Item1, tipRatioTres(tm));
      return () =>
        (from tm in TradingMacroTrender()
         from t in BSTipNOverlap(tm)
         where isTreshOk(tm, t)
         select t.Item2.IsBuy ? TradeDirections.Up : TradeDirections.Down
        )
        .Scan((p, n) => p | n)
        .Where(td => td == TradeDirections.Both)
        .DefaultIfEmpty()
        .Last();
    }
    private IEnumerable<Tuple<double, SuppRes>> BSTipNOverlap(TradingMacro tmTrender) {
      return (from bs in BuySellLevels//.Select(x => x.Rate)
              let rmm = new[] { tmTrender.RatesMin, tmTrender.RatesMax }
              from rm in rmm
              let bsrm = new[] { bs.Rate, rm }
              let r = bsrm.OverlapRatio(rmm)
              orderby r
              select Tuple.Create(r, bs)
              );
    }

    private Singleable<Tuple<double, SuppRes>> BSTipOverlap(TradingMacro tmTrender) {
      return (from bs in BuySellLevels//.Select(x => x.Rate)
              let rmm = new[] { tmTrender.RatesMin, tmTrender.RatesMax }
              from rm in rmm
              let bsrm = new[] { bs.Rate, rm }
              let r = bsrm.OverlapRatio(rmm)
              orderby r
              select Tuple.Create(r, bs)
              )
              .Take(1)
              .AsSingleable();
    }
    #endregion

    #region Properties
    [Category(categoryCorridor)]
    public bool ResetTradeStrip {
      get { return false; }
      set {
        if(value)
          CenterOfMassSell = CenterOfMassBuy = CenterOfMassSell2 = CenterOfMassBuy2 = double.NaN;
      }
    }
    #endregion

    private void CanTrade_TurnOffByAngle(TL tl) {
      if(tl.Slope < 0 && SellLevel.CanTrade)
        SellLevel.CanTrade = false;
      if(tl.Slope > 0 && BuyLevel.CanTrade)
        BuyLevel.CanTrade = false;
    }

    private double TradeLevelsEdgeRatio(double[] tradeLevles) {
      return TLBlue
        .YieldIf(p => !p.IsEmpty, p => p.Slope.SignUp())
        .Select(ss => {
          var tradeLevel = ss > 0 ? tradeLevles.Min() : tradeLevles.Max();
          var extream = ss > 0 ? _RatesMax : _RatesMin;
          var tip = extream.Abs(tradeLevel);
          return RatesHeight / tip;
        })
        .DefaultIfEmpty(double.NaN)
        .Single();
    }

    double _tipRatio = 4;
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsTradingParams)]
    public double TipRatio {
      get { return _tipRatio; }
      set {
        _tipRatio = value;
        OnPropertyChanged(() => TipRatio);
      }
    }

    #endregion

    #region After Tip

    public TradeConditionDelegate PigTailOk {
      get {
        Func<bool> ok = () => (
            from tm in TradingMacroTrender().AsSingleable()
            from tl in TradeTrendLines.AsSingleable()
            let ed = tl.EndDate
            let offset = tl.PriceAvg3.Abs(tl.PriceAvg2) * 0.1
            from r in RatesArray.BackwardsIterator().TakeWhile(r0 => r0.StartDate > ed)
            select r.PriceAvg.Between(tl.PriceAvg3 - offset, tl.PriceAvg2 + offset)
            ).All(b => b);
        return () => TradeDirectionByBool(ok());
      }
    }

    private void TradeConditionsRemoveExcept<T>(TradeConditionDelegate except) where T : Attribute {
      TradeConditionsRemove(TradeConditionsInfo<T>().Where(x => x != except).ToArray());
    }
    private void TradeConditionsRemove(params TradeConditionDelegate[] dels) {
      dels.ForEach(d => TradeConditions.Where(tc => tc.Item1 == d).ToList().ForEach(t => {
        Log = new Exception(new { d, was = "Removed" } + "");
        TradeConditions.Remove(t);
      }));
    }

    public TradeConditionDelegateHide IsInOk {
      get {
        return () => TradeDirectionByBool(IsCurrentPriceInsideTradeLevels);
      }
    }
    bool _isIn2Ok;
    public TradeConditionDelegate IsIn2Ok {
      get {
        return () => BuySellLevels.IfAnyCanTrade()
        .Select(_ => _isIn2Ok)
        .Where(b => b)
        .DefaultIfEmpty(IsCurrentPriceInsideTradeLevels2)
        .Select(b => TradeDirectionByBool(_isIn2Ok = b))
        .Single();
      }
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegateHide AnglesOk {
      get {
        Func<Func<TL, bool>, Func<TL, TL, bool>, TradeDirections, TradeDirections> aok = (signPredicate, slopeCompare, success) => {
          var cUp = TrendLinesTrendsAll.OrderByDescending(tl => tl.Count)
            .Where(signPredicate)
            .Scan((tlp, tln) => slopeCompare(tlp, tln) ? tln : Rate.TrendLevels.Empty)
            .TakeWhile(tl => !tl.IsEmpty)
            .Count();
          return cUp == TrendLinesTrendsAll.Length - 1 ? success : TradeDirections.None;
        };
        var up = MonoidsCore.ToFunc(() => aok(tl => tl.Slope > 0, (s1, s2) => s1.Slope < s2.Slope, TradeDirections.Down));
        var down = MonoidsCore.ToFunc(() => aok(tl => tl.Slope < 0, (s1, s2) => s1.Slope > s2.Slope, TradeDirections.Up));
        return () => up() | down();
      }
    }

    public TradeConditionDelegateHide UniAngleOk {
      get {
        Func<TradingMacro, IEnumerable<double>> signs = tm => tm.TrendLinesTrendsAll.Where(TL.NotEmpty).Select(tl => tl.Slope).Scan((a1, a2) => a1.SignUp() == a2.SignUp() ? a2 : double.NaN);
        return () => TradingMacroTrender(signs).SelectMany(x => x)
        .SkipWhile(Lib.IsNotNaN)
        .Any()
        ? TradeDirections.None
        : TradeDirections.Both;
      }
    }
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide HangOk {
      get {
        Func<Func<TL, bool>, Func<TL, TL, bool>, TradeDirections> aok = (signPredicate, slopeCompare) => {
          var cUp = TrendLinesTrendsAll
            .OrderByDescending(tl => tl.Count)
            .Take(3)
            .Where(signPredicate)
            .Scan((tlp, tln) => slopeCompare(tlp, tln) ? tln : Rate.TrendLevels.Empty)
            .TakeWhile(tl => !tl.IsEmpty)
            .Count();
          return cUp == 2 ? TradeDirections.Both : TradeDirections.None;
        };
        var up = MonoidsCore.ToFunc(() => aok(tl => tl.Slope > 0, (s1, s2) => s1.Slope > s2.Slope));
        var down = MonoidsCore.ToFunc(() => aok(tl => tl.Slope < 0, (s1, s2) => s1.Slope < s2.Slope));
        return () => up() | down();
      }
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegateHide GayGreenOk {
      get {
        Func<IEnumerable<double[]>> greenLeft = () => {
          var greenIndex = TLGreen.Count - TLLime.Count;
          var greenRangeMiddle = TLGreen.Coeffs.RegressionValue(greenIndex);
          var greenRangeHeight = TLGreen.PriceAvg2 - TLGreen.PriceAvg1;
          return new[] { new[] { greenRangeMiddle + greenRangeHeight, greenRangeMiddle - greenRangeHeight } };
        };
        Func<Lazy<IList<Rate>>, IEnumerable<double[]>> ranges = trends => trends.Value.Skip(1).Select(tl => new[] { tl.Trends.PriceAvg2, tl.Trends.PriceAvg3 });
        Func<double[], double[], bool> testInside = (outer, inner) => inner.All(i => i.Between(outer[0], outer[1]));
        Func<Lazy<IList<Rate>>, Lazy<IList<Rate>>, IEnumerable<bool>> isInside = (outer, inner) =>
          ranges(outer).Zip(ranges(inner), testInside);
        return () => TradeDirectionByBool(isInside(TrendLines1, TrendLines0).Min());
      }
    }

    bool IsFathessOk(TradingMacro tm) { return tm.WaveRanges.Take(1).Any(wr => Angle.Sign() == wr.Slope.Sign() && wr.IsFatnessOk); }
    bool IsDistanceCmaOk(TradingMacro tm) {
      return tm.WaveRanges.Take(1).Any(wr => TLBlue.Slope.Sign() == wr.Slope.Sign() && wr.DistanceCma < StDevByPriceAvgInPips);
    }

    #endregion

    public TradeConditionDelegate TrdCorChgOk {
      get {
        return () => {
          return TradeDirectionByBool(BuySellLevels.HasTradeCorridorChanged());
        };
      }
    }
    public TradeConditionDelegateHide TCCrsOk {
      get {
        return () => {
          var ok = MonoidsCore.ToFunc(() =>
             (from startDate in TrendLevelByTradeLevel().Select(tl => tl.StartDate)
              let rates = RatesArray.GetRange(startDate, DateTime.MaxValue, r => r.StartDate)
              let bsMinMax = BuySellLevels.Select(bs => bs.Rate).OrderBy(d => d).ToArray()
              let hasUp = rates.Any(r => r.PriceAvg > bsMinMax[1])
              let hasDown = rates.Any(r => r.PriceAvg < bsMinMax[0])
              select hasUp || hasDown
              ).DefaultIfEmpty()
              .AsSingleable()
          );
          return TradeDirectionByBool(ok().SingleOrDefault());
        };
      }
    }
    #region Helpers
    public SuppRes[] BuySellLevels { get { return new[] { BuyLevel, SellLevel }; } }
    void BuySellLevelsForEach(Action<SuppRes> action) {
      BuySellLevels.ForEach(action);
    }
    void BuySellLevelsForEach(Func<SuppRes, bool> predicate, Action<SuppRes> action) {
      BuySellLevels.Where(predicate).ForEach(action);
    }

    public void SetTradeCorridorToMinHeight2() {
      if(CanTriggerTradeDirection()) {
        var bsl = new[] { BuyLevel, SellLevel };
        var buy = GetTradeLevel(true, double.NaN);
        var sell = GetTradeLevel(false, double.NaN);
        var zip = bsl.Zip(new[] { buy, sell }, (sr, bs) => new { sr, bs });
        zip.Where(x => !x.sr.InManual)
          .ForEach(x => {
            x.sr.InManual = true;
            x.sr.ResetPricePosition();
            x.sr.Rate = x.bs;
          });
        var bsHeight = BuyLevel.Rate.Abs(SellLevel.Rate);
        var tlHeight = buy.Abs(sell);
        var tlAvg = buy.Avg(sell);
        var tlJumped = !tlAvg.Between(SellLevel.Rate, BuyLevel.Rate) && tlHeight.Div(bsHeight) > 1.5;
        if(tlJumped)
          bsl.ForEach(sr => sr.CanTrade = false);
        var canSetLevel = (tlJumped || tlHeight < bsHeight);
        if(canSetLevel)
          lock(_rateLocker) {
            zip.ForEach(x => {
              var rate = x.bs;
              var rateJump = InPips(rate.Abs(x.sr.Rate));
              var reset = rateJump > 1;
              if(reset)
                x.sr.ResetPricePosition();
              x.sr.Rate = rate;
              if(reset)
                x.sr.ResetPricePosition();
            });
          }
      }
    }

    #endregion
    #endregion


    [TradeConditionTurnOff]
    public TradeConditionDelegate ExprdOk {
      get {
        return () =>
          (from tl in TrendLinesTrendsAll.Take(1)
           select TradeDirectionByBool(BuySellLevels.IfAnyCanTrade().IsEmpty() || !IsCanTradeExpired(tl.TimeSpan))
          ).SingleOrDefault();
      }
    }


    #region StDev
    public TradeConditionDelegate M1SDROk {
      get { return () => TradeDirectionByBool((from sd in M1SD from sda in M1SDA select sd < sda).SingleOrDefault()); }
    }
    public TradeConditionDelegateHide VLOk {
      get {
        //Log = new Exception(new { System.Reflection.MethodBase.GetCurrentMethod().Name, TrendHeightPerc } + "");
        return () => { return TradeDirectionByTreshold(GetVoltageAverage(), TrendHeightPerc); };
      }
    }


    [TradeConditionTurnOff]
    public TradeConditionDelegateHide RhSDAvgOk {
      get { return () => TradeDirectionByBool(_macd2Rsd >= MacdRsdAvg); }
    }

    #region Volts
    [TradeConditionTurnOff]
    public TradeConditionDelegate VoltBelowTOOk { get { return () => VoltBelowOk(); } }
    public TradeConditionDelegate VoltBelowOk {
      get {
        return () => {
          return GetLastVolt(volt => volt < GetVoltageAverage()).Select(TradeDirectionByBool).SingleOrDefault();
        };
      }
    }

    private TradeDirections VoltsBelowByTrendLines(TL tls) {
      var d = RatesArray[RatesArray.Count - tls.Count].StartDate;
      var volt = _SetVoltsByStd.SkipWhile(t => t.Item1 < d).Select(t => t.Item2).DefaultIfEmpty(double.NaN).Min();
      return TradeDirectionByBool(volt < GetVoltageAverage());
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegate VoltAboveOk {
      get {
        return () => {
          return GetLastVolt(volt => volt > GetVoltageHigh()).Select(TradeDirectionByBool).SingleOrDefault();
        };
      }
    }
    TradeDirections _VROk;
    public TradeConditionDelegate VROk {
      get {
        //BuySellLevels.IfAnyCanTrade()
        return () => _VROk = BuySellLevels.IfAnyCanTrade().Select(_ => _VROk).Concat(VltRngImpl).FirstOrDefault();
      }
    }
    public TradeConditionDelegate VltRngOk {
      get {
        return () => VltRngImpl().SingleOrDefault();
      }
    }
    IEnumerable<TradeDirections> VltRngImpl() {
      return GetLastVolt(volt =>
          VoltRange1.IsNaN()
          ? TradeDirectionByTreshold(volt, VoltRange0)
          : VoltRange0 < VoltRange1
          ? TradeDirectionByBool(volt.Between(VoltRange0, VoltRange1))
          : TradeDirectionByBool(!volt.Between(VoltRange1, VoltRange0))
        );
    }
    public TradeConditionDelegate VltRng2Ok {
      get {
        return () => GetLastVolt(GetVoltage2)
        .Select(volt =>
          VoltRange_21.IsNaN()
          ? TradeDirectionByTreshold(volt, VoltRange_20)
          : VoltRange_20 < VoltRange_21
          ? TradeDirectionByBool(volt.Between(VoltRange_20, VoltRange_21))
          : TradeDirectionByBool(!volt.Between(VoltRange_21, VoltRange_20))
        ).SingleOrDefault();
      }
    }
    public TradeConditionDelegate VltAvgOk => () => TradeDirectionByTreshold(GetVoltageHigh(), VoltAvgRange);
    public TradeConditionDelegate VltUpOk {
      get {
        return () => TradeDirectionByBool(VoltOkBySlope(s => s < 0));
      }
    }

    private bool VoltOkBySlope(Func<double, bool> slopeCondition) {
      return VoltOkBySlope(GetVoltage, slopeCondition);
    }
    private bool VoltOkBySlope(Func<Rate, double> volter, Func<double, bool> slopeCondition) {
      return GetLastVolts(volter).ToArray().With(vs => vs.Length > 0 && slopeCondition(vs.LinearSlope()));
    }

    public TradeConditionDelegate VltDwOk {
      get {
        return () => TradeDirectionByBool(VoltOkBySlope(d => d > 0));
      }
    }
    public TradeConditionDelegate VltUp2Ok {
      get {
        return () => TradeDirectionByBool(VoltOkBySlope(GetVoltage2, s => s < 0));
      }
    }
    public TradeConditionDelegate VltDw2Ok {
      get {
        return () => TradeDirectionByBool(VoltOkBySlope(GetVoltage2, s => s > 0));
      }
    }

    public IEnumerable<double> GetLastVolt() {
      return GetLastVolt(GetVoltage);
    }
    public IEnumerable<double> GetLastVolt(Func<Rate, double> getVolt) {
      return UseRates(rates
                  => rates.BackwardsIterator()
                  .Select(getVolt)
                  .SkipWhile(double.IsNaN)
                  .Take(1)
                  )
                  .SelectMany(v => v);
    }
    private IEnumerable<double> GetLastVolts() {
      return GetLastVolts(GetVoltage);
    }
    private IEnumerable<double> GetLastVolts(Func<Rate, double> getVolt) {
      return (from vs in UseRates(rates
                  => rates.BackwardsIterator()
                  .Select(getVolt)
                  .SkipWhile(double.IsNaN)
                  .TakeWhile(Lib.IsNotNaN)
                  )
              from v in vs
              select v);
    }
    private IEnumerable<T> GetLastVolt<T>(Func<double, T> map) {
      return GetLastVolt().Select(map);
    }
    #endregion
    #endregion

    #region WwwInfo
    T[] TLsAreFresh<T>(Func<T> func, double freshPerc = 0.05) {
      var tls = TrendLinesByDate;
      if(tls.Any(tl => tl.IsEmpty) || tls.TakeLast(1).Any(tl => !IsTLFresh(tl, freshPerc)))
        return new T[0];
      return new[] { func() };
    }
    int[] TLTimeAvg(bool useMin) {
      var tls = TrendLinesByDate;
      if(TrendLinesMinMax.Where(t => t.Item2 == useMin).Select(t => t.Item1).Any(tl => tl.IsEmpty) || tls.TakeLast(1).Any(tl => !IsTLFresh(tl, 0.05)))
        return new int[0];
      return TLsTimeRatio(this, useMin);
    }

    private static int[] TLsTimeRatio(TradingMacro tm, bool useMin) {
      double timeAvg;
      return TLsTimeRatio(tm, useMin, out timeAvg);
    }
    private static int[] TLsTimeRatio(TradingMacro tm, bool useMin, out double timeAvg) {
      var tls = tm.TrendLinesMinMax.Where(t => t.Item2 == useMin).Select(t => new { tl = t.Item1, perc = t.Item3.Abs() }).ToList();
      var timeRatio = (timeAvg = TLsTimeAverage(tls.Select(x => x.tl)).GetValueOrDefault(double.NaN)) * tls.Count / tm.RatesDuration;
      var rangesSum = tls.Sum(x => (int?)x.perc) / 100.0;
      return timeRatio.IsNaN() || !rangesSum.HasValue ? new int[0] : new[] { (timeRatio / rangesSum.Value - 1).ToPercent() };
    }

    private static double? TLsTimeAverage(IEnumerable<TL> tls) {
      return tls.Select(tl => (double?)tl.TimeSpan.TotalMinutes).SquareMeanRoot();
    }

    public object WwwInfo() {
      Func<Func<TradingMacro, TL>, IEnumerable<TL>> trenderLine = tl => TradingMacroTrender().Select(tl);
      return TradingMacroTrender(tm => {
        var tlText = ToFunc((TL tl) => new { l = "Ang" + tl.Color, t = $"{tl.Angle.Abs().Round()},{tl.TimeSpan.ToString("h\\:mm")}" });
        var angles = tm.TrendLinesTrendsAll.Select(tlText).ToArray();
        var showBBSD = new[] { VoltageFunction, VoltageFunction2 }.Contains(VoltageFunction.BBSD) ||
          new[] { TradeLevelBy.BoilingerDown, TradeLevelBy.BoilingerUp }.Contains(LevelBuyBy) ||
          TakeProfitFunction == TradingMacroTakeProfitFunction.BBand;
        return new {
          StDevHP = tm.StDevByHeightInPips.AutoRound2(2) + "/" + tm.StDevByPriceAvgInPips.AutoRound2(2),
          //StdTLLast = InPips(tls.TakeLast(1).Select(tl => tl.StDev).SingleOrDefault(),1),
          //BolngrAvg= InPips(_boilingerAvg,1),
          ProfitPip = CalculateTakeProfitInPips().Round(1),
          RiskRewrd = RiskRewardRatio().ToPercent() + "%/" + InPips(RiskRewardDenominator).AutoRound2(2),
          TlTmeMnMx = string.Join(",", TLsTimeRatio(tm, true).Concat(TLsTimeRatio(tm, false)).Select(tr => tr.ToString("n0") + "%")),
          //GreenEdge = tm.TrendLinesGreenTrends.EdgeDiff.SingleOrDefault().Round(1),
          //Plum_Edge = tm.TrendLinesPlumTrends.EdgeDiff.SingleOrDefault().Round(1),
          //Red__Edge = tm.TrendLinesRedTrends.EdgeDiff.SingleOrDefault().Round(1),
          //Blue_Edge = tm.TrendLinesBlueTrends.EdgeDiff.SingleOrDefault().Round(1)
          //BlueHStd_ = TrendLines2Trends.HStdRatio.SingleOrDefault().Round(1),
          //WvDistRsd = _waveDistRsd.Round(2)
        }
        .ToExpando()
        .Add(showBBSD ? (object)new { BoilBand = _boilingerStDev.Value.Select(t => string.Format("{0:n2}:{1:n2}", InPips(t.Item1), InPips(t.Item2))) } : new { })
        .Add(angles.ToDictionary(x => x.l, x => (object)x.t))
        .Add(new { BarsCount = RatesLengthBy == RatesLengthFunction.DistanceMinSmth ? BarCountSmoothed : RatesArray.Count })
        .Add(TradeConditionsHave(nameof(BSTipOk), nameof(BSTipROk)) ? (object)new { Tip_Ratio = _tipRatioCurrent.Round(3) } : new { })
        .Add(new { HistVolLn = $"{HV(this)}/{TradingMacroM1(HV).SingleOrDefault()}" })
        .Add(HVP(this).Select(hvp => (object)new { HistVolDif = $"{hvp.AutoRound2(3)}/{TradingMacroM1(HVP).Concat().SingleOrDefault().AutoRound2(3)}" }).DefaultIfEmpty(new { }).Single())
        ;
      }
      //.Merge(new { EqnxRatio = tm._wwwInfoEquinox }, () => TradeConditionsHave(EqnxLGRBOk))
      //.Merge(new { BPA1Tip__ = _wwwBpa1 }, () =>TradeConditionsHave(BPA12Ok))
      )
      .SingleOrDefault();
      // RhSDAvg__ = _macd2Rsd.Round(1) })
      // CmaDist__ = InPips(CmaMACD.Distances().Last()).Round(3) })
      double HV(TradingMacro tm) => tm.UseRates(ra => tm.InPips(ra.Select(_priceAvg).HistoricalVolatility())).SingleOrDefault().AutoRound2(3);
      double[] HVP(TradingMacro tm) => HistoricalVolatilityByPipsImpl(tm);
    }
    public IEnumerable<double> HistoricalVolatility() => UseRates(ra => InPips(ra.Select(_priceAvg).HistoricalVolatility()));
    public IEnumerable<double> HistoricalVolatilityByPips() => HistoricalVolatilityByPipsMem(this);
    Func<TradingMacro, double[]> _historicalVolatilityByPipsMem;
    Func<TradingMacro, double[]> HistoricalVolatilityByPipsMem => _historicalVolatilityByPipsMem ?? (_historicalVolatilityByPipsMem = new Func<TradingMacro, double[]>((tm)
        => HistoricalVolatilityByPipsImpl(tm)).MemoizeLast(tm => tm.UseRates(ra => ra.BackwardsIterator(r => r.StartDate).Take(1)).Concat().SingleOrDefault()));
    static double[] HistoricalVolatilityByPipsImpl(TradingMacro tm)
      => tm.UseRates(ra => tm.InPips(ra.Select(_priceAvg).HistoricalVolatility((d1, d2) => d1 - d2)));
    #endregion

    #region Angles
    TradeDirections TradeDirectionByTradeLevels(double buyLevel, double sellLevel) {
      return buyLevel.Avg(sellLevel).PositionRatio(_RatesMin, _RatesMax).ToPercent() > 50
        ? TradeDirections.Down
        : TradeDirections.Up;
    }
    TradeDirections TradeDirectionByBool(bool value) {
      return value
        ? TradeDirections.Both
        : TradeDirections.None;
    }
    TradeDirections TradeDirectionBySuppRes(SuppRes value) {
      return value.IsBuy
        ? TradeDirections.Up
        : TradeDirections.Down;
    }
    TradeDirections TradeDirectionByTreshold(double value, double treshold) {
      return TradeDirectionByBool(IsTresholdAbsOk(value, treshold));
    }
    TradeDirections TradeDirectionByAngleCondition(Func<TradingMacro, TL> tls, double tradingAngleRange) {
      return TrenderTrenLine(tls)
        .Select(tl => TradeDirectionByBool(IsTresholdAbsOk(tl.Angle, tradingAngleRange)))
        .First();
    }

    #region Angles
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegateHide LimeAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TLLime, TrendAngleLime); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegateHide GreenAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TLGreen, TrendAngleGreen); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate RedAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TLRed, TrendAngleRed); } }
    public TradeConditionDelegateHide PlumAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TLPlum, TrendAnglePlum); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate BlueAngOk {
      get {
        return () => TrenderTrenLine(tm => tm.TLBlue)
        .Select(tl => TrendAngleBlue1.IsNaN()
        ? TradeDirectionByAngleCondition(tm => tl, TrendAngleBlue0)
        : TradeDirectionByBool(tl.Angle.Abs().Between(TrendAngleBlue0, TrendAngleBlue1))
        ).First();
      }
    }


    private IEnumerable<TL> TrenderTrenLine(Func<TradingMacro, TL> map) {
      return TradingMacroTrender().Select(map);
    }
    private IEnumerable<TL> TrenderTrenLine(Func<TradingMacro, IEnumerable<TL>> map) {
      return TradingMacroTrender().Select(map).Concat();
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegate BlueAngTOOk {
      get {
        return () => BlueAngOk();
      }
    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegate BPA1Ok {
      get {
        Func<IList<Rate>[]> rates = () => UseRates(ra => ra.GetRange(0, ra.Count - TLLime.Count));
        Func<IEnumerable<double>> max = () => rates().Select(ra => ra.Max(_priceAvg));
        Func<IEnumerable<double>> min = () => rates().Select(ra => ra.Min(_priceAvg));
        Func<Func<TL, double>, IEnumerable<double>> price = getter => TrendLinesTrendsAll.Skip(1).Select(getter);
        Action setUp = () => {
          BuyLevel.RateEx = BuyLevel.Rate.Max(price(tl => tl.PriceAvg3).Average());
          SellLevel.RateEx = SellLevel.Rate.Max(price(tl => tl.PriceAvg2).Average());
        };
        Action setDown = () => {
          BuyLevel.RateEx = BuyLevel.Rate.Min(price(tl => tl.PriceAvg3).Average());
          SellLevel.RateEx = SellLevel.Rate.Min(price(tl => tl.PriceAvg2).Average());
        };
        Func<bool> isOk = () => {
          var isUp = TLBlue.Slope > 0;
          var avg1 = TLBlue.PriceAvg1;
          (isUp ? setUp : setDown)();
          return isUp ? max().Any(m => avg1 > m) : min().Any(m => avg1 < m);
        };
        return () => TradeDirectionByBool(isOk());
      }
    }
    public TradeConditionDelegate BPA12Ok {
      get {
        //Log = new Exception(new { BPA12Ok = new { TipRatio, InPercent = true, Treshold = false } } + "");
        var scanAnon = new { r1 = (Rate)null, r2 = (Rate)null };
        Func<Rate, int> sign = rate => rate.PriceAvg.Sign(rate.PriceCMALast);
        var rateDist = MonoidsCore.ToFunc(scanAnon, x => sign(x.r1) == sign(x.r2));

        Func<IList<Rate>> rates = () => UseRates(ra => {
          var count = ra.BackwardsIterator()
          .SkipWhile(r => r.PriceCMALast.IsNaN())
          .Pairwise((r1, r2) => new { r1, r2 })
          .TakeWhile(rateDist)
          .Count();
          return ra.GetRange(0, ra.Count - count);
        }, i => i);
        Func<double> isOk = () => {
          var isUp = TLBlue.Slope > 0;
          var avg1 = TLBlue.PriceAvg1;
          return _wwwBpa1 = ((isUp ? avg1 - _RatesMax : _RatesMin - avg1) / (_RatesMax - _RatesMin)).ToPercent();
        };
        return () => TradeDirectionByBool(isOk() > TipRatio);
      }
    }

    private IEnumerable<double> TrendLevelsSorted(Func<TL, double> get, Func<double, double, bool> comp, int takeCount) {
      Func<Func<TL, double>, Func<double, double, bool>, IEnumerable<TL>> trends =
        (getter, sort) => TradeTrendLines.ToList().SortByLambda((tl1, tl2) => sort(getter(tl1), getter(tl2)));
      Func<Func<TL, double>, Func<double, double, bool>, IEnumerable<double>> price =
        (getter, sorter) => trends(getter, sorter).Select(getter).Take(takeCount);
      return price(get, comp);
    }

    bool? _mmaLastIsUp = null;
    [TradeConditionSetCorridor]
    public TradeConditionDelegateHide MMAOk {
      get {
        Func<Func<TL, double>, IEnumerable<double>> price = getter => TrendLinesTrendsAll.Skip(1).Select(getter);
        var setUp = MonoidsCore.ToFunc(() => new {
          buy = BuyLevel.Rate.Max(price(tl => tl.PriceAvg2).Max()),
          sell = SellLevel.Rate.Max(price(tl => tl.PriceAvg3).Average())
        });
        var setDown = MonoidsCore.ToFunc(() => new {
          buy = BuyLevel.Rate.Min(price(tl => tl.PriceAvg2).Average()),
          sell = SellLevel.Rate.Min(price(tl => tl.PriceAvg3).Min())
        });
        Func<bool> isOk = () => {
          var isUp = TLBlue.Slope > 0;
          if(isUp != _mmaLastIsUp)
            BuySellLevels.ForEach(sr => {
              sr.CanTradeEx = false;
              sr.TradesCount = 0;
            });
          _mmaLastIsUp = isUp;
          var bs = (isUp ? setUp : setDown)();
          BuyLevel.RateEx = bs.buy;
          SellLevel.RateEx = bs.sell;
          return true;
        };
        return () => TradeDirectionByBool(isOk());
      }
    }
    public TradeConditionDelegateHide BLPA1Ok {
      get {
        var func = MonoidsCore.ToFunc(() => {
          var range = new { up = _RatesMax, down = _RatesMin };
          var level = TLBlue.PriceAvg1;
          var isUp = level.PositionRatio(range.down, range.up) > 0.5;
          var height = TLGreen.StDev;
          var levelUp = isUp ? range.up + height : range.down + height;
          var levelDown = isUp ? range.up - height : range.down - height;
          CenterOfMassBuy = levelUp;
          CenterOfMassSell = levelDown;
          return isUp ? level >= levelDown : level <= levelUp;
        });
        return () => TradeDirectionByBool(func());
      }
    }

    TradeDirections TradeDirectionsAnglewise(params TL[] tls) {
      var slope = tls.Sum(tl => tl.Slope.Sign());
      return slope < 0
        ? TradeDirections.Down
        : slope > 0
        ? TradeDirections.Up
        : TradeDirections.None;
    }
    TradeDirections TradeDirectionsAnglecounterwise(params TL[] tls) {
      var slope = tls.Sum(tl => tl.Slope.Sign());
      return slope < 0
        ? TradeDirections.Up
        : slope > 0
        ? TradeDirections.Down
        : TradeDirections.None;
    }
    TradeDirections TradeDirectionsAnglewise(TL tl) {
      return tl.Slope < 0 ? TradeDirections.Down : TradeDirections.Up;
    }
    TradeDirections TradeDirectionsAnglecounterwise(TL tl) {
      return tl.Slope > 0 ? TradeDirections.Down : TradeDirections.Up;
    }
    public TradeConditionDelegate TLLstAOk { get { return () => TradeDirectionByTL(0, TradeDirectionsAnglecounterwise, TrendAngleLast0, TrendAngleLast1); } }
    public TradeConditionDelegate TLLstAROk { get { return () => TradeDirectionByTL(0, TradeDirectionsAnglewise, TrendAngleLast0, TrendAngleLast1); } }
    public TradeConditionDelegate TLPpvAOk { get { return () => TradeDirectionByTL(1, TradeDirectionsAnglecounterwise, TrendAnglePrev0, TrendAnglePrev1); } }
    public TradeConditionDelegate TLPpvAROk { get { return () => TradeDirectionByTL(1, TradeDirectionsAnglewise, TrendAnglePrev0, TrendAnglePrev1); } }
    public TradeConditionDelegate TLPpv2AOk { get { return () => TradeDirectionByTL(2, TradeDirectionsAnglecounterwise, TrendAnglePrev0, TrendAnglePrev1); } }
    public TradeConditionDelegate TLPpv2AROk { get { return () => TradeDirectionByTL(2, TradeDirectionsAnglewise, TrendAnglePrev20, TrendAnglePrev21); } }

    private TradeDirections TradeDirectionByTL(int skip, Func<TL, TradeDirections> tdMap, params double[] range) {
      return TrendLinesTrendsAll
      .OrderByDescending(tl => tl.EndDate)
      .Where(tl => IsTresholdRangeOk(tl.Angle, range))
      .Skip(skip)
      .Select(tl => tdMap(tl))
      .FirstOrDefault();
    }

    [TradeConditionTradeDirection]
    public TradeConditionDelegate BOk { get { return () => TradeDirectionsAnglecounterwise(TLBlue); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate BROk { get { return () => TradeDirectionsAnglewise(TLBlue); } }

    [TradeConditionTradeDirection]
    public TradeConditionDelegate POk { get { return () => TradeDirectionsAnglecounterwise(TLPlum); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate PROk { get { return () => TradeDirectionsAnglewise(TLPlum); } }


    [TradeConditionTradeDirection]
    public TradeConditionDelegate ROk { get { return () => TradeDirectionsAnglecounterwise(TLRed); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate RROk { get { return () => TradeDirectionsAnglewise(TLRed); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate GOk { get { return () => TradeDirectionsAnglecounterwise(TLGreen); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate GROk { get { return () => TradeDirectionsAnglewise(TLGreen); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate LOk { get { return () => TradeDirectionsAnglecounterwise(TLLime); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate LROk { get { return () => TradeDirectionsAnglewise(TLLime); } }
    #endregion

    #region Trends Ratios
    private IEnumerable<Tuple<int, SuppRes>> EquinoxValues(
      Func<TradingMacro, IList<IEnumerable<TL>>> tlses,
      Func<IEnumerable<WaveRange>> waveRangesFunc = null) {
      return TradingMacroTrender(tm => EquinoxValuesImpl2(tlses(tm), waveRangesFunc)).SelectMany(x => x.Select(y => y.Item2));
    }

    private static IEnumerable<Tuple<IEnumerable<TL>, Tuple<int, SuppRes>>> EquinoxValuesImpl2(
      IList<IEnumerable<TL>> tlses,
      Func<IEnumerable<WaveRange>> waveRangesFunc = null) {
      Func<Func<TL, double>, SuppRes, Tuple<Func<TL, double>, SuppRes>> paFunc = (pa, sr) => Tuple.Create(pa, sr);
      var paTuples = new[] { paFunc(tl => tl.PriceAvg2, new Store.SuppRes { IsSupport = true }), paFunc(tl => tl.PriceAvg3, new Store.SuppRes { IsSupport = false }) };
      Func<IEnumerable<TL>, IEnumerable<Tuple<int, SuppRes>>> exec = (tls) => Equinox(tls, paTuples, waveRangesFunc == null ? new WaveRange[0] : waveRangesFunc());

      var xx = (from tls in tlses
                let ts = exec(tls)
                let t = ts.MaxByOrEmpty(y => y.Item1).Take(1).ToArray()
                where t.Any()
                select new { tls, t = t.Single() });
      var xxx = xx
      .OrderBy(x => x.t.Item1)
      .Select(x => Tuple.Create(x.tls, x.t));
      return xxx;
    }

    #endregion
    private static IEnumerable<Tuple<int, SuppRes>> Equinox(
      IEnumerable<TL> trendLines,
      IEnumerable<Tuple<Func<TL, double>, SuppRes>> paFuncs,
      IEnumerable<WaveRange> waveRanges) {
      var heights = trendLines.Select(tl => tl.PriceAvg2 - tl.PriceAvg3).MaxByOrEmpty();
      return EdgesDiff2(trendLines, paFuncs, waveRanges)
        .OrderBy(pa => pa.Item1)
        .SelectMany(x => heights.Select(height => Tuple.Create((x.Item1 / height).ToPercent(), x.Item2)));
    }
    private static IEnumerable<Tuple<double, SuppRes>> EdgesDiff2(
      IEnumerable<TL> trendLines,
      IEnumerable<Tuple<Func<TL, double>, SuppRes>> paFuncs,
      IEnumerable<WaveRange> waveRanges
      ) {
      Func<Tuple<double, double>, double> spread = dbls => dbls.Item1.Abs(dbls.Item2);
      Func<SuppRes, WaveRange, double> price = (sr, wr) => sr.IsBuy ? wr.Min : wr.Max;
      var spreadAvg = MonoidsCore.ToFunc((Func<TL, double>)null, (SuppRes)null, (f, sr) =>
        new {
          spread = trendLines
        .Select(f)
        //.Concat(new[] { sr.Rate })
        .Concat(waveRanges.Select(waveRange => price(sr, waveRange)))
        .ToArray().Permutation().Select(spread).DefaultIfEmpty(double.NaN).Average(), sr
        });
      var pas = paFuncs.Select(t => spreadAvg(t.Item1, t.Item2));
      return pas.Where(pa => pa.spread.IsNotNaN()).Select(x => Tuple.Create(x.spread, x.sr));
    }
    #endregion

    #region TradingPriceRange
    public TradeConditionDelegate PrRngOk {
      get {
        return () => TradeDirectionByBool(IsTradingPriceInRange(TradingPriceRange, CurrentPrice.Average));
      }
    }

    string _tradingPriceRange;
    [DisplayName("Trading Price")]
    [Description("1.17-1.19")]
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsTradingParams)]
    public string TradingPriceRange {
      get { return _tradingPriceRange; }
      set {
        if(_tradingPriceRange == value)
          return;
        if(string.IsNullOrWhiteSpace(value))
          IsTradingPriceInRange(value ?? "", 0);
        _tradingPriceRange = value ?? "";
        OnPropertyChanged(nameof(TradingPriceRange));
      }
    }

    public static bool IsTradingPriceInRange(string rangeText, double price) {
      string[][] ranges;
      if(rangeText.TryFromJson(out ranges)) {
        var timeSpans = ranges?.Select(r => r.Select(t => double.Parse(t)).ToArray());
        return timeSpans?.Any(ts => IsPriceRangeOk(ts, price)) ?? true;
      }
      double range;
      if(double.TryParse(rangeText, out range))
        return IsTresholdAbsOk(price, range);
      throw new Exception(new { rangeText, error = "format" } + "");
    }
    private static bool IsPriceRangeOk(double[] times, double tod) {
      return times.First() < times.Last()
        ? tod.Between(times.First(), times.Last())
        : tod >= times.First() || tod <= times.Last();
    }
    #endregion

    #region TimeFrameOk
    bool TestTimeFrame() {
      return RatesTimeSpan()
      .Where(ts => ts != TimeSpan.Zero)
      .Select(ratesSpan => TimeFrameTresholdTimeSpan2 > TimeSpan.Zero
        ? ratesSpan <= TimeFrameTresholdTimeSpan && ratesSpan >= TimeFrameTresholdTimeSpan2
        : IsTresholdAbsOk(ratesSpan, TimeFrameTresholdTimeSpan)
        )
        .DefaultIfEmpty()
        .Any(b => b);
    }

    static readonly Calendar callendar = CultureInfo.GetCultureInfo("en-US").Calendar;
    static int GetWeekOfYear(DateTime dateTime) { return callendar.GetWeekOfYear(dateTime, CalendarWeekRule.FirstDay, DayOfWeek.Sunday); }
    TimeSpan _RatesTimeSpanCache = TimeSpan.Zero;
    DateTime[] _RateForTimeSpanCache = new DateTime[0];
    private IEnumerable<TimeSpan> RatesTimeSpan() {
      return UseRates(rates => rates.Count == 0
        ? TimeSpan.Zero
        : RatesTimeSpan(rates));// rates.Last().StartDate - rates[0].StartDate);
    }

    private TimeSpan RatesTimeSpan(IList<Rate> rates) {
      var ratesLast = new[] { rates[0].StartDate, rates.Last().StartDate };
      if((from rl in ratesLast join ch in _RateForTimeSpanCache on rl equals ch select rl).Count() == 2)
        return _RatesTimeSpanCache;
      _RateForTimeSpanCache = ratesLast;
      var periodMin = BarPeriodInt.Max(1);
      return _RatesTimeSpanCache = rates
        .Pairwise((r1, r2) => r1.StartDate.Subtract(r2.StartDate).Duration())
        .Where(ts => ts.TotalMinutes <= periodMin)
        .Sum(ts => ts.TotalMinutes)
        .FromMinutes();
    }

    //[TradeConditionAsleep]
    [TradeConditionTurnOff]
    public TradeConditionDelegate TimeFrameOk { get { return () => TradeDirectionByBool(TestTimeFrame()); } }
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide TimeFrameM1Ok {
      get {
        return () => TradingMacroOther().Select(tm =>
        tm.WaveRangeAvg.TotalMinutes < RatesDuration
        ? TradeDirections.Both
        : TradeDirections.None)
        .SingleOrDefault();
        ;
      }
    }
    #endregion

    #region M1
    [TradeConditionAsleep]
    public TradeConditionDelegateHide M1Ok {
      get {
        return () => TradingMacroOther()
        .SelectMany(tm => tm.WaveRanges.Take(1), (tm, wr) => new { wra = tm.WaveRangeAvg, wr })
        .Select(x =>
        x.wr.Angle.Abs() >= x.wra.Angle &&
        x.wr.TotalMinutes >= x.wra.TotalMinutes &&
        x.wr.HSDRatio >= x.wra.HSDRatio &&
        x.wr.StDev < x.wra.StDev
        )
        .Select(b => b
        ? TradeDirections.Both
        : TradeDirections.None)
        .FirstOrDefault();
      }
    }
    public TradeConditionDelegateHide M1SDOk {
      get {
        return () => TradingMacroM1(tm => tm.WaveRanges.StandardDeviation(wr => wr.StDev) < 1)
        .Select(TradeDirectionByBool)
        .Where(b => b.HasAny())
        .DefaultIfEmpty()
        .First();
      }
    }
    public TradeConditionDelegateHide M1HOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, wr => wr.HSDRatio);
      }
    }
    public TradeConditionDelegateHide M1MOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, wr => wr.TotalMinutes);
      }
    }
    public TradeConditionDelegateHide M1DOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, wr => wr.Distance);
      }
    }
    public TradeConditionDelegateHide M1D_Ok {
      get {
        return () => WaveConditions((d1, d2) => d1 < d2, wr => wr.Distance, wr => wr.TotalMinutes);
      }
    }
    public TradeConditionDelegateHide M1SOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, (d1, d2) => d1 < d2, wr => wr.StDev);
      }
    }
    public TradeConditionDelegateHide M1PAOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, wr => wr.PipsPerMinute, wr => wr.Angle.Abs());
      }
    }
    public TradeConditionDelegateHide M1PA_Ok {
      get {
        return () => WaveConditions((d1, d2) => d1 < d2, wr => wr.PipsPerMinute, wr => wr.Angle.Abs());
      }
    }
    public TradeConditionDelegateHide M1P_Ok {
      get {
        return () => WaveConditions((d1, d2) => d1 < d2, wr => wr.PipsPerMinute);
      }
    }
    public TradeConditionDelegateHide M1AOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, wr => wr.HSDRatio);
      }
    }
    private TradeDirections WaveConditions(Func<double, double, bool> comp, params Func<WaveRange, double>[] was) {
      return TradingMacroOther()
              .SelectMany(tm => tm.WaveRanges.Take(1), (tm, wr) => new { wra = tm.WaveRangeAvg, wr })
              .Select(x => was.All(wa => comp(wa(x.wr), wa(x.wra))))
              .Select(b => b
              ? TradeDirections.Both
              : TradeDirections.None)
              .SingleOrDefault();
    }
    private TradeDirections WaveConditions(Func<double, double, bool> compa, Func<double, double, bool> comps, params Func<WaveRange, double>[] was) {
      return TradingMacroM1()
              .SelectMany(tm => tm.WaveRanges.Take(1), (tm, wr) => new { wra = tm.WaveRangeAvg, wrs = tm.WaveRangeSum, wr })
              .Select(x => was.All(wa => compa(wa(x.wr), wa(x.wra)) && comps(wa(x.wr), wa(x.wrs))))
              .Select(b => b
              ? TradeDirections.Both
              : TradeDirections.None)
              .SingleOrDefault();
    }

    #endregion

    #region Outsides
    [Description("'Green' corridor is outside the 'Red' and 'Blue' ones")]
    public TradeConditionDelegateHide GreenOk {
      get {
        Func<TradeDirections, TradeDirections> getTD = td => (TradeConditionsHaveTD() ? TradeDirections.Both : td);
        return () =>
          TLGreen.PriceAvg2 >= TLRed.PriceAvg2.Max(TLBlue.PriceAvg2)
          ? getTD(TradeDirections.Down)
          : TLGreen.PriceAvg3 <= TLRed.PriceAvg3.Min(TLBlue.PriceAvg3)
          ? getTD(TradeDirections.Up)
          : TradeDirections.None;
      }
    }
    public TradeConditionDelegateHide GreenExtOk {
      get {
        Func<TradeDirections, TradeDirections> getTD = td => (TradeConditionsHaveTD() ? TradeDirections.Both : td);
        return () =>
          TLGreen.PriceAvg2 >= TLRed.PriceAvg21.Max(TLBlue.PriceAvg2)
          ? getTD(TradeDirections.Up)
          : TLGreen.PriceAvg3 <= TLRed.PriceAvg31.Min(TLBlue.PriceAvg3)
          ? getTD(TradeDirections.Down)
          : TradeDirections.None;
      }
    }
    #region Outsiders
    [TradeConditionAsleep]
    public TradeConditionDelegate AslpM1Ok {
      get {
        Func<TradingMacro, double, bool> isIn = (tm, v) =>
          EquinoxBasedMinMax(OutsiderTLs(tm)).Any(t => {
            var max = t.Item2;
            var min = t.Item1;
            var h = (max - min) / 4;
            return v.Between(min + h, max - h);
          });

        return () => TradingMacroM1(tm => !isIn(tm, CurrentPrice.Average)).Select(TradeDirectionByBool).SingleOrDefault();
      }
    }
    //[TradeConditionAsleep]
    public TradeConditionDelegate OutsideAnyOk {
      get { return () => IsCurrentPriceOutsideCorridor(tm => tm.BarPeriod > BarsPeriodType.t1, tm => OutsiderTLs(tm)); }
    }
    public TradeConditionDelegate OutEqnxOk {
      get {
        var tpl = TradingMacroM1(tm => EquinoxBasedMinMax(OutsiderTLs(tm)));
        return () => tpl.SelectMany(t => t)
        .Select(t =>
          !CurrentPrice.Average.Between(t.Item1, t.Item2) && t.Item3 < 5)
          .Where(b => b)
          .Select(TradeDirectionByBool)
          .FirstOrDefault();
      }
    }

    static IList<IEnumerable<TL>> OutsiderTLs(TradingMacro tm) {
      return tm.OutsidersInt.ToArray(i => i.SelectMany(i2 => tm.TrendLinesTrendsAll.Skip(i2).Take(1)));
    }

    #region Helpers
    private void ScanOutsideEquinox() {
      EquinoxBasedMinMax(OutsiderTLs(this))
        .ForEach(t => {
          //_outsideMin = t.Item1;
          //_outsideMax = t.Item2;
        });
    }

    private static IEnumerable<Tuple<double, double, int>> EquinoxBasedMinMax(IList<IEnumerable<TL>> tlss) {
      var tpl = EquinoxBasedTLs(tlss).ToArray();
      var tls = tpl.SelectMany(tl => tl.Item1).ToArray();
      var min = tls.Select(x => x.PriceAvg3).MinByOrEmpty().Take(1);
      var max = tls.Select(x => x.PriceAvg2).MaxByOrEmpty().Take(1);
      var perc = tpl.Select(t => t.Item2).Take(1);
      return min.Zip(max, (mn, mx) => new { mn, mx }).Zip(perc, (mm, i) => Tuple.Create(mm.mn, mm.mx, i));
    }

    private static IEnumerable<TL> EquinoxEdge(IList<IEnumerable<TL>> tlss) {
      return EquinoxEdges(tlss).Take(1).SelectMany(tls => tls);
    }
    private static IEnumerable<T> EquinoxEdges<T>(IList<IEnumerable<TL>> tlss, Func<TL[], T> map) {
      return EquinoxEdges(tlss).Select(tls => map(tls));
    }
    private static IEnumerable<TL[]> EquinoxEdges(IList<IEnumerable<TL>> tlss) {
      return tlss
        .Select(tls => tls.Where(tl => !tl.IsEmpty).ToArray())
        .Where(tls => !tls.IsEmpty())
        .OrderBy(tls => tls.SelectMany(tl => tl.EdgeDiff).Average());
      //.Take(1)
      //.SelectMany(tls => tls);
    }

    private static IEnumerableCore.Singleable<Tuple<IEnumerable<TL>, int>> EquinoxBasedTLs(IList<IEnumerable<TL>> tlss) {
      return EquinoxValuesImpl2(tlss)
        .Take(1)
        .Select(eqnx => Tuple.Create(eqnx.Item1, eqnx.Item2.Item1))
        .AsSingleable();
    }

    TradeDirections IsCurrentPriceOutsideCorridor(
        Func<TradingMacro, bool> tmPredicate,
        Func<TradingMacro, IList<IEnumerable<TL>>> trendLevels,
        Func<TL, double> min,
        Func<TL, double> max
        ) {
      Func<TradeDirections> onBelow = () => TradeDirections.Up;
      Func<TradeDirections> onAbove = () => TradeDirections.Down;
      return TradingMacroOther(tmPredicate)
        .SelectMany(tm => trendLevels(tm))
        .SelectMany(tls => tls)
        .Select(tls =>
          CurrentPrice.Average < min(tls) ? onBelow()
          : CurrentPrice.Average > max(tls) ? onAbove()
          : TradeDirections.None)
        .DefaultIfEmpty()
        .Min();
    }
    TradeDirections IsCurrentPriceOutsideCorridor(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, IList<IEnumerable<TL>>> trendLevels) {
      return IsCurrentPriceOutsideCorridor(tmPredicate, trendLevels, tl => tl.PriceAvg3, tl => tl.PriceAvg2);
    }
    #endregion
    #endregion

    #endregion

    #region TradingMacros
    public IEnumerable<U> TradingMacroTrender<T, U>(Func<TradingMacro, IEnumerable<T>> selector, Func<IEnumerable<T>, IEnumerable<U>> many) {
      return TradingMacroTrender(selector).SelectMany(many);
    }
    public IEnumerable<T> TradingMacroTrender<T>(Func<TradingMacro, T> map) {
      return TradingMacroTrender().Select(map);
    }
    public IEnumerable<TradingMacro> TradingMacroTrender() {
      return TradingMacrosByPair().Where(tm => tm.IsTrender);
    }
    public IEnumerable<T> TradingMacroTrader<T>(Func<TradingMacro, T> map) {
      return TradingMacrosByPair().Where(tm => tm.IsTrader).Select(map);
    }
    public IEnumerable<TradingMacro> TradingMacroTrader() {
      return TradingMacrosByPair().Where(tm => tm.IsTrader);
    }
    public IEnumerable<TradingMacro> TradingMacroM1() {
      return TradingMacrosByPair()
        .Where(tm => tm.BarPeriod > BarsPeriodType.t1);
    }
    public IEnumerable<T> TradingMacroM1<T>(Func<TradingMacro, T> selector) {
      return TradingMacroM1()
        .OrderBy(tm => tm.BarPeriod)
        .Take(1)
        .Select(selector);
    }
    public IEnumerable<U> TradingMacroM1<T, U>(Func<TradingMacro, IEnumerable<T>> selector, Func<IEnumerable<T>, IEnumerable<U>> many) {
      return TradingMacroM1(selector).SelectMany(many);
    }
    public IEnumerable<TradingMacro> TradingMacroOther(Func<TradingMacro, bool> predicate) {
      return TradingMacrosByPair().Where(predicate);
    }
    public IEnumerable<TradingMacro> TradingMacroOther() {
      return TradingMacrosByPair().Where(tm => tm != this);
    }
    public IEnumerable<TradingMacro> TradingMacroOther(string pair) {
      return TradingMacrosByPair().Where(tm => tm.Pair != Pair);
    }
    public IEnumerable<TradingMacro> TradingMacrosByPair() {
      return _tradingMacros.Where(tm => tm.Pair == Pair).OrderBy(tm => PairIndex);
    }
    public IEnumerable<TradingMacro> TradingMacrosByPair(string pair) {
      return _tradingMacros.Where(tm => tm.Pair == pair).OrderBy(tm => PairIndex);
    }
    public IEnumerable<T> TradingMacroHedged<T>(Func<TradingMacro, T> map) => TradingMacroHedged().Select(map);
    public IEnumerable<TradingMacro> TradingMacroHedged() => TradingMacrosByPair(PairHedge).Where(tm => tm.BarPeriod == BarPeriod);
    public IEnumerable<TradingMacro> TradingMacrosByPairHedge(string pair) => _tradingMacros.Where(tm => tm.PairHedge == pair).OrderBy(tm => PairIndex);
    #endregion

    #region Cross Handlers

    [TradeConditionTurnOff]
    public TradeConditionDelegate WCOk {
      get {
        return () => TradeDirectionByBool(CalculateTradingDistance() < StDevByHeight * 3);
      }
    }
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide TipRatioOk {
      get {
        return () => TradeDirectionByBool(IsTresholdAbsOk(_tipRatioCurrent, TipRatio));
      }
    }

    #endregion

    #region TradeConditions
    List<EventHandler<SuppRes.CrossedEvetArgs>> _crossEventHandlers = new List<EventHandler<Store.SuppRes.CrossedEvetArgs>>();
    public ReactiveUI.ReactiveList<Tuple<TradeConditionDelegate, PropertyInfo>> _TradeConditions;
    public IList<Tuple<TradeConditionDelegate, PropertyInfo>> TradeConditions {
      get {
        if(_TradeConditions == null) {
          _TradeConditions = new ReactiveUI.ReactiveList<Tuple<TradeConditionDelegate, PropertyInfo>>();
          _TradeConditions.ItemsAdded.Subscribe(tc => {
          });
        }
        return _TradeConditions;
      }
      //set {
      //  _TradeConditions = value;
      //  OnPropertyChanged("TradeConditions");
      //}
    }
    bool HasTradeConditions { get { return TradeConditions.Any(); } }
    void TradeConditionsReset() {
      _mmaLastIsUp = null;
      TradeConditions.Clear();
    }
    public T[] GetTradeConditions<A, T>(Func<A, bool> attrPredicate, Func<TradeConditionDelegate, PropertyInfo, TradeConditionAttribute.Types, T> map) where A : Attribute {
      return (from x in this.GetPropertiesByTypeAndAttribute(() => (TradeConditionDelegate)null, attrPredicate, (v, p) => new { v, p })
              let a = x.p.GetCustomAttributes<TradeConditionAttribute>()
              .DefaultIfEmpty(new TradeConditionAttribute(TradeConditionAttribute.Types.And)).First()
              select map(x.v, x.p, a.Type))
              .ToArray();
    }
    public Tuple<TradeConditionDelegate, PropertyInfo>[] GetTradeConditions(Func<PropertyInfo, bool> predicate = null) {
      return GetType().GetProperties()
        .Where(p => p.PropertyType == typeof(TradeConditionDelegate))
        .Where(p => predicate == null || predicate(p))
        .Select(p => Tuple.Create((TradeConditionDelegate)p.GetValue(this), p))
        .ToArray();

      //return new[] { WideOk, TpsOk, AngleOk, Angle0Ok };
    }
    public static string ParseTradeConditionNameFromMethod(MethodInfo method) {
      return Regex.Match(method.Name, "<(.+)>").Groups[1].Value.Substring(4);

    }
    public static string ParseTradeConditionToNick(string tradeConditionFullName) {
      return Regex.Replace(tradeConditionFullName, "ok$", "", RegexOptions.IgnoreCase);
    }
    void OnTradeConditionSet(Tuple<TradeConditionDelegate, PropertyInfo> tc) {
      switch(ParseTradeConditionNameFromMethod(tc.Item1.Method)) {
        case "Tip3Ok":
          BuyLevel.Crossed += BuyLevel_Crossed;
          break;
      }
    }

    private void BuyLevel_Crossed(object sender, SuppRes.CrossedEvetArgs e) {
      throw new NotImplementedException();
    }

    public IList<Tuple<TradeConditionDelegate, PropertyInfo>> TradeConditionsSet(IList<string> names) {
      TradeConditionsReset();
      GetTradeConditions().Where(tc => names.Contains(ParseTradeConditionNameFromMethod(tc.Item1.Method)))
        .ForEach(tc => _TradeConditions.Add(tc));
      return TradeConditions;
    }
    bool TradeConditionsHaveTurnOff() {
      return TradeConditionsInfo<TradeConditionTurnOffAttribute>().Any(d => {
        var td = d();
        return td.HasNone() || BuyLevel.CanTrade && !td.HasUp() || SellLevel.CanTrade && !td.HasDown();
      });
    }
    bool TradeConditionsHaveSetCorridor() {
      return TradeConditionsInfo<TradeConditionSetCorridorAttribute>().Any();
    }
    Singleable<TradeDirections> TradeConditionsCanSetCorridor() {
      return TradeConditionsInfo<TradeConditionCanSetCorridorAttribute>()
        .Select(d => d())
        .Scan(TradeDirections.Both, (td1, td2) => td1 & td2)
        .TakeLast(1)
        .DefaultIfEmpty(TradeDirections.Both)
        .AsSingleable();
    }
    IEnumerable<TradeDirections> TradeConditionsTradeStrip() {
      return TradeConditionsInfo<TradeConditionTradeStripAttribute>().Select(d => d());
    }
    bool TradeConditionsHaveTD() {
      return TradeConditionsInfo<TradeConditionTradeDirectionAttribute>().Any();
    }
    bool TradeConditionsHaveAsleep() {
      return TradeConditionsInfo<TradeConditionAsleepAttribute>().Any(d => d().HasNone()) || !IsTradingDay();
    }
    bool TradeConditionsHave(TradeConditionDelegateHide td) {
      return TradeConditionsInfo().Any(d => d.Method == td.Method);
    }
    bool TradeConditionsHave(TradeConditionDelegate td) {
      return TradeConditionsInfo().Any(d => d.Method == td.Method);
    }
    bool TradeConditionsHave(params string[] tds) {
      return TradeConditionsInfo().Any(d => tds.Any(td => d.Method.Name.Contains(td)));
    }
    IEnumerable<TradeDirections> TradeConditionHasAny(TradeConditionDelegate td) {
      return TradeConditionsInfo().Where(d => d.Method == td.Method).Select(d => d());
    }
    public IEnumerable<TradeConditionDelegate> TradeConditionsInfo<A>() where A : Attribute {
      return TradeConditionsInfo(new Func<Attribute, bool>(a => a.GetType() == typeof(A)), (d, p, ta, s) => d);
    }
    public IEnumerable<T> TradeConditionsInfo<T>(Func<TradeConditionDelegate, PropertyInfo, TradeConditionAttribute.Types, string, T> map) {
      return TradeConditionsInfo((Func<Attribute, bool>)null, map);
    }
    public IEnumerable<T> TradeConditionsInfo<A, T>(Func<A, bool> attrPredicate, Func<TradeConditionDelegate, PropertyInfo, TradeConditionAttribute.Types, string, T> map) where A : Attribute {
      return from tc in TradeConditionsInfo((d, p, s) => new { d, p, s })
             where attrPredicate == null || tc.p.GetCustomAttributes().OfType<A>().Count(attrPredicate) > 0
             from tca in tc.p.GetCustomAttributes<TradeConditionAttribute>().DefaultIfEmpty(new TradeConditionAttribute(TradeConditionAttribute.Types.And))
             select map(tc.d, tc.p, tca.Type, tc.s);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void TradeConditionsTrigger() {
      if(!IsRatesLengthStableGlobal()) return;
      if(!IsTrader) return;
      //var isSpreadOk = false.ToFunc(0,i=> CurrentPrice.Spread < PriceSpreadAverage * i);
      if(IsPairHedged && BarsCountCalc == RatesArray.Count) {
        if(IsTradingActive)
          TradeConditionsEval().Where(eval => eval.HasAny()).ForEach(eval => {
            var isBuy = eval.HasUp();
            if(!HedgeBuySell(isBuy)
              .Select(x => x.Value)
              .Select(t => t.tm.HaveTrades(t.IsBuy))
              .Any(b => b))
              OpenHedgedTrades(isBuy, "Trade Condition");
          });
        return;
      }
      if(IsAsleep) {
        BuySellLevels.ForEach(sr => {
          sr.CanTrade = false;
          sr.InManual = false;
          sr.TradesCount = 0;
        });
        return;
      }
      if(CanTriggerTradeDirection() && (IsContinuousTrading || !HaveTrades()) /*&& !HasTradeDirectionTriggers*/) {
        TradeConditionsEval().ForEach(eval => {
          var hasBuy = TradeDirection.HasUp() && eval.HasUp();
          var hasSell = TradeDirection.HasDown() && eval.HasDown();
          var canBuy = IsTurnOnOnly && BuyLevel.CanTrade || hasBuy;
          var canSell = IsTurnOnOnly && SellLevel.CanTrade || hasSell;
          var canBuyOrSell = canBuy || canSell;
          var canTradeBuy = canBuy || (canBuyOrSell && TradeCountMax > 0);
          var canTradeSell = canSell || (canBuyOrSell && TradeCountMax > 0);

          Action<SuppRes, bool> updateCanTrade = (sr, ct) => {
            if(sr.CanTrade != ct) {
              if(sr.CanTradeEx = ct) {
                var tradesCount = sr.IsSell && eval.HasDown() || sr.IsBuy && eval.HasUp() ? 0 : 1;
                sr.TradesCount = tradesCount + TradeCountStart;
              }
            }
          };

          updateCanTrade(BuyLevel, canTradeBuy);
          updateCanTrade(SellLevel, canTradeSell);
          var isPriceIn = new[] { CurrentEnterPrice(false), this.CurrentEnterPrice(true) }.All(cp => cp.Between(SellLevel.Rate, BuyLevel.Rate));

          if(BuyLevel.CanTrade && SellLevel.CanTrade && canTradeBuy != canTradeSell) {
            BuyLevel.CanTradeEx = hasBuy && isPriceIn;
            SellLevel.CanTradeEx = hasSell && isPriceIn;
          }

          if(eval.HasAny())
            BuySellLevels.ForEach(sr => sr.DateCanTrade = ServerTime);
        });
      }
    }

    bool _isTurnOnOnly = false;
    [Category(categoryActiveYesNo)]
    [WwwSetting(wwwSettingsTradingConditions)]
    public bool IsTurnOnOnly {
      get {
        return _isTurnOnOnly;
      }
      set {
        _isTurnOnOnly = value;
      }
    }

    public IEnumerable<TradeDirections> TradeConditionsEvalStartDate() {
      if(!IsTrader)
        return new TradeDirections[0];
      return (from d in TradeConditionsInfo<TradeConditionStartDateTriggerAttribute>()
              let b = d()
              group b by b into gb
              select gb.Key
              )
              .DefaultIfEmpty()
              .OrderBy(b => b)
              .Take(1)
              .Where(b => b.HasAny());
    }
    public IEnumerable<TradeDirections> TradeConditionsEval() {
      if(!IsTrader)
        yield break;
      var tds = (from tc in TradeConditionsInfo((d, p, t, s) => new { d, t, s })
                 group tc by tc.t into gtci
                 let and = gtci.Select(g => g.d()).ToArray()
                 let c = gtci.Key == TradeConditionAttribute.Types.And
                 ? and.Aggregate(TradeDirections.Both, (a, td) => a & td)
                 : and.Aggregate(TradeDirections.None, (a, td) => a | td)
                 select c
              )
              .Scan(TradeDirections.Both, (a, td) => a & td)
              .TakeLast(1)
              .Select(td => td & TradeDirection);
      foreach(var td in tds)
        yield return td;
    }
    public IEnumerable<TradeConditionDelegate> TradeConditionsInfo() {
      return TradeConditionsInfo(TradeConditions, (d, p, s) => d);
    }
    public IEnumerable<T> TradeConditionsInfo<T>(Func<TradeConditionDelegate, PropertyInfo, string, T> map) {
      return TradeConditionsInfo(TradeConditions, map);
    }
    public IEnumerable<T> TradeConditionsAllInfo<T>(Func<TradeConditionDelegate, PropertyInfo, string, T> map) {
      return TradeConditionsInfo(GetTradeConditions(), map);
    }
    public IEnumerable<T> TradeConditionsInfo<T>(IList<Tuple<TradeConditionDelegate, PropertyInfo>> tradeConditions, Func<TradeConditionDelegate, PropertyInfo, string, T> map) {
      return tradeConditions.Select(tc => map(tc.Item1, tc.Item2, ParseTradeConditionNameFromMethod(tc.Item1.Method)));
    }
    [DisplayName("Trade Conditions")]
    [Category(categoryActiveFuncs)]
    public string TradeConditionsSave {
      get { return string.Join(MULTI_VALUE_SEPARATOR, TradeConditionsInfo((tc, pi, name) => name)); }
      set {
        TradeConditionsSet(value.Split(MULTI_VALUE_SEPARATOR[0]));
      }
    }

    public double AvgLineMax {
      get;
      private set;
    }
    public double AvgLineMin {
      get;
      private set;
    }
    public double AvgLineAvg {
      get;
      private set;
    }

    int _wavesRsdPec = 33;

    [WwwSetting(wwwSettingsTradingParams)]
    [Category(categoryActiveFuncs)]
    public int WavesRsdPerc {
      get {
        return _wavesRsdPec;
      }

      set {
        _wavesRsdPec = value;
      }
    }
    int _riskRewardThresh = 100;
    [WwwSetting(wwwSettingsTradingParams)]
    [Category(categoryActiveFuncs)]
    public int RiskRewardThresh {
      get {
        return _riskRewardThresh;
      }

      set {
        if(_riskRewardThresh == value)
          return;
        _riskRewardThresh = value;
        OnPropertyChanged(nameof(RiskRewardThresh));
      }
    }

    string _equinoxCorridors = "0,1,2,3";
    [Category(categoryActiveFuncs)]
    //[WwwSetting(wwwSettingsCorridorEquinox)]
    [Description("0,1,2;1,3")]
    public string EquinoxCorridors {
      get {
        return _equinoxCorridors;
      }

      set {
        if(_equinoxCorridors == value)
          return;

        _equinoxCorridors = value;

        OnPropertyChanged(() => EquinoxCorridors);
      }
    }

    private int _wwwBpa1;


    public IList<IList<int>> OutsidersInt {
      get { return SplitterInts(Outsiders); }
    }
    string _outsiders;
    [Category(categoryActiveFuncs)]
    //[WwwSetting(wwwSettingsCorridorEquinox)]
    [Description("0,1,2;1,3")]
    public string Outsiders {
      get { return _outsiders; }
      set {
        if(_outsiders == value)
          return;
        _outsiders = value;
      }
    }

    #region UseFlatTrends
    private bool _UseFlatTrends;
    [Category(categoryActiveYesNo)]
    [WwwSetting(wwwSettingsTrends)]
    public bool UseFlatTrends {
      get { return _UseFlatTrends; }
      set {
        if(_UseFlatTrends != value) {
          _UseFlatTrends = value;
          OnPropertyChanged("UseFlatTrends");
        }
      }
    }

    #endregion
    #region UseMinuteTrends
    private bool _UseMinuteTrends;
    public bool UseMinuteTrends {
      get { return _UseMinuteTrends; }
      set {
        if(_UseMinuteTrends != value) {
          _UseMinuteTrends = value;
          OnPropertyChanged(nameof(UseMinuteTrends));
        }
      }
    }

    #endregion

    private IList<IList<int>> SplitterInts(string indexesAll) {
      return (from indexes in indexesAll.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
              select SplitterInt(indexes)).ToArray();
    }
    private int[] SplitterInt(string indexes) {
      return Splitter(indexes, int.Parse);
    }
    private T[] Splitter<T>(string indexes, Func<string, T> parse) {
      return (from s in (indexes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
              select parse(s)).ToArray();
    }
    private T[] Splitter<T>(string indexes, Func<string, int, T> parse) {
      return (indexes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(parse).ToArray();
    }
    #endregion

    #endregion
    private IEnumerable<double> _edgeDiffs = new double[0];
  }
}
