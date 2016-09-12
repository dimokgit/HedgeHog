using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;
using System.Windows.Data;

namespace HedgeHog.Alice.Store {
  public abstract class TraderModelBase : HedgeHog.Models.ModelBase {
    public virtual event EventHandler<TradingStatisticsEventArgs> NeedTradingStatistics;
    public abstract event EventHandler MasterTradeAccountChanged;
    public abstract event EventHandler<MasterTradeEventArgs> MasterTradeAdded;
    public abstract event EventHandler<MasterTradeEventArgs> MasterTradeRemoved;
    public abstract event EventHandler<OrderEventArgs> OrderToNoLoss;
    public abstract event EventHandler<EventArgs> StepBack;
    public abstract event EventHandler<EventArgs> StepForward;
    public abstract event EventHandler<EventArgs> TradingMacroNameChanged;
    public abstract Order2GoAddIn.CoreFX CoreFX { get; }
    public abstract Order2GoAddIn.FXCoreWrapper FWMaster { get; }
    public abstract TradingAccountModel AccountModel { get; }
    public abstract ITradesManager TradesManager { get; }
    public abstract Exception Log { get; set; }
    public abstract double CurrentLoss { set; }
    public abstract void AddCosedTrade(Trade trade);
    public abstract double CommissionByTrade(Trade trades);
    public abstract bool IsInVirtualTrading { get; set; }
    public abstract int IpPort { get; set; }
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
    ListCollectionView _RowsList;

    public ListCollectionView RowsList {
      get { return _RowsList; }
      set { 
        _RowsList = value;
        RaisePropertyChanged("RowsList");
      }
    }

  }
  public class Row<T> {
    public int Index { get; set; }
    public T Value { get; set; }
    public Row(int index, T value) {
      Index = index;
      Value = value;
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
