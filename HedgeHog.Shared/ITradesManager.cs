using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Reactive.Concurrency;
using System.Diagnostics;
using System.Threading;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows;
using System.Text.RegularExpressions;
using System.ComponentModel;

namespace HedgeHog.Shared {
  public delegate void LoginErrorHandler(Exception exc);
  public class LoggedInEventArgs : EventArgs {
    public bool IsInVirtualTrading { get; set; }
    public LoggedInEventArgs(bool isInVirtualTrading) {
      IsInVirtualTrading = isInVirtualTrading;
    }
  }

  public interface ICoreFX: INotifyPropertyChanged {
    bool LogOn(string user, string accountSubId, string password, bool isDemo);
    void Logout();
    bool ReLogin();
    void SetOfferSubscription(string pair);

    event EventHandler<LoggedInEventArgs>  LoggedIn;
    event LoginErrorHandler LoginError;
    event EventHandler<LoggedInEventArgs> LoggedOff;
    event EventHandler<LoggedInEventArgs> LoggingOff;

    bool IsInVirtualTrading { get; set; }
    bool IsLoggedIn { get; }
    TradingServerSessionStatus SessionStatus { get; set; }
    DateTime ServerTime { get; }
  }
  public interface ITradesManager {
    bool IsLoggedIn { get; }
    bool IsInTest { get; set; }
    bool IsHedged { get; }

    #region Common Info
    DateTime ServerTime { get; }
    void SetServerTime(DateTime serverTime);
    double Leverage(string pair);
    double PipsToMarginCall { get; }

    double Round(string pair, double value, int digitOffset = 0);
    double InPips(string pair, double? price);
    double InPoints(string pair, double? price);
    double GetPipSize(string pair);
    int GetDigits(string pair);
    double GetPipCost(string pair);
    int GetBaseUnitSize(string pair);
    #endregion

    #region Offers
    Offer[] GetOffers();
    Offer GetOffer(string pair);
    Price GetPrice(string pair);
    #endregion

    #region Money
    double RateForPipAmount(Price price);
    double RateForPipAmount(double ask, double bid);
    #endregion

    #region Trades
    Trade TradeFactory(string pair);
    Trade[] GetTradesInternal(string Pair);
    Trade[] GetTrades(string pair);
    Trade[] GetTrades();
    Trade GetLastTrade(string pair);
    Trade[] GetClosedTrades(string pair);
    void ResetClosedTrades(string pair);


    PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price);

    void CloseAllTrades();
    void CloseTrade(Trade trade);
    bool CloseTrade(Trade trade, int lot, Price price);
    bool ClosePair(string pair, bool isBuy, int lot);
    bool ClosePair(string pair, bool isBuy);
    bool ClosePair(string pair);

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
    event EventHandler<OrderEventArgs> OrderAdded;
    event EventHandler<TradeEventArgs> TradeClosed;
    event EventHandler<PriceChangedEventArgs> PriceChanged;
    void RaisePriceChanged(string pair, Price price);
    void RaisePriceChanged(string pair, int barPeriod, Price price);

    event EventHandler<TradeEventArgs> TradeAdded;
    event TradeRemovedEventHandler TradeRemoved;
    event EventHandler<ErrorEventArgs> Error;
    event EventHandler<RequestEventArgs> RequestFailed;
    event OrderRemovedEventHandler OrderRemoved;
    event EventHandler<OrderEventArgs> OrderChanged;
    #endregion

    #region Orders
    Order[] GetOrders(string pair);
    void DeleteEntryOrderStop(string orderId);
    void DeleteEntryOrderLimit(string orderId);
    void ChangeOrderRate(Order order, double rate);
    void ChangeOrderAmount(string orderId, int lot);
    void ChangeEntryOrderPeggedLimit(string orderId, double rate);
    void RefreshOrders();
    void FixOrderSetLimit(string tradeId, double takeProfit, string remark);
    #endregion

    #region Account
    Account GetAccount();
    //Account GetAccount(bool includeOtherInfo);
    #endregion

