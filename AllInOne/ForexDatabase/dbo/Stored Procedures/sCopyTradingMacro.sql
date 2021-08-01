CREATE PROCEDURE [dbo].[sCopyTradingMacro] 
@Pair sysname,
@NameSource sysname,
@NameTarget sysname
AS
insert into TradingMacro
SELECT        Pair, TradingRatio, NEWID(), LimitBar, CurrentLoss, ReverseOnProfit, FreezLimit, CorridorMethod, FreezeStop, FibMax, FibMin, CorridornessMin, CorridorIterationsIn, CorridorIterationsOut, 
                         CorridorIterations, CorridorBarMinutes, PairIndex, TradingGroup, MaximumPositions, IsActive, @NameTarget, LimitCorridorByBarHeight, MaxLotByTakeProfitRatio, BarPeriodsLow, BarPeriodsHigh, 
                         StrictTradeClose, BarPeriodsLowHighRatio, LongMAPeriod, CorridorAverageDaysBack, CorridorPeriodsStart, CorridorPeriodsLength, CorridorStartDate, CorridorRatioForRange, CorridorRatioForBreakout, 
                         RangeRatioForTradeLimit, TradeByAngle, ProfitToLossExitRatio, TradeByFirstWave, PowerRowOffset, RangeRatioForTradeStop, ReversePower, CorrelationTreshold, CloseOnProfitOnly, CloseOnProfit, 
                         CloseOnOpen, StreachTradingDistance, CloseAllOnProfit, ReverseStrategy, TradeAndAngleSynced, TradingAngleRange, CloseByMomentum, TradeByRateDirection, SupportDate, ResistanceDate, 
                         GannAnglesOffset, GannAngles, IsGannAnglesManual, GannAnglesAnchorDate, SpreadShortToLongTreshold, SupportPriceStore, ResistancePriceStore, SuppResLevelsCount, DoStreatchRates, IsSuppResManual, 
                         TradeOnCrossOnly, TakeProfitFunctionInt, DoAdjustTimeframeByAllowedLot, IsColdOnTrades, CorridorCrossesCountMinimum, StDevToSpreadRatio, LoadRatesSecondsWarning, CorridorHighLowMethodInt, 
                         CorridorStDevRatioMax, CorridorLengthMinimum, CorridorCrossHighLowMethodInt, MovingAverageTypeInt, PriceCmaLevels, VolumeTresholdIterations, StDevTresholdIterations, StDevAverageLeewayRatio, 
                         ExtreamCloseOffset, CurrentLossInPipsCloseAdjustment, CorridorBigToSmallRatio, ResetOnBalance, VoltageFunction
FROM            TradingMacro
WHERE        (Pair = @Pair) AND (TradingMacroName = @NameSource)
ORDER BY PairIndex,TradingGroup

IF @@ROWCOUNT = 0
THROW 50000, 'No records were copied',1
