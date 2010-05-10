using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace HedgeHog.Shared {
  [DataContract]
  public class Account {
    [DataMember]
    public string ID { get; set; }
    [DataMember]
    public double Balance { get; set; }
    [DataMember]
    public double Equity { get; set; }
    [DataMember]
    public double UsableMargin { get; set; }
    [DataMember]
    public bool IsMarginCall { get; set; }
    [DataMember]
    public int PipsToMC { get; set; }
    [DataMember]
    public bool Hedging { get; set; }
    public double Gross { get { return Equity - Balance; } }
  }
}
