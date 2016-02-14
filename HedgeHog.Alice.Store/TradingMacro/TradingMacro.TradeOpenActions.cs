using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    #region TradeOpenActions
    public delegate void TradeOpenAction(Trade trade);
    public TradeOpenAction MarkCorridorOnTrade { get { return trade => BuySellLevelsForEach(sr=> sr.SetRateTrade()); } }
    public TradeOpenAction FreezeOnTrade { get { return trade => BuyLevel.InManual = SellLevel.InManual = true; } }
    public TradeOpenAction WrapOnTrade {
      get {
        return trade => {
          BuySellLevels.SetInManual(true);
          WrapTradeInCorridor(true);
        };
      }
    }

    public TradeOpenAction Avg1ExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.Avg1Max, TradeLevelBy.Avg1Min));
      }
    }
    public TradeOpenAction Avg1GRBExitOnTrade {
      get {
        return trade => Task.Delay(100).ContinueWith(_ => SetCorridorExitImpl(trade, TradeLevelBy.Avg2GRBMax, TradeLevelBy.Avg3GRBMin));
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
      if(trade.IsBuy) {
        var level = TradeLevelFuncs[buy]();
        if(InPips(level - trade.Open) > 5)
          LevelBuyCloseBy = buy;
      } else {
        var level = TradeLevelFuncs[TradeLevelBy.PriceLow0]();
        if(InPips(-level + trade.Open) > 5)
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
      return _tradeOpenActions = GetTradeOpenActions().Where(tc => names.Contains(ParseTradeConditionNameFromMethod(tc.Method))).ToArray();
    }
    public IEnumerable<T> TradeOpenActionsInfo<T>(Func<TradeOpenAction, string, T> map) {
      return TradeOpenActionsInfo(_tradeOpenActions, map);
    }
    public IEnumerable<T> TradeOpenActionsAllInfo<T>(Func<TradeOpenAction, string, T> map) {
      return TradeOpenActionsInfo(GetTradeOpenActions(), map);
    }
    public IEnumerable<T> TradeOpenActionsInfo<T>(IList<TradeOpenAction> tradeOpenActions, Func<TradeOpenAction, string, T> map) {
      return tradeOpenActions.Select(tc => map(tc, ParseTradeConditionNameFromMethod(tc.Method)));
    }
    [DisplayName("Trade Actions")]
    [Category(categoryActiveFuncs)]
    public string TradeOpenActionsSave {
      get { return string.Join(MULTI_VALUE_SEPARATOR, TradeOpenActionsInfo((tc, name) => name)); }
      set {
        TradeOpenActionsSet(value.Split(MULTI_VALUE_SEPARATOR[0]));
      }
    }
    #endregion
  }
}
