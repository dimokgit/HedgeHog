using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.DateTimeZone {
  public static class DateTimeZone {
    public static readonly TimeZoneInfo LondonZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
    public static readonly TimeZoneInfo TokyoZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    public static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static DateTime InNewYork(this DateTime d) { return TimeZoneInfo.ConvertTime(d, Eastern); }
    public static DateTime InLondon(this DateTime d) { return TimeZoneInfo.ConvertTime(d, LondonZone); }
    public static DateTimeOffset InNewYork(this DateTimeOffset d) { return TimeZoneInfo.ConvertTime(d, Eastern); }
    public static DateTimeOffset InLondon(this DateTimeOffset d) { return TimeZoneInfo.ConvertTime(d, LondonZone); }
    public static DateTimeOffset InTZ(this DateTimeOffset d, TimeZoneInfo tz) { return TimeZoneInfo.ConvertTime(d, tz); }
  }
}
