using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IBApi;

namespace IBApp {
  public enum TimeUnit { S, D, W, M, Y }
  public enum BarSize {
    _1_secs, _5_secs, _15_secs, _30_secs, _1_min, _2_mins,
    _3_mins, _5_mins, _15_mins, _30_mins, _1_hour, _1_day
  }
  #region HistoryLoader
  public class HistoryLoader<T> {
    private static int _currentTicker=0;
    #region Fields
    public const int HISTORICAL_ID_BASE = 30000000;
    private readonly List<T> _list;
    private readonly List<T> _list2;
    private readonly int _reqId;
    private readonly IBClient _ibClient;
    private readonly Contract _contract;
    private readonly DateTime _dateStart;
    private  DateTime _endDate;
    private readonly TimeUnit _timeUnit;
    private readonly BarSize _barSize;
    private readonly TimeSpan _duration;
    private readonly Action<IList<T>> _done;
    private readonly Action<Exception> _error;
    private readonly Action<IList<T>> _dataEnd;
    private TimeSpan _delay;
    private readonly DataMapDelegate<T> _map;
    public bool Done { get; private set; }
    #endregion

    #region ctor
    public delegate T DataMapDelegate<T>(DateTime date, double open, double high, double low, double close, int volume, int count);
    public HistoryLoader(IBClient ibClient
      , Contract contract
      , DateTime endDate
      , TimeSpan duration
      , TimeUnit timeUnit
      , BarSize barSize
      ,DataMapDelegate<T> map
      , Action<IList<T>> done
      , Action<IList<T>> dataEnd
      , Action<Exception> error) {
      _ibClient = ibClient;
      _contract = contract;
      _reqId = (++_currentTicker) + HISTORICAL_ID_BASE;
      _list = new List<T>();
      _list2 = new List<T>();
      if(endDate.Kind == DateTimeKind.Unspecified)
        throw new Exception(new { endDate = new { endDate.Kind } } + "");
      _dateStart = endDate.Subtract(duration);
      _endDate = endDate.ToLocalTime();
      _timeUnit = timeUnit;
      _barSize = barSize;
      _duration = duration;
      _done = done;
      _dataEnd = dataEnd;
      _error = error;
      _delay = TimeSpan.Zero;
      _map = map;
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
      _ibClient.Error -= HandlePacer;
      _ibClient.HistoricalData -= IbClient_HistoricalData;
      _ibClient.HistoricalDataEnd -= IbClient_HistoricalDataEnd;
      Done = true;
    }
    #endregion

    #region Event Handlers
    private void HandlePacer(int reqId, int code, string error, Exception exc) {
      if(code == 162 && error.Contains("pacing violation")) {
        _delay += TimeSpan.FromSeconds(2);
        _error(new Exception(new { _delay } + ""));
        RequestNextDataChunk();
      } else if(reqId < 0 && exc == null) {
        _error(exc ?? new Exception(new { reqId, code, error } + ""));
      } else {
        CleanUp();
        _error(exc ?? new Exception(new { reqId, code, error } + ""));
      }
    }
    private void IbClient_HistoricalDataEnd(int reqId, string startDateTWS, string endDateTWS) {
      if(reqId != _reqId)
        return;
      _delay = TimeSpan.Zero;
      _list.InsertRange(0, _list2);
      if(_endDate <= _dateStart) {
        CleanUp();
        _done(_list);
      } else {
        _dataEnd(_list);
        var me = this;
        _list2.Clear();
        RequestNextDataChunk();
      }
    }
    private void IbClient_HistoricalData(int reqId, string date, double open, double high, double low, double close, int volume, int count, double WAP, bool hasGaps) {
      if(reqId == _reqId) {
        var date2 = date.FromTWSString();
        if(date2 < _endDate)
          _endDate = date2;
        _list2.Add(_map( date2, open, high, low, close, volume, count));
      }
    }
    #endregion

    #region Request Data
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
    #endregion

    #region Duration Helpers
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
    #endregion
  }
  #endregion
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
}
