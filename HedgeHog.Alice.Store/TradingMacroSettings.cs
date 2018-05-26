﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public class TradingMacroSettings {
    [System.ComponentModel.DataAnnotations.Key]
    public global::MongoDB.Bson.ObjectId _id { get; set; }
    public int BarsCount { get; set; }
    public int BarsCountMax { get; set; }
    public double BbRatio { get; set; }
    public double CanTradeLocalRatio { get; set; }
    public int CmaPasses { get; set; }
    public int CmaPassesMin { get; set; }
    public double CmaRatioForWaveLength { get; set; }
    public double CorridorDistanceRatio { get; set; }
    public double CorridorSDRatio { get; set; }
    public double EquinoxPerc { get; set; }
    public bool IsContinuousTrading { get; set; }
    public double RatesDistanceMin { get; set; }
    public double RatesHeightMin { get; set; }
    public string RatesMinutesMin { get; set; }
    public int RatesStDevMinInPips { get; set; }
    public double TakeProfitLimitRatio { get; set; }
    public double TipRatio { get; set; }
    public int TradeCountMax { get; set; }
    public int TradeCountStart { get; set; }
    public double TradingAngleRange_ { get; set; }
    public string TradingHoursRange { get; set; }
    public string TradingPriceRange { get; set; }
    public string TrendAngleBlue { get; set; }
    public double TrendAngleGreen { get; set; }
    public string TrendAngleLast { get; set; }
    public double TrendAngleLime { get; set; }
    public double TrendAnglePlum { get; set; }
    public string TrendAnglePrev { get; set; }
    public string TrendAnglePrev2 { get; set; }
    public double TrendAngleRed { get; set; }
    public double TrendHeightPerc { get; set; }
    public int VoltAverageIterations { get; set; }
    public int VoltAverageIterations2 { get; set; }
    public double VoltAvgRange { get; set; }
    public string VoltRange { get; set; }
    public string VoltRange_2 { get; set; }
    public double VoltCmaPeriod { get; set; }
    public int VoltCmaPasses { get; set; }
    public double WaveStDevPower { get; set; }
    public double WaveStDevPowerS { get; set; }
    public string BarPeriod { get; set; }
    public string BarPeriodCalc { get; set; }
    public string CorridorByStDevRatioFunc { get; set; }
    public string CorridorByStDevRatioFunc2 { get; set; }
    public string CorridorCalcMethod { get; set; }
    public string CorridorHighLowMethod { get; set; }
    public string EquinoxCorridors { get; set; }
    public string LevelBuyBy { get; set; }
    public string LevelBuyCloseBy { get; set; }
    public string LevelSellBy { get; set; }
    public string LevelSellCloseBy { get; set; }
    public string MovingAverageType { get; set; }
    public string Outsiders { get; set; }
    public string RatesLengthBy { get; set; }
    public int RiskRewardThresh { get; set; }
    public string ScanCorridorBy { get; set; }
    public string TakeProfitFunction { get; set; }
    public double TakeProfitXRatio { get; set; }
    public string TimeFrameTreshold { get; set; }
    public string TradeConditionsSave { get; set; }
    public string TradeDirection { get; set; }
    public string TradeDirectionTriggerssSave { get; set; }
    public string TradeEnterBy { get; set; }
    public string TradeExitBy { get; set; }
    public string TradeOpenActionsSave { get; set; }
    public string TradeTrends { get; set; }
    public string TradingDaysRange { get; set; }
    public string TradingDistanceFunction { get; set; }
    public double TradingDistanceX { get; set; }
    public string TrailingDistanceFunction { get; set; }
    public string TrendBlue { get; set; }
    public string TrendGreen { get; set; }
    public string TrendLime { get; set; }
    public string TrendPlum { get; set; }
    public string TrendRed { get; set; }
    public int TrendsAll { get; set; }
    public string VoltageFunction { get; set; }
    public string VoltageFunction2 { get; set; }
    public string WaveSmoothBy { get; set; }
    public int WavesRsdPerc { get; set; }
    public bool CanDoNetLimitOrders { get; set; }
    public bool CanDoNetStopOrders { get; set; }
    public bool CanTradeAlwaysOn { get; set; }
    public bool CloseAfterTradingHours { get; set; }
    public bool CloseTradesBeforeNews { get; set; }
    public bool DoAdjustExitLevelByTradeTime { get; set; }
    public bool DoCorrDistByDist { get; set; }
    public bool ExitByBuySellLevel { get; set; }
    public bool IsCorridorForwardOnly { get; set; }
    public bool IsTakeBack { get; set; }
    public bool IsTrender { get; set; }
    public bool IsTurnOnOnly { get; set; }
    public bool HedgedTrading { get; set; }
    public bool LimitProfitByRatesHeight { get; set; }
    public bool TradingRatioByPMC { get; set; }
    public bool UseFlatTrends { get; set; }
    public bool UseLastLoss { get; set; }
    public bool UsePrevHeight { get; set; }
    public bool UseVoltage { get; set; }
    public bool CanShowNews { get; set; }
    public string ChartHighMethod { get; set; }
    public string ChartLowMethod { get; set; }
    public bool ClearCOMs { get; set; }
    public double CorrelationMinimum { get; set; }
    public string CorridorCrossHighLowMethod { get; set; }
    public double CorridorLengthMinimum_ { get; set; }
    public bool DoShowTradeOnChart { get; set; }
    public bool FitRatesToPlotter { get; set; }
    public string GreenRedBlue { get; set; }
    public int GroupRatesCount { get; set; }
    public bool IsAutoSync { get; set; }
    public double IteratorLastRatioForCorridor { get; set; }
    public int PriceFftLevelsFast { get; set; }
    public int PriceFftLevelsSlow { get; set; }
    public bool ResetTradeStrip { get; set; }
    public bool ShowParabola { get; set; }
    public int SuppResLevelsCount_ { get; set; }
    public bool SyncAll { get; set; }
    public int VoltsAvgIterations { get; set; }
    public string TestBarsCount { get; set; }
    public string TestCorrelationMinimum { get; set; }
    public string TestCorridorDistanceRatio { get; set; }
    public string TestDistanceIterations { get; set; }
    public string TestPriceCmaLevels { get; set; }
    public string TestProfitToLossExitRatio { get; set; }
    public string TestRatesHeightMinimum { get; set; }
    public string TestWaveStDevRatio { get; set; }
    public double TestMinimumBalancePerc { get; set; }
    public string TestSuperSessionUid_ { get; set; }
    public bool TestUseSuperSession { get; set; }
    public int BigWaveIndex { get; set; }
    public double CurrentLoss_ { get; set; }
    public bool DoNews { get; set; }
    public int EllasticRange { get; set; }
    public string ExitFunction { get; set; }
    public bool IsTrader { get; set; }
    public double LastTradeLoss { get; set; }
    public bool StreatchTakeProfit { get; set; }
    public string TurnOffFunction { get; set; }
    public bool TurnOffOnProfit { get; set; }
    public bool CloseOnOpen_ { get; set; }
    public double CorridorHeightMax { get; set; }
    public int DistanceDaysBack { get; set; }
    public bool DoLogSaveRates { get; set; }
    public bool DoStreatchRates_ { get; set; }
    public object ForceOpenTrade { get; set; }
    public bool IsGannAnglesManual_ { get; set; }
    public bool IsSuppResManual_ { get; set; }
    public int LoadRatesSecondsWarning_ { get; set; }
    public double RsdTreshold { get; set; }
    public double StreatchRatesMaxRatio { get; set; }
    public double VoltsBelowAboveLengthMin { get; set; }
    public int VoltsFrameLength { get; set; }
    public int VoltsHighIterations { get; set; }
    public double WaveStDevRatio { get; set; }
    public bool CloseByMomentum_ { get; set; }
    public bool CloseOnProfitOnly_ { get; set; }
    public double CorridorBigToSmallRatio_ { get; set; }
    public int ExtreamCloseOffset_ { get; set; }
    public double ResetOnBalance_ { get; set; }
    public bool TradeByRateDirection_ { get; set; }
    public bool TradeOnBOW { get; set; }
    public int BarPeriodsHigh { get; set; }
    public int BarPeriodsLow { get; set; }
    public double BarPeriodsLowHighRatio { get; set; }
    public bool CloseAllOnProfit { get; set; }
    public bool CloseByMomentum { get; set; }
    public bool CloseOnOpen { get; set; }
    public bool CloseOnProfitOnly { get; set; }
    public double CorrelationTreshold { get; set; }
    public int CorridorAverageDaysBack { get; set; }
    public double CorridorBigToSmallRatio { get; set; }
    public int CorridorCrossesCountMinimum { get; set; }
    public int CorridorCrossHighLowMethodInt { get; set; }
    public int CorridorHighLowMethodInt { get; set; }
    public string CorridorIterations { get; set; }
    public int CorridorIterationsIn { get; set; }
    public int CorridorIterationsOut { get; set; }
    public double CorridorLengthMinimum { get; set; }
    public double CorridornessMin { get; set; }
    public int CorridorPeriodsLength { get; set; }
    public int CorridorPeriodsStart { get; set; }
    public double CorridorRatioForRange { get; set; }
    public double CorridorStDevRatioMax { get; set; }
    public double CurrentLossInPipsCloseAdjustment { get; set; }
    public bool DoAdjustTimeframeByAllowedLot { get; set; }
    public bool DoStreatchRates { get; set; }
    public int ExtreamCloseOffset { get; set; }
    public string FibMax { get; set; }
    public double FibMin { get; set; }
    public int FreezeStop { get; set; }
    public int FreezLimit { get; set; }
    public bool IsActive { get; set; }
    public bool IsColdOnTrades { get; set; }
    public bool IsSuppResManual { get; set; }
    public int LimitBar { get; set; }
    public bool LimitCorridorByBarHeight { get; set; }
    public int LoadRatesSecondsWarning { get; set; }
    public int LongMAPeriod { get; set; }
    public int MovingAverageTypeInt { get; set; }
    public string Pair { get; set; }
    public int PairIndex { get; set; }
    public string PairHedge { get; set; }
    public int HedgeCorrelation { get; set; }
    public int PowerRowOffset { get; set; }
    public double PriceCmaLevels { get; set; }
    public double ProfitToLossExitRatio { get; set; }
    public double RangeRatioForTradeLimit { get; set; }
    public double RangeRatioForTradeStop { get; set; }
    public double ResetOnBalance { get; set; }
    public object ResistanceDate { get; set; }
    public object ResistancePriceStore { get; set; }
    public bool ReverseOnProfit { get; set; }
    public bool ReversePower { get; set; }
    public double SpreadShortToLongTreshold { get; set; }
    public double StDevAverageLeewayRatio { get; set; }
    public double StDevToSpreadRatio { get; set; }
    public int StDevTresholdIterations { get; set; }
    public bool StreachTradingDistance { get; set; }
    public bool StrictTradeClose { get; set; }
    public object SupportDate { get; set; }
    public object SupportPriceStore { get; set; }
    public int SuppResLevelsCount { get; set; }
    public int TakeProfitFunctionInt { get; set; }
    public bool TradeAndAngleSynced { get; set; }
    public bool TradeByAngle { get; set; }
    public bool TradeByFirstWave { get; set; }
    public bool TradeByRateDirection { get; set; }
    public bool TradeOnCrossOnly { get; set; }
    public double TradingAngleRange { get; set; }
    public int TradingGroup { get; set; }
    public string TradingMacroName { get; set; }
    public double TradingRatio { get; set; }
    public int VolumeTresholdIterations { get; set; }
    public string TradeConditionEval { get; set; }
    public int OptionsDaysGap { get; set; }
  }
}
