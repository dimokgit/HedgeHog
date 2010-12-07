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
      lock (ratesByPair[pair]) {
        return ratesByPair[pair];
      }
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

      var tm = GetTradingMacro(e.Pair);
      if (tm.Strategy == Strategies.None) { MessageBox.Show("No strategy, dude!"); return; }
      tm.ResetSessionId();
      VirtualStartDate = e.StartDate.AddMinutes(-tm.BarsCount * tm.LimitBar); ;
      var minutesPerPeriod = tm.LimitBar;
      var ratesBuffer = GetRateFromDBBackward(VirtualPair, VirtualStartDate, tm.BarsCount, minutesPerPeriod).ToList();
      Func<Rate, DateTime, bool> dateFilter = (r, d) => r.StartDate >= d && r.StartDate < VirtualStartDate.AddMonths(e.MonthToTest);
      Func<DateTime, Rate> getRateBuffer = d => {
        if (ratesBuffer.Count == 0) {
          ratesBuffer = GetRateFromDB(VirtualPair, d, tm.BarsCount, minutesPerPeriod);
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
        var bars = GetRateFromDBBackward(VirtualPair, r.StartDate.AddDays(-1), 24*4, 15);
        hourlyDate = r.StartDate;
        return hourlyAverage = bars.GetMinuteTicks(60).Select(b => b.Spread).ToArray().AverageByIterations((v, a) => v >= a, 4).Average();
      };
      backTestThread = Task.Factory.StartNew(() => {
        try {
          tradesManager.IsInTest = true;
          var rates = GetRatesByPairRaw(VirtualPair);
          rates.Clear();
          rates.AddRange(ratesBuffer.Take(tm.BarsCount));
          ratesBuffer.RemoveRange(0, tm.BarsCount);
          tradesManager.ClosePair(VirtualPair);
          if (e.ClearTest) {
            tm.CurrentLoss = 0;
            tm.MinimumGross = 0;
            tm.HistoryMaximumLot = 0;
            tm.RunningBalance = 0;
            tm.StrategyScoresReset();
          }
          var maPeriod = tm.LongMAPeriod;
          ManualResetEvent suspendEvent = new ManualResetEvent(false);
          while (rates.Count() > 0) {
            ct.ThrowIfCancellationRequested();
            if (Application.Current == null) break;
            Thread.Yield();
            if (e.StepBack) {
              var dateStart = rates.Min(r => r.StartDate).AddHours(-1);
              rates.Clear();
              ratesBuffer = GetRateFromDBBackward(e.Pair, dateStart, tm.BarsCount, tm.LimitBar);
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
            tm.TradingDistance = tradesManager.InPips(VirtualPair, getHourlyAverage(rates.Last()));
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

    private static List<Rate> GetRateFromDB(string pair, DateTime startDate, int barsCount, int minutesPerBar) {
      var bars = GlobalStorage.ForexContext.t_Bar.
        Where(b => b.Pair == pair && b.Period == minutesPerBar && b.StartDate >= startDate)
        .OrderBy(b => b.StartDate).Take(barsCount);
      return GetRatesFromDBBars(bars);
    }

    private static List<Rate> GetRateFromDBBackward(string pair, DateTime startDate, int barsCount, int minutesPerPriod) {
      IQueryable<Store.t_Bar> bars;
      if (minutesPerPriod == 0) {
        bars = GlobalStorage.ForexContext.t_Bar
          .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate >= startDate)
          .OrderBy(b => b.StartDate).Take(barsCount*2);
      } else {
        var endDate = startDate.AddMinutes(barsCount * minutesPerPriod);
        bars = GlobalStorage.ForexContext.t_Bar
          .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate <= endDate)
          .OrderByDescending(b => b.StartDate).Take(barsCount);
      }
      return GetRatesFromDBBars(bars);
    }

    private static List<Rate> GetRatesFromDBBars(IQueryable<t_Bar> bars) {
      var ratesList = new List<Rate>();
      bars.ToList().ForEach(b => ratesList.Add(new Rate(b.AskHigh, b.AskLow, b.AskOpen, b.AskClose, b.BidHigh, b.BidLow, b.BidOpen, b.BidClose, b.StartDate)));
      //var rates = from b in bars
      //            select new Rate(b.AskHigh, b.AskLow, b.AskOpen, b.AskClose,b.BidHigh, b.BidLow, b.BidOpen, b.BidClose,b.StartDate) {
      //                //AskHigh = b.AskHigh, AskLow = b.AskLow, AskOpen = b.AskOpen, AskClose = b.AskClose,
      //                //BidHigh = b.BidHigh, BidLow = b.BidLow, BidOpen = b.BidOpen, BidClose = b.BidClose,
      //                //StartDate = b.StartDate
      //              };
      //return rates.OrderBars().ToArray();
      return ratesList.OrderBars().ToList();
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
      return TradingMacrosCopy.Where(tm => tm.Pair == pair).OrderBy(tm => tm.PairIndex).ToArray();
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

    #region PriceBars
    protected class PriceBarsDuplex {
      public PriceBar[] Long { get; set; }
      public PriceBar[] Short { get; set; }
      public PriceBar[] GetPriceBars(bool isLong) { return isLong ? Long : Short; }
    }
    protected Dictionary<string, PriceBarsDuplex> priceBarsDictionary = new Dictionary<string, PriceBarsDuplex>();
    protected void SetPriceBars(string pair, bool isLong, PriceBar[] priceBars) {
      if (!priceBarsDictionary.ContainsKey(pair))
        priceBarsDictionary.Add(pair, new PriceBarsDuplex());
      if (isLong)
        priceBarsDictionary[pair].Long = priceBars;
      else
        priceBarsDictionary[pair].Short = priceBars;
    }
    protected PriceBar[] FetchPriceBars(string pair, int rowOffset, bool reversePower) {
      return FetchPriceBars(pair, rowOffset,reversePower, DateTime.MinValue);
    }
    protected PriceBar[] FetchPriceBars(string pair, int rowOffset, bool reversePower, DateTime dateStart) {
      var isLong = dateStart == DateTime.MinValue;
      var rs = GetRatesByPair(pair).Where(r=>r.StartDate>=dateStart).GroupTicksToRates();
      var ratesForDensity = (reversePower ? rs.OrderBarsDescending() : rs.OrderBars()).ToArray();
      ratesForDensity.Index();
      SetPriceBars(pair,isLong, ratesForDensity.GetPriceBars(tradesManager.GetPipSize(pair), rowOffset));
      return GetPriceBars(pair,isLong);
    }
    protected PriceBar[] GetPriceBars(string pair,bool isLong) {
      return priceBarsDictionary.ContainsKey(pair) ? priceBarsDictionary[pair].GetPriceBars(isLong) : new PriceBar[0];
    }
    #endregion

    protected void ScanCorridor(string pair, List<Rate> rates) {
      var tm = GetTradingMacro(pair);
      if (tm == null) return;
      try {
        if (rates.Count == 0 /*|| !IsTradingHours(tm.Trades, rates.Last().StartDate)*/) return;
        if (false && !tm.IsTradingHours) return;
        #region Prepare Corridor
        var ratesForSpread = tm.LimitBar == 0?rates.GetMinuteTicks(1).OrderBars().ToArray() :rates.ToArray();
        var spreadShort = ratesForSpread.Skip(ratesForSpread.Count() - 10).ToArray().AverageByIterations(r => r.Spread, 1).Average(r => r.Spread);
        var spreadLong = ratesForSpread.AverageByIterations(r => r.Spread, 1).Average(r => r.Spread);
        var spread = tradesManager.InPips(pair, Math.Max(spreadLong, spreadShort));
        var priceBars = FetchPriceBars(pair, tm.PowerRowOffset, tm.ReversePower).OrderByDescending(pb => pb.StartDate).ToArray();
        var powerBars = priceBars.Select(pb => pb.Power).ToArray();
        var powerBarShort = priceBars.Take((priceBars.Count() / 10.0).ToInt()).OrderBy(pb => pb.Power).Last();
        tm.PowerAverage = powerBars.AverageByIterations((v, a) => v >= a, tm.IterationsForPower).Average();//stAvgPower + stDevPower * tm.PowerVolatilityMinimum
        var corridorMinimum = tm.CorridorHeightBySpreadRatio * (spread + MasterModel.CommissionByTrade(new Trade() { Lots = 10000 }));
        Func<CorridorStatistics, double> heightToMinimum = cs => corridorMinimum / tradesManager.InPips(pair, cs.HeightUpDown);
        Func<CorridorStatistics, bool> filter = cs => heightToMinimum(cs) < 1;
        var powerMinimum = tm.PowerAverage;
        var powerAverage = priceBars.Average(pb => pb.Power);
        var priceBarsForCorridor = priceBars.SkipWhile(pb => pb.Power > powerAverage).ToArray();
        var priceBarDates = priceBarsForCorridor
          .Where(pb => pb.Spread > corridorMinimum && pb.Power > powerMinimum)
          .Select(pb => pb.StartDate).DefaultIfEmpty(DateTime.MaxValue);
        var powerStart = tm.TradeByFirstWave.HasValue ? !tm.TradeByFirstWave.Value ? priceBarDates.Min() : priceBarDates.Max() : DateTime.MinValue;
        var powerBar = !tm.TradeByFirstWave.HasValue ? priceBarsForCorridor.OrderBy(pb => pb.Power).Last() :
          !tm.TradeByFirstWave.Value
          ? priceBarsForCorridor.OrderBy(pb => pb.StartDate).SkipWhile(pb => pb.StartDate < powerStart).TakeWhile(pb => pb.Power > powerMinimum).OrderBy(pb => pb.Power).Last()
          : priceBarsForCorridor.OrderByDescending(pb => pb.StartDate).SkipWhile(pb => pb.StartDate > powerStart).TakeWhile(pb => pb.Power > powerMinimum).OrderBy(pb => pb.Power).Last();
        var startDate = tm.CorridorStartDate ?? powerBar.StartDate;
        var periodsLength = startDate == null ? tm.CorridorPeriodsLength : 1;
        var periodsStart = Math.Min(rates.Count - 1, startDate == null ? tm.CorridorPeriodsStart : rates.Count(r => r.StartDate >= startDate));
        var corridornesses = rates.GetCorridornesses(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, periodsStart, periodsLength, tm.IterationsForCorridorHeights, false)
          //.Where(c => tradesManager.InPips(tm.Pair, c.Value.HeightUpDown) > 0)
          .Select(c => c.Value).ToArray();
        var corridorBig = rates.ScanCorridorWithAngle(TradingMacro.GetPriceHigh, TradingMacro.GetPriceLow, tm.IterationsForCorridorHeights, false);
        tm.BigCorridorHeight = corridorBig.HeightUpDown;
        #endregion
        #region Update Corridor
        if (corridornesses.Count() > 0) {
          foreach (int i in tm.CorridorIterationsArray) {
            //var a = corridornesses.Where(filter).Select(c => new { c.StartDate, c.Corridornes }).OrderBy(c => c.Corridornes).ToArray();
            var csCurr = corridornesses.OrderBy(c => c.Corridornes).First();
            //rates.ScanCorridornesses(i, corridornesses, tm.CorridornessMin, 
            ///*tm.Trades.Length == 0 && */tm.LimitCorridorByBarHeight ? tm.CorridorHeightMinimum : 0);
            var cs = tm.GetCorridorStats(csCurr.Iterations);
            cs.Init(csCurr.Density, csCurr.Slope, csCurr.HeightUp, csCurr.HeightDown, csCurr.LineHigh, csCurr.LineLow, csCurr.Periods, csCurr.EndDate, csCurr.StartDate, csCurr.Iterations);
            if (!filter(cs)) {
              var d = heightToMinimum(cs);
              cs.HeightUp *= d;
              cs.HeightDown *= d;
            }
            cs.FibMinimum = tm.CorridorFibMax(i - 1);
            cs.InPips = d => tradesManager.InPips(pair, d);
            SetCorrelations(tm, rates, cs, priceBars);
          }
          tm.CorridorStats = tm.GetCorridorStats().Last();
          tm.TakeProfitPips = tm.CorridorHeightByRegressionInPips;
          tm.RangeCorridorHeight = corridornesses.Last().HeightUpDown;
          var priceBarsShort = FetchPriceBars(pair,tm.PowerRowOffset,tm.ReversePower,tm.CorridorStats.StartDate)
            .OrderByDescending(pb=>pb.StartDate).ToArray();
          var stAvgPower = priceBarsShort.Average(pb=>pb.Power);
          var stDevPower = priceBarsShort.StdDev(pb => pb.Power);
          tm.PowerCurrent = priceBarsShort[0].Power;
          tm.PowerVolatility = stDevPower / stAvgPower;

        } else {
          throw new Exception("No corridors found for current range.");
        }
        #endregion
        tm.PopupText = "";
      } catch (Exception exc) {
        tm.PopupText = exc.Message;
      }
    }

    protected static double TradesMultiplier(TradingMacro tm) {
      return Math.Pow(tm.Trades.Select(t => t.Lots).DefaultIfEmpty(tm.LotSize).Max() / tm.LotSize, 0.6);
    }
    private static void SetCorrelations(TradingMacro tm, List<Rate> rates, CorridorStatistics csFirst, PriceBar[] priceBars) {
      var pbs = priceBars/*.Where(pb => pb.StartDate > csFirst.StartDate)*/.OrderBy(pb => pb.StartDate).Select(pb => pb.Power).ToArray();
      var rs = rates/*.Where(r => r.StartDate > csFirst.StartDate)*/.Select(r => r.PriceAvg).ToArray();
      tm.Correlation_P = global::alglib.pearsoncorrelation(pbs, rs);
      tm.Correlation_R = global::alglib.spearmancorr2(pbs, rs, Math.Min(pbs.Length, rs.Length));
    }
  }
}
