using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace UnitLib {
  [TestClass]
  public class UnitTest1 {
    [TestMethod]
    public void TestMethod1() {
      var date = DateTime.Parse("1/1/2013 23:00");
      Debug.WriteLine(date.AddMinutes(1));
    }
  }
}
