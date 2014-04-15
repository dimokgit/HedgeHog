using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Linq {
  public static class EnumerableHH {
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
    public static IEnumerable<T> Return<T>(this T v, int count = 1) { return Enumerable.Repeat(v, count); }
    public static Queue<T> ToQueue<T>(this IEnumerable<T> t) {
      return new Queue<T>(t);
    }
    public static List<T> YieldBreakAsList<T>(this T v) {
      return Enumerable.Repeat(v, 0).ToList();
    }
    public static IEnumerable<T> IEnumerable<T>(this T v) {
      return new[] { v };
    }
    public static T[] AsArray<T>(this T v, int size) {
      return new T[size];
    }
  }
}
