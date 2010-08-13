using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Client {
  public interface IMainModel {
    event EventHandler<MasterTradeEventArgs> MasterTradeAdded;
    event EventHandler<MasterTradeEventArgs> MasterTradeRemoved;
    event EventHandler<Order2GoAddIn.FXCoreWrapper.OrderEventArgs> OrderToNoLoss;
    Order2GoAddIn.CoreFX CoreFX { get; }
    Exception Log { set; }
    double CurrentLoss { set; }
    void AddCosedTrade(Trade trade);
    double CommissionByTrade(Trade trade);
  }
}
