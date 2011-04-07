using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace HedgeHog {
  public class BlockingConsumerBase<T> {
    BlockingCollection<T> _loadRatesQueue = new BlockingCollection<T>();
    Action<T> Action { get; set; }
    protected void Init(Action<T> action) {
      Action = action;
      Task.Factory.StartNew(new Action(() => {
        foreach (var tm in _loadRatesQueue.GetConsumingEnumerable()) {
          if (Action != null)
            Action(tm);
        }
        return;
      }));
    }
    public void Add(T tm) {
      if (!_loadRatesQueue.Contains(tm))
        _loadRatesQueue.TryAdd(tm);
    }
  }
}
