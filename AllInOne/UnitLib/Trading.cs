using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitLib {
  [TestClass]
  public class Trading {
    [TestMethod]
    public void CombineStrikes() {
      var strikes = new[] { 10.0, 10.5, 11.0, 11.5, 12, 13 };
      var spread = 2;
      strikes
        .Buffer(spread, 1)
        .Select(b => new[] { b.First(), b.Last() });
        

    }
  }
}
