﻿using HedgeHog.NewsCaster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using HtmlAgilityPack;
using System.Collections.Generic;
using MongoDB.Driver;
using AutoMapper;
using HedgeHog.Alice.Store;
using MongoDB.Bson;

namespace UnitLib {
  [TestClass()]
  public class MongoDBTest {

    #region Additional test attributes
    // 
    //You can use the following additional attributes as you write your tests:
    //
    //Use ClassInitialize to run code before running the first test in the class
    //[ClassInitialize()]
    //public static void MyClassInitialize(TestContext testContext)
    //{
    //}
    //
    //Use ClassCleanup to run code after all tests in a class have run
    //[ClassCleanup()]
    //public static void MyClassCleanup()
    //{
    //}
    //
    //Use TestInitialize to run code before running each test
    //[TestInitialize()]
    //public void MyTestInitialize()
    //{
    //}
    //
    //Use TestCleanup to run code after each test has run
    //[TestCleanup()]
    //public void MyTestCleanup()
    //{
    //}
    //
    #endregion


    public class TradingMacroSettings {
      public ObjectId _id { get; set; }
      public int BarsCount { get; set; }
      public int BarsCountMax { get; set; }
      public float BbRatio { get; set; }
      public float CanTradeLocalRatio { get; set; }
      public int CmaPasses { get; set; }
      public int CmaPassesMin { get; set; }
      public float CmaRatioForWaveLength { get; set; }
      public float CorridorDistanceRatio { get; set; }
      public float CorridorSDRatio { get; set; }
      public float EquinoxPerc { get; set; }
      public bool IsContinuousTrading { get; set; }
      public float RatesDistanceMin { get; set; }
      public float RatesHeightMin { get; set; }
      public string RatesMinutesMin { get; set; }
      public int RatesStDevMinInPips { get; set; }
      public float TakeProfitLimitRatio { get; set; }
      public float TipRatio { get; set; }
      public int TradeCountMax { get; set; }
      public int TradeCountStart { get; set; }
      public float TradingAngleRange_ { get; set; }
      public string TradingHoursRange { get; set; }
      public string TradingPriceRange { get; set; }
      public string TrendAngleBlue { get; set; }
      public float TrendAngleGreen { get; set; }
      public string TrendAngleLast { get; set; }
      public float TrendAngleLime { get; set; }
      public float TrendAnglePlum { get; set; }
      public string TrendAnglePrev { get; set; }
      public string TrendAnglePrev2 { get; set; }
      public float TrendAngleRed { get; set; }
      public float TrendHeightPerc { get; set; }
      public int VoltAverageIterations { get; set; }
      public float VoltAvgRange { get; set; }
      public string VoltRange { get; set; }
      public string VoltRange_2 { get; set; }
      public float WaveStDevPower { get; set; }
      public float WaveStDevPowerS { get; set; }
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
      public int OptionsDaysGap	 { get; set; }
      public string Outsiders { get; set; }
      public string RatesLengthBy { get; set; }
      public int RiskRewardThresh { get; set; }
      public string ScanCorridorBy { get; set; }
      public string TakeProfitFunction { get; set; }
      public float TakeProfitXRatio { get; set; }
      public string TimeFrameTreshold { get; set; }
      public string TradeConditionsSave { get; set; }
      public string TradeDirectionTriggerssSave { get; set; }
      public string TradeEnterBy { get; set; }
      public string TradeExitBy { get; set; }
      public string TradeOpenActionsSave { get; set; }
      public string TradeTrends { get; set; }
      public string TradingDaysRange { get; set; }
      public string TradingDistanceFunction { get; set; }
      public float TradingDistanceX { get; set; }
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
      public float CorrelationMinimum { get; set; }
      public string CorridorCrossHighLowMethod { get; set; }
      public float CorridorLengthMinimum_ { get; set; }
      public bool DoShowTradeOnChart { get; set; }
      public bool FitRatesToPlotter { get; set; }
      public string GreenRedBlue { get; set; }
      public int GroupRatesCount { get; set; }
      public bool IsAutoSync { get; set; }
      public float IteratorLastRatioForCorridor { get; set; }
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
      public float TestMinimumBalancePerc { get; set; }
      public string TestSuperSessionUid_ { get; set; }
      public bool TestUseSuperSession { get; set; }
      public int BigWaveIndex { get; set; }
      public float CurrentLoss_ { get; set; }
      public bool DoNews { get; set; }
      public int EllasticRange { get; set; }
      public string ExitFunction { get; set; }
      public bool IsTrader { get; set; }
      public float LastTradeLoss { get; set; }
      public bool StreatchTakeProfit { get; set; }
      public string TurnOffFunction { get; set; }
      public bool TurnOffOnProfit { get; set; }
      public bool CloseOnOpen_ { get; set; }
      public float CorridorHeightMax { get; set; }
      public int DistanceDaysBack { get; set; }
      public bool DoLogSaveRates { get; set; }
      public bool DoStreatchRates_ { get; set; }
      public object ForceOpenTrade { get; set; }
      public bool IsGannAnglesManual_ { get; set; }
      public bool IsSuppResManual_ { get; set; }
      public int LoadRatesSecondsWarning_ { get; set; }
      public float RsdTreshold { get; set; }
      public float StreatchRatesMaxRatio { get; set; }
      public float VoltsBelowAboveLengthMin { get; set; }
      public int VoltsFrameLength { get; set; }
      public int VoltsHighIterations { get; set; }
      public float WaveStDevRatio { get; set; }
      public bool CloseByMomentum_ { get; set; }
      public bool CloseOnProfitOnly_ { get; set; }
      public float CorridorBigToSmallRatio_ { get; set; }
      public int ExtreamCloseOffset_ { get; set; }
      public float ResetOnBalance_ { get; set; }
      public bool TradeByRateDirection_ { get; set; }
      public bool TradeOnBOW { get; set; }
      public int BarPeriodsHigh { get; set; }
      public int BarPeriodsLow { get; set; }
      public float BarPeriodsLowHighRatio { get; set; }
      public bool CloseAllOnProfit { get; set; }
      public bool CloseByMomentum { get; set; }
      public bool CloseOnOpen { get; set; }
      public bool CloseOnProfitOnly { get; set; }
      public float CorrelationTreshold { get; set; }
      public int CorridorAverageDaysBack { get; set; }
      public float CorridorBigToSmallRatio { get; set; }
      public int CorridorCrossesCountMinimum { get; set; }
      public int CorridorCrossHighLowMethodInt { get; set; }
      public int CorridorHighLowMethodInt { get; set; }
      public string CorridorIterations { get; set; }
      public int CorridorIterationsIn { get; set; }
      public int CorridorIterationsOut { get; set; }
      public float CorridorLengthMinimum { get; set; }
      public float CorridornessMin { get; set; }
      public int CorridorPeriodsLength { get; set; }
      public int CorridorPeriodsStart { get; set; }
      public float CorridorRatioForRange { get; set; }
      public float CorridorStDevRatioMax { get; set; }
      public float CurrentLossInPipsCloseAdjustment { get; set; }
      public bool DoAdjustTimeframeByAllowedLot { get; set; }
      public bool DoStreatchRates { get; set; }
      public int ExtreamCloseOffset { get; set; }
      public string FibMax { get; set; }
      public float FibMin { get; set; }
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
      public int PowerRowOffset { get; set; }
      public float PriceCmaLevels { get; set; }
      public float ProfitToLossExitRatio { get; set; }
      public float RangeRatioForTradeLimit { get; set; }
      public float RangeRatioForTradeStop { get; set; }
      public float ResetOnBalance { get; set; }
      public object ResistanceDate { get; set; }
      public object ResistancePriceStore { get; set; }
      public bool ReverseOnProfit { get; set; }
      public bool ReversePower { get; set; }
      public float SpreadShortToLongTreshold { get; set; }
      public float StDevAverageLeewayRatio { get; set; }
      public float StDevToSpreadRatio { get; set; }
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
      public float TradingAngleRange { get; set; }
      public int TradingGroup { get; set; }
      public string TradingMacroName { get; set; }
      public float TradingRatio { get; set; }
      public int VolumeTresholdIterations { get; set; }
    }

    static IMapper tradingMacroMapper = new MapperConfiguration(cfg => cfg.CreateMap<TradingMacro, TradingMacroSettings>()).CreateMapper();

    [TestMethod()]
    public void MongoTest() {
      var client = new MongoClient("mongodb://dimok:1Aaaaaaa@ds040017.mlab.com:40017/forex");
      var db = client.GetDatabase("forex");
      var colls = db.ListCollections().ToList();
      colls.ForEach(bd => Console.WriteLine(bd + ""));
      //db.CreateCollection("test");
      var testCollection = db.GetCollection<TradingMacroSettings>("test");
      testCollection.AsQueryable().ForEach(tm=> Console.WriteLine(tm.ToJson()));
      //testCollection.InsertOne(tradingMacroMapper.Map<TradingMacroSettings>(new HedgeHog.Alice.Store.TradingMacro()));
      Assert.Inconclusive("Verify the correctness of this test method.");
    }
  }
}
