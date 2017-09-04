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
using FXW = HedgeHog.Shared.ITradesManager;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using GalaSoft.MvvmLight.Command;
using System.Data.Entity.Core.Objects;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.ExceptionServices;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace HedgeHog.Alice.Store {
  public class RemoteControlModelBase : HedgeHog.Models.ModelBase {

    static string[] defaultInstruments = new string[] { "EUR/USD", "USD/JPY", "GBP/USD", "USD/CHF", "USD/CAD", "USD/SEK", "EUR/JPY" };
    public ObservableCollection<string> Instruments {
      get { return GlobalStorage.Instruments; }
    }

    protected bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }

    TraderModelBase _MasterModel;
    [Import]
    public TraderModelBase MasterModel {
      get { return _MasterModel; }
      set {
        if(_MasterModel != value) {
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

    void OrderToNoLossHandler(object sender, OrderEventArgs e) {
      TradesManager.DeleteEntryOrderLimit(e.Order.OrderID);
    }
    public bool IsLoggedIn { get { return MasterModel.CoreFX.IsLoggedIn; } }

    protected Exception Log {
      set {
        if(MasterModel == null)
          MessageBox.Show(value + "");
        else
          MasterModel.Log = value;
        ReplayArguments.LastWwwErrorObservable.OnNext(value.Message);
      }
    }

    ReplayArguments<TradingMacro> _replayArguments = new ReplayArguments<TradingMacro>();
    public ReplayArguments<TradingMacro> ReplayArguments {
      get { return _replayArguments; }
    }

    #region TradingMacros

    public static TradingMacro[] ReadTradingMacros(string tradingacroName, List<Exception> errors) {
      var searchPath = GlobalStorage.ActiveSettingsPath(TradingMacrosPath(tradingacroName.IfEmpty("*"), "*", "*", "*"));
      var paths = Directory.GetFiles(Path.GetDirectoryName(searchPath), Path.GetFileName(searchPath));

      return (from path in paths
              select new { tm = GlobalStorage.LoadJson<TradingMacro>(path, errors), path }
              )
              .Do(x => {
                x.tm.IsLoaded = true;
                if(x.tm == null)
                  errors.Add(new Exception(x + ""));
              })
              .Select(x => x.tm)
              //.Where(tm => tm != null)
              .ToArray();
    }

    public class DynamicContractResolver : DefaultContractResolver {

      public DynamicContractResolver() {
      }

      protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization) {
        IList<JsonProperty> properties = base.CreateProperties(type, memberSerialization);

        // only serializer properties that start with the specified character
        var saveAttrs = new Type[] { typeof(DataMemberAttribute), typeof(WwwSettingAttribute) };
        Func<PropertyInfo, bool> ok = pi => {
          var attrs = pi.GetCustomAttributes().ToArray();
          var should = (from a in attrs
                        join sa in saveAttrs on a.GetType() equals sa
                        select true);
          return should.Any();
        };
        var activeSettings = TradingMacro.GetActiveProprties(true, (pi, category) => new { pi, category });
        return (from s in activeSettings
                join p in properties on s.pi.Name equals p.PropertyName
                orderby s.category, s.pi.Name
                select p).ToList();

      }
    }

    public void SaveTradingMacros() {
      TradingMacros.ForEach(tm => GlobalStorage.SaveJson(tm.Serialize(false), TradingMacrosPath(MasterModel.MasterAccount.TradingMacroName, tm.Pair, tm.TradingGroup, tm.PairIndex)));
    }
    protected void ResetTradingMacros() {
      //_tradingMacrosCopy = TradingMacros.ToArray();
      RaisePropertyChanged(() => TradingMacrosCopy);
    }

    protected delegate void Context_ObjectMaterializedDelegate(object sender, ObjectMaterializedEventArgs e);
    Context_ObjectMaterializedDelegate Context_ObjectMaterializer;

    protected virtual void Context_ObjectMaterialized(object sender, ObjectMaterializedEventArgs e) { throw new NotImplementedException(); }
    protected static readonly string _tradingMacrosPath = "TradingMacros\\{0}+{1}_{2}_{3}.json";
    protected static string TradingMacrosPath(string name, string pair, object group, object index) { return _tradingMacrosPath.Formater(name, TradesManagerStatic.WrapPair(pair), group, index); }
    protected IList<TradingMacro> _TradingMacros;
    public IList<TradingMacro> TradingMacros {
      get {
        try {
          if(_TradingMacros == null) {
            var errors = new List<Exception>();
            //this.MasterModel.MasterAccount.cas
            _TradingMacros = ReadTradingMacros(MasterModel.MasterAccount.TradingMacroName, errors);
            if(errors.Any())
              throw errors.First();
            _TradingMacros.ForEach(tm => Context_ObjectMaterialized(tm, null));
          }
          //!IsInDesigh
          //? GlobalStorage.UseAliceContext(Context => Context.TradingMacroes
          //.Where(tm => tm.TradingMacroName == MasterModel.TradingMacroName)
          //.OrderBy(tm => tm.TradingMacroName)
          //.ThenBy(tm => tm.TradingGroup)
          //.ThenBy(tm => tm.PairIndex)
          //.ToArray())
          //: new[] { new TradingMacro() };
          return _TradingMacros;
        } catch(Exception exc) {
          Log = exc;
          ExceptionDispatchInfo.Capture(exc).Throw();
          throw;
        }
      }
    }
    List<TradingMacro> _tradingMacrosCopy = new List<TradingMacro>();
    public TradingMacro[] TradingMacrosCopy {
      get {
        if(_tradingMacrosCopy.Count == 0)
          _tradingMacrosCopy = TradingMacros
            .OrderBy(tm => !tm.IsActive)
            .ThenBy(tm => tm.TradingGroup)
            .ThenBy(tm => tm.PairIndex)
            .ToList();
        return _tradingMacrosCopy.ToArray();
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
      if(!_tradingMacrosDictionary.ContainsKey(pair))
        _tradingMacrosDictionary.Add(pair, TradingMacrosCopy.Where(tm => new[] { tm.Pair.ToLower(), "" }.Contains(pair) && tm.IsActive).OrderBy(tm => tm.PairIndex).ToList());
      return _tradingMacrosDictionary.ContainsKey(pair) ? _tradingMacrosDictionary[pair] : new TradingMacro[0];
    }
    #endregion

    #region Commands
    private bool _ShowAllMacrosFilter = false;
    public bool ShowAllMacrosFilter {
      get { return _ShowAllMacrosFilter; }
      set {
        if(_ShowAllMacrosFilter != value) {
          _ShowAllMacrosFilter = value;
          RaisePropertyChanged(() => ShowAllMacrosFilter);
          RaisePropertyChanged(() => TradingMacrosCopy);
        }
      }
    }


    ICommand _ToggleShowActiveMacroCommand;
    public ICommand ToggleShowActiveMacroCommand {
      get {
        if(_ToggleShowActiveMacroCommand == null) {
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
    public bool IsInVirtualTrading { get { return MasterModel == null ? false : MasterModel.MasterAccount.IsVirtual; } }

    #region PriceBars
    protected class PriceBarsDuplex {
      public PriceBar[] Long { get; set; }
      public PriceBar[] Short { get; set; }
      public PriceBar[] GetPriceBars(bool isLong) { return isLong ? Long : Short; }
    }
    protected Dictionary<TradingMacro, PriceBarsDuplex> priceBarsDictionary = new Dictionary<TradingMacro, PriceBarsDuplex>();
    protected void SetPriceBars(TradingMacro tradingMacro, bool isLong, PriceBar[] priceBars) {
      if(!priceBarsDictionary.ContainsKey(tradingMacro))
        priceBarsDictionary.Add(tradingMacro, new PriceBarsDuplex());
      if(isLong)
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
