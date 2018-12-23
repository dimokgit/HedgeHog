/* Copyright (C) 2018 Interactive Brokers LLC. All rights reserved. This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBApi;

namespace IBSampleApp.messages
{
  public class TickByTickBidAskMessage
    {
        public int ReqId { get; private set; }
        public long Time { get; private set; }
        public double BidPrice { get; private set; }
        public double AskPrice { get; private set; }
        public long BidSize { get; private set; }
        public long AskSize { get; private set; }
        public TickAttrib Attribs { get; private set; }

        public TickByTickBidAskMessage(int reqId, long time, double bidPrice, double askPrice, long bidSize, long askSize, TickAttrib attribs)
        {
            ReqId = reqId;
            Time = time;
            BidPrice = bidPrice;
            AskPrice = askPrice;
            BidSize = bidSize;
            AskSize = askSize;
            Attribs = attribs;
        }
    }
}
