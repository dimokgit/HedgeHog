using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class GCDExtension {
    public static int GCD(this int[] numbers) => numbers.Aggregate(GCD);

    static int GCD(int a, int b) => 
      b == 0 || a == 0 ? a + b : GCD(b.Abs().Min(a.Abs()), a.Abs().Max(b.Abs()) % b.Abs().Min(a.Abs()));
  }
}
