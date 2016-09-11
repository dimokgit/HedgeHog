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
using System.Data.Entity.Core.Objects;
using System.IO;
using System.Text.RegularExpressions;

namespace HedgeHog.Alice.Store {
  public class RemoteControlModelBase : HedgeHog.Models.ModelBase {

    static string[] defaultInstruments = new string[] { "EUR/USD", "USD/JPY", "GBP/USD", "USD/CHF", "USD/CAD", "USD/SEK", "EUR/JPY" };
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
          RaisePropertyChangedCore();
        }
      }
    }

    public ITradesManager TradesManager { get { return MasterModel.TradesManager; } }
    public Trade[] GetClosedTrades(string pair) { return TradesManager.GetClosedTrades(pair); }
    public string VirtualPair { get; set; }
    public DateTime VirtualStartDate { get; set; }
    Task backTestThread;
    CancellationTokenSource cancellationForBackTesting;

    void OrderToNoLossHandler(object sender, OrderEventArgs e) {
      TradesManager.DeleteEntryOrderLimit(e.Order.OrderID);
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

    public static TradingMacro[] ReadTradingMacros() {
      var searchPath = GlobalStorage.ActiveSettingsPath(TradingMacrosPath("*", "*", "*", "*"));
      var paths = Directory.GetFiles(Path.GetDirectoryName(searchPath),Path.GetFileName(searchPath));
      return paths.ToArray(path => GlobalStorage.LoadJson<TradingMacro>(path));
    }
    static string WrapPair(string pair) {
      return pair.Replace("/", "");
    }
    static string UnWrapPair(string pair) {
      return Regex.Replace(pair, @"(\w3)(\w3)", "$1/$2");
    }
    public void SaveTradingMacros() {
      TradingMacros.ForEach(tm => GlobalStorage.SaveJson(tm, TradingMacrosPath(tm.TradingMacroName, tm.Pair, tm.TradingGroup, tm.PairIndex)));
    }
    protected void ResetTradingMacros() {
      //_tradingMacrosCopy = TradingMacros.ToArray();
      RaisePropertyChanged(() => TradingMacrosCopy);
    }

    protected delegate void Context_ObjectMaterializedDelegate(object sender, ObjectMaterializedEventArgs e);
    Context_ObjectMaterializedDelegate Context_ObjectMaterializer;

    protected static readonly string _tradingMacrosPath = "TradingMacros\\{0}+{1}_{2}_{3}.json";
    protected static string TradingMacrosPath(string name, string pair,object group, object index) { return _tradingMacrosPath.Formater(name, WrapPair(pair), group, index); }
    protected IList<TradingMacro> _TradingMacros;
    public IList<TradingMacro> TradingMacros {
      get {
        try {
          if(_TradingMacros == null)
            _TradingMacros = ReadTradingMacros();
              //!IsInDesigh
              //? GlobalStorage.UseAliceContext(Context => Context.TradingMacroes
              //.Where(tm => tm.TradingMacroName == MasterModel.TradingMacroName)
              //.OrderBy(tm => tm.TradingMacroName)
              //.ThenBy(tm => tm.TradingGroup)
              //.ThenBy(tm => tm.PairIndex)
              //.ToArray())
              //: new[] { new TradingMacro() };
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
          return _tradingMacrosCopy.Where(tm => IsInVirtualTrading && isAnySelected ? tm.IsSelectedInUI : (TradingMacroFilter(tm) || ShowAllMacrosFilter)).ToArray();
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
    protected IEnumerable<TradingMacro> GetTradingMacrosByGroup(TradingMacro tm, Func<TradingMacro, bool> predicate) {
      return GetTradingMacrosByGroup(tm).Where(predicate);
    }
    protected IEnumerable<TradingMacro> GetTradingMacrosByGroup(TradingMacro tm) {
      return TradingMacrosCopy.Where(tm1 => tm1.TradingGroup == tm.TradingGroup && tm.IsActive);
    }
    protected TradingMacro GetTradingMacro(string pair, int period) {
      return GetTradingMacros(pair).Where(tm => (int)tm.BarPeriod == period).SingleOrDefault();
    }
    protected Dictionary<string, IList<TradingMacro>> _tradingMacrosDictionary = new Dictionary<string, IList<TradingMacro>>(StringComparer.OrdinalIgnoreCase);
    protected IList<TradingMacro> GetTradingMacros(string pair = "") {
      pair = pair.ToLower();
      if (!_tradingMacrosDictionary.ContainsKey(pair))
        _tradingMacrosDictionary.Add(pair, TradingMacrosCopy.Where(tm => new[] { tm.Pair.ToLower(), "" }.Contains(pair) && tm.IsActive && TradingMacroFilter(tm)).OrderBy(tm => tm.PairIndex).ToList());
      return _tradingMacrosDictionary.ContainsKey(pair) ? _tradingMacrosDictionary[pair] : new TradingMacro[0];
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

    //protected Account accountCached = new Account();


    //    protected ITradesManager tradesManager { get { return IsInVirtualTrading ? virtualTrader : (ITradesManager)fw; } }
    public bool IsInVirtualTrading { get { return MasterModel == null ? false : MasterModel.IsInVirtualTrading; } }

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
      return FetchPriceBars(tradingMacro, rowOffset, reversePower, DateTime.MinValue);
    }
    protected PriceBar[] FetchPriceBars(TradingMacro tradingMacro, int rowOffset, bool reversePower, DateTime dateStart) {
      var isLong = dateStart == DateTime.MinValue;
      var rs = tradingMacro.RatesArraySafe.Where(r => r.StartDate >= dateStart).GroupTicksToRates();
      var ratesForDensity = (reversePower ? rs.OrderBarsDescending() : rs.OrderBars()).ToArray();
      SetPriceBars(tradingMacro, isLong, ratesForDensity.GetPriceBars(TradesManager.GetPipSize(tradingMacro.Pair), rowOffset));
      return GetPriceBars(tradingMacro, isLong);
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
}
