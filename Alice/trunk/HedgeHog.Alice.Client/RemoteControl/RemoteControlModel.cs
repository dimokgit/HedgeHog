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
using System.Reflection;
using System.Windows.Controls.Primitives;
using HedgeHog.Alice.Store.Metadata;
namespace HedgeHog.Alice.Client {
  [Export]
  public class RemoteControlModel : RemoteControlModelBase {
    //Dimok:Show Closed trades

    #region Settings
    readonly double profitToClose = 1;
    #endregion

    #region members
    RatesLoader ratesLoader;
    Dictionary<TradingMacro, Corridors> charters = new Dictionary<TradingMacro, Corridors>();
    Corridors GetCharter(TradingMacro tradingMacro) {
      if (!charters.ContainsKey(tradingMacro)) {
        //Application.Current.Dispatcher.Invoke(new Action(() => {
        var charter = new Corridors(tradingMacro.Pair + "_" + tradingMacro.LimitBar, App.container);
        charter.StateChanged += new EventHandler(charter_StateChanged);
        charters.Add(tradingMacro, charter);
        App.ChildWindows.Add(charter);
        charter.CorridorStartPositionChanged += charter_CorridorStartPositionChanged;
        charter.SupportResistanceChanged += charter_SupportResistanceChanged;
        charter.Play += charter_Play;
        charter.GannAngleOffsetChanged += charter_GannAngleOffsetChanged;
        charter.Show();
        //}), System.Windows.Threading.DispatcherPriority.Send);
      }
      return charters[tradingMacro];
    }

    void charter_GannAngleOffsetChanged(object sender, GannAngleOffsetChangedEventArgs e) {
      var tm = GetTradingMacro((Corridors)sender);
      tm.GannAnglesOffset = e.Offset.Abs()/tm.GannAngle1x1;
      tm.SetGannAngles();
      ShowChart(tm);
    }

    void charter_Play(object sender, PlayEventArgs e) {
      var tm = GetTradingMacro((Corridors)sender);
      tm.SetPlayBackInfo(e.Play,e.StartDate,e.Delay);
    }

    void charter_SupportResistanceChanged(object sender, SupportResistanceChangedEventArgs e) {
      var tm = GetTradingMacro((Corridors)sender);
      if (e.IsSupport) tm.SupportPrice = e.NewPosition;
      else tm.ResistancePrice = e.NewPosition;
    }

    void charter_CorridorStartPositionChanged(object sender, CorridorPositionChangedEventArgs e) {
      var tm = GetTradingMacro((Corridors)sender);
      if (tm.CorridorStartDate == e.NewPosition) return;
      tm.CorridorStartDate = e.NewPosition;
      tm.ScanCorridor();
      ShowChart(tm);
    }

    private TradingMacro GetTradingMacro(Corridors sender) {
      var tm = charters.First(kv => kv.Value == sender).Key;
      return tm;
    }

    void charter_StateChanged(object sender, EventArgs e) {
      var charterMinimized = (sender as Window).WindowState == WindowState.Minimized;
      var tm = charters.Where(ch => ch.Value == sender).Single().Key;
      tm.IsCharterMinimized = charterMinimized;
    }



    #endregion

    #region Properties

    double CurrentLoss { get { return TradingMacrosCopy.Sum(tm => tm.CurrentLoss); } }

    Dictionary<string, Tick[]> ticksByPair = new Dictionary<string, Tick[]>();
    Dictionary<string, double> anglesByPair = new Dictionary<string, double>();

    #endregion

    #region Commands

    #region [Sell|Buy]OrderCommand
    void SellOrderCommand(Store.OrderTemplate ot) { OpenEntryOrderByTemplate(ot, false); }
    void BuyOrderCommand(Store.OrderTemplate ot) { OpenEntryOrderByTemplate(ot, true); }

    private void OpenEntryOrderByTemplate(Store.OrderTemplate ot, bool buy) {
      var messageHeader = (buy ? "Buy" : "Sell") + " Order";
      var stop = (buy ? -ot.Stop : ot.Stop);
      var limit = (buy ? ot.Limit : -ot.Limit);
      var price = fw.GetPrice(ot.Pair);
      var lot = ot.Lot * 1000 + tradesManager.GetTrades(ot.Pair).Where(t => t.Buy != buy).Sum(t => t.Lots);
      if (ot.Price == 0) {
        try {
          object psOrderId, DI;
          var slFLag = buy?(fw.Desk.SL_PEGLIMITOPEN+fw.Desk.SL_PEGSTOPOPEN):(fw.Desk.SL_PEGLIMITOPEN+fw.Desk.SL_PEGSTOPOPEN);
          fw.Desk.OpenTrade2(fw.AccountID, ot.Pair, buy, lot, 0, "", 0, slFLag, stop, limit, 0, out psOrderId, out DI);
        } catch (Exception exc) { MessageBox.Show(exc + "", messageHeader); }
      } else {
        var openPrice = buy ? price.Ask : price.Bid;
        var isPricePegged = ot.Price % 1.0 == 0;
        var rate = ot.Price == 0 ? 0 : isPricePegged ? openPrice + fw.InPoints(ot.Pair, ot.Price) : ot.Price;
        var stopOffset = price.Spread + fw.InPoints(ot.Pair, ot.Stop);
        var limitOffset = price.Spread + fw.InPoints(ot.Pair, ot.Limit);
        var orderInfo = string.Format("Order - Buy:{3},Rate:{0},Stop:{1},Limit:{2}", rate, stop, limit, buy);
        //if (MessageBox.Show(orderInfo + "?!", messageHeader, MessageBoxButton.OKCancel) == MessageBoxResult.Cancel) return;
        try {
          var orderId = fw.CreateEntryOrder(ot.Pair, buy, lot, rate, stop, limit);
        } catch (Exception exc) {
          MessageBox.Show(orderInfo + Environment.NewLine + exc.Message, messageHeader);
        }
      }
    }
    #endregion


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
        tm.CorridorIterationsOut, tm.CorridorIterations, tm.CorridorBarMinutes, tm.PairIndex, tm.TradingGroup, tm.MaximumPositions,
        tm.IsActive, "", tm.LimitCorridorByBarHeight, tm.MaxLotByTakeProfitRatio, tm.BarPeriodsLow, tm.BarPeriodsHigh,
        tm.StrictTradeClose, tm.BarPeriodsLowHighRatio, tm.LongMAPeriod, tm.CorridorAverageDaysBack, tm.CorridorPeriodsStart,
        tm.CorridorPeriodsLength, tm.CorridorRatioForRange, tm.CorridorRatioForBreakout, tm.RangeRatioForTradeLimit,
        tm.TradeByAngle, tm.ProfitToLossExitRatio, tm.CorridorHeightBySpreadRatio, tm.PowerRowOffset,tm.PowerVolatilityMinimum,
        tm.RangeRatioForTradeStop,tm.ReversePower,tm.CorrelationTreshold,tm.TradeByPowerVolatilty,tm.TradeByPowerAverage,
        tm.CloseOnProfitOnly,tm.CloseOnProfit,tm.CloseOnOpen,tm.StreachTradingDistance,tm.CloseAllOnProfit,tm.ReverseStrategy,
        tm.TradeAndAngleSynced,tm.TradingAngleRange,tm.CloseByMomentum,tm.TradeByRateDirection,tm.GannAngleIndexMinimum);
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
        var tm = tradingMacro as TradingMacro;
        tradesManager.CloseTradesAsync(tradesManager.GetTrades(tm.Pair));
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
        //if (MessageBox.Show("Buy " + tm.LotSizeByLoss.ToString("c0"), "Trade Confirmation", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
        tradesManager.ClosePair(tm.Pair);
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
        var tm = tradingMacro as TradingMacro;
        tradesManager.ClosePair(tm.Pair);
        OpenChainTrade(tm, false);
        //AddPendingOrder(false, tm.Pair, () => openTradeCondition(tm, false), () => OpenTrade(tm, false));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }



