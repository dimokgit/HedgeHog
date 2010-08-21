using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace HedgeHog.Alice.Store {
  public class RemoteControlModelBase : HedgeHog.Models.ModelBase {
    protected bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    public IQueryable<TradingMacro> TradingMacros {
      get {
        try {
          return !IsInDesigh ? GlobalStorage.Context.TradingMacroes.OrderBy(tm => tm.TradingGroup).ThenBy(tm => tm.PairIndex) : new[] { new TradingMacro() }.AsQueryable();
        } catch (Exception exc) {
          Debug.Fail("TradingMacros is null.");
          return null;
        }
      }
    }
    TradingMacro[] _tradingMacrosCiopy = new TradingMacro[0];
    public TradingMacro[] TradingMacrosCopy {
      get {
        if (_tradingMacrosCiopy.Length == 0)
          _tradingMacrosCiopy = TradingMacros.ToArray();
        return _tradingMacrosCiopy;
      }
    }
    protected IEnumerable<TradingMacro> GetTradingMacrosByGroup(TradingMacro tm) {
      return TradingMacrosCopy.Where(tm1 => tm1.TradingGroup == tm.TradingGroup);
    }
    protected TradingMacro GetTradingMacro(string pair) {
      var tms = TradingMacrosCopy.Where(tm => tm.Pair == pair).ToArray();
      if (tms.Length == 0)
        new NullReferenceException("TradingMacro is null");
      return tms.FirstOrDefault();
    }
  }
}
