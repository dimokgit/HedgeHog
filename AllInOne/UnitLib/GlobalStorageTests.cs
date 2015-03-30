using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Alice.Store;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog.Bars;
using System.Diagnostics;
using System.Reflection;
namespace HedgeHog.Alice.Store.Tests {
  [TestClass()]
  public class GlobalStorageTests {
    [TestMethod()]
    public void TicksPerSecond() {
      var ticks = GlobalStorage.GetRateFromDBBackwards<Rate>("usd/jpy", DateTime.Parse("2/2/15"), 8000, 0);
      var tps =ticks.CalcTicksPerSecond();
      var tps2 = ticks.CalcTicksPerSecond(1000);
      Assert.AreEqual(1.0, tps.Ratio(tps2).Round(3));
    }
    [TestMethod()]
    public void TicksPerSecondWithGap() {
      var ticks = GlobalStorage.GetRateFromDBBackwards<Rate>("usd/jpy", DateTime.Parse("2/2/15"), 80000, 0);
      var tps = ticks.CalcTicksPerSecond();
      var tps2 = ticks.CalcTicksPerSecond(1000);
      Assert.AreEqual(1.0, tps.Ratio(tps2).Round(3));
    }
  }
}
