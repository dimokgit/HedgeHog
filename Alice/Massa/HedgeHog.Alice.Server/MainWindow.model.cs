using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Gala = GalaSoft.MvvmLight.Command;
using System.Windows;
using System.Diagnostics;
using HedgeHog.UI;

namespace HedgeHog.Alice.Server {
  public class MainWindowModel:HedgeHog.Models.ModelBase {
    static MainWindowModel _Default;
    public static MainWindowModel Default {
      get { 
        if( _Default == null)_Default = new MainWindowModel();
        return _Default;
      }
    }

    #region Properties
    #region Title
    private string _Title = "Bar Tender";
    public string Title {
      get { return _Title; }
      set {
        if (_Title != value) {
          _Title = value;
          OnPropertyChanged("Title");
        }
      }
    }
    #endregion

    Order2GoAddIn.CoreFX _coreFX;
    Order2GoAddIn.CoreFX coreFX {
      get {
        if (_coreFX == null) {
          _coreFX = App.CoreFX;
          _coreFX.LoginError += exc => Log(exc);
        }
        return App.CoreFX;
      }
    }

    #endregion

    #region Commands

    AccountLoginRelayCommand _LoginCommand;
    public AccountLoginRelayCommand LoginCommand {
      get {
        if (_LoginCommand == null) {
          _LoginCommand = new AccountLoginRelayCommand(Login);
        }

        return _LoginCommand;
      }
    }
    void Login(LoginInfo li) {
      try {
        coreFX.LogOn();
      } catch (Exception exc) {
        Log(exc);
      }
    }

    #endregion
    void Log(object info) {
      MessageBox.Show(info.ToString());
    }
  }
}
