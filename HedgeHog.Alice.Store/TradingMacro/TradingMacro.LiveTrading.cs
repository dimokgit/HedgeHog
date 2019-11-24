using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Caching;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Disposables;
using ReactiveUI.Legacy;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    public bool HaveTrades() => Trades.Any() || HasPendingOrders();
    public bool HaveTradesIncludingHedged() => TradingMacroHedged(tm => tm.HaveTrades(), -1).Concat(new[] { HaveTrades() }).Any(b => b);
    public IList<Trade> HedgedTrades() => TradingMacroHedged(tm => Trades.Concat(tm.Trades), -1).Concat().ToList().With(l => l.Count > 1 ? l : new List<Trade>());
    public bool HaveHedgedTrades() => TradingMacroHedged(tm => tm.HaveTrades(), -1).Concat(new[] { HaveTrades() }).Count(ht => ht) == 2;

    public bool HaveTrades(bool isBuy) {
      return Trades.IsBuy(isBuy).Any() || HasPendingOrders();
    }
    void LogTradingAction(object message, [CallerMemberName] string Caller = "") {
      if(IsInVirtualTrading || LogTrades) {
        Log = new Exception(message + "" + new { Caller });
      }
    }
    void LogTradingActionStatus(string key, string status, [CallerMemberName] string Caller = "") => LogTradingActionStatus(key, status, false, Caller);
    void LogTradingActionStatus(string key, string status, bool sendSms, [CallerMemberName] string Caller = "") {
      var message = PendingOrderMessage(key, status);
      LogTradingAction(message, Caller);
      if(sendSms)
        OnSMS(SmsKey.Trading, message + "", ServerTime + "");
    }

    #region Pending Action
    MemoryCache _pendingEntryOrders;
    public MemoryCache PendingEntryOrders {
      get {
        lock(_pendingEntryOrdersLocker) {
          if(_pendingEntryOrders == null)
            _pendingEntryOrders = new MemoryCache(Pair);
          return _pendingEntryOrders;
        }
      }
    }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private void ReleasePendingAction(string key, [CallerMemberName] string Caller = "") {
      lock(_pendingEntryOrdersLocker) {
        LogExistingPendingActions(Common.CallerChain(Caller));
        //if(_pendingEntryOrders.Contains(key)) {
        PendingEntryOrders.Where(po => po.Key == key).ToList()
          .ForEach((po, i) => {
            PendingEntryOrders.Remove(po.Key);
            LogTradingActionStatus(key, $"Released:{i}", true);
          });
        //else
        //  LogTradingActionStatus(key, "Unknown");
      }
    }
    string PendingOrderMessage(string key, string status) => new { Pending = $"{this}", key, status } + "";
    /*
*/
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private bool TryAddPendingAction(string key) {
      if(HasPendingKey(key)) return false;
      AddPendingAction(key);
      return true;
    }
    private void AddPendingAction(string key) {
      var exp = ObjectCache.InfiniteAbsoluteExpiration;
      var cip = new CacheItemPolicy() {
        AbsoluteExpiration = exp,
        RemovedCallback = ce => { if(DateTime.Now > exp) Log = new Exception(ce.CacheItem.Key + "[" + Pair + "] expired without being closed."); }
      };
      AddPendingAction(key, DateTimeOffset.Now, cip);
    }
    private void AddPendingAction(string key, object value, CacheItemPolicy cip) {
      lock(_pendingEntryOrdersLocker) {
        if(PendingEntryOrders.Contains(key))
          throw new Exception(PendingOrderMessage(key, "Already exists"));
        PendingEntryOrders.Add(key, DateTimeOffset.Now, cip);
        LogTradingActionStatus(key, "Added");
      }
    }

    private void LogExistingPendingActions([CallerMemberName] string Caller = "") => PendingEntryOrders.ForEach(po => LogTradingActionStatus(po.Key, "Existing"));

    static object _pendingEntryOrdersLocker = new object();
    private bool HasPendingOrders() {
      lock(_pendingEntryOrdersLocker)
        return PendingEntryOrders.Any();
    }
    private bool HasPendingKey(string key) { return !CheckPendingKey(key); }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private bool CheckPendingKey(string key) {
      lock(_pendingEntryOrdersLocker)
        return !PendingEntryOrders.Any();//.Contains(key);
    }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    private void CheckPendingAction(string key, Action<Action> action = null) {
      if(!HasPendingOrders()) {
        if(action != null) {
          try {
            Action a = () => {
              AddPendingAction(key);
            };
            action(a);
          } catch(Exception exc) {
            Log = exc;
          }
        }
      } else {
        LogExistingPendingActions();
      }
    }
    #endregion

    #region CreateEntryOrder Subject
    class CreateEntryOrderHelper {
      public string Pair { get; set; }
      public bool IsBuy { get; set; }
      public int Amount { get; set; }
      public double Rate { get; set; }
      public CreateEntryOrderHelper(string pair, bool isbuy, int amount, double rate) {
        this.Pair = pair;
        this.IsBuy = isbuy;
        this.Amount = amount;
        this.Rate = rate;
      }
    }
    static ISubject<CreateEntryOrderHelper> _CreateEntryOrderSubject;

    ISubject<CreateEntryOrderHelper> CreateEntryOrderSubject {
      get {
        if(_CreateEntryOrderSubject == null) {
          _CreateEntryOrderSubject = new Subject<CreateEntryOrderHelper>();
          _CreateEntryOrderSubject
              .SubscribeToLatestOnBGThread(s => {
                try {
                  CheckPendingAction(EO, (pa) => { pa(); TradesManager.CreateEntryOrder(s.Pair, s.IsBuy, s.Amount, s.Rate, 0, 0); });
                } catch(Exception exc) {
                  Log = exc;
                }
              }, exc => Log = exc);
        }
        return _CreateEntryOrderSubject;
      }
    }

    void OnCreateEntryOrder(bool isBuy, int amount, double rate) {
      CreateEntryOrderSubject.OnNext(new CreateEntryOrderHelper(Pair, isBuy, amount, rate));
    }
    #endregion

    #region DeleteOrder Subject
    static object _DeleteOrderSubjectLocker = new object();
    static ISubject<string> _DeleteOrderSubject;
    ISubject<string> DeleteOrderSubject {
      get {
        lock(_DeleteOrderSubjectLocker)
          if(_DeleteOrderSubject == null) {
            _DeleteOrderSubject = new Subject<string>();
            _DeleteOrderSubject
              .Subscribe(s => {
                try {
                  TradesManager.DeleteOrder(s);
                } catch(Exception exc) { Log = exc; }
              }, exc => Log = exc);
          }
        return _DeleteOrderSubject;
      }
    }
    protected void OnDeletingOrder(Order order) {
      DeleteOrderSubject.OnNext(order.OrderID);
    }
    protected void OnDeletingOrder(string orderId) {
      DeleteOrderSubject.OnNext(orderId);
    }
    #endregion

    bool CanDoNetOrders { get { return CanDoNetStopOrders || CanDoNetLimitOrders; } }

    #region Real-time trading orders
    #region CanDoNetLimitOrders
    private bool _CanDoNetLimitOrders;
    [WwwSetting]
    [Category(categoryActiveYesNo)]
    [DisplayName("Can Do Limit Orders")]
    public bool CanDoNetLimitOrders {
      get { return _CanDoNetLimitOrders && IsTrader; }
      set {
        if(_CanDoNetLimitOrders != value) {
          _CanDoNetLimitOrders = value;
          OnPropertyChanged("CanDoNetLimitOrders");
        }
      }
    }

    #endregion
    #region CanDoNetStopOrders
    private bool _CanDoNetStopOrders;
    [Category(categoryActiveYesNo)]
    [DisplayName("Can Do Stop Orders")]
    [WwwSetting]
    public bool CanDoNetStopOrders {
      get { return _CanDoNetStopOrders && IsTrader; }
      set {
        if(_CanDoNetStopOrders != value) {
          _CanDoNetStopOrders = value;
          OnPropertyChanged("CanDoNetStopOrders");
        }
      }
    }

    #endregion
    #region CanDoEntryOrders
    private bool _CanDoEntryOrders = false;
    [WwwSetting]
    [Category(categoryActiveYesNo)]
    [DisplayName("Can Do Entry Orders")]
    [Dnr]
    public bool CanDoEntryOrders {
      get { return _CanDoEntryOrders && IsTrader; }
      set {
        if(_CanDoEntryOrders == value)
          return;
        _CanDoEntryOrders = value;
        OnPropertyChanged("CanDoEntryOrders");
      }
    }
    #endregion

    #region TakeProfitManual
    void ResetTakeProfitManual() { TakeProfitManual = double.NaN; }
    private double _TakeProfitManual = double.NaN;
    [Category(categoryTrading)]
    [Dnr]
    public double TakeProfitManual {
      get { return _TakeProfitManual; }
      set {
        if(_TakeProfitManual != value) {
          _TakeProfitManual = value;
          OnPropertyChanged("TakeProfitManual");
        }
      }
    }

    #endregion
    #region TradeLastChangeDate
    private DateTime _TradeLastChangeDate;
    public DateTime TradeLastChangeDate {
      get { return _TradeLastChangeDate; }
      set {
        if(_TradeLastChangeDate != value) {
          _TradeLastChangeDate = value;
          OnPropertyChanged("TradeLastChangeDate");
        }
      }
    }

    #endregion

    #endregion


  }
}
