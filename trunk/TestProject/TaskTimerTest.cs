using HedgeHog.Schedulers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Timers;
using System.Threading;
using HedgeHog;

namespace TestProject
{
    
    
    /// <summary>
    ///This is a test class for TaskTimerTest and is intended
    ///to contain all TaskTimerTest Unit Tests
    ///</summary>
  [TestClass()]
  public class TaskTimerTest {


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
    ///A test for OnException
    ///</summary>
    [TestMethod()]
    [DeploymentItem("HedgeHog.Lib.dll")]
    public void OnExceptionTest() {
      bool exceptionHandled = false;
      EventHandler<ExceptionEventArgs> errorHandler = (s, e) => exceptionHandled = true;
      TaskTimer target = new TaskTimer(1,errorHandler);
      target.Action = () => { throw new Exception(); };
      Thread.Sleep(1000);
      Assert.IsTrue(exceptionHandled,"Exception in TaskTimer was not handled");
    }

    /// <summary>
    ///A test for timer_Elapsed
    ///</summary>
    [TestMethod()]
    [DeploymentItem("HedgeHog.Lib.dll")]
    public void timer_ElapsedTest() {
      TaskTimer target = new TaskTimer(10);
      bool elapsed = false;
      target.Action = () => elapsed = true;
      Thread.Sleep(200);
      Assert.IsTrue(elapsed, "TaskTimer didn't elapse");
    }


    /// <summary>
    ///A test for IsBusy
    ///</summary>
    [TestMethod()]
    public void IsBusyTest() {
      TaskTimer target = new TaskTimer(10);
      int callCount = 0;
      Action increaseCount = () => {
        Thread.Sleep(500);
        callCount++;
      };

      target.Action = () => increaseCount();
      Thread.Sleep(100);
      target.Action = () => increaseCount();
      Thread.Sleep(100);
      target.Action = () => increaseCount();
      Thread.Sleep(1500);
      target.Action = () => increaseCount();
      Thread.Sleep(2000);
      Assert.AreEqual(3, callCount);
    }
  }
}
