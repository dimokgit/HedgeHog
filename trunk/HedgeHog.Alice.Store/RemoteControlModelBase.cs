using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Windows.Input;
using Gala = GalaSoft.MvvmLight.Command;
using System.ComponentModel.Composition;
using System.Windows;
using HedgeHog.Bars;
using System.Threading;
using HedgeHog.Shared;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using GalaSoft.MvvmLight.Command;

namespace HedgeHog.Alice.Store {
  public class RemoteControlModelBase : HedgeHog.Models.ModelBase {

    static string[] defaultInstruments = new string[] { "EUR/USD", "USD/JPY", "GBP/USD", "USD/CHF", "USD/CAD", "USD/SEK","EUR/JPY" };
    public ObservableCollection<string> Instruments {
      get { return GlobalStorage.Instruments; }
    }

    protected bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    protected Order2GoAddIn.FXCoreWrapper fwMaster { get { return MasterModel.FWMaster; } }
    
    TraderModelBase _MasterModel;
    [Import]
    public TraderModelBase MasterModel {
      get { return _MasterModel; }
      set {
        if (_MasterModel != value) {
          _MasterModel = value;
          value.OrderToNoLoss += OrderToNoLossHandler;
          value.StartBackTesting += MasterModel_StartBackTesting;
          RaisePropertyChangedCore();
        }
      }
    }

    protected ITradesManager tradesManager { get { return MasterModel.TradesManager; } }
    public string VirtualPair { get; set; }
    public DateTime VirtualStartDate { get; set; }
    Task backTestThread;
    CancellationTokenSource cancellationForBackTesting;

    void MasterModel_StartBackTesting(object sender, BackTestEventArgs e) {
      if (backTestThread != null && backTestThread.Status == TaskStatus.Running) {
        cancellationForBackTesting.Cancel();
        return;
      }
      cancellationForBackTesting = new CancellationTokenSource();
      var ct = cancellationForBackTesting.Token;

      var tm = GetTradingMacros("").First();
      VirtualPair = tm.Pair;
      if (tm.Strategy == Strategies.None) { MessageBox.Show("No strategy, dude!"); return; }
      //if (!tm.IsInPlayback) { MessageBox.Show("Set Chart to Playback, dude!"); return; }
      tm.ResetSessionId();
      tm.SuppResResetAllTradeCounts();
      var minutesPerPeriod = (int)tm.BarPeriod;
      VirtualStartDate = e.StartDate.AddMinutes(-tm.BarsCount * (int)tm.BarPeriod);
      var firstDate = GlobalStorage.UseForexContext(context => context.t_Bar.Where(b => b.Pair == VirtualPair && b.Period == minutesPerPeriod).Select(b => b.StartDate).DefaultIfEmpty(DateTime.MaxValue).Min());
      VirtualStartDate = new[] { VirtualStartDate, firstDate }.Max().DateTime;
      var ratesBuffer = GlobalStorage.GetRateFromDBBackward(VirtualPair, VirtualStartDate, tm.BarsCount, minutesPerPeriod).ToList();
      if (ratesBuffer.Count < tm.BarsCount)
        ratesBuffer = GlobalStorage.GetRateFromDB(VirtualPair, VirtualStartDate, tm.BarsCount, minutesPerPeriod);
      Func<Rate, DateTime, bool> dateFilter = (r, d) => r.StartDate >= d && r.StartDate < VirtualStartDate.AddMonths(e.MonthToTest);
      Func<DateTime, Rate> getRateBuffer = d => {
        if (ratesBuffer.Count == 0) {
          ratesBuffer = GlobalStorage.GetRateFromDB(VirtualPair, d, tm.BarsCount, minutesPerPeriod);
          if (ratesBuffer.Count() == 0) return null;
        }
        try {
          return ratesBuffer[0];
        } finally {
          ratesBuffer.RemoveAt(0);
        }
      };
      var hourlyDate = DateTime.MinValue;
      var hourlyAverage = 0.0;
      Func<Rate, double> getHourlyAverage = r => {
        if (r.StartDate - hourlyDate < TimeSpan.FromMinutes(15)) return hourlyAverage;
        var bars = GlobalStorage.GetRateFromDBBackward(VirtualPair, r.StartDate.AddDays(-1), 24 * 4, 15);
        hourlyDate = r.StartDate;
        return hourlyAverage = bars.GetMinuteTicks(60).Select(b => b.Spread).ToArray().AverageByIterations((v, a) => v >= a, 4).Average();
      };
      backTestThread = Task.Factory.StartNew(() => {
        try {
          tradesManager.IsInTest = true;
          var rates = tm.RatesInternal;
          rates.Clear();
          rates.AddRange(ratesBuffer.Take(tm.BarsCount));
          ratesBuffer.RemoveRange(0, tm.BarsCount);
          tradesManager.ClosePair(VirtualPair);
          if (e.ClearTest) {
            tm.CurrentLoss = 0;
            if (tm.CorridorStartDate != DateTime.Parse("1/1/2001"))
              tm.CorridorStartDate = null;
            tm.CorridorStats = null;
            tm.MinimumGross = 0;
            tm.HistoryMaximumLot = 0;
            tm.RunningBalance = 0;
            tm.RateGannCurrentLast = null;
          }
          ManualResetEvent suspendEvent = new ManualResetEvent(false);
          while (rates.Count() > 0) {
            ct.ThrowIfCancellationRequested();
            if (Application.Current == null) break;
            Thread.Yield();
            if (e.StepBack) {
              var dateStart = rates.Min(r => r.StartDate).AddHours(-1);
              rates.Clear();
              ratesBuffer = GlobalStorage.GetRateFromDBBackward(VirtualPair, dateStart, tm.BarsCount, (int)tm.BarPeriod);
              rates.AddRange(ratesBuffer.Take(tm.BarsCount));
              ratesBuffer.RemoveRange(0, tm.BarsCount);
              e.StepBack = false;
            }
            if (e.Delay != 0)
              Thread.Sleep((e.Delay * 1000).ToInt());
            Thread.Yield();
            if (e.Pause) {
              Thread.Sleep(100);
              continue;
            }
            (tradesManager as VirtualTradesManager).RaisePriceChanged(tm.Pair, rates.Last());
            tm.Profitability = tm.RunningBalance / (rates.Last().StartDate - VirtualStartDate).TotalDays * 30;
            Thread.Yield();
            var startDate = rates.Last().StartDate;
            var rate = getRateBuffer(startDate.AddMinutes(1));
            if (rate == null) break;
            lock (rates) {
              rates.Add(rate);
              tm.RatesArraySafe.Count();
              if(!tm.DoStreatchRates || (tm.CorridorStats.StartDate - rates[0].StartDate) > TimeSpan.FromMinutes(minutesPerPeriod) )
                rates.RemoveRange(0, Math.Max(0, rates.Count - tm.BarsCount));
              RaisePropertyChanged(Metadata.TradingMacroMetadata.RatesInternal);
            }
          }
          if (fwMaster.IsLoggedIn) {
            var ratesFx = new List<Rate>();
            fwMaster.GetBars(VirtualPair, 1,tm.BarsCount, rates.First().StartDate, rates.Last().StartDate, ratesFx);
            var pipSize = tradesManager.GetPipSize(VirtualPair) / 10;
            for (var i = 0; i < rates.Count; i++)
              if ((rates[i].PriceAvg - ratesFx[i].PriceAvg).Abs() > pipSize)
                Debugger.Break();
          }
          return;
        } catch (Exception exc) {
          Log = exc;
        } finally {
          tradesManager.IsInTest = false;
        }
      }, ct);
    }

