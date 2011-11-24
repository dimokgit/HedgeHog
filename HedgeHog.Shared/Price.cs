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
    public PriceChangedEventArgs(string pair, Price price,Account account,Trade[] trades) {
      this.Pair = pair;
      this.Price = price;
      this.Account = account;
      this.Trades = trades;
    }
  }
  public delegate void PriceChangedEventHandler(Price Price);
  public enum ClosePriceMode { Average, HighLow }
  [Serializable]
  public class Price {
    public static ClosePriceMode ClosePriceMode = ClosePriceMode.Average;
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
    public int Digits { get; set; }
    public double PipSize { get; set; }
    public Price() {    }
    public Price(string pair, Rate rate,DateTime serverTime,double pipSize,int digits,bool isPlayBack) {
      Ask = rate.AskClose;
      Bid = rate.BidClose;
      Digits = digits;
      Time = serverTime;
      Pair = pair;
      PipSize = pipSize;
      AskChangeDirection = 0;
      BidChangeDirection = 0;
      this.IsPlayback = isPlayBack;
      BuyClose = ClosePriceMode == Shared.ClosePriceMode.Average ? (rate.BidHigh + rate.BidClose) / 2 : rate.BidHigh;
      SellClose = ClosePriceMode == Shared.ClosePriceMode.Average ? (rate.AskLow + rate.AskClose) / 2 : rate.AskLow;
    }
    public double BuyClose { get; set; }
    public double SellClose { get; set; }
  }

}
