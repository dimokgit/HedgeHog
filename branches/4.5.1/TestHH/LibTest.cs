using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Collections.Generic;

namespace TestHH
{
    
    
    /// <summary>
    ///This is a test class for LibTest and is intended
    ///to contain all LibTest Unit Tests
    ///</summary>
  [TestClass()]
  public class LibTest {


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
    ///A test for TakeEx
    ///</summary>
    [TestMethod()]
    public void TakeExTestHelper() {
      IEnumerable<int> list = Enumerable.Range(1 , 10);
      int count = -2; // TODO: Initialize to an appropriate value
      IEnumerable<int> expected = Enumerable.Range(9,2); // TODO: Initialize to an appropriate value
      IEnumerable<int> actual;
      actual = Lib.TakeEx<int>(list, count);
      Assert.AreEqual(expected.Except( actual).Count(),0);
    }

  }
}
