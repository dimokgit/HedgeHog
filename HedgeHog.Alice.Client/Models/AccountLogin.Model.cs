using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FXW = HedgeHog.Shared.ITradesManager;

namespace HedgeHog.Alice.Client.Models {
  public class AccountModelBase : HedgeHog.Models.ModelBase {
    protected Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    protected FXW fwLocal;
    public bool IsLoggedIn { get { return CoreFX.IsLoggedIn; } }

    string _tradingAccount;
    public string TradingAccount {
      get { return _tradingAccount; }
      set { _tradingAccount = value; RaisePropertyChangedCore(); }
    }

    string _tradingPassword;
    public string TradingPassword {
      get { return _tradingPassword; }
      set { _tradingPassword = value; RaisePropertyChangedCore(); }
    }

    public bool? TradingDemo { get; set; }

    public AccountModelBase(string tradingAccount, string tradingPassword, bool isDemoAccount) {
      this.TradingAccount = tradingAccount;
      this.TradingPassword = tradingPassword;
      this.TradingDemo = isDemoAccount;
    }
  }
  public class AccountLocal : AccountModelBase {
    public AccountLocal(string tradingAccount, string tradingPassword, bool isDemoAccount):base(tradingAccount,  tradingPassword,  isDemoAccount) {

    }
  }
}
