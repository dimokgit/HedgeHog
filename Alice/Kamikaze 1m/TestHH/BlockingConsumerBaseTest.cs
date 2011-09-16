using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Threading;

namespace TestHH
{
    
    
    /// <summary>
    ///This is a test class for BlockingConsumerBaseTest and is intended
    ///to contain all BlockingConsumerBaseTest Unit Tests
    ///</summary>
  [TestClass()]
  public class BlockingConsumerBaseTest {


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


    public void AddTestHelper<T>() {
    }

    [TestMethod()]
    public void AddTest() {
      BlockingConsumerBase<string> target = new BlockingConsumerBase<string>(s=>Debug.WriteLine(s));
      target.Add("A");
      target.Add("A");
      target.Add("B");
      target.Add("A");
      target.Add("C");
      target.Add("B");
      Thread.Sleep(1000);
      target.Add("C");
    }
  }
}
