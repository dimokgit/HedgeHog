using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class IEnumerableCore {
    #region IfEmpty
    public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> enumerable,
          Action emptyAction) {

      var isEmpty = true;
      foreach (var e in enumerable) {
        isEmpty = false;
        yield return e;
      }
      if (isEmpty)
        emptyAction();
    }
    public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> enumerable,
          Func<IEnumerable<T>> emptySelector) {

      var isEmpty = true;
      foreach (var e in enumerable) {
        isEmpty = false;
        yield return e;
      }
      if (isEmpty)
        foreach (var e in emptySelector())
          yield return e;
    }
    public static T IfEmpty<T>(this T enumerable,
          Action<T> thenSelector,
          Action<T> elseSelector) where T : IEnumerable {

      if (!enumerable.GetEnumerator().MoveNext()) thenSelector(enumerable); else elseSelector(enumerable);
      return enumerable;
    }
    public static TResult IfEmpty<T, TResult>(this IEnumerable<T> enumerable,
          Func<TResult> thenSelector,
          Func<TResult> elseSelector) {

      return (!enumerable.Any()) ? thenSelector() : elseSelector();
    }
    #endregion

    public static IEnumerable<U> Yield<T, U>(this T v, Func<T, U> m) { yield return m(v); }
    public static IEnumerable<object> YieldObject(this object v) { yield return v; }
    public static IEnumerable<T> Yield<T>(this T v) { yield return v; }
    public static IEnumerable<T> YieldNotNull<T>(this T v, bool? condition) {
      if (v == null) yield break;
      if (!condition.HasValue || condition.Value) yield return v;
      yield break;
    }
    public static IEnumerable<T> YieldIf<T>(this T v, bool condition) {
      if (condition)
        yield return v;
      else yield break;
    }
    public static IEnumerable<T> YieldIf<T>(this T v, Func<T, bool> predicate) {
      if (predicate(v))
        yield return v;
      else yield break;
    }
    public static IEnumerable<T> YieldIf<T>(this bool v, Func<T> yielder) {
      if (v)
        yield return yielder();
      else yield break;
    }
    public static IEnumerable<TSource> IfEmpty<TSource>(this IEnumerable<TSource> source, Func<TSource> getDefaultValue) {
      using (var enumerator = source.GetEnumerator()) {
        if (enumerator.MoveNext()) {
          do {
            yield return enumerator.Current;
          }
          while (enumerator.MoveNext());
        } else
          yield return getDefaultValue();
      }
    }
    public static IEnumerable<T> YieldBreak<T>(this T v) { yield break; }
    public static Queue<T> ToQueue<T>(this IEnumerable<T> t) {
      return new Queue<T>(t);
    }
    public static List<T> YieldBreakAsList<T>(this T v) {
      return v.YieldBreak().ToList();
    }
    public static T[] AsArray<T>(this T v, int size) {
      return new T[size];
    }

  }
}
