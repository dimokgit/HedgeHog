using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.DateTimeZone {
  public static class DateTimeZone {
    public static readonly TimeZoneInfo LondonZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
    public static readonly TimeZoneInfo TokyoZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    public static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    
    public static DateTimeOffset InLondon(this DateTime d) { return TimeZoneInfo.ConvertTime(d, LondonZone); }
    public static DateTimeOffset InLondon(this DateTimeOffset d) { return TimeZoneInfo.ConvertTime(d, LondonZone); }
  }
}
