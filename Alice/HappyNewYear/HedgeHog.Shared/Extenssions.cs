using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public static class Extenssions {
    public static double GrossInPips(this IEnumerable<Trade> trades) {
      return trades == null || trades.Count() == 0 ? 0 : trades.Sum(t => t.PL * t.Lots) / trades.Sum(t => t.Lots);
    }
  }
}
