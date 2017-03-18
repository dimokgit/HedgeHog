using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class StringExtensions {
    public static bool IsNullOrWhiteSpace(this string s) { return string.IsNullOrWhiteSpace(s); }
    public static bool IsNullOrEmpty(this string s) { return string.IsNullOrEmpty(s); }
    public static string IfEmpty(this string s, string ifEmpty) { return string.IsNullOrWhiteSpace(s) ? ifEmpty : s; }
  }
}
