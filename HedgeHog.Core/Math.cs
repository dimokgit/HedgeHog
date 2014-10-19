using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class MathCore {
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
  }
}
