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
//using HedgeHog.Schedulers;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace HedgeHog.Alice.Server {
  // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "PriceService" in both code and config file together.
  [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
  public class PriceService : IPriceService {
    #region Members
    static FXCoreWrapper _fw;
    static ConcurrentDictionary<string, List<Rate>> _pairs = new ConcurrentDictionary<string, List<Rate>>();
    Exception Log { set { MainWindowModel.Default.Log = value; } }
    static ObservableCollection<PairInfo> PairInfos { get { return MainWindowModel.Default.PairInfos; } }
    #endregion

    #region Price Loader
    class BlockingLoader : BlockingConsumerBase<Tuple<string, List<Rate>>> {
      #region PairLoaded Event
      public class PairLoadedEventArgs : EventArgs {
        public string Pair { get; set; }
        public PairLoadedEventArgs(string pair) {
          this.Pair = pair;
        }
      }
      public event EventHandler<PairLoadedEventArgs> PairLoaded;
      protected void RaisePairLoaded(string pair) {
        if (PairLoaded != null) PairLoaded(this, new PairLoadedEventArgs(pair));
      }
      #endregion
      public BlockingLoader() {
        Init(t => {
          try {
            _fw.GetBars(t.Item1, 1, 120, TradesManagerStatic.FX_DATE_NOW, TradesManagerStatic.FX_DATE_NOW, t.Item2);
            RaisePairLoaded(t.Item1);
          } catch (Exception exc) { MainWindowModel.Default.Log = exc; }
        });
      }
      public void Add(string pair, List<Rate> rates) {
        Add(new Tuple<string,List<Rate>>(pair,rates), (t1, t2) => t1.Item1 == t2.Item1);
      }
    }

    static BlockingLoader bl_= new BlockingLoader();
    #endregion

    #region LoadRates Subject
    static TimeSpan THROTTLE_INTERVAL = TimeSpan.FromSeconds(1);
    static object _LoadRatesSubjectLocker = new object();
    static ISubject<string> _LoadRatesSubject;
    static ISubject<string> LoadRatesSubject {
      get {
        lock (_LoadRatesSubjectLocker)
          if (_LoadRatesSubject == null) {
            _LoadRatesSubject = new Subject<string>();
            _LoadRatesSubject
              .ObserveOn(Scheduler.ThreadPool)
              //.SubscribeOn(Scheduler.NewThread)
              .Subscribe(pair => {
                _fw.GetBars(pair, MainWindowModel.Default.Period, MainWindowModel.Default.Periods, TradesManagerStatic.FX_DATE_NOW, TradesManagerStatic.FX_DATE_NOW, _pairs[pair]);
                  AfterPairLoaded(pair);
              });
          }
        return _LoadRatesSubject;
      }
    }

    public void OnLoadRates(string pair) {
      LoadRatesSubject.OnNext(pair);
    }
    #endregion



    #region Ctor
    public PriceService() {
      _fw = new FXCoreWrapper(App.CoreFX);
      _fw.PriceChanged += fw_PriceChanged;
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
    void bl_PairLoaded(object sender, PriceService.BlockingLoader.PairLoadedEventArgs e) {
      var pair = e.Pair;
      AfterPairLoaded(pair);
    }

    private static void AfterPairLoaded(string pair) {
      var pairRates = _pairs[pair];
      var pairInfo = GetOrAddPairInfo(pair);
      pairInfo.LastDate = pairRates.Select(r => r.StartDate).DefaultIfEmpty().Max();
      pairInfo.Count = pairRates.Count;
    }
    void CoreFX_LoggedInEvent(object sender, LoggedInEventArgs e) {
      _pairs.Clear();
      foreach (var pair in HedgeHog.Alice.Store.GlobalStorage.Instruments) {
        App.CoreFX.SetOfferSubscription(pair);
        _pairs[pair] = new List<Rate>();
        OnLoadRates(pair);
        //bl.Add(pair, _pairs[pair]);
      }
    }
    void fw_PriceChanged(object sender, PriceChangedEventArgs e) {
      List<Rate> pairList;
      var pair = e.Price.Pair;
      if (!_pairs.TryGetValue(pair, out pairList)) return;
      OnLoadRates(pair);
      //bl.Add(pair, pairList);
    }
    #endregion

    #region Helpers
    private static PairInfo GetOrAddPairInfo(string pair) {
      var pairInfo = PairInfos.SingleOrDefault(pi => pi.Pair == pair);
      if (pairInfo == null) {
        AddPair(pair);
        GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(new Action(() => {
          PairInfos.Add(new PairInfo(pair));
        }));
        return GetOrAddPairInfo(pair);
      }
      return pairInfo;
    }
    #endregion

    #region Contract methods
    public Rate[] FillPrice(string pair, DateTime startDate) {
      try {
        List<Rate> ratesLocal;
        if (!_pairs.ContainsKey(pair)) {
          AddPair(pair);
          _pairs[pair] = new List<Rate>();
        }
        if (_pairs.TryGetValue(pair, out ratesLocal)) {
          var pairInfo = GetOrAddPairInfo(pair);
          pairInfo.PullsCount++;
          var ratesOut = ratesLocal.Where(r => r.StartDate > startDate).ToArray();
          return ratesOut;//.Take(3).ToArray();
        }
        return new Rate[0];
      } catch (Exception exc) {
        Log = exc;
        throw;
      }
    }

    public static bool AddPair(string pair) {
      App.CoreFX.SetOfferSubscription(pair);
      return true;
    }
    #endregion
  }
}
