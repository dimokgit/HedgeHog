using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Web;

namespace HedgeHog.WCF {
  [Serializable]
  [DataContract]
  public class TradeResponse {
    [DataMember]
    public string Pair { get; set; }
    [DataMember]
    public int TradeSignalDelay = 2;
    [DataMember]
    public DateTime ServerTime { get; set; }
    [DataMember]
    DateTime _goBuyTime = DateTime.MinValue;
    [DataMember]
    DateTime GoBuyTime { get { return _goBuyTime; } set { _goBuyTime = value; } }
    [DataMember]
    DateTime _goSellTime = DateTime.MinValue;
    [DataMember]
    DateTime GoSellTime { get { return _goSellTime; } set { _goSellTime = value; } }
    [DataMember]
    public int TradeWaveInMinutes { get; set; }

    //bool goBuy { 
    //  get { return (this.ServerTime - GoBuyTime).Duration().TotalSeconds.Between(0, TradeSignalDelay); }
    //  set { this.GoBuyTime = value ? this.ServerTime : DateTime.MinValue; }
    //}
    //bool goSell { 
    //  get { return (this.ServerTime - GoSellTime).TotalSeconds.Between(0, TradeSignalDelay); }
    //  set { this.GoSellTime = value ? this.ServerTime : DateTime.MinValue; }
    //}
    [DataMember]
    public bool GoBuy { get; set; }
    [DataMember]
    public bool GoSell { get; set; }
    [DataMember]
    public bool CloseBuy { get; set; }
    [DataMember]
    public bool CloseSell { get; set; }
    [DataMember]
    public bool CloseLastBuy { get; set; }
    [DataMember]
    public bool CloseLastSell { get; set; }
    [DataMember]
    public string[] CloseTradeIDs { get; set; }
    [DataMember]
    public bool TrancateBuy { get; set; }
    [DataMember]
    public bool TrancateSell { get; set; }
    [DataMember]
    public bool DoTakeProfitBuy { get; set; }
    [DataMember]
    public bool DoTakeProfitSell { get; set; }
    [DataMember]
    public double LotsToTradeBuy { get; set; }
    [DataMember]
    public double LotsToTradeSell { get; set; }
    [DataMember]
    public double DencityRatio { get; set; }
    [DataMember]
    public double DencityRatioBuy { get; set; }
    [DataMember]
    public double DencityRatioSell { get; set; }
    [DataMember]
    public int RsiHigh { get; set; }
    [DataMember]
    public int RsiLow { get; set; }
    [DataMember]
    public int RsiRegressionOffsetBuy { get; set; }
    [DataMember]
    public int RsiRegressionOffsetSell { get; set; }
    [DataMember]
    public double RsiRegressionAverage { get; set; }
    [DataMember]
    bool _canTrade = true;
    [DataMember]
    public bool CanTrade { get { return _canTrade; } set { _canTrade = value; } }
    [DataMember]
    public bool CorridorOK { get; set; }
    [DataMember]
    TradeStatistics _tradeStats = new TradeStatistics();
    [DataMember]
    public bool IsReady { get; set; }
    public DateTime[] FractalDates { get { return FractalDatesBuy.Concat(FractalDatesSell).ToArray(); } }

    [DataMember]
    DateTime[] _fractalDatesBuy = new DateTime[] { };
    public DateTime[] FractalDatesBuy { get { return _fractalDatesBuy ?? new DateTime[] { }; } set { _fractalDatesBuy = value; } }

    [DataMember]
    DateTime[] _fractalDatesSell = new DateTime[] { };
    public DateTime[] FractalDatesSell { get { return _fractalDatesSell ?? new DateTime[] { }; } set { _fractalDatesSell = value; } }

