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
    }
    public void ResetSuppResesInManual(bool isManual) {
      if (!isManual)
        IsTradingActive = false;
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
    public void WrapCurrentPriceInCorridor() {
      LevelBuyBy = LevelSellBy = LevelBuyCloseBy = LevelSellCloseBy = TradeLevelBy.None;
      var offset = StDevByHeight / 2;
      BuyLevel.Rate = CurrentPrice.Average + offset;
      SellLevel.Rate = CurrentPrice.Average - offset;
      BuyLevel.InManual = SellLevel.InManual = true;
      BuyLevel.TradesCount = SellLevel.TradesCount = 0;
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
    public void SetTradeLevel(TradeLevelBy level, bool isBuy) {
      Action setLevels = () => CorridorStats.Rates.Where(r => !r.PriceAvg1.IsNaN())
        .Take(1)
        .ForEach(rate => {
          IsTradingActive = false;
          if (isBuy) {
            LevelBuyBy = level;
            BuyLevel.Rate = TradeLevelFuncs[level]();
          } else {
            LevelSellBy = level;
            SellLevel.Rate = TradeLevelFuncs[level]();
          }
          RaiseShowChart();
        });
    }
    public void SetTradeLevelsPreset(TradeLevelsPreset preset, bool? isBuy) {
      Dictionary<TradeLevelsPreset, Tuple<TradeLevelBy,TradeLevelBy>> tlbs = new Dictionary<TradeLevelsPreset, Tuple<TradeLevelBy,TradeLevelBy>>(){
        {TradeLevelsPreset.SuperNarrow,Tuple.Create( TradeLevelBy.PriceAvg02, TradeLevelBy.PriceAvg03)},
        {TradeLevelsPreset.Narrow,Tuple.Create( TradeLevelBy.PriceAvg2, TradeLevelBy.PriceAvg3)},
        {TradeLevelsPreset.Wide,Tuple.Create( TradeLevelBy.PriceAvg21, TradeLevelBy.PriceAvg31)},
        {TradeLevelsPreset.SuperWide,Tuple.Create( TradeLevelBy.PriceAvg22, TradeLevelBy.PriceAvg32)},
        {TradeLevelsPreset.Corridor2,Tuple.Create( TradeLevelBy.PriceHigh, TradeLevelBy.PriceLow)},
        {TradeLevelsPreset.Corridor1,Tuple.Create( TradeLevelBy.PriceHigh0, TradeLevelBy.PriceLow0)},

        {TradeLevelsPreset.SuperNarrowR,Tuple.Create( TradeLevelBy.PriceAvg03, TradeLevelBy.PriceAvg02)},
        {TradeLevelsPreset.NarrowR,Tuple.Create( TradeLevelBy.PriceAvg3, TradeLevelBy.PriceAvg2)},
        {TradeLevelsPreset.WideR,Tuple.Create( TradeLevelBy.PriceAvg31, TradeLevelBy.PriceAvg21)},
        {TradeLevelsPreset.SuperWideR,Tuple.Create( TradeLevelBy.PriceAvg32, TradeLevelBy.PriceAvg22)},
        {TradeLevelsPreset.Corridor2R,Tuple.Create( TradeLevelBy.PriceLow, TradeLevelBy.PriceHigh)},
        {TradeLevelsPreset.Corridor1R,Tuple.Create( TradeLevelBy.PriceLow0, TradeLevelBy.PriceHigh0)}

      };
      IsTradingActive = false;
      Action setLevels =()=> CorridorStats.Rates.Where(r => !r.PriceAvg1.IsNaN())
        .Take(1)
        .ForEach(rate => {
          BuyLevel.Rate = TradeLevelFuncs[LevelBuyBy]();
          SellLevel.Rate = TradeLevelFuncs[LevelSellBy]();
          BuyLevel.InManual = SellLevel.InManual = false;
          RaiseShowChart();
        });
      Action<Func<TradeLevelBy>, Action<TradeLevelBy>, TradeLevelBy, TradeLevelBy> setTL = (g, s, v, rv) => s(v);
      if (isBuy.GetValueOrDefault(true)) 
        setTL(() => LevelBuyBy, v => LevelBuyBy = v, tlbs[preset].Item1, tlbs[preset].Item2);
      if (isBuy.GetValueOrDefault(false) == false)
        setTL(() => LevelSellBy, v => LevelSellBy = v, tlbs[preset].Item2, tlbs[preset].Item1);
      setLevels();
    }
    public void MoveBuySellLeve(bool isBuy, double pips) {
      Func<double, double> setOrDef = l => l > 0 ? l : RatesArray.Middle();
      new[] { BuyLevel, SellLevel }
        .Where(sr => sr.IsBuy == isBuy)
        .ForEach(sr => {
          if (pips == 0) {
            sr.Rate = setOrDef(sr.IsBuy ? CenterOfMassBuy : CenterOfMassSell);
            sr.InManual = true;
          } else {
            sr.Rate += InPoints(pips);
            sr.InManual = true;
          }
          RaiseShowChart();
        });
    }
  }
}
