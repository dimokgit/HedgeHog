using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace HedgeHog {
  public static class MathExtensions {

    public static double StDevP_(this double[] value,  bool fcompensate = true) {
      double[] average = { 0.0, 0.0 };
      double stdev = 0;
      double mean;
      double variance;
      mean = 0; // adjust values

      if ((fcompensate) && (value.Length > 1000)) {
        int n = (int)(value.Length * 0.1);
        for (int i = 0; i <= (n - 1); i++) {
          mean = mean + value[i];
        }
        mean = mean / n;
      }

      for (int i = 1; i <= value.Length; i++) {
        average[i % 2] = average[(i + 1) % 2] + (value[i - 1] - mean - average[(i + 1) % 2]) / i;
        stdev += (value[i - 1] - mean - average[(i + 1) % 2]) * (value[i - 1] - mean - average[i % 2]);
      }

      mean = average[value.Length % 2];
      variance = stdev / (value.Length - 1);

      return Math.Sqrt(variance);
    }

    public static double StDevAL(this double[] x,bool fcompensate = true){//, int n, ref double stddev, ref double mean, bool fcompensate) {
      int i;
      double v1 = 0;
      double v2 = 0;
      double variance;
      double sum = 0;
      double mean = 0;
      int n = x.Length;

      if (fcompensate) {
        for (i = 0; i <= (n - 1); i++) {
          sum = sum + x[i];
        }
        mean = sum / n;
      }

      //
      // Variance (using corrected two-pass algorithm)
      //
      if (n != 1) {
        v1 = 0; v2 = 0;
        for (i = 0; i <= n - 1; i++) {
          v1 = v1 + (x[i] - mean) * (x[i] - mean);
          v2 = v2 + (x[i] - mean);
        }

        v2 = v2 * v2 / n;
        variance = (v1 - v2) / (n - 1);
        if ((double)(variance) < (double)(0)) {
          variance = 0;
        }
        var stddev = Math.Sqrt(variance);
        return stddev;
      }
      return 0;
    }

    public static double[] Trima(this double[] inReal,int period,  out int outBegIdx, out int outNBElement) {
      try {
        if (inReal.Length < period) {
          outBegIdx = 0;
          outNBElement = 0;
          return new double[0];
        }
        double[] outReal = new double[inReal.Count() - period + 1];
        //TicTacTec.TA.Library.Core.Sma(0, d.Count - 1, inReal, period, out outBegIdx, out outNBElement, outReal);
        TicTacTec.TA.Library.Core.Trima(0, inReal.Count() - 1, inReal, period, out outBegIdx, out outNBElement, outReal);
        return outReal;
      } catch (Exception exc) {
        Debug.WriteLine(exc);
        throw;
      }
    }

    public static SortedList<T, double> MovingAverage<T>(this SortedList<T, double> series, int period) {
      var result = new SortedList<T, double>();
      double total = 0;
      for (int i = 0; i < series.Count(); i++) {
        if (i >= period) {
          total -= series.Values[i - period];
        }
        total += series.Values[i];
        if (i >= period - 1) {
          double average = total / period;
          result.Add(series.Keys[i], average);
        }
      } return result;
    }
    public static SortedList<T, double> MovingAverage_<T>(this SortedList<T, double> series, int period) {
      var result = new SortedList<T, double>();
      for (int i = 0; i < series.Count(); i++) {
        if (i >= period - 1) {
          double total = 0;
          for (int x = i; x > (i - period); x--)
            total += series.Values[x];
          double average = total / period;
          result.Add(series.Keys[i], average);
        }
      } 
      return result;
    }
    public static double[] Linear(double[] x, double[] y) {
      double [,] m = new double[x.Length,2];
      for (int i = 0; i < x.Length; i++) {
        m[i, 0] = x[i];
        m[i, 1] = y[i];
      }
      int info,nvars;
      double[] c;
      alglib.linearmodel lm;
      alglib.lrreport lr;
      alglib.lrbuild(m,x.Length,1, out info, out lm,out lr);
      alglib.lrunpack(lm, out c, out nvars);
      return new[] { c[1], c[0] };
    }

    public static void SetProperty<T>(this object o, string p, T v) {
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if (pi != null) pi.SetValue(o, v, new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if (fi == null) throw new NotImplementedException("Property " + p + " is not implemented in " + o.GetType().FullName + ".");
        fi.SetValue(o, v);
      }
    }


    public static void SetProperty(this object o, string p, object v) {
      var convert = new Func<object, Type, object>((valie, type) => {
        if (valie != null) {
          Type tThis = Nullable.GetUnderlyingType(type);
          if (tThis == null) tThis = type;
          valie = Convert.ChangeType(v, tThis, null);
        }
        return valie;
      });
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if (pi != null) pi.SetValue(o, v = convert(v, pi.PropertyType), new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if (fi == null) throw new NotImplementedException("Property " + p + " is not implemented in " + o.GetType().FullName + ".");
        fi.SetValue(o, convert(v, fi.FieldType));
      }
    }

    public static double Angle(this double tangent, double divideBy = 1) { return Math.Atan(tangent / divideBy) * (180 / Math.PI); }
    public static double Radians(this double angleInDegrees) { return angleInDegrees * Math.PI / 180; }

    public static double LineSlope(this double[] coeffs) {
      if (coeffs.Length != 2) throw new IndexOutOfRangeException();
      return coeffs[1];
    }
    public static double LineValue(this double[] coeffs) {
      if (coeffs.Length != 2) throw new IndexOutOfRangeException();
      return coeffs[0];
    }
    public static double RegressionValue(this double[] coeffs, int i) {
      double y = 0; int j = 0;
      for (var ii = 0; ii < coeffs.Length; ii++)
        y += coeffs[ii] * Math.Pow(i, ii);
        //coeffs.ToList().ForEach(c => y += coeffs[j] * Math.Pow(i, j++));
      return y;
    }

    public static double StDevRatio(this ICollection<double> values) {
      var stDev = values.StDev();
      var range = values.Max() - values.Min();
      return stDev / range;
    }
    public static double StDev<T>(this ICollection<T> values, Func<T, int, double> value) {
      return values.Select((v, i) => value(v, i)).ToArray().StDev();
    }
    public static double StDev<T>(this ICollection<T> values, Func<T, double> value) {
      return values.Select(v => value(v)).ToArray().StDev();
    }
    public static double StDev<T>(this ICollection<T> values, Func<T, double?> value) {
      return values.Where(v => value(v).HasValue).Select(v => value(v).Value).ToArray().StDev();
    }
    public static double StDev(this ICollection<double> values) {
      double ret = 0;
      if (values.Count() > 0) {
        double avg = values.Average();
        double sum = values.Sum(d => (d - avg) * (d - avg));
        ret = Math.Sqrt(sum / (values.Count() - 1));
      }
      return ret;
    }


    public static double OpsiteCathetusByFegrees(this double adjacentCathetus, double angleInDegrees) {
      return adjacentCathetus * Math.Tan(angleInDegrees.Radians());
    }

    public static IEnumerable<double> AverageByStDev(this IList<double> values) {
      if (values.Count < 2) return values.DefaultIfEmpty(double.NaN);
      var avg = values.Average();
      var stDev = values.StDev();
      var r1 = avg - stDev;
      var r2 = avg + stDev;
      return values.Where(v => v.Between(r1, r2));
    }

    public static IEnumerable<double> AverageInRange(this IList<double> a, int high) {
      return a.AverageInRange(high, high - 1);
    }
    public static IEnumerable<double> AverageInRange(this IList<double> a, int high, int low) {
      double b = double.NaN, c = double.NaN;
      return a.Where(v => {
        if (double.IsNaN(b)) {
          b = a.AverageByIterations(high, false).Average();
          c = a.AverageByIterations(low).Average();
        }
        return v.Between(c, b); 
      });
    }

    public static IList<double> AverageByIterations(this IList<double> values, double iterations, List<double> averagesOut = null) {
      return values.AverageByIterations(Math.Abs(iterations), iterations < 0, averagesOut);
    }
    public static IList<double> AverageByIterations(this IList<double> values, double iterations, bool low, List<double> averagesOut = null) {
      return values.AverageByIterations(low ? new Func<double, double, bool>((v, a) => v <= a) : new Func<double, double, bool>((v, a) => v >= a), iterations,averagesOut);
    }
    public static IList<double> AverageByIterations(this IList<double> values, Func<double, double, bool> compare, double iterations, List<double> averagesOut = null) {
      return values.AverageByIterations<double>(v => v, compare, iterations, averagesOut);
    }

    public static IList<T> AverageByIterations_<T>(this IList<T> values, Func<T, double> getValue, Func<T, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Add(avg);
      if (values.Count == 0) return values.ToArray();
      for (int i = 0; i < iterations; i++) {
        var vs = values.Where(r => compare(r, avg)).ToArray();
        if (vs.Length == 0) break;
        values = vs;
        avg = values.Average(getValue);
        if (averagesOut != null) averagesOut.Insert(0, avg);
      }
      return values;
    }
    public static IList<T> AverageByIterations<T>(this IList<T> values, Func<T, double> getValue, Func<double, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      return values.Count < 2 || iterations == 0 ? values : values.AsParallel().Where(r => compare(getValue(r), avg)).ToList().AverageByIterations(getValue, compare, iterations - 1, averagesOut);
    }

    public static IList<T> AverageByIterations<T>(this IList<T> values, Func<T, double> getValue, Func<T, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      return values.Count < 2 || iterations == 0 ? values : values.AsParallel().Where(r => compare(r, avg)).ToList().AverageByIterations(getValue, compare, iterations - 1, averagesOut);
    }

    public static IList<T> AverageByPercentage<T>(this IList<T> values, Func<T, double> getValue, Func<double, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      var countMax = values.Count * iterations;
      while( values.Count > countMax ) {
        var vs = values.Where(v => compare(getValue(v), avg)).ToArray();
        if (vs.Count() == 0) break;
        avg = vs.Average(getValue);
        if (averagesOut != null) averagesOut.Insert(0, avg);
        values = vs;
        if (values.Count == 1) break;
      }
      return values;
    }
    public static IList<T> AverageByCount<T>(this IList<T> values, Func<T, double> getValue, Func<double, double, bool> compare, double countMin, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      while (values.Count > countMin) {
        var vs = values.Where(v => compare(getValue(v), avg)).ToArray();
        if (vs.Count() == 0) break;
        avg = vs.Average(getValue);
        if (averagesOut != null) averagesOut.Insert(0, avg);
        values = vs;
        if (values.Count == 1) break;
      }
      return values;
    }

    public static double Average(this IEnumerable<double> values, Func<double> defaultValue) {
      return values.Count() == 0 ? defaultValue() : values.Average();
    }

    public static int Floor(this double d) { return (int)Math.Floor(d); }
    public static int Ceiling(this double d) { return (int)Math.Ceiling(d); }
    public static int ToInt(this double d, bool useCeiling) {
      return (int)(useCeiling ? Math.Ceiling(d) : Math.Floor(d));
    }
    public static int ToInt(this double d) { return (int)Math.Round(d, 0); }
    public static bool IsMax(this DateTime d) { return d == DateTime.MaxValue; }
    public static bool IsMin(this DateTime d) { return d == DateTime.MinValue; }
    public enum RoundTo { Second, Minute, Hour, Day }
    public static DateTime Round(this DateTime d, RoundTo rt) {
      DateTime dtRounded = new DateTime();
      switch (rt) {
        case RoundTo.Second:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second);
          if (d.Millisecond >= 500) dtRounded = dtRounded.AddSeconds(1);
          break;
        case RoundTo.Minute:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
          if (d.Second >= 30) dtRounded = dtRounded.AddMinutes(1);
          break;
        case RoundTo.Hour:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0);
          if (d.Minute >= 30) dtRounded = dtRounded.AddHours(1);
          break;
        case RoundTo.Day:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0);
          if (d.Hour >= 12) dtRounded = dtRounded.AddDays(1);
          break;
      }
      return dtRounded;
    }
    public static DateTime Round(this DateTime dt) { return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0); }
    public static DateTime Round_(this DateTime dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static DateTime Round(this DateTime dt, int period) {
      dt = dt.Round();
      return dt.AddMinutes(dt.Minute / period * period - dt.Minute);
    }

    #region Between
    public static bool Between(this int value, double d1, double d2) {
      return Math.Min(d1, d2) <= value && value <= Math.Max(d1, d2);
    }
    public static bool Between(this double value, double d1, double d2) {
      return Math.Min(d1, d2) <= value && value <= Math.Max(d1, d2);
    }
    public static bool Between(this DateTime value, DateTime d1, DateTime d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this TimeSpan value, TimeSpan d1, TimeSpan d2) {
      return d1 < d2 ? d1 <= value && value <= d2 : d1 <= value || value <= d2;
    }
    #endregion
  }
}
