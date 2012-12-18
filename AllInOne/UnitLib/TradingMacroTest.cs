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


    /// <summary>
    ///A test for SubscribeToTradeClosedEVent
    ///</summary>
    [TestMethod()]
    public void SubscribeToTradeClosedEVentTest() {
      TradingMacro tm = new TradingMacro() { Pair = "EUR/USD" }; // TODO: Initialize to an appropriate value
      var m = new VirtualTradesManager("AAAAAA", t => 0);
      Func<ITradesManager> getTradesManager = () => m; // TODO: Initialize to an appropriate value
      tm.SubscribeToTradeClosedEVent(getTradesManager);
      tm.RatesInternal.AddRange(new[] { new Rate(), new Rate(), new Rate() });
      var rates = tm.RatesArraySafe;
      var td = tm.TradingDistance;
      Assert.Inconclusive("A method that does not return a value cannot be verified.");
    }
  }
}
