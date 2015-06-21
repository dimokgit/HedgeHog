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
    public TradeConditionDelegate UseWFOk { get { return () => true; } }
    public TradeConditionDelegate WidthOk { get { return () => WaveRanges.Count >=4 && TrendLines1Trends.StDev > TrendLinesTrends.StDev; } }
    public TradeConditionDelegate TpsOk { get { return () => IsTresholdAbsOk(TicksPerSecondAverage, TpsMin); } }
    public TradeConditionDelegate TpsAvgMinOk { get { return () => IsTresholdAbsOk(TicksPerSecondAverage, TicksPerSecondAverageAverage * TpsMin.Sign()); } }
    public TradeConditionDelegate WaveLastOk { get { return () => WaveFirstSecondRatio > WaveFirstSecondRatioMin; } }
    public TradeConditionDelegate AngleOk {
      get {
        return () => IsTresholdAbsOk(TrendLinesTrends.Angle, TradingAngleRange);
      }
    }
    public TradeConditionDelegate Angle0Ok {
      get {
        return () => {
          var a = TrendLines1Trends.Angle.Abs();
          return a < 1;
        };
      }
    }
    [Description("'Green' corridor is outside the 'Red' and 'Blue' ones")]
    public TradeConditionDelegate GreenOk {
      get {
        return () =>
          TrendLines1Trends.PriceAvg2 >= TrendLinesTrends.PriceAvg21.Max(TrendLines2Trends.PriceAvg2) ||
          TrendLines1Trends.PriceAvg3 <= TrendLinesTrends.PriceAvg31.Min(TrendLines2Trends.PriceAvg3);
      }
    }
    public Func<bool> OutsideAnyOk {
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
          IsOuside(IsCurrentPriceOutsideCorridor(tm => tm.TrendLinesTrends, tl => tl.PriceAvg31, tl => tl.PriceAvg21)) &&
          Outside1Ok() &&
          Outside2Ok();
      }
    }
    TradeDirections IsCurrentPriceOutsideCorridor(Func<TradingMacro, Rate.TrendLevels> trendLevels, Func<Rate.TrendLevels, double> min, Func<Rate.TrendLevels, double> max) {
      Func<TradeDirections> onBelow = () => ReverseStrategy ? TradeDirections.Up : TradeDirections.Down;
      Func<TradeDirections> onAbove = () => ReverseStrategy ? TradeDirections.Down : TradeDirections.Up;
      return TradingMacroOther()
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
    TradeDirections IsCurrentPriceOutsideCorridor(Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(trendLevels, tl => tl.PriceAvg3, tl => tl.PriceAvg2);
    }
    TradeDirections IsCurrentPriceOutsideCorridor2(Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      return IsCurrentPriceOutsideCorridor(trendLevels, tl => tl.PriceAvg32, tl => tl.PriceAvg22);
    }
    private bool IsOutsideOk(string canTradeKey, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      var td = IsCurrentPriceOutsideCorridor(trendLevels);
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
      get { return () => IsOutsideOk("OusideOk", tm => tm.TrendLinesTrends); }
    }
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate Outside1Ok {
      get { return () => IsOutsideOk("Ouside1Ok", tm => tm.TrendLines1Trends); }
    }
    [TradeCondition(TradeConditionAttribute.Types.Or)]
    public TradeConditionDelegate Outside2Ok {
      get { return () => IsOutsideOk("Outside2Ok", tm => tm.TrendLines2Trends); }
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
    public TradeConditionDelegate[] GetTradeConditions() {
      return GetType().GetProperties()
        .Where(p => p.PropertyType == typeof(TradeConditionDelegate))
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
    DateTime _tradeConditionTriggerCancelDate = DateTime.MaxValue;
    void ResetTradeConditionTriggerCancel() { _tradeConditionTriggerCancelDate = DateTime.MaxValue; }
    void TradeConditionTriggerCancel() {
      WaveRanges.Take(1)
        .Select(wr => wr.StartDate)
        .Where(sd => sd > _tradeConditionTriggerCancelDate)
        .ForEach(_ => {
          BuyLevel.CanTradeEx = SellLevel.CanTradeEx = false;
          ResetTradeConditionTriggerCancel();
        });
    }
    public void TradeConditionsTrigger() {
      if (IsTrader) {
        TradeConditionTriggerCancel();
        if (TradeDirection.IsAny() && TradeConditionsEval()) {
          if (TradeDirection.HasUp()) BuyLevel.CanTradeEx = true;
          if (TradeDirection.HasDown()) SellLevel.CanTradeEx = true;
          _tradeConditionTriggerCancelDate = ServerTime;
        }
      }
    }
    public bool TradeConditionsEval() {
      if (!IsTrader) return false;
      return (from tc in TradeConditionsInfo((d, t, s) => new { d, t, s })
              group tc by tc.t into gtci
              let or = gtci.Select(x => x.d).DefaultIfEmpty(() => true).Select(d => d())
              let and = gtci.Select(g => g.d())
              select gtci.Key == TradeConditionAttribute.Types.And ? and.All(b => b) : or.Any(b => b)
              )
             .All(b => b);
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
