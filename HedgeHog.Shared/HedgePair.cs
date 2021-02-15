using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Shared {
  public class HedgePair {
    public HedgePair() { }
    [Key]
    public ObjectId _id { get; set; }
    public string hedge1 { get; set; }
    public string hedge2 { get; set; }
    public string prime { get; set; }
  }
}
