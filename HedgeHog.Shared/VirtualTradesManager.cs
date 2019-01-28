using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using HedgeHog.Bars;
using System.Diagnostics;
using ReactiveUI;
using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using ReactiveUI.Legacy;

namespace HedgeHog.Shared {
  public class VirtualTradesManager :ITradesManager {
    const string TRADE_ID_FORMAT = "yyMMddhhmmssffff";
    IDictionary<string, int> _baseUnits = null;
    ObservableCollection<Offer> _offersCollection;
    ObservableCollection<Offer> offersCollection {
      get {
        if(_offersCollection == null || _baseUnits == null) {
          var dbOffers = TradesManagerStatic.dbOffers;
          //  offers.Select(o => new Offer() {
          //  Pair = o.Pair, Digits = o.Digits, MMR = o.MMR, PipCost = o.PipCost, PointSize = o.PipSize, ContractSize = o.BaseUnitSize
          //}).ToArray();
          _offersCollection = new ObservableCollection<Offer>(dbOffers);
          _baseUnits = dbOffers.ToArray().ToDictionary(o => o.Pair, o => o.ContractSize);
        }
        return _offersCollection;
      }
    }
    public void SetHasTicks(bool hasTicks) => HasTicks = hasTicks;
    public bool HasTicks { get; private set; }
    double PipsToMarginCallCore(Account account) {
      var trades = GetTrades();
      if(!trades.Any())
        return int.MaxValue;
      var pair = trades[0].Pair;
      var offer = GetOffer(pair);
      return trades.Sum(trade =>
        TradesManagerStatic.PipToMarginCall(
        trade.Lots,
        trade.PL,
        account.Balance,
        trade.IsBuy ? offer.MMRLong : offer.MMRShort,
        GetBaseUnitSize(trade.Pair),
        TradesManagerStatic.PipAmount(trade.Pair, trade.Lots, trade.Close, GetPipSize(trade.Pair))
      // CommissionByTrade(trade)
      ) * trade.Lots) / trades.Lots();
    }
    public double PipsToMarginCall {
      get {
        return PipsToMarginCallCore(GetAccount(true));
      }
    }
    IDictionary<string, int> baseUnits {
      get {
        if(_baseUnits == null) {
          var o = offersCollection;
        }
        return _baseUnits;
      }
    }
    ObservableCollection<Trade> tradesOpened = new ObservableCollection<Trade>();
    ObservableCollection<Trade> tradesClosed = new ObservableCollection<Trade>();
    ObservableCollection<Order> ordersOpened = new ObservableCollection<Order>();
    int barMinutes;
    public int BarMinutes {
      get { return barMinutes; }
      set { barMinutes = value; }
    }
    public Func<Dictionary<string, ReactiveList<Rate>>> RatesByPair;

    Dictionary<string, double> _pipSizeDictionary = new Dictionary<string, double>();
    public double GetPipSize(string pair) {
      if(!_pipSizeDictionary.ContainsKey(pair))
        _pipSizeDictionary.Add(pair, GetOffer(pair).PointSize);
      return _pipSizeDictionary[pair];
    }
    public int GetDigits(string pair) { return GetOffer(pair).Digits; }

    //IDictionary<string, double> _pipCostDictionary = new Dictionary<string, double>();
    //public double GetPipCost(string pair) {
    //  if (!_pipCostDictionary.ContainsKey(pair)) {
    //    _pipCostDictionary.Add(pair, GetOffer(pair).PipCost);
    //  }
    //  return _pipCostDictionary[pair];
    //}

    public int GetBaseUnitSize(string pair) { return baseUnits.TryGetValue(pair, out var bu) ? bu : 1; }

    public Func<Trade, double> CommissionByTrade { get; set; }

