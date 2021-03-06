﻿using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using static HedgeHog.MathCore;
using static HedgeHog.LinearRegression;

namespace HedgeHog.Tests {
  [TestClass()]
  public class MathExtensionsTest {
    [TestMethod]
    public void GDC() {
      var gdc = new[] { -20, 40, 12 }.GCD();
      Assert.AreEqual(4, gdc);
      Assert.IsTrue(new[] { 1, 4, 4 }.SequenceEqual(new[] { 5, 20, 20 }.AdjustByGcd().values));
    }

    [TestMethod]
    public void GDCStocks() {
      var gdc = new[] { 370, 201, 3009 }.GCD();
      Console.WriteLine(gdc);
    }
    [TestMethod]
    public void CompoundAmount() {
      double interestRate = 0.5 / 100, days = 256 * 5, Amount = 30000;
      Console.WriteLine(new { Amount = Amount.CompoundAmount(interestRate, days) });
      for(int t = 0; t < days; t++) {
        Amount = Amount + Amount * interestRate;
        Debug.WriteLine("Your Total for Year {0} " + "is {1:F0}.", t, Amount);
      }
      Console.WriteLine(new { Amount });
    }
    [TestMethod]
    public void StDev() {
      var a = new[]{ -0.00213689867397825,
        0.00118154978526681,
        -0.00688782857231444,
        -0.00679585864418092,
        0.00184067034393126,
        -0.00787758079660816,
        0.00107056839558538,
        -0.00454202143450101,
        -0.00783311154489462,
        -0.0015416807370234,
        8.80320437207391E-05,
        -8.80320437207588E-05,
        -0.0112263606145178,
        0.00613371636545561,
        0.0102998401640537,
        -0.000751863107885203,
        0.00212436459622469 };
      var sd = a.StDev();
      var sd2 = a.StandardDeviation();
      Assert.AreEqual(sd2,sd);
    }
    [TestMethod]
    public void AverageByStDevTest() {
      //var source = Enumerable.Range(0, 100).Select(i => (double)i);
      double[] source = MathExtensions.Sin(100, 10000, 3, 0, 10);
      var avgStd = source.AverageByStDev().Average();
      var std2 = source.StandardDeviation();
      var avg = source.Average();
      var avgStd2 = source.Where(s => s >= avg - std2 && s <= avg + std2).Average();
      Assert.AreEqual(avgStd, avgStd2);
    }
    [TestMethod()]
    public void RelativeStandardDeviationTest() {
      var dbls = new double[] { 699, 1157, 2041, 2951, 2657, 3664, 3462, 2377, 3135, 2727, 2487 };
      Assert.AreEqual(0.507518025484428, dbls.RelativeStandardDeviation().Round(15));
    }

    [TestMethod()]
    public void RootMeanSquareTest() {
      double a = 5, b = 10;
      Assert.AreEqual(7.9057, a.SquareMeanRoot(b).Round(4));
      Assert.AreEqual(7.2855, a.RootMeanSquare(b).Round(4));
      Assert.AreEqual(7.2855, a.RootMeanPower(b, 2).Round(4));
      Assert.AreEqual(7.2855, new[] { a, b }.RootMeanPower(2).Round(4));
      Assert.AreEqual(7.9057, new[] { a, b }.RootMeanPower(.5).Round(4));
      Assert.AreEqual(20.9067, new[] { 5.0, 7.0, 100.0 }.RootMeanPower(3).Round(4));
    }
    [TestMethod()]
    public void DoSetsOverlapTest() {
      var set1 = new double[] { 200, 400 };
      var set2 = new double[] { 300, 400 };
      var set3 = new double[] { 500, 900 };
      var set4 = new double[] { 0, 100 };
      var set5 = new double[] { 100, 300 };
      var set6 = new double[] { 100, 900 };
      Assert.IsTrue(set1.DoSetsOverlap(set2));
      Assert.IsTrue(set1.DoSetsOverlap(set5));
      Assert.IsFalse(set1.DoSetsOverlap(0.1, set3));
      Assert.IsTrue(set1.DoSetsOverlap(0.5, set3));
      Assert.IsFalse(set1.DoSetsOverlap(set3));
      Assert.IsFalse(set1.DoSetsOverlap(set4));
      Assert.IsTrue(set1.DoSetsOverlap(set6));
      Assert.IsTrue(set4.DoSetsOverlap(0, set6));
      Assert.IsFalse(set4.DoSetsOverlap(-0.1, set6));
    }
  }
}

