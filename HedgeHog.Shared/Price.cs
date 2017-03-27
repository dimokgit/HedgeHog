using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Shared {
  public class PriceChangedEventArgs : EventArgs {
    public string Pair { get; set; }
    public Price Price { get; set; }
    public Account Account { get; set; }
    public Trade[] Trades { get; set; }
    public PriceChangedEventArgs(string pair, Price price, Account account, Trade[] trades) {
      Pair = pair;
      Price = price;
      Account = account;
      Trades = trades;
    }
  }
  public delegate void PriceChangedEventHandler(Price Price);
  [Serializable]
  public class Price {
    public double Bid { get; set; }
    public double Ask { get; set; }
    public double Average { get { return (Ask + Bid) / 2; } }
    public double Spread { get { return Ask - Bid; } }
    private DateTimeOffset _time2;
    public DateTimeOffset Time2 {
      get { return _time2; }
      set {
        _time2 = value.ToUniversalTime();
      }
    }
    public DateTime Time { get { return _time2.LocalDateTime; } }
    public string Pair { get; set; }
    public int BidChangeDirection { get; set; }
    public int AskChangeDirection { get; set; }
    public bool IsReal { get { return Time != DateTime.MinValue; } }
    public bool IsPlayback { get; set; }
    public Price(string pair) {
      Pair = pair;
    }

    public Price(string pair, Rate rate) {
      if(rate == null)
        throw new ArgumentNullException(nameof(rate));
      Ask = rate.AskClose;
      Bid = rate.BidClose;
      Time2 = rate.StartDate2;
      Pair = pair;
      AskChangeDirection = 0;
      BidChangeDirection = 0;
    }
    public double BuyClose { get; set; }
    public double SellClose { get; set; }

    public override string ToString() {
      return new { Time, Bid, Ask } + "";
    }
  }

}
