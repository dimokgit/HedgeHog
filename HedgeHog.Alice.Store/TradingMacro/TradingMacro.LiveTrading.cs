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

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    #region Pending Action
    bool HasPendingEntryOrders { get { return PendingEntryOrders.Count() > 0; } }
    static MemoryCache _pendingEntryOrders;
    MemoryCache PendingEntryOrders {
      [MethodImpl(MethodImplOptions.Synchronized)]
      get {
        if (_pendingEntryOrders == null)
          _pendingEntryOrders = new MemoryCache(Pair);
        return _pendingEntryOrders;
      }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void ReleasePendingAction(string key) {
      if (PendingEntryOrders.Contains(key)) {
        PendingEntryOrders.Remove(key);
        Debug.WriteLine("Pending[" + Pair + "] " + key + " released.");
      }
    }
    private bool HasPendingOrders() { return PendingEntryOrders.Any(); }
    private bool HasPendingKey(string key) { return !CheckPendingKey(key); }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private bool CheckPendingKey(string key) {
      return !PendingEntryOrders.Contains(key);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private void CheckPendingAction(string key, Action<Action> action = null) {
      if (!HasPendingOrders()) {
        if (action != null) {
          try {
            Action a = () => {
              var cip = new CacheItemPolicy() { AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(1), RemovedCallback = ce => { if (!IsInVitualTrading) Log = new Exception(ce.CacheItem.Key + "[" + Pair + "] expired."); } };
              PendingEntryOrders.Add(key, DateTimeOffset.Now, cip);
            };
            action(a);
          } catch (Exception exc) {
            ReleasePendingAction(key);
            Log = exc;
          }
        }
      } else {
        Debug.WriteLine(Pair + "." + key + " is pending:" + PendingEntryOrders[key] + " in " + Lib.CallingMethod());
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
        if (_CreateEntryOrderSubject == null) {
          _CreateEntryOrderSubject = new Subject<CreateEntryOrderHelper>();
          _CreateEntryOrderSubject
              .SubscribeToLatestOnBGThread(s => {
                try {
                  CheckPendingAction("EO", (pa) => { pa(); GetFXWraper().CreateEntryOrder(s.Pair, s.IsBuy, s.Amount, s.Rate, 0, 0); });
                } catch (Exception exc) {
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
        lock (_DeleteOrderSubjectLocker)
          if (_DeleteOrderSubject == null) {
            _DeleteOrderSubject = new Subject<string>();
            _DeleteOrderSubject
              .Subscribe(s => {
                try {
                  GetFXWraper().DeleteOrder(s, false);
                } catch (Exception exc) { Log = exc; }
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
        if (_CanDoNetLimitOrders != value) {
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
    public bool CanDoNetStopOrders {
      get { return _CanDoNetStopOrders && IsTrader; }
      set {
        if (_CanDoNetStopOrders != value) {
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
        if (_CanDoEntryOrders == value) return;
        _CanDoEntryOrders = value;
        OnPropertyChanged("CanDoEntryOrders");
      }
    }
    #endregion

    IReactiveDerivedList<SuppRes> _reactiveBuySellLevels = null;
    IReactiveDerivedList<SuppRes> _reactiveBuySellLimitLevels = null;
    IReactiveDerivedList<SuppRes> _reactiveBuySellStopLevels = null;
    CompositeDisposable _reactiveBuySellLevelsSubscribtion = null;
    CompositeDisposable _reactiveBuySellCloseLimitSubscribtion = null;
    CompositeDisposable _reactiveBuySellCloseStopSubscribtion = null;
    ReactiveList<Trade> _reactiveTrades = null;
    ReactiveList<Trade> ReactiveTrades {
      get {
        return _reactiveTrades ?? (_reactiveTrades = new ReactiveList<Trade>(Trades) { ChangeTrackingEnabled = true });
      }
    }
    #region TakeProfitManual
    void ResetTakeProfitManual() { TakeProfitManual = double.NaN; }
    private double _TakeProfitManual = double.NaN;
    [Category(categoryTrading)]
    [Dnr]
    public double TakeProfitManual {
      get { return _TakeProfitManual; }
      set {
        if (_TakeProfitManual != value) {
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
        if (_TradeLastChangeDate != value) {
          _TradeLastChangeDate = value;
          OnPropertyChanged("TradeLastChangeDate");
          ReactiveTrades.Clear();
          using (ReactiveTrades.SuppressChangeNotifications())
            Trades.ForEach(t => ReactiveTrades.Add(t));
          ReactiveTrades.Reset();
        }
      }
    }

    #endregion
    class UpdateEntryOrdersBuffer : AsyncBuffer<UpdateEntryOrdersBuffer, Action> {
      protected override Action PushImpl(Action action) {
        return action;
      }
    }
    UpdateEntryOrdersBuffer _updateEntryOrdersBuffer = UpdateEntryOrdersBuffer.Create();
    void SubscribeToEntryOrderRelatedEvents() {
      var bsThrottleTimeSpan = 0.1.FromSeconds();
      var cpThrottleTimeSpan = 0.25.FromSeconds();
      var buySelPropsExceptions = new[] { "CanTradeEx", "IsGhost" };
      Func<IReactivePropertyChangedEventArgs<SuppRes>, bool> buySellPropsFilter = _ => !buySelPropsExceptions.Contains(_.PropertyName);
      //ISubject<Action> fxWraper = new Subject<Action>();
      //fxWraper.ObserveOn(TradesManagerStatic.TradingScheduler).Subscribe(a => a(), () => { Debugger.Break(); });

      #region SetTradeNet
      Action<Trade, double, double> SetTradeNet = (trade, limit, stop) => {
        //fxWraper.OnNext(() => {
        var fw = GetFXWraper();
        if (!limit.IsNaN())
          try {
            if (fw.GetNetOrderRate(Pair, false).Abs(limit) > InPoints(1)) {
              //Log = new Exception("FixOrderSetLimit:" + new { trade.Pair, limit = limit.Round(Digits()) });
              fw.FixOrderSetLimit(trade.Id, limit, "");
            }
          } catch (Exception exc) { Log = exc; }
        if (!stop.IsNaN())
          try {
            if (fw.GetNetOrderRate(Pair, true).Abs(stop) > InPoints(1)) {
              //Log = new Exception("FixOrderSetStop:" + new { trade.Pair, stop = stop.Round(Digits()) });
              fw.FixOrderSetStop(trade.Id, stop, "");
            }
          } catch (Exception exc) { Log = exc; }
        TradeLastChangeDate = DateTime.Now;
        //});
      };
      Action<Trade, double> SetTradeNetLimit = (trade, limit) => SetTradeNet(trade, limit, double.NaN);
      Action<Trade, double> SetTradeNetStop = (trade, stop) => SetTradeNet(trade, double.NaN, stop);
      Action CloseAllNetLimits = () => Trades.Take(1).ForEach(trade => SetTradeNetLimit(trade, 0));
      Action CloseAllNetStops = () => Trades.Take(1).ForEach(trade => SetTradeNetStop(trade, 0));
      #endregion

      #region startBuySellLevelsTracking
      Action startBuySellLevelsTracking = () => {
        #region updateEntryOrders
        Action<string> updateEntryOrders = (reason) => {
          try {
            var buySellLevels = new[] { BuyLevel, SellLevel };
            GetEntryOrders().GroupBy(eo => eo.IsBuy).SelectMany(eog => eog.Skip(1)).ForEach(OnDeletingOrder);
            Func<SuppRes, bool> canTrade = (sr) =>/* IsTradingHour() &&*/ sr.CanTrade && sr.TradesCount <= 0
              && !Trades.IsBuy(sr.IsBuy).Any();
            Func<bool, int> lotSize = isBuy =>
              (buySellLevels.Where(sr => sr.IsBuy == isBuy).Any(canTrade) ? (isBuy ? LotSizeByLossBuy : LotSizeByLossSell) : 0)
              + (GetFXWraper().GetNetOrderRate(Pair, true) > 0 ? 0 : Trades.IsBuy(!isBuy).Lots());
            buySellLevels.Select(sr => new { sr.IsBuy, sr.Rate, lotSize = lotSize(sr.IsBuy) })
              .Do(sr => GetEntryOrders(sr.IsBuy).Where(a => sr.lotSize == 0).ForEach(OnDeletingOrder))
              .Where(sr => sr.lotSize > 0 && !GetEntryOrders(sr.IsBuy).Any())
             .ForEach(level => OnCreateEntryOrder(level.IsBuy, level.lotSize, level.Rate));

            Action<Order> changeLimit = eo => GetFXWraper().YieldNotNull(eo.Lot.Ratio(lotSize(eo.IsBuy)) > 1.025)
              .ForEach(fw => {
                //Log = new Exception("ChangeEntryOrderLot:" + reason);
                fw.ChangeEntryOrderLot(eo.OrderID, lotSize(eo.IsBuy));
              });

            Func<bool, double> orderRate = isBuy => buySellLevels.Where(sr => sr.IsBuy == isBuy).First().Rate;
            Action<Order> changeRate = eo => GetFXWraper().YieldNotNull(eo.Rate.Abs(orderRate(eo.IsBuy)) > PointSize)
              .ForEach(fw => {
                //Log = new Exception("ChangeEntryOrderRate:" + reason);
                fw.ChangeEntryOrderRate(eo.OrderID, orderRate(eo.IsBuy));
              });

            GetEntryOrders().ForEach(eo => {
              changeLimit(eo);
              changeRate(eo);
            });
          } catch (Exception exc) { Log = exc; }
        };
        #endregion
        _reactiveBuySellLevels = new[] { BuyLevel, SellLevel, BuyCloseLevel, SellCloseLevel }.CreateDerivedCollection(sr => sr);
        _reactiveBuySellLevels.ChangeTrackingEnabled = true;
        _reactiveBuySellLevelsSubscribtion = (CompositeDisposable)
          _reactiveBuySellLevels.ItemChanged
          .Where(buySellPropsFilter)
          .Sample(bsThrottleTimeSpan)
          //.Do(_ => Log = new Exception(new { Name = "startBuySellLevelsTracking", _.PropertyName, Value = _.Value + "" } + ""))
          .Select(_ => _.Sender.IsBuy ? "Buy" + (_.Sender.IsExitOnly ? "Close" : "") + "Level" : "Sell" + (_.Sender.IsExitOnly ? "Close" : "") + "Level")
          .Merge(ReactiveTrades.ItemChanged.Where(_ => _.PropertyName == "Stop").Select(_ => _.Sender.IsBuy ? "BuyTrade" : "SellTrade"))
          .Merge(Observable.FromEventPattern<EventHandler<OrderEventArgs>, OrderEventArgs>(
            h => GetFXWraper().OrderAdded += h, h => GetFXWraper().OrderAdded -= h).Select(e => "OrderAdded"))
          .Merge(Observable.FromEventPattern<EventHandler<OrderEventArgs>, OrderEventArgs>(
            h => GetFXWraper().OrderChanged += h, h => GetFXWraper().OrderChanged -= h).Select(e => "OrderChanged"))
          .Merge(Observable.FromEvent<OrderRemovedEventHandler, Order>(h => GetFXWraper().OrderRemoved += h, h => GetFXWraper().OrderRemoved -= h).Select(_ => "OrderRemoved"))
          .Merge(this.WhenAny(tm => tm.CurrentPrice, tm => "CurrentPrice").Sample(cpThrottleTimeSpan))
          .Merge(this.WhenAny(tm => tm.CanDoEntryOrders, tm => "CanDoEntryOrders"))
          .Merge(this.WhenAny(tm => tm.CanDoNetStopOrders, tm => "CanDoNetStopOrders"))
          .Subscribe(reason => _updateEntryOrdersBuffer.Push(() => updateEntryOrders(reason)));
        updateEntryOrders("Start Tracking");
      };
      #endregion
      #region startBuySellCloseLevelsTracking
      #region Net Update Implementations
      var bsCloseLevels = MonoidsCore.ToFunc(() => new[] { BuyCloseLevel, SellCloseLevel }.Where(sr => sr != null));
      Action updateTradeLimitOrders = () => {
        Func<Trade, double[]> levelRate = trade => bsCloseLevels().Where(sr => sr.IsBuy == !trade.IsBuy).Select(sr=>sr.Rate).Take(1).ToArray();
        Action<Trade> changeRate = trade => levelRate(trade)
          .Where(_=> GetFXWraper()!=null)
          .Where(lr=>trade.Limit.Abs(lr) > PointSize)
          .ForEach(lr => SetTradeNetLimit(trade, lr));
        Trades.Take(1).ForEach(changeRate);
      };
      Func<Trade, double[]> getDefaultStop = trade => // Take care of reversed corridor
        bsCloseLevels()
        .Where(_ => SellLevel.Rate > BuyLevel.Rate)
        .Where(bs => bs.IsBuy != trade.IsBuy)
        .Select(bs => trade.Open.Abs(bs.Rate) * 2)
        .Select(stop => trade.IsBuy ? -stop : stop)
        .Select(stop => trade.Open + stop)
        .ToArray();
      Action updateTradeStopOrders = () => {
        var bsLevels = new[] { BuyLevel, SellLevel }.Where(sr => sr != null);
        Func<Trade, IEnumerable<double>> levelRate = trade =>
          getDefaultStop(trade)
          .Concat(bsLevels
          .Where(sr => sr.IsBuy == !trade.IsBuy && (!CanDoEntryOrders || !sr.CanTrade))
          .Select(t => t.Rate)
          .Take(1)
          )
          .Take(1);
        Action<Trade> changeRate = trade => GetFXWraper().YieldNotNull(levelRate(trade).Any(rate => trade.Stop.Abs(rate) > PointSize))
          .ForEach(fw => levelRate(trade).ForEach(rate => SetTradeNetStop(trade, rate)));
        Trades.Take(1).ForEach(changeRate);
      };
      #endregion
      // New Limit
      Action startBuySellCloseLimitTracking = () => {
        _reactiveBuySellLimitLevels = new[] { BuyCloseLevel, SellCloseLevel, BuyLevel, SellLevel }
          .CreateDerivedCollection(sr => sr);
        _reactiveBuySellLimitLevels.ChangeTrackingEnabled = true;
        _reactiveBuySellCloseLimitSubscribtion = (CompositeDisposable)_reactiveBuySellLimitLevels
          .ItemChanged
          .Where(buySellPropsFilter)
          .Sample(bsThrottleTimeSpan)
          //.Do(_ => Log = new Exception(new { Name = "startBuySellCloseLevelsTracking", _.PropertyName, Value = _.Value + "" } + ""))
          .Select(_ => _.Sender.IsBuy ? "Buy(Close)Level" : "Sell(Close)Level")
          .Merge(this.WhenAny(tm => tm.CurrentPrice, tm => "CurrentPrice").Sample(cpThrottleTimeSpan))
          .Merge(this.WhenAny(tm => tm.CanDoEntryOrders, tm => "CanDoEntryOrders"))
          .Merge(this.WhenAny(tm => tm.CanDoNetStopOrders, tm => "CanDoNetStopOrders"))
          .Merge(this.WhenAny(tm => tm.IsTrader, tm => "IsTrader"))
          .Subscribe(_ => {
            if (CanDoNetLimitOrders) updateTradeLimitOrders();
            else CloseAllNetLimits();
          });
      };
      // New Stop
      Action startBuySellCloseStopTracking = () => {
        _reactiveBuySellStopLevels = new[] { BuyCloseLevel, SellCloseLevel, BuyLevel, SellLevel }
          .CreateDerivedCollection(sr => sr);
        _reactiveBuySellStopLevels.ChangeTrackingEnabled = true;
        _reactiveBuySellCloseStopSubscribtion = (CompositeDisposable)_reactiveBuySellStopLevels
          .ItemChanged
          .Where(buySellPropsFilter)
          .Sample(bsThrottleTimeSpan)
          //.Do(_ => Log = new Exception(new { Name = "startBuySellCloseLevelsTracking", _.PropertyName, Value = _.Value + "" } + ""))
          .Select(_ => _.Sender.IsBuy ? "Buy(Close)Level" : "Sell(Close)Level")
          .Merge(this.WhenAny(tm => tm.CurrentPrice, tm => "CurrentPrice").Sample(cpThrottleTimeSpan))
          .Merge(this.WhenAny(tm => tm.CanDoEntryOrders, tm => "CanDoEntryOrders"))
          .Merge(this.WhenAny(tm => tm.CanDoNetStopOrders, tm => "CanDoNetStopOrders"))
          .Merge(this.WhenAny(tm => tm.IsTrader, tm => "IsTrader"))
          .Subscribe(_ => {
            if (CanDoNetStopOrders) updateTradeStopOrders();
            else CloseAllNetStops();
          });
      };
      #endregion

      #region Init BuySellLevels
      this.WhenAny(tm => tm.Strategy
        , tm => tm.TrailingDistanceFunction
        , tm => tm.HasBuyLevel
        , tm => tm.HasSellLevel
        , tm => tm.CanDoEntryOrders
        , tm => tm.CanDoNetLimitOrders
        , tm => tm.MustStopTrading
        , (s, t, eo, no, ta, bl, sl) =>
          Strategy == Strategies.Universal && HasBuyLevel && HasSellLevel && CanDoEntryOrders && !MustStopTrading && !IsInVitualTrading
          )
          .DistinctUntilChanged()
          .Sample(bsThrottleTimeSpan)
          .Subscribe(st => {// Turn on/off live entry orders
            try {
              if (st) {// Subscribe to events in order to update live entry orders
                //Log = new Exception("startBuySellLevelsTracking");
                startBuySellLevelsTracking();
              } else if (_reactiveBuySellLevelsSubscribtion != null) {
                try {
                  GetEntryOrders().ToList().ForEach(order => OnDeletingOrder(order.OrderID));
                } catch (Exception exc) { Log = exc; }
                CleanReactiveBuySell(ref _reactiveBuySellLevelsSubscribtion, ref _reactiveBuySellLevels);
              }
            } catch (Exception exc) { Log = exc; }
          });
      #endregion
      #region Init BuySellCloseLevels
      //this.WhenAny(
      //    tm => tm.BuyCloseLevel
      //  , tm => tm.SellCloseLevel
      //  , tm => tm.CanDoNetLimitOrders
      //  , tm => tm.CanDoNetStopOrders
      //  , tm => tm.CanDoEntryOrders
      //  , (b, s, non, nos, eo) =>
      //    BuyCloseLevel != null && SellCloseLevel != null && CanDoNetOrders && !IsInVitualTrading)
      //    .DistinctUntilChanged()
      //    .Sample(bsThrottleTimeSpan)
      //    .Subscribe(st => {// Turn on/off live net orders
      //      try {
      //        CleanReactiveBuySell(ref _reactiveBuySellCloseLevelsSubscribtion, ref _reactiveBuySellCloseLevels);
      //        if (!CanDoNetLimitOrders) CloseAllNetLimits();
      //        if (!CanDoNetStopOrders) CloseAllNetStops();
      //        if (st) {// (Re)Subscribe to events in order to update live net orders
      //          Log = new Exception("startBuySellCloseLevelsTracking");
      //          startBuySellCloseLevelsTracking();
      //        }
      //      } catch (Exception exc) { Log = exc; }
      //    });
      // New Limit
      this.WhenAny(
          tm => tm.HasBuyCloseLevel
        , tm => tm.HasSellCloseLevel
        , tm => tm.CanDoNetLimitOrders
        , (b, s, non) =>
          HasBuyCloseLevel && HasSellCloseLevel && CanDoNetLimitOrders && !IsInVitualTrading)
          .DistinctUntilChanged()
          .Sample(bsThrottleTimeSpan)
          .Subscribe(st => {// Turn on/off live net orders
            try {
              CleanReactiveBuySell(ref _reactiveBuySellCloseLimitSubscribtion, ref _reactiveBuySellLimitLevels);
              if (!CanDoNetLimitOrders) {
                //Log = new Exception("Stop Limit Tracking");
                CloseAllNetLimits();
              }
              if (st) {// (Re)Subscribe to events in order to update live net orders
                //Log = new Exception("Start Limit Tracking");
                startBuySellCloseLimitTracking();
              }
            } catch (Exception exc) { Log = exc; }
          });
      // Net Stop
      this.WhenAny(
          tm => tm.HasBuyCloseLevel
        , tm => tm.HasSellCloseLevel
        , tm => tm.CanDoNetStopOrders
        , (b, s, non) =>
          HasBuyCloseLevel && HasSellCloseLevel && CanDoNetStopOrders && !IsInVitualTrading)
          .DistinctUntilChanged()
          .Sample(bsThrottleTimeSpan)
          .Subscribe(st => {// Turn on/off live net orders
            try {
              CleanReactiveBuySell(ref _reactiveBuySellCloseStopSubscribtion, ref _reactiveBuySellStopLevels);
              if (!CanDoNetStopOrders) {
                //Log = new Exception("Stop Stop Tracking");
                CloseAllNetStops();
              }
              if (st) {// (Re)Subscribe to events in order to update live net orders
                //Log = new Exception("Start Stop Tracking");
                startBuySellCloseStopTracking();
              }
            } catch (Exception exc) { Log = exc; }
          });
      #endregion

    }


    private static void CleanReactiveBuySell<T>(ref CompositeDisposable subscribsion, ref IReactiveDerivedList<T> reaciveList) {
      if (subscribsion != null) {
        subscribsion.Dispose();
        subscribsion = null;
        reaciveList.Dispose();
        reaciveList = null;
      }
    }
    #endregion


  }
}