    void OrderToNoLossHandler(object sender, OrderEventArgs e) {
      tradesManager.DeleteEntryOrderLimit(e.Order.OrderID);
    }
    public bool IsLoggedIn { get { return MasterModel.CoreFX.IsLoggedIn; } }

    protected Exception Log {
      set {
        if (MasterModel == null)
          MessageBox.Show(value + "");
        else
          MasterModel.Log = value;
      }
    }


    #region TradingMacros
    protected void ResetTradingMacros() {
      //_tradingMacrosCopy = TradingMacros.ToArray();
      RaisePropertyChanged(() => TradingMacrosCopy);
    }

    IQueryable<TradingMacro> _TradingMacros;
    public IQueryable<TradingMacro> TradingMacros {
      get {
        try {
          if (_TradingMacros == null)
            _TradingMacros = !IsInDesigh ? GlobalStorage.UseAliceContext(Context => Context.TradingMacroes.OrderBy(tm => tm.TradingGroup).ThenBy(tm => tm.PairIndex)) : new[] { new TradingMacro() }.AsQueryable();
          return _TradingMacros;
        } catch (Exception exc) {
          Debug.Fail(exc.ToString());
          return null;
        }
      }
    }
    List<TradingMacro> _tradingMacrosCopy = new List<TradingMacro>();
    public TradingMacro[] TradingMacrosCopy {
      get {
        if (MasterModel.TradingMacroName == "") {
          MessageBox.Show("Master Trading account mast have TradingMacroName.");
          return new TradingMacro[0];
        } else {
          if (_tradingMacrosCopy.Count == 0)
            _tradingMacrosCopy = TradingMacros.ToList();
          var isAnySelected = false;// _tradingMacrosCopy.Any(tm => tm.IsSelectedInUI);
          return _tradingMacrosCopy.Where(tm => IsInVirtualTrading && isAnySelected  ? tm.IsSelectedInUI : (TradingMacroFilter(tm) || ShowAllMacrosFilter)).ToArray();
        }
      }
    }
    protected bool TradingMacroFilter(TradingMacro tm) {
      return tm.TradingMacroName == MasterModel.TradingMacroName;
    }

