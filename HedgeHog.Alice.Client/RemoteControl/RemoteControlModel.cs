﻿using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using Gala = GalaSoft.MvvmLight.Command;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.Windows.Data;
using System.Data.Objects;
using System.Windows.Input;
using System.Windows;
using HedgeHog.Shared;
using HedgeHog.Bars;
using System.IO;
using System.Xml.Linq;
using System.ComponentModel.Composition;
using System.Threading;

namespace HedgeHog.Alice.Client {
  [Export]
  public class RemoteControlModel : HedgeHog.Models.ModelBase {
    #region Settings
    readonly int historyMinutesBack = 60 * 3;
    readonly double profitToClose = 0.01;
    #endregion
    #region Properties
    public bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    FXW fw;
    public bool IsLoggedIn { get { return MasterModel.CoreFX.IsLoggedIn; } }
    IMainModel _MasterModel;
    [Import]
    public IMainModel MasterModel {
      get { return _MasterModel; }
      set {
        if (_MasterModel != value) {
          _MasterModel = value;
          RaisePropertyChangedCore();
        }
      }
    }

    public ObservableCollection<string> Instruments { get; set; }
    public double[] TradingRatios { get { return new double[] {0, 0.1,0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }; } }
    public double[] StopsAndLimits { get { return new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90, 100, 120, 135, 150 }; } }

    double CurrentLoss { get { return TradingMacrosCopy.Sum(tm => tm.CurrentLoss); } }
    object _tradingMacrosLocker = new object();
    Models.TradingMacro[] _tradingMacrosCiopy = new Models.TradingMacro[0];
    public Models.TradingMacro[] TradingMacrosCopy {
      get {
          if (_tradingMacrosCiopy.Length == 0)
            _tradingMacrosCiopy = TradingMacros.ToArray();
          return _tradingMacrosCiopy;
      }
    }
    Models.TradingMacro GetTradingMacro(string pair) {
      var tms = TradingMacrosCopy.Where(tm => tm.Pair == pair).ToArray();
      if (tms.Length == 0)
        new NullReferenceException("TradingMacro is null");
      return tms.OrderBy(tm => tm.LotSize).ThenBy(tm => tm.Limit).FirstOrDefault();
    }

    public IQueryable<Models.TradingMacro> TradingMacros {
      get {
        try {
          return !IsInDesigh ? GlobalStorage.Context.TradingMacroes : new[] { new Models.TradingMacro() }.AsQueryable();
        } catch (Exception exc) {
          Debug.Fail("TradingMacros is null.");
          return null;
        }
      }
    }
    private Exception Log {
      set {
        if (MasterModel == null)
          MessageBox.Show(value + "");
        else
          MasterModel.Log = value;
      }
    }
    Dictionary<string, List<Rate>> ratesByPair = new Dictionary<string, List<Rate>>();
    Dictionary<string, Tick[]> ticksByPair = new Dictionary<string, Tick[]>();
    Dictionary<string, double> anglesByPair = new Dictionary<string, double>();

    Dictionary<string, ThreadScheduler> _loadTicks = new Dictionary<string, ThreadScheduler>();
    ThreadScheduler GetTickLoader(string pair) {
      if (!_loadTicks.ContainsKey(pair)) 
        _loadTicks.Add(pair, new ThreadScheduler(() => LoadTicks(pair), (s, e) => Log = e.Exception));
      return _loadTicks[pair];
    }

    #region PendingTrade
    class PendingTrade {
      public string Pair { get; set; }
      public bool IsBuy { get; set; }
      public Func<bool> Condition { get; set; }
      public Action OpenTradeCommand;
      public PendingTrade(string pair, bool isBuy, Func<bool> condition,Action openTradeCommand) {
        this.Pair = pair;
        this.IsBuy = isBuy;
        this.Condition = condition;
        this.OpenTradeCommand = openTradeCommand;
      }
    }
    List<PendingTrade> _pendingTrades= new List<PendingTrade>();
    PendingTrade GetPendingTrade(string pair, bool isBuy) {
       var pt = _pendingTrades.SingleOrDefault(po => po.Pair == pair && po.IsBuy == isBuy);
       return pt == null ? null : pt;
    }
    bool HasPendingOrder(string pair, bool isBuy) { return GetPendingTrade(pair, isBuy) != null; }
    void RemovePendingOrder(string pair, bool isBuy) {
      var pt = GetPendingTrade(pair, isBuy);
      if (pt != null) _pendingTrades.Remove(pt);
    }
    void AddPendingOrder(bool isBuy, string pair, Func<bool> condition, Action openTradeCommand) {
      _pendingTrades.Add(new PendingTrade(pair, isBuy, condition, openTradeCommand));
    }
    #endregion

    #endregion

    #region Commands

    #region ClearCurrentLossCommand

    ICommand _ClearCurrentLossCommand;
    public ICommand ClearCurrentLossCommand {
      get {
        if (_ClearCurrentLossCommand == null) {
          _ClearCurrentLossCommand = new Gala.RelayCommand(ClearCurrentLoss, () => true);
        }

        return _ClearCurrentLossCommand;
      }
    }
    void ClearCurrentLoss() {
      foreach (var tm in TradingMacrosCopy)
        tm.CurrentLoss = 0;
    }

