﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IBApi;
using HedgeHog;
using HedgeHog.Shared;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Threading;

namespace IBApp {
  public enum TimeUnit { S, D, W, M, Y }
  public enum BarSize {
    _1_secs, _5_secs, _15_secs, _30_secs, _1_min, _2_mins,
    _3_mins, _5_mins, _15_mins, _30_mins, _1_hour, _1_day
  }
  public class DelayException :SoftException {
    public DelayException(TimeSpan delay) : base(new { delay } + "") { }
  }
  #region HistoryLoader
  public class HistoryLoader<T> where T : HedgeHog.Bars.BarBaseDate {
    private static int _currentTicker = 0;
    private static EventLoopScheduler historyLoaderScheduler = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "HistoryLoader", Priority = ThreadPriority.Normal });
    #region Fields
    public const int HISTORICAL_ID_BASE = 30000000;
    private const int IBERROR_NOT_CONNECTED = 504;
    private readonly List<T> _list;
    private readonly List<T> _list2;
    private readonly int _reqId;
    private readonly IBClient _ibClient;
    private readonly Contract _contract;
    private readonly DateTime _dateStart;
    private DateTime _endDate;
    private readonly TimeUnit _timeUnit;
    private readonly BarSize _barSize;
    private readonly TimeSpan _duration;
    private readonly Action<IList<T>> _done;
    private readonly Action<Exception> _error;
    private readonly Action<IList<T>> _dataEnd;
    private TimeSpan _delay;
    private readonly DataMapDelegate<T> _map;
    public bool Done { get; private set; }
    List<IDisposable> _disposables = new List<IDisposable>();
    #endregion

    #region ctor
    public delegate TDM DataMapDelegate<TDM>(DateTime date, double open, double high, double low, double close, long volume, int count);
    public HistoryLoader(IBClientCore ibClient
      , Contract contract
      , int periodsBack
      , DateTime endDate
      , TimeSpan duration
      , TimeUnit timeUnit
      , BarSize barSize
      , DataMapDelegate<T> map
      , Action<IList<T>> done
      , Action<IList<T>> dataEnd
      , Action<Exception> error) {
      _ibClient = ibClient;
      _contract = contract;
      if(_contract.Exchange.IsNullOrEmpty())
        _contract = ibClient.ReqContractDetailsCached(contract).ToEnumerable().ToArray().Select(cd => cd.Contract.ContractFactory())
          .Count(1, new { HistoryLoader = new { _contract } })
          .Single();
      _periodsBack = periodsBack;
      _reqId = IBClientCore.IBClientCoreMaster.ValidOrderId();
      _list = new List<T>();
      _list2 = new List<T>();
      if(endDate.Kind == DateTimeKind.Unspecified)
        throw new Exception(new { endDate = new { endDate.Kind } } + "");
      _dateStart = endDate.Subtract(duration);
      _endDate = endDate.ToLocalTime();
      _timeUnit = timeUnit;
      _barSize = barSize;
      var durationByPeriod = barSize.Span().Multiply(periodsBack);
      _duration = duration.Max(durationByPeriod);
      _done = done;
      _dataEnd = dataEnd;
      _error = error;
      _delay = TimeSpan.Zero;
      _map = map;
      Done = false;
      Init();
      var me = this;
      Task.Run(RequestHistoryDataChunk);
    }

    void Init() {
      IBClientCore.IBClientCoreMaster.ErrorObservable
        .ObserveOn(historyLoaderScheduler)
        .Subscribe(e => HandlePacer(e.id, e.errorCode, e.errorMsg, e.exc)).SideEffect(_disposables.Add);
      Observable.FromEvent<HistoricalDataMessage>(h => _ibClient.HistoricalData += h, h => _ibClient.HistoricalData -= h, TaskPoolScheduler.Default)
        .ObserveOn(historyLoaderScheduler)
        .Subscribe(IbClient_HistoricalData).SideEffect(_disposables.Add);
      Observable.FromEvent<HistoricalDataEndMessage>(h => _ibClient.HistoricalDataEnd += h, h => _ibClient.HistoricalDataEnd -= h, TaskPoolScheduler.Default)
        .ObserveOn(historyLoaderScheduler)
        .Subscribe(IbClient_HistoricalDataEnd).SideEffect(_disposables.Add);
    }
    void CleanUp() {
      while(_disposables.Any()) {
        _disposables[0].Dispose();
        _disposables.RemoveAt(0);
      }
      Done = true;
    }
    #endregion

    #region Event Handlers
    private void HandlePacer(int reqId, int code, string error, Exception exc) {
      IBClientCore.SetRequestHandled(reqId);
      if(code == IBERROR_NOT_CONNECTED) {
        CleanUp();
        _error(MakeError(_contract, reqId, code, error, exc));
      }
      if(reqId != _reqId)
        return;
      const string NO_DATA = "HMDS query returned no data";
      if(code == 162 && error.Contains(NO_DATA)) {
        _endDate = _endDate.isWeekend()
          ? _endDate.AddDays(-2)
          : _timeUnit != TimeUnit.S
          ? _endDate.AddDays(-1)
          : _endDate.AddMinutes(-BarSizeRange(_barSize, _timeUnit).Last());
        _error(new SoftException(new { _endDate } + ""));
        RequestNextDataChunk();
      } else if(code == 162 && error.Contains("pacing violation")) {
        _delay = TimeSpan.FromSeconds(_delay.TotalSeconds + 1 / (_delay.TotalSeconds + 1));
        _error(new DelayException(_delay));
        RequestNextDataChunk();
      } else if(code == 162 && error.Contains(NO_DATA)) {
        CleanUp();
        _error(MakeError(_contract, reqId, code, error, exc));
      } else if(reqId < 0 && exc == null) {
        _error(MakeError(_contract, reqId, code, error, exc));
      } else {
        CleanUp();
        _error(MakeError(_contract, reqId, code, error, exc));
      }
    }

    private static Exception MakeError(Contract contract, int reqId, int code, string error, Exception exc) {
      return exc ?? new Exception(new { HistoryLoader = new { reqId, contract, code, error } } + "");
    }

    object _listLocker = new object();
    private void IbClient_HistoricalDataEnd(HistoricalDataEndMessage m) {
      if(m.RequestId != _reqId)
        return;
      _delay = TimeSpan.Zero;
      lock(_listLocker) {
        _list.InsertRange(0, _list2.Distinct().SkipWhile(b => _periodsBack == 0 && b.StartDate < _dateStart));
        if((_periodsBack == 0 && _endDate <= _dateStart) || (_periodsBack > 0 && _list.Count >= _periodsBack)) {
          CleanUp();
          _dataEnd(_list);
          _done(_list);
        } else {
          _dataEnd(_list);
          var me = this;
          _list2.Clear();
          RequestNextDataChunk();
        }
      }
    }
    private void IbClient_HistoricalData(HistoricalDataMessage m) {
      if(m.RequestId == _reqId) {
        var date2 = m.Date.FromTWSString();
        if(date2 < _endDate)
          _endDate = _contract.Symbol == "VIX" && date2.TimeOfDay == new TimeSpan(3, 15, 0) ? date2.Round(MathCore.RoundTo.Hour) : date2;
        lock(_listLocker)
          _list2.Add(_map(date2, m.Open, m.High, m.Low, m.Close, m.Volume, m.Count));
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
        string whatToShow = "MIDPOINT";// !_contract.IsIndex() ? "TRADES" : "MIDPOINT";
        //_error(new SoftException(new { ReqId = _reqId, _contract.Symbol, EndDate = _endDate, Duration = Duration(_barSize, _timeUnit, _duration) } + ""));
        var ls = _contract.LocalSymbol ?? _contract.Symbol ?? "";
        var useRTH = !_contract.IsFuture && !ls.IsOption() && !ls.IsCurrenncy() && !ls.IsFuture() && !ls.IsETF() && !ls.IsIndex() && _timeUnit != TimeUnit.S;
        // TODO: reqHistoricalData - keepUpToDate
        _ibClient.ClientSocket.reqHistoricalData(_reqId, _contract, _endDate.ToTWSString(), Duration(_barSize, _timeUnit, _duration), barSizeSetting, whatToShow, useRTH ? 1 : 0, 1, false, new List<TagValue>());
      } catch(Exception exc) {
        _error(exc);
        CleanUp();
      }
    }
    #endregion

    #region Duration Helpers
    static Dictionary<BarSize, Dictionary<TimeUnit, int[]>> BarSizeRanges = new Dictionary<BarSize, Dictionary<TimeUnit, int[]>> {
      [BarSize._1_secs] = new Dictionary<TimeUnit, int[]> {
        [TimeUnit.S] = new[] { 60, 1800 }
      },
      [BarSize._1_min] = new Dictionary<TimeUnit, int[]> {
        [TimeUnit.S] = new[] { 60, 28800 },
        [TimeUnit.D] = new[] { 1, 1 }
      },
      [BarSize._3_mins] = new Dictionary<TimeUnit, int[]> {
        [TimeUnit.W] = new[] { 1, 1 }
      }
    };
    private readonly int _periodsBack;

    private static int[] BarSizeRange(BarSize barSize, TimeUnit timeUnit) {
      return BarSizeRanges[barSize][timeUnit];
    }
    public static string Duration(BarSize barSize, TimeUnit timeUnit, TimeSpan timeSpan) {
      var interval = (timeUnit == TimeUnit.S ? timeSpan.TotalSeconds : timeUnit == TimeUnit.D ? timeSpan.TotalMinutes : timeSpan.TotalDays * 7);
      var range = BarSizeRange(barSize, timeUnit);
      var duration = Math.Min(Math.Max(interval, range[0]), range[1]).Ceiling();
      return duration + " " + timeUnit;
    }
    #endregion
  }
  #endregion
  public static class IBAppExtensions {
    public static TimeSpan Span(this BarSize bs) {
      var split = (bs + "").Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
      var unit = int.Parse(split[0]);
      #region bts
      Func<string, int, TimeSpan> bts = (s, u) => {
        switch(s) {
          case "secs":
            return TimeSpan.FromSeconds(u);
          case "min":
          case "mins":
            return TimeSpan.FromMinutes(u);
          case "hour":
            return TimeSpan.FromHours(u);
          case "day":
            return TimeSpan.FromDays(u);
          default:
            throw new ArgumentException(new { s, isNot = "supported" } + "");
        }
      };
      #endregion
      return bts(split[1], unit);
    }
  }
}
