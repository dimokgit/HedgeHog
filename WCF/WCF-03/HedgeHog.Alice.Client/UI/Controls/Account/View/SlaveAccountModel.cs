using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using FXW = Order2GoAddIn.FXCoreWrapper;
using Gala = GalaSoft.MvvmLight.Command;
using HedgeHog.Shared;
using HedgeHog.Alice.Client.TradeExtenssions;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading;

namespace HedgeHog.Alice.Client.UI.Controls {
  public class SlaveAccountModel : HedgeHog.Models.ModelBase,IAccountHolder {
    #region Fields
    private string logFileName = "Log.txt";
    protected bool isInDesign { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    public int TargetInPips { get; set; }
    ThreadScheduler syncronizeScheduler;
    #endregion

    TradingAccountModel _accountModel = new TradingAccountModel();
    public TradingAccountModel AccountModel { get { return _accountModel; } }

    TraderModel _masterModel;
    public TraderModel MasterModel {
      get { return _masterModel; }
      set {
        if (_masterModel == value) return;
        _masterModel = value;
        value.MasterListChangedEvent += new TraderModel.MasterListChangedeventHandler(value_MasterListChangedEvent);
        value.SlaveLoginRequestEvent += new EventHandler(value_SlaveLoginRequestEvent);
      }
    }

    void value_SlaveLoginRequestEvent(object sender, EventArgs e) {
      new ThreadScheduler((s, ev) => Log = ev.Exception).Command = () => Login();
    }

    void value_MasterListChangedEvent(object sender, MasterListChangedEventArgs e) {
      MasterList = e.MasterTrades;
    }



    public string ServerToLocalRatioValue {
      get { return TradingAccountModel.TradeRatio; }
      set {
        if (TradingAccountModel.TradeRatio == value) return;
        TradingAccountModel.TradeRatio = value;
        RaisePropertyChangedCore();
      }
    }

    double ServerToLocalRatio {
      get {
        return AccountModel.Balance / MasterModel.AccountModel.Balance;
        var pattern = @"^\d+:\d+$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(TradingAccountModel.TradeRatio, pattern))
          throw new InvalidCastException("Trade ratio mast look like N:M");
        var parts = TradingAccountModel.TradeRatio.Split(':');
        return double.Parse(parts[0]) / double.Parse(parts[1]);
      }
    }


    #region ServerTime
    DateTime _serverTime;
    public DateTime ServerTime {
      get { return _serverTime; }
      set { _serverTime = value; RaisePropertyChangedCore(); }
    }
    #endregion

    #region Alice
    AliceModes AliceMode { get { return MasterModel.AliceMode; } }
    private bool IsAliceActive {
      get {
        return new[] { AliceModes.Wonderland, AliceModes.Mirror }.Contains(AliceMode);
      }
    }
    #endregion

    #region Lists
    public ObservableCollection<Trade> LocalTrades { get; set; }
    public ListCollectionView LocalTradesList { get; set; }

    public ObservableCollection<Trade> AbsentTrades { get; set; }
    public ListCollectionView AbsentTradesList { get; set; }

    List<Trade> masterTradesPending = new List<Trade>();
    Trade[] _masterList = new Trade[] { };

    public Trade[] MasterList {
      get { return _masterList; }
      set { 
        _masterList = value;
        InvokeSyncronize();
      }
    }
    #endregion

    DateTime lastGetAccountTime = DateTime.MinValue;
    private void InvokeSyncronize() {
      if (IsLoggedIn && !syncronizeScheduler.IsRunning) {
        syncronizeScheduler.Command = () => {
          Trade[] trades = null;
          if ((DateTime.Now - lastGetAccountTime) > TimeSpan.FromSeconds(1)) {
            var a = fwLocal.GetAccount();
            AccountModel.Update(a, ServerToLocalRatio,fwLocal.ServerTime);
            trades = a.Trades;
            lastGetAccountTime = DateTime.Now;
          }
          Syncronize(MasterList.ToList(), (trades ?? fwLocal.GetTrades()).ToList());
          RaisePropertyChanged(() => IsLogExpanded);
        };
      }
    }
    private void Syncronize(List<Trade> serverTrades, List<Trade> localTrades) {
      if (MasterModel != null && (MasterModel.isInRemoteMode || MasterModel.IsLoggedIn)) {

        #region Absent trades
        var penditTradesToRemove = (from tl in localTrades
                                    join tp in masterTradesPending on tl.MasterTradeId() equals tp.MasterTradeId()
                                    select tp).ToList();
        penditTradesToRemove.ForEach(pt => masterTradesPending.Remove(pt));

        Func<Trade, bool?> localBuyOrSell = t => !IsAliceActive ? (bool?)null : AliceMode == AliceModes.Wonderland ? t.Buy : !t.Buy;
        DateTime localServerTime = fwLocal.ServerTime;
        var absentTrades = (from ts in serverTrades
                            join tl in localTrades
                            on new { Id = ts.Id, Buy = (bool?)ts.Buy } equals new { Id = tl.MasterTradeId(), Buy = localBuyOrSell(tl) }
                            into svrTrds
                            from st in svrTrds.DefaultIfEmpty()
                            where st == null
                            select ts.InitUnKnown(localServerTime)).ToList();

        masterTradesPending.ForEach(pt => localTrades.Add(pt));
        ShowTrades(localTrades, LocalTrades);

        GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
          AbsentTrades.Clear();
          absentTrades.ForEach(a => AbsentTrades.Add(a));
        }));

