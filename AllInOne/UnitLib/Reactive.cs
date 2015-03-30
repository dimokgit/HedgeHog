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

namespace UnitLib {
  [TestClass]
  public class ReactiveExtensions {
    [TestMethod]
    public void Throttle() {
      var c = 0;
      var res = Observable.Interval(TimeSpan.FromMilliseconds(110))
        .Select(_ => DateTime.Now)
        .ObserveOn(ThreadPoolScheduler.Instance)
        .Do(t => c++)
        .Sample(TimeSpan.FromSeconds(1))
        .Take(1)
        .ToEnumerable()
        .ToArray();
      Assert.AreEqual(7, c);
    }
  }
}
