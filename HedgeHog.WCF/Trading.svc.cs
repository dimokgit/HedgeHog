using HedgeHog.Shared;
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

    public TradeResponse GetPair(TradeRequest request) {
      //return new TradeResponse() { Pair = request.pair };
      var server = Wcf.FindServer(request.pair);
      if (server != null) return server.Decisioner(request);
      else return null;
    }

  }
  public static class Wcf {
    public static ITraderServer Trader;
    public interface ITraderServer {
      Account GetAccount();
      void OpenNewAccount(string account, string password);
      string CloseTrade(string tradeID);
      string[] CloseTrades(string[] tradeID);
      string[] CloseAllTrades();
    }

    public static List<IServer> Servers = new List<IServer>();
    public static void RegisterServer(IServer server) {
      Servers.Add(server);
    }
    public static IServer FindServer(string pair) {
      return Servers.FirstOrDefault(s => s.Pair.ToLower() == pair.ToLower());
    }
  }
  public interface IServer {
    string Pair { get; }
    TradeResponse Decisioner(TradeRequest tr);
  }
}
