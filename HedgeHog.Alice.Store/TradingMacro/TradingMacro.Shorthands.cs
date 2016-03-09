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
      } catch (Exception exc) {
        Log = exc;
      }
      RaiseShowChart();
    }

    #region MoveWrapTradeWithNewTrade
    private bool _MoveWrapTradeWithNewTrade = false ;
    [Category(categoryTrading)]
    [WwwSetting]
    public bool MoveWrapTradeWithNewTrade {
      get { return _MoveWrapTradeWithNewTrade; }
      set {
        if (_MoveWrapTradeWithNewTrade != value) {
          _MoveWrapTradeWithNewTrade = value;
          OnPropertyChanged("MoveWrapTradeWithNewTrade");
        }
      }
    }
    
    #endregion
    public void WrapTradeInCorridor(bool forceMove = false) {
      if (Trades.Any() && (SuppRes.All(sr => !sr.InManual) || forceMove || MoveWrapTradeWithNewTrade)) {
        SuppRes.ForEach(sr => sr.ResetPricePosition());
        BuyLevel.InManual = SellLevel.InManual = true;
        double offset = HeightForWrapToCorridor();
        if (Trades.HaveBuy()) {
          BuyLevel.Rate = Trades.NetOpen();
          SellLevel.Rate = BuyLevel.Rate - offset;
        } else {
          SellLevel.Rate = Trades.NetOpen();
          BuyLevel.Rate = SellLevel.Rate + offset;
        }
      }
      RaiseShowChart();
    }

    private double HeightForWrapToCorridor() {
      return TakeProfitFunction == TradingMacroTakeProfitFunction.Pips
        ? StDevByPriceAvg
        : CalculateTakeProfit(1);
    }

    public void WrapCurrentPriceInCorridor(Rate.TrendLevels tls) {
      WrapCurrentPriceInCorridor(tls.Count);
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
        {TradeLevelsPreset.None,Tuple.Create( TradeLevelBy.None, TradeLevelBy.None)},
        {TradeLevelsPreset.Lime,Tuple.Create( TradeLevelBy.LimeMax, TradeLevelBy.LimeMin)},
        {TradeLevelsPreset.Green,Tuple.Create( TradeLevelBy.GreenMax, TradeLevelBy.GreenMin)},
        {TradeLevelsPreset.Red,Tuple.Create( TradeLevelBy.RedMax, TradeLevelBy.RedMin)},
        {TradeLevelsPreset.Green23,Tuple.Create( TradeLevelBy.PriceHigh0, TradeLevelBy.PriceLow0)},
        {TradeLevelsPreset.Red23,Tuple.Create( TradeLevelBy.PriceAvg2, TradeLevelBy.PriceAvg3)},
        {TradeLevelsPreset.Blue23,Tuple.Create( TradeLevelBy.PriceHigh, TradeLevelBy.PriceLow)},
        {TradeLevelsPreset.MinMax,Tuple.Create( TradeLevelBy.PriceMax, TradeLevelBy.PriceMin)},

        {TradeLevelsPreset.NarrowR,Tuple.Create( TradeLevelBy.PriceAvg3, TradeLevelBy.PriceAvg2)},
        {TradeLevelsPreset.Corridor2R,Tuple.Create( TradeLevelBy.PriceLow, TradeLevelBy.PriceHigh)},
        {TradeLevelsPreset.Corridor1R,Tuple.Create( TradeLevelBy.PriceLow0, TradeLevelBy.PriceHigh0)},
        {TradeLevelsPreset.MinMaxR,Tuple.Create( TradeLevelBy.PriceMin, TradeLevelBy.PriceMax)}

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
      Action<Action<TradeLevelBy>, TradeLevelBy> setTL = ( s, v) => s(v);
      if (isBuy.GetValueOrDefault(true)) 
        setTL( v => LevelBuyBy = v, tlbs[preset].Item1);
      if (isBuy.GetValueOrDefault(false) == false)
        setTL( v => LevelSellBy = v, tlbs[preset].Item2);
      setLevels();
    }
    public  void SetTradeRate(bool isBuy,double price) {
      BuySellLevels
        .Where(sr => sr.IsBuy == isBuy)
        .ForEach(sr => {
          IsTradingActive = false;
          sr.InManual = true;
          sr.Rate = price;
          RaiseShowChart();
        });
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
