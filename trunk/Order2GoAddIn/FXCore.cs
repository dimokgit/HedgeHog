using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using HedgeHog.Shared;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;


namespace Order2GoAddIn {
  public class CoreFX :GalaSoft.MvvmLight.ViewModelBase, IDisposable{
    public bool IsInVirtualTrading { get; set; }
    FXCore.CoreAut mCore = new FXCore.CoreAut();
    public FXCore.TradeDeskAut _mDesk;
    public FXCore.TradeDeskAut Desk {
      get {
        if (_mDesk == null)
          _mDesk = (FXCore.TradeDeskAut)mCore.CreateTradeDesk("trader");
        return _mDesk;
      }
    }
    public delegate void LoginErrorHandler(Exception exc);
    public event LoginErrorHandler LoginError;
    private void RaiseLoginError(Exception exc) {
      if (LoginError != null) LoginError(exc);
    }

    #region LoggedIn Event
    event EventHandler<LoggedInEventArgs> LoggedInEvent;
    public event EventHandler<LoggedInEventArgs>  LoggedIn {
      add {
        if (LoggedInEvent == null || !LoggedInEvent.GetInvocationList().Contains(value))
          LoggedInEvent += value;
      }
      remove {
        LoggedInEvent -= value;
      }
    }

    private void RaiseLoggedIn() {
      if (LoggedInEvent != null) LoggedInEvent(this, new LoggedInEventArgs(IsInVirtualTrading));
    }
    #endregion

    //public event EventHandler<LoggedInEventArgs> LoggedOffEvent;
    //private void RaiseLoggedOff() {
    //  if (LoggedOffEvent != null) LoggedOffEvent(this, new LoggedInEventArgs(IsInVirtualTrading));
    //}


    event EventHandler<LoggedInEventArgs> LoggedOffEvent;
    public event EventHandler<LoggedInEventArgs> LoggedOff {
      add {
        if (LoggedOffEvent == null || !LoggedOffEvent.GetInvocationList().Contains(value))
          LoggedOffEvent += value;
      }
      remove {
        LoggedOffEvent -= value;
      }
    }
    void RaiseLoggedOff() {
      if (LoggedOffEvent != null) LoggedOffEvent(this, new LoggedInEventArgs(IsInVirtualTrading));
    }


    event EventHandler<LoggedInEventArgs> LoggingOffEvent;
    public event EventHandler<LoggedInEventArgs> LoggingOff {
      add {
        if (LoggingOffEvent == null || !LoggingOffEvent.GetInvocationList().Contains(value))
          LoggingOffEvent += value;
      }
      remove {
        LoggingOffEvent -= value;
      }
    }
    void RaiseLoggingOff() {
      if (LoggingOffEvent != null)
        try {
          LoggingOffEvent(this, new LoggedInEventArgs(IsInVirtualTrading));
        } catch(Exception exc) {
          Debug.Fail(exc.Message, exc.ToString());
        }
    }


    System.Threading.Timer timer;
    object _deskLocker = new object();
    string user = "";
    string password = "";
    bool isDemo = true;
    public bool IsDemo { get { return isDemo; } }
    string URL = "";
    bool noTimer = true;
    public readonly int ServerTimeOffset = -4;
    private TimeSpan _serverTimeOffset = TimeSpan.Zero;
    public static readonly string DefaultUrl = "http://www.fxcorporate.com";
    private DateTime serverTimeLast = DateTime.MaxValue;
    public DateTime ServerTime {
      get {
        if (_mDesk == null) return DateTime.Now;
        if (_serverTimeOffset == TimeSpan.Zero)
          _serverTimeOffset = TimeZoneInfo.ConvertTimeFromUtc((DateTime)Desk.ServerTime, TimeZoneInfo.Local) - DateTime.Now;
        return serverTimeLast = DateTime.Now + _serverTimeOffset;
      }
    }

    #region Ctor

    public CoreFX():this(true) { }
    public CoreFX(bool noTimer) {
      this.noTimer = noTimer;
    }
    ~CoreFX() {
      try {
        Logout();
      } catch { }
    }
    #endregion

