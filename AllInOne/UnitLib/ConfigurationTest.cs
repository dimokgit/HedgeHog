using System;
using System.Configuration;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitLib {
  [TestClass]
  public class ConfigurationTest {
    static ConfigurationTest() {
      HedgeHog.ConfigGlobal.DoRunTest = false;
    }
    [TestMethod]
    public void Config() {
      Assert.AreEqual("*******", AppSettings.Secret);
      Assert.AreEqual(48, AppSettings.SecretCount);
    }
    [TestMethod]
    [ExpectedException(typeof(AppSettings.MissingKeyException))]
    public void ConfigMissing() {
      Assert.AreEqual(48, AppSettings.SecretRequiredMissing);
    }
  }
}
