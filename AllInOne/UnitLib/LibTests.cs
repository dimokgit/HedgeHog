using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Reflection;
using static HedgeHog.Core.JsonExtensions;
namespace HedgeHog.Tests {
  [TestClass()]
  public class LibTests {
    [TestMethod()]
    public void LinearRegression_Test() {
      var r = new Random();
      var valuesY = Enumerable.Range(0, 1000001).Select(i => r.NextDouble()).ToArray();

      var swDict = new Dictionary<string, double>();

      Stopwatch sw = Stopwatch.StartNew();
      var intersept1 = 0.0;
      var slope1 = Intercept1(valuesY, out intersept1);
      swDict.Add("Intercept1", sw.ElapsedMilliseconds);
      sw.Restart();

      var coeffs0 = Lib.LinearRegression(valuesY);
      swDict.Add("LinearRegression", sw.ElapsedMilliseconds);
      sw.Restart();

      var coeffs = valuesY.Regress(1);
      swDict.Add("Regress", sw.ElapsedMilliseconds);
      sw.Restart();

      var coeffs2 = valuesY.LinearRegression((value, slope) => new { value, slope });
      swDict.Add("LinearRegression<T>", sw.ElapsedMilliseconds);
      sw.Restart();

      var linear = valuesY.Linear();
      swDict.Add("Linear", sw.ElapsedMilliseconds);
      sw.Restart();
      var linear2 = valuesY.Linear((intercept, slope) => new { intercept, slope });
      swDict.Add("Linear2", sw.ElapsedMilliseconds);
      sw.Restart();

      Console.WriteLine("{0}:{1:n1}ms" + Environment.NewLine + "{2}", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, string.Join(Environment.NewLine, swDict.Select(kv => "\t" + kv.Key + ":" + kv.Value)));
      Assert.AreEqual(coeffs.LineSlope().Round(7), coeffs0.LineSlope().Round(7));
      Assert.AreEqual(coeffs2.slope, coeffs0.LineSlope());
      Assert.AreEqual(coeffs.LineSlope(), linear2.slope);
      Assert.AreEqual(linear.LineSlope().Round(15), slope1.Round(15));
      Assert.IsTrue(coeffs.LineValue().Abs(intersept1) / intersept1 < 0.00000000001);
      Assert.IsTrue(coeffs.LineValue().Abs(linear.LineValue()) / linear.LineValue() < 0.00000000001);
      Assert.AreEqual(linear.LineValue(), linear2.intercept);
      Assert.AreEqual(linear.LineSlope(), linear2.slope);
    }

    public static double Slope1(double[] data, out double averageY) {
      var sum = 0.0;
      for(var i = 0; i < data.Length; i++)
        sum += data[i];
      double averageX = (data.Length - 1) / 2.0;
      averageY = sum / data.Length;
      double sum1 = 0.0, sum2 = 0.0;
      for(var j = 0; j < data.Length; j++) {
        sum1 += (j - averageX) * (data[j] - averageY);
        sum2 += (j - averageX) * (j - averageX);
      }
      return sum1 / sum2;
    }
    public static double Intercept1(double[] data, out double intersept) {
      double avgY;
      var slope = Slope1(data, out avgY);
      intersept = Intercept(slope, avgY, data.Length);
      return slope;
    }

    private static double Intercept(double slope, double avgY, int p) {
      throw new NotImplementedException();
    }
    static double Intersept(double slope, double yAverage, int dataLength) {
      return yAverage - slope * (dataLength - 1) / 2.0;

    }

    [TestMethod()]
    public void IteratonSequenceNextStepPowTest() {
      var values = Enumerable.Range(0, 5).Select(i => Lib.IteratonSequenceNextStepPow(5000, 0.6, i)).ToArray();
      Assert.AreEqual(0, values[3]);
    }

    [TestMethod()]
    public void InnerListTest() {
      ReactiveUI.ReactiveList<int> rl = new ReactiveUI.ReactiveList<int>(new[] { 1, 2, 3 });
      Assert.IsTrue(rl.InnerList().SequenceEqual(rl));
      Assert.AreEqual(rl.Last(), rl.InnerList().CopyLast(1)[0]);
      Assert.IsTrue(rl.Skip(1).SequenceEqual(rl.InnerList().CopyLast(2)));
      Assert.IsTrue(rl.Skip(0).SequenceEqual(rl.InnerList().CopyLast(30)));
      Assert.IsTrue(rl.InnerList().CopyLast(0).IsEmpty());
    }
    [TestMethod()]
    public void CopyLastFromArray() {
      var rl = new[] { 1, 2, 3 };
      Assert.AreEqual(rl.Last(), rl.CopyLast(1)[0]);
      Assert.IsTrue(rl.Skip(1).SequenceEqual(rl.CopyLast(2)));
      Assert.IsTrue(rl.Skip(0).SequenceEqual(rl.CopyLast(30)));
      Assert.IsTrue(rl.CopyLast(0).IsEmpty());
    }

