using HedgeHog.NewsCaster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using HtmlAgilityPack;
using System.Collections.Generic;
using MongoDB.Driver;
using AutoMapper;
using HedgeHog.Alice.Store;
using MongoDB.Bson;
using HedgeHog;

namespace UnitLib {
  [TestClass()]
  public class MongoDBTest {

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


      [TestMethod]
      [TestCategory("Manuals")]
      public void MongoUpdate() {
      Assert.Inconclusive("Manual");
      GlobalStorage.UseForexMongo(c => {
        foreach(var a in c.StraddleHistories)
          a.pair = HedgeHog.Shared.TradesManagerStatic.FutureCode(a.pair);
      }, true);
      return;
    }

    [TestMethod()]
    public void MongoTest() {
      var client = new MongoClient("mongodb://dimok:1Aaaaaaa@ds040017.mlab.com:40017/forex");
      var db = client.GetDatabase("forex");
      var colls = db.ListCollections().ToList();
      colls.ForEach(bd => Console.WriteLine(bd + ""));
      //db.CreateCollection("test");
      var testCollection = GlobalStorage.LoadTradingMacros("PROD 02");
      Assert.IsTrue(testCollection.Length > 0);
      testCollection.ForEach(tm=> Console.WriteLine(tm.ToJson()));
      //testCollection.InsertOne(tradingMacroMapper.Map<TradingMacroSettings>(new HedgeHog.Alice.Store.TradingMacro()));
      //Assert.Inconclusive("Verify the correctness of this test method.");
    }
  }
}
