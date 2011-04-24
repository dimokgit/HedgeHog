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
using HedgeHog.Schedulers;
using System.Collections.ObjectModel;

namespace HedgeHog.Alice.Server {
  // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "PriceService" in both code and config file together.
  [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
  public class PriceService : IPriceService {
    #region Members
    static FXCoreWrapper _fw;
    ConcurrentDictionary<string, List<Rate>> _pairs = new ConcurrentDictionary<string, List<Rate>>();
    TaskTimerDispenser<string> _loadRatesTaskTimer;
    Exception Log { set { MainWindowModel.Default.Log = value; } }
    ObservableCollection<PairInfo> PairInfos { get { return MainWindowModel.Default.PairInfos; } }
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

    static BlockingLoader bl= new BlockingLoader();
    #endregion

    #region Ctor
    public PriceService() {
      _fw = new FXCoreWrapper(App.CoreFX);
      _fw.PriceChanged += fw_PriceChanged;
      App.CoreFX.LoggedIn += CoreFX_LoggedInEvent;
      bl.PairLoaded += bl_PairLoaded;
    }
    ~PriceService() {
      _fw.Dispose();
    }
    #endregion

    #region Event handlers
    void bl_PairLoaded(object sender, PriceService.BlockingLoader.PairLoadedEventArgs e) {
      var pairRates = _pairs[e.Pair];
      var pairInfo = GetOrAddPairInfo(e.Pair);
      pairInfo.LastDate = pairRates.Select(r => r.StartDate).DefaultIfEmpty().Max();
      pairInfo.Count = pairRates.Count;
    }
    void CoreFX_LoggedInEvent(object sender, LoggedInEventArgs e) {
      _pairs.Clear();
      foreach (var pair in App.CoreFX.Instruments)
        _pairs[pair] = new List<Rate>();
      if (_loadRatesTaskTimer == null)
        _loadRatesTaskTimer = new TaskTimerDispenser<string>(100, (s, ea) => Log = ea.Exception);
    }
    void fw_PriceChanged(object sender, PriceChangedEventArgs e) {
      List<Rate> pairList;
      var pair = e.Price.Pair;
      if (!_pairs.TryGetValue(pair, out pairList)) return;
      bl.Add(pair, pairList);
    }
    #endregion

    #region Helpers
    private PairInfo GetOrAddPairInfo(string pair) {
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

    public bool AddPair(string pair) {
      App.CoreFX.SetOfferSubscription(pair);
      return true;
    }
    #endregion
  }
}
