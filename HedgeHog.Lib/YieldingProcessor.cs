using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using HedgeHog.Shared.Messages;
using HedgeHog.Shared;
using System.Reactive;
using System.Windows.Threading;
using System.Reactive.Concurrency;

namespace HedgeHog {
  public static class DataFlowProcessors {
    #region DispatcherScheduler
    private static DispatcherScheduler _UIDispatcherScheduler;
    public static DispatcherScheduler UIDispatcherScheduler {
      get { return _UIDispatcherScheduler ?? DispatcherScheduler.Current; }
    }
    public static void InvoceOnUI(this Action action,DispatcherPriority dispatcherPriority = DispatcherPriority.Background) { GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher.Invoke(action,DispatcherPriority.Background); }
    public static IDisposable ScheduleOnUI(this Action action) { return UIDispatcherScheduler.Schedule(action); }
    public static IDisposable ScheduleOnUI(this Action action, TimeSpan delay) { return UIDispatcherScheduler.Schedule(delay, action); }
    #endregion

    static TaskScheduler _DispatcherTaskScheduler;
    static public TaskScheduler DispatcherTaskScheduler {
      get {
        if (_DispatcherTaskScheduler == null)
          throw new NullReferenceException("DispatcherScheduler is null.\nCall DataFlowProcessors.Initialize() method as early as possible");//, but right after GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize().");
        return _DispatcherTaskScheduler;
      }
    }
    static DataFlowProcessors() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize();
      ReactiveUI.RxApp.MainThreadScheduler.Schedule(() => {
        _DispatcherTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        _UIDispatcherScheduler = DispatcherScheduler.Current;
      });
    }
    public static void Initialize() {
    }
    public static BroadcastBlock<Action<Unit>> SubscribeToBroadcastBlock() { return SubscribeToBroadcastBlock<Unit>(() => Unit.Default); }
    public static BroadcastBlock<Action<Unit>> SubscribeToBroadcastBlockOnDispatcher() { return SubscribeToBroadcastBlockOnDispatcher<Unit>(() => Unit.Default); }
    public static BroadcastBlock<Action<T>> SubscribeToBroadcastBlockOnDispatcher<T>(Func<T> getT) { return SubscribeToBroadcastBlock(getT, DispatcherTaskScheduler); }
    public static BroadcastBlock<Action<T>> SubscribeToBroadcastBlock<T>(Func<T> getT,TaskScheduler taskSCheduler = null) {
      var bb = new BroadcastBlock<Action<T>>(u => u, new DataflowBlockOptions() { TaskScheduler = taskSCheduler ?? TaskScheduler.Default });
      bb.SubscribeToYieldingObservable(u => u(getT()));
      return bb;
    }

    public static ITargetBlock<Action> CreateYieldingActionOnDispatcher() {
      return DispatcherTaskScheduler.CreateYieldingAction();
    }
    public static ITargetBlock<Action> CreateYieldingAction(this TaskScheduler taskScheduler) {
      return new Action<Action>(a => a()).CreateYieldingTargetBlock(false, taskScheduler);
    }
    public static ITargetBlock<T> CreateYieldingTargetBlock<T>(this Action<T> action, bool failOnError = false,TaskScheduler taskScheduler = null) {
      return CreateYieldingTargetBlock(action, null, failOnError, taskScheduler);
    }
    public static ITargetBlock<T> CreateYieldingTargetBlock<T>(this Action<T> action, Action<Exception> onError,bool failOnError = false,TaskScheduler taskScheduler = null) {
      var _source = new BroadcastBlock<T>(t => t);
      var options = new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 };
      if (taskScheduler != null)
        options.TaskScheduler = taskScheduler;
      var _target = new ActionBlock<T>(t => {
        try {
          action(t);
        } catch (Exception exc) {
          if (onError != null) onError(exc);
          if (failOnError) throw;
        }
      }, options);
      _source.LinkTo(_target);
      return _source;
    }
    public static bool Post<T>(this YieldingProcessor<T> yp, T t) {
      return yp._source.Post(t);
    }

    public static IDisposable SubscribeToYieldingObservable<T>(this BroadcastBlock<T> bb, Action<T> action,Action<T,Exception> error = null) {
      return bb.AsObservable().Subscribe(t => {
        try {
          action(t);
        } catch (Exception exc) {
          if (error != null)
            error(t, exc);
          else
            GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(new LogMessage(exc));
        }
      });
    }
  }
  public class YieldingProcessor<T> {

    ActionBlock<T> _target;
    internal BroadcastBlock<T> _source = new BroadcastBlock<T>(t => t);

    public YieldingProcessor(Action<T> action,TaskScheduler taskScheduler = null) {
      var options = new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 };
      if (taskScheduler != null)
        options.TaskScheduler = taskScheduler;
      _target = new ActionBlock<T>(action, options);
      _source.LinkTo(_target);
    }
  }
}
