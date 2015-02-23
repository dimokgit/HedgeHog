using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Shared {
  public class PriceChangedEventArgs : EventArgs {
    public string Pair { get; set; }
    public int BarPeriod { get; set; }
    public Price Price { get; set; }
    public Account Account { get; set; }
    public Trade[] Trades { get; set; }
    public PriceChangedEventArgs(string pair, Price price, Account account, Trade[] trades) : this(pair, int.MinValue, price, account, trades) { }
    public PriceChangedEventArgs(string pair,int barPeriod, Price price,Account account,Trade[] trades) {
      this.Pair = pair;
      this.BarPeriod = barPeriod;
      this.Price = price;
      this.Account = account;
      this.Trades = trades;
    }
  }
  public delegate void PriceChangedEventHandler(Price Price);
  [Serializable]
  public class Price {
    public double Bid { get; set; }
    public double Ask { get; set; }
    public double Average { get { return (Ask + Bid) / 2; } }
    public double Spread { get { return Ask - Bid; } }
    public DateTime Time { get; set; }
    public string Pair { get; set; }
    public int BidChangeDirection { get; set; }
    public int AskChangeDirection { get; set; }
    public bool IsReal { get { return Time != DateTime.MinValue; } }
    public bool IsPlayback { get; set; }
    public Price() {    }
    public Price (string pair, double ask, double bid, int asChangeDirection, int bidChangeDirection, DateTime serverTimeLocal) {
        Ask = ask; Bid = bid;
        AskChangeDirection = asChangeDirection;
        BidChangeDirection = bidChangeDirection;
        Time = serverTimeLocal;
        Pair = pair;
    }

    public Price(string pair, Rate rate ) {
      if (rate == null) {
        Ask = Bid = double.NaN;
      } else {
        Ask = rate.AskClose;
        Bid = rate.BidClose;
      }
      Time = rate.StartDate;
      Pair = pair;
      AskChangeDirection = 0;
      BidChangeDirection = 0;
    }
    public double BuyClose { get; set; }
    public double SellClose { get; set; }
  }

}
