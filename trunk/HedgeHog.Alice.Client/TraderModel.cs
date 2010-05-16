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
using System.Windows.Controls;
namespace HedgeHog.Alice.Client {
  public class TraderModel:HedgeHog.Models.ModelBase {
    #region FXCM
    private Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    FXW fw;
    public bool IsLoggedIn { get { return CoreFX.IsLoggedIn; } }
    ComboBoxItem _ServerToLocalRatioValue;
    public ComboBoxItem ServerToLocalRatioValue {
      set { _ServerToLocalRatioValue = value; }
    }

    double ServerToLocalRatio {
      get {
        var parts = _ServerToLocalRatioValue.Content.ToString().Split(':');
        return double.Parse(parts[0]) / double.Parse(parts[1]);
      }
    }

    #endregion

    #region Trade Lists
    public ObservableCollection<Trade> LocalTrades { get; set; }
    public ListCollectionView LocalTradesList { get; set; }
    List<Trade> serverTradesPending = new List<Trade>();

    public ObservableCollection<Trade> ServerTrades { get; set; }
    public ListCollectionView ServerTradesList { get; set; }

    public ObservableCollection<Trade> AbsentTrades { get; set; }
    public ListCollectionView AbsentTradesList { get; set; }

    private Account serverAccount = new Account();
    public Account[] ServerAccountRow { get { return new[] { serverAccount }; } }

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
    AliceModes _aliceMode;
    public AliceModes AliceMode {
      get { return _aliceMode; }
      set { 
        _aliceMode = value;
        RaisePropertyChanged(() => AliceMode, () => Title);
      }
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
        if (isInDesign) return;
        _log = value;
        var exc = value is Exception ? value : null;
        if (_logQueue.Count > 5) _logQueue.Dequeue();
        var messages = new List<string>(new[] { DateTime.Now.ToString("[dd HH:mm:ss] ") + GetExceptionShort(value) });
        while (value.InnerException != null) {
          messages.Add(GetExceptionShort(value.InnerException));
          value = value.InnerException;
        }
        _logQueue.Enqueue(string.Join(Environment.NewLine + "-", messages));
        IsLogExpanded = true;

        if (exc != null) {
          var text = "**************** Exception ***************" + Environment.NewLine;
          while (exc != null) {
            text += exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
            exc = exc.InnerException;
          }
          System.IO.File.AppendAllText(logFileName, text);
        }

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

    #region OpenNewLocalAccountCommand

    ICommand _OpenNewLocalAccountCommand;
    public ICommand OpenNewLocalAccountCommand {
      get {
        if (_OpenNewLocalAccountCommand == null) {
          _OpenNewLocalAccountCommand = new Gala.RelayCommand(OpenNewLocalAccount, () => true);
        }

        return _OpenNewLocalAccountCommand;
      }
    }
    void OpenNewLocalAccount() {
      try {
        string account, password;
        FXCM.Lib.GetNewAccount(out account, out password);
        TradingAccount = account;
        TradingPassword = password;
        Login();
      } catch (Exception exc) { Log = exc; }
    }

    #endregion

    #region OpenNewServerAccountCommand

    ICommand _OpenNewServerAccountCommand;
    public ICommand OpenNewServerAccountCommand {
      get {
        if (_OpenNewServerAccountCommand == null) {
          _OpenNewServerAccountCommand = new Gala.RelayCommand(OpenNewServerAccount, () => true);
        }

        return _OpenNewServerAccountCommand;
      }
    }
    void OpenNewServerAccount() {
      try {
        Using(tsc => {
          string account, password;
          FXCM.Lib.GetNewAccount(out account, out password);
          tsc.OpenNewAccount(account, password);
          Log = new Exception("New Rabit has arrived");
        });
      } catch (Exception exc) { Log = exc; }
    }

    #endregion

    ICommand _UpdatePasswordCommand;
    public ICommand UpdatePasswordCommand {
      get {
        if (_UpdatePasswordCommand == null) {
          _UpdatePasswordCommand = new Gala.RelayCommand<RoutedEventArgs>(UpdatePassword, (e) => true);
        }

        return _UpdatePasswordCommand;
      }
    }
    void UpdatePassword(RoutedEventArgs e) {
      TradingPassword = ((PasswordBox)(e.Source)).Password;
    }


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

    #region Close Local Trade
    ICommand _CloseLocalTradeCommand;
    public ICommand CloseLocalTradeCommand {
      get {
        if (_CloseLocalTradeCommand == null) {
          _CloseLocalTradeCommand = new Gala.RelayCommand<string>(CloseLocalTrade, id => true);
        }

        return _CloseLocalTradeCommand;
      }
    }
    void CloseLocalTrade(string tradeID) {
      try {
        var pendingTrade = serverTradesPending.FirstOrDefault(t => t.Id == tradeID);
        if (pendingTrade == null) fw.FixOrderClose(tradeID);
        else serverTradesPending.Remove(pendingTrade);
      } catch (Exception exc) { Log = exc; }
    }
    #endregion

    #region Close Server Trade
    ICommand _CloseServerTradeCommand;
    public ICommand CloseServerTradeCommand {
      get {
        if (_CloseServerTradeCommand == null) {
          _CloseServerTradeCommand = new Gala.RelayCommand<string>(CloseServerTrade, Id => true);
        }

        return _CloseServerTradeCommand;
      }
    }
    void CloseServerTrade(string tradeId) {
      Using(tsc => {
        try {
          Log = new Exception("Trade " + tradeId + " was closed with OrderId " + tsc.CloseTrade(tradeId));
        } catch (Exception exc) { Log = exc; }
      });
    }
    #endregion

    #region Close All Server Trades Command
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
      Using(tsc => {
        try {
          Log = new Exception("Closing all server trades.");
          var ordersIds = tsc.CloseAllTrades();
          Log = new Exception("Trades closed:" + string.Join(",", ordersIds));
        } catch (Exception exc) { Log = exc; }
      });
    }
    #endregion

