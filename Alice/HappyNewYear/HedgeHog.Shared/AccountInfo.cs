using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace HedgeHog.Shared {
  [DataContract]
  public class AccountInfo {
    [DataMember]
    public Account Account { get; set; }
    [DataMember]
    public Trade[] Trades { get; set; }
  }
}
