using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;

namespace HedgeHog {
  public interface IMaybe<out T> : IEnumerable<T> { }
  public class Maybe2<U, T> : Maybe<T> {
    public U Original;
    public Maybe2(IEnumerable<T> values):base(values) {
    }
  }
  public class Maybe<T> : IMaybe<T> {
    private readonly IEnumerable<T> values;
    public Maybe() {
      this.values = new T[0];
    }
    public Maybe(IEnumerable<T> values) {
      this.values = values;
    }
    public IEnumerator<T> GetEnumerator() {
      return this.values.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() {
      return this.GetEnumerator();
    }
  }
  public static class IMaybe {
    public static IMaybe<T> Do<T>(this IMaybe<T> me, Action action) {
      action();
      return me;
    }
    public static IMaybe<T> Do<T>(this IMaybe<T> me, Action<IMaybe<T>> action) {
      action(me);
      return me;
    }
    public static IMaybe<U> Do<T,U>(this IMaybe<T> me, Func<IMaybe<T>,IMaybe<U>> action) {
      return action(me);
    }
    public static Maybe2<U, T> Do2<U, T>(this Maybe2<U, T> me, Action<Maybe2<U, T>> action) {
      action(me);
      return me;
    }
    public static IMaybe<T> Do<T>(this IMaybe<T> me, Action<IMaybe<T>> action, Action<Exception> exception = null) {
      try {
        action(me);
      } catch (Exception exc) {
        if (exception == null) throw;
        exception(exc);
        return Empty<T>();
      }
      return me;
    }
    public static Maybe<T> AsMaybe<T>(this IEnumerable<T> values) {
      return new Maybe<T>(values);
    }
    public static Maybe<T> ToMaybe<T>(this IEnumerable<T> value){
      return new Maybe<T>(value);
    }
    public static Maybe2< U,T> ToMaybe2< U,T>(this  U value,Func<T> dummy) where U : IEnumerable<T> {
      return new Maybe2<U,T>(value) { Original = value };
    }
    public static Maybe<T> Empty<T>() {
      return new Maybe<T>();
    }
  }
}