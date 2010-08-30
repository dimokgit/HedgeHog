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
using HedgeHog.Alice.Store;
using System.Runtime.Serialization;
using System.Data.Objects.DataClasses;
using System.Collections.Specialized;
using Order2GoAddIn;

namespace HedgeHog.Alice.Client {
  [Export]
  public class RemoteControlModel : RemoteControlModelBase {
    //Dimok:Show Closed trades

    #region Settings
    readonly int historyMinutesBack = 1;
    readonly double profitToClose = 1;
    #endregion

    #region members
    RatesLoader ratesLoader = new RatesLoader();
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



    #endregion

    #region Properties


    ObservableCollection<string> Instruments { get; set; }

    double CurrentLoss { get { return TradingMacrosCopy.Sum(tm => tm.CurrentLoss); } }

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

    #region CopyTradingMacroCommand

    ICommand _CopyTradingMacroCommand;
    public ICommand CopyTradingMacroCommand {
      get {
        if (_CopyTradingMacroCommand == null) {
          _CopyTradingMacroCommand = new Gala.RelayCommand<object>(CopyTradingMacro, (tm) => tm is TradingMacro);
        }

        return _CopyTradingMacroCommand;
      }
    }
    void CopyTradingMacro(object tradingMacro) {
      var tm = tradingMacro as TradingMacro;
      var tmNew = TradingMacro.CreateTradingMacro(
        tm.Pair, tm.TradingRatio, Guid.NewGuid(), tm.LimitBar, tm.CurrentLoss, tm.ReverseOnProfit,
        tm.FreezLimit, tm.CorridorMethod, tm.FreezeStop, tm.FibMax, tm.FibMin, tm.CorridornessMin, tm.CorridorIterationsIn,
        tm.CorridorIterationsOut, tm.CorridorIterations,
        tm.CorridorBarMinutes, tm.PairIndex, tm.TradingGroup, tm.MaximumPositions, tm.IsActive, "", tm.LimitCorridorByBarHeight);
      //foreach (var p in tradingMacro.GetType().GetProperties().Where(p => p.GetCustomAttributes(typeof(DataMemberAttribute), false).Count() > 0))
      //  if (!(p.GetCustomAttributes(typeof(EdmScalarPropertyAttribute), false)
      //    .DefaultIfEmpty(new EdmScalarPropertyAttribute()).First() as EdmScalarPropertyAttribute).EntityKeyProperty
      //    && p.Name!="Pair"
      //    )
      //    tmNew.SetProperty(p.Name, tm.GetProperty(p.Name));
      try {
        GlobalStorage.Context.TradingMacroes.AddObject(tmNew);
        GlobalStorage.Context.SaveChanges();
        TradingMacrosCopy_Add(tmNew);
      } catch (Exception exc) {
        Log = exc;
      }
    }

    #endregion

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
          _DeleteTradingMacroCommand = new Gala.RelayCommand<object>(DeleteTradingMacro, (tm) => tm is TradingMacro);
        }

