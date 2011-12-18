using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class TradeStatistics {
    public double CorridorStDev { get; set; }
    public double CorridorStDevCma { get; set; }
    public double Resistanse { get; set; }
    public double Support { get; set; }

    public Guid SessionId { get; set; }
    public string SessionInfo { get; set; }
  }
}
