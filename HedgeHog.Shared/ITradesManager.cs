using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Shared {
  public interface ITradesManager {
    bool IsLoggedIn { get; }
    bool IsInTest { get; set; }
    bool IsHedged { get; }

    #region Common Info
    DateTime ServerTime { get; }
    int MinimumQuantity { get; }
    double Leverage(string pair);

    double Round(string pair, double value,int digitOffset = 0);
    double InPips(string pair, double? price);
    double InPoints(string pair, double? price);
    double GetPipSize(string pair);
    int GetDigits(string pair);
    double GetPipCost(string pair);
    #endregion

    #region Offers
    Offer[] GetOffers();
    Price GetPrice(string pair);
    #endregion

    #region Money
    int MoneyAndPipsToLot(double Money, double pips, string pair);
    #endregion

    #region Trades
    Trade[] GetTradesInternal(string Pair);
    Trade[] GetTrades(string pair);
    Trade[] GetTrades();
    Trade GetLastTrade(string pair);

    PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price);

    void CloseTrade(Trade trade);
    bool CloseTrade(Trade trade,int lot,Price price);
    bool ClosePair(string pair, bool isBuy,int lot);
    bool ClosePair(string pair, bool isBuy);
    bool ClosePair(string pair);

    void CloseTradeAsync(Trade trade);
    void CloseTradesAsync(Trade[] trades);

    object FixOrderClose(string tradeId, int mode, Price price, int lot);
    object FixOrderClose(string tradeId);
    object[] FixOrdersClose(params string[] tradeIds);

    PendingOrder FixCreateLimit(string tradeId, double limit, string remark);
    void FixOrderSetStop(string tradeId, double stopLoss, string remark);
    #endregion

    #region Events
    event EventHandler<OrderEventArgs> OrderAdded;
    event EventHandler<TradeEventArgs> TradeClosed;
    event EventHandler<PriceChangedEventArgs> PriceChanged;
    void RaisePriceChanged(string pair, Price price);

    event EventHandler<TradeEventArgs> TradeAdded;
    event TradeRemovedEventHandler TradeRemoved;
    event EventHandler<ErrorEventArgs> Error;
    event EventHandler<RequestEventArgs> RequestFailed;
    event OrderRemovedEventHandler OrderRemoved;
    #endregion

    #region Orders
    Order[] GetOrders(string pair);
    void DeleteEntryOrderStop(string orderId);
    void DeleteEntryOrderLimit(string orderId);
    void ChangeOrderRate(Order order, double rate);
    void ChangeOrderAmount(string orderId, int lot);
    void ChangeEntryOrderPeggedLimit(string orderId, double rate);
    #endregion

    #region Account
    Account GetAccount();
    //Account GetAccount(bool includeOtherInfo);
    #endregion

    Func<Trade, double> CommissionByTrade { get; }
    double CommissionByTrades(params Trade[] trades);
    IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate);

    Tick[] GetTicks(string pair, int periodsBack);

    void GetBars(string pair, int periodMinutes,int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList,bool doTrim);

    //void GetBars(string pair, int periodMinutes, int periodsBack, DateTime endDate, List<Rate> ratesList);
    void Replay(string pair, int period, DateTimeOffset dateStart, DateTimeOffset dateEnd);

    PendingOrder OpenTrade(string Pair, bool isBuy, int lot, double takeProfit, double stopLoss, double rate, string comment);
  }
  public class ErrorEventArgs : EventArgs {
    public string Pair { get; set; }
    public bool IsBuy { get; set; }
    public int Lot { get; set; }
    public double Stop { get; set; }
    public double Limit { get; set; }
    public string Remark { get; set; }
    public Exception Error { get; set; }
    public ErrorEventArgs(Exception error) {
      this.Error = error;
    }
    public ErrorEventArgs(Exception error, string pair, bool isBuy, int lot, double stop, double limit, string remark)
      : this(error) {
      this.Pair = pair;
      this.IsBuy = isBuy;
      this.Lot = lot;
      this.Stop = stop;
      this.Limit = limit;
      this.Remark = remark;
    }
  }
  public class RequestEventArgs : EventArgs {
    public string RequestId { get; set; }
    public string Error { get; set; }
    public RequestEventArgs(string requestId) : this(requestId, "") { }
    public RequestEventArgs(string requestId, string error) {
      this.RequestId = requestId;
      this.Error = error;
    }
  }


  public static class TradesManagerStatic {

    public static void RaisePriceChanged(this ITradesManager me, string pair, Rate rate) {
      me.RaisePriceChanged(pair, new Price(pair, rate, me.ServerTime, me.GetPipSize(pair), me.GetDigits(pair), true));
    }

    public static readonly DateTime FX_DATE_NOW = DateTime.FromOADate(0);
    public static int GetLotSize(double amountToTrade, int baseUnitSize) {
      return Math.Floor((amountToTrade / baseUnitSize)).ToInt() * baseUnitSize;
    }
    public static int GetLotstoTrade(double balance, double leverage, double tradeRatio, int baseUnitSize) {
      var amountToTrade = balance * leverage * tradeRatio;
      return GetLotSize(amountToTrade, baseUnitSize);
    }
    public static int MoneyAndPipsToLot(double Money, double pips, double PipCost, int BaseUnitSize) {
      return GetLotSize(Money / pips / PipCost * BaseUnitSize, BaseUnitSize);
    }
    public static double MoneyAndLotToPips(this ITradesManager tm, double money, double lots, string pair) {
      return tm == null ? double.NaN: MoneyAndLotToPips(money, lots, tm.GetPipCost(pair), tm.MinimumQuantity);
    }
    public static double MoneyAndLotToPips(double money, double lots, double pipCost, double baseUnitSize) {
      return money / lots / pipCost * baseUnitSize;
    }
    public static double InPoins(ITradesManager tm, string pair, double? price) {
      return (price * tm.GetPipSize(pair)).GetValueOrDefault();
    }
    public static double InPips(double? price, double pipSize) { return price.GetValueOrDefault() / pipSize; }
    public static bool IsInPips(this double value, double curentPrice) { return value / curentPrice < .5; }
    public static int GetMaxBarCount(int periodsBack, DateTime StartDate, List<Rate> Bars) {
      return Math.Max(StartDate == TradesManagerStatic.FX_DATE_NOW ? 0 : Bars.Count(b => b.StartDate >= StartDate), periodsBack);
    }
  }
  public enum TradingServerSessionStatus {
    Disconnected, Connecting, Connected, Reconnecting, Disconnecting
  }
}
