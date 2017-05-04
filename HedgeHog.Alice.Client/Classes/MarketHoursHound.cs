using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HtmlAgilityPack;
using HAP = HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace HedgeHog.Alice.Client {
  public static class MarketHoursHound {
    static string CleanHtml(string innerText) {
      return Regex.Replace(innerText,@"\s{2,}"," ");
    }
    public static IList<MarketHours> Fetch() {
      try {
        var web = new HAP.HtmlWeb();
        var doc = web.Load("http://forex.timezoneconverter.com/?timezone=America/New_York&refresh=5");
        var trs = doc.DocumentNode.SelectNodes("//body//div[@class='markets']/table/tr");
        return trs?.Skip(1).Aggregate(new List<MarketHours>(), (list, tr) => {
          var i = 0;
          var tds = tr.SelectNodes("td");
          list.Add(new MarketHours {
            Market = CleanHtml(tds[i++].InnerText),
            TimeZone = CleanHtml(tds[i++].InnerText).Split('/').Skip(1).First(),
            Opens = CleanHtml(tds[i++].InnerText).Substring(0, 8),
            Closes = CleanHtml(tds[i++].InnerText).Substring(0, 8),
            Status = CleanHtml(tds[i++].InnerText)
          });
          return list;
        });
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(exc);
        return new MarketHours[0];
      }
      //return new[] { new MarketHours { Market = "Dimok", TimeZone = "Dimon", Opens = "Today", Closes = "Tomorrow", Status = "Who Knows" } };
    }
  }
}