    #region Close All Local Trades Command
    ICommand _CloseAllLocalTradesCommand;
    public ICommand CloseAllLocalTradesCommand {
      get {
        if (_CloseAllLocalTradesCommand == null) {
          _CloseAllLocalTradesCommand = new Gala.RelayCommand(CloseAllLocalTrades, () => true);
        }

        return _CloseAllLocalTradesCommand;
      }
    }
    void CloseAllLocalTrades() {
      try {
        Log = new Exception("Closing all local trades.");
        var ordersIds = fw.FixOrdersCloseAll();
        Log = new Exception("Trades closed:" + string.Join(",", ordersIds));
      } catch (Exception exc) { Log = exc; }
    }
    #endregion

    #region Sync Trade Command
    ICommand _SyncTradeCommand;
    public ICommand SyncTradeCommand {
      get {
        if (_SyncTradeCommand == null) {
          _SyncTradeCommand = new Gala.RelayCommand<string>(SyncTrade, tradeId => true);
        }
        return _SyncTradeCommand;
      }
    }

    int syncTradeCount = 0;
    void SyncTrade(string tradeId) {
      syncTradeCount++;
      try {
        var trade = ServerTrades.FirstOrDefault(t => t.Id == tradeId);
        if (trade != null) SyncTrade(trade);
      } catch (Exception exc) { Log = exc; } finally {
        syncTradeCount--;
      }

    }
    #endregion

    #region Sync All Trade Command
    bool syncAll = false;
    ICommand _SyncAllTradeCommand;
    public ICommand SyncAllTradeCommand {
      get {
        if (_SyncAllTradeCommand == null) {
          _SyncAllTradeCommand = new Gala.RelayCommand(SyncAllTrades, () => true);
        }
        return _SyncAllTradeCommand;
      }
    }
    void SyncAllTrades() { syncAll = true; }
    #endregion


    ICommand _ReverseAliceModeCommand;
    public ICommand ReverseAliceModeCommand {
      get {
        if (_ReverseAliceModeCommand == null) {
          _ReverseAliceModeCommand = new Gala.RelayCommand(ReverseAliceMode, () => true);
        }

        return _ReverseAliceModeCommand;
      }
    }
    void ReverseAliceMode() {
      if (AliceMode == AliceModes.Wonderland) AliceMode = AliceModes.Mirror;
      if (AliceMode == AliceModes.Mirror) AliceMode = AliceModes.Wonderland;
      CloseAllLocalTrades();
      SyncAllTrades();
    }

    ICommand _TestCommand;
    public ICommand TestCommand {
      get {
        if (_TestCommand == null) {
          _TestCommand = new Gala.RelayCommand(Test, () => true);
        }

        return _TestCommand;
      }
    }
    void Test() { MessageBox.Show("Click!"); }


    #endregion

    #region Trading Info
    string _tradingAccount;
    public string TradingAccount {
      get { return _tradingAccount; }
      set { _tradingAccount = value; RaisePropertyChangedCore(); }
    }

    string _tradingPassword;
    public string TradingPassword {
      get { return _tradingPassword; }
      set { _tradingPassword = value; RaisePropertyChangedCore(); }
    }
    
    public bool? TradingDemo { get; set; }
    public int TargetInPips { get; set; }
    #endregion

    #region Fields
    ThreadScheduler GetTradesScheduler;
    public string Title { get { return AliceMode + ""; } }
    protected bool isInDesign { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    #endregion

    #region Ctor
    ThreadScheduler.CommandDelegate Using_FetchServerTrades;
    private string logFileName = "Log.txt";
    public TraderModel() {
      System.IO.File.Delete(logFileName);
      Using_FetchServerTrades = () => Using(FetchServerTrades);
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
        Using_FetchServerTrades,
        (s, e) => { Log = e.Exception; });
      }
    }
    #endregion

    #region FXCM
    void Login() {
      try {
        if (fw == null) {
          fw = new FXW();
          fw.TradesCountChanged += t => Log = new Exception(string.Format("Trade was opened/removed"));
        }
        fw.LogOff();
        if (fw.LogOn("", CoreFX, TradingAccount, TradingPassword, TradingDemo.GetValueOrDefault(false)))
          Log = new Exception("Account " + TradingAccount + " is loggin now");
        else
          Log = new Exception("Login attempt failed for account " + TradingAccount);
        RaisePropertyChanged(() => IsLoggedIn);
      } catch (Exception exc) {
        Log = exc;
        MessageBox.Show(exc + "");
      }
    }

