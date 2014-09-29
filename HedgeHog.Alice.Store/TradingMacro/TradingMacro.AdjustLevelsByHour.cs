using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store
{
	partial class TradingMacro
	{
            #region exitByLimit
        Action exitByLimit = () => {
          if (TradesManager.MoneyAndLotToPips(OpenTradesGross, Trades.Lots(), Pair) >= TakeProfitPips * ProfitToLossExitRatio)
            CloseAtZero = true;
        };
        #endregion

            #region CorridorBB
            case TrailingWaveMethod.CorridorBB:
              if (firstTime) {
                onCloseTradeLocal = trade => {
                  if (isCurrentGrossOk())
                    triggerAngle.Off();
                  if(!IsTradingHour())
                    _buySellLevelsForEach(sr => sr.CanTrade = false);
                };
              }{
                var rates = setTrendLines(2);
                var isBreakOut = _buyLevel.Rate >= _sellLevel.Rate;
                var canTrade = IsAutoStrategy && !_buySellLevels.Any(sr => sr.InManual);
                if (canTrade) {
                  var angle = CorridorStats.Slope.Angle(BarPeriodInt, PointSize).Abs();
                  var isAngleOk = angle <= TradingAngleRange;
                  var isTimeFrameOk = WaveShort.Rates.Count > CorridorDistanceRatio;
                  //triggerAngle.Set(angle <= TradingAngleRange, () => _buySellLevelsForEach(sr => sr.CanTrade = true));
                  var isHourOK = IsTradingHour();
                  if (!isHourOK && Trades.Any())
                    CloseAtZero = true;
                  if (isAngleOk && isTimeFrameOk && isHourOK) {
                    _buySellLevelsForEach(sr => sr.CanTrade = isAngleOk && isTimeFrameOk);
                    _buyLevel.RateEx = isBreakOut ? rates[0].PriceAvg2 : rates[0].PriceAvg3;
                    _sellLevel.RateEx = isBreakOut ? rates[0].PriceAvg3 : rates[0].PriceAvg2;
                  } 
                }
              }
              if (StreatchTakeProfit) adjustExitLevels0(); else adjustExitLevels1();
              break;
            #endregion
	}
}
