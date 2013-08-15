using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;

namespace UnitLib
{
    
    
    /// <summary>
    ///This is a test class for MathExtensionsTest and is intended
    ///to contain all MathExtensionsTest Unit Tests
    ///</summary>
  [TestClass()]
  public class MathExtensionsTest {


    private TestContext testContextInstance;

    /// <summary>
    ///Gets or sets the test context which provides
    ///information about and functionality for the current test run.
    ///</summary>
    public TestContext TestContext {
      get {
        return testContextInstance;
      }
      set {
        testContextInstance = value;
      }
    }

    #region Additional test attributes
    // 
    //You can use the following additional attributes as you write your tests:
    //
    //Use ClassInitialize to run code before running the first test in the class
    //[ClassInitialize()]
    //public static void MyClassInitialize(TestContext testContext)
    //{
    //}
    //
    //Use ClassCleanup to run code after all tests in a class have run
    //[ClassCleanup()]
    //public static void MyClassCleanup()
    //{
    //}
    //
    //Use TestInitialize to run code before running each test
    //[TestInitialize()]
    //public void MyTestInitialize()
    //{
    //}
    //
    //Use TestCleanup to run code after each test has run
    //[TestCleanup()]
    //public void MyTestCleanup()
    //{
    //}
    //
    #endregion


    /// <summary>
    ///A test for AverageByIterations
    ///</summary>
    [TestMethod()]
    public void AverageByIterationsTest() {
      IList<double> values = Enumerable.Range(0, 10000).Select(i => (double)i).ToList();
      double iterations = 4;
      List<double> averagesOut = new List<double>();
      var swDict = new Dictionary<string, double>();
      MathExtensions.AverageByIterations(values, d => d, (d1, d2) => d1 > d2, iterations);
      List<double> averagesOut2 = new List<double>();
      Stopwatch sw = Stopwatch.StartNew();
      MathExtensions.AverageByIterations_(values, d => d, (d1, d2) => d1 > d2, iterations, averagesOut2);
      swDict.Add("2", sw.ElapsedMilliseconds);
      sw.Restart();
      MathExtensions.AverageByIterations(values, iterations, averagesOut);
      swDict.Add("1", sw.ElapsedMilliseconds);
      Debug.WriteLine("{0}:{1:n1}ms" + Environment.NewLine + "{2}", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, string.Join(Environment.NewLine, swDict.Select(kv => "\t" + kv.Key + ":" + kv.Value)));
      Assert.IsFalse(averagesOut.Except(averagesOut2).Any());
    }

    /// <summary>
    ///A test for ValueByPisition
    ///</summary>
    [TestMethod()]
    public void ValueByPisitionTest() {
      double sampleCurrent = 601; // TODO: Initialize to an appropriate value
      double sampleLow = 10; // TODO: Initialize to an appropriate value
      double sampleHigh = 1000; // TODO: Initialize to an appropriate value
      double realLow = 120; // TODO: Initialize to an appropriate value
      double realHigh = 840; // TODO: Initialize to an appropriate value
      int expected = 550; // TODO: Initialize to an appropriate value
      double actual;
      actual = sampleCurrent.ValueByPosition(sampleLow, sampleHigh, realLow, realHigh);
      Assert.AreEqual(expected, actual.ToInt());
    }

    /// <summary>
    ///A test for Between
    ///</summary>
    [TestMethod()]
    public void BetweenTest() {
      double value = 5; // TODO: Initialize to an appropriate value
      Assert.IsTrue(value.Between(1,5));
      Assert.IsTrue(value.Between(5, 5));
      Assert.IsTrue(value.Between(5, 15));
      Assert.IsTrue(value.Between(15, 5));
      Assert.IsFalse(value.Between(15, 6));
      Assert.IsFalse(value.Between(double.NaN, 5));
      Assert.IsFalse(value.Between(3, double.NaN));
      Assert.IsFalse(double.NaN.Between(double.NaN, double.NaN));
      Assert.IsFalse(double.NaN.Between(double.NaN, 8));
    }

    /// <summary>
    ///A test for Sin
    ///</summary>
    [TestMethod()]
    public void SinTest() {
      int sinLength = 180;
      int waveLength = 300;
      int wavesCount = 3;
      double[] actual = MathExtensions.Sin(sinLength, waveLength,100, 1, wavesCount);
      Assert.IsNotNull(actual);
    }

    /// <summary>
    ///A test for Crosses
    ///</summary>
    [TestMethod()]
    public void CrossesTest() {
      var l = 180*2;
      IList<double> values1 = Enumerable.Range(0, l).Select(i => Math.Sin(i * Math.PI / 180)).ToArray();
      IList<double> values2 = Enumerable.Range(0, l).Select(i => Math.Cos(i * Math.PI / 180)).ToArray();
      int expected = 2;
      var crosses = MathExtensions.Crosses(values1, values2);
      Assert.AreEqual(expected, crosses.Count);
      var crosses1 = values1.Crosses<double>(values2, d => d).ToArray();
      Assert.AreEqual(2, crosses1.Length);
    }

    /// <summary>
    ///A test for Wavelette
    ///</summary>
    [TestMethod()]
    public void WaveletteTest() {
      IList<double> values = Enumerable.Range(0, 180).Select(i => Math.Sin(i * Math.PI / 180)).ToArray();
      var actual = MathExtensions.Wavelette(values);
      Assert.AreEqual(91, actual.Count);
      var values1 = new[] { 0.0, 0.0, 1.0, 2.0, 1.0 };
      actual = MathExtensions.Wavelette(values1);
      Assert.AreEqual(4,actual.Count);
    }

    /// <summary>
    ///A test for Edge
    ///</summary>
    [TestMethod()]
    public void EdgeTest() {
      double[] values = MathExtensions.Sin(100, 10000, 3, 0, 10);
      var csv = values.Csv();
      TestContext.WriteLine(csv);
      double step = 0.01;
      var actual = MathExtensions.Edge(values, step,3);
      Assert.AreEqual(0.62352314519258079, actual[0].SumAvg);
    }
  }
}
