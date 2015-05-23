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
    #region TradeConditions
    public delegate bool TradeConditionDelegate();
    public TradeConditionDelegate WideOk { get { return () => TrendLines1Trends.StDev > TrendLinesTrends.StDev; } }
    public TradeConditionDelegate TpsOk { get { return () => IsTresholdAbsOk(TicksPerSecondAverage, TpsMin); } }
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
    public TradeConditionDelegate[] _TradeConditions = new TradeConditionDelegate[0];
    public TradeConditionDelegate[] TradeConditions {
      get { return _TradeConditions; }
      set {
        _TradeConditions = value;
        OnPropertyChanged("TradeConditions");
      }
    }
    void TradeConditionsReset() { TradeConditions = new TradeConditionDelegate[0]; }
    public TradeConditionDelegate[] GetTradeConditions() { return new[] { WideOk, TpsOk, AngleOk, Angle0Ok }; }
    public static string ParseTradeConditionName(MethodInfo method) {
      return Regex.Match(method.Name, "<(.+)>").Groups[1].Value.Substring(4);

    }
    public TradeConditionDelegate[] TradeConditionsSet(IList<string> names) {
      return TradeConditions = GetTradeConditions().Where(tc => names.Contains(ParseTradeConditionName(tc.Method))).ToArray();
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
