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
          return !IsInDesigh ? GlobalStorage.Context.TradingMacroes.OrderBy(tm=>tm.TradingGroup).ThenBy(tm=>tm.PairIndex) : new[] { new Models.TradingMacro() }.AsQueryable();
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
        Instruments = new ObservableCollection<string>();
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
    private void ScanCorridor(string pair, List<Rate> rates) {
      if (rates.Count == 0) return;
      try {
        var trades = fw.GetTrades(pair).OrderBy(t => t.Id).ToArray();
        var tm = GetTradingMacro(pair);
        var mb = tm.CorridorStats == null ? DateTime.MinValue : tm.CorridorStats.StartDate;
        tm.CorridorIterationsTrade = trades.Length == 0 || trades.Max(t => t.PL) > 0 ? tm.CorridorIterationsOut : tm.CorridorIterationsOut - 1;
        tm.CorridorIterationsTrade = tm.CorridorIterationsOut;
        tm.CorridorStatsForTradeDistance = rates.ScanCorridors(tm.Overlap.ToInt(), tm.CorridorIterationsIn,0, tm.CorridorCalcMethod == Models.CorridorCalculationMethod.StDev);
        var limitMinimum = tm.CorridorStatsForTradeDistance.Heigth / tm.CorridorRatio;
        tm.CorridorStats = rates.ScanCorridors(tm.Overlap.ToInt(), tm.CorridorIterationsTrade,limitMinimum, tm.CorridorCalcMethod == Models.CorridorCalculationMethod.StDev);
        var ratesForCorridor = GetRatesForCorridor(ratesByPair[pair], tm);
        var askHigh = ratesForCorridor.Max(r => r.AskHigh);
        var bidLow = ratesForCorridor.Min(r => r.BidLow);
        tm.Limit = tm.TradeDistance = tm.TakeProfitPips = fw.InPips(tm.Pair, tm.CorridorStats.Heigth).Round(1);
        //tm.TakeProfitPips = fw.InPips(tm.Pair, tm.CorridorStatsForTradeDistance.Heigth).Round(1);
        var updateStop = false;// mb > 0 && tm.MinutesBack > mb + 3;
        if (tm.FreezeStopType != Models.Freezing.Freez && updateStop) {
          if (trades.Length > 0) {
            var lastTradeDate = trades.Max(t => t.Time);
            foreach (var trade in trades) {
              try {
                var newStop = GetStopByFractal(trade.Pair, rates.Where(r => r.StartDate < mb), trade.IsBuy, lastTradeDate);
                ChangeTradeStop(trade, newStop);
              } catch (Exception exc) { Log = exc; }
            }
          }
        }
          var charter = GetCharter(pair);
          ShowRatesSchedulers.Run(pair, () => {
            new Scheduler(charter.Dispatcher).Command = () => {
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
              var price = GetCurrentPrice(pair);
              price.Digits = fw.GetDigits(pair);
              charter.AddTicks(price, rates, null, 0, 0, 0, 0, tm.CorridorStats.AverageHigh, tm.CorridorStats.AverageLow, corridorStartDate, tm.CorridorStatsForTradeDistance.StartDate, new double[0]);
            };
          });
      } catch (Exception exc) {
        Log = exc;
      }
    }

    ThreadSchedulersDispenser ShowRatesSchedulers = new ThreadSchedulersDispenser();
    ThreadSchedulersDispenser RunPriceSchedulers = new ThreadSchedulersDispenser();
    Dictionary<string, Queue<Price>> priceStackByPair = new Dictionary<string, Queue<Price>>();

    void PriceStackAdd(Price price) {
      if ((price.Time - fw.ServerTime).Duration() > TimeSpan.FromMinutes(1)) {
        Log = new Exception("Price is out of range. Price.Time=" + price.Time);
        return;
      }
      var queue = PriceStackGet(price.Pair);
      if (queue.Count > 300) queue.Dequeue();
      queue.Enqueue(price);
      var totalMinutes = (queue.Max(p => p.Time) - queue.Min(p => p.Time)).TotalMinutes;
      GetTradingMacro(price.Pair).TicksPerMinute = queue.Count / Math.Max(1, totalMinutes);
    }

    private Queue<Price> PriceStackGet(string pair) {
      if (!priceStackByPair.ContainsKey(pair)) priceStackByPair.Add(pair, new Queue<Price>());
      return priceStackByPair[pair];
    }
    void fw_PriceChanged(Bars.Price price) {
      var pair = price.Pair;
      var tm = GetTradingMacro(pair);
      if (tm != null) {
        PriceStackAdd(price);
        AddCurrentTick(GetRatesByPair(pair), price);
        ScanCorridorSchedulers.Run(pair, () => ScanCorridor(pair, GetRatesByPair(pair)));
      }
      if (!CanTrade(price.Pair)) return;
      RunPriceSchedulers.Run(pair, () => RunPrice(price));
    }
    Dictionary<string, Price> pricesByPair = new Dictionary<string, Price>();
    Price GetCurrentPrice(string pair) {
      if (!IsLoggedIn) return new Price();
      if (!pricesByPair.ContainsKey(pair)) pricesByPair[pair] = fw.GetPrice(pair);
      return pricesByPair[pair]; 
    }
    Account accountCached = new Account();
    private void RunPrice(Price price) {
      try {
        if (price != null) pricesByPair[price.Pair] = price;
        string pair = price.Pair;
        if (!CanTrade(pair)) return;
        if (!price.IsReal) price = fw.GetPrice(pair);
        var tm = GetTradingMacro(pair);
        if (tm == null) return;
        var summary = fw.GetSummary(pair);
        var account = accountCached = fw.GetAccount();
        var trades = account.Trades.Where(t=>t.Pair == tm.Pair).ToArray();
        tm.TradesToHistory_Add(trades);
        tm.Positions = trades.Length;
        var tl = GetTickLoader(pair);
        if (!tl.IsRunning) tl.Run();
        tm.Net = summary != null ? summary.NetPL : (double?)null;
        tm.CurrentLossPercent = (tm.CurrentLoss + tm.Net.GetValueOrDefault()) / account.Balance;
        tm.BalanceOnStop = account.Balance + tm.StopAmount.GetValueOrDefault();
        tm.BalanceOnLimit = account.Balance + tm.LimitAmount.GetValueOrDefault();
        SetLotSize(tm, account);
        ProcessPendingOrders(pair);
        if (!CheckProfitScheduler.IsRunning)
          CheckProfitScheduler.Command = () => CheckProfit(account);
        var ratesForCorridor = GetRatesForCorridor(ratesByPair[pair], tm).ToArray();
        var rateBuy = ratesForCorridor.OrderBy(r => r.BidLow).First();
        var rateSell = ratesForCorridor.OrderBy(r => r.AskHigh).Last();
        tm.SetCorridorFib(fw.InPips(pair, price.Bid - rateBuy.BidLow), fw.InPips(pair, rateSell.AskHigh - price.Ask));
        CheckTrades(trades);
        if (!OpenTradeByStopScheduler.IsRunning)
          OpenTradeByStopScheduler.Command = () => OpenTradeByStop(pair);
      } catch (Exception exc) { Log = exc; }
    }

    ThreadScheduler OpenTradeByStopScheduler = new ThreadScheduler();
    void OpenTradeByStop(string pair) {
      var tm = GetTradingMacro(pair);
      if (tm == null) return;
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
      var fibInstant = tm.CorridorFibInstant.Round(1);
      var fib = tm.CorridorFib.Round(1);
      var fibAvg = tm.CorridorFibAverage.Round(1);
      double fibMim = tm.FibMin = Math.Ceiling(tm.FibMax / tm.CorridorStats.Iterations);
      //bool? buy = fib.Between(-fibMax, -fibMin) && fibAvg < -fibMax ? true : fib.Between(fibMin, fibMax) && fibAvg > fibMax ? false : (bool?)null;
      var buy = tm.TradeSignal =
        fibAvg < -fibMim && fib > fibAvg /*&& fibInstant < fib*/ ? true :
        fibAvg > +fibMim && fib < fibAvg /*&& fibInstant > fib*/ ? false :
        (bool?)null;
      var trades = fw.GetTrades(pair);
      if (buy.HasValue ) {
        var tradesToClose = trades.Where(t => t.IsBuy != buy).ToArray();
        if (tradesToClose.Length > 0) {
          try {
            fw.CloseTrades(tradesToClose);
            tm.TradesToHistory_Clear();
          } catch (Exception exc) {
            Log = exc;
          }
          return;
        }
        var tradesInSameDirection = trades.Where(t => t.IsBuy == buy).ToArray();
        var maxPL = tradesInSameDirection.Length == 0 ? 0 : tradesInSameDirection.Max(t => t.PL);
        if (tm.ReverseOnProfit 
            && tm.IsCorridornessOk 
            && tradesInSameDirection.Length < tm.MaximumPositions 
            && (tradesInSameDirection.Length == 0 || maxPL < -tm.TradeDistance)
          )
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



    void AdjustCurrentLosses(double profit,int tradingGroup) {
      if (profit <= 0) return;
      foreach (var tm in TradingMacrosCopy.Where(t => t.TradingGroup == tradingGroup && t.CurrentLoss < 0).OrderBy(t => t.CurrentLoss)) {
        tm.CurrentLoss = tm.CurrentLoss + profit;
        if (tm.CurrentLoss < 0) break;
        profit = tm.CurrentLoss;
        tm.CurrentLoss = 0;
      }
      MasterModel.CurrentLoss = Math.Min(0, CurrentLoss);
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
        var totalGross = tm.CurrentLoss + trade.GrossPL;
        tm.CurrentLoss = Math.Min(0, totalGross);
        AdjustCurrentLosses(totalGross,tm.TradingGroup);
        tm.CurrentLot = fw.GetTrades(trade.Pair).Sum(t => t.Lots);
        if (HasTradeToReverse(trade.Id))
          OpenChainTrade(tm, !trade.IsBuy);
      } catch (Exception exc) {
        Log = exc;
      }
      //fw.FixOrderOpen(trade.Pair, !trade.IsBuy, lot, limit, stop, trade.GrossPL < 0 ? trade.Id : "");
    }

    Models.TradingMacro[] ActiveTradingMacros { get { return TradingMacrosCopy.Where(tm => tm.CurrentLoss < 0).ToArray(); } }
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
      var pairs = TradingMacrosCopy.Where(tm => tm.LotSize > 0 && tm.Pair.Contains(currency)).Select(tm=>tm.Pair).ToArray();
      if( pairs.Length == 0)return 0;
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
        rates.Add(new Rate(priceTime,price.Ask,price.Bid, false));
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
          var sw = Stopwatch.StartNew();
          var firstTradeDate = fw.GetTrades(pair).OrderBy(t => t.Id).Select(t => t.Time).DefaultIfEmpty(fw.ServerTime).First();
          var minutesBack = Math.Max(tm.CorridorBarMinutes * historyMinutesBack, (fw.ServerTime - firstTradeDate).TotalMinutes).ToInt();
          rates = rates.Take(rates.Count - 2).ToList();
          if (rates.Count == 0)
            rates = fw.GetBarsBase(pair, 1, minutesBack).ToList();
          fw.GetBars(pair, 1, fw.ServerTime.AddMinutes(-minutesBack), DateTime.FromOADate(0), ref rates);
          if (sw.Elapsed > TimeSpan.FromSeconds(5))
            Debug.WriteLine("GetRates[" + pair + "]:{0:n2} sec", sw.ElapsedMilliseconds / 1000.0);
          rates.RemoveRange(0, Math.Max(0, rates.Count - minutesBack));
          ratesByPair[pair] = rates;
          FillOverlaps(pair, rates);
          tm.LastRateTime = rates.Max(r => r.StartDate);
          sw.Stop();
          if( sw.Elapsed > TimeSpan.FromSeconds(5) )
            Debug.WriteLine("LoadRates[" + pair + "]:{0:n2} sec", sw.ElapsedMilliseconds / 1000.0);
          RunCorrelations();
          foreach (var correlation in Correlations)
            foreach (var tmc in TradingMacrosCopy)
              tmc.SetCorrelation(correlation.Key, correlation.Value);
            //Debug.WriteLine("Correlation:{0} - {1:n1}", correlation.Key, correlation.Value);
          //fw_PriceChanged(price);
        } catch (Exception exc) {
          error = true;
          Log = exc;
        }
      new ThreadScheduler(TimeSpan.FromSeconds(error ? 5 : 60),
        ThreadScheduler.infinity, () => LoadRates(pair), (s, e) => Log = e.Exception);
    }

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
    bool TradeExists(Trade[] trades,Func<Trade,bool> condition) {
      if( trades.Length == 0)return false;
      return TradeExists(trades, trades[0].Pair, trades[0].IsBuy,condition); 
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
    Order GetEntryOrder(string pair,bool isBuy) {
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
    double GetEntryOrderLimit(Trade[] trades,int lot) {
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
      return GetStopByFractal(trade.Pair, trade.IsBuy,trade.Time);
    }
    private double GetStopByFractal(string pair, bool isBuy,DateTime tradeDate) {
      return GetStopByFractal(pair, ratesByPair[pair], isBuy,tradeDate);
    }
    private double GetStopByFractal(string pair, IEnumerable<Rate> rates, bool isBuy,DateTime tradeDate) {
      return GetStopByFractal(0,pair, rates, isBuy,tradeDate);
    }
    private double GetStopByFractal(double stopCurrent, string pair, IEnumerable<Rate> rates, bool isBuy,DateTime tradeDate) {
      if (!CanTrade(pair)) return 0;
      var stop = stopCurrent;
      var round = fw.GetDigits(pair);
      try {
        if (rates.Count() > 0) {
          var tm = GetTradingMacro(pair);
          var stopSlack = GetFibSlack(tm.FibMin, tm).Round(round);
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

    private Rate[] GetRatesForCorridor( Models.TradingMacro tm) {
      return GetRatesForCorridor(ratesByPair[tm.Pair], tm);
    }
    private Rate[] GetRatesForCorridor(IEnumerable<Rate> rates, Models.TradingMacro tm) {
      var rts = rates//.Where(r => r.Spread <= slack * 2)
        .Where(r => r.StartDate >= tm.CorridorStats.StartDate).ToArray();
      return rts;
    }
    private double GetLimitByFractal(Trade[] trades, Trade trade, IEnumerable<Rate> rates) {
      string pair = trade.Pair;
      bool isBuy = trade.IsBuy;
      var tm = GetTradingMacro(pair);
      if (!CanTrade(trade))return 0;
      var ratesForLimit = GetRatesForCorridor(ratesByPair[pair], tm);
      var digits = fw.GetDigits(pair);
      double limit = 0;
      Func<double> returnLimit = () => limit.Round(digits);
      switch (tm.FreezeType) {
        case Models.Freezing.Float:
        case Models.Freezing.Freez:
          var leg = GetFibSlack(tm.FibMin, tm);
          limit = isBuy ? tm.CorridorStats.AskHigh - leg : tm.CorridorStats.BidLow + leg;
          return returnLimit();
        case Models.Freezing.None:
          limit = isBuy ? tm.CorridorStats.AskHigh + tm.CorridorStats.Heigth : tm.CorridorStats.BidLow - tm.CorridorStats.Heigth;
          return returnLimit();
        default:
          var slack = GetFibSlack(tm.FibMin,tm);
          var price = fw.GetPrice(pair);
          limit = isBuy ? Math.Max(trade.Open, ratesForLimit.Max(r => r.BidHigh)) + slack
            : Math.Min(trade.Open, ratesForLimit.Min(r => r.AskLow)) - slack;
          if (isBuy && limit <= price.Bid) return 0;
          if (!isBuy && limit >= price.Ask) return 0;
          return limit.Round(digits);
      }
      if(tm.FreezeType != Models.Freezing.Freez) {
        if (tm.TakeProfitPips == 0) return 0;
        return  (trade.Open + fw.InPoints(pair,isBuy ? tm.TakeProfitPips : -tm.TakeProfitPips)).Round(digits);
      }
    }

    #region GetSlack
    private double GetFibSlack(double fib, Models.TradingMacro tm) {
      var slack = fib.FibReverse().YofS(tm.CorridorStats.Heigth);
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
      if (trade.IsBuy && limitNew <= GetCurrentPrice(trade.Pair).Bid ||
         !trade.IsBuy && limitNew >= GetCurrentPrice(trade.Pair).Ask)
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

    private bool DoStop(Trade trade, Models.TradingMacro tm, double stopNew,int round) {
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
