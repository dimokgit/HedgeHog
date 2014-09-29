using HedgeHog;
using HedgeHog.Bars;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;

namespace UnitLib
{
    
    
    /// <summary>
    ///This is a test class for BarBaseTest and is intended
    ///to contain all BarBaseTest Unit Tests
    ///</summary>
  [TestClass()]
  public class BarBaseTest {


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


    internal virtual BarBase CreateBarBase() {
      return new Rate(10, 9, 4, 5, 2, 1, 3, 4, DateTime.Now);
    }

    [TestMethod()]
    public void PriceAvgTest() {
      BarBase target = CreateBarBase();
      var xml = target.ToXml(saveOptions: System.Xml.Linq.SaveOptions.DisableFormatting);
      TestContext.WriteLine("Rate:" + xml);
      Assert.AreEqual(((10 + 9) / 2.0 + (2 + 1) / 2.0) / 2, target.PriceAvg);
      Assert.AreEqual(target.PriceAvg, target.RunningHigh);
      Assert.AreEqual(target.RunningHigh, target.RunningLow);
    }
  }
}
