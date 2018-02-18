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
using HedgeHog.Core;
using System.Collections.Specialized;
using System.Configuration;
using MongoDB.Bson;
using System.Reactive.Linq;

public class IBClientCore :IBClient, ICoreFX {
  class Configer {
    static NameValueCollection section;
    public static int[] WarningCodes;
    static Configer() {
      section = ConfigurationManager.GetSection("IBSettings") as NameValueCollection;
      var mongoUri = ConfigurationManager.AppSettings["MongoUri"];
      var mongoCollection = ConfigurationManager.AppSettings["MongoCollection"];
      try {
        var codeAnon = new { id = ObjectId.Empty, codes = new int[0] };
        WarningCodes = MongoExtensions.ReadCollectionAnon(codeAnon, mongoUri, "forex", mongoCollection).SelectMany(x => x.codes).ToArray();
      } catch(Exception exc) {
        throw new Exception(new { mongoUri, mongoCollection } + "", exc);
      }
    }
  }
  #region Fields
  EReaderMonitorSignal _signal;
  private int _port;
  private string _host;
  internal TimeSpan _serverTimeOffset;
  private string _managedAccount;
  public string ManagedAccount { get => _managedAccount; }
  private readonly Action<object> _trace;
  TradingServerSessionStatus _sessionStatus;
  readonly private MarketDataManager _marketDataManager;
  private static int _validOrderId;
  #endregion

  #region Properties
  public Action<object> Trace => _trace;
  private bool _verbose = true;
  public void Verbouse(object o) { if(_verbose) _trace(o); }
  #endregion

