using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace HedgeHog {
  public class ActionAsyncBuffer :AsyncBuffer<ActionAsyncBuffer, Action> {
    public ActionAsyncBuffer([CallerMemberName] string Caller = null) : base(Caller) { }
    public ActionAsyncBuffer(int boundCapacity,bool userEventLoop, ThreadPriority priority, [CallerMemberName] string Caller = null) 
      : base(boundCapacity, TimeSpan.Zero, userEventLoop, priority, null, Caller) { }
    public ActionAsyncBuffer(int boudCapacity, [CallerMemberName] string Caller = null) : base(boudCapacity, Caller) { }
    public ActionAsyncBuffer(TimeSpan sample, [CallerMemberName] string Caller = null) : base(sample, Caller) { }
    public ActionAsyncBuffer(Func<string> distinctUntilChanged, [CallerMemberName] string Caller = null) : base(1, TimeSpan.Zero, false, distinctUntilChanged, Caller) { }
    protected override Action PushImpl(Action a) => a;
  }

  public abstract class AsyncBuffer<TDerived, TContext> :IDisposable
    where TDerived : AsyncBuffer<TDerived, TContext> {

    //public static TDerived Create() { return new TDerived(); }

    #region Pipe
    TContext _lastContext;
    IDisposable _bufferDisposable;
    ISubject<Action> _StartProcessSubject = new Subject<Action>();
    IDisposable _StartProcessDisposable;
    static int _threadId = 1;
    int GetThreadIdNext() { Interlocked.Increment(ref _threadId); return _threadId; }
    public AsyncBuffer([CallerMemberName] string Caller = null) : this(1, Caller) { }
    public AsyncBuffer(bool useEventLoop, [CallerMemberName] string Caller = null) : this(1, TimeSpan.Zero, useEventLoop, Caller) { }
    public AsyncBuffer(int boundedCapacity, [CallerMemberName] string Caller = null) : this(boundedCapacity, TimeSpan.Zero, false, Caller) { }
    public AsyncBuffer(TimeSpan sample, [CallerMemberName] string Caller = null) : this(1, sample, false, Caller) { }
    public AsyncBuffer(int boundedCapacity, TimeSpan sample, bool useEventLoop, [CallerMemberName] string Caller = null)
      : this(boundedCapacity, sample, useEventLoop, null, Caller) { }
    public AsyncBuffer(int boundedCapacity, TimeSpan sample, bool useEventLoop, Func<string> distinctUntilChanged = null, [CallerMemberName] string Caller = null)
      : this(boundedCapacity, sample, useEventLoop, ThreadPriority.BelowNormal, distinctUntilChanged, Caller) { }
    public AsyncBuffer(int boundedCapacity, TimeSpan sample, bool useEventLoop, ThreadPriority priority, Func<string> distinctUntilChanged = null, [CallerMemberName] string Caller = null) {
      var buffer = new BroadcastBlock<Action>(n => n, new DataflowBlockOptions() { BoundedCapacity = boundedCapacity });

      _StartProcessDisposable = _StartProcessSubject
        .Subscribe(a => buffer.SendAsync(a)
        , exc => Error.OnNext(exc));
      var b = buffer
        .AsObservable();
      if(sample != TimeSpan.Zero)
        b = b.Sample(sample);
      if(distinctUntilChanged != null)
        b = b.DistinctUntilChanged(_ => distinctUntilChanged());
      _bufferDisposable = b
        .SubscribeOn(useEventLoop
          ? ObservableExtensions.BGTreadSchedulerFactory(priority, Caller ?? GetType().Name + GetThreadIdNext())
          : (IScheduler)TaskPoolScheduler.Default)
        .Subscribe(a => {
          try {
            a();
            Pushed.OnNext(_lastContext);
          } catch(Exception exc) {
            Error.OnNext(exc);
          }
        }, exc => Error.OnNext(exc));

    }
    #endregion

    #region Push
    protected abstract Action PushImpl(TContext context);
    public void Push(TContext context) {
      if(_StartProcessDisposable == null)
        throw new InvalidOperationException(new { Pipe = typeof(TDerived).FullName, Error = $"Calling Push({context}) after pipe was disposed." } + "");
      _StartProcessSubject.OnNext(PushImpl(context));
      _lastContext = context;
    }
    #endregion

    #region Push Events
    public Subject<TContext> Pushed = new Subject<TContext>();
    public Subject<TContext> Completed = new Subject<TContext>();
    public Subject<Exception> Error = new Subject<Exception>();
    #endregion

    #region Dispose
    // Flag: Has Dispose already been called? 
    bool _disposed = false;

    public bool Disposed { get => _disposed; set => _disposed = value; }

    // Public implementation of Dispose pattern callable by consumers. 
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    // Protected implementation of Dispose pattern. 
    protected virtual void Dispose(bool disposing) {
      if(_disposed)
        return;

      if(disposing) {
        // Free any other managed objects here. 
        if(_StartProcessDisposable != null) {
          _StartProcessSubject.OnCompleted();
          _StartProcessDisposable.Dispose();
          _StartProcessDisposable = null;
        }
        if(_bufferDisposable != null) {
          _bufferDisposable.Dispose();
          _bufferDisposable = null;
        }
        Completed.OnNext(_lastContext);
      }

      // Free any unmanaged objects here. 
      //
      _disposed = true;
    }
    #endregion
  }
}
