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

namespace HedgeHog.Alice.Store {
  public class RemoteControlModelBase : HedgeHog.Models.ModelBase {

    static string[] defaultInstruments = new string[] { "EUR/USD", "USD/JPY", "GBP/USD", "USD/CHF", "USD/CAD", "USD/SEK" };
    ObservableCollection<string> _Instruments = new ObservableCollection<string>(defaultInstruments);
    public ObservableCollection<string> Instruments { get { return _Instruments; } }

    protected bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    protected Order2GoAddIn.FXCoreWrapper fw;
    protected Dictionary<string, List<Rate>> ratesByPair = new Dictionary<string, List<Rate>>();
    protected void SetRatesByPair(string pair, List<Rate> rates) {
      ratesByPair[pair] = rates;
    }
    protected List<Rate> GetRatesByPair(string pair) {
      if (!ratesByPair.ContainsKey(pair)) ratesByPair.Add(pair, new List<Rate>());
      return ratesByPair[pair];
      lock (ratesByPair[pair]) {
        var tm = GetTradingMacro(pair);
        return ratesByPair[pair].ToArray().Skip(ratesByPair[pair].Count - tm.BarsCount).ToList();
      }
    }
    protected List<Rate> GetRatesByPairRaw(string pair) {
      if (!ratesByPair.ContainsKey(pair)) ratesByPair.Add(pair, new List<Rate>());
      return ratesByPair[pair];
    }

