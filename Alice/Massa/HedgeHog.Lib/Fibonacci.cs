using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class Fibonacci {
    static double[] InsideLevels = new[] { 1.618, 1.382, 1.272, .764, .618, .382, .236, -.272, -.382, -.618 };

    public static double[] Levels(double up, double down) {
      var spread = up - down;
      var levels = new List<double>();
      InsideLevels.ToList().ForEach(l => levels.Add((down + spread * l).Round(5)));
      return levels.ToArray();
    }

    public static double FibRatioSign(double d1, double d2) { return d1 / d2 - d2 / d1; }
    public static double FibRatio(double d1, double d2) { return Math.Abs(d1 / d2 - d2 / d1); }

    /// <summary>
    /// Returns X/Y ratio from fib = X/Y - Y/X equation
    /// </summary>
    /// <param name="fib"> Fibobacci between X and Y</param>
    /// <returns></returns>
    public static double FibReverse(this double fib) {
      return (fib + Math.Sqrt(fib * fib + 4)) / 2;
    }
    /// <summary>
    /// Returns X from S = X/Y
    /// </summary>
    /// <param name="ratio">X/Y ratio</param>
    /// <param name="sum"></param>
    /// <returns></returns>
    public static double XofS(this double ratio, double sum) {
      return sum / (1 + 1 / ratio);
    }
    /// <summary>
    /// Returns Y from S = X/Y
    /// </summary>
    /// <param name="ratio">X/Y ratio</param>
    /// <param name="sum"></param>
    /// <returns></returns>
    public static double YofS(this double ratio, double sum) {
      return sum / (ratio + 1);
    }
  }
}
