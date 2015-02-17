using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using HAP = HtmlAgilityPack;
using HedgeHog;
using System.Reactive.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.IO;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace HedgeHog.NewsCaster {
  public enum NewsEventType { Report, Speech };
  public enum NewsEventLevel { L, M, H } ;
  public class NewsEvent : Models.ModelBase {
    public string Country { get; set; }
    public string Name { get; set; }
    public NewsEventType Type { get; set; }
    public DateTimeOffset Time { get; set; }
    public NewsEventLevel Level { get; set; }
    public override string ToString() {
      return new { Country, Name, Time, Level, Type }.ToString();
    }
  }
  public class NewsParserException : Exception {
    public NewsParserException(object message) : base(message + "") { }
  }
  public class NewsHoundException : Exception {
    public NewsHoundException(object message, Exception inner = null) : base(message + "", inner) { }
  }
  static class Extensions {
    public static HAP.HtmlNode SelectNode(this HAP.HtmlNode parent, string xPath) {
      var node = parent.SelectSingleNode(xPath);
      if (node == null) throw new NewsParserException(new { xPath });
      return node;
    }
    public static HAP.HtmlNodeCollection SelectCollection(this HAP.HtmlNode parent, string xPath, bool doThrow) {
      var node = parent.SelectNodes(xPath);
      if (doThrow && node == null) throw new NewsParserException(new { xPath });
      return node ?? new HAP.HtmlNodeCollection(parent);
    }
    public static string Decode(this string text) { return HAP.HtmlEntity.DeEntitize(text).Trim(); }
  }
  public static class NewsHound {
    public class MyFxBook {
      private static FetchParam MakeUrl(DateTime date) {
        Func<DateTime, string> df = d => d.ToString("yyyy-MM-dd");
        date = date.AddDays((int)DayOfWeek.Monday - (int)date.DayOfWeek);
        var dateEnd = date.AddDays(7);
        return new FetchParam {
          Url = "http://www.myfxbook.com/calendar_statement.xml?filter=2-3_JPY-CNY-CAD-AUD-NZD-CLP-GBP-CHF-EUR-USD",
          Date = date
        };
      }
      static IEnumerable<NewsEvent> ParseWeek(MyFxBookEvents events) {
        return from evt in events.response.events.@event
               select new NewsEvent() {
                 Country = evt.currency,
                 Level = (NewsEventLevel)Enum.Parse(typeof(NewsEventLevel), evt.impact[0] + ""),
                 Name = evt.name,
                 Time = evt.dateUtc
               };
      }
      public static IEnumerable<NewsEvent> Fetch() {
        var xml = XDocument.Load("http://www.myfxbook.com/calendar_statement.xml?filter=2-3_JPY-CNY-CAD-AUD-NZD-CLP-GBP-CHF-EUR-USD");
        var json = JsonConvert.SerializeObject(xml);
        var o = JsonConvert.DeserializeObject<MyFxBookEvents>(json);
        return ParseWeek(o);
      }
    }
    public static class EconoDay {
      static DateTime ParseEventDate(string date) {
        DateTime d;
        if (!DateTime.TryParse(date.Substring(0, date.Length - 3), out d))
          throw new NewsParserException(new { date });
        return d;
      }
      static DateTimeOffset ParseWeekDate(string date) {
        DateTimeOffset d;
        if (!DateTimeOffset.TryParse(date, out d))
          throw new NewsParserException(new { date });
        return d;
      }
      static string starXPath(string text) { return ".//img[@alt='[" + text + "]']"; }

      private static IEnumerable<IEnumerable<NewsEvent>> ParseWeek(HAP.HtmlDocument doc, DateTime dateStart) {
        var tableTabSpace = doc.DocumentNode.SelectSingleNode("//body//table//tr/td[@class='tabspace']");
        var tableCaNavMon = tableTabSpace.SelectNode("//table[@class='calnavmon']");
        var tableEvents = tableTabSpace.SelectNode("//table[@class='eventstable']");
        var trWeekDay = tableEvents.SelectNode("tr/td[./@class='navwkday' or ./@class='currentnavwkday']/..");
        var tdsWeekDay = trWeekDay.SelectCollection("td", true);

        var weekDay = ParseWeekDate(tdsWeekDay[0].InnerText.Decode() + " " + dateStart.Year);
        var weekDays = new List<DateTimeOffset>(Enumerable.Range(0, 5).Select(i => weekDay.AddDays(i)));
        weekDays.Add(weekDays.Last().AddDays(2));

        var trEvents = tableEvents.SelectNode("tr/td[@class='events']/..");
        var tds = trEvents.SelectNodes("td");
        var news = tds.Take(5).Aggregate(new List<IEnumerable<NewsEvent>>(),
        (list0, td) => {
          var ne = ParseEventTD(weekDays, list0.Count, td);
          list0.Add(ne);
          return list0;
        });
        var tdEventsFri = tableEvents.SelectCollection("tr/td[@class='eventsfri']/..", true).Last().Element("td");
        news.Add(ParseEventTD(weekDays, news.Count, tdEventsFri));
        return news.Aggregate(new List<IEnumerable<NewsEvent>>(),
          (list, events) => {
            list.Add(events.Where(evt => evt.Level != NewsEventLevel.L));
            return list;
          });
      }
      //      private static IEnumerable<IEnumerable<NewsEvent>> ParseDay(HAP.HtmlDocument doc, DateTime date) {
      //        var trsEvents = doc.DocumentNode.SelectNodes("//tr@class='dailyeventtext']");
      //        trsEvents.Aggregate(
      //          new List<NewsEvent>(),
      //          (list, tr) => {
      //            var tds = tr.SelectNodes("td");
      //            var time = date.Date.Add(ParseEventDate(tds[0].InnerText.Decode()).TimeOfDay);
      //            var name = tds[2].InnerText.Decode().Trim();
      //            var country = name.Split(':').FirstOrDefault();
      //            if (string.IsNullOrWhiteSpace(country)) country = "ALL";
      //            else name = name.Substring(country.Length + 1);

      //            list.Add(new NewsEvent { 
      //            });
      //            return list;
      //          }
      //          );
      ////        var newsEvent = new NewsEvent();

      //        var weekDay = ParseWeekDate(tdsWeekDay[0].InnerText.Decode() + " " + date.Year);
      //        var weekDays = new List<DateTimeOffset>(Enumerable.Range(0, 5).Select(i => weekDay.AddDays(i)));
      //        weekDays.Add(weekDays.Last().AddDays(2));

      //        var trEvents = tableEvents.SelectNode("tr/td[@class='events']/..");
      //        var tds = trEvents.SelectNodes("td");
      //        var news = tds.Take(5).Aggregate(new List<IEnumerable<NewsEvent>>(),
      //        (list0, td) => {
      //          var ne = ParseEventTD(weekDays, list0.Count, td);
      //          list0.Add(ne);
      //          return list0;
      //        });
      //        var tdEventsFri = tableEvents.SelectCollection("tr/td[@class='eventsfri']/..", true).Last().Element("td");
      //        news.Add(ParseEventTD(weekDays, news.Count, tdEventsFri));
      //        return news.Aggregate(new List<IEnumerable<NewsEvent>>(),
      //          (list, events) => {
      //            list.Add(events.Where(evt => evt.Level != NewsEventLevel.Low));
      //            return list;
      //          });
      //      }

      private static List<NewsEvent> ParseEventTD(List<DateTimeOffset> weekDays, int columnIndex, HAP.HtmlNode td) {
        var ne = td.SelectCollection("div[@class='econoevents']", false)
          .Aggregate(new List<NewsEvent>(),
            (list1, div) => {
              ParseEventDiv(weekDays, columnIndex, list1, div);
              return list1;
            });
        return ne;
      }

      private static void ParseEventDiv(List<DateTimeOffset> weekDays, int columnIndex, List<NewsEvent> list1, HAP.HtmlNode div) {
        var name = div.SelectNode("a").InnerText.Trim();
        var country = name.Split(':').FirstOrDefault();
        if (string.IsNullOrWhiteSpace(country))
          country = "ALL";
        else
          name = name.Substring(country.Length + 1);
        var childNodes = div.ChildNodes;
        var dates = (from node in childNodes
                     where node.NodeType == HAP.HtmlNodeType.Text
                     select node.InnerText.Decode()).ToArray();
        if (!dates.Any())
          throw new NewsParserException("No text nodes found in column " + (columnIndex + 1) + " for " + name);

        var date = dates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (date == null) return;
        //throw new NewsParserException("No dates found in column " + (columnIndex + 1) + " for " + name);

        var level = div.SelectSingleNode(starXPath("Star")) != null
          ? NewsEventLevel.H
          : div.SelectSingleNode(starXPath("djStar")) != null ? NewsEventLevel.M
          : NewsEventLevel.L;
        var n = new NewsEvent() {
          Country = country,
          Name = name,
          Time = weekDays[columnIndex].Add(ParseEventDate(date).TimeOfDay),
          Type = name.Contains("Speaks") ? NewsEventType.Speech : NewsEventType.Report,
          Level = level
        };
        list1.Add(n);
      }

      public static IObservable<IEnumerable<NewsEvent>> Fetch(params DateTime[] dates) {
        return NewsHound.Fetch(ParseWeek, dates.Select(MakeUrl).ToArray());
      }

      private static FetchParam MakeUrl(DateTime date) {
        date = date.AddDays((int)DayOfWeek.Monday - (int)date.DayOfWeek);
        var dateQuery = string.Format("day={0}&month={1}&year={2}", date.Day, date.Month, date.Year);
        return new FetchParam {
          Url = "http://global.econoday.com/byweek.asp?" + dateQuery + "&cust=global-premium&lid=0",//"http://global.econoday.com/byweek.asp?day=30&month=5&year=2013&cust=global-premium&lid=0"
          Date = date
        };
      }

    }
    public class FetchParam {
      public DateTime Date { get; set; }
      public string Url { get; set; }
    }
    static IObservable<IEnumerable<NewsEvent>> Fetch(Func<HAP.HtmlDocument, DateTime, IEnumerable<IEnumerable<NewsEvent>>> parser, params FetchParam[] urls) {
      var hw = new HAP.HtmlWeb() { PreRequest = (hr) => { hr.Timeout = 150 * 1000; return true; } };
      return urls.ToObservable(NewThreadScheduler.Default).Select(url => {
        try {
          return parser(hw.Load(url.Url), url.Date).SelectMany(evt => evt);
        } catch (Exception exc) {
          throw new NewsHoundException(new { url }, exc);
        }
      });
    }
  }
}
