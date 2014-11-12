using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class WF {
    public delegate void OnLoop(List<object> list);
    public delegate void OnExit();
    public delegate bool MustExit();
    public delegate DateTime StartDate();
    public class DateStart : Box<DateTime> {
      public DateStart(DateTime value, EventHandler<DateTime> eventHandler) : base(value, eventHandler) { }
      public DateStart(DateTime value) : base(value) { }
    }
    static Dictionary<string, object> GetDict(IList<object> list) {
      return list.OfType<Dictionary<string, object>>().SingleOrDefault();
    }
    static Dictionary<string, object> AddDict(IList<object> list) {
      var d = new Dictionary<string, object>(); list.Add(d); return d;
    }
    static Dictionary<string, object> GetAddDict(IList<object> list) {
      return GetDict(list) ?? AddDict(list);
    }
    public static Func<IList<object>, Func<T>, T> DictValue<T>(string key) {
      return (l, f) => DictValue(l, key, f);
    }
    static T DictValue<T>(IList<object> list, string key, Func<T> factory = null) {
      if (factory != null)// Set
        return DictValue(list, key, factory());
      T value = default(T);
      DictValue(list, key, v => value = v, () => value);
      return value;
    }
    /// <summary>
    /// Set Dict Value
    /// </summary>
    static T DictValue<T>(IList<object> list, string key, T value) {
      var d = GetAddDict(list);
      if (d.ContainsKey(key)) d[key] = value;
      else d.Add(key, value);
      return value;
    }
    /// <summary>
    /// Get Dict Value
    /// </summary>
    static void DictValue<T>(IList<object> list, string key, Action<T> exists, Func<T> doesNotExist = null) {
      var d = GetAddDict(list);
      if (d.ContainsKey(key)) exists((T)d[key]);
      else if (doesNotExist != null) doesNotExist();
    }
  }
  delegate Func<A, R> RecursiveFunc<A, R>(RecursiveFunc<A, R> r);
  delegate Action<A> RecursiveAction<A>(RecursiveAction<A> r);
  public static class WorkflowMixin {
    public static Func<A,R> Y<A,R>(Func<Func<A,R>, Func<A,R>> f) {
      RecursiveFunc<A,R> rec = r => a => f(r(r))(a);
      return rec(rec);
    }
    public static Action<A> YAction<A>(Func<Action<A>, Action<A>> f) {
      RecursiveAction<A> rec = r => a => f(r(r))(a);
      return rec(rec);
    }
    public static void UnSubscribe<T>(this Action<T> delegatus, Action<T> handler, Action<Action<T>> unSubcriber) {
      delegatus.GetInvocationList().Where(d => d.Method == handler.Method).ToList().ForEach(d => unSubcriber(d as Action<T>));
    }
  }
}
