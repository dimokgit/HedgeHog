using DynamicExpresso;
using HedgeHog.Bars;
using HedgeHog.Models;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ReactiveUI;
using System.Reactive.Disposables;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    private CompositeDisposable _strategyTradesCountHandler;
    private void StrategyEnterUniversal() {
      if (!RatesArray.Any() || !IsTrader) return;

      #region ============ Local globals ============
      #region Loss/Gross
      Func<double> currentGrossInPips = () => CurrentGrossInPipTotal;
      Func<double> currentLoss = () => CurrentLoss;
      Func<double> currentGross = () => CurrentGross;
      Func<bool> isCurrentGrossOk = () => CurrentGrossInPips >= -SpreadForCorridorInPips;
      #endregion
      _isSelfStrategy = true;
      var reverseStrategy = new ObservableValue<bool>(false);
      Func<SuppRes, bool> isBuyR = sr => reverseStrategy.Value ? !sr.IsBuy : sr.IsBuy;
      Func<SuppRes, bool> isSellR = sr => reverseStrategy.Value ? !sr.IsSell : sr.IsSell;
      Func<bool> calcAngleOk = () => TradingAngleRange >= 0
        ? CorridorAngleFromTangent().Abs() >= TradingAngleRange : CorridorAngleFromTangent().Abs() < TradingAngleRange.Abs();
      Func<double, bool> calcCorrOk = ratio => ratio >= 0
        ? CorridorStats.Rates.Count > CorridorDistance * ratio
        : CorridorStats.Rates.Count < CorridorDistance * -ratio;
      Action resetCloseAndTrim = () => CloseAtZero = _trimAtZero = _trimToLotSize = false;
      Func<bool, double> enter = isBuy => CalculateLastPrice(RateLast, GetTradeEnterBy(isBuy));
      #endregion

      #region ============ Init =================

      if (_strategyExecuteOnTradeClose == null) {
        Func<SuppRes, IObservable<EventPattern<EventArgs>>> onCanTrade = sr => Observable.FromEventPattern<EventHandler<EventArgs>, EventArgs>(h => h, h => sr.CanTradeChanged += h, h => sr.CanTradeChanged -= h);
        if (IsInVirtualTrading && Trades.Any()) throw new Exception("StrategyEnterUniversal: All trades must be closed befor strategy init.");
        if (IsInVirtualTrading) TurnOffSuppRes(RatesArray.Select(r => r.PriceAvg).DefaultIfEmpty().Average());
        #region SetTrendLines
        Func<Func<Rate, double>, double, double[]> getStDevByAverage1 = (price, skpiRatio) => {
          var line = new double[CorridorStats.Rates.Count];
          try {
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          } catch (IndexOutOfRangeException) {
            line = new double[CorridorStats.Rates.Count];
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          }
          var hl = CorridorStats.Rates.Select((r, i) => price(r) - line[i]).ToArray();
          var hlh = hl.Where(d => d > 0).ToArray();
          var h = hlh.Average();
          h = hlh.Where(d => d >= h).Average();
          var hll = hl.Where(d => d < 0).ToArray();
          var l = hll.Average();
          l = hll.Where(d => d < l).Average().Abs();
          return new[] { h / 2, l / 2 };
        };
        Func<Func<Rate, double>, double, double[]> getStDevByAverage = (price, skpiRatio) => {
          var line = new double[CorridorStats.Rates.Count];
          try {
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          } catch (IndexOutOfRangeException) {
            line = new double[CorridorStats.Rates.Count];
            CorridorStats.Coeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
          }
          var hl = CorridorStats.Rates.Select((r, i) => price(r) - line[i]).ToArray();
          var h = hl.Where(d => d > 0).Average();
          var l = hl.Where(d => d < 0).Average().Abs();
          return new[] { h, l };
        };
        #endregion
        Func<bool, Func<Rate, double>> tradeExit = isBuy => MustExitOnReverse ? _priceAvg : GetTradeExitBy(isBuy);
        Action onEOW = () => { };
        #region Exit Funcs
        #region exitOnFriday
        Func<bool> exitOnFriday = () => {
          if (!SuppRes.Any(sr => sr.InManual)) {
            bool isEOW = IsAutoStrategy && IsEndOfWeek();
            if (isEOW) {
              if (Trades.Any())
                CloseTrades(Trades.Lots(), "exitOnFriday");
              BuyLevel.CanTrade = SellLevel.CanTrade = false;
              return true;
            }
          }
          return false;
        };
        Func<bool?> exitOnCorridorTouch = () => {
          var mustSell = false;
          var mustBuy = false;
          var currentBuy = CalculateLastPrice(_priceAvg);
          var currentSell = currentBuy;
          if (currentBuy >= RateLast.PriceAvg2) mustSell = true;
          if (currentSell <= RateLast.PriceAvg3) mustBuy = true;
          if (Trades.HaveBuy() && mustSell || Trades.HaveSell() && mustBuy)
            MustExitOnReverse = true;
          return mustBuy ? mustBuy : mustSell ? false : (bool?)null;
        };
        #endregion
        #region exitByLimit
        Action exitByLimit = () => {
          if (!exitOnFriday() && CurrentGross > 0 && TradesManager.MoneyAndLotToPips(OpenTradesGross, Trades.Lots(), Pair) >= TakeProfitPips * ProfitToLossExitRatio)
            CloseAtZero = true;
        };
        #endregion
        #region exitByHarmonic
        Action exitByHarmonic = () => {
          if (CurrentGrossInPips >= _harmonics[0].Height)
            CloseAtZero = true;
        };
        #endregion
        #region exitByGrossTakeProfit
        Func<bool> exitByGrossTakeProfit = () =>
          Trades.Lots() >= LotSize * ProfitToLossExitRatio && TradesManager.MoneyAndLotToPips(-currentGross(), LotSize, Pair) <= TakeProfitPips;
        #endregion

        //TradesManager.MoneyAndLotToPips(-currentGross(), LotSizeByLossBuy, Pair)
        #region exitByLossGross
        Func<bool> exitByLossGross = () =>
          Trades.Lots() >= LotSize * ProfitToLossExitRatio && currentLoss() < currentGross() * ProfitToLossExitRatio;// && LotSizeByLossBuy <= LotSize;
        #endregion
        #region exitVoid
        Action exitVoid = () => { };
        #endregion
        #region exitByWavelette
        Action exitByWavelette = () => {
          double als = LotSizeByLossBuy;
          if (exitOnFriday()) return;
          var waveOk = WaveShort.Rates.Count < CorridorDistanceRatio;
          if (Trades.Lots() > als && waveOk)
            _trimAtZero = true;
          if (currentGrossInPips() >= TakeProfitPips || currentGross() > 0 && waveOk)
            CloseAtZero = true;
          else if (Trades.Lots() > LotSize && currentGross() > 0)
            _trimAtZero = true;
        };
        #endregion
        #region exitByTakeProfit
        Action exitByTakeProfit = () => {
          double als = LotSizeByLossBuy;
          if (exitOnFriday()) return;
          if (exitByGrossTakeProfit())
            CloseTrades(Trades.Lots() - LotSize, "exitByTakeProfit");
          else if (Trades.Lots() > LotSize && currentGross() > 0)
            _trimAtZero = true;
        };
        #endregion
        #region exitByJumpOut
        Action exitByJumpOut = () => {
          if (CorridorAngleFromTangent().Abs() < 1 && (ServerTime - Trades.Last().Time).TotalHours < 2) return;
          var revs = RatesArray.Reverse<Rate>().Take(5).Select(_priceAvg);
          if (Trades.HaveBuy() && revs.Max() > RateLast.PriceAvg2
          || Trades.HaveSell() && revs.Min() < RateLast.PriceAvg3)
            CloseTrades("exitByJumpOut");
        };
        #endregion
        #region exitFunc
        Func<Action> exitFunc = () => {
          if (!Trades.Any()) return () => { };
          switch (ExitFunction) {
            case Store.ExitFunctions.Void: return exitVoid;
            case Store.ExitFunctions.Friday: return () => exitOnFriday();
            case Store.ExitFunctions.GrossTP: return exitByTakeProfit;
            case Store.ExitFunctions.JumpOut: return exitByJumpOut;
            case Store.ExitFunctions.Wavelette: return exitByWavelette;
            case Store.ExitFunctions.Limit: return exitByLimit;
            case Store.ExitFunctions.Harmonic: return exitByHarmonic;
            case Store.ExitFunctions.CorrTouch: return () => exitOnCorridorTouch();
          }
          throw new NotSupportedException(ExitFunction + " exit function is not supported.");
        };
        #endregion
        #endregion
        #region TurnOff Funcs
        Action<double, Action> turnOffIfCorridorInMiddle_ = (sections, a) => {
          var segment = RatesHeight / sections;
          var rest = (RatesHeight - segment) / 2;
          var bottom = _RatesMin + rest;
          var top = _RatesMax - rest;
          var tradeLevel = (BuyLevel.Rate + SellLevel.Rate) / 2;
          if ((BuyLevel.CanTrade || SellLevel.CanTrade) && IsAutoStrategy && tradeLevel.Between(bottom, top)) a();
        };
        Action<Action> turnOffByCrossCount = a => { if (BuyLevel.TradesCount.Min(SellLevel.TradesCount) < TradeCountMax) a(); };
        Action<Action> turnOffByWaveHeight = a => { if (WaveShort.RatesHeight < RatesHeight * .75) a(); };
        Action<Action> turnOffByWaveShortLeft = a => { if (WaveShort.Rates.Count < WaveShortLeft.Rates.Count) a(); };
        Action<Action> turnOffByWaveShortAndLeft = a => { if (WaveShortLeft.Rates.Count < CorridorDistanceRatio && WaveShort.Rates.Count < WaveShortLeft.Rates.Count) a(); };
        Action<Action> turnOff = a => {
          switch (TurnOffFunction) {
            case Store.TurnOffFunctions.Void: return;
            case Store.TurnOffFunctions.WaveHeight: turnOffByWaveHeight(a); return;
            case Store.TurnOffFunctions.WaveShortLeft: turnOffByWaveShortLeft(a); return;
            case Store.TurnOffFunctions.WaveShortAndLeft: turnOffByWaveShortAndLeft(a); return;
            case Store.TurnOffFunctions.InMiddle_4: turnOffIfCorridorInMiddle_(4, a); return;
            case Store.TurnOffFunctions.InMiddle_5: turnOffIfCorridorInMiddle_(5, a); return;
            case Store.TurnOffFunctions.InMiddle_6: turnOffIfCorridorInMiddle_(6, a); return;
            case Store.TurnOffFunctions.InMiddle_7: turnOffIfCorridorInMiddle_(7, a); return;
            case Store.TurnOffFunctions.CrossCount: turnOffByCrossCount(a); return;
          }
          throw new NotSupportedException(TurnOffFunction + " Turnoff function is not supported.");
        };
        #endregion
        if (_adjustEnterLevels != null) _adjustEnterLevels.GetInvocationList().Cast<Action>().ForEach(d => _adjustEnterLevels -= d);
        if (BuyLevel != null) BuyLevel.Crossed -= null;
        if (SellLevel != null) SellLevel.Crossed -= null;
        Action<Trade> onCloseTradeLocal = null;
        Action<Trade> onOpenTradeLocal = null;

        #region Levels
        if (SuppResLevelsCount < 2) SuppResLevelsCount = 2;
        if (SuppRes.Do(sr => sr.IsExitOnly = false).Count() == 0) return; ;

        if (!IsInVirtualTrading) {
          var buySellCanTradeObservable = onCanTrade(BuyLevel).Merge(onCanTrade(SellLevel))
            .Select(e => e.Sender as SuppRes)
            .DistinctUntilChanged(sr => sr.CanTrade)
            .Where(sr => sr.CanTrade)
            .Subscribe(sr => SendSms(Pair + "::", new { sr.CanTrade }, false));
        }
        if (BuyLevel.Rate.Min(SellLevel.Rate) == 0) BuyLevel.RateEx = SellLevel.RateEx = RatesArray.Middle();
        BuyLevel.CanTrade = SellLevel.CanTrade = false;
        var _buySellLevels = new[] { BuyLevel, SellLevel }.ToList();
        Action<Action> onCorridorCrossesMaximumExeeded = a => _buySellLevels.Where(bs => -bs.TradesCount >= TradeCountMax).Take(1).ForEach(_ => a());
        ObservableValue<double> ghostLevelOffset = new ObservableValue<double>(0);
        Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
        Action<Func<SuppRes, bool>, Action<SuppRes>> _buySellLevelsForEachWhere = (where, a) => _buySellLevels.Where(where).ToList().ForEach(sr => a(sr));
        _buySellLevelsForEach(sr => sr.ResetPricePosition());
        Action<SuppRes, bool> setCloseLevel = (sr, overWrite) => {
          if (!overWrite && sr.InManual) return;
          sr.InManual = false;
          sr.CanTrade = false;
          if (sr.TradesCount != 9) sr.TradesCount = 9;
        };
        Func<SuppRes, SuppRes> suppResNearest = supRes => _suppResesForBulk().Where(sr => sr.IsSupport != supRes.IsSupport).OrderBy(sr => (sr.Rate - supRes.Rate).Abs()).First();
        Action<bool> setCloseLevels = (overWrite) => setCloseLevel(BuyCloseLevel, overWrite);
        setCloseLevels += (overWrite) => setCloseLevel(SellCloseLevel, overWrite);
        ForEachSuppRes(sr => {
          if (IsInVirtualTrading) sr.InManual = false;
          sr.ResetPricePosition();
          sr.ClearCrossedHandlers();
        });
        setCloseLevels(true);
        #region updateTradeCount
        Action<SuppRes, SuppRes> updateTradeCount = (supRes, other) => {
          if (supRes.TradesCount <= other.TradesCount && new[] { supRes, other }.Any(sr => sr.CanTrade))
            other.TradesCount = supRes.TradesCount - 1;
        };
        Func<SuppRes, SuppRes> updateNeares = supRes => {
          var other = suppResNearest(supRes);
          updateTradeCount(supRes, other);
          return other;
        };
        #endregion
        Func<bool, bool> onCanTradeLocal = canTrade => canTrade;
        Func<SuppRes, bool> canTradeLocal = sr => {
          var ratio = CanTradeLocalRatio;
          var corr = BuyLevel.Rate.Abs(SellLevel.Rate);
          var tradeDist = (sr.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (SellLevel.Rate - CurrentPrice.Bid));
          var tradeRange = ((sr.IsBuy ? (CurrentPrice.Ask - SellLevel.Rate) : (BuyLevel.Rate - CurrentPrice.Bid))) / corr;
          var canTrade = IsPrimaryMacro && (tradeRange < ratio || tradeDist <= PriceSpreadAverage);
          //if (!canTrade) 
          //Log = new Exception(canTrade + "");
          return onCanTradeLocal(canTrade);
        };
        Func<bool> isTradingHourLocal = () => IsTradingHour() && IsTradingDay();
        Func<SuppRes, bool> suppResCanTrade = (sr) =>
          !CanDoEntryOrders && IsTradingActive &&
          isTradingHourLocal() &&
          sr.CanTrade &&
          sr.TradesCount <= 0 &&
          !HasTradesByDistance(isBuyR(sr)) &&
          canTradeLocal(sr) &&
          IsPriceSpreadOk &&
          !IsEndOfWeek();
        Func<bool> isProfitOk = () => Trades.HaveBuy() && RateLast.BidHigh > BuyCloseLevel.Rate ||
          Trades.HaveSell() && RateLast.AskLow < SellCloseLevel.Rate;
        #endregion

        #region SuppRes Event Handlers
        Func<bool> isCrossActive = () => _buySellLevels.Any(sr => !sr.CanTrade) || BuyLevel.Rate.Abs(SellLevel.Rate) > InPoints(1);
        Func<SuppRes, bool> isCrossDisabled = sr =>
          !isCrossActive() ||
          !IsTradingActive ||
          !IsPrimaryMacro ||
          (!sr.IsExitOnly && !sr.InManual && !CanOpenTradeByDirection(sr.IsBuy));
        Action<double> onTradesCount = tc => { };
        if (_strategyTradesCountHandler == null)
          _strategyTradesCountHandler = (CompositeDisposable)BuyLevel.WhenAnyValue(x => x.TradesCount).Merge(SellLevel.WhenAnyValue(x => x.TradesCount))
            .Throttle(TimeSpan.FromSeconds(.5))
            .Subscribe(tc => onTradesCount(tc));
        #region enterCrossHandler
        Func<SuppRes, bool> enterCrossHandler = (suppRes) => {
          if (CanDoEntryOrders || CanDoNetStopOrders || (reverseStrategy.Value && !suppRes.CanTrade) || isCrossDisabled(suppRes)) return false;
          if (suppRes.InManual || suppRes.CanTrade) {
            var isBuy = isBuyR(suppRes);
            var lot = Trades.IsBuy(!isBuy).Lots();
            var canTrade = suppResCanTrade(suppRes);
            if (canTrade) {
              lot += AllowedLotSizeCore();
              suppRes.TradeDate = ServerTime;
            }
            //var ghost = SuppRes.SingleOrDefault(sr => sr.IsExitOnly && sr.IsBuy == isBuy && sr.InManual && sr.CanTrade && sr.TradesCount <= 0);
            //if (ghost != null) {
            //  var real = _buySellLevels.Single(sr => sr.IsBuy == isBuy);
            //  if (real.IsBuy && real.Rate < ghost.Rate || real.IsSell && real.Rate > ghost.Rate)
            //    real.Rate = ghost.Rate;
            //}
            OpenTrade(isBuy, lot, "enterCrossHandler:" + new { isBuy, suppRes.IsExitOnly });
            return canTrade;
          } else return false;
        };
        #endregion
        #region exitCrossHandler
        Action<SuppRes> exitCrossHandler = (sr) => {
          if (CanDoNetLimitOrders || isCrossDisabled(sr)) return;
          var lot = Trades.Lots() - (_trimToLotSize ? LotSize.Max(Trades.Lots() / 2) : _trimAtZero ? AllowedLotSizeCore() : 0);
          resetCloseAndTrim();
          if (TradingStatistics.TradingMacros.Count > 1 && (
            CurrentGrossInPipTotal > PriceSpreadAverageInPips || CurrentGrossInPipTotal >= _tradingStatistics.GrossToExitInPips)
            )
            CloseTrading(new { exitCrossHandler = "", CurrentGrossInPipTotal, _tradingStatistics.GrossToExitInPips } + "");
          else
            CloseTrades(lot, "exitCrossHandler:" + new { sr.IsBuy, sr.IsExitOnly, CloseAtZero });
        };
        #endregion
        #endregion

        #region Crossed Events
        #region Enter Levels
        #region crossedEnter
        EventHandler<SuppRes.CrossedEvetArgs> crossedEnter = (s, e) => {
          var sr = (SuppRes)s;
          if (sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1) return;
          var srNearest = new Lazy<SuppRes>(() => suppResNearest(sr));
          updateTradeCount(sr, srNearest.Value);
          if (enterCrossHandler(sr)) {
            setCloseLevels(true);
          }
        };
        #endregion
        BuyLevel.Crossed += crossedEnter;
        SellLevel.Crossed += crossedEnter;
        #endregion
        #region ExitLevels
        Action<SuppRes> handleActiveExitLevel = (srExit) => {
          updateNeares(srExit);
          if (enterCrossHandler(srExit)) {
            setCloseLevels(true);
          }
        };
        EventHandler<SuppRes.CrossedEvetArgs> crossedExit = (s, e) => {
          var sr = (SuppRes)s;
          //if (reverseStrategy.Value && Trades.Any(t => t.IsBuy != sr.IsSell)) {
          //  exitCrossHandler(sr);
          //  return;
          //}
          if (sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1) return;
          if (sr.CanTrade) {
            if (sr.InManual) handleActiveExitLevel(sr);
            else crossedEnter(s, e);
          } else if (Trades.Any(t => t.IsBuy == sr.IsSell))
            exitCrossHandler(sr);
        };
        BuyCloseLevel.Crossed += crossedExit;
        SellCloseLevel.Crossed += crossedExit;
        #endregion
        #endregion

        #region adjustExitLevels
        Action<double, double> adjustExitLevels = AdjustCloseLevels();
        Action adjustExitLevels0 = () => adjustExitLevels(double.NaN, double.NaN);
        Action adjustExitLevels1 = () => {
          if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels);
          else adjustExitLevels(BuyLevel.Rate, SellLevel.Rate);
        };
        Action adjustExitLevels2 = () => {
          if(DoAdjustExitLevelByTradeTime)
            AdjustExitLevelsByTradeTime(adjustExitLevels);
          else
            adjustExitLevels0();
        };
        #endregion

        #region adjustLevels
        var firstTime = true;
        #region Watchers
        var watcherCanTrade = new ObservableValue<bool>(true, true);
        IList<MathExtensions.Extream<Rate>> extreamsSaved = null;

        var corridorMovedTrigger = new ValueTrigger<ValueTrigger<bool>>(false) { Value = new ValueTrigger<bool>(false) };
        var dateTrigger = new ValueTrigger<DateTime>(false);
        object any = null;
        object[] bag = null;
        #region Workflow tuple factories
        Func<List<object>> emptyWFContext = () => new List<object>();
        Func<List<object>, Tuple<int, List<object>>> tupleNext = e => Tuple.Create(1, e ?? new List<object>());
        Func<object, Tuple<int, List<object>>> tupleNextSingle = e => tupleNext(new List<object> { e });
        Func<Tuple<int, List<object>>> tupleNextEmpty = () => tupleNext(emptyWFContext());
        Func<List<object>, Tuple<int, List<object>>> tupleStay = e => Tuple.Create(0, e ?? new List<object>());
        Func<object, Tuple<int, List<object>>> tupleStaySingle = e => tupleStay(new[] { e }.ToList());
        Func<Tuple<int, List<object>>> tupleStayEmpty = () => tupleStay(emptyWFContext());
        Func<List<object>, Tuple<int, List<object>>> tuplePrev = e => Tuple.Create(-1, e ?? new List<object>());
        Func<List<object>, Tuple<int, List<object>>> tupleCancel = e => Tuple.Create(int.MaxValue / 2, e ?? new List<object>());
        Func<Tuple<int, List<object>>> tupleCancelEmpty = () => tupleCancel(emptyWFContext());
        Func<IList<object>, Dictionary<string, object>> getWFDict = l => l.OfType<Dictionary<string, object>>().SingleOrDefault();
        Func<IList<object>, Dictionary<string, object>> addWFDict = l => { var d = new Dictionary<string, object>(); l.Add(d); return d; };
        Func<IList<object>, Dictionary<string, object>> getAddWFDict = l => getWFDict(l) ?? addWFDict(l);
        #endregion
        Func<bool> cancelWorkflow = () => CloseAtZero;
        var workflowSubject = new Subject<IList<Func<List<object>, Tuple<int, List<object>>>>>();
        var workFlowObservable = workflowSubject
          .Scan(new { i = 0, o = emptyWFContext(), c = cancelWorkflow }, (i, wf) => {
            if (i.i >= wf.Count || i.c() || i.o.OfType<WF.MustExit>().Any(me => me())) {
              i.o.OfType<WF.OnExit>().ForEach(a => a());
              i.o.Clear();
              i = new { i = 0, o = i.o, i.c };
            }
            var o = wf[i.i](i.o);// Side effect
            o.Item2.OfType<WF.OnLoop>().ToList().ForEach(ol => ol(o.Item2));
            try {
              return new { i = (i.i + o.Item1).Max(0), o = o.Item2, i.c };
            } finally {
              if (o.Item1 != 0) workflowSubject.Repeat(1);
            }
          });

        var workflowSubjectDynamic = new Subject<IList<Func<ExpandoObject, Tuple<int, ExpandoObject>>>>();
        var workFlowObservableDynamic = workflowSubjectDynamic.WFFactory(cancelWorkflow);
        #endregion

        #region Funcs
        Func<Func<Rate, double>, double> getRateLast = (f) => f(RateLast) > 0 ? f(RateLast) : f(RatePrev);
        Func<bool> runOnce = null;
        #region initTradeRangeShift
        Action initTradeRangeShift = () => {
          Func<Trade, IEnumerable<Tuple<SuppRes, double>>> ootl_ = (trade) =>
            (from offset in (trade.IsBuy ? (CurrentPrice.Ask - BuyLevel.Rate) : (CurrentPrice.Bid - SellLevel.Rate)).Yield()
             from sr in new[] { BuyLevel, SellLevel }
             select Tuple.Create(sr, offset));
          Action<Trade> ootl = null;
          ootl = trade => {
            ootl_(trade).ForEach(tpl => tpl.Item1.Rate += tpl.Item2);
            onOpenTradeLocal -= ootl;
          };
          onCanTradeLocal = canTrade => {
            if (!canTrade) {
              if (!onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
                onOpenTradeLocal += ootl;
              }
            } else if (onOpenTradeLocal != null && onOpenTradeLocal.GetInvocationList().Select(m => m.Method.Name).Contains(ootl.Method.Name)) {
              onOpenTradeLocal -= ootl;
            }
            return true;
          };
        };
        #endregion
        #endregion

        #region adjustEnterLevels
        Func<Trade, bool> minPLOk = t => IsAutoStrategy && false ? CurrentGrossInPips == 0 : t.NetPL2 > 0;
        Action<Action> firstTimeAction = a => {
          if (firstTime) {
            Log = new Exception(new { CorrelationMinimum, CorridorDistance } + "");
            workFlowObservableDynamic.Subscribe();
            #region onCloseTradeLocal
            onCanTradeLocal = canTrade => canTrade || Trades.Any();
            onCloseTradeLocal += t => {
              var tpByGross = InPoints(_tradingStatistics.GetNetInPips()).Min(0).Abs() / 3;
              TakeProfitManual = (t.PL < 0 ? InPoints(t.PL.Abs() * 1.4).Max(TakeProfitManual) : double.NaN).Max(tpByGross);
              TakeProfitManual = double.NaN;
              BroadcastCloseAllTrades(this, tm => OnCloseTradeLocal(new[] { t }, tm));
            };
            #endregion
            if (a != null) a();
          }
        };
        Action adjustEnterLevels = () => {
          if (firstTime) {
            onOpenTradeLocal += t => { };
          }
          switch (TrailingDistanceFunction) {
            #region Dist
            #region DistAvgMin
            case TrailingWaveMethod.DistAvgMin:
            #region firstTime
            if (firstTime) {
              onCloseTradeLocal = t => {
                if (t.PL >= TakeProfitPips / 2) {
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = TradeCountMax; });
                  if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                }
              };
            }
            #endregion
            {
              var corridorRates = WaveShort.Rates;
              var buyLevel = CenterOfMassBuy;
              var sellLevel = CenterOfMassSell;
              if (!Trades.Any() || CurrentPrice.Average.Between(sellLevel, buyLevel)) {
                var rates = RatesArray.Select(_priceAvg).Where(r => r.Between(CenterOfMassSell, CenterOfMassBuy)).ToArray();
                var median = double.NaN;
                var offset = rates.StDev(out median) * 2;
                if (median > 0) {
                  BuyLevel.RateEx = median + offset;
                  SellLevel.RateEx = median - offset;
                }
              }

              try {
                var goTrade = GetVoltage(corridorRates.Last()) < GetVoltageHigh().Avg(GetVoltageAverage());
                corridorMovedTrigger.Set(goTrade
                  , (vt) => {
                    CloseAtZero = false;
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = goTrade && IsAutoStrategy; sr.ResetPricePosition(); });
                    corridorMovedTrigger.Value.Off();
                  }, corridorMovedTrigger.Value);
                corridorMovedTrigger.Value.Set(!goTrade || !isTradingHourLocal(), (vt) => {
                  corridorMovedTrigger.Off();
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; CloseAtZero = Trades.Any(); });
                });
              } catch (Exception exc) {
                Log = exc;
              }
            }
            if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
            break;
            #endregion
            #region DistAvgMax
            case TrailingWaveMethod.DistAvgMax:
            #region firstTime
            if (firstTime) {
              onCloseTradeLocal = t => {
                if (t.PL >= TakeProfitPips / 2) {
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = TradeCountMax; });
                  if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                }
              };
            }
            #endregion
            {
              Action setStopDate = () => {
                CorridorStats.StopRate = RateLast;
                _CorridorStopDate = CorridorStats.StopRate.StartDate;
              };
              var angleOk = calcAngleOk();
              var startDate = CorridorStats.StartDate;
              if (angleOk) {
                if (CorridorStopDate.IsMin()) setStopDate();
                else {
                  var cs = RatesArray.Reverse<Rate>().TakeWhile(r => r.StartDate >= startDate).ToArray()
                    .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
                  if (cs.StDevMin < CorridorStats.StDevMin && cs.Slope.Abs() <= CorridorStats.Slope.Abs())
                    setStopDate();
                }
              }
              BuyLevel.RateEx = RateLast.PriceAvg2;
              SellLevel.RateEx = RateLast.PriceAvg3;
              dateTrigger.Set(angleOk && dateTrigger.Value < startDate, (vt) => {
                if (IsAutoStrategy)
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = true; });
                vt.Off();
              }, startDate);
              if (!angleOk && IsAutoStrategy)
                _buySellLevelsForEach(sr => { sr.CanTradeEx = false; dateTrigger.Value = default(DateTime); });

            }
            if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
            break;
            #endregion
            #region DistAvgMinMax
            case TrailingWaveMethod.DistAvgMinMax:
            #region firstTime
            if (firstTime) {
              onCloseTradeLocal = t => {
                if (t.PL >= TakeProfitPips / 2) {
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = TradeCountMax; });
                  if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                }
              };
            }
            #endregion
            {
              {
                Action setStopDate = () => {
                  CorridorStats.StopRate = RateLast;
                  _CorridorStopDate = CorridorStats.StopRate.StartDate;
                };
                var angleOk = calcAngleOk();
                var startDate = CorridorStats.StartDate;
                if (angleOk) {
                  if (CorridorStopDate.IsMin()) setStopDate();
                  else {
                    var cs = RatesArray.Reverse<Rate>().TakeWhile(r => r.StartDate >= startDate).ToArray()
                      .ScanCorridorWithAngle(CorridorGetHighPrice(), CorridorGetLowPrice(), TimeSpan.Zero, PointSize, CorridorCalcMethod);
                    if (cs.StDevMin < CorridorStats.StDevMin && cs.Slope.Abs() <= CorridorStats.Slope.Abs())
                      setStopDate();
                  }
                }
              }
              var isCorridorOld = CorridorStopDate.IfMax(DateTime.MaxValue) < RatesArray.LastBC(CorridorDistance).StartDate;
              var corridorRates = WaveShort.Rates;
              var buyLevel = CenterOfMassBuy;
              var sellLevel = CenterOfMassSell;
              if (!Trades.Any() || CurrentPrice.Average.Between(sellLevel, buyLevel)) {
                var rates = RatesArray.Select(_priceAvg).Where(r => r.Between(CenterOfMassSell, CenterOfMassBuy)).ToArray();
                var median = double.NaN;
                var offset = rates.StDev(out median) * 2;
                if (median > 0) {
                  BuyLevel.RateEx = median + offset;
                  SellLevel.RateEx = median - offset;
                }
              }

              try {
                var goTrade = GetVoltage(corridorRates.Last()) < GetVoltageHigh().Avg(GetVoltageAverage());
                corridorMovedTrigger.Set(goTrade
                  , (vt) => {
                    CloseAtZero = false;
                    _buySellLevelsForEach(sr => { sr.CanTradeEx = goTrade && IsAutoStrategy; sr.ResetPricePosition(); });
                    corridorMovedTrigger.Value.Off();
                  }, corridorMovedTrigger.Value);
                corridorMovedTrigger.Value.Set(!goTrade || !isTradingHourLocal() || isCorridorOld, (vt) => {
                  corridorMovedTrigger.Off();
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; CloseAtZero = Trades.Any(); });
                });
              } catch (Exception exc) {
                Log = exc;
              }
            }
            if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
            break;
            #endregion
            #region DistAvgLT
            case TrailingWaveMethod.DistAvgLT:
            #region firstTime
            if (firstTime) {
              watcherCanTrade.Value = false;
              onCloseTradeLocal = t => {
                if (t.PL >= TakeProfitPips / 2) {
                  _buySellLevelsForEach(sr => { sr.CanTradeEx = false; sr.TradesCountEx = TradeCountMax; });
                  if (TurnOffOnProfit) Strategy = Strategy & ~Strategies.Auto;
                }
              };
            }
            #endregion
            {
              Func<double[], double[]> getLengthByAngle = ratesRev => {
                var start = 60;
                while (start < ratesRev.Length) {
                  if (AngleFromTangent(ratesRev.CopyToArray(start++).Regress(1).LineSlope(), () => CalcTicksPerSecond(CorridorStats.Rates)).Abs().ToInt() == 0)
                    break;
                }
                ratesRev = ratesRev.CopyToArray(start - 1);
                return new[] { ratesRev.Max(), ratesRev.Min() };
              };
              GeneralPurposeSubject.OnNext(() => {
                try {
                  var tail = CorridorDistance.Div(10).ToInt();
                  var range = extreamsSaved == null ? RatesInternal.Count
                    : (RatesArray.Count - RatesArray.IndexOf(extreamsSaved[0].Element) + tail).Min(CorridorDistance + tail);
                  UseRatesInternal(ri =>
                    ri.SafeArray().CopyToArray(ri.Count - range, range).Reverse().ToArray()).ForEach(ratesReversed => {
                      var extreamsAll = ratesReversed.Extreams(GetVoltage, CorridorDistance);
                      var extreams = extreamsAll.Where(e => e.Slope < 0).Take(2).ToArray();
                      if(range > RatesArray.Count && extreams.Length < 2)
                        throw new Exception("Range {0} is to short for two valleys.".Formater(RatesArray.Count));
                      Func<MathExtensions.Extream<Rate>, double> getLevel = e => ratesReversed.Skip(e.Index).Take(CorridorDistance).Average(_priceAvg);
                      var levels = extreams.Select(e => getLevel(e)).OrderBy(d => d).ToArray();
                      if(levels.Length == 1)
                        levels = new[] { levels[0], levels[0] > CenterOfMassBuy ? CenterOfMassBuy : CenterOfMassSell }
                          .OrderBy(d => d).ToArray();
                      if(levels.Length == 2) {
                        CenterOfMassBuy = levels[1];
                        CenterOfMassSell = levels[0];
                        extreamsSaved = extreamsAll;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      }
                    });
                } catch (Exception exc) {
                  Log = exc;
                }
              });
              var angleOk = calcAngleOk();
              var buy = RateLast.PriceAvg2;
              var sell = RateLast.PriceAvg3;
              var inRange = buy.Avg(sell).Between(CenterOfMassSell, CenterOfMassBuy);
              var canTrade = angleOk && inRange;
              watcherCanTrade.SetValue(canTrade, true, () => {
                BuyLevel.RateEx = buy;
                SellLevel.RateEx = sell;
                _buySellLevelsForEach(sr => sr.CanTradeEx = true);
              });
            }
            if (DoAdjustExitLevelByTradeTime) AdjustExitLevelsByTradeTime(adjustExitLevels); else adjustExitLevels1();
            break;
            #endregion
            #endregion
            #region FrameAngle
            #region FrameAngle
            case TrailingWaveMethod.FrameAngle:
            {
              #region firstTime
              if (firstTime) {
                Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (CurrentGrossInPipTotal > 0) {
                    if (!IsInVirtualTrading) IsTradingActive = false;
                    BroadcastCloseAllTrades();
                  }
                };
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait start";
                    var slopeCurrent = CorridorStats.Slope.Sign();
                    var slope = _ti.OfType<int>().FirstOrDefault(slopeCurrent);
                    if (corridorOk && slopeCurrent != slope) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                    }
                    return tupleStaySingle(slopeCurrent);
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #region FrameAngle2
            case TrailingWaveMethod.FrameAngle2:
            {
              #region firstTime
              if (firstTime) {
                Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (CurrentGrossInPipTotal > 0) {
                    if (!IsInVirtualTrading) IsTradingActive = false;
                    BroadcastCloseAllTrades();
                  }
                };
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;// && CorridorStats.Rates.Count > CorridorDistance * 2 && GetVoltageAverage() < WaveStDevRatio;
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait start";
                    var slopeCurrent = CorridorStats.Slope.Sign();
                    var slope = _ti.OfType<int>().FirstOrDefault(slopeCurrent);
                    if (corridorOk && (calcAngleOk() || slopeCurrent != slope)) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                        _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                        return tupleNextEmpty();
                    }
                    return tupleStaySingle(slopeCurrent);
                    },_ti =>{ WorkflowStep = "2 Wait Finish";
                    if (!corridorOk) return tupleNext(_ti);
                    if (CorridorStats.RatesHeight + point * 2 < BuyLevel.Rate.Abs(SellLevel.Rate)) {
                        BuyLevel.RateEx = CorridorStats.RatesMax + point;
                        SellLevel.RateEx = CorridorStats.RatesMin - point;
                    }
                        return tupleStay(_ti);
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #region FrameAngle3
            case TrailingWaveMethod.FrameAngle3:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates => rates.CopyLast((CorridorDistance * WaveStDevRatio).ToInt()).First().StartDateContinuous;
                Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (!IsInVirtualTrading && (CurrentGrossInPipTotal > 0 || !IsAutoStrategy && t.PL > 0))
                    IsTradingActive = false;
                  if (CurrentGrossInPipTotal > 0)
                    BroadcastCloseAllTrades();
                };
                onOpenTradeLocal += WorkflowMixin.YAction<Trade>(h => t => {
                  onOpenTradeLocal.UnSubscribe(h, d => onOpenTradeLocal -= d);
                });
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;
              var corridorOk2 = CorridorStats.Rates.Count > CorridorDistance;
              var rl = CorridorStats.Rates.SkipWhile(r => r.PriceAvg1.IfNaN(0) == 0)
                .Memoize()
                .Yield(rates => new[] { rates.First(), rates.Last() });
              var getUpDown = rl.Select(r => new { up = r.Max(rate => rate.PriceAvg2), down = r.Min(rates => rates.PriceAvg3) });
              var getUpDown0 = new { up = CorridorStats.RatesMax + point, down = CorridorStats.RatesMin - point };
              Action<bool> setUpDown0 = reset => {
                if (reset || getUpDown0.up.Abs(getUpDown0.down) < BuyLevel.Rate.Abs(SellLevel.Rate)) {
                  BuyLevel.RateEx = getUpDown0.up;
                  SellLevel.RateEx = getUpDown0.down;
                }
              };
              Action<bool> setUpDown = reset => {
                getUpDown
                  .Where(ud => reset || ud.up.Abs(ud.down) < BuyLevel.Rate.Abs(SellLevel.Rate))
                  .ForEach(ud => {
                    BuyLevel.RateEx = ud.up;
                    SellLevel.RateEx = ud.down;
                  });
              };
              var corrInfoAnonFunc = MonoidsCore.ToFunc(DateTime.Now, 0, (d, s) => new { corrStartDate = d.ToBox(), slopeCurrent = s.ToBox() });
              var corrInfoAnon = corrInfoAnonFunc(DateTime.Now, 0);
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait start";
                      var slopeCurrent = CorridorStats.Slope.Sign();
                      if (!_ti.OfType(corrInfoAnon).Any())
                        _ti.Add(corrInfoAnonFunc(DateTime.MaxValue, slopeCurrent));
                      var corrInfo = _ti.OfType(corrInfoAnon).Single();
                      var startDate = corrInfo.corrStartDate;
                      var slope = corrInfo.slopeCurrent;
                      if (corridorOk && (calcAngleOk() || slopeCurrent != slope) ){
                          startDate.Value = ServerTime.AddMinutes(5 * BarPeriodsHigh);
                          setUpDown0(true);
                          return tupleNext(_ti);
                      }
                      setUpDown0(false);
                      startDate.Value = DateTime.MaxValue;
                      slope.Value = slopeCurrent;
                      return tupleStay(_ti);
                    },_ti =>{ WorkflowStep = "2 Wait Finish";
                      if (!corridorOk) return tupleCancelEmpty();
                      var startDate = _ti.OfType(corrInfoAnon).Single().corrStartDate;
                      if (startDate < ServerTime) {
                          _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                          return tupleNextEmpty();
                      }
                      return tupleStay(_ti);
                    },_ti =>{ WorkflowStep = "3 Wait Trade";
                      if (!corridorOk2) return tupleNextEmpty();
                      setUpDown0(false);
                      return tupleStay(_ti);
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #region FrameAngle31
            case TrailingWaveMethod.FrameAngle31:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates => rates.CopyLast((CorridorDistance * WaveStDevRatio).ToInt()).First().StartDateContinuous;
                Log = new Exception(new { CorridorDistance, WaveStDevRatio } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (!IsInVirtualTrading && (CurrentGrossInPipTotal > 0 || !IsAutoStrategy && t.PL > 0))
                    IsTradingActive = false;
                  if (CurrentGrossInPipTotal > 0)
                    BroadcastCloseAllTrades();
                };
                onOpenTradeLocal += WorkflowMixin.YAction<Trade>(h => t => {
                  onOpenTradeLocal.UnSubscribe(h, d => onOpenTradeLocal -= d);
                });
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var voltLine = GetVoltageHigh();
              Func<Rate, bool> isLowFunc = r => GetVoltage(r) < GetVoltageAverage();
              Func<Rate, bool> isHighFunc = r => GetVoltage(r) < GetVoltageHigh();
              Func<IList<Rate>, bool> calcVoltsBAOk = corridor => VoltsBelowAboveLengthMin >= 0
                ? corridor.Count > VoltsBelowAboveLengthMinCalc * BarPeriodInt
                : corridor.Count < -VoltsBelowAboveLengthMinCalc * BarPeriodInt;
              var funcQueue = new[] { isLowFunc, isLowFunc, isHighFunc, isHighFunc };
              var funcQueuePointer = 0;
              RatesArray.Reverse<Rate>()
                .SkipWhile(r => !funcQueue[0](r))
                .Select(r => new { r, ud = funcQueue[funcQueuePointer](r) })
                .DistinctUntilChanged(a => a.ud)
                .Do(_ => funcQueuePointer++)
                .Take(funcQueue.Length)
                .Buffer(funcQueue.Length)
                .Where(b => b.Count == funcQueue.Length && b[0].ud)
                .Select(b => new { left = b[3].r, right = b[2].r })
                .Select(a => RatesArray.SkipWhile(r => r < a.left).TakeWhile(r => r <= a.right).ToArray())
                .Where(calcVoltsBAOk)
                .Select(corridor => new { max = corridor.Max(r => r.AskHigh), min = corridor.Min(r => r.BidLow) })
                .ForEach(a => {
                  CenterOfMassBuy = a.max;
                  CenterOfMassSell = a.min;
                });
              var corridorOk = CorridorStats.Rates.Count / WaveStDevRatio > CorridorDistance;
              var isVoltHigh = new Func<bool>(() => GetVoltage(RateLast) > GetVoltageHigh()).Yield()
                .Select(f => f())
                .Do(b => {
                    //if (b) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  }).Memoize();
              var isVoltLow = new Func<bool>(() => GetVoltage(RateLast) < GetVoltageAverage()).Yield()
                .Select(f => f());
              var getUpDown = new { up = CenterOfMassBuy, down = CenterOfMassSell };
              Action setUpDown = () => {
                BuyLevel.RateEx = getUpDown.up;
                SellLevel.RateEx = getUpDown.down;
              };
              setUpDown();
              var currentPriceOk = this.CurrentEnterPrice(null).Between(SellLevel.Rate, BuyLevel.Rate);
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait Start";
                    if (corridorOk && isVoltHigh.Single() && currentPriceOk) {
                        _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = true; });
                      }
                    if (isVoltLow.Single()) {
                      _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = false; });
                    }
                      return tupleStayEmpty();
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #region FrameAngle32
            case TrailingWaveMethod.FrameAngle32:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates => rates.CopyLast((CorridorDistance * WaveStDevRatio).ToInt()).First().StartDateContinuous;
                Log = new Exception(new { VoltsFrameLength } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (!IsInVirtualTrading && (CurrentGrossInPipTotal > 0 || !IsAutoStrategy && t.PL > 0))
                    IsTradingActive = false;
                  if (CurrentGrossInPipTotal > 0)
                    BroadcastCloseAllTrades();
                };
                onOpenTradeLocal += WorkflowMixin.YAction<Trade>(h => t => {
                  onOpenTradeLocal.UnSubscribe(h, d => onOpenTradeLocal -= d);
                });
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var voltLine = GetVoltageHigh();
              Func<Rate, bool> isLowFunc = r => GetVoltage(r) < GetVoltageAverage();
              {
                Func<Rate, bool> isHighFunc = r => GetVoltage(r) < GetVoltageHigh();
                Func<IList<Rate>, bool> calcVoltsBAOk = corridor => VoltsBelowAboveLengthMin >= 0
                  ? corridor.Count > VoltsBelowAboveLengthMinCalc * BarPeriodInt
                  : corridor.Count < -VoltsBelowAboveLengthMinCalc * BarPeriodInt;
                var funcQueue = new[] { isLowFunc, isLowFunc, isHighFunc, isHighFunc };
                var funcQueuePointer = 0;
                RatesArray.Reverse<Rate>()
                  .SkipWhile(r => !funcQueue[0](r))
                  .Select(r => new { r, ud = funcQueue[funcQueuePointer](r) })
                  .DistinctUntilChanged(a => a.ud)
                  .Do(_ => funcQueuePointer++)
                  .Take(funcQueue.Length)
                  .Buffer(funcQueue.Length)
                  .Where(b => b.Count == funcQueue.Length && b[0].ud)
                  .Select(b => new { left = b[3].r, right = b[2].r })
                  .Select(a => new { leftRight = a, rates = RatesArray.SkipWhile(r => r < a.left).TakeWhile(r => r <= a.right).ToArray() })
                  .Where(a => calcVoltsBAOk(a.rates))
                  .Select(a => new { a.leftRight, max = a.rates.Max(r => r.AskHigh), min = a.rates.Min(r => r.BidLow) })
                  .ForEach(a => {
                    CenterOfMassBuy = a.max;
                    CenterOfMassSell = a.min;
                    BuyLevel.RateEx = a.max;
                    SellLevel.RateEx = a.min;
                    this.CorridorStartDate = a.leftRight.left.StartDate;
                  });
              }
              var isVoltHigh = new Func<bool>(() => GetVoltage(RateLast) > GetVoltageHigh()).Yield()
                .Select(f => f()).Memoize();
              var isVoltLow = new Func<bool>(() => GetVoltage(RateLast) < GetVoltageAverage()).Yield()
                .Select(f => f());
              var getUpDown = MonoidsCore.ToFunc(() => new { up = CenterOfMassBuy, down = CenterOfMassSell }).Yield();
              var currentPriceOk = this.CurrentEnterPrice(null).Between(SellLevel.Rate, BuyLevel.Rate);
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait Start";
                    if (isVoltHigh.Single() && currentPriceOk) {
                        _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = true; });
                      }
                    if (isVoltLow.Single()) {
                      _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = false; });
                    }
                      return tupleStayEmpty();
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #region FrameAngle4
            case TrailingWaveMethod.FrameAngle4:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates => rates.CopyLast(CorridorDistance).First().StartDateContinuous;
                Log = new Exception(new { CorridorDistance, WaveStDevRatio, onCanTradeLocal } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCanTradeLocal = canTrade => canTrade || Trades.Any();
                onCloseTradeLocal += t => {
                  BuyCloseLevel.InManual = SellCloseLevel.InManual = false;
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (CurrentGrossInPipTotal > 0) {
                    if (!IsInVirtualTrading) IsTradingActive = false;
                    BroadcastCloseAllTrades();
                  }
                };
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              Func<Rate, double> getVolts = rate => rate.VoltageLocal0[0];
              var volts2 = RatesArray.Reverse<Rate>().SkipWhile(r => GetVoltage2(r).IsNaN()).ToList();
              var volts2Treshold = volts2.Select(GetVoltage2).ToArray().AverageByIterations(-3).DefaultIfEmpty(double.NaN).Average();
              var voltsDistinct = volts2.Select((r, i) => new { r, i, isDown = GetVoltage2(r) < volts2Treshold })
                .DistinctUntilChanged(a => a.isDown)
                .Where(a => a.isDown)
                .ToArray();
              var voltsZip = voltsDistinct.Zip(voltsDistinct.Skip(1), (a1, a2) => new { r1 = a1.r, r2 = a2.r, l = a2.i - a1.i, a1.i }).ToArray();
              var waveLengthIterations = WaveStDevRatio;
              var waveLengthTrashold = voltsZip.Select(a => (double)a.l).ToArray().AverageByIterations(waveLengthIterations).Average();
              var voltsZip2 = voltsZip
                .Take(1)
                .Where(a => a.l > waveLengthTrashold)
                .Select(a => volts2.GetRange(a.i, a.l))
                .Where(rates => rates.GetRange(rates.Count / 3, rates.Count / 3).Min(GetVoltage) < GetVoltageAverage())
                .ToArray();
              var tradeLevels = MonoidsCore.ToLazy(() => {
                var tradeCorr = RatesArray.CopyLast(CorridorStats.Rates.Count * 2).Take(CorridorStats.Rates.Count).ToArray();
                return new { max = tradeCorr.Max(_priceAvg), min = tradeCorr.Min(_priceAvg) };
              });
              Action setTradeLevels = () => {
                BuyLevel.RateEx = tradeLevels.Value.max;
                SellLevel.RateEx = tradeLevels.Value.min;
              };
              var corridorOk = voltsZip2.Where(v => CorridorStats.Rates.Count > CorridorDistance);
              var currentPriceOk = MonoidsCore.ToLazy(() => CurrentEnterPrice(null).Between(tradeLevels.Value.min, tradeLevels.Value.max));
              var getContext = MonoidsCore.ToFunc(0.0, 0.0, false, (Rate)null, (up, down, isUsed, rate) => new {
                up = CenterOfMassBuy = up,
                down = CenterOfMassSell = down,
                isUsed,
                rate,
                currPriceOk = MonoidsCore.ToFunc(() => CurrentEnterPrice(null).Between(down, up))
              });
              var getContext0 = MonoidsCore.ToFunc(() => getContext(0.0, 0.0, false, null));
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                  _ti =>{ WorkflowStep = "1 Wait corridorOk";
                    var context = _ti.OfType(getContext0).DefaultIfEmpty(getContext0).Single();
                    var replaceContext = MonoidsCore.ToFunc(context, newContext => {
                      _ti.Remove(context);
                      _ti.Add(newContext);
                      return newContext;
                    });
                    context = corridorOk.Take(1)
                      .Where(_ => context.down != tradeLevels.Value.min || context.up != tradeLevels.Value.max)
                      .Select(rates => replaceContext(getContext(tradeLevels.Value.max, tradeLevels.Value.min, false, rates[0])))
                      .DefaultIfEmpty(context)
                      .Single();
                    if (!context.isUsed && context.currPriceOk()) {
                      BuyLevel.RateEx = context.up;
                      SellLevel.RateEx = context.down;
                      _buySellLevelsForEach(sr => sr.CanTradeEx = true);
                      context = replaceContext(getContext(context.up, context.down, true, context.rate));
                      LineTimeMinFunc = rates => context.rate.StartDateContinuous;
                    }
                    return tupleStay(_ti);
                  }
                };
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #endregion
            #region StDevAngle
            case TrailingWaveMethod.StDevAngle:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates => rates.CopyLast((CorridorDistance * WaveStDevRatio).ToInt()).First().StartDateContinuous;
                Log = new Exception(new { CorridorDistance, WaveStDevRatio, onCanTradeLocal } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCanTradeLocal = canTrade => canTrade || Trades.Any();
                onCloseTradeLocal += t => {
                  BuyCloseLevel.InManual = SellCloseLevel.InManual = false;
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (CurrentGrossInPipTotal > 0) {
                    if (!IsInVirtualTrading) IsTradingActive = false;
                    BroadcastCloseAllTrades();
                  }
                };
                #endregion
              }
              #endregion
              var rate = CorridorStats.Rates.SkipWhile(r => r.PriceAvg1.IsNaN()).Take(1);
              var priceMAs = RatesArray.ToArray(GetPriceMA);
              var zips0 = MonoidsCore.ToLazy(() => priceMAs.Zip(LineMA, (m, l) => m.SignDown(l)).ToArray());
              var wavesCount = zips0.Value.Zip(zips0.Value.Skip(1), (z1, z2) => z1 != z2).Where(z => z);
              Action<double, double> setLevels = (buy, sell) => {
                BuyLevel.RateEx = buy;
                SellLevel.RateEx = sell;
                _buySellLevelsForEach(sr => sr.CanTradeEx = true);
              };
              (from r in rate
               where StDevByHeight < CorridorStats.StDevByPriceAvg
               let ma = GetPriceMA(r)
               let h = r.PriceAvg2 - r.PriceAvg1
               from levels in new[] { new[] { r.PriceAvg21 + h, r.PriceAvg21 }, new[] { r.PriceAvg31, r.PriceAvg31 - h } }
               select new { levels, active = ma.Between(levels[0], levels[1]) })
               .Where(a => a.active)
               .ForEach(a => setLevels(a.levels[0], a.levels[1]));

            }
            adjustExitLevels0();
            break;
            #endregion

            #region SimpleMove
            case TrailingWaveMethod.SimpleMove:
              {
                var conditions = MonoidsCore.ToFunc(() => new { TrailingDistanceFunction });
                var tci_ = MonoidsCore.ToFunc(() => TradeConditionsInfo((d, p, n) => new { n, v = d(), d }).ToArray());

                var toai = MonoidsCore.ToFunc(() => TradeOpenActionsInfo((d, n) => new { n, d }).ToArray());
                #region FirstTime
                if(firstTime && IsTrader) {
                  WorkflowStep = "";
                  Log = new Exception(conditions() + "");
                  ResetTakeProfitManual();
                  #region onCloseTradeLocal
                  onCanTradeLocal = canTrade => canTrade || Trades.Any();
                  var canTradeOff = !IsAutoStrategy;
                  #region turnItOff
                  Action<bool, Action> turnItOff = (should, a) => _buySellLevels
                    .Where(_ => should)
                    .Do(sr => {
                      if(!TradeConditionsHave(Tip2Ok))
                        sr.InManual = false;
                      sr.CanTrade = !IsAutoStrategy;
                      sr.TradesCount = TradeCountStart;
                    })
                    .ToArray()
                    .Take(1)
                    .Do(_ => {
                      UnFreezeCorridorStartDate();
                    })
                    .Where(_ => a != null)
                    .ForEach(_ => a());
                  #endregion
                  var hasTradeCountOff = _buySellLevels.Where(sr => sr.TradesCount < -TradeCountMax);
                  onCloseTradeLocal += t => {
                    if(!HaveTrades()) {
                      if(_buySellLevels.All(sr => sr.InManual && !sr.CanTrade))
                        _buySellLevelsForEach(sr => sr.InManual = false);
                      if(minPLOk(t) || hasTradeCountOff.Any()) {
                        BuyLevel.InManual = SellLevel.InManual = false;
                        turnItOff(true, () => {
                          if(canTradeOff)
                            IsTradingActive = false;
                        });
                      }
                      if(CurrentGrossInPipTotal > 0)
                        BroadcastCloseAllTrades();
                      BuyCloseLevel.InManual = SellCloseLevel.InManual = false;
                      CorridorStartDate = null;
                    }
                  };
                  #endregion
                  Action<Trade> canTradeByTradeCount = t =>
                    hasTradeCountOff
                    .Take(1)
                    .ForEach(_ => {
                      _buySellLevels
                      .ForEach(sr => {
                        sr.CanTrade = false;
                        sr.TradesCount = TradeCountStart;
                      });
                    });
                  onOpenTradeLocal += t => {
                    canTradeByTradeCount(t);
                    toai().ForEach(x => {
                      Log = new Exception("TradeOpenAction:" + x.n);
                      x.d(t);
                    });
                  };
                }
                #endregion
                if(IsTrader) {
                  exitFunc();
                  try {
                    TradeDirectionTriggersRun();
                    TradeConditionsTrigger();
                  } catch(Exception exc) {
                    Log = exc;
                  }
                }
                if(!TradeConditionsHaveSetCorridor())
                  SetTradeLevelsToLevelBy(GetTradeLevel)();
              }
            if (IsTrader)
              adjustExitLevels2();
            break;
            #endregion

            #region GreenStrip
            case TrailingWaveMethod.GreenStrip:
            if (firstTime) {
              Log = new Exception(new { CorrelationMinimum, CorridorDistance } + "");
              workFlowObservableDynamic.Subscribe();
              #region onCloseTradeLocal
              onCanTradeLocal = canTrade => canTrade || Trades.Any();
              onCloseTradeLocal += t => {
                var tpByGross = InPoints(_tradingStatistics.GetNetInPips()).Min(0).Abs() / 3;
                TakeProfitManual = tpByGross;
                BroadcastCloseAllTrades(this, tm => OnCloseTradeLocal(new[] { t }, tm));
              };
              #endregion
            }
            #region Set COMs
            var getPos = MonoidsCore.ToFunc(0.0, 0.0, (buy, sell) => new { buy, sell, pos = buy.Avg(sell).PositionRatio(_RatesMin, _RatesMax).Abs(0.5) });
            CorridorStats.Yield()
              .Where(cs => cs.Rates.Any() && AngleFromTangent(CorridorStats.Slope, () => CalcTicksPerSecond(cs.Rates)).Abs() < 0.1)
              .Select(cs => RatesArray.CopyLast(CorridorDistance).SkipLast(5).ToArray())
              .Select(rates => new { buy = rates.Max(_priceAvg), sell = rates.Min(_priceAvg) })
              .Where(tl => new[] { BuyLevel.Rate, SellLevel.Rate }.Zip(new[] { tl.buy, tl.sell }, (d1, d2) => d1 != d2).All(b => b))
              .SelectMany(tl => new[] { getPos(tl.buy, tl.sell), getPos(BuyLevel.Rate, SellLevel.Rate) })
              .Where(a => a.buy.Avg(a.sell).Abs(CurrentEnterPrice(null)) < RatesHeight / 2)
              .OrderByDescending(a => a.pos)
              .Take(1)
              .Where(tl => CurrentEnterPrice(null).Between(tl.sell, tl.buy))
              .ForEach(levels => {
                BuyLevel.RateEx = CenterOfMassBuy2 = levels.buy;
                SellLevel.RateEx = CenterOfMassSell2 = levels.sell;
                BuyLevel.CanTradeEx = SellLevel.CanTradeEx = true;
              });

            #endregion
            adjustExitLevels0();
            break;
            #endregion

            #region StDevFlat
            #region StDevFlat
            case TrailingWaveMethod.StDevFlat:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates => rates.CopyLast((CorridorDistance * WaveStDevRatio).ToInt()).First().StartDateContinuous;
                Log = new Exception(new { VoltsFrameLength, TradingAngleRange, VoltsBelowAboveLengthMin } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (!IsInVirtualTrading && (CurrentGrossInPipTotal > 0 || !IsAutoStrategy && t.PL > 0))
                    IsTradingActive = false;
                  if (CurrentGrossInPipTotal > 0)
                    BroadcastCloseAllTrades();
                };
                onOpenTradeLocal += WorkflowMixin.YAction<Trade>(h => t => {
                  onOpenTradeLocal.UnSubscribe(h, d => onOpenTradeLocal -= d);
                });
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var voltLine = GetVoltageHigh();
              {
                Func<IList<Rate>, bool> calcVoltsBAOk = corridor => VoltsBelowAboveLengthMin >= 0
                  ? corridor.Count > VoltsBelowAboveLengthMinCalc * BarPeriodInt
                  : corridor.Count < -VoltsBelowAboveLengthMinCalc * BarPeriodInt;
                var maxVolts = GetVoltageHigh;
                var minVolts = GetVoltageAverage;
                Func<Rate, bool> isInVerticalRange = (r) => GetVoltage(r).Between(minVolts(), maxVolts());
                RatesArray.Reverse<Rate>()
                  .SkipWhile(r => !isInVerticalRange(r))
                  .Select(r => new { r, isIn = isInVerticalRange(r) })
                  .DistinctUntilChanged(a => a.isIn)
                  .Take(2)
                  .Buffer(2)
                  .Select(b => new { left = b[1].r, right = b[0].r })
                  .Select(a => new { leftRight = a, rates = RatesArray.SkipWhile(r => r < a.left).TakeWhile(r => r <= a.right).ToArray() })
                  .Where(a => calcVoltsBAOk(a.rates))
                  .Select(a => new { a.leftRight, max = a.rates.Max(r => r.AskHigh), min = a.rates.Min(r => r.BidLow) })
                  .ForEach(a => {
                    CenterOfMassBuy = a.max;
                    CenterOfMassSell = a.min;
                    BuyLevel.RateEx = a.max;
                    SellLevel.RateEx = a.min;
                  });
              }
              var isVoltHigh = MonoidsCore.ToLazy(() => GetVoltage(RateLast) > GetVoltageHigh());
              var isVoltLow = MonoidsCore.ToLazy(() => GetVoltage(RateLast) < GetVoltageAverage());
              var getUpDown = MonoidsCore.ToLazy(() => new { up = CenterOfMassBuy, down = CenterOfMassSell });
              var currentPriceOk = MonoidsCore.ToLazy(() => this.CurrentEnterPrice(null).Between(SellLevel.Rate, BuyLevel.Rate));
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait Start";
                      if (isVoltLow.Value) {
                        _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = false; });
                      }else if(calcAngleOk() && currentPriceOk.Value)
                        _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = true; });
                      return tupleStayEmpty();
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #region StDevFlat2
            case TrailingWaveMethod.StDevFlat2:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates0 => rates0.CopyLast((CorridorDistance * WaveStDevRatio).ToInt()).First().StartDateContinuous;
                Log = new Exception(new { VoltsFrameLength, VoltsBelowAboveLengthMin } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (!IsInVirtualTrading && (CurrentGrossInPipTotal > 0 || !IsAutoStrategy && t.PL > 0))
                    IsTradingActive = false;
                  if (CurrentGrossInPipTotal > 0)
                    BroadcastCloseAllTrades();
                };
                onOpenTradeLocal += WorkflowMixin.YAction<Trade>(h => t => {
                  onOpenTradeLocal.UnSubscribe(h, d => onOpenTradeLocal -= d);
                });
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var voltsMax = GetVoltageHigh(); ;
              var voltsMin = GetVoltageAverage();
              var voltsRangeHeight = voltsMax.Abs(voltsMin);
              var voltsRangeStart = RatesArray.Min(GetVoltage);
              Func<Rate, double, double, bool> isInVerticalRange = (r, max, min) => GetVoltage(r).Between(min, max);
              var voltRanges = (
                from i in Range.Int32(1, 1000)
                let min = voltsMin + (i * point)
                select new { min, max = min + voltsRangeHeight }
                )
                .TakeWhile(a => a.max < voltsMin + voltsRangeHeight * 2);
              var rates = RatesArray.Reverse((r, i) => new { r, i }).ToArray();
              var rateSlots = (
                from range in voltRanges.AsParallel()
                select rates
                .Select(rate => new { rate.r, rate.i, isInside = isInVerticalRange(rate.r, range.min, range.max) })
                .SkipWhile(rate => !rate.isInside)
                .DistinctUntilChanged(rate => rate.isInside).ToArray()
                );
              var longestSlot = (
                from slot in rateSlots.AsParallel().Memoize()
                from z in (slot.Zip(slot.Skip(1), (s1, s2) => new { r1 = s1, r2 = s2, l = s2.i - s1.i })).AsParallel()
                where z.r1.isInside && z.l > VoltsBelowAboveLengthMin
                select z
                ).OrderByDescending(z => z.l)
                .Select(z => rates.SkipWhile(r => r.r > z.r1.r).Take(z.l).ToArray(r => r.r))
                .Take(1)
                .ToArray();
              var getUpDown = MonoidsCore.ToLazy(() => longestSlot.Select(ls =>
                new { up = ls.Max(r => r.AskHigh), down = ls.Min(r => r.BidLow), ls.Last().StartDate }).ToArray());
              getUpDown.Value.ForEach(ud => {
                CenterOfMassBuy = ud.up;
                CenterOfMassSell = ud.down;
              });
              var currentPriceOk = MonoidsCore.ToLazy(() =>
                (from ud in getUpDown.Value
                 select CurrentEnterPrice(null).Between(ud.up, ud.down)
                 ).Any());
              var setUpDown = getUpDown.Value
                .Where(_ => currentPriceOk.Value)
                .Do(ud => {
                  BuyLevel.RateEx = ud.up;
                  SellLevel.RateEx = ud.down;
                });
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait Start";
                    var dict = _ti.OfType<Dictionary<string, DateTime>>().FirstOrDefault();
                    if (dict == null) _ti.Add(dict = new Dictionary<string, DateTime>() { { "StartDate", DateTime.MinValue } });
                      var rangeDate = getUpDown.Value.Select(a => a.StartDate).DefaultIfEmpty().Single();
                      if (rangeDate > dict["StartDate"] && setUpDown.Any()) {
                      _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = true; });
                      dict["StartDate"] = rangeDate;
                    }
                      if(!getUpDown.Value.Any())
                        _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = false; });
                      return tupleStay(_ti);
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #region StDevFlat3
            case TrailingWaveMethod.StDevFlat3:
            {
              #region firstTime
              if (firstTime) {
                LineTimeMinFunc = rates0 => rates0.CopyLast((CorridorDistance * WaveStDevRatio).ToInt()).First().StartDateContinuous;
                Log = new Exception(new { VoltsFrameLength, VoltsBelowAboveLengthMin } + "");
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (t.PL > 0) _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                  if (!IsInVirtualTrading && (CurrentGrossInPipTotal > 0 || !IsAutoStrategy && t.PL > 0))
                    IsTradingActive = false;
                  if (CurrentGrossInPipTotal > 0)
                    BroadcastCloseAllTrades();
                };
                onOpenTradeLocal += WorkflowMixin.YAction<Trade>(h => t => {
                  onOpenTradeLocal.UnSubscribe(h, d => onOpenTradeLocal -= d);
                });
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              var point = InPoints(1);
              var voltsMax = GetVoltageHigh(); ;
              var voltsMin = GetVoltageAverage();
              var voltsRangeHeight = voltsMax.Abs(voltsMin);
              var voltsRangeStart = RatesArray.Min(GetVoltage);
              Func<Rate, double, double, bool> isInVerticalRange = (r, max, min) => GetVoltage(r).Between(min, max);
              var voltRanges = (
                from i in Range.Int32(1, 1000)
                let min = voltsMin + (i * point)
                select new { min, max = min + voltsRangeHeight }
                )
                .TakeWhile(a => a.max < voltsMin + voltsRangeHeight * 2);
              var rates = RatesArray.Reverse((r, i) => new { r, i }).ToArray();
              var rateSlots = (
                from range in voltRanges.AsParallel()
                select rates
                .Select(rate => new { rate.r, rate.i, isInside = isInVerticalRange(rate.r, range.min, range.max) })
                .SkipWhile(rate => !rate.isInside)
                .DistinctUntilChanged(rate => rate.isInside).ToArray()
                );
              var longestSlot = (
                from slot in rateSlots.ToArray().AsParallel()
                from z in (slot.Zip(slot.Skip(1), (s1, s2) => new { r1 = s1, r2 = s2, l = s2.i - s1.i })).AsParallel()
                where z.r1.isInside && z.l > VoltsBelowAboveLengthMin
                select z
                ).OrderByDescending(z => z.l)
                .Select(z => rates.SkipWhile(r => r.r > z.r1.r).Take(z.l).ToArray(r => r.r))
                .Take(1)
                .ToArray();
              var getUpDown = MonoidsCore.ToLazy(() => longestSlot.Select(ls =>
                new { up = ls.Max(r => r.AskHigh), down = ls.Min(r => r.BidLow), ls.Last().StartDate }).ToArray());
              getUpDown.Value.ForEach(ud => {
                CenterOfMassBuy = ud.up;
                CenterOfMassSell = ud.down;
              });
              var currentPriceOk = MonoidsCore.ToLazy(() =>
                (from ud in getUpDown.Value
                 select CurrentEnterPrice(null).Between(ud.up, ud.down)
                 ).Any());
              var setUpDown = getUpDown.Value
                //.Where(_ => currentPriceOk.Value)
                .Do(ud => {
                  BuyLevel.RateEx = ud.up;
                  SellLevel.RateEx = ud.down;
                });
              var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{ WorkflowStep = "1 Wait Start";
                    var dict = _ti.OfType<Dictionary<string, DateTime>>().FirstOrDefault();
                    if (dict == null) _ti.Add(dict = new Dictionary<string, DateTime>() { { "StartDate", DateTime.MinValue } });
                      var rangeDate = setUpDown.Select(a => a.StartDate).DefaultIfEmpty().Single();
                    if (rangeDate > dict["StartDate"]) {
                        _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = true; });
                        dict["StartDate"] = rangeDate;
                    }
                      if(!getUpDown.Value.Any())
                        _buySellLevelsForEach(sr => { sr.TradesCountEx = 0; sr.CanTradeEx = false; });
                      return tupleStay(_ti);
                    }};
              workflowSubject.OnNext(wfManual);
            }
            adjustExitLevels0();
            break;
            #endregion
            #endregion
            #region ManualRange
            case TrailingWaveMethod.ManualRange:
            {
              #region firstTime
              if (firstTime) {
                workFlowObservable.Subscribe();
                #region onCloseTradeLocal
                onCloseTradeLocal += t => {
                  if (minPLOk(t))
                    if (!IsAutoStrategy) {
                      IsTradingActive = false;
                      CorridorStartDate = null;
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    }
                  if (CurrentGrossInPipTotal > -PriceSpreadAverage) {
                    _buySellLevelsForEach(sr => sr.TradesCountEx = this.TradeCountMax);
                    BroadcastCloseAllTrades();
                  }
                };
                #endregion
                //initTradeRangeShift();
              }
              #endregion
              {
                #region Set locals
                var corridorRates = CorridorStats.Rates;
                var lastPrice = CurrentEnterPrice(null);
                var rateFirst = corridorRates.SkipWhile(r => r.PriceAvg1.IsZeroOrNaN()).Take(1).Memoize();
                var corridorOk = corridorRates.Count > CorridorDistance;
                Func<bool, bool, bool> setCanTrade = (condition, on) => { if (condition) _buySellLevelsForEach(sr => sr.CanTradeEx = on); return condition; };
                #endregion

                #region wfManual
                #region onTrade
                var tradesCountAnon = new { v = 0.ToBox() };
                var slope_a = new { slope_a = 0 };
                var slope_c = slope_a.ToFunc(0, slope => new { slope_a = slope });
                Func<bool> mustExit = () => BuyLevel.Rate.Abs(SellLevel.Rate) > InPoints(CorridorHeightMax) || !isTradingHourLocal();
                Func<List<object>, List<object>> onTrade = ti => {
                  Action<Trade> oto = t => {
                    var tc = ti.OfType(tradesCountAnon).Single();
                    if (tc.v > 0) {
                      _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                      CorridorStartDate = null;
                      ti.Add(new WF.MustExit(() => true));
                    };
                    tc.v.Value++;
                  };
                  ti.Add(tradesCountAnon);
                  ti.Add(new WF.MustExit(mustExit));
                  ti.Add(slope_c(CorridorStats.Slope.Sign()));
                  onOpenTradeLocal += oto;
                  ti.Add(new WF.OnExit(() => {
                    _buySellLevelsForEach(sr => sr.CanTradeEx = false);
                    onOpenTradeLocal -= oto;
                  }));
                  return ti;
                };
                #endregion
                #region Set Levels
                var bsLevels = rateFirst
                  .Select(rf => new {
                    buy = new { rate = rf.PriceAvg2 },
                    sell = new { rate = rf.PriceAvg3 }
                  });
                var setBSLevels = bsLevels
                  .Select(ud => new Action(() => {
                    BuyLevel.RateEx = ud.buy.rate;
                    SellLevel.RateEx = ud.sell.rate;
                  }));
                #endregion
                var wfManual = new Func<List<object>, Tuple<int, List<object>>>[] {
                    _ti =>{WorkflowStep = "1 Wait start";
                      setBSLevels.Do(a => a()).Any();
                      if (!mustExit())
                        return tupleNext(onTrade(_ti));
                      return tupleStay(_ti);
                    },_ti=>{WorkflowStep = "2 Wait Flat";
                      setBSLevels.Do(a => a()).Any();
                      if(CorridorStats.Slope.Sign() == _ti.OfType(slope_a).Single().slope_a)
                        return tupleStay(_ti);
                      setCanTrade(true, true);
                      return tupleNext(_ti);
                    },_ti=>{WorkflowStep = "3 Trading";
                      return tupleStay(_ti);
                    }
                  };
                #endregion
                workflowSubject.OnNext(wfManual);
              }
            }
            adjustExitLevels0();
            break;
            #endregion

            #region LongCross
            case TrailingWaveMethod.LongCross:
            #region FirstTime
            if (firstTime) {
              LogTrades = DoNews = !IsInVirtualTrading;
              Log = new Exception(new { WaveStDevRatio, DoAdjustExitLevelByTradeTime } + "");
              onCloseTradeLocal = t => { };
              onOpenTradeLocal = t => { };
            }
            #endregion
            {
              var middle = CorridorStats.Coeffs.LineValue();
              var levels = CorridorStats.Rates.Select(_priceAvg).ToArray().CrossedLevelsWithGap(InPoints(1));
              Func<Func<double, bool>, double> getLevel = f => levels.Where(t => f(t.Item1)).OrderByDescending(t => t.Item2).Select(t => t.Item1).DefaultIfEmpty(double.NaN).First();
              var levelUp = getLevel(l => l > middle);
              var levelDown = getLevel(l => l < middle);
              CenterOfMassBuy = levelUp.IfNaN(CenterOfMassBuy);
              CenterOfMassSell = levelDown.IfNaN(CenterOfMassSell);
              var isCountOk = CorridorStats.Rates.Count >= RatesArray.Count * WaveStDevRatio;// levels[0].Count < StDevLevelsCountsAverage;
              var bsHieght = CenterOfMassBuy.Abs(CenterOfMassSell);
              bool ok = isCountOk && bsHieght < CorridorStats.StDevByHeight * 2;
              if (ok) {
                BuyLevel.RateEx = CenterOfMassBuy;
                SellLevel.RateEx = CenterOfMassSell;
                if (IsAutoStrategy) {
                  _buySellLevelsForEach(sr => {
                    sr.CanTradeEx = true;
                  });
                }
              }
              if (IsAutoStrategy && (!isCountOk || bsHieght * 2.5 > RatesHeight))
                _buySellLevelsForEach(sr => { sr.CanTradeEx = false; });

            }
            adjustExitLevels1();
            break;
            #endregion
            default: throw new Exception(TrailingDistanceFunction + " is not supported.");
          }
          if (firstTime) {
            firstTime = false;
            ResetBarsCountCalc();
            ForEachSuppRes(sr => sr.ResetPricePosition());
            LogTrades = !IsInVirtualTrading;
          }
        };
        #endregion
        #endregion

        #region On Trade Close
        _strategyExecuteOnTradeClose = t => {
          if (!Trades.Any() && isCurrentGrossOk()) {
            CorridorStartDate = null;
            CorridorStopDate = DateTime.MinValue;
            LastProfitStartDate = CorridorStats.Rates.LastBC().StartDate;
          }

          if (onCloseTradeLocal != null)
            onCloseTradeLocal(t);
          if (TurnOffOnProfit && t.PL >= PriceSpreadAverageInPips) {
            Strategy = Strategy & ~Strategies.Auto;
          }
          CloseAtZero = _trimAtZero = _trimToLotSize = false;
        };
        #endregion

        #region On Trade Open
        _strategyExecuteOnTradeOpen = trade => {
          SuppRes.ForEach(sr => sr.ResetPricePosition());
          if (onOpenTradeLocal != null) onOpenTradeLocal(trade);
        };
        #endregion

        #region _adjustEnterLevels
        Action setLevelPrices = () => {
          try {
            if(IsTradingActive) {
              BuyLevel.SetPrice(enter(true));
              SellLevel.SetPrice(enter(false));
              BuyCloseLevel.SetPrice(CurrentExitPrice(true));
              SellCloseLevel.SetPrice(CurrentExitPrice(false));
            } else
              SuppRes.ForEach(sr => sr.ResetPricePosition());
          } catch(Exception exc) { Log = exc; }
        };
        _adjustEnterLevels += setLevelPrices;
        _adjustEnterLevels += adjustEnterLevels;
        _adjustEnterLevels += () => turnOff(() => _buySellLevelsForEach(sr => { if (IsAutoStrategy) sr.CanTradeEx = false; }));
        _adjustEnterLevels += () => exitFunc()();
        _adjustEnterLevels += setLevelPrices;
        _adjustEnterLevels += () => { if (runOnce != null && runOnce()) runOnce = null; };
        #endregion

      }

      #region if (!IsInVitualTrading) {
      if (!IsInVirtualTrading) {
        this.TradesManager.TradeClosed += TradeCloseHandler;
        this.TradesManager.TradeAdded += TradeAddedHandler;
      }
      #endregion

      #endregion

      #region ============ Run =============
      _adjustEnterLevels();
      #endregion
    }

    private Action SetTradeLevelsToLevelBy(Func<bool, double, double> getTradeLevel) {
      var isRange = BuyLevel.Rate < SellLevel.Rate;
      Func<double, double, double> buy = (r, p) => isRange ? r.Min(p) : r.Max(p);
      Func<double, double, double> sell = (r, p) => isRange ? r.Max(p) : r.Min(p);
      return new Action(() => {
        BuyLevel.RateEx = getTradeLevel(true, BuyLevel.Rate);
        //new[] { getTradeLevel(true, BuyLevel.Rate) }
        //   .SelectMany(rate => new[] { rate, buy(rate, CurrentEnterPrice(true)) })
        //   .Average();
        SellLevel.RateEx = getTradeLevel(false, SellLevel.Rate);
        //new[] { getTradeLevel(false, SellLevel.Rate) }
        //   .SelectMany(rate => new[] { rate, sell(rate, CurrentEnterPrice(false)) })
        //   .Average();
      });
    }
  }
}
