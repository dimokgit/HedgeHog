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
    private FXCore.TradeDeskEventsSinkClass _mSink;

    private bool _IsLoggedIn;
    public bool IsLoggedIn {
      get { return _IsLoggedIn; }
      set {
        if (_IsLoggedIn != value) {
          _IsLoggedIn = value;
          OnPropertyChanged("IsLoggedIn");
        }
      }
    }


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
          _coreFX.LoggedInEvent += coreFX_LoggedInEvent;
          _coreFX.LoggedOffEvent += coreFX_LoggedOffEvent;

          _mSink = new FXCore.TradeDeskEventsSinkClass();
          _mSink.ITradeDeskEvents_Event_OnSessionStatusChanged += mSink_ITradeDeskEvents_Event_OnSessionStatusChanged;
        }
        return App.CoreFX;
      }
    }

    void coreFX_LoggedOffEvent(object sender, Order2GoAddIn.LoggedInEventArgs e) {
      IsLoggedIn = coreFX.IsLoggedIn;
    }

    void coreFX_LoggedInEvent(object sender, Order2GoAddIn.LoggedInEventArgs e) {
      IsLoggedIn = coreFX.IsLoggedIn;
    }

    void mSink_ITradeDeskEvents_Event_OnSessionStatusChanged(string sStatus) {
      IsLoggedIn = coreFX.IsLoggedIn;
    }

    public string Account { get; set; }
    public string Password { get; set; }
    public bool IsDemo { get; set; }

    private string aaaa;

    public string AAAA {
      get { return aaaa; }
      set { aaaa = value; }
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
        coreFX.LogOn(li.Account,li.Password,li.IsDemo);
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
