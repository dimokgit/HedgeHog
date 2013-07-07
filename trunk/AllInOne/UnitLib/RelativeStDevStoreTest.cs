using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnitLib
{
    
    
    /// <summary>
    ///This is a test class for RelativeStDevStoreTest and is intended
    ///to contain all RelativeStDevStoreTest Unit Tests
    ///</summary>
  [TestClass()]
  public class RelativeStDevStoreTest {


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
    ///A test for Get
    ///</summary>
    [TestMethod()]
    public void GetThem() {
      var a = new[] { 
        new { height = 100, count = 16, expected = .6348 } ,
        new { height = 100, count = 20, expected = .6227 } ,
        new { height = 200, count = 25, expected = .6133 } ,
        new { height = 150, count = 20, expected = .6227 } 
      };
      Parallel.ForEach(a, b => RelativeStDevStore.Get(b.height, b.count));
      a.ForEach(b => Assert.AreEqual(b.expected, RelativeStDevStore.Get(b.height, b.count)));
    }
    [TestMethod()]
    public void GetThemRand() {
      var rand = new Random();
      var heights = Enumerable.Range(0,100).Select(_=>rand.Next(100,110));
      var counts = Enumerable.Range(0, 100).Select(_ => rand.Next(10, 20));
      var tests = (from height in heights
                   from count in counts
                   select new { height, count, expected = RelativeStDevStore.Get(height, count) }).ToArray();
      RelativeStDevStore.RSDs.Clear();
      tests.AsParallel().ForAll(a => RelativeStDevStore.Get(a.height, a.count));
      tests.ForEach(b => Assert.AreEqual(b.expected, RelativeStDevStore.Get(b.height, b.count)));
    }
  }
}
