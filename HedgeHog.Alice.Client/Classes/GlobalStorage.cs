using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Alice.Client.Models;

namespace HedgeHog.Alice.Client {
  public class GlobalStorage {
    static AliceEntities _context;
    public static AliceEntities Context {
      get {
        if (_context == null) {
          if ( false && GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) {
            _context = new AliceEntities(
"metadata=res://*/Models.Alice.csdl|res://*/Models.Alice.ssdl|res://*/Models.Alice.msl;provider=System.Data.SqlServerCe.3.5;provider connection string=\"Data Source=Store\\Alice.sdf\"");
          } else {
            _context = new AliceEntities();
          }
        }
        return _context;
      }
    }

    public static TradingAccount[] GetTradingAccounts() {
      if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return new TradingAccount[] { };
      return Context.TradingAccounts.ToArray();
    }
  }
}
