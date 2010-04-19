using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SQLCLR {
  static class Helpers {
    public static DateTime Round(this DateTime dt) { return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0); }
    public static DateTime Round_(this DateTime dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static DateTime Round(this DateTime dt, byte period) {
      dt = dt.Round();
      if (period == 1) return dt;
      if (period > 60) throw new ArgumentException("Period must be less then or equial to 60 minutes.");
      var evenMinutes = dt.Minute / period * period;
      return dt.AddMinutes(-dt.Minute).AddMinutes(evenMinutes);
    }

    static void LinearRegression(double[] values, out double a, out double b) {
      double xAvg = 0;
      double yAvg = 0;
      for (int x = 0; x < values.Length; x++) {
        xAvg += x;
        yAvg += values[x];
      }
      xAvg = xAvg / values.Length;
      yAvg = yAvg / values.Length;
      double v1 = 0;
      double v2 = 0;
      for (int x = 0; x < values.Length; x++) {
        v1 += (x - xAvg) * (values[x] - yAvg);
        v2 += Math.Pow(x - xAvg, 2);
      }
      a = v1 / v2;
      b = yAvg - a * xAvg;
      //Console.WriteLine("y = ax + b");
      //Console.WriteLine("a = {0}, the slope of the trend line.", Math.Round(a, 2));
      //Console.WriteLine("b = {0}, the intercept of the trend line.", Math.Round(b, 2));

    }

  }
}
