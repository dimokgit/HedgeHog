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

    static object getAccountRequest = new object();
    public Account GetAccount() {
      try {
        lock (getAccountRequest) {
          return Wcf.Trader == null ? new Account() : Wcf.Trader.GetAccount();
        }
      } catch (Exception exc) {
        return new Account() { Error = new WiredException(GetType().Name + " error.", exc) };
      }
    }



    public string CloseTrade(string tradeID) {
      return Wcf.Trader == null ? null : Wcf.Trader.CloseTrade(tradeID);
    }

    public string[] CloseTrades(string[] tradeIDs) {
      return Wcf.Trader == null ? null : Wcf.Trader.CloseTrades(tradeIDs);
    }

    public string[] CloseAllTrades() {
      return Wcf.Trader == null ? null : Wcf.Trader.CloseAllTrades();
    }


    public void OpenNewAccount(string account,string password) {
      Wcf.Trader.OpenNewAccount(account, password);
    }

  }
}
