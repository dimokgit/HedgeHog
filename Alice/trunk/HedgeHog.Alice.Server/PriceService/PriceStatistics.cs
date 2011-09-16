using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace HedgeHog.Alice.Server {
  [DataContract]
  public class PriceStatistics {
    [DataMember]
    public double BidHighAskLowSpread { get; set; }
  }
}
