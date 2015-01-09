using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Cloud;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
namespace HedgeHog.Cloud.Tests {
  [TestClass()]
  public class EmailerTests {
    [TestMethod()]
    public void SendTest() {
      var path = @"C:\Users\dimon\OneDrive\Public\Forex\Patterns\Archer.png";
      Emailer.Send("dimokdimon@gmail.com", 
        "13057880763@mymetropcs.com", 
        "1Aaaaaaa", 
        "New Trade Alert", 
        "USD/JPY"
        , new[] { Tuple.Create(File.ReadAllBytes(path), "Screen") }
        );
    }

    [TestMethod()]
    public void ReadSinceTest() {
      var search = new Emailer.IMapSearch { 
        From = "13057880763@mymetropcs.com" ,
        Since = DateTimeOffset.Now.Date,
        Subject ="dimokdimon"
      };
      var messages = Emailer.Read("dimokdimon", "1Aaaaaaa", "INBOX", search);
      Assert.IsTrue(messages.Count > 0);
    }
    [TestMethod()]
    public void ReadSubjectTest() {
      var messages = Emailer.Read("dimokdimon", "1Aaaaaaa", "INBOX", "FROM \"13057880763@mymetropcs.com\" SUBJECT \"go\" ");
      Assert.IsTrue(messages.Count > 0);
    }
  }
}
