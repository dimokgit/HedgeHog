using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class PendingExecution:IEqualityComparer<PendingExecution> {
    public string Pair { get; set; }
    public bool IsBuy { get; set; }

    public PendingExecution(string pair,bool isBuy) {
      this.Pair = pair;
      this.IsBuy = isBuy;
    }

    #region IEqualityComparer<PendingOrder> Members

    public bool Equals(PendingExecution x, PendingExecution y) {
      if (x == null && y == null) return true;
      if (x == null || y == null) return false;
      return x.Pair == y.Pair && x.IsBuy == y.IsBuy;
    }

    public int GetHashCode(PendingExecution obj) {
      return obj.Pair.GetHashCode() ^ obj.IsBuy.GetHashCode();
    }

    #endregion
  }
  public class CreateEntryOrderPendingExecution:PendingExecution {
    public CreateEntryOrderPendingExecution(string pair, bool isBuy) : base(pair, isBuy) { }
    public Action<string> SetOrderIdAction { get; set; }
  }
}
