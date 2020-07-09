using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public class StraddleHistory {
    [Key]
    public long _id { get; set; }
    public string pair { get; set; }
    public Double bid { get; set; }
    public Double ask { get; set; }
    public Double delta { get; set; }
    public Double theta { get; set; }
    public DateTime time { get; set; }

    public StraddleHistory(long id, string pair,double bid,double ask,double delta,DateTime time,double  theta) {
      this._id = id;
      this.pair = pair;
      this.bid = bid;
      this.ask = ask;
      this.delta = delta;
      this.time = time;
      this.theta = theta;
    }
  }
}
