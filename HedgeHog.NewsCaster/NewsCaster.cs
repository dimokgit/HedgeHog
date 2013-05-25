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
    public static IEnumerable<NewsEvent> Fetch() {
      throw new NotImplementedException();
    }
  }
}
