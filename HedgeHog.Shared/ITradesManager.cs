using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Subjects;

namespace HedgeHog.Shared {
  public delegate void LoginErrorHandler(Exception exc);
  public class LoggedInEventArgs :EventArgs {
    public bool IsInVirtualTrading { get; set; }
    public LoggedInEventArgs(bool isInVirtualTrading) {
      IsInVirtualTrading = isInVirtualTrading;
    }
  }
  public class RateLoadingCallbackArgs<TBar> where TBar : Rate {
    public bool IsProcessed { get; set; }
    public object Message { get; set; }
    public ICollection<TBar> NewRates { get; set; }
    public RateLoadingCallbackArgs(object message, ICollection<TBar> newBars) {
      this.Message = message;
      this.NewRates = newBars;
    }
  }

  public interface ICoreFX :INotifyPropertyChanged {
    bool LogOn(string user, string accountSubId, string password, bool isDemo);
    void Logout();
    bool ReLogin();
    void SetSymbolSubscription(string pair, Action callback);

    event EventHandler<LoggedInEventArgs> LoggedIn;
    event LoginErrorHandler LoginError;
    event EventHandler<LoggedInEventArgs> LoggedOff;
    event EventHandler<LoggedInEventArgs> LoggingOff;

    bool IsInVirtualTrading { get; set; }
    bool IsLoggedIn { get; }
    TradingServerSessionStatus SessionStatus { get; set; }
    DateTime ServerTime { get; }
  }
  public interface IPricer {
    event EventHandler<PriceChangedEventArgs> PriceChanged;
    IConnectableObservable<PriceChangedEventArgs> PriceChangedObservable { get; }
  }
  public interface ITradesManager :IPricer {
    ICoreFX CoreFX { get; set; }
    bool IsLoggedIn { get; }
    bool IsInTest { get; set; }
    bool IsHedged { get; }

    #region Common Info
    DateTime ServerTime { get; }
    void SetServerTime(DateTime serverTime);
    double Leverage(string pair, bool isBuy);
    double PipsToMarginCall { get; }

    double Round(string pair, double value, int digitOffset = 0);
    double InPips(string pair, double? price);
    double InPoints(string pair, double? price);
    double GetPipSize(string pair);
    //int GetMultiplier(string pair);
    int GetDigits(string pair);
    //double GetPipCost(string pair);
    int GetBaseUnitSize(string pair);
    double GetMinTick(string pair);
    int GetContractSize(string pair);
    #endregion

    #region Offers
    Offer[] GetOffers();
    Offer GetOffer(string pair);
    Price GetPrice(string pair);
    bool TryGetPrice(string pair, out Price price);
    IEnumerable<Price> TryGetPrice(string pair);
    #endregion

    #region Money
    double RateForPipAmount(Price price);
    double RateForPipAmount(double ask, double bid);
    #endregion

    #region Trades
    Trade TradeFactory(string pair);
    Trade[] GetTradesInternal(string Pair);
    Trade[] GetTrades(string pair);
    IList<Trade> GetTrades();
    Trade GetLastTrade(string pair);
    IList<Trade> GetClosedTrades(string pair);
    void SetClosedTrades(IEnumerable<Trade> trades);
    void ResetClosedTrades(string pair);


    PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price);
    PendingOrder OpenTrade(string pair, bool isBuy, int lot, double takeProfit, double stopLoss, double rate, string comment);

    void CloseAllTrades();
    void CloseTrade(Trade trade);
    bool CloseTrade(Trade trade, int lot, Price price);
    bool ClosePair(string pair, bool isBuy, int lot,Price price);
    bool ClosePair(string pair, bool isBuy, Price price);
    bool ClosePair(string pair, Price price);

    void CloseTradeAsync(Trade trade);
    void CloseTradesAsync(Trade[] trades);

    void ChangeEntryOrderPeggedStop(string orderId, double rate);
    object FixOrderClose(string tradeId, int mode, Price price, int lot);
    object FixOrderClose(string tradeId);
    object[] FixOrdersClose(params string[] tradeIds);
    bool DeleteOrder(string orderId);

    string CreateEntryOrder(string pair, bool isBuy, int amount, double rate, double stop, double limit);
    PendingOrder FixCreateLimit(string tradeId, double limit, string remark);
    void FixOrderSetStop(string tradeId, double stopLoss, string remark);
    #endregion

    #region Events
    IConnectableObservable<OrderEventArgs> OrderAddedObservable { get; }
    IObservable<Order> OrderRemovedObservable { get; }
    event EventHandler<OrderEventArgs> OrderAdded;
    event EventHandler<TradeEventArgs> TradeClosed;
    void RaisePriceChanged(Price price);

    event EventHandler<TradeEventArgs> TradeAdded;
    event EventHandler<TradeEventArgs> TradeChanged;
    event EventHandler<TradeEventArgs> TradeRemoved;
    event EventHandler<ErrorEventArgs> Error;
    event EventHandler<RequestEventArgs> RequestFailed;
    event OrderRemovedEventHandler OrderRemoved;
    event EventHandler<OrderEventArgs> OrderChanged;
    #endregion

    #region Orders
    Order[] GetOrders(string pair);
    IList<(string status, double filled, double remaining, bool isDone)> GetOrderStatuses(string pair = "");

    void DeleteEntryOrderStop(string orderId);
    void DeleteEntryOrderLimit(string orderId);
    void ChangeOrderRate(Order order, double rate);
    void ChangeOrderAmount(string orderId, int lot);
    void ChangeEntryOrderPeggedLimit(string orderId, double rate);
    void RefreshOrders();
    void FixOrderSetLimit(string tradeId, double takeProfit, string remark);
    void DeleteOrders(string pair);
    double GetNetOrderRate(string pair, bool isStop, bool getFromInternal = false);
    void ChangeEntryOrderLot(string orderId, int lot);
    void ChangeEntryOrderRate(string orderId, double rate);
    Order GetNetLimitOrder(Trade trade, bool getFromInternal = false);
    #endregion

    #region Account
    Account GetAccount();
    //Account GetAccount(bool includeOtherInfo);
    #endregion

    void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate,bool? isFast,bool? useRTH, List<Rate> Bars, Action<RateLoadingCallbackArgs<Rate>> callBack, bool doTrim, Func<List<Rate>, List<Rate>> map);
    void GetBarsBase<TBar>(string pair, int period, int periodsBack, DateTime startDate, DateTime endDate,bool? isFast, bool? useRTH, List<TBar> ticks, Func<List<TBar>, List<TBar>> map, Action<RateLoadingCallbackArgs<TBar>> callBack = null) where TBar : Rate, new();
    Func<Trade, double> CommissionByTrade { get; }
    bool HasTicks { get; }

    IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate);

    Tick[] GetTicks(string pair, int periodsBack, Func<List<Tick>, List<Tick>> map);

    void GetBars(string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate,bool? isFast, bool? useRTH, List<Rate> ratesList, bool doTrim, Func<List<Rate>, List<Rate>> map);

    void FetchMMRs();
  }
  public class ErrorEventArgs :EventArgs {
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
  public class RequestEventArgs :EventArgs {
    public string RequestId { get; set; }
    public string Error { get; set; }
    public RequestEventArgs(string requestId) : this(requestId, "") { }
    public RequestEventArgs(string requestId, string error) {
      this.RequestId = requestId;
      this.Error = error;
    }
  }
  public enum TradingServerSessionStatus {
    Disconnected, Connecting, Connected, Reconnecting, Disconnecting
  }
}
