﻿INSERT INTO TradingMacro
SELECT     Pair,
TradingRatio,
NewId(),
LimitBar,
CurrentLoss,
ReverseOnProfit,
FreezLimit,
CorridorMethod,
FreezeStop,
FibMax,
FibMin,
CorridornessMin,
CorridorIterationsIn,
CorridorIterationsOut,
CorridorIterations,
CorridorBarMinutes,
PairIndex,
TradingGroup,
MaximumPositions,
IsActive,
'RM 05',
LimitCorridorByBarHeight,
MaxLotByTakeProfitRatio,
BarPeriodsLow,
BarPeriodsHigh,
StrictTradeClose,
BarPeriodsLowHighRatio,
LongMAPeriod,
CorridorAverageDaysBack,
CorridorPeriodsStart,
CorridorPeriodsLength,
CorridorStartDate,
CorridorRatioForRange,
CorridorRatioForBreakout,
RangeRatioForTradeLimit,
TradeByAngle,
ProfitToLossExitRatio,
TradeByFirstWave,
PowerRowOffset,
RangeRatioForTradeStop,
ReversePower,
CorrelationTreshold,
CloseOnProfitOnly,
CloseOnProfit,
CloseOnOpen,
StreachTradingDistance,
CloseAllOnProfit,
ReverseStrategy,
TradeAndAngleSynced,
TradingAngleRange,
CloseByMomentum,
TradeByRateDirection,
SupportDate,
ResistanceDate,
GannAnglesOffset,
GannAngles,
IsGannAnglesManual,
GannAnglesAnchorDate,
SpreadShortToLongTreshold,
SupportPriceStore,
ResistancePriceStore,
SuppResLevelsCount,
DoStreatchRates,
IsSuppResManual,
TradeOnCrossOnly,
TakeProfitFunctionInt,
DoAdjustTimeframeByAllowedLot,
IsColdOnTrades,
CorridorCrossesCountMinimum,
StDevToSpreadRatio,
LoadRatesSecondsWarning,
CorridorHighLowMethodInt,
CorridorStDevRatioMax,
CorridorLengthMinimum,
CorridorCrossHighLowMethodInt
FROM         TradingMacro
WHERE     (TradingMacroName = N'RM 03')AND IsActive = 1