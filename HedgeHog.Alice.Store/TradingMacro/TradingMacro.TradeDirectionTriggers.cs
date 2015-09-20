using HedgeHog.Bars;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Reactive.Threading;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    #region Trade Direction Triggers
    void TriggerOnOutside(Func<Func<TradingMacro, Rate.TrendLevels>, TradeDirections> isOutside, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      var td = isOutside(trendLevels);
      if(td.HasAny()) {
        if(TradeConditionsEval().All(b => b.HasAny()))
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
      if(!TradeConditionsHaveTurnOff() && Trades.Length == 0 && TradeConditionsEval().Any(b => b.HasAny())) {
        var bs = new[] { BuyLevel, SellLevel };
        double sell = GetTradeLevel(false, SellLevel.RateEx), buy = GetTradeLevel(true, BuyLevel.Rate);
        if(new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(sell, buy))) {
          BuyLevel.ResetPricePosition();
          BuyLevel.Rate = buy;// GetTradeLevel(true, BuyLevel.Rate);
          SellLevel.ResetPricePosition();
          SellLevel.Rate = sell;// GetTradeLevel(false, SellLevel.RateEx);
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
    public void OnBlueOk() {
      if(!TradeConditionsHaveTurnOff() && Trades.Length == 0 && TradeConditionsEval().Any(b => b.HasAny())) {
        var bs = new[] { BuyLevel, SellLevel };
        UseRates(ra => {
          var startIndex = ra.Count - (TrendLinesTrends.Count - 10);
          var count = TrendLinesTrends.Count - TrendLines1Trends.Count + 10;
          if(startIndex <= 0 || count >= startIndex)
            return new List<Rate>();
          return ra.GetRange(ra.Count - startIndex, count);
        })
        .Where(range=>range.Any())
        .ForEach(range => {
          range.Sort(r=>r.PriceAvg);
          var buy = range.Last().AskHigh;
          var sell = range.First().BidLow;
          //if(new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(sell, buy))) {
            BuyLevel.ResetPricePosition();
            BuyLevel.Rate = buy;// GetTradeLevel(true, BuyLevel.Rate);
            SellLevel.ResetPricePosition();
            SellLevel.Rate = sell;// GetTradeLevel(false, SellLevel.RateEx);
                                  // init trade levels
            bs.ForEach(sr => {
              sr.TradesCount = TradeCountStart;
              sr.CanTrade = true;
              sr.InManual = true;
            });
          //}
        });
      }
    }
    [TradeDirectionTrigger]
    public void OnAngRGOk() {
      var bs = new[] { BuyLevel, SellLevel };
      if(bs.Any(sr => sr.InManual))
        return;
      if(!TradeConditionsHaveTurnOff() && Trades.Length == 0 && TradeConditionsEval().Any(b => b.HasAny())) {
        var angCondsAll = new Dictionary<TradeConditionDelegate, Rate.TrendLevels> {
          { GreenAngOk,TrendLines1Trends  },
          { RedAngOk,TrendLinesTrends  },
          { BlueAngOk,TrendLines2Trends  }
        };
        var angConds = from ac in angCondsAll
                       join tc in TradeConditionsInfo() on ac.Key equals tc
                       select ac;
        var anonBS = MonoidsCore.ToFunc(0.0, 0.0, (buy, sell) => new { buy, sell });
        var getBS = MonoidsCore.ToFunc(0, count => {
          double buy, sell;
          RatesArray.GetRange(RatesArray.Count - count, count).Height(out sell, out buy);
          return anonBS(buy, sell);
        });
        var setLevels = MonoidsCore.ToFunc(anonBS(0, 0), x => {
          BuyLevel.ResetPricePosition();
          BuyLevel.Rate = x.buy;// GetTradeLevel(true, BuyLevel.Rate);
          SellLevel.ResetPricePosition();
          SellLevel.Rate = x.sell;// GetTradeLevel(false, SellLevel.RateEx);
                                  // init trade levels
          bs.ForEach(sr => {
            sr.TradesCount = TradeCountStart;
            sr.CanTrade = true;
            sr.InManual = true;
          });
          return true;
        });
        angConds
          //.Where(kv => kv.Key().HasAny()) //only need this for OR conditions
          .MaxBy(kv=>kv.Value.Count)
          .Select(kv => getBS(kv.Value.Count))
          .Where(x => new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(x.sell, x.buy)))
          .Select(x => MonoidsCore.ToFunc(() => setLevels(x)))
          .ForEach(a => a());
      }
    }

    [TradeDirectionTrigger]
    public void OnManualMove() {
      if(TradeConditionsHave(TimeFrameOk) && TimeFrameOk().HasAny()) {
        var bs = new[] { BuyLevel, SellLevel };
        if(bs.All(sr => sr.InManual)) {
          var buyOffset = CurrentPrice.Ask - BuyLevel.Rate;
          if(buyOffset > 0 && BuyLevel.TradesCount > 0)
            bs.ForEach(sr => sr.Rate += buyOffset);
          var sellOffset = CurrentPrice.Bid - SellLevel.Rate;
          if(sellOffset < 0 && SellLevel.TradesCount > 0)
            bs.ForEach(sr => sr.Rate += sellOffset);
        }
      }
    }
    [TradeDirectionTrigger]
    public void OnHaveTurnOff() {
      if(TradeConditionsHaveTurnOff()) 
        TurnOfManualCorridor();
    }

    private void TurnOfManualCorridor() {
      if(!Trades.Any())
        new[] { BuyLevel, SellLevel }
        .Where(sr => sr.InManual)
        .Do(sr => {
          sr.CanTrade = false;
          sr.InManual = false;
          sr.TradesCount = TradeCountStart;
        })
        .Take(1)
        .ForEach(_ => Log = new Exception("TurnOfManualCorridor"));
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