    #endregion

    ICommand _DeleteTradingMacroCommand;
    public ICommand DeleteTradingMacroCommand {
      get {
        if (_DeleteTradingMacroCommand == null) {
          _DeleteTradingMacroCommand = new Gala.RelayCommand<object>(DeleteTradingMacro, (tm) => tm is Models.TradingMacro);
        }

        return _DeleteTradingMacroCommand;
      }
    }
    void DeleteTradingMacro(object tradingMacro) {
      var tm = tradingMacro as Models.TradingMacro;
      if (tm == null || tm.EntityState == System.Data.EntityState.Detached) return;
      GlobalStorage.Context.TradingMacroes.DeleteObject(tm);
      GlobalStorage.Context.SaveChanges();
    }


    ICommand _ClosePairCommand;
    public ICommand ClosePairCommand {
      get {
        if (_ClosePairCommand == null) {
          _ClosePairCommand = new Gala.RelayCommand<object>(ClosePair, (tm) => true);
        }

        return _ClosePairCommand;
      }
    }

    void ClosePair(object tradingMacro) {
      try {
        var pair = (tradingMacro as Models.TradingMacro).Pair;
        var tradeIds = fw.GetTrades(pair).Select(t => t.Id).ToArray();
        if (tradeIds.Length > 0) AddTradeToReverse(tradeIds.Last());
        fw.FixOrdersClose(tradeIds);
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }



    ICommand _ReversePairCommand;
    public ICommand ReversePairCommand {
      get {
        if (_ReversePairCommand == null) {
          _ReversePairCommand = new Gala.RelayCommand<object>(ReversePair, (tm) => true);
        }

        return _ReversePairCommand;
      }
    }

    List<string> tradesToReverse = new List<string>();
    void AddTradeToReverse(string tradeId) {
      if (!tradesToReverse.Any(s => s == tradeId)) tradesToReverse.Add(tradeId);
    }
    string GetTradeToReverse(string tradeId) {
      return tradesToReverse.SingleOrDefault(s => s == tradeId);
    }
    bool HasTradeToReverse(string tradeId) {
      return !string.IsNullOrWhiteSpace(GetTradeToReverse(tradeId));
    }
    void RemoveTradeToReverse(string tradeId) {
      if (HasTradeToReverse(tradeId)) 
        tradesToReverse.Remove(GetTradeToReverse(tradeId));
    }

    void ReversePair(object tradingMacro) {
      try {
        var pair = (tradingMacro as Models.TradingMacro).Pair;
        var tradeIds = fw.GetTrades(pair).Select(t => t.Id).ToArray();
        if (tradeIds.Length > 0) AddTradeToReverse(tradeIds.Last());
        fw.FixOrdersClose(tradeIds);
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    ICommand _BuyCommand;
    public ICommand BuyCommand {
      get {
        if (_BuyCommand == null) {
          _BuyCommand = new Gala.RelayCommand<object>(Buy, (tm) => true);
        }

        return _BuyCommand;
      }
    }
    void Buy(object tradingMacro) {
      try {
        OpenChainTrade(tradingMacro as Models.TradingMacro, true);
        //AddPendingOrder(true, tm.Pair, () => openTradeCondition( tm,true), () => OpenTrade(tm, true));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    ICommand _SellCommand;
    public ICommand SellCommand {
      get {
        if (_SellCommand == null) {
          _SellCommand = new Gala.RelayCommand<object>(Sell, (tm) => true);
        }

        return _SellCommand;
      }
    }
    void Sell(object tradingMacro) {
      try {
        OpenChainTrade(tradingMacro as Models.TradingMacro,false);
        //AddPendingOrder(false, tm.Pair, () => openTradeCondition(tm, false), () => OpenTrade(tm, false));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }
    #endregion

    #region Ctor
    void CleanEntryOrders() {
      try {
        var trades = fw.GetTrades();
        foreach (var order in fw.GetOrders(""))
          if (!trades.Any(t => t.Pair == order.Pair)) fw.DeleteOrder(order.OrderID);
      } catch (Exception exc) {
        Log = exc;
      }
    }
    public RemoteControlModel() {
      try {
        Instruments = new ObservableCollection<string>(new[] { "EUR/USD", "USD/JPY" });
        if (!IsInDesigh) {
          GlobalStorage.Context.ObjectMaterialized += new ObjectMaterializedEventHandler(Context_ObjectMaterialized);
          GlobalStorage.Context.ObjectStateManager.ObjectStateManagerChanged += new System.ComponentModel.CollectionChangeEventHandler(ObjectStateManager_ObjectStateManagerChanged);
          App.container.SatisfyImportsOnce(this);
          fw = new FXW(MasterModel.CoreFX);
          MasterModel.CoreFX.LoggedInEvent += CoreFX_LoggedInEvent;
          MasterModel.CoreFX.LoggedOffEvent += CoreFX_LoggedOffEvent;
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    ~RemoteControlModel() {
      if (MasterModel != null) {
        MasterModel.CoreFX.LoggedInEvent -= CoreFX_LoggedInEvent;
        MasterModel.CoreFX.LoggedOffEvent -= CoreFX_LoggedOffEvent;
      }
    }
    #endregion

    #region Event Handlers
    void ObjectStateManager_ObjectStateManagerChanged(object sender, System.ComponentModel.CollectionChangeEventArgs e) {
      var tm = e.Element as Models.TradingMacro;
      if (tm != null) {
        if (tm.EntityState == System.Data.EntityState.Detached)
          tm.PropertyChanged -= TradingMacro_PropertyChanged;
        else if (tm.EntityState == System.Data.EntityState.Added)
          InitTradingMacro(tm);
      }
    }

    void Context_ObjectMaterialized(object sender, ObjectMaterializedEventArgs e) {
      var tm = e.Entity as Models.TradingMacro;
      if (tm == null) return;
      InitTradingMacro(tm);
    }

    void TradingMacro_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
      try {
        var tm = sender as Models.TradingMacro;
        var propsToHandle = Lib.GetLambdas(() => tm.Pair, () => tm.TradingRatio, () => tm.Limit);
        if (propsToHandle.Contains(e.PropertyName)) SetLotSize(tm, fw.GetAccount());
        //if (e.PropertyName == Lib.GetLambda(() => tm.OverlapToStop)) LoadRates(tm.Pair);
        if (e.PropertyName == Lib.GetLambda(() => tm.CurrentLoss)) {
          System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => {
            try {
              MasterModel.CurrentLoss = CurrentLoss;
              GlobalStorage.Context.SaveChanges();
            } catch (Exception exc) {
              Log = exc;
            }
          }));
        }
      } catch (Exception exc) { Log = exc; }
    }

    void CoreFX_LoggedInEvent(object sender, EventArgs e) {
      try {
        if (TradingMacrosCopy.Length > 0) {
          InitInstruments();
          fw.PriceChanged += fw_PriceChanged;
          fw.TradeRemoved += fw_TradeRemoved;
          fw.TradeChanged += fw_TradeChanged;
          fw.TradeAdded += fw_TradeAdded;
          fw.OrderAdded += fw_OrderAdded;
          fw.Error += fw_Error;
        }
        foreach (var tm in TradingMacrosCopy)
          tm.CurrentLot = fw.GetTrades().Where(t => t.Pair == tm.Pair).Sum(t => t.Lots);
        MasterModel.CurrentLoss = CurrentLoss;
      } catch (Exception exc) { MessageBox.Show(exc + ""); }
    }

    void CoreFX_LoggedOffEvent(object sender, EventArgs e) {
      if (fw != null) {
        fw.TradeRemoved -= fw_TradeRemoved;
        fw.TradeChanged -= fw_TradeChanged;
        fw.TradeAdded -= fw_TradeAdded;
        fw.OrderAdded -= fw_OrderAdded;
        fw.Error -= fw_Error;
        fw.PriceChanged -= fw_PriceChanged;
      }
    }


    ThreadSchedulersDispenser ScanCorridorSchedulers = new ThreadSchedulersDispenser();
    private void ScanCorridor(string pair, IEnumerable<Rate> rates) {
      try {
        var tm = GetTradingMacro(pair);
        var corridorStats = rates.ScanCorridors(tm.Overlap.ToInt(), tm.CorridorIterations, tm.CorridorCalcMethod == Models.CorridorCalculationMethod.StDev);
        tm.Corridornes = tm.CorridorCalcMethod == Models.CorridorCalculationMethod.Density ? corridorStats.Density : 1 / corridorStats.Density;
        var updateStop = tm.CorridorMinutes > 0 && corridorStats.Minutes > tm.CorridorMinutes + 2;
        tm.CorridorMinutes = corridorStats.Minutes;// Lib.CMA(tm.CorridorMinutes, 0, 1, corridorMinutes).ToInt();
        SetLimitByBar(tm, corridorStats);
        if (updateStop)
          foreach (var trade in fw.GetTrades(pair))
            try {
              var newStop = GetStopByFractal(trade.Pair, rates.Take(rates.Count() - tm.OverlapTotal), trade.IsBuy);
              fw.FixOrderSetStop(trade.Id, newStop, "");
            } catch (Exception exc) { Log = exc; }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    ThreadSchedulersDispenser RunPriceSchedulers = new ThreadSchedulersDispenser();
    void fw_PriceChanged(Bars.Price Price) {
      if (!CanTrade(Price.Pair)) return;
      var pair = Price.Pair;
      CurrentRateAdd(pair, fw.ServerTime, Price.Ask, Price.Bid, false);
      ScanCorridorSchedulers.Run(pair, () => ScanCorridor(pair, ratesByPair[pair]));
      RunPriceSchedulers.Run(pair, () => RunPrice(Price));
    }

    private void RunPrice(Price price) {
      try {
        string pair = price.Pair;
        if (!CanTrade(pair)) return;
        if (!price.IsReal) price = fw.GetPrice(pair);
        var tm = GetTradingMacro(pair);
        if (tm == null) return;
        var trades = fw.GetTrades(tm.Pair);
        CheckProfit(trades);
        var tl = GetTickLoader(pair);
        if (!tl.IsRunning) tl.Run();
        var summary = fw.GetSummary(pair);
        var account = fw.GetAccount();
        tm.Net = summary != null ? summary.NetPL : (double?)null;
        tm.BalanceOnStop = account.Balance + tm.StopAmount.GetValueOrDefault();
        tm.BalanceOnLimit = account.Balance + tm.LimitAmount.GetValueOrDefault();
        SetLotSize(tm, account);
        ProcessPendingOrders(pair);
        if (!CheckProfitScheduler.IsRunning) 
          CheckProfitScheduler.Command = () => CheckProfit(account);
        var stopBuy = GetStopByFractal(pair, true);
        if (stopBuy != 0)
          tm.BuyStopByCorridor = fw.InPips(pair, price.Bid - stopBuy);
        var stopSell = GetStopByFractal(pair, false);
        if( stopSell!=0)
          tm.SellStopByCorridor = fw.InPips(pair, stopSell- price.Ask);
        foreach (var trade in trades) {
          CheckTrade(trade);
          //Dimok:Take it out of the loop
          UpdateEntryOrder(trade);
        }
        if (!OpenTradeByStopScheduler.IsRunning)
          OpenTradeByStopScheduler.Command = () => OpenTradeByStop(pair);
      } catch (Exception exc) { Log = exc; }
    }

    void CheckProfit(Trade[] trades) {
      try {
        var trade = trades.FirstOrDefault();
        if (trade == null) return;
        var tm = GetTradingMacro(trade.Pair);
        if (tm.Limit <= 0 || tm.TakeProfitPips <= 0) return;
        var pl = trades.Sum(t => t.PL);
        if (pl >= tm.TakeProfitPips)
          foreach (var t in trades)
            fw.CloseTradeAsync(t);
      } catch (Exception exc) { Log = exc; }
    }

    private bool CanTrade(Order order) {
      return CanTrade(order.Pair);
    }
    private bool CanTrade(Trade trade) {
      return CanTrade(trade.Pair);
    }
    private bool CanTrade(string pair) {
      return ratesByPair.ContainsKey(pair) && ratesByPair[pair].Count() > 0;
    }

    bool TradeExists(Trade trade) { return TradeExists(trade.Pair, trade.IsBuy); }
    bool TradeExists(string pair,bool isBuy) { return fw.GetTrades(pair).Any(t => t.IsBuy == isBuy); }
    ThreadScheduler OpenTradeByStopScheduler = new ThreadScheduler();
    void OpenTradeByStop(string pair) {
      var tm = GetTradingMacro(pair);
      if (tm.LotSize == 0 || !tm.ReverseOnProfit) return;
      //if (fw.GetTrades(pair).Count() > 0 || tm.BuyStopByCorridor == 0 || tm.SellStopByCorridor == 0) return;
      if (tm.BuyStopByCorridor == 0 || tm.SellStopByCorridor == 0) return;
      PendingOrder po = null; ;
      Action<object, FXW.RequestEventArgs> reqiesFailedAction = (s, e) => {
        if (po != null && e.ReqiestId == po.RequestId) {
          po = null;
          Log = new Exception(e.Error);
        }
      };
      Action<Order> orderRemovedAvtion = order => {
        var o = order.FixStatus;
      };
      var rfh = new EventHandler<FXW.RequestEventArgs>(reqiesFailedAction);
      var orh = new FXW.OrderRemovedEventHandler(orderRemovedAvtion);
      var fib = Lib.FibRatioSign(tm.BuyStopByCorridor, tm.SellStopByCorridor);
      bool? buy = fib.Between(-2, -1) ? true : fib.Between(1, 2) ? false : (bool?)null;
      if (buy.HasValue && !TradeExists(pair,buy.Value)) {
        try {
          fw.RequestFailed += rfh;
          fw.OrderRemoved += orh;
          po = OpenChainTrade(tm, buy.Value);
          var start = DateTime.Now;
          var stop = TimeSpan.FromSeconds(30);
          while (po != null && fw.GetTrades(pair).Count() == 0 && (DateTime.Now - start) < stop)
            Thread.Sleep(100);
        } catch (Exception exc) { Log = exc; 
        } finally {
          fw.RequestFailed -= rfh;
          fw.OrderRemoved -= orh;
        }
      }
    }

    ThreadScheduler CheckProfitScheduler = new ThreadScheduler();
    void CheckProfit(Account account) {
      try {
        var originalBalance = account.Balance - CurrentLoss;
        var profit = (account.Equity / originalBalance) - 1;
        if (profit > profitToClose) {
          var trades = fw.GetTrades("");
          try {
            fw.TradeRemoved -= fw_TradeRemoved;
            fw.CloseAllTrades();
          } finally {
            fw.TradeRemoved += fw_TradeRemoved;
          }

          foreach (var tm in TradingMacrosCopy)
            tm.CurrentLoss = tm.CurrentLot = 0;
          GlobalStorage.Context.SaveChanges();
        }
      } catch (Exception exc) { Log = exc; } 
    }

    void fw_TradeAdded(Trade trade) {
      if (!CanTrade(trade)) return;
      foreach (var trd in fw.GetTrades(trade.Pair).Where(t => t.Id != trade.Id))
        fw.CloseTradeAsync(trd);
      var tm = GetTradingMacro(trade.Pair);
      tm.CurrentLot = fw.GetTrades(trade.Pair).Sum(t => t.Lots);
      if (!tm.ReverseOnProfit)
        CreateEntryOrder(trade);
    }


    #region RunTrdadeChaned
    Dictionary<string, int> runCounters = new Dictionary<string, int>();
    int GetRunTrdadeChanedCounter(string key) {
      if (!runCounters.ContainsKey(key)) runCounters.Add(key,0);
      return runCounters[key];
    }
    int ChangeRunTradeChangedCounter(string key,int step) {
      if (!runCounters.ContainsKey(key)) runCounters.Add(key, 0);
      return runCounters[key] = runCounters[key] + step;
    }

    ThreadSchedulersDispenser TradeChangedSchedulers = new ThreadSchedulersDispenser();
    void fw_TradeChanged(object sender, FXW.TradeEventArgs e) {
      if (!CanTrade(e.Trade)) return;
      TradeChangedSchedulers.Run(e.Trade.Pair, () => RunTradeChanged(e));
    }

    private void RunTradeChanged(FXW.TradeEventArgs e) {
      var key = "RunTradeChanged:" + e.Trade.Pair;
      if (GetRunTrdadeChanedCounter(key) > 1) {
        Log = new Exception(key + " is busy(" + GetRunTrdadeChanedCounter(key) + ")");
        return;
      }
      try {
        ChangeRunTradeChangedCounter(key,1);
        if (e.Trade.IsParsed) return;
        var pair = e.Trade.Pair;
        if (waitingForStop.ContainsKey(pair)) {
          try {
            waitingForStop[pair]();
            waitingForStop.Remove(pair);
          } catch (Exception exc) {
            Log = exc;
            return;
          }
        }
      } catch (Exception exc) {
        Log = exc;
      } finally {
        ChangeRunTradeChangedCounter(key, -1);
      }
    }
    #endregion


    void fw_OrderAdded(object sender, FXW.OrderEventArgs e) {
      Order order = e.Order;
      if (!CanTrade(order)) return;
      if (order.IsEntryOrder) {
        var po = GetPendingFxOrder(order.Pair);
        if (po != null)
          new ThreadScheduler(TimeSpan.FromSeconds(5), ThreadScheduler.infinity, () => pendingFxOrders.Remove(po));
      }
    }

    void fw_Error(object sender, Order2GoAddIn.ErrorEventArgs e) {
      Log = e.Error;
    }



    void AdjustCurrentLosses(double profit) {
      if (profit <= 0) return;
      foreach (var tm in TradingMacrosCopy.Where(t => t.CurrentLoss < 0).OrderBy(t => t.CurrentLoss)) {
        tm.CurrentLoss = tm.CurrentLoss + profit;
        if (tm.CurrentLoss < 0) break;
        profit = tm.CurrentLoss;
        tm.CurrentLoss = 0;
      }
      MasterModel.CurrentLoss = Math.Min(0, CurrentLoss);
    }

    void fw_TradeRemoved(Trade trade) {
      if (!CanTrade(trade)) return;
      CleanEntryOrders();
      try {
        new ThreadScheduler(TimeSpan.FromSeconds(1), ThreadScheduler.infinity, () => {
          if (fw.GetTrades(trade.Pair).Length == 0) RemoveEntryOrder(trade.Pair);
        }, (s, e) => Log = e.Exception);
        var pair = trade.Pair;
        var tm = GetTradingMacro(pair);
        var totalGross = tm.CurrentLoss + trade.GrossPL;
        tm.CurrentLoss = Math.Min(0, totalGross);
        AdjustCurrentLosses(totalGross);
        tm.CurrentLot = fw.GetTrades(trade.Pair).Sum(t => t.Lots);
        tm.FreezStop = false;
        if (HasTradeToReverse(trade.Id))
          OpenChainTrade(tm, !trade.IsBuy);
      } catch (Exception exc) {
        Log = exc;
      }
      //fw.FixOrderOpen(trade.Pair, !trade.IsBuy, lot, limit, stop, trade.GrossPL < 0 ? trade.Id : "");
    }

    Models.TradingMacro[] ActiveTradingMacros { get { return TradingMacrosCopy.Where(tm => tm.LotSize > 0).ToArray(); } }
    private int CalculateLot(Models.TradingMacro tm) {
      var stopLoss = fw.GetTrades(tm.Pair).Sum(t => t.StopAmount);
      return CalculateLotCore(tm, CurrentLoss/ActiveTradingMacros.Length + stopLoss);
    }
    private int CalculateLotCore(Models.TradingMacro tm, double totalGross) {
      return fw.MoneyAndPipsToLot(totalGross.Abs(), tm.Limit, tm.Pair) + tm.LotSize;
    }
    #endregion

    #region Rate Loading
    void LoadTicks(string pair) {
      return;
      if (!IsLoggedIn) return;
      try {
        var ticks = fw.GetTicks(pair, 300);
        ticksByPair[pair] = ticks;
        var regress = Lib.Regress(ticks.Select(t => t.PriceAvg).ToArray(), 1);
        GetTradingMacro(pair).Angle = regress[1];
      } catch (Exception exc) {
        Log = exc;
      }
    }
    Dictionary<string, Rate> currentRates = new Dictionary<string, Rate>();
    Rate CurrentRate(string pair) {
      if (!currentRates.ContainsKey(pair))
        currentRates.Add(pair, new Rate());
      return currentRates[pair]; ;
    }
    void CurrentRateAdd(string pair,DateTime startDate, double ask, double bid, bool reset) {
      var rate = CurrentRate(pair);
      if (reset) rate.Count = 0;
      rate.AddTick(startDate, ask, bid);
    }

    void LoadRates(string pair) {
      var error = false;
      var tm = GetTradingMacro(pair);
      if (tm == null || !IsLoggedIn || tm.TradingRatio == 0) error = true;
      else
        try {
          var sw = Stopwatch.StartNew();
          var dateEnd = fw.ServerTime.Round();
          var rates = fw.GetBars(pair, 1, DateTime.MinValue);
          Debug.WriteLine("GetRates[" + pair + "]:{0:n2} sec", sw.ElapsedMilliseconds / 1000.0);
          var price = fw.GetPrice(pair);
          CurrentRateAdd(pair, fw.ServerTime, price.Ask, price.Bid, true);
          rates = rates.Skip(rates.Count - historyMinutesBack).Concat(new[] { CurrentRate(pair) }).ToList();
          ratesByPair[pair] = rates;
          FillOverlaps(pair, rates);
          tm.LastRateTime = rates.Max(r => r.StartDate);
          Debug.WriteLine("LoadRates[" + pair + "]:{0:n2} sec", sw.ElapsedMilliseconds / 1000.0);
        } catch (Exception exc) {
          error = true;
          Log = exc;
        }
      new ThreadScheduler(TimeSpan.FromSeconds(error ? 5 : 60),
        ThreadScheduler.infinity, () => LoadRates(pair), (s, e) => Log = e.Exception);
    }

    int MinutesBack(Models.TradingMacro tm) { return tm.CorridorMinutes; }

    private void FillOverlaps(string pair, IEnumerable<Rate> rates) {
      var ratesOverlap = rates.ToArray().Reverse().ToArray();
      ratesOverlap.FillOverlaps();
      var overlapAverage = ratesOverlap.Select(r => r.Overlap).Average();
      var tm = GetTradingMacro(pair);
      var highOverlapPeriod = 5;
      var mb = MinutesBack(tm);
      tm.Overlap = Math.Ceiling(overlapAverage.TotalMinutes).ToInt();
      tm.Overlap5 = Math.Ceiling(rates.ToArray().GetMinuteTicks(highOverlapPeriod).OrderBarsDescending().ToArray().FillOverlaps().Where(r => r.Overlap != TimeSpan.Zero).Select(r => r.Overlap).Average().TotalMinutes / highOverlapPeriod).ToInt();
      var trade = fw.GetTrades(pair).FirstOrDefault();
      if (trade != null && mb < MinutesBack(tm))
          fw.FixOrderSetStop(trade.Id, 0, "");
    }

    void SetLimitByBar(Models.TradingMacro tm,CorridorStatistics cs) {
      tm.Limit = fw.InPips(tm.Pair, cs.AskHigh - cs.BidLow).Round(1);
    }
    double GetCorridorByMinutesBack(Models.TradingMacro tm) {
      string pair = tm.Pair;
      var slack = GetSlack(pair);
      var ratesForLimit = GetRatesForCorridor(ratesByPair[pair], tm);
      return fw.InPips(pair, ratesForLimit.Max(r => r.AskHigh) - ratesForLimit.Min(r => r.BidLow)).Round(1);
    }
    #endregion

    #region Helpers


    #region Entry Orders
    Dictionary<string, Action> waitingForStop = new Dictionary<string, Action>();
    List<string> pendingFxOrders = new List<string>();
    bool HasPendingFxOrder(string pair) {
      return GetPendingFxOrder(pair) != null;
    }
    string GetPendingFxOrder(string pair) {
      return pendingFxOrders.SingleOrDefault(s => s == pair);
    }
    Order[] GetEntryOrders(string pair, bool isBuy) {
      return fw.GetOrders("").Where(o => o.Pair == pair && o.IsBuy == isBuy).OrderBy(o => o.OrderID).ToArray();
    }
    Order GetEntryOrder(Trade trade) { return GetEntryOrder(trade.Pair, !trade.IsBuy); }
    Order GetEntryOrder(string pair,bool isBuy) {
      var orders = GetEntryOrders(pair, isBuy);
      try {
        if (orders.Length > 1) fw.DeleteOrder(orders.First().OrderID);
      } catch (Exception exc) { Log = exc; }
      return GetEntryOrders(pair, isBuy).SingleOrDefault();
    }
    void RemoveEntryOrder(string pair) {
      try {
        foreach (var order in fw.GetOrders("").Where(o => o.Pair == pair))
          fw.DeleteOrder(order.OrderID);
      } catch (Exception exc) {
        Log = exc;
      }
    }
    double GetEntryOrderRate(Trade trade) {
      return trade.Stop.Round(fw.GetDigits(trade.Pair));
    }
    #region CreateEntryOrder
    void CreateEntryOrder(Trade trade) {
      try {
        string pair = trade.Pair;
        bool isBuy = !trade.IsBuy;
        CreateEntryOrder(pair, isBuy);
      } catch (Exception exc) { Log = exc; }
    }

    private void CreateEntryOrder(string pair, bool isBuy) {
      var order = GetEntryOrder(pair,isBuy);
      if (order == null) {
        var tm = GetTradingMacro(pair);
        if (!HasPendingFxOrder(pair)) {
          pendingFxOrders.Add(pair);
          Action openAction = () => fw.FixOrderOpenEntry(pair, isBuy, CalculateLot(tm), GetStopByFractal(pair, !isBuy), 0, 0, pair);
          try {
            openAction();
          } catch (Exception exc) {
            waitingForStop.Add(pair, openAction);
            Log = exc;
          }
        }
      }
    }
    #endregion

    #region UpdateEntryOrder
    /// <summary>
    /// Need this for parsed order that is not yet in orders table
    /// </summary>
    /// <param name="order"></param>
    private void UpdateEntryOrder(Trade trade) {
      try {
        var pair = trade.Pair;
        var tm = GetTradingMacro(pair);
        var order = GetEntryOrder(trade);
        if (order != null) {
          double rate = GetEntryOrderRate(trade);
          if (rate == 0) rate = GetStopByFractal(pair, trade.IsBuy);
          var period = fw.GetDigits(pair);
          var lot = CalculateLot(tm);
          if (order.Rate.Round(period) != rate) {
            if (order.Limit != 0)
              fw.DeleteEntryOrderLimit(order.OrderID);
            if (order.Stop != 0)
              fw.DeleteEntryOrderStop(order.OrderID);
            fw.ChangeOrderRate(order, rate);
          }
          if (order.Lot != lot)
            fw.ChangeOrderAmount(order.OrderID, lot);
          if (tm.FreezeType != Models.Freezing.None && order.Limit.Round(period).Abs() != tm.Limit.Round(period))
            fw.ChangeEntryOrderPeggedLimit(order.OrderID, tm.Limit.Round(period));
        }
      } catch (Exception exc) { Log = exc; }
    }
    #endregion
    #endregion

    #region OpenTrade
    PendingOrder OpenChainTrade(Models.TradingMacro tm, bool isBuy) {
      var lot = CalculateLot(tm);
      return OpenTrade(isBuy, tm.Pair, lot, tm.FreezeType == Models.Freezing.None ? 0 : tm.Limit, 0, GetStopByFractal(tm.Pair, isBuy), "");
    }


    private PendingOrder OpenTrade(bool buy, string pair, int lot, double limitInPips, double stopInPips, double stop, string remark) {
      var price = fw.GetPrice(pair);
      var limit = limitInPips == 0 ? 0 : buy ? price.Ask + fw.InPoints(pair, limitInPips) : price.Bid - fw.InPoints(pair, limitInPips);
      if (stop == 0 && stopInPips != 0)
        stop = buy ? price.Bid + fw.InPoints(pair, stopInPips) : price.Ask - fw.InPoints(pair, stopInPips);
      return fw.FixOrderOpen(pair, buy, lot, limit, stop, remark);
    }
    #endregion

    void ProcessPendingOrders(string pair) {
      var tm = GetTradingMacro(pair);
      tm.PendingSell = HasPendingOrder(pair,false);
      if (tm.PendingSell) {
        var pt = GetPendingTrade(pair, false);
        if (pt.Condition()) {
          RemovePendingOrder(pair, false);
          pt.OpenTradeCommand();
        }
      }
      tm.PendingBuy = HasPendingOrder(pair, true);
      if (tm.PendingBuy) {
        var pt = GetPendingTrade(pair, true);
        if (pt.Condition()) {
          RemovePendingOrder(pair, true);
          pt.OpenTradeCommand();
        }
      }
    }

    #region Get (stop/limit)
    private double GetStopByFractal(Trade trade) {
      return GetStopByFractal(trade.Pair, trade.IsBuy);
    }
    private double GetStopByFractal(string pair, bool isBuy) {
      return GetStopByFractal(pair, ratesByPair[pair], isBuy);
    }
    private double GetStopByFractal(string pair, IEnumerable<Rate> rates, bool isBuy) {
      return GetStopByFractal(0,pair, rates, isBuy);
    }
    double GetSlack(IEnumerable<Rate> rates) { return rates.Average(r => r.Spread); }
    double GetSlack(string pair) {
      var rates = ratesByPair[pair].Skip(ratesByPair[pair].Count - GetTradingMacro(pair).Overlap.ToInt());
      var slack = GetSlack(rates);
      GetTradingMacro(pair).SlackInPips = fw.InPips(pair, slack);
      return slack;
    }
    private double GetStopByFractal(double stopCurrent, string pair, IEnumerable<Rate> rates, bool isBuy) {
      if (!CanTrade(pair)) return 0;
      var stop = stopCurrent;
      try {
        if (rates.Count() > 0) {
          var tm = GetTradingMacro(pair);
          var stopSlack = GetSlack(pair);
          var ratesForStop = GetRatesForCorridor(rates, tm);
          ratesForStop = ratesForStop.OrderBarsDescending().Skip(tm.Overlap.ToInt()).ToArray();
          if (ratesForStop.Count() == 0) {
            Log = new Exception("Pair [" + pair + "] has no rates.");
          } else {
            stop = isBuy ? ratesForStop.Min(r => r.BidLow) - stopSlack : ratesForStop.Max(r => r.AskHigh) + stopSlack;
            var price = fw.GetPrice(pair);
            if (isBuy && stop >= price.Bid) stop = price.Bid - stopSlack;
            if (!isBuy && stop <= price.Ask) stop = price.Ask + stopSlack;
          }
        }
      } catch (Exception exc) {
        Log = exc;
      }
      return stop.Round(fw.GetDigits(pair));
    }

    private Rate[] GetRatesForCorridor(IEnumerable<Rate> rates, Models.TradingMacro tm) {
      return rates//.Where(r => r.Spread <= slack * 2)
        .Where(r => r.StartDate >= fw.ServerTime.AddMinutes(-MinutesBack(tm))).ToArray();
    }
    private double GetLimitByFractal(Trade trade, IEnumerable<Rate> rates) {
      string pair = trade.Pair;
      var tm = GetTradingMacro(pair);
      if (!CanTrade(trade))return 0;
      var digits = fw.GetDigits(pair);
      if(tm.FreezeType == Models.Freezing.None) {
        if (tm.TakeProfitPips == 0) return 0;
        return  (trade.Open + fw.InPoints(pair,trade.IsBuy ? tm.TakeProfitPips : -tm.TakeProfitPips)).Round(digits);
      }
      bool isBuy = trade.IsBuy;
      var slack = GetSlack(pair);
      var ratesForLimit = GetRatesForCorridor(ratesByPair[pair], tm);
      var price = fw.GetPrice(pair);
      var limit = isBuy ? Math.Max(trade.Open,ratesForLimit.Max(r => r.BidHigh)) + slack 
        : Math.Min(trade.Open, ratesForLimit.Min(r => r.AskLow)) - slack;
      if (isBuy && limit <= price.Bid) return 0;
      if (!isBuy && limit >= price.Ask) return 0;
      return limit.Round(digits);
    }
    double GetLimit(Trade trade) {
      var limitInPoints = fw.InPoints(trade.Pair, GetTradingMacro(trade.Pair).Limit);
      return Math.Round(trade.IsBuy ? trade.Open + limitInPoints : trade.Open - limitInPoints, fw.GetDigits(trade.Pair));
    }
    #endregion

    private void CheckTrade(Trade trade) {
      try {
        var tm = GetTradingMacro(trade.Pair);
        var round = fw.GetDigits(trade.Pair);
        var stopNew = GetStopByFractal(trade);
        var stopOld = trade.Stop.Round(round);
        if (!tm.FreezStop && stopNew != 0 && stopNew != stopOld)
          if (trade.Stop >= 0 || trade.IsBuy && stopNew > stopOld || !trade.IsBuy && stopNew < stopOld) {
            fw.FixCreateStop(trade.Id, trade.Stop = stopNew, "");
            UpdateEntryOrder(trade);
          }
        if (trade.Limit == 0 || tm.FreezeType == Models.Freezing.Float || tm.FreezeType == Models.Freezing.None && tm.TakeProfitPips>0) {
          var limitNew = GetLimitByFractal(trade, ratesByPair[trade.Pair]);
          if (limitNew != 0) {
            var limitOld = trade.Limit.Round(round);
            if (limitNew != limitOld)
              fw.FixCreateLimit(trade.Id, limitNew, "");
          }
        }
        //if (trade.Lots >= tm.LotSize * 10 && tm.CurrentLoss < 0 && trade.LimitAmount >= tm.CurrentLoss.Abs() * tm.LimitBar) tm.FreezLimit = true;
        if (trade.StopAmount > 0 && !tm.ReverseOnProfit) 
          RemoveEntryOrder(trade.Pair);
      } catch (Exception exc) { Log = exc; }
    }

    #region Child trade helpers
    void SetLotSize(Models.TradingMacro tm, Account account) {
      if (IsLoggedIn) {
        tm.LotSize = tm.TradingRatio >= 1 ? (tm.TradingRatio * 1000).ToInt() 
          : FXW.GetLotstoTrade(account.Balance, fw.Leverage(tm.Pair), tm.TradingRatio, fw.MinimumQuantity);
        var stopAmount = 0.0;
        var limitAmount = 0.0;
        foreach (var trade in fw.GetTrades(tm.Pair )) {
          stopAmount += trade.StopAmount;
          limitAmount += trade.LimitAmount;
        }
        tm.StopAmount = stopAmount;
        tm.LimitAmount = limitAmount;
      }
    }
    #endregion

    #region Init ...
    private void InitInstruments() {
//      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
      try {
        while (Instruments.Count > 0) Instruments.RemoveAt(0);
        fw.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
      } catch (Exception exc) {
        Log = exc;
      }
      RaisePropertyChangedCore("TradingMacros");
      //    }));
    }
    private void InitTradingMacro(Models.TradingMacro tm) {
      tm.PropertyChanged += TradingMacro_PropertyChanged;
      if (!ratesByPair.ContainsKey(tm.Pair)) {
        ratesByPair.Add(tm.Pair, new List<Rate>());
      }
      LoadRates(tm.Pair);
    }
    #endregion

    #endregion

  }
}
