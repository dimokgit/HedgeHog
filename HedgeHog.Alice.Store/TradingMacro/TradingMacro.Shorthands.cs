using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;

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
    }
    public void ResetSuppResesInManual(bool isManual) {
      _suppResesForBulk().ToList().ForEach(sr => sr.InManual = isManual);
    }
    public void SetCanTrade(bool canTrade, bool? isBuy) {
      _suppResesForBulk()
        .Where(sr => sr.IsBuy == isBuy.GetValueOrDefault(sr.IsBuy))
        .ForEach(sr => sr.CanTrade = canTrade);
    }
    public void ToggleCanTrade() {
      var srs = _suppResesForBulk().ToList();
      var canTrade = !srs.Any(sr => sr.CanTrade);
      srs.ForEach(sr => sr.CanTrade = canTrade);
    }
    public void SetTradeCount(int tradeCount) {
      _suppResesForBulk().ForEach(sr => sr.TradesCount = tradeCount);
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
    }
    public void SetDefaultTradeLevels() {
      CorridorStats.Rates.Where(r => !r.PriceAvg1.IsNaN())
        .Take(1)
        .ForEach(rate => {
          IsTradingActive = false;
          LevelBuyCloseBy = LevelSellCloseBy = TradeLevelBy.None;
          BuyLevel.InManual = SellLevel.InManual = true;
          Func<bool> isWide2 = () =>
            LevelBuyBy == TradeLevelBy.PriceAvg22 &&
            LevelSellBy == TradeLevelBy.PriceAvg32;
          Func<bool> isNarrow = () =>
            LevelBuyBy == TradeLevelBy.PriceAvg2 &&
            LevelSellBy == TradeLevelBy.PriceAvg3;
          Action setWide = () => {
            LevelBuyBy = TradeLevelBy.PriceAvg21;
            LevelSellBy = TradeLevelBy.PriceAvg31;
            BuyLevel.Rate = rate.PriceAvg21;
            SellLevel.Rate = rate.PriceAvg31;
          };
          Action setWide2 = () => {
            LevelBuyBy = TradeLevelBy.PriceAvg22;
            LevelSellBy = TradeLevelBy.PriceAvg32;
            BuyLevel.Rate = rate.PriceAvg2 + rate.PriceAvg2 - rate.PriceAvg1;
            SellLevel.Rate = rate.PriceAvg3-(rate.PriceAvg1-rate.PriceAvg3);
          };
          Action setNarrow = () => {
            LevelBuyBy = TradeLevelBy.PriceAvg2;
            LevelSellBy = TradeLevelBy.PriceAvg3;
            BuyLevel.Rate = rate.PriceAvg2;
            SellLevel.Rate = rate.PriceAvg3;
          };
          (isWide2() ? setNarrow : isNarrow() ? setWide : setWide2)();
        });
    }

  }
}
