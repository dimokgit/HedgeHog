using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class Fibonacci {
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
