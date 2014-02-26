using HedgeHog.Bars;
using HedgeHog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;

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

    private Action<double, double> AdjustCloseLevels(
      Func<double> takeProfitLimitRatio,
      Func<bool, double> crossLevelDefault,
      Func<bool, double> exitPrice,
      ObservableValue<double> ghostLevelOffset) {
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
        buyCloseLevel.SetPrice(exitPrice(false));
        sellCloseLevel.SetPrice(exitPrice(true));
        #region setExitLevel
        Action<SuppRes> setExitLevel = sr => {
          if (sr.IsGhost && sr.Rate.Between(SellLevel.Rate, BuyLevel.Rate)) {
            if (ghostLevelOffset == null) throw new ArgumentException(new { ghostLevelOffset } + "");
            var enterLevel = GetTradeEnterBy(sr.IsBuy)(RateLast);
            var offset = PointSize / 10;
            var rate = sr.Rate;
            if (sr.IsBuy) {
              if (sr.Rate - enterLevel > offset) {
                ghostLevelOffset.Value = enterLevel + offset - rate;
              }
            } else {
              if (enterLevel - sr.Rate > offset) {
                ghostLevelOffset.Value = enterLevel - offset - rate;
              }
            }
          } else {
            sr.RateEx = crossLevelDefault(sr.IsSell);
            sr.ResetPricePosition();
          }
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
            var clozeAtZero = false;// CloseAtZero;
            //if (isProfitOk()) CloseAtZero = true;
            var close0 = CloseAtZero || _trimAtZero || _trimToLotSize || MustExitOnReverse;
            var masterTakeProfit = _tradingStatistics.GrossToExitInPips.GetValueOrDefault(double.NaN);
            var tpColse = ClosingDistanceByCurrentGross(takeProfitLimitRatio, close0);
            var ratesShort = RatesArray.TakeLast(5).ToArray();
            var priceAvgMax = ratesShort.Max(GetTradeExitBy(true)) - PointSize / 10;
            var priceAvgMin = ratesShort.Min(GetTradeExitBy(false)) + PointSize / 10;
            var currentPriceMax = _priceQueue.Average(p => p.Bid);
            var currentPriceMin = _priceQueue.Average(p => p.Ask);
            if (buyCloseLevel.IsGhost)
              setExitLevel(buyCloseLevel);
            else if (buyCloseLevel.InManual) {
              if (buyCloseLevel.Rate <= priceAvgMax)
                buyCloseLevel.Rate = priceAvgMax;
            } else if (Trades.HaveBuy()) {
              var signB = (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign();
              buyCloseLevel.RateEx = new[]{
                ExitLevelByCurrentPrice(tpColse,true), 
                currentPriceMax
              }.Max();
              if (signB != (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign())
                buyCloseLevel.ResetPricePosition();
            } else buyCloseLevel.RateEx = crossLevelDefault(true);

            if (sellCloseLevel.IsGhost)
              setExitLevel(sellCloseLevel);
            else if (sellCloseLevel.InManual) {
              if (sellCloseLevel.Rate >= priceAvgMin)
                sellCloseLevel.Rate = priceAvgMin;
            } else if (Trades.HaveSell()) {
              var sign = (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign();
              sellCloseLevel.RateEx = new[] { 
                ExitLevelByCurrentPrice(tpColse,false),
                currentPriceMin
              }.Min();
              if (sign != (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign())
                sellCloseLevel.ResetPricePosition();
            } else sellCloseLevel.RateEx = crossLevelDefault(false);
          }
        }
      };
      return adjustExitLevels;
    }

    private double ExitLevelByCurrentPrice(bool isBuy) {
      return ExitLevelByCurrentPrice(ClosingDistanceByCurrentGross(), isBuy);
    }
    private double ExitLevelByCurrentPrice(double colsingDistance, bool isBuy) {
      return isBuy ? CurrentPrice.Bid + colsingDistance : CurrentPrice.Ask - colsingDistance;
    }

    private double ClosingDistanceByCurrentGross() { return ClosingDistanceByCurrentGross(() => TakeProfitLimitRatio, false); }
    private double ClosingDistanceByCurrentGross(Func<double> takeProfitLimitRatio, bool close0) {
      var tpCloseInPips = close0 ? 0 : TakeProfitPips - CurrentGrossInPipTotal / _tradingStatistics.TradingMacros.Count;
      var tpColse = InPoints(tpCloseInPips.Min(TakeProfitPips * takeProfitLimitRatio()));//.Min(TradingDistance);
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