    FXCore.TradeDeskEventsSinkClass mSink = new FXCore.TradeDeskEventsSinkClass();
    int mSubscriptionId = -1;
    bool isSubsribed = false;
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void Subscribe() {
      if (isSubsribed) return;
      Unsubscribe();
      mSink.ITradeDeskEvents_Event_OnSessionStatusChanged += OnSessionStatusChanged;
      mSubscriptionId = Desk.Subscribe(mSink);
      isSubsribed = true;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void Unsubscribe() {
      if (mSubscriptionId != -1) {
        try {
          mSink.ITradeDeskEvents_Event_OnSessionStatusChanged -= OnSessionStatusChanged;
        } catch { }
        try {
          Desk.Unsubscribe(mSubscriptionId);
        } catch { }
        isSubsribed = false;
      }
      mSubscriptionId = -1;
    }

    ISubject<string> _sessionStatusSubject;
    ISubject<string> SessionStatusSubject {
      get {
        if (_sessionStatusSubject == null) {
          _sessionStatusSubject = new Subject<string>();
          _sessionStatusSubject.Throttle(TimeSpan.FromSeconds(1)).Subscribe(s => SessionStatusChanged(s));
        }
        return _sessionStatusSubject;
      }
    }

    protected void OnSessionStatusChanged(string status) {
      SessionStatusSubject.OnNext(status);
    }

    void SessionStatusChanged(string status) {
      SessionStatus = (TradingServerSessionStatus)Enum.Parse(typeof(TradingServerSessionStatus),status);
      if (SessionStatus == TradingServerSessionStatus.Disconnected) {
        Logout();
        LogOn();
      }

    }

    public enum Tables { Accounts, Orders, Offers, Trades, ClosedTrades, Summary, Messages };

    [CLSCompliant(false)]
    public FXCore.TableAut Table(Tables table) { return Table(table + ""); }
    [CLSCompliant(false)]
    public FXCore.TableAut Table(string TableName) {
      return (FXCore.TableAut)Desk.FindMainTable(TableName);
    }

    [CLSCompliant(false)]
    public FXCore.RowAut[] TableRows(Tables table) { return TableRows(table + ""); }
    public FXCore.RowAut[] TableRows(string tableName) { return ((FXCore.RowsEnumAut)Table(tableName).Rows).Cast<FXCore.RowAut>().ToArray(); }

    private string[] _Instruments;
    public string[] Instruments {
      get {
        if (!IsLoggedIn || IsInVirtualTrading) return defaultInstruments;
        if (_Instruments == null)
          _Instruments = (from offer in TableRows(Tables.Offers)
                          select offer.CellValue("Instrument") + ""
                         ).ToArray();
        return _Instruments;
      }
    }
    static string[] defaultInstruments = new string[] { "EUR/GBP","EUR/USD","EUR/CAD","EUR/CHF","EUR/JPY","GBP/USD","GBP/CAD","GBP/CHF","GBP/JPY","USD/CAD","USD/CHF","USD/JPY","CAD/CHF","CAD/JPY","CHF/JPY"};
    public string[] InstrumentsAll() { 
        if (Desk == null) return defaultInstruments;
        var instruments = Desk.GetInstruments() as FXCore.StringEnumAut;
        List<string> l = new List<string>();
        foreach (var i in instruments)
          l.Add(i+"");
        return l.ToArray();
    }

    TimeSpan silenceInterval = TimeSpan.FromSeconds(60*5);
    void InitTimer() {
      if (noTimer || timer != null) return;
      timer = new System.Threading.Timer(o => {
        try {
          if (serverTimeLast > DateTime.Now - silenceInterval) return;
          timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
          RaiseLoggedOff();
          LogOn();
          timer.Change(TimeSpan.Zero, silenceInterval);
        } catch (Exception exc) { RaiseLoginError(exc); }
      },
      null, TimeSpan.Zero, silenceInterval);
    }
    public bool LogOn(string user, string password, bool isDemo) {
      return LogOn(user, password, "", isDemo);
    }
    public bool ReLogin() {
      Logout();
      return LogOn();
    }
    object loginLocker = new object();
    public bool LogOn() {
      lock (loginLocker) {
        return LogOn(user, password, URL, isDemo);
      }
    }
    public bool LogOn(string user, string password, string url, bool isDemo) {
      if (!IsLoggedIn) {
        this.user = user;
        this.password = password;
        this.isDemo = isDemo;
        this.URL = url + "" != "" ? url : DefaultUrl;
        Logout();
        try {
          Desk.SetTimeout(Desk.TIMEOUT_PRICEHISTORY, 30000);
          Desk.SetTimeout(Desk.TIMEOUT_COMMON, 60*1000);
          Desk.SetRowsFilterType(Desk.ROWSFILTER_EXTENDED);
          Desk.Login(this.user, this.password, this.URL, this.isDemo ? "Demo" : "Real");
          InitTimer();
        } catch (Exception e) {
          RaiseLoginError(e);
          return false;
        }
        RaiseLoggedIn();
        _isLoggedInSubscription = _isLoggedInObserver.Subscribe(n => {
          if (!IsLoggedIn)
            LogOn();
        });
      }
      return true;
    }
    bool _isInLogOut = false;
    public void Logout() {
      _isInLogOut = true;
      try {
        Unsubscribe();
        if (_isLoggedInSubscription != null)
          _isLoggedInSubscription.Dispose();
        RaiseLoggingOff();
        if (mCore != null) {
          if (IsLoggedIn) {
            try { Desk.Logout(); } catch { }
            _mDesk = null;
          }
          try { RaiseLoggedOff(); } catch { }
        }
      } catch { } finally { _isInLogOut = false; }
    }

    IObservable<long> _isLoggedInObserver = Observable.Interval(TimeSpan.FromMinutes(1));

    public bool IsLoggedIn { get { 
      lock(_deskLocker)
      try { return IsInVirtualTrading || Desk != null && Desk.IsLoggedIn(); } catch { return false; } } }

    public void SetOfferSubscription(string pair) {
      Desk.SetOfferSubscription(pair, "Enabled");
    }

    #region IDisposable Members

    public void Dispose() {
      Logout();
      _mDesk = null;
      this.mSink = null;
    }

    #endregion

    public const string SessionStatusPropertyName = "SessionStatus";
    private TradingServerSessionStatus _sessionStatus =  TradingServerSessionStatus.Disconnected;
    private IDisposable _isLoggedInSubscription;
    public TradingServerSessionStatus SessionStatus {
      get { return _sessionStatus; }
      set {
        if (_isInLogOut || _sessionStatus == value) {
          return;
        }

        var oldValue = _sessionStatus;
        _sessionStatus = value;

        // Update bindings, no broadcast
        RaisePropertyChanged(SessionStatusPropertyName);

        // Update bindings and broadcast change using GalaSoft.MvvmLight.Messenging
        //RaisePropertyChanged(SessionStatusPropertyName, oldValue, value, true);
      }
    }
  }
  public class LoggedInEventArgs : EventArgs {
    public bool IsInVirtualTrading { get; set; }
    public LoggedInEventArgs(bool isInVirtualTrading) {
      this.IsInVirtualTrading = isInVirtualTrading;
    }
  }
}
