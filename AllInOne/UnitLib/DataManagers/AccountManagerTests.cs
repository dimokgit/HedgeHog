using Microsoft.VisualStudio.TestTools.UnitTesting;
using IBApp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Core;

namespace IBApp.Tests {
  [TestClass()]
  public class AccountManagerTests {
    [TestMethod()]
    public void BreakEvenCall() {
      var isCall = true;
      var pos = new[] { (2750.0, 10.0, isCall), (2775, 15, isCall) };
      var bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2775, bes[0]);
      pos = new[] { (2750.0, 10.0, isCall), (2774.5, 15, isCall) };
      bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2774.75, bes[0]);
      pos = new[] { (2750.0, 10.0, isCall), (2775, 15, isCall) };
      bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2775, bes[0]);
      pos = new[] { (2750.0, 10.0, isCall), (2780, 15, isCall) };
      bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2775, bes[0]);
    }
    [TestMethod()]
    public void BreakEvenPut() {
      var isCall = false;
      var pos = new[] { (2750.0, 10.0, isCall), (2730, 15, isCall) };
      var bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2727.5, bes[0]);
      pos = new[] { (2750.0, 10.0, isCall), (2725.5, 15, isCall) };
      bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2725.25, bes[0]);
      pos = new[] { (2750.0, 10.0, isCall), (2725, 15, isCall) };
      bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2725, bes[0]);
      pos = new[] { (2750.0, 10.0, isCall), (2720, 15, isCall) };
      bes = AccountManager.BreakEvens(pos);
      Console.WriteLine(bes.ToJson());
      Assert.AreEqual(2725, bes[0]);
    }
  }
}