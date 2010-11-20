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
using System.Windows;
using System.Windows.Controls;
using HedgeHog.Alice.Client.UI.Controls;
using System.Data.Objects;
using HedgeHog.Bars;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Threading;
using HedgeHog.Reports;
using HedgeHog.Alice.Store;
using System.Collections;
using System.Collections.Specialized;
namespace HedgeHog.Alice.Client {
  public class MasterListChangedEventArgs : EventArgs {
    public Trade[] MasterTrades { get; set; }
    public MasterListChangedEventArgs(Trade[] masterTrades)
      : base() {
      this.MasterTrades = masterTrades;
    }
  }
  public class NeedAccountInfoEventArgs : EventArgs {
    public SlaveAccountModel[] Accounts { get; set; }
    public NeedAccountInfoEventArgs(SlaveAccountModel[] accounts) {
      this.Accounts = accounts;
    }
  }
  [Export]
  [Export(typeof(IMainModel))]
  [Export("MainWindowModel")]
  public class TraderModel:HedgeHog.Models.ModelBase,IMainModel {
    #region FXCM
    private Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    FXW fwMaster;
    public bool IsLoggedIn { get { return CoreFX!= null && CoreFX.IsLoggedIn; } }
    bool _isInLogin;
    public bool IsInLogin {
      get { return _isInLogin; }
      set { _isInLogin = value; RaisePropertyChanged(() => IsInLogin, () => IsNotInLogin); }
    }
    public bool IsNotInLogin { get { return !IsInLogin; } }
    private string _SessionStatus;
    public string SessionStatus {
      get { return _SessionStatus; }
      set {
        if (_SessionStatus != value) {
          _SessionStatus = value;
          RaisePropertyChangedCore();
        }
      }
    }


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

    #region Properties
    private bool _IsInVirtualTrading;
    public bool IsInVirtualTrading {
      get { return _IsInVirtualTrading; }
      set {
        if (_IsInVirtualTrading != value) {
          _IsInVirtualTrading = value;
          RaisePropertyChanged();
        }
      }
    }

    private DateTime _VirtualDateStart = DateTime.Now;
    public DateTime VirtualDateStart {
      get { return _VirtualDateStart; }
      set {
        if (_VirtualDateStart != value) {
          _VirtualDateStart = value;
          RaisePropertyChanged();
        }
      }
    }

    private string _VirtualPair;
    public string VirtualPair {
      get { return _VirtualPair; }
      set {
        if (_VirtualPair != value) {
          _VirtualPair = value;
          RaisePropertyChanged();
        }
      }
    }

    private int _VirtualMonthsToTest = 12;
    public int VirtualMonthsToTest {
      get { return _VirtualMonthsToTest; }
      set {
        if (_VirtualMonthsToTest != value) {
          _VirtualMonthsToTest = value;
          RaisePropertyChanged();
        }
      }
    }


    RemoteControlModel _remoteController;
    public RemoteControlModel RemoteController1 {
      get {
        if (_remoteController == null) {
          _remoteController = new RemoteControlModel();
          RaisePropertyChangedCore();
        }
        return _remoteController;
      }
    }
    public TradingAccount MasterAccount { get { return TradingMaster; } }
    #endregion

    #region Events
    public event EventHandler<OrderEventArgs> OrderToNoLoss;
    protected void OnOrderToNoLoss(Order order) {
      if (OrderToNoLoss != null) {
        try {
          OrderToNoLoss(this, new OrderEventArgs(order));
        } catch (Exception exc) { Log = exc; }
      }
    }

    #region MasterListChangedEvent
    public delegate void MasterListChangedeventHandler(object sender, MasterListChangedEventArgs e);
    public event MasterListChangedeventHandler MasterListChangedEvent;

    protected virtual void RaiseMasterListChangedEvent(Trade[] trades) {
      if (MasterListChangedEvent != null)
        MasterListChangedEvent(this, new MasterListChangedEventArgs(trades));
    }
    #endregion

    public event EventHandler SlaveLoginRequestEvent;
    protected void RaiseSlaveLoginRequestEvent() {
      if (SlaveLoginRequestEvent != null)
        SlaveLoginRequestEvent(this, new EventArgs());
    }
    #endregion

    #region Trade Lists

    public string[] TradingMacrosCases {
      get {
        try {
          return GlobalStorage.Context.TradingMacroes.Select(tm=>tm.TradingMacroName).Distinct().ToArray();
        } catch (Exception exc) {
          Log = exc;
          return null;
        }
      }
    }


    ObservableCollection<TradingAccount> slaveAccounts = new ObservableCollection<TradingAccount>();

    public ObservableCollection<TradingAccount> SlaveAccounts {
      get {
        if (slaveAccounts.Count == 0)
          GlobalStorage.GetTradingAccounts().ToList().ForEach(ta => slaveAccounts.Add(ta));
        return slaveAccounts; 
      }
      set { slaveAccounts = value; }
    }

    bool _ShowAllAccountsFilter = false;
    public bool ShowAllAccountsFilter {
      get { return _ShowAllAccountsFilter; }
      set { 
        _ShowAllAccountsFilter = value; 
        RaisePropertyChangedCore();
        TradingAccountsList.Refresh();
      }
    }

    ListCollectionView _tradingAccountsList;
    public ListCollectionView TradingAccountsList {
      get {
        if (_tradingAccountsList == null) {
          _TradingAccountsSet = new ObservableCollection<Store.TradingAccount>(GlobalStorage.Context.TradingAccounts);
          _TradingAccountsSet.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(_TradingAccountsSet_CollectionChanged);
          _tradingAccountsList = new ListCollectionView(_TradingAccountsSet);
          _tradingAccountsList.Filter = FilterTradingAccounts;
          
            //new Predicate<TradingAccount>(ta => new[] { ta.AccountId, ta.MasterId }.Contains(TradingAccount) || ShowAllAccountsFilter) as Predicate<object>;
        }
        return _tradingAccountsList; 
      }
    }

