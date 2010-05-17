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

namespace HedgeHog.Alice.Client.UI.Controls {
  public class SlaveAccountModel : HedgeHog.Models.ModelBase {
    #region Fields
    private string logFileName = "Log.txt";
    protected bool isInDesign { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    public int TargetInPips { get; set; }
    Scheduler syncronizeScheduler;
    #endregion

    TraderModel _masterModel;

    public TraderModel MasterModel {
      get { return _masterModel; }
      set {
        _masterModel = value;
        value.MasterListChangedEvent += new TraderModel.MasterListChangedeventHandler(value_MasterListChangedEvent);
      }
    }

    void value_MasterListChangedEvent(object sender, MasterListChangedEventArgs e) {
      MasterList = e.MasterTrades;
    }



    string _ServerToLocalRatioValue = "1:2";
    public string ServerToLocalRatioValue {
      get { return _ServerToLocalRatioValue; }
      set { _ServerToLocalRatioValue = value; }
    }

    double ServerToLocalRatio {
      get {
        var pattern = @"^\d+:\d+$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(_ServerToLocalRatioValue, pattern))
          throw new InvalidCastException("Trade ratio mast look like N:M");
        var parts = _ServerToLocalRatioValue.Split(':');
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
    AliceModes AliceMode = AliceModes.Wonderland;
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
        if (IsLoggedIn) {
          var mt = value.ToList();
          var lt = fwLocal.GetTrades().ToList();
          InvokeSyncronize(mt, lt);
        }
      }
    }
    #endregion

    private void InvokeSyncronize(Trade[] slaveTrades) {
      InvokeSyncronize(MasterList.ToList(), slaveTrades.ToList());
    }
    private void InvokeSyncronize(List<Trade> serverTrades, List<Trade> localTrades) {
      LocalTradesList.Dispatcher.Invoke(new Action(() => Syncronize(serverTrades, localTrades)));
    }
    private void Syncronize(List<Trade> serverTrades, List<Trade> localTrades) {

        #region Absent trades
        var penditTradesToRemove = (from tl in localTrades
                                    join tp in masterTradesPending on tl.MasterTradeId() equals tp.MasterTradeId()
                                    select tp).ToList();
        penditTradesToRemove.ForEach(pt => masterTradesPending.Remove(pt));

        masterTradesPending.ForEach(pt => localTrades.Add(pt));
        ShowTrades(localTrades, LocalTrades);

        var absentTrades = (from ts in serverTrades
                            join tl in localTrades on ts.Id equals tl.MasterTradeId() into svrTrds
                            from st in svrTrds.DefaultIfEmpty()
                            where st == null
                            select ts).ToList();


        AbsentTrades.Clear();
        absentTrades.ForEach(a => AbsentTrades.Add(a.InitUnKnown(fwLocal.ServerTime)));

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
                  masterTradesPending.Remove(tradeToClose);
                  Log = new Exception("Pending trade " + tradeToClose.Id + " was canceled.");
                } else {
                  var orderId = fwLocal.FixOrderClose(tradeToClose.Id);
                  Log = new Exception("Closing trade " + tradeToClose.Id + ". OrderId:" + orderId);
                }
              } catch (Exception exc) { Log = new Exception("TradeId:" + tradeToClose.Id, exc); }
            }
          }
        }
        #endregion
      ServerTime = DateTime.Now;
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
          masterTradesPending.Add(new Trade() { Id = serverTradeId, Remark = new TradeRemark(serverTradeId) });
          Log = new Exception(string.Format("Trade {0} is being clonned", tradeToCopy.Id));
          var buy = AliceMode == AliceModes.Wonderland ? tradeToCopy.Buy : !tradeToCopy.Buy;
          var lots = ((tradeToCopy.Lots * ServerToLocalRatio) / 1000).ToInt() * 1000;
          OpenTrade(tradeToCopy.Pair, buy, lots, serverTradeId);
        }
      } catch (Exception exc) { Log = exc; }
    }
    private void OpenTrade(string pair, bool buy, int lots, string serverTradeID) {
      try {
        fwLocal.FixOrderOpen(pair, buy, lots, 0, 0, serverTradeID);
      } catch (Exception exc) { Log = exc; }
    }
    private void ShowTrades(List<Trade> tradesList, ObservableCollection<Trade> tradesCollection) {
      tradesCollection.Clear();
      tradesList.ForEach(a => tradesCollection.Add(a));
    }


    private TradingAccountModel localAccount = new TradingAccountModel() { ID = "Dimok" };
    public TradingAccountModel[] LocalAccounts { get { return new[] { localAccount }; } }

    void UpdateAccountRow(Account account) {
      var accountRow = LocalAccounts[0];
      accountRow.Balance = account.Balance;
      accountRow.Equity = account.Equity;
      accountRow.Hedging = account.Hedging;
      accountRow.ID = account.ID;
      accountRow.IsMarginCall = account.IsMarginCall;
      accountRow.PipsToMC = account.PipsToMC;
      accountRow.UsableMargin = account.UsableMargin;
      accountRow.Trades = account.Trades;
      accountRow.OnPropertyChanged(
      () => accountRow.Balance,
      () => accountRow.Equity,
      () => accountRow.Hedging,
      () => accountRow.ID,
      () => accountRow.IsMarginCall,
      () => accountRow.PipsToMC,
      () => accountRow.PL,
      () => accountRow.Gross,
      () => accountRow.UsableMargin
        );
    }


    #region Ctor
    FXW.TradesCountChangedEventHandler fw_TradesCountChangedDelegate;
    public SlaveAccountModel() {

      syncronizeScheduler = new Scheduler(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher);

      LocalTradesList = new ListCollectionView(LocalTrades = new ObservableCollection<Trade>());
      AbsentTradesList = new ListCollectionView(AbsentTrades = new ObservableCollection<Trade>());

      fw_TradesCountChangedDelegate = new FXW.TradesCountChangedEventHandler(fw_TradesCountChanged);
      fwLocal = new FXW(this.CoreFX);
      CoreFX.LoggedInEvent += (s, e) => {
        fwLocal.TradesCountChanged += fw_TradesCountChangedDelegate;
        fwLocal.PriceChanged += new FXW.PriceChangedEventHandler(fwLocal_PriceChanged);
        RaisePropertyChanged(() => IsLoggedIn);
        Log = new Exception("Account " + TradingAccount + " logged in.");
        UpdateAccountRow(fwLocal.GetAccount());
      };
      CoreFX.LoginError += exc => {
        Log = exc;
        RaisePropertyChanged(() => IsLoggedIn);
      };
      CoreFX.LoggedOffEvent += (s, e) => {
        Log = new Exception("Account " + TradingAccount + " logged out.");
        RaisePropertyChanged(() => IsLoggedIn);
        fwLocal.TradesCountChanged -= fw_TradesCountChangedDelegate;
      };

    }

    void fwLocal_PriceChanged(Order2GoAddIn.Price Price) {
      if (!syncronizeScheduler.IsRunning) {
        syncronizeScheduler.Command = () => {
          var a = fwLocal.GetAccount();
          UpdateAccountRow(a);
          InvokeSyncronize(a.Trades);
        };
      }
    }
    public SlaveAccountModel(string tradingAccount, string tradingPassword, bool tradingDemo) {
      this.TradingAccount = tradingAccount;
      this.TradingPassword = tradingPassword;
      this.TradingDemo = tradingDemo;
    }
    void fw_TradesCountChanged(Trade trade) {
      Log = new Exception("Trades count changed. TradeId:" + trade.Id);
    }

    #endregion

    #region FXCM
    private Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    FXW fwLocal;
    public bool IsLoggedIn { get { return CoreFX.IsLoggedIn; } }
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
    bool Login(string tradingAccount, string tradingPassword, bool tradingDemo) {
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
      get { return _TradingAccount; }
      set { _TradingAccount = value; RaisePropertyChangedCore(); }
    }

    string _TradingPassword;
    public string TradingPassword {
      get { return _TradingPassword; }
      set { _TradingPassword = value; RaisePropertyChangedCore(); }
    }

    bool _TradingDemo;
    public bool TradingDemo {
      get { return _TradingDemo; }
      set { _TradingDemo = value; RaisePropertyChangedCore(); }
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

        RaisePropertyChanged(() => LogText);
      }
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

  }
}
