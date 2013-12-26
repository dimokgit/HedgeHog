using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Threading;
using System.Reactive;
using System.Reactive.Subjects;

namespace HedgeHog {
  public static class ObservableExtensions {
    public static IDisposable SubscribeToLatestOnBGThread<TSource>(this IObservable<TSource> subject, Action<TSource> onNext, Action<Exception> onError, ThreadPriority priority = ThreadPriority.Normal) {
      return subject.SubscribeToLatestOnBGThread(onNext, onError, () => { }, priority);
    }

    public static IDisposable SubscribeToLatestOnBGThread<TSource>(this IObservable<TSource> subject, Action<TSource> onNext, Action<Exception> onError, Action onCompleted, ThreadPriority priority = ThreadPriority.Normal) {
      return subject.Latest()
        .ToObservable(new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true, Priority = priority }; }))
        .Subscribe(onNext, onError, onCompleted);
    }

    public static IDisposable SubscribeToLatestOnBGThread(this IObservable<Action> subject, Action<Exception> onError, ThreadPriority priority = ThreadPriority.Normal) {
      return subject.SubscribeToLatestOnBGThread(a => a(), onError, priority);
    }
  }
}
