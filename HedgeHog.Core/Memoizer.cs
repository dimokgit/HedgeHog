using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  //http://stackoverflow.com/questions/2852161/c-sharp-memoization-of-functions-with-arbitrary-number-of-arguments
  public static  class Memoizer {
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
    static Func<Tuple<A, B>, R> Tuplify<A, B, R>(this Func<A, B, R> f) {
      return t => f(t.Item1, t.Item2);
    }
    static Func<A, B, R> Detuplify<A, B, R>(this Func<Tuple<A, B>, R> f) {
      return (a, b) => f(Tuple.Create(a, b));
    }
    static Func<A, B, R> Memoize<A, B, R>(this Func<A, B, R> f) {
      return f.Tuplify().Memoize().Detuplify();
    }
    static Func<A, B, R> Memoize2<A, B, R>(this Func<A, B, R> f) {
      Func<Tuple<A, B>, R> tuplified = t => f(t.Item1, t.Item2);
      Func<Tuple<A, B>, R> memoized = tuplified.Memoize();
      return (a, b) => memoized(Tuple.Create(a, b));
    }
  }
}
