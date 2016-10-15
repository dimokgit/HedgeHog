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
    private Store.SuppRes[] SellCloseSupResLevel() {
      var sellCloseLevel = Resistance1();
      sellCloseLevel.Where(sr => !sr.IsExitOnly).ForEach(sr => sr.IsExitOnly = true);
      return sellCloseLevel;
    }

    #region EllasticRange
    private int _EllasticRange = 5;
    [WwwSetting(wwwSettingsTradingProfit)]
    [Category(categoryTrading)]
    public int EllasticRange {
      get { return _EllasticRange; }
      set {
        if(_EllasticRange != value) {
          _EllasticRange = value;
          OnPropertyChanged(() => EllasticRange);
        }
      }
    }

    #endregion
    #region DistanceDaysBack
    private int _DistanceDaysBack = 2;
    [Category(categoryXXX)]
    public int DistanceDaysBack {
      get { return _DistanceDaysBack; }
      set {
        if(_DistanceDaysBack != value) {
          _DistanceDaysBack = value;
          OnPropertyChanged("DistanceDaysBack");
        }
      }
    }

    #endregion
    double CrossLevelDefault(bool isBuy) { return isBuy ? _RatesMax + RatesHeight : _RatesMin - RatesHeight; }
    delegate double SetExitDelegate(double currentPrice, double exitLevel, Func<double, double> calcExitLevel);
    private Action<double, double> AdjustCloseLevels() {
      Store.SuppRes buyCloseLevel = BuyCloseSupResLevel().First();
      Store.SuppRes sellCloseLevel = SellCloseSupResLevel().First();
      Action<double, double> adjustExitLevels = (buyLevel, sellLevel) => {
        #region Set (buy/sell)Level
        {
          var d = Trades.CopyLast(1).Select(t => t.Time).FirstOrDefault();
          var rateSinceTrade = EnumerableEx.If(() => !d.IsMin() && DoAdjustExitLevelByTradeTime, RatesArray
            .Reverse<Rate>()
            .TakeWhile(r => r.StartDate >= d)
            .Select(_priceAvg))
            .Memoize(2);

          Func<SuppRes, IEnumerable<double>> getLevel = sr =>
            EnumerableEx.If(() => !ExitByBuySellLevel, Trades.NetOpen().Yield()).DefaultIfEmpty(sr.Rate);
          Func<double, SuppRes, IEnumerable<double>> getLevels = (level, sr) =>
           rateSinceTrade
            .Concat(level.Yield()
              .Expand(l => EnumerableEx.If(l.IsNaN().ToFunc(), getLevel(sr)))
              .Where(Lib.IsNotNaN)
              .Take(1)
            );
          //buyLevel = getLevels(buyLevel, BuyLevel).Min();
          //sellLevel = getLevels(sellLevel, SellLevel).Max();
        }
        #endregion
        if(buyLevel.Min(sellLevel) < .5) {
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
        if(tradesCount == 0) {
          //if (LevelBuyCloseBy == TradeLevelBy.None) 
          setExitLevel(buyCloseLevel);
          //if (LevelSellCloseBy == TradeLevelBy.None) 
          setExitLevel(sellCloseLevel);
        } else {
          if(!Trades.Any()) {
            throw new Exception("Should have some trades here.");
            //adjustExitLevels(buyLevel, sellLevel);
            //buyCloseLevel.ResetPricePosition();
            //sellCloseLevel.ResetPricePosition();
          } else {
            var cpBuy = CurrentExitPrice(true);
            var cpSell = CurrentExitPrice(false);
            var calculateTakeProfit = CalculateTakeProfit();
            var takeProfitPips = InPips(calculateTakeProfit);
            var currentGrossOthers = _tradingStatistics.TradingMacros.Where(tm => tm != this).Sum(tm => tm.CurrentGross);
            var currentGrossOthersInPips = TradesManager.MoneyAndLotToPips(currentGrossOthers, CurrentGrossLot, Pair);
            var ellasic = RatesArray.CopyLast(EllasticRange).Average(_priceAvg).Abs(RateLast.PriceAvg);
            var ratesHeightInPips = new[] {
              LimitProfitByRatesHeight? TradingDistance :double.NaN
            }.Min(m => InPips(m));
            var takeBackInPips = (IsTakeBack ? Trades.GrossInPips() - CurrentGrossInPips - currentGrossOthersInPips + this.PriceSpreadAverageInPips : 0);
            var ratesShort = RatesArray.CopyLast(5);
            var priceAvgMax = ratesShort.Max(GetTradeExitBy(true)).Max(cpBuy - PointSize / 10);
            var priceAvgMin = ratesShort.Min(GetTradeExitBy(false)).Min(cpSell + PointSize / 10);
            var takeProfitLocal = TakeProfitFunction.IfNotDirect(takeProfitPips,
              tp => (tp + (UseLastLoss ? LastTradeLossInPips.Abs() : 0)).Max(takeBackInPips).Min(ratesHeightInPips));
            Func<bool, double> levelByNetOpenAndTakeProfit = isBuy => isBuy
              ? Trades.IsBuy(isBuy).NetOpen() + InPoints(takeProfitLocal)
              : Trades.IsBuy(isBuy).NetOpen() - InPoints(takeProfitLocal);
            Func<bool, double> getTradeCloseLevel = isBuy => !IsTakeBack
              ? GetTradeCloseLevel(isBuy)
              : isBuy
              ? levelByNetOpenAndTakeProfit(isBuy).Max(GetTradeCloseLevel(isBuy))
              : levelByNetOpenAndTakeProfit(isBuy).Min(GetTradeCloseLevel(isBuy));
            Func<bool, double> levelByDefault = isBuy => IsTakeBack
              ? double.NaN
              : isBuy
              ? buyLevel + calculateTakeProfit
              : sellLevel - calculateTakeProfit;
            if(buyCloseLevel.IsGhost)
              setExitLevel(buyCloseLevel);
            else if(buyCloseLevel.InManual) {
              if(buyCloseLevel.Rate <= priceAvgMax)
                buyCloseLevel.Rate = priceAvgMax;
            } else if(Trades.HaveBuy()) {
              var signB = (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign();
              buyCloseLevel.RateEx = new[]{
                getTradeCloseLevel(true)
                .Min(levelByNetOpenAndTakeProfit(true))
                .Min(levelByDefault(true))
                , priceAvgMax
              }.MaxBy(l => l)/*.Select(l => setBuyExit(l))*/.First() - ellasic
              ;
              if(signB != (_buyLevelNetOpen() - buyCloseLevel.Rate).Sign())
                buyCloseLevel.ResetPricePosition();
            } else if(LevelBuyCloseBy == TradeLevelBy.None)
              buyCloseLevel.RateEx = CrossLevelDefault(true);

            if(sellCloseLevel.IsGhost)
              setExitLevel(sellCloseLevel);
            else if(sellCloseLevel.InManual) {
              if(sellCloseLevel.Rate >= priceAvgMin)
                sellCloseLevel.Rate = priceAvgMin;
            } else if(Trades.HaveSell()) {
              var sign = (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign();
              sellCloseLevel.RateEx = new[] {
                getTradeCloseLevel(false)
                .Max(levelByNetOpenAndTakeProfit(false))
                .Max(levelByDefault(false))
                , priceAvgMin
              }.MinBy(l => l)/*.Select(l => setSellExit(l))*/.First() + ellasic
              ;
              if(sign != (_sellLevelNetOpen() - sellCloseLevel.Rate).Sign())
                sellCloseLevel.ResetPricePosition();
            } else if(LevelSellCloseBy == TradeLevelBy.None)
              sellCloseLevel.RateEx = CrossLevelDefault(false);
          }
        }
      };
      return adjustExitLevels;
    }
    bool _limitProfitByRatesHeight;
    [WwwSetting(wwwSettingsTradingProfit)]
    [Category(categoryActiveYesNo)]
    public bool LimitProfitByRatesHeight {
      get { return _limitProfitByRatesHeight; }
      set {
        _limitProfitByRatesHeight = value;
        OnPropertyChanged(() => LimitProfitByRatesHeight);
      }
    }
    private void AdjustExitLevelsByTradeTime(Action<double, double> adjustExitLevels) {
      Func<double, IEnumerable<double>> rateSinceTrade = def => {
        var d = Trades.Max(t => t.Time);
        d = d - ServerTime.Subtract(d);
        return TradingMacroTrender(tm
          => tm.UseRates(rates
          => rates.BackwardsIterator()
            .TakeWhile(r => r.StartDate >= d)
            .Select(GetPriceMA)))
            .SelectMany(x => x)
            .SelectMany(x => x)
            .DefaultIfEmpty(def);
      };
      var buyLevel = Trades.HaveBuy() ? rateSinceTrade(BuyLevel.Rate).Min().Min(ExitByBuySellLevel ? BuyLevel.Rate : double.NaN) : BuyLevel.Rate;
      var sellLevel = Trades.HaveSell() ? rateSinceTrade(SellLevel.Rate).Max().Max(ExitByBuySellLevel ? SellLevel.Rate : double.NaN) : SellLevel.Rate;
      adjustExitLevels(buyLevel, sellLevel);
    }
  }
}
