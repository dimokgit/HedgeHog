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
      double rate = 2.43 / 100;//(Math.Pow(2/100 + 1, 1) - 1);
      double volatility = 17.73 / 100;
      int daysToExp = 2;
      double strike = 2750;
      double dividents = 2.0029 / 100;
      double spot = 2752;
      var p = BlackScholes.PutPrice(spot, strike, rate, dividents, daysToExp, volatility);
      var c = BlackScholes.CallPrice(2752, 2750, 0.0243, 0.020029, 2, 0.1773);
      //var p = BlackScholes.CallPrice(spot, strike, rate, dividents, daysToExp, volatility);
      Assert.AreEqual(c, p);
    }
  }
}