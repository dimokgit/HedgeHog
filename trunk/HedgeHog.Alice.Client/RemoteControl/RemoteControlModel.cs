using System;
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
    //Dimok:Show Closed trades

    #region Settings
    readonly int historyMinutesBack = 60 * 5;
    readonly double profitToClose = 1;
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
          value.OrderToNoLoss += OrderToNoLossHandler;
          RaisePropertyChangedCore();
        }
      }
    }

    void OrderToNoLossHandler(object sender, FXW.OrderEventArgs e) {
      fw.DeleteEntryOrderLimit(e.Order.OrderID);
    }

    Dictionary<string, Corridors> charters = new Dictionary<string, Corridors>();
    Corridors GetCharter(string pair) {
      if (!charters.ContainsKey(pair)) {
        GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(new Action(() => {
          var charter = new Corridors(pair);
          charters.Add(pair, charter);
          App.ChildWindows.Add(charter);
          charter.Show();
        }));
      }
      return charters[pair];
    }

    public ObservableCollection<string> Instruments { get; set; }

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
    private IEnumerable<Models.TradingMacro> GetTradingMacrosByGroup(Models.TradingMacro tm) {
      return TradingMacrosCopy.Where(tm1 => tm1.TradingGroup == tm.TradingGroup);
    }
    Models.TradingMacro GetTradingMacro(string pair) {
      var tms = TradingMacrosCopy.Where(tm => tm.Pair == pair).ToArray();
      if (tms.Length == 0)
        new NullReferenceException("TradingMacro is null");
      return tms.FirstOrDefault();
    }

    public IQueryable<Models.TradingMacro> TradingMacros {
      get {
        try {
          return !IsInDesigh ? GlobalStorage.Context.TradingMacroes.OrderBy(tm => tm.TradingGroup).ThenBy(tm => tm.PairIndex) : new[] { new Models.TradingMacro() }.AsQueryable();
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
    List<Rate> GetRatesByPair(string pair) {
      if (!ratesByPair.ContainsKey(pair)) ratesByPair.Add(pair, new List<Rate>());
      return ratesByPair[pair];
    }
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
      public PendingTrade(string pair, bool isBuy, Func<bool> condition, Action openTradeCommand) {
        this.Pair = pair;
        this.IsBuy = isBuy;
        this.Condition = condition;
        this.OpenTradeCommand = openTradeCommand;
      }
    }
    List<PendingTrade> _pendingTrades = new List<PendingTrade>();
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
        fw.CloseTradesAsync(fw.GetTrades(pair));
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
        var tm = tradingMacro as Models.TradingMacro;
        if (MessageBox.Show("Buy " + tm.LotSizeByLoss.ToString("c0"), "Trade Confirmation", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
          OpenChainTrade(tm, true);
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
        OpenChainTrade(tradingMacro as Models.TradingMacro, false);
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
    List<Trade> ClosedTrades = new List<Trade>();
    public RemoteControlModel() {
      try {
        Instruments = new ObservableCollection<string>();
        if (!IsInDesigh) {
          GlobalStorage.Context.ObjectMaterialized += new ObjectMaterializedEventHandler(Context_ObjectMaterialized);
          GlobalStorage.Context.ObjectStateManager.ObjectStateManagerChanged += new System.ComponentModel.CollectionChangeEventHandler(ObjectStateManager_ObjectStateManagerChanged);
          App.container.SatisfyImportsOnce(this);
          fw = new FXW(MasterModel.CoreFX);
          MasterModel.CoreFX.LoggedInEvent += CoreFX_LoggedInEvent;
          MasterModel.CoreFX.LoggedOffEvent += CoreFX_LoggedOffEvent;
          foreach (var tradeString in File.ReadAllLines("ClosedTrades.xml").Where(s => !string.IsNullOrWhiteSpace(s))) {
            var trade = new Trade().FromString(tradeString);
            if (trade.TimeClose > DateTime.Now.AddDays(-14)) ClosedTrades.Add(trade);
          }
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
        var propsToHandle = Lib.GetLambdas(() => tm.Pair, () => tm.TradingRatio);
        if (propsToHandle.Contains(e.PropertyName)) SetLotSize(tm, fw.GetAccount());
        //if (e.PropertyName == Lib.GetLambda(() => tm.OverlapToStop)) LoadRates(tm.Pair);
        if (e.PropertyName == Lib.GetLambda(() => tm.CorridorBarMinutes))
          ratesByPair[tm.Pair].Clear();
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
          fw.TradeClosed += fw_TradeClosed;
        }
        foreach (var tm in TradingMacrosCopy) 
          tm.CurrentLot = fw.GetTrades().Where(t => t.Pair == tm.Pair).Sum(t => t.Lots);
        foreach (var tm in TradingMacrosCopy)
          LoadRates(tm.Pair);
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
        fw.TradeClosed += fw_TradeClosed;
      }
    }


    ThreadSchedulersDispenser ScanCorridorSchedulers = new ThreadSchedulersDispenser();
    private void ScanCorridor(string pair, List<Rate> rates) {
      Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
      if (rates.Count == 0) return;
      try {
        var price = GetCurrentPrice(pair);
        var tm = GetTradingMacro(pair);
        var trades = accountCached.Trades.Where(t => t.Pair == pair).ToArray();// fw.GetTrades(pair).OrderBy(t => t.Id).ToArray();
        var maxPL = trades.Select(t => t.PL).OrderBy(pl => pl).LastOrDefault();
        tm.CorridorHeighMinimum = maxPL > 0 ? tm.BarHeight60 : tm.BarHeight60;
        var corridornesses = rates.GetCorridornesses( tm.CorridorCalcMethod == Models.CorridorCalculationMethod.StDev);
        foreach (int i in tm.CorridorIterationsArray) {
          var csCurr = rates.ScanCorridornesses(i, corridornesses, tm.CorridornessMin, tm.CorridorHeighMinimum);
          var cs = tm.GetCorridorStats(csCurr.Iterations);
          cs.Init(csCurr.Density, csCurr.AverageHigh, csCurr.AverageLow, csCurr.AskHigh, csCurr.BidLow, csCurr.Periods, csCurr.EndDate, csCurr.StartDate, csCurr.Iterations);
          cs.FibMinimum = tm.CorridorFibMax(i-1);
          cs.InPips = d => fw.InPips(pair, d);
        }
        tm.CorridorStats = (from cs in tm.CorridorStatsArray
                            where cs.Height >= tm.CorridorHeighMinimum
                            orderby cs.CorridorFibAverage.Abs() // cs.Iterations
                            select cs
                            ).DefaultIfEmpty(tm.GetCorridorStats(0)).Last();
        tm.TradeDistance = fw.InPips(tm.Pair, tm.CorridorStats.Height);
        var takeProfitCS = tm.CorridorStatsArray.Where(cs => cs.Height * .9 > tm.CorridorStats.Height)
          .OrderBy(cs => cs.Height).DefaultIfEmpty(tm.GetCorridorStats(0)).First();
        tm.TakeProfitPips = Math.Ceiling(fw.InPips(tm.Pair, takeProfitCS.Height));
        #region Run Charter
        var charter = GetCharter(pair);
        new Scheduler(charter.Dispatcher, (s, e) => Log = e.Exception).Command = () => {
          var corridorStartDate = tm.CorridorStats.StartDate;
          if (false) {
            var ratesForChart = rates.GetMinuteTicks(1, true);
            Rate ratePrev = null;
            foreach (var rate in ratesForChart) {
              var startDate = rate.StartDate;
              if (ratePrev != null) rate.StartDate = ratePrev.StartDate.AddMinutes(-1);
              if (corridorStartDate == startDate) corridorStartDate = rate.StartDate;
              ratePrev = rate;
            }
          }
          price.Digits = fw.GetDigits(pair);
          var csFirst = tm.GetCorridorStats(0);
          var timeHigh = csFirst.StartDate;
          var timeCurr = tm.CorridorStatsArray.Count() > 1 ? tm.GetCorridorStats(-1).StartDate : DateTime.MinValue;
          var timeLow = tm.CorridorStatsArray.Count() > 2 ? tm.GetCorridorStats(-2).StartDate : DateTime.MinValue;
          charter.AddTicks(price, GetRatesByPair(pair), null,
            0, 0,
            csFirst.AskHigh, csFirst.BidLow,
            csFirst.AverageHigh, csFirst.AverageLow,
            timeHigh, timeCurr, timeLow,
            new double[0]);
        };
        #endregion
      } catch (Exception exc) {
        Log = exc;
      }
    }

    ThreadSchedulersDispenser ShowRatesSchedulers = new ThreadSchedulersDispenser();
    ThreadSchedulersDispenser RunPriceSchedulers = new ThreadSchedulersDispenser();

    class TicksPerPeriod {
      Dictionary<string, Queue<Price>> priceStackByPair = new Dictionary<string, Queue<Price>>();
      int maxCount;
      public TicksPerPeriod(int maxCount) {
        this.maxCount = maxCount;
      }
      private Queue<Price> GetQueue(string pair) {
        if (!priceStackByPair.ContainsKey(pair)) priceStackByPair.Add(pair, new Queue<Price>());
        return priceStackByPair[pair];
      }
      public double Add(Price price, DateTime serverTime) {
        var queue = GetQueue(price.Pair);
        if ((price.Time - serverTime).Duration() < TimeSpan.FromMinutes(1)) {
          if (queue.Count > maxCount) queue.Dequeue();
          queue.Enqueue(price);
        }
        var totalMinutes = (queue.Max(p => p.Time) - queue.Min(p => p.Time)).TotalMinutes;
        return queue.Count / Math.Max(1, totalMinutes);
      }
    }

    TicksPerPeriod ticksInst = new TicksPerPeriod(150);
    TicksPerPeriod ticksFast = new TicksPerPeriod(300);
    TicksPerPeriod ticksSlow = new TicksPerPeriod(600);
    void fw_PriceChanged(Bars.Price price) {
      var sw = Stopwatch.StartNew();
      if (price != null) pricesByPair[price.Pair] = price;
      var pair = price.Pair;
      var tm = GetTradingMacro(pair);
      if (tm != null) {
        tm.TicksPerMinuteSet(ticksInst.Add(price, fw.ServerTime), ticksFast.Add(price, fw.ServerTime), ticksSlow.Add(price, fw.ServerTime));
        AddCurrentTick(GetRatesByPair(pair), price);
        foreach (var cs in tm.CorridorStatsArray) {
          var rates = GetRatesByPair(pair);
          if (cs != null) {
            var ratesForStats = GetRatesForCorridor(rates, cs);
            cs.SetCorridorFib(
              fw.InPips(pair, price.Bid - ratesForStats.Min(r => r.BidLow)),
              fw.InPips(pair, ratesForStats.Max(r => r.AskHigh) - price.Ask),
              tm.TicksPerMinuteMinimum);
          }
        }
      }
      if (!CanTrade(price.Pair)) return;
      RunPriceSchedulers.Run(pair, () => RunPrice(pair));
      ScanCorridorSchedulers.Run(pair, () => {
        ScanCorridor(pair, GetRatesByPair(pair));
        OpenTradeByStop(pair);
      });
      CheckTradesLotSizeScheduler.Run(pair, () => CheckTradesLotSize(pair));
      if (sw.Elapsed > TimeSpan.FromSeconds(1))
        Log = new Exception("fw_PriceChanged took " + sw.Elapsed.TotalSeconds + " secods");
    }
    Dictionary<string, Price> pricesByPair = new Dictionary<string, Price>();
    Price GetCurrentPrice(string pair) {
      if (!IsLoggedIn) return new Price();
      if (!pricesByPair.ContainsKey(pair)) pricesByPair[pair] = fw.GetPrice(pair);
      return pricesByPair[pair];
    }
    Account accountCached = new Account();
    private void RunPrice(string pair) {
      var sw = Stopwatch.StartNew();
      try {
        if (!CanTrade(pair)) return;
        Price price = GetCurrentPrice(pair);
        if (!price.IsReal) price = fw.GetPrice(pair);
        var tm = GetTradingMacro(pair);
        if (tm == null) return;
        var account = accountCached = fw.GetAccount();
        if (sw.Elapsed > TimeSpan.FromSeconds(1))
          Log = new Exception("RunPrice2(" + pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
        var trades = account.Trades.Where(t => t.Pair == tm.Pair).ToArray();
        tm.TradesToHistory_Add(trades);
        tm.Positions = trades.Length;
        tm.Net = trades.Length > 0 ? trades.Sum(t => t.GrossPL) : (double?)null;
        tm.CurrentLossPercent = (tm.CurrentLoss + tm.Net.GetValueOrDefault()) / account.Balance;
        tm.BalanceOnStop = account.Balance + tm.StopAmount.GetValueOrDefault();
        tm.BalanceOnLimit = account.Balance + tm.LimitAmount.GetValueOrDefault();
        SetLotSize(tm, account);

        ProcessPendingOrders(pair);
        if (!CheckProfitScheduler.IsRunning)
          CheckProfitScheduler.Command = () => CheckProfit(account);
        CheckTrades(trades);
      } catch (Exception exc) { Log = exc; }
      if (sw.Elapsed > TimeSpan.FromSeconds(1))
        Log = new Exception("RunPrice(" + pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
      //Debug.WriteLine("RunPrice[{1}]:{0} ms", sw.Elapsed.TotalMilliseconds, pair);
    }

    void OpenTradeByStop(string pair) {
      var tm = GetTradingMacro(pair);
      if (tm == null) return;
      if (tm.CorridorStats.BuyStopByCorridor == 0 || tm.CorridorStats.SellStopByCorridor == 0) return;
      var buy = tm.CorridorStats.TradeSignal;
      var close = tm.CloseTrades;
      if (buy.HasValue && tm.CloseTrades.HasValue) {
        var trades = fw.GetTrades(pair);
        #region Close Trades
        if (close.HasValue) {
          var tradesToClose = trades.Where(t => t.IsBuy != close).ToArray();
          if (tradesToClose.Length > 0) {
            try {
              fw.CloseTrades(tradesToClose);
              tm.TradesToHistory_Clear();
            } catch (Exception exc) {
              Log = exc;
            } finally {
              RunPrice(pair);
            }
            return;
          }
        }
        #endregion
        #region Open Trade
        if (buy.HasValue) {
          var tradesInSameDirection = trades.Where(t => t.IsBuy == buy).ToArray();
          var maxPL = tradesInSameDirection.Length == 0 ? 0 : tradesInSameDirection.Max(t => t.PL);
          if (tm.ReverseOnProfit
              && tm.CorridorStats.IsCorridornessOk
              && tradesInSameDirection.Length < tm.MaximumPositions
              && (tradesInSameDirection.Length == 0 || maxPL < -tm.TradeDistance)
            ) {
            #region Pending Order Setup
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
            #endregion
            try {
              var currentCount = trades.Length;
              fw.RequestFailed += rfh;
              fw.OrderRemoved += orh;
              po = OpenChainTrade(tm, buy.Value);
              var start = DateTime.Now;
              var stop = TimeSpan.FromSeconds(30);
              while (po != null && fw.GetTrades(pair).Count() == currentCount && (DateTime.Now - start) < stop)
                Thread.Sleep(100);
            } catch (Exception exc) {
              Log = exc;
            } finally {
              fw.RequestFailed -= rfh;
              fw.OrderRemoved -= orh;
            }
          }
        }
        #endregion
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
      var tm = GetTradingMacro(trade.Pair);
      if (tm == null) return;
      tm.CurrentLot = fw.GetTrades(trade.Pair).Sum(t => t.Lots);
      if (false) {
        if (tm.ReverseOnProfit) {
          foreach (var trd in fw.GetTrades(trade.Pair).Where(t => t.Id != trade.Id && t.IsBuy != trade.IsBuy))
            fw.CloseTradeAsync(trd);
        } else
          CreateEntryOrder(trade);
      }
    }


    #region RunTrdadeChaned
    Dictionary<string, int> runCounters = new Dictionary<string, int>();
    int GetRunTrdadeChanedCounter(string key) {
      if (!runCounters.ContainsKey(key)) runCounters.Add(key, 0);
      return runCounters[key];
    }
    int ChangeRunTradeChangedCounter(string key, int step) {
      if (!runCounters.ContainsKey(key)) runCounters.Add(key, 0);
      return runCounters[key] = runCounters[key] + step;
    }

    ThreadSchedulersDispenser TradeChangedSchedulers = new ThreadSchedulersDispenser();
    void fw_TradeChanged(object sender, FXW.TradeEventArgs e) {
      if (!CanTrade(e.Trade)) return;
      TradeChangedSchedulers.Run(e.Trade.Pair, () => RunTradeChanged(e));
    }

    private void RunTradeChanged(FXW.TradeEventArgs e) {
      try {
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
      }
    }
    #endregion


    void fw_OrderAdded(object sender, FXW.OrderEventArgs e) {
      Order order = e.Order;
      if (order.IsEntryOrder) {
        var po = GetPendingFxOrder(order.Pair);
        if (po != null)
          new ThreadScheduler(TimeSpan.FromSeconds(5), ThreadScheduler.infinity, () => pendingFxOrders.Remove(po));
      }
    }

    void fw_Error(object sender, Order2GoAddIn.ErrorEventArgs e) {
      Log = e.Error;
    }



    void AdjustCurrentLosses(Models.TradingMacro tradingMacro) {
      var tmGroup = GetTradingMacrosByGroup(tradingMacro);
      double profit = tmGroup.Where(tm => tm.CurrentLoss > 0).Sum(tm => tm.CurrentLoss);
      foreach (var tm in tmGroup.Where(t => t.CurrentLoss < 0).OrderBy(t => t.CurrentLoss)) {
        if (profit <= 0) break;
        tm.CurrentLoss = tm.CurrentLoss + profit;
        profit = tm.CurrentLoss;
      }
      //new ThreadScheduler(TimeSpan.FromSeconds(3), ThreadScheduler.infinity, () =>
      GetTradingMacrosByGroup(tradingMacro).Where(tm => tm.CurrentLoss > 0).ToList()
      .ForEach(tm => tm.CurrentLoss = 0);
      //);
      MasterModel.CurrentLoss = Math.Min(0, CurrentLoss);
    }

    void fw_TradeClosed(object sender, FXW.TradeEventArgs e) {
      try {
        System.IO.File.AppendAllText("ClosedTrades.xml", Environment.NewLine + e.Trade);
      } catch (Exception exc) { Log = exc; }
    }

    void fw_TradeRemoved(Trade trade) {
      //CleanEntryOrders();
      try {
        new ThreadScheduler(TimeSpan.FromSeconds(1), ThreadScheduler.infinity, () => {
          if (fw.GetTrades(trade.Pair).Length == 0) RemoveEntryOrder(trade.Pair);
        }, (s, e) => Log = e.Exception);
        var pair = trade.Pair;
        var tm = GetTradingMacro(pair);
        if (tm == null) return;
        tm.CurrentLoss = tm.CurrentLoss + trade.GrossPL;
        AdjustCurrentLosses(tm);
        tm.CurrentLot = fw.GetTrades(trade.Pair).Sum(t => t.Lots);
        if (HasTradeToReverse(trade.Id))
          OpenChainTrade(tm, !trade.IsBuy);
      } catch (Exception exc) {
        Log = exc;
      }
      //fw.FixOrderOpen(trade.Pair, !trade.IsBuy, lot, limit, stop, trade.GrossPL < 0 ? trade.Id : "");
    }

    private int CalculateLot(Models.TradingMacro tm) {
      Func<int, int> returnLot = d => Math.Max(tm.LotSize, d);
      if (tm.FreezeStopType == Models.Freezing.Freez)
        return returnLot(fw.GetTrades(tm.Pair).Sum(t => t.Lots) * 2);
      var currentLoss = GetCurrentLossByGroup(tm);
      var grossPL = GetGrossPLByGroup(tm);
      return returnLot(CalculateLotCore(tm, currentLoss + grossPL));
    }
    double GetGrossPLByGroup(Models.TradingMacro tm) {
      return GetTradingMacrosByGroup(tm).Sum(tm1 => fw.GetTrades(tm1.Pair).Sum(t => t.GrossPL));
    }
    private double GetCurrentLossByGroup(Models.TradingMacro tm) {
      return GetTradingMacrosByGroup(tm).Sum(tm1 => tm1.CurrentLoss);
    }
    private int CalculateLotCore(Models.TradingMacro tm, double totalGross) {
      return fw.MoneyAndPipsToLot(Math.Min(0, totalGross).Abs(), tm.TakeProfitPips, tm.Pair);
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

    Dictionary<string, double> Correlations = new Dictionary<string, double>();

    void RunCorrelations() {
      var currencies = new List<string>();
      foreach (var tm in TradingMacrosCopy.Where(t => t.LotSize > 0))
        currencies.AddRange(tm.Pair.Split('/'));
      currencies = currencies.Distinct().ToList();
      foreach (var currency in currencies)
        Correlations[currency] = RunCorrelation(currency);
    }

    private double RunCorrelation(string currency) {
      Func<string, double[]> getRatesForCorrelation = pair =>
        GetRatesForCorridor(GetTradingMacro(pair)).Select(r => r.PriceAvg).ToArray();
      var correlations = new List<double>();
      var pairs = TradingMacrosCopy.Where(tm => tm.LotSize > 0 && tm.Pair.Contains(currency)).Select(tm => tm.Pair).ToArray();
      if (pairs.Length == 0) return 0;
      foreach (var pair in pairs) {
        var price1 = getRatesForCorrelation(pair);
        foreach (var pc in pairs.Where(p => p != pair)) {
          var price2 = getRatesForCorrelation(pc);
          correlations.Add(alglib.correlation.pearsoncorrelation(ref price1, ref price2, Math.Min(price1.Length, price2.Length)).Abs());
        }
      }
      return correlations.Count > 0 ? correlations.Average() : 0;
    }
    void AddCurrentTick(List<Rate> rates, Price price) {
      if (rates == null || rates.Count == 0) return;
      var priceTime = price.Time.Round();
      if (priceTime > rates.Last().StartDate)
        rates.Add(new Rate(priceTime, price.Ask, price.Bid, false));
      else rates.Last().AddTick(priceTime, price.Ask, price.Bid);
    }
    void LoadRates(string pair) {
      var error = false;
      var tm = GetTradingMacro(pair);
      var rates = GetRatesByPair(pair);
      if (rates.Count > 0 && !IsLoggedIn) {
        MasterModel.CoreFX.LogOn();
      }
      if (tm == null || !IsLoggedIn || tm.TradingRatio == 0) error = true;
      else
        try {
          Debug.WriteLine("LoadRates({0}) @ {1:HH:mm:ss}", pair, fw.ServerTime);
          var sw = Stopwatch.StartNew();
          var firstTradeDate = accountCached.Trades.Where(t => t.Pair == pair).OrderBy(t => t.Id).Select(t => t.Time).DefaultIfEmpty(fw.ServerTime).First();
          var minutesBack = Math.Max(tm.CorridorBarMinutes * historyMinutesBack, (fw.ServerTime - firstTradeDate).TotalMinutes).ToInt();
          rates = rates.Take(rates.Count - 2).ToList();
          if (rates.Count == 0)
            rates = fw.GetBarsBase(pair, 1, minutesBack).ToList();
          fw.GetBars(pair, 1, fw.ServerTime.Round().AddMinutes(-minutesBack), DateTime.FromOADate(0), ref rates);
          if (sw.Elapsed > TimeSpan.FromSeconds(5))
            Debug.WriteLine("GetRates[" + pair + "]:{0:n2} sec", sw.ElapsedMilliseconds / 1000.0);
          rates.RemoveRange(0, Math.Max(0, rates.Count - minutesBack));
          ratesByPair[pair] = rates;
          FillOverlaps(pair, rates);
          tm.LastRateTime = rates.Max(r => r.StartDate);
          sw.Stop();
          RunCorrelations();
          foreach (var correlation in Correlations)
            foreach (var tmc in TradingMacrosCopy)
              tmc.SetCorrelation(correlation.Key, correlation.Value);
          //Debug.WriteLine("Correlation:{0} - {1:n1}", correlation.Key, correlation.Value);
          //fw_PriceChanged(price);
          var rates60 = rates.GetMinuteTicks(tm.LimitBar);
          var hs = rates60.Select(r => r.AskHigh - r.BidLow).ToArray();
          var hsAvg = hs.Average();
          tm.BarHeight60 = hs.Where(h => h > hsAvg).Average();
          tm.BarHeight60InPips = fw.InPips(pair, tm.BarHeight60);
          if (sw.Elapsed > TimeSpan.FromSeconds(2))
            Debug.WriteLine("LoadRates[" + pair + "]:{0:n2} sec", sw.ElapsedMilliseconds / 1000.0);
          //ScanCorridorSchedulers.Run(pair, () => { ScanCorridor(pair, GetRatesByPair(pair)); });
        } catch (Exception exc) {
          error = true;
          Log = exc;
        }
      if (!timersByPair.ContainsKey(pair)) {
        var dueTime = fw.ServerTime.Round().AddMinutes(1) - fw.ServerTime;
        timersByPair.Add(pair,new Timer((p) => LoadRates(p + ""), pair, dueTime, TimeSpan.FromMinutes(1)));
      }
      //new ThreadScheduler(TimeSpan.FromSeconds(error ? 5 : 60), ThreadScheduler.infinity, () => LoadRates(pair), (s, e) => Log = e.Exception);
    }

    Dictionary<string, Timer> timersByPair = new Dictionary<string, Timer>();

    private void FillOverlaps(string pair, IEnumerable<Rate> rates) {
      var ratesOverlap = rates.ToArray().Reverse().ToArray();
      ratesOverlap.FillOverlaps(TimeSpan.FromMinutes(1));
      var overlapAverage = ratesOverlap.Select(r => r.Overlap).Average();
      var tm = GetTradingMacro(pair);
      var highOverlapPeriod = 5;
      tm.Overlap = Math.Ceiling(overlapAverage.TotalMinutes).ToInt();
      tm.Overlap5 = Math.Ceiling(rates.ToArray().GetMinuteTicks(highOverlapPeriod).OrderBarsDescending().ToArray().FillOverlaps(TimeSpan.FromMinutes(highOverlapPeriod)).Where(r => r.Overlap != TimeSpan.Zero).Select(r => r.Overlap).Average().TotalMinutes / highOverlapPeriod).ToInt();
    }

    #endregion

    #region Helpers


    #region CanTrade
    private bool CanTrade(Order order) {
      return CanTrade(order.Pair);
    }
    private bool CanTrade(Trade trade) {
      return CanTrade(trade.Pair);
    }
    private bool CanTrade(string pair) {
      return ratesByPair.ContainsKey(pair) && ratesByPair[pair].Count() > 0;
    }
    #endregion

    #region TradeExists
    bool TradeExists(Trade[] trades, Func<Trade, bool> condition) {
      if (trades.Length == 0) return false;
      return TradeExists(trades, trades[0].Pair, trades[0].IsBuy, condition);
    }
    bool TradeExists(Trade[] trades, string pair, bool isBuy, Func<Trade, bool> condition) {
      return trades.Any(t => t.IsBuy == isBuy && condition(t));
    }
    #endregion

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
    Order GetEntryOrder(string pair, bool isBuy) {
      var orders = GetEntryOrders(pair, isBuy);
      try {
        if (orders.Length > 1) fw.DeleteOrder(orders.First().OrderID);
      } catch (Exception exc) { Log = exc; }
      return GetEntryOrders(pair, isBuy).SingleOrDefault();
    }
    void ResetEntryOrder(Trade trade) {
      var order = GetEntryOrder(trade);
      if (order != null) order.Limit = 0;
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
    double GetEntryOrderLimit(Trade[] trades, int lot) {
      if (trades.Length == 0) return 0;
      var tm = GetTradingMacro(trades[0].Pair);
      var addProfit = tm.FreezeStopType == Models.Freezing.Float;
      var loss = GetCurrentLossByGroup(tm);
      return Static.GetEntryOrderLimit(fw, trades, lot, addProfit, loss);
    }
    #region CreateEntryOrder

    private void CreateEntryOrder(Trade trade) {
      string pair = trade.Pair;
      bool isBuy = !trade.IsBuy;
      var order = GetEntryOrder(pair, isBuy);
      if (order == null) {
        var tm = GetTradingMacro(pair);
        if (tm.ReverseOnProfit) return;
        if (!HasPendingFxOrder(pair)) {
          pendingFxOrders.Add(pair);
          Action openAction = () => {
            var trd = fw.GetTrade(trade.Id);
            if (trd == null) return;
            var rate = GetEntryOrderRate(trd);
            var lot = CalculateLot(tm);
            fw.FixOrderOpenEntry(pair, isBuy, lot, rate, 0, 0, pair);
          };
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
    private void UpdateEntryOrder(Trade[] trades) {
      try {
        if (trades.Length == 0) return;
        var trade = trades.OrderBy(t => t.Id).Last();
        var pair = trade.Pair;
        var tm = GetTradingMacro(pair);
        var order = GetEntryOrder(trade);
        if (order != null) {
          double rate = GetEntryOrderRate(trade);
          if (rate == 0) return;
          var period = fw.GetDigits(pair);
          var lot = CalculateLot(tm);
          if (order.Rate.Round(period) != rate) {
            if (order.Limit != 0)
              fw.DeleteEntryOrderLimit(order.OrderID);
            if (order.TypeStop == 1 && order.Stop != 0)
              fw.DeleteEntryOrderStop(order.OrderID);
            fw.ChangeOrderRate(order, rate);
          }
          if (order.Lot != lot) {
            fw.ChangeOrderAmount(order.OrderID, lot);
            order.Limit = 0;
          }
          if (order.Limit == 0) {
            var limit = GetEntryOrderLimit(trades, order.Lot).Round(period).Abs();
            fw.ChangeEntryOrderPeggedLimit(order.OrderID, limit);
          }
        }
      } catch (Exception exc) { Log = exc; }
    }
    #endregion
    #endregion

    #region OpenTrade
    PendingOrder OpenChainTrade(Models.TradingMacro tm, bool isBuy) {
      var lot = CalculateLot(tm);
      if (lot == 0) return null;
      var stop = tm.FreezeStopType == Models.Freezing.None ? 0 : GetStopByFractal(tm.Pair, isBuy, fw.ServerTime);
      return OpenTrade(isBuy, tm.Pair, lot, tm.TakeProfitPips, 0, stop, "");
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
      tm.PendingSell = HasPendingOrder(pair, false);
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
      return GetStopByFractal(trade.Pair, trade.IsBuy, trade.Time);
    }
    private double GetStopByFractal(string pair, bool isBuy, DateTime tradeDate) {
      return GetStopByFractal(pair, ratesByPair[pair], isBuy, tradeDate);
    }
    private double GetStopByFractal(string pair, IEnumerable<Rate> rates, bool isBuy, DateTime tradeDate) {
      return GetStopByFractal(0, pair, rates, isBuy, tradeDate);
    }
    private double GetStopByFractal(double stopCurrent, string pair, IEnumerable<Rate> rates, bool isBuy, DateTime tradeDate) {
      if (!CanTrade(pair)) return 0;
      var stop = stopCurrent;
      var round = fw.GetDigits(pair);
      try {
        if (rates.Count() > 0) {
          var tm = GetTradingMacro(pair);
          var stopSlack = GetFibSlack(tm.CorridorStats.FibMinimum, tm).Round(round);
          var ratesForStop = GetRatesForCorridor(rates, tm);
          var skip = Math.Min(tm.OverlapTotal, Math.Floor((fw.ServerTime - tradeDate).TotalMinutes)).ToInt();
          ratesForStop = ratesForStop.OrderBarsDescending().Skip(skip).ToArray();
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
      return stop.Round(round);
    }

    private Rate[] GetRatesForCorridor(Models.TradingMacro tm) {
      return GetRatesForCorridor(ratesByPair[tm.Pair], tm);
    }
    private Rate[] GetRatesForCorridor(IEnumerable<Rate> rates, Models.TradingMacro tm) {
      if (tm.CorridorStats == null) return rates.ToArray();
      return GetRatesForCorridor(rates, tm.CorridorStats.StartDate);
    }
    private Rate[] GetRatesForCorridor(IEnumerable<Rate> rates, CorridorStatistics cs) {
      return GetRatesForCorridor(rates, cs.StartDate);
    }
    private Rate[] GetRatesForCorridor(IEnumerable<Rate> rates, DateTime startDate) {
      var rts = rates//.Where(r => r.Spread <= slack * 2)
        .Where(r => r.StartDate >= startDate).ToArray();
      return rts;
    }
    private double GetLimitByFractal(Trade[] trades, Trade trade, IEnumerable<Rate> rates) {
      string pair = trade.Pair;
      bool isBuy = trade.IsBuy;
      var tm = GetTradingMacro(pair);
      if (!CanTrade(trade)) return 0;
      var ratesForLimit = GetRatesForCorridor(ratesByPair[pair], tm);
      var digits = fw.GetDigits(pair);
      double limit = 0;
      Func<double> returnLimit = () => limit.Round(digits);
      if (tm.CorridorStats == null) return 0;
      switch (tm.FreezeType) {
        case Models.Freezing.Float:
        case Models.Freezing.Freez:
          var leg = GetFibSlack(tm.CorridorStats.FibMinimum, tm);
          limit = isBuy ? tm.CorridorStats.AskHigh - leg : tm.CorridorStats.BidLow + leg;
          return returnLimit();
        case Models.Freezing.None:
          limit = isBuy ? tm.CorridorStats.AskHigh + tm.CorridorStats.Height : tm.CorridorStats.BidLow - tm.CorridorStats.Height;
          return returnLimit();
        default:
          var slack = GetFibSlack(tm.CorridorStats.FibMinimum, tm);
          var price = fw.GetPrice(pair);
          limit = isBuy ? Math.Max(trade.Open, ratesForLimit.Max(r => r.BidHigh)) + slack
            : Math.Min(trade.Open, ratesForLimit.Min(r => r.AskLow)) - slack;
          if (isBuy && limit <= price.Bid) return 0;
          if (!isBuy && limit >= price.Ask) return 0;
          return limit.Round(digits);
      }
      if (tm.FreezeType != Models.Freezing.Freez) {
        if (tm.TakeProfitPips == 0) return 0;
        return (trade.Open + fw.InPoints(pair, isBuy ? tm.TakeProfitPips : -tm.TakeProfitPips)).Round(digits);
      }
    }

    #region GetSlack
    private double GetFibSlack(double fib, Models.TradingMacro tm) {
      var slack = fib.FibReverse().YofS(tm.CorridorStats.Height);
      tm.SlackInPips = fw.InPips(tm.Pair, slack);
      return slack;
    }
    #endregion
    #endregion

    void ChangeTradeStop(Trade trade, double stopAbsolute) {
      fw.FixOrderSetStop(trade.Id, trade.Stop = stopAbsolute, "");
    }
    private void CheckTrades(Trade[] trades) {
      try {
        var stopAmount = trades.Sum(t => t.StopAmount);
        foreach (var trade in trades)
          CheckTrade(trades, trade, stopAmount);
        UpdateEntryOrder(trades);
      } catch (Exception exc) { Log = exc; }
    }

    ThreadSchedulersDispenser CheckTradesLotSizeScheduler = new ThreadSchedulersDispenser();
    void CheckTradesLotSize(string pair) {
      Trade[] trades = fw.GetTrades(pair).OrderByDescending(t=>t.Lots).ToArray();
      if( trades.Length == 0)return;
      var tm = GetTradingMacro(pair);
      var lotSizeCalc = CalculateLot(tm);
      var lotSizeCurr = trades.Sum(t => t.Lots);
      if (lotSizeCurr / lotSizeCalc < 5) return;
      var lotSizeRemove = lotSizeCurr - lotSizeCalc; 
      while (lotSizeRemove>0) {
        foreach (var trade in trades) {
          if (trade.Lots >= lotSizeRemove) {
            fw.CloseTrade(trade,lotSizeRemove);
            return;
          }
          lotSizeRemove -= trade.Lots;
          fw.CloseTrade(trade);
        }
        trades = fw.GetTrades(pair).OrderByDescending(t => t.Lots).ToArray();
      }
    }
    private void CheckTrade(Trade[] trades, Trade trade, double stopAmount) {
      var tm = GetTradingMacro(trade.Pair);
      var round = fw.GetDigits(trade.Pair) - 1;
      if (tm.FreezeStopType != Models.Freezing.None) {
        var stopNew = GetStopByFractal(trade).Round(round);
        var stopOld = trade.Stop.Round(round);
        if (DoStop(trade, tm, stopNew, round))
          ChangeTradeStop(trade, stopNew);
      }
      var limitNew = GetLimitByFractal(trades, trade, ratesByPair[trade.Pair]).Round(round);
      if (limitNew != 0 &&(
        trade.IsBuy && limitNew <= GetCurrentPrice(trade.Pair).Bid ||
         !trade.IsBuy && limitNew >= GetCurrentPrice(trade.Pair).Ask))
        fw.CloseTradeAsync(trade);
      else if (DoLimit(trade, tm, limitNew, round))
        fw.FixCreateLimit(trade.Id, limitNew, "");
      //if (trade.Lots >= tm.LotSize * 10 && tm.CurrentLoss < 0 && trade.LimitAmount >= tm.CurrentLoss.Abs() * tm.LimitBar) tm.FreezLimit = true;
      if (stopAmount > 0 && tm.CurrentLoss == 0)
        RemoveEntryOrder(trade.Pair);
      if (stopAmount < 0 && tm.FreezeStopType == Models.Freezing.Float)
        CreateEntryOrder(trade);
    }

    private bool DoLimit(Trade trade, Models.TradingMacro tm, double limitNew, int round) {
      if (trade.IsBuy && limitNew <= GetCurrentPrice(trade.Pair).Ask) return false;
      if (!trade.IsBuy && limitNew >= GetCurrentPrice(trade.Pair).Bid) return false;
      if (trade.Limit == 0) return true;
      if (tm.FreezeType == Models.Freezing.Freez && trade.Limit != 0) return false;
      if (trade.Limit.Round(round) == limitNew.Round(round)) return false;
      return true;
    }

    private bool DoStop(Trade trade, Models.TradingMacro tm, double stopNew, int round) {
      if (tm.FreezeStopType == Models.Freezing.None) return false;
      var stopOld = trade.Stop.Round(round);
      stopNew = stopNew.Round(round);
      if (stopOld == stopNew) return false;
      if (tm.FreezeStopType == Models.Freezing.Freez && stopOld != 0) return false;
      return trade.Stop == 0 || trade.IsBuy && stopNew > stopOld || !trade.IsBuy && stopNew < stopOld;
    }

    #region Child trade helpers
    void SetLotSize(Models.TradingMacro tm, Account account) {
      if (IsLoggedIn) {
        tm.LotSize = tm.TradingRatio >= 1 ? (tm.TradingRatio * 1000).ToInt()
          : FXW.GetLotstoTrade(account.Balance, fw.Leverage(tm.Pair), tm.TradingRatio, fw.MinimumQuantity);
        tm.LotSizePercent = tm.LotSize / account.Balance / fw.Leverage(tm.Pair);
        tm.LotSizeByLoss = CalculateLot(tm);
        //Math.Max(tm.LotSize, FXW.GetLotSize(Math.Ceiling(tm.CurrentLossPercent.Abs() / tm.LotSizePercent) * tm.LotSize, fw.MinimumQuantity));
        var stopAmount = 0.0;
        var limitAmount = 0.0;
        foreach (var trade in account.Trades.Where(t => t.Pair == tm.Pair)) {
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
        if (Instruments.Count == 0)
          fw.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
      } catch (Exception exc) {
        Log = exc;
      }
      RaisePropertyChangedCore("TradingMacros");
      //    }));
    }
    private void InitTradingMacro(Models.TradingMacro tm) {
      if (!ratesByPair.ContainsKey(tm.Pair)) {
        tm.PropertyChanged += TradingMacro_PropertyChanged;
        tm.HistoricalGrossPL = ClosedTrades.Where(t => t.Pair == tm.Pair).Sum(t => t.GrossPL).ToInt();
        ratesByPair.Add(tm.Pair, new List<Rate>());
        foreach (var i in tm.CorridorIterationsArray)
          tm.CorridorStatsArray.Add(new CorridorStatistics(tm) { Iterations = i });
      }
    }
    #endregion

    #endregion

  }
}
