using System;
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
using HedgeHog.DateTimeZone;
using System.Diagnostics;
using Microsoft.VisualBasic;

namespace IBApp {
  #region HistoryLoader_Slow
  public class HistoryLoader_Slow<T> :IHistoryLoader where T : HedgeHog.Bars.BarBaseDate {
    private static EventLoopScheduler HistoryLoader_SlowScheduler = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "HistoryLoader_Slow", Priority = ThreadPriority.Normal });
    #region Fields
    private const int IBERROR_NOT_CONNECTED = 504;
    private const int SERVER_ERROR = 162;
    private readonly SortedSet<T> _list;
    private readonly SortedSet<T> _list2;
    private int _reqId;
    private readonly IBClientCore _ibClient;
    private Contract _contract;
    private readonly ContractDetails _contractDetails;
    private readonly DateTime _dateStart;
    private DateTime __endDate;
    private DateTime _endDate {
      get { return __endDate; }
      set {
        Debug.WriteLine(new { __endDate, value });
        __endDate = value;
      }
    }
    private readonly TimeUnit _timeUnit;
    private readonly BarSize _barSize;
    private readonly TimeSpan _duration;
    private readonly Action<ICollection<T>> _done;
    private readonly Action<Exception> _error;
    private readonly Action<ICollection<T>> _dataEnd;
    private TimeSpan _delay;
    private readonly DataMapDelegate<T> _map;
    bool _useRTH;
    List<IDisposable> _disposables = new List<IDisposable>();
    #endregion

    #region ctor
    public HistoryLoader_Slow(IBClientCore ibClient
      , Contract contract
      , int periodsBack
      , DateTime endDate
      , TimeSpan duration
      , TimeUnit timeUnit
      , BarSize barSize
      , bool? useRTH
      , DataMapDelegate<T> map
      , Action<ICollection<T>> done
      , Action<ICollection<T>> dataEnd
      , Action<Exception> error) {
      _ibClient = ibClient;
      _periodsBack = periodsBack;
      _reqId = IBClientCore.IBClientCoreMaster.ValidOrderId();
      _list = new SortedSet<T>();
      _list2 = new SortedSet<T>();
      if(endDate.Kind == DateTimeKind.Unspecified)
        throw new Exception(new { endDate = new { endDate.Kind } } + "");
      var lastHour = contract.IsFuture ? 17 : 20;
      _endDate = endDate;
      _timeUnit = timeUnit;
      _barSize = barSize;
      _contract = contract;
      _contractDetails = contract.FromDetailsCache().SingleOrDefault() ?? ContractDetails.ContractDetailsCache.Values.First(cd => cd.Contract.ConId == contract.ConId);
      var barSpan = barSize.Span();
      var barsPerWeek = _contractDetails.TradingTimePerWeek.Ticks / barSpan.Ticks;
      var hoursBack = (periodsBack.Div(barsPerWeek).Ceiling() * 7 * 24.0).FromHours();
      var durationByPeriod = hoursBack;
      _duration = periodsBack == 0 ? duration : durationByPeriod;
       _dateStart = _endDate - _duration;
      _done = done;
      _dataEnd = dataEnd;
      _error = error;
      _delay = TimeSpan.Zero;
      _map = map;
      Init();
      var me = this;
      var ls = _contract.LocalSymbol ?? _contract.Symbol ?? "";
      var isETF = /*cd.LongName.Contains(" ETF") ||*/ ls.IsETF();
      _useRTH = useRTH.GetValueOrDefault(!_contract.IsFuture && !ls.IsOption() && !ls.IsCurrenncy() && !ls.IsFuture() && !isETF && !ls.IsIndex() && _timeUnit != TimeUnit.S);
      Task.Run(RequestHistoryDataChunk);

      //contract.ReqContractDetailsCached()
      //  .SubscribeOn(TaskPoolScheduler.Default)
      //  .ObserveOn(TaskPoolScheduler.Default)
      //  .Timeout(2.FromSeconds())
      //  .Catch<ContractDetails,Exception>(e=> {
      //    LogMessage.Send(e);
      //    CleanUp();
      //    return  Observable.Empty<ContractDetails>();
      //    })
      //  .Subscribe(cd => {
      //    _contract = cd.Contract;
      //    Passager.ThrowIf(() => _contract.Exchange.IsNullOrEmpty());
      //    Passager.ThrowIf(() => _contract.Exchange.IsNullOrEmpty());
      //  },exc=> { LogMessage.Send(exc); CleanUp(); });
    }

    void Init() {
      IBClientCore.IBClientCoreMaster.ErrorObservable
        .ObserveOn(HistoryLoader_SlowScheduler)
        .Subscribe(e => HandlePacer(e.id, e.errorCode, e.errorMsg, e.exc)).SideEffect(_disposables.Add);
      IBClientCore.SetRequestHandled(_reqId);
      Observable.FromEvent<HistoricalDataMessage>(h => _ibClient.HistoricalData += h, h => _ibClient.HistoricalData -= h, TaskPoolScheduler.Default)
        .ObserveOn(HistoryLoader_SlowScheduler)
        .Subscribe(IbClient_HistoricalData).SideEffect(_disposables.Add);
      Observable.FromEvent<HistoricalDataEndMessage>(h => _ibClient.HistoricalDataEnd += h, h => _ibClient.HistoricalDataEnd -= h, TaskPoolScheduler.Default)
        .ObserveOn(HistoryLoader_SlowScheduler)
        .Subscribe(IbClient_HistoricalDataEnd).SideEffect(_disposables.Add);
    }
    void CleanUp() {
      while(_disposables.Any()) {
        _disposables[0].Dispose();
        _disposables.RemoveAt(0);
      }
    }
    #endregion

    #region Event Handlers
    private void HandlePacer(int reqId, int code, string error, Exception exc) {
      if(reqId != _reqId) return;
      if(code == IBERROR_NOT_CONNECTED) {
        CleanUp();
        _error(MakeError(_contract, reqId, code, error, exc));
      }
      if(reqId != _reqId)
        return;
      const string NO_DATA = "HMDS query returned no data";
      if(code == SERVER_ERROR && error.Contains(NO_DATA)) {
        var newEndDate = _endDate.isWeekend()
          ? _endDate.AddDays(-2)
          : _timeUnit != TimeUnit.S
          ? _endDate.AddDays(-1)
          : _endDate.AddSeconds(-BarSizeRange(_barSize, _timeUnit).range.Last());
        _endDate = newEndDate;
        _error(new SoftException(GetType().Name + new { _contract, _endDate, } + " No Data"));
        RequestNextDataChunk();
      } else if(code == SERVER_ERROR && error.Contains("pacing violation")) {
        _delay = TimeSpan.FromSeconds(_delay.TotalSeconds + 1).Min(10.FromSeconds());
        _error(new DelayException($"HistoryLoader_Slow:{_contract.Instrument}:{_barSize}:{_reqId}:{new { listCount = _list.Count }}", _delay));
        RequestNextDataChunk();
      } else if(code == SERVER_ERROR && error.Contains(NO_DATA)) {
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
      return exc ?? new Exception(new { HistoryLoader_Slow = new { reqId, contract, code, error } } + "");
    }

    SortedSet<T> _cache = new System.Collections.Generic.SortedSet<T>();
    object _listLocker = new object();

    static LambdaComparer<T> comp = LambdaComparer.Factory<T>((r1, r2) => r1.StartDate == r2.StartDate);
    private void IbClient_HistoricalDataEnd(HistoricalDataEndMessage m) {
      if(m.RequestId != _reqId)
        return;
      _delay = TimeSpan.Zero;
      Debug.WriteLineIf(false, new { m, _list2 = _list2.Count });
      var ds = m.StartDate.FromTWSString().Min((_cache.FirstOrDefault()?.StartDate).GetValueOrDefault(DateTime.MaxValue));
      var de = m.EndDate.FromTWSString();
      lock(_listLocker) {
        var c = _list2.Distinct().Except(_cache, comp)
          .Do(r => {
            if(r.StartDate.Between(ds, de)) _list.Add(r);
            _cache.Add(r);
          }).Count() > 0;
        var d = _cache.GroupBy(r => r.StartDate.Date).ToDictionary(g => g.Key, g => g.Count());
        //_list.InsertRange(0, _list2.Distinct().SkipWhile(b => _periodsBack == 0 && b.StartDate < _dateStart));
        if(!c /*|| (_periodsBack == 0 && _endDate <= _dateStart) || (_periodsBack > 0 && _list.Count >= _periodsBack)*/) {
          if(_periodsBack == 0 && ds < _dateStart || _list.Count >= _periodsBack.IfZero(int.MaxValue)) {
            CleanUp();
            _dataEnd(_list);
            _done(_cache);
            return;
          } else {
            _reqId = IBClientCore.IBClientCoreMaster.ValidOrderId();
            _error(new SoftException($"HistoryLoader_Slow[{_contract}]:{new { _reqId, listCount = _list.Count }}"));
            _endDate = ds.Subtract(_barSize.Span());
            while(!_contractDetails.IsTradeHour(_endDate)) {
              _endDate = _endDate.Subtract(_barSize.Span());
            }

          }
        } else {
          if(_list.Any())
            _dataEnd(_list);
          _list2.Clear();
          if(_list.Count >= _periodsBack.IfZero(int.MaxValue)){
            CleanUp();
            _done(_cache);
            return;
          }
        }
        RequestHistoryDataChunk();
      }
    }
    private void IbClient_HistoricalData(HistoricalDataMessage m) {
      if(m.RequestId == _reqId) {
        //Debug.WriteLine(m);
        var date2 = m.Date.FromTWSString();
        if(false && date2 < _endDate)
          _endDate = _contract.Symbol == "VIX" && date2.TimeOfDay == new TimeSpan(3, 15, 0)
            ? date2.Round(MathCore.RoundTo.Hour)
            : date2.Subtract(_barSize.Span());
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
        string whatToShow = _contract.IsIndex ? "TRADES" : "MIDPOINT";
        //_error(new SoftException(new { ReqId = _reqId, _contract.Symbol, EndDate = _endDate, Duration = Duration(_barSize, _timeUnit, _duration) } + ""));
        // TODO: reqHistoricalData - keepUpToDate
        var timeLeft = _endDate - _dateStart;
        var duration = Duration(_barSize, _timeUnit, _duration);

        var months = 0;// DateAndTime.DateDiff(DateInterval.Month, _dateStart, _endDate);
        var weeks = (DateAndTime.DateDiff(DateInterval.WeekOfYear, _dateStart, _endDate).Max(0) + 1).Min(52);
        duration = months>0 ? $"{months} M": $"{weeks} W";

        Debug.WriteLine(new { duration });

        var dt = _endDate.ToUniversalTime().ToTWSString().Replace(" ", "-");// + " US/Eastern";
        _ibClient.OnReqMktData(() =>
        _ibClient.ClientSocket.reqHistoricalData(_reqId, _contract, dt, duration, barSizeSetting, whatToShow, _useRTH ? 1 : 0, 1, false, new List<TagValue>())
        );
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
      },
      [BarSize._5_mins] = new Dictionary<TimeUnit, int[]> {
        [TimeUnit.W] = new[] { 1, 1 }
      },
      [BarSize._10_mins] = new Dictionary<TimeUnit, int[]> {
        [TimeUnit.M] = new[] { 1, 1 }
      },
      [BarSize._15_mins] = new Dictionary<TimeUnit, int[]> {
        [TimeUnit.M] = new[] { 1, 1 }
      },
      [BarSize._30_mins] = new Dictionary<TimeUnit, int[]> {
        //[TimeUnit.W] = new[] { 1, 1 },
        [TimeUnit.M] = new[] { 1, 6 }
      },
      [BarSize._1_hour] = new Dictionary<TimeUnit, int[]> {
        //[TimeUnit.W] = new[] { 1, 1 },
        [TimeUnit.M] = new[] { 1, 6 }
      },
      [BarSize._1_day] = new Dictionary<TimeUnit, int[]> {
        [TimeUnit.Y] = new[] { 1, 1 }
      }
    };
    private readonly int _periodsBack;

    private static (TimeUnit timeUnit, int[]  range ) BarSizeRange(BarSize barSize, TimeUnit timeUnit) {
      return BarSizeRanges[barSize].TryGetValue(timeUnit,out var bs) ? (timeUnit, bs) : BarSizeRanges[barSize].Select(kv=>(kv.Key,kv.Value)).Last();
    }
    public static string Duration(BarSize barSize, TimeUnit timeUnit, TimeSpan timeSpan) {
      var interval = (timeUnit == TimeUnit.S ? timeSpan.TotalSeconds 
        : timeUnit == TimeUnit.D ? timeSpan.TotalMinutes : timeSpan.TotalDays * 7);
      var (timeUnit2,range) = BarSizeRange(barSize, timeUnit);
      var duration = Math.Min(Math.Max(interval, range[0]), range[1]).Ceiling();
      return duration + " " + timeUnit2;
    }
    #endregion
  }
  #endregion
}
