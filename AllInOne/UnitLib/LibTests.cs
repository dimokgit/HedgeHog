using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Reflection;
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
      swDict.Add("Intercept1", sw.ElapsedMilliseconds); sw.Restart();

      var coeffs0 = Lib.LinearRegression(valuesY);
      swDict.Add("LinearRegression", sw.ElapsedMilliseconds); sw.Restart();

      var coeffs = valuesY.Regress(1);
      swDict.Add("Regress", sw.ElapsedMilliseconds); sw.Restart();

      var coeffs2 = valuesY.LinearRegression((value, slope) => new { value, slope });
      swDict.Add("LinearRegression<T>", sw.ElapsedMilliseconds); sw.Restart();

      var linear = valuesY.Linear();
      swDict.Add("Linear", sw.ElapsedMilliseconds); sw.Restart();
      var linear2 = valuesY.Linear((intercept, slope) => new { intercept, slope });
      swDict.Add("Linear2", sw.ElapsedMilliseconds); sw.Restart();

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
      for (var i = 0; i < data.Length; i++)
        sum += data[i];
      double averageX = (data.Length - 1) / 2.0;
      averageY = sum / data.Length;
      double sum1 = 0.0, sum2 = 0.0;
      for (var j = 0; j < data.Length; j++) {
        sum1 += (j - averageX) * (data[j] - averageY);
        sum2 += (j - averageX) * (j - averageX);
      }
      return sum1 / sum2;
    }
    public static double Intercept1(double[] data, out double intersept) {
      double avgY;
      var slope = Slope1(data, out avgY);
      intersept = Intecsept(slope, avgY, data.Length);
      return slope;
    }
    static double Intecsept(double slope, double yAverage, int dataLength) {
      return yAverage - slope * (dataLength - 1) / 2.0;

    }

    [TestMethod()]
    public void IteratonSequenceNextStepPowTest() {
      var values = Enumerable.Range(0, 5).Select(i => Lib.IteratonSequenceNextStepPow(5000, 0.6, i)).ToArray();
      Assert.AreEqual(0, values[3]);
    }
  }
}
