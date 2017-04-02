/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBApi;

namespace IBApp {
  public class PositionMessage : IBMessage {

    public PositionMessage(string account, Contract contract, double pos, double avgCost) : base(MessageType.Position) {
      Account = account;
      Contract = contract;
      Position = pos;
      AverageCost = avgCost;
    }

    public string Account { get; set; }

    public Contract Contract { get; set; }

    public double Position { get; set; }

    public double AverageCost { get; set; }

    public bool IsBuy => Position > 0;

    public int Quantity => (int)Math.Abs(Position);

    public override string ToString() {
      return new { Symbol = Contract.LocalSymbol, Position, AverageCost, Type } + "";
    }
  }
}
