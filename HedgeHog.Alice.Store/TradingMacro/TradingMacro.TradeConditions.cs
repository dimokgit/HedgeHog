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
  partial class TradingMacro {
    #region TradeOpenActions
    public delegate void TradeOpenAction(Trade trade);
    public TradeOpenAction FreezeOnTrade { get { return trade => FreezeCorridorStartDate(); } }
    public TradeOpenAction WrapOnTrade { get { return trade => WrapTradeInCorridor(); } }

    TradeOpenAction _greenExitOnTrade;
    public TradeOpenAction GreenExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.PriceHigh0, TradeLevelBy.PriceLow0));
      }
    }

    TradeOpenAction _redExitOnTrade;
    public TradeOpenAction RedExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.PriceAvg2, TradeLevelBy.PriceAvg3));
      }
    }

    TradeOpenAction _blueExitOnTrade;
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
    public delegate bool TradeConditionDelegate();
    bool WidthCommonOk(Func<bool> ok) { return WaveCountOk() && ok(); }
    [TradeConditionStartDateTrigger]
    public TradeConditionDelegate WidthRBOk {
      get {
        return () => WidthCommonOk(() => TrendLinesTrends.StDev > TrendLines2Trends.StDev);
      }
    }
    [TradeConditionStartDateTrigger]
    public TradeConditionDelegate WidthGROk {
      get { return () => WidthCommonOk(() => TrendLines1Trends.StDev > TrendLinesTrends.StDev); }
    }
    public TradeConditionDelegate WaveAvgOk {
      get {
        return () => WaveRanges.Take(1).Any(wr => wr.Height > WaveRangeAvg.Height);
      }
    }
    public TradeConditionDelegate PowerOk {
      get {
        return () => new[]{
          WaveRangeAvg.WorkByHeight,
          WaveRangeAvg.Height ,
          WaveRangeAvg.DistanceByRegression
        }.StandardDeviation() < 1;
      }
    }
    public TradeConditionDelegate DbrOk {
      get {
        return () => WaveRanges.Take(1).Any(wr => WaveRangesGRB.Index(wr, w => w.DistanceByRegression) <= DbrIndexMax);
      }
    }
    bool WaveTresholdOk(double value, double treshold) {
      return !CorridorStartDate.HasValue && WaveRanges[0].Height > WaveHeightAverage && IsTresholdAbsOk(value, treshold);
    } 
    public TradeConditionDelegate WaveGOk {
      get { return () => WaveTresholdOk(WaveFirstSecondRatio, WaveFirstSecondRatioMin); }
    }
    Func<WaveRange, double>[] _elliotProps = new Func<WaveRange, double> []{
      w=>w.Height,
      w=>w.WorkByHeight,
      w=>w.DistanceByRegression,
      w=>w.Distance
    };
    public TradeConditionDelegate ElliotOk {
      get {
        return () => WaveRanges
          .Where(wr => WaveRanges.Take(1).Any(w=>w.Height > WaveRangeAvg.Height || w.WorkByHeight > WaveRangeAvg.WorkByHeight))
          .Select(wr => new {
            wr,
            score = _elliotProps.Select(f => WaveRanges.Index(wr, f)).Count(i => i == 0)
          })
          .Any(x => x.score >= 3 && WaveRanges.IndexOf(x.wr).Between(1,1));
      }
    }

    public Func<bool> TpsOk { get { return () => IsTresholdAbsOk(TicksPerSecondAverage, TpsMin); } }
    public TradeConditionDelegate TpsAvgMinOk { get { return () => IsTresholdAbsOk(TicksPerSecondAverage, TicksPerSecondAverageAverage * TpsMin.Sign()); } }
    public TradeConditionDelegate WaveCountOk { get { return () => WaveRanges.Count >= _greenRedBlue.Sum(); } }
    public Func<bool> WaveSteepOk { get { return () => WaveRanges[0].Slope.Abs() > WaveRanges[1].Slope.Abs(); } }
    public Func<bool> WaveEasyOk { get { return () => WaveRanges[0].Slope.Abs() < WaveRanges[1].Slope.Abs(); } }

    [Description("'Green' corridor is outside the 'Red' and 'Blue' ones")]
    public TradeConditionDelegate GreenOk {
      get {
        return () =>
          TrendLines1Trends.PriceAvg2 >= TrendLinesTrends.PriceAvg21.Max(TrendLines2Trends.PriceAvg2) ||
          TrendLines1Trends.PriceAvg3 <= TrendLinesTrends.PriceAvg31.Min(TrendLines2Trends.PriceAvg3);
      }
    }
    public TradeConditionDelegate OutsideAnyOk {
      get { return () => Outside1Ok() || OutsideOk() || Outside2Ok(); }
    }
    public TradeConditionDelegate OutsideAllOk {
      [TradeCondition(TradeConditionAttribute.Types.Or)]
      get { return () => Outside1Ok() && OutsideOk() && Outside2Ok(); }
    }
    private bool IsOuside(TradeDirections td) { return td == TradeDirections.Up || td == TradeDirections.Down; }
    public TradeConditionDelegate OutsideExtOk {
      get {
        return () =>
          IsOuside(IsCurrentPriceOutsideCorridor(tm => tm == this, tm => tm.TrendLinesTrends, tl => tl.PriceAvg31, tl => tl.PriceAvg21)) &&
          Outside1Ok() &&
          Outside2Ok();
      }
    }
    TradeDirections IsCurrentPriceOutsideCorridorSelf( Func<TradingMacro, Rate.TrendLevels> trendLevels, Func<Rate.TrendLevels, double> min, Func<Rate.TrendLevels, double> max) {
      return IsCurrentPriceOutsideCorridor(MySelf, trendLevels, min, max);
    }
    TradeDirections IsCurrentPriceOutsideCorridor(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, Rate.TrendLevels> trendLevels, Func<Rate.TrendLevels, double> min, Func<Rate.TrendLevels, double> max) {
      Func<TradeDirections> onBelow = () => ReverseStrategy ? TradeDirections.Up : TradeDirections.Down;
      Func<TradeDirections> onAbove = () => ReverseStrategy ? TradeDirections.Down : TradeDirections.Up;
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
    TradeDirections IsCurrentPriceOutsideCorridorSelf( Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(MySelf, trendLevels);
    }
    TradeDirections IsCurrentPriceOutsideCorridor(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(tmPredicate, trendLevels, tl => tl.PriceAvg3, tl => tl.PriceAvg2);
    }
    TradeDirections IsCurrentPriceOutsideCorridor2Self(Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor2(MySelf, trendLevels);
    }
    TradeDirections IsCurrentPriceOutsideCorridor2(Func<TradingMacro, bool> tmPredicate, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(tmPredicate, trendLevels, tl => tl.PriceAvg32, tl => tl.PriceAvg22);
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
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate OutsideOk {
      get { return () => IsOutsideOk(MySelfNext, "OusideOk", tm => tm.TrendLinesTrends); }
    }
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate Outside1Ok {
      get { return () => IsOutsideOk(MySelfNext, "Ouside1Ok", tm => tm.TrendLines1Trends); }
    }
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate Outside2Ok {
      get { return () => IsOutsideOk(MySelfNext, "Outside2Ok", tm => tm.TrendLines2Trends); }
    }

    public TradeConditionDelegate[] _TradeConditions = new TradeConditionDelegate[0];
    public TradeConditionDelegate[] TradeConditions {
      get { return _TradeConditions; }
      set {
        _TradeConditions = value;
        OnPropertyChanged("TradeConditions");
      }
    }
    bool HasTradeConditions { get { return TradeConditions.Any(); } }
    void TradeConditionsReset() { TradeConditions = new TradeConditionDelegate[0]; }
    public T[] GetTradeConditions<T>(Func<TradeConditionDelegate, TradeConditionAttribute.Types, T> map) {
      return (from x in this.GetPropertiesByType(() => (TradeConditionDelegate)null, (v, p) => new { v, p })
              let a = x.p.GetCustomAttributes<TradeConditionAttribute>()
              .DefaultIfEmpty(new TradeConditionAttribute(TradeConditionAttribute.Types.And)).First()
              select map(x.v, a.Type))
              .ToArray();
    }
    public TradeConditionDelegate[] GetTradeConditions(Func<PropertyInfo,bool> predicate = null) {
      return GetType().GetProperties()
        .Where(p => p.PropertyType == typeof(TradeConditionDelegate))
        .Where(p=>predicate == null || predicate(p))
        .Select(p => p.GetValue(this))
        .Cast<TradeConditionDelegate>()
        .ToArray();

      //return new[] { WideOk, TpsOk, AngleOk, Angle0Ok };
    }
    public static string ParseTradeConditionName(MethodInfo method) {
      return Regex.Match(method.Name, "<(.+)>").Groups[1].Value.Substring(4);

    }
    public TradeConditionDelegate[] TradeConditionsSet(IList<string> names) {
      return TradeConditions = GetTradeConditions().Where(tc => names.Contains(ParseTradeConditionName(tc.Method))).ToArray();
    }
    public IEnumerable<T> TradeConditionsInfo<T>(Func<TradeConditionDelegate, TradeConditionAttribute.Types, string, T> map) {
      return from tcsAll in GetTradeConditions((d, t) => new { d, t })
             join tc in TradeConditionsInfo((d, s) => new { d, s }) on tcsAll.d equals tc.d
             select map(tc.d, tcsAll.t, tc.s);
    }
    DateTime _tradeConditionsTriggerDate = DateTime.MinValue;
    public void TradeConditionsTrigger() {
      if (IsTrader) {
        if (Trades.Length > 0) {
          BuyLevel.CanTradeEx = SellLevel.CanTradeEx = false;
        } else {
          var evals = TradeConditionsEval();
          var direction = TradeDirection;
          //TradeConditions.Any(tc => tc == ElliotOk)
          //? WaveRanges[0].Slope > 0
          //? TradeDirections.Up
          //: TradeDirections.Down
          //: TradeDirections.Both;
          //TradeConditionsEvalStartDate().ForEach(_ => FreezeCorridorStartDate());
          evals.ForEach(eval => {
            if (eval /*&& WaveRanges[0].StartDate > _tradeConditionsTriggerDate*/) {
              _tradeConditionsTriggerDate = WaveRanges[0].EndDate;
              if (TradeDirection.HasUp() && direction.HasUp()) BuyLevel.CanTradeEx = true;
              if (TradeDirection.HasDown() && direction.HasDown()) SellLevel.CanTradeEx = true;
            } else {
              BuyLevel.CanTradeEx = SellLevel.CanTradeEx = false;
            }
          });
        }
      }
    }
    public IEnumerable<bool> TradeConditionsEvalStartDate() {
      if (!IsTrader) return new bool[0];
      return (from d in GetTradeConditions(p=>p.GetCustomAttributes<TradeConditionStartDateTriggerAttribute>().Any())
              let b = d()
              group b by b into gb
              select gb.Key
              )
              .DefaultIfEmpty()
              .OrderBy(b => b)
              .Take(1)
              .Where(b => b);
    }
    public IEnumerable<bool> TradeConditionsEval() {
      if (!IsTrader) return new bool[0];
      return (from tc in TradeConditionsInfo((d, t, s) => new { d, t, s })
              group tc by tc.t into gtci
              let or = gtci.Select(x => x.d).DefaultIfEmpty(() => true).Select(d => d())
              let and = gtci.Select(g => g.d())
              let c = gtci.Key == TradeConditionAttribute.Types.And ? and.All(b => b) : or.Any(b => b)
              group c by c into gc
              select gc.Key
              )
              .OrderBy(b => b)
              .Take(1);
    }
    public IEnumerable<T> TradeConditionsInfo<T>(Func<TradeConditionDelegate, string, T> map) {
      return TradeConditionsInfo(TradeConditions, map);
    }
    public IEnumerable<T> TradeConditionsAllInfo<T>(Func<TradeConditionDelegate, string, T> map) {
      return TradeConditionsInfo(GetTradeConditions(), map);
    }
    public IEnumerable<T> TradeConditionsInfo<T>(IList<TradeConditionDelegate> tradeConditions, Func<TradeConditionDelegate, string, T> map) {
      return tradeConditions.Select(tc => map(tc, ParseTradeConditionName(tc.Method)));
    }
    [DisplayName("Trade Conditions")]
    [Category(categoryActiveFuncs)]
    public string TradeConditionsSave {
      get { return string.Join(",", TradeConditionsInfo((tc, name) => name)); }
      set {
        TradeConditionsSet(value.Split(','));
      }
    }
    #endregion
  }
}