    [DataMember]
    public TradeStatistics TradeStats { get { return _tradeStats; } set { _tradeStats = value; } }

  }
  [Serializable]
  [DataContract]
  public class TradeRequest : IEquatable<TradeRequest> {
    [DataMember]
    public string pair { get; set; }
    [DataMember]
    public Guid guid { get; set; }
    [DataMember]
    public int highBarMinutes = 5;
    [DataMember]
    public int closeOppositeOffset = 0;
    [DataMember]
    public bool sellOnProfitLast = true;
    [DataMember]
    public int shortStack = 0;
    [DataMember]
    public int shortStackTruncateOffset = 2;
    [DataMember]
    public int corridorMinites = 5;
    [DataMember]
    public int corridorSmoothSeconds = 5;
    [DataMember]
    public int goTradeFooBuy = 3;
    [DataMember]
    public int goTradeFooSell = 3;
    [DataMember]
    public int densityFoo = 2;
    [DataMember]
    public int lotsToTradeFooBuy = 3;
    [DataMember]
    public int lotsToTradeFooSell = 3;
    [DataMember]
    public int DecisionFoo = 10;
    [DataMember]
    public double profitMin = -.5;
    [DataMember]
    public bool doTrend;
    [DataMember]
    public bool tradeByDirection = true;
    [DataMember]
    public bool closeAllOnTrade = false;
    [DataMember]
    public bool setLimitOrder = true;
    [DataMember]
    public double closeTradeFibRatio = .5;
    [DataMember]
    public bool closeOnProfitOnly;
    [DataMember]
    public bool closeOnNet = false;
    [DataMember]
    public int closeIfProfitTradesMoreThen = 2;
    [DataMember]
    public int closeProfitTradesMaximum = -1;
    [DataMember]
    public bool? tradeByVolatilityMaximum = null;
    [DataMember]
    public double tradeAngleMax = 0.0001;
    [DataMember]
    public double tradeAngleMin = -0.00001;
    [DataMember]
    public double tradeByFractalCoeff = 1;
    [DataMember]
    public int rsiPeriod = 14;
    [DataMember]
    public int rsiBar = 3;
    [DataMember]
    public int rsiTresholdBuy = 30;
    [DataMember]
    public int rsiTresholdSell = 70;
    [DataMember]
    public int rsiProfit = 0;
    [DataMember]
    public bool tradeByRsi = false;
    [DataMember]
    public bool? closeOnCorridorBorder = null;
    [DataMember]
    public int tradeOnProfitAfter = 0;

    [DataMember]
    public DateTime serverTime;
    [DataMember]
    public Trade tradeAdded;
    [DataMember]
    public int BuyPositions;
    [DataMember]
    public int SellPositions;
    [DataMember]
    public double BuyNetPLPip;
    [DataMember]
    public double SellNetPLPip;
    [DataMember]
    public double BuyAvgOpen;
    [DataMember]
    public double SellAvgOpen;
    [DataMember]
    public Trade[] tradesBuy = new Trade[] { };
    [DataMember]
    public Trade[] tradesSell = new Trade[] { };

