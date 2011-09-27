using HedgeHog.Bars;
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
    ///This is a test class for ExtensionsTest and is intended
    ///to contain all ExtensionsTest Unit Tests
    ///</summary>
  [TestClass()]
  public class ExtensionsTest {


    private TestContext testContextInstance;
    private List<Rate> ratesForPrev;
    private List<Rate> ratesForCrosses;

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
    [TestInitialize()]
    public void MyTestInitialize() {
      var dateStart = DateTime.Now;
      ratesForPrev = Enumerable.Range(0, 10000).Select(i => new Rate() { StartDate = dateStart.AddMinutes(i) }).ToList();
      var r = new Random();
      ratesForCrosses = Enumerable.Range(0, 10000).Select(i => new Rate() { StartDate = dateStart.AddMinutes(i+r.Next(100)) }).ToList();

    }
    //
    //Use TestCleanup to run code after each test has run
    //[TestCleanup()]
    //public void MyTestCleanup()
    //{
    //}
    //
    #endregion

    [TestMethod]
    public void TestCrossec() {
      var stDev = Enumerable.Range(1,60).Select(i=>(double)i).ToArray().StDevP();
      Debug.WriteLine(stDev);
    }

    /// <summary>
    ///A test for Previous
    ///</summary>
    public void PreviousTestHelper(){
      var rate = ratesForPrev[ratesForPrev.Count / 2];
      var expected = ratesForPrev.Single(r => r.StartDate == rate.StartDate.AddMinutes(-1));
      
      Stopwatch sw = Stopwatch.StartNew();
      var swDict = new Dictionary<string, double>();
      swDict.Add("1", sw.ElapsedMilliseconds);
      var actual = ratesForPrev.Previous(rate);
      Debug.WriteLine("[{2}]{0}:{1:n1}ms" + Environment.NewLine + "{3}", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, "Test", string.Join(Environment.NewLine, swDict.Select(kv => "\t" + kv.Key + ":" + kv.Value)));

      Assert.AreEqual(expected, actual);
    }
    [TestMethod]
    public void PreviousTest() {
      PreviousTestHelper();
    }
  }
}
