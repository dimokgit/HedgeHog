using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using O2G = Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.ServiceModel;
using System.Windows.Data;
using System.Windows.Input;
using HedgeHog.Alice.Client.Commands;
using HedgeHog.Shared;
namespace HedgeHog.Alice.Client {
  class TraderModel:HedgeHog.Models.ModelBase {
    #region FXCM
    private Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    public bool isLoggedIn { get { return CoreFX.IsLoggedIn; } }
    #endregion

    ThreadScheduler GetTradesScheduler;
    
    #region ServerTrades
    ObservableCollection<Trade> _localTrades = new ObservableCollectionEx<Trade>();
    public ObservableCollection<Trade> LocalTrades {
      get { return _localTrades; }
      set { _localTrades = value; RaisePropertyChangedCore(); }
    }
    public ListCollectionView LocalTradesList { get; set; }

    ObservableCollection<Trade> _serverTrades = new ObservableCollectionEx<Trade>();
    public ObservableCollection<Trade> ServerTrades {
      get { return _serverTrades; }
      set { _serverTrades = value; RaisePropertyChangedCore(); }
    }
    public ListCollectionView ServerTradesList { get; set; }
    #endregion

    #region ServerAddress
    string _serverAddress="";
    string serverAddressPostfix = "HedgeHog.Alice.WCF";
    string serverAddressSuffix = "net.tcp://";
    public string ServerAddress {
      get { return Path.Combine(serverAddressSuffix, _serverAddress, serverAddressPostfix); }
      set { _serverAddress = value; }
    }
    #endregion
    
    #region AliceMode
    int _aliceMode = (int)AliceModes.Neverland;
    public int AliceMode {
      private get { return _aliceMode; }
      set { _aliceMode = value; RaisePropertyChanged(() => Title); }
    }
    #endregion
    
    #region ServerTime
    DateTime _serverTime;
    public DateTime ServerTime {
      get { return _serverTime; }
      set { _serverTime = value; RaisePropertyChangedCore(); }
    }
    #endregion

    #region Log
    public string LogText { get { return string.Join(Environment.NewLine, _logQueue.Reverse()); } }
    Queue<string> _logQueue = new Queue<string>();
    Exception _log;
    Exception Log {
      get { return _log; }
      set { 
        _log = value;
        if (_logQueue.Count > 5) _logQueue.Dequeue();
        _logQueue.Enqueue(DateTime.Now.ToString("[dd HH:mm:ss] ") + value.Message);
        IsLogExpanded = true;
        RaisePropertyChanged(() => LogText); }
    }

    bool _isLogExpanded;
    public bool IsLogExpanded {
      get { return _isLogExpanded; }
      set { _isLogExpanded = value; RaisePropertyChangedCore(); }
    }
    #endregion

    #region Commanding
    #region Login command
    ICommand _loginCommand;
    public ICommand LoginCommand {
      get {
        if (_loginCommand== null) {
          _loginCommand = new DelegateCommand(Login, () => true);
        }

        return _loginCommand;
      }
    }
    #endregion
    #region Open Trade command
    ICommand _openTradeCommand;
    public ICommand OpenTradeCommand {
      get {
        if (_openTradeCommand == null) {
          _openTradeCommand = new DelegateCommand(OpenTrade, () => true);
        }

        return _openTradeCommand;
      }
    }

    private void OpenTrade() {
      try {
        FXW.FixOrderOpen("EUR/USD", true, 1000, 0, 0, "Dimok");
      } catch (Exception exc) { Log = exc; }
    }
    #endregion


    #endregion

    #region Trading Info
    public string TradingAccount { get; set; }
    public string TradingPassword { get; set; }
    public bool? TradingMode { get; set; }
    #endregion

    public string Title { get { return (AliceModes)AliceMode + ""; } }

    #region Ctor
    public TraderModel() {
      CoreFX.LoggedInEvent += (s, e) => Log = new Exception("User logged in.");
      CoreFX.LoginError += exc => Log = exc;
      
      ServerTradesList = new ListCollectionView(ServerTrades);
      LocalTradesList = new ListCollectionView(LocalTrades);

      GetTradesScheduler = new ThreadScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 
        () => {
          Using(tsc => {
            var serverTrades = tsc.GetTrades().ToList();
            var localTrades = isLoggedIn ? FXW.GetTrades("") : new Trade[] { };
            ServerTradesList.Dispatcher.BeginInvoke(new Action(() => {
              ServerTrades.Clear();
              serverTrades.ForEach(t => ServerTrades.Add(t));

              LocalTrades.Clear();
              localTrades.ToList().ForEach(t => LocalTrades.Add(t));

              ServerTime = DateTime.Now;
            }));
          });
        },
        (s, e) => { Log = e.Exception; });
    }
    #endregion

    #region FXCM
    void Login() {
      try {
        CoreFX.LogOn(TradingAccount, TradingPassword, TradingMode.GetValueOrDefault(true));
        RaisePropertyChanged(() => isLoggedIn);
      } catch (Exception exc) { Log = exc; }
    }
    #endregion

    #region WCF Service
    public void Using(Action<TraderService.TraderServiceClient> action) {
      var service = new TraderService.TraderServiceClient("NetTcpBinding_ITraderService", ServerAddress);
      bool success = false;
      try {
        action(service);
        if (service.State != CommunicationState.Faulted) {
          service.Close();
          success = true;
        }
      } finally {
        if (!success) {
          service.Abort();
        }
      }
    }
    #endregion
  }
  public enum AliceModes {Neverland = 0, Wonderland = 1, Mirror = 2 }
}
