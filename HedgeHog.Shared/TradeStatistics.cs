using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class TradeStatistics {
    public Guid SessionId { get; set; }
    double _PLMaximum;
    public double PLMaximum {
      get { return _PLMaximum; }
      set { _PLMaximum = Math.Max(_PLMaximum, value); }
    }
    public double PowerAverage { get; set; }
    public double PowerVolatility { get; set; }
    public double CorridorHeight { get; set; }
    public double Resistanse { get; set; }
    public double Support { get; set; }
  }
}
