using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace HedgeHog.Shared.Tests {
  [TestClass()]
  public class TradesManagerStaticTests {
    [TestMethod()]
    public void PipAmount2Test() {
      Assert.AreEqual(18.251244785358632, TradesManagerStatic.PipAmount2(217000, 118.896, 0.01));
    }
  }
}
