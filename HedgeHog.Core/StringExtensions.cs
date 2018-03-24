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
    public static string Mash(this string s, params string[] ss) => new[] { s }.Concat(ss).ToArray().Mash();
    public static string Mash(this IList<string> ss) {
      var diffs = (from s in ss.Zip(ss.Skip(1), (s1, s2) => (s1, s2))
                   select Diff(s.s1, s.s2)).ToArray();
      var leftCount = diffs.Min(d => d.left.Length);
      var rightCount = diffs.Min(d => d.right.Length);
      var left = new string(ss.Take(1).Select(d => d.Take(leftCount)).Concat().ToArray());
      var right = new string(ss.Take(1).Select(d => d.TakeLast(rightCount)).Concat().ToArray());
      var middle = ss.Select(s => new string(s.Skip(leftCount).SkipLast(rightCount).ToArray()));
      return $"{left}[{string.Join("][", middle)}]{right}";
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
