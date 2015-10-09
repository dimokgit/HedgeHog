using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class IEnumerableCore {
    public class Singleable<T> : IEnumerable<T> {
      private IEnumerable<T> _source;
      public Singleable(IEnumerable<T> sourse  ) {
        this._source = sourse;
      }
      IEnumerator IEnumerable.GetEnumerator() {
        return _source.GetEnumerator();
      }
      IEnumerator<T> IEnumerable<T>.GetEnumerator() {
        return _source.GetEnumerator();
      }
    }
    public static T[] GetRange<T>(this IList<T> source,int count) {
      var a = new T[count];
      var start = source.Count - count;
      Array.Copy(source.ToArray(), start, a, 0, count);
      return a;
    }
    public static Singleable<T> AsSingleable<T>(this IEnumerable<T> source) {
      return new Singleable<T>(source);
    }
    public static IEnumerable<T> BackwardsIterator<T>(this IList<T> lst) {
      for (int i = lst.Count - 1; i >= 0; i--) {
        yield return lst[i];
      }
    }
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
    #endregion

    #region Yield
    public static IEnumerable<U> Yield<T, U>(this T v, Func<T, U> m) { yield return m(v); }
    public static IEnumerable<object> YieldObject(this object v) { yield return v; }
    public static IEnumerable<T> Yield<T>(this T v) { yield return v; }
    public static IEnumerable<T> YieldNotNull<T>(this T v) { return v.YieldNotNull(true); }
    public static IEnumerable<T> YieldNotNull<T>(this T v, bool? condition) {
      if (v == null) yield break;
      if (!condition.HasValue || condition.Value) yield return v;
      yield break;
    }
    public static IEnumerable<bool> YieldTrue(this bool v) {
      if (v) yield return v;
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
    public static IEnumerable<T> YieldIf<T>(this T v, Func<T, bool> predicate,Func<T> otherwise) {
      if (predicate(v))
        yield return v;
      else yield return otherwise();
    }
    public static IEnumerable<T> YieldIf<T>(this bool v, Func<T> yielder) {
      if (v)
        yield return yielder();
      else yield break;
    }
    public static IEnumerable<T> YieldBreak<T>(this T v) { yield break; }
    public static Queue<T> ToQueue<T>(this IEnumerable<T> t) {
      return new Queue<T>(t);
    }
    public static List<T> YieldBreakAsList<T>(this T v) {
      return v.YieldBreak().ToList();
    }
    #endregion
    public static T[] AsArray<T>(this T v, int size) {
      return new T[size];
    }
    public static IList<V> BufferVertical2<T, V>(
      this IEnumerable<T> input
      , Func<T, double> getValue
      , double height
      , Func<double, double, int, V> mapInputValue
      ) {
      var values = input
        .Select((t, i) => new { v = getValue(t) })
        .OrderBy(d => d.v)
        .GroupBy(d=>d.v)
        .Select(g => new { v=g.Key, c = g.Count()})
        .ToArray();
      var valuesRange = values.Distinct(a => a.v).ToArray();
      var last = valuesRange.LastOrDefault().YieldNotNull().Select(a => a.v).DefaultIfEmpty(double.NaN).Last();
      //var strips = values.BufferVertical(d => d, 0.05, (d => d * .009 * 3), (b, t, rates) => new { b, t, rates }).ToArray();
      var ranges = (
        from value in valuesRange
        let bottom = value.v
        let top = bottom + height
        where top <= last
        select new { value.v, bottom, top }
        )
        .AsParallel()
        .Select(value => {
          var bottom = value.v;
          var top = bottom + height;
          return
            mapInputValue(bottom, top,
            values
            .SkipWhile(d => d.v < bottom)
            .TakeWhile(d => d.v <= top)
            .Sum(d => d.c));
        });
      return ranges.ToArray();
    }
    public delegate U BufferVerticalDelegate<T, U>(double bottom, double top, IEnumerable<T> values);
    public static IEnumerable<U> BufferVertical<T, U>(this IList<T> rates, Func<T, double> _priceAvg, double height, Func<double, double> toStep, BufferVerticalDelegate<T, U> map) {
      var bottom = rates.Min(_priceAvg);
      var top = rates.Max(_priceAvg) - height;
      return Enumerable.Range(0, (top - bottom).Div(toStep(1)).ToInt())
        .Select(i => {
          var b = bottom + toStep(i);
          var t = b + height;
          return map(b, t, rates.Where(r => _priceAvg(r).Between(b, t)));
        });
    }

  }
}
