using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Core;

namespace HedgeHog.Tests {
  [TestClass()]
  public class IEnumerableCoreTests {
    [TestMethod()]
    public void GetRangeByDates() {
      var start = DateTime.Parse("1/2/2000");
      var start2 = DateTime.Parse("1/2/1999");
      var end = DateTime.Parse("1/3/2000");
      var end2 = DateTime.Parse("1/30/2000");
      var data = new[] {
        new { d = DateTime.Parse("1/1/2000") },
        new { d = start},
        new { d = end },
        new { d = DateTime.Parse("1/4/2000") }
      }.ToList();
      var range = data.GetRange(start, end, a => a.d);
      Assert.AreEqual(start, range[0].d);
      Assert.AreEqual(end, range[1].d);
      Console.WriteLine(range.ToJson());

      range = data.GetRange(start2, end, a => a.d);
      Console.WriteLine(range.ToJson());
      Assert.AreEqual(data[0].d, range[0].d);
      Assert.AreEqual(end, range.Last().d);

      range = data.GetRange(start, end2, a => a.d);
      Console.WriteLine(range.ToJson());
      Assert.AreEqual(start, range[0].d);
      Assert.AreEqual(data.Last().d, range.Last().d);

      range = data.GetRange(start2, end2, a => a.d);
      Console.WriteLine(range.ToJson());
      Assert.AreEqual(data[0].d, range[0].d);
      Assert.AreEqual(data.Last().d, range.Last().d);
    }

    [TestMethod()]
    public void SingleOrElse() {
      Assert.AreEqual(3, new[] { 1, 2 }.SingleOrElse(() => 3));
      Assert.AreEqual(3, new[] { 3 }.SingleOrElse(() => 3));
    }
  }
}