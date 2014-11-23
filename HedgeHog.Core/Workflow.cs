using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class WFD {
    public delegate T GetSetter<T>(ExpandoObject expando,params Func<T>[] setter);
    #region Extensions
    public static dynamic D(this ExpandoObject e) { var d = e; return d; }
    static T Get2<T>(this ExpandoObject e, string key, T defaultValue) {
      dynamic d = e;
      if (((IDictionary<string, object>)d).ContainsKey(key)) return (T)d[key];
      else ((IDictionary<string, object>)d).Add(key, defaultValue);
      return defaultValue;
    }
    static T Get3<T>(this ExpandoObject e, string key, params Func<T>[] defaultLazy) {
      dynamic d = e;
      if (!((IDictionary<string, object>)d).ContainsKey(key)) 
        ((IDictionary<string, object>)d).Add(key, defaultLazy.Select(f => f()).DefaultIfEmpty().Single());
      return (T)((IDictionary<string, object>)d)[key];
    }
    static T GetSet<T>(this ExpandoObject e, string key, params Func<T>[] setter) {
      var d = (IDictionary<string, object>)e;
      setter.Take(1).Where(f => f != null).ForEach(f => d[key] = f());
      if (!d.ContainsKey(key))
        setter.Skip(1).Take(1).ForEach(f => d[key] = f());
      return (T)d[key];
    }
    public static GetSetter<T> Make<T>(string key,params T[] def) {
      GetSetter<T> ret = (e, f) => e.GetSet(key, f);
      //def.ForEach(d=>ret())
      return ret;
    }
    #endregion
    #region Delegates
    public delegate void OnLoop(ExpandoObject list);
    public delegate void OnExit();
    #endregion
    #region Context Helpers
    public static Func<ExpandoObject> emptyWFContext = () => new ExpandoObject();
    public static Func<ExpandoObject, Tuple<int, ExpandoObject>> tupleNext = e => Tuple.Create(1, e ?? new ExpandoObject());
    public static Func<Tuple<int, ExpandoObject>> tupleNextEmpty = () => tupleNext(emptyWFContext());
    public static Func<ExpandoObject, Tuple<int, ExpandoObject>> tupleStay = e => Tuple.Create(0, e ?? new ExpandoObject());
    public static Func<Tuple<int, ExpandoObject>> tupleStayEmpty = () => tupleStay(emptyWFContext());
    public static Func<ExpandoObject, Tuple<int, ExpandoObject>> tuplePrev = e => Tuple.Create(-1, e ?? new ExpandoObject());
    public static Func<ExpandoObject, Tuple<int, ExpandoObject>> tupleBreak = e => Tuple.Create(int.MaxValue / 2, e ?? new ExpandoObject());
    public static Func<Tuple<int, ExpandoObject>> tupleBreakEmpty = () => tupleBreak(emptyWFContext());
    #endregion
    #region Factory
    public struct scan {
      public int i;
      public ExpandoObject o;
      public Func<bool> c;
      public scan(int i, ExpandoObject o, Func<bool> c) {
        this.i = i;
        this.o = o;
        this.c = c;
      }
    }
    public static IObservable<scan> WFFactory(this Subject<IList<Func<ExpandoObject, Tuple<int, ExpandoObject>>>> workflowSubject, Func<bool> cancelWorkflow) {
      #region Workflow tuple factories
      #endregion
      return workflowSubject
        .Scan(new scan( 0, emptyWFContext(), cancelWorkflow ), (i, wf) => {
          if (i.i >= wf.Count || i.c() || i.o.OfType<WF.MustExit>().Any(me => me())) {
            i.o.Select(kv => kv.Value).OfType<WFD.OnExit>().ForEach(a => a());
            dynamic d = i.o;
            ((IDictionary<string, object>)d).Clear();
            i = new scan(0, i.o, i.c);
          }
          var o = wf[i.i](i.o);// Side effect
          o.Item2.OfType<WFD.OnLoop>().ToList().ForEach(ol => ol(o.Item2));
          try {
            var d = new scan((i.i + o.Item1).Max(0), o.Item2, i.c);
            return d;
          } finally {
            if (o.Item1 != 0) workflowSubject.Repeat(1);
          }
        });
    }
    #endregion
  }
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
      new[] { delegatus }
        .Where(dlg => dlg != null)
        .ForEach(dlg => dlg.GetInvocationList().Where(d => d.Method == handler.Method).ToList().ForEach(d => unSubcriber(d as Action<T>)));
    }
  }
}
