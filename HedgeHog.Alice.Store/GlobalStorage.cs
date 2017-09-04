using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Alice.Store;
using System.Windows;
using System.Runtime.CompilerServices;
using HedgeHog.Bars;
using HedgeHog.DB;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Data;
using NotifyCollectionChangedWrapper;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Threading;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Net;
using System.Runtime.ExceptionServices;

namespace HedgeHog.Alice.Store {
  public class GlobalStorage : Models.ModelBase {
    static GlobalStorage _Instance;
    public static GlobalStorage Instance {
      get { return GlobalStorage._Instance ?? (GlobalStorage._Instance = new GlobalStorage()); }
      set {
        if(GlobalStorage._Instance != null)
          throw new InvalidOperationException("GlobalStorage has already been instantiated.");
        GlobalStorage._Instance = value;
      }
    }
    static GlobalStorage() {
      Instance = new GlobalStorage();
    }
    static ForexEntities _forexContext;
    static AliceEntities _context;
    static object contextLocker = new object();
    static string _databasePath;
    public static string DatabasePath {
      get {
        if(string.IsNullOrWhiteSpace(_databasePath) || !System.IO.Directory.Exists(_databasePath)) {
          _databasePath = OpenDataBasePath();
          if(string.IsNullOrWhiteSpace(_databasePath))
            throw new Exception("No database path ptovided for AliceEntities");
        }
        return _databasePath;
      }
      set { _databasePath = value; }
    }

    public static string ActiveSettingsPath(string path) {
      return Path.IsPathRooted(path) ? path : Path.Combine(Lib.CurrentDirectory, "Settings", path);
    }

    public class GenericRow<T> {
      public int Index { get; set; }
      public T Value { get; set; }
      public GenericRow(int index, T value) {
        this.Index = index;
        this.Value = value;
      }
    }

    public GlobalStorage() {
      //Instance = this;
      //var list = new[] { new { Index = 1, Value = 48 } };
      //SetGenericList(list);
    }
    private static ListCollectionView _GenericList { get; set; }

    public ListCollectionView GenericList {
      get { return GlobalStorage._GenericList; }
      set {
        GlobalStorage._GenericList = value;
        RaisePropertyChangedCore();
      }
    }

    public NotifyCollectionChangedWrapper<T> SetGenericList<T>(IEnumerable<T> list) {
      var collectionWrapper = new NotifyCollectionChangedWrapper<T>(new ObservableCollection<T>(list));
      GenericList = new ListCollectionView(collectionWrapper);
      return collectionWrapper;
    }
    #region GenericList Subject
    object _GenericListSubjectLocker = new object();
    ISubject<Action> _GenericListSubject;
    ISubject<Action> GenericListSubject {
      get {
        lock(_GenericListSubjectLocker)
          if(_GenericListSubject == null) {
            _GenericListSubject = new Subject<Action>();
            _GenericListSubject
              .Sample(0.5.FromSeconds())
              //.Latest().ToObservable(new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; }))
              .SubscribeOn(new DispatcherScheduler(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher))
              .Subscribe(s => s(), exc => { });
          }
        return _GenericListSubject;
      }
    }
    void OnGenericList(Action p) {
      GenericListSubject.OnNext(() => GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(new Action(p), System.Windows.Threading.DispatcherPriority.Background));
    }
    #endregion

    object GenericListLocker = new object();
    public void ResetGenericList<T>(IEnumerable<T> list) {
      OnGenericList(() => {
        lock(GenericListLocker) {
          if(GenericList == null)
            SetGenericList(list);
          else {
            while(GenericList.Count > 0)
              GenericList.RemoveAt(0);
            list.ForEach(l => {
              GenericList.AddNewItem(l);
              GenericList.CommitNew();
            });
            //GenericList.MoveCurrentToPosition(selectedIndex);
          }
        }
      });
    }

    static ObservableCollection<string> _Instruments;// = new ObservableCollection<string>(defaultInstruments);
    public static ObservableCollection<string> Instruments {
      get {
        if(_Instruments == null)
          _Instruments = GlobalStorage.UseForexContext(context => new ObservableCollection<string>(context.v_Pair.Select(p => p.Pair)));
        return _Instruments;
      }
    }
    static void SetTimeout(IObjectContextAdapter oca, int timeOut) {
      oca.ObjectContext.CommandTimeout = timeOut;
    }

    static ForexEntities ForexEntitiesFactory() {
      var fe = new ForexEntities();
      SetTimeout(fe, 60 * 1);
      return fe;
    }
    public static void UseForexContext(Action<ForexEntities> action, Action<ForexEntities, Exception> error = null) {
      using(var context = ForexEntitiesFactory())
        try {
          action(context);
        } catch(Exception exc) {
          if(error != null)
            error(context, exc);
          else {
            GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
            throw;
          }
        }
    }
    public static T UseForexContext<T>(Func<ForexEntities, T> action) {
      try {
        using(var context = ForexEntitiesFactory()) {
          SetTimeout(context, 60 * 1);
          return action(context);
        }
      } catch(Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
        throw;
      }
    }

