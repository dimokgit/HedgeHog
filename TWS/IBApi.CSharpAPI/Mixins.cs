﻿using HedgeHog;
using HedgeHog.DateTimeZone;
using IBApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IBApi {
  public static class IBApiMixins {
    private static IDictionary<string, string> timeZoneMap = new Dictionary<string, string>() {
      ["US/Central"] = "Central Standard Time",
      ["US/Eastern"] = "Eastern Standard Time"
    };
    public static IEnumerable<Contract> Sort(this IEnumerable<Contract> l) => l.OrderBy(c => c.Strike).ThenBy(c => c.Right);
    public static string MakeOptionSymbol(string tradingClass, DateTime expiration, double strike, bool isCall) {
      var date = expiration.ToTWSOptionDateString();
      var cp = isCall ? "C" : "P";
      var price = strike.ToString("00000.000").Replace(".", "");
      return $"{tradingClass.PadRight(6)}{date}{cp}{price}";
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="date"></param>
    /// <returns>yymmdd</returns>
    public static string ToTWSOptionDateString(this DateTime date) => date.ToString("yyMMdd");
    /// <summary>
    /// 
    /// </summary>
    /// <param name="date"></param>
    /// <returns>yyyymmdd</returns>
    public static string ToTWSDateString(this DateTime date) => date.ToString("yyyyMMdd");
    public static string ToTWSString(this DateTime date) {
      return date.ToString("yyyyMMdd HH:mm:ss");
    }
    public static DateTime FromTWSString(this string dateTime) {
      if(dateTime.IsNullOrWhiteSpace()) return default;
      var date = Regex.Split(dateTime, @"\s+")[0];
      var time = Regex.Split(dateTime, @"\s+").Skip(1).FirstOrDefault();
      return date.ToDateTime("yyyyMMdd", DateTimeKind.Local) +
        time.ToDateTime("HH:mm:ss", DateTimeKind.Local).TimeOfDay;
    }
    public static DateTime FromTWSDateString(this string d, DateTime defaultDate) {
      if(d.IsNullOrWhiteSpace()) return defaultDate;
      var date = Regex.Split(d, @"\s+")[0];
      return date.ToDateTime("yyyyMMdd", DateTimeKind.Local);
    }
    static DateTime ToDateTime(this string dateTimeString, string dateTimeFormat, DateTimeKind dateTimeKind) {
      if(string.IsNullOrEmpty(dateTimeString)) {
        return DateTime.MinValue;
      }

      return DateTime.SpecifyKind(DateTime.ParseExact(dateTimeString, dateTimeFormat, CultureInfo.InvariantCulture), dateTimeKind);
    }
    public static DateTime ChangeTwsZone(this DateTime date, string twsZone) {
      var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneMap[twsZone]);
      return date + (DateTimeZone.Eastern.BaseUtcOffset - tz.BaseUtcOffset);
    }
  }
}
