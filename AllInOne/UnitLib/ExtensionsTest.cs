using HedgeHog.Bars;
using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace HedgeHog.Tests {
  [TestClass()]
  public class ExtensionsTest {
    [TestMethod()]
    public void LevelsTest() {
      Console.WriteLine(Fibonacci.Levels(10, 0).ToJson());
    }
  }
}

namespace UnitLib {


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
      ratesForPrev = Enumerable.Range(0, 10000).Select(i => new Rate() { StartDate2 = dateStart.ToUniversalTime().AddMinutes(i) }).ToList();
      var r = new Random();
      ratesForCrosses = Enumerable.Range(0, 10000).Select(i => new Rate() { StartDate2 = dateStart.ToUniversalTime().AddMinutes(i + r.Next(100)) }).ToList();

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
    public void TestAny() {
      var any = Enumerable.Range(0, 10).Select(i => { TestContext.WriteLine("i:{0}", i); return i; });
      Assert.IsTrue(any.Any());
    }
    [TestMethod]
    public void TestCrossec() {
      var stDev = Enumerable.Range(1, 60).Select(i => (double)i).ToArray().StDevP();
      Debug.WriteLine(stDev);
    }

    /// <summary>
    ///A test for Previous
    ///</summary>
    public void PreviousTestHelper() {
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

    [TestMethod()]
    public void SetStDevPriceTest() {
      SetStDevPriceTestHelper<Rate>();
    }
    public void SetStDevPriceTestHelper<TBar>() where TBar : BarBase, new() {
      var count = 1500;
      IList<TBar> rates = Enumerable.Range(1, count).Select(i => new TBar() { AskHigh = i, AskLow = i, BidHigh = 1, BidLow = i }).ToList();
      IList<TBar> rates1 = Enumerable.Range(1, count).Select(i => new TBar() { AskHigh = i, AskLow = i, BidHigh = 1, BidLow = i }).ToList();
      IList<TBar> rates2 = Enumerable.Range(1, count).Select(i => new TBar() { AskHigh = i, AskLow = i, BidHigh = 1, BidLow = i }).ToList();
      IList<TBar> rates3 = Enumerable.Range(1, count).Select(i => new TBar() { AskHigh = i, AskLow = i, BidHigh = 1, BidLow = i }).ToList();
      IList<TBar> rates4 = Enumerable.Range(1, count).Select(i => new TBar() { AskHigh = i, AskLow = i, BidHigh = 1, BidLow = i }).ToList();
      IList<TBar> rates5 = Enumerable.Range(1, count).Select(i => new TBar() { AskHigh = i, AskLow = i, BidHigh = 1, BidLow = i }).ToList();
      Func<TBar, double> getPrice = b => b.PriceAvg;
      rates.SetStDevPrice(getPrice);

      var swDict = new Dictionary<string, double>();
      Stopwatch sw = Stopwatch.StartNew();

      rates.SetStDevPrice(getPrice);
      sw.Stop();
      swDict.Add("rates", sw.ElapsedMilliseconds);
      
      sw.Restart();
      rates1.SetStDevPrice_(getPrice);
      sw.Stop();
      swDict.Add("rates1", sw.ElapsedMilliseconds);
      
      sw.Restart();
      rates2.SetStDevPrice_1(getPrice);
      sw.Stop();
      swDict.Add("rates2", sw.ElapsedMilliseconds);

      sw.Restart();
      rates3.SetStDevPrice_2(getPrice);
      sw.Stop();
      swDict.Add("rates3", sw.ElapsedMilliseconds);

      sw.Restart();
      rates4.SetStDevPrice_3(getPrice);
      sw.Stop();
      swDict.Add("rates4", sw.ElapsedMilliseconds);

      sw.Restart();
      rates5.SetStDevPrice_4(getPrice);
      sw.Stop();
      swDict.Add("rates5", sw.ElapsedMilliseconds);

      Debug.WriteLine("[{2}]{0}:{1:n1}ms" + Environment.NewLine + "{3}", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds, "Test", string.Join(Environment.NewLine, swDict.Select(kv => "\t" + kv.Key + ":" + kv.Value)));
      var diff = rates.Select(r => r.PriceStdDev).Except(rates1.Select(r => r.PriceStdDev)).ToList();
      Assert.IsFalse(diff.Any(), "PriceStDev is different between rates and rates1");
      diff = rates.Select(r => r.PriceStdDev).Except(rates2.Select(r => r.PriceStdDev)).ToList();
      Assert.IsFalse(diff.Any(), "PriceStDev is different between rates and rates2");
      diff = rates.Select(r => r.PriceStdDev).Except(rates3.Select(r => r.PriceStdDev)).ToList();
      Assert.IsFalse(diff.Any(), "PriceStDev is different between rates and rates3");
      diff = rates.Select(r => r.PriceStdDev).Except(rates4.Select(r => r.PriceStdDev)).ToList();
      Assert.IsFalse(diff.Any(), "PriceStDev is different between rates and rates4");
      diff = rates.Select(r => r.PriceStdDev).Except(rates5.Select(r => r.PriceStdDev)).ToList();
      Assert.IsFalse(diff.Any(), "PriceStDev is different between rates and rates5");
      return;
    }


    [TestMethod]
    public void SetStDevPrice4Test() {
      var ds = new List<Action<IList<Rate>, Func<Rate, double>>>();
      ds.Add((rs, gp) => rs.SetStDevPrice_(gp));
      ds.Add((rs, gp) => rs.SetStDevPrice_1(gp));
      ds.Add((rs, gp) => rs.SetStDevPrice_2(gp));
      ds.Add((rs, gp) => rs.SetStDevPrice_3(gp));
      ds.Add((rs, gp) => rs.SetStDevPrice_4(gp));
      ds.ForEach(d => {
        SetStDevPrice4TestHelper(d,d.ToString());
      });
    }
    public void SetStDevPrice4TestHelper(Action<IList<Rate>, Func<Rate, double>> d,string name) {
      var count = 1500;
      IList<Rate> rates = Enumerable.Range(1, count).Select(i => new Rate() { AskHigh = i, AskLow = i, BidHigh = 1, BidLow = i }).ToList();
      Func<Rate, double> getPrice = b => b.PriceAvg;

      var swDict = new Dictionary<string, double>();
      Stopwatch sw = Stopwatch.StartNew();
      Enumerable.Range(1, 10).ToList().ForEach(i => {
        sw.Restart();
        d( rates,getPrice);
        sw.Stop();
        swDict.Add("rate:" + i, sw.ElapsedMilliseconds);
      });
      Debug.WriteLine("[{2}]{0}:{1:n1}ms" + Environment.NewLine + "Average:{3:n1}ms", d.Method.Name, sw.ElapsedMilliseconds, "Test", swDict.Average(kv => kv.Value));
    }


    [TestMethod]
    public void OrderableListPartitionerTest() {
      var nums = Enumerable.Range(0, 100).ToArray();
      OrderableListPartitioner<int> partitioner = new OrderableListPartitioner<int>(nums);

      // Use with Parallel.ForEach
      Parallel.ForEach(partitioner, (i) => Debug.WriteLine("Thread:"+Thread.CurrentThread.Name+","+ i));


      // Use with PLINQ
      var query = from num in partitioner.AsParallel()
                  where num % 2 == 0
                  select num;

      foreach (var v in query)
        Debug.WriteLine(v);
    }

  }
}
