using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace UnitLib {
  [TestClass]
  public class UnitTest1 {
    [TestMethod]
    public void TestMethod1() {
      Func<int,int> counter = count => count;
      Debug.WriteLine(counter(1));
    }
  }
}
