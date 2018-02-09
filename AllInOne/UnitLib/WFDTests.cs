using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Dynamic;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using HedgeHog.Core;

namespace HedgeHog.Tests {
  [TestClass()]
  public class WFDTests {
    [TestMethod()]
    public void GetTest() {
      var key1 = WFD.Make<int>("key1");
      var key2 = WFD.Make<int>("key2");
      var e = new ExpandoObject();
      dynamic d = e;

      try {
        Assert.AreEqual<int>(0, key1(e));
      } catch(KeyNotFoundException) {
        key1(e, () => 48);
      }
      Assert.AreEqual<int>(48, key1(e));
    }
    [TestMethod]
    public void WorkflowSubject() {
      var wfs = new Subject<int>();
      var b = 0;
      var o = wfs
        .Scan((p: 0, n: 0), (p, n) => ((p.n, n)))
        .Subscribe(x => XOR(x, i => b = i));

      wfs.OnNext(0);
      Assert.AreEqual(0, b);

      wfs.OnNext(1);
      Assert.AreEqual(0, b);

      wfs.OnNext(1);
      Assert.AreEqual(1, b);

      wfs.OnNext(0);
      Assert.AreEqual(1, b);

      wfs.OnNext(0);
      Assert.AreEqual(0, b);

    }

    private static T XOR<T>((T p, T n) x, Func<T, T> a) => a(x.p.Equals(x.n) ? x.n : x.p);
  }
}
