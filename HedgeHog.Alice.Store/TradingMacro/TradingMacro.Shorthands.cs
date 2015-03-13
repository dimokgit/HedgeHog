using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;
using HedgeHog.Bars;
using HedgeHog;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    void _setAlwaysOnDefault() { Log = new Exception("SetAlwaysOn is not implemented"); }
    Action _setAlwaysOn;
    public Action SetAlwaysOn {
      get { return _setAlwaysOn ?? _setAlwaysOnDefault; }
      set { _setAlwaysOn = value; }
    }
    public void ResetSuppResesInManual() {
      ResetSuppResesInManual(!_suppResesForBulk().Any(sr => sr.InManual));
      RaiseShowChart();
    }
    public void ResetSuppResesInManual(bool isManual) {
      _suppResesForBulk().ToList().ForEach(sr => sr.InManual = isManual);
      RaiseShowChart();
    }
    public void SetCanTrade(bool canTrade, bool? isBuy) {
      _suppResesForBulk()
        .Where(sr => sr.IsBuy == isBuy.GetValueOrDefault(sr.IsBuy))
        .ForEach(sr => sr.CanTrade = canTrade);
      RaiseShowChart();
    }
    public void ToggleCanTrade() {
      var srs = _suppResesForBulk().ToList();
      var canTrade = !srs.Any(sr => sr.CanTrade);
      srs.ForEach(sr => sr.CanTrade = canTrade);
      RaiseShowChart();
    }
    public void SetTradeCount(int tradeCount) {
      _suppResesForBulk().ForEach(sr => sr.TradesCount = tradeCount);
      RaiseShowChart();
    }
    public void FlipTradeLevels() {
      try {
        IsTradingActive = false;
        var b = BuyLevel.Rate;
        BuyLevel.Rate = SellLevel.Rate;
        SellLevel.Rate = b;
        var s = LevelSellBy;
        LevelSellBy = LevelBuyBy;
        LevelBuyBy = s;
      } catch (Exception exc) {
        Log = exc;
      }
      RaiseShowChart();
    }

    public void WrapTradeInCorridor() {
      if (Trades.Any()) {
        BuyLevel.InManual = SellLevel.InManual = true;
        LevelBuyBy = LevelSellBy = LevelBuyCloseBy = LevelSellCloseBy = TradeLevelBy.None;
        if (Trades.HaveBuy()) {
          BuyLevel.Rate = Trades.NetOpen();
          SellLevel.Rate = BuyLevel.Rate - CorridorStats.HeightByRegression;
        } else {
          SellLevel.Rate = Trades.NetOpen();
          BuyLevel.Rate = SellLevel.Rate + CorridorStats.HeightByRegression;
        }
      }
      RaiseShowChart();
    }
    public void SetDefaultTradeLevels() {
      Func<bool> isWide2 = () =>
        LevelBuyBy == TradeLevelBy.PriceAvg22 &&
        LevelSellBy == TradeLevelBy.PriceAvg32;
      Func<bool> isNarrow = () =>
        LevelBuyBy == TradeLevelBy.PriceAvg2 &&
        LevelSellBy == TradeLevelBy.PriceAvg3;
      Action<Rate> setWide = rate => {
        LevelBuyBy = TradeLevelBy.PriceAvg21;
        LevelSellBy = TradeLevelBy.PriceAvg31;
        BuyLevel.Rate = rate.PriceAvg21;
        SellLevel.Rate = rate.PriceAvg31;
      };
      Action<Rate> setWide2 = rate => {
        LevelBuyBy = TradeLevelBy.PriceAvg22;
        LevelSellBy = TradeLevelBy.PriceAvg32;
        BuyLevel.Rate = rate.PriceAvg2 + rate.PriceAvg2 - rate.PriceAvg1;
        SellLevel.Rate = rate.PriceAvg3 - (rate.PriceAvg1 - rate.PriceAvg3);
      };
      Action<Rate> setNarrow = rate => {
        LevelBuyBy = TradeLevelBy.PriceAvg2;
        LevelSellBy = TradeLevelBy.PriceAvg3;
        BuyLevel.Rate = rate.PriceAvg2;
        SellLevel.Rate = rate.PriceAvg3;
      };
      CorridorStats.Rates.Where(r => !r.PriceAvg1.IsNaN())
        .Take(1)
        .ForEach(rate => {
          IsTradingActive = false;
          LevelBuyCloseBy = LevelSellCloseBy = TradeLevelBy.None;
          (isWide2() ? setNarrow : isNarrow() ? setWide : setWide2)(rate);
        });
      RaiseShowChart();
    }
    public void SetTradeLevelsPreset(TradeLevelsPreset preset) {
      IsTradingActive = false;
      Action<TradeLevelBy,TradeLevelBy> setLevels =(b,s)=> CorridorStats.Rates.Where(r => !r.PriceAvg1.IsNaN())
        .Take(1)
        .ForEach(rate => {
          BuyLevel.Rate = TradeLevelFuncs[b](rate, CorridorStats);
          SellLevel.Rate = TradeLevelFuncs[s](rate, CorridorStats);
        });
      switch (preset) {
        case TradeLevelsPreset.SuperNarrow:
          LevelBuyBy = TradeLevelBy.PriceAvg02;
          LevelSellBy = TradeLevelBy.PriceAvg03;
          break;
        case TradeLevelsPreset.Narrow:
          LevelBuyBy = TradeLevelBy.PriceAvg2;
          LevelSellBy = TradeLevelBy.PriceAvg3;
          break;
        case TradeLevelsPreset.Wide:
          LevelBuyBy = TradeLevelBy.PriceAvg21;
          LevelSellBy = TradeLevelBy.PriceAvg31;
          break;
        case TradeLevelsPreset.SuperWide:
          LevelBuyBy = TradeLevelBy.PriceAvg22;
          LevelSellBy = TradeLevelBy.PriceAvg32;
          break;
      }
      setLevels(LevelBuyBy, LevelSellBy);
    }
  }
}
