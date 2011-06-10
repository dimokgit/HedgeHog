using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text;
using Order2GoAddIn;
using HedgeHog.Shared;
using HedgeHog.Bars;
using System.Collections.Concurrent;
using GMT = GalaSoft.MvvmLight.Threading;
//using HedgeHog.Schedulers;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using NCCW = NotifyCollectionChangedWrapper;
using System.Runtime.CompilerServices;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Server {
  // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "PriceService" in both code and config file together.
  [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
  public class PriceService : IPriceService {
    #region Members
    static FXCoreWrapper _fw;
    Exception Log { set { MainWindowModel.Default.Log = value; } }
    static NCCW.NotifyCollectionChangedWrapper<PairInfo<Rate>> PairInfos { get { return MainWindowModel.Default.PairInfos; } }
    #endregion

    #region LoadRates
    static TimeSpan THROTTLE_INTERVAL = TimeSpan.FromSeconds(1);
    static object _LoadRatesSubjectLocker = new object();
    static ISubject<string> _LoadRatesSubject;
    private IDisposable _priceChangedSubscribsion;
    static ISubject<string> LoadRatesSubject {
      get {
        lock (_LoadRatesSubjectLocker)
          if (_LoadRatesSubject == null) {
            _LoadRatesSubject = new Subject<string>();
            _LoadRatesSubject
              .ObserveOn(Scheduler.ThreadPool)
              .Subscribe(LoadRates);
          }
        return _LoadRatesSubject;
      }
    }
    public static void OnLoadRates(string pair) {
      LoadRatesSubject.OnNext(pair);
    }
    private static void LoadRates(string pair) {
      try {
        var pairInfo = GetOrAddPairInfo(pair);
        var period = pairInfo.Period < 0 ? MainWindowModel.Default.Period : pairInfo.Period;
        var periods = pairInfo.Periods < 0 ? MainWindowModel.Default.Periods : pairInfo.Periods;
        if (pairInfo.Rates.Any()) pairInfo.Rates.Remove(pairInfo.Rates.Last());
        _fw.GetBars(pair, period, periods, TradesManagerStatic.FX_DATE_NOW, TradesManagerStatic.FX_DATE_NOW, pairInfo.Rates);
        pairInfo.Rates.SavePairCsv(pairInfo.Pair);
        AfterPairLoaded(pair);
      } catch (Exception exc) {
        MainWindowModel.Default.Log = exc;
      }
    }
    #endregion

    #region Ctor
    public PriceService() {
      _fw = new FXCoreWrapper(App.CoreFX);
      App.CoreFX.LoggedIn += CoreFX_LoggedInEvent;
      MainWindowModel.Default.PropertyChanged += MainWindowModel_PropertyChanged;
      //bl.PairLoaded += bl_PairLoaded;
    }

    void MainWindowModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
    }
    ~PriceService() {
      _fw.Dispose();
    }
    #endregion

    #region Event handlers
    static void RriceInfo_ReLoadRates(object sender, EventArgs e) {
      var pi = (PairInfo<Rate>)sender;
      pi.Rates.Clear();
      OnLoadRates(pi.Pair);
    }
    private static void AfterPairLoaded(string pair) {
      GetOrAddPairInfo(pair).UpdateStatistics();
    }
    void CoreFX_LoggedInEvent(object sender, LoggedInEventArgs e) {
      _priceChangedSubscribsion = Observable.FromEventPattern<EventHandler<PriceChangedEventArgs>, PriceChangedEventArgs>(
        h => h, h => _fw.PriceChanged += h, h => _fw.PriceChanged -= h)
        .GroupByUntil(pca=>pca.EventArgs.Pair,(d)=>Observable.Timer(55.FromSeconds()))
        .ObserveOn(Scheduler.ThreadPool)
        .Select(go=>go.TakeLast(1))
        .Subscribe(go => go.Subscribe(pce=> fw_PriceChanged(pce.Sender, pce.EventArgs)))
        //.Subscribe(el => {
        //  el.GroupBy(e2 => e2.EventArgs.Pair).Select(e2 => e2.Last()).ToList()
        //    .ForEach(ie => fw_PriceChanged(ie.Sender, ie.EventArgs));
        //})
        ;
      foreach (var pair in HedgeHog.Alice.Store.GlobalStorage.Instruments) {
        OnLoadRates(pair);
      }
    }
    void fw_PriceChanged(object sender, PriceChangedEventArgs e) {
      var pair = e.Price.Pair;
      OnLoadRates(pair);
    }
    #endregion

    #region Helpers
    [MethodImpl(MethodImplOptions.Synchronized)]
    private static PairInfo<Rate> GetOrAddPairInfo(string pair) {
      var pairInfo = PairInfos.SingleOrDefault(pi => pi.Pair == pair);
      if (pairInfo == null) {
        AddOfferPair(pair);
        var pi = new PairInfo<Rate>(pair, d => _fw.InPips(pair, d));
        pi.ReLoadRates += RriceInfo_ReLoadRates;
        PairInfos.Add(pi);
        return GetOrAddPairInfo(pair);
      }
      return pairInfo;
    }
    public static bool AddOfferPair(string pair) {
      App.CoreFX.SetOfferSubscription(pair);
      return true;
    }
    #endregion

    #region Contract methods
    public Rate[] FillPrice(string pair, DateTime startDate) {
      try {
        var pairInfo = GetOrAddPairInfo(pair);
        pairInfo.PullsCount++;
        var ratesOut = pairInfo.Rates.Where(r => r.StartDate > startDate).ToArray();
        return ratesOut;//.Take(3).ToArray();
      } catch (Exception exc) {
        Log = exc;
        throw;
      }
    }

    public PriceStatistics PriceStatistics(string pair) {
      try {
        var pairInfo = GetOrAddPairInfo(pair);
        var priceSats = new PriceStatistics() { BidHighAskLowSpread = pairInfo.BidHighToAskLowRatio };
        return priceSats;
      } catch (Exception exc) {
        Log = exc;
        throw;
      }
    }

    #endregion
  }
}
