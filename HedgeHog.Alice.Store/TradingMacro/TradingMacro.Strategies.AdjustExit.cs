using HedgeHog.Bars;
using HedgeHog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    private Store.SuppRes SellCloseSupResLevel() {
      var sellCloseLevel = Resistance1(); sellCloseLevel.IsExitOnly = true;
      return sellCloseLevel;
    }

    private Store.SuppRes BuyCloseSupResLevel() {
      var buyCloseLevel = Support1(); buyCloseLevel.IsExitOnly = true;
      return buyCloseLevel;
    }
    #region EllasticRange
    private int _EllasticRange = 5;
    [Category(categoryActive)]
    public int EllasticRange {
      get { return _EllasticRange; }
      set {
        if (_EllasticRange != value) {
          _EllasticRange = value;
          OnPropertyChanged(() => EllasticRange);
        }
      }
    }

    #endregion
    double CrossLevelDefault (bool isBuy){return isBuy ? _RatesMax + RatesHeight : _RatesMin - RatesHeight;}
    delegate double SetExitDelegate(double currentPrice, double exitLevel, Func<double, double> calcExitLevel);
    private Action<double, double> AdjustCloseLevels() {
        SetExitDelegate setExit = (currentPrice, exitLevel, calcExitLevel) =>
          new[] { BuyLevel, SellLevel }.All(sr => sr.CanTrade) && currentPrice.Between(SellLevel.Rate, BuyLevel.Rate)
            ? calcExitLevel(exitLevel)
            : exitLevel;
      Store.SuppRes buyCloseLevel = BuyCloseSupResLevel();
      Store.SuppRes sellCloseLevel = SellCloseSupResLevel();
      Action<double, double> adjustExitLevels = (buyLevel, sellLevel) => {
        #region Set (buy/sell)Level
        {
          var d = Trades.TakeLast(1).Select(t => t.Time).FirstOrDefault();
          var rateSinceTrade = EnumerableEx.If(() => !d.IsMin() && DoAdjustExitLevelByTradeTime, RatesArray
            .Reverse<Rate>()
            .TakeWhile(r => r.StartDate >= d)
            .Select(_priceAvg))
            .Memoize(2);

          Func<SuppRes, double> getLevel = sr =>
            EnumerableEx.If(() => !ExitByBuySellLevel, Trades.Select(trade => trade.Open)).DefaultIfEmpty(sr.Rate).Last();
          Func<double, SuppRes, IEnumerable<double>> getLevels = (level, sr) =>
           rateSinceTrade
            .Concat(level.Yield()
              .Expand(l => EnumerableEx.If(l.IsNaN().ToFunc(), getLevel(sr).Yield()))
              .Where(Lib.IsNotNaN)
              .Take(1)
            );
          buyLevel = getLevels(buyLevel, BuyLevel).Min();
          sellLevel = getLevels(sellLevel, SellLevel).Max();
        }
        #endregion
        if (buyLevel.Min(sellLevel) < .5) {
          Log = new Exception(new { buyLevel, sellLevel } + "");
          return;
        }
        buyCloseLevel.SetPrice(CurrentExitPrice(false));
        sellCloseLevel.SetPrice(CurrentExitPrice(true));
        #region setExitLevel
        Action<SuppRes> setExitLevel = sr => {
            sr.RateEx = CrossLevelDefault(sr.IsSell);
            sr.ResetPricePosition();
        };
        #endregion
        var tradesCount = Trades.Length;
        if (tradesCount == 0) {
          setExitLevel(buyCloseLevel);
          setExitLevel(sellCloseLevel);
        } else {
          if (!Trades.Any()) {
            throw new Exception("Should have some trades here.");
            //adjustExitLevels(buyLevel, sellLevel);
            //buyCloseLevel.ResetPricePosition();
            //sellCloseLevel.ResetPricePosition();
          } else {
            var cpBuy = CurrentExitPrice(true);
            var cpSell = CurrentExitPrice(false);
            Func<double, double> setBuyExit = (exitLevel) => setExit(cpBuy, exitLevel, el => BuyLevel.Rate.Max(el));
            Func<double, double> setSellExit = (exitLevel) => setExit(cpSell, exitLevel, el => SellLevel.Rate.Min(el));
            var tpColse = InPoints((TakeProfitPips - CurrentGrossInPipTotal).Min(TakeProfitPips));// ClosingDistanceByCurrentGross(takeProfitLimitRatio);
            var currentGrossOthers = _tradingStatistics.TradingMacros.Where(tm => tm != this).Sum(tm => tm.CurrentGross);
            var currentGrossOthersInPips = TradesManager.MoneyAndLotToPips(currentGrossOthers, CurrentGrossLot, Pair);
            var ellasic = RatesArray.TakeLast(EllasticRange).Average(_priceAvg).Abs(RateLast.PriceAvg);
            var ratesHeightInPips = new[] { 
              RatesArray.Take(RatesArray.Count * 9 / 10).Height() / 2,
              LimitProfitByStDev?StDevByPriceAvg:double.MaxValue
            }.Min(m => InPips(m));
            var takeBackInPips = (IsTakeBack ? Trades.GrossInPips() - CurrentGrossInPips - currentGrossOthersInPips : 0)
              .Min(ratesHeightInPips);
            var ratesShort = RatesArray.TakeLast(5).ToArray();
            var priceAvgMax = ratesShort.Max(GetTradeExitBy(true)).Max(cpBuy) - PointSize / 10;
            var priceAvgMin = ratesShort.Min(GetTradeExitBy(false)).Min(cpSell) + PointSize / 10;
            var takeProfitLocal = TakeProfitPips.Max(takeBackInPips).Min(ratesHeightInPips);
            if (buyCloseLevel.IsGhost)
              setExitLevel(buyCloseLevel);
            else if (buyCloseLevel.InManual) {
              if (buyCloseLevel.Rate <= priceAvgMax)
                buyCloseLevel.Rate = priceAvgMax;
            } else if (Trades.HaveBuy()) {
              var signB = (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign();
              buyCloseLevel.RateEx = new[]{
                Trades.IsBuy(true).NetOpen()+InPoints(takeProfitLocal)
                ,priceAvgMax
              }.MaxBy(l => l)/*.Select(l => setBuyExit(l))*/.First()
              - ellasic;
              if (signB != (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign())
                buyCloseLevel.ResetPricePosition();
            } else buyCloseLevel.RateEx = CrossLevelDefault(true);

            if (sellCloseLevel.IsGhost)
              setExitLevel(sellCloseLevel);
            else if (sellCloseLevel.InManual) {
              if (sellCloseLevel.Rate >= priceAvgMin)
                sellCloseLevel.Rate = priceAvgMin;
            } else if (Trades.HaveSell()) {
              var sign = (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign();
              var sellExit = ExitLevelByCurrentPrice(tpColse, false);
              sellCloseLevel.RateEx = new[] { 
                Trades.IsBuy(false  ).NetOpen()-InPoints(takeProfitLocal)
                , priceAvgMin
              }.MinBy(l => l)/*.Select(l => setSellExit(l))*/.First()
              + ellasic;
              if (sign != (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign())
                sellCloseLevel.ResetPricePosition();
            } else sellCloseLevel.RateEx = CrossLevelDefault(false);
          }
        }
      };
      return adjustExitLevels;
    }
    bool _limitProfitByStDev;
    [Category(categoryActiveYesNo)]
    public bool LimitProfitByStDev {
      get { return _limitProfitByStDev; }
      set {
        _limitProfitByStDev = value;
        OnPropertyChanged(() => LimitProfitByStDev);
      }
    }
    private double ExitLevelByCurrentPrice(bool isBuy) {
      return ExitLevelByCurrentPrice(ClosingDistanceByCurrentGross(), isBuy);
    }
    private double ExitLevelByCurrentPrice(double colsingDistance, bool isBuy) {
      return isBuy ? CurrentPrice.Bid + colsingDistance : CurrentPrice.Ask - colsingDistance;
    }

    private double ClosingDistanceByCurrentGross() { return ClosingDistanceByCurrentGross(() => TakeProfitLimitRatio); }
    private double ClosingDistanceByCurrentGross(Func<double> takeProfitLimitRatio) {
      var tpCloseInPips =  TakeProfitPips - CurrentGrossInPipTotal / _tradingStatistics.TradingMacros.Count;
      var tpColse = InPoints(tpCloseInPips);//.Min(TakeProfitPips * takeProfitLimitRatio()));//.Min(TradingDistance);
      return tpColse;
    }

    private void AdjustExitLevelsByTradeTime(Action<double, double> adjustExitLevels) {
      Func<double, IEnumerable<double>> rateSinceTrade = def => {
        var d = Trades.Max(t => t.Time);
        //d = d - ServerTime.Subtract(d);
        return RatesArray
          .Take(DoAdjustExitLevelByTradeTime ? RatesArray.Count : 0)
          .Reverse()
          .TakeWhile(r => r.StartDate >= d)
          .Select(_priceAvg)
          .DefaultIfEmpty(def);
      };
      var buyLevel = Trades.HaveBuy() ? rateSinceTrade(_buyLevel.Rate).Min().Min(ExitByBuySellLevel ? _buyLevel.Rate : double.NaN) : _buyLevel.Rate;
      var sellLevel = Trades.HaveSell() ? rateSinceTrade(_sellLevel.Rate).Max().Max(ExitByBuySellLevel ? _sellLevel.Rate : double.NaN) : _sellLevel.Rate;
      adjustExitLevels(buyLevel, sellLevel);
    }
  }
}
