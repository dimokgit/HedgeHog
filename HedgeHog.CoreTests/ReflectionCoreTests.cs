using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Tests {
  [TestClass()]
  public class ReflectionCoreTests {
    [TestMethod()]
    public void ValueTupleFields() {
      var t = (name: "dimok", age: 42);
      var f = ToFunc(() => t);
      Console.WriteLine(f());
      Func<T> ToFunc<T>(Func<T> anon) => anon;
    }

    [TestMethod()]
    public void RoundBySeconds() {
      Console.WriteLine(DateTime.Now.RoundBySeconds(7));
    }
  }
}