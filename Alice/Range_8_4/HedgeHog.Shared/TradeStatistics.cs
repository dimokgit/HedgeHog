using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class TradeStatistics {
    public Guid SessionId { get; set; }
    public double PowerAverage { get; set; }
    public double PowerVolatility { get; set; }
  }
}
