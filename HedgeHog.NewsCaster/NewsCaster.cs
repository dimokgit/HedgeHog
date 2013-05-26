using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GalaSoft.MvvmLight;
using HAP = HtmlAgilityPack;

namespace HedgeHog.NewsCaster {
  public enum NewsEventType { Report, Speech };
  public class NewsEvent {
    public string Name { get; set; }
    public NewsEventType Type { get; set; }
    public DateTimeOffset Time { get; set; }
  }
  public static class NewsHound {
    public static class Bloomberg {
      private static IEnumerable<IEnumerable<NewsEvent>> Parse(HAP.HtmlDocument doc) {
        var tableTabSpace = doc.DocumentNode.SelectSingleNode("//body//table//tr/td[@class='tabspace']");
        var tableCaNavMon = tableTabSpace.SelectSingleNode("//table[@class='calnavmon']");
        var tableEvents = tableTabSpace.SelectSingleNode("//table[@class='eventstable']");
        var tds = tableEvents.SelectNodes("tr").Skip(1).First().SelectNodes("td");
        var news = tds.Aggregate(new List<IEnumerable<NewsEvent>>(),
        (list0, td) => {
          var ne = td.SelectNodes("div[@class='econoevents']")
            .Aggregate(new List<NewsEvent>(),
              (list1, div) => {
                var n = new NewsEvent() {
                  Name = div.ChildNodes[0].InnerText,
                  Time = DateTime.Parse(div.ChildNodes[1].InnerText.Replace("&#160", "").Split(';')[0])
                };
                list1.Add(n);
                return list1; });
          list0.Add(ne)  ;
          return list0;
        });
        return news;
      }
      public static IEnumerable<IEnumerable<NewsEvent>> Fetch() {
        return NewsHound.Fetch("http://bloomberg.econoday.com/byweek.asp",Parse);
      }
    }
    static IEnumerable<IEnumerable<NewsEvent>> Fetch(string url, Func<HAP.HtmlDocument, IEnumerable<IEnumerable<NewsEvent>>> parser) {
      var doc = new HAP.HtmlWeb().Load(url);
      return parser(doc);
    }
  }
}
