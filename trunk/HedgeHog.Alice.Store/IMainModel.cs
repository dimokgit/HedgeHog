using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;

namespace HedgeHog.Alice.Store {
  public abstract class TraderModelBase : HedgeHog.Models.ModelBase {
    public virtual event EventHandler<TradingStatisticsEventArgs> NeedTradingStatistics;
    public abstract event EventHandler MasterTradeAccountChanged;
    public abstract event EventHandler<MasterTradeEventArgs> MasterTradeAdded;
    public abstract event EventHandler<MasterTradeEventArgs> MasterTradeRemoved;
    public abstract event EventHandler<OrderEventArgs> OrderToNoLoss;
    public abstract event EventHandler<BackTestEventArgs> StartBackTesting;
    public abstract event EventHandler<EventArgs> StepBack;
    public abstract event EventHandler<EventArgs> StepForward;
    public abstract event EventHandler<EventArgs> TradingMacroNameChanged;
    public abstract Order2GoAddIn.CoreFX CoreFX { get; }
    public abstract Order2GoAddIn.FXCoreWrapper FWMaster { get; }
    public abstract ITradesManager TradesManager { get; }
    public abstract Exception Log { get; set; }
    public abstract string TradingMacroName { get; }
    public abstract double CurrentLoss { set; }
    public abstract void AddCosedTrade(Trade trade);
    public abstract double CommissionByTrade(Trade trades);
    public abstract bool IsInVirtualTrading { get; set; }
    public abstract DateTime VirtualDateStart { get; set; }
    public abstract TradingLogin LoginInfo { get; }
    private double _ActiveTakeProfit;
    public double ActiveTakeProfit {
      get { return _ActiveTakeProfit; }
      set {
        if (_ActiveTakeProfit != value) {
          _ActiveTakeProfit = value;
          RaisePropertyChanged("ActiveTakeProfit");
        }
      }
    }

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
