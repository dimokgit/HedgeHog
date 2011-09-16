using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;
using System.Windows.Threading;
using System.Windows;

namespace HedgeHog.Schedulers {
  public class TaskTimerDispenser<T> : ConcurrentDictionary<T, TaskTimer> {
    private bool _runInUIThread;
    private EventHandler<ExceptionEventArgs> _errorHandler;
    private int _delay { get; set; }
    public void TryAction(T key, Action action) {
      var tt = this.GetOrAdd(key, new Func<T,TaskTimer>( k => new TaskTimer(_delay,_errorHandler,_runInUIThread)));
      tt.Action = action;
    }
    public TaskTimerDispenser(int delay,EventHandler<ExceptionEventArgs> errorHandler,bool runInUIThread = false) {
      this._delay = delay;
      this._runInUIThread = runInUIThread;
      this._errorHandler = errorHandler;
    }
  }
  public class TaskTimer {
    #region Properties
    int _delay;
    bool _runInUIThread;
    System.Timers.Timer _timer;
    public bool IsBusy { get; set; }
    #region Action
    Action _action;
    public Action Action {
      get { return _action; }
      set { 
        _action = value;
        if (!IsBusy && !_timer.Enabled)
          this._timer.Enabled = true;
      }
    }
    #endregion
    #endregion

    #region Exception Event
    event EventHandler<ExceptionEventArgs> ExceptionEvent;
    public event EventHandler<ExceptionEventArgs> Exception {
      add {
        if (ExceptionEvent == null || !ExceptionEvent.GetInvocationList().Contains(value))
          ExceptionEvent += value;
      }
      remove {
        ExceptionEvent -= value;
      }
    }
    protected void OnException(Exception exception) {
      if (ExceptionEvent != null) ExceptionEvent(this,new ExceptionEventArgs(exception));
    }
    #endregion

    #region Ctor
    public TaskTimer(int delay, EventHandler<ExceptionEventArgs> exceptionHandler, bool runInUIThhread = false)
      : this(delay, runInUIThhread) {
      Exception += exceptionHandler;
    }
    public TaskTimer(int delay, bool runInUIThhread = false) {
      this._delay = delay;
      this._timer = new System.Timers.Timer(delay) { Enabled = false, AutoReset = false };
      this._runInUIThread = runInUIThhread;
      this._timer.Elapsed += new System.Timers.ElapsedEventHandler(timer_Elapsed);
    }
    #endregion

    void timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e) {
      Action action = Interlocked.Exchange<Action>(ref this._action, null);
      try {
        if (action != null) {
          IsBusy = true;
          if (_runInUIThread && !Application.Current.Dispatcher.CheckAccess()) {
            var t = new Thread(new ThreadStart(() => action()));
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(1000);
          } else
            action();
        }
      } catch (Exception exc) {
        OnException(exc);
      } finally {
        IsBusy = false;
        if (this.Action != null) 
          this._timer.Enabled = true;
      }
    }
  }
}
