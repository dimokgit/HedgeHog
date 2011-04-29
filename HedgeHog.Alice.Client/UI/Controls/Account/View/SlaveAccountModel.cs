using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using FXW = Order2GoAddIn.FXCoreWrapper;
using Gala = GalaSoft.MvvmLight.Command;
using HedgeHog.Shared;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading;
using HedgeHog.Bars;
using System.Diagnostics;
using Order2GoAddIn;
using HedgeHog.Alice.Store;

namespace HedgeHog.Alice.Client.UI.Controls {
  public class SlaveAccountModel : HedgeHog.Models.ModelBase,IAccountHolder {
    #region Fields
    private double secondsToWaitForTrade = 30;
    private string logFileName = "Log.txt";
    protected bool isInDesign { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    public int TargetInPips { get; set; }
    Schedulers.ThreadScheduler syncronizeScheduler;
    #endregion

    TradingAccountModel _accountModel = new TradingAccountModel();
    public TradingAccountModel AccountModel { get { return _accountModel; } }

    TraderModel _masterModel;
    public TraderModel MasterModel {
      get { return _masterModel; }
      set {
        if (_masterModel == value) return;
        _masterModel = value;
        value.MasterListChangedEvent += value_MasterListChangedEvent;
        value.SlaveLoginRequestEvent += value_SlaveLoginRequestEvent;
        value.MasterTradeAdded += value_MasterTradeAdded;
        value.MasterTradeRemoved += value_MasterTradeRemoved;
      }
    }

    void value_MasterTradeRemoved(object sender, MasterTradeEventArgs e) {
      var slaveTrade = fwLocal.GetTrades("").SingleOrDefault(t => t.MasterTradeId() == e.MasterTrade.Id);
      if (slaveTrade != null)
        fwLocal.CloseTradeAsync(slaveTrade);
      masterTradesPending.Remove(e.MasterTrade.Id);
    }

    void value_MasterTradeAdded(object sender, MasterTradeEventArgs e) {
        SyncTrade(e.MasterTrade);
    }

    void value_SlaveLoginRequestEvent(object sender, EventArgs e) {
      new Schedulers.ThreadScheduler((s, ev) => Log = ev.Exception).Command = () => Login();
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
    AliceModes AliceMode { 
      get { return MasterModel.AliceMode; }
      set { MasterModel.AliceMode = value; }
    }
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

    List<string> masterTradesPending = new List<string>();
    List<string> masterTradesSynced = new List<string>();
    Trade[] _masterList = new Trade[] { };

    public Trade[] MasterList {
      get { return _masterList; }
      set { 
        _masterList = value;
        InvokeSyncronize();
      }
    }
    #endregion

    #region Syncronize
    private void InvokeSyncronize() {
      if (IsLoggedIn && !syncronizeScheduler.IsRunning) {
        syncronizeScheduler.Command = () => {
          AccountModel.Update(fwLocal.GetAccount(), ServerToLocalRatio, fwLocal.ServerTime);
          Syncronize(MasterModel.AccountModel, AccountModel);
          RaisePropertyChanged(() => IsLogExpanded);
        };
      }
    }
    private void Syncronize(Account masterAccount, Account slaveAccount) {
      Func<Trade, bool?> localBuyOrSell = t => !IsAliceActive ? (bool?)null : AliceMode == AliceModes.Wonderland ? t.Buy : !t.Buy;
      Func<Trade[]> localTrades = () => fwLocal.GetTrades("");
      Trade tradeOut;
      var serverTrades = masterAccount.Trades;
      if (MasterModel != null && (MasterModel.isInRemoteMode || MasterModel.IsLoggedIn)) {

        #region Close/Cancel Trades
        if (TargetInPips != 0 && localTrades().Count() > 0 && AccountModel.Equity / AccountModel.Balance >= TargetInPips) CloseAllLocalTrades();
        else {
          if (IsAliceActive) {
            var tradesToClose = (from tl in localTrades()
                                 join ts in serverTrades
                                 on new { Id = tl.MasterTradeId(), Buy = localBuyOrSell(tl) }
                                 equals new { Id = ts.Id, Buy = (bool?)ts.Buy } into lclTrds
                                 from st in lclTrds.DefaultIfEmpty()
                                 where st == null
                                 select tl).ToList();

            foreach (var tradeToClose in tradesToClose) {
              try {
                if (tradeToClose.IsPending()) {
                  masterTradesPending.Remove(tradeToClose.Id);
                  Log = new Exception("Pending trade " + tradeToClose.Id + " was canceled.");
                } else {
                  fwLocal.CloseTradeAsync(tradeToClose);
                  Log = new Exception("Closing trade " + tradeToClose.Id);
                }
              } catch (Exception exc) { Log = new Exception("TradeId:" + tradeToClose.Id, exc); }
            }
            if (tradesToClose.Count > 0) return;
          }
        }
        #endregion

        #region Absent trades
        var penditTradesToRemove = (from tl in localTrades()
                                    join tp in masterTradesPending on tl.MasterTradeId() equals tp
                                    select tp).ToList();
        penditTradesToRemove.ForEach(pt => masterTradesPending.Remove(pt));

        DateTime localServerTime = fwLocal.ServerTime;
        var absentTrades = (from ts in serverTrades
                            join tl in localTrades()
                            on new { Id = ts.Id, Buy = (bool?)ts.Buy }
                            equals new { Id = tl.MasterTradeId(), Buy = localBuyOrSell(tl) }
                            into svrTrds
                            from st in svrTrds.DefaultIfEmpty()
                            where st == null
                            select ts.InitUnKnown(localServerTime)).ToList();

        //masterTradesPending.ForEach(pt => localTrades.Add(pt));
        ShowTrades(
          localTrades().Concat(masterTradesPending.Select(id => new Trade() { Id = id, Remark = new TradeRemark(id) })).ToList(),
          LocalTrades);

        #region Sync (Open)
        if (AliceMode != AliceModes.Mirror) {
          if (absentTrades.Count == 0) {
            var syncStop = (from ts in serverTrades
                            join tl in localTrades().Where(t => t.Open > 0) on new { Id = ts.Id, Stop = GetMasterStop(ts, AliceMode) }
                            equals new { Id = tl.MasterTradeId(), Stop = tl.Stop }
                            into svrTrds
                            from st in svrTrds.DefaultIfEmpty()
                            where st == null && IsStopOk(fwLocal.GetPrice(ts.Pair), GetLocalBuyFromMaster(ts, AliceMode), GetMasterStop(ts, AliceMode))
                            select ts).ToList();
            syncStop.ForEach(ss => {
              ss.GetUnKnown().AutoSync = false;
              ss.GetUnKnown().SyncStop = true;
              absentTrades.Add(ss);
            });
            foreach (var serverTrade in syncStop) {
              var locatTrade = localTrades().FirstOrDefault(lt => lt.MasterTradeId() == serverTrade.Id);
              if (locatTrade != null) {
                var stop = GetMasterStop(serverTrade, AliceMode);
                new Schedulers.ThreadScheduler().Command = () => fwLocal.FixOrderSetStop(locatTrade.Id, stop, serverTrade.Id);
              }
            }
          }
          if (absentTrades.Count == 0) {

            var syncLimit = (from ts in serverTrades
                             join tl in localTrades().Where(t => t.Open > 0) on new { Id = ts.Id, Limit = GetMasterLimit(ts, AliceMode) }
                             equals new { Id = tl.MasterTradeId(), Limit = tl.Limit }
                             into svrTrds
                             from st in svrTrds.DefaultIfEmpty()
                             where st == null && IsLimitOk(fwLocal.GetPrice(ts.Pair), GetLocalBuyFromMaster(ts, AliceMode), GetMasterLimit(ts, AliceMode))
                             select ts).ToList();
            syncLimit.ForEach(ss => {
              ss.GetUnKnown().AutoSync = false;
              ss.GetUnKnown().SyncLimit = true;
              absentTrades.Add(ss);
            });
            foreach (var serverTrade in syncLimit) {
              var locatTrade = localTrades().FirstOrDefault(lt => lt.MasterTradeId() == serverTrade.Id);
              if (locatTrade != null) {
                var limit = GetMasterLimit(serverTrade, AliceMode);
                new Schedulers.ThreadScheduler().Command = () => fwLocal.FixOrderSetLimit(locatTrade.Id, limit, serverTrade.Id);
              }
            }
          }
        }
        GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
          AbsentTrades.Clear();
          absentTrades.ForEach(a => AbsentTrades.Add(a));
        }));
        if (masterTradesPending.Count == 0 || resubmitMasterPendingTrade != null) {
          if (syncAll) {
            SyncTrade(AbsentTrades);
            syncAll = false;
          } else if (IsAliceActive) {
            if (absentTrades.Select(at => at.Id).Distinct().Count() != absentTrades.Count())
              Debugger.Break();
            foreach (var tradeToCopy in absentTrades.Where(t => t.GetUnKnown().AutoSync).ToArray()) {
              SyncTrade(tradeToCopy);
              //return;
            }
          }
        }
        #endregion
        #endregion

        #region Orders

        #endregion

        ServerTime = DateTime.Now;
      }
    }

