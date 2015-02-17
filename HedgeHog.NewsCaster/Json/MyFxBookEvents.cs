using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace HedgeHog.NewsCaster {
  public class Event {
    public string date { get; set; }
    public DateTime dateUtc { get { return DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc); } }
    public string name { get; set; }
    public string impact { get; set; }
    public string previous { get; set; }
    public string currency { get; set; }
    public string consensus { get; set; }
  }

  public class Events {
    public List<Event> @event { get; set; }
  }

  public class Response {
    public Events events { get; set; }
  }

  public class MyFxBookEvents {
    public Response response { get; set; }
  }
}
