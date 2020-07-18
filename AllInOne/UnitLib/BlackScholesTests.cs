using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests {
  [TestClass()]
  public class BlackScholesTests {
    [TestMethod()]
    public void CallPrice() {
      double rate = 0 / 100;//(Math.Pow(2/100 + 1, 1) - 1);
      double volatility = 17.63 / 100;
      int daysToExp = 1;
      double strike = 3215;
      double dividents = 0 / 100;
      double spot = 3215;
      var p = BlackScholes.PutPrice(spot, strike, rate, dividents, daysToExp, volatility);
      var c = BlackScholes.CallPrice(spot, strike, rate, dividents, daysToExp, volatility);
      Console.WriteLine(new { c, p });
      Assert.AreEqual(c, p);
    }
  }
}