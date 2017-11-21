using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Tests {
  [TestClass()]
  public class MemoizerTests {
    [TestMethod()]
    public void MemoizePrevTest() {
      var test1 = new { a = new { b = 1 } };
      var test2 = new { a = new { b = 100 } };
      var test3 = new { a = new { b = 2 } };
      var func = MonoidsCore.ToFunc(test1, a => a.a.b).MemoizePrev(a => a > 10);
      Assert.AreEqual(1,func(test1));
      Assert.AreEqual(1, func(test2));
      Assert.AreEqual(2,func(test3));
    }
  }
}