namespace UnitLib {
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
      double sampleCurrent = 601;
      double sampleLow = 10;
      double sampleHigh = 1000;
      double realLow = 120;
      double realHigh = 840;
      int expected = 550;
      double actual;
      actual = sampleCurrent.ValueByPosition(sampleLow, sampleHigh, realLow, realHigh);
      Assert.AreEqual(expected, actual.ToInt());
    }

    /// <summary>
    ///A test for Between
    ///</summary>
    [TestMethod()]
    public void BetweenTest() {
      double value = 5;
      Assert.IsTrue(value.Between(1, 5));
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
      double[] actual = MathExtensions.Sin(sinLength, waveLength, 100, 1, wavesCount);
      Assert.IsNotNull(actual);
    }

    /// <summary>
    ///A test for Crosses
    ///</summary>
    [TestMethod()]
    public void CrossesTest() {
      var l = 180 * 2;
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
      Assert.AreEqual(4, actual.Count);
    }

    /// <summary>
    ///A test for Edge
    ///</summary>
    [TestMethod()]
    public void Edge() {
      double[] values = MathExtensions.Sin(100, 10000, 3, 0, 10);
      var csv = values.Csv();
      TestContext.WriteLine(csv);
      double step = 0.01;
      var actual = Lib.Edge(values, step, 3);
      Assert.AreEqual(0.62352314519258079, actual[0].SumAvg);
    }
    [TestMethod()]
    public void EdgeByStDev() {
      double[] values = MathExtensions.Sin(100, 10000, 3, 0, 10);
      var csv = values.Csv();
      TestContext.WriteLine(csv);
      double step = 0.01;
      var actual = Lib.EdgeByStDev(values, step, 0);
      Assert.AreEqual(0.62352314519258079, actual.First().Item1);
    }
    [TestMethod()]
    public void EdgeByAverageSlowTest() {
      double[] values = MathExtensions.Sin(100, 10000, 3, 0, 10);
      double step = 0.01;
      var sw = Stopwatch.StartNew();
      var actual2 = Lib.EdgeByAverage(values, step).ToArray();
      sw.Stop();
      Console.WriteLine(new { actual = sw.ElapsedMilliseconds });

    }

    /// <summary>
    ///A test for Round
    ///</summary>
    [TestMethod()]
    public void RoundTest() {
      var date = DateTime.Parse("1999-01-01 15:14:13");
      var dateFloor = DateTime.Parse("1999-01-01 15:14:00");
      var dateCieling = DateTime.Parse("1999-01-01 15:15:00");
      Assert.AreEqual(dateFloor, date.Round(RoundTo.Minute));
      Assert.AreEqual(dateFloor, date.Round(RoundTo.MinuteFloor));
      Assert.AreEqual(dateCieling, date.Round(RoundTo.MinuteCieling));
      return;
      DateTime dt = DateTime.Parse("1/1/1990  15:34");
      int period = 3;
      DateTime expected = new DateTime();
      DateTime actual;
      actual = dt.Round(period);
      Assert.AreEqual(expected, actual);
      Assert.Inconclusive("Verify the correctness of this test method.");

    }

    /// <summary>
    ///A test for RSD
    ///</summary>

    [TestMethod()]
    public void RSDTest() {
      IList<double> values = new double[] { 1, 5, 6, 8, 10, 40, 65, 88 };
      double expected = 118.04;
      double actual;
      actual = (values.Rsd() * 100).Round(2);
      Assert.AreEqual(expected, actual);
    }

    /// <summary>
    ///A test for FftSignalBins
    ///</summary>
    [TestMethod()]
    public void FftSignalBinsTest() {
      var r = new Random();
      IEnumerable<double> signalIn = Enumerable.Range(0, 1000).Select(i => (double)r.Next(10)).ToArray();
      IList<alglib.complex> expected = null; // TODO: Initialize to an appropriate value
      IList<alglib.complex> bins = MathExtensions.Fft0(signalIn);
      //bins[0] = new alglib.complex(0);
      //Enumerable.Range(500, 499).ToList().ForEach(i => bins[i] = new alglib.complex(0));
      double[] ifft;
      alglib.fftr1dinv(bins.SafeArray(), out ifft);

      Assert.AreEqual(expected, bins);
      Assert.Inconclusive("Verify the correctness of this test method.");
    }
    [TestMethod]
    public void BasedRatio() {
      var result = (-20.0).Percentage(-10);
      Assert.AreEqual(0.5, result);
    }
  }
}
