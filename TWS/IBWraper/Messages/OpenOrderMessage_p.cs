/* Copyright (C) 2018 Interactive Brokers LLC. All rights reserved. This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBApi;

namespace IBSampleApp.messages {
  partial class OpenOrderMessage :OrderMessage {

    public string Key => this + "";
    public override string ToString() => new { Contract, Order, OrderState } + "";
    public bool IsCancelled => OrderState.IsCancelled;

  }
}
