﻿using System;
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
using HedgeHog.Alice.Client.UI.Controls;
using System.Data.Objects;
using HedgeHog.Bars;
using System.ComponentModel;
using System.ComponentModel.Composition;
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
    #endregion

    #region Events
    public event EventHandler<FXW.OrderEventArgs> OrderToNoLoss;
    protected void OnOrderToNoLoss(Order order) {
      if (OrderToNoLoss != null) {
        try {
          OrderToNoLoss(this, new FXW.OrderEventArgs(order));
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

    ObservableCollection<Models.TradingAccount> slaveAccounts = new ObservableCollection<Models.TradingAccount>();

    public ObservableCollection<Models.TradingAccount> SlaveAccounts {
      get {
        if (slaveAccounts.Count == 0)
          GlobalStorage.GetTradingAccounts().ToList().ForEach(ta => slaveAccounts.Add(ta));
        return slaveAccounts; 
      }
      set { slaveAccounts = value; }
    }

    public ObjectSet<Models.TradingAccount> TradingAccountsSet {
      get {
        try {
          return GlobalStorage.Context.TradingAccounts;
        } catch (Exception exc) {
          Log = exc;
          return null;
        }
      }
    }
    public Models.TradingAccount TradingMaster { get { return TradingMasters.FirstOrDefault(); } }
    public IQueryable<Models.TradingAccount> TradingMasters { get { return TradingAccountsSet.Where(ta => ta.IsMaster); } }
    public Models.TradingAccount[] TradingSlaves { get { return TradingAccountsSet.Where(ta=>!ta.IsMaster).ToArray(); } }

    SlaveAccountModel[] _slaveModels;
    public SlaveAccountModel[] SlaveModels {
      get {
        if( _slaveModels == null || _slaveModels.Length == 0)
          _slaveModels = TradingSlaves.Select(ts => new SlaveAccountModel(this, ts)).ToArray();
        return _slaveModels;
      }
    }

    public CollectionViewSource TradingAccountsView { get; set; }

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
      TradingAccountsSet.DeleteObject(ta as Models.TradingAccount);
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
      } catch (Exception exc) { Log = exc; }
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
      TradingAccountsSet.AddObject(
        new Models.TradingAccount() { AccountId = account, Password = password, IsDemo = true, IsMaster = false, TradeRatio = "1:1" });
      SaveTradingSlaves();
    }


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
      new ThreadScheduler((s, e) => Log = e.Exception).Command = () => Login(li.Account, li.Password, li.IsDemo);
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
      ServerTradesList = new ListCollectionView(ServerTrades = new ObservableCollection<Trade>());
      ServerTradesList.SortDescriptions.Add(new SortDescription(Lib.GetLambda(() => new Trade().Pair), ListSortDirection.Ascending));
      ServerTradesList.SortDescriptions.Add(new SortDescription(Lib.GetLambda(() => new Trade().Time), ListSortDirection.Descending));
      OrdersList = new ListCollectionView(orders = new ObservableCollection<Order>());
      AbsentTradesList = new ListCollectionView(AbsentTrades = new ObservableCollection<Trade>());

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
        AccountModel.Update(fwMaster.GetAccount(), 0, fwMaster.ServerTime);
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

      Using_FetchServerTrades = () => { 
        Using(FetchServerTrades);
      };
      if (!isInDesign) {
        GetTradesScheduler = new ThreadScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        Using_FetchServerTrades,
        (s, e) => { Log = e.Exception; });
      }
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

    void fwMaster_Error(object sender, O2G.ErrorEventArgs e) {
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
        if (CoreFX.LogOn(tradingAccount, tradingPassword, tradingDemo)) {
          RaiseSlaveLoginRequestEvent();
          InvokeSyncronize(fwMaster.GetAccount());
          return true;
        } else return false;
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
      PriceChangedSchedulers.Run(Price.Pair, () => RunPriceChanged(Price.Pair));
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
      fwMaster_PriceChanged(trade.Pair);
    }
    void fwMaster_TradeChanged(object sender, FXW.TradeEventArgs e) {
      fwMaster_PriceChanged(e.Trade.Pair);
    }

    void fwMaster_OrderChanged(object sender, FXW.OrderEventArgs e) {
      fwMaster_PriceChanged(e.Order.Pair);
    }
    void fwMaster_OrderAdded(object sender, FXW.OrderEventArgs e) {
      fwMaster_PriceChanged(e.Order.Pair);
    }


    void FetchServerTrades(TraderService.TraderServiceClient tsc) {
      try {
        InvokeSyncronize(tsc.GetAccount());
      } catch (Exception exc) { Log = exc; }
    }

    void InvokeSyncronize(Account account) {
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
    private bool CanExecuteStopChange(Trade trade) { return CanExecuteStopLimitChange(trade, t => t.Stop != 0); }
    private bool CanExecuteLimitChange(Trade trade) { return CanExecuteStopLimitChange(trade, t => t.Limit != 0); }
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

    #endregion

  }
  public class TraderModelDesign : TraderModel {
  }
  public enum AliceModes {Neverland = 0, Wonderland = 1, Mirror = 2 }
}