    IMainModel _MasterModel;
    [Import]
    public IMainModel MasterModel {
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
      VirtualPair = e.Pair;
      VirtualStartDate = e.StartDate;

      var virtualDateEnd = VirtualStartDate.AddMonths(e.MonthToTest);
      var tm = GetTradingMacro(e.Pair);
      tm.ResetSessionId();
      var minutesPerPeriod = tm.LimitBar;
      var ratesBuffer = GetRateFromDBBackward(VirtualPair, VirtualStartDate, tm.BarsCount, minutesPerPeriod);
      Func<Rate, DateTime, bool> dateFilter = (r, d) => r.StartDate >= d && r.StartDate < virtualDateEnd;
      Func<DateTime, Rate> getRateBuffer = d => {
        var rate = ratesBuffer.Where(r => dateFilter(r, d)).FirstOrDefault();
        if (rate == null) {
          ratesBuffer = GetRateFromDB(VirtualPair, d, tm.BarsCount, minutesPerPeriod);
          if (ratesBuffer.Length == 0) return null;
        }
        return ratesBuffer.Where(r => dateFilter(r, d)).FirstOrDefault();
      };
      backTestThread = Task.Factory.StartNew(() => {
        try {
          tradesManager.IsInTest = true;
          var rates = GetRatesByPairRaw(VirtualPair);
          rates.Clear();
          rates.AddRange(ratesBuffer.Take(tm.BarsCount));
          tradesManager.ClosePair(VirtualPair);
          tm.CurrentLoss = 0;
          tm.MinimumGross = 0;
          tm.HistoryMaximumLot = 0;
          tm.RunningBalance = 0;
          var maPeriod = tm.LongMAPeriod;
          while (rates.Count() > 0) {
            ct.ThrowIfCancellationRequested();
            if (Application.Current == null) break;
            Thread.Yield();
            virtualTrader.RaisePriceChanged(VirtualPair, rates.Last());
            tm.Profitability = tm.RunningBalance / (rates.Last().StartDate - VirtualStartDate).TotalDays * 30;
            Thread.Yield();
            var startDate = rates.Last().StartDate;
            var rate = getRateBuffer(startDate.AddMinutes(1));
            if (rate == null) break;
            lock (rates) {
              rates.Add(rate);
              rates.RemoveRange(0, Math.Max(0, rates.Count - tm.BarsCount));
            }
          }
          if (fw.IsLoggedIn) {
            var ratesFx = new List<Rate>();
            fw.GetBars(VirtualPair, 1, rates.First().StartDate, rates.Last().StartDate, ref ratesFx);
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

    private static Rate[] GetRateFromDB(string pair, DateTime startDate, int barsCount,int minutesPerBar) {
      var bars = GlobalStorage.ForexContext.t_Bar.
        Where(b => b.Pair == pair && b.Period == minutesPerBar && b.StartDate >= startDate)
        .OrderBy(b => b.StartDate).Take(barsCount);
      return GetRatesFromDBBars(bars);
    }

    private static Rate[] GetRateFromDBBackward(string pair, DateTime startDate, int barsCount, int minutesPerPriod) {
      var endDate = startDate.AddMinutes(barsCount * minutesPerPriod);
      var bars = GlobalStorage.ForexContext.t_Bar
        .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate <= endDate)
        .OrderByDescending(b => b.StartDate).Take(barsCount);
      return GetRatesFromDBBars(bars);
    }

    private static Rate[] GetRatesFromDBBars(IQueryable<t_Bar> bars) {
      var ratesList = new List<Rate>();
      bars.ToList().ForEach(b => ratesList.Add(new Rate(b.AskHigh, b.AskLow, b.AskOpen, b.AskClose, b.BidHigh, b.BidLow, b.BidOpen, b.BidClose, b.StartDate)));
      //var rates = from b in bars
      //            select new Rate(b.AskHigh, b.AskLow, b.AskOpen, b.AskClose,b.BidHigh, b.BidLow, b.BidOpen, b.BidClose,b.StartDate) {
      //                //AskHigh = b.AskHigh, AskLow = b.AskLow, AskOpen = b.AskOpen, AskClose = b.AskClose,
      //                //BidHigh = b.BidHigh, BidLow = b.BidLow, BidOpen = b.BidOpen, BidClose = b.BidClose,
      //                //StartDate = b.StartDate
      //              };
      //return rates.OrderBars().ToArray();
      return ratesList.OrderBars().ToArray();
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
    private string _TradingMacroKey;
    public string TradingMacroKey {
      get { return _TradingMacroKey; }
      set {
        if (_TradingMacroKey != value) {
          _TradingMacroKey = value;
          _tradingMacrosCopy = TradingMacros.ToList();
          RaisePropertyChanged(() => TradingMacroKey);
          RaisePropertyChanged(() => TradingMacrosCopy);
        }
      }
    }

    protected void ResetTradingMacros() {
      //_tradingMacrosCopy = TradingMacros.ToArray();
      RaisePropertyChanged(() => TradingMacrosCopy);
    }

    public IQueryable<TradingMacro> TradingMacros {
      get {
        try {
          return !IsInDesigh ? GlobalStorage.Context.TradingMacroes.OrderBy(tm => tm.TradingGroup).ThenBy(tm => tm.PairIndex) : new[] { new TradingMacro() }.AsQueryable();
        } catch (Exception exc) {
          Debug.Fail("TradingMacros is null.");
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
          return _tradingMacrosCopy.Where(tm => tm.IsActive && tm.TradingMacroName == MasterModel.TradingMacroName || ShowAllMacrosFilter).ToArray();
        }
      }
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
    protected TradingMacro GetTradingMacro(string pair) {
      return GetTradingMacros(pair).FirstOrDefault();
    }
    protected TradingMacro[] GetTradingMacros(string pair) {
      return TradingMacrosCopy.Where(tm => tm.Pair == pair).OrderBy(tm=>tm.PairIndex).ToArray();
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


    protected ITradesManager tradesManager = null;
    protected bool IsInVirtualTrading { get { return MasterModel == null ? false : MasterModel.IsInVirtualTrading; } }

    protected void ScanCorridor(string pair, List<Rate> rates) {
      var tm = GetTradingMacro(pair);
      if (tm == null) return;
      try {
        if (rates.Count == 0 /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        if (false && !tm.IsTradingHours) return;
        var startDate = tm.CorridorStartDate;// new DateTime?[] { tm.CorridorStartDate, tm.Trades.Select(t => (DateTime?)t.Time).DefaultIfEmpty().Min() }.Min();
        var corridorPeriodsStart = startDate == null ? tm.CorridorPeriodsStart : 1;
        var corridorPeriodsLength = startDate == null ? tm.CorridorPeriodsLength : rates.Count(r => r.StartDate >= startDate);
        var corridornesses = rates.ToArray()
          .GetCorridornesses(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, tm.CorridorCalcMethod == CorridorCalculationMethod.StDev, corridorPeriodsStart, corridorPeriodsLength)
          .Where(c => tradesManager.InPips(tm.Pair, c.Value.HeightUpDown) > 0).Select(c=>c.Value).ToArray();
        var diffs = new List<double>();
        var corrs = corridornesses.GroupBy(c=>c.Corridornes).Select(c=>c.Key).OrderBy(c=>c).ToArray();
        corrs.Aggregate((cp, cn) => { if (cn != cp) diffs.Add(cn - cp); return cn; });
        var corridornessesMax = corrs[0] + diffs.ToArray().AverageByIterations((d1, d2) => d1 <= d2, 2).Average();
        Func<CorridorStatistics, bool> csFilter = cs => cs.Corridornes < corridornessesMax;
        if (corridornesses.Count() > 0) {
          foreach (int i in tm.CorridorIterationsArray) {
            var minCorr = corridornesses.Max(c => c.Corridornes) * .975;
            var a = corridornesses.Where(csFilter).Select(c => new { c.StartDate, c.Corridornes }).OrderBy(c => c.Corridornes).ToArray();
            var csCurr = corridornesses.Where(csFilter)./*OrderBy(c => c.Corridornes).*/OrderByDescending(c => c.StartDate).First();
            //rates.ScanCorridornesses(i, corridornesses, tm.CorridornessMin, 
            ///*tm.Trades.Length == 0 && */tm.LimitCorridorByBarHeight ? tm.CorridorHeightMinimum : 0);
            if (csCurr == null /*|| csCurr.AverageHeight < pricesByPair[pair].Spread * tm.CurrentLot/tm.LotSize*/) continue;
            var cs = tm.GetCorridorStats(csCurr.Iterations);
            cs.Init(csCurr.Density, csCurr.HeightUp, csCurr.HeightDown, csCurr.LineHigh, csCurr.LineLow, csCurr.Periods, csCurr.EndDate, csCurr.StartDate, csCurr.Iterations);
            cs.FibMinimum = tm.CorridorFibMax(i - 1);
            cs.InPips = d => tradesManager.InPips(pair, d);
          }
          tm.CorridorStats = tm.GetCorridorStats().Last();
          tm.TakeProfitPips = tm.CorridorHeightByRegressionInPips;
        } else {
          throw new Exception("No corridors found for current range.");
        }
        tm.PopupText = "";
      } catch (Exception exc) {
        tm.PopupText = exc.Message;
        Log = exc;
      }
    }
  }
}
