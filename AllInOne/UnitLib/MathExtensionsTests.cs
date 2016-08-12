using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Tests {
  [TestClass()]
  public class MathExtensionsTests {
    [TestMethod()]
    public void LinearTest() {
      var xs = Enumerable.Range(0, 3).Select(i => (double)i).ToArray();
      alglib.lrreport lr1;
      var y1 = new double[] { 0, 10, 30 };
      MathExtensions.Linear(xs, y1, out lr1);
      alglib.lrreport lr2;
      var y2 = new double[] { 0, 1, 3 };
      MathExtensions.Linear(xs, y2, out lr2);
      Assert.Inconclusive();
    }

    [TestMethod()]
    public void IteratonSequenceTest() {
      var steps = Lib.IteratonSequence(1, 500).ToArray();
      Assert.IsNotNull(steps);
    }
  }
}