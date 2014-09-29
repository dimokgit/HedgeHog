using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using System.Reactive.Linq;
using System.Reactive.Concurrency;

namespace UnitLib {
  [TestClass]
  public class RxDataFlow {
    public void RX_Action2() {
      var yp = DataFlowProcessors.CreateYieldingTargetBlock<int>(i => {
        Debug.WriteLine("ab:" + i);
        Thread.Sleep(1000);
        if (i.Between(500, 700))
          throw new MulticastNotSupportedException();
      }, exc => { Debug.WriteLine(exc); });
      Enumerable.Range(0, 1000).ToList().ForEach(i => {
        yp.Post(i);
        //Debug.WriteLine("ss:" + i);
        Thread.Sleep(10);
      });
      Debug.WriteLine("Waiting for ab.");
      Thread.Sleep(5000);
    }
    [TestMethod()]
    public void RX_Action1() {
      var bb = new BroadcastBlock<int>(i => i);
      bb.SubscribeToYieldingObservable(i => {
        Debug.WriteLine("bb:" + i);
        Thread.Sleep(1000);
      });
      var isDone = false;
      Observable.Range(0, 100)
        .ObserveOn(Scheduler.ThreadPool)
        .Subscribe(i => {
          bb.Post(i);
          Thread.Sleep(110);
        }, () => isDone = true);
      while (!isDone) { }
      Debug.WriteLine("Waiting for ab.");
      Thread.Sleep(2000);
    }
    public void RX_Semple() {
      var bb = new BroadcastBlock<int>(i => i);
      var d = bb.AsObservable()
        //.Where(i => i % 3 == 0)
        .Subscribe(i => {
          Debug.WriteLine("bb:" + i);
          Thread.Sleep(1000);
        });
      var isDone = false;
      Observable.Range(0, 100)
        .ObserveOn(Scheduler.ThreadPool)
        .Subscribe(i => {
          bb.Post(i);
          Thread.Sleep(110);
        }, () => isDone = true);
      while (!isDone) { }
      Debug.WriteLine("Waiting for ab.");
      Thread.Sleep(2000);
    }
  }
}
