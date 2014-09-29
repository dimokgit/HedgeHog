using HedgeHog.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitLib
{
    
    
    /// <summary>
    ///This is a test class for TradeExtensionsTest and is intended
    ///to contain all TradeExtensionsTest Unit Tests
    ///</summary>
  [TestClass()]
  public class TradeExtensionsTest {


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
    ///A test for DistanceMaximum
    ///</summary>
    [TestMethod()]
    public void DistanceMaximumTest() {
      IList<Trade> trades = new[] { new Trade() { PL = -100 }, new Trade() { PL = -50 }, new Trade() { PL = -300 } }.ToList();
      double expected = 200;
      double actual = trades.DistanceMaximum();
      Assert.AreEqual(expected, actual);
      trades.Clear();
      actual = trades.DistanceMaximum();
      Assert.AreEqual(0, actual);
      trades = null;
      actual = trades.DistanceMaximum();
      Assert.AreEqual(0, actual);
    }
  }
}
