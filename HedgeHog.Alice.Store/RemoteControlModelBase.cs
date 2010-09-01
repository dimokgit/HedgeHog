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

namespace HedgeHog.Alice.Store {
  public class RemoteControlModelBase : HedgeHog.Models.ModelBase {
    protected bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    protected Order2GoAddIn.FXCoreWrapper fw;
    protected Dictionary<string, List<Rate>> ratesByPair = new Dictionary<string, List<Rate>>();
    protected void SetRatesByPair(string pair, List<Rate> rates) {
      ratesByPair[pair] = rates;
    }
    protected List<Rate> GetRatesByPair(string pair) {
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
      var bufferSize = tm.CorridorBarMinutes;
      var ratesBuffer = GetRateFromDBBackward(VirtualPair, VirtualStartDate, bufferSize*2);
      Func<Rate, DateTime, bool> dateFilter = (r, d) => r.StartDate >= d && r.StartDate < virtualDateEnd;
      Func<DateTime, Rate> getRateBuffer = d => {
        var rate = ratesBuffer.Where(r => dateFilter(r, d)).FirstOrDefault();
        if (rate == null) {
          ratesBuffer = GetRateFromDB(VirtualPair, d, bufferSize);
          if (ratesBuffer.Length == 0) return null;
        }
        return ratesBuffer.Where(r => dateFilter(r, d)).FirstOrDefault();
      };
      backTestThread = Task.Factory.StartNew(() => {
        try {
          tradesManager.IsInTest = true;
          var rates = GetRatesByPair(VirtualPair);
          rates.Clear();
          rates.AddRange(ratesBuffer.Take(tm.CorridorBarMinutes));
          tradesManager.ClosePair(VirtualPair);
          tm.CurrentLoss = 0;
          tm.MinimumGross = 0;
          tm.HistoryMaximumLot=0;
          tm.RunningBalance = 0;
          while (rates.Count() > 0) {
            ct.ThrowIfCancellationRequested();
            if (Application.Current == null) break;
            Thread.Yield();
            virtualTrader.RaisePriceChanged(VirtualPair, rates.Last());
            Thread.Yield();
            var startDate = rates.Last().StartDate;
            var rate = getRateBuffer(startDate.AddMinutes(1));
            //GetRatesFromDBBars(GlobalStorage.ForexContext.t_Bar
            //.Where(b => b.Pair == VirtualPair && b.Period == 1 && b.StartDate > startDate).OrderBy(b => b.StartDate)).FirstOrDefault();
            if (rate == null) break;
            rates.RemoveAt(0);
            rates.Add(rate);
          }
          var ratesFx = new List<Rate>();
          fw.GetBars(VirtualPair, 1, rates.First().StartDate, rates.Last().StartDate, ref ratesFx);
          var pipSize = fw.GetPipSize(VirtualPair) / 10;
          for (var i = 0; i < rates.Count; i++)
            if ((rates[i].PriceAvg - ratesFx[i].PriceAvg).Abs() > pipSize)
              Debugger.Break();
          return;
        } finally {
          tradesManager.IsInTest = false;
        }
      },ct);
    }

    private static Rate[] GetRateFromDB(string pair, DateTime startDate, int corridorBarMinutes) {
      var bars = GlobalStorage.ForexContext.t_Bar.Where(b => b.Pair == pair && b.StartDate >= startDate).Take(corridorBarMinutes).OrderBy(b => b.StartDate);
      return GetRatesFromDBBars(bars);
    }
    private static Rate[] GetRateFromDBBackward(string pair, DateTime startDate, int corridorBarMinutes) {
      var bars = GlobalStorage.ForexContext.t_Bar.Where(b => b.Pair == pair && b.StartDate <= startDate).OrderByDescending(b => b.StartDate).Take(corridorBarMinutes);
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
    void OrderToNoLossHandler(object sender, Order2GoAddIn.FXCoreWrapper.OrderEventArgs e) {
      fw.DeleteEntryOrderLimit(e.Order.OrderID);
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
      var tms = TradingMacrosCopy.Where(tm => tm.Pair == pair).ToArray();
      if (tms.Length == 0)
        new NullReferenceException("TradingMacro is null");
      return tms.FirstOrDefault();
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
      if (!pricesByPair.ContainsKey(pair)) pricesByPair[pair] = fw.GetPrice(pair);
      return pricesByPair[pair];
    }
    #endregion
    protected Account accountCached = new Account();


    protected ITradesManager tradesManager = null;
    protected bool IsInVirtualTrading { get { return MasterModel == null ? false : MasterModel.IsInVirtualTrading; } }
    protected void ScanCorridor(string pair, List<Rate> rates) {
      Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
      if (rates.Count == 0) return;
      try {
        var sw = Stopwatch.StartNew();
        var swi = 0;
        var price = GetCurrentPrice(pair);
        var tm = GetTradingMacro(pair);
        var corridornesses = rates.ToArray().GetCorridornesses(tm.CorridorCalcMethod == CorridorCalculationMethod.StDev);
        foreach (int i in tm.CorridorIterationsArray) {
          var csCurr = rates.ScanCorridornesses(i, corridornesses, tm.CorridornessMin, tm.LimitCorridorByBarHeight ? tm.CorridorHeightMinimum : 0);
          //if (/*!tm.LimitCorridorByBarHeight &&*/ !CorridorStatistics.GetCorridorAverageHeightOk(tm, csCurr.AverageHeight))
          //  csCurr = rates.ScanCorridornesses(i, corridornesses, tm.CorridornessMin, tm.BarHeight60);
          if (csCurr == null) continue;
          var cs = tm.GetCorridorStats(csCurr.Iterations);
          cs.Init(csCurr.Density, csCurr.AverageHigh, csCurr.AverageLow, csCurr.AskHigh, csCurr.BidLow, csCurr.Periods, csCurr.EndDate, csCurr.StartDate, csCurr.Iterations);
          cs.FibMinimum = tm.CorridorFibMax(i - 1);
          cs.InPips = d => fw.InPips(pair, d);
        }
        tm.HeightFib = Fibonacci.FibRatio(tm.GetCorridorStats().First().AverageHeight, tm.GetCorridorStats().Last().AverageHeight);
        tm.CorridorStats = tm.GetCorridorStats().Where(cs => cs.IsCorridorAvarageHeightOk)
          .DefaultIfEmpty(tm.GetCorridorStats().First()).Last();
        // tm.HeightFib <= 1 ? tm.GetCorridorStats().Last() : tm.GetCorridorStats().First();
        //(from cs in tm.CorridorStatsArray
        //                  where cs.Height >= tm.CorridorHeightMinimum
        //                  orderby cs.CorridorFibAverage.Abs() // cs.Iterations
        //                  select cs
        //                  ).DefaultIfEmpty(tm.GetCorridorStats(0)).Last();
        //tm.TradeDistanceInPips = fw.InPips(tm.Pair, tm.CorridorStats.Height);
        //var takeProfitCS = tm.CorridorStatsArray.Where(cs => cs.Height * .9 > tm.CorridorStats.Height)
        //  .OrderBy(cs => cs.Height).DefaultIfEmpty(tm.GetCorridorStats(0)).First();
        tm.TakeProfitPips = fw.InPips(tm.Pair, Math.Max(tm.CorridorHeightMinimum, tm.CorridorStats.AverageHeight));
        //Debug.WriteLine("ScanCorridor[{1:n0}]:{0:n0}", sw.ElapsedMilliseconds, swi++); sw.Restart();
        #region Run Charter
        #endregion
      } catch (Exception exc) {
        Log = exc;
      }
    }


  }
}