    public static bool IsLocalDB { get { return false; } }
    #region AliceMaterializer Subject
    static object _AliceMaterializerSubjectLocker = new object();
    static ISubject<ObjectMaterializedEventArgs> _AliceMaterializerSubject;
    public static ISubject<ObjectMaterializedEventArgs> AliceMaterializerSubject {
      get {
        lock(_AliceMaterializerSubjectLocker)
          if(_AliceMaterializerSubject == null) {
            _AliceMaterializerSubject = new Subject<ObjectMaterializedEventArgs>();
          }
        return _AliceMaterializerSubject;
      }
    }
    static void OnAliceMaterializer(ObjectMaterializedEventArgs p) {
      AliceMaterializerSubject.OnNext(p);
    }
    #endregion

    static void _context_ObjectMaterialized(object sender, System.Data.Entity.Core.Objects.ObjectMaterializedEventArgs e) {
      OnAliceMaterializer(e);
    }

    #region JSON
    class AllPropertiesResolver : DefaultContractResolver {
      protected override List<MemberInfo> GetSerializableMembers(Type objectType) {
        return objectType.GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0)
            .Cast<MemberInfo>()
            .ToList();
      }
    }
    public static void SaveJson<T>(T source, string path) {
      SaveJson(source, path, null, null);
    }
    public static void SaveJson<T>(T source, string path, JsonConverter converter) {
      SaveJson(source, path, null, converter);
    }
    public static void SaveJson<T>(T source, string path, IContractResolver contractResolver) {
      SaveJson(source, path, contractResolver, null);
    }
    public static void SaveJson<T>(T source, string path, IContractResolver contractResolver, JsonConverter converter) {
      var settings = new Newtonsoft.Json.JsonSerializerSettings();
      if(contractResolver != null)
        settings.ContractResolver = contractResolver;
      if(converter != null)
        settings.Converters.Add(converter);
      settings.Converters.Add(new StringEnumConverter());
      SaveJson(JsonConvert.SerializeObject(source, Newtonsoft.Json.Formatting.Indented, settings), path);

    }
    public static void SaveJson(string json, string path) {
      var settingsPath = ActiveSettingsPath(path);
      var dir = Path.GetDirectoryName(settingsPath);
      Directory.CreateDirectory(dir);
      File.WriteAllText(settingsPath, json);

    }

