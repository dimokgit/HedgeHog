using HedgeHog.DB;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnitLib
{
    
    
    /// <summary>
    ///This is a test class for t_TradeTest and is intended
    ///to contain all t_TradeTest Unit Tests
    ///</summary>
  [TestClass()]
  public class t_TradeTest {


    private TestContext testContextInstance;

    /// <summary>
    ///Gets or sets the test context which provides
    ///information about and functionality for the current test run.
    ///</summary>
    public TestContext TestContext {
      get {
        return testContextInstance;
      }
      set {
        testContextInstance = value;
      }
    }

    #region Additional test attributes
    // 
    //You can use the following additional attributes as you write your tests:
    //
    //Use ClassInitialize to run code before running the first test in the class
    //[ClassInitialize()]
    //public static void MyClassInitialize(TestContext testContext)
    //{
    //}
    //
    //Use ClassCleanup to run code after all tests in a class have run
    //[ClassCleanup()]
    //public static void MyClassCleanup()
    //{
    //}
    //
    //Use TestInitialize to run code before running each test
    //[TestInitialize()]
    //public void MyTestInitialize()
    //{
    //}
    //
    //Use TestCleanup to run code after each test has run
    //[TestCleanup()]
    //public void MyTestCleanup()
    //{
    //}
    //
    #endregion


    /// <summary>
    ///A test for SessionInfo
    ///</summary>
    [TestMethod()]
    public void SessionInfoTest() {
      var re = new Regex(@":(\d\d):(\d\d)-(\d\d):(\d\d)", RegexOptions.Compiled);
      ForexStorage.UseForexContext(c => {
        c.t_Trade.ToList().ForEach(t => {
          if (re.IsMatch(t.SessionInfo)) {
            var session = re.Replace(t.SessionInfo, ":$1|$2-$3|$4");
            session = session.Replace(":", "\t").Replace("|", ":");
            t.SessionInfo = session;
          }
        });
      }, null, c => c.SaveChanges(), 60 * 10);
      TestContext.WriteLine("Done");
    }
  }
}
