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
using System.Reactive.Subjects;
using System.Data.Objects;

namespace HedgeHog.Alice.Store {
  public class GlobalStorage {
    static ForexEntities _forexContext;
    static AliceEntities _context;
    static object contextLocker = new object();
    static string _databasePath;
    public static string DatabasePath {
      get {
        if (string.IsNullOrWhiteSpace(_databasePath) || !System.IO.Directory.Exists(_databasePath)) {
          _databasePath = OpenDataBasePath();
          if (string.IsNullOrWhiteSpace(_databasePath)) throw new Exception("No database path ptovided for AliceEntities");
        }
        return _databasePath; 
      }
      set { _databasePath = value; }
    }

    static ObservableCollection<string> _Instruments;// = new ObservableCollection<string>(defaultInstruments);
    public static ObservableCollection<string> Instruments {
      get {
        if (_Instruments == null)
          _Instruments = GlobalStorage.UseForexContext(context => new ObservableCollection<string>(context.v_Pair.Select(p => p.Pair)));
        return _Instruments;
      }
    }

    public static void UseForexContext(Action<ForexEntities> action) {
      try {
        using (var context = new ForexEntities())
          action(context);
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
        throw;
      }
    }
    public static T UseForexContext<T>(Func<ForexEntities,T> action) {
      try {
        using (var context = new ForexEntities())
          return action(context);
      } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
        throw;
      }
    }

    public static bool IsLocalDB { get { return false; } }
    static AliceEntities Context {
      get {
        lock (contextLocker)
          if (_context == null)
            if (!IsLocalDB) {
              _context = new AliceEntities();
              _context.ObjectMaterialized += new System.Data.Objects.ObjectMaterializedEventHandler(_context_ObjectMaterialized);
            } else
              if (false && GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic)
                _context = new AliceEntities("metadata=res://*/Models.Alice.csdl|res://*/Models.Alice.ssdl|res://*/Models.Alice.msl;provider=System.Data.SqlServerCe.3.5;provider connection string=\"Data Source=Store\\Alice.sdf\"");
              else
                _context = InitAliceEntityContext();
        return _context;
      }
    }
    #region AliceMaterializer Subject
    static object _AliceMaterializerSubjectLocker = new object();
    static ISubject<ObjectMaterializedEventArgs> _AliceMaterializerSubject;
    public static ISubject<ObjectMaterializedEventArgs> AliceMaterializerSubject {
      get {
        lock (_AliceMaterializerSubjectLocker)
          if (_AliceMaterializerSubject == null) {
            _AliceMaterializerSubject = new Subject<ObjectMaterializedEventArgs>();
          }
        return _AliceMaterializerSubject;
      }
    }
    static void OnAliceMaterializer(ObjectMaterializedEventArgs p) {
      AliceMaterializerSubject.OnNext(p);
    }
    #endregion

    static void _context_ObjectMaterialized(object sender, System.Data.Objects.ObjectMaterializedEventArgs e) {
      OnAliceMaterializer(e);
    }
    public static AliceEntities AliceContext { get { return Context; } }

    public static void UseAliceContextSaveChanges() {
      UseAliceContext(c => { }, true);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static void UseAliceContext(Action<AliceEntities> action, bool saveChanges = false) {
      try {
        action(Context);
        if (saveChanges) Context.SaveChanges();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
        throw;
      }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static T UseAliceContext<T>(Func<AliceEntities, T> action, bool saveChanges = false) {
      try {
        var r = action(Context);
        if (saveChanges) Context.SaveChanges();
        return r;
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(exc);
        throw;
      }
    }
    private static AliceEntities InitAliceEntityContext() {
      var path = DatabasePath;
      var context = new AliceEntities();
      var storeConn = ((System.Data.EntityClient.EntityConnection)(context.Connection)).StoreConnection;
      storeConn.ConnectionString = "Data Source=" + path;// +"\\Store\\Alice.sdf";
      return context;
    }

    public static string OpenDataBasePath() {
      var dlg = new Microsoft.Win32.OpenFileDialog();
      dlg.FileName = "Alice";
      dlg.DefaultExt = ".sdf";
      dlg.Filter = "SQL CE Files (.sdf)|*.sdf";
      if (dlg.ShowDialog() != true) return "";
      return dlg.FileName;
    }

    public static TradingAccount[] GetTradingAccounts() {
      if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return new TradingAccount[] { };
      return Context.TradingAccounts.ToArray();
    }
    public static List<Rate> GetRateFromDB(string pair, DateTime startDate, int barsCount, int minutesPerBar) {
      return GlobalStorage.UseForexContext(c => {
        var q = c.t_Bar
          .Where(b => b.Pair == pair && b.Period == minutesPerBar && b.StartDate >= startDate)
        .OrderBy(b => b.StartDate).Take(barsCount);
        return GetRatesFromDBBars(q);
      });
    }

    public static List<Rate> GetRateFromDBBackward(string pair, DateTime startDate, int barsCount, int minutesPerPriod) {
      return GlobalStorage.UseForexContext(context => {
        IQueryable<t_Bar> bars;
        if (minutesPerPriod == 0) {
          bars = context.t_Bar
            .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate >= startDate)
            .OrderBy(b => b.StartDate).Take(barsCount * 2);
        } else {
          var endDate = startDate.AddMinutes(barsCount * minutesPerPriod);
          bars = context.t_Bar
            .Where(b => b.Pair == pair && b.Period == minutesPerPriod && b.StartDate <= endDate)
            .OrderByDescending(b => b.StartDate).Take(barsCount);
        }
        return GetRatesFromDBBars(bars);
      });
    }

    static List<Rate> GetRatesFromDBBars(IQueryable<t_Bar> bars) {
      var ratesList = new List<Rate>();
      bars.ToList().ForEach(b => ratesList.Add(new Rate(b.AskHigh, b.AskLow, b.AskOpen, b.AskClose, b.BidHigh, b.BidLow, b.BidOpen, b.BidClose, b.StartDate.DateTime) { Volume = b.Volume }));
      return ratesList.OrderBars().ToList();
    }

  }
}
