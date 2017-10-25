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
using HedgeHog;
using AutoMapper;

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
    public string Message { get; set; }
    public ICollection<TBar> NewRates { get; set; }
    public RateLoadingCallbackArgs(string message, ICollection<TBar> newBars) {
      this.Message = message;
      this.NewRates = newBars;
    }
  }

  public interface ICoreFX :INotifyPropertyChanged {
    bool LogOn(string user, string accountSubId, string password, bool isDemo);
    void Logout();
    bool ReLogin();
    void SetOfferSubscription(string pair);

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
    int GetDigits(string pair);
    //double GetPipCost(string pair);
    int GetBaseUnitSize(string pair);
    #endregion

    #region Offers
    Offer[] GetOffers();
    Offer GetOffer(string pair);
    Price GetPrice(string pair);
    bool TryGetPrice(string pair, out Price price);
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
    void RaisePriceChanged(Price price);

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

    void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, Action<RateLoadingCallbackArgs<Rate>> callBack, bool doTrim, Func<List<Rate>, List<Rate>> map);
    void GetBarsBase<TBar>(string pair, int period, int periodsBack, DateTime startDate, DateTime endDate, List<TBar> ticks, Func<List<TBar>, List<TBar>> map, Action<RateLoadingCallbackArgs<TBar>> callBack = null) where TBar : Rate, new();
    Func<Trade, double> CommissionByTrade { get; }
    bool HasTicks { get; }

    IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate);

    Tick[] GetTicks(string pair, int periodsBack, Func<List<Tick>, List<Tick>> map);

    void GetBars(string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList, bool doTrim, Func<List<Rate>, List<Rate>> map);

    PendingOrder OpenTrade(string Pair, bool isBuy, int lot, double takeProfit, double stopLoss, double rate, string comment);
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


  public static class TradesManagerStatic {
    public static IMapper TradeMapper() => TradeMapper(opt => opt);//.ForMember(t => t.TradesManager, o => o.Ignore()));
    public static IMapper TradeMapper(Func<IMappingExpression<Trade, Trade>, IMappingExpression<Trade, Trade>> opt)
      => new MapperConfiguration(cfg => opt(cfg.CreateMap<Trade, Trade>())).CreateMapper();

    public readonly static Offer OfferDefault = new Offer { Pair = "DEFAULT", Digits = 3, PointSize = 0.01, MMRLong = 0.250, MMRShort = 0.3, ContractSize = 1 };
    public static Offer[] dbOffers = new[] {
            new Offer { Pair = "USDJPY", Digits = 3, PointSize = 0.01, MMRLong=1, ContractSize = 1000 },
            new Offer { Pair = "EURUSD", Digits = 5, PointSize = 0.0001, MMRLong=1, ContractSize = 1000 },
            new Offer { Pair = "XAUUSD", Digits = 2, PointSize = 0.01, MMRLong=0.513, ContractSize = 1 },
            new Offer { Pair = "SPY", Digits = 3, PointSize = 0.01, MMRLong = 0.250, MMRShort= 0.3, ContractSize = 1 },
            new Offer { Pair = "TVIX", Digits = 3, PointSize = 0.01, MMRLong = 1/1.14, MMRShort= 1/1.14, ContractSize = 1 },
            new Offer { Pair = "UVXY", Digits = 3, PointSize = 0.01, MMRLong = 1/1.14, MMRShort= 1/1.14, ContractSize = 1 }
          };
    static Func<string, Offer> GetOfferImpl = symbol
        => dbOffers
      .Where(o => o.Pair.ToUpper() == symbol.WrapPair())
      .Take(1)
      .DefaultIfEmpty(OfferDefault)
      .Single();
    public static Offer GetOffer(string pair) => GetOfferImpl(pair);
    public static double GetPointSize(string symbol) => GetOffer(symbol).PointSize;
    public static int GetBaseUnitSize(string symbol) => GetOffer(symbol).ContractSize;
    public static int GetDigits(string symbol) => GetOffer(symbol).Digits;
    public static double GetMMR(string symbol, bool isBuy) => isBuy ? GetOffer(symbol).MMRLong : GetOffer(symbol).MMRShort;
    public static double Leverage(string pair, double mmr) => GetBaseUnitSize(pair) / mmr;

    private static string[] _currencies = new[]{
      "USD",
      "SEK",
      "NZD",
      "JPY",
      "GBP",
      "EUR",
      "CHF",
      "CAD",
      "AUD"
    };
    private static string[] _commodities = new[]{
      "XAUUSD",
      "XAGUSD"
    };
    public static string WrapPair(this string pair) {
      return Regex.Replace(pair, "[./-]", "").ToUpper();
    }
    static string UnWrapPair(string pair) {
      if(!pair.IsNullOrEmpty())
        throw new NotImplementedException();
      return Regex.Replace(pair, @"(\w3)(\w3)", "$1/$2");
    }

    public static bool IsCurrenncy(this string s) => _currencies.Any(c => s.ToUpper().StartsWith(c)) && _currencies.Any(c => s.ToUpper().EndsWith(c));
    public static bool IsFuture(this string s) => Regex.IsMatch(s, @"^\w{2,3}[HMUZ]\d{1,2}$", RegexOptions.IgnoreCase);
    public static bool IsCommodity(this string s) => _commodities.Contains(s.ToUpper());
    public static bool IsUSStock(this string s) => !s.IsCurrenncy() && !s.IsFuture() && !s.IsCommodity();
    static string[] _etfs = new[] { "SPY", "TVIX", "VXX", "UVXY" };
    public static bool IsETF(this string s) => _etfs.Contains(s);
    private static readonly EventLoopScheduler _tradingThread =
      new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; });

    public static EventLoopScheduler TradingScheduler { get { return _tradingThread; } }

    private static object syncRoot = new Object();
    private static volatile ISubject<Action> _tradingSubject;
    public static ISubject<Action> TradingSubject {
      get {
        if(_tradingSubject == null)
          lock(syncRoot)
            if(_tradingSubject == null) {
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
      if(rates == null || rates.Count == 0)
        return DateTime.MinValue;
      return rates.Last().StartDate;
    }
    public static readonly DateTime FX_DATE_NOW = DateTime.FromOADate(0);
    public static int GetLotSize(double lot, int baseUnitSize, bool useCeiling) {
      return (lot / baseUnitSize).ToInt(useCeiling) * baseUnitSize;
    }
    public static int GetLotSize(double lot, int baseUnitSize) {
      return GetLotSize(lot, baseUnitSize, false);
    }
    public static int GetLotstoTrade(double rate, string symbol, double balance, double leverage, double tradeRatio, int baseUnitSize) {
      var amountToTrade = symbol.IsCurrenncy()
        ? balance * leverage * tradeRatio
        : rate == 0
        ? 0
        : balance * leverage / rate * tradeRatio;
      return GetLotSize(amountToTrade, baseUnitSize);
    }
    public static double MoneyAndLotToPips(this ITradesManager tm, double money, int lots, string pair) {
      return tm == null || !tm.TryGetPrice(pair, out Price price) ? double.NaN : MoneyAndLotToPips(pair, money, lots, tm.RateForPipAmount(price), tm.GetPipSize(pair));
    }
    public static double MarginRequired(int lot, double baseUnitSize, double mmr) {
      return lot / baseUnitSize * mmr;
    }
    public static string AccountCurrency = null;
    static string[] PairCurrencies(string pair) {
      var ret = Regex.Matches(Regex.Replace(pair, "[^a-z]", "", RegexOptions.IgnoreCase), @"\w{3}")
        .Cast<Match>().Select(m => m.Value.ToUpper()).ToArray();
      //if(ret.Length != 2)
      //  throw new ArgumentException(new { pair, error = "Wrong format" } + "");
      return ret;
    }
    public static double PipByPair(string pair, Func<double> left, Func<double> right, Func<double> middle) {
      if(string.IsNullOrEmpty(AccountCurrency))
        throw new ArgumentNullException(new { AccountCurrency } + "");
      Func<double> error = () => { throw new NotSupportedException(new { pair, error = "Not Supported" } + ""); };
      var acc = AccountCurrency.ToUpper();
      var foos = new[] {
        new { acc, a = left} ,
        new { acc, a = right}
      };
      var curs = PairCurrencies(pair);
      return (curs.Length > 1 && curs.All(c => c.IsCurrenncy())
        ? PairCurrencies(pair)
        .Where(cur => cur.IsCurrenncy())
        .Zip(foos, (c, f) => new { ok = c == f.acc, f.a })
        .Where(a => a.ok)
        .Select(a => a.a)
        .DefaultIfEmpty(error)
        .First()
        : middle)
        ();
    }
    #region PipAmount
    /// <summary>
    /// Pip Dollar Value
    /// </summary>
    /// <param name="pair"></param>
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
        () => pl / 10000,
        () => pl * pipSize);
    }
    public static double MoneyAndLotToPips(string pair, double money, int lot, double rate, double pipSize) {
      if(money == 0 || lot == 0 || double.IsNaN(rate))
        return 0;
      var ml = money / lot;
      return PipByPair(pair,
        () => ml * rate / pipSize,
        () => ml * 10000,
        () => ml / pipSize);
    }
    #region PipCost
    public static double PipCost(string pair, double rate, int baseUnit, double pipSize) {
      return PipByPair(pair,
        () => baseUnit * pipSize / rate,
        () => baseUnit / 10000.0,
        () => pipSize
        );
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
