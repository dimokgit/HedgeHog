using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Order2GoAddIn {
  public class CoreFX :IDisposable{
    static FXCore.CoreAut mCore = null;
    public static FXCore.TradeDeskAut mDesk = null;
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
    public static FXCore.TradeDeskAut Desk { get { return mDesk; } }
    public bool LoggedIn { get { return mDesk != null; } }
    string user = "";
    string password = "";
    bool isDemo = true;
    string URL = "";
    bool noTimer;
    public readonly int ServerTimeOffset = -4;
    private TimeSpan _serverTimeOffset = TimeSpan.Zero;
    public DateTime ServerTime {
      get {
        if (Desk == null) return DateTime.Now;
        if (_serverTimeOffset == TimeSpan.Zero)
          _serverTimeOffset = TimeZoneInfo.ConvertTimeFromUtc((DateTime)Desk.ServerTime, TimeZoneInfo.Local) - DateTime.Now;
        return DateTime.Now + _serverTimeOffset;
      }
    }
    public CoreFX():this(false) { }
    public CoreFX(bool noTimer) {
      this.noTimer = noTimer;
    }
    ~CoreFX() {
      Logout();
    }
    void InitTimer() {
      if (noTimer || timer != null) return;
      timer = new System.Threading.Timer(o => {
        if (ServerTime.AddMinutes(2) > DateTime.Now) return;
        LogOn();
      },
      null, TimeSpan.FromMilliseconds(0), TimeSpan.FromMinutes(2));
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
        this.URL = url + "" != "" ? url : "http://www.fxcorporate.com";
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
      if (mCore != null) {
        if (IsLoggedIn) {
          try { mDesk.Logout(); } catch { }
          RaiseLoggedOff();
          mDesk = null;
          mCore = null;
          if (timer != null)
            timer.Dispose();
        }
      }
    }
    public bool IsLoggedIn { get { try { return Desk != null && Desk.IsLoggedIn(); } catch { return false; } } }

    #region IDisposable Members

    public void Dispose() {
      Logout();
    }

    #endregion
  }
}