    public bool IsLoggedIn { get { return true; } }
    public double Leverage(string pair, bool isBuy) { return (double)GetBaseUnitSize(pair) / TradesManagerStatic.GetMMR(pair, isBuy); }
    DateTime _serverTime;
    public DateTime ServerTime {
      get {
        //if (_serverTime.IsMin()) throw new Exception(new { VirtualTradesManager = new { _serverTime } } + "");
        return _serverTime;
      }
    }
    public void SetServerTime(DateTime serverTime) {
      _serverTime = serverTime;
    }
    #region Money
    public double RateForPipAmount(Price price) { return RateForPipAmount(price.Ask, price.Bid); }
    public double RateForPipAmount(double ask, double bid) { return ask.Avg(bid); }

    #endregion

    #region Account
    string accountId;
    Account _account;
    bool isHedged;

    public bool IsHedged {
      get { return isHedged; }
      set { isHedged = value; }
    }

    public void SetInitialBalance(int balance) {
      Account.Balance = Account.UsableMargin = Account.Equity = balance;
    }
    public Account Account {
      get {
        if(_account == null) {
          _account = new Account() {
            ID = accountId,
            Balance = 50000,
            UsableMargin = 50000,
            IsMarginCall = false,
            Equity = 50000,
            Hedging = false,
            //Trades = includeOtherInfo ? trades = GetTrades("") : null,
            //StopAmount = includeOtherInfo ? trades.Sum(t => t.StopAmount) : 0,
            //LimitAmount = includeOtherInfo ? trades.Sum(t => t.LimitAmount) : 0,
            //ServerTime = ServerTime
          };
          IsHedged = _account.Hedging;
        }
        return _account;
      }
    }
    public Account GetAccount() { return GetAccount(true); }
    public Account GetAccount(bool includeOtherInfo) {
      if(includeOtherInfo) {
        var trades = GetTrades();
        Account.Trades = trades;
        if(trades.Any())
          Account.UsableMargin = Account.Equity - TradesManagerStatic.MarginRequired(trades.Lots(), GetBaseUnitSize(trades[0].Pair), TradesManagerStatic.GetMMR(trades[0].Pair, trades[0].IsBuy));
        Account.StopAmount = includeOtherInfo ? trades.Sum(t => t.StopAmount) : 0;
        Account.LimitAmount = includeOtherInfo ? trades.Sum(t => t.LimitAmount) : 0;
        Account.PipsToMC = PipsToMarginCallCore(Account).ToInt();
      }
      return Account;
    }
    #endregion

    static long tradeId = 0;
    static VirtualTradesManager() {
      _orderAddedSubject = new Subject<OrderEventArgs>();
    }
    public VirtualTradesManager(string accountId, Func<Trade, double> commissionByTrade) {
      OrderAddedObservable = _orderAddedSubject.Replay(TimeSpan.FromMinutes(1));
      this.accountId = accountId;
      this.tradesOpened.CollectionChanged += VirualPortfolio_CollectionChanged;
      this.CommissionByTrade = commissionByTrade;
    }
    ~VirtualTradesManager() {
      this.tradesOpened.CollectionChanged -= VirualPortfolio_CollectionChanged;
    }
    public double Round(string pair, double value, int digitOffset = 0) { return Math.Round(value, GetDigits(pair) + digitOffset); }
    public double InPips(string pair, double? price) { return TradesManagerStatic.InPips(price, GetPipSize(pair)); }
    public double InPoints(string pair, double? price) { return TradesManagerStatic.InPoins(this, pair, price); }
    static long NewTradeId() {
      if(tradeId == 0)
        tradeId = DateTime.Now.Ticks / 10000;
      return ++tradeId;
    }
    void AddTrade(bool isBuy, int lot, Price price) {
      if(PriceCurrent == null)
        throw new NullReferenceException("PriceCurrent");
      //if (tradesOpened.Count > 0) Debugger.Break();
      var trade = TradeFactory(price.Pair);
      {
        trade.Id = NewTradeId() + "";
        trade.Pair = price.Pair;
        trade.Buy = isBuy;
        trade.IsBuy = isBuy;
        trade.Lots = lot;
        trade.Open = isBuy ? price.Ask : price.Bid;
        trade.Close = isBuy ? price.Bid : price.Ask;
        trade.Time2 = price.Time2;
        trade.Time2Close = price.Time2;
        trade.IsVirtual = true;
      };
      tradesOpened.Add(trade);
    }

