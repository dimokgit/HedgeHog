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

    [TestMethod()]
    public void DistancesTest() {
      DistanceImpl(100, 3);
      DistanceImpl(100, 2);
      DistanceImpl(100, 1);
    }

    private static void DistanceImpl(int count, int buffer) {
      var seq = Enumerable.Range(0, count).Select(i => Tuple.Create(i + "", i)).ToArray();
      Assert.AreEqual(count - 1, seq.Distances(t => t.Item2).Last().Item2);
      var seq2 = seq.Buffer(buffer, buffer - 1)
        .TakeWhile(b => b.Count > 1)
        .Select(b => new { item = b[0].Item1, dist = b.Distances(t => t.Item2).Last().Item2 }).ToArray();
      Assert.AreEqual(count - 1, seq2.Sum(x => x.dist));
    }

    [TestMethod()]
    public void RelativeRatiosTest() {
      Assert.AreEqual((0.25, 0.75), 1.0.RelativeRatios(3));
      Assert.AreEqual((0.25, 0.75), (-1.0).RelativeRatios(-3));
      Assert.AreEqual((double.NaN, double.NaN), (1.0).RelativeRatios( -3));
    }
  }
}