    static bool IsStopOk(Price price, bool buy, double stop) {
      return stop == 0 ? true : buy ? price.Bid > stop : price.Ask < stop;
    }

    static bool IsLimitOk(Price price, bool buy, double limit) {
      return limit == 0 ? true : buy ? price.Ask < limit : price.Bid > limit;
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
            //Log = new Exception(string.Format("Trade {0} is being clonned", tradeToCopy.Id));
            var buy = AliceMode == AliceModes.Wonderland ? tradeToCopy.Buy : !tradeToCopy.Buy;
            var mq = fwLocal.MinimumQuantity;
            var lots = ((tradeToCopy.Lots * ServerToLocalRatio) / mq).ToInt() * mq;
            var stop = AliceMode == AliceModes.Mirror ? 0 : GetMasterStop(tradeToCopy, AliceMode);
            var limit = AliceMode == AliceModes.Mirror ? 0 : GetMasterLimit(tradeToCopy, AliceMode);
            if (lots == 0) Log = new Exception("Balance is to small to trade with master.");
            else {
              if (!OpenTradeSchedulers.ContainsKey(tradeToCopy.Id))
                OpenTradeSchedulers.Run(tradeToCopy.Id, () => OpenTrade(fwLocal, tradeToCopy.Pair, buy, lots, limit, stop, tradeToCopy));
            }
        }
      } catch (Exception exc) { Log = exc; }
    }
    static bool GetLocalBuyFromMaster(Trade masterTrade, AliceModes aliceMode) {
      return aliceMode == AliceModes.Wonderland ? masterTrade.Buy : !masterTrade.Buy;
    }
    static double GetMasterStop(Trade masterTrade, AliceModes aliceMode) {
      return aliceMode == AliceModes.Wonderland ? masterTrade.Stop : masterTrade.Limit;
    }
    static double GetMasterLimit(Trade masterTrade, AliceModes aliceMode) {
      return aliceMode == AliceModes.Wonderland ? masterTrade.Limit : masterTrade.Stop;
    }

    Schedulers.ThreadSchedulersDispenser OpenTradeSchedulers = new Schedulers.ThreadSchedulersDispenser();
    void OpenTrade(FXW fw, string pair, bool buy, int lots, double limit, double stop, Trade masterTrade) {
      string serverTradeID = masterTrade.Id;
      Func<string, bool> tradeExists = id => fw.GetTrade(serverTradeID) != null;
      if (!tradeExists(serverTradeID)) {
        if (masterTradesPending.Contains(serverTradeID) || masterTradesSynced.Contains(serverTradeID)) return;
        masterTradesPending.Add(serverTradeID);
        PendingOrder po = null;
        Action<object, RequestEventArgs> reqiesFailedAction = (s, e) => {
          if (po != null && e.RequestId == po.RequestId) {
            masterTradesPending.Remove(serverTradeID);
            po = null;
            Log = new Exception(e.Error);
          }
        };
        Action<Order> orderRemovedAvtion = order => {
          var o = order.FixStatus;
        };
        Action<object, ErrorEventArgs> errorAction = (s, e) => {
          if (serverTradeID == e.Remark) {
            masterTradesPending.Remove(serverTradeID);
            po = null;
            Log = e.Error;
          }
        };
        var rfh = new EventHandler<RequestEventArgs>(reqiesFailedAction);
        var orh = new OrderRemovedEventHandler(orderRemovedAvtion);
        var erh = new EventHandler<ErrorEventArgs>(errorAction);
        try {
          fw.RequestFailed += rfh;
          fw.OrderRemoved += orh;
          fw.Error += erh;
          po = fw.FixOrderOpen(pair, buy, lots, limit, stop, serverTradeID);
          //if (po != null)          pendingTrade.GetUnKnown().ErrorMessage = "Waiting for " + po.RequestId;
          var done = SpinWait.SpinUntil(
            () => {
              Thread.Sleep(100);
              return masterTradesPending.Contains(serverTradeID) && po != null && tradeExists(serverTradeID);
            },
            TimeSpan.FromSeconds(secondsToWaitForTrade));
          if (tradeExists(serverTradeID))
            masterTradesSynced.Add(serverTradeID);
        } catch (Exception exc) { Log = exc; } finally {
          masterTradesPending.Remove(serverTradeID);
          fw.RequestFailed -= rfh;
          fw.OrderRemoved -= orh;
          fw.Error -= erh;
        }
      }
      OpenTradeSchedulers.Remove(serverTradeID);
    }

    void fwLocal_Error(object sender, ErrorEventArgs e) {
      Log = e.Error;
    }



    private void ShowTrades(List<Trade> tradesList, ObservableCollection<Trade> tradesCollection) {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
        tradesCollection.Clear();
        tradesList.ForEach(a => tradesCollection.Add(a));
      }));
    }
    #endregion

    #region Ctor
    TradeRequestManager tradeRequestManager;
    SlaveAccountModel() {
      if (!isInDesign)
        syncronizeScheduler = new Schedulers.ThreadScheduler((s, e) => Log = e.Exception);

      LocalTradesList = new ListCollectionView(LocalTrades = new ObservableCollection<Trade>());
      AbsentTradesList = new ListCollectionView(AbsentTrades = new ObservableCollection<Trade>());

      fwLocal = new FXW(this.CoreFX);
      tradeRequestManager = new TradeRequestManager(fwLocal);
      CoreFX.LoggedIn += (s, e) => {
        //fwLocal.TradeAdded += fw_TradeAded;
        fwLocal.PriceChanged += fwLocal_PriceChanged;
        fwLocal.OrderRemoved += DoOrderRemoved;
        fwLocal.Error+=fwLocal_Error;
        RaisePropertyChanged(() => IsLoggedIn);
        Log = new Exception("Account " + TradingAccount + " logged in.");
      };
      CoreFX.LoginError += exc => {
        Log = exc;
        RaisePropertyChanged(() => IsLoggedIn);
      };
      CoreFX.LoggedOff += (s, e) => {
        Log = new Exception("Account " + TradingAccount + " logged out.");
        RaisePropertyChanged(() => IsLoggedIn);
        //fwLocal.TradeAdded -= fw_TradeAded;
        fwLocal.PriceChanged -= fwLocal_PriceChanged;
        fwLocal.OrderRemoved -= DoOrderRemoved;
        fwLocal.Error -= fwLocal_Error;
      };

    }

    void DoOrderRemoved(Order order) {
      Log = new Exception("Order removed");
    }

    public TradingAccount TradingAccountModel { get; set; }
    public SlaveAccountModel(TraderModel masterModel, TradingAccount tradingAccountModel)
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
    void fwLocal_PriceChanged(object sender, PriceChangedEventArgs e) {
      InvokeSyncronize();
    }
    Trade resubmitMasterPendingTrade = null;
    void fw_TradeAded(Trade trade) {
      //Log = new Exception("Trades count changed. TradeId:" + trade.Id);
      //AccountModel.Trades = AccountModel.Trades.Concat(new[] { trade }).ToArray();
      //Debug.WriteLine("Trade with RequestId " + trade.OpenOrderReqID + " added.");
      //var mp = masterTradesPending.SingleOrDefault(m => m.Id == trade.MasterTradeId());
      //if (mp != null) {
      //  masterTradesPending.Remove(mp);
      //  if (fwLocal.GetTrade(trade.Id) == null) 
      //    SyncTrade(mp.GetUnKnown().MasterTrade);
      //}
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
        if (masterTradesPending.Contains(tradeID)) masterTradesPending.Remove(tradeID);
        else fwLocal.CloseTradeAsync(fwLocal.GetTrade(tradeID));
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
        AliceMode = AliceModes.Neverland;
        var trades = fwLocal.GetTrades("");
        foreach(var trade in trades)
        fwLocal.CloseTradeAsync(trade);
        Log = new Exception("Trades closed:" + string.Join(",", trades.Select(t => t.Id)));
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
    public string LogText {
      get {
        lock (_logQueue) {
          return string.Join(Environment.NewLine, _logQueue.Reverse());
        }
      }
    }
    Queue<string> _logQueue = new Queue<string>();
    Exception _log;
    Exception Log {
      get { return _log; }
      set {
        if (isInDesign) return;
        lastLogTime = DateTime.Now;
        _log = value;
        var exc = value is Exception ? value : null;
        lock (_logQueue) {
          if (_logQueue.Count > 5) _logQueue.Dequeue();
          var messages = new List<string>(new[] { DateTime.Now.ToString("[dd HH:mm:ss] ") + GetExceptionShort(value) });
          while (value.InnerException != null) {
            messages.Add(GetExceptionShort(value.InnerException));
            value = value.InnerException;
          }
          _logQueue.Enqueue(string.Join(Environment.NewLine + "-", messages));
        }
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
