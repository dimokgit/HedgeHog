using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  //http://stackoverflow.com/questions/2852161/c-sharp-memoization-of-functions-with-arbitrary-number-of-arguments
  public static class Memoizer {
    public static Func<A, R> Memoize<A, R>(this Func<A, R> f) {
      var cache = new ConcurrentDictionary<A, R>();
      var syncMap = new ConcurrentDictionary<A, object>();
      return a => {
        R r;
        if(!cache.TryGetValue(a, out r)) {
          var sync = syncMap.GetOrAdd(a, new object());
          lock(sync) {
            r = cache.GetOrAdd(a, f);
          }
          syncMap.TryRemove(a, out sync);
        }
        return r;
      };
    }
    public static Func<A, R> Memoize<A, R, K>(this Func<A, R> f, Func<A, K> key) => f.Memoize(key, _ => true);
    public static Func<A, R> Memoize<A, R, K>(this Func<A, R> f, Func<A, K> key, Predicate<R> result) {
      var cache = new ConcurrentDictionary<K, R>();
      var syncMap = new ConcurrentDictionary<K, object>();
      return a => {
        R r;
        K k = key(a);
        if(!cache.TryGetValue(k, out r)) {
          var sync = syncMap.GetOrAdd(k, new object());
          lock(sync) {
            r = f(a);
            if(result(r)) cache.GetOrAdd(k, _ => r);
          }
          syncMap.TryRemove(k, out sync);
        }
        return r;
      };
    }

    public static Func<A, R> Create<A, R, K>(Func<A, R> f, Func<A, K> key) => f.Memoize(key);

    public static Func<A, R> CreateLast<A, R, K>(Func<A, R> f, Func<A, K> key) => f.MemoizeLast(key);
    public static Func<A, B, R> CreateLast<A, B, R, K>(Func<A, B, R> f, Func<(A, B), K> key) => f.MemoizeLast(key);
    public static Func<A, B, C, R> CreateLast<A, B, C, R, K>(Func<A, B, C, R> f, Func<(A, B, C), K> key) => f.MemoizeLast(key);

    public static Func<A, B, C, R> MemoizeLast<A, B, C, R, K>(this Func<A, B, C, R> f, Func<(A, B, C), K> key) => f.Tuplify().MemoizeLast(key).Detuplify();
    public static Func<A, B, R> MemoizeLast<A, B, R, K>(this Func<A, B, R> f, Func<(A, B), K> key) => f.Tuplify().MemoizeLast(key).Detuplify();
    public static Func<A, R> MemoizeLast<A, R, K>(this Func<A, R> f, Func<A, K> key) => f.MemoizeLast(key, r => true);

    public static Func<A, B, R> MemoizeLast<A, B, R, K>(this Func<A, B, R> f, Func<(A, B), K> key, Predicate<R> result) => f.Tuplify().MemoizeLast(key, result).Detuplify();
    public static Func<A, B, C, R> MemoizeLast<A, B, C, R, K>(this Func<A, B, C, R> f, Func<(A a, B b, C c), K> key, Predicate<R> result) => f.Tuplify().MemoizeLast(key, result).Detuplify();

    public static Func<A, R> MemoizeLast<A, R, K>(this Func<A, R> f, Func<A, K> key, Predicate<R> result) {
      var cache = new Tuple<K, R>[0];
      return a => {
        K k = key(a);
        var r = cache.Where(t => result(t.Item2) && EqualityComparer<K>.Default.Equals(t.Item1, k)).ToArray();
        if(r.Length == 0)
          r = cache = new[] { Tuple.Create(k, f(a)) };
        return r[0].Item2;
      };
    }
    public static Action<A> MemoizeLast<A>(this Action<A> f) {
      var cache = default(A);
      return a => {
        if(!EqualityComparer<A>.Default.Equals(cache, a)) {
          cache = a;
          f(a);
        };
      };
    }
    public static Func<A, R> MemoizePrev<A, R>(this Func<A, R> f, Predicate<R> usePrevCondition) {
      var cache = default(R);
      return a => {
        var r = f(a);
        return usePrevCondition(r) ? cache : (cache = r);
      };
    }
    static Func<(A a, B b), R> Tuplify<A, B, R>(this Func<A, B, R> f) => t => f(t.a, t.b);
    static Func<(A a, B b, C c), R> Tuplify<A, B, C, R>(this Func<A, B, C, R> f) => t => f(t.a, t.b, t.c);
    static Func<(A a, B b, C c, D d), R> Tuplify<A, B, C, D, R>(this Func<A, B, C, D, R> f) => t => f(t.a, t.b, t.c, t.d);

    static Func<A, B, R> Detuplify<A, B, R>(this Func<(A, B), R> f) => (a, b) => f((a, b));
    static Func<A, B, C, R> Detuplify<A, B, C, R>(this Func<(A, B, C), R> f) => (a, b, c) => f((a, b, c));
    static Func<A, B, C, D, R> Detuplify<A, B, C, D, R>(this Func<(A, B, C, D), R> f) => (a, b, c, d) => f((a, b, c, d));

    public static Func<A, B, R> Memoize<A, B, R>(this Func<A, B, R> f) => f.Tuplify().Memoize().Detuplify();
    public static Func<A, B, C, R> Memoize<A, B, C, R>(this Func<A, B, C, R> f) => f.Tuplify().Memoize().Detuplify();
    public static Func<A, B, C, D, R> Memoize<A, B, C, D, R>(this Func<A, B, C, D, R> f) => f.Tuplify().Memoize().Detuplify();
    public static Func<A, B, C, D, R> Memoize<A, B, C, D, R, K>(this Func<A, B, C, D, R> f, Func<(A, B, C, D), K> k) => f.Tuplify().Memoize(k).Detuplify();

    public static Func<A, B, R> Memoize2<A, B, R>(this Func<A, B, R> f) {
      Func<Tuple<A, B>, R> tuplified = t => f(t.Item1, t.Item2);
      Func<Tuple<A, B>, R> memoized = tuplified.Memoize();
      return (a, b) => memoized(Tuple.Create(a, b));
    }
  }
}
