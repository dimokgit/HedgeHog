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
      int sampleCurrent = 601; // TODO: Initialize to an appropriate value
      double sampleLow = 10; // TODO: Initialize to an appropriate value
      double sampleHigh = 1000; // TODO: Initialize to an appropriate value
      double realLow = 120; // TODO: Initialize to an appropriate value
      double realHigh = 840; // TODO: Initialize to an appropriate value
      int expected = 550; // TODO: Initialize to an appropriate value
      int actual;
      actual = sampleCurrent.ValueByPosition(sampleLow, sampleHigh, realLow, realHigh);
      Assert.AreEqual(expected, actual);
    }
  }
}