    [TestMethod()]
    public void AutoRoundTest() {
      var d = 1.1435765205628123;
      Assert.AreEqual(1.14, d.AutoRound(1));
      Assert.AreEqual(1.14, d.AutoRound2(3));
    }

    [TestMethod()]
    public void Permutation() {
      var source = new[] { 1, 2, 3, 4 };
      int[][] target = new int[][] { new[] { 1, 2 }, new[] { 1, 3 }, new[] { 1, 4 }, new[] { 2, 3 }, new[] { 2, 4 }, new[] { 3, 4 } };
      var res = source.Permutation();
      Console.WriteLine(res.ToJson());
      Assert.IsTrue(res.Select(t => new[] { t.Item1, t.Item2 }).Zip(target, (s, t) => s.Zip(t, (v1, v2) => v1 == v2).All(b => b)).All(b => b));
    }
    [TestMethod()]
    public void CartesianProduct() {
      {
        var len = 3;
        var source = new[] { 1, 2, 3, 4 };
        var res  = source.Permutation( len);
        int[][] target = new int[][] { new[] { 1, 2, 3 }, new[] { 1, 2, 4 }, new[] { 1, 3, 4 }, new[] { 2, 3, 4 } };
        Console.WriteLine("source:" + source.ToJson());
        Console.WriteLine("result:" + res.ToJson());
        Assert.IsTrue(res.Zip(target, (r, t) => t.SequenceEqual(r)).All(b => b));
      }
      {
        var len = 4;
        var source = new[] { 1, 2, 3, 4, 5 };
        var res = source.Permutation(len);
        Console.WriteLine(source.ToJson());
        Console.WriteLine(res.ToJson());
        //Assert.IsTrue(res.Zip(target, (r, t) => t.SequenceEqual(t)).All(b => b));
      }
      {
        var len = 4;
        var source = new[] { 1, 2, 3, 4 };
        int[][] target = new int[][] { new[] { 1, 2, 3, 4 } };
        var res = source.Permutation(len);
        Console.WriteLine(source.ToJson());
        Console.WriteLine(res.ToJson());
        Assert.IsTrue(res.Zip(target, (r, t) => r.SequenceEqual(t)).All(b => b));
      }
      {
        var len = 5;
        var source = new[] { 1, 2, 3, 4, 5, 6 };
        int[][] target = new int[][] { new[] { 1, 2, 3, 4, 5 }, new[] { 1, 2, 3, 4, 6 }, new[] { 1, 2, 3, 5, 6 }, new[] { 1, 2, 4, 5, 6 }, new[] { 1, 3, 4, 5, 6 }, new[] { 2, 3, 4, 5, 6 } };
        var res = source.Permutation(len);
        Console.WriteLine(source.ToJson());
        Console.WriteLine(res.ToJson());
        Assert.IsTrue(res.Zip(target, (r, t) => t.SequenceEqual(r)).All(b => b));
      }
    }
    [TestMethod]
    public void CartesianProductSelf() {
      var source = new[] { 1, 2, 3, 4 };
      var result = source.CartesianProductSelf();
      Console.WriteLine(result.ToJson());
    }

    /*
     * Test Name:	CartesianProduct
Test Outcome:	Passed
Result StandardOutput:	
source:[1,2,3,4]
result:[[1,2,3],[1,2,4],[1,3,4],[2,3,4]]
[1,2,3,4,5]
[[1,2,3,4],[1,2,3,5],[1,2,4,5],[1,3,4,5],[2,3,4,5]]
[1,2,3,4,5,6]
[],[1,2,3,4,6]

*/


    [TestMethod()]
    public void AvrageByPositionTest() {
      var source = new[] {2.5 ,
9.63  ,
3.52  ,
2.64  ,
6.06  ,
3.76  ,
4.56  ,
0.84  ,
7 ,
3.6 ,
0.7 ,
3.21  ,
1.61  ,
11.12 ,
2.72  ,
2.1 ,
2.17  ,
1.45  ,
2.31  ,
1.57  ,
1.16  ,
2.42  ,
1.93  ,
1.77  ,
1.26  ,
1.38  ,
1.62  ,
3.04  ,
5.25  ,
5.83  ,
3.2 ,
1.97  ,
6.14  ,
3.39  ,
2.53  ,
5.03  ,
5.31  ,
5.02  ,
3.79  ,
12.34 ,
5.03  ,
2.79  ,
4.79  ,
6.19  ,
9.18  ,
9.82  ,
5.52  ,
9.24  ,
14.6  ,
7.09  ,
12.23 ,
16.36 ,
11.01 ,
25.22 ,
16.15 ,
62.59 ,
15.16 ,
21.18 ,
5.68  ,
6.12  ,
7.86  ,
4.63  ,
27.71
 };
      var wa = source.AverageByPosition();
      Assert.AreEqual(4.840501349, wa.Round(9));
    }
  }
}
