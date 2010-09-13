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
    ObservableCollection<Trade> tradesOpened = new ObservableCollection<Trade>();
    ObservableCollection<Trade> tradesClosed = new ObservableCollection<Trade>();
    Dictionary<string, List<Rate>> ratesByPair;
    Func<string,double> GetPipSize;
    Func<string,int> GetDigits;
    static long tradeId = 0;
    public VirtualTradesManager(Func<string,double>getPipSize,Func<string,int> getDigits,  Dictionary<string, List<Rate>> ratesByPair) {
      this.GetPipSize = getPipSize;
      this.GetDigits = getDigits;
      this.ratesByPair = ratesByPair;
      this.tradesOpened.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(_virualPortfolio_CollectionChanged);
    }
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
    public bool ClosePair(string pair) {
      tradesOpened.Where(t => t.Pair == pair).ToList().ForEach(t => tradesOpened.Remove(t));
      return true;
    }
    public bool ClosePair(string pair, bool isBuy) {
      tradesOpened.Where(t => t.Pair == pair && t.Buy == isBuy).ToList().ForEach(t => CloseTrade(t));
      return true;
    }

    private bool CloseTrade(Trade t) {
      return tradesOpened.Remove(t);
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
    void _virualPortfolio_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      var trade = (e.NewItems ?? e.OldItems)[0] as Trade;
      switch (e.Action) {
        case NotifyCollectionChangedAction.Add:
          trade.TradesManager = this;
          OnTradeAdded(trade);
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
  }

}
