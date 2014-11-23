using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Dynamic;
namespace HedgeHog.Tests {
  [TestClass()]
  public class WFDTests {
    [TestMethod()]
    public void GetTest() {
      var key1 = WFD.Make<int>("key1");
      var key2 = WFD.Make<int>("key2");
      var e = new ExpandoObject();
      dynamic d = e;

      try {
        Assert.AreEqual<int>(0, key1(e));
      } catch (KeyNotFoundException) {
        key1(e, () => 48);
      }
      Assert.AreEqual<int>(48, key1(e));
    }
  }
}
