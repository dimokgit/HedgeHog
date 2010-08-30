using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public interface ITradesManager {
    Trade[] GetTrades(string pair);
    Trade[] GetTrades();
    Price GetPrice(string pair);
    PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark,Price price);
    bool ClosePair(string pair, bool isBuy);
    bool ClosePair(string pair);
    event PriceChangedEventHandler PriceChanged;
    event TradeAddedEventHandler TradeAdded;
    event TradeRemovedEventHandler TradeRemoved;

    bool IsInTest { get; set; }
  }
}
