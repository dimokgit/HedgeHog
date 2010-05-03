using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace HedgeHog.WCF {
  // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "Service1" in code, svc and config file together.
  public class Trading : ITrading {



    public string GetData(int value) {
      return string.Format("I entered: {0}", value);
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

    #region ITrading Members


    public TradeResponse GetPair(TradeRequest request) {
      //return new TradeResponse() { Pair = request.pair };
      var server = Wcf.FindServer(request.pair);
      if (server != null) return server.Decisioner(request);
      else return null;
    }

    #endregion
  }
}
