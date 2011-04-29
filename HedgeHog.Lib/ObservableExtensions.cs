using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Concurrency;

namespace HedgeHog {
  public static class ObservableExtensions {
    public static IObservable<T> DistinctUntilChanged<T>(this IObservable<T> o, Func<T, T, bool> comparer) {
      return o.DistinctUntilChanged(new LambdaComparer<T>(comparer));
    }
    public static IObservable<T> IntervalThrottle<T>(this IObservable<T> source, TimeSpan interval) {
      return Observable.CreateWithDisposable<T>(o => {
        DateTimeOffset last = DateTimeOffset.MinValue;
        return source.Subscribe(x => {
          DateTimeOffset now = DateTimeOffset.Now;
          if (now - last >= interval) {
            last = now;
            o.OnNext(x);
          }
        }, o.OnError, o.OnCompleted);
      });
    }
  }
}
