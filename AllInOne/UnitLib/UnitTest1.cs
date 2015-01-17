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
    public void IsTimeOutException() {
      Func<string, bool> isTimeOut = s =>
        Regex.IsMatch(s, "time[A-Z ]{0,3}out", RegexOptions.IgnoreCase);

      Assert.IsTrue(isTimeOut("timeout"));
      Assert.IsTrue(isTimeOut("timed out"));
      Assert.IsTrue(isTimeOut("time out"));
    }
  }
}
