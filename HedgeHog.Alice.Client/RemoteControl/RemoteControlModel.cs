using HedgeHog;
using HedgeHog.Alice.Store;
using HedgeHog.Bars;
using HedgeHog.Charter;
using HedgeHog.Shared;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Hosting;
using Order2GoAddIn;
using Owin;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Data.Entity.Core.Objects;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Gala = GalaSoft.MvvmLight.Command;
using System.Text.RegularExpressions;
using HedgeHog.Shared.Messages;
using ReactiveUI.Legacy;
using static HedgeHog.ReflectionCore;
namespace HedgeHog.Alice.Client {
  [Export]
  public partial class RemoteControlModel :RemoteControlModelBase {
    //Dimok:Show Closed trades

    #region Settings
    readonly double profitToClose = 1;
    #endregion

    #region members
    TradingStatistics _tradingStatistics = new TradingStatistics();
    Dictionary<TradingMacro, CharterControl> charters = new Dictionary<TradingMacro, CharterControl>();
    void DeleteCharter(TradingMacro tradingMacro) {
      if(charters.ContainsKey(tradingMacro)) {
        try {
          var charter = charters[tradingMacro];
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<CharterControl>(charter, (object)CharterControl.MessageType.Remove);
          charters.Remove(tradingMacro);
          //charter.Close();
        } catch(Exception exc) {
          Log = exc;
        }
      }
    }
    void RequestAddCharterToUI(CharterControl charter) {
      try {
        charter.Dispatcher.InvokeAsync(
         () =>
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(charter, CharterControl.MessageType.Add));
      } catch(Exception exc) {
        Log = exc;
      }
    }
    static object _chartersLocker = new object();
    public CharterControl GetCharter(TradingMacro tradingMacro) {
      lock(_chartersLocker) {
        if(!charters.ContainsKey(tradingMacro)) {
          var charterNew = new CharterControl(tradingMacro.CompositeId, App.container) { tm = tradingMacro };
          RequestAddCharterToUI(charterNew);
          try {
            charters.Add(tradingMacro, charterNew);
          } catch(ArgumentException exc) {
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
          var isSelectedBinding = new Binding(GetLambda(() => tradingMacro.IsSelectedInUI)) { Source = tradingMacro };
          charterNew.SetBinding(CharterControl.IsSelectedProperty, isSelectedBinding);
          var isActiveBinding = new Binding(GetLambda(() => tradingMacro.IsTradingActive)) { Source = tradingMacro };
          charterNew.SetBinding(CharterControl.IsActiveProperty, isActiveBinding);
          charterNew.TradeLineChanged += new EventHandler<PositionChangedBaseEventArgs<double>>(charterNew_TradeLineChanged);
          charterNew.ShowChart += new EventHandler(charterNew_ShowChart);
          charterNew.RoundTo = tradingMacro.Digits();
          charterNew.SetDefaultTradeLevels += charterNew_CenterTradeLevels;
        }
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
      if(charterOld.Parent == null)
        RequestAddCharterToUI(charterOld);
      return charterOld;
    }

    void charterNew_CenterTradeLevels(object sender, EventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.SetDefaultTradeLevels();
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
      switch(e.Key) {
        case Key.D0:
        case Key.NumPad0:
          tm.SetTradeCount(0);
          break;
        case Key.D1:
        case Key.NumPad1:
          tm.SetTradeCount(1);
          break;
        case Key.A:
          tm.ToggleIsActive();
          charter.FitToView();
          break;
        case Key.C:
          tm.IsTradingActive = false;
          //tm.CloseTrades();
          tm.SetCanTrade(false, null);
          tm.SetTradeCount(0);
          charter.FitToView();
          tm.FreezeCorridorStartDate(true);
          break;
        case Key.R:
          this.TradesManager.RefreshOrders();
          break;
        case Key.S:
          tm.FreezeCorridorStartDate();
          break;
        case Key.G:
          tm.MakeGhosts();
          break;
        case Key.L:
          tm.SetLevelsBy();
          break;
        case Key.M:
          tm.ResetSuppResesInManual();
          break;
        case Key.T:
          tm.ToggleCanTrade();
          break;
        case Key.W:
          tm.WrapTradeInCorridor();
          break;
      }
    }

    void charterNew_LineTimeMiddleChanged(object sender, PositionChangedBaseEventArgs<DateTime> e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.IsTradingActive = false;
      tm.CorridorStopDate = e.NewPosition;
      tm.OnPropertyChangedCore(nameof(tm.CorridorStartDate));
    }

    void charterNew_LineTimeShortChanged(object sender, PositionChangedBaseEventArgs<DateTime> e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.IsTradingActive = false;
      var bar = tm.RatesArray.ReverseIfNot().TakeWhile(r => r.StartDateContinuous >= e.NewPosition).Last();
      //tm.WaveShort.Distance = bar.Distance;
      //tm.CorridorStartDate = bar.StartDate;
      /////tm.OnPropertyChangedCore(TradingMacroMetadata.CorridorStartDate);
      //tm.ScanCorridor(tm.RatesArray);
    }

    void charter_BuySellRemoved(object sender, BuySellRateRemovedEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      try {
        tm.RemoveSuppRes(e.UID);
      } catch(Exception exc) {
        Log = exc;
      }
    }

    void charter_BuySellAdded(object sender, BuySellRateAddedEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      try {
        tm.AddBuySellRate(e.Rate, e.IsBuy);
      } catch(Exception exc) {
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
        tm.UpdateSuppRes(e.UID, e.NewPosition).IsActive = false;
        ;
        tm.IsTradingActive = false;
      } catch(Exception exc) {
        Log = exc;
      }
    }

    void charter_CorridorStartPositionChanged(object sender, CorridorPositionChangedEventArgs e) {
      var tm = GetTradingMacro((CharterControl)sender);
      tm.IsTradingActive = false;
      if(tm.CorridorStartDate == e.NewPosition)
        return;
      var startDate2 = e.NewPosition.ToUniversalTime();
      var index = tm.RatesArray.TakeWhile(r => r.StartDate2 < startDate2).Count();
      //var index = tm.RatesArray.IndexOf(new Rate() { StartDate2 = e.NewPosition.ToUniversalTime() });
      var rate = tm.RatesArray.IsEmpty() ? null : tm.RatesArray.GetRange((index - 5).Max(0), 10).OrderByDescending(r => r.PriceHigh - r.PriceLow).First();
      tm.CorridorStartDate = rate?.StartDate;
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
    ReactiveCommand<object, Unit> _LoadTradingSettingsCommand;
    public ReactiveCommand<object, Unit> LoadTradingSettingsCommand {
      get {
        if(_LoadTradingSettingsCommand == null) {
          _LoadTradingSettingsCommand = ReactiveUI.ReactiveCommand.CreateFromObservable<object, Unit>(LoadTradingSettings);
          //_LoadTradingSettingsCommand.Subscribe(LoadTradingSettings);
        }

        return _LoadTradingSettingsCommand;
      }
    }
    IObservable<Unit> LoadTradingSettings(object _) {
      var ret = Observable.Return(Unit.Default);
      var tm = (TradingMacro)_;
      try {
        var od = new Microsoft.Win32.OpenFileDialog() { FileName = "Params_" + tm.Pair.Replace("/", ""), DefaultExt = ".txt", Filter = "Text documents(.txt)|*.txt" };
        var odRes = od.ShowDialog();
        if(!odRes.GetValueOrDefault())
          return ret;
        var settings = Lib.ReadTestParameters(od.FileName);
        settings.ForEach(tp => {
          try {
            tm.SetProperty(tp.Key, (object)tp.Value);
          } catch(MissingMemberException exc) {
            Log = exc;
          }
        });
      } catch(Exception exc) {
        Log = exc;
        return ret;
      }
      Log = new Exception("Settings loaded.");
      return ret;
    }
    #endregion

    #region SaveTradingSettings
    ReactiveCommand<object> _SaveTradingSettingsCommand;
    public ReactiveCommand<object> SaveTradingSettingsCommand {
      get {
        if(_SaveTradingSettingsCommand == null) {
          _SaveTradingSettingsCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          _SaveTradingSettingsCommand.Subscribe(OnSaveTradingSettings);
        }

        return _SaveTradingSettingsCommand;
      }
    }
    void OnSaveTradingSettings(object _) {
      var tm = (TradingMacro)_;
      try {
        var od = new Microsoft.Win32.SaveFileDialog() { FileName = "Params_" + tm.Pair.Replace("/", ""), DefaultExt = ".txt", Filter = "Text documents(.txt)|*.txt" };
        var odRes = od.ShowDialog();
        var path = !odRes.GetValueOrDefault() ? "" : od.FileName;
        tm.SaveActiveSettings(path);
      } catch(Exception exc) {
        Log = exc;
        return;
      }
      Log = new Exception("Settings saved.");
    }
    #endregion

    #region CopyTradingMacroCommand

    ReactiveCommand<object> _CopyTradingMacroCommand;
    public ReactiveCommand<object> CopyTradingMacroCommand {
      get {
        if(_CopyTradingMacroCommand == null) {
          _CopyTradingMacroCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          ;
          _CopyTradingMacroCommand.Subscribe(CopyTradingMacros);
        }

        return _CopyTradingMacroCommand;
      }
    }
    void CopyTradingMacros(object tradingMacro) {
      var tms = GetTradingMacros().Where(tm => tm.IsSelectedInUI).ToList();
      if(tms.Count > 1) {
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
        tm.FreezLimit, tm.FreezeStop, tm.FibMax, tm.FibMin, tm.CorridornessMin, tm.CorridorIterationsIn,
        tm.CorridorIterationsOut, tm.CorridorIterations, tm.CorridorBarMinutes, pairIndex, tm.TradingGroup,
        tradingMacroName != null,
        tradingMacroName ?? tm.TradingMacroName,
        tm.LimitCorridorByBarHeight, tm.BarPeriodsLow, tm.BarPeriodsHigh,
        tm.StrictTradeClose, tm.BarPeriodsLowHighRatio, tm.LongMAPeriod, tm.CorridorAverageDaysBack, tm.CorridorPeriodsStart,
        tm.CorridorPeriodsLength, tm.CorridorRatioForRange, tm.CorridorRatioForBreakout, tm.RangeRatioForTradeLimit,
        tm.TradeByAngle, tm.ProfitToLossExitRatio, tm.PowerRowOffset, tm.RangeRatioForTradeStop,
        tm.ReversePower, tm.CorrelationTreshold, tm.CloseOnProfitOnly, tm.CloseOnOpen, tm.StreachTradingDistance,
        tm.CloseAllOnProfit, tm.TradeAndAngleSynced, tm.TradingAngleRange, tm.CloseByMomentum, tm.TradeByRateDirection,
        tm.GannAngles, tm.IsGannAnglesManual, tm.SpreadShortToLongTreshold,
        tm.SuppResLevelsCount, tm.DoStreatchRates, tm.IsSuppResManual, tm.TradeOnCrossOnly, tm.TakeProfitFunctionInt,
        tm.DoAdjustTimeframeByAllowedLot, tm.IsColdOnTrades, tm.CorridorCrossesCountMinimum, tm.StDevToSpreadRatio,
        loadRatesSecondsWarning: tm.LoadRatesSecondsWarning, corridorHighLowMethodInt: tm.CorridorHighLowMethodInt,
        corridorStDevRatioMax: tm.CorridorStDevRatioMax,
        corridorLengthMinimum: tm.CorridorLengthMinimum, corridorCrossHighLowMethodInt: tm.CorridorCrossHighLowMethodInt,
        priceCmaLevels: tm.PriceCmaLevels, volumeTresholdIterations: tm.VolumeTresholdIterations, stDevTresholdIterations: tm.StDevTresholdIterations,
        stDevAverageLeewayRatio: tm.StDevAverageLeewayRatio,
        extreamCloseOffset: tm.ExtreamCloseOffset, currentLossInPipsCloseAdjustment: tm.CurrentLossInPipsCloseAdjustment, corridorBigToSmallRatio: tm.CorridorBigToSmallRatio,
        voltageFunction: tm.VoltageFunction);
      tmNew.PropertyChanged += TradingMacro_PropertyChanged;
      //foreach (var p in tradingMacro.GetType().GetProperties().Where(p => p.GetCustomAttributes(typeof(DataMemberAttribute), false).Count() > 0))
      //  if (!(p.GetCustomAttributes(typeof(EdmScalarPropertyAttribute), false)
      //    .DefaultIfEmpty(new EdmScalarPropertyAttribute()).First() as EdmScalarPropertyAttribute).EntityKeyProperty
      //    && p.Name!="Pair"
      //    )
      //    tmNew.SetProperty(p.Name, tm.GetProperty(p.Name));
      try {
        throw new NotImplementedException();
        TradingMacrosCopy_Add(tmNew);
        new Action(() => InitTradingMacro(tmNew)).ScheduleOnUI(2.FromSeconds());
      } catch(Exception exc) {
        Log = exc;
      }
    }

    #endregion

    #region ClearCurrentLossCommand

    ReactiveCommand<object> _ClearCurrentLossCommand;
    public ReactiveCommand<object> ClearCurrentLossCommand {
      get {
        if(_ClearCurrentLossCommand == null) {
          _ClearCurrentLossCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          _ClearCurrentLossCommand.Subscribe(_ => ClearCurrentLoss());
        }

        return _ClearCurrentLossCommand;
      }
    }
    void ClearCurrentLoss() {
      foreach(var tm in TradingMacrosCopy)
        tm.CurrentLoss = 0;
    }

    #endregion

    ReactiveCommand<object> _DeleteTradingMacroCommand;
    public ReactiveCommand<object> DeleteTradingMacroCommand {
      get {
        if(_DeleteTradingMacroCommand == null) {
          _DeleteTradingMacroCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          _DeleteTradingMacroCommand.Subscribe(DeleteTradingMacro);
          //new Gala.RelayCommand<object>(DeleteTradingMacro, (tm) => tm is TradingMacro);
        }

        return _DeleteTradingMacroCommand;
      }
    }
    void DeleteTradingMacro(object tradingMacro) {
      var tm = tradingMacro as TradingMacro;
      if(tm == null || tm.EntityState == System.Data.Entity.EntityState.Detached)
        return;
      tm.IsActive = false;
      throw new NotImplementedException();
      //GlobalStorage.UseAliceContext(c => c.TradingMacroes.DeleteObject(tm), true);
      TradingMacrosCopy_Delete(tm);

    }

    Task loadHistoryTast;
    bool isLoadHistoryTaskRunning;
    ReactiveCommand<object> _PriceHistoryCommand;
    public ReactiveCommand<object> PriceHistoryCommand {
      get {
        if(_PriceHistoryCommand == null) {
          _PriceHistoryCommand = ReactiveUI.Legacy.ReactiveCommand.CreateAsyncTask<object>(PriceHistory);
          _PriceHistoryCommand.ThrownExceptions.Subscribe(exc => MessageBox.Show(exc + ""));
        }

        return _PriceHistoryCommand;
      }
    }
    async Task<object> PriceHistory(object o) {
      var tm = o as TradingMacro;
      await Task.Factory.StartNew(() => {
        Store.PriceHistory.AddTicks(TradesManager, (int)tm.BarPeriod, tm.Pair, TradesManager.ServerTime.AddMonths(-tm.MonthsOfHistory), obj => Log = new Exception(obj + ""));
      });
      return null;
    }

    ReactiveCommand<object> _ClosePairCommand;
    public ReactiveCommand<object> ClosePairCommand {
      get {
        if(_ClosePairCommand == null) {
          _ClosePairCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          _ClosePairCommand.Subscribe(ClosePair);
        }

        return _ClosePairCommand;
      }
    }

    void ClosePair(object tradingMacro) {
      try {
        var tm = tradingMacro as TradingMacro;
        TradesManager.ClosePair(tm.Pair);
      } catch(Exception exc) {
        MessageBox.Show(exc + "");
      }
    }

    ReactiveCommand<object> _BuyCommand;
    public ReactiveCommand<object> BuyCommand {
      get {
        if(_BuyCommand == null) {
          _BuyCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          _BuyCommand.Subscribe(Buy);
        }

        return _BuyCommand;
      }
    }
    void Buy(object tradingMacro) {
      try {
        var tm = tradingMacro as TradingMacro;
        var lot = tm.AllowedLotSizeCore(true);
        if(!TradesManager.GetAccount().Hedging)
          lot += TradesManager.GetTradesInternal(tm.Pair).IsBuy(false).Sum(t => t.Lots);
        OpenTrade(true, tm.Pair, lot, 0, 0, 0, "");
        //AddPendingOrder(true, tm.Pair, () => openTradeCondition( tm,true), () => OpenTrade(tm, true));
      } catch(Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    ReactiveCommand<object> _SellCommand;
    public ReactiveCommand<object> SellCommand {
      get {
        if(_SellCommand == null) {
          _SellCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          _SellCommand.Subscribe(Sell);
        }

        return _SellCommand;
      }
    }
    void Sell(object tradingMacro) {
      try {
        var tm = tradingMacro as TradingMacro;
        var lot = tm.AllowedLotSizeCore(false);
        if(!TradesManager.GetAccount().Hedging)
          lot += TradesManager.GetTradesInternal(tm.Pair).IsBuy(true).Sum(t => t.Lots);
        OpenTrade(false, tm.Pair, lot, 0, 0, 0, "");
        //AddPendingOrder(false, tm.Pair, () => openTradeCondition(tm, false), () => OpenTrade(tm, false));
      } catch(Exception exc) {
        MessageBox.Show(exc + "");
      }
    }



    ICommand _ShowPropertiesDialogCommand;
    public ICommand ShowPropertiesDialogCommand {
      get {
        if(_ShowPropertiesDialogCommand == null) {
          _ShowPropertiesDialogCommand = new Gala.RelayCommand<object>(ShowPropertiesDialog, (o) => true);
        }

        return _ShowPropertiesDialogCommand;
      }
    }
    void ShowPropertiesDialog(object o) {
      var tm = o as TradingMacro;
      if(tm == null)
        MessageBox.Show("ShowPropertiesDialog needs TradingMacro");
      else
        tm.ShowProperties = !tm.ShowProperties;
    }

    #region SetStrategiesCommand

    ICommand _SetStrategiesCommand;
    public ICommand SetStrategiesCommand {
      get {
        if(_SetStrategiesCommand == null) {
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
        if(_HidePropertiesDialogCommand == null) {
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
    ReactiveCommand<object> _ToggleCloseAtZeroCommand;
    public ReactiveCommand<object> ToggleCloseAtZeroCommand {
      get {
        if(_ToggleCloseAtZeroCommand == null) {
          _ToggleCloseAtZeroCommand = ReactiveUI.Legacy.ReactiveCommand.Create();
          _ToggleCloseAtZeroCommand.Subscribe(ToggleCloseAtZero);
        }

        return _ToggleCloseAtZeroCommand;
      }
    }
    void ToggleCloseAtZero(object o) {
      var tm = o as TradingMacro;
      if(tm == null)
        MessageBox.Show("ToggleCloseAtZero needs TradingMacro");
      tm.CloseAtZero = !tm.CloseAtZero;
    }
    #endregion

    #region Ctor
    void CleanEntryOrders() {
      try {
        var trades = TradesManager.GetTrades();
        foreach(var order in TradesManager.GetOrders(""))
          if(!trades.Any(t => t.Pair == order.Pair))
            TradesManager.DeleteOrder(order.OrderID);
      } catch(Exception exc) {
        Log = exc;
      }
    }
    CancellationTokenSource _threadCancelation = new CancellationTokenSource();
    public RemoteControlModel() {
      try {
        _tradingStatistics.GetNetInPips = () => CalculateCurrentNetInPips();
        _tradingStatistics.GetNet = () => CalculateCurrentNet();
        if(!IsInDesigh) {
          InitializeModel();
          App.container.SatisfyImportsOnce(this);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<LogMessage>(this, lm => Log = lm.Exception);
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<string>("Shutdown", (m) => {
            try {
              _replayTaskCancellationToken.Cancel();
            } catch(Exception exc) {
              Log = exc;
            }
          });
          TradingMacrosCopy.Any();
          //GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<bool>(this, typeof(VirtualTradesManager), vt => { MessageBox.Show("VirtualTradesManager:" + vt); });
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Window>(this, typeof(WindowState), IsMinimized);
          MasterModel.CoreFX.LoggedIn += CoreFX_LoggedInEvent;
          MasterModel.CoreFX.LoggedOff += CoreFX_LoggedOffEvent;
          MasterModel.MasterTradeAccountChanged += MasterModel_MasterTradeAccountChanged;
          MasterModel.NeedTradingStatistics += MasterModel_NeedTradingStatistics;
          MasterModel.TradingMacroNameChanged += new EventHandler<EventArgs>(MasterModel_TradingMacroNameChanged);

          MessageBus.Current.Listen<AppExitMessage>().Subscribe(_ => SaveTradingMacros());

        }
      } catch(Exception exc) {
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
        _tradingStatistics.CurrentGross = MasterModel.TradesManager.GetTrades().Net2();

        if(GetTradingMacros().Any(tm => !tm.UseRates(rs => rs.Count > 0).SingleOrDefault()))
          return;
        var tms = GetTradingMacros().Where(tm => tm.Trades.Length > 0 && tm.Strategy != Strategies.None).ToArray();
        if(tms.Any() && tms.All(tm => tm.UseRates(rs => rs.Count > 0).SingleOrDefault())) {
          var tp = (tms.Sum(tm => (tm.CloseOnOpen ? tm.CalculateTakeProfit() : tm.CalcTakeProfitDistance(inPips: true)) * tm.Trades.Lots()) / tms.Select(tm => tm.Trades.Lots()).Sum()) / tms.Length;
          _tradingStatistics.TakeProfitDistanceInPips = tp;
        } else {
          _tradingStatistics.TakeProfitDistanceInPips = double.NaN;
        }
        tms = GetTradingMacrosForStatistics();
        _tradingStatistics.TradingMacros = tms;
        _tradingStatistics.GrossToExitInPips = MasterModel.AccountModel.GrossToExitInPips;
        if(tms.Any()) {
          ///_tradingStatistics.StDevPips = tms.Select(tm => tm.InPips(tm.CorridorStats.RatesStDev)).ToList().AverageByIterations(1).Average();
          ///_tradingStatistics.TakeProfitPips = tms.Select(tm => tm.CalculateTakeProfitInPips()).ToList().AverageByIterations(2).Average();
          ///_tradingStatistics.VolumeRatioH = tms.Select(tm => tm.VolumeShortToLongRatio).ToArray().AverageByIterations(2).Average();
          ///_tradingStatistics.VolumeRatioL = tms.Select(tm => tm.VolumeShortToLongRatio).ToArray().AverageByIterations(2, true).Average();
          ///_tradingStatistics.RatesStDevToRatesHeightRatioH = tms.Select(tm => tm.RatesStDevToRatesHeightRatio).ToArray().AverageByIterations(2).Average();
          ///_tradingStatistics.RatesStDevToRatesHeightRatioL = tms.Select(tm => tm.RatesStDevToRatesHeightRatio).ToArray().AverageByIterations(2, true).Average();
          ///_tradingStatistics.AllowedLotMinimum = tms.Select(tm => tm.LotSizeByLossBuy.Max(tm.LotSizeByLossSell)).Min();
          {
            _tradingStatistics.CurrentGrossInPips = _tradingStatistics.GetNetInPips();
          }
          var clp = tms.Sum(tm => tm.CurrentLossInPips);
          _tradingStatistics.CurrentLossInPips = clp;
          _tradingStatistics.OriginalProfit = MasterModel.AccountModel.OriginalProfit;
          var net = MasterModel.TradesManager.GetTrades().Net2();
          (
            from tm in tms
            where tm.HaveHedgedTrades()
            from mps in tm.MaxHedgeProfit
            from mp in mps
            where mp.buy == tm.Trades.HaveBuy()
            select new Action(() => MasterModel.GrossToExitCalc = () => mp.profit * MasterModel.ProfitByHedgeRatioDiff)
            )
            .DefaultIfEmpty(() => MasterModel.GrossToExitCalc = null)
            .SingleOrElse(() => MasterModel.GrossToExitCalc = null)();
          var grossToExit = MasterModel.GrossToExitCalc();
          if(grossToExit != 0
            && !tms.SelectMany(tm => tm.PendingEntryOrders).Any()
            && net > grossToExit) {
            MasterModel.GrossToExitSoftReset();
            tms.ForEach(tm => tm.CloseTrades(new { grossToExit = grossToExit.AutoRound2(1), net = net.AutoRound2(1) } + ""));
          }
        }
      } catch(Exception exc) {
        Log = exc;
      }
    }

    private double CalculateCurrentNetInPips() {
      return GetTradingMacrosForStatistics()
        .Where(tm => tm.Trades.Any())
        .Select(tm => new { tm.CurrentGrossInPips, tm.CurrentGrossLot })
        .ToArray().Yield()
        .Select(_ => _.Sum(tm => tm.CurrentGrossInPips * tm.CurrentGrossLot) / _.Sum(tm => tm.CurrentGrossLot).Max(1))
        .FirstOrDefault();
    }
    private double CalculateCurrentNet() {
      return GetTradingMacrosForStatistics()
        .Select(tm => tm.CurrentGross)
        .Sum();
    }

    public TradingMacro[] GetTradingMacrosForStatistics() {
      // TODO: should only pick first of the same pair
      return (from tm in GetTradingMacros()
              where tm.IsTrader
              orderby tm.PairIndex
              group tm by tm.Pair into g
              select g
              .Where(t => t.Strategy != Strategies.None)
              .DefaultIfEmpty(g.First())
              .First()
              ).ToArray();
    }
    //
    private void InitializeModel() {
      GlobalStorage.AliceMaterializerSubject
        .SubscribeOn(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher)
        .Subscribe(e => Context_ObjectMaterialized(null, e));
      //GlobalStorage.AliceContext.ObjectMaterialized += Context_ObjectMaterialized;
      //throw new NotImplementedException();
      //if(GlobalStorage.AliceContext != null)
      //  GlobalStorage.AliceContext.ObjectStateManager.ObjectStateManagerChanged += ObjectStateManager_ObjectStateManagerChanged;
    }

    private void LoadClosedTrades() {
      var fileName = "ClosedTrades.xml";
      if(!File.Exists("ClosedTrades.xml"))
        return;
      foreach(var tradeString in File.ReadAllLines("ClosedTrades.xml").Where(s => !string.IsNullOrWhiteSpace(s))) {
        var trade = TradesManager.TradeFactory("").FromString(tradeString);
        MasterModel.AddCosedTrade(trade);
        //if (trade.TimeClose > DateTime.Now.AddMonths(-1)) ClosedTrades.Add(trade);
      }
      File.Move(fileName, fileName + ".old");
    }
    // TODO Why TradingMacrosCopy.ToObservable()
    ~RemoteControlModel() {
      if(MasterModel != null) {
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
      if(tm != null) {
        if(tm.EntityState == System.Data.Entity.EntityState.Detached) {
          tm.PropertyChanged -= TradingMacro_PropertyChanged;
          tm.ShowChart -= TradingMacro_ShowChart;
        }
        //else if (tm.EntityState == System.Data.EntityState.Added) {
        //  tm.PropertyChanged += TradingMacro_PropertyChanged;
        //  InitTradingMacro(tm);
        //}
      }
    }

    protected override void Context_ObjectMaterialized(object sender, ObjectMaterializedEventArgs e) {
      var tm = (sender as TradingMacro) ?? (e.Entity as TradingMacro);
      if(tm == null)
        return;
      tm.PropertyChanged += TradingMacro_PropertyChanged;
      tm.ShowChart += TradingMacro_ShowChart;
      tm.NeedChartSnaphot += tm_NeedChartSnaphot;
      //new Action(() => InitTradingMacro(tm)).ScheduleOnUI(2.FromSeconds());
      //InitTradingMacro(tm);
    }

    void tm_NeedChartSnaphot(object sender, EventArgs e) {
      var tm = sender as TradingMacro;
      tm.SetChartSnapshot(GetCharter(tm).GetPng());
    }

    internal IScheduler findDispatcherScheduler() {
      Type result = null;
      try {
        result = Type.GetType("System.Reactive.Concurrency.DispatcherScheduler, System.Reactive.Windows.Threading", true);
      } catch {
      }
      if(result == null) {
        Log = new Exception("WPF Rx.NET DLL reference not added - using Event Loop");
        return new EventLoopScheduler();
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

        if(e.PropertyName == nameof(tm.IsActive)) {
          _tradingMacrosDictionary.Clear();
          ScheduleInitTradingMacro(tm);
        }
        if(e.PropertyName == nameof(tm.CurrentPrice)) {
          try {
            if(tm.IsActive && tm.HasRates && !IsInVirtualTrading) {
              if(!_isMinimized) {
                var charter = GetCharter(tm);
                charter.Dispatcher.Invoke(new Action(() => {
                  charter.LineAvgAsk = tm.CurrentPrice.Ask;
                  charter.LineAvgBid = tm.CurrentPrice.Bid;
                  var high = tm.CalculateLastPrice(tm.RateLast, tm.ChartHighPrice());
                  var low = tm.CalculateLastPrice(tm.RateLast, tm.ChartLowPrice());
                  var ma = tm.CalculateLastPrice(tm.RateLast, tm.GetPriceMA());
                  var ma2 = tm.CalculateLastPrice(tm.RateLast, tm.GetPriceMA2());
                  charter.SetLastPoint(high, low, ma, ma2, tm.RateLast);
                  ;
                  //Debug.WriteLineIf(tm.Pair == "EUR/JPY", string.Format("Current price:{0} @ {1:mm:ss}", tm.CurrentPrice.Average.Round(3), tm.CurrentPrice.Time));
                }), DispatcherPriority.Send);
              }
            }
          } catch(Exception exc) {
            Log = exc;
          }
        }


        if(e.PropertyName == nameof(tm.GannAngles_)) {
          tm.SetGannAngles();
          AddShowChart(tm);
        }

        if(e.PropertyName == nameof(tm.Log)) {
          Log = tm.Log;
        }

        switch(e.PropertyName) {
          case nameof(tm.Pair):
          case nameof(tm.TradingRatio):
            tm.SetLotSize(TradesManager.GetAccount());
            break;
        }
        if(e.PropertyName == nameof(tm.CorridorIterations))
          tm.CorridorStatsArray.Clear();
        if(e.PropertyName == nameof(tm.IsActive) && ShowAllMacrosFilter)
          RaisePropertyChanged(() => TradingMacrosCopy);

        if(e.PropertyName == nameof(tm.CurrentLoss)) {
          MasterModel.CurrentLoss = CurrentLoss;
          //GlobalStorage.UseAliceContext(c => { }, true);
        }
        if(e.PropertyName == nameof(tm.SyncAll)) {
          if(tm.SyncAll) {
            var categories = new[] { TradingMacro.categoryActive, TradingMacro.categoryActiveFuncs, TradingMacro.categoryActiveYesNo, TradingMacro.categoryTrading, TradingMacro.categoryCorridor };
            tm.SyncAll = false;
            Func<PropertyInfo, bool> hasAtribute = p => {
              var attr = p.GetCustomAttributes(typeof(CategoryAttribute), false).FirstOrDefault() as CategoryAttribute;
              return attr != null && categories.Contains(attr.Category);
            };
            var props = tm.GetType().GetProperties().Where(hasAtribute).ToArray();
            foreach(var p in props)
              foreach(var t in GetTradingMacros().Except(new[] { tm }))
                p.SetValue(t, p.GetValue(tm, null), null);
          }
        }
        if(e.PropertyName != nameof(tm.IsAutoSync) && tm.IsAutoSync) {
          var property = tm.GetType().GetProperty(e.PropertyName);
          if(property == null)
            Debug.Fail("Property " + e.PropertyName + " does not exist.");
          if(property != null && property.GetCustomAttributes(typeof(CategoryAttribute), true).Length > 0) {
            tm.IsAutoSync = false;
            GetTradingMacros().Except(new[] { tm }).ToList().ForEach(_tm => {
              _tm.IsAutoSync = false;
              _tm.SetProperty(e.PropertyName, tm.GetProperty(e.PropertyName));
            });
          }
        }
      } catch(Exception exc) {
        Log = exc;
      }
    }

    void CoreFX_LoggedInEvent(object sender, EventArgs e) {
      try {
        if(TradingMacrosCopy.Length > 0) {
          if(IsInVirtualTrading) {
            var vt = (VirtualTradesManager)TradesManager;
            vt.SetServerTime(DateTime.MinValue);
            vt.RatesByPair = () => GetTradingMacros().GroupBy(tm => tm.Pair).ToDictionary(tm => tm.First().Pair, tm => tm.First().UseRatesInternal(ri => ri, 2000).Single());
            vt.BarMinutes = (int)GetTradingMacros().First().BarPeriod;
          }
          TradesManager.TradeAdded += fw_TradeAdded;
          TradesManager.TradeClosed += fw_TradeClosed;
          TradesManager.Error += fw_Error;
        }
        List<Action> runPriceQueue = new List<Action>();
        foreach(var tm in TradingMacrosCopy) {
          InitTradingMacro(tm);
          if(!IsInVirtualTrading) {
            (sender as ICoreFX).SetOfferSubscription(tm.Pair);
            //tm.CurrentPrice = TradesManager.GetPrice(tm.Pair);
          }
          tm.CurrentLot = tm.Trades.Sum(t => t.Lots);
          if(!IsInVirtualTrading) {
            var currTM = tm;
            Task.Factory.StartNew(() => currTM.LastTrade = TradesManager.GetLastTrade(currTM.Pair));
            tm.OnLoadRates();
            runPriceQueue.Add(() => {
              //currTM.RunPriceChanged(new PriceChangedEventArgs(currTM.Pair, TradesManager.GetPrice(currTM.Pair), TradesManager.GetAccount(), TradesManager.GetTradesInternal(currTM.Pair)), null);
            });
          }
          tm.SetLotSize(TradesManager.GetAccount());
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
      } catch(Exception exc) { Log = exc; }
    }

    void CoreFX_LoggedOffEvent(object sender, EventArgs e) {
      if(TradesManager != null) {
        TradesManager.TradeAdded -= fw_TradeAdded;
        TradesManager.Error -= fw_Error;

        TradingMacrosCopy.ToList().ForEach(tm => new Action(() => InitTradingMacro(tm, true)).InvoceOnUI());
      }
    }
    object _showChartQueueLocker = new object();
    static ISubject<Action> _showChartQueue;
    ISubject<Action> ShowChartQueue {
      get {
        lock(_showChartQueueLocker) {
          if(_showChartQueue == null) {
            _showChartQueue = new Subject<Action>();
            _showChartQueue.Throttle(TimeSpan.FromSeconds(0.1)).SubscribeToLatestOnBGThread(action => action.InvoceOnUI(DispatcherPriority.ContextIdle), exc => Log = exc);
          }
        }
        return _showChartQueue;
      }
    }
    double IntOrDouble(double? d, double max = 10) {
      return d.Abs() > max ? d.GetValueOrDefault().ToInt() : d.GetValueOrDefault().Round(1);
    }
    void AddShowChart(TradingMacro tm) {
      if(tm.IsInVirtualTrading)
        GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(() => ShowChart(tm), DispatcherPriority.ContextIdle);
      else
        ShowChartQueue.OnNext(() => ShowChart(tm));
    }
    bool _isMinimized = false;
    void IsMinimized(Window w) {
      _isMinimized = w.WindowState == WindowState.Minimized;
      if(!_isMinimized)
        GetTradingMacros().ForEach(tm => AddShowChart(tm));
    }
    #region Strategies
    #endregion
    bool? _isParentHidden;
    void ShowChart(TradingMacro tm) {
      try {
        if(_isMinimized)
          return;
        var charter = GetCharter(tm);
        if(tm.IsInVirtualTrading) {
          if(_isParentHidden.HasValue && !tm.Trades.Any()) {
            charter.IsParentHidden = _isParentHidden.Value;
            _isParentHidden = null;
          }
          if(charter.IsParentHidden && tm.Trades.Any()) {
            _isParentHidden = true;
            charter.IsParentHidden = false;
          }
          if(charter.IsParentHidden)
            return;
          if(tm.IsCharterMinimized)
            return;
        }
        List<Rate> rates = tm.RatesArray.ToList();//.RatesCopy();
        if(!rates.Any())
          return;
        string pair = tm.Pair;
        if(tm == null)
          return;
        if(rates.Count() == 0)
          return;
        var ratesForChart = rates;
        if(tm.FitRatesToPlotter) {
          var distanceInSeconds = rates.DistinctUntilChanged(r => r.StartDate2.AddMilliseconds(-r.StartDate2.Millisecond)).Count() / charter.ChartAreaWidth;
          ratesForChart = Task.Factory.StartNew(() => rates
            .Reverse<Rate>()
            .GroupByCloseness(distanceInSeconds, (r1, r2, d) => (r1.StartDate2 - r2.StartDate2).TotalSeconds.Abs() < d)
            .Reverse()
            .ToList(g => {
              var rate = g.GroupToRate();
              tm.SetVoltage(rate, g.Average(r => tm.GetVoltage(r)));
              return rate;
            })).Result;
          //Log = new Exception(("[{2}]{0}:{1:n1}ms" + Environment.NewLine + "{3}").Formater(MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, tm.Pair, string.Join(Environment.NewLine, swDict.Select(kv => "\t" + kv.Key + ":" + kv.Value))));
        }
        var startDateInterval = tm.BarPeriodCalc == BarsPeriodType.s1
          ? 1.FromSeconds()
          : tm.BarPeriod == BarsPeriodType.t1
          ? tm.HasTicks
          ? TimeSpan.Zero
          : 1.FromSeconds()
          : ((int)tm.BarPeriod).FromMinutes();
        ratesForChart.SetStartDateForChart(startDateInterval);
        if(tm.FitRatesToPlotter)
          rates.SetStartDateForChart(startDateInterval);
        var corridorTime0 = tm.WaveTradeStart == null || !tm.WaveTradeStart.HasRates ? DateTime.MinValue : tm.WaveTradeStart.Rates[0].StartDateContinuous;
        var corridorTime1 = tm.WaveTradeStart == null || !tm.WaveTradeStart.HasRates ? DateTime.MinValue : tm.WaveTradeStart.Rates.Min(r => r.StartDateContinuous);// tm.CorridorsRates.Count < 2 ? DateTime.MinValue : tm.CorridorsRates[1][0].StartDateContinuous;
        var corridorTime2 = !tm.WaveTradeStart1.HasRates ? DateTime.MinValue : tm.WaveTradeStart1.Rates.LastBC().StartDateContinuous;// tm.CorridorsRates.Count < 2 ? DateTime.MinValue : tm.CorridorsRates[1][0].StartDateContinuous;
        var dateMin = ratesForChart.Min(r => r.StartDateContinuous);
        string[] info = new string[] { };
        //RunWithTimeout.WaitFor<object>.Run(TimeSpan.FromSeconds(1), () => {
        //charter.Dispatcher.Invoke(new Action(() => {
        try {
          charter.PipSize = tm.PointSize;
          charter.SetPriceLineColor(tm.Trades.HaveBuy() ? true : tm.Trades.HaveSell() ? false : (bool?)null);

          charter.GetPriceHigh = tm.ChartHighPrice();
          charter.GetPriceLow = tm.ChartLowPrice();
          charter.GetPriceMA = tm.GetPriceMA();
          charter.GetPriceMA2 = tm.GetPriceMA2();

          charter.CenterOfMassBuy = tm.CenterOfMassBuy;
          charter.CenterOfMassSell = tm.CenterOfMassSell;
          charter.CenterOfMassBuy2 = tm.CenterOfMassBuy2;
          charter.CenterOfMassSell2 = tm.CenterOfMassSell2;
          charter.MagnetPrice = tm.MagnetPrice;

          charter.SelectedGannAngleIndex = tm.GannAngleActive;
          charter.GannAnglesCount = tm.GannAnglesArray.Count;
          charter.GannAngle1x1Index = tm.GannAngle1x1Index;

          charter.HeaderText =
            string.Format(":{0}×[{1}]{2}°{3:n0}‡{4:n0}∆[{5}/{6}][{7}/{8}][{10}]"///↨↔
            /*0*/, tm.BarPeriod
            /*1*/, tm.RatesArray.Count + "," + tm.TicksPerSecondAverage.Round(1)
            /*2*/, tm.TLBlue.Angle.Round(2) + "/" + tm.CorridorAngle.Round(2) + "/" + tm.TLGreen.Angle.Round(2)
            /*3*/, tm.RatesHeightInPips
            /*4*/, tm.CorridorStats?.HeightByRegressionInPips
            /*5*/, IntOrDouble(tm.StDevByHeightInPips, 5)
            /*6*/, IntOrDouble(tm.StDevByPriceAvgInPips, 5)
            /*7*/, IntOrDouble(tm.CorridorStats?.StDevByHeightInPips, 5)
            /*8*/, IntOrDouble(tm.CorridorStats?.StDevByPriceAvgInPips, 5)
            /*9*/, tm.CorridorStats?.Rates.Count.Div(tm.CorridorDistance).ToInt()
            /*10*/, tm.WorkflowStep
          );
          if(tm.TrendLines.Value != null) {
            charter.SetTrendLines(tm.TrendLines.Value.OrderBarsDescending().ToArray(), tm.CorridorStartDate.HasValue);
            charter.SetTrendLines2(tm.TrendLines2.Value);
            charter.SetTrendLines1(tm.TrendLines1.Value);
            charter.SetMATrendLines(tm.LineMA);
          }
          charter.CalculateLastPrice = tm.IsInVirtualTrading || tm.FitRatesToPlotter ? (Func<Rate, Func<Rate, double>, double>)null : tm.CalculateLastPrice;
          charter.PriceBarValue = pb => pb.Speed;
          //var stDevBars = rates.Select(r => new PriceBar { StartDate = r.StartDateContinuous, Speed = tm.InPips(r.PriceStdDev) }).ToArray();
          var volts = tm.GetVoltage;
          var volts2 = tm.GetVoltage2;
          //Task.WaitAll(
          //  Task.Factory.StartNew(() => rates.SkipWhile(r => double.IsNaN(volts(r))).ToArray().FillGaps(r => double.IsNaN(volts(r)), r => r.DistanceHistory, (r, d) => r.DistanceHistory = d)),
          //  Task.Factory.StartNew(() => rates.SkipWhile(r => double.IsNaN(r.Distance1)).ToArray().FillGaps(r => double.IsNaN(r.Distance1), r => r.Distance1, (r, d) => r.Distance1 = d))
          //);
          PriceBar[] distances = !tm.UseVoltage ? new PriceBar[0]
            : ratesForChart.Select(r => new PriceBar { StartDate2 = new DateTimeOffset(r.StartDateContinuous.ToUniversalTime()), Speed = volts(r) }).ToArray();
          var lastDist = distances.TakeWhile(d => d.Speed.IsNotNaN()).Select(d => d.Speed).LastOrDefault();
          distances.Where(d => d.Speed.IsNaN()).ForEach(d => d.Speed = lastDist);
          //var volt2Dedault = rates.SkipWhile(r => volts2(r).IsNaN()).Select(volts2).FirstOrDefault();
          //PriceBar[] distances1 = rates.Select(r => new PriceBar { StartDate2 = new DateTimeOffset(r.StartDateContinuous.ToUniversalTime()), Speed = volts2(r).IfNaN(volt2Dedault) }).ToArray();
          charter.AddTicks(ratesForChart, true ? new PriceBar[1][] { distances/*, distances1*/} : new PriceBar[0][], info, null,
            new[] { tm.GetVoltageHigh().SingleOrDefault(), tm.GetVoltageLow().SingleOrDefault() }, tm.GetVoltageAverage().SingleOrDefault(), 0, 0, tm.Trades.IsBuy(true).NetOpen(), tm.Trades.IsBuy(false).NetOpen(),
            corridorTime0, corridorTime1, corridorTime2, new double[0]);
          //if (tm.IsAsleep) return;
          if(tm.CorridorStats?.StopRate != null)
            charter.LineTimeMiddle = tm.CorridorStats.StopRate;
          else if(tm.CorridorStartDate.HasValue)
            tm.CorridorStats?.Rates.Take(1).ToList().ForEach(v => charter.LineTimeMiddle = v);
          charter.LineTimeMiddle = null;
          if(tm.WaveShortLeft.HasRates)
            charter.LineTimeMin = tm.WaveShortLeft.Rates.LastBC().StartDateContinuous;
          else if(tm.LineTimeMin.HasValue)
            charter.LineTimeMin = tm.LineTimeMin.Value;
          else if(tm.LineTimeMinFunc != null)
            charter.LineTimeMin = tm.LineTimeMinFunc(rates);
          if(tm.WaveShort.HasRates)
            charter.LineTimeShort = rates.Skip(rates.Count - tm.WaveShort.Rates.Count).First();
          if(tm.CorridorDistance > 0)
            charter.LineTimeTakeProfit = rates.Skip(rates.Count - tm.CorridorDistance).First().StartDateContinuous;
          var dic = tm.Resistances.ToDictionary(s => s.UID, s => new CharterControl.BuySellLevel(s, s.Rate, true));
          charter.SetBuyRates(dic);
          dic = tm.Supports.ToDictionary(s => s.UID, s => new CharterControl.BuySellLevel(s, s.Rate, false));
          charter.SetSellRates(dic);
          charter.SetTradeLines(tm.Trades);
          charter.SuppResMinimumDistance = tm.Strategy.HasFlag(Strategies.Hot) ? tm.SuppResMinimumDistance : 0;

          var times = tm.NewEventsCurrent.Where(_ => tm.CanShowNews).Select(ne => ne.Time.DateTime)
            .Concat(tm.Fractals.Where(_ => !tm.CanShowNews).SelectMany(r => r.Select(r1 => r1.StartDate)))
            .Concat(tm.FractalTimes.Where(_ => !tm.CanShowNews));
          charter.DrawNewsTimes(times.ToArray());
          var tradeTime = tm.DoShowTradeOnChart ? tm.Trades.Select(t => t.Time).DefaultIfEmpty(tm.LastTrade.TimeClose).Where(d => !d.IsMin()) : new DateTime[0];

          charter.DrawLevelUpLines(tm.ChartLevelsUp);
          charter.DrawTradeTimes(tradeTime);
          charter.DrawLevels(tm.CenterOfMassLevels);
        } catch(Exception exc) {
          Log = exc;
        }
        //}), System.Windows.Threading.DispatcherPriority.Normal);
        //  return null;
        //});
      } catch(Exception exc) {
        Log = exc;
      }
      //    };
    }

    public void fw_TradeAdded(object sender, TradeEventArgs e) {
      if(e.IsHandled)
        return;
      e.IsHandled = true;
      try {
        Trade trade = e.Trade;
        if(IsInVirtualTrading) {
          var comm = MasterModel.CommissionByTrade(trade);
          TradesManager.GetAccount().Balance -= comm;
        }
        GetTradingMacros(trade.Pair).Take(1).ForEach(tm => {
          var trades = TradesManager.GetTradesInternal(trade.Pair);
          tm.CurrentLot = trades.Sum(t => t.Lots);
          var amountK = tm.CurrentLot / tm.BaseUnitSize;
          if(tm.HistoryMaximumLot < amountK)
            tm.HistoryMaximumLot = amountK;
          var ts = tm.SetTradeStatistics(trade);
        });
      } catch(Exception exc) {
        Log = exc;
      }
    }

    void fw_Error(object sender, HedgeHog.Shared.ErrorEventArgs e) {
      Log = e.Error;
    }

    #region ZeroPositiveLoss Subject
    object _ZeroPositiveLossSubjectLocker = new object();
    ISubject<TradingMacro> _ZeroPositiveLossSubject;
    ISubject<TradingMacro> ZeroPositiveLossSubject {
      get {
        lock(_ZeroPositiveLossSubjectLocker)
          if(_ZeroPositiveLossSubject == null) {
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
      if(IsInVirtualTrading)
        AdjustCurrentLosses();
      else
        ZeroPositiveLossSubject.OnNext(tm);
    }
    #endregion


    void AdjustCurrentLosses_New() {
      var tms = GetTradingMacros();
      var tmsWithProfit = tms.Where(tm => tm.CurrentLoss > 0 && !tm.Trades.Any()).ToList();
      if(tmsWithProfit.Any()) {
        var tmWithLoss = tms.Where(tm => tm.CurrentLoss < 0).ToList();
        var lossSum = tmWithLoss.Sum(tm => tm.CurrentLoss).Abs();
        var profitTotal = tmsWithProfit.Sum(tm => tm.CurrentLoss);
        var profitToSpread = profitTotal.Min(lossSum);
        var tmByLoss = tmWithLoss.Select(tm => new { tm, profit = profitToSpread * tm.CurrentLoss.Abs() / lossSum }).ToList();
        tmByLoss.ForEach(tm => tm.tm.CurrentLoss += tm.profit);
        tmsWithProfit.ForEach(tm => tm.CurrentLoss = 0);
        if(tms.Any(tm => tm.Trades.Any())) {
          var tmsByProfit = tmsWithProfit.Select(tm => new { tm, profit = profitToSpread * tm.CurrentLoss / profitTotal }).ToList();
          tmsByProfit.ForEach(tmbp => tmbp.tm.CurrentLoss -= tmbp.profit);
        } else
          tms.Where(tm => tm.CurrentLoss > -0.1).ToList().ForEach(tm => tm.CurrentLoss = 0);
        //try { GlobalStorage.UseAliceContextSaveChanges(); } catch { }
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
      //GlobalStorage.UseAliceContextSaveChanges();
    }
    void fw_TradeClosed(object sender, TradeEventArgs e) {
      if(e.IsHandled)
        return;
      e.IsHandled = true;
      var trade = e.Trade;
      try {
        var pair = trade.Pair;
        GetTradingMacros(pair).Take(1).ForEach(tm => {
          tm.LastTrade = trade;
          var totalGross = trade.NetPL2;
          tm.LastTradeLoss = tm.TradesClosed.Where(t => t.OpenOrderID == trade.OpenOrderID).Net2().Min(0);
          tm.RunningBalance += totalGross;
          tm.CurrentLoss = tm.CurrentLoss + totalGross;
          OnZeroPositiveLoss(tm);
          SaveTradeAction.Post(trade);
        });
      } catch(Exception exc) {
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
      } catch(Exception exc) { Log = exc; }
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


    #endregion

    #region Helpers


    #region CanTrade
    private bool CanTrade(TradingMacro tm) {
      return tm.RatesArray.Count() > 0;
    }
    #endregion

    #region TradeExists
    bool TradeExists(Trade[] trades, Func<Trade, bool> condition) {
      if(trades.Length == 0)
        return false;
      return TradeExists(trades, trades[0].Pair, trades[0].IsBuy, condition);
    }
    bool TradeExists(Trade[] trades, string pair, bool isBuy, Func<Trade, bool> condition) {
      return trades.Any(t => t.IsBuy == isBuy && condition(t));
    }
    #endregion

    #region OpenTrade

    private void OpenTrade(bool buy, string pair, int lot, double limitInPips, double stopInPips, double stop, string remark) {
      if(!TradesManager.TryGetPrice(pair, out var price))
        throw new Exception(new { pair, error = "No price found" } + "");
      var limit = limitInPips == 0 ? 0 : buy ? price.Ask + TradesManager.InPoints(pair, limitInPips) : price.Bid - TradesManager.InPoints(pair, limitInPips);
      if(stop == 0 && stopInPips != 0)
        stop = buy ? price.Bid + TradesManager.InPoints(pair, stopInPips) : price.Ask - TradesManager.InPoints(pair, stopInPips);
      TradesManager.OpenTrade(pair, buy, lot, limit, stop, remark, price);
    }
    #endregion

    #region Get (stop/limit)
    private Rate[] GetRatesForCorridor(TradingMacro tm) {
      return GetRatesForCorridor(tm.RatesArray, tm);
    }
    private Rate[] GetRatesForCorridor(IEnumerable<Rate> rates, TradingMacro tm) {
      if(tm.CorridorStats == null)
        return rates.ToArray();
      return tm.CorridorStats?.StartDate.YieldNotNull().Select(sd => GetRatesForCorridor(rates, sd)).Concat().ToArray();
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
      var slack = fib.FibReverse().YofS(tm.CorridorStats?.HeightUpDown);
      tm.SlackInPips = TradesManager.InPips(tm.Pair, slack);
      return slack;
    }
    #endregion
    #endregion

    #region Init ...
    bool _useDb;
    private void InitInstruments() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
        try {
          if(_useDb && Instruments.Count == 0)
            TradesManager.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
        } catch(Exception exc) {
          Log = exc;
        }
        RaisePropertyChangedCore("TradingMacros");
      }));
    }
    void ScheduleInitTradingMacro(TradingMacro tm, bool unwind = false) {
      new Action(() => InitTradingMacro(tm, unwind)).ScheduleOnUI();
    }
    Dictionary<TradingMacro, IDisposable> _syncDisposables = new Dictionary<TradingMacro, IDisposable>();
    private void InitTradingMacro(TradingMacro tm, bool unwind = false) {
      var isFilterOk = tm.IsActive;
      if(!unwind && isFilterOk) {
        tm.TradingStatistics = _tradingStatistics;
        tm.IpPort = MasterModel.IpPort;
        try {
          tm.SubscribeToTradeClosedEVent(() => TradesManager, GetTradingMacros());
          _syncDisposables.Add(tm, tm.SyncObservable.Subscribe(_ => {
            UpdateTradingStatistics();
          }));
        } catch(Exception exc) {
          Log = exc;
        }
      } else
        try {
          tm.UnSubscribeToTradeClosedEVent(TradesManager);
          if(_syncDisposables.ContainsKey(tm)) {
            _syncDisposables[tm].Dispose();
            _syncDisposables.Remove(tm);
          }
          if(!isFilterOk)
            DeleteCharter(tm);
        } catch(Exception exc) {
          Log = exc;
        }

    }
    #endregion

    #endregion

  }
}
