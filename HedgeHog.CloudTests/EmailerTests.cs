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
      var path = @"C:\Users\dimok\OneDrive\Pictures\Screenshots\2018-10-27.png";
      Emailer.Send(AppSettings.SmsEmailAddress, 
        AppSettings.SmsTradeConfirmation, 
        AppSettings.SmsEmailPassword, 
        "New Trade Alert", 
        "USD/JPY"
        , new[] { Tuple.Create(File.ReadAllBytes(path), "Screen") }
        );
    }

    [TestMethod()]
    public void ReadSinceTest() {
      var search = new Emailer.IMapSearch {
        From = From(),
        Since = DateTimeOffset.Now.Date,
        Subject = "dimokdimon"
      };
      var messages = Emailer.Read(AppSettings.SmsEmailAddress, AppSettings.SmsEmailPassword, "INBOX", search);
      Assert.IsTrue(messages.Count > 0);
    }

    private static string From() => AppSettings.SmsTradeConfirmation.Split(',')[0];

    [TestMethod()]
    public void ReadSubjectTest() {
      var messages = Emailer.Read(AppSettings.SmsEmailAddress, AppSettings.SmsEmailPassword, "INBOX", $"FROM \"{From()}\" SUBJECT \"go\" ");
      Assert.IsTrue(messages.Count > 0);
    }
  }
}
