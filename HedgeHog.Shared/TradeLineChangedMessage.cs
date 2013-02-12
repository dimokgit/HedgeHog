using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class TradeLineChangedMessage {
    public double OldValue { get; set; }
    public double NewValue { get; set; }
    public object Target { get; set; }
    public TradeLineChangedMessage(object target, double newValue,double oldValue) {
      this.Target = target;
      this.OldValue = oldValue;
      this.NewValue = newValue;
    }
  }
}
