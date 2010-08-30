using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Alice.Store;
using System.Windows;
using System.Runtime.CompilerServices;

namespace HedgeHog.Alice.Store {
  public class GlobalStorage {
    static ForexEntities _forexContext;
    static AliceEntities _context;
    static object contextLocker = new object();
    static string _databasePath;
    public static string DatabasePath {
      get {
        if (string.IsNullOrWhiteSpace(_databasePath)) {
          _databasePath = OpenDataBasePath();
          if (string.IsNullOrWhiteSpace(_databasePath)) throw new Exception("No database path ptovided for AliceEntities");
        }
        return _databasePath; 
      }
      set { _databasePath = value; }
    }
    public static ForexEntities ForexContext {
      get {
        if (_forexContext == null) _forexContext = new ForexEntities();
        return _forexContext;
      }
    }

    public static AliceEntities Context {
      get {
        lock (contextLocker) {
          if (_context == null) {
            if (false && GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) {
              _context = new AliceEntities(
  "metadata=res://*/Models.Alice.csdl|res://*/Models.Alice.ssdl|res://*/Models.Alice.msl;provider=System.Data.SqlServerCe.3.5;provider connection string=\"Data Source=Store\\Alice.sdf\"");
            } else {
              var path = DatabasePath;
              _context = new AliceEntities();
              var storeConn = ((System.Data.SqlServerCe.SqlCeConnection)(((System.Data.EntityClient.EntityConnection)(((System.Data.Objects.ObjectContext)(_context)).Connection)).StoreConnection));
              storeConn.ConnectionString = "Data Source=" + path;// +"\\Store\\Alice.sdf";
            }
          }
          return _context;
        }
      }
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
  }
}
