using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog;

namespace HedgeHog {
  public interface IServer {
    TradeResponse Decisioner(TradeRequest tr);
  }
  public class RemoteClient {
    public static Remoter Activate(string tcpPath) { return (Remoter)Activator.GetObject(typeof(Remoter), tcpPath); }
  }
  public class Remoter : MarshalByRefObject, IServer {
    public static IServer Server;
    public TradeResponse Decisioner(TradeRequest tr) {
      if (Server == null) return new TradeResponse();
      return Server.Decisioner(tr); 
    }
  }
  [Serializable]
  public class TradeResponse {
    public int TradeSignalDelay = 2;
    public DateTime ServerTime { get; set; }
    DateTime _goBuyTime = DateTime.MinValue;
    DateTime GoBuyTime { get { return _goBuyTime; } set { _goBuyTime = value; } }
    DateTime _goSellTime = DateTime.MinValue;
    DateTime GoSellTime { get { return _goSellTime; } set { _goSellTime = value; } }
    public int TradeWaveInMinutes { get; set; }

    public bool GoBuy {
      get;
      set;
    }
    bool goBuy { 
      get { return (this.ServerTime - GoBuyTime).Duration().TotalSeconds.Between(0, TradeSignalDelay); }
      set { this.GoBuyTime = value ? this.ServerTime : DateTime.MinValue; }
    }
    public bool GoSell {
      get;
      set;
    }
    bool goSell { 
      get { return (this.ServerTime - GoSellTime).TotalSeconds.Between(0, TradeSignalDelay); }
      set { this.GoSellTime = value ? this.ServerTime : DateTime.MinValue; }
    }
    public bool CloseBuy { get; set; }
    public bool CloseSell { get; set; }
    public bool CloseLastBuy { get; set; }
    public bool CloseLastSell { get; set; }
    public string[] CloseTradeIDs { get; set; }
    public bool TrancateBuy { get; set; }
    public bool TrancateSell { get; set; }
    public bool DoTakeProfitBuy { get; set; }
    public bool DoTakeProfitSell { get; set; }
    public double LotsToTradeBuy { get; set; }
    public double LotsToTradeSell { get; set; }
    public double DencityRatio { get; set; }
    public double DencityRatioBuy { get; set; }
    public double DencityRatioSell { get; set; }
    public int RsiHigh { get; set; }
    public int RsiLow { get; set; }
    public int RsiRegressionOffsetBuy { get; set; }
    public int RsiRegressionOffsetSell { get; set; }
    public double RsiRegressionAverage { get; set; }
    bool _canTrade = true;
    public bool CanTrade { get { return _canTrade; } set { _canTrade = value; } }
    public bool CorridorOK { get; set; }
    TradeStatistics _tradeStats = new TradeStatistics();
    public bool IsReady { get; set; }
    public TradeStatistics TradeStats { get { return _tradeStats; } set { _tradeStats = value; } }

  }
  [Serializable]
  public class TradeRequest :IEquatable<TradeRequest> {
    Guid guid = Guid.NewGuid();
    public int highBarMinutes = 5;
    public int closeOppositeOffset = 0;
    public bool sellOnProfitLast = true;
    public int shortStack = 0;
    public int shortStackTruncateOffset = 2;
    public int corridorMinites = 5;
    public int corridorSmoothSeconds = 5;
    public int goTradeFooBuy = 3;
    public int goTradeFooSell = 3;
    public int densityFoo = 2;
    public int lotsToTradeFooBuy = 3;
    public int lotsToTradeFooSell = 3;
    public int DecisionFoo = 10;
    public double profitMin = -.5;
    public bool doTrend;
    public bool tradeByDirection = true;
    public bool closeAllOnTrade = false;
    public bool setLimitOrder = true;
    public double closeTradeFibRatio = .5;
    public bool closeOnProfitOnly;
    public bool closeOnNet = false;
    public int closeIfProfitTradesMoreThen = 2;
    public int closeProfitTradesMaximum = -1;
    public bool? tradeByVolatilityMaximum = null;
    public double tradeAngleMax = 0.0001;
    public double tradeAngleMin = -0.00001;
    public double tradeByFractalCoeff = 1;
    public int rsiPeriod = 14;
    public int rsiBar = 3;
    public int rsiTresholdBuy = 30;
    public int rsiTresholdSell = 70;
    public int rsiProfit = 0;
    public bool tradeByRsi = false;
    public bool? closeOnCorridorBorder = null;
    public int tradeOnProfitAfter = 0;

    public DateTime serverTime;
    public Order2GoAddIn.Trade tradeAdded;
    public int BuyPositions;
    public int SellPositions;
    public double BuyNetPLPip;
    public double SellNetPLPip;
    public Order2GoAddIn.Trade[] tradesBuy = new Order2GoAddIn.Trade[] { };
    public Order2GoAddIn.Trade[] tradesSell = new Order2GoAddIn.Trade[] { };

    public void Update(TradeRequest tr) {
      foreach (var field in GetType().GetFields().Where(f=>!f.FieldType.IsArray)) 
        field.SetValue(this, field.GetValue(tr));
    }
    #region IEquatable<TradeRequest> Members
    public bool Equals(TradeRequest other) {
      return other == null ? false : this.highBarMinutes == other.highBarMinutes &&
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
      return highBarMinutes ^
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
    public static bool operator !=(TradeRequest tr1, TradeRequest tr2) { return !(tr1==tr2); }
    #endregion
  }
  [Serializable]
  public class TradeStatistics {
    public double positionBuy;
    public double positionSell;
    public double spreadAverage;
    public double spreadAverage5Min;
    public double spreadAverage10Min;
    public double spreadAverage15Min;
    public double spreadAverageHighMin;
    public double voltPriceMax;
    public double voltPriceMin;
    public double voltsAverage;
    public double peakVolts;
    public double valleyVolts;
    public double ticksPerMinuteAverageShort;
    public double ticksPerMinuteAverageLong;
    public double corridorMinimum;
    public double corridorSpread;
    public int timeFrame;
    public double peakPriceHigh;
    public double peakPriceHighAvg;
    public double valleyPriceLow;
    public double valleyPriceLowAvg;
    public double CorridorSpreadAvg { get { return peakPriceHighAvg - valleyPriceLowAvg; } }
    public double Angle;
}
}
