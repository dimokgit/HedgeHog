using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
namespace HedgeHog.Tests {
  [TestClass()]
  public class SpeedTests {
    [TestMethod()]
    public void HeightByRegressoinTest() {
      var rand = new Random();
      var range = Enumerable.Range(0, 200);
      var times = new List<double>();
      var values = new List<double>();
      range
        .Do(i => values.Add(rand.NextDouble()))
        //.SkipWhile(_=>values.Count < 5000)
        .ForEach(_ => {
          times.Add(values.HeightByRegressoin());
        });
      times = times.Zip(times.Skip(1), (t1, t2) => t1.Ratio(t2)).Where(Lib.IsNotNaN).ToList();
      times = times.Zip(times.Skip(1), (t1, t2) => t1.Ratio(t2)).Where(Lib.IsNotNaN).Select(d=>1/d).ToList();
      var reg = times.Linear();
      Console.WriteLine(string.Join(",", reg));
      //Console.WriteLine(times.Csv());
    }
  }
}
