/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IBApp {
  public class TickPriceMessage : MarketDataMessage {
    public int CanAutoExecute { get; private set; }
    public double Price { get; private set; }

    public TickPriceMessage(int requestId, int field, double price, int canAutoExecute) : base(MessageType.TickPrice, requestId, field) {
      Price = price;
      CanAutoExecute = canAutoExecute;
    }
  }
}
