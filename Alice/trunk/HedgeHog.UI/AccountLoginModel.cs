using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace HedgeHog.UI {
  public class AccountLoginModel : HedgeHog.Models.ModelBase {
    #region Properties
    static AccountLoginModel _Default;
    public static AccountLoginModel Default {
      get {
        if (_Default == null) _Default = new AccountLoginModel();
        return _Default;
      }
    }
    private string _Account;
    public string Account {
      get { return _Account; }
      set { _Account = value; }
    }

    private string _Password;
    public string Password {
      get { return _Password; }
      set { _Password = value; }
    }

    private bool _IsDemo;
    public bool IsDemo {
      get { return _IsDemo; }
      set { _IsDemo = value; }
    }
    #endregion

    ICommand _LoginCommand;
    public ICommand LoginCommand {
      get {
        if (_LoginCommand == null) {
          _LoginCommand = new GalaSoft.MvvmLight.Command.RelayCommand<AccountLoginRelayCommand>(Login, (l) => true);
        }

        return _LoginCommand;
      }
    }
    void Login(AccountLoginRelayCommand login) {
      login.Execute(new LoginInfo(Account, Password, IsDemo));
    }
  }

}
