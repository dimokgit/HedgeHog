using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text;
using HedgeHog.Bars;

namespace HedgeHog.Alice.Server {
  // NOTE: You can use the "Rename" command on the "Refactor" menu to change the interface name "IPriceService" in both code and config file together.
  [ServiceContract]
  public interface IPriceService {
    [OperationContract]
    Rate[] FillPrice(string pair, DateTime startDate);
    [OperationContract]
    PriceStatistics PriceStatistics(string pair);
  }
}