    void FetchServerTrades(TraderService.TraderServiceClient tsc) {
      RaisePropertyChanged(() => IsLoggedIn);
      serverAccount = tsc.GetAccount();
      if (serverAccount.IsMarginCall)
        GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => OpenNewServerAccount());
      RaisePropertyChanged(() => ServerAccountRow);
      var serverTrades = serverAccount.Trades.ToList();
      var localTrades = (IsLoggedIn ? fw.GetTrades("") : new Trade[] { }).ToList();
      if( IsLoggedIn ) localAccount = fw.GetAccount();
      RaisePropertyChanged(() => LocalAccountRow);
      // Need to run in on UI thread because of ListCollectionView ServerTradesList and such
      ServerTradesList.Dispatcher.Invoke(new Action(() => Syncronize(serverTrades, localTrades)));
    }

    private void Syncronize(List<Trade> serverTrades, List<Trade> localTrades) {
      ShowTrades(serverTrades, ServerTrades);
      if (CoreFX.IsLoggedIn) {

        #region Absent trades
        var penditTradesToRemove = (from tl in localTrades
                                   join tp in serverTradesPending on tl.MasterTradeId() equals tp.MasterTradeId()
                                   select tp).ToList();
        penditTradesToRemove.ForEach(pt => serverTradesPending.Remove(pt));

        serverTradesPending.ForEach(pt => localTrades.Add(pt));
        ShowTrades(localTrades, LocalTrades);

        var absentTrades = (from ts in serverTrades
                            join tl in localTrades on ts.Id equals tl.MasterTradeId() into svrTrds
                            from st in svrTrds.DefaultIfEmpty()
                            where st == null
                            select ts).ToList();


        AbsentTrades.Clear();
        absentTrades.ForEach(a => AbsentTrades.Add(a.InitUnKnown(fw.ServerTime)));

        if (syncAll) {
          SyncTrade(AbsentTrades);
          syncAll = false;
        } else if (IsAliceActive) {
          var tradeToCopy = AbsentTrades.FirstOrDefault(t => t.GetUnKnown().AutoSync);
          if (tradeToCopy != null) {
            SyncTrade(tradeToCopy);
          }
        }
        #endregion

        #region Close/Cancel Trades
        if (TargetInPips != 0 && localAccount.Trades.Length > 0 && localAccount.PL >= TargetInPips) CloseAllLocalTrades();
        else {
          if (IsAliceActive) {
            var tradesToClose = (from tl in localTrades
                                 join ts in serverTrades on tl.MasterTradeId() equals ts.Id into lclTrds
                                 from st in lclTrds.DefaultIfEmpty()
                                 where st == null
                                 select tl).ToList();
            var tradeToClose = tradesToClose.FirstOrDefault();
            if (tradeToClose != null) {
              try {
                if (tradeToClose.IsPending()) {
                  serverTradesPending.Remove(tradeToClose);
                  Log = new Exception("Pending trade " + tradeToClose.Id + " was canceled.");
                } else {
                  var orderId = fw.FixOrderClose(tradeToClose.Id);
                  Log = new Exception("Closing trade " + tradeToClose.Id + ". OrderId:" + orderId);
                }
              } catch (Exception exc) { Log = new Exception("TradeId:" + tradeToClose.Id, exc); }
            }
          }
        }
        #endregion
      }
      ServerTime = DateTime.Now;
    }

    private bool IsAliceActive {
      get {
        return new[] { AliceModes.Wonderland, AliceModes.Mirror }.Contains(AliceMode);
      }
    }

    private void SyncTrade(IEnumerable<Trade> tradeToCopy) {
      foreach (var trade in tradeToCopy)
        SyncTrade(trade);
    }
    private void SyncTrade(Trade tradeToCopy) {
      if (AliceMode == AliceModes.Neverland)
        Log = new Exception("Can't syncronize while Alice is in " + AliceMode);
      else {
        var serverTradeId = tradeToCopy.Id;
        serverTradesPending.Add(new Trade() {Id = serverTradeId,  Remark = new TradeRemark(serverTradeId) });
        Log = new Exception(string.Format("Trade {0} is being clonned", tradeToCopy.Id));
        var buy = AliceMode == AliceModes.Wonderland ? tradeToCopy.Buy : !tradeToCopy.Buy;
        var lots = ((tradeToCopy.Lots * ServerToLocalRatio)/1000).ToInt()*1000;
        OpenTrade(tradeToCopy.Pair, buy, lots, serverTradeId);
        Using_FetchServerTrades();
      }
    }

    private void OpenTrade(string pair, bool buy, int lots, string serverTradeID) {
      try {
        fw.FixOrderOpen(pair, buy, lots, 0, 0, serverTradeID);
      } catch (Exception exc) { Log = exc; }
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
  }
  public enum AliceModes {Neverland = 0, Wonderland = 1, Mirror = 2 }
}
