/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IBApp {
  public class HistoricalDataEndMessage : IBMessage {
    public string StartDate { get; }
    public int RequestId { get; }
    public string EndDate { get; }

    public HistoricalDataEndMessage(int requestId, string startDate, string endDate):base(MessageType.HistoricalDataEnd) {
      RequestId = requestId;
      StartDate = startDate;
      EndDate = endDate;
    }
    public override string ToString() {
      return new { Type, RequestId, StartDate, EndDate } + "";
    }
  }
}