    private bool FilterTradingAccounts(object o) {
      var ta = o as TradingAccount;
      return new[] { ta.AccountId, ta.MasterId }.Contains(TradingAccount) || ShowAllAccountsFilter;
    }

    ObservableCollection<TradingAccount> _TradingAccountsSet;
    public IEnumerable<TradingAccount> TradingAccountsSet {
      get {
        try {
          return TradingAccountsList.SourceCollection.OfType<TradingAccount>().Where(FilterTradingAccounts);
        } catch (Exception exc) {
          Log = exc;
          return null;
        }
      }
    }

    void _TradingAccountsSet_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      var ta = e.NewItems[0] as TradingAccount;
      switch (e.Action) {
        case NotifyCollectionChangedAction.Add:
          GlobalStorage.Context.TradingAccounts.AddObject(ta); break;
        case NotifyCollectionChangedAction.Remove:
          GlobalStorage.Context.TradingAccounts.DeleteObject(ta); break;
      }
    }
    
    public TradingAccount TradingMaster { get { return TradingMasters.FirstOrDefault(); } }
    public IEnumerable<TradingAccount> TradingMasters { get { return TradingAccountsSet.Where(ta => ta.IsMaster); } }
    public TradingAccount[] TradingSlaves { get { return TradingAccountsSet.Where(ta=>!ta.IsMaster).ToArray(); } }

    SlaveAccountModel[] _slaveModels;
    public SlaveAccountModel[] SlaveModels {
      get {
        if( _slaveModels == null || _slaveModels.Length == 0)
          _slaveModels = TradingSlaves.Select(ts => new SlaveAccountModel(this, ts)).ToArray();
        return _slaveModels;
      }
    }


    public ObservableCollection<Order> orders { get; set; }
    public ListCollectionView OrdersList { get; set; }

    public ObservableCollection<Trade> ServerTrades { get; set; }
    public ListCollectionView ServerTradesList { get; set; }

    public ObservableCollection<Trade> AbsentTrades { get; set; }
    public ListCollectionView AbsentTradesList { get; set; }

    private TradingAccountModel _accountModel = new TradingAccountModel();
    public TradingAccountModel AccountModel { get { return _accountModel; } }
    public TradingAccountModel[] ServerAccountRow { get { return new[] { AccountModel }; } }
    public double CurrentLoss { set { AccountModel.CurrentLoss = value; } }

    #region SlaveAccountInfos
    ObservableCollection<TradingAccountModel> SlaveAccountInfos = new ObservableCollection<TradingAccountModel>();
    ListCollectionView _slaveAccountInfosView;
    public ListCollectionView SlaveAccountInfosView {
      get {
        if (_slaveAccountInfosView == null)
          _slaveAccountInfosView = new ListCollectionView(SlaveAccountInfos);
        return _slaveAccountInfosView;
      }
    }
    #endregion

    #endregion

    #region ServerAddress
    string _serverAddress="";
    string serverAddressPostfix = "HedgeHog.Alice.WCF";
    string serverAddressSuffix = "net.tcp://";
    public string ServerAddress {
      get { return Path.Combine(serverAddressSuffix, _serverAddress, serverAddressPostfix); }
      set { _serverAddress = value; }
    }
    public bool isInRemoteMode { get { return !string.IsNullOrWhiteSpace(_serverAddress); } }
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
    DateTime lastLogTime = DateTime.MinValue;
    public string LogText { get {
      lock (_logQueue) {
        return string.Join(Environment.NewLine, _logQueue.Reverse());
      }
    }
    }
    Queue<string> _logQueue = new Queue<string>();
    Exception _log;
    public Exception Log {
      get { return _log; }
      set {
        if (isInDesign) return;
        _log = value;
        var exc = value is Exception ? value : null;
        //var comExc = exc as System.Runtime.InteropServices.COMException;
        //if (comExc != null && comExc.ErrorCode == -2147467259)
        //  AccountLogin(new LoginInfo(TradingAccount, TradingPassword, TradingDemo));
        lock (_logQueue) {
          if (_logQueue.Count > 5) _logQueue.Dequeue();
          var messages = new List<string>(new[] { DateTime.Now.ToString("[dd HH:mm:ss] ") + GetExceptionShort(value) });
          while (value.InnerException != null) {
            messages.Add(GetExceptionShort(value.InnerException));
            value = value.InnerException;
          }
          _logQueue.Enqueue(string.Join(Environment.NewLine + "-", messages));
        }
        exc = FileLogger.LogToFile(exc);
        RaisePropertyChanged(() => LogText, () => IsLogExpanded);
      }
    }


    string GetExceptionShort(Exception exc) {
      return (exc.TargetSite == null ? "" : exc.TargetSite.DeclaringType.Name + "." +
      exc.TargetSite.Name + ": ") + exc.Message;
    }

    public bool IsLogExpanded {
      get { return (DateTime.Now - lastLogTime) < TimeSpan.FromSeconds(10); }
    }
    #endregion

    #region Commanding

    #region LoginCommand

    ICommand _LoginCommand;
    public ICommand LoginCommand {
      get {
        if (_LoginCommand == null) {
          _LoginCommand = new Gala.RelayCommand<object>(Login, (o) => o is TradingAccount);
        }

        return _LoginCommand;
      }
    }
    void Login(object tradingAccount) {
      var ta = tradingAccount as TradingAccount;
      LoginAsync(ta.AccountId, ta.Password, ta.IsDemo);
    }

