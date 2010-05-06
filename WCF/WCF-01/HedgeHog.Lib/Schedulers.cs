using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HedgeHog {
  #region Schedulers
  public class Scheduler {
    public class TimerErrorException : EventArgs {
      public bool CancelFinishedEvent;
      public Exception Exception;
      public TimerErrorException(Exception exception,bool cancelFinishedEvent):this(exception) {
        CancelFinishedEvent = cancelFinishedEvent;
      }
      public TimerErrorException(Exception exception) {
        Exception = exception;
      }
    }
    public event EventHandler<TimerErrorException> Error;
    public event EventHandler<EventArgs> Finished;
    void RaiseFinished() {
      if (Finished != null) Finished(this, new EventArgs());
    }
    System.Windows.Threading.DispatcherTimer _time;
    public delegate void CommandDelegate();
    CommandDelegate _command;
    private bool _isRunning;
    public bool IsRunning {
      get { return _isRunning; }
      private set { _isRunning = value; }
    }
    public TimeSpan Delay {
      get { return _time.Interval; }
      set { _time.Interval = value; }
    }

    public CommandDelegate Command {
      get { return _command; }
      set {
        _command = value;
        if( !_time.IsEnabled )
          _time.Start();
      }
    }
    public Scheduler(Dispatcher dispatcher, EventHandler<TimerErrorException> error)
      : this(dispatcher) {
      Error += error;
    }
    public Scheduler(Dispatcher dispatcher)
      : this(dispatcher, new TimeSpan(0, 0, 0, 0, 100)) {
      _time = new DispatcherTimer(new TimeSpan(0, 0, 0, 0, 100), DispatcherPriority.Background, _time_Tick, dispatcher);
    }
    public Scheduler(Dispatcher dispatcher, TimeSpan delay) {
      _time = new DispatcherTimer(delay, DispatcherPriority.Background, _time_Tick, dispatcher);
    }


    public void Cancel() {
      IsRunning = false;
      _time.Stop();
    }

    void _time_Tick(object sender, EventArgs e) {
      TimerErrorException tex = null;
      try {
        var t = (DispatcherTimer)sender;
        if (!t.IsEnabled) return;
        t.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
          t.Stop();
        }));
        IsRunning = true;
        if( Command!=null) Command();
      } catch (Exception exc) {
        if (Error != null) Error(this,tex = new TimerErrorException(exc));
      } finally {
        IsRunning = false;
        if (tex == null || !tex.CancelFinishedEvent) RaiseFinished();
      }
    }
  }

  public class ThreadScheduler {
    public class TimerErrorException : EventArgs {
      public bool CancelFinishedEvent;
      public Exception Exception;
      public TimerErrorException(Exception exception, bool cancelFinishedEvent)
        : this(exception) {
        CancelFinishedEvent = cancelFinishedEvent;
      }
      public TimerErrorException(Exception exception) {
        Exception = exception;
      }
    }
    public event EventHandler<TimerErrorException> Error;
    public event EventHandler<EventArgs> Finished;
    void RaiseFinished() {
      if (Finished != null) Finished(this, new EventArgs());
    }
    public EventWaitHandle WaitHandler { get; private set; }
    public readonly static TimeSpan infinity = new TimeSpan(0, 0, 0, 0, -1);
    TimeSpan _period = infinity;

    public TimeSpan Period {
      get { return _period; }
      set {
        _period = value;
        if (_timer != null)
          _timer.Change(_delay, value);
      }
    }
    System.Threading.Timer _timer;
    TimeSpan _delay = new TimeSpan(0, 0, 0, 0, 500);
    private bool _isRunning;
    public bool IsRunning {
      get { return _isRunning; }
      private set { _isRunning = value; }
    }
    public TimeSpan Delay {
      get { return _delay; }
      set {
        _delay = value;
        if (_timer != null)
          _timer.Change(value, _period);
      }
    }
    public delegate void CommandDelegate();
    private CommandDelegate _command;
    public CommandDelegate Command { set { Init(_command = value); } }
    public void Run() { Init(_command); }
    public void Init(CommandDelegate command) { Init(command, TimeSpan.MinValue); }
    public void Init(CommandDelegate command, TimeSpan delay) {
      Cancel();
      if (delay != TimeSpan.MinValue) Delay = delay;
      _timer = new Timer(o => {
        if (IsRunning) return;
        WaitHandler.Reset();
        IsRunning = true;
        TimerErrorException tex = null;
        try {
          command();
          if (_period == infinity) Cancel();
        } catch (Exception exc) {
          if (Error != null) Error(this,tex = new TimerErrorException(exc));
        } finally {
          IsRunning = false;
          WaitHandler.Set();
          if (tex == null || !tex.CancelFinishedEvent) RaiseFinished();
        }
      },
      null, Delay, _period);
    }
    public ThreadScheduler(EventHandler<TimerErrorException> error) : this(TimeSpan.FromMilliseconds(0)) {
      Error += error;
    }
    public ThreadScheduler(TimeSpan delay, EventHandler<TimerErrorException> error) : this(delay) {
      Error += error;
    }
    public ThreadScheduler(TimeSpan delay) : this(delay, TimeSpan.FromMilliseconds(-1)) { }
    public ThreadScheduler(TimeSpan delay, TimeSpan period)
      : this(delay, period, null,null) {
    }
    public ThreadScheduler(TimeSpan delay, TimeSpan period, CommandDelegate command)      : this(delay, period, command,null) {
    }
    public ThreadScheduler(TimeSpan delay, TimeSpan period, CommandDelegate command, EventHandler<TimerErrorException> error) {
      this.WaitHandler = new EventWaitHandle(true, EventResetMode.AutoReset);
      this._period = period;
      this.Delay = delay;
      if (command != null) this.Command = command;
      if (error != null) this.Error = error;
    }

    public void Cancel() {
      if (_timer != null) {
        lock (_timer) {
          try {
            _timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _timer.Dispose(WaitHandler);
            _timer = null;
          } catch { }
        }
      }
    }

  }
  #endregion
}
