using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace HedgeHog {
  public class ActionAsyncBuffer :AsyncBuffer<ActionAsyncBuffer, Action> {
    public ActionAsyncBuffer() : base() { }
    protected override Action PushImpl(Action a) => a;
  }

  public abstract class AsyncBuffer<TDerived, TContext> :IDisposable
    where TDerived : AsyncBuffer<TDerived, TContext>, new() {

    public static TDerived Create() { return new TDerived(); }

    #region Pipe
    TContext _lastContext;
    IDisposable _bufferDisposable;
    ISubject<Action> _StartProcessSubject = new Subject<Action>();
    IDisposable _StartProcessDisposable;
    public AsyncBuffer() : this(1) { }
    public AsyncBuffer(int boundedCapacity) : this(boundedCapacity, TimeSpan.Zero) { }
    public AsyncBuffer(int boundedCapacity, TimeSpan sample) {
      var buffer = new BroadcastBlock<Action>(n => n, new DataflowBlockOptions() { BoundedCapacity = boundedCapacity });

      _StartProcessDisposable = _StartProcessSubject
        .Subscribe(a => buffer.SendAsync(a)
        , exc => Error.OnNext(exc));
      var b = buffer
        .AsObservable();
      if(sample != TimeSpan.Zero)
        b = b.Sample(sample);
      _bufferDisposable = b
        .SubscribeOn(ObservableExtensions.BGTreadSchedulerFactory(System.Threading.ThreadPriority.BelowNormal))
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
        throw new InvalidOperationException(new { Pipe = typeof(TDerived).FullName, Error = "Calling Push after pipe was disposed." } + "");
      _StartProcessSubject.OnNext(PushImpl(context));
      _lastContext = context;
    }
    #endregion

    #region Push Events
    public static Subject<TContext> Pushed = new Subject<TContext>();
    public static Subject<TContext> Completed = new Subject<TContext>();
    public static Subject<Exception> Error = new Subject<Exception>();
    #endregion

    #region Dispose
    // Flag: Has Dispose already been called? 
    bool _disposed = false;

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
