using HedgeHog;
using IBApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IBApp {
  static class IBApiMixins {
    public static ContractDetails AddToCache(this ContractDetails cd) {
      Contract.ContractDetails.TryAdd(cd.Summary.Instrument, cd);
      return cd;
    }
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
      var date = Regex.Split(dateTime, @"\s+")[0];
      var time = Regex.Split(dateTime, @"\s+")[1];
      return date.ToDateTime("yyyyMMdd", DateTimeKind.Local) +
        time.ToDateTime("HH:mm:ss", DateTimeKind.Local).TimeOfDay;
    }
    public static DateTime FromTWSDateString(this string d) {
      var date = Regex.Split(d, @"\s+")[0];
      return date.ToDateTime("yyyyMMdd", DateTimeKind.Local);
    }
    static DateTime ToDateTime(this string dateTimeString, string dateTimeFormat, DateTimeKind dateTimeKind) {
      if(string.IsNullOrEmpty(dateTimeString)) {
        return DateTime.MinValue;
      }

      return DateTime.SpecifyKind(DateTime.ParseExact(dateTimeString, dateTimeFormat, CultureInfo.InvariantCulture), dateTimeKind);
    }
  }
}
