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

namespace HedgeHog.Alice.Store {
  class TradeConditionStartDateTriggerAttribute : Attribute { }
  class TradeConditionOtherCorridorAttribute : Attribute { }
  class TradeConditionUseCorridorAttribute : Attribute { }
  partial class TradingMacro {
    #region TradeOpenActions
    public delegate void TradeOpenAction(Trade trade);
    public TradeOpenAction FreezeOnTrade { get { return trade => FreezeCorridorStartDate(); } }
    public TradeOpenAction WrapOnTrade { get { return trade => WrapTradeInCorridor(); } }

    public TradeOpenAction Avg1ExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.Avg1Max, TradeLevelBy.Avg1Min));
      }
    }
    public TradeOpenAction Avg1GRBExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.Avg1GRBMax, TradeLevelBy.Avg1GRBMin));
      }
    }
    public TradeOpenAction GreenExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.PriceHigh0, TradeLevelBy.PriceLow0));
      }
    }

    public TradeOpenAction RedExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.PriceAvg2, TradeLevelBy.PriceAvg3));
      }
    }

    public TradeOpenAction BlueExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.PriceHigh, TradeLevelBy.PriceLow));
      }
    }

    TradeOpenAction SetCorridorExit(TradeLevelBy buy, TradeLevelBy sell) {
      return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, buy, sell));
    }
    private void SetCorridorExitImpl(Trade trade, TradeLevelBy buy, TradeLevelBy sell) {
      if (trade.IsBuy) {
        var level = TradeLevelFuncs[buy]();
        if (InPips(level - trade.Open) > 5)
          LevelBuyCloseBy = buy;
      } else {
        var level = TradeLevelFuncs[TradeLevelBy.PriceLow0]();
        if (InPips(-level + trade.Open) > 5)
          LevelSellCloseBy = TradeLevelBy.PriceLow0;
      }
    }

    TradeOpenAction[] _tradeOpenActions = new TradeOpenAction[0];
    void OnTradeConditionsReset() { _tradeOpenActions = new TradeOpenAction[0]; }
    public TradeOpenAction[] GetTradeOpenActions() {
      return GetType().GetProperties()
        .Where(p => p.PropertyType == typeof(TradeOpenAction))
        .Select(p => p.GetValue(this))
        .Cast<TradeOpenAction>()
        .ToArray();
    }

    public TradeOpenAction[] TradeOpenActionsSet(IList<string> names) {
      return _tradeOpenActions = GetTradeOpenActions().Where(tc => names.Contains(ParseTradeConditionName(tc.Method))).ToArray();
    }
    public IEnumerable<T> TradeOpenActionsInfo<T>(Func<TradeOpenAction, string, T> map) {
      return TradeOpenActionsInfo(_tradeOpenActions, map);
    }
    public IEnumerable<T> TradeOpenActionsAllInfo<T>(Func<TradeOpenAction, string, T> map) {
      return TradeOpenActionsInfo(GetTradeOpenActions(), map);
    }
    public IEnumerable<T> TradeOpenActionsInfo<T>(IList<TradeOpenAction> tradeOpenActions, Func<TradeOpenAction, string, T> map) {
      return tradeOpenActions.Select(tc => map(tc, ParseTradeConditionName(tc.Method)));
    }
    [DisplayName("Trade Actions")]
    [Category(categoryActiveFuncs)]
    public string TradeOpenActionsSave {
      get { return string.Join(",", TradeOpenActionsInfo((tc, name) => name)); }
      set {
        TradeOpenActionsSet(value.Split(','));
      }
    }
    #endregion

    #region TradeConditions
    public delegate TradeDirections TradeConditionDelegate();
    TradeDirections IsTradeConditionOk(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, TradeDirections> condition) {
      return TradingMacroOther(tmPredicate).Take(1).Select(condition).DefaultIfEmpty(TradeDirections.Both).First();
    }
    public TradeConditionDelegate CorrAngAOk {
      get {
        return () => TrendLinesTrendsAll.All(tlt => !tlt.IsEmpty) &&
          IsTresholdAbsOk(TrendLinesTrendsAll.Average(tlt => tlt.Angle), this.TradingAngleRange) ? TradeDirections.Both : TradeDirections.None;
      }
    }
    public TradeConditionDelegate CorrAngMOk {
      get {
        return () => TrendLinesTrendsAll.All(tlt => !tlt.IsEmpty) &&
          IsTresholdAbsOk(TrendLinesTrendsAll.Min(tlt => tlt.Angle.Abs()), this.TradingAngleRange) ? TradeDirections.Both : TradeDirections.None;
      }
    }
    public TradeConditionDelegate CorrCntOk { get { return () => TradeDirectionBoth(CorridorLengths.Distinct().Count() == 3); } }
    public TradeConditionDelegate CorrCnt2Ok {
      get {
        return () => IsTradeConditionOk(MySelfNext, tm => TradeDirectionBoth(tm.CorridorLengths.Distinct().Count() == 3));
      }
    }
    TradeDirections WidthCommonOk(Func<TradeDirections> ok) { return CorrCntOk().Any() ? ok() : TradeDirections.None; }
    [TradeConditionStartDateTrigger]
    public TradeConditionDelegate WidthRBOk {
      get {
        return () => WidthCommonOk(() => TrendLinesTrends.StDev > TrendLines2Trends.StDev ? TradeDirections.Both : TradeDirections.None);
      }
    }
    [TradeConditionStartDateTrigger]
    public TradeConditionDelegate WidthGROk {
      get { return () => WidthCommonOk(() => TrendLines1Trends.StDev > TrendLinesTrends.StDev ? TradeDirections.Both : TradeDirections.None); }
    }
    [TradeConditionStartDateTrigger]
    public TradeConditionDelegate WaveAvgOk {
      get {
        return () => WaveRanges.Take(2)
          .Where(wr =>
            wr.DistanceByRegression > WaveRangeAvg.DistanceByRegression &&
            wr.UID > WaveRangeAvg.UID
            )
            .Select(wr => TradeDirectionBySlope(wr))
            .DefaultIfEmpty(TradeDirections.None)
            .First();
      }
    }
    public TradeConditionDelegate DoubleTapOk {
      get {
        return () => WaveRanges.Count > 2 && WaveRanges[2].Distance > WaveRangeAvg.Distance && WaveRangeTail.IsEmpty
          ? TradeDirectionBySlope(WaveRanges[2])
          : TradeDirections.None;
      }
    }
    Func<WaveRange, double>[] _elliotProps = new Func<WaveRange, double>[]{
      w=>w.Height,
      w=>w.WorkByHeight,
      w=>w.DistanceByRegression,
      w=>w.Distance
    };
    TradeDirections TradeDirectionBoth(bool ok) { return ok ? TradeDirections.Both : TradeDirections.None; }
    TradeConditionDelegate TradeDirectionEither(Func<bool> ok) { return () => ok() ? TradeDirections.Up : TradeDirections.Down; }
    TradeDirections TradeDirectionBySlope(WaveRange wr, bool range = true) {
      return wr.Slope > 0
        ? range
        ? TradeDirections.Down
        : TradeDirections.Up
        : range
        ? TradeDirections.Up
        : TradeDirections.Down;
    }
    public Func<bool> TpsOk { get { return () => IsTresholdAbsOk(TicksPerSecondAverage, TpsMin); } }
    public TradeConditionDelegate TpsAvgMinOk { get { return () => TradeDirectionBoth(IsTresholdAbsOk(TicksPerSecondAverage, TicksPerSecondAverageAverage * TpsMin.Sign())); } }
    public Func<bool> WaveSteepOk { get { return () => WaveRanges[0].Slope.Abs() > WaveRanges[1].Slope.Abs(); } }
    public Func<bool> WaveEasyOk { get { return () => WaveRanges[0].Slope.Abs() < WaveRanges[1].Slope.Abs(); } }

    [Description("'Green' corridor is outside the 'Red' and 'Blue' ones")]
    public TradeConditionDelegate GreenOk {
      get {
        return () =>
          TrendLines1Trends.PriceAvg2 >= TrendLinesTrends.PriceAvg21.Max(TrendLines2Trends.PriceAvg2)
          ? TradeDirections.Down
          : TrendLines1Trends.PriceAvg3 <= TrendLinesTrends.PriceAvg31.Min(TrendLines2Trends.PriceAvg3)
          ? TradeDirections.Up
          : TradeDirections.None;
      }
    }
    public TradeConditionDelegate GreenExtOk {
      get {
        return () =>
          TrendLines1Trends.PriceAvg2 >= TrendLinesTrends.PriceAvg21.Max(TrendLines2Trends.PriceAvg2)
          ? TradeDirections.Up
          : TrendLines1Trends.PriceAvg3 <= TrendLinesTrends.PriceAvg31.Min(TrendLines2Trends.PriceAvg3)
          ? TradeDirections.Down
          : TradeDirections.None;
      }
    }
    [TradeConditionOtherCorridor]
    public TradeConditionDelegate OutsideAnyOk {
      get { return () => Outside1Ok() | OutsideOk() | Outside2Ok(); }
    }
    TradeDirections TradeDirectionByAll(params TradeDirections[] tradeDirections) {
      return tradeDirections
        .Where(td => td.Any())
        .Buffer(3)
        .Where(b => b.Count == 3 && b.Distinct().Count() == 1)
        .Select(b => b[2])
        .DefaultIfEmpty(TradeDirections.None)
        .Single();
    }
    [TradeConditionOtherCorridor]
    public TradeConditionDelegate OutsideAllOk {
      [TradeCondition(TradeConditionAttribute.Types.Or)]
      get {
        return () =>
          IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLinesTrends, tl => tl.PriceAvg31, tl => tl.PriceAvg21,IsReverseStrategy).Any()
          ? TradeDirections.None
          : TradeDirectionByAll(Outside1Ok(), OutsideOk(), Outside2Ok());
      }
    }
    private bool IsOuside(TradeDirections td) { return td == TradeDirections.Up || td == TradeDirections.Down; }
    [TradeConditionOtherCorridor]
    public TradeConditionDelegate OutsideExtOk {
      get {
        return () => TradeDirectionByAll(
          IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLinesTrends, tl => tl.PriceAvg31, tl => tl.PriceAvg21,IsReverseStrategy),
          Outside1Ok(),
          Outside2Ok());
      }
    }
    TradeDirections IsCurrentPriceOutsideCorridorSelf(Func<TradingMacro, Rate.TrendLevels> trendLevels, Func<Rate.TrendLevels, double> min, Func<Rate.TrendLevels, double> max) {
      return IsCurrentPriceOutsideCorridor(MySelf, trendLevels, min, max,IsReverseStrategy);
    }
    TradeDirections IsCurrentPriceOutsideCorridor(
      Func<TradingMacro, bool> tmPredicate, 
      Func<TradingMacro, Rate.TrendLevels> trendLevels,
      Func<Rate.TrendLevels, double> min, 
      Func<Rate.TrendLevels, double> max,
      bool ReverseStrategy
      ) {
      Func<TradeDirections> onBelow = () => ReverseStrategy ? TradeDirections.Down : TradeDirections.Up;
      Func<TradeDirections> onAbove = () => ReverseStrategy ? TradeDirections.Up : TradeDirections.Down;
      return TradingMacroOther(tmPredicate)
        .Select(tm => trendLevels(tm))
        .Select(tls =>
          CurrentPrice.Average < min(tls) ? onBelow()
          : CurrentPrice.Average > max(tls) ? onAbove()
          : TradeDirections.None)
        .DefaultIfEmpty(TradeDirections.None)
        .First();
    }

    private IEnumerable<TradingMacro> TradingMacroOther(Func<TradingMacro, bool> predicate) {
      return TradingMacrosByPair().Where(predicate);
    }
    private IEnumerable<TradingMacro> TradingMacroOther() {
      return TradingMacrosByPair().Where(tm => tm != this);
    }
    private IEnumerable<TradingMacro> TradingMacrosByPair() {
      return _tradingMacros.Where(tm => tm.Pair == Pair);
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
      if (ok && !_canOpenTradeAutoConditions.ContainsKey(canTradeKey))
        _canOpenTradeAutoConditions.TryAdd(canTradeKey, () => td);
      if ((!ok || TradeDirection != TradeDirections.Auto) && _canOpenTradeAutoConditions.ContainsKey(canTradeKey)) {
        Func<TradeDirections> f;
        _canOpenTradeAutoConditions.TryRemove(canTradeKey, out f);
      }
      return ok;
    }
    [TradeConditionOtherCorridor]
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate OutsideOk {
      get { return () => IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLinesTrends); }
    }
    [TradeConditionOtherCorridor]
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate Outside1Ok {
      get { return () => IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLines1Trends); }
    }
    [TradeConditionOtherCorridor]
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate Outside2Ok {
      get { return () => IsCurrentPriceOutsideCorridor(MySelfNext, tm => tm.TrendLines2Trends); }
    }

    public Tuple<TradeConditionDelegate, PropertyInfo>[] _TradeConditions = new Tuple<TradeConditionDelegate, PropertyInfo>[0];
    public Tuple<TradeConditionDelegate, PropertyInfo>[] TradeConditions {
      get { return _TradeConditions; }
      set {
        _TradeConditions = value;
        OnPropertyChanged("TradeConditions");
      }
    }
    bool HasTradeConditions { get { return TradeConditions.Any(); } }
    void TradeConditionsReset() { TradeConditions = new Tuple<TradeConditionDelegate, PropertyInfo>[0]; }
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
    public static string ParseTradeConditionName(MethodInfo method) {
      return Regex.Match(method.Name, "<(.+)>").Groups[1].Value.Substring(4);

    }
    public Tuple<TradeConditionDelegate, PropertyInfo>[] TradeConditionsSet(IList<string> names) {
      return TradeConditions = GetTradeConditions().Where(tc => names.Contains(ParseTradeConditionName(tc.Item1.Method))).ToArray();
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
      if (IsTrader) {


        TradeConditionsEval().ForEach(eval => {
          BuyLevel.CanTradeEx = TradeDirection.HasUp() && eval.HasUp();
          SellLevel.CanTradeEx = TradeDirection.HasDown() && eval.HasDown();
        });
      }
    }
    public IEnumerable<TradeDirections> TradeConditionsEvalStartDate() {
      if (!IsTrader) return new TradeDirections[0];
      return (from d in TradeConditionsInfo<TradeConditionStartDateTriggerAttribute>()
              let b = d()
              group b by b into gb
              select gb.Key
              )
              .DefaultIfEmpty()
              .OrderBy(b => b)
              .Take(1)
              .Where(b => b.Any());
    }
    public IEnumerable<TradeDirections> TradeConditionsEval() {
      if (!IsTrader) return new TradeDirections[0];
      return (from tc in TradeConditionsInfo((d, p, t, s) => new { d, t, s })
              group tc by tc.t into gtci
              let and = gtci.Select(g => g.d()).ToArray()
              let c = gtci.Key == TradeConditionAttribute.Types.And
              ? and.Aggregate(TradeDirections.Both, (a, td) => a & td)
              : and.Aggregate(TradeDirections.Both, (a, td) => a | td)
              select c
              )
              .Scan(TradeDirections.Both, (a, td) => a & td)
              .TakeLast(1);
    }
    public IEnumerable<T> TradeConditionsInfo<T>(Func<TradeConditionDelegate, PropertyInfo, string, T> map) {
      return TradeConditionsInfo(TradeConditions, map);
    }
    public IEnumerable<T> TradeConditionsAllInfo<T>(Func<TradeConditionDelegate, PropertyInfo, string, T> map) {
      return TradeConditionsInfo(GetTradeConditions(), map);
    }
    public IEnumerable<T> TradeConditionsInfo<T>(IList<Tuple<TradeConditionDelegate, PropertyInfo>> tradeConditions, Func<TradeConditionDelegate, PropertyInfo, string, T> map) {
      return tradeConditions.Select(tc => map(tc.Item1, tc.Item2, ParseTradeConditionName(tc.Item1.Method)));
    }
    [DisplayName("Trade Conditions")]
    [Category(categoryActiveFuncs)]
    public string TradeConditionsSave {
      get { return string.Join(",", TradeConditionsInfo((tc, pi, name) => name)); }
      set {
        TradeConditionsSet(value.Split(','));
      }
    }
    #endregion
  }
}
