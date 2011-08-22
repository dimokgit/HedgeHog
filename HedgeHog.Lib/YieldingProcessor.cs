﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace HedgeHog {
  public static class DataFlowProcessors {
    public static ITargetBlock<T> CreateYieldingTargetBlock<T>(this Action<T> action, bool failOnError = false) {
      return CreateYieldingTargetBlock(action, null, failOnError);
    }
    public static ITargetBlock<T> CreateYieldingTargetBlock<T>(this Action<T> action, Action<Exception> onError,bool failOnError = false) {
      var _source = new BroadcastBlock<T>(t => t);
      var options = new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 };
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

    public static IDisposable SubscribeToYieldingObservable<T>(this BroadcastBlock<T> bb, Action<T> action) {
      return bb.AsObservable().Subscribe(t => action(t));
    }
  }
  public class YieldingProcessor<T> {

    ActionBlock<T> _target;
    internal BroadcastBlock<T> _source = new BroadcastBlock<T>(t => t);

    public YieldingProcessor(Action<T> action) {
      _target = new ActionBlock<T>(action, new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });
      _source.LinkTo(_target);
    }
  }
}