    public Trade TradeFactory(string pair) {
      return Trade.Create(this, pair, GetPipSize(pair), GetBaseUnitSize(pair), CommissionByTrade);
    }

    #region Close Trade
    public void CloseAllTrades() {
      tradesOpened.ToList().ForEach(CloseTrade);
    }
    public void CloseTradeAsync(Trade trade) {
      CloseTrade(trade);
    }
    public void CloseTradesAsync(Trade[] trades) {
      CloseTrades(trades);
    }

    private void CloseTrades(Trade[] trades) {
      foreach(var trade in trades)
        CloseTrade(trade);
    }
    public bool ClosePair(string pair) {
      CloseTrades(tradesOpened.Where(t => t.Pair == pair).ToArray());
      return true;
    }
    public bool ClosePair(string pair, bool isBuy, int lot) {
      try {
        foreach(var trade in tradesOpened.Where(t => t.Pair == pair).ToArray()) {
          CloseTrade(trade, Math.Min(trade.Lots, lot), null);
          lot -= trade.Lots;
          if(lot <= 0)
            break;
        }
        return true;
      } catch {
        throw;
      }
    }
    public bool ClosePair(string pair, bool isBuy) {
      CloseTrades(tradesOpened.Where(t => t.Pair == pair && t.Buy == isBuy).ToArray());
      return true;
    }
    public void CloseTrade(Trade trade) {
      tradesClosed.Add(trade);
      TradeRemoved(this, new TradeEventArgs(trade));
      OnTradeClosed(trade);
      tradesOpened.Remove(trade);
      RaiseOrderRemoved(new Order() { Pair = trade.Pair });
    }
    public bool CloseTrade(Trade trade, int lot, Price price) {
      if(trade.Lots <= lot)
        CloseTrade(trade);
      else {
        var newTrade = trade.Clone();
        newTrade.Lots = trade.Lots - lot;
        newTrade.Id = NewTradeId() + "";
        var e = new PriceChangedEventArgs(price ?? GetPrice(trade.Pair), GetAccount(), GetTrades());
        newTrade.UpdateByPrice(this, e);
        trade.Lots = lot;
        trade.UpdateByPrice(this, e);
        CloseTrade(trade);
        tradesOpened.Add(newTrade);
      }
      return true;
    }
    #endregion

    void VirualPortfolio_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      var trade = (e.NewItems ?? e.OldItems)[0] as Trade;
      switch(e.Action) {
        case NotifyCollectionChangedAction.Add:
          OnTradeAdded(trade);
          RaiseOrderRemoved(new Order() { Pair = trade.Pair });
          break;
        case NotifyCollectionChangedAction.Reset:
        case NotifyCollectionChangedAction.Remove:
          trade.UpdateByPrice(this, GetPrice(trade.Pair));
          trade.CloseTrade();
          break;
      }
    }

    #region ITradesManager Members

    object _tradesOpenedLocker = new object();
    public Trade[] GetTrades(string pair) {
      lock(_tradesOpenedLocker) {
        return tradesOpened.ToArray().Where(PairCmp).ToArray();
      }
      bool PairCmp(Trade trade) {
        if(pair == null) throw new NullReferenceException(new { pair } + "");
        if(trade == null) throw new NullReferenceException(new { trade } + "");
        if(trade.Pair == null) throw new NullReferenceException(new { trade = new { trade.Pair } } + "");
        return trade.Pair.ToLower() == pair.ToLower();
      }
    }

    public IList<Trade> GetTrades() {
      return tradesOpened.ToArray();
    }
    public void ResetClosedTrades(string Pair) {
      tradesClosed.Clear();
    }
    public IList<Trade> GetClosedTrades(string Pair) {
      return tradesClosed.Where(t => t.Pair.ToLower() == Pair.ToLower()).ToArray();
    }
    public void SetClosedTrades(IEnumerable<Trade> trades) {
      trades.OrderBy(trade => trade.Time).ToList().ForEach(trade => tradesClosed.Add(trade));
    }
    public Trade GetLastTrade(string pair) {
      throw new NotImplementedException();
    }

