using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace UnitLib {
  [TestClass]
  public class UnitTest1 {
    class Comp :IEquatable<Comp> {
      private readonly int i;

      public Comp(int i) {
        this.i = i;
      }

      public static bool operator !=(Comp a, Comp b) => !(a==b);
      public static bool operator ==(Comp a, Comp b) => (a?.Equals(b)).GetValueOrDefault(false);
      public bool Equals(Comp other) {
        if(other is null) return false;
        var a = new { i = Math.Abs(i) };
        var b = new { i = Math.Abs(other.i) };
        var c = a.Equals(b);
        return c;
      }
      public override int GetHashCode() => 165851236 + i.GetHashCode();
      public override bool Equals(object obj) => !(obj is null) && Equals(obj as Comp);
    }
    [TestMethod]
    public void Equatable() {
      var a = new Comp(1);
      var b = new Comp(-1);
      var c = (Comp)null;
      var d = new Comp(2);
      Assert.AreEqual(new { i = 0, j = new { k = 1 } }, new { i = 0, j = new { k = 1 } });
      Assert.IsFalse(new { i = 0, j = new { k = 1 } } == new { i = 0, j = new { k = 1 } });
      Assert.IsTrue(a == b);
      Assert.IsFalse(a != b);
      Assert.IsTrue(a != c);
      Assert.IsFalse(a == c);
      Assert.IsTrue(c != a);
      Assert.IsFalse(c == a);
      Assert.IsTrue(a != d);
      Assert.IsFalse(a == d);
      Assert.IsTrue(d != a);
      Assert.IsFalse(d == c);
    }
    [TestMethod]
    public void IsProcessRunning() {
      var t = Process.GetProcessesByName("HedgeHog.Alice.Client.exe").ToArray();
      Assert.IsTrue(t.Length > 0);
    }

    [TestMethod]
    public void ValueTuple() {
      var t = (a: 0, b: new Nullable<int>());
      Update(t);
      Assert.AreEqual(0, t.b.GetValueOrDefault());
      void Update((int a, Nullable<int> b) p) {
        p.a = 1;
        p.b = 1;
      }
    }
    [TestMethod]
    public void IsTimeOutException() {
      Func<string, bool> isTimeOut = s =>
        Regex.IsMatch(s, "time[A-Z ]{0,3}out", RegexOptions.IgnoreCase);

      Assert.IsTrue(isTimeOut("timeout"));
      Assert.IsTrue(isTimeOut("timed out"));
      Assert.IsTrue(isTimeOut("time out"));
    }
  }
}
