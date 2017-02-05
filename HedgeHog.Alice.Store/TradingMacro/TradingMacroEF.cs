using System;
using System.Data.Entity.Core.EntityClient;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Core.Objects.DataClasses;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace HedgeHog.Alice.Store {
  //[JsonObject(MemberSerialization.OptOut)]
  public partial class TradingMacro : EntityObject {
    #region Factory Method

    /// <summary>
    /// Create a new TradingMacro object.
    /// </summary>
    /// <param name="pair">Initial value of the Pair property.</param>
    /// <param name="tradingRatio">Initial value of the TradingRatio property.</param>
    /// <param name="uID">Initial value of the UID property.</param>
    /// <param name="limitBar">Initial value of the LimitBar property.</param>
    /// <param name="currentLoss">Initial value of the CurrentLoss property.</param>
    /// <param name="reverseOnProfit">Initial value of the ReverseOnProfit property.</param>
    /// <param name="freezLimit">Initial value of the FreezLimit property.</param>
    /// <param name="corridorMethod">Initial value of the CorridorMethod property.</param>
    /// <param name="freezeStop">Initial value of the FreezeStop property.</param>
    /// <param name="fibMax">Initial value of the FibMax property.</param>
    /// <param name="fibMin">Initial value of the FibMin property.</param>
    /// <param name="corridornessMin">Initial value of the CorridornessMin property.</param>
    /// <param name="corridorIterationsIn">Initial value of the CorridorIterationsIn property.</param>
    /// <param name="corridorIterationsOut">Initial value of the CorridorIterationsOut property.</param>
    /// <param name="corridorIterations">Initial value of the CorridorIterations property.</param>
    /// <param name="corridorBarMinutes">Initial value of the CorridorBarMinutes property.</param>
    /// <param name="pairIndex">Initial value of the PairIndex property.</param>
    /// <param name="tradingGroup">Initial value of the TradingGroup property.</param>
    /// <param name="maximumPositions">Initial value of the MaximumPositions property.</param>
    /// <param name="isActive">Initial value of the IsActive property.</param>
    /// <param name="tradingMacroName">Initial value of the TradingMacroName property.</param>
    /// <param name="limitCorridorByBarHeight">Initial value of the LimitCorridorByBarHeight property.</param>
    /// <param name="maxLotByTakeProfitRatio">Initial value of the MaxLotByTakeProfitRatio property.</param>
    /// <param name="barPeriodsLow">Initial value of the BarPeriodsLow property.</param>
    /// <param name="barPeriodsHigh">Initial value of the BarPeriodsHigh property.</param>
    /// <param name="strictTradeClose">Initial value of the StrictTradeClose property.</param>
    /// <param name="barPeriodsLowHighRatio">Initial value of the BarPeriodsLowHighRatio property.</param>
    /// <param name="longMAPeriod">Initial value of the LongMAPeriod property.</param>
    /// <param name="corridorAverageDaysBack">Initial value of the CorridorAverageDaysBack property.</param>
    /// <param name="corridorPeriodsStart">Initial value of the CorridorPeriodsStart property.</param>
    /// <param name="corridorPeriodsLength">Initial value of the CorridorPeriodsLength property.</param>
    /// <param name="corridorRatioForRange">Initial value of the CorridorRatioForRange property.</param>
    /// <param name="corridorRatioForBreakout">Initial value of the CorridorRatioForBreakout property.</param>
    /// <param name="rangeRatioForTradeLimit">Initial value of the RangeRatioForTradeLimit property.</param>
    /// <param name="tradeByAngle">Initial value of the TradeByAngle property.</param>
    /// <param name="profitToLossExitRatio">Initial value of the ProfitToLossExitRatio property.</param>
    /// <param name="powerRowOffset">Initial value of the PowerRowOffset property.</param>
    /// <param name="rangeRatioForTradeStop">Initial value of the RangeRatioForTradeStop property.</param>
    /// <param name="reversePower">Initial value of the ReversePower property.</param>
    /// <param name="correlationTreshold">Initial value of the CorrelationTreshold property.</param>
    /// <param name="closeOnProfitOnly">Initial value of the CloseOnProfitOnly property.</param>
    /// <param name="closeOnProfit">Initial value of the CloseOnProfit property.</param>
    /// <param name="closeOnOpen">Initial value of the CloseOnOpen property.</param>
    /// <param name="streachTradingDistance">Initial value of the StreachTradingDistance property.</param>
    /// <param name="closeAllOnProfit">Initial value of the CloseAllOnProfit property.</param>
    /// <param name="reverseStrategy">Initial value of the ReverseStrategy property.</param>
    /// <param name="tradeAndAngleSynced">Initial value of the TradeAndAngleSynced property.</param>
    /// <param name="tradingAngleRange">Initial value of the TradingAngleRange property.</param>
    /// <param name="closeByMomentum">Initial value of the CloseByMomentum property.</param>
    /// <param name="tradeByRateDirection">Initial value of the TradeByRateDirection property.</param>
    /// <param name="gannAngles">Initial value of the GannAngles property.</param>
    /// <param name="isGannAnglesManual">Initial value of the IsGannAnglesManual property.</param>
    /// <param name="spreadShortToLongTreshold">Initial value of the SpreadShortToLongTreshold property.</param>
    /// <param name="suppResLevelsCount">Initial value of the SuppResLevelsCount property.</param>
    /// <param name="doStreatchRates">Initial value of the DoStreatchRates property.</param>
    /// <param name="isSuppResManual">Initial value of the IsSuppResManual property.</param>
    /// <param name="tradeOnCrossOnly">Initial value of the TradeOnCrossOnly property.</param>
    /// <param name="takeProfitFunctionInt">Initial value of the TakeProfitFunctionInt property.</param>
    /// <param name="doAdjustTimeframeByAllowedLot">Initial value of the DoAdjustTimeframeByAllowedLot property.</param>
    /// <param name="isColdOnTrades">Initial value of the IsColdOnTrades property.</param>
    /// <param name="corridorCrossesCountMinimum">Initial value of the CorridorCrossesCountMinimum property.</param>
    /// <param name="stDevToSpreadRatio">Initial value of the StDevToSpreadRatio property.</param>
    /// <param name="loadRatesSecondsWarning">Initial value of the LoadRatesSecondsWarning property.</param>
    /// <param name="corridorHighLowMethodInt">Initial value of the CorridorHighLowMethodInt property.</param>
    /// <param name="corridorStDevRatioMax">Initial value of the CorridorStDevRatioMax property.</param>
    /// <param name="corridorLengthMinimum">Initial value of the CorridorLengthMinimum property.</param>
    /// <param name="corridorCrossHighLowMethodInt">Initial value of the CorridorCrossHighLowMethodInt property.</param>
    /// <param name="priceCmaLevels">Initial value of the PriceCmaLevels property.</param>
    /// <param name="volumeTresholdIterations">Initial value of the VolumeTresholdIterations property.</param>
    /// <param name="stDevTresholdIterations">Initial value of the StDevTresholdIterations property.</param>
    /// <param name="stDevAverageLeewayRatio">Initial value of the StDevAverageLeewayRatio property.</param>
    /// <param name="extreamCloseOffset">Initial value of the ExtreamCloseOffset property.</param>
    /// <param name="currentLossInPipsCloseAdjustment">Initial value of the CurrentLossInPipsCloseAdjustment property.</param>
    /// <param name="corridorBigToSmallRatio">Initial value of the CorridorBigToSmallRatio property.</param>
    /// <param name="voltageFunction">Initial value of the VoltageFunction property.</param>
    public static TradingMacro CreateTradingMacro(global::System.String pair, global::System.Double tradingRatio, global::System.Guid uID, global::System.Int32 limitBar, global::System.Double currentLoss, global::System.Boolean reverseOnProfit, global::System.Int32 freezLimit, global::System.Int32 freezeStop, global::System.String fibMax, global::System.Double fibMin, global::System.Double corridornessMin, global::System.Int32 corridorIterationsIn, global::System.Int32 corridorIterationsOut, global::System.String corridorIterations, global::System.Int32 corridorBarMinutes, global::System.Int32 pairIndex, global::System.Int32 tradingGroup, global::System.Int32 maximumPositions, global::System.Boolean isActive, global::System.String tradingMacroName, global::System.Boolean limitCorridorByBarHeight, global::System.Double maxLotByTakeProfitRatio, global::System.Int32 barPeriodsLow, global::System.Int32 barPeriodsHigh, global::System.Boolean strictTradeClose, global::System.Double barPeriodsLowHighRatio, global::System.Int32 longMAPeriod, global::System.Int32 corridorAverageDaysBack, global::System.Int32 corridorPeriodsStart, global::System.Int32 corridorPeriodsLength, global::System.Double corridorRatioForRange, global::System.Double corridorRatioForBreakout, global::System.Double rangeRatioForTradeLimit, global::System.Boolean tradeByAngle, global::System.Double profitToLossExitRatio, global::System.Int32 powerRowOffset, global::System.Double rangeRatioForTradeStop, global::System.Boolean reversePower, global::System.Double correlationTreshold, global::System.Boolean closeOnProfitOnly, global::System.Boolean closeOnOpen, global::System.Boolean streachTradingDistance, global::System.Boolean closeAllOnProfit, global::System.Boolean tradeAndAngleSynced, global::System.Double tradingAngleRange, global::System.Boolean closeByMomentum, global::System.Boolean tradeByRateDirection, global::System.String gannAngles, global::System.Boolean isGannAnglesManual, global::System.Double spreadShortToLongTreshold, global::System.Int32 suppResLevelsCount, global::System.Boolean doStreatchRates, global::System.Boolean isSuppResManual, global::System.Boolean tradeOnCrossOnly, global::System.Int32 takeProfitFunctionInt, global::System.Boolean doAdjustTimeframeByAllowedLot, global::System.Boolean isColdOnTrades, global::System.Int32 corridorCrossesCountMinimum, global::System.Double stDevToSpreadRatio, global::System.Int32 loadRatesSecondsWarning, global::System.Int32 corridorHighLowMethodInt, global::System.Double corridorStDevRatioMax, global::System.Double corridorLengthMinimum, global::System.Int32 corridorCrossHighLowMethodInt, global::System.Double priceCmaLevels, global::System.Int32 volumeTresholdIterations, global::System.Int32 stDevTresholdIterations, global::System.Double stDevAverageLeewayRatio, global::System.Int32 extreamCloseOffset, global::System.Double currentLossInPipsCloseAdjustment, global::System.Double corridorBigToSmallRatio, VoltageFunction voltageFunction) {
      TradingMacro tradingMacro = new TradingMacro();
      tradingMacro.Pair = pair;
      tradingMacro.TradingRatio = tradingRatio;
      tradingMacro.UID = uID;
      tradingMacro.LimitBar = limitBar;
      tradingMacro.CurrentLoss = currentLoss;
      tradingMacro.ReverseOnProfit = reverseOnProfit;
      tradingMacro.FreezLimit = freezLimit;
      tradingMacro.FreezeStop = freezeStop;
      tradingMacro.FibMax = fibMax;
      tradingMacro.FibMin = fibMin;
      tradingMacro.CorridornessMin = corridornessMin;
      tradingMacro.CorridorIterationsIn = corridorIterationsIn;
      tradingMacro.CorridorIterationsOut = corridorIterationsOut;
      tradingMacro.CorridorIterations = corridorIterations;
      tradingMacro.CorridorBarMinutes = corridorBarMinutes;
      tradingMacro.PairIndex = pairIndex;
      tradingMacro.TradingGroup = tradingGroup;
      tradingMacro.MaximumPositions = maximumPositions;
      tradingMacro.IsActive = isActive;
      tradingMacro.TradingMacroName = tradingMacroName;
      tradingMacro.LimitCorridorByBarHeight = limitCorridorByBarHeight;
      tradingMacro.MaxLotByTakeProfitRatio = maxLotByTakeProfitRatio;
      tradingMacro.BarPeriodsLow = barPeriodsLow;
      tradingMacro.BarPeriodsHigh = barPeriodsHigh;
      tradingMacro.StrictTradeClose = strictTradeClose;
      tradingMacro.BarPeriodsLowHighRatio = barPeriodsLowHighRatio;
      tradingMacro.LongMAPeriod = longMAPeriod;
      tradingMacro.CorridorAverageDaysBack = corridorAverageDaysBack;
      tradingMacro.CorridorPeriodsStart = corridorPeriodsStart;
      tradingMacro.CorridorPeriodsLength = corridorPeriodsLength;
      tradingMacro.CorridorRatioForRange = corridorRatioForRange;
      tradingMacro.CorridorRatioForBreakout = corridorRatioForBreakout;
      tradingMacro.RangeRatioForTradeLimit = rangeRatioForTradeLimit;
      tradingMacro.TradeByAngle = tradeByAngle;
      tradingMacro.ProfitToLossExitRatio = profitToLossExitRatio;
      tradingMacro.PowerRowOffset = powerRowOffset;
      tradingMacro.RangeRatioForTradeStop = rangeRatioForTradeStop;
      tradingMacro.ReversePower = reversePower;
      tradingMacro.CorrelationTreshold = correlationTreshold;
      tradingMacro.CloseOnProfitOnly = closeOnProfitOnly;
      tradingMacro.CloseOnOpen = closeOnOpen;
      tradingMacro.StreachTradingDistance = streachTradingDistance;
      tradingMacro.CloseAllOnProfit = closeAllOnProfit;
      tradingMacro.TradeAndAngleSynced = tradeAndAngleSynced;
      tradingMacro.TradingAngleRange = tradingAngleRange;
      tradingMacro.CloseByMomentum = closeByMomentum;
      tradingMacro.TradeByRateDirection = tradeByRateDirection;
      tradingMacro.GannAngles = gannAngles;
      tradingMacro.IsGannAnglesManual = isGannAnglesManual;
      tradingMacro.SpreadShortToLongTreshold = spreadShortToLongTreshold;
      tradingMacro.SuppResLevelsCount = suppResLevelsCount;
      tradingMacro.DoStreatchRates = doStreatchRates;
      tradingMacro.IsSuppResManual = isSuppResManual;
      tradingMacro.TradeOnCrossOnly = tradeOnCrossOnly;
      tradingMacro.TakeProfitFunctionInt = takeProfitFunctionInt;
      tradingMacro.DoAdjustTimeframeByAllowedLot = doAdjustTimeframeByAllowedLot;
      tradingMacro.IsColdOnTrades = isColdOnTrades;
      tradingMacro.CorridorCrossesCountMinimum = corridorCrossesCountMinimum;
      tradingMacro.StDevToSpreadRatio = stDevToSpreadRatio;
      tradingMacro.LoadRatesSecondsWarning = loadRatesSecondsWarning;
      tradingMacro.CorridorHighLowMethodInt = corridorHighLowMethodInt;
      tradingMacro.CorridorStDevRatioMax = corridorStDevRatioMax;
      tradingMacro.CorridorLengthMinimum = corridorLengthMinimum;
      tradingMacro.CorridorCrossHighLowMethodInt = corridorCrossHighLowMethodInt;
      tradingMacro.PriceCmaLevels = priceCmaLevels;
      tradingMacro.VolumeTresholdIterations = volumeTresholdIterations;
      tradingMacro.StDevTresholdIterations = stDevTresholdIterations;
      tradingMacro.StDevAverageLeewayRatio = stDevAverageLeewayRatio;
      tradingMacro.ExtreamCloseOffset = extreamCloseOffset;
      tradingMacro.CurrentLossInPipsCloseAdjustment = currentLossInPipsCloseAdjustment;
      tradingMacro.CorridorBigToSmallRatio = corridorBigToSmallRatio;
      return tradingMacro;
    }

    #endregion

    #region Simple Properties

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.String Pair {
      get {
        return _Pair;
      }
      set {
        OnPairChanging(value);
        ReportPropertyChanging("Pair");
        _Pair = StructuralObject.SetValidValue(value, false, "Pair");
        ReportPropertyChanged("Pair");
        OnPairChanged();
      }
    }
    private global::System.String _Pair;
    partial void OnPairChanging(global::System.String value);
    partial void OnPairChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double TradingRatio {
      get {
        return _TradingRatio;
      }
      set {
        OnTradingRatioChanging(value);
        ReportPropertyChanging("TradingRatio");
        _TradingRatio = StructuralObject.SetValidValue(value, "TradingRatio");
        ReportPropertyChanged("TradingRatio");
        OnTradingRatioChanged();
      }
    }
    private global::System.Double _TradingRatio;
    partial void OnTradingRatioChanging(global::System.Double value);
    partial void OnTradingRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = true, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Guid UID {
      get {
        return _UID;
      }
      set {
        if(_UID != value) {
          OnUIDChanging(value);
          ReportPropertyChanging("UID");
          _UID = StructuralObject.SetValidValue(value, "UID");
          ReportPropertyChanged("UID");
          OnUIDChanged();
        }
      }
    }
    private global::System.Guid _UID;
    partial void OnUIDChanging(global::System.Guid value);
    partial void OnUIDChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 LimitBar {
      get {
        return _LimitBar;
      }
      set {
        OnLimitBarChanging(value);
        ReportPropertyChanging("LimitBar");
        _LimitBar = StructuralObject.SetValidValue(value, "LimitBar");
        ReportPropertyChanged("LimitBar");
        OnLimitBarChanged();
      }
    }
    private global::System.Int32 _LimitBar;
    partial void OnLimitBarChanging(global::System.Int32 value);
    partial void OnLimitBarChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [JsonIgnore]
    [Dnr]
    public global::System.Double CurrentLoss {
      get {
        return _CurrentLoss;
      }
      set {
        OnCurrentLossChanging(value);
        ReportPropertyChanging("CurrentLoss");
        _CurrentLoss = StructuralObject.SetValidValue(value, "CurrentLoss");
        ReportPropertyChanged("CurrentLoss");
        OnCurrentLossChanged();
      }
    }
    private global::System.Double _CurrentLoss;
    partial void OnCurrentLossChanging(global::System.Double value);
    partial void OnCurrentLossChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean ReverseOnProfit {
      get {
        return _ReverseOnProfit;
      }
      set {
        OnReverseOnProfitChanging(value);
        ReportPropertyChanging("ReverseOnProfit");
        _ReverseOnProfit = StructuralObject.SetValidValue(value, "ReverseOnProfit");
        ReportPropertyChanged("ReverseOnProfit");
        OnReverseOnProfitChanged();
      }
    }
    private global::System.Boolean _ReverseOnProfit;
    partial void OnReverseOnProfitChanging(global::System.Boolean value);
    partial void OnReverseOnProfitChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 FreezLimit {
      get {
        return _FreezLimit;
      }
      set {
        OnFreezLimitChanging(value);
        ReportPropertyChanging("FreezLimit");
        _FreezLimit = StructuralObject.SetValidValue(value, "FreezLimit");
        ReportPropertyChanged("FreezLimit");
        OnFreezLimitChanged();
      }
    }
    private global::System.Int32 _FreezLimit;
    partial void OnFreezLimitChanging(global::System.Int32 value);
    partial void OnFreezLimitChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 FreezeStop {
      get {
        return _FreezeStop;
      }
      set {
        OnFreezeStopChanging(value);
        ReportPropertyChanging("FreezeStop");
        _FreezeStop = StructuralObject.SetValidValue(value, "FreezeStop");
        ReportPropertyChanged("FreezeStop");
        OnFreezeStopChanged();
      }
    }
    private global::System.Int32 _FreezeStop;
    partial void OnFreezeStopChanging(global::System.Int32 value);
    partial void OnFreezeStopChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.String FibMax {
      get {
        return _FibMax;
      }
      set {
        OnFibMaxChanging(value);
        ReportPropertyChanging("FibMax");
        _FibMax = StructuralObject.SetValidValue(value, false, "FibMax");
        ReportPropertyChanged("FibMax");
        OnFibMaxChanged();
      }
    }
    private global::System.String _FibMax;
    partial void OnFibMaxChanging(global::System.String value);
    partial void OnFibMaxChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double FibMin {
      get {
        return _FibMin;
      }
      set {
        OnFibMinChanging(value);
        ReportPropertyChanging("FibMin");
        _FibMin = StructuralObject.SetValidValue(value, "FibMin");
        ReportPropertyChanged("FibMin");
        OnFibMinChanged();
      }
    }
    private global::System.Double _FibMin;
    partial void OnFibMinChanging(global::System.Double value);
    partial void OnFibMinChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double CorridornessMin {
      get {
        return _CorridornessMin;
      }
      set {
        OnCorridornessMinChanging(value);
        ReportPropertyChanging("CorridornessMin");
        _CorridornessMin = StructuralObject.SetValidValue(value, "CorridornessMin");
        ReportPropertyChanged("CorridornessMin");
        OnCorridornessMinChanged();
      }
    }
    private global::System.Double _CorridornessMin;
    partial void OnCorridornessMinChanging(global::System.Double value);
    partial void OnCorridornessMinChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorIterationsIn {
      get {
        return _CorridorIterationsIn;
      }
      set {
        OnCorridorIterationsInChanging(value);
        ReportPropertyChanging("CorridorIterationsIn");
        _CorridorIterationsIn = StructuralObject.SetValidValue(value, "CorridorIterationsIn");
        ReportPropertyChanged("CorridorIterationsIn");
        OnCorridorIterationsInChanged();
      }
    }
    private global::System.Int32 _CorridorIterationsIn;
    partial void OnCorridorIterationsInChanging(global::System.Int32 value);
    partial void OnCorridorIterationsInChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorIterationsOut {
      get {
        return _CorridorIterationsOut;
      }
      set {
        OnCorridorIterationsOutChanging(value);
        ReportPropertyChanging("CorridorIterationsOut");
        _CorridorIterationsOut = StructuralObject.SetValidValue(value, "CorridorIterationsOut");
        ReportPropertyChanged("CorridorIterationsOut");
        OnCorridorIterationsOutChanged();
      }
    }
    private global::System.Int32 _CorridorIterationsOut;
    partial void OnCorridorIterationsOutChanging(global::System.Int32 value);
    partial void OnCorridorIterationsOutChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.String CorridorIterations {
      get {
        return _CorridorIterations;
      }
      set {
        OnCorridorIterationsChanging(value);
        ReportPropertyChanging("CorridorIterations");
        _CorridorIterations = StructuralObject.SetValidValue(value, false, "CorridorIterations");
        ReportPropertyChanged("CorridorIterations");
        OnCorridorIterationsChanged();
      }
    }
    private global::System.String _CorridorIterations;
    partial void OnCorridorIterationsChanging(global::System.String value);
    partial void OnCorridorIterationsChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    public global::System.Int32 CorridorBarMinutes {
      get {
        return _CorridorBarMinutes;
      }
      set {
        OnCorridorBarMinutesChanging(value);
        ReportPropertyChanging("CorridorBarMinutes");
        _CorridorBarMinutes = StructuralObject.SetValidValue(value, "CorridorBarMinutes");
        ReportPropertyChanged("CorridorBarMinutes");
        OnCorridorBarMinutesChanged();
      }
    }
    private global::System.Int32 _CorridorBarMinutes;
    partial void OnCorridorBarMinutesChanging(global::System.Int32 value);
    partial void OnCorridorBarMinutesChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    [IsNotStrategy]
    public global::System.Int32 PairIndex {
      get {
        return _PairIndex;
      }
      set {
        OnPairIndexChanging(value);
        ReportPropertyChanging("PairIndex");
        _PairIndex = StructuralObject.SetValidValue(value, "PairIndex");
        ReportPropertyChanged("PairIndex");
        OnPairIndexChanged();
      }
    }
    private global::System.Int32 _PairIndex;
    partial void OnPairIndexChanging(global::System.Int32 value);
    partial void OnPairIndexChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    [IsNotStrategy]
    public global::System.Int32 TradingGroup {
      get {
        return _TradingGroup;
      }
      set {
        OnTradingGroupChanging(value);
        ReportPropertyChanging("TradingGroup");
        _TradingGroup = StructuralObject.SetValidValue(value, "TradingGroup");
        ReportPropertyChanged("TradingGroup");
        OnTradingGroupChanged();
      }
    }
    private global::System.Int32 _TradingGroup;
    partial void OnTradingGroupChanging(global::System.Int32 value);
    partial void OnTradingGroupChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 MaximumPositions {
      get {
        return _MaximumPositions;
      }
      set {
        OnMaximumPositionsChanging(value);
        ReportPropertyChanging("MaximumPositions");
        _MaximumPositions = StructuralObject.SetValidValue(value, "MaximumPositions");
        ReportPropertyChanged("MaximumPositions");
        OnMaximumPositionsChanged();
      }
    }
    private global::System.Int32 _MaximumPositions;
    partial void OnMaximumPositionsChanging(global::System.Int32 value);
    partial void OnMaximumPositionsChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean IsActive {
      get {
        return _IsActive;
      }
      set {
        OnIsActiveChanging(value);
        ReportPropertyChanging("IsActive");
        _IsActive = StructuralObject.SetValidValue(value, "IsActive");
        ReportPropertyChanged("IsActive");
        OnIsActiveChanged();
      }
    }
    private global::System.Boolean _IsActive;
    partial void OnIsActiveChanging(global::System.Boolean value);
    partial void OnIsActiveChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    [IsNotStrategy]
    public global::System.String TradingMacroName {
      get {
        return _TradingMacroName;
      }
      set {
        OnTradingMacroNameChanging(value);
        ReportPropertyChanging("TradingMacroName");
        _TradingMacroName = StructuralObject.SetValidValue(value, false, "TradingMacroName");
        ReportPropertyChanged("TradingMacroName");
        OnTradingMacroNameChanged();
      }
    }
    private global::System.String _TradingMacroName;
    partial void OnTradingMacroNameChanging(global::System.String value);
    partial void OnTradingMacroNameChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean LimitCorridorByBarHeight {
      get {
        return _LimitCorridorByBarHeight;
      }
      set {
        OnLimitCorridorByBarHeightChanging(value);
        ReportPropertyChanging("LimitCorridorByBarHeight");
        _LimitCorridorByBarHeight = StructuralObject.SetValidValue(value, "LimitCorridorByBarHeight");
        ReportPropertyChanged("LimitCorridorByBarHeight");
        OnLimitCorridorByBarHeightChanged();
      }
    }
    private global::System.Boolean _LimitCorridorByBarHeight;
    partial void OnLimitCorridorByBarHeightChanging(global::System.Boolean value);
    partial void OnLimitCorridorByBarHeightChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double MaxLotByTakeProfitRatio {
      get {
        return _MaxLotByTakeProfitRatio;
      }
      set {
        OnMaxLotByTakeProfitRatioChanging(value);
        ReportPropertyChanging("MaxLotByTakeProfitRatio");
        _MaxLotByTakeProfitRatio = StructuralObject.SetValidValue(value, "MaxLotByTakeProfitRatio");
        ReportPropertyChanged("MaxLotByTakeProfitRatio");
        OnMaxLotByTakeProfitRatioChanged();
      }
    }
    private global::System.Double _MaxLotByTakeProfitRatio;
    partial void OnMaxLotByTakeProfitRatioChanging(global::System.Double value);
    partial void OnMaxLotByTakeProfitRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 BarPeriodsLow {
      get {
        return _BarPeriodsLow;
      }
      set {
        OnBarPeriodsLowChanging(value);
        ReportPropertyChanging("BarPeriodsLow");
        _BarPeriodsLow = StructuralObject.SetValidValue(value, "BarPeriodsLow");
        ReportPropertyChanged("BarPeriodsLow");
        OnBarPeriodsLowChanged();
      }
    }
    private global::System.Int32 _BarPeriodsLow;
    partial void OnBarPeriodsLowChanging(global::System.Int32 value);
    partial void OnBarPeriodsLowChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 BarPeriodsHigh {
      get {
        return _BarPeriodsHigh;
      }
      set {
        OnBarPeriodsHighChanging(value);
        ReportPropertyChanging("BarPeriodsHigh");
        _BarPeriodsHigh = StructuralObject.SetValidValue(value, "BarPeriodsHigh");
        ReportPropertyChanged("BarPeriodsHigh");
        OnBarPeriodsHighChanged();
      }
    }
    private global::System.Int32 _BarPeriodsHigh;
    partial void OnBarPeriodsHighChanging(global::System.Int32 value);
    partial void OnBarPeriodsHighChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean StrictTradeClose {
      get {
        return _StrictTradeClose;
      }
      set {
        OnStrictTradeCloseChanging(value);
        ReportPropertyChanging("StrictTradeClose");
        _StrictTradeClose = StructuralObject.SetValidValue(value, "StrictTradeClose");
        ReportPropertyChanged("StrictTradeClose");
        OnStrictTradeCloseChanged();
      }
    }
    private global::System.Boolean _StrictTradeClose;
    partial void OnStrictTradeCloseChanging(global::System.Boolean value);
    partial void OnStrictTradeCloseChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double BarPeriodsLowHighRatio {
      get {
        return _BarPeriodsLowHighRatio;
      }
      set {
        OnBarPeriodsLowHighRatioChanging(value);
        ReportPropertyChanging("BarPeriodsLowHighRatio");
        _BarPeriodsLowHighRatio = StructuralObject.SetValidValue(value, "BarPeriodsLowHighRatio");
        ReportPropertyChanged("BarPeriodsLowHighRatio");
        OnBarPeriodsLowHighRatioChanged();
      }
    }
    private global::System.Double _BarPeriodsLowHighRatio;
    partial void OnBarPeriodsLowHighRatioChanging(global::System.Double value);
    partial void OnBarPeriodsLowHighRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 LongMAPeriod {
      get {
        return _LongMAPeriod;
      }
      set {
        OnLongMAPeriodChanging(value);
        ReportPropertyChanging("LongMAPeriod");
        _LongMAPeriod = StructuralObject.SetValidValue(value, "LongMAPeriod");
        ReportPropertyChanged("LongMAPeriod");
        OnLongMAPeriodChanged();
      }
    }
    private global::System.Int32 _LongMAPeriod;
    partial void OnLongMAPeriodChanging(global::System.Int32 value);
    partial void OnLongMAPeriodChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorAverageDaysBack {
      get {
        return _CorridorAverageDaysBack;
      }
      set {
        OnCorridorAverageDaysBackChanging(value);
        ReportPropertyChanging("CorridorAverageDaysBack");
        _CorridorAverageDaysBack = StructuralObject.SetValidValue(value, "CorridorAverageDaysBack");
        ReportPropertyChanged("CorridorAverageDaysBack");
        OnCorridorAverageDaysBackChanged();
      }
    }
    private global::System.Int32 _CorridorAverageDaysBack;
    partial void OnCorridorAverageDaysBackChanging(global::System.Int32 value);
    partial void OnCorridorAverageDaysBackChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorPeriodsStart {
      get {
        return _CorridorPeriodsStart;
      }
      set {
        OnCorridorPeriodsStartChanging(value);
        ReportPropertyChanging("CorridorPeriodsStart");
        _CorridorPeriodsStart = StructuralObject.SetValidValue(value, "CorridorPeriodsStart");
        ReportPropertyChanged("CorridorPeriodsStart");
        OnCorridorPeriodsStartChanged();
      }
    }
    private global::System.Int32 _CorridorPeriodsStart;
    partial void OnCorridorPeriodsStartChanging(global::System.Int32 value);
    partial void OnCorridorPeriodsStartChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorPeriodsLength {
      get {
        return _CorridorPeriodsLength;
      }
      set {
        OnCorridorPeriodsLengthChanging(value);
        ReportPropertyChanging("CorridorPeriodsLength");
        _CorridorPeriodsLength = StructuralObject.SetValidValue(value, "CorridorPeriodsLength");
        ReportPropertyChanged("CorridorPeriodsLength");
        OnCorridorPeriodsLengthChanged();
      }
    }
    private global::System.Int32 _CorridorPeriodsLength;
    partial void OnCorridorPeriodsLengthChanging(global::System.Int32 value);
    partial void OnCorridorPeriodsLengthChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [Dnr]
    public Nullable<global::System.DateTime> CorridorStartDate {
      get {
        return _CorridorStartDate;
      }
      set {
        OnCorridorStartDateChanging(value);
        ReportPropertyChanging("CorridorStartDate");
        _CorridorStartDate = StructuralObject.SetValidValue(value, "CorridorStartDate");
        ReportPropertyChanged("CorridorStartDate");
        OnCorridorStartDateChanged();
      }
    }
    private Nullable<global::System.DateTime> _CorridorStartDate;
    partial void OnCorridorStartDateChanging(Nullable<global::System.DateTime> value);
    partial void OnCorridorStartDateChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double CorridorRatioForRange {
      get {
        return _CorridorRatioForRange;
      }
      set {
        OnCorridorRatioForRangeChanging(value);
        ReportPropertyChanging("CorridorRatioForRange");
        _CorridorRatioForRange = StructuralObject.SetValidValue(value, "CorridorRatioForRange");
        ReportPropertyChanged("CorridorRatioForRange");
        OnCorridorRatioForRangeChanged();
      }
    }
    private global::System.Double _CorridorRatioForRange;
    partial void OnCorridorRatioForRangeChanging(global::System.Double value);
    partial void OnCorridorRatioForRangeChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [Dnr]
    public global::System.Double CorridorRatioForBreakout {
      get {
        return _CorridorRatioForBreakout;
      }
      set {
        OnCorridorRatioForBreakoutChanging(value);
        ReportPropertyChanging("CorridorRatioForBreakout");
        _CorridorRatioForBreakout = StructuralObject.SetValidValue(value, "CorridorRatioForBreakout");
        ReportPropertyChanged("CorridorRatioForBreakout");
        OnCorridorRatioForBreakoutChanged();
      }
    }
    private global::System.Double _CorridorRatioForBreakout;
    partial void OnCorridorRatioForBreakoutChanging(global::System.Double value);
    partial void OnCorridorRatioForBreakoutChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double RangeRatioForTradeLimit {
      get {
        return _RangeRatioForTradeLimit;
      }
      set {
        OnRangeRatioForTradeLimitChanging(value);
        ReportPropertyChanging("RangeRatioForTradeLimit");
        _RangeRatioForTradeLimit = StructuralObject.SetValidValue(value, "RangeRatioForTradeLimit");
        ReportPropertyChanged("RangeRatioForTradeLimit");
        OnRangeRatioForTradeLimitChanged();
      }
    }
    private global::System.Double _RangeRatioForTradeLimit;
    partial void OnRangeRatioForTradeLimitChanging(global::System.Double value);
    partial void OnRangeRatioForTradeLimitChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean TradeByAngle {
      get {
        return _TradeByAngle;
      }
      set {
        OnTradeByAngleChanging(value);
        ReportPropertyChanging("TradeByAngle");
        _TradeByAngle = StructuralObject.SetValidValue(value, "TradeByAngle");
        ReportPropertyChanged("TradeByAngle");
        OnTradeByAngleChanged();
      }
    }
    private global::System.Boolean _TradeByAngle;
    partial void OnTradeByAngleChanging(global::System.Boolean value);
    partial void OnTradeByAngleChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double ProfitToLossExitRatio {
      get {
        return _ProfitToLossExitRatio;
      }
      set {
        OnProfitToLossExitRatioChanging(value);
        ReportPropertyChanging("ProfitToLossExitRatio");
        _ProfitToLossExitRatio = StructuralObject.SetValidValue(value, "ProfitToLossExitRatio");
        ReportPropertyChanged("ProfitToLossExitRatio");
        OnProfitToLossExitRatioChanged();
      }
    }
    private global::System.Double _ProfitToLossExitRatio;
    partial void OnProfitToLossExitRatioChanging(global::System.Double value);
    partial void OnProfitToLossExitRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.Boolean> TradeByFirstWave {
      get {
        return _TradeByFirstWave;
      }
      set {
        OnTradeByFirstWaveChanging(value);
        ReportPropertyChanging("TradeByFirstWave");
        _TradeByFirstWave = StructuralObject.SetValidValue(value, "TradeByFirstWave");
        ReportPropertyChanged("TradeByFirstWave");
        OnTradeByFirstWaveChanged();
      }
    }
    private Nullable<global::System.Boolean> _TradeByFirstWave;
    partial void OnTradeByFirstWaveChanging(Nullable<global::System.Boolean> value);
    partial void OnTradeByFirstWaveChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 PowerRowOffset {
      get {
        return _PowerRowOffset;
      }
      set {
        OnPowerRowOffsetChanging(value);
        ReportPropertyChanging("PowerRowOffset");
        _PowerRowOffset = StructuralObject.SetValidValue(value, "PowerRowOffset");
        ReportPropertyChanged("PowerRowOffset");
        OnPowerRowOffsetChanged();
      }
    }
    private global::System.Int32 _PowerRowOffset;
    partial void OnPowerRowOffsetChanging(global::System.Int32 value);
    partial void OnPowerRowOffsetChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double RangeRatioForTradeStop {
      get {
        return _RangeRatioForTradeStop;
      }
      set {
        OnRangeRatioForTradeStopChanging(value);
        ReportPropertyChanging("RangeRatioForTradeStop");
        _RangeRatioForTradeStop = StructuralObject.SetValidValue(value, "RangeRatioForTradeStop");
        ReportPropertyChanged("RangeRatioForTradeStop");
        OnRangeRatioForTradeStopChanged();
      }
    }
    private global::System.Double _RangeRatioForTradeStop;
    partial void OnRangeRatioForTradeStopChanging(global::System.Double value);
    partial void OnRangeRatioForTradeStopChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean ReversePower {
      get {
        return _ReversePower;
      }
      set {
        OnReversePowerChanging(value);
        ReportPropertyChanging("ReversePower");
        _ReversePower = StructuralObject.SetValidValue(value, "ReversePower");
        ReportPropertyChanged("ReversePower");
        OnReversePowerChanged();
      }
    }
    private global::System.Boolean _ReversePower;
    partial void OnReversePowerChanging(global::System.Boolean value);
    partial void OnReversePowerChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double CorrelationTreshold {
      get {
        return _CorrelationTreshold;
      }
      set {
        OnCorrelationTresholdChanging(value);
        ReportPropertyChanging("CorrelationTreshold");
        _CorrelationTreshold = StructuralObject.SetValidValue(value, "CorrelationTreshold");
        ReportPropertyChanged("CorrelationTreshold");
        OnCorrelationTresholdChanged();
      }
    }
    private global::System.Double _CorrelationTreshold;
    partial void OnCorrelationTresholdChanging(global::System.Double value);
    partial void OnCorrelationTresholdChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean CloseOnProfitOnly {
      get {
        return _CloseOnProfitOnly;
      }
      set {
        OnCloseOnProfitOnlyChanging(value);
        ReportPropertyChanging("CloseOnProfitOnly");
        _CloseOnProfitOnly = StructuralObject.SetValidValue(value, "CloseOnProfitOnly");
        ReportPropertyChanged("CloseOnProfitOnly");
        OnCloseOnProfitOnlyChanged();
      }
    }
    private global::System.Boolean _CloseOnProfitOnly;
    partial void OnCloseOnProfitOnlyChanging(global::System.Boolean value);
    partial void OnCloseOnProfitOnlyChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean CloseOnOpen {
      get {
        return _CloseOnOpen;
      }
      set {
        OnCloseOnOpenChanging(value);
        ReportPropertyChanging("CloseOnOpen");
        _CloseOnOpen = StructuralObject.SetValidValue(value, "CloseOnOpen");
        ReportPropertyChanged("CloseOnOpen");
        OnCloseOnOpenChanged();
      }
    }
    private global::System.Boolean _CloseOnOpen;
    partial void OnCloseOnOpenChanging(global::System.Boolean value);
    partial void OnCloseOnOpenChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean StreachTradingDistance {
      get {
        return _StreachTradingDistance;
      }
      set {
        OnStreachTradingDistanceChanging(value);
        ReportPropertyChanging("StreachTradingDistance");
        _StreachTradingDistance = StructuralObject.SetValidValue(value, "StreachTradingDistance");
        ReportPropertyChanged("StreachTradingDistance");
        OnStreachTradingDistanceChanged();
      }
    }
    private global::System.Boolean _StreachTradingDistance;
    partial void OnStreachTradingDistanceChanging(global::System.Boolean value);
    partial void OnStreachTradingDistanceChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean CloseAllOnProfit {
      get {
        return _CloseAllOnProfit;
      }
      set {
        OnCloseAllOnProfitChanging(value);
        ReportPropertyChanging("CloseAllOnProfit");
        _CloseAllOnProfit = StructuralObject.SetValidValue(value, "CloseAllOnProfit");
        ReportPropertyChanged("CloseAllOnProfit");
        OnCloseAllOnProfitChanged();
      }
    }
    private global::System.Boolean _CloseAllOnProfit;
    partial void OnCloseAllOnProfitChanging(global::System.Boolean value);
    partial void OnCloseAllOnProfitChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean TradeAndAngleSynced {
      get {
        return _TradeAndAngleSynced;
      }
      set {
        OnTradeAndAngleSyncedChanging(value);
        ReportPropertyChanging("TradeAndAngleSynced");
        _TradeAndAngleSynced = StructuralObject.SetValidValue(value, "TradeAndAngleSynced");
        ReportPropertyChanged("TradeAndAngleSynced");
        OnTradeAndAngleSyncedChanged();
      }
    }
    private global::System.Boolean _TradeAndAngleSynced;
    partial void OnTradeAndAngleSyncedChanging(global::System.Boolean value);
    partial void OnTradeAndAngleSyncedChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double TradingAngleRange {
      get {
        return _TradingAngleRange;
      }
      set {
        OnTradingAngleRangeChanging(value);
        ReportPropertyChanging("TradingAngleRange");
        _TradingAngleRange = StructuralObject.SetValidValue(value, "TradingAngleRange");
        ReportPropertyChanged("TradingAngleRange");
        OnTradingAngleRangeChanged();
      }
    }
    private global::System.Double _TradingAngleRange;
    partial void OnTradingAngleRangeChanging(global::System.Double value);
    partial void OnTradingAngleRangeChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean CloseByMomentum {
      get {
        return _CloseByMomentum;
      }
      set {
        OnCloseByMomentumChanging(value);
        ReportPropertyChanging("CloseByMomentum");
        _CloseByMomentum = StructuralObject.SetValidValue(value, "CloseByMomentum");
        ReportPropertyChanged("CloseByMomentum");
        OnCloseByMomentumChanged();
      }
    }
    private global::System.Boolean _CloseByMomentum;
    partial void OnCloseByMomentumChanging(global::System.Boolean value);
    partial void OnCloseByMomentumChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean TradeByRateDirection {
      get {
        return _TradeByRateDirection;
      }
      set {
        OnTradeByRateDirectionChanging(value);
        ReportPropertyChanging("TradeByRateDirection");
        _TradeByRateDirection = StructuralObject.SetValidValue(value, "TradeByRateDirection");
        ReportPropertyChanged("TradeByRateDirection");
        OnTradeByRateDirectionChanged();
      }
    }
    private global::System.Boolean _TradeByRateDirection;
    partial void OnTradeByRateDirectionChanging(global::System.Boolean value);
    partial void OnTradeByRateDirectionChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.DateTime> SupportDate {
      get {
        return _SupportDate;
      }
      set {
        OnSupportDateChanging(value);
        ReportPropertyChanging("SupportDate");
        _SupportDate = StructuralObject.SetValidValue(value, "SupportDate");
        ReportPropertyChanged("SupportDate");
        OnSupportDateChanged();
      }
    }
    private Nullable<global::System.DateTime> _SupportDate;
    partial void OnSupportDateChanging(Nullable<global::System.DateTime> value);
    partial void OnSupportDateChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.DateTime> ResistanceDate {
      get {
        return _ResistanceDate;
      }
      set {
        OnResistanceDateChanging(value);
        ReportPropertyChanging("ResistanceDate");
        _ResistanceDate = StructuralObject.SetValidValue(value, "ResistanceDate");
        ReportPropertyChanged("ResistanceDate");
        OnResistanceDateChanged();
      }
    }
    private Nullable<global::System.DateTime> _ResistanceDate;
    partial void OnResistanceDateChanging(Nullable<global::System.DateTime> value);
    partial void OnResistanceDateChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [Dnr]
    public Nullable<global::System.Double> GannAnglesOffset {
      get {
        return _GannAnglesOffset;
      }
      set {
        OnGannAnglesOffsetChanging(value);
        ReportPropertyChanging("GannAnglesOffset");
        _GannAnglesOffset = StructuralObject.SetValidValue(value, "GannAnglesOffset");
        ReportPropertyChanged("GannAnglesOffset");
        OnGannAnglesOffsetChanged();
      }
    }
    private Nullable<global::System.Double> _GannAnglesOffset;
    partial void OnGannAnglesOffsetChanging(Nullable<global::System.Double> value);
    partial void OnGannAnglesOffsetChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    [Dnr]
    public global::System.String GannAngles {
      get {
        return _GannAngles;
      }
      set {
        OnGannAnglesChanging(value);
        ReportPropertyChanging("GannAngles");
        _GannAngles = StructuralObject.SetValidValue(value, false, "GannAngles");
        ReportPropertyChanged("GannAngles");
        OnGannAnglesChanged();
      }
    }
    private global::System.String _GannAngles;
    partial void OnGannAnglesChanging(global::System.String value);
    partial void OnGannAnglesChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    [Dnr]
    public global::System.Boolean IsGannAnglesManual {
      get {
        return _IsGannAnglesManual;
      }
      set {
        OnIsGannAnglesManualChanging(value);
        ReportPropertyChanging("IsGannAnglesManual");
        _IsGannAnglesManual = StructuralObject.SetValidValue(value, "IsGannAnglesManual");
        ReportPropertyChanged("IsGannAnglesManual");
        OnIsGannAnglesManualChanged();
      }
    }
    private global::System.Boolean _IsGannAnglesManual;
    partial void OnIsGannAnglesManualChanging(global::System.Boolean value);
    partial void OnIsGannAnglesManualChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    [Dnr]
    public Nullable<global::System.DateTime> GannAnglesAnchorDate {
      get {
        return _GannAnglesAnchorDate;
      }
      set {
        OnGannAnglesAnchorDateChanging(value);
        ReportPropertyChanging("GannAnglesAnchorDate");
        _GannAnglesAnchorDate = StructuralObject.SetValidValue(value, "GannAnglesAnchorDate");
        ReportPropertyChanged("GannAnglesAnchorDate");
        OnGannAnglesAnchorDateChanged();
      }
    }
    private Nullable<global::System.DateTime> _GannAnglesAnchorDate;
    partial void OnGannAnglesAnchorDateChanging(Nullable<global::System.DateTime> value);
    partial void OnGannAnglesAnchorDateChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double SpreadShortToLongTreshold {
      get {
        return _SpreadShortToLongTreshold;
      }
      set {
        OnSpreadShortToLongTresholdChanging(value);
        ReportPropertyChanging("SpreadShortToLongTreshold");
        _SpreadShortToLongTreshold = StructuralObject.SetValidValue(value, "SpreadShortToLongTreshold");
        ReportPropertyChanged("SpreadShortToLongTreshold");
        OnSpreadShortToLongTresholdChanged();
      }
    }
    private global::System.Double _SpreadShortToLongTreshold;
    partial void OnSpreadShortToLongTresholdChanging(global::System.Double value);
    partial void OnSpreadShortToLongTresholdChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.Double> SupportPriceStore {
      get {
        return _SupportPriceStore;
      }
      set {
        OnSupportPriceStoreChanging(value);
        ReportPropertyChanging("SupportPriceStore");
        _SupportPriceStore = StructuralObject.SetValidValue(value, "SupportPriceStore");
        ReportPropertyChanged("SupportPriceStore");
        OnSupportPriceStoreChanged();
      }
    }
    private Nullable<global::System.Double> _SupportPriceStore;
    partial void OnSupportPriceStoreChanging(Nullable<global::System.Double> value);
    partial void OnSupportPriceStoreChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.Double> ResistancePriceStore {
      get {
        return _ResistancePriceStore;
      }
      set {
        OnResistancePriceStoreChanging(value);
        ReportPropertyChanging("ResistancePriceStore");
        _ResistancePriceStore = StructuralObject.SetValidValue(value, "ResistancePriceStore");
        ReportPropertyChanged("ResistancePriceStore");
        OnResistancePriceStoreChanged();
      }
    }
    private Nullable<global::System.Double> _ResistancePriceStore;
    partial void OnResistancePriceStoreChanging(Nullable<global::System.Double> value);
    partial void OnResistancePriceStoreChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 SuppResLevelsCount {
      get {
        return _SuppResLevelsCount;
      }
      set {
        OnSuppResLevelsCountChanging(value);
        ReportPropertyChanging("SuppResLevelsCount");
        _SuppResLevelsCount = StructuralObject.SetValidValue(value, "SuppResLevelsCount");
        ReportPropertyChanged("SuppResLevelsCount");
        OnSuppResLevelsCountChanged();
      }
    }
    private global::System.Int32 _SuppResLevelsCount;
    partial void OnSuppResLevelsCountChanging(global::System.Int32 value);
    partial void OnSuppResLevelsCountChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean DoStreatchRates {
      get {
        return _DoStreatchRates;
      }
      set {
        OnDoStreatchRatesChanging(value);
        ReportPropertyChanging("DoStreatchRates");
        _DoStreatchRates = StructuralObject.SetValidValue(value, "DoStreatchRates");
        ReportPropertyChanged("DoStreatchRates");
        OnDoStreatchRatesChanged();
      }
    }
    private global::System.Boolean _DoStreatchRates;
    partial void OnDoStreatchRatesChanging(global::System.Boolean value);
    partial void OnDoStreatchRatesChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean IsSuppResManual {
      get {
        return _IsSuppResManual;
      }
      set {
        OnIsSuppResManualChanging(value);
        ReportPropertyChanging("IsSuppResManual");
        _IsSuppResManual = StructuralObject.SetValidValue(value, "IsSuppResManual");
        ReportPropertyChanged("IsSuppResManual");
        OnIsSuppResManualChanged();
      }
    }
    private global::System.Boolean _IsSuppResManual;
    partial void OnIsSuppResManualChanging(global::System.Boolean value);
    partial void OnIsSuppResManualChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean TradeOnCrossOnly {
      get {
        return _TradeOnCrossOnly;
      }
      set {
        OnTradeOnCrossOnlyChanging(value);
        ReportPropertyChanging("TradeOnCrossOnly");
        _TradeOnCrossOnly = StructuralObject.SetValidValue(value, "TradeOnCrossOnly");
        ReportPropertyChanged("TradeOnCrossOnly");
        OnTradeOnCrossOnlyChanged();
      }
    }
    private global::System.Boolean _TradeOnCrossOnly;
    partial void OnTradeOnCrossOnlyChanging(global::System.Boolean value);
    partial void OnTradeOnCrossOnlyChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 TakeProfitFunctionInt {
      get {
        return _TakeProfitFunctionInt;
      }
      set {
        OnTakeProfitFunctionIntChanging(value);
        ReportPropertyChanging("TakeProfitFunctionInt");
        _TakeProfitFunctionInt = StructuralObject.SetValidValue(value, "TakeProfitFunctionInt");
        ReportPropertyChanged("TakeProfitFunctionInt");
        OnTakeProfitFunctionIntChanged();
      }
    }
    private global::System.Int32 _TakeProfitFunctionInt;
    partial void OnTakeProfitFunctionIntChanging(global::System.Int32 value);
    partial void OnTakeProfitFunctionIntChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean DoAdjustTimeframeByAllowedLot {
      get {
        return _DoAdjustTimeframeByAllowedLot;
      }
      set {
        OnDoAdjustTimeframeByAllowedLotChanging(value);
        ReportPropertyChanging("DoAdjustTimeframeByAllowedLot");
        _DoAdjustTimeframeByAllowedLot = StructuralObject.SetValidValue(value, "DoAdjustTimeframeByAllowedLot");
        ReportPropertyChanged("DoAdjustTimeframeByAllowedLot");
        OnDoAdjustTimeframeByAllowedLotChanged();
      }
    }
    private global::System.Boolean _DoAdjustTimeframeByAllowedLot;
    partial void OnDoAdjustTimeframeByAllowedLotChanging(global::System.Boolean value);
    partial void OnDoAdjustTimeframeByAllowedLotChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean IsColdOnTrades {
      get {
        return _IsColdOnTrades;
      }
      set {
        OnIsColdOnTradesChanging(value);
        ReportPropertyChanging("IsColdOnTrades");
        _IsColdOnTrades = StructuralObject.SetValidValue(value, "IsColdOnTrades");
        ReportPropertyChanged("IsColdOnTrades");
        OnIsColdOnTradesChanged();
      }
    }
    private global::System.Boolean _IsColdOnTrades;
    partial void OnIsColdOnTradesChanging(global::System.Boolean value);
    partial void OnIsColdOnTradesChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorCrossesCountMinimum {
      get {
        return _CorridorCrossesCountMinimum;
      }
      set {
        OnCorridorCrossesCountMinimumChanging(value);
        ReportPropertyChanging("CorridorCrossesCountMinimum");
        _CorridorCrossesCountMinimum = StructuralObject.SetValidValue(value, "CorridorCrossesCountMinimum");
        ReportPropertyChanged("CorridorCrossesCountMinimum");
        OnCorridorCrossesCountMinimumChanged();
      }
    }
    private global::System.Int32 _CorridorCrossesCountMinimum;
    partial void OnCorridorCrossesCountMinimumChanging(global::System.Int32 value);
    partial void OnCorridorCrossesCountMinimumChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double StDevToSpreadRatio {
      get {
        return _StDevToSpreadRatio;
      }
      set {
        OnStDevToSpreadRatioChanging(value);
        ReportPropertyChanging("StDevToSpreadRatio");
        _StDevToSpreadRatio = StructuralObject.SetValidValue(value, "StDevToSpreadRatio");
        ReportPropertyChanged("StDevToSpreadRatio");
        OnStDevToSpreadRatioChanged();
      }
    }
    private global::System.Double _StDevToSpreadRatio;
    partial void OnStDevToSpreadRatioChanging(global::System.Double value);
    partial void OnStDevToSpreadRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 LoadRatesSecondsWarning {
      get {
        return _LoadRatesSecondsWarning;
      }
      set {
        OnLoadRatesSecondsWarningChanging(value);
        ReportPropertyChanging("LoadRatesSecondsWarning");
        _LoadRatesSecondsWarning = StructuralObject.SetValidValue(value, "LoadRatesSecondsWarning");
        ReportPropertyChanged("LoadRatesSecondsWarning");
        OnLoadRatesSecondsWarningChanged();
      }
    }
    private global::System.Int32 _LoadRatesSecondsWarning;
    partial void OnLoadRatesSecondsWarningChanging(global::System.Int32 value);
    partial void OnLoadRatesSecondsWarningChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorHighLowMethodInt {
      get {
        return _CorridorHighLowMethodInt;
      }
      set {
        OnCorridorHighLowMethodIntChanging(value);
        ReportPropertyChanging("CorridorHighLowMethodInt");
        _CorridorHighLowMethodInt = StructuralObject.SetValidValue(value, "CorridorHighLowMethodInt");
        ReportPropertyChanged("CorridorHighLowMethodInt");
        OnCorridorHighLowMethodIntChanged();
      }
    }
    private global::System.Int32 _CorridorHighLowMethodInt;
    partial void OnCorridorHighLowMethodIntChanging(global::System.Int32 value);
    partial void OnCorridorHighLowMethodIntChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double CorridorStDevRatioMax {
      get {
        return _CorridorStDevRatioMax;
      }
      set {
        OnCorridorStDevRatioMaxChanging(value);
        ReportPropertyChanging("CorridorStDevRatioMax");
        _CorridorStDevRatioMax = StructuralObject.SetValidValue(value, "CorridorStDevRatioMax");
        ReportPropertyChanged("CorridorStDevRatioMax");
        OnCorridorStDevRatioMaxChanged();
      }
    }
    private global::System.Double _CorridorStDevRatioMax;
    partial void OnCorridorStDevRatioMaxChanging(global::System.Double value);
    partial void OnCorridorStDevRatioMaxChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double CorridorLengthMinimum {
      get {
        return _CorridorLengthMinimum;
      }
      set {
        OnCorridorLengthMinimumChanging(value);
        ReportPropertyChanging("CorridorLengthMinimum");
        _CorridorLengthMinimum = StructuralObject.SetValidValue(value, "CorridorLengthMinimum");
        ReportPropertyChanged("CorridorLengthMinimum");
        OnCorridorLengthMinimumChanged();
      }
    }
    private global::System.Double _CorridorLengthMinimum;
    partial void OnCorridorLengthMinimumChanging(global::System.Double value);
    partial void OnCorridorLengthMinimumChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 CorridorCrossHighLowMethodInt {
      get {
        return _CorridorCrossHighLowMethodInt;
      }
      set {
        OnCorridorCrossHighLowMethodIntChanging(value);
        ReportPropertyChanging("CorridorCrossHighLowMethodInt");
        _CorridorCrossHighLowMethodInt = StructuralObject.SetValidValue(value, "CorridorCrossHighLowMethodInt");
        ReportPropertyChanged("CorridorCrossHighLowMethodInt");
        OnCorridorCrossHighLowMethodIntChanged();
      }
    }
    private global::System.Int32 _CorridorCrossHighLowMethodInt;
    partial void OnCorridorCrossHighLowMethodIntChanging(global::System.Int32 value);
    partial void OnCorridorCrossHighLowMethodIntChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.Int32> MovingAverageTypeInt {
      get {
        return _MovingAverageTypeInt;
      }
      set {
        OnMovingAverageTypeIntChanging(value);
        ReportPropertyChanging("MovingAverageTypeInt");
        _MovingAverageTypeInt = StructuralObject.SetValidValue(value, "MovingAverageTypeInt");
        ReportPropertyChanged("MovingAverageTypeInt");
        OnMovingAverageTypeIntChanged();
      }
    }
    private Nullable<global::System.Int32> _MovingAverageTypeInt;
    partial void OnMovingAverageTypeIntChanging(Nullable<global::System.Int32> value);
    partial void OnMovingAverageTypeIntChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double PriceCmaLevels {
      get {
        return _PriceCmaLevels;
      }
      set {
        OnPriceCmaLevelsChanging(value);
        ReportPropertyChanging("PriceCmaLevels");
        _PriceCmaLevels = StructuralObject.SetValidValue(value, "PriceCmaLevels");
        ReportPropertyChanged("PriceCmaLevels");
        OnPriceCmaLevelsChanged();
      }
    }
    private global::System.Double _PriceCmaLevels;
    partial void OnPriceCmaLevelsChanging(global::System.Double value);
    partial void OnPriceCmaLevelsChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 VolumeTresholdIterations {
      get {
        return _VolumeTresholdIterations;
      }
      set {
        OnVolumeTresholdIterationsChanging(value);
        ReportPropertyChanging("VolumeTresholdIterations");
        _VolumeTresholdIterations = StructuralObject.SetValidValue(value, "VolumeTresholdIterations");
        ReportPropertyChanged("VolumeTresholdIterations");
        OnVolumeTresholdIterationsChanged();
      }
    }
    private global::System.Int32 _VolumeTresholdIterations;
    partial void OnVolumeTresholdIterationsChanging(global::System.Int32 value);
    partial void OnVolumeTresholdIterationsChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 StDevTresholdIterations {
      get {
        return _StDevTresholdIterations;
      }
      set {
        OnStDevTresholdIterationsChanging(value);
        ReportPropertyChanging("StDevTresholdIterations");
        _StDevTresholdIterations = StructuralObject.SetValidValue(value, "StDevTresholdIterations");
        ReportPropertyChanged("StDevTresholdIterations");
        OnStDevTresholdIterationsChanged();
      }
    }
    private global::System.Int32 _StDevTresholdIterations;
    partial void OnStDevTresholdIterationsChanging(global::System.Int32 value);
    partial void OnStDevTresholdIterationsChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    public global::System.Double StDevAverageLeewayRatio {
      get {
        return _StDevAverageLeewayRatio;
      }
      set {
        OnStDevAverageLeewayRatioChanging(value);
        ReportPropertyChanging("StDevAverageLeewayRatio");
        _StDevAverageLeewayRatio = StructuralObject.SetValidValue(value, "StDevAverageLeewayRatio");
        ReportPropertyChanged("StDevAverageLeewayRatio");
        OnStDevAverageLeewayRatioChanged();
      }
    }
    private global::System.Double _StDevAverageLeewayRatio;
    partial void OnStDevAverageLeewayRatioChanging(global::System.Double value);
    partial void OnStDevAverageLeewayRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Int32 ExtreamCloseOffset {
      get {
        return _ExtreamCloseOffset;
      }
      set {
        OnExtreamCloseOffsetChanging(value);
        ReportPropertyChanging("ExtreamCloseOffset");
        _ExtreamCloseOffset = StructuralObject.SetValidValue(value, "ExtreamCloseOffset");
        ReportPropertyChanged("ExtreamCloseOffset");
        OnExtreamCloseOffsetChanged();
      }
    }
    private global::System.Int32 _ExtreamCloseOffset;
    partial void OnExtreamCloseOffsetChanging(global::System.Int32 value);
    partial void OnExtreamCloseOffsetChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double CurrentLossInPipsCloseAdjustment {
      get {
        return _CurrentLossInPipsCloseAdjustment;
      }
      set {
        OnCurrentLossInPipsCloseAdjustmentChanging(value);
        ReportPropertyChanging("CurrentLossInPipsCloseAdjustment");
        _CurrentLossInPipsCloseAdjustment = StructuralObject.SetValidValue(value, "CurrentLossInPipsCloseAdjustment");
        ReportPropertyChanged("CurrentLossInPipsCloseAdjustment");
        OnCurrentLossInPipsCloseAdjustmentChanged();
      }
    }
    private global::System.Double _CurrentLossInPipsCloseAdjustment;
    partial void OnCurrentLossInPipsCloseAdjustmentChanging(global::System.Double value);
    partial void OnCurrentLossInPipsCloseAdjustmentChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double CorridorBigToSmallRatio {
      get {
        return _CorridorBigToSmallRatio;
      }
      set {
        OnCorridorBigToSmallRatioChanging(value);
        ReportPropertyChanging("CorridorBigToSmallRatio");
        _CorridorBigToSmallRatio = StructuralObject.SetValidValue(value, "CorridorBigToSmallRatio");
        ReportPropertyChanged("CorridorBigToSmallRatio");
        OnCorridorBigToSmallRatioChanged();
      }
    }
    private global::System.Double _CorridorBigToSmallRatio;
    partial void OnCorridorBigToSmallRatioChanging(global::System.Double value);
    partial void OnCorridorBigToSmallRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.Double> ResetOnBalance {
      get {
        return _ResetOnBalance;
      }
      set {
        OnResetOnBalanceChanging(value);
        ReportPropertyChanging("ResetOnBalance");
        _ResetOnBalance = StructuralObject.SetValidValue(value, "ResetOnBalance");
        ReportPropertyChanged("ResetOnBalance");
        OnResetOnBalanceChanged();
      }
    }
    private Nullable<global::System.Double> _ResetOnBalance;
    partial void OnResetOnBalanceChanging(Nullable<global::System.Double> value);
    partial void OnResetOnBalanceChanged();

    #endregion

  }
}