    public Order[] GetOrders(string pair) {
      return ordersOpened.Where(o => new[] { "", o.Pair }.Contains(pair)).ToArray();
    }

    public Offer[] GetOffers() { return offersCollection.ToArray(); }
    Dictionary<string, Offer> _offersDictionary = new Dictionary<string, Offer>();
    public Offer GetOffer(string pair) {
      if(!_offersDictionary.ContainsKey(pair))
        _offersDictionary.Add(pair, offersCollection.Where(o => o.Pair == pair).DefaultIfEmpty(TradesManagerStatic.OfferDefault).Single());
      return _offersDictionary[pair];
    }

#pragma warning disable 0067
    public event EventHandler<RequestEventArgs> RequestFailed;
    public event OrderRemovedEventHandler OrderRemoved;
    void RaiseOrderRemoved(HedgeHog.Shared.Order args) => OrderRemoved?.Invoke(args);

    event EventHandler<ErrorEventArgs> ErrorEvent;
    public event EventHandler<ErrorEventArgs> Error {
      add {
        if(ErrorEvent == null || !ErrorEvent.GetInvocationList().Contains(value))
          ErrorEvent += value;
      }
      remove {
        ErrorEvent -= value;
      }
    }

    event EventHandler<OrderEventArgs> OrderAddedEvent;
    public static Subject<OrderEventArgs> _orderAddedSubject { get; set; }
    public IConnectableObservable<OrderEventArgs> OrderAddedObservable { get; private set; }
    public event EventHandler<OrderEventArgs> OrderAdded {
      add {
        if(OrderAddedEvent == null || !OrderAddedEvent.GetInvocationList().Contains(value))
          OrderAddedEvent += value;
      }
      remove {
        OrderAddedEvent -= value;
      }
    }



    event EventHandler<TradeEventArgs> TradeClosedEvent;
    public event EventHandler<TradeEventArgs> TradeClosed {
      add {
        if(TradeClosedEvent == null || !TradeClosedEvent.GetInvocationList().Contains(value))
          TradeClosedEvent += value;
      }
      remove {
        if(TradeClosedEvent != null && TradeClosedEvent.GetInvocationList().Contains(value))
          TradeClosedEvent -= value;
      }
    }
    void OnTradeClosed(Trade trade) {
      TradeClosedEvent?.Invoke(this, new TradeEventArgs(trade));
    }

    public event EventHandler<PriceChangedEventArgs> PriceChanged;

    public void RaisePriceChanged(Price price) {
      PriceCurrent.AddOrUpdate(price.Pair, price, (k, v) => price);
      if(PriceChanged != null) {
        var args = new PriceChangedEventArgs(price, GetAccount(), GetTrades());
        PriceChanged(this, args);
      }
    }

    public event EventHandler<TradeEventArgs> TradeAdded;
    void OnTradeAdded(Trade trade) {
      TradeAdded?.Invoke(this, new TradeEventArgs(trade));
    }



    public event EventHandler<TradeEventArgs> TradeRemoved;
    public event EventHandler<OrderEventArgs> OrderChanged;
    public event EventHandler<TradeEventArgs> TradeChanged;

    private ConcurrentDictionary<string, Price> PriceCurrent = new ConcurrentDictionary<string, Price>();

    #endregion

    #region ITradesManager Members

