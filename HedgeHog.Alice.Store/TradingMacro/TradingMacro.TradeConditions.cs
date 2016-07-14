using HedgeHog.Bars;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Dynamic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Reactive;
using System.Reactive.Linq;
using TL = HedgeHog.Bars.Rate.TrendLevels;
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

    #region Edges
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
    class SetEdgeLinesAsyncBuffer : AsyncBuffer<SetEdgeLinesAsyncBuffer, Action> {
      public SetEdgeLinesAsyncBuffer() : base() {

      }
      protected override Action PushImpl(Action context) {
        return context;
      }
    }
    LoadRateAsyncBuffer _setEdgeLinesAsyncBuffer = new LoadRateAsyncBuffer();

    public TradeConditionDelegateHide EdgesAOk {
      get {
        return () => {
          return UseRates(rates => rates.Select(_priceAvg).ToArray())
          .Select(rates => {
            _setEdgeLinesAsyncBuffer.Push(() => SetAvgLines(rates));
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
    [WwwSetting]
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
        return () => IsWaveOk((wr, tm) => wr.Distance > tm.WaveRangeAvg.Distance, BigWaveIndex, 2);
      }
    }
    public TradeConditionDelegate CalmOk {
      get {
        return () => AreWavesOk((wr, tm) =>
        wr.Distance < tm.WaveRangeAvg.Distance && wr.TotalMinutes < tm.WaveRangeAvg.TotalMinutes, 0, BigWaveIndex);
      }
    }
    public TradeConditionDelegate Calm2Ok {
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
    public TradeConditionDelegate Calm3Ok {
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
    public TradeConditionDelegate Calm4Ok {
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
    public Func<TradeDirections> TipOk {
      get {
        return () => TrendLinesBlueTrends
          .YieldIf(p => !p.IsEmpty, p => p.Slope)
          .Select(ss => {
            var tradeLevel = ss > 0 ? SellLevel.Rate : BuyLevel.Rate;
            var extream = ss > 0 ? _RatesMax : _RatesMin;
            var tip = (extream - tradeLevel).Abs();
            _tipRatioCurrent = RatesHeight / tip;
            return IsTresholdAbsOk(_tipRatioCurrent, TipRatio)
              ? TradeDirectionByAngleSign(ss)
              : TradeDirections.None;
          })
          .DefaultIfEmpty(TradeDirections.None)
          .Single();
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
          lock (_rateLocker) {
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

    #region Properties
    [Category(categoryCorridor)]
    [WwwSetting]
    public bool ResetTradeStrip {
      get { return false; }
      set {
        if(value)
          CenterOfMassSell = CenterOfMassBuy = CenterOfMassSell2 = CenterOfMassBuy2 = double.NaN;
      }
    }
    double _tradeStripJumpRatio = 1.333333;
    [Category(categoryActive)]
    [WwwSetting]
    public double TradeStripJumpRatio {
      get { return _tradeStripJumpRatio; }
      set {
        if(_tradeStripJumpRatio == value)
          return;
        _tradeStripJumpRatio = value;
        OnPropertyChanged(() => TradeStripJumpRatio);
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
      return TrendLinesBlueTrends
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

    public TradeConditionDelegateHide PigTailOk {
      get {
        Func<TL, double[]> corr = tl => new[] { tl.PriceAvg2, tl.PriceAvg3 };
        Func<double[], double[], bool> corrOk = (corr1, corr2) => corr1.All(p => p.Between(corr2));
        Func<double[], double[], bool> corrsOk = (corr1, corr2) => corrOk(corr1, corr2) || corrOk(corr2, corr1);
        return () => {
          var tls = TrendLinesTrendsAll.OrderByDescending(tl => tl.Count).Skip(1).ToArray();
          var ok = tls.Permutation().Select(c => corrsOk(corr(c.Item1), corr(c.Item2)))
          .TakeWhile(b => b)
          .Count() == tls.Length - 1;
          var td = TradeDirectionByBool(ok);
          if(!HaveTrades() && td.HasAny() && BuySellLevels.IfAnyCanTrade().Any() && BuySellLevels.Any(sr => sr.TradesCount < TradeCountStart))
            BuySellLevels.ForEach(sr => sr.TradesCount = TradeCountStart);
          return td;
        };
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
    public TradeConditionDelegate IsIn2Ok {
      get {
        return () => TradeDirectionByBool(IsCurrentPriceInsideTradeLevels2);
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
        var signs = TrendLinesTrendsAll.Where(tl => !tl.IsEmpty).Select(tl => tl.Slope.Sign()).Distinct().ToArray();
        return () => signs.Length > 1
          ? TradeDirections.None
          : TradeDirectionByAngleSign(signs.SingleOrDefault());
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
          return cUp == 2 ? TradeDirectionByAngleSign(TrendLinesBlueTrends.Slope) : TradeDirections.None;
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
          var greenIndex = TrendLinesGreenTrends.Count - TrendLinesLimeTrends.Count;
          var greenRangeMiddle = TrendLinesGreenTrends.Coeffs.RegressionValue(greenIndex);
          var greenRangeHeight = TrendLinesGreenTrends.PriceAvg2 - TrendLinesGreenTrends.PriceAvg1;
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
      return tm.WaveRanges.Take(1).Any(wr => TrendLinesBlueTrends.Slope.Sign() == wr.Slope.Sign() && wr.DistanceCma < StDevByPriceAvgInPips);
    }

    #endregion

    public TradeConditionDelegate TrdCorChgOk {
      get {
        return () => {
          return TradeDirectionByBool(BuySellLevels.HasTradeCorridorChanged());
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
          lock (_rateLocker) {
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
          (from s in TrendLinesGreenTrends.Sorted
           select TradeDirectionByBool(BuySellLevels.IfAnyCanTrade().IsEmpty() || !IsCanTradeExpired(s[0].StartDate.Subtract(s[1].StartDate).Duration()))
          ).Value;
      }
    }


    #region StDev
    public TradeConditionDelegateHide VLOk {
      get {
        Log = new Exception(new { System.Reflection.MethodBase.GetCurrentMethod().Name, TrendHeightPerc } + "");
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
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide VoltOk {
      get {
        return () => {
          return GetLastVolt(volt => volt > GetVoltageHigh()).Select(TradeDirectionByBool).SingleOrDefault();
        };
      }
    }
    public TradeConditionDelegate VltRngOk {
      get {
        return () => GetLastVolt(volt =>
          VoltRange1.IsNaN()
          ? TradeDirectionByTreshold(volt, VoltRange0)
          : VoltRange0 < VoltRange1
          ? TradeDirectionByBool(volt.Between(VoltRange0, VoltRange1))
          : TradeDirectionByBool(!volt.Between(VoltRange1, VoltRange0))
        ).SingleOrDefault();
      }
    }
    public TradeConditionDelegate VltUpOk {
      get {
        return () => TradeDirectionByBool(GetLastVolts().ToArray().LinearSlope() < 0);
      }
    }
    public TradeConditionDelegate VltDwOk {
      get {
        return () => TradeDirectionByBool(GetLastVolts().ToArray().LinearSlope() > 0);
      }
    }

    private IEnumerable<double> GetLastVolt() {
      return UseRates(rates
                  => rates.BackwardsIterator()
                  .Select(GetVoltage)
                  .SkipWhile(double.IsNaN)
                  .Take(1)
                  )
                  .SelectMany(v => v);
    }
    private IEnumerable<double> GetLastVolts() {
      return UseRates(rates
                  => rates.BackwardsIterator()
                  .Select(GetVoltage)
                  .SkipWhile(double.IsNaN)
                  .TakeWhile(Lib.IsNotNaN)
                  )
                  .SelectMany(v => v);
    }
    private IEnumerable<T> GetLastVolt<T>(Func<double, T> map) {
      return GetLastVolt().Select(map);
    }
    #endregion
    #endregion

    #region WwwInfo
    public object WwwInfo() {
      Func<Func<TradingMacro, TL>, IEnumerable<TL>> trenderLine = tl => TradingMacroTrender().Select(tl);
      return TradingMacroTrender(tm =>
        new {
          //GRB_Ratio = tm._wwwInfoGRatio,
          //RB__Ratio = tm._wwwInfoRRatio,

          StDevHght = InPips(tm.StDevByHeight).Round(1),
          //HStdRatio = (RatesHeight / (StDevByHeight * 4)).Round(1),
          //VPCorr___ = string.Join(",", _voltsPriceCorrelation.Value.Select(vp => vp.Round(2)).DefaultIfEmpty()),
          //TipRatio_ = _tipRatioCurrent.Round(1),
          LimeAngle = tm.TrendLinesLimeTrends.Angle.Round(1),
          Grn_Angle = tm.TrendLinesGreenTrends.Angle.Round(1),
          Red_Angle = tm.TrendLinesRedTrends.Angle.Round(1),
          PlumAngle = tm.TrendLinesPlumTrends.Angle.Round(1),
          BlueAngle = tm.TrendLinesBlueTrends.Angle.Round(1)
          //BlueHStd_ = TrendLines2Trends.HStdRatio.SingleOrDefault().Round(1),
          //WvDistRsd = _waveDistRsd.Round(2)
        }
        //.ToExpando()
        //.Merge(new { EqnxRatio = tm._wwwInfoEquinox }, () => TradeConditionsHave(EqnxLGRBOk))
        //.Merge(new { BPA1Tip__ = _wwwBpa1 }, () =>TradeConditionsHave(BPA12Ok))
      )
      .SingleOrDefault();
      // RhSDAvg__ = _macd2Rsd.Round(1) })
      // CmaDist__ = InPips(CmaMACD.Distances().Last()).Round(3) })
    }
    #endregion

    #region Angles
    TradeDirections TradeDirectionByTradeLevels(double buyLevel, double sellLevel) {
      return buyLevel.Avg(sellLevel).PositionRatio(_RatesMin, _RatesMax).ToPercent() > 50
        ? TradeDirections.Down
        : TradeDirections.Up;
    }
    TradeDirections TradeDirectionByAngleSign(double angle) {
      return TradeConditionsHaveTD()
        ? TradeDirections.Both
        : angle > 0
        ? TradeDirections.Both
        : angle < 0
        ? TradeDirections.Both
        : TradeDirections.None;
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
        .Select(tl =>
        IsTresholdAbsOk(tl.Angle, tradingAngleRange)
        ? tradingAngleRange > 0
        ? TradeDirectionByAngleSign(tl.Angle)
        : TradeDirections.Both
        : TradeDirections.None
        ).First();
    }

    #region Angles
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate LimeAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TrendLinesLimeTrends, TrendAngleLime); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate GreenAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TrendLinesGreenTrends, TrendAngleGreen); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate RedAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TrendLinesRedTrends, TrendAngleRed); } }
    public TradeConditionDelegate PlumAngOk { get { return () => TradeDirectionByAngleCondition(tm => tm.TrendLinesPlumTrends, TrendAnglePlum); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate BlueAngOk {
      get {
        return () => TrenderTrenLine(tm => tm.TrendLinesBlueTrends)
        .Select(tl => TrendAngleBlue1.IsNaN()
        ? TradeDirectionByAngleCondition(tm => tl, TrendAngleBlue0)
        : tl.Angle.Abs().Between(TrendAngleBlue0, TrendAngleBlue1)
        ? TradeDirectionByAngleSign(tl.Angle)
        : TradeDirections.None
        ).First();
      }
    }

    private IEnumerable<TL> TrenderTrenLine(Func<TradingMacro, TL> map) {
      return TradingMacroTrender().Select(map);
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
        Func<IList<Rate>[]> rates = () => UseRates(ra => ra.GetRange(0, ra.Count - TrendLinesLimeTrends.Count));
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
          var isUp = TrendLinesBlueTrends.Slope > 0;
          var avg1 = TrendLinesBlueTrends.PriceAvg1;
          (isUp ? setUp : setDown)();
          return isUp ? max().Any(m => avg1 > m) : min().Any(m => avg1 < m);
        };
        return () => TradeDirectionByBool(isOk());
      }
    }
    public TradeConditionDelegate BPA12Ok {
      get {
        Log = new Exception(new { BPA12Ok = new { TipRatio, InPercent = true, Treshold = false } } + "");
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
          var isUp = TrendLinesBlueTrends.Slope > 0;
          var avg1 = TrendLinesBlueTrends.PriceAvg1;
          return _wwwBpa1 = ((isUp ? avg1 - _RatesMax : _RatesMin - avg1) / (_RatesMax - _RatesMin)).ToPercent();
        };
        return () => TradeDirectionByBool(isOk() > TipRatio);
      }
    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegate PA23Ok {
      get {
        return () => {
          SetTradelevelsDirectional(SellLevel, BuyLevel);
          return TradeDirections.Both;
        };
      }
    }
    [TradeConditionSetCorridor]
    public TradeConditionDelegate PA23ROk {
      get {
        return () => {
          SetTradelevelsDirectional(BuyLevel, SellLevel);
          return TradeDirections.Both;
        };
      }
    }
    void SetTradelevelsDirectional(SuppRes srPA2, SuppRes srPA3) {
      Action<SuppRes> up3 = sr => sr.RateEx = sr.Rate.Max(TradeLevelByPA3(2));
      Action<SuppRes> up2 = sr => sr.RateEx = sr.Rate.Max(TradeLevelByPA2(2));
      Action<SuppRes> down3 = sr => sr.RateEx = sr.Rate.Min(TradeLevelByPA3(2));
      Action<SuppRes> down2 = sr => sr.RateEx = sr.Rate.Min(TradeLevelByPA2(2));
      Action setUp = () => {
        up3(srPA3);
        up2(srPA2);
      };
      Action setDown = () => {
        down3(srPA3);
        down2(srPA2);
      };
      var isUp = TrendLinesBlueTrends.Slope > 0;
      var avg1 = TrendLinesBlueTrends.PriceAvg1;
      (isUp ? setUp : setDown)();
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
          var isUp = TrendLinesBlueTrends.Slope > 0;
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
          var level = TrendLinesBlueTrends.PriceAvg1;
          var isUp = level.PositionRatio(range.down, range.up) > 0.5;
          var height = TrendLinesGreenTrends.StDev;
          var levelUp = isUp ? range.up + height : range.down + height;
          var levelDown = isUp ? range.up - height : range.down - height;
          CenterOfMassBuy = levelUp;
          CenterOfMassSell = levelDown;
          return isUp ? level >= levelDown : level <= levelUp;
        });
        return () => TradeDirectionByBool(func());
      }
    }

    TradeDirections TradeDirectionsAnglewise(TL tl) {
      return tl.Slope < 0 ? TradeDirections.Down : TradeDirections.Up;
    }
    TradeDirections TradeDirectionsAnglecounterwise(TL tl) {
      return tl.Slope > 0 ? TradeDirections.Down : TradeDirections.Up;
    }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate BothOk { get { return () => TradeDirections.Both; } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate BOk { get { return () => TradeDirectionsAnglecounterwise(TrendLinesBlueTrends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate BROk { get { return () => TradeDirectionsAnglewise(TrendLinesBlueTrends); } }

    [TradeConditionTradeDirection]
    public TradeConditionDelegate POk { get { return () => TradeDirectionsAnglecounterwise(TrendLinesPlumTrends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate PROk { get { return () => TradeDirectionsAnglewise(TrendLinesPlumTrends); } }


    [TradeConditionTradeDirection]
    public TradeConditionDelegate ROk { get { return () => TradeDirectionsAnglecounterwise(TrendLinesRedTrends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate RROk { get { return () => TradeDirectionsAnglewise(TrendLinesRedTrends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate GOk { get { return () => TradeDirectionsAnglecounterwise(TrendLinesGreenTrends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate GROk { get { return () => TradeDirectionsAnglewise(TrendLinesGreenTrends); } }
    #endregion

    #region Trends Ratios
    public TradeConditionDelegate EqnxLGRBOk {
      get {
        Log = new Exception(new { EqnxPureOk = new { EquinoxPerc, EquinoxCorridors } } + "");
        Func<double, IEnumerable<int>> setWww = i => TradingMacroTrender().Select(tm => tm._wwwInfoEquinox = i.ToInt());

        Func<IEnumerable<int>> exec = EquinoxCalcFunc();
        return () => exec().SelectMany(d => setWww(d).Select(i => TradeDirectionByTreshold(i, EquinoxPerc))).FirstOrDefault();
      }
    }

    private Func<IEnumerable<int>> EquinoxCalcFunc() {

      Func<IEnumerable<int>> exec = () => EquinoxValues(tm => tm.EquinoxTrendLines)
        .Select(t => t.Item1)
        .Take(1);
      return exec;
    }

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
      var height = trendLines.Max(tl => tl.PriceAvg2 - tl.PriceAvg3);
      return EdgesDiff2(trendLines, paFuncs, waveRanges)
        .OrderBy(pa => pa.Item1)
        .Select(x => Tuple.Create((x.Item1 / height).ToPercent(), x.Item2));
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
    public TradeConditionDelegate M1SDOk {
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
    public TradeConditionDelegate M1DOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, wr => wr.Distance);
      }
    }
    public TradeConditionDelegate M1D_Ok {
      get {
        return () => WaveConditions((d1, d2) => d1 < d2, wr => wr.Distance, wr => wr.TotalMinutes);
      }
    }
    public TradeConditionDelegate M1SOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, (d1, d2) => d1 < d2, wr => wr.StDev);
      }
    }
    public TradeConditionDelegate M1PAOk {
      get {
        return () => WaveConditions((d1, d2) => d1 > d2, wr => wr.PipsPerMinute, wr => wr.Angle.Abs());
      }
    }
    public TradeConditionDelegate M1PA_Ok {
      get {
        return () => WaveConditions((d1, d2) => d1 < d2, wr => wr.PipsPerMinute, wr => wr.Angle.Abs());
      }
    }
    public TradeConditionDelegate M1P_Ok {
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
          TrendLinesGreenTrends.PriceAvg2 >= TrendLinesRedTrends.PriceAvg2.Max(TrendLinesBlueTrends.PriceAvg2)
          ? getTD(TradeDirections.Down)
          : TrendLinesGreenTrends.PriceAvg3 <= TrendLinesRedTrends.PriceAvg3.Min(TrendLinesBlueTrends.PriceAvg3)
          ? getTD(TradeDirections.Up)
          : TradeDirections.None;
      }
    }
    public TradeConditionDelegateHide GreenExtOk {
      get {
        Func<TradeDirections, TradeDirections> getTD = td => (TradeConditionsHaveTD() ? TradeDirections.Both : td);
        return () =>
          TrendLinesGreenTrends.PriceAvg2 >= TrendLinesRedTrends.PriceAvg21.Max(TrendLinesBlueTrends.PriceAvg2)
          ? getTD(TradeDirections.Up)
          : TrendLinesGreenTrends.PriceAvg3 <= TrendLinesRedTrends.PriceAvg31.Min(TrendLinesBlueTrends.PriceAvg3)
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
            var h = (max - min) / 6;
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
      return tm.OutsidersInt.ToArray(i => i.Select(i2 => tm.TrendLinesTrendsAll[i2]));
    }

    #region Helpers
    private void ScanOutsideEquinox() {
      EquinoxBasedMinMax(OutsiderTLs(this))
        .ForEach(t => {
          //_outsideMin = t.Item1;
          //_outsideMax = t.Item2;
        });
    }

    private void ScanTradeEquinox() {
      EquinoxBasedMinMax(EquinoxTrendLines)
        .ForEach(t => {
          //_tradeEquinoxMin = t.Item1;
          //_tradeEquinoxMax = t.Item2;
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
    public IEnumerable<TradingMacro> TradingMacrosByPair() {
      return _tradingMacros.Where(tm => tm.Pair == Pair).OrderBy(tm => PairIndex);
    }
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

    void BounceStrategy(object sender, SuppRes.CrossedEvetArgs e) {
      var supRes = (SuppRes)sender;
      var td = TradeConditionsEval().FirstOrDefault();
      var tradeLevelSet = MonoidsCore.ToFunc(double.NaN, false, (rate, canTrade) => new { rate, canTrade });
      var tradeLevelsSet0 = MonoidsCore.ToFunc(tradeLevelSet(0, false), tradeLevelSet(0, false), (buy, sell) => new { buy, sell });
      var tradeLevelsSet = MonoidsCore.ToFunc(0.0, false, 0.0, false, (br, bct, sr, sct) => tradeLevelsSet0(tradeLevelSet(br, bct), tradeLevelSet(sr, sct)));
      var setLevel = MonoidsCore.ToFunc((SuppRes)null, tradeLevelSet(0, false), (sr, l) => {
        sr.Rate = l.rate;
        sr.CanTrade = l.canTrade;
        sr.TradesCount = 0;
        return true;
      });
      var setTLs = MonoidsCore.ToFunc(() =>
        new[] { supRes.IsBuy && e.Direction == 1 && td.HasDown()
        ? tradeLevelsSet(supRes.Rate, false, RatesMin, true)
        : supRes.IsSell && e.Direction == -1 && td.HasUp()
        ? tradeLevelsSet(RatesMax, true, supRes.Rate, false)
        : null}.Where(x => x != null).ToArray()
      );
      setTLs()
        .ForEach(x => {
          var tlHeight = x.buy.rate.Abs(x.sell.rate);
          _tipRatioCurrent = RatesHeightCma / tlHeight;
          var tipOk = IsTresholdAbsOk(_tipRatioCurrent, TipRatio);
          if(tipOk) {
            setLevel(BuyLevel, x.buy);
            setLevel(SellLevel, x.sell);
          }
        });
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
    IEnumerable<TradeDirections> TradeConditionsTradeStrip() {
      return TradeConditionsInfo<TradeConditionTradeStripAttribute>().Select(d => d());
    }
    bool TradeConditionsHaveTD() {
      return TradeConditionsInfo<TradeConditionTradeDirectionAttribute>().Any();
    }
    bool TradeConditionsHaveAsleep() {
      return TradeConditionsInfo<TradeConditionAsleepAttribute>().Any(d => d().HasNone()) || !IsTradingDay();
    }
    bool TradeConditionsHave(TradeConditionDelegate td) {
      return TradeConditionsInfo().Any(d => d.Method == td.Method);
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
    public void TradeConditionsTrigger() {
      if(IsAsleep || !IsTradingTime()) {
        BuySellLevels.ForEach(sr => {
          sr.CanTrade = false;
          sr.InManual = false;
          sr.TradesCount = 0;
        });
        return;
      }
      if(IsTrader && CanTriggerTradeDirection() && !HaveTrades() /*&& !HasTradeDirectionTriggers*/) {
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
            BuyLevel.CanTrade = hasBuy && isPriceIn;
            SellLevel.CanTrade = hasSell && isPriceIn;
          }

          if(eval.HasAny())
            BuySellLevels.ForEach(sr => sr.DateCanTrade = ServerTime);
        });
      }
    }
    bool _isTurnOnOnly = false;
    private double _waveDistRsd;

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
    public IEnumerableCore.Singleable<TradeDirections> TradeConditionsEval() {
      if(!IsTrader)
        return new TradeDirections[0].AsSingleable();
      return (from tc in TradeConditionsInfo((d, p, t, s) => new { d, t, s })
              group tc by tc.t into gtci
              let and = gtci.Select(g => g.d()).ToArray()
              let c = gtci.Key == TradeConditionAttribute.Types.And
              ? and.Aggregate(TradeDirections.Both, (a, td) => a & td)
              : and.Aggregate(TradeDirections.None, (a, td) => a | td)
              select c
              )
              .Scan(TradeDirections.Both, (a, td) => a & td)
              .TakeLast(1)
              .Select(td => td & TradeDirection)
              .AsSingleable();
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
    private int _wwwInfoGRatio;
    private int _wwwInfoEquinox;
    private int _wwwInfoRRatio;

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

    string _equinoxCorridors = "0,1,2,3";
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsCorridorEquinox)]
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

    private IList<IEnumerable<TL>> EquinoxTrendLinesCalcAll(string indexesAll) {
      return (from indexes in indexesAll.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
              select EquinoxTrendLinesCalc(indexes))
              .Where(tls => !tls.IsEmpty())
              .ToArray();
    }
    private IEnumerable<TL> EquinoxTrendLinesCalc(string indexes) {
      return (from i in SplitterInt(indexes)
              let tl = TrendLinesTrendsAll[i]
              where !tl.IsEmpty
              select tl);
    }

    private int _wwwBpa1;

    IList<IEnumerable<TL>> EquinoxTrendLines {
      get {
        return  EquinoxTrendLinesCalcAll(_equinoxCorridors);
      }
    }

    public IList<IList<int>> OutsidersInt {
      get { return SplitterInts(Outsiders); }
    }
    string _outsiders;
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsCorridorEquinox)]
    [Description("0,1,2;1,3")]
    public string Outsiders {
      get { return _outsiders; }
      set {
        if(_outsiders == value)
          return;
        _outsiders = value;
      }
    }

    string _tradeTrends = "0,1,2,3";
    [Category(categoryActiveFuncs)]
    [WwwSetting(wwwSettingsTradingParams)]
    [Description("0,1,2")]
    public string TradeTrends {
      get { return _tradeTrends; }
      set {
        if(_tradeTrends == value)
          return;
        _tradeTrends = value;
        TradeTrendsInt = SplitterInt(value);
      }
    }

    int[] _tradeTrendsInt;
    public int[] TradeTrendsInt {
      get { return _tradeTrendsInt ?? (_tradeTrendsInt = SplitterInt(TradeTrends)); }
      set { _tradeTrendsInt = value; }
    }

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
  }
}
