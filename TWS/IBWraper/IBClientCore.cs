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
using static EpochTimeExtensions;
using IBApp;

public class IBClientCore : IBClient, ICoreFX {
  #region Fields
  EReaderMonitorSignal _signal;
  private int _port;
  private string _host;
  internal TimeSpan _serverTimeOffset;
  private string _managedAccount;
  private readonly Action<object> _trace;
  private AccountManager _accountManager;
  TradingServerSessionStatus _sessionStatus;
  readonly private MarketDataManager _marketDataManager;
  #endregion

  #region Properties
  public AccountManager AccountManager { get { return _accountManager; } }
  #endregion

  #region ICoreEX Implementation
  public void SetOfferSubscription(string pair) {
    var c = pair.IsCurrenncy() ? ContractSamples.FxContract(pair) : ContractSamples.Commodity(pair);
    _marketDataManager.AddRequest(c);
  }
  public bool IsInVirtualTrading { get; set; }

  public DateTime ServerTime {
    get {
      return DateTime.Now + _serverTimeOffset;
    }
  }

  public event EventHandler<LoggedInEventArgs> LoggedOff;
  public event EventHandler<LoggedInEventArgs> LoggingOff;
  public event PropertyChangedEventHandler PropertyChanged;
  #endregion

  #region Ctor
  public static IBClientCore Create(Action<object> trace) {
    var signal = new EReaderMonitorSignal();
    return new IBClientCore(signal, trace) { _signal = signal };
  }
  public IBClientCore(EReaderSignal signal, Action<object> trace) : base(signal) {
    _trace = trace;
    Error += OnError;
    ConnectionClosed += OnConnectionClosed;
    ConnectionOpend += OnConnectionOpend;
    CurrentTime += OnCurrentTime;
    ManagedAccounts += OnManagedAccounts;
    _marketDataManager = new MarketDataManager(this);
    _marketDataManager.PriceChanged += OnPriceChanged;
  }
  public Price GetPrice(string symbol) { return _marketDataManager.GetPrice(symbol); }
  #region Price Changed
  private void OnPriceChanged(Price price) {
    RaisePriceChanged(price);
  }


  #region PriceChanged Event
  event PriceChangedEventHandler PriceChangedEvent;
  public event PriceChangedEventHandler  PriceChanged {
    add {
      if (PriceChangedEvent == null || !PriceChangedEvent.GetInvocationList().Contains(value))
        PriceChangedEvent += value;
    }
    remove {
      PriceChangedEvent -= value;
    }
  }
  protected void RaisePriceChanged(Price price) {
    price.Time2 = ServerTime;
    PriceChangedEvent?.Invoke(price);
  }
  #endregion

  #endregion
  private void OnManagedAccounts(string obj) {
    if(_accountManager != null)
      _trace(new { _accountManager, isNot = (string)null });
    var ma = obj.Splitter('.').Where(a => _managedAccount.IsNullOrWhiteSpace() || a == _managedAccount).SingleOrDefault();
    if(ma == null)
      throw new Exception(new { _managedAccount, error = "Not Found" } + "");
    _accountManager = new AccountManager(this, ma, CommissionByTrade, _trace);
    _accountManager.RequestAccountSummary();
    _accountManager.SubscribeAccountUpdates();
    _accountManager.RequestPositions();
  }

  private void OnCurrentTime(long obj) {
    var ct = obj.ToDateTimeFromEpoch(DateTimeKind.Utc).ToLocalTime();
    _serverTimeOffset = ct - DateTime.Now;
  }
  private void OnConnectionOpend() {
    ClientSocket.reqCurrentTime();
    SessionStatus = TradingServerSessionStatus.Connected;
  }

  private void OnConnectionClosed() {
    SessionStatus = TradingServerSessionStatus.Disconnected;
  }
  #endregion

  #region Events
  public void RaiseError(Exception exc) {
    ((EWrapper)this).error(exc);
  }
  private void OnError(int arg1, int arg2, string arg3, Exception exc) {
    if(exc is System.Net.Sockets.SocketException && !ClientSocket.IsConnected())
      RaiseLoginError(exc);
  }
  #endregion

  #region Connect/Disconnect
  public void Disconnect() {
    if(!IsInVirtualTrading && ClientSocket.IsConnected())
      ClientSocket.eDisconnect();
  }
  public void Connect(int port, string host, int clientId) {
    _port = port;
    _host = host;
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
  #endregion


  #region ICoreFx Implemented

  #region Events
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
  private void RaiseLoggedIn() {
    LoggedInEvent?.Invoke(this, new LoggedInEventArgs(IsInVirtualTrading));
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
  #endregion

  public override string ToString() {
    return new { Host = _host, Port = _port, ClientId } + "";
  }

  #region Log(In/Out)
  public bool LogOn(string host, string port, string clientId, bool isDemo) {
    try {
      var hosts = host.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
      _managedAccount = hosts.Skip(1).LastOrDefault();
      if(!IsInVirtualTrading) {
        int iPort;
        if(!int.TryParse(port, out iPort))
          throw new ArgumentException("Value is not integer", nameof(port));
        int iClientId;
        if(!int.TryParse(clientId, out iClientId))
          throw new ArgumentException("Value is not integer", nameof(port));
        Connect(iPort, hosts.FirstOrDefault(), iClientId);
      }
      RaiseLoggedIn();
      return IsLoggedIn;
    } catch(Exception exc) {
      RaiseLoginError(exc);
      return false;
    }
  }

  public void Logout() {
    Disconnect();
  }

  public bool ReLogin() {
    Disconnect();
    Connect(_port, _host, ClientId);
    return true;
  }
  public bool IsLoggedIn => IsInVirtualTrading || ClientSocket.IsConnected();

  public TradingServerSessionStatus SessionStatus {
    get {
      return IsInVirtualTrading ? TradingServerSessionStatus.Connected : _sessionStatus;
    }

    set {
      if(_sessionStatus == value)
        return;
      _sessionStatus = value;
      NotifyPropertyChanged();
    }
  }

  public Func<Trade, double> CommissionByTrade { get; internal set; }

  #endregion

  #endregion
  private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}