    ICommand _ShowPropertiesDialogCommand;
    public ICommand ShowPropertiesDialogCommand {
      get {
        if (_ShowPropertiesDialogCommand == null) {
          _ShowPropertiesDialogCommand = new Gala.RelayCommand<object>(ShowPropertiesDialog, (o) => true);
        }

        return _ShowPropertiesDialogCommand;
      }
    }
    void ShowPropertiesDialog(object o) {
      var tm = o as TradingMacro;
      if (tm == null) MessageBox.Show("ShowPropertiesDialog needs TradingMacro");
      else tm.ShowProperties = !tm.ShowProperties;
    }


    ICommand _HidePropertiesDialogCommand;
    public ICommand HidePropertiesDialogCommand {
      get {
        if (_HidePropertiesDialogCommand == null) {
          _HidePropertiesDialogCommand = new Gala.RelayCommand<Popup>(HidePropertiesDialog, (tm) => true);
        }

        return _HidePropertiesDialogCommand;
      }
    }
    void HidePropertiesDialog(Popup ke) {
      var tm = ke.DataContext as TradingMacro;
      tm.ShowProperties = false;
    }

    #endregion

    #region Ctor
    void CleanEntryOrders() {
      try {
        var trades = tradesManager.GetTrades();
        foreach (var order in tradesManager.GetOrders(""))
          if (!trades.Any(t => t.Pair == order.Pair)) tradesManager.DeleteOrder(order.OrderID);
      } catch (Exception exc) {
        Log = exc;
      }
    }
    public RemoteControlModel() {
      try {
        if (!IsInDesigh) {
          InitializeModel();
          App.container.SatisfyImportsOnce(this);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Store.OrderTemplate>(this, (object)false, SellOrderCommand);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Store.OrderTemplate>(this, (object)true, BuyOrderCommand);
          //GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<bool>(this, typeof(VirtualTradesManager), vt => { MessageBox.Show("VirtualTradesManager:" + vt); });
          fw = new FXW(MasterModel.CoreFX);
          virtualTrader = new VirtualTradesManager(MasterModel.LoginInfo.AccountId, 10000, GetTradingMacros().First().Rates);
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

    protected override void MasterModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
      base.MasterModel_PropertyChanged(sender, e);
      try {
        if (Lib.GetLambda(() => MasterModel.IsInVirtualTrading) == e.PropertyName) {
          var isInVirtualTrading = (bool)sender.GetProperty(e.PropertyName);
          GetTradingMacros().ToList().ForEach(tm => tm.LoadRates());
        }
      } catch (Exception exc) {
        Log = exc;
      }

    }
    void OnVirtual(bool isInVirtualTrading) {
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

    SchedulersDispenser<TradingMacro> ShowChartSchedulersDispenser = new SchedulersDispenser<TradingMacro>();
    void TradingMacro_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
      try {
        var tm = sender as TradingMacro;
        var propsToHandle = new[] { TradingMacroMetadata.Pair, TradingMacroMetadata.TradingRatio };
        if (propsToHandle.Contains(e.PropertyName)) SetLotSize(tm, tradesManager.GetAccount());
        //if (e.PropertyName == Lib.GetLambda(() => tm.OverlapToStop)) LoadRates(tm.Pair);
        propsToHandle = new[] { TradingMacroMetadata.CorridorBarMinutes, TradingMacroMetadata.LimitBar };
        if (propsToHandle.Contains(e.PropertyName)) {
          if (tm.LimitBar == 0) {
            tm.CorridorStartDate = null;
          }
          tm.Rates.Clear();
        }
        if (TradingMacroMetadata.CorridorStats == e.PropertyName) {
          var rates = tm.RatesCopy();
          ShowChartSchedulersDispenser.Run(tm,() => ShowChart(tm,rates));
        }
        if (e.PropertyName == TradingMacroMetadata.CorridorIterations)
          tm.CorridorStatsArray.Clear();
        if (e.PropertyName == TradingMacroMetadata.IsActive && ShowAllMacrosFilter)
          RaisePropertyChanged(() => TradingMacrosCopy);

        if (e.PropertyName == TradingMacroMetadata.CurrentLoss) {
          System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => {
            try {
              MasterModel.CurrentLoss = CurrentLoss;
              GlobalStorage.Context.SaveChanges();
            } catch (Exception exc) {
              Log = exc;
            }
          }));
          if( e.PropertyName == TradingMacroMetadata.IsActive){
            if (tm.IsActive) tm.SubscribeToTradeClosedEVent(tradesManager);
          }
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
          tradesManager.TradeClosed += fw_TradeClosed;
          tradesManager.OrderAdded += fw_OrderAdded;
          tradesManager.Error += fw_Error;
        }
        foreach (var tm in TradingMacrosCopy) {
          tm.CurrentLot = tradesManager.GetTrades().Where(t => t.Pair == tm.Pair).Sum(t => t.Lots);
          tm.LastTrade = tradesManager.GetLastTrade(tm.Pair);
        }
        MasterModel.CurrentLoss = CurrentLoss;
        if (!IsInVirtualTrading) {
          Instruments.Clear();
          (sender as CoreFX).Instruments.ToList().ForEach(i => Instruments.Add(i));
        }
      } catch (Exception exc) { MessageBox.Show(exc + ""); }
    }