    static string DownloadText(Uri url) {
      using(var wc = new WebClient())
        return wc.DownloadString(url);
    }
    public static T LoadJson<T>(string path, List<Exception> errors = null) {
      string _path() => !Path.HasExtension(path) ? path = ".json" : path;
      var json = Uri.IsWellFormedUriString(path, UriKind.Absolute)
        ? DownloadText(new Uri(path))
        : File.ReadAllText(ActiveSettingsPath(_path()));
      return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings {
        Error = (s, e) => {
          if(errors==null)
            ExceptionDispatchInfo.Capture(e.ErrorContext.Error).Throw();
          e.ErrorContext.Handled = true;
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(e.ErrorContext.Error);
          errors.Add(e.ErrorContext.Error);
        }
      });
    }

    #endregion
    public static string OpenDataBasePath() {
      var dlg = new Microsoft.Win32.OpenFileDialog();
      dlg.FileName = "Alice";
      dlg.DefaultExt = ".sdf";
      dlg.Filter = "SQL CE Files (.sdf)|*.sdf";
      if(dlg.ShowDialog() != true)
        return "";
      return dlg.FileName;
    }

    public static List<Rate> GetRateFromDBByDateRange(string pair, DateTime startDate, DateTime endDate, int minutesPerBar) {
      return GlobalStorage.UseForexContext(c => {
        var q = c.t_Bar
          .Where(b => b.Pair == pair && b.Period == minutesPerBar && b.StartDate >= startDate && b.StartDate < endDate)
        .OrderBy(b => b.StartDate);
        return GetRatesFromDBBars(q);
      });
    }
    public static List<Rate> GetRateFromDB(string pair, DateTime startDate, int barsCount, int minutesPerBar) {
      return GlobalStorage.UseForexContext(c => {
        var q = c.t_Bar
          .Where(b => b.Pair == pair && b.Period == minutesPerBar && b.StartDate >= startDate)
        .OrderBy(b => b.StartDate).Take(barsCount);
        return GetRatesFromDBBars(q);
      });
    }

    public static List<Rate> GetRateFromDBBackward(string pair, DateTime endDate, int barsCount, int minutesPerPriod) {
      return GlobalStorage.UseForexContext(context => {
        IQueryable<t_Bar> bars = context.t_Bar
            .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate <= endDate)
            .OrderByDescending(b => b.StartDate).Take(barsCount);
        return GetRatesFromDBBars(bars);
      });
    }
    public static List<TBar> GetRateFromDBBackwards<TBar>(string pair, DateTime endDate, int barsCount, int minutesPerPriod, Func<List<TBar>, List<TBar>> map) where TBar : BarBase, new() {
      try {
        map = map ?? new Func<List<TBar>, List<TBar>>(rs => rs);
        var rates = map(GetRateFromDBBackwards<TBar>(pair, endDate, barsCount, minutesPerPriod));
        while(rates.Count < barsCount) {
          var moreRates = map(GetRateFromDBBackwards<TBar>(pair, rates[0].StartDate.ToUniversalTime(), barsCount, minutesPerPriod));
          if(moreRates.Count == 0)
            throw new Exception(new { pair, barsCount, minutesPerPriod, error = "Don't have that much." } + "");
          rates = moreRates.Take(barsCount - rates.Count).Concat(rates).ToList();
        }
        return rates;
      } catch(Exception exc) {
        throw new Exception(new { pair, endDate, barsCount, minutesPerPriod } + "", exc);
      }
    }
    public static List<TBar> GetRateFromDBBackwards<TBar>(string pair, DateTime endDate, int barsCount, int minutesPerPriod) where TBar : BarBase, new() {
      return (minutesPerPriod == 0
        ? GetRateFromDBBackwardsInternal<Tick>(pair, endDate, barsCount, minutesPerPriod).Cast<TBar>().ToList()
        : GetRateFromDBBackwardsInternal<TBar>(pair, endDate, barsCount, minutesPerPriod));
    }
    static List<TBar> GetRateFromDBBackwardsInternal<TBar>(string pair, DateTime endDate, int barsCount, int minutesPerPriod) where TBar : BarBase, new() {
      try {
        return GlobalStorage.UseForexContext(context => {
          var bars = context.t_Bar
              .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate <= endDate)
              .OrderByDescending(b => b.StartDate)
              .Take(barsCount)
              .ToList()
              .OrderBy(b => b.StartDate)
              .ThenBy(b => b.Row);
          return GetRatesFromDbBars<TBar>(bars.ToList());
        });
      } catch(Exception exc) {
        throw new Exception(new { pair, endDate, barsCount, minutesPerPriod } + "", exc);
      }
    }

    static List<Rate> GetRatesFromDBBars(IQueryable<t_Bar> bars) {
      var ratesList = new List<Rate>();
      bars.ToList().ForEach(b => ratesList.Add(new Rate(b.AskHigh, b.AskLow, b.AskOpen, b.AskClose, b.BidHigh, b.BidLow, b.BidOpen, b.BidClose, b.StartDate.UtcDateTime) {
        Volume = b.Volume
      }));
      return ratesList.OrderBars().ToList();
    }
    public static List<TBar> GetRateFromDBForwards<TBar>(string pair, DateTimeOffset startDate, int barsCount, int minutesPerPriod) where TBar : BarBase, new() {
      return (minutesPerPriod == 0
        ? GetRateFromDBForwardsInternal<Tick>(pair, startDate, barsCount, minutesPerPriod).Cast<TBar>().ToList()
        : GetRateFromDBForwardsInternal<TBar>(pair, startDate, barsCount, minutesPerPriod));
    }
    static List<TBar> GetRateFromDBForwardsInternal<TBar>(string pair, DateTimeOffset startDate, int barsCount, int minutesPerPriod) where TBar : BarBase, new() {
      return GlobalStorage.UseForexContext(context => {
        var bars = context.t_Bar
          .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate >= startDate)
          .OrderBy(b => b.StartDate)
          .ThenBy(b => b.Row)
          .Take(barsCount);
        return GetRatesFromDbBars<TBar>(bars.ToList());
      });
    }
    static List<TBar> GetRatesFromDbBars<TBar>(IList<t_Bar> bars) where TBar : BarBase, new() {
      var bs = bars.Select(b => {
        var bar = new TBar() {
          AskHigh = b.AskHigh,
          AskLow = b.AskLow,
          AskOpen = b.AskOpen,
          AskClose = b.AskClose,
          BidHigh = b.BidHigh,
          BidLow = b.BidLow,
          BidOpen = b.BidOpen,
          BidClose = b.BidClose,
          StartDate2 = b.StartDate,
          Volume = b.Volume,
          IsHistory = true
        };
        var tick = bar as Tick;
        if(tick != null)
          tick.Row = b.Row;
        return bar;
      }).ToList();
      var ticks = Lazy.Create(() => bs.Cast<Tick>().ToArray());
      if(typeof(TBar) == typeof(Tick) && ticks.Value.Select(t => t.Row).DefaultIfEmpty(1).Max() == 0) {
        ticks.Value
          .GroupBy(t => t.StartDate2)
          .ForEach(g => g.ForEach((t, i) => t.Row = i));
      }
      return bs;
    }

  }
}
