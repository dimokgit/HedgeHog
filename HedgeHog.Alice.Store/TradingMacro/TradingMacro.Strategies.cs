using DynamicExpresso;
using HedgeHog.Bars;
using HedgeHog.Models;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks.Dataflow;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive.Disposables;
using HedgeHog.Shared.Messages;
using System.Dynamic;
namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    bool IsTradingHour() { return IsTradingHour(ServerTime); }
    bool IsTradingDay() { return IsTradingDay(ServerTime); }
    bool IsTradingTime() { return IsTradingDay() && IsTradingHour(); }
    public bool TradingTimeState { get { try { return IsTradingTime(); } catch { throw; } } }
    private bool IsEndOfWeek() {
      return ServerTime.DayOfWeek == DayOfWeek.Friday && ServerTime.ToUniversalTime().TimeOfDay > TimeSpan.Parse("2" + (ServerTime.IsDaylightSavingTime() ? "0" : "1") + ":45")
        || (!TradeOnBOW && IsBeginningOfWeek());
    }
    private bool IsBeginningOfWeek() { return ServerTime.DayOfWeek == DayOfWeek.Sunday; }

    bool _CloseAtZero;

    public bool CloseAtZero {
      get { return _CloseAtZero; }
      set {
        if (_CloseAtZero == value) return;
        _CloseAtZero = value;
        _trimAtZero = false;
        MustExitOnReverse = false;
        OnPropertyChanged(() => CloseAtZero);
      }
    }
    bool _trimAtZero;
    bool _trimToLotSize;

    int CorridorDistanceByLengthRatio { get { return (RatesArray.Count * CorridorLengthRatio).ToInt(); } }
    #region CorridorDistance
    public int CorridorDistance {
      get {
        return (CorridorDistanceRatio > 1
          ? CorridorDistanceRatio
          : DoCorrDistByDist
          ? CorridorDistanceByDistance(RatesArray, ScanCorridorByStDevAndAngleHeightMin() / RatesHeight)
          : RatesArray.Count * CorridorDistanceRatio).ToInt();
      }
    }
    #region DoCorrDistByDist
    private bool _DoCorrDistByDist;
    [Category(categoryActiveYesNo)]
    public bool DoCorrDistByDist {
      get { return _DoCorrDistByDist; }
      set {
        if (_DoCorrDistByDist != value) {
          _DoCorrDistByDist = value;
          OnPropertyChanged("DoCorrDistByDist");
        }
      }
    }

    #endregion
    Tuple<double, int> _CorridorDistanceByDistance = Tuple.Create(0.0, 0);
    Func<double> ScanCorridorByStDevAndAngleHeightMin {
      get { return GetHeightMinFunc(CorridorByStDevRatioFunc); }
    }
    Func<double> ScanCorridorByStDevAndAngleHeightMin2 {
      get { return GetHeightMinFunc(CorridorByStDevRatioFunc2); }
    }

    private Func<double> GetHeightMinFunc(CorridorByStDevRatio func) {
      switch (func) {
        case CorridorByStDevRatio.HPAverage: return () => StDevByPriceAvg.Avg(StDevByHeight);
        case CorridorByStDevRatio.Height: return () => StDevByHeight;
        case CorridorByStDevRatio.Price: return () => StDevByPriceAvg;
        case CorridorByStDevRatio.HeightPrice: return () => StDevByPriceAvg + StDevByHeight;
        case CorridorByStDevRatio.Height2: return () => StDevByHeight * 2;
        case CorridorByStDevRatio.Price12: return () => StDevByPriceAvg * _stDevUniformRatio / 2;
        case CorridorByStDevRatio.Price2: return () => StDevByPriceAvg * 2;
        default:
          throw new NotSupportedException(new { CorridorByStDevRatioFunc } + "");
      }
    }
    Func<IList<double>, double> ScanCorridorByStDevAndAngleHeightMinEx {
      get { return GetHeightMinFuncEx(CorridorByStDevRatioFunc); }
    }
    Func<IList<double>, double> ScanCorridorByStDevAndAngleHeightMinEx2 {
      get { return GetHeightMinFuncEx(CorridorByStDevRatioFunc2); }
    }
    private Func<IList<double>, double> GetHeightMinFuncEx(CorridorByStDevRatio func) {
      switch (func) {
        case CorridorByStDevRatio.HPAverage: return values => values.StandardDeviation().Avg(values.StDevByRegressoin());
        case CorridorByStDevRatio.Height: return values => values.StDevByRegressoin();
        case CorridorByStDevRatio.Price: return values => values.StandardDeviation();
        case CorridorByStDevRatio.HeightPrice: return values => values.StandardDeviation() + values.StDevByRegressoin();
        case CorridorByStDevRatio.Height2: return values => values.StDevByRegressoin() * 2;
        case CorridorByStDevRatio.Price12: return values => values.StandardDeviation() * _stDevUniformRatio / 2;
        case CorridorByStDevRatio.Price2: return values => values.StandardDeviation() * 2;
        default:
          throw new NotSupportedException(new { CorridorByStDevRatioFunc } + "");
      }
    }
    int CorridorDistanceByDistance(IList<Rate> rates, double ratio) {
      if (_CorridorDistanceByDistance.Item1 == ratio) return _CorridorDistanceByDistance.Item2;
      if (_CorridorDistanceByDistance.Item2 == 0)
        return SetCorridorDistanceByDistance(rates, ratio);
      else OnCorridorDistance(() => SetCorridorDistanceByDistance(rates, ratio));
      return _CorridorDistanceByDistance.Item2;
    }
    int _SetCorridorDistanceByDistanceIsRunning = 0;
    private int SetCorridorDistanceByDistance(IList<Rate> rates, double ratio) {
      if (ratio.IsNaN()) return RatesArray.Count;
      if (_SetCorridorDistanceByDistanceIsRunning > 0)
        Log = new Exception(new { _SetCorridorDistanceByDistanceIsRunning } + "");
      _SetCorridorDistanceByDistanceIsRunning++;
      try {
        var count = CalcCountByDistanceRatio(rates.Reverse().ToArray(_priceAvg), ratio);
        _CorridorDistanceByDistance = Tuple.Create(ratio, count);
        return count;
      } finally {
        _SetCorridorDistanceByDistanceIsRunning--;
      }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="revs"></param>
    /// <param name="ratio">0 < ratio < 1</param>
    /// <returns></returns>
    private static int CalcCountByDistanceRatio(IList<double> revs, double ratio) {
      var ds = revs.Zip(revs.Skip(1), (r1, r2) => r1.Abs(r2)).ToArray();
      var total = ds.Sum() * ratio;
      var runnig = 0.0;
      var count = ds.Select(d => runnig += d)
        .TakeWhile(r => r < total)
        .Count();
      return count;
    }
    #region CorridorDistance Subject
    object _CorridorDistanceSubjectLocker = new object();
    ISubject<Action> _CorridorDistanceSubject;
    ISubject<Action> CorridorDistanceSubject {
      get {
        lock (_CorridorDistanceSubjectLocker)
          if (_CorridorDistanceSubject == null) {
            _CorridorDistanceSubject = new Subject<Action>();
            _CorridorDistanceSubject.SubscribeWithoutOverlap<Action>(a => a());
          }
        return _CorridorDistanceSubject;
      }
    }
    void OnCorridorDistance(Action p) {
      CorridorDistanceSubject.OnNext(p);
    }
    #endregion

    #endregion

    #region Corridor Start/Stop
    public double StartStopDistance { get; set; }
    double GetDistanceByDates(DateTime start, DateTime end) {
      var a = RatesArray.FindBar(start);
      var b = RatesArray.FindBar(end);
      return a.Distance - b.Distance;
    }
    partial void OnCorridorStartDateChanged() {
      if (!CorridorStartDate.HasValue)
        CorridorStopDate = DateTime.MinValue;
      return;
      if (CorridorStartDate.HasValue && RatesArray.Count > 0) {
        var rateStart = RatesArray.SkipWhile(r => r.StartDate <= CorridorStartDate.Value);
        if (StartStopDistance > 0) {
          var a = RatesArray.FindBar(CorridorStartDate.Value);
          CorridorStats.StopRate = rateStart.TakeWhile(r => a.Distance - r.Distance <= StartStopDistance).Last();
        } else
          CorridorStats.StopRate = rateStart.Skip(60).FirstOrDefault() ?? RatesArray.LastBC();
        _CorridorStopDate = CorridorStats.StopRate.StartDate;
      }
    }


    void _broadcastCorridorDateChanged() {
      if (!RatesArray.Any() || !CorridorStats.Rates.Any()) return;
      Action<int> a = u => {
        try {
          //Debug.WriteLine("broadcastCorridorDatesChange.Proc:{0:n0},Start:{1},Stop:{2}", u, CorridorStartDate, CorridorStopDate);
          OnScanCorridor(RatesArray, () => {
            SetLotSize();
            RunStrategy();
            RaiseShowChart();
          }, false);
        } catch (Exception exc) {
          Log = exc;
        }
      };
      if (false && IsInVitualTrading) Scheduler.CurrentThread.Schedule(TimeSpan.FromMilliseconds(1), () => a(0));
      else broadcastCorridorDatesChange.SendAsync(a);
    }
    partial void OnCorridorStartDateChanging(DateTime? value) {
      if (value == CorridorStartDate) return;
      _broadcastCorridorDateChanged();
    }

    bool _isCorridorStopDateManual { get { return CorridorStopDate != DateTime.MinValue; } }
    void SetCorridorStopDate(Rate rate) {
      CorridorStats.StopRate = rate;
      _CorridorStopDate = CorridorStats.StopRate == null || rate == null ? DateTime.MinValue : CorridorStats.StopRate.StartDate;
    }
    DateTime _CorridorStopDate;
    public DateTime CorridorStopDate {
      get { return _CorridorStopDate; }
      set {
        if (value == DateTime.MinValue || RateLast == null || value > RateLast.StartDate) {
          _CorridorStopDate = value;
          CorridorStats.StopRate = null;
        } else {
          value = value.Min(RateLast.StartDate).Max(CorridorStartDate.GetValueOrDefault(CorridorStats.StartDate).Add((BarPeriodInt * 2).FromMinutes()));
          if (_CorridorStopDate == value && CorridorStats.StopRate != null) return;
          _CorridorStopDate = value;
          if (value == RateLast.StartDate)
            CorridorStats.StopRate = RateLast;
          else {
            var index = RatesArray.IndexOf(new Rate() { StartDate2 = new DateTimeOffset(value) });
            CorridorStats.StopRate = RatesArray.Reverse<Rate>().SkipWhile(r => r.StartDate > value).First();
            if (CorridorStats.StopRate != null)
              _CorridorStopDate = CorridorStats.StopRate.StartDate;
          }
          if (CorridorStats.Rates.Any())
            StartStopDistance = CorridorStats.Rates.LastBC().Distance - CorridorStats.StopRate.Distance;
        }
        OnPropertyChanged("CorridorStopDate");
        _broadcastCorridorDateChanged();
      }
    }
    #endregion


    Models.ObservableValue<bool> _isCorridorDistanceOk = new Models.ObservableValue<bool>(false);

    IEnumerable<Rate> _waveMiddle {
      get {
        return WaveShort.Rates.Skip(WaveTradeStart.Rates.Count);
      }
    }


    double[] _getRegressionLeftRightRates() {
      var rateLeft = CorridorStats.Coeffs.RegressionValue(CorridorStats.Rates.Count - 1);
      var rightIndex = RatesArray.ReverseIfNot().IndexOf(CorridorStats.Rates.LastBC());
      var rateRight = new[] { rateLeft, -CorridorStats.Coeffs[1] }.RegressionValue(rightIndex);
      return new[] { rateLeft, rateRight };
    }
    void ForEachSuppRes(params Action<SuppRes>[] actions) {
      (from sr in SuppRes
       from action in actions
       select new { sr, action }
      ).ForEach(a => a.action(a.sr));
    }
    double _buyLevelNetOpen() { return Trades.IsBuy(true).NetOpen(_buyLevel.Rate); }
    double _sellLevelNetOpen() { return Trades.IsBuy(false).NetOpen(_sellLevel.Rate); }
    Action _adjustEnterLevels = () => { throw new NotImplementedException(); };
    Action<double?, double?> _adjustExitLevels = (buyLevel, selLevel) => { throw new NotImplementedException(); };
    Action _exitTrade = () => { throw new NotImplementedException(); };

    Func<Rate, double> _priceAvg = rate => rate.PriceAvg;
    void SetCorridorStartDateAsync(DateTime? date) {
      Scheduler.CurrentThread.Schedule(TimeSpan.FromMilliseconds(1), () => CorridorStartDate = date);
    }
    double CurrentEnterPrice(bool? isBuy) { return CalculateLastPrice(GetTradeEnterBy(isBuy)); }
    double CurrentExitPrice(bool? isBuy) { return CalculateLastPrice(GetTradeExitBy(isBuy)); }
    bool IsTrendsEmpty(Lazy<IList<Rate>> trends) {
      return trends == null || trends.Value.IsEmpty();
    }
    private Rate.TrendLevels TrendLines2Trends { get { return IsTrendsEmpty(TrendLines2) ? Rate.TrendLevels.Empty : TrendLines2.Value[1].Trends; } }
    private Rate.TrendLevels TrendLines1Trends { get { return IsTrendsEmpty(TrendLines1) ? Rate.TrendLevels.Empty : TrendLines1.Value[1].Trends; } }
    private Rate.TrendLevels TrendLinesTrends { get { return IsTrendsEmpty(TrendLines) ? Rate.TrendLevels.Empty : TrendLines.Value[1].Trends; } }
    private double TrendLinesTrendsPriceMax(TradingMacro tm) {
      return tm.TrendLinesTrends.PriceAvg2.Max(tm.TrendLines2Trends.PriceAvg2, tm.TrendLines1Trends.PriceAvg2);
    }
    private double TrendLinesTrendsPriceMax1(TradingMacro tm) {
      return tm.TrendLinesTrends.PriceAvg21.Max(tm.TrendLines2Trends.PriceAvg2, tm.TrendLines1Trends.PriceAvg2);
    }
    private double TrendLinesTrendsPriceMin(TradingMacro tm) {
      return tm.TrendLinesTrends.PriceAvg3.Min(tm.TrendLines2Trends.PriceAvg3, tm.TrendLines1Trends.PriceAvg3);
    }
    private double TrendLinesTrendsPriceMin1(TradingMacro tm) {
      return tm.TrendLinesTrends.PriceAvg31.Min(tm.TrendLines2Trends.PriceAvg3, tm.TrendLines1Trends.PriceAvg3);
    }
    double GetTradeCloseLevel(bool buy, double def = double.NaN) { return TradeLevelFuncs[buy ? LevelBuyCloseBy : LevelSellCloseBy]().IfNaN(def); }

    void SendSms(string header, object message, bool sendScreenshot) {
      if (sendScreenshot) RaiseNeedChartSnaphot();
      SendSms(header, message, sendScreenshot ? _lastChartSnapshot : null);
    }
    void SendSms(string header, object message, byte[] attachment) {
      if (!IsInVitualTrading)
        Observable.Timer(TimeSpan.FromSeconds(1))
          .Do(_ => HedgeHog.Cloud.Emailer.Send(
            "dimokdimon@gmail.com",
            "dimokdimon@gmail.com",
            //"13057880763@mymetropcs.com",
            "1Aaaaaaa", Pair + "::" + header, message + "\nhttp://ruleover.com:" + IpPort + "/trader.html",
            new[] { Tuple.Create(attachment, "File.png") }.Where(t => t.Item1 != null).ToArray())
            )
            .Catch<long, Exception>(_ => { Log = _; return Observable.Throw<long>(_); })
            .Retry(2)
            .Subscribe(_ => { }, exc => Log = exc);
    }
    public void ToggleIsActive() {
      IsTradingActive = !IsTradingActive;
    }
    public void ToggleCorridorStartDate() {
      FreezeCorridorStartDate(CorridorStartDate.HasValue);
    }
    public void UnFreezeCorridorStartDate() {
      FreezeCorridorStartDate(true);
    }
    public void FreezeCorridorStartDate(bool unFreeze = false) {
      if (unFreeze) CorridorStartDate = null;
      else CorridorStartDate = CorridorStats.Rates.Last().StartDate;
    }


    private Rate[] SetTrendLines1231(IList<Rate> source, Func<double[]> getStDev, Func<double[]> getRegressionLeftRightRates, int levels) {
      double h, l, h1, l1;
      double[] hl = getStDev == null ? new[] { CorridorStats.StDevMin, CorridorStats.StDevMin } : getStDev();
      h = hl[0] * 2;
      l = hl[1] * 2;
      if (hl.Length >= 4) {
        h1 = hl[2];
        l1 = hl[3];
      } else {
        h1 = hl[0] * 3;
        l1 = hl[1] * 3;
      }
      if (CorridorStats == null || !CorridorStats.Rates.Any()) return new[] { new Rate(), new Rate() };
      var rates = new[] { source.Last(), source.Skip(source.Count - CorridorStats.Rates.Count).First() };
      var regRates = getRegressionLeftRightRates();

      rates[0].PriceChartAsk = rates[0].PriceChartBid = double.NaN;
      rates[0].PriceAvg1 = regRates[1];
      rates[1].PriceAvg1 = regRates[0];

      if (levels > 1) {
        rates[0].PriceAvg2 = rates[0].PriceAvg1 + h;
        rates[0].PriceAvg3 = rates[0].PriceAvg1 - l;
        rates[1].PriceAvg2 = rates[1].PriceAvg1 + h;
        rates[1].PriceAvg3 = rates[1].PriceAvg1 - l;
      }
      if (levels > 2) {
        rates[0].PriceAvg21 = rates[0].PriceAvg1 + h1;
        rates[0].PriceAvg31 = rates[0].PriceAvg1 - l1;
        rates[1].PriceAvg21 = rates[1].PriceAvg1 + h1;
        rates[1].PriceAvg31 = rates[1].PriceAvg1 - l1;
      }
      return rates;
    }
    public IList<Rate> SetTrendLines1231() {
      return UseRates(rates => {
        var c = CorridorStats.Rates.Count.Min(rates.Count);
        return CalcTrendLines(CorridorStats.Rates.Count > 0 
          ? CorridorStats.Rates.Reverse<Rate>().ToList() 
          : rates.GetRange(rates.Count - c, c));
      });
    }
    public IList<Rate> CalcTrendLines(int count) {
      return CalcTrendLines(UseRates(rates => {
        var c = count.Min(rates.Count);
        return rates.GetRange(rates.Count - c, c);
      }), count);
    }
    public IList<Rate> CalcTrendLines(List<Rate> source, int count) {
      var c = count.Min(source.Count);
      var range = source.Count == count ? source : source.GetRange(source.Count - c, c);
      return CalcTrendLines(range);
    }
    public IList<Rate> CalcTrendLines(IList<Rate> corridorValues) {
      if (corridorValues.Count == 0) return new Rate[0];
      var minutes = (corridorValues.Last().StartDate - corridorValues[0].StartDate).Duration().TotalMinutes;
      var isTicks = minutes > 20 && BarPeriod == BarsPeriodType.t1;
      var angleBM = isTicks || BarPeriod != BarsPeriodType.t1 ? 1 : minutes / corridorValues.Count;
      var groupped = corridorValues.GroupAdjacentTicks(1.FromMinutes()
        , rate => rate.StartDate
        , g => g.Average(rate => rate.PriceAvg >= rate.PriceCMALast ? rate.PriceHigh : rate.PriceLow));
      double h, l, h1, l1;
      var doubles = isTicks ? groupped.ToList() : corridorValues.ToList(r => r.PriceAvg);
      var coeffs = doubles.Linear();
      var hl = doubles.StDevByRegressoin(coeffs);
      h = hl * 2;
      l = hl * 2;
      h1 = hl * 3;
      l1 = hl * 3;
      var rates = new[] { (Rate)corridorValues[0].Clone(), (Rate)RatesArray.Last().Clone() };
      var count = (RatesArray.Count - RatesArray.IndexOf(corridorValues[0])).Div(corridorValues.Count.Div(doubles.Count)).ToInt();
      var regRates = new[] { coeffs.RegressionValue(0), coeffs.RegressionValue(count - 1) };
      rates.ForEach(r => r.Trends = new Rate.TrendLevels(corridorValues.Count, coeffs.LineSlope(), hl) {
        Angle = coeffs.LineSlope().Angle(angleBM, PointSize)
      });

      rates[0].Trends.PriceAvg1 = regRates[0];
      rates[1].Trends.PriceAvg1 = regRates[1];

      var pa1 = rates[0].Trends.PriceAvg1;
      rates[0].Trends.PriceAvg02 = pa1 + hl;
      rates[0].Trends.PriceAvg03 = pa1 - hl;
      rates[0].Trends.PriceAvg2 = pa1 + h;
      rates[0].Trends.PriceAvg3 = pa1 - l;
      rates[0].Trends.PriceAvg21 = pa1 + h1;
      rates[0].Trends.PriceAvg31 = pa1 - l1;
      rates[0].Trends.PriceAvg22 = pa1 + h * 2;
      rates[0].Trends.PriceAvg32 = pa1 - l * 2;

      pa1 = rates[1].Trends.PriceAvg1;
      rates[1].Trends.PriceAvg02 = pa1 + hl;
      rates[1].Trends.PriceAvg03 = pa1 - hl;
      rates[1].Trends.PriceAvg2 = pa1 + h;
      rates[1].Trends.PriceAvg3 = pa1 - l;
      rates[1].Trends.PriceAvg21 = pa1 + h1;
      rates[1].Trends.PriceAvg31 = pa1 - l1;
      rates[1].Trends.PriceAvg22 = pa1 + h * 2;
      rates[1].Trends.PriceAvg32 = pa1 - l * 2;
      return rates;
    }


    private static void OnCloseTradeLocal(IList<Trade> trades, TradingMacro tm) {
      tm.BuyCloseLevel.InManual = tm.SellCloseLevel.InManual = false;
      if (trades.Any(t => t.Pair == tm.Pair) && trades.Select(t => t.PL).DefaultIfEmpty().Sum() >= -tm.PriceSpreadAverage) {
        tm.BuyLevel.CanTradeEx = tm.SellLevel.CanTradeEx = false;
        if (!tm.IsInVitualTrading && !tm.IsAutoStrategy) tm.IsTradingActive = false;
      }
      if (tm.CurrentGrossInPipTotal > 0) {
        tm.BuyLevel.CanTrade = tm.SellLevel.CanTrade = false;
        if (!tm.IsInVitualTrading) tm.IsTradingActive = false;
      }
    }

    Func<List<Rate>, DateTime> _LineTimeMinFunc;
    public Func<List<Rate>, DateTime> LineTimeMinFunc {
      get { return _LineTimeMinFunc; }
      set { _LineTimeMinFunc = value; }
    }
    private void BroadcastCloseAllTrades() {
      BroadcastCloseAllTrades(this, tm => { });
    }
    private static void BroadcastCloseAllTrades(TradingMacro tm, Action<TradingMacro> onClose) {
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(new CloseAllTradesMessage<TradingMacro>(tm, onClose));
    }
    #region WorkflowStep
    private string _WorkflowStep;
    public string WorkflowStep {
      get { return _WorkflowStep; }
      set {
        if (_WorkflowStep != value) {
          _WorkflowStep = value;
          OnPropertyChanged("WorkflowStep");
        }
      }
    }

    #endregion
    private void CloseTrading(string reason) {
      Log = new Exception("Closing Trading:" + reason);
      TradingStatistics.TradingMacros.ForEach(tm => {
        TradesManager.ClosePair(tm.Pair);
        tm.SuppRes.ForEach(sr1 => sr1.CanTrade = false);
      });
    }
    public static IDictionary<bool, double[]> FractalsRsd(IList<Rate> rates, int fractalLength, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) {
      var prices = rates.Select(r => new { PriceHigh = priceHigh(r), PriceLow = priceLow(r) }).ToArray();
      var indexMiddle = fractalLength / 2;
      var zipped = prices.Zip(prices.Skip(1), (f, s) => new[] { f, s }.ToList());
      for (var i = 2; i < fractalLength; i++)
        zipped = zipped.Zip(prices.Skip(i), (z, v) => { z.Add(v); return z; });
      var zipped2 = zipped.Where(z => z.Count == fractalLength)
        .Select(z => new { max = z.Max(a => a.PriceHigh), min = z.Min(a => a.PriceLow), middle = z[indexMiddle] })
        .Select(a => new { price = a.middle, isUp = a.max == a.middle.PriceHigh, IsDpwm = a.middle.PriceLow == a.min })
        .ToArray();
      return zipped2
        .Where(a => a.isUp || a.IsDpwm)
        .GroupBy(a => a.isUp, a => a.isUp ? a.price.PriceHigh : a.price.PriceLow)
        .ToDictionary(g => g.Key, g => g.ToArray());
    }
    IList<Rate> CorridorByVerticalLineCrosses(IList<Rate> rates, int lengthMin, out double levelOut) {
      Func<double, Tuple<Rate, Rate>, bool> isOk = (l, t) => l.Between(t.Item1.PriceAvg, t.Item2.PriceAvg);
      IList<Tuple<Rate, Rate>> ratesFused = rates.Zip(rates.Skip(1), (f, s) => new Tuple<Rate, Rate>(f, s)).ToArray();
      var maxIntervals = new { span = TimeSpan.Zero, level = double.NaN }.Yield().ToList();
      var height = rates.Take(lengthMin).Height();
      var levelMIn = rates.Take(lengthMin).Min(t => t.PriceAvg);
      var levels = Enumerable.Range(1, InPips(height).ToInt() - 1).Select(i => levelMIn + InPoints(i)).ToArray();
      levels.ForEach(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t)).Select(t => t.Item2).ToArray();
        if (ratesCrossed.Length > 3) {
          var ratesCrossesLinked = ratesCrossed.ToLinkedList();
          var span = TimeSpan.MaxValue;
          var corridorOk = false;
          //ratesCrossesLinked.ForEach(fs => 
          for (var node = ratesCrossesLinked.First.Next; node != null; node = node.Next) {
            var interval = node.Previous.Value.StartDate - node.Value.StartDate;
            if (interval > span) {
              while (!maxIntervals.Any() && node != null && (rates[0].StartDate - node.Value.StartDate).Duration().TotalMinutes < lengthMin) {
                node = node.Next;
              }
              corridorOk = node != null;
            }
            if (node != null)
              span = rates[0].StartDate - node.Value.StartDate;
            if (corridorOk || node == null) break;
          };
          if (corridorOk)
            maxIntervals.Add(new { span = span, level });
        }
      });
      var maxInterval = maxIntervals.Where(mi => mi.span.Duration().TotalMinutes >= lengthMin)
        .OrderByDescending(a => a.span).FirstOrDefault();
      if (maxInterval == null) {
        levelOut = double.NaN;
        return null;
      } else
        try {
          levelOut = maxInterval.level;
          var dateStop = rates[0].StartDate.Subtract(maxInterval.span);
          return rates.TakeWhile(r => r.StartDate >= dateStop).ToArray();
        } catch (Exception exc) {
          throw;
        }
    }

    IList<Rate> CorridorByVerticalLineCrosses2(IList<Rate> ratesOriginal, Func<Rate, double> price, int lengthMin, out double levelOut) {
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, Rate, Rate, bool> isOk = (l, Item1, Item2) => l.Between(price(Item1), price(Item2));
      var ratesFused = rates.Zip(rates.Skip(1), (f, s) => new { f.index, Item1 = f.rate, Item2 = s.rate }).ToArray();
      var maxIntervals = new { length = 0, level = double.NaN }.Yield().ToList();
      var height = rates.Take(lengthMin).Height(a => price(a.rate));
      var levelMIn = rates.Take(lengthMin).Min(t => price(t.rate));
      var levels = Enumerable.Range(1, InPips(height).ToInt() - 1).Select(i => levelMIn + InPoints(i)).ToArray();
      levels.ForEach(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t.Item1, t.Item2)).Select(a => new { a.index, rate = a.Item2 }).ToArray();
        if (ratesCrossed.Length > 3) {
          var span = int.MaxValue;
          var corridorOk = false;
          for (var i = 1; i < ratesCrossed.Count(); i++) {
            var node = ratesCrossed[i];
            var interval = node.index - ratesCrossed[i - 1].index;
            if (interval > span) {
              if (node.index < lengthMin) {
                node = ratesCrossed.Zip(ratesCrossed.Skip(1), (a, b) => new { length = b.index - a.index, item = b })
                  .Where(a => a.length < span && a.item.index > lengthMin)
                  .Select(a => a.item).FirstOrDefault();
              }
              corridorOk = node != null;
            }
            if (node != null)
              span = node.index;
            if (corridorOk || node == null) break;
          };
          if (corridorOk)
            maxIntervals.Add(new { length = span, level });
        }
      });
      var maxInterval = maxIntervals.Where(mi => mi.length >= lengthMin).OrderByDescending(a => a.length).FirstOrDefault();
      if (maxInterval == null) {
        levelOut = double.NaN;
        return null;
      } else
        try {
          levelOut = maxInterval.level;
          return ratesOriginal.Take(maxInterval.length).ToArray();
        } catch {
          throw;
        }
    }

    IList<Rate> CorridorByVerticalLineLongestCross(Rate[] ratesOriginal, Func<Rate, double> price) {
      var point = InPoints(1);
      Func<long, long, double> getHeight = (index1, index2) => {
        var rates1 = new Rate[index2 - index1 + 1];
        Array.Copy(ratesOriginal, index1, rates1, 0, rates1.Length);
        var price0 = price(rates1[0]);
        return rates1.Average(r => price0.Abs(price(r)) / point).Round(0);
      };
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, Rate, Rate, bool> isOk = (l, Item1, Item2) => l.Between(price(Item1), price(Item2));
      var ratesFused = rates.Zip(rates.Skip(1), (f, s) => new { f.index, Item1 = f.rate, Item2 = s.rate }).ToArray();
      var levelMin = ratesOriginal.Min(price);
      var levels = Enumerable.Range(1, InPips(ratesOriginal.Height(price)).ToInt() - 1)
        .Select(level => levelMin + level * point).ToArray().AsParallel();
      var ratesByLevels = levels.Select(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t.Item1, t.Item2)).Select(a => new { a.index, rate = a.Item2 }).ToArray();
        var h0 = 0.0;
        var d0 = 0;
        return ratesCrossed.Zip(ratesCrossed.Skip(1)
          , (prev, next) => new { prev, next, distance = d0 = next.index - prev.index, height = h0 = getHeight(prev.index, next.index), dToH = (d0 / h0).Round(1) });
      }).SelectMany(a => a.Where(b => b.height > 0))
      .Distinct((a, b) => a.prev.index == b.prev.index && a.distance == b.distance)
      .ToList();
      var distanceMin = ratesByLevels.AverageByIterations(a => a.distance, (a, d) => a > d, 2).Average(a => a.distance);
      Func<double, double, bool> comp = (a, b) => a <= b;
      //ratesByLevels = ratesByLevels.Where(a => a.distance >= distanceMin).ToList().SortByLambda((a, b) => comp(a.dToH, b.dToH));
      //var dToHMin = ratesByLevels.AverageByIterations(a => a.dToH, comp, 2).Average(a => a.dToH);
      var winner = ratesByLevels/*.TakeWhile(a => comp(a.dToH, dToHMin))*/.OrderByDescending(a => a.distance).FirstOrDefault();
      if (winner == null) return null;
      var ratesOut = new Rate[winner.next.index - winner.prev.index + 1];
      Array.Copy(ratesOriginal, winner.prev.index, ratesOut, 0, ratesOut.Length);
      return ratesOut;
    }

    IList<Rate> CorridorByVerticalLineLongestCross2(Rate[] ratesOriginal, int indexMax, Func<Rate, double> price) {
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      Func<long, long, double> getHeight = (index1, index2) => {
        var rates1 = new Rate[index2 - index1 + 1];
        Array.Copy(ratesOriginal, index1, rates1, 0, rates1.Length);
        var price0 = price(rates1[0]);
        return rates1.Average(r => price0.Abs(price(r)) / point).Round(0);
      };
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, Rate, Rate, bool> isOk = (l, Item1, Item2) => l.Between(price(Item1), price(Item2));
      var ratesFused = rates.Zip(rates.Skip(1), (f, s) => new { f.index, Item1 = f.rate, Item2 = s.rate }).ToArray();
      var levelMin = (ratesOriginal.Min(price) * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var levels = Enumerable.Range(0, InPips(ratesOriginal.Height(price)).ToInt())
        .Select(level => levelMin + level * point).ToArray();
      var ratesByLevels = levels.AsParallel().Select(level => {
        var ratesCrossed = ratesFused.Where(t => isOk(level, t.Item1, t.Item2)).Select(a => new { a.index, rate = a.Item2 }).ToArray();
        return ratesCrossed.Zip(ratesCrossed.Skip(1), (prev, next) => new { prev, next, distance = next.index - prev.index });
      }).SelectMany(a => a.Where(b => b.prev.index <= indexMax))
      .Distinct((a, b) => a.prev.index == b.prev.index && a.distance == b.distance)
      .ToList();
      var distanceMin = ratesByLevels.AverageByIterations(a => a.distance, (a, d) => a > d, 2).Average(a => a.distance);
      Func<double, double, bool> comp = (a, b) => a <= b;
      //ratesByLevels = ratesByLevels.Where(a => a.distance >= distanceMin).ToList().SortByLambda((a, b) => comp(a.dToH, b.dToH));
      //var dToHMin = ratesByLevels.AverageByIterations(a => a.dToH, comp, 2).Average(a => a.dToH);
      var winner = ratesByLevels/*.TakeWhile(a => comp(a.dToH, dToHMin))*/.OrderByDescending(a => a.distance).FirstOrDefault();
      if (winner == null) return null;
      var ratesOut = new Rate[winner.next.index - winner.prev.index + 1];
      Array.Copy(ratesOriginal, winner.prev.index, ratesOut, 0, ratesOut.Length);
      return ratesOut;
    }

    class CorridorByStDev_Results {
      public double Level { get; set; }
      public double StDev { get; set; }
      public int Count { get; set; }
      public override string ToString() {
        return new { Level, StDev, Count } + "";
      }
    }
    IList<CorridorByStDev_Results> StDevsByHeight(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Select(price).DefaultIfEmpty(double.NaN).Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips.Sub(halfInPips).Max(halfInPips))
        .Select(levelInner => levelMin + levelInner * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }
    IList<CorridorByStDev_Results> StDevsByHeight1(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Select(price).DefaultIfEmpty(double.NaN).Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt() * 0;
      var rangeHeightInPips = ratesHeightInPips - halfInPips * 2;
      var levels = ParallelEnumerable.Range(halfInPips, rangeHeightInPips.Max(0))
        .Select(levelInner => levelMin + levelInner * point);
      var levelsByCount = levels.Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }

    IList<CorridorByStDev_Results> StDevsByHeight2(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Select(price).DefaultIfEmpty(double.NaN).Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height;
      var halfInPips = 0 * InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips.Sub(halfInPips * 2).Max(halfInPips))
        .Select(levelInner => levelMin + levelInner * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }
    IList<CorridorByStDev_Results> StDevsByHeight3(IList<Rate> ratesReversed, double height) {
      var half = height / 2;
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var prices = ratesReversed.Select(price).DefaultIfEmpty(double.NaN).ToArray();
      var levelMin = (prices.Min() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var levelMax = (prices.Max() * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var levels = Range.Double(levelMin + half * 2, levelMax - half * 2, point).ToArray();
      return StDevsByHeightBase(ratesReversed, half, price, levels);
    }

    private static IList<CorridorByStDev_Results> StDevsByHeightBase(IList<Rate> ratesReversed, double half, Func<Rate, double> price, double[] levels) {
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(price), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      return levelsByCount.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }

    IList<CorridorByStDev_Results> CorridorByStDev(IList<Rate> ratesReversed, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesReversed.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesReversed.Min(price) * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesReversed.Height(price)).Floor();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips.Sub(halfInPips).Max(halfInPips))
        .Select(levelInner => levelMin + levelInner * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(levelInner => {
        var ratesInner = ratesReversed.Where(r => isOk(levelInner - half, levelInner + half, r)).ToArray();
        if (ratesInner.Length < 2) return null;
        return new { level = levelInner, stDev = ratesInner.StDev(_priceAvg), count = ratesInner.Length };
      }).Where(a => a != null).ToList();
      StDevLevelsCountsAverage = levelsByCount.Select(a => (double)a.count).ToArray().AverageByIterations(2, true).DefaultIfEmpty(double.NaN).Average().ToInt();
      levelsByCount.Sort(a => -a.stDev);
      var levelsGrouped = levelsByCount//.Where(a => a.count >= StDevLevelsCountsAverage).ToArray()
        .GroupByLambda((a, b) => a.level.Abs(b.level) < height * 2)
        .Select(k => new { k.Key.level, k.Key.stDev, k.Key.count }).ToList();
      levelsGrouped.Sort(a => -a.stDev);
      return levelsGrouped.Select(a => new CorridorByStDev_Results() {
        Level = a.level, StDev = a.stDev, Count = a.count
      }).ToArray();
    }

    #region StDevLevelsRatio
    private double _StDevLevelsCountsAverage;
    public double StDevLevelsCountsAverage {
      get { return _StDevLevelsCountsAverage; }
      set {
        if (_StDevLevelsCountsAverage != value) {
          _StDevLevelsCountsAverage = value;
          OnPropertyChanged("StDevLevelsCountsAverage");
        }
      }
    }

    #endregion
    double CorridorByDensity(IList<Rate> ratesOriginal, double height) {
      var price = _priceAvg;
      var point = InPoints(1);
      var rountTo = TradesManager.GetDigits(Pair) - 1;
      var rates = ratesOriginal.Select((rate, index) => new { index, rate }).ToArray();
      Func<double, double, Rate, bool> isOk = (h, l, rate) => rate.PriceAvg.Between(l, h);
      var levelMin = (ratesOriginal.Min(price) * Math.Pow(10, rountTo)).Ceiling() / Math.Pow(10, rountTo);
      var ratesHeightInPips = InPips(ratesOriginal.Height(price)).ToInt();
      var corridorHeightInPips = InPips(height);
      var half = height / 2;
      var halfInPips = InPips(half).ToInt();
      var levels = Enumerable.Range(halfInPips, ratesHeightInPips - halfInPips)
        .Select(level => levelMin + level * point).ToArray();
      var levelsByCount = levels.AsParallel().Select(level =>
        new { level, count = ratesOriginal.Count(r => isOk(level - half, level + half, r)) }
      ).ToList();
      return levelsByCount.Aggregate((p, n) => p.count > n.count ? p : n).level;
    }
    private double _buyLevelRate = double.NaN;
    private double _sellLevelRate = double.NaN;

    double _MagnetPriceRatio = double.NaN;

    public double MagnetPriceRatio {
      get { return _MagnetPriceRatio; }
      set {
        if (_MagnetPriceRatio == value) return;
        _MagnetPriceRatio = value;
        OnPropertyChanged(() => MagnetPriceRatio);
      }
    }

    List<double> _CenterOfMassLevels = new List<double>();

    public List<double> CenterOfMassLevels {
      get { return _CenterOfMassLevels; }
      private set { _CenterOfMassLevels = value; }
    }

    ILookup<bool, Rate> _Fractals = "".ToLookup(c => true, c => (Rate)null);
    public ILookup<bool, Rate> Fractals {
      get { return _Fractals; }
      set { _Fractals = value; }
    }

    IEnumerable<DateTime> _fractalTimes = new DateTime[0];
    double VoltsBelowAboveLengthMinCalc {
      get {
        return VoltsBelowAboveLengthMin.Abs() > 10 ? VoltsBelowAboveLengthMin : VoltsFrameLength * VoltsBelowAboveLengthMin;
      }
    }

    private double _VoltsBelowAboveLengthMin = 60;
    [Category(categoryXXX)]
    public double VoltsBelowAboveLengthMin {
      get { return _VoltsBelowAboveLengthMin; }
      set {
        _VoltsBelowAboveLengthMin = value;
        OnPropertyChanged("VoltsBelowAboveLengthMin");
      }
    }
    public IEnumerable<DateTime> FractalTimes {
      get { return _fractalTimes; }
      set { _fractalTimes = value; }
    }
    public int FttMax { get; set; }

    public bool MustExitOnReverse { get; set; }
  }
}
