using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Reactive;
using System.Threading.Tasks.Dataflow;
using System.Runtime.CompilerServices;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Collections.Concurrent;

namespace HedgeHog {
  public static class ObservableExtensions {
    public static IObservable<T> ToObservable<T>(this Action<T> action) => Observable.FromEvent<Action<T>, T>(h => action += h, h => action -= h);
    public static IObservable<U> ToObservable<T, U>(this Action<T> action, Func<T, U> map)
      => Observable.FromEvent<Action<T>, U>(next => t => map(t), h => action += h, h => action -= h);

    public static IObservable<T> OrderBy<T, TKey>(this IObservable<T> source, Func<T, TKey> sort) =>
      source.ToArray().SelectMany(a => a.OrderBy(sort));
    public static IObservable<T> OrderByDescending<T, TKey>(this IObservable<T> source, Func<T, TKey> sort) =>
      source.ToArray().SelectMany(a => a.OrderByDescending(sort));

    public static IObservable<T> OnEmpty<T>(this IObservable<T> source, Action onEmpty) {
      var isEmpty = true;
      return source.Do(_ => isEmpty = false).Finally(() => { if(isEmpty) onEmpty(); });
    }

    public static IObservable<TSource> RateLimit<TSource>(
            this IObservable<TSource> source,
            int itemsPerSecond,
            IScheduler scheduler = null) {
      scheduler = scheduler ?? Scheduler.Default;
      var timeSpan = TimeSpan.FromSeconds(1);
      var itemsEmitted = 0L;
      return Observable.Create<TSource>(
          observer => {
            var buffer = new ConcurrentQueue<TSource>();
            Action emit = delegate () {
              while(Interlocked.Read(ref itemsEmitted) < itemsPerSecond) {
                TSource item;
                if(!buffer.TryDequeue(out item))
                  break;
                observer.OnNext(item);
                Interlocked.Increment(ref itemsEmitted);

              }
            };

            var sourceSub = source
                      .Subscribe(x => {
                        buffer.Enqueue(x);
                        emit();

                      });
            var timer = Observable.Interval(timeSpan, scheduler)
                      .Subscribe(x => {
                        Interlocked.Exchange(ref itemsEmitted, 0);
                        emit();
                      }, observer.OnError, observer.OnCompleted);
            return new CompositeDisposable(sourceSub, timer);
          });
    }
    public static IObservable<TSource> CatchAndStop<TSource>(
      this IObservable<TSource> source) => source.CatchAndStop(() => new Exception());

    public static IObservable<TSource> CatchAndStop<TSource, TException>(
      this IObservable<TSource> source, Func<TException> witness
      ) where TException : Exception
      => source.Catch((TException exc) => Observable.Empty<TSource>());
    public static IObservable<T> Spy<T>(this IObservable<T> source, string opName = null) {
      opName = opName ?? typeof(T) + "";
      Console.WriteLine("{0}: Observable obtained on Thread: {1}",
                        opName,
                        ThreadInfo());

      return Observable.Create<T>(obs => {
        Console.WriteLine("{0}: Subscribed to on Thread: {1}",
                          opName,
                          ThreadInfo());

        try {
          var subscription = source
              .Do(x => Console.WriteLine("{0}: OnNext({1}) on Thread: {2}",
                                          opName,
                                          x,
                                          ThreadInfo()),
                  ex => Console.WriteLine("{0}: OnError({1}) on Thread: {2}",
                                           opName,
                                           ex,
                                           ThreadInfo()),
                  () => Console.WriteLine("{0}: OnCompleted() on Thread: {1}",
                                           opName,
                                           ThreadInfo())
              )
              .Subscribe(obs);
          return new CompositeDisposable(
              subscription,
              Disposable.Create(() => Console.WriteLine(
                    "{0}: Cleaned up on Thread: {1}",
                    opName,
                    ThreadInfo())));
        } finally {
          Console.WriteLine("{0}: Subscription completed.", opName);
        }
      });
      string ThreadInfo() => $"{Thread.CurrentThread.ManagedThreadId}:{Thread.CurrentThread.Name}";
    }
    public static IDisposable SubscribeWithoutOverlap<T>(this IObservable<T> source, Action<T> action, IScheduler scheduler = null) {
      var sampler = new Subject<Unit>();
      scheduler = scheduler ?? Scheduler.Default;
      var p = source.Publish();
      var connection = p.Connect();

      var subscription = sampler.Select(x => p.Take(1))
          .Switch()
          .ObserveOn(scheduler)
          .Subscribe(l => {
            action(l);
            sampler.OnNext(Unit.Default);
          });

      sampler.OnNext(Unit.Default);

      return new CompositeDisposable(connection, subscription);
    }
    public static ISubject<T> SubjectFuctory<T>(this T v) {
      return new Subject<T>();
    }
    public static IObservable<IList<T>> SlidingWindow<T>(
           this IObservable<T> src,
           int windowSize) {
      var feed = src.Publish().RefCount();
      // (skip 0) + (skip 1) + (skip 2) + ... + (skip nth) => return as list  
      return Observable.Zip(
         Enumerable.Range(0, windowSize)
             .Select(skip => feed.Skip(skip))
             .ToArray());
    }
    static IObservable<List<T>> SlidingWindow_<T>(this IObservable<T> seq, int length) {
      var seed = new List<T>();

      Func<List<T>, T, List<T>> accumulator = (list, arg2) => {
        list.Add(arg2);

        if(list.Count > length)
          list.RemoveRange(0, (list.Count - length));

        return list;
      };

      return seq.Scan(seed, accumulator)
                  .Where(list => list.Count == length);
    }
    public static IDisposable SubscribeToLatestOnBGThread<TSource>(this IObservable<TSource> subject
      , Action<TSource> onNext, Action<Exception> onError, ThreadPriority priority = ThreadPriority.Normal) {
      return subject.SubscribeToLatestOnBGThread(onNext, onError, () => { }, priority);
    }

