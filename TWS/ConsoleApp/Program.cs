using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IBApi;
using IBApp;

namespace ConsoleApp {
  class Program {
    static void Main(string[] args) {
      int _nextValidId = 0;
      var signal = new EReaderMonitorSignal();
      var ibClient = new IBClient(signal);
      ibClient.Error += HandleError;
      ibClient.NextValidId += id => _nextValidId = id;
      ibClient.CurrentTime += time => HandleMessage("Current Time: " + time + "\n");
      ibClient.HistoricalData += (reqId, date, open, high, low, close, volume, count, WAP, hasGaps) =>
        HandleMessage(new HistoricalDataMessage(reqId, date, open, high, low, close, volume, count, WAP, hasGaps));
      ibClient.HistoricalDataEnd += (reqId, startDate, endDate) =>
        HandleMessage(new HistoricalDataEndMessage(reqId, startDate, endDate));

      Connect(ibClient, signal, 7497, "", 2);

      HandleMessage("Press any key ...");
      Console.ReadKey();
      HandleMessage("Disconnecting ...");
      ibClient.ClientSocket.eDisconnect();
      HandleMessage("Disconnected ...");
      Console.ReadKey();
    }


    private static void Connect(IBClient ibClient,EReaderSignal signal,int port,string host,int clientId) {

        if(host == null || host.Equals(""))
          host = "127.0.0.1";
        try {
          ibClient.ClientId = clientId;
          ibClient.ClientSocket.eConnect(host, port, ibClient.ClientId);

        var reader = new EReader(ibClient.ClientSocket, signal);

          reader.Start();

          new Thread(() => { while(ibClient.ClientSocket.IsConnected()) { signal.waitForSignal(); reader.processMsgs(); } }) { IsBackground = true }.Start();
        } catch(Exception) {
          HandleMessage(new ErrorMessage(-1, -1, "Please check your connection attributes.")+"");
        }
    }


    static void HandleError(int id, int errorCode, string str, Exception ex) {
      if(ex != null)
        Console.Error.WriteLine("Error: " + ex);
      else if(id == 0 || errorCode == 0)
        Console.Error.WriteLine("Error: " + str + "\n");
      else
        Console.Error.WriteLine(new ErrorMessage(id, errorCode, str));
    }


    private static void HandleMessage(string message) {
      Console.WriteLine(message);
    }
    private static void HandleMessage(HistoricalDataMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
    private static void HandleMessage(HistoricalDataEndMessage historicalDataEndMessage) {
      HandleMessage(historicalDataEndMessage + "");
    }
  }
}
