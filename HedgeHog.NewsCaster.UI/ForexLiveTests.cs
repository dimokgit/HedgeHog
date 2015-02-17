using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
namespace HedgeHog.NewsCaster {
  [TestClass()]
  public class ForexLiveTests {
    [TestMethod()]
    public void FetchTest() {
      Console.WriteLine(
        JsonConvert.SerializeObject(
        NewsHound.MyFxBook.Fetch(), Formatting.Indented));
    }
  }
}
