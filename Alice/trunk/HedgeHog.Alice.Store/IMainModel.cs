using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  public interface IMainModel : INotifyPropertyChanged {
    event EventHandler<MasterTradeEventArgs> MasterTradeAdded;
    event EventHandler<MasterTradeEventArgs> MasterTradeRemoved;
    event EventHandler<OrderEventArgs> OrderToNoLoss;
    event EventHandler<BackTestEventArgs> StartBackTesting;
    event EventHandler<EventArgs> StepBack;
    event EventHandler<EventArgs> StepForward;
    Order2GoAddIn.CoreFX CoreFX { get; }
    Exception Log { set; }
    string TradingMacroName { get; }
    double CurrentLoss { set; }
    void AddCosedTrade(Trade trade);
    double CommissionByTrade(Trade trades);
    bool IsInVirtualTrading { get; set; }
    DateTime VirtualDateStart { get; set; }
    TradingLogin LoginInfo { get; }
  }
  public class TradingLogin {
    public string AccountId { get; set; }
    public string Password { get; set; }
    public bool IsDemo { get; set; }
    public TradingLogin(string accountId,string password,bool isDemo) {
      this.AccountId = accountId;
      this.Password = password;
      this.IsDemo = IsDemo;
    }
  }
}
