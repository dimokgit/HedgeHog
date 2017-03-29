﻿using System;
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

namespace HedgeHog {
  public static class ObservableExtensions {
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

        if (list.Count > length)
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

    static EventLoopScheduler BGTreadSchedulerFactory(ThreadPriority priority = ThreadPriority.Normal) {
      return new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true, Priority = priority }; });
    }
    public static IDisposable SubscribeToLatestOnBGThread<TSource>(this IObservable<TSource> subject
      , Action<TSource> onNext, EventLoopScheduler scheduler = null, Action<Exception> onError = null, Action onCompleted = null) {
      return subject.Latest()
        .ToObservable(scheduler ?? BGTreadSchedulerFactory())
        .Subscribe(onNext, onError ?? (exc => { }), onCompleted ?? (() => { }));
    }

    public static IDisposable SubscribeToLatestOnBGThread<TSource>(this IObservable<TSource> subject
      , Action<TSource> onNext, Action<Exception> onError, Action onCompleted, ThreadPriority priority = ThreadPriority.Normal) {
      return subject.Latest()
        .ToObservable(BGTreadSchedulerFactory(priority))
        .Subscribe(onNext, onError, onCompleted);
    }

    public static IDisposable SubscribeToLatestOnBGThread(this IObservable<Action> subject
      , Action<Exception> onError, ThreadPriority priority = ThreadPriority.Normal) {
      return subject.SubscribeToLatestOnBGThread(a => a(), onError, priority);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static ISubject<T> InitBufferedObservable<T>(this ISubject<T> subject, ref IObservable<T> observable, Action<Exception> onError) {
      if (subject != null) {
        if (observable == null) throw new Exception("observable is null");
        return subject;
      }
      if (observable != null) throw new Exception("observable is not null");
      subject = new Subject<T>();
      observable = subject.InitBufferedObservable(onError);
      return subject;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static IObservable<T> InitBufferedObservable<T>(this IObservable<T> observable,ref ISubject<T> subject , Action<Exception> onError) {
      if (observable != null) {
        if (subject == null) throw new Exception("subject is null");
        return observable;
      }
      if (subject == null) 
        subject = new Subject<T>();
      return observable = subject.InitBufferedObservable(onError);
    }
    public static IObservable<T> InitBufferedObservable<T>(this ISubject<T> subject, Action<Exception> onError) {
      BroadcastBlock<T> buffer = new BroadcastBlock<T>(n => n);
      subject.ObserveOn(TaskPoolScheduler.Default)
        .Subscribe(a => buffer.SendAsync(a), onError);
      return buffer.AsObservable();
    }
  }
}