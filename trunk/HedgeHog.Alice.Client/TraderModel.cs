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
using Gala = GalaSoft.MvvmLight.Command;
using HedgeHog.Shared;
using HedgeHog.Alice.Client.TradeExtenssions;
using System.Windows;
namespace HedgeHog.Alice.Client {
  public class TraderModel:HedgeHog.Models.ModelBase {
    #region FXCM
    private Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    FXW fw;
    public bool IsLoggedIn { get { return CoreFX.IsLoggedIn; } }
    #endregion

    #region Trade Lists
    public ObservableCollection<Trade> LocalTrades { get; set; }
    public ListCollectionView LocalTradesList { get; set; }

    public ObservableCollection<Trade> ServerTrades { get; set; }
    public ListCollectionView ServerTradesList { get; set; }

    public ObservableCollection<Trade> AbsentTrades { get; set; }
    public ListCollectionView AbsentTradesList { get; set; }

    private AccountInfo serverAccountInfo = new AccountInfo() { Account = new Account() };
    public Account[] ServerAccountRow { get { return new[] { serverAccountInfo.Account }; } }

    private Account localAccount = new Account();
    public Account[] LocalAccountRow { get { return new[] { localAccount }; } }
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
    AliceModes _aliceMode = AliceModes.Neverland;
    public AliceModes AliceMode {
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
        var messages = new List<string>(new[] { DateTime.Now.ToString("[dd HH:mm:ss] ") + GetExceptionShort(value) });
        while (value.InnerException != null) {
          messages.Add(GetExceptionShort(value.InnerException));
          value = value.InnerException;
        }
        _logQueue.Enqueue(string.Join(Environment.NewLine + "-", messages));
        IsLogExpanded = true;
        RaisePropertyChanged(() => LogText); }
    }

    string GetExceptionShort(Exception exc) {
      return (exc.TargetSite == null ? "" : exc.TargetSite.DeclaringType.Name + "." +
      exc.TargetSite.Name + ": ") + exc.Message;
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
          _loginCommand = new Gala.RelayCommand(Login, () => true);
        }

        return _loginCommand;
      }
    }
    #endregion
    #region Open Trade command
    ICommand _CloseAllServerTradesCommand;
    public ICommand CloseAllServerTradesCommand {
      get {
        if (_CloseAllServerTradesCommand == null) {
          _CloseAllServerTradesCommand = new Gala.RelayCommand(CloseAllServerTrades, () => true);
        }

        return _CloseAllServerTradesCommand;
      }
    }

    private void CloseAllServerTrades() {
      MessageBox.Show("This is test.");
    }
    private void OpenTrade(string pair,bool buy, int lots, string serverTradeID) {
      try {
        FXW.FixOrderOpen(pair, buy, lots, 0, 0, serverTradeID);
      } catch (Exception exc) { Log = exc; }
    }
    #endregion


    #endregion

    #region Trading Info
    public string TradingAccount { get; set; }
    public string TradingPassword { get; set; }
    public bool? TradingDemo { get; set; }
    #endregion

    #region Fields
    ThreadScheduler GetTradesScheduler;
    public string Title { get { return AliceMode + ""; } }
    protected bool isInDesign = false;
    #endregion

    #region Ctor
    public TraderModel() {
      CoreFX.LoggedInEvent += (s, e) => {
        RaisePropertyChanged(() => IsLoggedIn);
        Log = new Exception("User logged in.");
      };
      CoreFX.LoginError += exc => {
        Log = exc;
        RaisePropertyChanged(() => IsLoggedIn);
      };

      ServerTradesList = new ListCollectionView(ServerTrades = new ObservableCollection<Trade>());
      LocalTradesList = new ListCollectionView(LocalTrades = new ObservableCollection<Trade>());
      AbsentTradesList = new ListCollectionView(AbsentTrades = new ObservableCollection<Trade>());

      if (!isInDesign) {
        GetTradesScheduler = new ThreadScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        () => Using(FetchServerTrades),
        (s, e) => { Log = e.Exception; });
      }
    }
    #endregion

    #region FXCM
    void Login() {
      try {
        if (fw == null) fw = new FXW();
        if (CoreFX.IsLoggedIn) {
          Log = new Exception("Account is already logged in.");
        } else {
          fw.LogOn("",CoreFX, TradingAccount, TradingPassword, TradingDemo.GetValueOrDefault(true));
          fw.TradesCountChanged += t => Log = new Exception(string.Format("Trade was opened/removed"));
        }
      } catch (Exception exc) {
        Log = exc;
        MessageBox.Show(exc + "");
      }
    }

    void FetchServerTrades(TraderService.TraderServiceClient tsc) {
      RaisePropertyChanged(() => IsLoggedIn);
      serverAccountInfo = tsc.GetTrades();
      RaisePropertyChanged(() => ServerAccountRow);
      var serverTrades = serverAccountInfo.Trades.ToList();
      var localTrades = (IsLoggedIn ? FXW.GetTrades("") : new Trade[] { }).ToList();
      localAccount = FXW.GetAccount();
      RaisePropertyChanged(() => LocalAccountRow);
      ServerTradesList.Dispatcher.BeginInvoke(new Action(() => Syncronize(serverTrades, localTrades)));
    }

    private void Syncronize(List<Trade> serverTrades, List<Trade> localTrades) {
      ShowTrades(serverTrades, ServerTrades);
      if (CoreFX.IsLoggedIn) {
        ShowTrades(localTrades, LocalTrades);

        #region Clone trades
        var absentTrades = (from ts in serverTrades
                            join tl in localTrades on ts.Id equals tl.Remark.Remark into svrTrds
                            from st in svrTrds.DefaultIfEmpty()
                            where st == null
                            select ts).ToList();

        AbsentTrades.Clear();
        absentTrades.ForEach(a => AbsentTrades.Add(a.InitUnKnown(fw.ServerTime)));

        var tradeToCopy = AbsentTrades.FirstOrDefault(t => t.GetUnKnown().CanSync);
        if (tradeToCopy != null) {
          if (AliceMode != AliceModes.Neverland) {
            var buy = AliceMode == AliceModes.Wonderland ? tradeToCopy.Buy : !tradeToCopy.Buy;
            OpenTrade(tradeToCopy.Pair, buy, tradeToCopy.Lots, tradeToCopy.Id);
            Log = new Exception(string.Format("Trade {0} is being clonned", tradeToCopy.Id));
          }
        }
        #endregion

        var tradesToClose = (from tl in localTrades
                             join ts in serverTrades on tl.Remark.Remark equals ts.Id into lclTrds
                            from st in lclTrds.DefaultIfEmpty()
                            where st == null
                            select tl).ToList();
        var tradeToClose = tradesToClose.FirstOrDefault();
        if (tradeToClose != null) {
          FXW.FixOrderClose(tradeToClose.Id);
          Log = new Exception("Closing trade " + tradeToClose.Id);
        }
      }
      ServerTime = DateTime.Now;
    }

    private void ShowTrades(List<Trade> tradesList, ObservableCollection<Trade> tradesCollection) {
      tradesCollection.Clear();
      tradesList.ForEach(a => tradesCollection.Add(a));
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
  public class TraderModelDesign : TraderModel {
    public TraderModelDesign() {
      isInDesign = true;
    }
  }
  public enum AliceModes {Neverland = 0, Wonderland = 1, Mirror = 2 }
}
