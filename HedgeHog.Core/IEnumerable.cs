using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static partial class IEnumerableCore {
    public class Singleable<T> :IEnumerable<T> {
      private readonly IEnumerable<T> source;
      public Singleable(IEnumerable<T> sourse) {
        source = sourse;
      }
      IEnumerator IEnumerable.GetEnumerator() {
        return MyEnumerator();
      }
      IEnumerator<T> IEnumerable<T>.GetEnumerator() {
        return MyEnumerator();
      }

      IEnumerator<T> MyEnumerator() {
        if(source == null)
          throw new ArgumentNullException(nameof(source));
        var e = source.GetEnumerator();
        if(e.MoveNext())
          yield return e.Current;
        if(e.MoveNext())
          throw new InvalidOperationException("Singleable has more then 1 element.");
      }
    }

    #region Counter
    public static IEnumerable<T> Count<T, C>(this IEnumerable<T> source, int expectedCount, C context) =>
      source.Count(expectedCount, (Action<int>)null, (Action<int>)null, context);
    public static IEnumerable<T> Count<T, E>(this IEnumerable<T> source, int expectedCount, E onLess, E onMore) where E : Exception {
      return source.Count(expectedCount,
        onLess == null ? (Action<int>)null : _ => { throw onLess; },
        onMore == null ? (Action<int>)null : _ => { throw onMore; });
    }
    public static IEnumerable<T> Count<T>(this IEnumerable<T> source, int expectedCount, Action<int> onLess, Action<int> onMore) =>
    source.Count(expectedCount, onLess, onMore, (string)null);
    public static IEnumerable<T> Count<T, C>(this IEnumerable<T> source, int expectedCount, Action<int> onLess, Action<int> onMore, C context) {
      if(source == null) throw new ArgumentNullException("source");
      var counter = 0;
      var handled = false;
      string Context() => context.IsDefault() ? "" : $" Context: {context}";
      foreach(var v in source) {
        if(++counter > expectedCount) {
          (onMore != null
            ? onMore
            : c => { throw new Exception($"Sequence has more items[{c}] then expected[{expectedCount}].{Context()}"); }
            )(counter);
          handled = true;
          break;
        }
        yield return v;
      }
      if(!handled && counter != expectedCount)
        (onLess != null ? onLess : c => { throw new Exception($"Sequence has less items[{c}] then expected[{expectedCount}].{Context()}"); })(counter);
    }
    #endregion

    public static string[] Splitter(this string s, params char[] split) { return s.Split(split, StringSplitOptions.RemoveEmptyEntries); }
    public static string Flatter<T>(this IEnumerable<T> s, string split) { return string.Join(split, s); }
    public static IList<double> MaxByOrEmpty(this IEnumerable<double> source) {
      return source.MaxByOrEmpty(d => d);
    }
    public static IList<TSource> MaxByOrEmpty<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) {
      if(source == null) {
        throw new ArgumentNullException("source");
      }
      if(keySelector == null) {
        throw new ArgumentNullException("keySelector");
      }
      return source.MaxByOrEmpty(keySelector, Comparer<TKey>.Default);
    }
    public static IList<TSource> MaxByOrEmpty<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) {
      if(source == null) {
        throw new ArgumentNullException("source");
      }
      if(keySelector == null) {
        throw new ArgumentNullException("keySelector");
      }
      if(comparer == null) {
        throw new ArgumentNullException("comparer");
      }
      return ExtremaBy<TSource, TKey>(source, keySelector, (TKey key, TKey minValue) => comparer.Compare(key, minValue));
    }

    public static IList<double> MinByOrEmpty(this IEnumerable<double> source) {
      return source.MinByOrEmpty(d => d);
    }
    public static IList<TSource> MinByOrEmpty<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) {
      if(source == null) {
        throw new ArgumentNullException("source");
      }
      if(keySelector == null) {
        throw new ArgumentNullException("keySelector");
      }
      return source.MinByOrEmpty(keySelector, Comparer<TKey>.Default);
    }
    public static IList<TSource> MinByOrEmpty<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) {
      if(source == null) {
        throw new ArgumentNullException("source");
      }
      if(keySelector == null) {
        throw new ArgumentNullException("keySelector");
      }
      if(comparer == null) {
        throw new ArgumentNullException("comparer");
      }
      return ExtremaBy<TSource, TKey>(source, keySelector, (TKey key, TKey minValue) => -comparer.Compare(key, minValue));
    }


    private static IList<TSource> ExtremaBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TKey, TKey, int> compare) {
      List<TSource> list = new List<TSource>();
      using(IEnumerator<TSource> enumerator = source.GetEnumerator()) {
        if(enumerator.MoveNext()) {
          TSource current = enumerator.Current;
          TKey arg = keySelector(current);
          list.Add(current);
          while(enumerator.MoveNext()) {
            TSource current2 = enumerator.Current;
            TKey tKey = keySelector(current2);
            int num = compare(tKey, arg);
            if(num == 0) {
              list.Add(current2);
            } else if(num > 0) {
              list = new List<TSource>
              {
          current2
        };
              arg = tKey;
            }
          }
        }
      }
      return list;
    }
    public static T[] MinMaxBy<T>(this IEnumerable<T> source, Func<T, double> getter) {
      return source.MinMaxBy(getter, getter);
    }
    public static double[] MinMax<T>(this IEnumerable<T> source, Func<T, double> getter) {
      return source.MinMax(getter, getter);
    }
    public static double[] MinMax<T>(this IEnumerable<T> source, Func<T, double> miner, Func<T, double> maxer) {
      var minMax = source.MinMaxBy(miner, maxer);
      return minMax.Any()
        ? new[] { miner(minMax[0]), maxer(minMax.Last()) }
        : new[] { double.NaN };
    }
    public static T[] MinMaxBy<T>(this IEnumerable<T> source, Func<T, double> miner, Func<T, double> maxer) {
      var def = default(T);
      var min = def;
      var max = def;
      var b = false;
      var p = def;
      foreach(var t in source) {
        if(!b) {
          min = max = t;
          b = true;
        } else {
          if(miner(min) > miner(t))
            min = t;
          if(maxer(max) < maxer(t))
            max = t;
        }
      }
      return b ? new[] { min, max } : new T[0];
    }
    public static double[] MinMax(this IEnumerable<double> source) {
      var min = 0.0;
      var max = 0.0;
      var b = false;
      foreach(var t in source) {
        if(!b) {
          min = max = t;
          b = true;
        } else {
          if(min > t)
            min = t;
          if(max < t)
            max = t;
        }
      }
      return b ? new[] { min, max } : new double[0];
    }


    /// <summary>
    /// Get last <paramref name="count" />  elements
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public static T[] GetRange<T>(this IList<T> source, int count) {
      var a = new T[count];
      var start = source.Count - count;
      if(start < 0) return new T[0];
      Array.Copy(source.ToArray(), start, a, 0, count);
      return a;
    }
    public static T[] GetRange<T>(this IList<T> source, double size) {
      var count = (source.Count * size).ToInt();
      if(count < 0)
        return source.GetRange(-count);
      var a = new T[count];
      Array.Copy(source.ToArray(), a, count);
      return a;
    }
    public static IList<T> GetRange<T>(this List<T> source, DateTime start, DateTime end, Func<T, DateTime> toDate) {
      Func<DateTime, int[]> index = date => source.FuzzyIndex(date, (d, r1, r2) => d.Between(toDate(r1), toDate(r2)));
      var i1 = index(start.Max(toDate(source[0])));
      var i2 = index(end.Min(toDate(source.Last())));
      return (from s in i1
              from e in i2
              select source.GetRange(s, e - s + 1))
              .DefaultIfEmpty(new List<T>())
              .First();
    }
    public static Singleable<T> AsSingleable<T>(this IEnumerable<T> source) {
      return new Singleable<T>(source);
    }
    public static IEnumerable<T> BackwardsIterator<T>(this IList<T> lst) {
      for(int i = lst.Count - 1; i >= 0; i--) {
        yield return lst[i];
      }
    }
    public static IEnumerable<U> BackwardsIterator<T, U>(this IList<T> lst, Func<T, U> map) {
      for(int i = lst.Count - 1; i >= 0; i--) {
        yield return map(lst[i]);
      }
    }
    public static IEnumerable<T> TakeFirst<T>(this IList<T> lst, int count) {
      var last = (count >= 0 ? count : (lst.Count + count).Max(0)).Min(lst.Count);
      for(int i = 0; i < last; i++) {
        yield return lst[i];
      }
    }
    public static IEnumerable<TSource> TakeWhile<TSource>(this IEnumerable<TSource> source, Predicate<TSource> predicate, int count) {
      if(count < 0)
        throw new ArgumentException("Must be >= 0", "count");
      using(IEnumerator<TSource> iterator = source.GetEnumerator()) {
        while(iterator.MoveNext()) {
          TSource item = iterator.Current;
          if(predicate(item))
            yield return item;
          else {
            while(count > 0) {
              yield return item;
              count--;
              if(count == 0 || !iterator.MoveNext())
                break;
              item = iterator.Current;
            }
            break;
          }
        }
      }
    }
    #region IfEmpty
    public static T AverageOrNaN<T>(this IEnumerable<double> enumerable, Func<double, T> map) {
      return map(enumerable.AverageOrNaN());
    }
    public static double AverageOrNaN(this IEnumerable<double> enumerable) {
      return enumerable.DefaultIfEmpty(double.NaN).Average();
    }
    public static IEnumerable<double> NaNIfEmpty(this IEnumerable<double> enumerable) {
      return enumerable.DefaultIfEmpty(double.NaN);
    }
    public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> enumerable,
            Action emptyAction) {

      var isEmpty = true;
      foreach(var e in enumerable) {
        isEmpty = false;
        yield return e;
      }
      if(isEmpty)
        emptyAction();
    }
    public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> enumerable, Func<IEnumerable<T>> emptySelector) {
      var isEmpty = true;
      foreach(var e in enumerable) {
        isEmpty = false;
        yield return e;
      }
      if(isEmpty)
        foreach(var e in emptySelector())
          yield return e;
    }
    static T IfEmpty<T>(this T enumerable,
          Action<T> thenSelector,
          Action<T> elseSelector) where T : IEnumerable {

      if(!enumerable.GetEnumerator().MoveNext())
        thenSelector(enumerable);
      else
        elseSelector(enumerable);
      return enumerable;
    }
    static TResult IfEmpty<T, TResult>(this IEnumerable<T> enumerable,
          Func<TResult> thenSelector,
          Func<TResult> elseSelector) {

      return (!enumerable.Any()) ? thenSelector() : elseSelector();
    }
    public static IEnumerable<TSource> RunIfEmpty<TSource>(this IEnumerable<TSource> source, Func<TSource> getDefaultValue) {
      using(var enumerator = source.GetEnumerator()) {
        if(enumerator.MoveNext()) {
          do {
            yield return enumerator.Current;
          }
          while(enumerator.MoveNext());
        } else
          yield return getDefaultValue();
      }
    }
    /// <summary>
    /// If more then one - returns else
    /// </summary>
    /// <typeparam name="TSource"></typeparam>
    /// <param name="source"></param>
    /// <param name="else">returned if there is more then one element</param>
    /// <returns></returns>
    public static TSource SingleOrElse<TSource>(this IEnumerable<TSource> source, TSource @else) {
      var res = default(TSource);
      using(IEnumerator<TSource> enumerator = source.GetEnumerator()) {
        if(!enumerator.MoveNext()) throw new Exception("Sequence contains no elements.");
        res = enumerator.Current;
        if(enumerator.MoveNext()) return @else;
        else return res;
      }
    }
    /// <summary>
    /// If more then one - returns else
    /// </summary>
    /// <typeparam name="TSource"></typeparam>
    /// <param name="source"></param>
    /// <param name="else">returned if there is more then one element</param>
    /// <returns></returns>
    public static TSource SingleOrElse<TSource>(this IEnumerable<TSource> source, Func<TSource> @else) {
      var res = default(TSource);
      using(IEnumerator<TSource> enumerator = source.GetEnumerator()) {
        if(!enumerator.MoveNext()) throw new Exception("Sequence contains no elements.");
        res = enumerator.Current;
        if(enumerator.MoveNext()) return @else();
        else return res;
      }
    }
    #endregion

    #region Yield
    public static void If(this bool v, Action then) {
      if(v) then();
    }
    public static void If(this bool v, Action then, Action @else) {
      if(v)
        then();
      else
        @else();
    }
    public static (U value, IEnumerable<Exception> error, T param) WithError<T, U>(this Func<T, U> func, T value) {
      try {
        return (func(value), new Exception[0], value);
      } catch(Exception exc) {
        return (default, new[] { exc }, value);
      }
    }
    public static (U value, IEnumerable<Exception> error, T param) WithError<T, V, U>(this (T value, IEnumerable<Exception> error, V param) param, Func<(T value, IEnumerable<Exception> error, V param), U> func) {
      try {
        return (func(param), param.error, param.value);
      } catch(Exception exc) {
        return (default, param.error.Concat(new[] { exc }), param.value);
      }
    }

    public static U With<T, U>(this T v, Predicate<T> @if, Func<T, U> then, Func<T, U> @else) {
      return @if(v) ? then(v) : @else(v);
    }
    public static U With<T, U>(this T v, Func<T, U> m) { return m(v); }
    public static V With<T, U, V>(this T v, Func<T, U> m, Func<T, U, V> r) { return r(v, m(v)); }
    public static void With<T>(this T v, Action<T> m) { m(v); }
    public static T SideEffect<T>(this T v, Action<T> io) { io(v); return v; }
    public static IEnumerable<T> Concat<T>(this IEnumerable<T> source, Func<T> value) { return source.Concat(value.Yield()); }
    public static IEnumerable<T> Concat<T>(this IEnumerable<T> source, Func<IEnumerable<T>> value) {
      foreach(var v in source)
        yield return v;
      foreach(var v in value())
        yield return v;
    }
    public static IEnumerable<U> Yield<U>(this Func<U> m) { yield return m(); }
    public static IEnumerable<U> Yield<T, U>(this T v, Func<T, U> m) { yield return m(v); }
    public static IEnumerable<object> YieldObject(this object v) { yield return v; }
    public static IEnumerable<T> Yield<T>(this T v) { yield return v; }
    public static IEnumerable<U> YieldNotNull<T, U>(this T v, Func<T, U> map) {
      if(v == null)
        yield break;
      else
        yield return map(v);
    }
    public static IEnumerable<T> YieldNotNull<T>(this T v) { return v.YieldNotNull(true); }
    public static IEnumerable<T> YieldNotNull<T>(this T v, bool? condition) {
      if(v == null)
        yield break;
      if(!condition.HasValue || condition.Value)
        yield return v;
      yield break;
    }
    public static IEnumerable<bool> YieldTrue(this bool v) {
      if(v)
        yield return v;
      yield break;
    }
    public static IEnumerable<U> YieldIf<T, U>(this T v, Func<T, bool> condition, Func<T, U> map) {
      if(condition(v))
        yield return map(v);
    }
    public static IEnumerable<T> YieldIf<T>(this T v, bool condition) {
      if(condition)
        yield return v;
      else
        yield break;
    }
    public static IEnumerable<T> YieldIf<T>(this T v, Func<T, bool> predicate) {
      if(predicate(v))
        yield return v;
      else
        yield break;
    }
    public static IEnumerable<T> YieldIf<T>(this T v, Func<T, bool> predicate, Func<T> otherwise) {
      if(predicate(v))
        yield return v;
      else
        yield return otherwise();
    }
    public static IEnumerable<T> YieldIf<T>(this bool v, Func<T> yielder) {
      if(v)
        yield return yielder();
      else
        yield break;
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
    public static IList<T> AsIList<T>(this T v, int size) {
      return new T[size];
    }
    public static IList<T> AsIList<T>(this IList<T> source) {
      return source;
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
        .GroupBy(d => d.v)
        .Select(g => new { v = g.Key, c = g.Count() })
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