    protected void TradingMacrosCopy_Add(TradingMacro tm) {
      _tradingMacrosCopy.Add(tm);
      ResetTradingMacros();
    }
    protected void TradingMacrosCopy_Delete(TradingMacro tm) {
      _tradingMacrosCopy.Remove(tm);
      ResetTradingMacros();
    }
    protected IEnumerable<TradingMacro> GetTradingMacrosByGroup(TradingMacro tm,Func<TradingMacro,bool> predicate) {
      return GetTradingMacrosByGroup(tm).Where(predicate);
    }
    protected IEnumerable<TradingMacro> GetTradingMacrosByGroup(TradingMacro tm) {
      return TradingMacrosCopy.Where(tm1 => tm1.TradingGroup == tm.TradingGroup && tm.IsActive);
    }
    protected TradingMacro GetTradingMacro(string pair,int period) {
      return GetTradingMacros(pair).Where(tm => (int)tm.BarPeriod == period).SingleOrDefault();
    }
    protected List<TradingMacro> GetTradingMacros(string pair = "") {
      return TradingMacrosCopy.Where(tm => new[] { tm.Pair, "" }.Contains(pair) && tm.IsActive && TradingMacroFilter(tm)).OrderBy(tm => tm.PairIndex).ToList();
    }
    #endregion

    #region Commands
    private bool _ShowAllMacrosFilter = false;
    public bool ShowAllMacrosFilter {
      get { return _ShowAllMacrosFilter; }
      set {
        if (_ShowAllMacrosFilter != value) {
          _ShowAllMacrosFilter = value;
          RaisePropertyChanged(() => ShowAllMacrosFilter);
          RaisePropertyChanged(() => TradingMacrosCopy);
        }
      }
    }


    ICommand _ToggleShowActiveMacroCommand;
    public ICommand ToggleShowActiveMacroCommand {
      get {
        if (_ToggleShowActiveMacroCommand == null) {
          _ToggleShowActiveMacroCommand = new RelayCommand(ToggleShowActiveMacro, () => true);
        }

        return _ToggleShowActiveMacroCommand;
      }
    }
    void ToggleShowActiveMacro() {
      ShowAllMacrosFilter = !ShowAllMacrosFilter;
    }


    #endregion

    #region PriceByPair
    Dictionary<string, Price> pricesByPair = new Dictionary<string, Price>();
    protected void SetCurrentPrice(Price price) {
      pricesByPair[price.Pair] = price;
    }
    protected Price GetCurrentPrice(string pair) {
      //if (!IsLoggedIn) return new Price();
      if (!pricesByPair.ContainsKey(pair)) pricesByPair[pair] = tradesManager.GetPrice(pair);
      return pricesByPair[pair];
    }
    #endregion
    protected Account accountCached = new Account();


//    protected ITradesManager tradesManager { get { return IsInVirtualTrading ? virtualTrader : (ITradesManager)fw; } }
    protected bool IsInVirtualTrading { get { return MasterModel == null ? false : MasterModel.IsInVirtualTrading; } }

    #region PriceBars
    protected class PriceBarsDuplex {
      public PriceBar[] Long { get; set; }
      public PriceBar[] Short { get; set; }
      public PriceBar[] GetPriceBars(bool isLong) { return isLong ? Long : Short; }
    }
    protected Dictionary<TradingMacro, PriceBarsDuplex> priceBarsDictionary = new Dictionary<TradingMacro, PriceBarsDuplex>();
    protected void SetPriceBars(TradingMacro tradingMacro, bool isLong, PriceBar[] priceBars) {
      if (!priceBarsDictionary.ContainsKey(tradingMacro))
        priceBarsDictionary.Add(tradingMacro, new PriceBarsDuplex());
      if (isLong)
        priceBarsDictionary[tradingMacro].Long = priceBars;
      else
        priceBarsDictionary[tradingMacro].Short = priceBars;
    }
    protected PriceBar[] FetchPriceBars(TradingMacro tradingMacro, int rowOffset, bool reversePower) {
      return FetchPriceBars(tradingMacro, rowOffset,reversePower, DateTime.MinValue);
    }
    protected PriceBar[] FetchPriceBars(TradingMacro tradingMacro, int rowOffset, bool reversePower, DateTime dateStart) {
      var isLong = dateStart == DateTime.MinValue;
      var rs = tradingMacro.RatesArraySafe.Where(r=>r.StartDate>=dateStart).GroupTicksToRates();
      var ratesForDensity = (reversePower ? rs.OrderBarsDescending() : rs.OrderBars()).ToArray();
      SetPriceBars(tradingMacro,isLong, ratesForDensity.GetPriceBars(tradesManager.GetPipSize(tradingMacro.Pair), rowOffset));
      return GetPriceBars(tradingMacro,isLong);
    }
    protected PriceBar[] GetPriceBars(TradingMacro tradingMacro, bool isLong) {
      return priceBarsDictionary.ContainsKey(tradingMacro) ? priceBarsDictionary[tradingMacro].GetPriceBars(isLong) : new PriceBar[0];
    }
    #endregion

