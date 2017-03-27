/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBApi;
using static HedgeHog.Core.JsonExtensions;
namespace IBApp {
  public abstract class IBMessage {
    readonly MessageType _type;
    public MessageType Type {
      get { return _type; }
    }
    public IBMessage(MessageType type) {
      _type = type;
    }
    public override string ToString() {
      return this.ToJson();
    }
  }
}