    #endregion

    #region ToggleShowAllAccountsCommand
    ICommand _ToggleShowAllAccountsCommand;
    public ICommand ToggleShowAllAccountsCommand {
      get {
        if (_ToggleShowAllAccountsCommand == null) {
          _ToggleShowAllAccountsCommand = new Gala.RelayCommand(ToggleShowAllAccounts, () => true);
        }

        return _ToggleShowAllAccountsCommand;
      }
    }
    void ToggleShowAllAccounts() {
      ShowAllAccountsFilter = !ShowAllAccountsFilter;
    }

    #endregion

    #region SetOrderToNoLossCommand
    ICommand _SetOrderToNoLossCommand;
    public ICommand SetOrderToNoLossCommand {
      get {
        if (_SetOrderToNoLossCommand == null) {
          _SetOrderToNoLossCommand = new Gala.RelayCommand<Order>(SetOrderToNoLoss, (o) => true);
        }

        return _SetOrderToNoLossCommand;
      }
    }
    void SetOrderToNoLoss(Order order) {
      OnOrderToNoLoss(order);
    }
    #endregion

    #region IncreaseLimitCommand
    ICommand _IncreaseLimitCommand;
    public ICommand IncreaseLimitCommand {
      get {
        if (_IncreaseLimitCommand == null) _IncreaseLimitCommand = new Gala.RelayCommand<Trade>(IncreaseLimit, CanExecuteLimitChange);
        return _IncreaseLimitCommand;
      }
    }
    void IncreaseLimit(Trade trade) {
      try {
        AddtDelta(trade.Id, 1, limitDeltas);
        if (!changeLimitScheduler.IsRunning)
          changeLimitScheduler.Run();
      } catch (Exception exc) {
        Log = new Exception("DecreaseLimit Error", exc);
      }
    }
    #endregion

    #region DecreaseLimitCommand
    ICommand _DecreaseLimitCommand;
    public ICommand DecreaseLimitCommand {
      get {
        if (_DecreaseLimitCommand == null) _DecreaseLimitCommand = new Gala.RelayCommand<Trade>(DecreaseLimit, CanExecuteLimitChange);
        return _DecreaseLimitCommand;
      }
    }

    void DecreaseLimit(Trade trade) {
      try {
        AddtDelta(trade.Id, -1, limitDeltas);
        if( !changeLimitScheduler.IsRunning) 
          changeLimitScheduler.Run();
      } catch (Exception exc) {
        Log = new Exception("DecreaseLimit Error", exc);
      }
    }
    #endregion

    #region DecreaseStopCommand
    ICommand _DecreaseStopCommand;
    public ICommand DecreaseStopCommand {
      get {
        if (_DecreaseStopCommand == null) _DecreaseStopCommand = new Gala.RelayCommand<Trade>(DecreaseStop, CanExecuteStopChange);
        return _DecreaseStopCommand;
      }
    }
    void DecreaseStop(Trade trade) {
      try {
        AddtDelta(trade.Id, -1, stopDeltas);
        if (!changeStopScheduler.IsRunning)
          changeStopScheduler.Run();
      } catch (Exception exc) {
        Log = new Exception("DecreaseStop Error", exc);
      }
    }
    #endregion


    #region Entry Order Commands
    ICommand _IncreaseEntryStopCommand;
    public ICommand IncreaseEntryStopCommand {
      get {
        if (_IncreaseEntryStopCommand == null) {
          _IncreaseEntryStopCommand = new Gala.RelayCommand<Order>(IncreaseEntryStop, (o) => true);
        }

        return _IncreaseEntryStopCommand;
      }
    }
    void IncreaseEntryStop(Order order) {
      fwMaster.ChangeEntryOrderPeggedStop(order.OrderID, order.StopInPips + 1);
    }

    ICommand _DecreaseEntryStopCommandCommand;
    public ICommand DecreaseEntryStopCommandCommand {
      get {
        if (_DecreaseEntryStopCommandCommand == null) {
          _DecreaseEntryStopCommandCommand = new Gala.RelayCommand<Order>(DecreaseEntryStopCommand, (o) => true);
        }

        return _DecreaseEntryStopCommandCommand;
      }
    }
    void DecreaseEntryStopCommand(object o) { 
      var order = o as Order;
      fwMaster.ChangeEntryOrderPeggedStop(order.OrderID, order.StopInPips - 1);
    }


    ICommand _IncreaseEntryLimitCommand;
    public ICommand IncreaseEntryLimitCommand {
      get {
        if (_IncreaseEntryLimitCommand == null) {
          _IncreaseEntryLimitCommand = new Gala.RelayCommand<Order>(IncreaseEntryLimit, (o) => true);
        }

        return _IncreaseEntryLimitCommand;
      }
    }
    void IncreaseEntryLimit(Order order) {
      fwMaster.ChangeEntryOrderPeggedLimit(order.OrderID, order.LimitInPips + 1);
    }

    ICommand _DecreaseEntryLimitCommand;
    public ICommand DecreaseEntryLimitCommand {
      get {
        if (_DecreaseEntryLimitCommand == null) {
          _DecreaseEntryLimitCommand = new Gala.RelayCommand<Order>(DecreaseEntryLimit, (o) => true);
        }

        return _DecreaseEntryLimitCommand;
      }
    }
    void DecreaseEntryLimit(Order order) {
      fwMaster.ChangeEntryOrderPeggedLimit(order.OrderID, order.LimitInPips - 1);
    }


