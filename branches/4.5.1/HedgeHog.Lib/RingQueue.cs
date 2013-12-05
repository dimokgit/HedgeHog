using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public class RingQueue<T> : System.Collections.Concurrent.ConcurrentQueue<T> {
    public int MaxCount { get; set; }
    public RingQueue(int maxCount) {
      this.MaxCount = maxCount;
    }
    public new void Enqueue(T item) {
      if (this.Count == MaxCount) {
        T itemOut;
        this.TryDequeue(out itemOut);
      }
      base.Enqueue(item);
    }
  }
  public class RingActionQueue : RingQueue<Action> {
    public RingActionQueue(int maxCount):base(maxCount) {
    }
  }
}
