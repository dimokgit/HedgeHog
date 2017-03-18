using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HedgeHog.Shared;
using IBApi;
using HedgeHog;
using System.Runtime.CompilerServices;

public class IBClientCore : IBClient, ICoreFX {
  #region Fields
  EReaderMonitorSignal _signal;
  private int _port;
  private string _host;
  private int _clientId;
  #endregion

  private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  #region ICoreEX Implementation
  public bool IsInVirtualTrading {
    get {
      throw new NotImplementedException();
    }

    set {
      throw new NotImplementedException();
    }
  }

  public bool IsLoggedIn => ClientSocket.IsConnected();

  TradingServerSessionStatus _sessionStatus;
  public TradingServerSessionStatus SessionStatus {
    get {
      return _sessionStatus;
    }

    set {
      if(_sessionStatus == value)
        return;
      _sessionStatus = value;
      NotifyPropertyChanged();
    }
  }

  public DateTime ServerTime {
    get {
      throw new NotImplementedException();
    }
  }

  private EventHandler<LoggedInEventArgs> LoggedInEvent;

  public event EventHandler<LoggedInEventArgs>  LoggedIn {
    add {
      if (LoggedInEvent == null || !LoggedInEvent.GetInvocationList().Contains(value))
        LoggedInEvent += value;
    }
    remove {
      LoggedInEvent -= value;
    }
  }

  event LoginErrorHandler LoginErrorEvent;
  public event LoginErrorHandler  LoginError {
    add {
      if (LoginErrorEvent == null || !LoginErrorEvent.GetInvocationList().Contains(value))
        LoginErrorEvent += value;
    }
    remove {
      LoginErrorEvent -= value;
    }
  }
  private void RaiseLoginError(Exception exc) {
    LoginErrorEvent?.Invoke(exc);
  }


  #region ErrorEx Event

  public event Action<int, int, string, Exception> ErrorExEvent;
  public event Action<int, int, string, Exception> ErrorEx {
    add {
      if (ErrorExEvent == null || !ErrorExEvent.GetInvocationList().Contains(value))
        ErrorExEvent += value;
    }
    remove {
      ErrorExEvent -= value;
    }
  }
  protected void RaiseErrorEx(int arg1,int arg2,string arg3,Exception arg4) {
    ErrorExEvent?.Invoke(arg1, arg2, arg3, arg4);
  }
  #endregion


  public event EventHandler<LoggedInEventArgs> LoggedOff;
  public event EventHandler<LoggedInEventArgs> LoggingOff;
  public event PropertyChangedEventHandler PropertyChanged;
  #endregion

  #region Ctor
  public static IBClientCore Create() {
    var signal = new EReaderMonitorSignal();
    return new IBClientCore(signal) { _signal = signal };
  }
  public IBClientCore(EReaderSignal signal) :base(signal) {
    Error += OnError;
    ConnectionClosed += OnConnectionClosed;
    ConnectionOpend += OnConnectionOpend;
  }

  private void OnConnectionOpend() {
    SessionStatus = TradingServerSessionStatus.Connected;
  }

  private void OnConnectionClosed() {
    SessionStatus = TradingServerSessionStatus.Disconnected;
  }
  #endregion

  #region Events
  private void OnError(int arg1, int arg2, string arg3, Exception exc) {
    if(exc is System.Net.Sockets.SocketException && !ClientSocket.IsConnected())
      RaiseLoginError(exc);
    else
      RaiseErrorEx(arg1, arg2, arg3, exc);
  }
  #endregion


  public void Disconnect() {
    ClientSocket.eDisconnect();
  }
  public void Connect( int port, string host, int clientId) {
    _port = port;
    _host = host;
    _clientId = clientId;
    if(host.IsNullOrWhiteSpace())
      host = "127.0.0.1";
    try {
      ClientId = clientId;
      ClientSocket.eConnect(host, port, ClientId);

      var reader = new EReader(ClientSocket, _signal);

      reader.Start();

      new Thread(() => { while(ClientSocket.IsConnected()) { _signal.waitForSignal(); reader.processMsgs(); } }) { IsBackground = true }.Start();
    } catch(Exception exc) {
      //HandleMessage(new ErrorMessage(-1, -1, "Please check your connection attributes.") + "");
      RaiseLoginError(exc);
    }
  }

  public bool LogOn(string user, string accountSubId, string password, bool isDemo) {
    throw new NotImplementedException();
  }

  public void Logout() {
    Disconnect();
  }

  public bool ReLogin() {
    Disconnect();
    Connect(_port, _host, _clientId);
    return true;
  }

  public void SetOfferSubscription(string pair) {
    throw new NotImplementedException();
  }

  #region ICoreFx Implementation
  #endregion
}
