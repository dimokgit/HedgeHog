using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using HedgeHog.Bars;

namespace HedgeHog.Shared {
  public class VirtualTradesManager : ITradesManager {
    const string TRADE_ID_FORMAT = "yyMMddhhmmssffff";
    ObservableCollection<Offer> offersCollection = new ObservableCollection<Offer>(new[]{
      new Offer(){Pair = "EUR/USD",Digits=5,PipCost=1,MMR = 150,PointSize=.0001}
    });
    ObservableCollection<Trade> tradesOpened = new ObservableCollection<Trade>();
    ObservableCollection<Trade> tradesClosed = new ObservableCollection<Trade>();
    ObservableCollection<Order> ordersOpened = new ObservableCollection<Order>();
    Dictionary<string, List<Rate>> ratesByPair;
    public double GetPipSize(string pair) { return GetOffer(pair).PointSize; }
    public int GetDigits(string pair) { return GetOffer(pair).Digits; }
    public double GetPipCost(string pair) { return GetOffer(pair).PipCost; }

    int minimumQuantity;
    public int MinimumQuantity { get { return minimumQuantity; } }
    public double Leverage(string pair) { return MinimumQuantity/ GetOffer(pair).MMR; }
    public DateTime ServerTime { get { return DateTime.Now; } }

    #region Money
    public int MoneyAndPipsToLot(double Money, double pips, string pair) {
      return TradesManagedStatic.MoneyAndPipsToLot(Money, pips, GetPipCost(pair), MinimumQuantity);
    }
    #endregion

    #region Account
    string accountId;
    Account _account;

    public Account Account {
      get {
        if (_account == null)
          _account = new Account() {
            ID = accountId,
            Balance = 10000,
            UsableMargin = 10000,
            IsMarginCall = false,
            Equity = 10000,
            Hedging = true,
            //Trades = includeOtherInfo ? trades = GetTrades("") : null,
            //StopAmount = includeOtherInfo ? trades.Sum(t => t.StopAmount) : 0,
            //LimitAmount = includeOtherInfo ? trades.Sum(t => t.LimitAmount) : 0,
            ServerTime = ServerTime
          };
        return _account;
      }
    }
    public Account GetAccount() { return GetAccount(true); }
    public Account GetAccount(bool includeOtherInfo) {
      if (includeOtherInfo) {
        var trades = GetTrades();
        Account.Trades = trades;
        Account.StopAmount = includeOtherInfo ? trades.Sum(t => t.StopAmount) : 0;
        Account.LimitAmount = includeOtherInfo ? trades.Sum(t => t.LimitAmount) : 0;
      }
      return Account;
    }
    #endregion

    static long tradeId = 0;
    public VirtualTradesManager(string accountId, int minimumQuantity, Dictionary<string, List<Rate>> ratesByPair) {
      this.accountId = accountId;
      this.minimumQuantity = minimumQuantity;
      this.ratesByPair = ratesByPair;
      this.tradesOpened.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(_virualPortfolio_CollectionChanged);
    }
    public double InPips(string pair, double? price) { return TradesManagedStatic.InPips(price, GetPipSize(pair)); }
    public double InPoints(string pair, double? price) { return TradesManagedStatic.InPoins(this, pair, price); }
    static long NewTradeId() {
      if( tradeId == 0)
        tradeId = DateTime.Now.Ticks/10000;
      return ++tradeId;
    }
    void AddTrade(bool isBuy, int lot, Price price) {
      tradesOpened.Add(new Trade() {
        Id = NewTradeId() + "",
        Pair = price.Pair,
        Buy = isBuy,
        IsBuy = isBuy,
        Lots = lot,
        Open = isBuy ? price.Ask : price.Bid,
        Close = isBuy ? price.Bid : price.Ask,
        Time = price.Time,
        TimeClose = price.Time,
        IsVirtual = true
      });
    }

    #region Close Trade
    public void CloseTradeAsync(Trade trade) {
      CloseTrade(trade);
    }
    public void CloseTradesAsync(Trade[] trades) {
      CloseTrades(trades);
    }

    private void CloseTrades(Trade[] trades) {
      foreach (var trade in trades)
        CloseTrade(trade);
    }
    public bool ClosePair(string pair) {
      CloseTrades(tradesOpened.Where(t => t.Pair == pair).ToArray());
      return true;
    }
    public bool ClosePair(string pair, bool isBuy) {
      CloseTrades(tradesOpened.Where(t => t.Pair == pair && t.Buy == isBuy).ToArray());
      return true;
    }
    public void CloseTrade(Trade t) {
      tradesOpened.Remove(t);
    }
    public bool CloseTrade(Trade trade,int lot,Price price) {
      if (trade.Lots <= lot) CloseTrade(trade);
      else {
        var newTrade = trade.Clone();
        newTrade.Lots = trade.Lots - lot;
        newTrade.Id = NewTradeId() + "";
        newTrade.UpdateByPrice(price);
        tradesOpened.Add(trade);
        trade.Lots = lot;
        trade.UpdateByPrice(price);
        CloseTrade(trade);
      }
      return true;
    }
    #endregion
    
