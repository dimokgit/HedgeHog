using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;
using HedgeHog.Bars;
using HedgeHog;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    public void ResetSuppResesInManual() {
      ResetSuppResesInManual(!_suppResesForBulk().Any(sr => sr.InManual));
    }
    public void ResetSuppResesPricePosition() {
      SuppRes.ForEach(sr => sr.ResetPricePosition());
    }
    public void ResetSuppResesInManual(bool isManual) {
      if(ShouldTurnTradingOff() && !isManual)
        IsTradingActive = false;
      _suppResesForBulk().ToList().ForEach(sr => sr.InManual = isManual);
      RaiseShowChart();
    }

    private bool ShouldTurnTradingOff() {
      return BuySellLevels.Any(sr => sr.CanTrade) || HaveTrades();
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
    public IEnumerable<bool> ToggleCanTrade(bool isBuy) {
      return BuySellLevels.IsBuy(isBuy).Do(sr => sr.CanTrade = !sr.CanTrade).Select(sr => sr.CanTrade);
    }
    public void SetTradeCount(int tradeCount) {
      _suppResesForBulk().ForEach(sr => sr.TradesCount = tradeCount);
      RaiseShowChart();
    }
    public void FlipTradeLevels() {
      try {
        if(ShouldTurnTradingOff())
          IsTradingActive = false;
        var b = BuyLevel.Rate;
        BuyLevel.Rate = SellLevel.Rate;
        SellLevel.Rate = b;
        var s = LevelSellBy;
        LevelSellBy = LevelBuyBy;
        LevelBuyBy = s;
      } catch(Exception exc) {
        Log = exc;
      }
      RaiseShowChart();
    }

    public void WrapTradeInCorridorEdge() {
      if(Trades.Any()) {
        SuppRes.ForEach(sr => sr.ResetPricePosition());
        BuyLevel.InManual = SellLevel.InManual = true;
        var sd = TrendLinesByDate.Last().StartDate;
        var rates = RatesArray.BackwardsIterator().TakeWhile(r => r.StartDate >= sd);
        if(Trades.HaveBuy()) {
          SellLevel.Rate = rates.Min(r => r.BidLow);
          BuyLevel.Rate = Trades.NetOpen();
        } else {
          BuyLevel.Rate = rates.Max(r => r.AskHigh);
          SellLevel.Rate = Trades.NetOpen();
        }
        SuppRes.ForEach(sr => sr.ResetPricePosition());
      }
      RaiseShowChart();
    }
    public void WrapTradeInCorridor(bool forceMove = false, bool useTakeProfit = true) {
      if(Trades.Any() && (SuppRes.All(sr => !sr.InManual) || forceMove)) {
        SuppRes.ForEach(sr => sr.ResetPricePosition());
        BuyLevel.InManual = SellLevel.InManual = true;
        double offset = HeightForWrapToCorridor(useTakeProfit);
        if(Trades.HaveBuy()) {
          BuyLevel.Rate = Trades.NetOpen();
          SellLevel.Rate = BuyLevel.Rate - offset;
        } else {
          SellLevel.Rate = Trades.NetOpen();
          BuyLevel.Rate = SellLevel.Rate + offset;
        }
        SuppRes.ForEach(sr => sr.ResetPricePosition());
      }
      RaiseShowChart();
    }
    public void WrapTradeInTradingDistance(bool forceMove = false) {
      if(Trades.Any() && (SuppRes.All(sr => !sr.InManual) || forceMove)) {
        SuppRes.ForEach(sr => sr.ResetPricePosition());
        BuyLevel.InManual = SellLevel.InManual = true;
        double offset = CalculateTradingDistance();
        if(Trades.HaveBuy()) {
          BuyLevel.Rate = Trades.NetOpen();
          SellLevel.Rate = BuyLevel.Rate - offset;
        } else {
          SellLevel.Rate = Trades.NetOpen();
          BuyLevel.Rate = SellLevel.Rate + offset;
        }
        SuppRes.ForEach(sr => sr.ResetPricePosition());
      }
      RaiseShowChart();
    }

    private double HeightForWrapToCorridor(bool useTakeProfit) {
      return BuyLevel.Rate.Abs(SellLevel.Rate)
        .Max(StDevByPriceAvg, TakeProfitFunction == TradingMacroTakeProfitFunction.Pips || !useTakeProfit
        ? 0
        : CalculateTakeProfit(1));
    }

    public void WrapCurrentPriceInCorridor(int count) {
      UseRates(ra => ra.GetRange(ra.Count - count, count)).ForEach(rates => {
        if(rates.Any()) {
          rates.Sort(r => r.PriceAvg);
          LevelBuyCloseBy = LevelSellCloseBy = TradeLevelBy.None;
          BuyLevel.Rate = rates.Last().AskHigh;
          SellLevel.Rate = rates.First().BidLow;
          BuyLevel.InManual = SellLevel.InManual = true;
          BuyLevel.TradesCount = SellLevel.TradesCount = TradeCountStart;
          IsTradingActive = false;
          RaiseShowChart();
        }
      });
    }
    public void SetDefaultTradeLevels() {
      Func<bool> isNarrow = () =>
        LevelBuyBy == TradeLevelBy.PriceAvg2 &&
        LevelSellBy == TradeLevelBy.PriceAvg3;
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

        });
      RaiseShowChart();
    }
    public void SetTradeLevel(TradeLevelBy level, bool isBuy) {
      Action setLevels = () => CorridorStats.Rates.Where(r => !r.PriceAvg1.IsNaN())
        .Take(1)
        .ForEach(rate => {
          IsTradingActive = false;
          if(isBuy) {
            LevelBuyBy = level;
            BuyLevel.Rate = TradeLevelFuncs[level]();
          } else {
            LevelSellBy = level;
            SellLevel.Rate = TradeLevelFuncs[level]();
          }
          RaiseShowChart();
        });
    }
    static Dictionary<TradeLevelsPreset, Tuple<TradeLevelBy, TradeLevelBy>> tlbs = new Dictionary<TradeLevelsPreset, Tuple<TradeLevelBy, TradeLevelBy>>(){
        {TradeLevelsPreset.None,Tuple.Create( TradeLevelBy.None, TradeLevelBy.None)},
        {TradeLevelsPreset.Lime,Tuple.Create( TradeLevelBy.LimeMax, TradeLevelBy.LimeMin)},
        {TradeLevelsPreset.Green,Tuple.Create( TradeLevelBy.GreenMax, TradeLevelBy.GreenMin)},
        {TradeLevelsPreset.Red,Tuple.Create( TradeLevelBy.RedMax, TradeLevelBy.RedMin)},
        {TradeLevelsPreset.Plum,Tuple.Create( TradeLevelBy.PlumMax, TradeLevelBy.PlumMin)},
        {TradeLevelsPreset.Blue,Tuple.Create( TradeLevelBy.BlueMax, TradeLevelBy.BlueMin)},
        {TradeLevelsPreset.Lime23,Tuple.Create( TradeLevelBy.PriceLimeH, TradeLevelBy.PriceLimeL)},
        {TradeLevelsPreset.Green23,Tuple.Create( TradeLevelBy.PriceHigh0, TradeLevelBy.PriceLow0)},
        {TradeLevelsPreset.Red23,Tuple.Create( TradeLevelBy.PriceAvg2, TradeLevelBy.PriceAvg3)},
        {TradeLevelsPreset.Blue23,Tuple.Create( TradeLevelBy.PriceHigh, TradeLevelBy.PriceLow)},
        {TradeLevelsPreset.MinMax,Tuple.Create( TradeLevelBy.PriceMax, TradeLevelBy.PriceMin)},

        {TradeLevelsPreset.NarrowR,Tuple.Create( TradeLevelBy.PriceAvg3, TradeLevelBy.PriceAvg2)},
        {TradeLevelsPreset.BBand,Tuple.Create( TradeLevelBy.BoilingerUp, TradeLevelBy.BoilingerDown)},
        {TradeLevelsPreset.TLMinMax,Tuple.Create( TradeLevelBy.TrendMax, TradeLevelBy.TrendMin)},
        {TradeLevelsPreset.Corridor2R,Tuple.Create( TradeLevelBy.PriceLow, TradeLevelBy.PriceHigh)}

      };
    public void SetTradeLevelsPreset(TradeLevelsPreset preset, bool? isBuy) {
      IsTradingActive = false;
      Action setLevels = () => CorridorStats.Rates.Where(r => !r.PriceAvg1.IsNaN())
         .Take(1)
         .ForEach(rate => {
           BuyLevel.Rate = TradeLevelFuncs[LevelBuyBy]();
           SellLevel.Rate = TradeLevelFuncs[LevelSellBy]();
           BuyLevel.InManual = SellLevel.InManual = false;
           RaiseShowChart();
         });
      Action<Action<TradeLevelBy>, TradeLevelBy> setTL = (s, v) => s(v);
      if(isBuy.GetValueOrDefault(true))
        setTL(v => LevelBuyBy = v, tlbs[preset].Item1);
      if(isBuy.GetValueOrDefault(false) == false)
        setTL(v => LevelSellBy = v, tlbs[preset].Item2);
      setLevels();
    }
    public void SetTradeRate(bool isBuy, double price) {
      BuySellLevels
        .Where(sr => sr.IsBuy == isBuy)
        .ForEach(sr => {
          IsTradingActive = false;
          sr.InManual = true;
          sr.Rate = price;
          RaiseShowChart();
        });
    }
    public IEnumerable<TradeLevelsPreset> GetTradeLevelsPreset() {
      var bl = LevelBuyBy;
      var sl = LevelSellBy;
      return tlbs.Where(tlb =>
      tlb.Value.Item1 == bl && tlb.Value.Item2 == sl ||
      tlb.Value.Item1 == sl && tlb.Value.Item2 == bl
      ).Select(tlb => tlb.Key);
    }
    public void MoveBuySellLeve(bool isBuy, double pips) {
      Func<double, double> setOrDef = l => l > 0 ? l : RatesArray.Middle();
      new[] { BuyLevel, SellLevel }
        .Where(sr => sr.IsBuy == isBuy)
        .ForEach(sr => {
          if(pips == 0) {
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