    Func<Trade, double> CommissionByTrade { get; }
    double CommissionByTrades(params Trade[] trades);
    IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate);

    Tick[] GetTicks(string pair, int periodsBack,Func<List<Tick>,List<Tick>> map);

    void GetBars(string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList, bool doTrim, Func<List<Rate>, List<Rate>> map);

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
    private static readonly EventLoopScheduler _tradingThread =
      new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; });

    public static EventLoopScheduler TradingScheduler { get { return _tradingThread; } }

    private static object syncRoot = new Object();
    private static volatile ISubject<Action> _tradingSubject;
    public static ISubject<Action> TradingSubject {
      get {
        if (_tradingSubject == null)
          lock (syncRoot)
            if (_tradingSubject == null) {
              _tradingSubject = new Subject<Action>();
              _tradingSubject.ObserveOn(TradesManagerStatic.TradingScheduler).Subscribe(a => a()
                , exc => {
                  LogMessage.Send(exc);
                  Debug.Fail("TradingSubject stopped working.", exc + "");
                });
            }
        return _tradingSubject;
      }
    }


    public static DateTime GetVirtualServerTime(this IList<Rate> rates, int barMinutes) {
      if (rates == null || rates.Count == 0) return DateTime.MinValue;
      return rates.Last().StartDate;
    }
    public static readonly DateTime FX_DATE_NOW = DateTime.FromOADate(0);
    public static int GetLotSize(double lot, int baseUnitSize, bool useCeiling) {
      return (lot / baseUnitSize).ToInt(useCeiling) * baseUnitSize;
    }
    public static int GetLotSize(double lot, int baseUnitSize) {
      return GetLotSize(lot, baseUnitSize, false);
    }
    public static int GetLotstoTrade(double balance, double leverage, double tradeRatio, int baseUnitSize) {
      var amountToTrade = balance * leverage * tradeRatio;
      return GetLotSize(amountToTrade, baseUnitSize);
    }
    public static double MoneyAndLotToPips(this ITradesManager tm, double money, int lots, string pair) {
      return tm == null ? double.NaN : MoneyAndLotToPips(pair, money, lots,tm.RateForPipAmount(tm.GetPrice(pair)), tm.GetPipSize(pair));
    }
    public static double MarginRequired(int lot, double baseUnitSize, double mmr) {
      return lot / baseUnitSize * mmr;
    }
    public static string AccountCurrency = null;
    static string[] PairCurrencies(string pair) {
      var ret = Regex.Matches(Regex.Replace(pair, "[^a-z]", "", RegexOptions.IgnoreCase), @"\w{3}")
        .Cast<Match>().Select(m => m.Value.ToUpper()).ToArray();
      if (ret.Length != 2) throw new ArgumentException(new { pair, error = "Wrong format" } + "");
      return ret;
    }
    public static double PipByPair(string pair, Func<double> left, Func<double> right) {
      if (string.IsNullOrEmpty(AccountCurrency)) throw new ArgumentNullException(new { AccountCurrency } + "");
      Func<double> error = () => { throw new NotSupportedException(new { pair, error = "Not Supported" } + ""); };
      var acc = AccountCurrency.ToUpper();
      var foos = new[] { 
        new { acc, a = left} ,
        new { acc, a = right}
      };
      return PairCurrencies(pair)
        .Zip(foos, (c, f) => new { ok = c == f.acc, f.a })
        .Where(a => a.ok)
        .Select(a => a.a)
        .DefaultIfEmpty(() => error)
        .First()();
    }
    #region PipAmount
    /// <summary>
    /// Pip Dollar Value
    /// </summary>
    /// <param name="lot"></param>
    /// <param name="rate">Usually Ask or (Ask+Bid)/2</param>
    /// <param name="pipSize">EUR/USD:0.0001, USD/JPY:0.01</param>
    /// <returns></returns>
    public static double PipAmount(string pair, int lot, double rate, double pipSize) {
      return PipsAndLotToMoney(pair, 1, lot, rate, pipSize);
    }
    #endregion
    public static double PipsAndLotToMoney(string pair, double pips, int lot, double rate, double pipSize) {
      var pl = pips * lot;
      return PipByPair(pair,
        () => pl * pipSize / rate,
        () => pl / 10000);
    }
    public static double MoneyAndLotToPips(string pair, double money, int lot, double rate, double pipSize) {
      if (money == 0 || lot == 0) return 0;
      var ml = money / lot;
      return PipByPair(pair,
        () => ml * rate /  pipSize,
        () => ml * 10000);
    }
    #region PipCost
    public static double PipCost(string pair, double rate, int baseUnit, double pipSize) {
      return PipByPair(pair,
        () => baseUnit * pipSize / rate,
        () => baseUnit / 10000.0);
    }
    #endregion
    public static double InPoins(ITradesManager tm, string pair, double? price) {
      return (price * tm.GetPipSize(pair)).GetValueOrDefault();
    }
    public static double MarginLeft2(int lot, double pl, double balance, double mmr, double baseUnitSize, double pipAmount) {
      return balance - MarginRequired(lot, baseUnitSize, mmr) + pl * pipAmount;
    }
    public static double PipToMarginCall(int lot, double pl, double balance, double mmr, double baseUnitSize, double pipAmount) {
      return MarginLeft2(lot, pl, balance, mmr, baseUnitSize, pipAmount) / pipAmount;
    }
    public static int LotToMarginCall(int pipsToMC, double balance, int baseUnitSize, double pipCost, double MMR) {
      var lot = balance / (pipsToMC * pipCost / baseUnitSize + 1.0 / baseUnitSize * MMR);
      return pipsToMC < 1 ? 0 : GetLotSize(lot, baseUnitSize);
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