  #region ICoreEX Implementation
  public void SetOfferSubscription(string pair) => _marketDataManager.AddRequest(ContractSamples.ContractFactory(pair), "233,221,236");
  public bool IsInVirtualTrading { get; set; }
  public DateTime ServerTime => DateTime.Now + _serverTimeOffset;
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
    NextValidId += OnNextValidId;
    Error += OnError;
    ConnectionClosed += OnConnectionClosed;
    ConnectionOpend += OnConnectionOpend;
    CurrentTime += OnCurrentTime;
    _marketDataManager = new MarketDataManager(this);
    _marketDataManager.PriceChanged += OnPriceChanged;
  }

  private static object _validOrderIdLock = new object();
  private void OnNextValidId(int obj) {
    lock(_validOrderIdLock) {
      _validOrderId = obj + 1;
    }
    Verbouse(new { _validOrderId });
  }
  /// <summary>
  /// https://docs.microsoft.com/en-us/dotnet/api/system.threading.interlocked.increment?f1url=https%3A%2F%2Fmsdn.microsoft.com%2Fquery%2Fdev15.query%3FappId%3DDev15IDEF1%26l%3DEN-US%26k%3Dk(System.Threading.Interlocked.Increment);k(SolutionItemsProject);k(TargetFrameworkMoniker-.NETFramework,Version%3Dv4.6.2);k(DevLang-csharp)%26rd%3Dtrue&view=netframework-4.7.1
  /// </summary>
  /// <returns></returns>
  public int ValidOrderId() {
    //ClientSocket.reqIds(1);
    lock(_validOrderIdLock) {
      try {
        return Interlocked.Increment(ref _validOrderId);
      } finally {
        Trace(new { _validOrderId });
      }
    }
  }
  public bool TryGetPrice(string symbol, out Price price) { return _marketDataManager.TryGetPrice(symbol, out price); }
  public Price GetPrice(string symbol) { return _marketDataManager.GetPrice(symbol); }
  #region Price Changed
  private void OnPriceChanged(Price price) {
    RaisePriceChanged(price);
  }


  #region PriceChanged Event
  event EventHandler<PriceChangedEventArgs> PriceChangedEvent;
  public event EventHandler<PriceChangedEventArgs> PriceChanged {
    add {
      if(PriceChangedEvent == null || !PriceChangedEvent.GetInvocationList().Contains(value))
        PriceChangedEvent += value;
    }
    remove {
      PriceChangedEvent -= value;
    }
  }
  protected void RaisePriceChanged(Price price) {
    price.Time2 = ServerTime;
    PriceChangedEvent?.Invoke(this, new PriceChangedEventArgs(price, null, null));
  }
  #endregion

  #endregion

  #region OrderAddedEvent
  event EventHandler<OrderEventArgs> OrderAddedEvent;
  public event EventHandler<OrderEventArgs> OrderAdded {
    add {
      if(OrderAddedEvent == null || !OrderAddedEvent.GetInvocationList().Contains(value))
        OrderAddedEvent += value;
    }
    remove {
      OrderAddedEvent -= value;
    }
  }

  void RaiseOrderAdded(object sender, OrderEventArgs args) => OrderAddedEvent?.Invoke(sender, args);
  #endregion
  #region OrderRemovedEvent
  event OrderRemovedEventHandler OrderRemovedEvent;
  public event OrderRemovedEventHandler OrderRemoved {
    add {
      if(OrderRemovedEvent == null || !OrderRemovedEvent.GetInvocationList().Contains(value))
        OrderRemovedEvent += value;
    }
    remove {
      OrderRemovedEvent -= value;
    }
  }

  void RaiseOrderRemoved(HedgeHog.Shared.Order args) => OrderRemovedEvent?.Invoke(args);
  #endregion


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

  static int[] _warningCodes = new[] { 2104, 2106, 2108 };
  static bool IsWarning(int code) => Configer.WarningCodes.Contains(code);
  System.Collections.Concurrent.ConcurrentBag<int> _handledReqErros = new System.Collections.Concurrent.ConcurrentBag<int>();
  public int SetRequestHandled(int id) {
    _handledReqErros.Add(id); return id;
  }
  private void OnError(int id, int errorCode, string message, Exception exc) {
    if(_handledReqErros.Contains(id)) return;
    if(IsWarning(errorCode)) return;
    if(exc is System.Net.Sockets.SocketException && !ClientSocket.IsConnected())
      RaiseLoginError(exc);
    if(exc != null) 
      Trace(exc);
    else
      Trace(new { IBCC = new { id, error = errorCode, message } });
  }

  #region TradeAdded Event
  //public class TradeEventArgs : EventArgs {
  //  public Trade Trade { get; private set; }
  //  public TradeEventArgs(Trade trade) : base() {
  //    Trade = trade;
  //  }
  //}
  event EventHandler<TradeEventArgs> TradeAddedEvent;
  public event EventHandler<TradeEventArgs> TradeAdded {
    add {
      if(TradeAddedEvent == null || !TradeAddedEvent.GetInvocationList().Contains(value))
        TradeAddedEvent += value;
    }
    remove {
      TradeAddedEvent -= value;
    }
  }
  protected void RaiseTradeAdded(Trade trade) {
    if(TradeAddedEvent != null)
      TradeAddedEvent(this, new TradeEventArgs(trade));
  }
  #endregion

  #region TradeRemoved Event
  event EventHandler<TradeEventArgs> TradeRemovedEvent;
  public event EventHandler<TradeEventArgs> TradeRemoved {
    add {
      if(TradeRemovedEvent == null || !TradeRemovedEvent.GetInvocationList().Contains(value))
        TradeRemovedEvent += value;
    }
    remove {
      TradeRemovedEvent -= value;
    }
  }
  protected void RaiseTradeRemoved(Trade trade) {
    if(TradeRemovedEvent != null)
      TradeRemovedEvent(this, new TradeEventArgs(trade));
  }
  #endregion

  #region TradeClosedEvent
  event EventHandler<TradeEventArgs> TradeClosedEvent;
  public event EventHandler<TradeEventArgs> TradeClosed {
    add {
      if(TradeClosedEvent == null || !TradeClosedEvent.GetInvocationList().Contains(value))
        TradeClosedEvent += value;
    }
    remove {
      if(TradeClosedEvent != null)
        TradeClosedEvent -= value;
    }
  }
  void RaiseTradeClosed(Trade trade) {
    if(TradeClosedEvent != null)
      TradeClosedEvent(this, new TradeEventArgs(trade));
  }
  #endregion

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

      if(ClientSocket.IsConnected())
        throw new Exception(nameof(ClientSocket) + " is already connected");
      ClientSocket.eConnect(host, port, ClientId);
      if(!ClientSocket.IsConnected()) return;

      var reader = new EReader(ClientSocket, _signal);

      reader.Start();

      Task.Factory.StartNew(() => {
        while(ClientSocket.IsConnected() && !reader.MessageQueueThread.IsCompleted) {
          _signal.waitForSignal();
          reader.processMsgs();
        }
      }, TaskCreationOptions.LongRunning)
      .ContinueWith(t => {
        Observable.Interval(TimeSpan.FromSeconds(5))
        .Select(_ => {
          try {
            RaiseError(new Exception(new { ClientSocket = new { IsConnected = ClientSocket.IsConnected() } } + ""));
            ReLogin();
            if(ClientSocket.IsConnected()) {
              RaiseLoggedIn();
              return true;
            };
          } catch {
          }
          return false;
        })
        .TakeWhile(b => b == false)
        .Subscribe();
      });

    } catch(Exception exc) {
      //HandleMessage(new ErrorMessage(-1, -1, "Please check your connection attributes.") + "");
      RaiseLoginError(exc);
    }
  }
  #endregion


  #region ICoreFx Implemented

  #region Events
  private EventHandler<LoggedInEventArgs> LoggedInEvent;
  public event EventHandler<LoggedInEventArgs> LoggedIn {
    add {
      if(LoggedInEvent == null || !LoggedInEvent.GetInvocationList().Contains(value))
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
  public event LoginErrorHandler LoginError {
    add {
      if(LoginErrorEvent == null || !LoginErrorEvent.GetInvocationList().Contains(value))
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
      if(IsLoggedIn) {
        RaiseLoggedIn();
        return IsLoggedIn;
      } else
        throw new Exception("Not logged in.");
    } catch(Exception exc) {
      RaiseLoginError(exc);
      return false;
    }
  }

  public void Logout() {
    Disconnect();
  }

  public bool ReLogin() {
    _signal.issueSignal();
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

  public Func<Trade, double> CommissionByTrade { get; set; }

  #endregion

  #endregion
  private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}