    void _virualPortfolio_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      var trade = (e.NewItems ?? e.OldItems)[0] as Trade;
      switch (e.Action) {
        case NotifyCollectionChangedAction.Add:
          trade.TradesManager = this;
          OnTradeAdded(trade);
          OnTradeClosed(trade);
          break;
        case NotifyCollectionChangedAction.Reset:
        case NotifyCollectionChangedAction.Remove:
          trade.TradesManager = null;
          tradesClosed.Add(trade);
          TradeRemoved(trade);
          break;
      }
    }

    #region ITradesManager Members

    public Trade[] GetTrades(string pair) {
      return tradesOpened.Where(t => t.Pair == pair).ToArray();
    }

    public Trade[] GetTrades() {
      return tradesOpened.ToArray();
    }
    public Trade[] GetClosedTrades(string Pair) {
      return tradesClosed.Where(t => t.Pair == Pair).ToArray();
    }
    public Trade GetLastTrade(string pair) {
      var trades = GetTrades(pair).OrderBy(t => t.Id).ToArray();
      if (trades.Length == 0)
        trades = GetClosedTrades(pair);
      //if (trades.Length == 0)
      //  trades = GetTradesFromReport(DateTime.Now.AddDays(-7), DateTime.Now.AddDays(1).Date).ToArray();
      return trades.DefaultIfEmpty(new Trade()).OrderBy(t => t.Id).Last();
    }

    public Order[] GetOrders(string pair) {
      return ordersOpened.Where(o => new[] { "", o.Pair }.Contains(pair)).ToArray();
    }

    public Offer[] GetOffers() { return offersCollection.ToArray(); }
    public Offer GetOffer(string pair) { return offersCollection.Where(o => o.Pair == pair).Single(); }

    public event EventHandler<RequestEventArgs> RequestFailed;
    public event OrderRemovedEventHandler OrderRemoved;

    event EventHandler<ErrorEventArgs> ErrorEvent;
    public event EventHandler<ErrorEventArgs> Error {
      add {
        if (ErrorEvent == null || !ErrorEvent.GetInvocationList().Contains(value))
          ErrorEvent += value;
      }
      remove {
        ErrorEvent -= value;
      }
    }

    event EventHandler<OrderEventArgs> OrderAddedEvent;
    public event EventHandler<OrderEventArgs> OrderAdded {
      add {
        if (OrderAddedEvent == null || !OrderAddedEvent.GetInvocationList().Contains(value))
          OrderAddedEvent += value;
      }
      remove {
        OrderAddedEvent -= value;
      }
    }



    event EventHandler<TradeEventArgs> TradeClosedEvent;
    public event EventHandler<TradeEventArgs> TradeClosed {
      add {
        if (TradeClosedEvent == null || !TradeClosedEvent.GetInvocationList().Contains(value))
          TradeClosedEvent += value;
      }
      remove {
        TradeClosedEvent -= value;
      }
    }
    void OnTradeClosed(Trade trade) {
      if (TradeClosedEvent != null) TradeClosedEvent(this, new TradeEventArgs(trade));
    }

    public event PriceChangedEventHandler PriceChanged;

    public void RaisePriceChanged(string pair,Rate rate) {
      RaisePriceChanged(new Price(pair, rate, this.GetPipSize(pair), GetDigits(pair)));
    }
    public void RaisePriceChanged(Price price) {
      if (PriceChanged != null) PriceChanged(price);
    }

    public event TradeAddedEventHandler TradeAdded;
    void OnTradeAdded(Trade trade) {
      if (TradeAdded != null) TradeAdded(trade);
    }



    public event TradeRemovedEventHandler TradeRemoved;
    void OnTradeRemoved(Trade trade) {
      if (TradeRemoved != null) TradeRemoved(trade);
    }

    #endregion

    #region ITradesManager Members


    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark,Price price) {
      AddTrade(buy, lots, price);
      return null;
    }

    #endregion

    #region ITradesManager Members


    public Price GetPrice(string pair) {
      var rate = ratesByPair[pair].Last();
      return new Price(pair, rate, GetPipSize(pair), GetDigits(pair));
    }

    #endregion

    #region ITradesManager Members


    public bool IsInTest {
      get;
      set;
    }

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
      throw new NotImplementedException();
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
  }

}
