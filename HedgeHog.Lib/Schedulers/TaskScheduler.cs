using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Reflection;

namespace HedgeHog.Schedulers {
  public class TaskTimer {
    #region Properties
    int _delay;
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
    public TaskTimer(int delay, EventHandler<ExceptionEventArgs> exceptionHandler):this(delay) {
      Exception += exceptionHandler;
    }
    public TaskTimer(int delay) {
      this._delay = delay;
      this._timer = new System.Timers.Timer(delay) { Enabled = false, AutoReset = false };
      this._timer.Elapsed += new System.Timers.ElapsedEventHandler(timer_Elapsed);
    }
    #endregion

    void timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e) {
      Action action = Interlocked.Exchange<Action>(ref this._action, null);
      try {
        if (action != null) {
          IsBusy = true;
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
