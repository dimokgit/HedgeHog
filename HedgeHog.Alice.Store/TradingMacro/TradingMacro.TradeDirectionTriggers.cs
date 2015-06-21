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
      if (td.IsAny()) {
        TradeDirection = td;
        if (!HasTradeConditions) {
          BuyLevel.CanTradeEx = td.HasUp();
          SellLevel.CanTradeEx = td.HasDown();
        }
      }
    }

    #region Triggers
    [TradeDirectionTrigger]
    public void OnOutsideRed() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridor, tm => tm.TrendLinesTrends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideRed2() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridor2, tm => tm.TrendLinesTrends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideGreen() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridor, tm => tm.TrendLines1Trends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideBlue() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridor, tm => tm.TrendLines2Trends);
    }
    #endregion

    public Action[] _tradeDirectionTriggers = new Action[0];
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
      if (IsTrader)
        TradeDirectionTriggersInfo((a, n) => a).ForEach(a => a());
    }
    [DisplayName("Trade Dir. Trgs")]
    [Description("Trade Direction Triggers")]
    [Category(categoryActiveFuncs)]
    public string TradeDirectionTriggerssSave {
      get { return string.Join(",", TradeDirectionTriggersInfo((tc, name) => name)); }
      set {
        TradeDirectionTriggersSet(value.Split(','));
      }
    }

    #endregion
  }
}
