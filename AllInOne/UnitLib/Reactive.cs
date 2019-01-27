using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Joins;
using System.Reactive.Linq;
using System.Reactive.PlatformServices;
using System.Reactive.Subjects;
using System.Reactive.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using HedgeHog;

namespace UnitLib {
  [TestClass]
  public class ReactiveExtensions {
    object _hm = new object();
    bool HallMonitor(int count ) {
      if(!Monitor.TryEnter(_hm, 1000)) return false;
      if(count == 0) return HallMonitor(1);
      Monitor.Exit(_hm);
      return true;
    }
    [TestMethod]
    public void MonitorEnter() {
      var b = HallMonitor(0);
      Assert.IsTrue(b);
    }
    [TestMethod]
    public async Task ReactiveCatch() {
      var source = new[] { new { i = 1 }, new { i = 0 } }.ToObservable();
      var a =await source
        .Do(x => { var j = 1 / x.i; })
        .CatchAndStop(() => new TimeoutException())
        .CatchAndStop()
        .LastAsync();
      Assert.AreEqual(a.i,  1);
    }
    [TestMethod]
    public async Task Throttle() {
      var c = 0;
      var res = await Observable.Interval(TimeSpan.FromMilliseconds(110))
        .Select(_ => DateTime.Now)
        .ObserveOn(ThreadPoolScheduler.Instance)
        .Do(t => c++)
        .Sample(TimeSpan.FromSeconds(1))
        .FirstAsync();
      Assert.AreEqual(8, c);
    }
  }
}
