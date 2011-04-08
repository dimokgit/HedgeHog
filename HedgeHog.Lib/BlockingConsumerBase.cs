using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;

namespace HedgeHog {
  public class BlockingConsumerBase<T> {
    BlockingCollection<T> _loadRatesQueue = new BlockingCollection<T>();
    Action<T> Action { get; set; }
    protected void Init(Action<T> action) {
      Action = action;
      Task.Factory.StartNew(new Action(() => {
        foreach (var tm in _loadRatesQueue.GetConsumingEnumerable()) {
          if (Action != null)
            try {
              Action(tm);
            } catch(Exception exc) {
              Debug.WriteLine(exc + "");
            }
        }
        return;
      }));
    }
    public void Add(T tm,Func<T,T,bool> compare) {
      if (!_loadRatesQueue.Any(t=>compare(t,tm)))
        _loadRatesQueue.TryAdd(tm);
    }
    public void Add(T t) {
      if (!_loadRatesQueue.Contains(t))
        _loadRatesQueue.TryAdd(t);
    }
  }
}
