using HedgeHog.Alice.Store;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using HedgeHog.Shared;
using HedgeHog.Bars;
using System.Collections.Generic;

namespace UnitLib
{
    
    
    /// <summary>
    ///This is a test class for TradingMacroTest and is intended
    ///to contain all TradingMacroTest Unit Tests
    ///</summary>
  [TestClass()]
  public class TradingMacroTest {


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

    [TestMethod]
    public void GetTradeConditions() {
      var tcs = new TradingMacro().GetTradeConditions();
      Assert.IsTrue(tcs != null);
    }
    /// <summary>
    ///A test for Fractals
    ///</summary>
    //[TestMethod()]
    //public void FractalsTest() {
    //  IList<double> prices = new double[] { 1, 2, 3, 2, 1, 13, 12, 11, 12, 13 };
    //  int fractalLength = 5;
    //  IDictionary<bool, double> expected = new Dictionary<bool, double>() { { true, 8 }, { false, 6 } };
    //  var actual = TradingMacro.Fractals(prices, fractalLength);
    //  Assert.AreEqual(expected[true], actual[true].Average());
    //  Assert.AreEqual(expected[false], actual[false].Average());
    //}
  }
}
