using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class TradeStatistics {
    public Guid SessionId { get; set; }
    public double BarHeightHighShort { get; set; }
    public double BarHeightLowShort { get; set; }
    public double BarHeightHighLong { get; set; }
    public double BarHeightLowLong { get; set; }
    public int TakeProfitInPipsMinimum { get; set; }
    public int MinutesBack { get; set; }
  }
}
