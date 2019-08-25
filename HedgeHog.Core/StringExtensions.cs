using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class StringExtensions {
    public static string AllCaps(this string source) => string.Concat(source.Where(c => c >= 'A' && c <= 'Z'));
    public static bool IsNullOrWhiteSpace(this string s) { return string.IsNullOrWhiteSpace(s); }
    public static bool IsNullOrEmpty(this string s) { return string.IsNullOrEmpty(s); }
    public static string IfEmpty(this string s, string ifEmpty) { return string.IsNullOrWhiteSpace(s) ? ifEmpty : s; }
    public static string IfEmpty(this string s, params string[] ifEmpty) { return string.IsNullOrWhiteSpace(s) ? ifEmpty.Where(s2 => !s2.IsNullOrWhiteSpace()).FirstOrDefault() : s; }
    public static string IfEmpty(this string s, Func<string> ifEmpty) { return string.IsNullOrWhiteSpace(s) ? ifEmpty() : s; }
    public static string IfNotEmpty(this string s, string prefix) { return string.IsNullOrWhiteSpace(s) ? s : prefix + s; }
    public static string MashDiffs(this string s, params string[] ss) => new[] { s }.Concat(ss).ToArray().MashDiffs();
    public static (IList<T> source, string mash) MashDiffs<T>(this IList<T> source, Func<T, string> map) {
      var mash = source.Select(map).ToArray().MashDiffs();
      return (source, mash);
    }
    public static string MashDiffs(this IList<string> ss, string divider = " ") {
      if(ss?.Count == 0) return "";
      if(ss.Count == 1) return ss[0];
      var diffs = (from s in ss.Zip(ss.Skip(1), (s1, s2) => (s1, s2))
                   select Diff(s.s1, s.s2)).ToArray();
      var leftCount = diffs.Select(d => d.left.Length).DefaultIfEmpty().Min();
      var rightCount = diffs.Select(d => d.right.Length).DefaultIfEmpty().Min();
      var left = new string(ss.Take(1).Select(d => d.Take(leftCount)).Concat().ToArray());
      var right = new string(ss.Take(1).Select(d => d.TakeLast(rightCount)).Concat().ToArray());
      var middle = ss.Select(s => new string(s.Skip(leftCount).SkipLast(rightCount).ToArray()));
      return $"{left}[{string.Join(divider, middle)}]{right}";
    }
    private static (char[] left, IEnumerable<char> mid1, IEnumerable<char> mid2, char[] right)
      Diff(string s1, string s2) {
      var charsLeft = s1.Zip(s2, (c1, c2) => (c1, c2)).ToArray();
      var left = charsLeft.TakeWhile(t => t.c1 == t.c2).Select(t => t.c1).ToArray();
      var charsRight = s1.Reverse().Zip(s2.Reverse(), (c1, c2) => (c1, c2)).ToArray();
      var right = charsRight.TakeWhile(t => t.c1 == t.c2).Select(t => t.c1).Reverse().ToArray();
      var str1 = s1.Skip(left.Length).SkipLast(right.Length);
      var str2 = s2.Skip(left.Length).SkipLast(right.Length);
      return (left, str1, str2, right);
    }


  }
}
