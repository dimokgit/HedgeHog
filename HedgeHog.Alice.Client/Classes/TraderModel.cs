using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using XLinq = System.Xml.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using HedgeHog.Alice.Client.UI.Controls;
using HedgeHog.Alice.Store;
using HedgeHog.DB;
using HedgeHog.Shared;
using FXW = HedgeHog.Shared.ITradesManager;
using Gala = GalaSoft.MvvmLight.Command;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using NotifyCollectionChangedWrapper;
using System.Threading.Tasks.Dataflow;
using HedgeHog.Shared.Messages;
using ReactiveUI;
using System.Reactive;
using static HedgeHog.ReflectionCore;
using IBApp;
using static HedgeHog.Core.JsonExtensions;
using Newtonsoft.Json;
using System.Net;
using System.Reactive.Subjects;
using Loggly;
using Loggly.Config;

namespace HedgeHog.Alice.Client {
  public class MasterListChangedEventArgs :EventArgs {
    public IList<Trade> MasterTrades { get; set; }
    public MasterListChangedEventArgs(IList<Trade> masterTrades)
      : base() {
      this.MasterTrades = masterTrades;
    }
  }
  [Export]
  [Export(typeof(TraderModelBase))]
  [Export("MainWindowModel")]
  public class TraderModel :TraderModelBase {
    static object _defaultLocker = new object();
    static private TraderModel _default;
    static public TraderModel Default {
      get {
        lock(_defaultLocker)
          if(_default == null)
            _default = new TraderModel();
        return _default;
      }
    }
    #region Title
    public string TitleRoot {
      get { return TitleImpl(); }
    }

    #endregion
    public string TitleImpl() {
      return Common.CurrentDirectory.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Last() + ":" + IpPortActual;
    }

    #region FXCM
    void ToJsonLog(object o) => Log = o is Exception ? (Exception)o : new Exception(o.GetType() == typeof(string) || o.IsAnonymous() ? o + "" : o.ToJson(Formatting.None));
    bool _isIB => MasterAccount.Broker == "IB";
    ICoreFX _coreFX;
    public override ICoreFX CoreFX { get { return _coreFX ?? (_coreFX = _isIB ? IBClientCore.Create(ToJsonLog) : throw new Exception(new { _isIB } + "")); } }
    FXW _fwMaster;

    public override FXW FWMaster {
      get { return _fwMaster ?? (_fwMaster = _isIB ? new IBWraper(CoreFX, CommissionByTrade) : throw new Exception(new { _isIB } + "")); }
    }
    public bool IsLoggedIn { get { return CoreFX != null && CoreFX.IsLoggedIn; } }
    bool _isInLogin;
    public bool IsInLogin {
      get { return _isInLogin; }
      set { _isInLogin = value; RaisePropertyChanged(() => IsInLogin, () => IsNotInLogin); }
    }
    public bool IsNotInLogin { get { return !IsInLogin; } }

    private VirtualTradesManager virtualTrader;
    public VirtualTradesManager VirtualTrader {
      get { return virtualTrader; }
      set { virtualTrader = value; }
    }
    public override FXW TradesManager { get { return IsInVirtualTrading ? virtualTrader : FWMaster; } }

