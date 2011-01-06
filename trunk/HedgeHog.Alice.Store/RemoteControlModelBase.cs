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

namespace HedgeHog.Alice.Store {
  public class RemoteControlModelBase : HedgeHog.Models.ModelBase {

    static string[] defaultInstruments = new string[] { "EUR/USD", "USD/JPY", "GBP/USD", "USD/CHF", "USD/CAD", "USD/SEK","EUR/JPY" };
    ObservableCollection<string> _Instruments = new ObservableCollection<string>(defaultInstruments);
    public ObservableCollection<string> Instruments { get { return _Instruments; } }

    protected bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    protected Order2GoAddIn.FXCoreWrapper fw;
    
    IMainModel _MasterModel;
    [Import]
    public IMainModel MasterModel {
      get { return _MasterModel; }
      set {
        if (_MasterModel != value) {
          _MasterModel = value;
          value.OrderToNoLoss += OrderToNoLossHandler;
          value.StartBackTesting += MasterModel_StartBackTesting;
          (value as INotifyPropertyChanged).PropertyChanged += MasterModel_PropertyChanged;
          RaisePropertyChangedCore();
        }
      }
    }

    protected virtual void MasterModel_PropertyChanged(object sender, PropertyChangedEventArgs e) {
    }

    protected VirtualTradesManager virtualTrader;
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
      if (!tm.IsInPlayback) { MessageBox.Show("Set Chart to Playback, dude!"); return; }
      tm.ResetSessionId();
      VirtualStartDate = e.StartDate.AddMinutes(-tm.BarsCount * tm.LimitBar); ;
      var minutesPerPeriod = tm.LimitBar;
      var ratesBuffer = GlobalStorage.GetRateFromDBBackward(VirtualPair, VirtualStartDate, tm.BarsCount, minutesPerPeriod).ToList();
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
          var rates = tm.Rates;
          rates.Clear();
          rates.AddRange(ratesBuffer.Take(tm.BarsCount));
          ratesBuffer.RemoveRange(0, tm.BarsCount);
          tradesManager.ClosePair(VirtualPair);
          if (e.ClearTest) {
            tm.CurrentLoss = 0;
            tm.CorridorStartDate = null;
            tm.CorridorStats = null;
            tm.Support = new Rate();
            tm.Resistance = new Rate();
            tm.MinimumGross = 0;
            tm.HistoryMaximumLot = 0;
            tm.RunningBalance = 0;
            tm.StrategyScoresReset();
          }
          ManualResetEvent suspendEvent = new ManualResetEvent(false);
          while (rates.Count() > 0) {
            ct.ThrowIfCancellationRequested();
            if (Application.Current == null) break;
            Thread.Yield();
            if (e.StepBack) {
              var dateStart = rates.Min(r => r.StartDate).AddHours(-1);
              rates.Clear();
              ratesBuffer = GlobalStorage.GetRateFromDBBackward(VirtualPair, dateStart, tm.BarsCount, tm.LimitBar);
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
              if( (tm.CorridorStats.StartDate - rates[0].StartDate) > TimeSpan.FromMinutes(minutesPerPeriod) )
                rates.RemoveRange(0, Math.Max(0, rates.Count - tm.BarsCount));
            }
          }
          if (fw.IsLoggedIn) {
            var ratesFx = new List<Rate>();
            fw.GetBars(VirtualPair, 1,tm.BarsCount, rates.First().StartDate, rates.Last().StartDate, ratesFx);
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

    public IQueryable<TradingMacro> TradingMacros {
      get {
        try {
          return !IsInDesigh ? GlobalStorage.Context.TradingMacroes.OrderBy(tm => tm.TradingGroup).ThenBy(tm => tm.PairIndex) : new[] { new TradingMacro() }.AsQueryable();
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
          return _tradingMacrosCopy.Where(tm => TradingMacroFilter(tm) || ShowAllMacrosFilter).ToArray();
        }
      }
    }
    protected bool TradingMacroFilter(TradingMacro tm) {
      return tm.IsActive && tm.TradingMacroName == MasterModel.TradingMacroName;
    }

    protected void TradingMacrosCopy_Add(TradingMacro tm) {
      _tradingMacrosCopy.Add(tm);
      ResetTradingMacros();
    }
    protected void TradingMacrosCopy_Delete(TradingMacro tm) {
      _tradingMacrosCopy.Remove(tm);
      ResetTradingMacros();
    }
    protected IEnumerable<TradingMacro> GetTradingMacrosByGroup(TradingMacro tm) {
      return TradingMacrosCopy.Where(tm1 => tm1.TradingGroup == tm.TradingGroup);
    }
    protected TradingMacro GetTradingMacro(string pair,int period) {
      return GetTradingMacros(pair).Where(tm => tm.LimitBar == period).SingleOrDefault();
    }
    protected TradingMacro[] GetTradingMacros(string pair = "") {
      return TradingMacrosCopy.Where(tm => new[] { tm.Pair, "" }.Contains(pair) && TradingMacroFilter(tm) ).OrderBy(tm => tm.PairIndex).ToArray();
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
          _ToggleShowActiveMacroCommand = new Gala.RelayCommand(ToggleShowActiveMacro, () => true);
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


    protected ITradesManager tradesManager { get { return IsInVirtualTrading ? virtualTrader : (ITradesManager)fw; } }
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
      var rs = tradingMacro.Rates.Where(r=>r.StartDate>=dateStart).GroupTicksToRates();
      var ratesForDensity = (reversePower ? rs.OrderBarsDescending() : rs.OrderBars()).ToArray();
      ratesForDensity.Index();
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
  public class RatesLoader {
    FXW fw = new FXW();
    Dictionary<string, Rate[]> ticksDictionary = new Dictionary<string, Rate[]>();
    public RatesLoader() {}
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
    public void ClearRates(string pair) { SetTicks(pair, new Rate[0]); }
    public void LoadRates(ITradesManager fw, string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate,List<Rate> ratesList) {
      try {
        var fetchRates = ratesList.Count() == 0;
        if (ratesList.Count() > 0 && (ratesList[0].StartDate - ratesList[1].StartDate).Duration() > TimeSpan.FromMinutes(periodMinutes))
          ratesList.Clear();
        //fw.GetBars(pair, fetchRates ? 1 : 0, startDate, DateTime.FromOADate(0), ref ticks);
        if (ratesList.Count() == 0) {
          if (periodMinutes > 0)
            ratesList.AddRange(fw.GetBarsFromHistory(pair, periodMinutes, DateTime.MinValue, endDate).Except(ratesList));
          else ratesList.AddRange(fw.GetTicks(pair, periodsBack).Except(ratesList));
        }
        if (periodMinutes == 0) {
          var d = ratesList.OrderBarsDescending().TakeWhile(t => t.StartDate.Millisecond == 0)
            .Select(r=>r.StartDate).DefaultIfEmpty(DateTime.MaxValue).Min();
          ratesList.RemoveAll(r => r.StartDate >= d);
        }
        fw.GetBars(pair, periodMinutes,periodsBack, startDate, endDate, ratesList);

      } catch (Exception exc) {
        Debug.Fail("load Rates",exc.ToString());
      }
    }
    void SaveToDB(string pair) {
      var lastTickInDB = GlobalStorage.Context.Bars.LastOrDefault();
      var dateLast = lastTickInDB == null ? DateTime.MinValue : lastTickInDB.StartDate;
      GetTicks(pair).Where(t => t.StartDate > dateLast).ToList().ForEach(t =>
        GlobalStorage.Context.Bars.AddObject(new Store.Bar() { AskClose = t.AskClose }));
    }
  }
}
