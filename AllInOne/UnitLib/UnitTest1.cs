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
    [TestMethod]
    public void IsProcessRunning() {
      var t = Process.GetProcessesByName("HedgeHog.Alice.Client.exe").ToArray();
      Assert.IsTrue(t.Length>0);
    }

    [TestMethod]
    public void ValueTuple() {
      var t = (a: 0, b: new Nullable<int>());
      Update(t);
      Assert.AreEqual( 0, t.b.GetValueOrDefault());
      void Update((int a,Nullable<int> b) p) {
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