        return _DeleteTradingMacroCommand;
      }
    }
    void DeleteTradingMacro(object tradingMacro) {
      var tm = tradingMacro as TradingMacro;
      if (tm == null || tm.EntityState == System.Data.EntityState.Detached) return;
      GlobalStorage.Context.TradingMacroes.DeleteObject(tm);
      GlobalStorage.Context.SaveChanges();
      TradingMacrosCopy_Delete(tm);
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
        var pair = (tradingMacro as TradingMacro).Pair;
        fw.CloseTradesAsync(tradesManager.GetTrades(pair));
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
        var pair = (tradingMacro as TradingMacro).Pair;
        var tradeIds = tradesManager.GetTrades(pair).Select(t => t.Id).ToArray();
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
        var tm = tradingMacro as TradingMacro;
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
        OpenChainTrade(tradingMacro as TradingMacro, false);
        //AddPendingOrder(false, tm.Pair, () => openTradeCondition(tm, false), () => OpenTrade(tm, false));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }
    #endregion

    #region Ctor
    void CleanEntryOrders() {
      try {
        var trades = tradesManager.GetTrades();
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
          InitializeModel();
          App.container.SatisfyImportsOnce(this);
          fw = new FXW(MasterModel.CoreFX);
          tradesManager = IsInVirtualTrading ? (virtualTrader = new VirtualTradesManager(fw.GetPipSize, fw.GetDigits, ratesByPair)) : (ITradesManager)fw;
          MasterModel.CoreFX.LoggedInEvent += CoreFX_LoggedInEvent;
          MasterModel.CoreFX.LoggedOffEvent += CoreFX_LoggedOffEvent;
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    private void InitializeModel() {
      GlobalStorage.Context.ObjectMaterialized += new ObjectMaterializedEventHandler(Context_ObjectMaterialized);
      GlobalStorage.Context.ObjectStateManager.ObjectStateManagerChanged += new System.ComponentModel.CollectionChangeEventHandler(ObjectStateManager_ObjectStateManagerChanged);
    }

    private void LoadClosedTrades() {
      var fileName = "ClosedTrades.xml";
      if (!File.Exists("ClosedTrades.xml")) return;
      foreach (var tradeString in File.ReadAllLines("ClosedTrades.xml").Where(s => !string.IsNullOrWhiteSpace(s))) {
        var trade = new Trade().FromString(tradeString);
        MasterModel.AddCosedTrade(trade);
        //if (trade.TimeClose > DateTime.Now.AddMonths(-1)) ClosedTrades.Add(trade);
      }
      File.Move(fileName, fileName + ".old");
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
      var tm = e.Element as TradingMacro;
      if (tm != null) {
        if (tm.EntityState == System.Data.EntityState.Detached)
          tm.PropertyChanged -= TradingMacro_PropertyChanged;
        else if (tm.EntityState == System.Data.EntityState.Added)
          InitTradingMacro(tm);
      }
    }

    void Context_ObjectMaterialized(object sender, ObjectMaterializedEventArgs e) {
      var tm = e.Entity as TradingMacro;
      if (tm == null) return;
      InitTradingMacro(tm);
    }

    void TradingMacro_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
      try {
        var tm = sender as TradingMacro;
        var propsToHandle = Lib.GetLambdas(() => tm.Pair, () => tm.TradingRatio);
        if (propsToHandle.Contains(e.PropertyName)) SetLotSize(tm, fw.GetAccount());
        //if (e.PropertyName == Lib.GetLambda(() => tm.OverlapToStop)) LoadRates(tm.Pair);
        if (e.PropertyName == Lib.GetLambda(() => tm.CorridorBarMinutes))
          ratesByPair[tm.Pair].Clear();
        //if (Lib.GetLambda(() => tm.LimitBar) == e.PropertyName)
        //  GetBarHeight(tm);
        if (e.PropertyName == Lib.GetLambda(() => tm.CorridorIterations))
          tm.CorridorStatsArray.Clear();
        if (e.PropertyName == Lib.GetLambda(() => tm.CorridorIterationsIn)) {
          tm.PriceCmaWalker.Reset(tm.CorridorIterationsIn);
          tm.PriceCmaDiffHighWalker.Reset(tm.CorridorIterationsIn);
          tm.PriceCmaDiffLowWalker.Reset(tm.CorridorIterationsIn);
        }
        if (e.PropertyName == Lib.GetLambda(() => tm.IsActive) && ShowAllMacrosFilter)
          RaisePropertyChanged(() => TradingMacrosCopy);

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
          
          tradesManager.PriceChanged += fw_PriceChanged;
          tradesManager.TradeRemoved += fw_TradeRemoved;
          tradesManager.TradeAdded += fw_TradeAdded;
        
          fw.TradeClosed += fw_TradeClosed;
          fw.TradeChanged += fw_TradeChanged;
          fw.OrderAdded += fw_OrderAdded;
          fw.Error += fw_Error;
        }
        foreach (var tm in TradingMacrosCopy) {
          tm.CurrentLot = tradesManager.GetTrades().Where(t => t.Pair == tm.Pair).Sum(t => t.Lots);
          tm.LastTrade = fw.GetLastTrade(tm.Pair);
          LoadRates(tm.Pair);
        }
        MasterModel.CurrentLoss = CurrentLoss;
      } catch (Exception exc) { MessageBox.Show(exc + ""); }
    }

    void CoreFX_LoggedOffEvent(object sender, EventArgs e) {
      if (fw != null) {
        tradesManager.PriceChanged -= fw_PriceChanged;
        tradesManager.TradeAdded -= fw_TradeAdded;
        tradesManager.TradeRemoved -= fw_TradeRemoved;

        fw.TradeChanged -= fw_TradeChanged;
        fw.OrderAdded -= fw_OrderAdded;
        fw.Error -= fw_Error;
        fw.TradeClosed -= fw_TradeClosed;
      }
    }


    BackgroundWorkerDispenser LoadRatesSchedulers = new BackgroundWorkerDispenser();
    BackgroundWorkerDispenser RunPriceSchedulers = new BackgroundWorkerDispenser();
    BackgroundWorkerDispenser ScanCorridorSchedulers = new BackgroundWorkerDispenser();
    BackgroundWorkerDispenser GetBarHeightScheduler = new BackgroundWorkerDispenser();
    BackgroundWorkerDispenser ShowChartScheduler = new BackgroundWorkerDispenser();
    void fw_PriceChanged(Price price) {
      try {
        var sw = Stopwatch.StartNew();
        if (price != null) SetCurrentPrice(price);
        var pair = price.Pair;
        var tm = GetTradingMacro(pair);
        if (tm != null) {
          var rates = GetRatesByPair(pair);
          if (!IsInVirtualTrading && (rates.Count == 0 || tm.LastRateTime.AddSeconds(10) <= rates.Last().StartDate))
            LoadRatesSchedulers.Run(pair, () => LoadRates(tm.Pair), e => Log = e);
          if (tm.InPips == null) tm.InPips = (d) => fw.InPips(pair, d);
          tm.SetPriceCma(price,rates);
          tm.TicksPerMinuteSet(price, fw.ServerTime, d => fw.InPips(pair, d));
          if( !IsInVirtualTrading)
            AddCurrentTick(GetRatesByPair(pair), price);
          if (false)
            foreach (var cs in tm.CorridorStatsArray) {
              if (cs != null) {
                var ratesForStats = GetRatesForCorridor(rates, cs);
                if (ratesForStats.Length > 0)
                  cs.SetCorridorFib(
                    //fw.InPips(pair, price.Bid - ratesForStats.Min(r => r.BidLow)),
                    fw.InPips(pair, price.Average - ratesForStats.Min(r => r.PriceAvg)),
                    //fw.InPips(pair, ratesForStats.Max(r => r.AskHigh) - price.Ask),
                    fw.InPips(pair, ratesForStats.Max(r => r.PriceAvg) - price.Average),
                    tm.TicksPerMinuteMinimum);
              }
            }
          GetBarHeightScheduler.Run(pair,IsInVirtualTrading,
            () => {
              //Thread.CurrentThread.Priority = ThreadPriority.Lowest;
              //var swl = Stopwatch.StartNew();
              tm.BarHeight60 = rates.ToArray().GetWaveHeight(4, tm.LimitBar);
              //Debug.WriteLine("BarHeight:{0:n1}", swl.ElapsedMilliseconds);
            }, e => Log = e);
          ScanCorridorSchedulers.Run(pair,IsInVirtualTrading, () => {
            var swl = Stopwatch.StartNew();
            ScanCorridor(pair, GetRatesByPair(pair));
            Debug.WriteLine("ScanCorridor:{0:n1}", swl.ElapsedMilliseconds);
            OpenTradeByStop(pair);
            ShowChartScheduler.Run(pair, () => {
              ShowChart(pair);
              //Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            }, e => Log = e);
          }, e => Log = e);
        }
        //if (!CanTrade(price.Pair)) return;
        RunPriceSchedulers.Run(pair, () => RunPrice(pair));
        //CheckTradesLotSizeScheduler.Run(pair, () => CheckTradesLotSize(pair));
        if (sw.Elapsed > TimeSpan.FromSeconds(1))
          Log = new Exception("fw_PriceChanged took " + sw.Elapsed.TotalSeconds + " secods");
      } catch (Exception exc) {
        Log = exc;
      }
    }
    void ShowChart(string pair) {
//      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
      var charter = GetCharter(pair);
      charter.Dispatcher.Invoke(new Action(() => {
        var tm = GetTradingMacro(pair);
        var rates = GetRatesByPair(pair);
        var price = GetCurrentPrice(pair);
        //      new Scheduler(charter.Dispatcher, (s, e) => Log = e.Exception).Command = () => {
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
        var csFirst = tm.CorridorStats;
        var timeHigh = tm.GetCorridorStats(0).StartDate;
        var timeCurr = tm.CorridorStatsArray.Count() > 1 ? tm.GetCorridorStats(-1).StartDate : DateTime.MinValue;
        var timeLow = tm.LastTrade.Time;
        charter.AddTicks(price, GetRatesByPair(pair).ToList(), null,
          0, 0,
          csFirst.AskHigh, csFirst.BidLow,
          csFirst.AverageHigh, csFirst.AverageLow,
          timeHigh, timeCurr, timeLow,
          new double[0]);
        //    };
      }));
    }
    private void RunPrice(string pair) {
      var sw = Stopwatch.StartNew();
      try {
        if (!CanTrade(pair)) return;
        Price price = GetCurrentPrice(pair);
        if (!price.IsReal) price = tradesManager.GetPrice(pair);
        var tm = GetTradingMacro(pair);
        if (tm == null) return;
        var account = accountCached = IsInVirtualTrading ? fw.GetAccount(false) : fw.GetAccount();
        if (IsInVirtualTrading) account.Trades = tradesManager.GetTrades(pair);
        var trades = account.Trades.Where(t => t.Pair == tm.Pair).ToArray();
        var minGross = trades.Select(t => t.GrossPL).DefaultIfEmpty().Min();
        if (tm.MinimumGross > minGross) tm.MinimumGross = minGross;
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
      if (sw.Elapsed > TimeSpan.FromSeconds(5))
        Log = new Exception("RunPrice(" + pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
      //Debug.WriteLine("RunPrice[{1}]:{0} ms", sw.Elapsed.TotalMilliseconds, pair);
    }

    Dictionary<string, PendingOrder> pendingOrders = new Dictionary<string, PendingOrder>();
    Dictionary<string, Predicate<Trade>> pendingOpenRequest = new Dictionary<string, Predicate<Trade>>();
    void OpenTradeByStop(string pair) {
      var tm = GetTradingMacro(pair);
      if (tm == null || tm.CorridorStats == null) return;
      //if (tm.CorridorStats.BuyStopByCorridor == 0 || tm.CorridorStats.SellStopByCorridor == 0) return;
      var trades = tradesManager.GetTrades(pair);
      if (trades.Length > 0) tm.LastTrade = trades.OrderBy(t => t.Time).Last();

      #region Close trades
      var grossPL = trades.Sum(t => t.GrossPL);
      var tradesWithProfit = trades.Where(t => t.PL > 0 || grossPL > 0).ToArray();
      if (tradesWithProfit.Length > 1) {
        fw.FixOrdersClose(tradesWithProfit.Select(t => t.Id).ToArray());
        return;
      }
      #endregion

      //CheckTradesLotSize(pair, trades);
      //var calcLotSize = CalculateLot(tm);
      var buy = tm.TradeSignal;
      if (buy.HasValue) {
        try {
          #region Conditions
          var tradesInOpositDirection = trades.Where(t => t.IsBuy != buy).ToArray();
          var tradesInSameDirection = trades.Where(t => t.IsBuy == buy).ToArray();
          var maxPL = tradesInSameDirection.Length == 0 ? 0 : tradesInSameDirection.Max(t => t.PL);
          var lastTradeProfit = trades.Length > 0 ? trades.Sum(t => t.PL) : tm.LastLotSize;
          var maxLotSize = tm.MaxLotSize;// Math.Max(tm.LastLotSize, trades.Select(t => t.Lots).DefaultIfEmpty().Max()) + tm.LotSize;
          var isTradeConditionsOk =
               tm.IsTakeProfitPipsMinimumOk
            //&& calcLotSize <= maxLotSize
               && tm.IsCorridorAvarageHeightOk;
          #endregion
          #region Open Trade
          if (isTradeConditionsOk) {
            if (tradesInOpositDirection.Length > 0) {
              bool pendingOpen = true;
              pendingOpenRequest[pair] = (trade) => {
                try {
                  var tl = tradesManager.GetTrades(trade.Pair);
                  var tod = tl.Where(t => t.Buy == trade.Buy).ToArray();
                  if (tod.Length > 0) return false;
                  var tsd = tl.Where(t => t.Buy != trade.Buy).ToArray();
                  if (CanOpenTrade(pair, tod, tsd, maxPL))
                    OpenTradeWithWait(pair, !trade.Buy);
                  pendingOpen = false;
                } catch (Exception exc) {
                  Log = exc;
                  pendingOpen = false;
                }
                return true;
              };
              try {
                //fw.TradeClosed += tradeClosed;
                if (!tradesManager.ClosePair(pair, !buy.Value)) {
                  foreach (var trade in tradesManager.GetTrades(pair).Where(t => t.IsBuy != buy).ToArray())
                    fw.FixOrderClose(trade.Id);
                }
                while (pendingOpen)
                  Thread.Sleep(100);
              } catch (Exception exc) {
                Log = exc;
                return;
              } finally {
                //fw.TradeClosed -= tradeClosed;
                RunPrice(pair);
              }
              tradesInOpositDirection = tradesManager.GetTrades(pair).Where(t => t.IsBuy != buy).ToArray();
              return;
            }
            if (CanOpenTrade(pair, tradesInOpositDirection, tradesInSameDirection, maxPL)
              //&& tm.CorridorStats.IsCorridornessOk
              ) {
              OpenTradeWithWait(pair, buy.Value);
            }
          }
          #endregion
        } finally {
          //if (IsInVirtualTrading) ShowChart(pair);
        }
      }
    }

    private bool CanOpenTrade(string pair, Trade[] tradesInOpositDirection, Trade[] tradesInSameDirection, double maxPL) {
      TradingMacro tm = GetTradingMacro(pair);
      return tradesInOpositDirection.Length == 0
                  && (!IsInVirtualTrading || tradesManager.IsInTest)
                  && tm.ReverseOnProfit
                  && tradesInSameDirection.Length < tm.MaximumPositions
                  && (tradesInSameDirection.Length == 0 || maxPL < -tm.TakeProfitPips)
                  && !pendingOrders.ContainsKey(pair);
    }

    private void OpenTradeWithWait(string pair, bool buy) {
      TradingMacro tm = GetTradingMacro(pair);
      #region Pending Order Setup
      Action<object, FXW.RequestEventArgs> reqiesFailedAction = (s, e) => {
        if (pendingOrders.ContainsKey(pair) && e.ReqiestId == pendingOrders[pair].RequestId) {
          pendingOrders.Remove(pair);
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
        var currentCount = tradesManager.GetTrades(pair).Length;
        fw.RequestFailed += rfh;
        fw.OrderRemoved += orh;
        //po = OpenChainTrade(tm, buy.Value);
        var po = OpenTrade(buy, pair, Math.Min(tm.MaxLotSize, CalculateLot(tm)), tm.TakeProfitPips, 0, 0, "");
        pendingOrders.Add(pair, po);
        var start = DateTime.Now;
        var stop = TimeSpan.FromSeconds(30);
        while (pendingOrders.ContainsKey(pair) && tradesManager.GetTrades(pair).Count() == currentCount && (DateTime.Now - start) < stop)
          Thread.Sleep(100);
      } catch (Exception exc) {
        Log = exc;
      } finally {
        fw.RequestFailed -= rfh;
        fw.OrderRemoved -= orh;
        if (pendingOrders.ContainsKey(pair)) pendingOrders.Remove(pair);
      }
    }

    ThreadScheduler CheckProfitScheduler = new ThreadScheduler();
    void CheckProfit(Account account) {
      try {
        var originalBalance = account.Balance - CurrentLoss;
        var profit = (account.Equity / originalBalance) - 1;
        if (profit > profitToClose) {
          var trades = tradesManager.GetTrades("");
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

    public void fw_TradeAdded(Trade trade) {
      var tm = GetTradingMacro(trade.Pair);
      if (tm == null) return;
      if (tm.LastTrade.Time < trade.Time) tm.LastTrade = trade;
      tm.CurrentLot = tradesManager.GetTrades(trade.Pair).Sum(t => t.Lots);
      var amountK = trade.Lots/1000;
      if (tm.HistoryMaximumLot < amountK) tm.HistoryMaximumLot = amountK;
      if (false) {
        if (tm.ReverseOnProfit) {
          foreach (var trd in tradesManager.GetTrades(trade.Pair).Where(t => t.Id != trade.Id && t.IsBuy != trade.IsBuy))
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



    void AdjustCurrentLosses(TradingMacro tradingMacro) {
      var tmGroup = GetTradingMacrosByGroup(tradingMacro);
      double profit = tmGroup.Where(tm => tm.CurrentLoss > 0).Sum(tm => tm.CurrentLoss);
      foreach (var tm in tmGroup.Where(t => t.CurrentLoss < 0).OrderBy(t => t.CurrentLoss)) {
        if (profit <= 0) break;
        tm.CurrentLoss = tm.CurrentLoss + profit;
        profit = tm.CurrentLoss;
      }
        GetTradingMacrosByGroup(tradingMacro).Where(tm => tm.CurrentLoss > 0).ToList()
        .ForEach(tm => tm.CurrentLoss = 0);
      MasterModel.CurrentLoss = Math.Min(0, CurrentLoss);
    }

    void fw_TradeClosed(object sender, FXW.TradeEventArgs e) {
    }

    void fw_TradeRemoved(Trade trade) {
      //CleanEntryOrders();
      try {
        new ThreadScheduler(TimeSpan.FromSeconds(1), ThreadScheduler.infinity, () => {
          if (tradesManager.GetTrades(trade.Pair).Length == 0) RemoveEntryOrder(trade.Pair);
        }, (s, e) => Log = e.Exception);
        var pair = trade.Pair;
        var tm = GetTradingMacro(pair);
        if (tm == null) return;
        tm.LastTrade = trade;
        var commission = MasterModel.CommissionByTrade(trade);
        Debug.WriteLine("Commission:" + commission);
        var totalGross = trade.GrossPL - commission;
        tm.RunningBalance += totalGross;
        tm.CurrentLoss = tm.CurrentLoss + totalGross;
        AdjustCurrentLosses(tm);
        tm.CurrentLot = tradesManager.GetTrades(trade.Pair).Sum(t => t.Lots);

        try {
          System.IO.File.AppendAllText("ClosedTrades_new.xml", Environment.NewLine + trade);
          MasterModel.AddCosedTrade(trade);
        } catch (Exception exc) { Log = exc; }

        if (HasTradeToReverse(trade.Id))
          OpenChainTrade(tm, !trade.IsBuy);
        if (pendingOpenRequest.ContainsKey(pair))
          try {
            if (pendingOpenRequest[pair](trade)) pendingOpenRequest.Remove(pair);
          } catch (Exception exc) {
            Log = exc;
            pendingOpenRequest.Remove(pair);
          }
      } catch (Exception exc) {
        Log = exc;
      }
      //fw.FixOrderOpen(trade.Pair, !trade.IsBuy, lot, limit, stop, trade.GrossPL < 0 ? trade.Id : "");
    }

    private int CalculateLot(TradingMacro tm) {
      Func<int, int> returnLot = d => Math.Max(tm.LotSize, d);
      if (tm.FreezeStopType == Freezing.Freez)
        return returnLot(tradesManager.GetTrades(tm.Pair).Sum(t => t.Lots) * 2);
      var currentLoss = GetCurrentLossByGroup(tm);
      var grossPL = GetGrossPLByGroup(tm);
      return returnLot(CalculateLotCore(tm, currentLoss + grossPL));
    }
    double GetGrossPLByGroup(TradingMacro tm) {
      return GetTradingMacrosByGroup(tm).Sum(tm1 => tradesManager.GetTrades(tm1.Pair).Sum(t => t.GrossPL));
    }
    private double GetCurrentLossByGroup(TradingMacro tm) {
      return GetTradingMacrosByGroup(tm).Sum(tm1 => tm1.CurrentLoss);
    }
    private int CalculateLotCore(TradingMacro tm, double totalGross) {
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

    Thread reLogin;
    Scheduler loginScheduler = new Scheduler(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher);
    void AddCurrentTick(List<Rate> rates, Price price) {
      if (rates == null || rates.Count == 0) return;
      var priceTime = price.Time.Round();
      if (priceTime > rates.Last().StartDate)
        rates.Add(new Rate(priceTime, price.Ask, price.Bid, false));
      else rates.Last().AddTick(priceTime, price.Ask, price.Bid);
    }
    class RatesLoader {
      FXW fw = new FXW();
      Dictionary<string, Rate[]> ticksDictionary = new Dictionary<string, Rate[]>();
      public RatesLoader() {
        fw.CoreFX.LogOn("MICR512463001", "6131", true);
      }
      void SetTicks(string pair, Rate[] rates) {
        lock (ticksDictionary) {
          ticksDictionary[pair] = rates;
        }
      }
      Rate[] GetTicks(string pair) {
        lock (ticksDictionary) {
          if (!ticksDictionary.ContainsKey(pair)) SetTicks(pair, new Rate[0]);
          return ticksDictionary[pair];
        }
      }
      public List<Rate> LoadRates(string pair, int minutesBack, DateTime startDate, DateTime endDate) {
        Rate[] ticks = GetTicks(pair);
        try {
          var fetchRates = ticks.Count() == 0;
          if (ticks.Count() > 0 && (ticks[0].StartDate - ticks[1].StartDate).Duration() >= TimeSpan.FromSeconds(59))
            ticks = new Rate[0];
          var period = fetchRates ? 1 : 0;
          //fw.GetBars(pair, fetchRates ? 1 : 0, startDate, DateTime.FromOADate(0), ref ticks);
          if (ticks.Count() == 0)
            ticks = fw.GetBarsFromHistory(pair, period, DateTime.MinValue, endDate);
          fw.GetBars(pair, period, minutesBack, endDate, ref ticks);
          SetTicks(pair, ticks);
          return ticks.GetMinuteTicks(1).OrderBars().ToList();
        } catch (Exception exc) {
          Debug.WriteLine("load Rates:" + exc);
          return new List<Rate>();
        }
      }
      void SaveToDB(string pair) {
        var lastTickInDB = GlobalStorage.Context.Bars.LastOrDefault();
        var dateLast = lastTickInDB == null ? DateTime.MinValue : lastTickInDB.StartDate;
        GetTicks(pair).Where(t => t.StartDate > dateLast).ToList().ForEach(t =>
          GlobalStorage.Context.Bars.AddObject(new Store.Bar() { AskClose = t.AskClose }));
      }
    }
    void LoadRates(string pair) {
      try {
        var tm = GetTradingMacro(pair);
        if (tm == null || !IsLoggedIn || tm.TradingRatio == 0) return;
        var rates = GetRatesByPair(pair);
        if (rates.Count > 0 && !IsLoggedIn) {
          MasterModel.CoreFX.LogOn();
        }
        if (tm.PriceQueue.LastTickTime() < fw.ServerTime.AddMinutes(-1) && !loginScheduler.IsRunning) {
          loginScheduler.Command = () => {
            Log = new Exception("Forced re-login.");
            MasterModel.CoreFX.Logout();
            MasterModel.CoreFX.LogOn();
          };
        }
        {
          Debug.WriteLine("LoadRates({0}) @ {1:HH:mm:ss}", pair, fw.ServerTime);
          var sw = Stopwatch.StartNew();
          var serverTime = fw.ServerTime;
          var firstTradeDate = accountCached.Trades.Where(t => t.Pair == pair).OrderBy(t => t.Id).Select(t => t.Time).DefaultIfEmpty(fw.ServerTime).First();
          var minutesBack = Math.Max(tm.CorridorBarMinutes * historyMinutesBack, (serverTime - firstTradeDate).TotalMinutes).ToInt();
          var startDate = new[] { tm.CorridorStats == null || tm.CorridorStats.StartDate == DateTime.MinValue ? DateTime.Now : tm.CorridorStats.StartDate.AddMinutes(-10), fw.ServerTime.Round().AddMinutes(-minutesBack + 16) }.Min();
          rates = ratesLoader.LoadRates(pair, minutesBack, startDate, serverTime);
          if (sw.Elapsed > TimeSpan.FromSeconds(1))
            Debug.WriteLine("GetRates[" + pair + "]:{0:n1} sec", sw.Elapsed.TotalSeconds);
          SetRatesByPair(pair, rates);
          tm.LastRateTime = rates.Max(r => r.StartDate);
          sw.Stop();
          var lastRate = rates.Last();
          fw_PriceChanged(new Price(pair, lastRate, fw.GetPipSize(pair), fw.GetDigits(pair)));
          #region RunCorrelations
          //RunCorrelations();
          //foreach (var correlation in Correlations)
          //  foreach (var tmc in TradingMacrosCopy)
          //    tmc.SetCorrelation(correlation.Key, correlation.Value);
          #endregion
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    private void GetBarHeight(TradingMacro tm) {
      //var sw = Stopwatch.StartNew();
      string pair = tm.Pair;
      var rates = GetRatesByPair(pair);
      var bhList = new List<double>();
      for (var i = 0; i < rates.Count - tm.LimitBar; i++)
        bhList.Add(GetBarHeight(tm.LimitBar, rates.Skip(i).ToList()));
      tm.BarHeight60 = bhList.Average();
      //Debug.WriteLine("GetBarHeight:{0} - {1:n0} ms", tm.Pair, sw.Elapsed.TotalMilliseconds);
    }

    private static double GetBarHeight(int barPeriod, List<Rate> rates) {
      var rates60 = rates.GetMinuteTicks(barPeriod);
      var hs = rates60.Select(r => r.PriceHigh - r.PriceLow).ToArray();
      var hsAvg = hs.Average();
      hs = hs.Where(h => h >= hsAvg).ToArray();
      hsAvg = hs.Average();
      return hs.Where(h => h >= hsAvg).Average();
    }

    Dictionary<string, Timer> timersByPair = new Dictionary<string, Timer>();

    private void FillOverlaps(string pair, IEnumerable<Rate> rates) {
      if (rates.Count() == 0) return;
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
      var addProfit = tm.FreezeStopType == Freezing.Float;
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
    PendingOrder OpenChainTrade(TradingMacro tm, bool isBuy) {
      var lot = CalculateLot(tm);
      lot = lot - tradesManager.GetTrades(tm.Pair).Sum(t => t.Lots);
      if (lot <= 0) return null;
      var stop = tm.FreezeStopType == Freezing.None ? 0 : GetStopByFractal(tm.Pair, isBuy, fw.ServerTime);
      return OpenTrade(isBuy, tm.Pair, lot, tm.TakeProfitPips, 0, stop, "");
    }


    private PendingOrder OpenTrade(bool buy, string pair, int lot, double limitInPips, double stopInPips, double stop, string remark) {
      var price = tradesManager.GetPrice(pair);
      var limit = limitInPips == 0 ? 0 : buy ? price.Ask + fw.InPoints(pair, limitInPips) : price.Bid - fw.InPoints(pair, limitInPips);
      if (stop == 0 && stopInPips != 0)
        stop = buy ? price.Bid + fw.InPoints(pair, stopInPips) : price.Ask - fw.InPoints(pair, stopInPips);
      return tradesManager.OpenTrade(pair, buy, lot, limit, stop, remark, price);
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

    private Rate[] GetRatesForCorridor(TradingMacro tm) {
      return GetRatesForCorridor(ratesByPair[tm.Pair], tm);
    }
    private Rate[] GetRatesForCorridor(IEnumerable<Rate> rates, TradingMacro tm) {
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
        case Freezing.Float:
        case Freezing.Freez:
          var leg = GetFibSlack(tm.CorridorStats.FibMinimum, tm);
          limit = isBuy ? tm.CorridorStats.AskHigh - leg : tm.CorridorStats.BidLow + leg;
          return returnLimit();
        case Freezing.None:
          limit = isBuy ? tm.CorridorStats.AskHigh + tm.CorridorStats.AverageHeight : tm.CorridorStats.BidLow - tm.CorridorStats.AverageHeight;
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
      if (tm.FreezeType != Freezing.Freez) {
        if (tm.TakeProfitPips == 0) return 0;
        return (trade.Open + fw.InPoints(pair, isBuy ? tm.TakeProfitPips : -tm.TakeProfitPips)).Round(digits);
      }
    }

    #region GetSlack
    private double GetFibSlack(double fib, TradingMacro tm) {
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

    bool CheckTradesLotSize(string pair, Trade[] trades) {
      if (trades.Length == 0) return false;
      var tm = GetTradingMacro(pair);
      if (tm.CorridorStats == null) return false;
      var lotSizeCalc = CalculateLot(tm);
      double lotSizeCurr = trades.Sum(t => t.Lots);
      //var isMinSize = lotSizeCalc == tm.LotSize && lotSizeCurr > lotSizeCalc;
      if (((lotSizeCurr + 0.1) / lotSizeCalc).ToInt() <= 2) return false;
      var lotSizeRemove = lotSizeCurr - lotSizeCalc;
      if (lotSizeRemove <= 0) return false;
      var tradesToClose = trades.Where(t => t.PL > 0).OrderByDescending(t => t.PL).ToArray();
      if (tradesToClose.Length == 0) return false;
      CloseLooseTrades(pair, tradesToClose, lotSizeRemove.ToInt());
      return true;
    }

    private void CloseLooseTrades(string pair, Trade[] trades, int lotSizeRemove) {
      var tradesList = trades.OrderBy(t => t.PL).ToList();
      while (lotSizeRemove > 0 && tradesList.Count > 0) {
        var trade = tradesList.First();
        if (trade.Lots >= lotSizeRemove) {
          fw.FixOrderClose(trade.Id, fw.Desk.FIX_CLOSEMARKET, (Price)null, lotSizeRemove);
          return;
        }
        lotSizeRemove -= trade.Lots;
        fw.FixOrderClose(trade.Id);
        tradesList.Remove(trade);
      }
    }
    private void CheckTrade(Trade[] trades, Trade trade, double stopAmount) {
      var tm = GetTradingMacro(trade.Pair);
      var round = fw.GetDigits(trade.Pair) - 1;
      if (tm.FreezeStopType != Freezing.None) {
        var stopNew = GetStopByFractal(trade).Round(round);
        var stopOld = trade.Stop.Round(round);
        if (DoStop(trade, tm, stopNew, round))
          ChangeTradeStop(trade, stopNew);
      }
      var limitNew = GetLimitByFractal(trades, trade, ratesByPair[trade.Pair]).Round(round);
      if (limitNew != 0 && (
        trade.IsBuy && limitNew <= GetCurrentPrice(trade.Pair).Bid ||
         !trade.IsBuy && limitNew >= GetCurrentPrice(trade.Pair).Ask))
        fw.CloseTradeAsync(trade);
      else if (false && DoLimit(trade, tm, limitNew, round))
        fw.FixCreateLimit(trade.Id, limitNew, "");
      //if (trade.Lots >= tm.LotSize * 10 && tm.CurrentLoss < 0 && trade.LimitAmount >= tm.CurrentLoss.Abs() * tm.LimitBar) tm.FreezLimit = true;
      if (stopAmount > 0 && tm.CurrentLoss == 0)
        RemoveEntryOrder(trade.Pair);
      if (stopAmount < 0 && tm.FreezeStopType == Freezing.Float)
        CreateEntryOrder(trade);
    }

    private bool DoLimit(Trade trade, TradingMacro tm, double limitNew, int round) {
      if (trade.IsBuy && limitNew <= GetCurrentPrice(trade.Pair).Ask) return false;
      if (!trade.IsBuy && limitNew >= GetCurrentPrice(trade.Pair).Bid) return false;
      if (trade.Limit == 0) return true;
      if (tm.FreezeType == Freezing.Freez && trade.Limit != 0) return false;
      if (trade.Limit.Round(round) == limitNew.Round(round)) return false;
      return true;
    }

    private bool DoStop(Trade trade, TradingMacro tm, double stopNew, int round) {
      if (tm.FreezeStopType == Freezing.None) return false;
      var stopOld = trade.Stop.Round(round);
      stopNew = stopNew.Round(round);
      if (stopOld == stopNew) return false;
      if (tm.FreezeStopType == Freezing.Freez && stopOld != 0) return false;
      return trade.Stop == 0 || trade.IsBuy && stopNew > stopOld || !trade.IsBuy && stopNew < stopOld;
    }

    #region Child trade helpers
    void SetLotSize(TradingMacro tm, Account account) {
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
    #endregion

    #region Init ...
    private void InitInstruments() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
        try {
          if (Instruments.Count == 0)
            fw.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
        } catch (Exception exc) {
          Log = exc;
        }
        RaisePropertyChangedCore("TradingMacros");
      }));
    }
    private void InitTradingMacro(TradingMacro tm) {
      tm.PropertyChanged += TradingMacro_PropertyChanged;
      if (!ratesByPair.ContainsKey(tm.Pair))
        ratesByPair.Add(tm.Pair, new List<Rate>());
      tm.CorridorStatsArray.Clear();
      foreach (var i in tm.CorridorIterationsArray)
        tm.CorridorStatsArray.Add(new CorridorStatistics(tm) { Iterations = i });
    }
    #endregion

    #endregion

  }
}
