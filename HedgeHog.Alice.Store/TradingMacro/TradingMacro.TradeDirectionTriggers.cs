using HedgeHog.Bars;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    #region Trade Direction Triggers
    void TriggerOnOutside(Func<Func<TradingMacro, Rate.TrendLevels>, TradeDirections> isOutside, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      var td = isOutside(trendLevels);
      if(td.Any()) {
        if(TradeConditionsEval().All(b => b.Any()))
          TradeDirection = td;
        if(!HasTradeConditions) {
          BuyLevel.CanTradeEx = td.HasUp();
          SellLevel.CanTradeEx = td.HasDown();
        }
      }
    }
    bool MySelf(TradingMacro tm) { return tm == this; }
    bool MySelfNext(TradingMacro tm) { return tm.PairIndex > this.PairIndex; }
    #region Triggers
    [TradeDirectionTrigger]
    public void OnTradeCondOk() {
      if(Trades.Length == 0 && TradeConditionsEval().DefaultIfEmpty(TradeDirections.None).All(b => b.Any())) {
        var bs = new[] { BuyLevel, SellLevel };
        // Turn off triegger
        //_tradeDirectionTriggers.RemoveAll(tdt => tdt == OnTradeCondOk);
        double min = GetTradeLevel(false, SellLevel.RateEx), max = GetTradeLevel(true, BuyLevel.Rate);
        //var l = CorridorLength1;
        //UseRates(rates => rates.GetRange(rates.Count - l, l).Height(out min, out max));
        // set trade levels rates
        if(new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(min, max))) {
          BuyLevel.ResetPricePosition();
          BuyLevel.Rate = max;// GetTradeLevel(true, BuyLevel.Rate);
          SellLevel.ResetPricePosition();
          SellLevel.Rate = min;// GetTradeLevel(false, SellLevel.RateEx);
          // init trade levels
          bs.ForEach(sr => {
            sr.TradesCount = TradeCountStart;
            sr.CanTrade = true;
            sr.InManual = true;
          });
        }
      }
    }
    [TradeDirectionTrigger]
    public void OnOutsideRed() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridorSelf, tm => tm.TrendLinesTrends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideRed2() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridor2Self, tm => tm.TrendLinesTrends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideGreen() {
      Func<Func<TradingMacro, Rate.TrendLevels>, TradeDirections> f = foo =>
        IsCurrentPriceOutsideCorridor(MySelf, foo, tl => tl.PriceAvg3, tl => tl.PriceAvg2, false);
      TriggerOnOutside(IsCurrentPriceOutsideCorridorSelf, tm => tm.TrendLines1Trends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideBlue() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridorSelf, tm => tm.TrendLines2Trends);
    }
    DateTime _onElliotTradeCorridorDate = DateTime.MinValue;
    [TradeDirectionTrigger]
    public void OnElliotWave() {
      WaveRanges.Where(wr => wr.ElliotIndex > 0).Take(1)
        .ForEach(wr => {
          var max = wr.Max;
          var min = wr.Min;
          var mid = max.Avg(min);
          var offset = WaveHeightAverage / 2;
          if(new[] { false, true }.All(b => CurrentEnterPrice(b).Between(min, max))) {
            BuyLevel.Rate = mid - offset;
            SellLevel.Rate = mid + offset;
            BuyLevel.InManual = SellLevel.InManual = true;
            BuyLevel.CanTrade = TradeDirection.HasUp() && true;
            SellLevel.CanTrade = TradeDirection.HasDown() && true;
          }
        });
    }
    #endregion

    #region Infrastructure
    public Action[] _tradeDirectionTriggers = new Action[0];
    bool HasTradeDirectionTriggers { get { return _tradeDirectionTriggers.Length > 0; } }
    public Action[] GetTradeDirectionTriggers() {
      return this.GetMethodsByAttibute<TradeDirectionTriggerAttribute>()
        .Select(me => (Action)me.CreateDelegate(typeof(Action), this))
        .ToArray();
    }
    public Action[] TradeDirectionTriggersSet(IList<string> names) {
      return _tradeDirectionTriggers = GetTradeDirectionTriggers().Where(tc => names.Contains(tc.Method.Name)).ToArray();
    }
    static IEnumerable<T> TradeDirectionTriggersInfo<T>(IList<Action> tradeDirectionTrggers, Func<Action, string, T> map) {
      return tradeDirectionTrggers.Select(tc => map(tc, tc.Method.Name));
    }
    public IEnumerable<T> TradeDirectionTriggersInfo<T>(Func<Action, string, T> map) {
      return TradeDirectionTriggersInfo(_tradeDirectionTriggers, map);
    }
    public IEnumerable<T> TradeDirectionTriggersAllInfo<T>(Func<Action, string, T> map) {
      return TradeDirectionTriggersInfo(GetTradeDirectionTriggers(), map);
    }
    void TradeDirectionTriggersRun() {
      if(IsTrader)
        TradeDirectionTriggersInfo((a, n) => a).ForEach(a => a());
    }
    [DisplayName("Trade Dir. Trgs")]
    [Description("Trade Direction Triggers")]
    [Category(categoryActiveFuncs)]
    public string TradeDirectionTriggerssSave {
      get { return string.Join(MULTI_VALUE_SEPARATOR, TradeDirectionTriggersInfo((tc, name) => name)); }
      set {
        TradeDirectionTriggersSet(value.Split(MULTI_VALUE_SEPARATOR[0]));
      }
    }
    #endregion
    #endregion
  }
}
