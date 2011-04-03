using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.ComponentModel;

namespace HedgeHog {
  #region Schedulers
  public class Scheduler {
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
        if (!_time.IsEnabled)
          _time.Start();
      }
    }
    public Scheduler(Dispatcher dispatcher, EventHandler<TimerErrorException> error)
      : this(dispatcher) {
      Error += error;
    }
    public Scheduler() : this(Application.Current.Dispatcher) { }
    public Scheduler(Dispatcher dispatcher)
      : this(dispatcher, new TimeSpan(0, 0, 0, 0, 100)) {
      if (dispatcher != null)
        _time = new DispatcherTimer(new TimeSpan(0, 0, 0, 0, 100), DispatcherPriority.Background, _time_Tick, dispatcher);
    }
    public Scheduler(Dispatcher dispatcher, TimeSpan delay) {
      if (dispatcher != null)
        _time = new DispatcherTimer(delay, DispatcherPriority.Background, _time_Tick, dispatcher);
    }

    public void TryRun(CommandDelegate command) {
      if (IsRunning) return;
      this.Command = command;
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
        t.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
          t.Stop();
        }));
        IsRunning = true;
        if (Command != null) Command();
      } catch (Exception exc) {
        if (Error != null) Error(this, tex = new TimerErrorException(exc));
      } finally {
        IsRunning = false;
        if (tex == null || !tex.CancelFinishedEvent) RaiseFinished();
      }
    }
  }

  public class SchedulersDispenser<T> : Dictionary<T, Scheduler> {
    object _locker = new object();
    public Scheduler Get(T key) {
      lock (_locker) {
        if (!this.ContainsKey(key)) this.Add(key, new Scheduler());
        return this[key];
      }
    }
    public void Run(T key, Action runner, bool runSync = false) {
      if (runSync) runner();
      else {
        lock (_locker) {
          var ts = Get(key);
          if (ts.IsRunning) return;// ts.SetFinished((s, ea) => runner());
          else ts.Command = () => runner();
        }
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
    EventHandler<EventArgs> _finishedHandler;
    public event EventHandler<EventArgs> Finished;
    void RaiseFinished() {
      if (Finished != null) Finished(this, new EventArgs());
      if (_finishedHandler != null) {
        Finished -= _finishedHandler;
        _finishedHandler = null;
      }
    }
    public void SetFinished(EventHandler<EventArgs> handler) {
      if (_finishedHandler != null) Finished -= _finishedHandler;
      _finishedHandler = handler;
      Finished += handler;
    }

    public EventWaitHandle WaitHandler { get; private set; }
    public readonly static TimeSpan infinity = TimeSpan.FromMilliseconds(-1);
    public readonly static TimeSpan zero = TimeSpan.Zero;
    TimeSpan _period = infinity;
    TimeSpan _delay = zero;

    public TimeSpan Period {
      get { return _period; }
      set {
        _period = value;
        if (_timer != null)
          _timer.Change(_delay, value);
      }
    }
    System.Threading.Timer _timer;
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
    public void TryRun(CommandDelegate command) {
      if (IsRunning) return;
      Init(_command);
    }
    public void Run() { Init(_command); }
    public void Init(CommandDelegate command) { Init(command, TimeSpan.MinValue); }
    public void Init(CommandDelegate command, TimeSpan delay) {
      Cancel();
      if (delay != TimeSpan.MinValue) Delay = delay;
      _timer = new Timer(o => {
        if (IsRunning) return;
        TimerErrorException tex = null;
        try {
          WaitHandler.Reset();
          IsRunning = true;
          command();
          if (_period == infinity) Cancel();
        } catch (Exception exc) {
          if (Error != null) Error(this, tex = new TimerErrorException(exc));
        } finally {
          IsRunning = false;
          try {
            WaitHandler.Set();
          } catch { }
          if (tex == null || !tex.CancelFinishedEvent) RaiseFinished();
        }
      },
      null, Delay, _period);
    }
    public ThreadScheduler(EventHandler<TimerErrorException> error)
      : this(TimeSpan.FromMilliseconds(0)) {
      Error += error;
    }
    public ThreadScheduler(TimeSpan delay, EventHandler<TimerErrorException> error)
      : this(delay) {
      Error += error;
    }
    public ThreadScheduler() : this(zero, infinity) { }
    public ThreadScheduler(TimeSpan delay) : this(delay, infinity) { }
    public ThreadScheduler(TimeSpan delay, TimeSpan period)
      : this(delay, period, null, null) {
    }
    public ThreadScheduler(TimeSpan delay, TimeSpan period, CommandDelegate command)
      : this(delay, period, command, null) {
    }
    public ThreadScheduler(CommandDelegate command, EventHandler<TimerErrorException> error) : this(zero, infinity, command, error) { }
    public ThreadScheduler(TimeSpan delay, TimeSpan period, CommandDelegate command, EventHandler<TimerErrorException> error) {
      this.WaitHandler = new EventWaitHandle(true, EventResetMode.AutoReset);
      this._period = period;
      this.Delay = delay;
      if (command != null) this.Command = command;
      if (error != null) this.Error = error;
    }

    object timerLocker = new object();
    public void Cancel() {
      if (_timer != null) {
        lock (timerLocker) {
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
  public class ThreadSchedulersDispenser : Dictionary<string, ThreadScheduler> {
    public ThreadScheduler Get(string key) {
      if (!this.ContainsKey(key)) this.Add(key, new ThreadScheduler());
      return this[key];
    }
    public void Run(string key, Action runner) {
      var ts = Get(key);
      if (ts.IsRunning) return;// ts.SetFinished((s, ea) => runner());
      else ts.Command = () => runner();
    }
  }

  public class BackgroundWorkerDispenser<T> : Dictionary<T, BackgroundWorker> {
    TaskStatus[] done = new[] { TaskStatus.RanToCompletion, TaskStatus.Created, TaskStatus.Faulted };
    bool IsDone(BackgroundWorker task) { return !task.IsBusy; }
    object locker = new object();
    BackgroundWorker Get(T key) {
      lock (locker) {
        if (!this.ContainsKey(key)) {
          var bw = new BackgroundWorker();
          bw.DoWork += new DoWorkEventHandler(DoWork);
          this.Add(key, bw);
        }
      }
      return this[key];
    }
    delegate void Command();
    void DoWork(object sender, DoWorkEventArgs e) {
      var task = e.Argument as Action;
      task();
    }
    public void Run(T key, Action runner) {
      Run(key, runner, e => { });
    }
    public void Run(T key, Action runner, ThreadPriority threadPriority) {
      Run(key, runner, e => { }, threadPriority);
    }
    public void Run(T key, Action runner, Action<Exception> log) {
      Run(key, false, runner, log, false, ThreadPriority.Lowest);
    }
    public void Run(T key, Action runner, Action<Exception> log, ThreadPriority threadPriority) {
      Run(key, false, runner, log, threadPriority);
    }
    public void Run(T key, bool runSync, Action runner, Action<Exception> log) {
      Run(key, runSync, runner, log, false, ThreadPriority.Lowest);
    }
    public void Run(T key, bool runSync, Action runner, Action<Exception> log, ThreadPriority threadPriority) {
      Run(key, runSync, runner, log, true, threadPriority);
    }
    private void Run(T key, bool runSync, Action runner, Action<Exception> log, bool useThreadPriority, ThreadPriority threadPriority) {
      if (runSync) runner();
      else {
        var ts = Get(key);
        if (!IsDone(ts)) return;// ts.SetFinished((s, ea) => runner());
        else ts.RunWorkerAsync(runner);
      }
    }
  }

  public class TasksDispenser<T> : Dictionary<T, Task> {
    object locker = new object();
    public void Run(T key, Action runner) {
      Run(key, runner, e => { });
    }
    public void Run(T key, Action runner, Action<Exception> log) {
      lock (locker) {
        if (this.ContainsKey(key)) {
          var done = this[key].Wait(0);
          if (!done) return;
        }
        this[key] = Task.Factory.StartNew(() => {
          try {
            runner();
          } catch (Exception exc) { log(exc); }
        });
      }
    }
  }
}
