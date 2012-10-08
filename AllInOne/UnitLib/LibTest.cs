using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Caching;
using System.Threading;
using HedgeHog.Bars;
using System.Reflection;
using System.Threading.Tasks.Dataflow;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive;
using System.Reactive.Concurrency;
using System.IO;

namespace UnitLib
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

    [TestMethod]
    public void TestToParams() {
      var paramsText = @"\
Dimok:1 2 3
Dimon:aaa bbb ccc
Privet:2.3 3.4
";
      var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      paramsText = File.ReadAllText(Path.Combine(path, "TestParams.txt"));
      var paramLines = paramsText.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
      var paramsArray = paramLines.Select(pl => pl.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
      var paramDict = paramsArray.Where(a=>a.Length>1).ToDictionary(pa => pa[0], pa => pa[1] );
      var params1 = paramDict.ToArray();
    }
    public void ToListSpeed() {
      var array = Enumerable.Range(0, 1000000).ToArray();
      Stopwatch sw = Stopwatch.StartNew();
      var array1 = array.Where(i => i % 2 == 0).ToArray();
      Debug.WriteLine("{0}:{1:n1}ms", "ToArray", sw.ElapsedMilliseconds); sw.Restart();
      var list = array.Where(i => i % 2 == 0).ToList();
      Debug.WriteLine("{0}:{1:n1}ms", "ToList", sw.ElapsedMilliseconds); sw.Restart();
      list = array.ToList();
      Debug.WriteLine("{0}:{1:n1}ms", "ToList1", sw.ElapsedMilliseconds); sw.Restart();
    }
    public void RingQueue() {
      Action<int> action = i => {
        Debug.WriteLine("a:" + i);
        Thread.Sleep(1000);
      };
      var rq = new RingQueue<Action>(5);
      for (int i = 1; i < 6; i++) {
        var i1 = i;
        rq.Enqueue(() => action(i1));
      }
      bool isCompleted = false;
      rq.ToObservable(Scheduler.NewThread).Subscribe(a=>a(),()=>isCompleted = true);
      var ii = 6;
      while (!isCompleted) {
        var i1 = ++ii;
        rq.Enqueue(() => action(i1));
        Thread.Sleep(1000);
      }
    }
    public void Task_Continue() {
      var t = Task.Factory.StartNew(() => {
        Debug.WriteLine("Task 1");
        Thread.Sleep(5000);
      });
      t.ContinueWith(q => {
        Debug.WriteLine("Task 2");
      });
      t.ContinueWith(q => {
        Debug.WriteLine("Task 3");
      });
      Thread.Sleep(5000);
    }
    public void Observe_Buffer2() {
      var bb = new BroadcastBlock<string>(s => { /*Debug.WriteLine("b:" + s);*/ return s; });
      bb.AsObservable().Buffer(1.FromSeconds()).Where(s=>s.Any()).Subscribe(s => {
//        Debug.WriteLine(DateTime.Now.ToString("mm:ss.fff") + Environment.NewLine + string.Join("\t" + Environment.NewLine, s));
        Debug.WriteLine(DateTime.Now.ToString("mm:ss.fff") + Environment.NewLine + "\t" + s.Count);
      });
      for (int i = 0; i < 1000000;i++ )
        bb.SendAsync(DateTime.Now.ToString(i + " mm:ss.fff"));
      Thread.Sleep(10000);
    }
    public void Observe_Buffer() {
      var subject = new Subject<string>();
      subject.Buffer(1.FromSeconds())
        .Where(sl=>sl.Any())
        .Subscribe(sl => {
          Debug.WriteLine(string.Join(Environment.NewLine, sl));
        });
      subject.OnNext("DImok");
      Thread.Sleep(10000);
      subject.OnNext("DImok");
      Thread.Sleep(5000);
    }
    public void Observe_Timer() {
      Observable.Timer(1.FromSeconds()).Subscribe(l => Debug.WriteLine(l), () => Debug.WriteLine("Done"));
      Task.Factory.StartNew(() => { while (true) { } }).Wait(5.FromSeconds());
    }
    public void DataFlow() {
      var bb = new BroadcastBlock<string>(s => { Debug.WriteLine("b:" + s); return s; });
      Task.Factory.StartNew(() => {
        while (true) {
          var s = bb.Receive();
          Debug.WriteLine("a:" + s);
          Thread.Sleep(2000);
        }
      });
      Task.Factory.StartNew(() => {
        var d = DateTime.Now;
        while (d.AddSeconds(5)>DateTime.Now) {
          bb.SendAsync(DateTime.Now.ToString("mm:ss"));
        }
      });
      Task.Factory.StartNew(() => { while (true) { } }).Wait(15.FromSeconds());
    }
    public void Cma() {
      var rates = new List<Rate>();
      var rand = new Random();
      foreach (var i in Enumerable.Range(0, 2000)) {
        var price = rand.Next(14000, 14500) / 10000.0;
        rates.Add(new Rate() { AskHigh = price, AskLow = price, BidHigh = price, BidLow = price });
      }
      Stopwatch sw = Stopwatch.StartNew();
      rates.SetCma(5);
      Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds);
      var cma1 = rates.Average(r => r.PriceCMALast);
      sw.Restart();
      rates.SetCMA(5);
      Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds);
      var cma2 = rates.Average(r => r.PriceCMALast);
      Assert.AreNotEqual(cma1, cma2);
    }
    public void Cache() {
      var key = "Dimok";
      var mc = new MemoryCache("X");
      var cip = new CacheItemPolicy() { RemovedCallback = ce => { /*Debugger.Break();*/ }, AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(5) };
      mc.Add("Dimok", "Dimon", cip);
      Assert.IsTrue(mc.Contains(key));
      Task.WaitAll(Task.Factory.StartNew(() => Thread.Sleep(10000)));
      Assert.IsTrue(mc.Contains(key));
    }
    public void MA() {
      var d = Enumerable.Range(1, 1000).Select(i => Math.Cos(i)).ToDictionary(n=>n,n=>n);
      SortedList<double, double> sl = new SortedList<double, double>(d);
      var period = 9;
      Stopwatch sw = Stopwatch.StartNew();
      var mas1 = sl.MovingAverage_(period);
      Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds);
      sw.Restart();
      var mas2 = sl.MovingAverage(period);
      Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds);
      var mas10 = mas2.Except(mas1);
      //Assert.IsFalse(mas10.Any());
      sw.Restart();
      double[] inReal = d.Select(li => li.Value).ToArray();
      int outBegIdx, outNBElement;
      double[] outReal = inReal.Trima(period, out outBegIdx, out outNBElement);
      Debug.WriteLine("{0}:{1:n1}ms", MethodBase.GetCurrentMethod().Name, sw.ElapsedMilliseconds);
      var mas11 = mas2.Select(li => li.Value).Except(outReal);
      Assert.IsFalse(mas11.Any());
    }

    public void CMA() {
      var d = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
      var period = 10;
      var maList = new List<double>() { d[0] };
      var a = d.Aggregate((p, c) => {
        var ma = Lib.Cma(p, period, c);
        maList.Add(ma);
        return ma;
      });
      Debug.WriteLine(string.Join(Environment.NewLine, maList));
    }
    public void ObservableCollectionTest() {
      var oc = new ObservableCollection<string>();
      var t = Task.Factory.StartNew(() => {
        try {
          oc.Add("Dimok");
        } catch (Exception exc) {
          TestContext.WriteLine("{0}", exc);
        };
      });
      Task.WaitAll(t);
      Assert.AreEqual(oc[0], "Dimok");
    }
    public void MaxTest() {
      double a = 1.0, b = double.NaN, c = 3;
      var actual = c.Max(b, a);
      Assert.AreEqual(3, actual);
      actual = b.Min(a, c);
      Assert.AreEqual(1, actual);
    }
  }
}