    public void Update(TradeRequest tr) {
      foreach (var field in GetType().GetFields().Where(f => !f.FieldType.IsArray))
        field.SetValue(this, field.GetValue(tr));
    }
    #region IEquatable<TradeRequest> Members
    public bool Equals(TradeRequest other) {
      return guid == other.guid;
      return other == null ? false :
        this.pair == other.pair &&
        this.highBarMinutes == other.highBarMinutes &&
      this.closeOppositeOffset == other.closeOppositeOffset &&
      this.sellOnProfitLast == other.sellOnProfitLast &&
      this.shortStack == other.shortStack &&
      this.shortStackTruncateOffset == other.shortStackTruncateOffset &&
      this.corridorMinites == other.corridorMinites &&
      this.corridorSmoothSeconds == other.corridorSmoothSeconds &&
      this.goTradeFooBuy == other.goTradeFooBuy &&
      this.goTradeFooSell == other.goTradeFooSell &&
      this.densityFoo == other.densityFoo &&
      this.lotsToTradeFooBuy == other.lotsToTradeFooBuy &&
      this.lotsToTradeFooSell == other.lotsToTradeFooSell &&
      this.DecisionFoo == other.DecisionFoo &&
      this.profitMin == other.profitMin &&
      this.doTrend == other.doTrend &&
      this.tradeByDirection == other.tradeByDirection &&
      this.closeAllOnTrade == other.closeAllOnTrade &&
      this.setLimitOrder == other.setLimitOrder &&
      this.closeTradeFibRatio == other.closeTradeFibRatio &&
      this.closeOnProfitOnly == other.closeOnProfitOnly &&
      this.closeOnNet == other.closeOnNet &&
      this.closeIfProfitTradesMoreThen == other.closeIfProfitTradesMoreThen &&
      this.closeProfitTradesMaximum == other.closeProfitTradesMaximum &&
      this.tradeByVolatilityMaximum == other.tradeByVolatilityMaximum &&
      this.tradeAngleMax == other.tradeAngleMax &&
      this.tradeAngleMin == other.tradeAngleMin &&
      this.tradeByFractalCoeff == other.tradeByFractalCoeff &&
      this.rsiPeriod == other.rsiPeriod &&
      this.rsiBar == other.rsiBar &&
      this.rsiTresholdBuy == other.rsiTresholdBuy &&
      this.rsiTresholdSell == other.rsiTresholdSell &&
      this.rsiProfit == other.rsiProfit &&
      this.tradeByRsi == other.tradeByRsi &&
      this.closeOnCorridorBorder == other.closeOnCorridorBorder &&
      this.tradeOnProfitAfter == other.tradeOnProfitAfter;
    }
    public override bool Equals(object obj) {
      if (obj is TradeRequest) return Equals(obj as TradeRequest);
      return false;
    }
    public override int GetHashCode() {
      return guid.GetHashCode();
      return highBarMinutes ^
        pair.GetHashCode() ^
          closeOppositeOffset.GetHashCode() ^
          sellOnProfitLast.GetHashCode() ^
          shortStack ^
          shortStackTruncateOffset ^
          corridorMinites ^
          corridorSmoothSeconds ^
          goTradeFooBuy ^
          goTradeFooSell ^
          densityFoo ^
          lotsToTradeFooBuy ^
          lotsToTradeFooSell ^
          DecisionFoo ^
          profitMin.GetHashCode() ^
          doTrend.GetHashCode() ^
          tradeByDirection.GetHashCode() ^
          closeAllOnTrade.GetHashCode() ^
          setLimitOrder.GetHashCode() ^
          closeTradeFibRatio.GetHashCode() ^
          closeOnProfitOnly.GetHashCode() ^
          closeOnNet.GetHashCode() ^
      closeIfProfitTradesMoreThen ^
      closeProfitTradesMaximum ^
      tradeByVolatilityMaximum.GetHashCode() ^
      tradeAngleMax.GetHashCode() ^
      tradeAngleMin.GetHashCode() ^
      tradeByFractalCoeff.GetHashCode() ^
      rsiPeriod ^
      rsiBar ^
      rsiTresholdBuy ^
      rsiTresholdSell ^
      rsiProfit ^
      tradeByRsi.GetHashCode() ^
      closeOnCorridorBorder.GetHashCode() ^
      tradeOnProfitAfter;
    }
    public static bool operator ==(TradeRequest tr1, TradeRequest tr2) {
      return (object)tr1 == null && (object)tr2 == null
        ? true : (object)tr2 == null ? false : tr1.Equals(tr2);
    }
    public static bool operator !=(TradeRequest tr1, TradeRequest tr2) { return !(tr1 == tr2); }
    #endregion
  }
  [Serializable]
  [DataContract]
  public class TradeStatistics {
    [DataMember]
    public double positionBuy;
    [DataMember]
    public double positionSell;
    [DataMember]
    public double spreadAverage;
    [DataMember]
    public double spreadAverage5Min;
    [DataMember]
    public double spreadAverage10Min;
    [DataMember]
    public double spreadAverage15Min;
    [DataMember]
    public double spreadAverageHighMin;
    [DataMember]
    public double voltPriceMax;
    [DataMember]
    public double voltPriceMin;
    [DataMember]
    public double voltsAverage;
    [DataMember]
    public double peakVolts;
    [DataMember]
    public double valleyVolts;
    [DataMember]
    public double ticksPerMinuteCurr;
    [DataMember]
    public double ticksPerMinutePrev;
    [DataMember]
    public double ticksPerMinuteLong;
    [DataMember]
    public double legUpInPips;
    [DataMember]
    public double legDownInPips;
    [DataMember]
    public double corridorMinimum;
    [DataMember]
    public double corridorSpread;
    [DataMember]
    public int timeFrame;
    [DataMember]
    public double peakPriceHigh;
    [DataMember]
    public double peakPriceHighAvg;
    [DataMember]
    public double valleyPriceLow;
    [DataMember]
    public double valleyPriceLowAvg;
    public double CorridorSpreadAvg { get { return peakPriceHighAvg - valleyPriceLowAvg; } }
    [DataMember]
    public double Angle;
  }
}