    ICommand _IncreaseEntryRateCommand;
    public ICommand IncreaseEntryRateCommand {
      get {
        if (_IncreaseEntryRateCommand == null) {
          _IncreaseEntryRateCommand = new Gala.RelayCommand<Order>(IncreaseEntryRate, (o) => true);
        }

        return _IncreaseEntryRateCommand;
      }
    }
    void IncreaseEntryRate(Order order) {
      fwMaster.ChangeOrderRate(order.OrderID, order.Rate + order.PointSize);
    }

    ICommand _DecreaseEntryRateCommand;
    public ICommand DecreaseEntryRateCommand {
      get {
        if (_DecreaseEntryRateCommand == null) {
          _DecreaseEntryRateCommand = new Gala.RelayCommand<Order>(DecreaseEntryRate, (o) => true);
        }

        return _DecreaseEntryRateCommand;
      }
    }
    void DecreaseEntryRate(Order order) {
      fwMaster.ChangeOrderRate(order.OrderID, order.Rate - order.PointSize);
    }

    ICommand _CancelEntryOrderCommand;
    public ICommand CancelEntryOrderCommand {
      get {
        if (_CancelEntryOrderCommand == null) {
          _CancelEntryOrderCommand = new Gala.RelayCommand<Order>(CancelEntryOrder, (o) => true);
        }

        return _CancelEntryOrderCommand;
      }
    }
    void CancelEntryOrder(Order order) {
      fwMaster.DeleteOrder(order.OrderID);
    }

    #endregion

    #region EncreaseStopCommand
    ICommand _EncreaseStopCommand;
    public ICommand EncreaseStopCommand {
      get {
        if (_EncreaseStopCommand == null) _EncreaseStopCommand = new Gala.RelayCommand<Trade>(EncreaseStop, CanExecuteStopChange);
        return _EncreaseStopCommand;
      }
    }

    void EncreaseStop(Trade trade) {
      try {
        AddtDelta(trade.Id, 1, stopDeltas);
        if (!changeStopScheduler.IsRunning)
          changeStopScheduler.Run();
      } catch (Exception exc) {
        Log = new Exception("EncreaseStop Error", exc);
      }
    }
    #endregion

    #region DeleteTradingAccountCommand

    ICommand _DeleteTradingAccountCommand;
    public ICommand DeleteTradingAccountCommand {
      get {
        if (_DeleteTradingAccountCommand == null) {
          _DeleteTradingAccountCommand = new Gala.RelayCommand<object>(DeleteTradingAccount, (ta) => true);
        }

        return _DeleteTradingAccountCommand;
      }
    }
    void DeleteTradingAccount(object ta) {
      GlobalStorage.Context.TradingAccounts.DeleteObject(ta as TradingAccount);
      SaveTradingSlaves();
    }

    #endregion

    #region SaveTradingSlaves

    ICommand _SaveTradingSlavesCommand;
    public ICommand SaveTradingSlavesCommand {
      get {
        if (_SaveTradingSlavesCommand == null) {
          _SaveTradingSlavesCommand = new Gala.RelayCommand(SaveTradingSlaves, () => true);
        }

        return _SaveTradingSlavesCommand;
      }
    }
    void SaveTradingSlaves() {
      try {
        GlobalStorage.Context.SaveChanges();
        Log = new Exception("Slave accounts were saved");
      } catch (Exception exc) { 
        Log = exc;
        MessageBox.Show(exc + "");
      }
    }

    #endregion

    #region OpenNewLocalAccountCommand

    ICommand _OpenNewLocalAccountCommand;
    public ICommand OpenNewLocalAccountCommand {
      get {
        if (_OpenNewLocalAccountCommand == null) {
          _OpenNewLocalAccountCommand = new OpenNewAccountRelayCommand(OpenNewLocalAccount, (al) => true);
        }

        return _OpenNewLocalAccountCommand;
      }
    }
    void OpenNewLocalAccount(LoginInfo li) {
      try {
        string account, password;
        FXCM.Lib.GetNewAccount(out account, out password);
        if (Login(account, password, true)) {
          li.Account = account;
          li.Password = password;
          li.IsDemo = true;
          SaveTradingSlaves();
        }
      } catch (Exception exc) { Log = exc; }
    }

    #endregion

