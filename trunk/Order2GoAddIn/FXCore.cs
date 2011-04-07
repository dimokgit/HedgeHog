using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;


namespace Order2GoAddIn {
  public class CoreFX :IDisposable{
    public bool IsInVirtualTrading { get; set; }
    FXCore.CoreAut mCore = new FXCore.CoreAut();
    public FXCore.TradeDeskAut mDesk = null;
    public delegate void LoginErrorHandler(Exception exc);
    public event LoginErrorHandler LoginError;
    private void RaiseLoginError(Exception exc) {
      if (LoginError != null) LoginError(exc);
    }
    public event EventHandler<LoggedInEventArgs> LoggedInEvent;
    private void RaiseLoggedIn() {
      if (LoggedInEvent != null) LoggedInEvent(this, new LoggedInEventArgs(IsInVirtualTrading));
    }

    //public event EventHandler<LoggedInEventArgs> LoggedOffEvent;
    //private void RaiseLoggedOff() {
    //  if (LoggedOffEvent != null) LoggedOffEvent(this, new LoggedInEventArgs(IsInVirtualTrading));
    //}


    event EventHandler<LoggedInEventArgs> LoggedOffEventEvent;
    public event EventHandler<LoggedInEventArgs> LoggedOffEvent {
      add {
        if (LoggedOffEventEvent == null || !LoggedOffEventEvent.GetInvocationList().Contains(value))
          LoggedOffEventEvent += value;
      }
      remove {
        LoggedOffEventEvent -= value;
      }
    }
    void RaiseLoggedOff() {
      if (LoggedOffEventEvent != null) LoggedOffEventEvent(this, new LoggedInEventArgs(IsInVirtualTrading));
    }


    System.Threading.Timer timer;
    object _deskLocker = new object();
    public FXCore.TradeDeskAut Desk { get { return mDesk; } }
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
        if (Desk == null) return DateTime.Now;
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
          mDesk = (FXCore.TradeDeskAut)mCore.CreateTradeDesk("trader");
          mDesk.SetTimeout(mDesk.TIMEOUT_PRICEHISTORY, 30000);
          mDesk.SetTimeout(mDesk.TIMEOUT_COMMON, 60*1000);
          mDesk.Login(this.user, this.password, this.URL, this.isDemo ? "Demo" : "Real");
          InitTimer();
        } catch (Exception e) {
          RaiseLoginError(e);
          return false;
        }
      }
      RaiseLoggedIn();
      return true;
    }
    public void Logout() {
      try {
        if (mCore != null) {
          if (IsLoggedIn)
            try { mDesk.Logout(); } catch { }
          try { RaiseLoggedOff(); } catch { }
        }
      } catch { }
    }
    public bool IsLoggedIn { get { 
      lock(_deskLocker)
      try { return IsInVirtualTrading || Desk != null && Desk.IsLoggedIn(); } catch { return false; } } }

    public void SetOfferSubscription(string pair) {
      Desk.SetOfferSubscription(pair, "Enabled");
    }

    #region IDisposable Members

    public void Dispose() {
      Logout();
    }

    #endregion
  }
  public class LoggedInEventArgs : EventArgs {
    public bool IsInVirtualTrading { get; set; }
    public LoggedInEventArgs(bool isInVirtualTrading) {
      this.IsInVirtualTrading = isInVirtualTrading;
    }
  }
}
