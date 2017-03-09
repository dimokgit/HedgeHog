using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

      //ibClient.HistoricalData += (reqId, date, open, high, low, close, volume, count, WAP, hasGaps) => HandleMessage(new HistoricalDataMessage(reqId, date, open, high, low, close, volume, count, WAP, hasGaps));
      //ibClient.HistoricalDataEnd += (reqId, startDate, endDate) => HandleMessage(new HistoricalDataEndMessage(reqId, startDate, endDate));

      var usdJpi = ContractSamples.FxContract("usd/jpy");
      Connect(ibClient, signal, 4001, "", 2);
      Action<Action> load = a =>
       new HistoryLoader(ibClient, usdJpi, 1, DateTime.Now, TimeSpan.FromHours(3), 1800, TimeInit.S, BarSize._1_secs,
         list => {
           HandleMessage(new { list = new { list.Count, last = list.Last() } } + "");
           a();
         },
         exc => HandleError(exc));

      //load(() => load(() => { }));
      load(() => { });

      HandleMessage("Press any key ...");
      Console.ReadKey();
      HandleMessage("Disconnecting ...");
      ibClient.ClientSocket.eDisconnect();
      HandleMessage("Disconnected ...");
      Console.ReadKey();
    }

    struct HistoryLoader {
      #region Fields
      public const int HISTORICAL_ID_BASE = 30000000;
      private readonly List<HistoricalDataMessage> _list;
      private readonly List<HistoricalDataMessage> _list2;
      private readonly int _reqId;
      private readonly IBClient _ibClient;
      private readonly Contract _contract;
      private readonly DateTime _dateStart;
      private readonly TimeInit _timeUnit;
      private readonly BarSize _barSize;
      private readonly Action<IList<HistoricalDataMessage>> _done;
      private readonly int _chunkSize;
      private readonly Action<Exception> _error;
      #endregion

      public HistoryLoader(IBClient ibClient, Contract contract, int currentTicker, DateTime endDate, TimeSpan duration, int chunkSize, TimeInit timeUnit, BarSize barSize, Action<IList<HistoricalDataMessage>> done, Action<Exception> error) {
        _ibClient = ibClient;
        _contract = contract;
        _reqId = currentTicker + HISTORICAL_ID_BASE;
        _list = new List<HistoricalDataMessage>();
        _list2 = new List<HistoricalDataMessage>();
        _dateStart = endDate.Subtract(duration);
        _timeUnit = timeUnit;
        _barSize = barSize;
        _chunkSize = chunkSize;
        _done = done;
        _error = error;
        Init();
        RequestHistoryDataChunk(_ibClient, _contract, _reqId, endDate, _chunkSize, _timeUnit, _barSize);
      }

      void Init() {
        _ibClient.HistoricalData += IbClient_HistoricalData;
        _ibClient.HistoricalDataEnd += IbClient_HistoricalDataEnd;
      }
      void CleanUp() {
        _ibClient.HistoricalData -= IbClient_HistoricalData;
        _ibClient.HistoricalDataEnd -= IbClient_HistoricalDataEnd;
      }
      private void IbClient_HistoricalDataEnd(int reqId, string startDateTWS, string endDateTWS) {
        if(reqId != _reqId)
          return;
        _list.InsertRange(0, _list2);
        _list2.Clear();
        var dateStart = startDateTWS.FromTWSString();
        if(dateStart <= _dateStart) {
          CleanUp();
          _done(_list);
        } else
          RequestHistoryDataChunk(_ibClient, _contract, _reqId, dateStart, _chunkSize, _timeUnit, _barSize);
      }


      private void IbClient_HistoricalData(int reqId, string date, double open, double high, double low, double close, int volume, int count, double WAP, bool hasGaps) {
        if(reqId == _reqId)
          _list2.Add(new HistoricalDataMessage(reqId, date.FromTWSString(), open, high, low, close, volume, count, WAP, hasGaps));
      }

      private void RequestHistoryDataChunk(IBClient ibClient, Contract contract, int currentTicker, DateTime endDateTime, int duration, TimeInit timeUnit, BarSize barSize) {
        try {
          string barSizeSetting = (barSize + "").Replace("_", " ").Trim();
          string whatToShow = "MIDPOINT";
          var endTimeWTS = endDateTime.ToTWSString().FromTWSString();
          ibClient.ClientSocket.reqHistoricalData(currentTicker, contract, endDateTime.ToTWSString(), duration + "", barSizeSetting, whatToShow, 0, 1, new List<TagValue>());
        } catch(Exception exc) {
          _error(exc);
          CleanUp();
        }
      }
    }

    enum TimeInit { S, D, W, M, Y }
    enum BarSize {
      _1_secs,
      _5_secs,
      _15_secs,
      _30_secs,
      _1_min,
      _2_mins,
      _3_mins,
      _5_mins,
      _15_mins,
      _30_mins,
      _1_hour,
      _1_day
    }


    private static void Connect(IBClient ibClient, EReaderSignal signal, int port, string host, int clientId) {

      if(host == null || host.Equals(""))
        host = "127.0.0.1";
      try {
        ibClient.ClientId = clientId;
        ibClient.ClientSocket.eConnect(host, port, ibClient.ClientId);

        var reader = new EReader(ibClient.ClientSocket, signal);

        reader.Start();

        new Thread(() => { while(ibClient.ClientSocket.IsConnected()) { signal.waitForSignal(); reader.processMsgs(); } }) { IsBackground = true }.Start();
      } catch(Exception) {
        HandleMessage(new ErrorMessage(-1, -1, "Please check your connection attributes.") + "");
      }
    }


    static void HandleError(Exception ex) {
      HandleError(0, 0, "", ex);
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
public static class IBAppExtensions {
  public static string ToTWSString(this DateTime date) {
    return date.ToString("yyyyMMdd HH:mm:ss");
  }
  public static DateTime FromTWSString(this string dateTime) {
    var date = Regex.Split(dateTime, @"\s+")[0];
    var time = Regex.Split(dateTime, @"\s+")[1];
    return date.ToDateTime("yyyyMMdd", DateTimeKind.Local) +
      time.ToDateTime("HH:mm:ss", DateTimeKind.Local).TimeOfDay;
  }
  static DateTime ToDateTime(this string dateTimeString, string dateTimeFormat, DateTimeKind dateTimeKind) {
    if(string.IsNullOrEmpty(dateTimeString)) {
      return DateTime.MinValue;
    }

    return DateTime.SpecifyKind(DateTime.ParseExact(dateTimeString, dateTimeFormat, CultureInfo.InvariantCulture), dateTimeKind);
  }
}
