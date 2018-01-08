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

    [TestMethod()]
    public void IsTradingHour2() {
      var range = "[['9:00','17:00']]";
      Assert.IsTrue(TradingMacro.IsTradingHour2(range, DateTime.Parse("10:00")));
      Assert.IsFalse(TradingMacro.IsTradingHour2(range, DateTime.Parse("17:30")));

      Assert.IsTrue(TradingMacro.IsTradingHour2("[['9:00','17:00'],['17:30','17:40']]", DateTime.Parse("17:30")));
      Assert.IsTrue(TradingMacro.IsTradingHour2("[['9:00','17:00'],['17:30','17:40']]", DateTime.Parse("9:30")));
      Assert.IsFalse(TradingMacro.IsTradingHour2("[['9:00','17:00'],['17:30','17:40']]", DateTime.Parse("17:20")));

      Assert.IsTrue(TradingMacro.IsTradingHour2("[['17:00','9:00']]", DateTime.Parse("17:20")));
      Assert.IsTrue(TradingMacro.IsTradingHour2("[['17:00','9:00']]", DateTime.Parse("9:00")));
      Assert.IsFalse(TradingMacro.IsTradingHour2("[['17:00','9:00']]", DateTime.Parse("16:20")));
      Assert.IsFalse(TradingMacro.IsTradingHour2("[['17:00','9:00']]", DateTime.Parse("9:20")));
    }

    [TestMethod]
    public void GetTradeConditions() {
      var tcs = new TradingMacro().GetTradeConditions();
      Assert.IsTrue(tcs != null);
    }
    [TestMethod]
    public void IsTradingHour() {
      var d = DateTime.Parse("1:00");
      var d2 = DateTime.Parse("17:00");
      var d3 = DateTime.Parse("00:00");
      Assert.IsTrue(TradingMacro.IsTradingHour("", d));
      Assert.IsTrue(TradingMacro.IsTradingHour("", d3));
      Assert.IsTrue(TradingMacro.IsTradingHour("0:29-1:01", d));
      Assert.IsFalse(TradingMacro.IsTradingHour("0:29-1:01", d2));
      Assert.IsTrue(TradingMacro.IsTradingHour("21:00-1:00", d));
      Assert.IsFalse(TradingMacro.IsTradingHour("21:00-1:00", d2));
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
