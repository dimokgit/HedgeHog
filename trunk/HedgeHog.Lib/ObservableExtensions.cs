using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Threading;
using System.Reactive;

namespace HedgeHog {
  public static class ObservableExtensions {
    public static IObservable<TSource> ObserveLatestOn<TSource>(this IObservable<TSource> source, IScheduler scheduler) {
      return Observable.Create<TSource>(observer => {
        Notification<TSource> pendingNotification = null;
        var cancelable = new MultipleAssignmentDisposable();
        var sourceSubscription = source.Materialize().Subscribe(notification => {
          var previousNotification = Interlocked.Exchange(ref pendingNotification, notification);
          if (previousNotification == null) {
            cancelable.Disposable = scheduler.Schedule(() => {
              var notificationToSend = Interlocked.Exchange(ref pendingNotification, null);
              notificationToSend.Accept(observer);
            });
          }
        });
        return new CompositeDisposable(sourceSubscription, cancelable);
      });
    }
    public static IObservable<T> DistinctUntilChanged<T>(this IObservable<T> o, Func<T, T, bool> comparer) {
      return o.DistinctUntilChanged(new LambdaComparer<T>(comparer));
    }
    public static IObservable<T> IntervalThrottle<T>(this IObservable<T> source, TimeSpan interval) {
      return Observable.Create<T>(o => {
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
