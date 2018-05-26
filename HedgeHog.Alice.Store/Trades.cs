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
    public Double bid { get; set; }
    public Double ask { get; set; }
    public Double delta { get; set; }
    public DateTime time { get; set; }
  }
}
