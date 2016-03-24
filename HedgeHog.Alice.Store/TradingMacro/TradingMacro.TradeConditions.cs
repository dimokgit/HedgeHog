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
    private bool IsCurrentPriceInsideTradeLevels4 {
      get {
        Func<double, double[], bool> isIn = (v, levels) => {
          var h = levels.Height() / 3;
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
          DoSetTradeStrip = false;
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
          DoSetTradeStrip = false;
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
    public TradeConditionDelegateHide BigWaveOk {
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
    #endregion

    #region Trade Corridor and Directions conditions
    #region Tip COnditions

    [TradeConditionTurnOff]
    public Func<TradeDirections> TipOk {
      get {
        return () => TrendLines2Trends
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
          _tipRatioCurrent = _ratesHeightCma / BuyLevel.Rate.Abs(SellLevel.Rate);
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
          _tipRatioCurrent = _ratesHeightCma / BuyLevel.Rate.Abs(SellLevel.Rate);
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
    [TradeConditionSetCorridor]
    public TradeConditionDelegateHide TradeSlideOk {
      get {
        int sell = 1, buy = 0;
        Func<Rate.TrendLevels> baseTL = () => TrendLines2Trends;
        var getTradeLevels = MonoidsCore.ToFunc(() => {
          var h = GetTradeLevel(true, double.NaN).Abs(GetTradeLevel(false, double.NaN)) / 2;
          var mean = baseTL().PriceAvg1;
          return new[] { mean + h, mean - h };
        });
        var getTradeLevels2 = MonoidsCore.ToFunc((Rate.TrendLevels)null, tl => {
          var sign = tl.Slope;
          var baseLevel = tl.PriceAvg1;
          var buyLevel = sign > 0 ? GetTradeLevel(true, double.NaN) : baseLevel;
          var sellLevel = sign < 0 ? GetTradeLevel(false, double.NaN) : baseLevel;
          return new[] { buyLevel, sellLevel };
        });
        return () => {
          var tradeLevels = getTradeLevels2(baseTL());
          var blueLevles = GetTradeLevelsToTradeStrip();
          _tipRatioCurrent = TradeLevelsEdgeRatio(tradeLevels).Min(_ratesHeightCma / tradeLevels.Height());
          var tipRatioOk = IsTresholdAbsOk(_tipRatioCurrent, TipRatio);
          if(!tipRatioOk)
            BuySellLevelsForEach(sr => sr.CanTradeEx = false);
          CanTrade_TurnOffByAngle(baseTL());
          var td = TradeDirectionByBool(tipRatioOk);
          if(td.HasAny()) {
            BuyLevel.RateEx = tradeLevels[buy];
            SellLevel.RateEx = tradeLevels[sell];
          }
          return td;
        };
      }
    }

    private void CanTrade_TurnOffByAngle(Rate.TrendLevels tl) {
      if(tl.Slope < 0 && SellLevel.CanTrade)
        SellLevel.CanTrade = false;
      if(tl.Slope > 0 && BuyLevel.CanTrade)
        BuyLevel.CanTrade = false;
    }

    [TradeConditionSetCorridor]
    public TradeConditionDelegateHide TradeStripOk {
      get {
        return () => {
          var canSetStrip = true;// Trades.IsEmpty();
          var tradeLevles = GetTradeLevelsToTradeStrip();
          _tipRatioCurrent = TradeLevelsEdgeRatio(tradeLevles).Min(_ratesHeightCma / tradeLevles.Height());
          var tipRatioOk = IsTresholdAbsOk(_tipRatioCurrent, TipRatio);
          if(!tipRatioOk)
            BuySellLevelsForEach(sr => sr.CanTradeEx = false);
          var td = TradeDirectionByBool(IsCurrentPriceInsideBlueStrip && tipRatioOk);
          if(canSetStrip && td.HasAny())
            SetTradeLevelsToTradeStrip();
          return td;
        };
      }
    }
    private double TradeLevelsEdgeRatio(double[] tradeLevles) {
      return TrendLines2Trends
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
    [WwwSetting(wwwSettingsTradingConditions)]
    public double TipRatio {
      get { return _tipRatio; }
      set {
        _tipRatio = value;
        OnPropertyChanged(() => TipRatio);
      }
    }

    #endregion

    #region After Tip
    public TradeConditionDelegate IsInOk {
      get {
        return () => TradeDirectionByBool(IsCurrentPriceInsideTradeLevels);
      }
    }

    [TradeConditionTurnOff]
    [TradeConditionByRatio]
    public TradeConditionDelegateHide TCbVOk {
      get {
        //TradeConditionsRemoveExcept<TradeConditionByRatioAttribute>(TCbVOk);
        Log = new Exception(new { TCbVOk = new { RhSDRatio } } + "");
        return () => TradeDirectionByBool(IsTresholdAbsOk(_tipRatioCurrent = InPips(BuyLevel.Rate.Abs(SellLevel.Rate)) / GetVoltageHigh(), RhSDRatio));
      }
    }

    public TradeConditionDelegate PigTailOk {
      get {
        Func<Rate.TrendLevels, double[]> corr = tl => new[] { tl.PriceAvg2, tl.PriceAvg3 };
        Func<double[], double[], bool> corrOk = (corr1, corr2) => corr1.All(p => p.Between(corr2));
        Func<double[], double[], bool> corrsOk = (corr1, corr2) => corrOk(corr1, corr2) || corrOk(corr2, corr1);
        return () => {
          var tls = TrendLinesTrendsAll.OrderByDescending(tl => tl.Count).Skip(1).ToArray();
          var ok = tls.Permutation().Select(c => corrsOk(corr(c[0]), corr(c[1])))
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

    public TradeConditionDelegate IsIn2Ok {
      get {
        return () => TradeDirectionByBool(IsCurrentPriceInsideTradeLevels2);
      }
    }
    public TradeConditionDelegate IsIn3Ok {
      get {
        return () => TradeDirectionByBool(IsCurrentPriceInsideTradeLevels4);
      }
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegateHide IsLimieOk {
      get {
        return () => IsLimie();
      }
    }
    public TradeDirections IsLimie() {
      var tlCount = TrendLines0Trends.Count;
      return UseRates(rates => rates.GetRange(rates.Count - tlCount, tlCount)).Select(range => {
        var minMax = range.Select((r, i) => new { r, i }).MinMaxBy(x => x.r.PriceAvg);

        var fibRange = Fibonacci.Levels(minMax[1].r.AskHigh, minMax[0].r.BidLow).Skip(4).Take(2).ToArray();
        CenterOfMassBuy = fibRange[1];
        CenterOfMassSell = fibRange[0];

        double buy, sell;
        var td = TradeDirections.None;
        var isUp = minMax[0].r.StartDate < minMax[1].r.StartDate;
        if(isUp) {
          buy = minMax[1].r.AskHigh;
          sell = range.GetRange(range.Count - minMax[1].i).Select(r => r.BidLow).DefaultIfEmpty(minMax[0].r.BidLow).Min();
          if(sell.Between(fibRange))
            td = TradeDirections.Both;
        } else {
          sell = minMax[0].r.BidLow;
          buy = range.GetRange(range.Count - minMax[0].i).Select(r => r.AskHigh).DefaultIfEmpty(minMax[1].r.AskHigh).Max();
          if(buy.Between(fibRange))
            td = TradeDirections.Both;
        }
        td = td & TradeDirectionByBool(InPips(buy.Abs(sell)) > GetVoltageHigh());

        if(BuyLevel.RateEx != buy && SellLevel.RateEx != sell)
          BuySellLevels.ForEach(sr => sr.CanTradeEx = false);

        //var cpTd = TradeDirectionByBool(!CurrentEnterPrices(cp => !cp.Between(fibRange)).Any());
        if(td.HasAny() && !BuySellLevels.Any(sr => sr.CanTrade)) {
          BuyLevel.RateEx = buy;
          SellLevel.RateEx = sell;
        }

        return td;
      })
      .DefaultIfEmpty()
      .Single();
    }


    [TradeConditionTurnOff]
    public TradeConditionDelegateHide ToStripOk {
      get {
        #region Locals
        Action setGreenStrip = () => {
          if(CenterOfMassSell2.IsNaN() & !TrendLines2Trends.IsEmpty) {
            var ofset = TrendLines2.Value[0].Trends.PriceAvg1 - TrendLines2.Value[1].Trends.PriceAvg1;
            CenterOfMassBuy2 = CenterOfMassBuy + ofset;
            CenterOfMassSell2 = CenterOfMassSell + ofset;
          }
        };
        var maxMin = MonoidsCore.ToFunc(0.0, 0.0, (max, min) => new { max, min });
        var maxMinFunc = MonoidsCore.ToFunc(() => new[] { maxMin(CenterOfMassBuy, CenterOfMassSell), maxMin(CenterOfMassBuy2, CenterOfMassSell2) });
        var isUp = MonoidsCore.ToFunc(0.0, maxMin(0, 0), maxMin(0, 0), (cp, mm, mm2) => {
          return mm2.min < mm.max || cp > mm.max
          ? TradeDirections.None
          : TradeDirections.Up;
        });
        var hasNaN = MonoidsCore.ToFunc(() => new[] { CenterOfMassBuy, CenterOfMassSell, CenterOfMassBuy2, CenterOfMassSell2 }.Any(double.IsNaN));
        var isDown = MonoidsCore.ToFunc(0.0, maxMin(0, 0), maxMin(0, 0), (cp, mm, mm2) => {
          return mm2.max > mm.min || cp < mm.min
          ? TradeDirections.None
          : TradeDirections.Down;
        });
        var conds = new[] { isUp, isDown };
        #endregion
        return () => {
          setGreenStrip();
          var maxMins = maxMinFunc();
          var cp = CurrentPrice.Average;
          var evals = conds.SelectMany(ud => new[] { ud(cp, maxMins[0], maxMins[1]), ud(cp, maxMins[1], maxMins[0]) });
          try {
            return hasNaN() ? TradeDirections.None : evals.SingleOrDefault(eval => eval.HasAny());
          } catch(Exception exc) {
            Log = exc;
            throw;
          }
        };
      }
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegateHide CorlOk {
      get {
        return () => TradeDirectionByBool(TradingMacroOther().Any(tm => IsFathessOk(tm)));
      }
    }
    [TradeConditionTurnOff]
    public TradeConditionDelegateHide WvLenOk {
      get {
        return () => TradeDirectionByBool(TradingMacroOther().Any(tm => IsDistanceCmaOk(tm)));
      }
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegate AnglesOk {
      get {
        Func<Func<Rate.TrendLevels, bool>, Func<Rate.TrendLevels, Rate.TrendLevels, bool>, TradeDirections, TradeDirections> aok = (signPredicate, slopeCompare, success) => {
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

    public TradeConditionDelegate UniAngleOk {
      get {
        var signs = TrendLinesTrendsAll.Where(tl => !tl.IsEmpty).Select(tl => tl.Slope.Sign()).Distinct().ToArray();
        return () => signs.Length > 1
          ? TradeDirections.None
          : TradeDirectionByAngleSign(signs.SingleOrDefault());
      }
    }
    [TradeConditionTurnOff]
    public TradeConditionDelegate HangOk {
      get {
        Func<Func<Rate.TrendLevels, bool>, Func<Rate.TrendLevels, Rate.TrendLevels, bool>, TradeDirections> aok = (signPredicate, slopeCompare) => {
          var cUp = TrendLinesTrendsAll
            .OrderByDescending(tl => tl.Count)
            .Take(3)
            .Where(signPredicate)
            .Scan((tlp, tln) => slopeCompare(tlp, tln) ? tln : Rate.TrendLevels.Empty)
            .TakeWhile(tl => !tl.IsEmpty)
            .Count();
          return cUp == 2 ? TradeDirectionByAngleSign(TrendLines2Trends.Slope) : TradeDirections.None;
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
          var greenIndex = TrendLines1Trends.Count - TrendLines0Trends.Count;
          var greenRangeMiddle = TrendLines1Trends.Coeffs.RegressionValue(greenIndex);
          var greenRangeHeight = TrendLines1Trends.PriceAvg2 - TrendLines1Trends.PriceAvg1;
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
      return tm.WaveRanges.Take(1).Any(wr => TrendLines2Trends.Slope.Sign() == wr.Slope.Sign() && wr.DistanceCma < StDevByPriceAvgInPips);
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
    bool _doSetTradeStrip = true;
    [Category(categoryActiveYesNo)]
    [WwwSetting]
    public bool DoSetTradeStrip {
      get { return _doSetTradeStrip; }
      set {
        _doSetTradeStrip = value;
        OnPropertyChanged(() => DoSetTradeStrip);
      }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public double SetTradeStrip() {
      if(DoSetTradeStrip && CanTriggerTradeDirection()) {
        var tlbuy = TradeLevelFuncs[TradeLevelBy.PriceMax]();// GetTradeLevel(true, double.NaN);//TradeLevelFuncs[TradeLevelBy.PriceMax]();
        var tlSell = TradeLevelFuncs[TradeLevelBy.PriceMin]();// GetTradeLevel(false, double.NaN);//TradeLevelFuncs[TradeLevelBy.PriceMin]();
        var bsHeight = CenterOfMassBuy.Abs(CenterOfMassSell).IfNaN(double.MaxValue);
        var tlHeight = tlbuy.Abs(tlSell);
        var tlJumped = !GetTradeLevelsToTradeStrip().DoSetsOverlap(tlSell, tlbuy)
          && tlHeight.Div(bsHeight.Max(StDevByHeight)) <= TradeStripJumpRatio;
        if(tlJumped) {
          CenterOfMassBuy2 = CenterOfMassBuy;
          CenterOfMassSell2 = CenterOfMassSell;
          BuySellLevelsForEach(sr => sr.CanTradeEx = false);
        }
        if(tlJumped || tlHeight < bsHeight) {
          CenterOfMassBuy = tlbuy.Max(tlSell);
          CenterOfMassSell = tlSell.Min(tlbuy);
        }
        return CenterOfMassBuy.Abs(CenterOfMassSell);
      }
      return double.NaN;
    }

    private void SetTradeLevelsToTradeStrip() {
      BuyLevel.RateEx = CenterOfMassBuy.IfNaN(GetTradeLevel(true, BuyLevel.Rate));
      SellLevel.RateEx = CenterOfMassSell.IfNaN(GetTradeLevel(false, SellLevel.Rate));
    }
    private double[] GetTradeLevelsToTradeStrip() {
      return new[] { CenterOfMassBuy, CenterOfMassSell };
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
          (from s in TrendLines1Trends.Sorted
           select TradeDirectionByBool(BuySellLevels.IfAnyCanTrade().IsEmpty() || !IsCanTradeExpired(s[0].StartDate.Subtract(s[1].StartDate).Duration()))
          ).Value;
      }
    }


    #region StDev
    public TradeConditionDelegate VLOk {
      get {
        Log = new Exception(new { System.Reflection.MethodBase.GetCurrentMethod().Name, TrendHeightPerc } + "");
        return () => { return TradeDirectionByTreshold(GetVoltageAverage(), TrendHeightPerc); };
      }
    }
    public TradeConditionDelegate HStdOk {
      get {
        Log = new Exception(new { System.Reflection.MethodBase.GetCurrentMethod().Name, RhSDRatio } + "");
        return () => { return TradeDirectionByTreshold(RatesHeight / (StDevByHeight * 4), RhSDRatio); };
      }
    }


    double _rhsdRatio = 0.4;
    [Category(categoryActive)]
    [WwwSetting(wwwSettingsTradingConditions)]
    public double RhSDRatio {
      get {
        return _rhsdRatio;
      }

      set {
        _rhsdRatio = value;
        OnPropertyChanged(() => RhSDRatio);
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
          var volt = GetLastVolt();
          return TradeDirectionByBool(volt < GetVoltageAverage());
        };
      }
    }
    public TradeConditionDelegate VltBlwLOk {
      get { return () => { return VoltsBelowByTrendLines(TrendLines0Trends); }; }
    }
    public TradeConditionDelegate VltBlwGOk {
      get { return () => { return VoltsBelowByTrendLines(TrendLines1Trends); }; }
    }
    public TradeConditionDelegate VltBlwROk {
      get { return () => { return VoltsBelowByTrendLines(TrendLinesTrends); }; }
    }

    private TradeDirections VoltsBelowByTrendLines(Rate.TrendLevels tls) {
      var d = RatesArray[RatesArray.Count - tls.Count].StartDate;
      var volt = _SetVoltsByStd.SkipWhile(t => t.Item1 < d).Select(t => t.Item2).DefaultIfEmpty(double.NaN).Min();
      return TradeDirectionByBool(volt < GetVoltageAverage());
    }

    [TradeConditionTurnOff]
    public TradeConditionDelegate VoltAboveOk {
      get {
        return () => {
          var volt = GetLastVolt();
          return TradeDirectionByBool(volt > GetVoltageHigh());
        };
      }
    }

    private double GetLastVolt() {
      return UseRates(rates
                  => rates.BackwardsIterator()
                  .Select(GetVoltage)
                  .SkipWhile(double.IsNaN)
                  .DefaultIfEmpty(double.NaN)
                  .First()
                  )
                  .DefaultIfEmpty(double.NaN)
                  .Single();
    }
    #endregion

    public TradeConditionDelegateHide CmaRsdOk {
      get {
        return () => TradeDirectionByBool(IsTresholdAbsOk(MacdRsdAvg, MacdRsdAvgLevel));
      }
    }
    #endregion

    #region WwwInfo
    public object WwwInfo() {
      return new {
        GRBHRatio = TrendHeighRatioGRB(),
        HStdRatio = (RatesHeight / (StDevByHeight * 4)).Round(1),
        VPCorr___ = string.Join(",", _voltsPriceCorrelation.Value.Select(vp => vp.Round(2)).DefaultIfEmpty()),
        //TipRatio_ = _tipRatioCurrent.Round(1),
        LimeAngle = TrendLines0Trends.Angle.Round(1),
        GrnAngle_ = TrendLines1Trends.Angle.Round(1),
        RedAngle_ = TrendLinesTrends.Angle.Round(1),
        BlueAngle = TrendLines2Trends.Angle.Round(1)
      };
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
        ? TradeDirections.Down
        : angle < 0
        ? TradeDirections.Up
        : TradeDirections.None;
    }
    TradeDirections TradeDirectionByBool(bool value) {
      return value
        ? TradeDirections.Both
        : TradeDirections.None;
    }
    TradeDirections TradeDirectionByTreshold(double value, double treshold) {
      return TradeDirectionByBool(IsTresholdAbsOk(value, treshold));
    }
    TradeDirections TradeDirectionByAngleCondition(Rate.TrendLevels tls, double tradingAngleRange) {
      return IsTresholdAbsOk(tls.Angle, tradingAngleRange)
        ? tradingAngleRange > 0
        ? TradeDirectionByAngleSign(tls.Angle)
        : TradeDirections.Both
        : TradeDirections.None;
    }

    #region Angles
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate LimeAngOk { get { return () => TradeDirectionByAngleCondition(TrendLines0Trends, TrendAngleLime); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate GreenAngOk { get { return () => TradeDirectionByAngleCondition(TrendLines1Trends, TrendAngleGreen); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate RedAngOk { get { return () => TradeDirectionByAngleCondition(TrendLinesTrends, TrendAngleRed); } }
    [TradeCondition(TradeConditionAttribute.Types.And)]
    public TradeConditionDelegate BlueAngOk {
      get {
        return () => TrendAngleBlue1.IsNaN()
        ? TradeDirectionByAngleCondition(TrendLines2Trends, TrendAngleBlue0)
        : TrendLines2Trends.Angle.Abs().Between(TrendAngleBlue0, TrendAngleBlue1)
        ? TradeDirectionByAngleSign(TrendLines2Trends.Angle)
        : TradeDirections.None;
      }
    }
    [TradeConditionTurnOff]
    public TradeConditionDelegate BlueAngTOOk {
      get {
        return () => BlueAngOk();
      }
    }

    TradeDirections TradeDirectionsAnglewise(Rate.TrendLevels tl) {
      return tl.Slope < 0 ? TradeDirections.Down : TradeDirections.Up;
    }
    TradeDirections TradeDirectionsAnglecounterwise(Rate.TrendLevels tl) {
      return tl.Slope > 0 ? TradeDirections.Down : TradeDirections.Up;
    }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate BOk { get { return () => TradeDirectionsAnglecounterwise(TrendLines2Trends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate BROk { get { return () => TradeDirectionsAnglewise(TrendLines2Trends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate GOk { get { return () => TradeDirectionsAnglecounterwise(TrendLines1Trends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate GROk { get { return () => TradeDirectionsAnglewise(TrendLines1Trends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate LOk { get { return () => TradeDirectionsAnglecounterwise(TrendLines0Trends); } }
    [TradeConditionTradeDirection]
    public TradeConditionDelegate LROk { get { return () => TradeDirectionsAnglewise(TrendLines0Trends); } }
    #endregion

    int TrendHeighRatioGRB() { return TrendHeighRatio(1, 0); }
    int TrendHeighRatioLGR() { return TrendHeighRatio(0, 1); }
    int TrendHeighRatio(int skip, int skipLast) {
      Func<IList<Rate.TrendLevels>, double> spread = tls => tls[0].StDev.Percentage(tls[1].StDev);
      return TrendsPermutationsAvgPerc(TrendLinesTrendsAll.Skip(skip).SkipLast(skipLast).ToArray(), spread);
    }
    private int TrendsPermutationsAvgPerc(IList<Rate.TrendLevels> tls, Func<IList<Rate.TrendLevels>, double> spread) {
      return tls.Permutation().Select(spread).Average().ToPercent();
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
    public TradeConditionDelegate TimeFrameOk {
      get {
        return () => TestTimeFrame()
        ? TradeDirections.Both
        : TradeDirections.None;
        ;
      }
    }
    #endregion

    #region Outsides
    [Description("'Green' corridor is outside the 'Red' and 'Blue' ones")]
    public TradeConditionDelegateHide GreenOk {
      get {
        return () =>
          TrendLines1Trends.PriceAvg2 >= TrendLinesTrends.PriceAvg2.Max(TrendLines2Trends.PriceAvg2)
          ? TradeDirections.Down
          : TrendLines1Trends.PriceAvg3 <= TrendLinesTrends.PriceAvg3.Min(TrendLines2Trends.PriceAvg3)
          ? TradeDirections.Up
          : TradeDirections.None;
      }
    }
    public TradeConditionDelegateHide GreenExtOk {
      get {
        return () =>
          TrendLines1Trends.PriceAvg2 >= TrendLinesTrends.PriceAvg21.Max(TrendLines2Trends.PriceAvg2)
          ? TradeDirections.Up
          : TrendLines1Trends.PriceAvg3 <= TrendLinesTrends.PriceAvg31.Min(TrendLines2Trends.PriceAvg3)
          ? TradeDirections.Down
          : TradeDirections.None;
      }
    }
    #region Outsiders
    [TradeConditionAsleep]
    public TradeConditionDelegateHide OutsideAnyOk {
      get { return () => Outside1Ok() | OutsideOk() | Outside2Ok(); }
    }
    TradeDirections TradeDirectionByAll(params TradeDirections[] tradeDirections) {
      return tradeDirections
        .Where(td => td.HasAny())
        .Buffer(3)
        .Where(b => b.Count == 3 && b.Distinct().Count() == 1)
        .Select(b => b[2])
        .DefaultIfEmpty(TradeDirections.None)
        .Single();
    }
    [TradeConditionAsleep]
    public TradeConditionDelegateHide OutsideAllOk {
      [TradeCondition(TradeConditionAttribute.Types.Or)]
      get {
        return () =>
          IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLinesTrends, tl => tl.PriceAvg31, tl => tl.PriceAvg21, IsReverseStrategy).HasAny()
          ? TradeDirections.None
          : TradeDirectionByAll(Outside1Ok(), OutsideOk(), Outside2Ok());
      }
    }
    private bool IsOuside(TradeDirections td) { return td == TradeDirections.Up || td == TradeDirections.Down; }
    [TradeConditionAsleep]
    public TradeConditionDelegateHide OutsideExtOk {
      get {
        return () => TradeDirectionByAll(
          IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLinesTrends, tl => tl.PriceAvg31, tl => tl.PriceAvg21, IsReverseStrategy),
          Outside1Ok(),
          Outside2Ok());
      }
    }
    TradeDirections IsCurrentPriceOutsideCorridorSelf(Func<TradingMacro, Rate.TrendLevels> trendLevels, Func<Rate.TrendLevels, double> min, Func<Rate.TrendLevels, double> max) {
      return IsCurrentPriceOutsideCorridor(MySelf, trendLevels, min, max, IsReverseStrategy);
    }

    [TradeConditionAsleep]
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegateHide OutsideOk {
      get { return () => IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLinesTrends); }
    }
    [TradeConditionAsleep]
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegateHide Outside1Ok {
      get { return () => IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLines1Trends); }
    }
    [TradeConditionAsleep]
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegateHide Outside2Ok {
      get { return () => IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLines2Trends); }
    }

    #region Helpers
    TradeDirections IsCurrentPriceOutsideCorridor(
      Func<TradingMacro, bool> tmPredicate,
      Func<TradingMacro, Rate.TrendLevels> trendLevels,
      Func<Rate.TrendLevels, double> min,
      Func<Rate.TrendLevels, double> max,
      bool ReverseStrategy
      ) {
      Func<TradeDirections> onBelow = () => TradeDirections.Up;
      Func<TradeDirections> onAbove = () => TradeDirections.Down;
      return TradingMacroOther(tmPredicate)
        .Select(tm => trendLevels(tm))
        .Select(tls =>
          CurrentPrice.Average < min(tls) ? onBelow()
          : CurrentPrice.Average > max(tls) ? onAbove()
          : TradeDirections.None)
        .DefaultIfEmpty(TradeDirections.None)
        .First();
    }
    TradeDirections IsCurrentPriceOutsideCorridorSelf(Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(MySelf, trendLevels);
    }
    TradeDirections IsCurrentPriceOutsideCorridor(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(tmPredicate, trendLevels, tl => tl.PriceAvg3, tl => tl.PriceAvg2, InPips(BuyLevel.Rate - SellLevel.Rate) > 0);
    }
    TradeDirections IsCurrentPriceOutsideCorridor2Self(Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor2(MySelf, trendLevels);
    }
    TradeDirections IsCurrentPriceOutsideCorridor2(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(tmPredicate, trendLevels, tl => tl.PriceAvg32, tl => tl.PriceAvg22, IsReverseStrategy);
    }
    private bool IsOutsideOk(Func<TradingMacro, bool> tmPredicate, string canTradeKey, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      var td = IsCurrentPriceOutsideCorridor(tmPredicate, trendLevels);
      var ok = IsOuside(td);
      if(ok && !_canOpenTradeAutoConditions.ContainsKey(canTradeKey))
        _canOpenTradeAutoConditions.TryAdd(canTradeKey, () => td);
      if((!ok || TradeDirection != TradeDirections.Auto) && _canOpenTradeAutoConditions.ContainsKey(canTradeKey)) {
        Func<TradeDirections> f;
        _canOpenTradeAutoConditions.TryRemove(canTradeKey, out f);
      }
      return ok;
    }
    #endregion
    #endregion

    #endregion

    #region TradingMacros
    private IEnumerable<TradingMacro> TradingMacroOther(Func<TradingMacro, bool> predicate) {
      return TradingMacrosByPair().Where(predicate);
    }
    private IEnumerable<TradingMacro> TradingMacroOther() {
      return TradingMacrosByPair().Where(tm => tm != this);
    }
    private IEnumerable<TradingMacro> TradingMacrosByPair() {
      return _tradingMacros.Where(tm => tm.Pair == Pair).OrderBy(tm => PairIndex);
    }
    #endregion

    #region Cross Handlers

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
          _tipRatioCurrent = _ratesHeightCma / tlHeight;
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
    void TradeConditionsReset() { TradeConditions.Clear(); }
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
      return TradeConditionsInfo().Any(d => d == td);
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
      if(IsTrader && CanTriggerTradeDirection())
        SetTradeStrip();
      if(!IsTradingTime()) {
        BuySellLevels.ForEach(sr => {
          sr.CanTrade = false;
          sr.InManual = false;
          sr.TradesCount = 0;
        });
        Log = new Exception("IsTradingTime() == false");
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
    [Category(categoryActiveYesNo)]
    [WwwSetting(wwwSettingsTradingOther)]
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


    #endregion

    #endregion
  }
}
