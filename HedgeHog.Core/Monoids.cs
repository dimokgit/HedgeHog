using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class MonoidsCore {
    public static Lazy<T> Create<T>(Func<T> func) { return new Lazy<T>(func); }
    public static Lazy<T> Create<T>(Func<T> func, T defaultValue, Action<Exception> error) {
      try {
        return new Lazy<T>(func);
      } catch(Exception exc) {
        error(exc);
        return new Lazy<T>(() => defaultValue);
      }
    }
    public static Lazy<T> ToLazy<T>(Func<T> projector) {
      return new Lazy<T>(projector);
    }
    #region ToFunc
    public static Func<T> ToFunc<T>(this T value) { return () => value; }
    public static Func<T> ToFunc<T>(Func<T> anon) {
      return anon;
    }

    public static Func<T1, T> ToFunc<T1, T>(T1 t1, Func<T1, T> anon) { return anon; }
    public static Func<U1, T> ToFunc<U1, T>(Func<U1, T> projector) { return projector; }

    public static Func<T1, T2, T> ToFunc<T1, T2, T>(T1 t1, T2 t2, Func<T1, T2, T> anon) { return anon; }
    public static Func<U1, U2, T> ToFunc<T, U1, U2>(Func<U1, U2, T> projector) { return projector; }

    public static Func<T1, T2, T3, T> ToFunc<T1, T2, T3, T>(T1 t1, T2 t2, T3 t3, Func<T1, T2, T3, T> anon) { return anon; }
    public static Func<U1, U2, U3, T> ToFunc<T, U1, U2, U3>(Func<U1, U2, U3, T> projector) { return projector; }

    public static Func<T1, T2, T3, T4, T> ToFunc<T1, T2, T3, T4, T>(T1 t1, T2 t2, T3 t3, T4 t4, Func<T1, T2, T3, T4, T> anon) { return anon; }
    public static Func<U1, U2, U3, U4, T> ToFunc<T, U1, U2, U3, U4>(Func<U1, U2, U3, U4, T> projector) { return projector; }
    public static Func<T1, T2, T3, T4, T5, T> ToFunc<T1, T2, T3, T4, T5, T>(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, Func<T1, T2, T3, T4, T5, T> anon) { return anon; }
    public static Func<U1, U2, U3, U4, U5, T> ToFunc<T, U1, U2, U3, U4, U5>(Func<U1, U2, U3, U4, U5, T> projector) { return projector; }

    public static Func<T1, T2, T3, T4, T5, T6, T> ToFunc<T1, T2, T3, T4, T5, T6, T>(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, Func<T1, T2, T3, T4, T5, T6, T> anon) { return anon; }
    public static Func<U1, U2, U3, U4, U5, U6, T> ToFunc<T, U1, U2, U3, U4, U5, U6>(Func<U1, U2, U3, U4, U5, U6, T> projector) { return projector; }

    public static Func<T1, T2, T3, T4, T5, T6, T7, T> ToFunc<T1, T2, T3, T4, T5, T6, T7, T>(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, Func<T1, T2, T3, T4, T5, T6, T7, T> anon) { return anon; }
    public static Func<U1, U2, U3, U4, U5, U6, U7, T> ToFunc<T, U1, U2, U3, U4, U5, U6, U7>(Func<U1, U2, U3, U4, U5, U6, U7, T> projector) { return projector; }

    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T> ToFunc<T1, T2, T3, T4, T5, T6, T7, T8, T>(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, Func<T1, T2, T3, T4, T5, T6, T7, T8, T> anon) { return anon; }
    public static Func<U1, U2, U3, U4, U5, U6, U7, U8, T> ToFunc<T, U1, U2, U3, U4, U5, U6, U7, U8>(Func<U1, U2, U3, U4, U5, U6, U7, U8, T> projector) { return projector; }
    #endregion

  }
}
