using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;

namespace HedgeHog {
  public class BlockingConsumerBase<T> {
    protected BlockingCollection<T> LoadRatesQueue = new BlockingCollection<T>();
    protected Action<T> Action { get; set; }
    Task _task;
    T _activeItem;
    public virtual bool IsCompleted { get { return _task == null || _task.IsCompleted; } }
    public BlockingConsumerBase() {}
    public BlockingConsumerBase(Action<T> action) {
      this.Action = action;
    }
    protected void Init(Action<T> action) {
      if (action == null) throw new ArgumentNullException("Action");
      if (Action != null) throw new InvalidOperationException("Action property can only be assigned once.");
      Action = action;
    }
    public void Add(T tm,Func<T,T,bool> compare) {
      var isActiveItemOk = (typeof(T).IsValueType ? default(T).Equals(_activeItem) : _activeItem == null) || !compare(tm, _activeItem);
      if (!LoadRatesQueue.Any(t=>compare(t,tm)) && isActiveItemOk)
        AddCore(tm);
    }
    public void Add(T t) {
      if (LoadRatesQueue.Contains(t) || t.Equals(_activeItem)) return;
      AddCore(t);
    }
    void AddCore(T t) {
      LoadRatesQueue.TryAdd(t);
      lock (LoadRatesQueue) {
        if (IsCompleted)
          RunQueue();
      }
    }

    protected virtual void RunQueue() {
      _task = Task.Factory.StartNew(RunAction);
    }

    protected void RunAction() {
      foreach (var tm in LoadRatesQueue.GetConsumingEnumerable()) {
        _activeItem = tm;
        try {
          Action(tm);
        } catch (Exception exc) {
          Debug.WriteLine(exc + "");
        } finally {
          _activeItem = default(T);
        }
        lock (LoadRatesQueue) {
          if (LoadRatesQueue.Count == 0) {
            return;
          }
        }
      }
    }
  }
  public class DispatcherConsumer<T> : BlockingConsumerBase<T> {
    private DispatcherPriority _priority = DispatcherPriority.Normal;

    public DispatcherPriority Priority {
      get { return _priority; }
      set { _priority = value; }
    }

    Dispatcher _dispatcher = Application.Current.Dispatcher;
    DispatcherOperation _dispatcherOperation;
    public override bool IsCompleted {
      get {
        return _dispatcherOperation == null || _dispatcherOperation.Status == DispatcherOperationStatus.Completed;
      }
    }

    public DispatcherConsumer(Action<T> action,DispatcherPriority priority) : base(action) {
      this.Priority = priority;
    }
    public DispatcherConsumer(Action<T> action) : base(action) { }

    protected override void RunQueue() {
      _dispatcherOperation = _dispatcher.BeginInvoke(new Action(RunAction),Priority);
    }
  }
}
