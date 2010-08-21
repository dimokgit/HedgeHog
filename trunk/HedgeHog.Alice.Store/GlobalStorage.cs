using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Alice.Store;

namespace HedgeHog.Alice.Store {
  public class GlobalStorage {
    static AliceEntities _context;
    static object contextLocker = new object();
    public static AliceEntities Context {
      get {
        lock (contextLocker) {
          if (_context == null) {
            if (false && GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) {
              _context = new AliceEntities(
  "metadata=res://*/Models.Alice.csdl|res://*/Models.Alice.ssdl|res://*/Models.Alice.msl;provider=System.Data.SqlServerCe.3.5;provider connection string=\"Data Source=Store\\Alice.sdf\"");
            } else {
              _context = new AliceEntities();
              var storeConn = ((System.Data.SqlServerCe.SqlCeConnection)(((System.Data.EntityClient.EntityConnection)(((System.Data.Objects.ObjectContext)(_context)).Connection)).StoreConnection));
              storeConn.ConnectionString = "Data Source="+Environment.CurrentDirectory + "\\Store\\Alice.sdf";
            }
          }
          return _context;
        }
      }
    }

    public static TradingAccount[] GetTradingAccounts() {
      if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return new TradingAccount[] { };
      return Context.TradingAccounts.ToArray();
    }
  }
}