    public static EventLoopScheduler BGTreadSchedulerFactory(ThreadPriority priority = ThreadPriority.Normal, [CallerMemberName] string Caller = null) {
      return new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true, Priority = priority, Name = Caller }; });
    }
    public static IDisposable SubscribeToLatestOnBGThread<TSource>(this IObservable<TSource> subject
      , Action<TSource> onNext, EventLoopScheduler scheduler = null, Action<Exception> onError = null, Action onCompleted = null, [CallerMemberName] string Caller = null) {
      return subject.Latest()
        .ToObservable(scheduler ?? BGTreadSchedulerFactory(Caller: Caller))
        .Subscribe(onNext, onError ?? (exc => { }), onCompleted ?? (() => { }));
    }

    public static IDisposable SubscribeToLatestOnBGThread<TSource>(this IObservable<TSource> subject
      , Action<TSource> onNext, Action<Exception> onError, Action onCompleted, ThreadPriority priority = ThreadPriority.Normal, [CallerMemberName] string Caller = null) {
      return subject.Latest()
        .ToObservable(BGTreadSchedulerFactory(priority, Caller))
        .Subscribe(onNext, onError, onCompleted);
    }

    public static IDisposable SubscribeToLatestOnBGThread(this IObservable<Action> subject
      , Action<Exception> onError, ThreadPriority priority = ThreadPriority.Normal) {
      return subject.SubscribeToLatestOnBGThread(a => a(), onError, priority);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static ISubject<T> InitBufferedObservable<T>(this ISubject<T> subject, ref IObservable<T> observable, Action<Exception> onError) {
      if(subject != null) {
        if(observable == null) throw new Exception("observable is null");
        return subject;
      }
      if(observable != null) throw new Exception("observable is not null");
      subject = new Subject<T>();
      observable = subject.InitBufferedObservable(onError);
      return subject;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static IObservable<T> InitBufferedObservable<T>(this IObservable<T> observable, ref ISubject<T> subject, Action<Exception> onError) {
      if(observable != null) {
        if(subject == null) throw new Exception("subject is null");
        return observable;
      }
      if(subject == null)
        subject = new Subject<T>();
      return observable = subject.InitBufferedObservable(onError);
    }
    public static IObservable<T> InitBufferedObservable<T>(this ISubject<T> subject, Action<Exception> onError) {
      BroadcastBlock<T> buffer = new BroadcastBlock<T>(n => n);
      subject.ObserveOn(TaskPoolScheduler.Default)
        .Subscribe(a => buffer.SendAsync(a), onError);
      return buffer.AsObservable();
    }
    public static IObservable<T> ObserveLatestOn<T>(
    this IObservable<T> source, IScheduler scheduler) {
      return Observable.Create<T>(observer => {
        Notification<T> outsideNotification = null;
        var gate = new object();
        bool active = false;
        var cancelable = new MultipleAssignmentDisposable();
        var disposable = source.Materialize().Subscribe(thisNotification => {
          bool wasNotAlreadyActive;
          lock(gate) {
            wasNotAlreadyActive = !active;
            active = true;
            outsideNotification = thisNotification;
          }

          if(wasNotAlreadyActive) {
            cancelable.Disposable = scheduler.Schedule(self => {
              Notification<T> localNotification = null;
              lock(gate) {
                localNotification = outsideNotification;
                outsideNotification = null;
              }
              localNotification.Accept(observer);
              bool hasPendingNotification = false;
              lock(gate) {
                hasPendingNotification = active = (outsideNotification != null);
              }
              if(hasPendingNotification) {
                self();
              }
            });
          }
        });
        return new CompositeDisposable(disposable, cancelable);
      });
    }
  }
}
