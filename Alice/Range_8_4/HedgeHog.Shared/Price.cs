﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Shared {
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
    public int Digits { get; set; }
    public double PipSize { get; set; }
    public Price() {    }
    public Price(string pair, Rate rate,double pipSize,int digits) {
      Ask = rate.AskClose;
      Bid = rate.BidClose;
      Digits = digits;
      Time = rate.StartDate;
      Pair = pair;
      PipSize = pipSize;
      AskChangeDirection = 0;
      BidChangeDirection = 0;

    }
  }

}