    private TradingServerSessionStatus _SessionStatus = TradingServerSessionStatus.Disconnected;
    public TradingServerSessionStatus SessionStatus {
      get { return _SessionStatus; }
      set {
        if(_SessionStatus != value) {
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

    private int _IpPortActual = 0;
    public int IpPortActual {
      get { return _IpPortActual; }
      set {
        if(_IpPortActual != value) {
          _IpPortActual = value;
          RaisePropertyChangedCore();
          OnPropertyChanged(GetLambda(() => TitleRoot));
        }
      }
    }

    private bool _VirtualPause;
    public bool VirtualPause {
      get { return _VirtualPause; }
      set {
        _VirtualPause = value;
        this.BackTestEventArgs.Pause = value;
        RaisePropertyChanged(() => VirtualPause);
        CommandManager.InvalidateRequerySuggested();
      }
    }

    private bool _VirtualClearTest = true;
    public bool VirtualClearTest {
      get { return _VirtualClearTest; }
      set {
        if(_VirtualClearTest != value) {
          _VirtualClearTest = value;
          this.BackTestEventArgs.Pause = value;
          RaisePropertyChangedCore();
        }
      }
    }
    public override TradingAccount MasterAccount => TradingMaster;
    #endregion

    #region Events
    TradingStatistics _tradingStatistics;
    public TradingStatistics TradingStatistics {
      get {
        return _tradingStatistics;
      }
      set {
        if(_tradingStatistics == value)
          return;
        _tradingStatistics = value;
        RaisePropertyChanged(() => TradingStatistics);
      }
    }
    public override event EventHandler<TradingStatisticsEventArgs> NeedTradingStatistics;
    protected void OnNeedTradingStatistics() {
      if(NeedTradingStatistics != null && TradingStatistics == null) {
        var tse = new TradingStatisticsEventArgs();
        NeedTradingStatistics(this, tse);
        TradingStatistics = tse.TradingStatistics;
      }
    }

    public override event EventHandler MasterTradeAccountChanged;
    protected void OnMasterTradeAccountChanged() {
      if(MasterTradeAccountChanged != null)
        MasterTradeAccountChanged(this, EventArgs.Empty);
    }

    EventHandler _stepForwarRequery;
    EventHandler stepForwarRequery {
      get {
        if(_stepForwarRequery == null)
          _stepForwarRequery = new EventHandler(CommandManager_RequerySuggested);
        return _stepForwarRequery;
      }
    }
    ICommand _StepForwardCommand;
    public ICommand StepForwardCommand {
      get {
        if(_StepForwardCommand == null) {
          _StepForwardCommand = new Gala.RelayCommand(OnStepForward, () => VirtualPause);
          _StepForwardCommand.CanExecuteChanged += new EventHandler(_StepForwardCommand_CanExecuteChanged);
          CommandManager.RequerySuggested += stepForwarRequery;
        }
        return _StepForwardCommand;
      }
    }

    void CommandManager_RequerySuggested(object sender, EventArgs e) {
    }

    void _StepForwardCommand_CanExecuteChanged(object sender, EventArgs e) {
    }
    public override event EventHandler<EventArgs> StepForward;
    protected void OnStepForward() {
      if(StepForward != null)
        StepForward(this, new EventArgs());
    }


    ICommand _StepBackCommand;
    public ICommand StepBackCommand {
      get {
        if(_StepBackCommand == null) {
          _StepBackCommand = new Gala.RelayCommand(OnStepBack, () => VirtualPause);
        }
        return _StepBackCommand;
      }
    }

    public override event EventHandler<EventArgs> StepBack;
    protected void OnStepBack() {
      if(StepBack != null)
        StepBack(this, new EventArgs());
    }

    public override event EventHandler<OrderEventArgs> OrderToNoLoss;
    protected void OnOrderToNoLoss(Order order) {
      if(OrderToNoLoss != null) {
        try {
          OrderToNoLoss(this, new OrderEventArgs(order));
        } catch(Exception exc) { Log = exc; }
      }
    }

    #region MasterListChangedEvent
    public delegate void MasterListChangedeventHandler(object sender, MasterListChangedEventArgs e);
    public event MasterListChangedeventHandler MasterListChangedEvent;

    protected virtual void RaiseMasterListChangedEvent(IList<Trade> trades) {
      if(MasterListChangedEvent != null)
        MasterListChangedEvent(this, new MasterListChangedEventArgs(trades));
    }
    #endregion

    public event EventHandler SlaveLoginRequestEvent;
    protected void RaiseSlaveLoginRequestEvent() {
      if(SlaveLoginRequestEvent != null)
        SlaveLoginRequestEvent(this, new EventArgs());
    }
    #endregion

    #region Trade Lists
    private bool _IsAccountManagerExpanded = true;
    public bool IsAccountManagerExpanded {
      get { return _IsAccountManagerExpanded; }
      set {
        if(_IsAccountManagerExpanded != value) {
          _IsAccountManagerExpanded = value;
          RaisePropertyChanged("IsAccountManagerExpanded");
        }
      }
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
    TradingAccount[] _tradingAccounts;
    public ListCollectionView TradingAccountsList {
      get {
        if(_tradingAccountsList == null) {
          _TradingAccountsSet = new ObservableCollection<Store.TradingAccount>(_tradingAccounts);
          _TradingAccountsSet.CollectionChanged += _TradingAccountsSet_CollectionChanged;
          _tradingAccountsList = new ListCollectionView(_TradingAccountsSet);
          _tradingAccountsList.Filter = FilterTradingAccounts;
          _tradingAccountsList.CurrentChanged += _tradingAccountsList_CurrentChanged;

          //new Predicate<TradingAccount>(ta => new[] { ta.AccountId, ta.MasterId }.Contains(TradingAccount) || ShowAllAccountsFilter) as Predicate<object>;
        }
        return _tradingAccountsList;
      }
    }

    #region TradingMacroNameChanged Event
    event EventHandler<EventArgs> TradingMacroNameChangedEvent;
    public override event EventHandler<EventArgs> TradingMacroNameChanged {
      add {
        if(TradingMacroNameChangedEvent == null || !TradingMacroNameChangedEvent.GetInvocationList().Contains(value))
          TradingMacroNameChangedEvent += value;
      }
      remove {
        TradingMacroNameChangedEvent -= value;
      }
    }
    protected void RaiseTradingMacroNameChanged() {
      if(TradingMacroNameChangedEvent != null)
        TradingMacroNameChangedEvent(this, new EventArgs());
    }
    #endregion


    void _tradingAccountsList_CurrentChanged(object sender, EventArgs e) {
      OnMasterTradeAccountChanged();
    }

    private bool FilterTradingAccounts(object o) {
      var ta = o as TradingAccount;
      return ta.IsActive || ShowAllAccountsFilter;
      //return new[] { ta.AccountId, ta.MasterId }.Contains(TradingAccount) || ShowAllAccountsFilter;
    }

    ObservableCollection<TradingAccount> _TradingAccountsSet;
    public IEnumerable<TradingAccount> TradingAccountsSet {
      get {
        try {
          return TradingAccountsList.SourceCollection.OfType<TradingAccount>().Where(FilterTradingAccounts);
        } catch(Exception exc) {
          Log = exc;
          throw;
        }
      }
    }

    void _TradingAccountsSet_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
      var ta = e.NewItems[0] as TradingAccount;
      switch(e.Action) {
        case NotifyCollectionChangedAction.Add:
          throw new NotImplementedException("_TradingAccountsSet_CollectionChanged");
          break;
        case NotifyCollectionChangedAction.Remove:
          throw new NotImplementedException("_TradingAccountsSet_CollectionChanged");
          break;
      }
    }

    public TradingAccount TradingMaster { get { return TradingMasters.Single(); } }
    public IEnumerable<TradingAccount> TradingMasters { get { return TradingAccountsSet.Where(ta => ta.IsMaster); } }
    public TradingAccount[] TradingSlaves { get { return TradingAccountsSet.Where(ta => !ta.IsMaster).ToArray(); } }


    public NotifyCollectionChangedWrapper<Order> orders { get; set; }
    public ListCollectionView OrdersList { get; set; }

    public NotifyCollectionChangedWrapper<Trade> ServerTrades { get; set; }
    public ListCollectionView ServerTradesList { get; set; }

    public NotifyCollectionChangedWrapper<Trade> ClosedTrades { get; set; }
    public ListCollectionView ClosedTradesList { get; set; }

    public NotifyCollectionChangedWrapper<MarketHours> Markets { get; set; }
    public ListCollectionView MarketsList { get; set; }

    public NotifyCollectionChangedWrapper<Trade> AbsentTrades { get; set; }
    public ListCollectionView AbsentTradesList { get; set; }

    private TradingAccountModel _accountModel;
    public override TradingAccountModel AccountModel {
      get {
        if(_accountModel == null) {
          _accountModel = new TradingAccountModel();
          AccountModel.CloseAllTrades += CloseAllTrades;
        }
        return _accountModel;
      }
    }

    void CloseAllTrades(object sender, EventArgs e) {
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(new CloseAllTradesMessage<TradingMacro>(null, tm => tm.CloseTrades(GetType().Name + "::CloseAllTrades")));
    }
    public TradingAccountModel[] ServerAccountRow { get { return new[] { AccountModel }; } }
    public override double CurrentLoss { set { AccountModel.CurrentLoss = value; } }

    IDisposable _priceChangeSubscribtion;
    public IDisposable PriceChangeSubscribtion {
      get { return _priceChangeSubscribtion; }
      set {
        if(_priceChangeSubscribtion != null)
          _priceChangeSubscribtion.Dispose();
        _priceChangeSubscribtion = value;
      }
    }
    IDisposable _tradeColsedSubscribtion;
    public IDisposable TradeColsedSubscribtion {
      get { return _tradeColsedSubscribtion; }
      set {
        if(_tradeColsedSubscribtion != null)
          _tradeColsedSubscribtion.Dispose();
        _tradeColsedSubscribtion = value;
      }
    }
    private IDisposable _orderChangedSubscribtion;
    public IDisposable OrderChangedSubscribtion {
      get { return _orderChangedSubscribtion; }
      set {
        if(_orderChangedSubscribtion != null)
          _orderChangedSubscribtion.Dispose();
        _orderChangedSubscribtion = value;
      }
    }

    #region SlaveAccountInfos
    ObservableCollection<TradingAccountModel> SlaveAccountInfos = new ObservableCollection<TradingAccountModel>();
    ListCollectionView _slaveAccountInfosView;
    public ListCollectionView SlaveAccountInfosView {
      get {
        if(_slaveAccountInfosView == null)
          _slaveAccountInfosView = new ListCollectionView(SlaveAccountInfos);
        return _slaveAccountInfosView;
      }
    }
    #endregion

    #endregion

    #region ServerAddress
    string _serverAddress = "";
    string serverAddressPostfix = "HedgeHog.Alice.WCF";
    string serverAddressSuffix = "net.tcp://";
    public string ServerAddress {
      get { return Path.Combine(serverAddressSuffix, _serverAddress, serverAddressPostfix); }
      set { _serverAddress = value; }
    }
    public bool isInRemoteMode { get { return false && !string.IsNullOrWhiteSpace(_serverAddress); } }
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
    public string LogText {
      get {
        lock(_logQueue) {
          return string.Join(Environment.NewLine, _logQueue.Reverse());
        }
      }
    }

    #region Log Subject
    object _LogSubjectLocker = new object();
    ISubject<Exception> _LogSubject;
    ISubject<Exception> LogSubject {
      get {
        lock(_LogSubjectLocker)
          if(_LogSubject == null) {
            _LogSubject = new Subject<Exception>();
            _LogSubject
              .ObserveOn(ThreadPoolScheduler.Instance)
              .SelectMany(exc => IEnumerableCore.WithError(LogToFile, exc).WithError(t => LogToCloud(t.param)).error)
              .Subscribe(exc => _logQueue.Enqueue(Environment.NewLine + exc)
            , exc => Log = exc);
          }
        return _LogSubject;
      }
    }
    public void OnLog(Exception exc) => LogSubject.OnNext(exc);
    #endregion


    private IDisposable _logExpandedTargetBlock;
    Queue<string> _logQueue = new Queue<string>();
    Exception _log;

    public override Exception Log {
      get { return _log; }
      set {
        if(isInDesign)
          return;
        _log = value;
        OnLog(value);
      }
    }

    private Exception LogToFile(Exception exc) {
      try {
        lock(_logQueue) {
          while(_logQueue.Count > 150)
            _logQueue.Dequeue();
          var messages = new List<string>(new[] { DateTime.Now.ToString("[dd HH:mm:ss.fff] ") + exc.GetExceptionShort() });
          while(exc.InnerException != null) {
            messages.Add(exc.InnerException.GetExceptionShort());
            exc = exc.InnerException;
          }
          _logQueue.Enqueue(string.Join(Environment.NewLine + "-", messages));
        }
        FileLogger.LogToFile(exc);

        //ReactiveUI.MessageBus.Current.SendMessage<WwwWarningMessage>(new WwwWarningMessage(exc.Message));

        lastLogTime = DateTime.Now;
        RaisePropertyChanged(nameof(LogText));
        RaisePropertyChanged(nameof(IsLogExpanded));
        RaisePropertyChanged(nameof(IsLogPinned));
        IsLogExpanded = true;
        if(_logExpandedTargetBlock != null) {
          _logExpandedTargetBlock.Dispose();
        }
        try {
          _logExpandedTargetBlock = new Action(() => IsLogExpanded = false).ScheduleOnUI(10.FromSeconds());
        } catch(InvalidOperationException) { }
      } catch(Exception exc2) {
        AsyncMessageBox.BeginMessageBoxAsync(exc2 + "");
      }
      return exc;
    }

    static readonly ILogglyConfig CloudLogConfig = InitCloudLogger();
    private static ILogglyConfig InitCloudLogger() {

      var config = LogglyConfig.Instance;
      config.CustomerToken = "217a334f-b2bd-4eb8-bcc2-ee55ba3c7f0a";
      config.ApplicationName = $"{CurrentDirectory()}";

      config.Transport.EndpointHostname = "logs-01.loggly.com";
      config.Transport.EndpointPort = 443;
      config.Transport.LogTransport = LogTransport.Https;

      var ct = new ApplicationNameTag();
      ct.Formatter = "{0}";
      config.TagConfig.Tags.Add(ct);
      return config;
    }

    Exception LogToCloud(Exception exc) {
      if(!CloudLogConfig.IsValid) return exc;
      ILogglyClient _loggly = new LogglyClient();
      var logEvent = new LogglyEvent();
      logEvent.Data.Add("message", string.Join(Environment.NewLine, FileLogger.ExceptionMessages(exc)));
      _loggly.Log(logEvent);
      return exc;
    }

    ITargetBlock<object> _LogExoandedTargetBlock;
    ITargetBlock<object> LogExoandedTargetBlock {
      get {
        if(_LogExoandedTargetBlock == null)
          _LogExoandedTargetBlock = new Action<object>((o) => RaisePropertyChanged(() => IsLogExpanded, () => IsLogPinned)).CreateYieldingTargetBlock(exc => Log = exc, false, TaskScheduler.FromCurrentSynchronizationContext());
        return _LogExoandedTargetBlock;
      }
    }

    public bool IsLogPinned { get { return !IsLogExpanded; } }
    bool _IsLogExpanded = false;
    public bool IsLogExpanded {
      get {
        return _IsLogExpanded;
      }
      set {
        if(_IsLogExpanded != value) {
          _IsLogExpanded = value;
          RaisePropertyChanged("IsLogExpanded");
        }
      }
    }
    #endregion

    #region Commanding

    #region LoginCommand

    ReactiveCommand<TradingAccount, Unit> _LoginCommand;
    public ReactiveCommand<TradingAccount, Unit> LoginCommand {
      get {
        if(_LoginCommand == null) {
          _LoginCommand = ReactiveUI.ReactiveCommand.Create<TradingAccount>(Login);
          //_LoginCommand.Subscribe(Login);
        }

        return _LoginCommand;
      }
    }
    void Login(TradingAccount tradingAccount) {
      var ta = tradingAccount as TradingAccount;
      LoginAsync(_isIB ? IB_IPAddress + "" : ta.AccountId, _isIB ? IB_IPPort + "" : ta.AccountSubId, _isIB ? IB_ClientId + "" : ta.Password, ta.IsDemo);
    }

    #endregion

    #region ToggleShowAllAccountsCommand
    ICommand _ToggleShowAllAccountsCommand;
    public ICommand ToggleShowAllAccountsCommand {
      get {
        if(_ToggleShowAllAccountsCommand == null) {
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
        if(_SetOrderToNoLossCommand == null) {
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
        if(_IncreaseLimitCommand == null)
          _IncreaseLimitCommand = new Gala.RelayCommand<Trade>(IncreaseLimit, CanExecuteLimitChange);
        return _IncreaseLimitCommand;
      }
    }
    void IncreaseLimit(Trade trade) {
      try {
        AddtDelta(trade.Id, 1, limitDeltas);
        if(!changeLimitScheduler.IsRunning)
          changeLimitScheduler.Run();
      } catch(Exception exc) {
        Log = new Exception("DecreaseLimit Error", exc);
      }
    }
    #endregion

    #region DecreaseLimitCommand
    ICommand _DecreaseLimitCommand;
    public ICommand DecreaseLimitCommand {
      get {
        if(_DecreaseLimitCommand == null)
          _DecreaseLimitCommand = new Gala.RelayCommand<Trade>(DecreaseLimit, CanExecuteLimitChange);
        return _DecreaseLimitCommand;
      }
    }

    void DecreaseLimit(Trade trade) {
      try {
        AddtDelta(trade.Id, -1, limitDeltas);
        if(!changeLimitScheduler.IsRunning)
          changeLimitScheduler.Run();
      } catch(Exception exc) {
        Log = new Exception("DecreaseLimit Error", exc);
      }
    }
    #endregion

    #region DecreaseStopCommand
    ICommand _DecreaseStopCommand;
    public ICommand DecreaseStopCommand {
      get {
        if(_DecreaseStopCommand == null)
          _DecreaseStopCommand = new Gala.RelayCommand<Trade>(DecreaseStop, CanExecuteStopChange);
        return _DecreaseStopCommand;
      }
    }
    void DecreaseStop(Trade trade) {
      try {
        AddtDelta(trade.Id, -1, stopDeltas);
        if(!changeStopScheduler.IsRunning)
          changeStopScheduler.Run();
      } catch(Exception exc) {
        Log = new Exception("DecreaseStop Error", exc);
      }
    }
    #endregion


    #region Entry Order Commands
    ICommand _IncreaseEntryStopCommand;
    public ICommand IncreaseEntryStopCommand {
      get {
        if(_IncreaseEntryStopCommand == null) {
          _IncreaseEntryStopCommand = new Gala.RelayCommand<Order>(IncreaseEntryStop, (o) => true);
        }

        return _IncreaseEntryStopCommand;
      }
    }
    void IncreaseEntryStop(Order order) {
      TradesManager.ChangeEntryOrderPeggedStop(order.OrderID, order.StopInPips + 1);
    }

    ICommand _DecreaseEntryStopCommandCommand;
    public ICommand DecreaseEntryStopCommandCommand {
      get {
        if(_DecreaseEntryStopCommandCommand == null) {
          _DecreaseEntryStopCommandCommand = new Gala.RelayCommand<Order>(DecreaseEntryStopCommand, (o) => true);
        }

        return _DecreaseEntryStopCommandCommand;
      }
    }
    void DecreaseEntryStopCommand(object o) {
      var order = o as Order;
      TradesManager.ChangeEntryOrderPeggedStop(order.OrderID, order.StopInPips - 1);
    }


    ICommand _IncreaseEntryLimitCommand;
    public ICommand IncreaseEntryLimitCommand {
      get {
        if(_IncreaseEntryLimitCommand == null) {
          _IncreaseEntryLimitCommand = new Gala.RelayCommand<Order>(IncreaseEntryLimit, (o) => true);
        }

        return _IncreaseEntryLimitCommand;
      }
    }
    void IncreaseEntryLimit(Order order) {
      TradesManager.ChangeEntryOrderPeggedLimit(order.OrderID, order.LimitInPips + 1);
    }

    ICommand _DecreaseEntryLimitCommand;
    public ICommand DecreaseEntryLimitCommand {
      get {
        if(_DecreaseEntryLimitCommand == null) {
          _DecreaseEntryLimitCommand = new Gala.RelayCommand<Order>(DecreaseEntryLimit, (o) => true);
        }

        return _DecreaseEntryLimitCommand;
      }
    }
    void DecreaseEntryLimit(Order order) {
      TradesManager.ChangeEntryOrderPeggedLimit(order.OrderID, order.LimitInPips - 1);
    }


    ICommand _IncreaseEntryRateCommand;
    public ICommand IncreaseEntryRateCommand {
      get {
        if(_IncreaseEntryRateCommand == null) {
          _IncreaseEntryRateCommand = new Gala.RelayCommand<Order>(IncreaseEntryRate, (o) => true);
        }

        return _IncreaseEntryRateCommand;
      }
    }
    void IncreaseEntryRate(Order order) {
      TradesManager.ChangeOrderRate(order, order.Rate + order.PointSize);
    }

    ICommand _DecreaseEntryRateCommand;
    public ICommand DecreaseEntryRateCommand {
      get {
        if(_DecreaseEntryRateCommand == null) {
          _DecreaseEntryRateCommand = new Gala.RelayCommand<Order>(DecreaseEntryRate, (o) => true);
        }

        return _DecreaseEntryRateCommand;
      }
    }
    void DecreaseEntryRate(Order order) {
      TradesManager.ChangeOrderRate(order, order.Rate - order.PointSize);
    }

    ICommand _CancelEntryOrderCommand;
    public ICommand CancelEntryOrderCommand {
      get {
        if(_CancelEntryOrderCommand == null) {
          _CancelEntryOrderCommand = new Gala.RelayCommand<Order>(CancelEntryOrder, (o) => true);
        }

        return _CancelEntryOrderCommand;
      }
    }
    void CancelEntryOrder(Order order) {
      TradesManager.DeleteOrder(order.OrderID);
    }

    #endregion

    #region EncreaseStopCommand
    ICommand _EncreaseStopCommand;
    public ICommand EncreaseStopCommand {
      get {
        if(_EncreaseStopCommand == null)
          _EncreaseStopCommand = new Gala.RelayCommand<Trade>(EncreaseStop, CanExecuteStopChange);
        return _EncreaseStopCommand;
      }
    }

    void EncreaseStop(Trade trade) {
      try {
        AddtDelta(trade.Id, 1, stopDeltas);
        if(!changeStopScheduler.IsRunning)
          changeStopScheduler.Run();
      } catch(Exception exc) {
        Log = new Exception("EncreaseStop Error", exc);
      }
    }
    #endregion

    #region DeleteTradingAccountCommand

    ICommand _DeleteTradingAccountCommand;
    public ICommand DeleteTradingAccountCommand {
      get {
        if(_DeleteTradingAccountCommand == null) {
          _DeleteTradingAccountCommand = new Gala.RelayCommand<object>(DeleteTradingAccount, (ta) => true);
        }

        return _DeleteTradingAccountCommand;
      }
    }
    void DeleteTradingAccount(object ta) {
      throw new NotImplementedException("DeleteTradingAccount");
      SaveTradingSlaves();
    }

    #endregion

    #region SaveTradingSlaves

    ICommand _SaveTradingSlavesCommand;
    public ICommand SaveTradingSlavesCommand {
      get {
        if(_SaveTradingSlavesCommand == null) {
          _SaveTradingSlavesCommand = new Gala.RelayCommand(SaveTradingSlaves, () => true);
        }

        return _SaveTradingSlavesCommand;
      }
    }
    static string _accountsPath = "Accounts.json";
    void SaveTradingSlaves() {
      try {
        //GlobalStorage.UseAliceContextSaveChanges();

        //GlobalStorage.SaveJson(_tradingAccounts, _accountsPath);
        //IMapper traderMapper2 = new MapperConfiguration(cfg => cfg.CreateMap<TraderModelBase, TraderModelPersist>()).CreateMapper();
        //GlobalStorage.UseForexMongo(c => c.TraderSettings.Add(traderMapper2.Map<TraderModelPersist>(this)));
        GlobalStorage.SaveTraderSettings(this);
        GlobalStorage.ForexMongoSave();
        Log = new Exception("Trade accounts were saved");
      } catch(Exception exc) {
        Log = exc;
        MessageBox.Show(exc + "");
      }
    }

    #endregion

    #region AddNewSlaveAccountCommand
    ICommand _AddNewSlaveAccountCommand;
    public ICommand AddNewSlaveAccountCommand {
      get {
        if(_AddNewSlaveAccountCommand == null) {
          _AddNewSlaveAccountCommand = new Gala.RelayCommand(AddNewSlaveAccount, () => true);
        }

        return _AddNewSlaveAccountCommand;
      }
    }
    void AddNewSlaveAccount() {
      throw new NotImplementedException();
      string account, password;
      FXCM.Lib.GetNewAccount(out account, out password);
      SaveTradingSlaves();
    }
    #endregion

    #region OpenNewServerAccountCommand

    ICommand _OpenNewServerAccountCommand;
    public ICommand OpenNewServerAccountCommand {
      get {
        if(_OpenNewServerAccountCommand == null) {
          _OpenNewServerAccountCommand = new Gala.RelayCommand(OpenNewServerAccount, () => true);
        }

        return _OpenNewServerAccountCommand;
      }
    }
    void OpenNewServerAccount() {
      try {
        throw new NotImplementedException();
        //Using(tsc => {
        //  string account, password;
        //  FXCM.Lib.GetNewAccount(out account, out password);
        //  TradingAccount = account;
        //  TradingPassword = password;
        //  TradingDemo = true;
        //  if( isInRemoteMode)
        //    tsc.OpenNewAccount(account, password);
        //  Log = new Exception("New Rabit has arrived");
        //});
      } catch(Exception exc) { Log = exc; }
    }

    #endregion

    #region UpdatePasswordCommand
    ICommand _UpdatePasswordCommand;
    public ICommand UpdatePasswordCommand {
      get {
        if(_UpdatePasswordCommand == null) {
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
        if(_CloseServerTradeCommand == null) {
          _CloseServerTradeCommand = new Gala.RelayCommand<string>(CloseServerTrade, Id => true);
        }

        return _CloseServerTradeCommand;
      }
    }
    void CloseServerTrade(string tradeId) {
      if(isInRemoteMode)
        throw new NotImplementedException();
      //Using(tsc => {
      //  try {
      //    Log = new Exception("Trade " + tradeId + " was closed with OrderId " + tsc.CloseTrade(tradeId));
      //  } catch (Exception exc) { Log = exc; }
      //});
      else
        try {
          TradesManager.CloseTrade(TradesManager.GetTrades().Single(t => t.Id == tradeId));
          Log = new Exception("Trade " + tradeId + " was closed");
        } catch(Exception exc) { Log = exc; }
    }
    #endregion

    #region BackTestCommand
    BackTestEventArgs BackTestEventArgs = new BackTestEventArgs();
    #endregion

    #region BackTestStepBackCommand

    ICommand _BackTestStepBackCommand;
    public ICommand BackTestStepBackCommand {
      get {
        if(_BackTestStepBackCommand == null) {
          _BackTestStepBackCommand = new Gala.RelayCommand(BackTestStepBack, () => true);
        }

        return _BackTestStepBackCommand;
      }
    }
    void BackTestStepBack() {
      BackTestEventArgs.StepBack = true;
    }

    #endregion

    #region LoadHistoryCommand

    ICommand _LoadHistoryCommand;
    public ICommand LoadHistoryCommand {
      get {
        if(_LoadHistoryCommand == null) {
          _LoadHistoryCommand = new Gala.RelayCommand(LoadHistory, () => !isLoadHistoryTaskRunning);
        }

        return _LoadHistoryCommand;
      }
    }
    Task loadHistoryTask;
    bool isLoadHistoryTaskRunning { get { return loadHistoryTask != null && loadHistoryTask.Status == TaskStatus.Running; } }
    void LoadHistory() {
      if(isLoadHistoryTaskRunning)
        MessageBox.Show("LoadHistoryTask is in " + loadHistoryTask.Status + " status.");
      else {
        Action a = () => { PriceHistory.LoadBars(TradesManager, "", o => Log = new Exception(o + "")); };
        if(loadHistoryTask == null)
          loadHistoryTask = Task.Factory.StartNew(a);
        else
          loadHistoryTask.Start();
      }
    }

    #endregion

    #region ReportCommand
    ICommand _ReportCommand;
    public ICommand ReportCommand {
      get {
        if(_ReportCommand == null) {
          _ReportCommand = new Gala.RelayCommand(Report, () => true);
        }
        return _ReportCommand;
      }
    }
    void Report() {
      throw new NotImplementedException();
      //new Reports.Report(FWMaster).Show();
    }
    #endregion

    #region Close All Server Trades Command
    ICommand _CloseAllServerTradesCommand;
    public ICommand CloseAllServerTradesCommand {
      get {
        if(_CloseAllServerTradesCommand == null) {
          _CloseAllServerTradesCommand = new Gala.RelayCommand(CloseAllServerTrades, () => true);
        }

        return _CloseAllServerTradesCommand;
      }
    }

    private void CloseAllServerTrades() {
      if(isInRemoteMode)
        throw new NotImplementedException();
      //Using(tsc => {
      //  try {
      //    Log = new Exception("Closing all server trades.");
      //    var ordersIds = tsc.CloseAllTrades();
      //    Log = new Exception("Trades closed:" + string.Join(",", ordersIds));
      //  } catch (Exception exc) { Log = exc; }
      //});
      else {
        try {
          Log = new Exception("Closing all trades.");
          var trades = TradesManager.GetTradesInternal("");
          foreach(var pair in trades.Select(t => t.Pair).Distinct())
            TradesManager.ClosePair(pair);
          Log = new Exception("Trades closed:" + string.Join(",", trades.Select(t => t.Id)));
        } catch(Exception exc) {
          Log = exc;
        }
      }
    }

    #endregion

    #region AccountLoginCommand

    private void LoginAsync(string account, string accountSubId, string password, bool isDemo) {
      if(IsInVirtualTrading)
        new Action(() => Login(account, accountSubId, password, isDemo)).ScheduleOnUI();
      else
        Login(account, accountSubId, password, isDemo);
    }

    #endregion

    #region OpenDataBaseCommand
    private string _DatabasePath;
    public string DatabasePath {
      get { return _DatabasePath; }
      set {
        if(_DatabasePath != value) {
          _DatabasePath = value;
          RaisePropertyChanged("DatabasePath");
        }
      }
    }

    ICommand _OpenDataBaseCommand;
    public ICommand OpenDataBaseCommand {
      get {
        if(_OpenDataBaseCommand == null) {
          _OpenDataBaseCommand = new Gala.RelayCommand(OpenDataBase, () => true);
        }
        return _OpenDataBaseCommand;
      }
    }
    void OpenDataBase() {
      var dbPath = GlobalStorage.OpenDataBasePath();
      if(string.IsNullOrWhiteSpace(dbPath))
        return;
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
        if(_TestCommand == null) {
          _TestCommand = new Gala.RelayCommand(Test, () => true);
        }

        return _TestCommand;
      }
    }
    void Test() { MessageBox.Show("Click!"); }
    #endregion

    #endregion

    #region Trading Info

    public string[] TradingAccounts {
      get {
        return _tradingAccounts.Select(ta => ta.AccountId).ToArray().Distinct().ToArray();
      }
    }

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
    Schedulers.ThreadScheduler GetTradesScheduler;
    public string Title { get { return AliceMode + ""; } }
    protected bool isInDesign { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    #endregion

    #region Ctor
    Schedulers.ThreadScheduler.CommandDelegate Using_FetchServerTrades;
    IObservable<EventPattern<PriceChangedEventArgs>> _priceChanged;
    public IObservable<EventPattern<PriceChangedEventArgs>> PriceChanged {
      get { return _priceChanged; }
      set {
        _priceChanged = value;
        OnPropertyChanged(GetLambda<TraderModel>(tm => tm.PriceChanged));
      }
    }
    private IObservable<EventPattern<TradeEventArgs>> _tradeAdded;
    public IObservable<EventPattern<TradeEventArgs>> TradeAdded {
      get { return _tradeAdded; }
      set {
        _tradeAdded = value;
        OnPropertyChanged(nameof(TradeAdded));
      }
    }
    private IObservable<EventPattern<MasterTradeEventArgs>> _tradeRemoved;
    public IObservable<EventPattern<MasterTradeEventArgs>> TradeRemoved {
      get { return _tradeRemoved; }
      set {
        _tradeRemoved = value;
        OnPropertyChanged(GetLambda<TraderModel>(tm => tm.TradeRemoved));
      }
    }

    TimeSpan _throttleInterval = TimeSpan.FromSeconds(1);
    static TraderModel() {
      var pack = new global::MongoDB.Bson.Serialization.Conventions.ConventionPack {
        new global::MongoDB.Bson.Serialization.Conventions.EnumRepresentationConvention(global::MongoDB.Bson.BsonType.String)
      };
      global::MongoDB.Bson.Serialization.Conventions.ConventionRegistry.Register("EnumStringConvention", pack, t => true);
    }
    TraderModel() : base() {
      lock(_defaultLocker) {
        if(_default != null)
          throw new InvalidOperationException(nameof(TraderModel) + " has already beem initialized.");
        _default = this;
        Initialize();
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Exception>(this, exc => Log = exc);
        LoadOffers();
        //_tradingAccounts = GlobalStorage.LoadJson<TradingAccount[]>(_accountsPath);
        _tradingAccounts = GlobalStorage.UseForexMongo(c => c.TradingAccount.ToArray());
        //GlobalStorage.UseForexMongo(c => c.TradingAccount.AddRange(_tradingAccounts.Select(ta => { ta._id = global::MongoDB.Bson.ObjectId.GenerateNewId(); return ta; })), true);
        var activeTradeAccounts = (_tradingAccounts?.Where(ta => ta.IsActive).ToArray());
        if(activeTradeAccounts.Length == 0) {
          Log = new Exception(new { activeTradeAccounts } + "");
          throw new Exception("No Trading Account found.");
        }
        if(activeTradeAccounts.Length > 1) {
          Log = new Exception(new { activeTradeAccounts } + "");
          throw new Exception("Multiple Trading Accounts found.");
        }
        TradingAccount = activeTradeAccounts.Single().AccountId;
        #region FXCM
        TradesManagerStatic.AccountCurrency = MasterAccount.Currency;
        virtualTrader = new VirtualTradesManager(TradingAccount, CommissionByTrade);
        virtualTrader.SetHasTicks(!_isIB);
        CoreFX.SubscribeToPropertyChanged(cfx => cfx.SessionStatus, cfx => SessionStatus = cfx.SessionStatus);
        //_coreFXObserver = new MvvmFoundation.Wpf.PropertyObserver<O2G.CoreFX>(this.CoreFX)
        //.RegisterHandler(c=>c.SessionStatus,c=>SessionStatus = c.SessionStatus);
        PriceChanged = Observable.FromEventPattern<EventHandler<PriceChangedEventArgs>, PriceChangedEventArgs>(h => h, h => TradesManager.PriceChanged += h, h => TradesManager.PriceChanged -= h);
        TradeAdded = Observable.FromEventPattern<EventHandler<TradeEventArgs>, TradeEventArgs>(h => h, h => TradesManager.TradeAdded += h, h => TradesManager.TradeAdded -= h);
        TradeRemoved = Observable.FromEventPattern<EventHandler<MasterTradeEventArgs>, MasterTradeEventArgs>(h => h, h => MasterTradeRemoved += h, h => MasterTradeRemoved -= h);
        CoreFX.LoggedIn += (s, e) => {
          IsInLogin = false;
          TradesManager.Error += FWMaster_Error;
          TradesManager.TradeAdded += FWMaster_TradeAdded;
          TradesManager.TradeRemoved += FWMaster_TradeRemoved;
          if(IsInVirtualTrading)
            TradesManager.PriceChanged += FWMaster_PriceChanged;
          else
            PriceChangeSubscribtion = PriceChanged
              .Do(ie => UpdateTradingAccountTargetBlock.Post(ie.EventArgs.Account))
              .Where(ie => TradesManager.GetTrades().Select(t => t.Pair).Contains(ie.EventArgs.Price.Pair))
              .Buffer(_throttleInterval)
              .Subscribe(l => {
                l.GroupBy(ie => ie.EventArgs.Price.Pair).Select(g => g.Last()).ToList()
                  .ForEach(ie => FWMaster_PriceChanged(ie.Sender, ie.EventArgs));
              });
          //.GroupByUntil(g => g.EventArgs.Pair, g => Observable.Timer(TimeSpan.FromSeconds(1), System.Concurrency.Scheduler.ThreadPool))
          //.Subscribe(g => g.TakeLast(1)
          //  .Subscribe(ie => FWMaster_PriceChanged(ie.Sender, ie.EventArgs), exc => Log = exc, () => { }));
          OrderChangedSubscribtion = Observable.FromEventPattern<EventHandler<OrderEventArgs>, OrderEventArgs>(h => h, h => TradesManager.OrderChanged += h, h => TradesManager.OrderChanged -= h)
            .Throttle(_throttleInterval)//, System.Concurrency.Scheduler.Dispatcher)
            .Subscribe(ie => FWMaster_OrderChanged(ie.Sender, ie.EventArgs), exc => Log = exc, () => { });
          //FWMaster.OrderChanged += FWMaster_OrderChanged;
          TradesManager.OrderAdded += FWMaster_OrderAdded;

          TradeColsedSubscribtion = Observable.FromEventPattern<EventHandler<TradeEventArgs>, TradeEventArgs>(h => h, h => TradesManager.TradeClosed += h, h => TradesManager.TradeClosed -= h)
            .Subscribe(ie => ClosedTrades.Add(ie.EventArgs.Trade), exc => Log = exc);
          GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
            ClosedTrades.Clear();
            TradesManager.GetClosedTrades("")
              .ForEach(ct => ClosedTrades.Add(ct));
          });

          if(!(TradesManager is VirtualTradesManager)) {
            Action<IList<MarketHours>> a = mks => {
              try {
                if((mks?.Any()).GetValueOrDefault()) {
                  Markets.Clear();
                  mks.ForEach(mh => Markets.Add(mh));
                }
                //Log = new Exception("Markets Loaded.");
              } catch(Exception exc) { Log = exc; }
            };
            var o = Observable.Interval(5.FromMinutes(), TaskPoolScheduler.Default).StartWith(TaskPoolScheduler.Default, 0)
              //.Do(l => Log = new Exception("Loading Markets"))
              .Select(t => {
                try {
                  return MarketHoursHound.Fetch();
                } catch(Exception exc) { Log = exc; }
                return Markets;
              }).ObserveOn(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher);
            o.Subscribe(a, exc => Log = new Exception("MarketHoursHound aborted", exc), () => Log = new Exception("MarketHoursHound aborted"));
          }
          RaisePropertyChanged(() => IsLoggedIn);
          Log = new Exception("Account " + TradingAccount + " logged in.");
          try {
            var account = TradesManager.GetAccount();
            if(account == null) {
              Thread.Sleep(1000);
              account = TradesManager.GetAccount();
              TradesManager.SetClosedTrades(GlobalStorage.UseForexMongo(c => c.Trades.Where(t => t.Kind == PositionBase.PositionKind.Closed).ToArray()));
            }
            if(account != null)
              GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.BeginInvoke(new Action(() => {
                try {
                  UpdateTradingAccount(account);
                  OnInvokeSyncronize(account);
                } catch(Exception exc) { Log = exc; }
              }));
          } catch(Exception exc) {
            Log = exc;
            MessageBox.Show(exc.ToString(), "GetAccount", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.None, MessageBoxOptions.ServiceNotification);
          }
          IsAccountManagerExpanded = false;
        };
        CoreFX.LoginError += exc => {
          IsInLogin = false;
          Log = exc;
          RaisePropertyChanged(() => IsLoggedIn);
        };
        CoreFX.LoggedOff += (s, e) => {
          Log = new Exception("Account " + TradingAccount + " logged out.");
          RaisePropertyChanged(() => IsLoggedIn);
          TradesManager.TradeAdded -= FWMaster_TradeAdded;
          TradesManager.TradeRemoved -= FWMaster_TradeRemoved;
          TradesManager.Error -= FWMaster_Error;
          //TradesManager.PriceChanged -= FWMaster_PriceChanged;
          //FWMaster.OrderChanged -= FWMaster_OrderChanged;
          TradesManager.OrderAdded -= FWMaster_OrderAdded;
          PriceChangeSubscribtion.YieldNotNull().ForEach(d => d.Dispose());
        };
        #endregion

        MessageBus.Current.Listen<AppExitMessage>().Subscribe(_ => SaveTradingSlaves());
        this.WhenAnyValue(tm => tm.IsInVirtualTrading)
          .Where(b => b)
          .Subscribe(isiv => Login(MasterAccount));

        //if (false) {
        //  Using_FetchServerTrades = () => Using(FetchServerTrades);
        //  if (!isInDesign) {
        //    GetTradesScheduler = new Schedulers.ThreadScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        //    Using_FetchServerTrades,
        //    (s, e) => { Log = e.Exception; });
        //  }
        //}
      }
    }

    public static Offer[] LoadOffers() => TradesManagerStatic.dbOffers = GlobalStorage.LoadOffers();

    private void UpdateTradingAccount(Account account) {
      if(account == null) {
        Log = new Exception($"{nameof(UpdateTradingAccount)}:{new { account }}");
      } else {
        OnNeedTradingStatistics();
        AccountModel.Update(account, 0, TradingStatistics, TradesManager.IsLoggedIn ? TradesManager.ServerTime : DateTime.Now);
      }
    }
    private ITargetBlock<Account> _UpdateTradingAccountTargetBlock;
    public ITargetBlock<Account> UpdateTradingAccountTargetBlock {
      get {
        return (_UpdateTradingAccountTargetBlock
        ?? (_UpdateTradingAccountTargetBlock = DataFlowProcessors.CreateYieldingTargetBlock<Account>(a => UpdateTradingAccount(a))));
      }
    }

    private ITargetBlock<Account> _InvokeSyncronizeTargetBlock;
    public ITargetBlock<Account> InvokeSyncronizeTargetBlock {
      get {
        return (_InvokeSyncronizeTargetBlock
        ?? (_InvokeSyncronizeTargetBlock = DataFlowProcessors.CreateYieldingTargetBlock<Account>(a => InvokeSyncronize(a))));
      }
    }

    private void Initialize() {
      GlobalStorage.LoadTradeSettings(this);

      var settings = new WpfPersist.UserSettingsStorage.Settings().Dictionary;
      DatabasePath = settings.Where(kv => kv.Key.Contains("DatabasePath")).LastOrDefault().Value;
      //if (!string.IsNullOrWhiteSpace(DatabasePath)) GlobalStorage.DatabasePath = DatabasePath;
      //else DatabasePath = GlobalStorage.DatabasePath;

      ServerTradesList = new ListCollectionView(ServerTrades = new NotifyCollectionChangedWrapper<Trade>(new ObservableCollection<Trade>()));
      ServerTradesList.SortDescriptions.Add(new SortDescription(GetLambda<Trade>(t => t.Pair), ListSortDirection.Ascending));
      ServerTradesList.SortDescriptions.Add(new SortDescription(GetLambda<Trade>(t => t.Time), ListSortDirection.Descending));

      OrdersList = new ListCollectionView(orders = new NotifyCollectionChangedWrapper<Order>(new ObservableCollection<Order>()));
      OrdersList.SortDescriptions.Add(new SortDescription(GetLambda<Order>(t => t.Pair), ListSortDirection.Ascending));
      OrdersList.SortDescriptions.Add(new SortDescription(GetLambda<Order>(t => t.IsBuy), ListSortDirection.Descending));

      ClosedTradesList = new ListCollectionView(ClosedTrades = new NotifyCollectionChangedWrapper<Trade>(new ObservableCollection<Trade>()));
      ClosedTradesList.SortDescriptions.Add(new SortDescription("Time", ListSortDirection.Descending));
      AbsentTradesList = new ListCollectionView(AbsentTrades = new NotifyCollectionChangedWrapper<Trade>(new ObservableCollection<Trade>()));

      MarketsList = new ListCollectionView(Markets = new NotifyCollectionChangedWrapper<MarketHours>(new ObservableCollection<MarketHours>()));
    }

    public override event EventHandler<MasterTradeEventArgs> MasterTradeRemoved;
    protected void OnMasterTradeRemoved(Trade trade) {
      if(MasterTradeRemoved != null)
        MasterTradeRemoved(this, new MasterTradeEventArgs(trade));
    }
    void FWMaster_TradeRemoved(object sender, TradeEventArgs e) {
      if(IsInVirtualTrading) {
        var account = TradesManager.GetAccount();
        account.Balance += e.Trade.NetPL;
      }
      OnMasterTradeRemoved(e.Trade);
    }

    void FWMaster_Error(object sender, HedgeHog.Shared.ErrorEventArgs e) {
      Log = e.Error;
    }

    ~TraderModel() {
      if(IsLoggedIn)
        CoreFX.Logout();
    }


    #endregion

    #region FXCM
    bool Login(string tradingAccount, string accountSubId, string tradingPassword, bool tradingDemo) {
      try {
        if(CoreFX.IsLoggedIn)
          CoreFX.Logout();
        IsInLogin = true;
        CoreFX.IsInVirtualTrading = IsInVirtualTrading;
        if(CoreFX.LogOn(tradingAccount, accountSubId, tradingPassword, tradingDemo)) {
          RaiseSlaveLoginRequestEvent();
          while(TradesManager.GetAccount() == null)
            Thread.Sleep(300);
          OnInvokeSyncronize(TradesManager.GetAccount());
          UpdateTradingAccount(TradesManager.GetAccount());
          return true;
        } else {
          return false;
        }
      } catch(Exception exc) {
        Log = exc;
        return false;
      } finally {
        IsInLogin = false;
        RaisePropertyChanged(() => IsLoggedIn);
      }
    }

    void FWMaster_PriceChanged(string pair) {
      FWMaster_PriceChanged(TradesManager, new PriceChangedEventArgs(new Price(pair), TradesManager.GetAccount(), TradesManager.GetTrades()));
    }
    void FWMaster_PriceChanged(object sender, PriceChangedEventArgs e) {
      try {
        if(IsInVirtualTrading) {
          e.Account.Equity = e.Account.Balance + e.Account.Trades.Sum(t => t.NetPL);
          UpdateTradingAccount(e.Account);
        }
        RunPriceChanged(e);
      } catch(Exception exc) {
        Log = exc;
      }
    }

    private void RunPriceChanged(PriceChangedEventArgs e) {
      string pair = e.Price.Pair;
      try {
        var a = e.Account;
        a.Trades = TradesManager.GetTrades();
        if(a.Trades.Any(t => t.Pair == pair) || a.Trades.IsEmpty()) {
          a.Orders = TradesManager.GetOrders("");
          OnInvokeSyncronize(a);
        }
      } catch(Exception exc) { Log = exc; }
    }

    void FWMaster_TradeAdded(object sender, TradeEventArgs e) {
      try {
        Trade trade = e.Trade;
        var tm = (ITradesManager)sender;
        if(tm.TryGetPrice(trade.Pair, out var price))
          RunPriceChanged(new PriceChangedEventArgs(price, tm.GetAccount(), tm.GetTrades()));
      } catch(Exception exc) {
        Log = exc;
      }
    }

    void FWMaster_OrderChanged(object sender, OrderEventArgs e) {
      FWMaster_PriceChanged(e.Order.Pair);
    }
    void FWMaster_OrderAdded(object sender, OrderEventArgs e) {
      FWMaster_PriceChanged(e.Order.Pair);
    }


    //void FetchServerTrades(TraderService.TraderServiceClient tsc) {
    //  try {
    //    OnInvokeSyncronize(tsc.GetAccount());
    //  } catch (Exception exc) { Log = exc; }
    //}

    class InvokeSyncronizeEventArgs :EventArgs {
      public Account Account { get; set; }
      public InvokeSyncronizeEventArgs(Account account) {
        this.Account = account;
      }
    }

    protected void OnInvokeSyncronize(Account account) {
      if(IsInVirtualTrading)
        InvokeSyncronize(account);
      else
        InvokeSyncronizeTargetBlock.Post(account);
    }

    void InvokeSyncronize(Account account) {
      if(account == null)
        return;
      if(account.Error != null)
        Log = account.Error;
      else {
        var trades = TradesManager.GetTrades();
        RaiseMasterListChangedEvent(trades);
        //UpdateTrades(account, trades.ToList(), ServerTrades);
        UpdateOrders(account, (account.Orders ?? new Order[0]).ToList(), orders);
      }
    }


    private bool IsAliceActive {
      get {
        return new[] { AliceModes.Wonderland, AliceModes.Mirror }.Contains(AliceMode);
      }
    }
    private void UpdateTrades(Account account, List<Trade> tradesList, NotifyCollectionChangedWrapper<Trade> tradesCollection) {
      try {
        var oldIds = tradesCollection.Select(t => t.Id).ToArray();
        var newIds = tradesList.Select(t => t.Id);
        var deleteIds = oldIds.Except(newIds).ToList();
        try {
          deleteIds.ForEach(d => tradesCollection.RemoveAll(tradesCollection.Where(t => t.Id == d)));
        } catch(InvalidOperationException) {
          deleteIds.ForEach(d => tradesCollection.RemoveAll(tradesCollection.Where(t => t.Id == d)));
        }
        var addIds = newIds.Except(oldIds).ToList();
        addIds.ForEach(a => tradesCollection.Add(tradesList.Single(t => t.Id == a)));
        foreach(var trade in tradesList) {
          // TODO ! Concurrency Problem (tradesCollection.SingleOrDefault)
          var trd = tradesCollection.SingleOrDefault(t => t.Id == trade.Id);
          if(trd != null) {
            var t = trade;
            trd.Update(t,
              o => { trd.InitUnKnown<TradeUnKNown>().BalanceOnLimit = trd.Limit == 0 ? 0 : account.Balance + trd.LimitAmount; },
              o => { trd.InitUnKnown<TradeUnKNown>().BalanceOnStop = account.Balance + trd.StopAmount; }
              );
          }
        }
        RaisePropertyChanged(() => ServerTradesList);
      } catch(Exception exc) {
        Log = exc;
      }
    }
    private void UpdateOrders(Account account, List<Order> ordersList, NotifyCollectionChangedWrapper<Order> ordersCollection) {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
        try {
          var oldIds = ordersCollection.Select(t => t.OrderID).ToArray();
          var newIds = ordersList.Select(t => t.OrderID);
          var deleteIds = oldIds.Except(newIds).ToList();
          deleteIds.ForEach(d => ordersCollection.Remove(ordersCollection.Single(t => t.OrderID == d)));
          var addIds = newIds.Except(oldIds).ToList();
          addIds.ForEach(a => ordersCollection.Add(ordersList.Single(t => t.OrderID == a)));
          foreach(var order in ordersList) {
            var odr = ordersCollection.SingleOrDefault(t => t.OrderID == order.OrderID);
            if(odr == null)
              break;
            var stopBalance = account.Balance + account.Trades.Where(t => t.Pair == odr.Pair && t.IsBuy != odr.IsBuy).Sum(t => t.StopAmount);
            odr.Update(order,
              o => { odr.InitUnKnown<OrderUnKnown>().BalanceOnLimit = odr.Limit == 0 ? 0 : stopBalance + odr.LimitAmount; },
              o => { odr.InitUnKnown<OrderUnKnown>().BalanceOnStop = stopBalance + odr.StopAmount; },
              o => { odr.InitUnKnown<OrderUnKnown>().NoLossLimit = Static.GetEntryOrderLimit(TradesManager, account.Trades, odr.Lot, false, AccountModel.CurrentLoss).Round(1); },
              o => { odr.InitUnKnown<OrderUnKnown>().PercentOnStop = odr.StopAmount / stopBalance; },
              o => { odr.InitUnKnown<OrderUnKnown>().PercentOnLimit = odr.LimitAmount / stopBalance; }
              );
          }
        } catch(Exception exc) {
          Log = exc;
        }
      });
    }
    #endregion

    #region WCF Service
    //public void Using(Action<TraderService.TraderServiceClient> action) {
    //  if (!isInRemoteMode || isInDesign) return;
    //  var service = new TraderService.TraderServiceClient("NetTcpBinding_ITraderService", ServerAddress);
    //  bool success = false;
    //  try {
    //    action(service);
    //    if (service.State != CommunicationState.Faulted) {
    //      service.Close();
    //      success = true;
    //    }
    //  } finally {
    //    if (!success) {
    //      service.Abort();
    //    }
    //  }
    //}
    #endregion

    #region Methods

    #region Change Stop/Limit
    void changeStop(string tradeId, double newStop) {
      TradesManager.FixOrderSetStop(tradeId, newStop, "");
    }
    void changeStops() {
      changeStopsOrLimits(stopDeltas, trade => trade.Stop, (id, v) => changeStop(id, v), changeStopScheduler);
    }
    // TODO: sync new level with others by using NessageBus
    void changeLimit(string tradeId, double newLimit) {
      TradesManager.FixOrderSetLimit(tradeId, newLimit, "");
    }
    void changeLimits() {
      changeStopsOrLimits(limitDeltas, trade => trade.Limit, (id, v) => changeLimit(id, v), changeLimitScheduler);
    }
    void changeStopsOrLimits(Dictionary<string, double> deltas, Func<Trade, double> getValue, Action<string, double> changeValue, Schedulers.ThreadScheduler changeScheduler) {
      foreach(var ld in deltas.Where(kv => kv.Value != 0).ToArray()) {
        var trade = TradesManager.GetTrades().SingleOrDefault(t => t.Id == ld.Key);
        if(trade == null)
          deltas.Remove(ld.Key);
        else {
          var newValue = TradesManager.InPoints(trade.Pair, ld.Value) + getValue(trade);
          deltas[ld.Key] = 0;
          changeValue(ld.Key, newValue);
          if(deltas[ld.Key] != 0)
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
      if(!deltas.ContainsKey(pair))
        deltas.Add(pair, limitDelta);
      else
        deltas[pair] = deltas[pair] + limitDelta;
    }
    Schedulers.ThreadScheduler _changeLimitScheduler;
    Schedulers.ThreadScheduler changeLimitScheduler {
      get {
        if(_changeLimitScheduler == null)
          _changeLimitScheduler = new Schedulers.ThreadScheduler(TimeSpan.FromSeconds(0.5), Schedulers.ThreadScheduler.infinity, changeLimits, (s, e) => Log = e.Exception);
        return _changeLimitScheduler;
      }
    }
    Schedulers.ThreadScheduler _changeStopScheduler;
    Schedulers.ThreadScheduler changeStopScheduler {
      get {
        if(_changeStopScheduler == null)
          _changeStopScheduler = new Schedulers.ThreadScheduler(TimeSpan.FromSeconds(0.5), Schedulers.ThreadScheduler.infinity, changeStops, (s, e) => Log = e.Exception);
        return _changeStopScheduler;
      }
    }
    #endregion

    double CommissionByLot(int lot) {
      return MasterAccount == null
        ? 0
        : MasterAccount.CommissionType == Store.TradingAccount.CommissionTypes.Rel
        //? lot / 10000.0 * MasterAccount.Commission
        ? lot * MasterAccount.Commission
        : MasterAccount.Commission;
    }
    static T TestDefault<T>(T value, string errorMessage) {
      if(value.IsDefault())
        throw new ArgumentException(errorMessage);
      return value;
    }
    override public double CommissionByTrade(Trade trade) {
      return MasterAccount == null
        ? 0
        : trade == null && trade.Lots == 0
        ? 0
        : CommissionByLot(trade.Lots);
    }
    string tradeIdLast = "";
    public override void AddCosedTrade(Trade trade) {
      try {
        if(TradingMaster == null)
          MessageBox.Show("Trading Master account is not found.");
        else {
          if(tradeIdLast == trade.Id)
            return;
          tradeIdLast = trade.Id;
          if(IsInVirtualTrading) {
            TradeStatistics tradeStats = trade.InitUnKnown<TradeUnKNown>().TradeStats ?? new TradeStatistics();
            //if (GlobalStorage.Context.TradeHistories.Count(t => t.Id == trade.Id) > 0) return;
            ////var ct = ClosedTrade.CreateClosedTrade(trade.Buy, trade.Close, trade.CloseInPips, trade.GrossPL, trade.Id + "", trade.IsBuy, trade.IsParsed, trade.Limit, trade.LimitAmount, trade.LimitInPips, trade.Lots, trade.Open, trade.OpenInPips, trade.OpenOrderID + "", trade.OpenOrderReqID + "", trade.Pair, trade.PipValue, trade.PL, trade.PointSize, trade.PointSizeFormat, trade.Remark + "", trade.Stop, trade.StopAmount, trade.StopInPips, trade.Time, trade.TimeClose, trade.UnKnown + "", TradingMaster.AccountId + "", CommissionByTrade(trade), trade.IsVirtual, DateTime.Now, tradeStats.TakeProfitInPipsMinimum, tradeStats.MinutesBack);
            var ct = new t_Trade { Id = trade.Id, Buy = trade.Buy, PL = trade.PL, GrossPL = trade.GrossPL, Lot = trade.Lots, Pair = trade.Pair, TimeOpen = trade.Time, TimeClose = trade.TimeClose, AccountId = TradingMaster.AccountId + "", Commission = trade.Commission * 2, IsVirtual = trade.IsVirtual, CorridorMinutesBack = tradeStats.CorridorStDev, CorridorHeightInPips = tradeStats.CorridorStDevCma, SessionId = tradeStats.SessionId, PriceOpen = trade.Open, PriceClose = trade.Close };
            ct.t_TradeValue = tradeStats.Values.Select(kv => new t_TradeValue { Name = kv.Key, Value = kv.Value + "" }).ToArray();
            //var ct = TradeHistory.CreateTradeHistory(trade.Id, trade.Buy, (float)trade.PL, (float)trade.GrossPL, trade.Lots, trade.Pair, trade.Time, trade.TimeClose, TradingMaster.AccountId + "", (float)CommissionByTrade(trade), trade.IsVirtual, tradeStats.TakeProfitInPipsMinimum, tradeStats.MinutesBack, tradeStats.SessionId);
            ct.TimeStamp = DateTime.Now;
            ct.SessionInfo = tradeStats.SessionInfo;
            GlobalStorage.UseForexContext(c => {
              c.t_Trade.Add(ct);
              c.SaveChanges();
            }, (c, e) => {
              Log = new Exception(ct.ToJson());
              Log = e;
              c.t_Trade.Remove(ct);
            });
          }
        }
      } catch(Exception exc) { Log = exc; }
    }

    #endregion
  }
  public enum AliceModes {
    Neverland = 0, Wonderland = 1, Mirror = 2
  }
}
