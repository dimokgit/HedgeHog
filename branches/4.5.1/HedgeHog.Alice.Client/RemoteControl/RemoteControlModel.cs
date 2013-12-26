using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Objects;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using HedgeHog.Alice.Store;
using HedgeHog.Alice.Store.Metadata;
using HedgeHog.Bars;
using HedgeHog.Models;
//using HedgeHog.Schedulers;
using HedgeHog.Shared;
using Order2GoAddIn;
using Gala = GalaSoft.MvvmLight.Command;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using HedgeHog.Charter;
using System.Reactive.Concurrency;
using System.Threading;
using ReactiveUI;
using HedgeHog.UI;
using System.ComponentModel.Composition;
namespace HedgeHog.Alice.Client {
  [Export]
  public partial class RemoteControlModel : RemoteControlModelBase {
    //Dimok:Show Closed trades

    #region Settings
    readonly double profitToClose = 1;
    #endregion

    #region members
    TradingStatistics _tradingStatistics = new TradingStatistics();
    Dictionary<TradingMacro, CharterControl> charters = new Dictionary<TradingMacro, CharterControl>();
    void DeleteCharter(TradingMacro tradingMacro) {
      if (charters.ContainsKey(tradingMacro)) {
        try {
          var charter = charters[tradingMacro];
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CharterControl>(charter, (object)CharterControl.MessageType.Remove);
          charters.Remove(tradingMacro);
          //charter.Close();
        } catch (Exception exc) {
          Log = exc;
        }
      }
    }
    void RequestAddCharterToUI(CharterControl charter) {
      DispatcherScheduler.Current.Schedule(3.FromSeconds(), () =>
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CharterControl>(charter, (object)CharterControl.MessageType.Add));
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    CharterControl GetCharter(TradingMacro tradingMacro) {
      if (!charters.ContainsKey(tradingMacro)) {
        var charterNew = new CharterControl(tradingMacro.CompositeId, App.container) { tm = tradingMacro };
        RequestAddCharterToUI(charterNew);
        try {
          charters.Add(tradingMacro, charterNew);
        } catch (ArgumentException exc) {
          Log = new Exception(new { tradingMacro.Pair } + "", exc);
          return charters[tradingMacro];
        }
        charterNew.CorridorStartPositionChanged += charter_CorridorStartPositionChanged;
        charterNew.SupportResistanceChanged += charter_SupportResistanceChanged;
        charterNew.LineTimeShortChanged += charterNew_LineTimeShortChanged;
        charterNew.LineTimeMiddleChanged += charterNew_LineTimeMiddleChanged;
        charterNew.Play += charter_Play;
        charterNew.GannAngleOffsetChanged += charter_GannAngleOffsetChanged;
        charterNew.BuySellAdded += charter_BuySellAdded;
        charterNew.BuySellRemoved += charter_BuySellRemoved;
        charterNew.PlotterKeyDown += charterNew_PlotterKeyDown;
        var isSelectedBinding = new Binding(Lib.GetLambda(() => tradingMacro.IsSelectedInUI)) { Source = tradingMacro };
        charterNew.SetBinding(CharterControl.IsSelectedProperty, isSelectedBinding);
        var isActiveBinding = new Binding(Lib.GetLambda(() => tradingMacro.IsTradingActive)) { Source = tradingMacro };
        charterNew.SetBinding(CharterControl.IsActiveProperty, isActiveBinding);
        charterNew.TradeLineChanged += new EventHandler<PositionChangedBaseEventArgs<double>>(charterNew_TradeLineChanged);
        charterNew.ShowChart += new EventHandler(charterNew_ShowChart);
        //charter.Show();

        /*
        //Application.Current.Dispatcher.Invoke(new Action(() => {
        var charter = new Corridors(tradingMacro.CompositeId, App.container);
        charter.SetBinding(Window.TitleProperty, new Binding(TradingMacroMetadata.CompositeName) { Source = tradingMacro });
        charter.StateChanged += new EventHandler(charter_StateChanged);
        charters.Add(tradingMacro, charter);
        App.ChildWindows.Add(charter);
        charter.CorridorStartPositionChanged += charter_CorridorStartPositionChanged;
        charter.SupportResistanceChanged += charter_SupportResistanceChanged;
        charter.Play += charter_Play;
        charter.GannAngleOffsetChanged += charter_GannAngleOffsetChanged;
        charter.BuySellAdded += charter_BuySellAdded;
        charter.BuySellRemoved += charter_BuySellRemoved;
        charter.Show();
        //}), System.Windows.Threading.DispatcherPriority.Send);
         */
      }
      var charterOld = charters[tradingMacro];
      if (charterOld.Parent == null)
        RequestAddCharterToUI(charterOld);
      return charterOld;
    }

    void charterNew_ShowChart(object sender, EventArgs e) {
      AddShowChart(GetTradingMacro((CharterControl)sender));
    }

    void charterNew_TradeLineChanged(object sender, PositionChangedBaseEventArgs<double> e) {
      var tm = GetTradingMacro((CharterControl)sender);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<TradeLineChangedMessage>(new TradeLineChangedMessage(tm, e.NewPosition, e.OldPosition));
    }

    void charterNew_PlotterKeyDown(object sender, CharterControl.PlotterKeyDownEventArgs e) {
      var charter = (CharterControl)sender;
      var tm = GetTradingMacro(charter);
      switch (e.Key) {
        case Key.D0:
        case Key.NumPad0:
          tm.SetTradeCount(0);
          break;
        case Key.D1:
        case Key.NumPad1:
          tm.SetTradeCount(1);
          break;
        case Key.A:
          tm.IsTradingActive = !tm.IsTradingActive;
          charter.FitToView();
          break;
        case Key.C:
          tm.IsTradingActive = false;
          //tm.CloseTrades();
          tm.SetCanTrade(false);
          tm.SetTradeCount(0);
          charter.FitToView();
          tm.CorridorStartDate = null;
          break;
        case Key.G:
          tm.MakeGhosts();
          break;
        case Key.M:
          tm.ResetSuppResesInManual();
          break;
        case Key.T:
          tm.ToggleCanTrade();
          tm.SetTradeCount(0);
          break;
      }
    }

    void charterNew_LineTimeMiddleChanged(object sender, PositionChangedBaseEventArgs<DateTime> e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.IsTradingActive = false;
      tm.CorridorStopDate = e.NewPosition;
      tm.OnPropertyChangedCore(TradingMacroMetadata.CorridorStartDate);
    }

    void charterNew_LineTimeShortChanged(object sender, PositionChangedBaseEventArgs<DateTime> e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.IsTradingActive = false;
      var bar = tm.RatesArray.ReverseIfNot().TakeWhile(r=>r.StartDateContinuous>= e.NewPosition).Last();
      //tm.WaveShort.Distance = bar.Distance;
      //tm.CorridorStartDate = bar.StartDate;
      /////tm.OnPropertyChangedCore(TradingMacroMetadata.CorridorStartDate);
      //tm.ScanCorridor(tm.RatesArray);
    }

    void charter_BuySellRemoved(object sender, BuySellRateRemovedEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      try {
        tm.RemoveSuppRes(e.UID);
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void charter_BuySellAdded(object sender, BuySellRateAddedEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      try {
        tm.AddBuySellRate(e.Rate, e.IsBuy);
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void charter_GannAngleOffsetChanged(object sender, GannAngleOffsetChangedEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.GannAnglesOffset = e.Offset.Abs() / tm.GannAngle1x1;
      tm.SetGannAngles();
      AddShowChart(tm);
    }

    void charter_Play(object sender, PlayEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.SetPlayBackInfo(e.Play, e.StartDate, e.Delay);
    }

    void charter_SupportResistanceChanged(object sender, SupportResistanceChangedEventArgs e) {
      try {
        var tm = GetTradingMacro((CharterControl)sender);
        tm.UpdateSuppRes(e.UID, e.NewPosition);
        tm.IsTradingActive = false;
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void charter_CorridorStartPositionChanged(object sender, CorridorPositionChangedEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.IsTradingActive = false;
      if (tm.CorridorStartDate == e.NewPosition) return;
      var index = tm.RatesArray.IndexOf(new Rate() { StartDate2 = e.NewPosition.ToUniversalTime() });
      var rate = tm.RatesArray.GetRange(index - 5, 10).OrderByDescending(r => r.PriceHigh - r.PriceLow).First();
      tm.CorridorStartDate = rate.StartDate;
    }

    private TradingMacro GetTradingMacro(CharterControl sender) {
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


    #region LoadTradingSettings
    ICommand _LoadTradingSettingsCommand;
    public ICommand LoadTradingSettingsCommand {
      get {
        if (_LoadTradingSettingsCommand == null) {
          _LoadTradingSettingsCommand = new Gala.RelayCommand<TradingMacro>(LoadTradingSettings, (tm) => true);
        }

        return _LoadTradingSettingsCommand;
      }
    }
    void LoadTradingSettings(TradingMacro tm) {
      try {
        var od = new Microsoft.Win32.OpenFileDialog() { FileName = "Params_" + tm.Pair.Replace("/", ""), DefaultExt = ".txt", Filter = "Text documents(.txt)|*.txt" };
        var odRes = od.ShowDialog();
        if (!odRes.GetValueOrDefault()) return;
        var settings = Lib.ReadTestParameters(od.FileName);
        settings.ForEach(tp => tm.SetProperty(tp.Key, (object)tp.Value));
      } catch (Exception exc) {
        Log = exc;
        return;
      }
      Log = new Exception("Settings loaded.");
    }
    #endregion

    #region SaveTradingSettings
    ICommand _SaveTradingSettingsCommand;
    public ICommand SaveTradingSettingsCommand {
      get {
        if (_SaveTradingSettingsCommand == null) {
          _SaveTradingSettingsCommand = new Gala.RelayCommand<TradingMacro>(OnSaveTradingSettings, (tm) => true);
        }

        return _SaveTradingSettingsCommand;
      }
    }
    void OnSaveTradingSettings(TradingMacro tm) {
      try {
        //var attrs = new[] { TradingMacro.categoryActive, TradingMacro.categoryActiveFuncs };
        var settings = tm.GetPropertiesByAttibute<CategoryAttribute>(a => true)
          .OrderBy(a=>a.Item1.Category)
          .GroupBy(a => a.Item1.Category)
          .ToList().SelectMany(g =>
            new[] { "//{0}//".Formater(g.Key) }.Concat(g.Select(p => "{0}={1}".Formater(p.Item2.Name, p.Item2.GetValue(tm, null))).OrderBy(s => s)));
        var od = new Microsoft.Win32.SaveFileDialog() { FileName = "Params_" + tm.Pair.Replace("/", ""), DefaultExt = ".txt", Filter = "Text documents(.txt)|*.txt" };
        var odRes = od.ShowDialog();
        if (!odRes.GetValueOrDefault()) return;
        File.WriteAllLines(od.FileName, settings);
      } catch (Exception exc) {
        Log = exc;
        return;
      }
      Log = new Exception("Settings saved.");
    }
    #endregion


    #region [Sell|Buy]OrderCommand
    void SellOrderCommand(Store.OrderTemplate ot) { OpenEntryOrderByTemplate(ot, false); }
    void BuyOrderCommand(Store.OrderTemplate ot) { OpenEntryOrderByTemplate(ot, true); }

    private void OpenEntryOrderByTemplate(Store.OrderTemplate ot, bool buy) {
      var messageHeader = (buy ? "Buy" : "Sell") + " Order";
      var stop = (buy ? -ot.Stop : ot.Stop);
      var limit = (buy ? ot.Limit : -ot.Limit);
      var price = fwMaster.GetPrice(ot.Pair);
      var lot = ot.Lot * 1000 + tradesManager.GetTradesInternal(ot.Pair).Where(t => t.Buy != buy).Sum(t => t.Lots);
      if (ot.Price == 0) {
        try {
          object psOrderId, DI;
          throw new NotImplementedException();
          //var slFLag = buy?(fwMaster.Desk.SL_PEGLIMITOPEN+fwMaster.Desk.SL_PEGSTOPOPEN):(fwMaster.Desk.SL_PEGLIMITOPEN+fwMaster.Desk.SL_PEGSTOPOPEN);
          throw new NotImplementedException();
          //fwMaster.Desk.OpenTrade2(fwMaster.AccountID, ot.Pair, buy, lot, 0, "", 0, slFLag, stop, limit, 0, out psOrderId, out DI);
        } catch (Exception exc) { MessageBox.Show(exc + "", messageHeader); }
      } else {
        var openPrice = buy ? price.Ask : price.Bid;
        var isPricePegged = ot.Price % 1.0 == 0;
        var rate = ot.Price == 0 ? 0 : isPricePegged ? openPrice + fwMaster.InPoints(ot.Pair, ot.Price) : ot.Price;
        var stopOffset = price.Spread + fwMaster.InPoints(ot.Pair, ot.Stop);
        var limitOffset = price.Spread + fwMaster.InPoints(ot.Pair, ot.Limit);
        var orderInfo = string.Format("Order - Buy:{3},Rate:{0},Stop:{1},Limit:{2}", rate, stop, limit, buy);
        //if (MessageBox.Show(orderInfo + "?!", messageHeader, MessageBoxButton.OKCancel) == MessageBoxResult.Cancel) return;
        try {
          var orderId = fwMaster.CreateEntryOrder(ot.Pair, buy, lot, rate, stop, limit);
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
          _CopyTradingMacroCommand = new Gala.RelayCommand<object>(CopyTradingMacros, (tm) => true || tm is TradingMacro);
        }

        return _CopyTradingMacroCommand;
      }
    }
    void CopyTradingMacros(object tradingMacro) {
      var tms = GetTradingMacros().Where(tm => tm.IsSelectedInUI).ToList();
      if (tms.Count > 1) {
        tms.ForEach(tm => {
          var tmName = tm.TradingMacroName.Split(' ')[0] + ' ' + (int.Parse(tm.TradingMacroName.Split(' ')[1]) + 1).ToString("00");
          CopyTradingMacro(tm, tmName);
        });
      } else
        CopyTradingMacro(tradingMacro);

    }
    void CopyTradingMacro(object tradingMacro, string tradingMacroName = null) {
      var tm = tradingMacro as TradingMacro;
      var pairIndex = GetTradingMacros().Select(aTM => aTM.PairIndex).DefaultIfEmpty().Max() + 1;
      var tmNew = TradingMacro.CreateTradingMacro(
        tm.Pair, tm.TradingRatio, Guid.NewGuid(), (int)tm.BarPeriod, tm.CurrentLoss, tm.ReverseOnProfit,
        tm.FreezLimit, tm.CorridorMethod, tm.FreezeStop, tm.FibMax, tm.FibMin, tm.CorridornessMin, tm.CorridorIterationsIn,
        tm.CorridorIterationsOut, tm.CorridorIterations, tm.CorridorBarMinutes, pairIndex, tm.TradingGroup, tm.MaximumPositions,
        tradingMacroName != null,
        tradingMacroName ?? tm.TradingMacroName,
        tm.LimitCorridorByBarHeight, tm.MaxLotByTakeProfitRatio, tm.BarPeriodsLow, tm.BarPeriodsHigh,
        tm.StrictTradeClose, tm.BarPeriodsLowHighRatio, tm.LongMAPeriod, tm.CorridorAverageDaysBack, tm.CorridorPeriodsStart,
        tm.CorridorPeriodsLength, tm.CorridorRatioForRange, tm.CorridorRatioForBreakout, tm.RangeRatioForTradeLimit,
        tm.TradeByAngle, tm.ProfitToLossExitRatio, tm.PowerRowOffset, tm.RangeRatioForTradeStop,
        tm.ReversePower, tm.CorrelationTreshold, tm.CloseOnProfitOnly, tm.CloseOnProfit, tm.CloseOnOpen, tm.StreachTradingDistance,
        tm.CloseAllOnProfit, tm.ReverseStrategy, tm.TradeAndAngleSynced, tm.TradingAngleRange, tm.CloseByMomentum, tm.TradeByRateDirection,
        tm.GannAngles, tm.IsGannAnglesManual, tm.SpreadShortToLongTreshold,
        tm.SuppResLevelsCount, tm.DoStreatchRates, tm.IsSuppResManual, tm.TradeOnCrossOnly, tm.TakeProfitFunctionInt,
        tm.DoAdjustTimeframeByAllowedLot, tm.IsColdOnTrades, tm.CorridorCrossesCountMinimum, tm.StDevToSpreadRatio,
        loadRatesSecondsWarning: tm.LoadRatesSecondsWarning, corridorHighLowMethodInt: tm.CorridorHighLowMethodInt,
        corridorStDevRatioMax: tm.CorridorStDevRatioMax,
        corridorLengthMinimum: tm.CorridorLengthMinimum, corridorCrossHighLowMethodInt: tm.CorridorCrossHighLowMethodInt,
        priceCmaLevels: tm.PriceCmaLevels, volumeTresholdIterations: tm.VolumeTresholdIterations, stDevTresholdIterations: tm.StDevTresholdIterations,
        stDevAverageLeewayRatio:tm.StDevAverageLeewayRatio, 
        extreamCloseOffset:tm.ExtreamCloseOffset,currentLossInPipsCloseAdjustment:tm.CurrentLossInPipsCloseAdjustment,corridorBigToSmallRatio:tm.CorridorBigToSmallRatio,
        voltageFunction:tm.VoltageFunction);
      tmNew.PropertyChanged += TradingMacro_PropertyChanged;
      //foreach (var p in tradingMacro.GetType().GetProperties().Where(p => p.GetCustomAttributes(typeof(DataMemberAttribute), false).Count() > 0))
      //  if (!(p.GetCustomAttributes(typeof(EdmScalarPropertyAttribute), false)
      //    .DefaultIfEmpty(new EdmScalarPropertyAttribute()).First() as EdmScalarPropertyAttribute).EntityKeyProperty
      //    && p.Name!="Pair"
      //    )
      //    tmNew.SetProperty(p.Name, tm.GetProperty(p.Name));
      try {
        GlobalStorage.UseAliceContext(c => c.AddToTradingMacroes(tmNew), true);
        TradingMacrosCopy_Add(tmNew);
        new Action(() => InitTradingMacro(tmNew)).ScheduleOnUI(2.FromSeconds());
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
      tm.IsActive = false;
      GlobalStorage.UseAliceContext(c => c.TradingMacroes.DeleteObject(tm), true);
      TradingMacrosCopy_Delete(tm);

    }

    Task loadHistoryTast;
    bool isLoadHistoryTaskRunning { get { return loadHistoryTast != null && loadHistoryTast.Status == TaskStatus.Running; } }
    ICommand _PriceHistoryCommand;
    public ICommand PriceHistoryCommand {
      get {
        if (_PriceHistoryCommand == null) {
          _PriceHistoryCommand = new Gala.RelayCommand<object>(PriceHistory, (o) => !isLoadHistoryTaskRunning);
        }

        return _PriceHistoryCommand;
      }
    }
    void PriceHistory(object o) {
      var tm = o as TradingMacro;
      if (tm.MonthsOfHistory <= 0) {
        Log = new ArgumentException(new { tm.MonthsOfHistory } + " must be more than zero.");
      } else {
        if (isLoadHistoryTaskRunning)
          MessageBox.Show("LoadHistoryTask is in " + loadHistoryTast.Status + " status.");
        else {
          Action a = () => { Store.PriceHistory.AddTicks(fwMaster, (int)tm.BarPeriod, tm.Pair, fwMaster.ServerTime.AddMonths(-tm.MonthsOfHistory), obj => Log = new Exception(obj + "")); };
          if (loadHistoryTast != null && !loadHistoryTast.Wait(0))
            Log = new Exception("Task is running.");
          else
            loadHistoryTast = Task.Factory.StartNew(a);
        }
      }
    }



    #region TrimPairCommand
    ICommand _TrimPairCommand;
    public ICommand TrimPairCommand {
      get {
        if (_TrimPairCommand == null) {
          _TrimPairCommand = new Gala.RelayCommand<TradingMacro>(TrimPair, (tm) => true);
        }

        return _TrimPairCommand;
      }
    }
    void TrimPair(TradingMacro tradingMacro) {
      tradingMacro.TrimTrades("TrimTrades Command");
    }
    #endregion

    ICommand _ClosePairCommand;
    public ICommand ClosePairCommand {
      get {
        if (_ClosePairCommand == null) {
          _ClosePairCommand = new Gala.RelayCommand<TradingMacro>(ClosePair, (tm) => true);
        }

        return _ClosePairCommand;
      }
    }

    void ClosePair(TradingMacro tradingMacro) {
      try {
        var tm = tradingMacro as TradingMacro;
        tradesManager.ClosePair(tm.Pair);
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
        var lot = tm.AllowedLotSizeCore();
        if (!tradesManager.GetAccount().Hedging)
          lot += tradesManager.GetTradesInternal(tm.Pair).IsBuy(false).Sum(t => t.Lots);
        OpenTrade(true, tm.Pair, lot, 0, 0, 0, "");
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
        var lot = tm.AllowedLotSizeCore();
        if (!tradesManager.GetAccount().Hedging)
          lot += tradesManager.GetTradesInternal(tm.Pair).IsBuy(true).Sum(t => t.Lots);
        OpenTrade(false, tm.Pair, lot, 0, 0, 0, "");
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

    #region SetStrategiesCommand

    ICommand _SetStrategiesCommand;
    public ICommand SetStrategiesCommand {
      get {
        if (_SetStrategiesCommand == null) {
          _SetStrategiesCommand = new Gala.RelayCommand<Strategies>(SetStrategies, (o) => true);
        }

        return _SetStrategiesCommand;
      }
    }
    void SetStrategies(Strategies strategy) {
      GetTradingMacros().ToList().ForEach(tm => tm.Strategy = strategy);
    }
    #endregion
    #endregion

    #region HidePropertiesDialogCommand
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


    #region ToggleCloseAtZero
    ICommand _ToggleCloseAtZeroCommand;
    public ICommand ToggleCloseAtZeroCommand {
      get {
        if (_ToggleCloseAtZeroCommand == null) {
          _ToggleCloseAtZeroCommand = new Gala.RelayCommand<object>(ToggleCloseAtZero, (tm) => true);
        }

        return _ToggleCloseAtZeroCommand;
      }
    }
    void ToggleCloseAtZero(object o) {
      var tm = o as TradingMacro;
      if (tm == null) MessageBox.Show("ToggleCloseAtZero needs TradingMacro");
      tm.CloseAtZero = !tm.CloseAtZero;
    }
    #endregion

    #region Ctor
    void CleanEntryOrders() {
      try {
        var fw = tradesManager as FXCoreWrapper;
        if (fw == null) return;
        var trades = tradesManager.GetTrades();
        foreach (var order in tradesManager.GetOrders(""))
          if (!trades.Any(t => t.Pair == order.Pair)) fw.DeleteOrder(order.OrderID);
      } catch (Exception exc) {
        Log = exc;
      }
    }
    CancellationTokenSource _threadCancelation = new CancellationTokenSource();
    public RemoteControlModel() {
      try {
        if (!IsInDesigh) {
          InitializeModel();
          App.container.SatisfyImportsOnce(this);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<LogMessage>(this, lm => Log = lm.Exception);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Store.OrderTemplate>(this, (object)false, SellOrderCommand);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Store.OrderTemplate>(this, (object)true, BuyOrderCommand);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<string>("Shutdown", (m) => {
            try {
              _replayTaskCancellationToken.Cancel();
            } catch (Exception exc) {
              Log = exc;
            }
          });

          //GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<bool>(this, typeof(VirtualTradesManager), vt => { MessageBox.Show("VirtualTradesManager:" + vt); });
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Window>(this, typeof(WindowState), IsMinimized);
          MasterModel.CoreFX.LoggedIn += CoreFX_LoggedInEvent;
          MasterModel.CoreFX.LoggedOff += CoreFX_LoggedOffEvent;
          MasterModel.MasterTradeAccountChanged += MasterModel_MasterTradeAccountChanged;
          MasterModel.NeedTradingStatistics += MasterModel_NeedTradingStatistics;
          MasterModel.TradingMacroNameChanged += new EventHandler<EventArgs>(MasterModel_TradingMacroNameChanged);
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void MasterModel_TradingMacroNameChanged(object sender, EventArgs e) {
      _TradingMacros = null;
      RaisePropertyChanged(() => TradingMacrosCopy);
    }


    void MasterModel_NeedTradingStatistics(object sender, TradingStatisticsEventArgs e) {
      e.TradingStatistics = _tradingStatistics;
    }

    void MasterModel_MasterTradeAccountChanged(object sender, EventArgs e) {
      RaisePropertyChanged(() => TradingMacrosCopy);
    }

    void UpdateTradingStatistics() {
      try {
        if (GetTradingMacros().Any(tm => !tm.RatesArray.Any())) return;
        var tms = GetTradingMacros().Where(tm => tm.Trades.Length > 0 && tm.Strategy != Strategies.None).ToArray();
        if (tms.Any() && tms.All(tm => tm.RatesArray.Any())) {
          var tp = (tms.Sum(tm => (tm.CloseOnOpen ? tm.TakeProfitPips : tm.CalcTakeProfitDistance(inPips: true)) * tm.Trades.Lots()) / tms.Select(tm => tm.Trades.Lots()).Sum()) / tms.Length;
          _tradingStatistics.TakeProfitDistanceInPips = tp;
        } else {
          _tradingStatistics.TakeProfitDistanceInPips = double.NaN;
        }
        tms = GetTradingMacros().Where(tm => tm.Strategy != Strategies.None).ToArray();
        if (tms.Any()) {
          _tradingStatistics.TradingMacros = tms;
          _tradingStatistics.StDevPips = tms.Select(tm => tm.InPips(tm.CorridorStats.RatesStDev)).ToList().AverageByIterations(1).Average();
          _tradingStatistics.TakeProfitPips = tms.Select(tm => tm.CalculateTakeProfitInPips()).ToList().AverageByIterations(2).Average();
          _tradingStatistics.VolumeRatioH = tms.Select(tm => tm.VolumeShortToLongRatio).ToArray().AverageByIterations(2).Average();
          _tradingStatistics.VolumeRatioL = tms.Select(tm => tm.VolumeShortToLongRatio).ToArray().AverageByIterations(2, true).Average();
          _tradingStatistics.RatesStDevToRatesHeightRatioH = tms.Select(tm => tm.RatesStDevToRatesHeightRatio).ToArray().AverageByIterations(2).Average();
          _tradingStatistics.RatesStDevToRatesHeightRatioL = tms.Select(tm => tm.RatesStDevToRatesHeightRatio).ToArray().AverageByIterations(2, true).Average();
          _tradingStatistics.AllowedLotMinimum = tms.Select(tm => tm.LotSizeByLossBuy.Max(tm.LotSizeByLossSell)).Min();
          var grosses = tms.Select(tm => tm.CurrentGross).Where(g => g != 0).DefaultIfEmpty().ToList();
          _tradingStatistics.CurrentGross = grosses.Sum(g => g);
          _tradingStatistics.CurrentGrossAverage = grosses.Average();
          _tradingStatistics.CurrentGrossInPips = tms.Sum(tm => tm.CurrentGrossInPips * tm.Trades.Lots()) / tms.Sum(tm => tm.Trades.Lots());
          _tradingStatistics.CurrentLoss = tms.Sum(tm => tm.CurrentLoss);
          _tradingStatistics.CurrentLossInPips = tms.Sum(tm => tm.CurrentLossInPips);
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }
    private void InitializeModel() {
      GlobalStorage.AliceMaterializerSubject
        .SubscribeOn(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher)
        .Subscribe(e => Context_ObjectMaterialized(null, e));
      //GlobalStorage.AliceContext.ObjectMaterialized += Context_ObjectMaterialized;
      GlobalStorage.AliceContext.ObjectStateManager.ObjectStateManagerChanged += ObjectStateManager_ObjectStateManagerChanged;
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
        MasterModel.CoreFX.LoggedIn -= CoreFX_LoggedInEvent;
        MasterModel.CoreFX.LoggedOff -= CoreFX_LoggedOffEvent;
        TradingMacrosCopy.ToObservable()
          .SubscribeOn(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher)
          .Take(10000)
          .Subscribe(tm => InitTradingMacro(tm, true));
      }
    }
    #endregion

    #region Event Handlers
    void ObjectStateManager_ObjectStateManagerChanged(object sender, System.ComponentModel.CollectionChangeEventArgs e) {
      var tm = e.Element as TradingMacro;
      if (tm != null) {
        if (tm.EntityState == System.Data.EntityState.Detached) {
          tm.PropertyChanged -= TradingMacro_PropertyChanged;
          tm.ShowChart -= TradingMacro_ShowChart;
        }
        //else if (tm.EntityState == System.Data.EntityState.Added) {
        //  tm.PropertyChanged += TradingMacro_PropertyChanged;
        //  InitTradingMacro(tm);
        //}
      }
    }

    void Context_ObjectMaterialized(object sender, ObjectMaterializedEventArgs e) {
      var tm = e.Entity as TradingMacro;
      if (tm == null) return;
      tm.PropertyChanged += TradingMacro_PropertyChanged;
      tm.ShowChart += TradingMacro_ShowChart;
      //new Action(() => InitTradingMacro(tm)).ScheduleOnUI(2.FromSeconds());
      InitTradingMacro(tm);
    }

    internal IScheduler findDispatcherScheduler() {
      Type result = null;
      try {
        result = Type.GetType("System.Reactive.Concurrency.DispatcherScheduler, System.Reactive.Windows.Threading", true);
      } catch {
      }
      if (result == null) {
        Log = new Exception("WPF Rx.NET DLL reference not added - using Event Loop"); return new EventLoopScheduler();
      }
      return (IScheduler)result.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
    }

    void TradingMacro_ShowChart(object sender, EventArgs e) {
      var tm = sender as TradingMacro;
      //SCD.Add(tm);
      AddShowChart(tm);
    }

    void TradingMacro_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
      try {
        var tm = sender as TradingMacro;

        if (e.PropertyName == TradingMacroMetadata.IsActive) {
          _tradingMacrosDictionary.Clear();
           ScheduleInitTradingMacro(tm);
        }
        if (e.PropertyName == TradingMacroMetadata.CurrentPrice) {
          try {
            if (tm.IsActive && tm.HasRates && !IsInVirtualTrading) {
              var charter = GetCharter(tm);
              charter.Dispatcher.Invoke(new Action(() => {
                charter.LineAvgAsk = tm.CurrentPrice.Ask;
                charter.LineAvgBid = tm.CurrentPrice.Bid;
                var high = tm.CalculateLastPrice(tm.RateLast, tm.ChartHighPrice());
                var low = tm.CalculateLastPrice(tm.RateLast, tm.ChartLowPrice());
                var ma = tm.CalculateLastPrice(tm.RateLast, tm.GetPriceMA());
                charter.SetLastPoint(high, low, ma, tm.RateLast); ;
                //Debug.WriteLineIf(tm.Pair == "EUR/JPY", string.Format("Current price:{0} @ {1:mm:ss}", tm.CurrentPrice.Average.Round(3), tm.CurrentPrice.Time));
              }), DispatcherPriority.Send);
            }
          } catch (Exception exc) {
            Log = exc;
          }
        }


        if (e.PropertyName == TradingMacroMetadata.GannAngles_) {
          tm.SetGannAngles();
          AddShowChart(tm);
        }

        if (e.PropertyName == TradingMacroMetadata.Log) {
          Log = tm.Log;
        }

        switch (e.PropertyName) {
          case TradingMacroMetadata.Pair:
          case TradingMacroMetadata.TradingRatio:
            tm.SetLotSize(tradesManager.GetAccount()); break;
        }
        if (e.PropertyName == TradingMacroMetadata.CorridorIterations)
          tm.CorridorStatsArray.Clear();
        if (e.PropertyName == TradingMacroMetadata.IsActive && ShowAllMacrosFilter)
          RaisePropertyChanged(() => TradingMacrosCopy);

        if (e.PropertyName == TradingMacroMetadata.CurrentLoss) {
          MasterModel.CurrentLoss = CurrentLoss;
          GlobalStorage.UseAliceContext(c => { }, true);
        }
        if (e.PropertyName == TradingMacroMetadata.SyncAll) {
          if (tm.SyncAll) {
            tm.SyncAll = false;
            Func<PropertyInfo, bool> hasAtribute = p => {
              var attr = p.GetCustomAttributes(typeof(CategoryAttribute), false).FirstOrDefault() as CategoryAttribute;
              return attr != null && attr.Category == TradingMacro.categoryActive;
            };
            var props = tm.GetType().GetProperties().Where(hasAtribute).ToArray();
            foreach (var p in props)
              foreach (var t in GetTradingMacros().Except(new[] { tm }))
                p.SetValue(t, p.GetValue(tm, null), null);
          }
        }
        if (e.PropertyName != TradingMacroMetadata.IsAutoSync && tm.IsAutoSync) {
          var property = tm.GetType().GetProperty(e.PropertyName);
          if (property == null)
            Debug.Fail("Property " + e.PropertyName + " does not exist.");
          if (property != null && property.GetCustomAttributes(typeof(CategoryAttribute), true).Length > 0) {
            tm.IsAutoSync = false;
            GetTradingMacros().Except(new[] { tm }).ToList().ForEach(_tm => {
              _tm.IsAutoSync = false;
              _tm.SetProperty(e.PropertyName, tm.GetProperty(e.PropertyName));
            });
          }
        }
      } catch (Exception exc) {
        Log = exc;
      }
    }

    IDisposable _priceChangedSubscribsion;
    private void PriceChangeSubscriptionDispose() {
      if (_priceChangedSubscribsion != null) {
        _priceChangedSubscribsion.Dispose();
        _priceChangedSubscribsion = null;
      }
    }
    void CoreFX_LoggedInEvent(object sender, EventArgs e) {
      try {
        if (TradingMacrosCopy.Length > 0) {
          if (IsInVirtualTrading) {
            var vt = (VirtualTradesManager)tradesManager;
            vt.RatesByPair = () => GetTradingMacros().GroupBy(tm => tm.Pair).ToDictionary(tm => tm.First().Pair, tm => tm.First().UseRatesInternal(ri => ri));
            vt.BarMinutes = (int)GetTradingMacros().First().BarPeriod;
          }
          PriceChangeSubscriptionDispose();
          if (!IsInVirtualTrading)
            //_priceChangedSubscribsion = Observable.FromEventPattern<EventHandler<PriceChangedEventArgs>, PriceChangedEventArgs>(
            //  h => h, h => tradesManager.PriceChanged += h, h => tradesManager.PriceChanged -= h)
            //  .Buffer(TimeSpan.FromSeconds(1))
            //  .Subscribe(el => {
            //    el.GroupBy(e2 => e2.EventArgs.Pair).Select(e2 => e2.Last()).ToList()
            //      .ForEach(ie => fw_PriceChanged(ie.Sender, ie.EventArgs));
            //  });
            _priceChangedSubscribsion = Observable.FromEventPattern<EventHandler<PriceChangedEventArgs>, PriceChangedEventArgs>(
              h => h, h => tradesManager.PriceChanged += h, h => tradesManager.PriceChanged -= h)
              .Sample(1.FromSeconds())
                .Subscribe(pce => {
                  UpdateTradingStatistics();
                }, exc => Log = exc);
          else
            tradesManager.PriceChanged += fw_PriceChanged;
          //.GroupByUntil(g => g.EventArgs.Pair, g => Observable.Timer(TimeSpan.FromSeconds(1)))
          //.SubscribeOn(System.Concurrency.Scheduler.ThreadPool)
          //.Subscribe(g => g.TakeLast(1).Subscribe(ie => fw_PriceChanged(ie.Sender, ie.EventArgs), exc => Log = exc), exc => Log = exc);
          tradesManager.TradeAdded += fw_TradeAdded;
          tradesManager.TradeClosed += fw_TradeClosed;
          tradesManager.Error += fw_Error;
        }
        List<Action> runPriceQueue = new List<Action>();
        foreach (var tm in TradingMacrosCopy) {
          InitTradingMacro(tm);
          if (!IsInVirtualTrading) {
            (sender as CoreFX).SetOfferSubscription(tm.Pair);
            tm.CurrentPrice = tradesManager.GetPrice(tm.Pair) ?? tm.CurrentPrice;
          }
          tm.CurrentLot = tm.Trades.Sum(t => t.Lots);
          if (!IsInVirtualTrading) {
            var currTM = tm;
            Task.Factory.StartNew(() => currTM.LastTrade = tradesManager.GetLastTrade(currTM.Pair));
            tm.OnLoadRates();
            runPriceQueue.Add(() => {
              currTM.RunPriceChanged(new PriceChangedEventArgs(currTM.Pair,currTM.BarPeriodInt, tradesManager.GetPrice(currTM.Pair), tradesManager.GetAccount(), tradesManager.GetTradesInternal(currTM.Pair)), null);
            });
          }
          tm.SetLotSize(tradesManager.GetAccount());
        }
        runPriceQueue.ToObservable(Scheduler.Default).Subscribe(rp => rp());
        InitInstruments();
        MasterModel.CurrentLoss = CurrentLoss;
        //var a = new Action(() => {
        //  Instruments.Clear();
        //  (sender as CoreFX).Instruments.ToList().ForEach(i => Instruments.Add(i));
        //});
        //if( Application.Current.Dispatcher.CheckAccess() )
        //  a();
        //else
        //  Application.Current.Dispatcher.BeginInvoke(a);
      } catch (Exception exc) { Log = exc; }
    }

    void CoreFX_LoggedOffEvent(object sender, EventArgs e) {
      if (tradesManager != null) {
        PriceChangeSubscriptionDispose();
        tradesManager.PriceChanged -= fw_PriceChanged;
        tradesManager.TradeAdded -= fw_TradeAdded;
        tradesManager.Error -= fw_Error;

        TradingMacrosCopy.ToList().ForEach(tm => new Action(() => InitTradingMacro(tm, true)).InvoceOnUI());
      }
    }

    void fw_PriceChanged(object sender, PriceChangedEventArgs e) {
      Price price = e.Price;
      try {
        var sw = Stopwatch.StartNew();
        if (price != null) SetCurrentPrice(price);
        var pair = price.Pair;
        foreach (var tm in GetTradingMacros(pair).Where(t => !IsInVirtualTrading && !t.IsInPlayback || IsInVirtualTrading && t.BarPeriodInt == e.BarPeriod /*&& t.IsInPlayback*/)) {
          tm.RunPriceChanged(e, null/*OpenTradeByStop*/);
        }
        UpdateTradingStatistics();
      } catch (Exception exc) {
        Log = exc;
      }
    }

    object _showChartQueueLocker = new object();
    static ISubject<Action> _showChartQueue;
    ISubject<Action> ShowChartQueue {
      get {
        lock (_showChartQueueLocker) {
          if (_showChartQueue == null) {
            _showChartQueue = new Subject<Action>();
            _showChartQueue.SubscribeToLatestOnBGThread(action => action.InvoceOnUI(), exc => Log = exc);
          }
        }
        return _showChartQueue;
      }
    }
    void AddShowChart(TradingMacro tm) {
      if (tm.IsInVitualTrading)
        GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(() => ShowChart(tm), DispatcherPriority.DataBind);
      else
        ShowChartQueue.OnNext(() => ShowChart(tm));
    }
    bool _isMinimized = false;
    void IsMinimized(Window w) {
      _isMinimized = w.WindowState == WindowState.Minimized;
      if (!_isMinimized)
        GetTradingMacros().ForEach(tm => AddShowChart(tm));
    }
    void ShowChart(TradingMacro tm) {
      try {
        if (_isMinimized) return;
        Rate[] rates = tm.RatesArray.ToArray();//.RatesCopy();
        if (!rates.Any()) return;
        var charter = GetCharter(tm);
        if (charter.IsParentHidden) return;
        string pair = tm.Pair;
        if (tm.IsCharterMinimized) return;
        if (tm == null) return;
        if (rates.Count() == 0) return;
          rates.SetStartDateForChart(((int)tm.BarPeriod).FromMinutes());
        var price = tm.CurrentPrice;
        price.Digits = tradesManager.GetDigits(pair);
        var csFirst = tm.CorridorStats;
        if (csFirst == null || !csFirst.Rates.Any()) return;
        var corridorTime0 = tm.WaveTradeStart == null || !tm.WaveTradeStart.HasRates ? DateTime.MinValue : tm.WaveTradeStart.Rates[0].StartDateContinuous;
        var corridorTime1 = tm.WaveTradeStart == null || !tm.WaveTradeStart.HasRates ? DateTime.MinValue : tm.WaveTradeStart.Rates.Min(r => r.StartDateContinuous);// tm.CorridorsRates.Count < 2 ? DateTime.MinValue : tm.CorridorsRates[1][0].StartDateContinuous;
        var corridorTime2 = !tm.WaveTradeStart1.HasRates ? DateTime.MinValue : tm.WaveTradeStart1.Rates.LastBC().StartDateContinuous;// tm.CorridorsRates.Count < 2 ? DateTime.MinValue : tm.CorridorsRates[1][0].StartDateContinuous;
        var timeCurr = tm.LastTrade.Pair == tm.Pair && !tm.LastTrade.Buy ? new[] { tm.LastTrade.Time, tm.LastTrade.TimeClose }.Max() : DateTime.MinValue;
        var timeLow = tm.LastTrade.Pair == tm.Pair && tm.LastTrade.Buy ? new[] { tm.LastTrade.Time, tm.LastTrade.Time }.Max() : DateTime.MinValue;
        var dateMin = rates.Min(r => r.StartDateContinuous);
        string[] info = new string[] { };
        //RunWithTimeout.WaitFor<object>.Run(TimeSpan.FromSeconds(1), () => {
        //charter.Dispatcher.Invoke(new Action(() => {
        try {
          charter.PipSize = tm.PointSize;
          charter.CorridorHeightMultiplier = csFirst.HeightUpDown0 / csFirst.HeightUpDown;// tm.CorridorHeightMultiplier;
          charter.SetPriceLineColor(tm.Trades.HaveBuy() ? true : tm.Trades.HaveSell() ? false : (bool?)null);

          charter.GetPriceHigh = tm.ChartHighPrice();
          charter.GetPriceLow = tm.ChartLowPrice();
          charter.GetPriceMA = tm.GetPriceMA();
          
          charter.CenterOfMassBuy = tm.CenterOfMassBuy;
          charter.CenterOfMassSell = tm.CenterOfMassSell;
          charter.MagnetPrice = tm.MagnetPrice;
          
          charter.SelectedGannAngleIndex = tm.GannAngleActive;
          charter.GannAnglesCount = tm.GannAnglesArray.Count;
          charter.GannAngle1x1Index = tm.GannAngle1x1Index;

          charter.HeaderText =
            string.Format(":{0}×[{1}]{2:n1}°{3:n0}‡{4:n0}∆[{5:n0}/{6:n0}][{7:n0}/{8:n0}][{9:n1},{10:n2}]↨"///↨↔
            /*0*/, tm.BarPeriod
            /*1*/, tm.RatesArray.Count + (tm.RatesArray.Count == tm.BarsCount ? "" : (',' + tm.BarsCount.ToString()))
            /*2*/, tm.CorridorAngle
            /*3*/, tm.RatesHeightInPips
            /*4*/, tm.CorridorStats.HeightByRegressionInPips
            /*5*/, tm.StDevByHeightInPips
            /*6*/, tm.StDevByPriceAvgInPips
            /*7*/, tm.CorridorStats.StDevByHeightInPips
            /*8*/, tm.CorridorStats.StDevByPriceAvgInPips
            /*9*/, tm.RatesStDevHourlyAvgNativeInPips
            /*10*/, tm.CorridorCorrelation
          );
          charter.SetTrendLines(tm.SetTrendLines());
          charter.CalculateLastPrice = tm.CalculateLastPrice;
          charter.PriceBarValue = pb => pb.Speed;
          var distance = rates.LastBC().DistanceHistory;
          //var stDevBars = rates.Select(r => new PriceBar { StartDate = r.StartDateContinuous, Speed = tm.InPips(r.PriceStdDev) }).ToArray();
          var volts = tm.GetVoltage;
          var volts2 = tm.GetVoltage2;
          //Task.WaitAll(
          //  Task.Factory.StartNew(() => rates.SkipWhile(r => double.IsNaN(volts(r))).ToArray().FillGaps(r => double.IsNaN(volts(r)), r => r.DistanceHistory, (r, d) => r.DistanceHistory = d)),
          //  Task.Factory.StartNew(() => rates.SkipWhile(r => double.IsNaN(r.Distance1)).ToArray().FillGaps(r => double.IsNaN(r.Distance1), r => r.Distance1, (r, d) => r.Distance1 = d))
          //);
          PriceBar[] distances = rates.Select(r => new PriceBar { StartDate2 = new DateTimeOffset(r.StartDateContinuous.ToUniversalTime()), Speed = volts(r).IfNaN(0) }).ToArray();
          PriceBar[] distances1 = rates.Take(0).Select(r => new PriceBar { StartDate2 = new DateTimeOffset(r.StartDateContinuous.ToUniversalTime()), Speed = volts2(r).IfNaN(0) }).ToArray();
          var distancesAverage = tm.GetVoltageAverage();// distances.Take(distances.Length - tm.CorridorDistanceRatio.ToInt()).Select(charter.PriceBarValue).ToArray().AverageByIterations(1).Average();
          charter.AddTicks(price, rates, true ? new PriceBar[1][] { distances/*, distances1*/} : new PriceBar[0][], info, null,
            tm.GetVoltageHigh(), tm.GetVoltageAverage(), 0, 0, tm.Trades.IsBuy(true).NetOpen(), tm.Trades.IsBuy(false).NetOpen(),
            corridorTime0, corridorTime1, corridorTime2, new double[0]);
          if (tm.CorridorStats.StopRate != null)
            charter.LineTimeMiddle = tm.CorridorStats.StopRate;
          else if (tm.CorridorStartDate.HasValue)
            charter.LineTimeMiddle = tm.CorridorStats.Rates[0];
          else
            charter.LineTimeMiddle = null;
          if (tm.WaveShortLeft.HasRates)
            charter.LineTimeMin = tm.WaveShortLeft.Rates.LastBC().StartDateContinuous;
          else if (tm.LineTimeMin.HasValue)
            charter.LineTimeMin = tm.LineTimeMin.Value;
          if (tm.WaveShort.HasRates)
            charter.LineTimeShort = tm.WaveShort.Rates.LastBC();
          charter.LineTimeTakeProfit = tm.RatesArray.Skip(tm.RatesArray.Count - tm.CorridorDistance).First().StartDateContinuous;
          var dic = tm.Resistances.ToDictionary(s => s.UID, s => new CharterControl.BuySellLevel(s, s.Rate, true));
          charter.SetBuyRates(dic);
          dic = tm.Supports.ToDictionary(s => s.UID, s => new CharterControl.BuySellLevel(s,s.Rate, false));
          charter.SetSellRates(dic);
          charter.SetTradeLines(tm.Trades);
          charter.SuppResMinimumDistance = tm.Strategy.HasFlag(Strategies.Hot) ? tm.SuppResMinimumDistance : 0;

          var times = tm.NewEventsCurrent.Select(ne => ne.Time.DateTime)
            .Concat(tm.Fractals.SelectMany(r => r).Select(r => r.StartDate));
          charter.DrawNewsTimes(times.ToArray());
          var tradeTime = tm.DoShowTradeOnChart ? tm.Trades.Select(t => t.Time).DefaultIfEmpty(tm.LastTrade.TimeClose).Where(d => !d.IsMin()) : new DateTime[0];
          charter.DrawTradeTimes(tradeTime);

          charter.DrawLevels(tm.CenterOfMassLevels);
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

    public void fw_TradeAdded(object sender, TradeEventArgs e) {
      try {
        Trade trade = e.Trade;
        var tm = GetTradingMacros(trade.Pair).First();
        if (tm.LastTrade.Time < trade.Time) tm.LastTrade = trade;
        var trades = tradesManager.GetTradesInternal(trade.Pair);
        tm.CurrentLot = trades.Sum(t => t.Lots);
        var amountK = tm.CurrentLot / tm.BaseUnitSize;
        if (tm.HistoryMaximumLot < amountK) tm.HistoryMaximumLot = amountK;
        var ts = tm.SetTradeStatistics(GetCurrentPrice(tm.Pair), trade);
      } catch (Exception exc) {
        Log = exc;
      }
    }

    void fw_Error(object sender, HedgeHog.Shared.ErrorEventArgs e) {
      Log = e.Error;
    }



    void AdjustCurrentLosses_(TradingMacro tradingMacro) {
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

    #region ZeroPositiveLoss Subject
    object _ZeroPositiveLossSubjectLocker = new object();
    ISubject<TradingMacro> _ZeroPositiveLossSubject;
    ISubject<TradingMacro> ZeroPositiveLossSubject {
      get {
        lock (_ZeroPositiveLossSubjectLocker)
          if (_ZeroPositiveLossSubject == null) {
            _ZeroPositiveLossSubject = new Subject<TradingMacro>();
            _ZeroPositiveLossSubject
              .Throttle(5.FromSeconds())
              .Subscribe(tradingMacro => {
                AdjustCurrentLosses();
              }, exc => Log = exc);
          }
        return _ZeroPositiveLossSubject;
      }
    }
    void OnZeroPositiveLoss(TradingMacro tm) {
      if (IsInVirtualTrading)
        AdjustCurrentLosses();
      else
        ZeroPositiveLossSubject.OnNext(tm);
    }
    #endregion


    void AdjustCurrentLosses_New() {
      var tms = GetTradingMacros();
      var tmsWithProfit = tms.Where(tm => tm.CurrentLoss > 0 && !tm.Trades.Any()).ToList();
      if (tmsWithProfit.Any()) {
        var tmWithLoss = tms.Where(tm => tm.CurrentLoss < 0).ToList();
        var lossSum = tmWithLoss.Sum(tm => tm.CurrentLoss).Abs();
        var profitTotal = tmsWithProfit.Sum(tm => tm.CurrentLoss);
        var profitToSpread = profitTotal.Min(lossSum);
        var tmByLoss = tmWithLoss.Select(tm => new { tm, profit = profitToSpread * tm.CurrentLoss.Abs() / lossSum }).ToList();
        tmByLoss.ForEach(tm => tm.tm.CurrentLoss += tm.profit);
        tmsWithProfit.ForEach(tm => tm.CurrentLoss = 0);
        if (tms.Any(tm => tm.Trades.Any())) {
          var tmsByProfit = tmsWithProfit.Select(tm => new { tm, profit = profitToSpread * tm.CurrentLoss / profitTotal }).ToList();
          tmsByProfit.ForEach(tmbp => tmbp.tm.CurrentLoss -= tmbp.profit);
        } else
          tms.Where(tm => tm.CurrentLoss > -0.1).ToList().ForEach(tm => tm.CurrentLoss = 0);
        try { GlobalStorage.UseAliceContextSaveChanges(); } catch { }
      }
    }
    void AdjustCurrentLosses() {
      var tmsWithProfit = GetTradingMacros().Where(tm => tm.CurrentLoss > 0).ToList();
      var tmWithLoss = GetTradingMacros().Where(tm => tm.CurrentLoss < 0).ToList();
      var lossSum = tmWithLoss.Sum(tm => tm.CurrentLoss).Abs();
      var profit = tmsWithProfit.Sum(tm => tm.CurrentLoss).Min(lossSum);
      var tmByLoss = tmWithLoss.Select(tm => new { tm, profit = profit * tm.CurrentLoss.Abs() / lossSum }).ToList();
      tmByLoss.ForEach(tm => tm.tm.CurrentLoss += tm.profit);
      tmsWithProfit.ForEach(tm => tm.CurrentLoss = 0);
      GlobalStorage.UseAliceContextSaveChanges();
    }
    void fw_TradeClosed(object sender, TradeEventArgs e) {
      var trade = e.Trade;
      try {
        var pair = trade.Pair;
        var tm = GetTradingMacros(pair).First();
        tm.LastTrade = trade;
        var commission = MasterModel.CommissionByTrade(trade);
        var totalGross = trade.GrossPL - commission;
        tm.RunningBalance += totalGross;
        tm.CurrentLoss = tm.CurrentLoss + totalGross;
        OnZeroPositiveLoss(tm);
        SaveTradeAction.Post(trade);
      } catch (Exception exc) {
        Log = exc;
      }
      //fw.FixOrderOpen(trade.Pair, !trade.IsBuy, lot, limit, stop, trade.GrossPL < 0 ? trade.Id : "");
    }

    private void SaveTrade(Trade trade) {
      try {
        var tm = GetTradingMacros(trade.Pair).First();
        var ts = trade.InitUnKnown<TradeUnKNown>().InitTradeStatistics(tm.GetTradeStatistics(trade));
        ts.SessionId = TradingMacro.SessionId;
        ts.SessionInfo = tm.SessionInfo;
        MasterModel.AddCosedTrade(trade);
      } catch (Exception exc) { Log = exc; }
    }
    ITargetBlock<Trade> _saveTradeAction;

    public ITargetBlock<Trade> SaveTradeAction {
      get { return _saveTradeAction ?? (_saveTradeAction = new ActionBlock<Trade>(t => SaveTrade(t))); }
    }

    #endregion

    #region Rate Loading
    Dictionary<string, double> Correlations = new Dictionary<string, double>();

    public class PairCorrelation {
      public string Pair1 { get; set; }
      public string Pair2 { get; set; }
      public double Correlation { get; set; }
      public PairCorrelation(string pair1, string pair2, double correlation) {
        this.Pair1 = pair1;
        this.Pair2 = pair2;
        this.Correlation = correlation;
      }
      public override string ToString() {
        return Pair1 + "," + Pair2 + "," + Correlation;
      }
    }

    #region RunCorrelations Subject
    object _RunCorrelationsSubjectLocker = new object();
    ISubject<string> _RunCorrelationsSubject;
    ISubject<string> RunCorrelationsSubject {
      get {
        lock (_RunCorrelationsSubjectLocker)
          if (_RunCorrelationsSubject == null) {
            _RunCorrelationsSubject = new Subject<string>();
            _RunCorrelationsSubject
              .Buffer(1.FromSeconds())
              .Select(g => g.First())
              .Subscribe(s => RunCorrelations(), exc => Log = exc);
          }
        return _RunCorrelationsSubject;
      }
    }

    System.Threading.Tasks.Dataflow.BroadcastBlock<string> _runCorrelationBlock;

    public System.Threading.Tasks.Dataflow.BroadcastBlock<string> RunCorrelationBlock {
      get {
        if (_runCorrelationBlock == null)
          _runCorrelationBlock = new System.Threading.Tasks.Dataflow.BroadcastBlock<string>(s => { RunCorrelations(); return s; });
        return _runCorrelationBlock;
      }
    }

    void OnRunCorrelations(string p) {
      RunCorrelationsSubject.OnNext(p);
    }
    #endregion

    List<PairCorrelation> _CorrelationsByPair = new List<PairCorrelation>();
    public List<PairCorrelation> CorrelationsByPair {
      get { return _CorrelationsByPair; }
      set { _CorrelationsByPair = value; }
    }

    void RunCorrelations() {
      Stopwatch sw = Stopwatch.StartNew();
      var currencies = new List<string>();
      foreach (var tm in TradingMacrosCopy.Where(t => t.LotSize > 0))
        currencies.AddRange(tm.Pair.Split('/'));
      currencies = currencies.Distinct().ToList();
      CorrelationsByPair.Clear();
      foreach (var currency in currencies)
        CorrelationsByPair.AddRange(RunPairCorrelation(currency));
      //Correlations[currency] = RunCorrelation(currency);
      Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds);
    }

    private ICollection<PairCorrelation> RunPairCorrelation(string currency) {
      Func<string, double[]> getRatesForCorrelation = pair =>
        GetRatesForCorridor(GetTradingMacros(pair).First()).Select(r => r.PriceAvg).ToArray();
      var correlations = new List<PairCorrelation>();
      var pairs = TradingMacrosCopy.Where(tm => tm.LotSize > 0 && tm.Pair.Contains(currency)).Select(tm => tm.Pair).ToArray();
      if (pairs.Any())
        foreach (var pair in pairs) {
          var price1 = getRatesForCorrelation(pair);
          foreach (var pc in pairs.Where(p => p != pair)) {
            var price2 = getRatesForCorrelation(pc);
            var correlation = AlgLib.correlation.pearsoncorrelation(ref price1, ref price2, Math.Min(price1.Length, price2.Length)).Abs();
            correlations.Add(new PairCorrelation(pair, pc, correlation));
          }
        }
      return correlations;
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
          correlations.Add(AlgLib.correlation.pearsoncorrelation(ref price1, ref price2, Math.Min(price1.Length, price2.Length)).Abs());
        }
      }
      return correlations.Count > 0 ? correlations.Average() : 0;
    }

    #endregion

    #region Helpers


    #region CanTrade
    private bool CanTrade(TradingMacro tm) {
      return tm.RatesArray.Count() > 0;
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

    #region OpenTrade

    private PendingOrder OpenTrade(bool buy, string pair, int lot, double limitInPips, double stopInPips, double stop, string remark) {
      var price = tradesManager.GetPrice(pair);
      var limit = limitInPips == 0 ? 0 : buy ? price.Ask + tradesManager.InPoints(pair, limitInPips) : price.Bid - tradesManager.InPoints(pair, limitInPips);
      if (stop == 0 && stopInPips != 0)
        stop = buy ? price.Bid + tradesManager.InPoints(pair, stopInPips) : price.Ask - tradesManager.InPoints(pair, stopInPips);
      return tradesManager.OpenTrade(pair, buy, lot, limit, stop, remark, price);
    }
    #endregion

    #region Get (stop/limit)
    private Rate[] GetRatesForCorridor(TradingMacro tm) {
      return GetRatesForCorridor(tm.RatesArray, tm);
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

    #region GetSlack
    private double GetFibSlack(double fib, TradingMacro tm) {
      var slack = fib.FibReverse().YofS(tm.CorridorStats.HeightUpDown);
      tm.SlackInPips = tradesManager.InPips(tm.Pair, slack);
      return slack;
    }
    #endregion
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
    void ScheduleInitTradingMacro(TradingMacro tm, bool unwind = false) {
      new Action(() => InitTradingMacro(tm, unwind)).ScheduleOnUI();
    }
    private void InitTradingMacro(TradingMacro tm, bool unwind = false) {
      var isFilterOk = TradingMacroFilter(tm) && tm.IsActive;
      if (!unwind && isFilterOk) {
        tm.TradingStatistics = _tradingStatistics;
        try {
          tm.SubscribeToTradeClosedEVent(() => tradesManager);
        } catch (Exception exc) {
          Log = exc;
        }
      } else
        try {
          tm.UnSubscribeToTradeClosedEVent(tradesManager);
          if (!isFilterOk) DeleteCharter(tm);
        } catch (Exception exc) {
          Log = exc;
        }

    }
    #endregion

    #endregion

  }
}
