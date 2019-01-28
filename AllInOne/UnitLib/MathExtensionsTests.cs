using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;

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
      Assert.AreEqual((double.NaN, double.NaN), (1.0).RelativeRatios(-3));
    }

    [TestMethod()]
    public void CrossRange() {
      int sinLength = 180;
      int waveLength = 300;
      int wavesCount = 4;
      double[] sinus = MathExtensions.Sin(sinLength, waveLength, 100, 1, wavesCount);
      double min = -95, max = 95;
      Assert.AreEqual(4, sinus.CrossRange(min, max));
      Assert.AreEqual(1, sinus.CrossRange(-100, 100));
    }

    [TestMethod()]
    public void MinMaxByRegressoin2() {
      var r = new Random();
      var source = Range.Int32(0, 100000).ToArray(i => i + r.NextDouble());

      var sw = Stopwatch.StartNew();
      var minMax = source.MinMaxByRegression();
      Debug.WriteLine(new { sw.ElapsedMilliseconds, minMax });
      sw.Restart();
      var minMax2 = source.MinMaxByRegressoin2();
      Debug.WriteLine(new { sw.ElapsedMilliseconds, minMax2 });
      Assert.AreEqual((minMax[0], minMax[1]), (minMax2[0], minMax2[1]));
      sw.Restart();
      var height = source.Height();
      var heightR = source.HeightByRegression();
      Debug.WriteLine(new { sw.ElapsedMilliseconds, heightR });
      Assert.AreEqual(minMax2[1] - minMax2[0], heightR);
    }

    [TestMethod()]
    public void RoundBySample() {
      Assert.AreEqual(100.25, 100.28.RoundBySample(.25));
      Assert.AreEqual(100.50,100.38.RoundBySample(.25));
    }
  }
}