    public PendingOrder OpenTrade(string Pair, bool isBuy, int lot, double takeProfit, double stopLoss, double rate, string comment) {
      return OpenTrade(Pair, isBuy, lot, takeProfit, stopLoss, comment, null);
    }

    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price) {
      if(price == null)
        price = GetPrice(pair);
      if(!isHedged) {
        var closeLots = GetTrades(pair).Where(t => t.Buy != buy).Sum(t => t.Lots);
        if(closeLots > 0) {
          ClosePair(pair, !buy);
          lots -= closeLots;
        }
      }
      if(lots > 0)
        AddTrade(buy, lots, price);
      return null;
    }

    #endregion

    #region ITradesManager Members


    public IEnumerable<Price> TryGetPrice(string pair) {
      if(TryGetPrice(pair, out var price))
        yield return price;
      else yield break;
    }
    public bool TryGetPrice(string pair, out Price price) => PriceCurrent.TryGetValue(pair, out price);
    public Price GetPrice(string pair) {
      if(PriceCurrent.Count == 0)
        return new Price(pair);
      if(!PriceCurrent.TryGetValue(pair, out var price))
        throw new ArgumentNullException(new { pair, error = "No Current Price" } + "");
      return price;
    }

    #endregion

    #region ITradesManager Members


    public bool IsInTest {
      get;
      set;
    }

    public ICoreFX CoreFX {
      get {
        throw new NotImplementedException();
      }

      set {
        throw new NotImplementedException();
      }
    }

    public IConnectableObservable<PriceChangedEventArgs> PriceChangedObservable => throw new NotImplementedException();

    public IObservable<Order> OrderRemovedObservable => throw new NotImplementedException();

    #endregion

    #region ITradesManager Members


    public void DeleteOrder(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderStop(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderLimit(string orderId) {
      throw new NotImplementedException();
    }

    public void ChangeOrderRate(Order order, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeOrderAmount(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedLimit(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public object FixOrderClose(string tradeId, int mode, Price price, int lot) {
      return CloseTrade(GetTrades().Single(t => t.Id == tradeId), lot, price);
    }
    public object FixOrderClose(string tradeId) {
      throw new NotImplementedException();
    }

    public object[] FixOrdersClose(params string[] tradeIds) {
      throw new NotImplementedException();
    }

    public PendingOrder FixCreateLimit(string tradeId, double limit, string remark) {
      throw new NotImplementedException();
    }

    public void FixOrderSetStop(string tradeId, double stopLoss, string remark) {
      throw new NotImplementedException();
    }

    #endregion

    #region ITradesManager Members


    public IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate) {
      throw new NotImplementedException();
    }

    public Tick[] GetTicks(string pair, int periodsBack, Func<List<Tick>, List<Tick>> map) {
      throw new NotImplementedException();
    }

    public void GetBars(string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      throw new NotImplementedException();
    }

    public void GetBars(string pair, int periodMinutes, int periodsBack, DateTime endDate, List<Rate> ratesList) {
      throw new NotImplementedException();
    }

    #endregion

    #region ITradesManager Members


    public Trade[] GetTradesInternal(string Pair) {
      return GetTrades(Pair);
    }

    #endregion

    #region ITradesManager Members

    #endregion



    public void RefreshOrders() {
      throw new NotImplementedException();
    }

    public string CreateEntryOrder(string pair, bool isBuy, int amount, double rate, double stop, double limit) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedStop(string orderId, double rate) {
      throw new NotImplementedException();
    }

    bool ITradesManager.DeleteOrder(string orderId) {
      throw new NotImplementedException();
    }

    public void FixOrderSetLimit(string tradeId, double takeProfit, string remark) {
      throw new NotImplementedException();
    }

    public void GetBarsBase<TBar>(string pair, int period, int periodsBack, DateTime startDate, DateTime endDate, List<TBar> ticks, Func<List<TBar>, List<TBar>> map, Action<RateLoadingCallbackArgs<TBar>> callBack = null) where TBar : Rate, new() {
      throw new NotImplementedException();
    }

    public void DeleteOrders(string pair) {
    }

    public double GetNetOrderRate(string pair, bool isStop, bool getFromInternal = false) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderLot(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderRate(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public Order GetNetLimitOrder(Trade trade, bool getFromInternal = false) {
      throw new NotImplementedException();
    }

    public void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, Action<RateLoadingCallbackArgs<Rate>> callBack, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      throw new NotImplementedException();
    }

    public void FetchMMRs() => throw new NotImplementedException();
    (string status, double filled, double remaining, bool isDone)[] _orderStatuses = new (string status, double filled, double remaining, bool isDone)[0];
    public IList<(string status, double filled, double remaining, bool isDone)> GetOrderStatuses(string pair) => _orderStatuses;
    public double GetMinTick(string pair) => throw new NotImplementedException();
  }

}