    void CoreFX_LoggedOffEvent(object sender, EventArgs e) {
      if (tradesManager != null) {
        tradesManager.PriceChanged -= fw_PriceChanged;
        tradesManager.TradeAdded -= fw_TradeAdded;
        tradesManager.TradeRemoved -= fw_TradeRemoved;
        tradesManager.OrderAdded -= fw_OrderAdded;

        tradesManager.Error -= fw_Error;
      }
    }

    void fw_PriceChanged(object sender,PriceChangedEventArgs e) {
      Price price = e.Price;
      try {
        var sw = Stopwatch.StartNew();
        if (price != null) SetCurrentPrice(price);
        var pair = price.Pair;
        foreach (var tm in GetTradingMacros(pair).Where(t => !IsInVirtualTrading && !t.IsInPlayback || IsInVirtualTrading && t.IsInPlayback)) {
          tm.RunPriceChanged(e,OpenTradeByStop);
        }
        CheckTrades(e.Trades.ByPair(price.Pair));
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void ShowChart(TradingMacro tm) {
      ShowChart(tm, (Rate[])tm.Rates.ToArray().Clone());
    }
    void ShowChart(TradingMacro tm, Rate[] rates) {
      try {
        string pair = tm.Pair;
        var charter = GetCharter(tm);
        if (tm.IsCharterMinimized) return;
        if (tm == null) return;
        if (rates.Count() == 0) return;
        if (tm.LimitBar > 0) rates.SetStartDateForChart();
        var price = GetCurrentPrice(pair);
        price.Digits = tradesManager.GetDigits(pair);
        var csFirst = tm.CorridorStats;
        if (csFirst == null) return;
        var timeHigh = rates.Skip(rates.Count() - csFirst.Periods).Min(r => r.StartDateContinuous);
        var timeCurr = tm.LastTrade.Pair == tm.Pair && !tm.LastTrade.Buy ? new[] { tm.LastTrade.Time, tm.LastTrade.TimeClose }.Max() : DateTime.MinValue;
        var timeLow = tm.LastTrade.Pair == tm.Pair && tm.LastTrade.Buy ? new[] { tm.LastTrade.Time, tm.LastTrade.TimeClose }.Max() : DateTime.MinValue;
        bool? trendHighlight = csFirst.TrendLevel == TrendLevel.None ? (bool?)null : csFirst.TrendLevel == TrendLevel.Resistance;
        var priceBars = tm.GetPriceBars(true).OrderBars().Select(pb=>pb.Clone() as PriceBar).ToArray();
        if (tm.LimitBar > 0)
          (from r in rates
           join pb in priceBars on r.StartDate equals pb.StartDate
           select new { pb, r.StartDateContinuous }
          ).ToList().ForEach(a => a.pb.StartDateContinuous = a.StartDateContinuous);
        var powerBars = priceBars.Select(pb => pb.Power).ToArray();
        var stDevPower = priceBars.Average(pb=>pb.Power);
        var stAvgPower = priceBars.StdDev(pb => pb.Power);
        var dateMin = rates.Min(r => r.StartDateContinuous);
        string[] info = new string[] { 
          "Range:" + string.Format("{0:n0} @ {1:HH:mm:ss}", tradesManager.InPips(pair, rates.Height()),tradesManager.ServerTime),
          "RngCr:" + tradesManager.InPips(pair, tm.BigCorridorHeight).ToString("n1"),
          "Spred:" + string.Format("{2:00.0}/{0:00.0}={1:n1}",tm.SpreadLongInPips,tm.CorridorHeightToSpreadRatio,tm.CorridorHeightByRegressionInPips)
        };
        //RunWithTimeout.WaitFor<object>.Run(TimeSpan.FromSeconds(1), () => {
          //charter.Dispatcher.Invoke(new Action(() => {
            try {
              charter.CorridorHeightMultiplier = csFirst.HeightUpDown0 / csFirst.HeightUpDown;// tm.CorridorHeightMultiplier;
              charter.PriceBarValue = pb => pb.Power;
              charter.SetPriceLineColor(tm.BuyWhenReady ? true : tm.SellWhenReady ? false : (bool?)null);
              charter.GetPriceFunc = r => r.PriceAvg > r.PriceAvg1 ? TradingMacro.GetPriceHigh(r) : TradingMacro.GetPriceLow(r);
              charter.CenterOfMass = tm.CenterOfMass.PriceAvg;
              charter.SelectedGannAngleIndex = tm.GannAngleActive;
              charter.AddTicks(price, rates, new PriceBar[1][] { priceBars}, info, trendHighlight,
                tm.PowerAverage,  powerBars.AverageByIterations((v,a)=>v<=a,tm.IterationsForPower).Average(),// tm.PowerVolatility,
                tm.ResistancePrice, tm.SupportPrice,
                0/*csFirst.AverageHigh*/, 0/*csFirst.AverageLow*/,
                timeHigh, timeCurr, timeLow,
                new double[0]);
            } catch (Exception exc) {
              Log = exc;
            }
          //}), System.Windows.Threading.DispatcherPriority.Normal);
        //  return null;
        //});
      } catch (Exception exc) {
        Log = exc;
      }
      //    };
    }

    private void RunPrice(TradingMacro tm,Trade[] trades) {
      string pair = tm.Pair;
      var sw = Stopwatch.StartNew();
      try {
        if (!CanTrade(tm)) return;
        Price price = GetCurrentPrice(pair);
        if (!price.IsReal) price = tradesManager.GetPrice(pair);
        if (tm == null) return;
        var account = accountCached = tradesManager.GetAccount();
        var minGross = tm.CurrentLoss + trades.Sum(t => t.GrossPL);// +tm.RunningBalance;
        if (tm.MinimumGross > minGross) tm.MinimumGross = minGross;
        tm.Net = trades.Length > 0 ? trades.Sum(t => t.GrossPL) : (double?)null;
        tm.CurrentLossPercent = (tm.CurrentLoss + tm.Net.GetValueOrDefault()) / account.Balance;
        tm.BalanceOnStop = account.Balance + tm.StopAmount.GetValueOrDefault();
        tm.BalanceOnLimit = account.Balance + tm.LimitAmount.GetValueOrDefault();
        SetLotSize(tm, account);
        tm.SetTradesStatistics(price, trades);
      } catch (Exception exc) { Log = exc; }
      if (sw.Elapsed > TimeSpan.FromSeconds(5))
        Log = new Exception("RunPrice(" + pair + ") took " + Math.Round(sw.Elapsed.TotalSeconds, 1) + " secods");
      //Debug.WriteLine("RunPrice[{1}]:{0} ms", sw.Elapsed.TotalMilliseconds, pair);
    }

    Dictionary<string, PendingOrder> pendingOrders = new Dictionary<string, PendingOrder>();
    Dictionary<string, Predicate<Trade>> pendingOpenRequest = new Dictionary<string, Predicate<Trade>>();
    void OpenTradeByStop(TradingMacro tm) {
      string pair = tm.Pair;
      if (tm == null || tm.CorridorStats == null) return;
      if (tm.Strategy == Strategies.None) return;
      //if (tm.CorridorStats.BuyStopByCorridor == 0 || tm.CorridorStats.SellStopByCorridor == 0) return;
      var trades = tradesManager.GetTrades(pair).OrderBy(t => t.Time).ToArray();

      var open = tm.OpenSignal;
      Action<Trade> setTradesLocks = t=>SetTradeLocks(tm,t,t.PL);
      var eh = new TradeRemovedEventHandler(setTradesLocks);
      tradesManager.TradeRemoved += eh;
      try {
        if (trades.Length > 0 && (tm.CloseOnProfit || tm.CloseAllOnProfit) && tm.Strategy != Strategies.Momentum) {
          //if (!((tm.Strategy & Strategies.Stop) == Strategies.None && tm.OpenSignal.HasValue)) {
          var lot = CalculateLot(tm,trades);
          var trade = trades.OrderBy(t => t.Lots).Last();
          if (open.HasValue && CanOpenTrade(tm, open.Value, trades) && trade.Buy == open.Value) { } else {
            if (tm.TradeByRateDirection && (trade.Buy && tm.RateDirection > 0 || !trade.Buy && tm.RateDirection < 0)) return;
            var tradeBuy = trade.Buy;
            var openDate = trade.Time;
            //var ratesSinceOpen = rates.SkipWhile(r => r.StartDate < openDate);var peakSinceOpen = tradeBuy ? ratesSinceOpen.Min(r => r.AskLow) : ratesSinceOpen.Max(r => r.BidHigh);
            var heightSinceOpen = -10000;// tradesManager.InPips(pair, tradeBuy ? trade.Close - peakSinceOpen : peakSinceOpen - trade.Close);
            var corridorHeight = tm.CorridorHeightByRegressionInPips;
            var closeProfit = corridorHeight * tm.RangeRatioForTradeLimit;
            var closeLoss = -corridorHeight * tm.RangeRatioForTradeStop;
            var pl = tm.CloseAllOnProfit ? trades.Max(t => t.PL) : trades/*.Where(t => new bool?(t.Buy) != buy)*/.GrossInPips();
            var gross = trades/*.Where(t => new bool?(t.Buy) != buy)*/.Sum(t => t.GrossPL);
            //if ((tm.CurrentLoss<0 && gross > -tm.CurrentLoss) || (tm.CurrentLoss>=0 && pl > closeProfit) || pl < closeLoss) {
            {
              if (pl > closeProfit || (!tm.CloseOnProfitOnly && pl < closeLoss) || (trade.Lots > tm.LotSize*10 && tm.CurrentLoss<0 && gross*2>-tm.CurrentLoss) || heightSinceOpen > corridorHeight) {
                tradesManager.ClosePair(pair);
              }
            }
          }
          //}
        }

        try {
          if (open.HasValue && CanOpenTrade(tm, open.Value, trades)) {
            var isHedged = tradesManager.GetAccount().Hedging;
            if (isHedged)
              tradesManager.ClosePair(pair, !open.Value);
            if (tm.ReverseOnProfit) OpenTradeWithWait(tm, open.Value, isHedged);
            return;
          }
          #region Close Trade
          var close = new[] { tm.CloseSignal }.Max();
          if (close.HasValue && (!tm.CloseOnProfitOnly || trades.GrossInPips() > 0)) {
            var tradesInOpositDirection = trades.Where(t => t.IsBuy == close.Value).ToArray();
            if (tradesInOpositDirection.Length > 0) {
              bool pendingOpen = false;
              if (false) {
                pendingOpenRequest[pair] = (trade) => {
                  pendingOpen = true;
                  try {
                    var tl = tradesManager.GetTrades(trade.Pair);
                    var tod = tl.Where(t => t.Buy == trade.Buy).ToArray();
                    if (tod.Length > 0) return false;
                    var tsd = tl.Where(t => t.Buy != trade.Buy).ToArray();
                    if (CanOpenTrade(tm, open.Value, tl))
                      OpenTradeWithWait(tm, !trade.Buy, false);
                    pendingOpen = false;
                  } catch (Exception exc) {
                    Log = exc;
                    pendingOpen = false;
                  }
                  return true;
                };
              }
              try {
                //fw.TradeClosed += tradeClosed;
                if (!tradesManager.ClosePair(pair, close.Value)) {
                  foreach (var trade in tradesManager.GetTrades(pair).Where(t => t.IsBuy != open).ToArray())
                    tradesManager.FixOrderClose(trade.Id);
                }
                while (!IsInVirtualTrading && pendingOpen)
                  Thread.Sleep(100);
              } catch (Exception exc) {
                Log = exc;
                return;
              } finally {
                //fw.TradeClosed -= tradeClosed;
                RunPrice(tm,trades);
                open = tm.OpenSignal;
              }
              tm.ScanCorridor();
              //return;
            }
          }
          #endregion
          #region Open Trade
          open = tm.OpenSignal;
          if (open.HasValue && CanOpenTrade(tm, open.Value, trades) && tm.ReverseOnProfit) {
            OpenTradeWithWait(tm, open.Value, false);
          }
          #endregion
        } catch (Exception exc) {
          Log = exc;
        } finally {
          //if (IsInVirtualTrading) ShowChart(pair);
        }
      } finally {
        tradesManager.TradeRemoved -= eh;
      }
    }

    void tradesManager_TradeRemoved(Trade trade) {
      throw new NotImplementedException();
    }

    private static void SetTradeLocks(TradingMacro tm, Trade trade, double pl) {
      if (false && (tm.Strategy & Strategies.Breakout) == Strategies.Breakout) {
        if (trade.Buy) tm.IsSellLock = true;
        else tm.IsBuyLock = true;
      }
      if ((tm.Strategy & Strategies.Range) == Strategies.Range)
        if (!trade.Buy) tm.IsBuyLock = true;
        else tm.IsSellLock = true;
      //if (tm.Strategy == Strategies.Breakout) tm.Strategy = Strategies.None;
      if ((tm.Strategy & Strategies.Stop) == Strategies.Stop) tm.Strategy = Strategies.None;
    }

    private static bool CloseTradeByRateDirection(TradingMacro tm, Trade t) {
      return (t.Buy && tm.RateDirection < 0 || !t.Buy && tm.RateDirection > 0);
    }

    private bool CanOpenTrade(TradingMacro tm,bool isBuy,Trade[] trades) {
      string pair = tm.Pair;
      var opositeTrades = trades.Where(t => t.Buy != isBuy).ToArray();
      var tradesInSameDirection = trades.Where(t => t.Buy == isBuy).ToArray();
      if (tm.CloseOnProfitOnly && opositeTrades.Length > 0 && opositeTrades.GrossInPips() < 0) return false;
      if (isBuy && tm.TradeDirection == TradeDirections.Down) return false;
      if (!isBuy && tm.TradeDirection == TradeDirections.Up) return false;
      var date = tm.Rates.Last().StartDate;
      if (!tm.IsTradingHours) return false;
      var tradeTimeFrameOk = true;// tradesInSameDirection.Length == 0 || date - tradesInSameDirection.Max(t => t.Time) > TimeSpan.FromHours(8);
      var tradeDistanceMin = Math.Max(tm.TradingDistance, tradesManager.InPips(pair, tm.Rates.Height()));
      var istradeDistanceOk = tradesInSameDirection.Length == 0 || tradesInSameDirection.Max(t => t.PL) <= -tradeDistanceMin * (tm.StreachTradingDistance ? tradesInSameDirection.Length : 1);
      if (!tradeTimeFrameOk) return false;
      return (tradesInSameDirection.Length < tm.MaximumPositions)
                  && (!IsInVirtualTrading || tradesManager.IsInTest)
                  && istradeDistanceOk
                  && tm.IsPowerVolatilityOk
                  && tm.IsPowerAverageOk
                  && tm.IsCorridorToRangeRatioOk
                  && !pendingOrders.ContainsKey(pair)
                  ;
    }

    private static Func<Trade, bool> HasNoProfit(Trade[] trades, TradingMacro tm) {
      return t => !HasProfit(trades, tm)(t);
    }
    private static Func<Trade, bool> HasProfit(Trade[] trades, TradingMacro tm) {
      return t => t.PL > tm.TakeProfitPips * Math.Sqrt(trades.Length);
    }


    private void OpenTradeWithWait(TradingMacro tm, bool buy,bool isHedged) {
      string pair = tm.Pair;
      #region Pending Order Setup
      Action<object, RequestEventArgs> reqiesFailedAction = (s, e) => {
        if (pendingOrders.ContainsKey(pair) && e.ReqiestId == pendingOrders[pair].RequestId) {
          pendingOrders.Remove(pair);
          Log = new Exception(e.Error);
        }
      };
      Action<Order> orderRemovedAvtion = order => {
        var o = order.FixStatus;
      };
      var rfh = new EventHandler<RequestEventArgs>(reqiesFailedAction);
      var orh = new OrderRemovedEventHandler(orderRemovedAvtion);
      #endregion
      try {
        var trades = tradesManager.GetTrades(pair);
        var currentCount = trades.Length;
        tradesManager.RequestFailed += rfh;
        tradesManager.OrderRemoved += orh;
        //po = OpenChainTrade(tm, buy.Value);
        var lot = AllowedLotSize(tm, trades.Where(t => t.Buy == buy).ToArray()) + (!isHedged ? trades.Where(t => t.Buy != buy).Select(t => t.Lots).Sum() : 0);
        if (lot > 0) {
          var po = OpenTrade(buy, pair, lot, tm.TakeProfitPips, 0, 0, "");
          pendingOrders.Add(pair, po);
          var start = DateTime.Now;
          var stop = TimeSpan.FromSeconds(30);
          while (pendingOrders.ContainsKey(pair) && tradesManager.GetTrades(pair).Count() == currentCount && (DateTime.Now - start) < stop)
            Thread.Sleep(100);
        }
      } catch (Exception exc) {
        Log = exc;
      } finally {
        tradesManager.RequestFailed -= rfh;
        tradesManager.OrderRemoved -= orh;
        if (pendingOrders.ContainsKey(pair)) pendingOrders.Remove(pair);
      }
    }

    private int AllowedLotSize(TradingMacro tm,ICollection<Trade> trades) {
      return Math.Min(tm.MaxLotSize(trades)/* - trades.Sum(t=>t.Lots)*/, Math.Max(tm.LotSize, CalculateLot(tm,trades)));
    }

    public void fw_TradeAdded(Trade trade) {
      var tm = GetTradingMacros(trade.Pair).First();
      if (tm.LastTrade.Time < trade.Time) tm.LastTrade = trade;
      var trades = tradesManager.GetTrades(trade.Pair);
      tm.Trades = trades;
      tm.CurrentLot = trades.Sum(t => t.Lots);
      var amountK = tm.CurrentLot / 1000;
      if (tm.HistoryMaximumLot < amountK) tm.HistoryMaximumLot = amountK;
      var ts = tm.SetTradeStatistics(GetCurrentPrice(tm.Pair), trade);
      //MessageBox.Show(trade.PropertiesToString(Environment.NewLine), "Open");
    }

    void fw_OrderAdded(object sender, OrderEventArgs e) {
      Order order = e.Order;
      if (order.IsEntryOrder) {
        var po = GetPendingFxOrder(order.Pair);
        if (po != null)
          new ThreadScheduler(TimeSpan.FromSeconds(5), ThreadScheduler.infinity, () => pendingFxOrders.Remove(po));
      }
    }

    void fw_Error(object sender, HedgeHog.Shared.ErrorEventArgs e) {
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

    void fw_TradeClosed(object sender, TradeEventArgs e) {
      var trade = e.Trade;
    }

    void fw_TradeRemoved(Trade trade) {
      try {
        new ThreadScheduler(TimeSpan.FromSeconds(1), ThreadScheduler.infinity, () => {
          if (tradesManager.GetTrades(trade.Pair).Length == 0) RemoveEntryOrder(trade.Pair);
        }, (s, e) => Log = e.Exception);
        var pair = trade.Pair;
        foreach (var tm in GetTradingMacros(pair)) {

          //if (trade.Buy) tm.CorridorStats.IsBuyLock = false;        else tm.CorridorStats.IsSellLock = false;
          trade.TimeClose = tradesManager.ServerTime;
          tm.LastTrade = trade;
          var commission = MasterModel.CommissionByTrade(trade);
          Debug.WriteLine("Commission:" + commission);
          var totalGross = trade.GrossPL - commission;
          tm.RunningBalance += totalGross;
          tm.CurrentLoss = tm.CurrentLoss + totalGross;

          var trades = tradesManager.GetTrades(trade.Pair);
          tm.PositionsBuy = trades.Count(t => t.Buy);
          tm.PositionsSell = trades.Count(t => !t.Buy);
          tm.Trades = trades;
          AdjustCurrentLosses(tm);
          tm.CurrentLot = trades.Sum(t => t.Lots);
          tm.ScanCorridor();
        }
        try {
          var tm = GetTradingMacros(pair).First();
          System.IO.File.AppendAllText("ClosedTrades_new.xml", Environment.NewLine + trade);
          var ts = trade.InitUnKnown<TradeUnKNown>().InitTradeStatistics(tm.GetTradeStatistics(trade));
          ts.SessionId = TradingMacro.SessionId;
          ts.PowerAverage = tm.PowerAverage;
          ts.PowerVolatility = tm.PowerVolatility;
          MasterModel.AddCosedTrade(trade);
        } catch (Exception exc) { Log = exc; }

        if (pendingOpenRequest.ContainsKey(pair))
          try {
            if (pendingOpenRequest[pair](trade)) pendingOpenRequest.Remove(pair);
          } catch (Exception exc) {
            Log = exc;
            pendingOpenRequest.Remove(pair);
          }
        //MessageBox.Show(trade.PropertiesToString(Environment.NewLine), "Close");
      } catch (Exception exc) {
        Log = exc;
      }
      //fw.FixOrderOpen(trade.Pair, !trade.IsBuy, lot, limit, stop, trade.GrossPL < 0 ? trade.Id : "");
    }

    private int CalculateLot(TradingMacro tm, ICollection<Trade> trades) {
      Func<int, int> returnLot = d => Math.Max(tm.LotSize, d);
      if (tm.FreezeStopType == Freezing.Freez)
        return returnLot(trades.Sum(t => t.Lots) * 2);
      var currentLoss = GetCurrentLossByGroup(tm);
      var grossPL = GetGrossPLByGroup(tm,trades);
      return returnLot(CalculateLotCore(tm, currentLoss + grossPL));
    }
    double GetGrossPLByGroup(TradingMacro tm,ICollection<Trade> trades) {
      return GetTradingMacrosByGroup(tm).Sum(tm1 => trades.Sum(t => t.GrossPL));
    }
    private double GetCurrentLossByGroup(TradingMacro tm) {
      return GetTradingMacrosByGroup(tm).Sum(tm1 => tm1.CurrentLoss);
    }
    private int CalculateLotCore(TradingMacro tm, double totalGross) {
      return tradesManager.MoneyAndPipsToLot(Math.Min(0, totalGross).Abs(), tm.TakeProfitPips, tm.Pair);
    }
    #endregion

    #region Rate Loading
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
        GetRatesForCorridor(GetTradingMacros(pair).First()).Select(r => r.PriceAvg).ToArray();
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
    void AddCurrentTick(TradingMacro tm, Price price,int barMinutes) {
      var rates = tm.Rates;
      if (rates == null || rates.Count == 0) return;
      if (tm == null || tm.LimitBar == 0) {
        rates.RemoveAt(0);
        rates.Add(new Rate(price, false));
      } else {
        var priceTime = price.Time.Round(barMinutes);
        if (priceTime > rates.Last().StartDate)
          rates.Add(new Rate(priceTime, price.Ask, price.Bid, false));
        else rates.Last().AddTick(priceTime, price.Ask, price.Bid);
      }
    }

    Dictionary<string, Timer> timersByPair = new Dictionary<string, Timer>();

    #endregion

    #region Helpers


    #region CanTrade
    private bool CanTrade(TradingMacro tm) {
      return tm.Rates.Count() > 0;
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
    List<string> pendingFxOrders = new List<string>();
    bool HasPendingFxOrder(string pair) {
      return GetPendingFxOrder(pair) != null;
    }
    string GetPendingFxOrder(string pair) {
      return pendingFxOrders.SingleOrDefault(s => s == pair);
    }
    Order[] GetEntryOrders(string pair, bool isBuy) {
      return tradesManager.GetOrders("").Where(o => o.Pair == pair && o.IsBuy == isBuy).OrderBy(o => o.OrderID).ToArray();
    }
    Order GetEntryOrder(Trade trade) { return GetEntryOrder(trade.Pair, !trade.IsBuy); }
    Order GetEntryOrder(string pair, bool isBuy) {
      var orders = GetEntryOrders(pair, isBuy);
      try {
        if (orders.Length > 1) tradesManager.DeleteOrder(orders.First().OrderID);
      } catch (Exception exc) { Log = exc; }
      return GetEntryOrders(pair, isBuy).SingleOrDefault();
    }
    void ResetEntryOrder(Trade trade) {
      var order = GetEntryOrder(trade);
      if (order != null) order.Limit = 0;
    }
    void RemoveEntryOrder(string pair) {
      try {
        foreach (var order in tradesManager.GetOrders("").Where(o => o.Pair == pair))
          tradesManager.DeleteOrder(order.OrderID);
      } catch (Exception exc) {
        Log = exc;
      }
    }
    double GetEntryOrderRate(Trade trade) {
      return trade.Stop.Round(tradesManager.GetDigits(trade.Pair));
    }
    double GetEntryOrderLimit(Trade[] trades, int lot) {
      if (trades.Length == 0) return 0;
      var tm = GetTradingMacros(trades[0].Pair).First();
      var addProfit = tm.FreezeStopType == Freezing.Float;
      var loss = GetCurrentLossByGroup(tm);
      return Static.GetEntryOrderLimit(tradesManager, trades, lot, addProfit, loss);
    }

    #region UpdateEntryOrder
    /// <summary>
    /// Need this for parsed order that is not yet in orders table
    /// </summary>
    /// <param name="order"></param>
    private void UpdateEntryOrder(Trade[] trades) {
      try {
        return;
        if (trades.Length == 0) return;
        var trade = trades.OrderBy(t => t.Id).Last();
        var pair = trade.Pair;
        var tm = GetTradingMacros(pair).First();
        var order = GetEntryOrder(trade);
        if (order != null) {
          double rate = GetEntryOrderRate(trade);
          if (rate == 0) return;
          var period = tradesManager.GetDigits(pair);
          var lot = CalculateLot(tm, trades);
          if (order.Rate.Round(period) != rate) {
            if (order.Limit != 0)
              tradesManager.DeleteEntryOrderLimit(order.OrderID);
            if (order.TypeStop == 1 && order.Stop != 0)
              tradesManager.DeleteEntryOrderStop(order.OrderID);
            tradesManager.ChangeOrderRate(order, rate);
          }
          if (order.Lot != lot) {
            tradesManager.ChangeOrderAmount(order.OrderID, lot);
            order.Limit = 0;
          }
          if (order.Limit == 0) {
            var limit = GetEntryOrderLimit(trades, order.Lot).Round(period).Abs();
            tradesManager.ChangeEntryOrderPeggedLimit(order.OrderID, limit);
          }
        }
      } catch (Exception exc) { Log = exc; }
    }
    #endregion
    #endregion

    #region OpenTrade
    PendingOrder OpenChainTrade(TradingMacro tm, bool isBuy) {
      var trades = tradesManager.GetTrades(tm.Pair);
      var lot = AllowedLotSize(tm,trades);
      lot = lot - trades.Sum(t => t.Lots);
      if (lot <= 0) return null;
      var stop = tm.FreezeStopType == Freezing.None ? 0 : GetStopByFractal(0,tm,tm.Rates, isBuy, tradesManager.ServerTime);
      return OpenTrade(isBuy, tm.Pair, lot, tm.TakeProfitPips, 0, stop, "");
    }


    private PendingOrder OpenTrade(bool buy, string pair, int lot, double limitInPips, double stopInPips, double stop, string remark) {
      foreach(var tm in GetTradingMacros(pair))
        tm.ResetTradeReady();
      var price = tradesManager.GetPrice(pair);
      var limit = limitInPips == 0 ? 0 : buy ? price.Ask + tradesManager.InPoints(pair, limitInPips) : price.Bid - tradesManager.InPoints(pair, limitInPips);
      if (stop == 0 && stopInPips != 0)
        stop = buy ? price.Bid + tradesManager.InPoints(pair, stopInPips) : price.Ask - tradesManager.InPoints(pair, stopInPips);
      return tradesManager.OpenTrade(pair, buy, lot, limit, stop, remark, price);
    }
    #endregion

    #region Get (stop/limit)
    private double GetStopByFractal(double stopCurrent, TradingMacro tm, IEnumerable<Rate> rates, bool isBuy, DateTime tradeDate) {
      string pair = tm.Pair;
      if (!CanTrade(tm)) return 0;
      var stop = stopCurrent;
      var round = tradesManager.GetDigits(pair);
      try {
        if (rates.Count() > 0) {
          var stopSlack = GetFibSlack(tm.CorridorStats.FibMinimum, tm).Round(round);
          var ratesForStop = GetRatesForCorridor(rates, tm);
          var skip = Math.Min(tm.OverlapTotal, Math.Floor((tradesManager.ServerTime - tradeDate).TotalMinutes)).ToInt();
          ratesForStop = ratesForStop.OrderBarsDescending().Skip(skip).ToArray();
          if (ratesForStop.Count() == 0) {
            Log = new Exception("Pair [" + pair + "] has no rates.");
          } else {
            stop = isBuy ? ratesForStop.Min(r => r.BidLow) - stopSlack : ratesForStop.Max(r => r.AskHigh) + stopSlack;
            var price = tradesManager.GetPrice(pair);
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
      return GetRatesForCorridor(tm.Rates, tm);
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
    private double GetLimitByFractal(Trade[] trades, Trade trade, TradingMacro tm) {
      var rates = tm.Rates;
      string pair = trade.Pair;
      bool isBuy = trade.IsBuy;
      if (!CanTrade(tm)) return 0;
      var ratesForLimit = GetRatesForCorridor(rates, tm);
      var digits = tradesManager.GetDigits(pair);
      double limit = 0;
      Func<double> returnLimit = () => limit.Round(digits);
      if (tm.CorridorStats == null) return 0;
      switch (tm.FreezeType) {
        case Freezing.Float:
        case Freezing.Freez:
          var leg = GetFibSlack(tm.CorridorStats.FibMinimum, tm);
          //limit = isBuy ? tm.CorridorStats.AskHigh - leg : tm.CorridorStats.BidLow + leg;
          return returnLimit();
        case Freezing.None:
          //limit = isBuy ? tm.CorridorStats.AskHigh + tm.CorridorStats.AverageHeight : tm.CorridorStats.BidLow - tm.CorridorStats.AverageHeight;
          return returnLimit();
        default:
          var slack = GetFibSlack(tm.CorridorStats.FibMinimum, tm);
          var price = tradesManager.GetPrice(pair);
          limit = isBuy ? Math.Max(trade.Open, ratesForLimit.Max(r => r.BidHigh)) + slack
            : Math.Min(trade.Open, ratesForLimit.Min(r => r.AskLow)) - slack;
          if (isBuy && limit <= price.Bid) return 0;
          if (!isBuy && limit >= price.Ask) return 0;
          return limit.Round(digits);
      }
      if (tm.FreezeType != Freezing.Freez) {
        if (tm.TakeProfitPips == 0) return 0;
        return (trade.Open + tradesManager.InPoints(pair, isBuy ? tm.TakeProfitPips : -tm.TakeProfitPips)).Round(digits);
      }
    }

    #region GetSlack
    private double GetFibSlack(double fib, TradingMacro tm) {
      var slack = fib.FibReverse().YofS(tm.CorridorStats.Height);
      tm.SlackInPips = tradesManager.InPips(tm.Pair, slack);
      return slack;
    }
    #endregion
    #endregion

    void ChangeTradeStop(Trade trade, double stopAbsolute) {
      tradesManager.FixOrderSetStop(trade.Id, trade.Stop = stopAbsolute, "");
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
      var tm = GetTradingMacros(pair).First();
      if (tm.CorridorStats == null) return false;
      var lotSizeCalc = CalculateLot(tm,trades);
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
          tradesManager.FixOrderClose(trade.Id, fw.Desk.FIX_CLOSEMARKET, (Price)null, lotSizeRemove);
          return;
        }
        lotSizeRemove -= trade.Lots;
        tradesManager.FixOrderClose(trade.Id);
        tradesList.Remove(trade);
      }
    }
    private void CheckTrade(Trade[] trades, Trade trade, double stopAmount) {
      /*
      var tm = GetTradingMacro(trade.Pair);
      var round = tradesManager.GetDigits(trade.Pair) - 1;
      if (tm.FreezeStopType != Freezing.None) {
        var stopNew = GetStopByFractal(trade).Round(round);
        var stopOld = trade.Stop.Round(round);
        if (DoStop(trade, tm, stopNew, round))
          ChangeTradeStop(trade, stopNew);
      }
      var limitNew = 0;// GetLimitByFractal(trades, trade, GetRatesByPair(trade.Pair)).Round(round);
      if (limitNew != 0 && (
        trade.IsBuy && limitNew <= GetCurrentPrice(trade.Pair).Bid ||
         !trade.IsBuy && limitNew >= GetCurrentPrice(trade.Pair).Ask))
        tradesManager.CloseTradeAsync(trade);
      else if (false && DoLimit(trade, tm, limitNew, round))
        tradesManager.FixCreateLimit(trade.Id, limitNew, "");
      //if (trade.Lots >= tm.LotSize * 10 && tm.CurrentLoss < 0 && trade.LimitAmount >= tm.CurrentLoss.Abs() * tm.LimitBar) tm.FreezLimit = true;
      if (stopAmount > 0 && tm.CurrentLoss == 0)
        RemoveEntryOrder(trade.Pair);
     */ 
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
        : TradesManagedStatic.GetLotstoTrade(account.Balance, tradesManager.Leverage(tm.Pair), tm.TradingRatio, tradesManager.MinimumQuantity);
      tm.LotSizePercent = tm.LotSize / account.Balance / tradesManager.Leverage(tm.Pair);
      tm.LotSizeByLoss = AllowedLotSize(tm,account.Trades);
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
            tradesManager.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
        } catch (Exception exc) {
          Log = exc;
        }
        RaisePropertyChangedCore("TradingMacros");
      }));
    }
    private void InitTradingMacro(TradingMacro tm) {
      tm.PropertyChanged += TradingMacro_PropertyChanged;
      tm.CorridorStatsArray.Clear();
      foreach (var i in tm.CorridorIterationsArray)
        tm.CorridorStatsArray.Add(new CorridorStatistics(tm) { Iterations = i });
      if (tm.IsActive)
        Application.Current.Dispatcher.BeginInvoke(new Action(() => {
          tm.SubscribeToTradeClosedEVent(tradesManager); 
          GetCharter(tm);
        }));
    }
    #endregion

    #endregion

  }
}