    private static PriceBar LastPowerWave(PriceBar[] priceBarsForCorridor, DateTime powerStart) {
      Func<PriceBar, bool> dateFilter = pb => pb.StartDate < powerStart;
      return priceBarsForCorridor.OrderBy(pb => pb.StartDate).SkipWhile(dateFilter).OrderBy(pb => pb.Power).Last();
    }
    private static void SetCorrelations(TradingMacro tm, List<Rate> rates, CorridorStatistics csFirst, PriceBar[] priceBars) {
      var pbs = priceBars/*.Where(pb => pb.StartDate > csFirst.StartDate)*/.OrderBy(pb => pb.StartDate).Select(pb => pb.Power).ToArray();
      var rs = rates/*.Where(r => r.StartDate > csFirst.StartDate)*/.Select(r => r.PriceAvg).ToArray();
      tm.Correlation_P = global::alglib.pearsoncorrelation(pbs, rs);
      tm.Correlation_R = global::alglib.spearmancorr2(pbs, rs, Math.Min(pbs.Length, rs.Length));
    }
  }
  public class TaskerDispenser<TKey> {
    ConcurrentDictionary<TKey, Tasker> _taskers = new ConcurrentDictionary<TKey, Tasker>();
    public void RunOrEnqueue(TKey key, Action action, Action<Exception> logError) {
      if (!_taskers.ContainsKey(key)) _taskers[key] = new Tasker(key+"");
      _taskers[key].RunOrEnqueue(action,logError);
    }
  }
  public class Tasker {
    Task _task;
    Action _queuedAction;
    private string _name;
    private Guid _id;
    public Tasker(string name) {
      this._name = name;
      _id = Guid.NewGuid();
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void RunOrEnqueue(Action action, Action<Exception> logError) {
      if (_task == null || _task.IsCompleted) {
        _task = Task.Factory.StartNew(MakeActionInternal(action, logError));
        _task.ContinueWith(RunQueue);
      } else {
        //Debug.WriteLine("Tasker " + _name + " is busy.");
        //_queuedAction = MakeActionInternal(action, logError);
      }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    void RunQueue(Task task) {
      if (_queuedAction != null) {
        _task = Task.Factory.StartNew(_queuedAction);
        _queuedAction = null;
      }
    }
    Action MakeActionInternal(Action action, Action<Exception> logError) {
      return () => {
        try {
          action();
        } catch (Exception exc) {
          if (logError != null)
            logError(exc);
        }
      };
    }
  }
  
  public class RatesLoader {
    public void LoadRates(ITradesManager fw, string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList) {
      var fetchRates = ratesList.Count() == 0;
      if (ratesList.Count() > 0 && (ratesList[0].StartDate - ratesList[1].StartDate).Duration() > TimeSpan.FromMinutes(Math.Max(1, periodMinutes)))
        ratesList.Clear();
      //fw.GetBars(pair, fetchRates ? 1 : 0, startDate, DateTime.FromOADate(0), ref ticks);
      if (ratesList.Count() == 0) {
        if (periodMinutes > 0)
          ratesList.AddRange(fw.GetBarsFromHistory(pair, periodMinutes, TradesManagerStatic.FX_DATE_NOW, endDate).Except(ratesList));
        else ratesList.AddRange(fw.GetTicks(pair, periodsBack).Except(ratesList));
      }
      if (periodMinutes == 0) {
        var d = ratesList.OrderBarsDescending().TakeWhile(t => t.StartDate.Millisecond == 0)
          .Select(r => r.StartDate).DefaultIfEmpty(TradesManagerStatic.FX_DATE_NOW).Min();
        ratesList.RemoveAll(r => r.StartDate >= d);
      }
      fw.GetBars(pair, periodMinutes, periodsBack, startDate, endDate, ratesList,true);
    }
  }
}