    #region AddNewSlaveAccountCommand
    ICommand _AddNewSlaveAccountCommand;
    public ICommand AddNewSlaveAccountCommand {
      get {
        if (_AddNewSlaveAccountCommand == null) {
          _AddNewSlaveAccountCommand = new Gala.RelayCommand(AddNewSlaveAccount, () => true);
        }

        return _AddNewSlaveAccountCommand;
      }
    }
    void AddNewSlaveAccount() {
      string account, password;
      FXCM.Lib.GetNewAccount(out account, out password);
      GlobalStorage.Context.TradingAccounts.AddObject(
        new TradingAccount() { AccountId = account, Password = password, IsDemo = true, IsMaster = false, TradeRatio = "1:1" });
      SaveTradingSlaves();
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
          TradingAccount = account;
          TradingPassword = password;
          TradingDemo = true;
          if( isInRemoteMode)
            tsc.OpenNewAccount(account, password);
          Log = new Exception("New Rabit has arrived");
        });
      } catch (Exception exc) { Log = exc; }
    }

    #endregion

    #region UpdatePasswordCommand
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
      if (isInRemoteMode)
        Using(tsc => {
          try {
            Log = new Exception("Trade " + tradeId + " was closed with OrderId " + tsc.CloseTrade(tradeId));
          } catch (Exception exc) { Log = exc; }
        });
      else
        try {
          Log = new Exception("Trade " + tradeId + " was closed with OrderId " + fwMaster.FixOrdersClose(tradeId).FirstOrDefault());
        } catch (Exception exc) { Log = exc; }
    }
    #endregion

    #region BackTestCommand
    public event EventHandler<BackTestEventArgs> StartBackTesting;

    ICommand _BackTestCommand;
    public ICommand BackTestCommand {
      get {
        if (_BackTestCommand == null) {
          _BackTestCommand = new Gala.RelayCommand(BackTest, () => true);
        }

        return _BackTestCommand;
      }
    }
    void BackTest() {
      if (StartBackTesting != null) StartBackTesting(this, new BackTestEventArgs(VirtualPair,  VirtualDateStart,VirtualMonthsToTest));
    }

    #endregion

    #region ReportCommand
    ICommand _ReportCommand;
    public ICommand ReportCommand {
      get {
        if (_ReportCommand == null) {
          _ReportCommand = new Gala.RelayCommand(Report, () => true);
        }
        return _ReportCommand;
      }
    }
    void Report() {
      new Reports.Report(fwMaster).Show();
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
      if (isInRemoteMode)
        Using(tsc => {
          try {
            Log = new Exception("Closing all server trades.");
            var ordersIds = tsc.CloseAllTrades();
            Log = new Exception("Trades closed:" + string.Join(",", ordersIds));
          } catch (Exception exc) { Log = exc; }
        });
      else {
        Log = new Exception("Closing all trades.");
        var trades = fwMaster.GetTrades("");
        fwMaster.CloseTradesAsync(trades);
        Log = new Exception("Trades closed:" + string.Join(",", trades.Select(t => t.Id)));
      }
    }

    #endregion

    #region AccountLoginCommand

    ICommand _AccountLoginCommand;
    public ICommand AccountLoginCommand {
      get {
        if (_AccountLoginCommand == null) {
          _AccountLoginCommand = new AccountLoginRelayCommand(AccountLogin, (li) => true);
        }

        return _AccountLoginCommand;
      }
    }
    void AccountLogin(LoginInfo li) {
      LoginAsync(li.Account, li.Password, li.IsDemo);
    }

    private void LoginAsync(string account, string password, bool isDemo) {
      new ThreadScheduler((s, e) => Log = e.Exception).Command = () => Login(account, password, isDemo);
    }

    #endregion

    #region OpenDataBaseCommand
    private string _DatabasePath;
    public string DatabasePath {
      get { return _DatabasePath; }
      set {
        if (_DatabasePath != value) {
          _DatabasePath = value;
          OnPropertyChanged("DatabasePath");
        }
      }
    }

    ICommand _OpenDataBaseCommand;
    public ICommand OpenDataBaseCommand {
      get {
        if (_OpenDataBaseCommand == null) {
          _OpenDataBaseCommand = new Gala.RelayCommand(OpenDataBase, () => true);
        }
        return _OpenDataBaseCommand;
      }
    }
    void OpenDataBase() {
      var dbPath = GlobalStorage.OpenDataBasePath();
      if (string.IsNullOrWhiteSpace(dbPath)) return;
      DatabasePath = dbPath;
      MessageBox.Show("Mast re-start program to re-load database.");
    }

    #endregion
    //#region ReverseAliceModeCommand
    //ICommand _ReverseAliceModeCommand;
    //public ICommand ReverseAliceModeCommand {
    //  get {
    //    if (_ReverseAliceModeCommand == null) {
    //      _ReverseAliceModeCommand = new Gala.RelayCommand(ReverseAliceMode, () => true);
    //    }

    //    return _ReverseAliceModeCommand;
    //  }
    //}
    //void ReverseAliceMode() {
    //  MessageBox.Show("This method is not implemented.");
    //  return;
    //  if (AliceMode == AliceModes.Wonderland) AliceMode = AliceModes.Mirror;
    //  if (AliceMode == AliceModes.Mirror) AliceMode = AliceModes.Wonderland;
    //  //CloseAllLocalTrades();
    //  //SyncAllTrades();
    //}
    //#endregion

    #region TestCommand
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

    #endregion

    #region Trading Info
    public TradingLogin LoginInfo { get { return new TradingLogin(TradingAccount, TradingPassword, TradingDemo); } }

    public string[] TradingAccounts { get { return GlobalStorage.Context.TradingAccounts.Select(ta => ta.AccountId).ToArray().Distinct().ToArray(); } }

    string _tradingAccount;
    public string TradingAccount {
      get { return _tradingAccount; }
      set { 
        _tradingAccount = value; 
        RaisePropertyChangedCore();
        ShowAllAccountsFilter = false;
      }
    }

    string _tradingPassword;
    public string TradingPassword {
      get { return _tradingPassword; }
      set { _tradingPassword = value; RaisePropertyChangedCore(); }
    }

    bool _tradingDemo;
    public bool TradingDemo {
      get { return _tradingDemo; }
      set { _tradingDemo = value; RaisePropertyChangedCore(); }
    }

    public int TargetInPips { get; set; }
    #endregion

    #region Fields
    ThreadScheduler GetTradesScheduler;
    public string Title { get { return AliceMode + ""; } }
    protected bool isInDesign { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    #endregion

    #region Ctor
    ThreadScheduler.CommandDelegate Using_FetchServerTrades;

    public TraderModel() {
      Initialize();
      #region FXCM
      fwMaster = new FXW(this.CoreFX);
      CoreFX.LoggedInEvent += (s, e) => {
        IsInLogin = false;
        fwMaster.Error += fwMaster_Error;
        fwMaster.TradeAdded += fwMaster_TradeAdded;
        fwMaster.TradeRemoved += fwMaster_TradeRemoved;
        fwMaster.TradeChanged += fwMaster_TradeChanged;
        fwMaster.PriceChanged += fwMaster_PriceChanged;
        fwMaster.OrderChanged += fwMaster_OrderChanged;
        fwMaster.OrderAdded += fwMaster_OrderAdded;
        fwMaster.SessionStatusChanged += fwMaster_SessionStatusChanged;

        RaisePropertyChanged(() => IsLoggedIn);
        Log = new Exception("Account " + TradingAccount + " logged in.");
        try {
          var account = fwMaster.GetAccount();
          if (account == null) {
            Thread.Sleep(1000);
            account = fwMaster.GetAccount();
          }
          if (account != null)
            AccountModel.Update(account, 0, fwMaster.ServerTime);
        } catch (Exception exc) {
          Log = exc;
          MessageBox.Show(exc.ToString(), "GetAccount", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.None, MessageBoxOptions.ServiceNotification);
        }
      };
      CoreFX.LoginError += exc => {
        IsInLogin = false;
        Log = exc;
        RaisePropertyChanged(() => IsLoggedIn);
      };
      CoreFX.LoggedOffEvent += (s, e) => {
        Log = new Exception("Account " + TradingAccount + " logged out.");
        RaisePropertyChanged(() => IsLoggedIn);
        fwMaster.TradeAdded -= fwMaster_TradeAdded;
        fwMaster.TradeRemoved -= fwMaster_TradeRemoved;
        fwMaster.Error -= fwMaster_Error;
        fwMaster.TradeChanged -= fwMaster_TradeChanged;
        fwMaster.PriceChanged -= fwMaster_PriceChanged;
        fwMaster.OrderChanged -= fwMaster_OrderChanged;
        fwMaster.OrderAdded -= fwMaster_OrderAdded;
        fwMaster.SessionStatusChanged -= fwMaster_SessionStatusChanged;
      };
      #endregion

      if (false) {
        Using_FetchServerTrades = () => Using(FetchServerTrades);
        if (!isInDesign) {
          GetTradesScheduler = new ThreadScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
          Using_FetchServerTrades,
          (s, e) => { Log = e.Exception; });
        }
      }
    }
    private void Initialize(){
      var settings = new WpfPersist.UserSettingsStorage.Settings().Dictionary;
      DatabasePath = settings.Where(kv => kv.Key.Contains("DatabasePath")).LastOrDefault().Value;
      if (!string.IsNullOrWhiteSpace(DatabasePath)) GlobalStorage.DatabasePath = DatabasePath;
      else DatabasePath = GlobalStorage.DatabasePath;
      ServerTradesList = new ListCollectionView(ServerTrades = new ObservableCollection<Trade>());
      ServerTradesList.SortDescriptions.Add(new SortDescription(Lib.GetLambda(() => new Trade().Pair), ListSortDirection.Ascending));
      ServerTradesList.SortDescriptions.Add(new SortDescription(Lib.GetLambda(() => new Trade().Time), ListSortDirection.Descending));
      OrdersList = new ListCollectionView(orders = new ObservableCollection<Order>());
      AbsentTradesList = new ListCollectionView(AbsentTrades = new ObservableCollection<Trade>());

    }

    void fwMaster_SessionStatusChanged(object sender, FXW.SesstionStatusEventArgs e) {
      SessionStatus = e.Status;
      if (e.Status == "Disconnected") {
        CoreFX.Logout();
        CoreFX.LogOn();
      }
    }

    public event EventHandler<MasterTradeEventArgs> MasterTradeRemoved;
    protected void OnMasterTradeRemoved(Trade trade) {
      if (MasterTradeRemoved != null) MasterTradeRemoved(this, new MasterTradeEventArgs(trade));
    }
    void fwMaster_TradeRemoved(Trade trade) {
      OnMasterTradeRemoved(trade);
    }

    void fwMaster_Error(object sender, HedgeHog.Shared.ErrorEventArgs e) {
      Log = e.Error;
    }

    ~TraderModel() {
      if (IsLoggedIn) CoreFX.Logout();
    }


    #endregion

    #region FXCM
    bool Login( string tradingAccount,string tradingPassword,bool tradingDemo) {
      try {
        if (CoreFX.IsLoggedIn) CoreFX.Logout();
        IsInLogin = true;
        CoreFX.IsInVirtualTrading = IsInVirtualTrading;
        if (CoreFX.LogOn(tradingAccount, tradingPassword, tradingDemo)) {
          RaiseSlaveLoginRequestEvent();
          InvokeSyncronize(fwMaster.GetAccount());
          return true;
        } else {
          MessageBox.Show("Login failed. See Log.");
          return false;
        }
      } catch (Exception exc) {
        Log = exc;
        MessageBox.Show(exc + "");
        return false;
      } finally {
        IsInLogin = false;
        RaisePropertyChanged(() => IsLoggedIn);
      }
    }

    ThreadSchedulersDispenser PriceChangedSchedulers = new ThreadSchedulersDispenser();
    void fwMaster_PriceChanged(string pair) {
      fwMaster_PriceChanged(new Price() { Pair = pair });
    }
    void fwMaster_PriceChanged(Price Price) {
      //if (!ServerTrades.Any(t => t.Pair == Price.Pair)) return;
      RunPrice(Price.Pair);
    }

    private void RunPrice(string pair) {
      PriceChangedSchedulers.Run(pair, () => RunPriceChanged(pair));
    }

    private void RunPriceChanged(string pair) {
      try {
        var a = fwMaster.GetAccount();
        if (a.Trades.Any(t => t.Pair == pair) || a.Trades.Length == 0) {
          a.Orders = fwMaster.GetOrders("");
          InvokeSyncronize(a);
        }
      } catch (Exception exc) { Log = exc; }
    }

    public event EventHandler<MasterTradeEventArgs> MasterTradeAdded;
    protected void OnMasterTradeAdded(Trade trade) {
      if (MasterTradeAdded != null) 
        MasterTradeAdded(this, new MasterTradeEventArgs(trade));
    }

    void fwMaster_TradeAdded(Trade trade) {
      OnMasterTradeAdded(trade);
      RunPrice(trade.Pair);
    }
    void fwMaster_TradeChanged(object sender, TradeEventArgs e) {
      //fwMaster_PriceChanged(e.Trade.Pair);
    }

    void fwMaster_OrderChanged(object sender, OrderEventArgs e) {
      fwMaster_PriceChanged(e.Order.Pair);
    }
    void fwMaster_OrderAdded(object sender, OrderEventArgs e) {
      fwMaster_PriceChanged(e.Order.Pair);
    }


    void FetchServerTrades(TraderService.TraderServiceClient tsc) {
      try {
        InvokeSyncronize(tsc.GetAccount());
      } catch (Exception exc) { Log = exc; }
    }

    void InvokeSyncronize(Account account) {
      if (account == null) return;
      if (account.Error!= null)
        Log = account.Error;
      else {
        var trades = account.Trades;
        AccountModel.Update(account, 0, fwMaster.IsLoggedIn ? fwMaster.ServerTime : DateTime.Now);
        RaiseMasterListChangedEvent(trades);
        ServerTradesList.Dispatcher.BeginInvoke(new Action(() => {
          UpdateTrades(account, trades.ToList(), ServerTrades);
          UpdateOrders(account, (account.Orders ?? new Order[0]).ToList(), orders);
        }));
      }
    }


    private bool IsAliceActive {
      get {
        return new[] { AliceModes.Wonderland, AliceModes.Mirror }.Contains(AliceMode);
      }
    }

    private void OpenTrade(string pair, bool buy, int lots, string serverTradeID) {
      try {
        fwMaster.FixOrderOpen(pair, buy, lots, 0, 0, serverTradeID);
      } catch (Exception exc) { Log = exc; }
    }
    private void UpdateTrades(Account account, List<Trade> tradesList, ObservableCollection<Trade> tradesCollection) {
      var oldIds = tradesCollection.Select(t => t.Id).ToArray();
      var newIds = tradesList.Select(t => t.Id);
      var deleteIds = oldIds.Except(newIds).ToList();
      deleteIds.ForEach(d => tradesCollection.Remove(tradesCollection.Single(t => t.Id == d)));
      var addIds = newIds.Except(oldIds).ToList();
      addIds.ForEach(a => tradesCollection.Add(tradesList.Single(t => t.Id == a)));
      foreach (var trade in tradesList) {
        var trd = tradesCollection.Single(t => t.Id == trade.Id);
        trd.Update(trade,
          o => { trd.InitUnKnown<TradeUnKNown>().BalanceOnLimit = trd.Limit == 0 ? 0 : account.Balance + trd.LimitAmount; },
          o => { trd.InitUnKnown<TradeUnKNown>().BalanceOnStop = account.Balance + trd.StopAmount; }
          );
      }
    }
    private void UpdateOrders(Account account,List<Order> ordersList, ObservableCollection<Order> ordersCollection) {
      var oldIds = ordersCollection.Select(t => t.OrderID).ToArray();
      var newIds = ordersList.Select(t => t.OrderID);
      var deleteIds = oldIds.Except(newIds).ToList();
      deleteIds.ForEach(d => ordersCollection.Remove(ordersCollection.Single(t => t.OrderID == d)));
      var addIds = newIds.Except(oldIds).ToList();
      addIds.ForEach(a => ordersCollection.Add(ordersList.Single(t => t.OrderID == a)));
      foreach (var order in ordersList) {
        var odr = ordersCollection.Single(t => t.OrderID == order.OrderID);
        var stopBalance = account.Balance + account.Trades.Where(t => t.Pair == odr.Pair && t.IsBuy != odr.IsBuy).Sum(t => t.StopAmount);
        odr.Update(order,
          o => { odr.InitUnKnown<OrderUnKnown>().BalanceOnLimit = odr.Limit == 0 ? 0 : stopBalance + odr.LimitAmount; },
          o => { odr.InitUnKnown<OrderUnKnown>().BalanceOnStop = stopBalance + odr.StopAmount; },
          o => { odr.InitUnKnown<OrderUnKnown>().NoLossLimit = Static.GetEntryOrderLimit(fwMaster, account.Trades,odr.Lot, false, AccountModel.CurrentLoss).Round(1); },
          o => { odr.InitUnKnown<OrderUnKnown>().PercentOnStop = odr.StopAmount/stopBalance ; },
          o => { odr.InitUnKnown<OrderUnKnown>().PercentOnLimit = odr.LimitAmount/stopBalance ; }
          );
      }
    }
    private void ShowTrades<TList>(List<TList> tradesList, ObservableCollection<TList> tradesCollection) {
      tradesCollection.Clear();
      tradesList.ForEach(a => tradesCollection.Add(a));
    }

    #endregion

    #region WCF Service
    public void Using(Action<TraderService.TraderServiceClient> action) {
      if (!isInRemoteMode || isInDesign) return;
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

    #region Methods

    #region Change Stop/Limit
    void changeStop(string tradeId, double newStop) {
      fwMaster.FixOrderSetStop(tradeId, newStop, "");
    }
    void changeStops() {
      changeStopsOrLimits(stopDeltas, trade => trade.Stop, (id, v) => changeStop(id, v), changeStopScheduler);
    }
    void changeLimit(string tradeId, double newLimit) {
      fwMaster.FixOrderSetLimit(tradeId, newLimit, "");
    }
    void changeLimits() {
      changeStopsOrLimits(limitDeltas, trade => trade.Limit, (id, v) => changeLimit(id, v), changeLimitScheduler);
    }
    void changeStopsOrLimits(Dictionary<string, double> deltas, Func<Trade, double> getValue, Action<string, double> changeValue, ThreadScheduler changeScheduler) {
      foreach (var ld in deltas.Where(kv => kv.Value != 0).ToArray()) {
        var trade = fwMaster.GetTrade(ld.Key);
        if (trade == null) deltas.Remove(ld.Key);
        else {
          var newValue = fwMaster.InPoints(trade.Pair, ld.Value) + getValue(trade);
          deltas[ld.Key] = 0;
          changeValue(ld.Key, newValue);
          if (deltas[ld.Key] != 0)
            changeScheduler.Run();
        }
      }
    }

    private bool CanExecuteStopLimitChange(Trade trade, Func<Trade, bool> predicate) {
      return trade != null && predicate(trade);
    }
    private bool CanExecuteStopChange(Trade trade) { return true || CanExecuteStopLimitChange(trade, t => t.Stop != 0); }
    private bool CanExecuteLimitChange(Trade trade) { return true || CanExecuteStopLimitChange(trade, t => t.Limit != 0); }
    Dictionary<string, double> stopDeltas = new Dictionary<string, double>();
    Dictionary<string, double> limitDeltas = new Dictionary<string, double>();
    void AddtDelta(string pair, double limitDelta, Dictionary<string, double> deltas) {
      if (!deltas.ContainsKey(pair)) deltas.Add(pair, limitDelta);
      else deltas[pair] = deltas[pair] + limitDelta;
    }
    ThreadScheduler _changeLimitScheduler;
    ThreadScheduler changeLimitScheduler {
      get {
        if (_changeLimitScheduler == null)
          _changeLimitScheduler = new ThreadScheduler(TimeSpan.FromSeconds(0.5), ThreadScheduler.infinity, changeLimits, (s, e) => Log = e.Exception);
        return _changeLimitScheduler;
      }
    }
    ThreadScheduler _changeStopScheduler;
    ThreadScheduler changeStopScheduler {
      get {
        if (_changeStopScheduler == null)
          _changeStopScheduler = new ThreadScheduler(TimeSpan.FromSeconds(0.5), ThreadScheduler.infinity, changeStops, (s, e) => Log = e.Exception);
        return _changeStopScheduler;
      }
    }
    #endregion

    public string TradingMacroName { get { return MasterAccount == null ? "" : MasterAccount.TradingMacroName; } }
    public double CommissionByTrade(Trade trade) { return MasterAccount == null ? 0 : trade.Lots / 10000.0 * MasterAccount.Commission; }
    string tradeIdLast = "";
    public void AddCosedTrade(Trade trade) {
      try {
        if (TradingMaster == null) MessageBox.Show("Trading Master account is not found.");
        else {
          if (tradeIdLast == trade.Id) return;
          tradeIdLast = trade.Id;
          TradeStatistics tradeStats = trade.InitUnKnown<TradeUnKNown>().TradeStats ?? new TradeStatistics();
          //if (GlobalStorage.Context.TradeHistories.Count(t => t.Id == trade.Id) > 0) return;
          ////var ct = ClosedTrade.CreateClosedTrade(trade.Buy, trade.Close, trade.CloseInPips, trade.GrossPL, trade.Id + "", trade.IsBuy, trade.IsParsed, trade.Limit, trade.LimitAmount, trade.LimitInPips, trade.Lots, trade.Open, trade.OpenInPips, trade.OpenOrderID + "", trade.OpenOrderReqID + "", trade.Pair, trade.PipValue, trade.PL, trade.PointSize, trade.PointSizeFormat, trade.Remark + "", trade.Stop, trade.StopAmount, trade.StopInPips, trade.Time, trade.TimeClose, trade.UnKnown + "", TradingMaster.AccountId + "", CommissionByTrade(trade), trade.IsVirtual, DateTime.Now, tradeStats.TakeProfitInPipsMinimum, tradeStats.MinutesBack);
          var ct = t_Trade.Createt_Trade(trade.Id, trade.Buy, trade.PL, trade.GrossPL, trade.Lots, trade.Pair, trade.Time, trade.TimeClose, TradingMaster.AccountId + "", CommissionByTrade(trade), trade.IsVirtual, tradeStats.TakeProfitInPipsMinimum, tradeStats.MinutesBack, tradeStats.SessionId);
          //var ct = TradeHistory.CreateTradeHistory(trade.Id, trade.Buy, (float)trade.PL, (float)trade.GrossPL, trade.Lots, trade.Pair, trade.Time, trade.TimeClose, TradingMaster.AccountId + "", (float)CommissionByTrade(trade), trade.IsVirtual, tradeStats.TakeProfitInPipsMinimum, tradeStats.MinutesBack, tradeStats.SessionId);
          ct.TimeStamp = DateTime.Now;
          GlobalStorage.ForexContext.t_Trade.AddObject(ct);
          GlobalStorage.ForexContext.SaveChanges();
        }
      } catch (Exception exc) { Log = exc; }
    }

    #endregion

  }
  public class TraderModelDesign : TraderModel {
  }
  public enum AliceModes {Neverland = 0, Wonderland = 1, Mirror = 2 }
}
