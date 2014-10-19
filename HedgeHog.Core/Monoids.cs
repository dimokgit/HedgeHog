using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class MonoidsCore {
    public static Lazy<T> ToLazy<T>(Func<T> projector) {
      return new Lazy<T>(projector);
    }
    #region ToFunc
    public static Func<T> ToFunc<T>(this T value, Func<T> action) {
      return action;
    }
    public static Func<T> ToFunc<T>(this T value) {
      return () => value;
    }
    public static Func<U1, U2, T> ToFunc<T, U1, U2>(this T value, U1 input, U2 input2) {
      return (u1, u2) => value;
    }
    public static Func<U1, T> ToFunc<T, U1>(this T value, U1 input, Func<U1, T> projector) {
      return projector;
    }
    public static Func<T> ToFunc<T>(Func<T> projector) {
      return projector;
    }
    public static Func<U1, T> ToFunc<T, U1>(U1 input, Func<U1, T> projector) {
      return projector;
    }
    public static Func<U1, U2, T> ToFunc<T, U1, U2>(U1 input, U2 input2, Func<U1, U2, T> projector) {
      return projector;
    }
    public static Func<U1, U2, U3, T> ToFunc<T, U1, U2, U3>(U1 input, U2 input2, U3 input3, Func<U1, U2, U3, T> projector) {
      return projector;
    }
    public static Func<U1, U2, U3, U4, T> ToFunc<T, U1, U2, U3, U4>(U1 input, U2 input2, U3 input3, U4 input4, Func<U1, U2, U3, U4, T> projector) {
      return projector;
    }
    public static Func<U1, U2, T> ToFunc<T, U1, U2>(this T value, U1 input, U2 input2, Func<U1, U2, T> projector) {
      return projector;
    }
    public static Func<U1, U2, U3, T> ToFunc<T, U1, U2, U3>(this T value, U1 input, U2 input2, U3 input3, Func<U1, U2, U3, T> projector) {
      return projector;
    }
    public static Func<U1, U2, U3, T> ToFunc<T, U1, U2, U3>(this T value, U1 input, U2 input2, U3 input3) {
      return (u1, u2, u3) => value;
    }
    #endregion
  }
}
