/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IBApp {
  public class HistoricalDataMessage : IBMessage {

    public int RequestId { get; set; }

    public DateTime Date { get; set; }

    public double Open { get; set; }


  public double High { get; set; }

  public double Low { get; set; }

    public double Close { get; set; }

    public int Volume { get; set; }

    public int Count { get; set; }

    public double Wap { get; set; }

    public bool HasGaps { get; set; }

    public HistoricalDataMessage(int reqId, DateTime date, double open, double high, double low, double close, int volume, int count, double WAP, bool hasGaps) {
      Type = MessageType.HistoricalData;
      RequestId = reqId;
      Date = date;
      Open = open;
      High = high;
      Low = low;
      Close = close;
      Volume = volume;
      Count = count;
      Wap = WAP;
      HasGaps = hasGaps;
    }
    public override string ToString() {
      return new {
        Type,
        RequestId,
        Date,
        Open,
        High,
        Low,
        Close,
        Volume,
        Count,
        Wap,
        HasGaps
      } + "";
    }
  }
}
