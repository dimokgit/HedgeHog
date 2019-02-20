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
using TL = HedgeHog.Bars.Rate.TrendLevels;
using System.Threading.Tasks;
using System.Threading;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    Action StrategyAction {
      get {
        switch((Strategy & ~Strategies.Auto)) {
          case Strategies.Hot:
            return StrategyEnterUniversal;
          case Strategies.Universal:
            return StrategyEnterUniversal;
          case Strategies.ShortPut:
            return StrategyShortPut;
          case Strategies.ShortStraddle:
            return StrategyShortStraddle;
          case Strategies.Long:
            return StrategyLong;
          case Strategies.None:
            return () => { };
        }
        throw new NotSupportedException("Strategy " + Strategy + " is not supported.");
      }
    }

    private void StrategyShortPut() => UseAccountManager(am => {
      var puts = OpenPuts().ToList();
      var distanceOk = (from curPut in CurrentPut
                        from openPut in puts.OrderBy(p => p.contract.ComboStrike()).Take(1).ToList()
                        let strikeAvg = curPut.strikeAvg
                        let openPutPrice = openPut.price.Abs()
                        let openPutStrike = openPut.contract.ComboStrike()
                        where curPut.option.LastTradeDateOrContractMonth == openPut.contract.LastTradeDateOrContractMonth
                        && strikeAvg + openPutPrice > openPutStrike
                        select true
                        ).IsEmpty();
      var hasOptions = puts.Count +
        am.UseOrderContracts(OrderContracts =>
        (from put in CurrentPut
         join oc in OrderContracts.Values.Where(o => !o.isDone & o.order.Action == "SELL") on put.instrument equals oc.contract.Instrument
         select true
         )).Concat().Count();
      var hasSellOrdes = am.UseOrderContracts(OrderContracts =>
      (from oc in OrderContracts.Values.Where(o => !o.isDone && o.contract.IsPut && !o.contract.IsCombo && o.order.Action == "SELL")
       select true
       )).Concat().Count();
      if(distanceOk && hasOptions < TradeCountMax) {
        TradeConditionsEval()
          .DistinctUntilChanged(td => td)
          .Where(td => td.HasUp())
          .Take(1)
          .ForEach(_ => {
            var pos = -puts.Select(p => p.position.Abs()).DefaultIfEmpty(TradingRatio.ToInt()).Max();
            CurrentPut?.ForEach(p => {
              Log = new Exception($"{nameof(TradeConditionsTrigger)}:{nameof(am.OpenTrade)}:{new { p.option, pos, Thread.CurrentThread.ManagedThreadId }}");
              am.OpenTrade(p.option, pos, p.ask, 0.2, true, ServerTime.AddMinutes(5));
            });
          });
      }
    });
    private void StrategyShortStraddle() => UseAccountManager(am => {
      var straddles = OpenStraddles(am);
      var hasOrders = am.UseOrderContracts(ocs => ocs.Values.Where(o => !o.isDone)).Concat().Any();
      if(straddles.IsEmpty() && !hasOrders) {
        TradeConditionsEval()
          .DistinctUntilChanged(td => td)
          .Where(td => td.HasUp())
          .Take(1)
          .ForEach(_ => {
            var pos = -TradingRatio.ToInt();
            CurrentStraddle?.ForEach(p => {
              Log = new Exception($"{nameof(StrategyShortStraddle)}:{nameof(am.OpenTrade)}:{new { p.combo.contract, pos, Thread.CurrentThread.ManagedThreadId }}");
              am.OpenTrade(p.combo.contract, pos, p.ask, 0.2, true, ServerTime.AddMinutes(10));
            });
          });
      }
    });
    private void StrategyLong() => UseAccountManager(am => {
      var hasOrders = am.UseOrderContracts(ocs => ocs.Values.Where(o => o.contract.Instrument == Pair && !o.isDone)).Concat().Any();
      if(Trades.IsEmpty() && !hasOrders) {
        TradeConditionsEval()
          .DistinctUntilChanged(td => td)
          .Where(td => td.HasUp())
          .Take(1)
          .ForEach(_ => {
            var pos = TradingRatio.ToInt();
            Log = new Exception($"{nameof(StrategyLong)}:{nameof(am.OpenTrade)}:{new { Pair }}");
            var p = CurrentPrice.Bid;
            if(!IBApi.Contract.Contracts.TryGetValue(Pair, out var contract))
              throw new Exception($"Pair:{Pair} is not fround in Contracts");
            am.OpenTrade(contract, pos, p, CalculateTakeProfit(), true, ServerTime.AddMinutes(10));
          });
      }
    });
    void RunStrategy() {
      StrategyAction();
    }


    public void CleanStrategyParameters() {
      DoAdjustExitLevelByTradeTime = false;
      TakeProfitFunction = TradingMacroTakeProfitFunction.BuySellLevels;
      TakeProfitXRatio = 1;
      TimeFrameTreshold = "";
      TradeDirection = TradeDirections.Both;
      LevelBuyBy = TradeLevelBy.None;
      LevelSellBy = TradeLevelBy.None;
      IsTurnOnOnly = false;
      TradeCountMax = 0;
      TradeCountStart = 0;
      TradingDistanceFunction = TradingMacroTakeProfitFunction.RatesHeight;
      TradingDistanceX = 1;
      TradingRatioByPMC = true;
      TipRatio = 0;
      TradingAngleRange = 0;
      TrendAngleBlue = "";
      TrendAngleRed = TrendAngleGreen = 0;
      CmaPasses = 20;
      CmaRatioForWaveLength = 2;
      PriceCmaLevels = 0.1;
      RatesDistanceMin = 0.5;
      BigWaveIndex = 0;
      CanDoEntryOrders = false;
      CanDoNetLimitOrders = false;
      IsTakeBack = false;
      IsTrader = BarPeriod == BarsPeriodType.t1;
      IsTrender = true;
      LimitProfitByRatesHeight = false;
      ForceOpenTrade = false;
    }

    bool IsTradingHour() { return IsTradingHour(ServerTime); }
    bool IsTradingDay() { return IsTradingDay(ServerTime); }
    bool IsTradingTime() { return IsTradingDay() && IsTradingHour(); }
    public bool TradingTimeState { get { try { return IsTradingTime(); } catch { throw; } } }
    private bool IsEndOfWeek() {
      var isEow = ServerTime.DayOfWeek == DayOfWeek.Friday && ServerTime.ToUniversalTime().Hour > 20
        || IsBeginningOfWeek();
      if(isEow) {
        Log = new Exception(new { isEow, Is = "Turned off" } + "");
        return false;
      }
      return isEow;
    }
    private bool IsBeginningOfWeek() { return ServerTime.DayOfWeek == DayOfWeek.Sunday; }

    bool _CloseAtZero;

    public bool CloseAtZero {
      get { return _CloseAtZero; }
      set {
        if(_CloseAtZero == value)
          return;
        _CloseAtZero = value;
        MustExitOnReverse = false;
        OnPropertyChanged(() => CloseAtZero);
      }
    }

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
        if(_DoCorrDistByDist != value) {
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
      switch(func) {
        case CorridorByStDevRatio.HPAverage:
          return () => StDevByPriceAvg.Avg(StDevByHeight);
        case CorridorByStDevRatio.Height:
          return () => StDevByHeight;
        case CorridorByStDevRatio.Price:
          return () => StDevByPriceAvg;
        case CorridorByStDevRatio.HeightPrice:
          return () => StDevByPriceAvg + StDevByHeight;
        case CorridorByStDevRatio.Height2:
          return () => StDevByHeight * 2;
        case CorridorByStDevRatio.Price12:
          return () => StDevByPriceAvg * _stDevUniformRatio / 2;
        case CorridorByStDevRatio.Price2:
          return () => StDevByPriceAvg * 2;
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
      switch(func) {
        case CorridorByStDevRatio.HPAverage:
          return values => values.StandardDeviation().Avg(values.StDevByRegressoin());
        case CorridorByStDevRatio.Height:
          return values => values.StDevByRegressoin();
        case CorridorByStDevRatio.Price:
          return values => values.StandardDeviation();
        case CorridorByStDevRatio.HeightPrice:
          return values => values.StandardDeviation() + values.StDevByRegressoin();
        case CorridorByStDevRatio.Height2:
          return values => values.StDevByRegressoin() * 2;
        case CorridorByStDevRatio.Price12:
          return values => values.StandardDeviation() * _stDevUniformRatio / 2;
        case CorridorByStDevRatio.Price2:
          return values => values.StandardDeviation() * 2;
        default:
          throw new NotSupportedException(new { CorridorByStDevRatioFunc } + "");
      }
    }
    int CorridorDistanceByDistance(IList<Rate> rates, double ratio) {
      if(_CorridorDistanceByDistance.Item1 == ratio)
        return _CorridorDistanceByDistance.Item2;
      if(_CorridorDistanceByDistance.Item2 == 0)
        return SetCorridorDistanceByDistance(rates, ratio);
      else
        OnCorridorDistance(() => SetCorridorDistanceByDistance(rates, ratio));
      return _CorridorDistanceByDistance.Item2;
    }
    int _SetCorridorDistanceByDistanceIsRunning = 0;
    private int SetCorridorDistanceByDistance(IList<Rate> rates, double ratio) {
      if(ratio.IsNaN())
        return RatesArray.Count;
      if(_SetCorridorDistanceByDistanceIsRunning > 0)
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
    /// <param name="ratio"></param>
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
        lock(_CorridorDistanceSubjectLocker)
          if(_CorridorDistanceSubject == null) {
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
      if(!CorridorStartDate.HasValue)
        CorridorStopDate = DateTime.MinValue;
    }


    void _broadcastCorridorDateChanged() {
      if(!RatesArray.Any() || !CorridorStats.Rates.Any())
        return;
      Action<int> a = u => {
        try {
          //Debug.WriteLine("broadcastCorridorDatesChange.Proc:{0:n0},Start:{1},Stop:{2}", u, CorridorStartDate, CorridorStopDate);
          OnScanCorridor(RatesArray, () => {
            SetLotSize();
            RunStrategy();
            RaiseShowChart();
          }, false);
        } catch(Exception exc) {
          Log = exc;
        }
      };
      if(false && IsInVirtualTrading)
        Scheduler.CurrentThread.Schedule(TimeSpan.FromMilliseconds(1), () => a(0));
      else
        broadcastCorridorDatesChange.SendAsync(a);
    }
    partial void OnCorridorStartDateChanging(DateTime? value) {
      if(value == CorridorStartDate)
        return;
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
        if(value == DateTime.MinValue || RateLast == null || value > RateLast.StartDate) {
          _CorridorStopDate = value;
          CorridorStats.StopRate = null;
        } else {
          value = value.Min(RateLast.StartDate).Max(CorridorStartDate.GetValueOrDefault(CorridorStats.StartDate).Add((BarPeriodInt * 2).FromMinutes()));
          if(_CorridorStopDate == value && CorridorStats.StopRate != null)
            return;
          _CorridorStopDate = value;
          if(value == RateLast.StartDate)
            CorridorStats.StopRate = RateLast;
          else {
            var index = RatesArray.IndexOf(new Rate() { StartDate2 = new DateTimeOffset(value) });
            CorridorStats.StopRate = RatesArray.Reverse<Rate>().SkipWhile(r => r.StartDate > value).First();
            if(CorridorStats.StopRate != null)
              _CorridorStopDate = CorridorStats.StopRate.StartDate;
          }
          if(CorridorStats.Rates.Any())
            StartStopDistance = CorridorStats.Rates.LastBC().Distance - CorridorStats.StopRate.Distance;
        }
        OnPropertyChanged("CorridorStopDate");
        _broadcastCorridorDateChanged();
      }
    }
    #endregion


    void ForEachSuppRes(params Action<SuppRes>[] actions) {
      (from sr in SuppRes
       from action in actions
       select new { sr, action }
      ).ForEach(a => a.action(a.sr));
    }
    double _buyLevelNetOpen() { return Trades.IsBuy(true).NetOpen(BuyLevel.Rate); }
    double _sellLevelNetOpen() { return Trades.IsBuy(false).NetOpen(SellLevel.Rate); }
    Action _adjustEnterLevels = () => { throw new NotImplementedException(); };
    Action<double?, double?> _adjustExitLevels = (buyLevel, selLevel) => { throw new NotImplementedException(); };
    Action _exitTrade = () => { throw new NotImplementedException(); };

    static Func<Rate, double> _priceAvg = rate => rate.PriceAvg;
    void SetCorridorStartDateAsync(DateTime? date) {
      Scheduler.CurrentThread.Schedule(TimeSpan.FromMilliseconds(1), () => CorridorStartDate = date);
    }
    double CurrentEnterPrice(bool? isBuy) { return CalculateLastPrice(GetTradeEnterBy(isBuy)); }
    IEnumerable<double> CurrentEnterPrices(Func<double, bool> predicate) { return CurrentEnterPrices().Where(predicate); }
    double[] CurrentEnterPrices() { return new[] { CurrentEnterPrice(false), CurrentEnterPrice(true) }; }
    double CurrentExitPrice(bool? isBuy) { return CalculateLastPrice(GetTradeExitBy(isBuy)); }

    double GetTradeCloseLevel(bool buy, double def = double.NaN) { return TradeLevelFuncs[buy ? LevelBuyCloseBy : LevelSellCloseBy]().IfNaN(def); }

    void SendSms(string header, object message, bool sendScreenshot) {
      if(sendScreenshot)
        RaiseNeedChartSnaphot();
      SendSms(header, message, sendScreenshot ? _lastChartSnapshot : null);
    }
    void SendSms(string header, object message, byte[] attachment) {
      if(!IsInVirtualTrading)
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
    public bool ToggleIsActive() {
      return IsTradingActive = !IsTradingActive;
    }
    public void ToggleCorridorStartDate() {
      FreezeCorridorStartDate(CorridorStartDate.HasValue);
    }
    public void UnFreezeCorridorStartDate() {
      FreezeCorridorStartDate(true);
    }
    public void FreezeCorridorStartDate(bool unFreeze = false) {
      if(!unFreeze)
        throw new NotSupportedException("FreezeCorridorStartDate is dosabled until further notice.");
      if(unFreeze)
        CorridorStartDate = null;
      else
        CorridorStartDate = CorridorStats.Rates.Last()?.StartDate;
    }


    public IList<Rate> CalcTrendLines(int start, int count, Func<TL, TL> map, bool useExtremas = false) {
      return UseRates(rates => {
        return rates.GetRange(start, count.Min(rates.Count - start).Max(0));
      })
      .Select(rates => CalcTrendLines(rates, count, map, useExtremas))
      .DefaultIfEmpty(new[] { TL.EmptyRate, TL.EmptyRate })
      .Single();
    }
    public IList<Rate> CalcTrendLines(int count, Func<TL, TL> map, bool useExtremas = false) {
      return UseRates(rates => {
        var c = count.Min(rates.Count);
        return count == 0 ? new List<Rate>() : rates.GetRange(rates.Count - c, c);
      })
      .Select(rates => CalcTrendLines(rates, count, map, useExtremas))
      .DefaultIfEmpty(new[] { TL.EmptyRate, TL.EmptyRate })
      .Single();
    }
    public IList<Rate> CalcTrendLines(List<Rate> source, int count, Func<TL, TL> map, bool useExtremas = false) {
      var c = count.Min(source.Count).Max(0);
      var range = source.Count == count ? source : source.GetRange(source.Count - c, c);
      return CalcTrendLines(range, map, useExtremas);
    }
    public IList<Rate> CalcTrendLines(List<Rate> corridorValues, Func<TL, TL> map, bool useExtremas = false) {
      if(corridorValues.Count == 0)
        return new[] { TL.EmptyRate, TL.EmptyRate };
      if(corridorValues[0] == null) {
        Log = new Exception("corridorValues[0] == null");
        return new[] { TL.EmptyRate, TL.EmptyRate };
      }

      var up = Lazy.Create(() => corridorValues.Select((r, i) => new { r, i }).MaxBy(r => r.r.AskHigh).First().i);
      var down = Lazy.Create(() => corridorValues.Select((r, i) => new { r, i }).MinBy(r => r.r.BidLow).First().i);
      if(useExtremas)
        corridorValues = corridorValues.GetRange(up.Value.Min(down.Value), up.Value.Abs(down.Value));

      var minutes = (corridorValues.Last().StartDate - corridorValues[0].StartDate).Duration().TotalMinutes;
      var isTicks = BarPeriod == BarsPeriodType.t1;
      var angleBM = HasTicks ? 1 / 60.0 : isTicks ? 0.1 : 1.0;
      Func<IList<Rate>, (double ask, double bid, double avg)> price = rl => (rl.Max(r => r.AskHigh), rl.Min(r => r.BidLow), rl.Average(r => r.PriceCMALast));
      Func<Rate, (double ask, double bid, double avg)> price0 = r => (r.AskHigh, r.BidLow, r.PriceCMALast);
      var groupped = corridorValues.GroupedDistinct(r => r.StartDate.AddMilliseconds(-r.StartDate.Millisecond), price);
      double h, l, h1, l1;
      var doubles = isTicks && BarPeriodCalc != BarsPeriodType.s1 ? groupped.ToList() : corridorValues.ToList(price0);
      if(doubles.Count < 5)
        return new List<Rate>();
      var coeffs = doubles.Select(t => t.Item3).ToArray().Linear();
      var stDev = CalcCorridorStDev(doubles, coeffs);
      var hl = stDev * CorridorSDRatio;
      h = hl * 2;
      l = hl * 2;
      h1 = hl * 3;
      l1 = hl * 3;
      var rates = new List<Rate> { (Rate)corridorValues[0].Clone(), (Rate)corridorValues.Last().Clone() };
      if(rates.Count == 1)
        return new[] { TL.EmptyRate, TL.EmptyRate };
      //var count = UseRates(rs => (rs.IndexOf(corridorValues.Last()) - rs.IndexOf(corridorValues[0])).Div(corridorValues.Count.Div(doubles.Count)).ToInt()).DefaultIfEmpty().Single();
      //if(count == 0)
      //  return new[] { TL.EmptyRate, TL.EmptyRate };
      var regRates = new[] { coeffs.RegressionValue(0), coeffs.RegressionValue(doubles.Count - 1) };

      var indexAdjust = corridorValues.Count.Div(doubles.Count).ToInt();
      Func<int, DateTime> indexToDate = i => corridorValues[(i * indexAdjust).Min(corridorValues.Count - 1)].StartDate;
      var edges = Enumerable.Range(0, doubles.Count)
        .Select(i => new { i, l = coeffs.RegressionValue(i) - h, h = coeffs.RegressionValue(i) + h, d = doubles[i] })
        .ToList();
      var edgeHigh = edges.OrderBy(x => x.d.Item1.Abs(x.h)).Select(x => Tuple.Create(indexToDate(x.i), InPips(x.d.Item1.Abs(x.h)), x.d.Item1)).First();
      var edgeLow = edges.OrderBy(x => x.d.Item2.Abs(x.l)).Select(x => Tuple.Create(indexToDate(x.i), InPips(x.d.Item2.Abs(x.l)), x.d.Item2)).First();

      rates.ForEach(r => r.Trends = map(new TL(corridorValues, coeffs, stDev, corridorValues.First().StartDate, corridorValues.Last().StartDate) {
        Angle = coeffs.LineSlope().Angle(angleBM, PointSize),
        EdgeHigh = new[] { edgeHigh },
        EdgeLow = new[] { edgeLow }
      }));


      rates[0].Trends.PriceAvg1 = regRates[0];
      rates[1].Trends.PriceAvg1 = regRates[1];
      rates[1].Trends.Height = regRates[1] - regRates[0];
      rates[1].Trends.Sorted = Lazy.Create(() => {
        var range = corridorValues.ToList();
        range.Sort(_priceAvg);
        return new[] { range[0], range.Last() };
      });

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
    // TODO: MinMaxMM
    private double CalcCorridorStDev(List<(double ask, double bid, double avg)> doubles, double[] coeffs) {
      var cm = Trades.Any() && CorridorCalcMethod != CorridorCalculationMethod.MinMax ? CorridorCalculationMethod.Height : CorridorCalcMethod;
      var ds = doubles.Select(r => r.avg);

      switch(CorridorCalcMethod) {
        case CorridorCalculationMethod.PowerMeanPower:

          return ds.ToArray().StDevByRegressoin(coeffs).RootMeanPower(ds.StandardDeviation(), 100);
        case CorridorCalculationMethod.Height:
          return ds.ToArray().StDevByRegressoin(coeffs);
        case CorridorCalculationMethod.MinMax:
          //var cmaPrices = UseRates(rates => rates.GetRange(doubles.Count)).SelectMany(rates => rates.Cma(r => r.PriceAvg, 1)).ToArray();
          var mm = ds.ToArray().MinMaxByRegressoin2(coeffs).Select(d => d.Abs()).Max();
          return mm / 2;
        case CorridorCalculationMethod.MinMaxMM:
          var mm2 = doubles.MinMaxByRegressoin2(t => t.bid, t => t.ask, coeffs).Select(d => d.Abs()).Min();
          return mm2 / 2;
        case CorridorCalculationMethod.RootMeanSquare:
          return ds.ToArray().StDevByRegressoin(coeffs).SquareMeanRoot(ds.StandardDeviation());
        default:
          throw new NotSupportedException(new { CorridorCalcMethod, Error = "Nosupported by CalcCorridorStDev" } + "");
      }
    }

    private static void OnCloseTradeLocal(IList<Trade> trades, TradingMacro tm) {
      tm.BuyCloseLevel.InManual = tm.SellCloseLevel.InManual = false;
      if(trades.Any(t => t.Pair == tm.Pair) && trades.Select(t => t.PL).DefaultIfEmpty().Sum() >= -tm.PriceSpreadAverage) {
        tm.BuyLevel.CanTradeEx = tm.SellLevel.CanTradeEx = false;
        if(!tm.IsInVirtualTrading && !tm.IsAutoStrategy)
          tm.IsTradingActive = false;
      }
      if(tm.CurrentGrossInPipTotal > 0) {
        tm.BuyLevel.CanTrade = tm.SellLevel.CanTrade = false;
        if(!tm.IsInVirtualTrading)
          tm.IsTradingActive = false;
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
        if(_WorkflowStep != value) {
          _WorkflowStep = value;
          OnPropertyChanged("WorkflowStep");
        }
      }
    }

    #endregion
    private void CloseTrading(string reason) {
      Log = new Exception("Closing Trading:" + reason);
      TradingStatistics.TradingMacros.ForEach(tm => {
        tm.CloseTrades(reason);
        tm.SuppRes.ForEach(sr1 => sr1.CanTrade = false);
      });
    }


    List<double> _CenterOfMassLevels = new List<double>();

    public List<double> CenterOfMassLevels {
      get { return _CenterOfMassLevels; }
      private set { _CenterOfMassLevels = value; }
    }

    public int FftMax { get; set; }

    public bool MustExitOnReverse { get; set; }

    double _corridorSDRatio = 1;
    [WwwSetting(wwwSettingsTradingCorridor)]
    [Category(categoryActive)]
    public double CorridorSDRatio {
      get { return _corridorSDRatio; }
      set {
        _corridorSDRatio = value;
        OnPropertyChanged(() => CorridorSDRatio);
      }
    }
  }
}