        #region Sync (Open)
        if (syncAll) {
          SyncTrade(AbsentTrades);
          syncAll = false;
        } else if (IsAliceActive) {
          foreach (var tradeToCopy in absentTrades.Where(t => t.GetUnKnown().AutoSync).ToArray()) {
            SyncTrade(tradeToCopy);
          }
        }
        #endregion
        #endregion

        #region Close/Cancel Trades
        if (TargetInPips != 0 && localTrades.Count > 0 && AccountModel.PL >= TargetInPips) CloseAllLocalTrades();
        else {
          if (IsAliceActive) {
            var tradesToClose = (from tl in localTrades
                                 join ts in serverTrades
                                 on new { Id = tl.MasterTradeId(), Buy = localBuyOrSell(tl) } 
                                 equals new { Id = ts.Id, Buy = (bool?)ts.Buy } into lclTrds
                                 from st in lclTrds.DefaultIfEmpty()
                                 where st == null
                                 select tl).ToList();

            foreach (var tradeToClose in tradesToClose) {
              try {
                if (tradeToClose.IsPending()) {
                  masterTradesPending.Remove(tradeToClose);
                  Log = new Exception("Pending trade " + tradeToClose.Id + " was canceled.");
                } else {
                  new ThreadScheduler((s, e) => Log = e.Exception).Command = () => {
                    var orderId = fwLocal.FixOrderClose(tradeToClose.Id);
                    Log = new Exception("Closing trade " + tradeToClose.Id + ". OrderId:" + orderId);
                  };
                }
              } catch (Exception exc) { Log = new Exception("TradeId:" + tradeToClose.Id, exc); }
            }
          }
        }
        #endregion
        ServerTime = DateTime.Now;
      }
    }

    private void SyncTrade(IEnumerable<Trade> tradeToCopy) {
      foreach (var trade in tradeToCopy)
        SyncTrade(trade);
    }
    private void SyncTrade(Trade tradeToCopy) {
      try {
        if (AliceMode == AliceModes.Neverland)
          Log = new Exception("Can't syncronize while Alice is in " + AliceMode);
        else {
          var serverTradeId = tradeToCopy.Id;
          Trade pendingTrade = new Trade() { Id = serverTradeId, Remark = new TradeRemark(serverTradeId) };
          if( !masterTradesPending.Any(t=>t.MasterTradeId() == pendingTrade.MasterTradeId()))
            masterTradesPending.Add(pendingTrade);
          Log = new Exception(string.Format("Trade {0} is being clonned", tradeToCopy.Id));
          var buy = AliceMode == AliceModes.Wonderland ? tradeToCopy.Buy : !tradeToCopy.Buy;
          var mq = fwLocal.MinimumQuantity;
          var lots = ((tradeToCopy.Lots * ServerToLocalRatio) / mq).ToInt() * mq;
          if (lots == 0) Log = new Exception("Balance is to small to trade with master.");
          else OpenTrade(tradeToCopy.Pair, buy, lots, serverTradeId, pendingTrade);
        }
      } catch (Exception exc) { Log = exc; }
    }
    private void OpenTrade(string pair, bool buy, int lots, string serverTradeID,Trade pendingTrade) {
      tradeRequestManager.AddOpenTradeRequest(pair, buy, lots, serverTradeID, pendingTrade);
    }

    private void ShowTrades(List<Trade> tradesList, ObservableCollection<Trade> tradesCollection) {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
        tradesCollection.Clear();
        tradesList.ForEach(a => tradesCollection.Add(a));
      }));
    }

    #region Ctor
    TradeRequestManager tradeRequestManager;
    FXW.TradesCountChangedEventHandler fw_TradesCountChangedDelegate;
    FXW.PriceChangedEventHandler fwLocal_PriceChangedDelegate;
    SlaveAccountModel() {
      if (!isInDesign)
        syncronizeScheduler = new ThreadScheduler((s, e) => Log = e.Exception);

      LocalTradesList = new ListCollectionView(LocalTrades = new ObservableCollection<Trade>());
      AbsentTradesList = new ListCollectionView(AbsentTrades = new ObservableCollection<Trade>());

      fw_TradesCountChangedDelegate = new FXW.TradesCountChangedEventHandler(fw_TradesCountChanged);
      fwLocal_PriceChangedDelegate = new FXW.PriceChangedEventHandler(fwLocal_PriceChanged);
      fwLocal = new FXW(this.CoreFX);
      tradeRequestManager = new TradeRequestManager(fwLocal);
      CoreFX.LoggedInEvent += (s, e) => {
        fwLocal.TradesCountChanged += fw_TradesCountChangedDelegate;
        fwLocal.PriceChanged += fwLocal_PriceChangedDelegate;
        RaisePropertyChanged(() => IsLoggedIn);
        Log = new Exception("Account " + TradingAccount + " logged in.");
      };
      CoreFX.LoginError += exc => {
        Log = exc;
        RaisePropertyChanged(() => IsLoggedIn);
      };
      CoreFX.LoggedOffEvent += (s, e) => {
        Log = new Exception("Account " + TradingAccount + " logged out.");
        RaisePropertyChanged(() => IsLoggedIn);
        fwLocal.TradesCountChanged -= fw_TradesCountChangedDelegate;
        fwLocal.PriceChanged -= fwLocal_PriceChangedDelegate;
      };

    }

    public Models.TradingAccount TradingAccountModel { get; set; }
    public SlaveAccountModel(TraderModel masterModel, Models.TradingAccount tradingAccountModel)
      : this() {
      //Dimok: Use WeakReference
        this.MasterModel = masterModel;
        this.TradingAccountModel = tradingAccountModel;
    }

    #endregion

    #region FXCM

    private Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    FXW fwLocal;
    public bool IsLoggedIn { get { return CoreFX.IsLoggedIn; } }

    #region Event Handlers
    void fwLocal_PriceChanged(Order2GoAddIn.Price Price) {
      InvokeSyncronize();
    }
    void fw_TradesCountChanged(Trade trade) {
      Log = new Exception("Trades count changed. TradeId:" + trade.Id);
    }
    #endregion


    #endregion

    #region Commanding
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
      Login(li.Account, li.Password, li.IsDemo);
    }
    public bool Login() {
      return Login(TradingAccountModel.AccountId, TradingAccountModel.Password, TradingAccountModel.IsDemo);
    }
    public bool Login(string tradingAccount, string tradingPassword, bool tradingDemo) {
      try {
        if (CoreFX.IsLoggedIn) CoreFX.Logout();
        return CoreFX.LogOn(tradingAccount, tradingPassword, tradingDemo);
      } catch (Exception exc) {
        Log = exc;
        return false;
      } finally {
        RaisePropertyChanged(() => IsLoggedIn);
      }
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
        var trade = MasterList.FirstOrDefault(t => t.Id == tradeId);
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
        }
      } catch (Exception exc) { Log = exc; }
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
        var pendingTrade = masterTradesPending.FirstOrDefault(t => t.Id == tradeID);
        if (pendingTrade == null) fwLocal.FixOrderClose(tradeID);
        else masterTradesPending.Remove(pendingTrade);
      } catch (Exception exc) { Log = exc; }
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
        var ordersIds = fwLocal.FixOrdersCloseAll();
        Log = new Exception("Trades closed:" + string.Join(",", ordersIds));
      } catch (Exception exc) { Log = exc; }
    }
    #endregion


    #endregion

    #region Dependency Properties
    string _TradingAccount;
    public string TradingAccount {
      get { return TradingAccountModel.AccountId; }
      set {
        if(TradingAccountModel.AccountId == value)return;
        TradingAccountModel.AccountId = value; 
        RaisePropertyChangedCore(); }
    }

    string _TradingPassword;
    public string TradingPassword {
      get { return TradingAccountModel.Password; }
      set {
        if (TradingAccountModel.Password == value) return;
        TradingAccountModel.Password = value; 
        RaisePropertyChangedCore(); }
    }

    bool _TradingDemo;
    public bool TradingDemo {
      get { return TradingAccountModel.IsDemo; }
      set {
        if (TradingAccountModel.IsDemo == value) return;
        TradingAccountModel.IsDemo = value; 
        RaisePropertyChangedCore(); }
    }
    #endregion

    #region Log
    DateTime lastLogTime = DateTime.MinValue;
    public string LogText { get { return string.Join(Environment.NewLine, _logQueue.Reverse()); } }
    Queue<string> _logQueue = new Queue<string>();
    Exception _log;
    Exception Log {
      get { return _log; }
      set {
        if (isInDesign) return;
        lastLogTime = DateTime.Now;
        _log = value;
        var exc = value is Exception ? value : null;
        if (_logQueue.Count > 5) _logQueue.Dequeue();
        var messages = new List<string>(new[] { DateTime.Now.ToString("[dd HH:mm:ss] ") + GetExceptionShort(value) });
        while (value.InnerException != null) {
          messages.Add(GetExceptionShort(value.InnerException));
          value = value.InnerException;
        }
        _logQueue.Enqueue(string.Join(Environment.NewLine + "-", messages));

        FileLogger.LogToFile(exc);
        RaisePropertyChanged(() => LogText, () => IsLogExpanded);
      }
    }

    string GetExceptionShort(Exception exc) {
      return (exc.TargetSite == null ? "" : exc.TargetSite.DeclaringType.Name + "." +
      exc.TargetSite.Name + ": ") + exc.Message;
    }

    public bool IsLogExpanded {
      get { return (DateTime.Now-lastLogTime) < TimeSpan.FromSeconds(10); }
    }
    #endregion

  }
}
