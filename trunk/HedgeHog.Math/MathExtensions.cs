using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class MathExtensions {

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


    public static int Floor(this double d) { return (int)Math.Floor(d); }
    public static int Ceiling(this double d) { return (int)Math.Ceiling(d); }
    public static int ToInt(this double d) { return (int)Math.Round(d, 0); }
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
      var evenMinutes = dt.Minute / period * period;
      return dt.AddMinutes(-dt.Minute).AddMinutes(evenMinutes);
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
