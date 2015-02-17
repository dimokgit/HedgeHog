using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class MathCore {
    public static int Max(this int d1, int d2) {
      return Math.Max(d1, d2);
    }
    public static DateTime Max(this DateTime d1, DateTime d2) {
      return d1 >= d2 ? d1 : d2;
    }
    public static DateTime Min(this DateTime d1, DateTime d2) {
      return d1 <= d2 ? d1 : d2;
    }
    public static bool IsMin(this DateTime d) {
      return d == DateTime.MinValue;
    }
    public static bool IsMax(this DateTime d) {
      return d == DateTime.MaxValue;
    }
    public static DateTimeOffset IfMin(this DateTimeOffset d, DateTimeOffset d1) {
      return d == DateTimeOffset.MinValue ? d1 : d;
    }
    public static DateTime IfMin(this DateTime d, DateTime d1) {
      return d == DateTime.MinValue ? d1 : d;
    }
    public static DateTime IfMax(this DateTime d, DateTime d1) {
      return d == DateTime.MaxValue ? d1 : d;
    }

    public static double Div(this int v, int other) {
      return (v / (double)other);
    }
    public static double Div(this int v, double other) {
      return v / other;
    }
    public static int Sub(this int v, int other) {
      return v - other;
    }
    public static double Sub(this double v, double other) {
      return v - other;
    }
    public static double Div(this double v, double other) {
      return v / other;
    }

    public static double Avg(this double v, double other) {
      return (v + other) / 2;
    }
    public static double? Abs(this double? v) {
      return v.HasValue ? v.Value.Abs() : (double?)null;
    }
    public static double Abs(this double v) {
      return Math.Abs(v);
    }
    public static double Abs(this double v, double other) {
      return Math.Abs(v - other);
    }
    public static int Abs(this int v) {
      return Math.Abs(v);
    }
    public static int Abs(this int v, int other) {
      return Math.Abs(v - other);
    }
    public static int Sign(this int v) {
      return Math.Sign(v);
    }
    public static int Sign(this double v) {
      return Math.Sign(v);
    }
    public static int Sign(this double v, double other) {
      return Math.Sign(v - other);
    }
    public static int SignUp(this double v) {
      return Math.Sign(v) >= 0 ? 1 : -1;
    }
    public static int SignUp(this double v, double other) {
      return (v - other).SignUp();
    }
    public static int SignDown(this double v, double other) {
      var s = Math.Sign(v - other);
      return s > 0 ? 1 : -1;
    }
    public static double Max(this double? v, double? other) {
      return !v.HasValue ? other.GetValueOrDefault(double.NaN) : !other.HasValue ? v.GetValueOrDefault(double.NaN) : Math.Max(v.Value, other.Value);
    }
    public static double Max(this double v, double other) {
      return double.IsNaN(v) ? other : double.IsNaN(other) ? v : Math.Max(v, other);
    }
    public static double Max(this double v, params double[] other) {
      return other.Aggregate(v, (p, n) => p.Max(n));
    }
    public static int Max(this int v, params int[] other) {
      return other.Aggregate(v, (p, n) => p.Max(n));
    }
    public static double Min(this double? v, double? other) {
      return !v.HasValue ? other.GetValueOrDefault(double.NaN) : !other.HasValue ? v.GetValueOrDefault(double.NaN) : Math.Min(v.Value, other.Value);
    }
    public static double Min(this double v, double other) {
      return double.IsNaN(v) ? other : double.IsNaN(other) ? v : Math.Min(v, other);
    }
    public static double Min(this double v, params double[] other) {
      return other.Aggregate(v, (p, n) => p.Min(n));
    }
    public static int Min(this int v, int other) {
      return Math.Min(v, other);
    }
    public static int Min(this int v, params int[] other) {
      return other.Aggregate(v, (p, n) => p.Min(n));
    }

    public static int Floor(this double d) { return (int)Math.Floor(d); }
    public static int Floor(this double d, double other) { return (int)Math.Floor(d / other); }
    public static int Floor(this int d, double other) { return (int)Math.Floor(d / other); }
    public static int Ceiling(this double d) { return (int)Math.Ceiling(d); }
    public static int ToInt(this double d, bool useCeiling) {
      return (int)(useCeiling ? Math.Ceiling(d) : Math.Floor(d));
    }
    public static int ToInt(this double d) { return (int)Math.Round(d, 0); }

    #region Between
    public static bool Between(this int value, double d1, double d2) {
      return Math.Min(d1, d2) <= value && value <= Math.Max(d1, d2);
    }
    public static bool Between(this double value, double[] dd) {
      return value.Between(dd[0], dd[1]);
    }
    public static bool Between(this double value, double d1, double d2) {
      return d1 < d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this DateTime value, DateTime d1, DateTime d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this DateTime value, DateTimeOffset d1, DateTimeOffset d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this DateTimeOffset value, DateTimeOffset d1, DateTimeOffset d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this TimeSpan value, TimeSpan d1, TimeSpan d2) {
      return d1 < d2 ? d1 <= value && value <= d2 : d1 <= value || value <= d2;
    }
    #endregion
    public static double IntOrDouble(this double d, double max = 10) {
      return d.Abs() > max ? d.ToInt() : Math.Round(d, 1);
    }

  }
}
