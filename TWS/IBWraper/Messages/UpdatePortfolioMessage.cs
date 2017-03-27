/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBApi;

namespace IBApp {
  public class UpdatePortfolioMessage : IBMessage {
    public UpdatePortfolioMessage(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealisedPNL, double realisedPNL, string accountName) : base(MessageType.PortfolioValue) {
      Contract = contract;
      Position = position;
      MarketPrice = marketPrice;
      MarketValue = marketValue;
      AverageCost = averageCost;
      UnrealisedPNL = unrealisedPNL;
      RealisedPNL = realisedPNL;
      AccountName = accountName;
    }

    public Contract Contract { get; set; }

    public double Position { get; set; }

    public double MarketPrice { get; set; }

    public double MarketValue { get; set; }

    public double AverageCost { get; set; }

    public double UnrealisedPNL { get; set; }

    public double RealisedPNL { get; set; }

    public string AccountName { get; set; }
    public override string ToString() {
      return new {
        Contract = new { Contract.LocalSymbol } + "",
        Position,
        UnrealisedPNL,
        AverageCost,
        MarketPrice,
        MarketValue,
        Type
      } + "";
    }

  }
}
