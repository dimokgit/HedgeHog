using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using HedgeHog.Shared;

namespace HedgeHog.Alice.WCF {
  // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "Service1" in code, svc and config file together.
  public class TraderService : ITraderService {
    public string GetData(int value) {
      return string.Format("You entered: {0}", value);
    }

    public CompositeType GetDataUsingDataContract(CompositeType composite) {
      if (composite == null) {
        throw new ArgumentNullException("composite");
      }
      if (composite.BoolValue) {
        composite.StringValue += "Suffix";
      }
      return composite;
    }

    public AccountInfo GetTrades() {
      return new AccountInfo() { Account = Wcf.Trader.GetAccount(), Trades = Wcf.Trader.GetTrades() };
    }

  }
}
