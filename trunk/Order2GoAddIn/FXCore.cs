using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Order2GoAddIn {
  public class CoreFX :IDisposable{
    FXCore.CoreAut mCore = null;
    public FXCore.TradeDeskAut mDesk = null;
    public delegate void LoginErrorHandler(Exception exc);
    public event LoginErrorHandler LoginError;
    private void RaiseLoginError(Exception exc) {
      if (LoginError != null) LoginError(exc);
    }
    public event EventHandler<EventArgs> LoggedInEvent;
    private void RaiseLoggedIn() {
      if (LoggedInEvent != null) LoggedInEvent(this, new EventArgs());
    }
    public event EventHandler<EventArgs> LoggedOffEvent;
    private void RaiseLoggedOff() {
      if (LoggedOffEvent != null) LoggedOffEvent(this, new EventArgs());
    }
    System.Threading.Timer timer;
    public FXCore.TradeDeskAut Desk { get { return mDesk; } }
    public bool LoggedIn { get { return mDesk != null; } }
    string user = "";
    string password = "";
    bool isDemo = true;
    public bool IsDemo { get { return isDemo; } }
    string URL = "";
    bool noTimer;
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
    public CoreFX():this(false) { }
    public CoreFX(bool noTimer) {
      this.noTimer = noTimer;
    }
    ~CoreFX() {
      Logout();
    }
    #endregion

    public string[] Instruments { 
      get {
        var instruments = Desk.GetInstruments() as FXCore.StringEnumAut;
        List<string> l = new List<string>();
        foreach (var i in instruments)
          l.Add(i+"");
        return l.ToArray();
      } 
    }

    TimeSpan silenceInterval = TimeSpan.FromSeconds(60*5);
    void InitTimer() {
      if (noTimer || timer != null) return;
      timer = new System.Threading.Timer(o => {
        try {
          if (serverTimeLast > DateTime.Now - silenceInterval) return;
          timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
          RaiseLoggedOff();
          mDesk = null;
          mCore = null;
          LogOn();
          timer.Change(TimeSpan.Zero, silenceInterval);
        } catch (Exception exc) { RaiseLoginError(exc); }
      },
      null, TimeSpan.Zero, silenceInterval);
    }
    public bool LogOn(string user, string password, bool isDemo) {
      return LogOn(user, password, "", isDemo);
    }
    public bool LogOn() {
      return LogOn(user, password, URL, isDemo);
    }
    public bool LogOn(string user, string password, string url, bool isDemo) {
      if (!IsLoggedIn) {
        this.user = user;
        this.password = password;
        this.isDemo = isDemo;
        this.URL = url + "" != "" ? url : DefaultUrl;
        Logout();
        try {
          mCore = new FXCore.CoreAut();
          mDesk = (FXCore.TradeDeskAut)mCore.CreateTradeDesk("trader");
          mDesk.SetTimeout(mDesk.TIMEOUT_PRICEHISTORY, 30000);
          mDesk.Login(this.user, this.password, this.URL, this.isDemo ? "Demo" : "Real");
          InitTimer();
        } catch (Exception e) {
          mCore = null;
          mDesk = null;
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
          if (IsLoggedIn) {
            try { mDesk.Logout(); } catch { }
            RaiseLoggedOff();
            mDesk = null;
            mCore = null;
          }
        }
      } catch { }
    }
    public bool IsLoggedIn { get { try { return Desk != null && Desk.IsLoggedIn(); } catch { return false; } } }

    #region IDisposable Members

    public void Dispose() {
      Logout();
    }

    #endregion
  }
}
