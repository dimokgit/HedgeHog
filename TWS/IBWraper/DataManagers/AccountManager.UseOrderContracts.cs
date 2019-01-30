using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Runtime.CompilerServices;

namespace IBApp {
  public partial class AccountManager {
    public Action[] UseOrderContractsDeferred(Action<ConcurrentDictionary<int,OrderContractHolder>> func, int timeoutInMilliseconds = 10000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      //if(!_mutexOpenTrade.Wait(timeoutInMilliseconds)) {
      //  Trace(message + " could't enter Monitor");
      //  return new Action[0];
      //}
      Stopwatch sw = Stopwatch.StartNew();
      Action ret = () => {
        //_mutexOpenTrade.Release();
        if(sw.ElapsedMilliseconds > timeoutInMilliseconds)
          Trace(message + $" Spent {sw.ElapsedMilliseconds} ms");
      };
      try {
        func(OrderContractsInternal);
        return new[] { ret };
      } catch(Exception exc) {
        Trace(exc);
        ret();
      }
      return new Action[0];
    }

    public IList<T> UseOrderContracts<T>(Func<ConcurrentDictionary<int, OrderContractHolder>, T> func, int timeoutInMilliseconds = 10000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      //if(!_mutexOpenTrade.Wait(timeoutInMilliseconds)) {
      //  Trace(message + " could't enter Monitor");
      //  return new T[0];
      //}
      Stopwatch sw = Stopwatch.StartNew();
      T ret;
      try {
        ret = func(OrderContractsInternal);
      } catch(Exception exc) {
        Trace(exc);
        return new T[0];
      } finally {
        //_mutexOpenTrade.Release();
        if(sw.ElapsedMilliseconds > timeoutInMilliseconds) {
          Trace(message + $" Spent {sw.ElapsedMilliseconds} ms");
        }
      }
      return new[] { ret };
    }
    public void UseOrderContracts<T>(Func<ConcurrentDictionary<int, OrderContractHolder>, IEnumerable<T>> func, Action<T> action, [CallerMemberName] string Caller = "") {
      UseOrderContracts(_ => {
        func(_).ForEach(action);
        return Unit.Default;
      }, 10000, Caller).Count();

    }
    public void UseOrderContracts(Action<ConcurrentDictionary<int, OrderContractHolder>> action, [CallerMemberName] string Caller = "") {
      Func<ConcurrentDictionary<int, OrderContractHolder>, Unit> f = rates => { action(rates); return Unit.Default; };
      UseOrderContracts(f, 10000, Caller).Count();
    }
    public IList<T> UseOrderContracts<T>(Func<IBClientCore, ConcurrentDictionary<int, OrderContractHolder>, T> func, int timeoutInMilliseconds = 10000, [CallerMemberName] string Caller = "") {
      var message = $"{nameof(UseOrderContracts)}:{new { Caller, timeoutInMilliseconds }}";
      //if(false && !_mutexOpenTrade.Wait(timeoutInMilliseconds)) {
      //  Trace(message + " could't enter Monitor");
      //  return new T[0];
      //}
      Stopwatch sw = Stopwatch.StartNew();
      T ret;
      try {
        ret = func(IbClient, OrderContractsInternal);
      } catch(Exception exc) {
        Trace(exc);
        return new T[0];
      } finally {
        //_mutexOpenTrade.Release();
        if(sw.ElapsedMilliseconds > timeoutInMilliseconds) {
          Trace(message + $" Spent {sw.ElapsedMilliseconds} ms");
        }
      }
      return new[] { ret };
    }
  }
}
