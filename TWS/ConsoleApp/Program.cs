using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
      var dateEnd = DateTime.Parse("2017-03-08 12:00");
      var count = 0;
      new HistoryLoader(ibClient, usdJpi, 1, dateEnd, TimeSpan.FromHours(40), TimeUnit.S, BarSize._1_secs,
         list => {
           HandleMessage(new { list = new { list.Count, first = list.First().Date, last = list.Last().Date } } + "");
         },
         dates => HandleMessage(new { dateStart = dates[0], dateEnd = dates[1], reqCount = ++count } + ""),
         exc => HandleError(exc));

      //load(() => load(() => { }));

      HandleMessage("Press any key ...");
      Console.ReadKey();
      HandleMessage("Disconnecting ...");
      ibClient.ClientSocket.eDisconnect();
      HandleMessage("Disconnected ...");
      Console.ReadKey();
    }

    class HistoryLoader {
      #region Fields
      public const int HISTORICAL_ID_BASE = 30000000;
      private readonly List<HistoricalDataMessage> _list;
      private readonly List<HistoricalDataMessage> _list2;
      private readonly int _reqId;
      private readonly IBClient _ibClient;
      private readonly Contract _contract;
      private readonly DateTime _dateStart;
      private  DateTime _endDate;
      private readonly TimeUnit _timeUnit;
      private readonly BarSize _barSize;
      private readonly TimeSpan _duration;
      private readonly Action<IList<HistoricalDataMessage>> _done;
      private readonly Action<Exception> _error;
      private readonly Action<DateTime?[]> _dataEnd;
      private TimeSpan _delay;
      public bool Done { get; private set; }
      #endregion

      #region ctor
      public HistoryLoader(IBClient ibClient, Contract contract, int currentTicker, DateTime endDate, TimeSpan duration, TimeUnit timeUnit, BarSize barSize, Action<IList<HistoricalDataMessage>> done, Action<DateTime?[]> dataEnd, Action<Exception> error) {
        _ibClient = ibClient;
        _contract = contract;
        _reqId = currentTicker + HISTORICAL_ID_BASE;
        _list = new List<HistoricalDataMessage>();
        _list2 = new List<HistoricalDataMessage>();
        _dateStart = endDate.Subtract(duration);
        _endDate = endDate;
        _timeUnit = timeUnit;
        _barSize = barSize;
        _duration = duration;
        _done = done;
        _dataEnd = dataEnd;
        _error = error;
        _delay = TimeSpan.Zero;
        Done = false;
        Init();
        var me = this;
        RequestHistoryDataChunk();
      }

      void Init() {
        _ibClient.Error += HandlePacer;
        _ibClient.HistoricalData += IbClient_HistoricalData;
        _ibClient.HistoricalDataEnd += IbClient_HistoricalDataEnd;
      }
      void CleanUp() {
        _ibClient.HistoricalData -= IbClient_HistoricalData;
        _ibClient.HistoricalDataEnd -= IbClient_HistoricalDataEnd;
        Done = true;
      }
      #endregion

      #region Event Handlers
      private void HandlePacer(int arg1, int arg2, string arg3, Exception arg4) {
        if(arg2 != 162)
          return;
        _delay += TimeSpan.FromSeconds(2);
        _error(new Exception(new { _delay } + ""));
        RequestNextDataChunk();
      }
      private void IbClient_HistoricalDataEnd(int reqId, string startDateTWS, string endDateTWS) {
        if(reqId != _reqId)
          return;
        _delay = TimeSpan.Zero;
        _list.InsertRange(0, _list2);
        _endDate = _list[0].Date;
        if(_endDate <= _dateStart) {
          CleanUp();
          _done(_list);
        } else {
          _dataEnd(new[] { _list2.FirstOrDefault()?.Date, _list2.LastOrDefault()?.Date });
          var me = this;
          _list2.Clear();
          RequestNextDataChunk();
        }
      }
      private void IbClient_HistoricalData(int reqId, string date, double open, double high, double low, double close, int volume, int count, double WAP, bool hasGaps) {
        if(reqId == _reqId)
          _list2.Add(new HistoricalDataMessage(reqId, date.FromTWSString(), open, high, low, close, volume, count, WAP, hasGaps));
      }
      #endregion

      private void RequestNextDataChunk() {
        Task.Delay(_delay).ContinueWith(t => RequestHistoryDataChunk());
      }


      private void RequestHistoryDataChunk() {
        try {
          string barSizeSetting = (_barSize + "").Replace("_", " ").Trim();
          string whatToShow = "MIDPOINT";
          _ibClient.ClientSocket.reqHistoricalData(_reqId, _contract, _endDate.ToTWSString(), Duration(_barSize, _timeUnit, _duration), barSizeSetting, whatToShow, 0, 1, new List<TagValue>());
        } catch(Exception exc) {
          _error(exc);
          CleanUp();
        }
      }

      static Dictionary<BarSize, Dictionary<TimeUnit, int[]>> BarSizeRanges=new Dictionary<BarSize, Dictionary<TimeUnit, int[]>> {
        [BarSize._1_secs]= new Dictionary<TimeUnit, int[]> {
          [TimeUnit.S] = new[] { 60, 1800 }
        },
        [BarSize._1_min]= new Dictionary<TimeUnit, int[]> {
          [TimeUnit.S] = new[] { 60, 28800 },
          [TimeUnit.M] = new[] { 1, 1 }
        }
      };

      static string Duration(BarSize barSize, TimeUnit timeUnit, TimeSpan timeSpan) {
        var interval = (int)(timeUnit == TimeUnit.S ? timeSpan.TotalSeconds : timeSpan.TotalMinutes);
        var range = BarSizeRanges[barSize][timeUnit];
        var duration = Math.Min(Math.Max(interval, range[0]), range[1]);
        return duration + " " + timeUnit;
      }
    }

    enum TimeUnit { S, D, W, M, Y }
    enum BarSize {
      _1_secs, _5_secs, _15_secs, _30_secs, _1_min, _2_mins,
      _3_mins, _5_mins, _15_mins, _30_mins, _1_hour, _1_day
    }
    //Dictionary<BarSize,int>

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
      Console.WriteLine(DateTime.Now + ": " + message);
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
