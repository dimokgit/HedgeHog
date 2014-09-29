using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Gala = GalaSoft.MvvmLight.Command;
using System.Windows;
using System.Diagnostics;
using HedgeHog;
using HedgeHog.UI;
using System.Collections.ObjectModel;
using NCCW = NotifyCollectionChangedWrapper;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Server {
  public class MainWindowModel:HedgeHog.Models.ModelBase {
    #region singleton
    MainWindowModel() {}
    static MainWindowModel _Default;
    public static MainWindowModel Default {
      get {
        if (_Default == null) _Default = new MainWindowModel();
        return _Default;
      }
    }
    #endregion

    #region Properties

    #region Period
    private int _Period = 1;
    public int Period {
      get { return _Period; }
      set {
        if (_Period != value) {
          _Period = value;
          RaisePropertyChangedCore();
        }
      }
    }
    #endregion

    #region Periods
    private int _Periods = 180;
    public int Periods {
      get { return _Periods; }
      set {
        if (_Periods != value) {
          _Periods = value;
          RaisePropertyChangedCore();
        }
      }
    }
    #endregion

    private string _LogText;
    public string LogText {
      get {
        lock (_logQueue) {
          return string.Join(Environment.NewLine, _logQueue.Reverse());
        }
      }
    }

    Queue<string> _logQueue = new Queue<string>();
    Exception _log;
    public Exception Log {
      get { return _log; }
      set {
        if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return;
        _log = value;
        var exc = value is Exception ? value : null;
        //var comExc = exc as System.Runtime.InteropServices.COMException;
        //if (comExc != null && comExc.ErrorCode == -2147467259)
        //  AccountLogin(new LoginInfo(TradingAccount, TradingPassword, TradingDemo));
        lock (_logQueue) {
          if (_logQueue.Count > 5) _logQueue.Dequeue();
          var messages = new List<string>(new[] { DateTime.Now.ToString("[dd HH:mm:ss] ") + value.GetExceptionShort() });
          while (value.InnerException != null) {
            messages.Add(value.InnerException.GetExceptionShort());
            value = value.InnerException;
          }
          _logQueue.Enqueue(string.Join(Environment.NewLine + "-", messages));
        }
        exc = FileLogger.LogToFile(exc);
        RaisePropertyChanged(() => LogText);
      }
    }

    
    private FXCore.TradeDeskEventsSinkClass _mSink;

    private bool _IsLoggedIn;
    public bool IsLoggedIn {
      get { return _IsLoggedIn; }
      set {
        if (_IsLoggedIn != value) {
          _IsLoggedIn = value;
          RaisePropertyChanged("IsLoggedIn");
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
          RaisePropertyChanged("Title");
        }
      }
    }
    #endregion

    Order2GoAddIn.CoreFX _coreFX;
    Order2GoAddIn.CoreFX coreFX {
      get {
        if (_coreFX == null) {
          _coreFX = App.CoreFX;
          _coreFX.LoginError += exc => Log = exc;
          _coreFX.LoggedIn += coreFX_LoggedInEvent;
          _coreFX.LoggedOff += coreFX_LoggedOffEvent;

          _mSink = new FXCore.TradeDeskEventsSinkClass();
          _mSink.ITradeDeskEvents_Event_OnSessionStatusChanged += mSink_ITradeDeskEvents_Event_OnSessionStatusChanged;
        }
        return App.CoreFX;
      }
    }

    NCCW.NotifyCollectionChangedWrapper<PairInfo<Rate>> _PairInfos;
    public NCCW.NotifyCollectionChangedWrapper<PairInfo<Rate>> PairInfos {
      get {
        if (_PairInfos == null) {
          _PairInfos = new NCCW.NotifyCollectionChangedWrapper<PairInfo<Rate>>(new ObservableCollection<PairInfo<Rate>>());
        }
        return _PairInfos; 
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
        Log = exc;
      }
    }

    #endregion
  }
}
