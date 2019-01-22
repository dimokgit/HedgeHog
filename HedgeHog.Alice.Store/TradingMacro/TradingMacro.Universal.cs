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
    private IDisposable _strategyTradesCountHandler;
    private void StrategyEnterUniversal() {
      if(!RatesArray.Any() || !IsTrader)
        return;

      #region ============ Local globals ============
      Func<bool, double> enter = isBuy => CalculateLastPrice(RateLast, GetTradeEnterBy(isBuy));
      #endregion

      #region ============ Init =================
      if(_strategyExecuteOnTradeClose == null) {
        if(IsInVirtualTrading && Trades.Any())
          throw new Exception("StrategyEnterUniversal: All trades must be closed befor strategy init.");
        if(IsInVirtualTrading)
          TurnOffSuppRes(RatesArray.Select(r => r.PriceAvg).DefaultIfEmpty().Average());

        if(_adjustEnterLevels != null)
          _adjustEnterLevels.GetInvocationList().Cast<Action>().ForEach(d => _adjustEnterLevels -= d);
        if(false) {
          if(BuyLevel != null)
            BuyLevel.Crossed -= null;
          if(SellLevel != null)
            SellLevel.Crossed -= null;
        }
        Action<Trade> onCloseTradeLocal = null;
        Action<Trade> onOpenTradeLocal = null;

        #region Levels
        if(SuppResLevelsCount < 2)
          SuppResLevelsCount = 2;
        if(SuppRes.Do(sr => sr.IsExitOnly = false).Count() == 0)
          return;
        ;

        BuyLevel.CanTrade = SellLevel.CanTrade = false;
        var _buySellLevels = new[] { BuyLevel, SellLevel }.ToList();
        Action<Action<SuppRes>> _buySellLevelsForEach = a => _buySellLevels.ForEach(sr => a(sr));
        _buySellLevelsForEach(sr => sr.ResetPricePosition());
        Action<SuppRes, bool> setCloseLevel = (sr, overWrite) => {
          if(!overWrite && sr.InManual)
            return;
          sr.InManual = false;
          sr.CanTrade = false;
          if(sr.TradesCount != 9)
            sr.TradesCount = 9;
        };
        Func<SuppRes, SuppRes> suppResNearest = supRes => _suppResesForBulk().Where(sr => sr.IsSupport != supRes.IsSupport).OrderBy(sr => (sr.Rate - supRes.Rate).Abs()).First();
        Action<bool> setCloseLevels = (overWrite) => setCloseLevel(BuyCloseLevel, overWrite);
        setCloseLevels += (overWrite) => setCloseLevel(SellCloseLevel, overWrite);
        ForEachSuppRes(sr => {
          if(IsInVirtualTrading)
            sr.InManual = false;
          sr.ResetPricePosition();
          sr.ClearCrossedHandlers();
        });
        setCloseLevels(true);
        #region updateTradeCount
        Action<SuppRes, SuppRes> updateTradeCount = (supRes, other) => {
          if(supRes.TradesCount <= other.TradesCount && new[] { supRes, other }.Any(sr => sr.CanTrade))
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
          canTradeLocal(sr) &&
          IsPriceSpreadOk &&
          !IsEndOfWeek();
        Func<bool> isProfitOk = () => Trades.HaveBuy() && RateLast.BidHigh > BuyCloseLevel.Rate ||
          Trades.HaveSell() && RateLast.AskLow < SellCloseLevel.Rate;
        #endregion

        #region SuppRes Event Handlers
        Func<SuppRes, bool> isCrossDisabled = sr =>
          //!isCrossActive() ||
          !IsTradingActive ||
          !IsPrimaryMacro ||
          (!sr.IsExitOnly && !sr.InManual && !CanOpenTradeByDirection(sr.IsBuy));
        Action<double> onTradesCount = tc => { };
        if(_strategyTradesCountHandler == null) {
          _strategyTradesCountHandler = BuyLevel.WhenAnyValue(x => x.TradesCount).Merge(SellLevel.WhenAnyValue(x => x.TradesCount))
            .Throttle(TimeSpan.FromSeconds(.5))
            .Subscribe(tc => onTradesCount(tc));
        }
        #region enterCrossHandler
        Func<SuppRes, bool> enterCrossHandler = (suppRes) => {
          if(CanDoEntryOrders || CanDoNetStopOrders ||  !suppRes.CanTrade || isCrossDisabled(suppRes) || HaveTrades(suppRes.IsBuy) || isHedgeChild || IsHedgedTrading)
            return false;
          if(suppRes.InManual || BuyLevel.Rate > SellLevel.Rate || suppRes.CanTrade) {
            var isBuy = suppRes.IsBuy;
            var lot = Trades.IsBuy(!isBuy).Lots();
            var canTrade = suppResCanTrade(suppRes);
            if(canTrade) {
              lot += AllowedLotSizeCore(isBuy);
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
          } else
            return false;
        };
        #endregion
        #region exitCrossHandler
        Action<SuppRes> exitCrossHandler = (sr) => {
          if((!IsInVirtualTrading && CanDoNetLimitOrders) || isCrossDisabled(sr) || IsHedgedTrading || isHedgeChild || HaveHedgedTrades())
            return;
          var lot = Trades.Lots();
          if(TradingStatistics.TradingMacros.Distinct(tm => tm.Pair).Count() > 1 && (
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
          if(sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1)
            return;
          var srNearest = new Lazy<SuppRes>(() => suppResNearest(sr));
          updateTradeCount(sr, srNearest.Value);
          if(enterCrossHandler(sr)) {
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
          if(enterCrossHandler(srExit)) {
            setCloseLevels(true);
          }
        };
        EventHandler<SuppRes.CrossedEvetArgs> crossedExit = (s, e) => {
          var sr = (SuppRes)s;
          //if (reverseStrategy.Value && Trades.Any(t => t.IsBuy != sr.IsSell)) {
          //  exitCrossHandler(sr);
          //  return;
          //}
          if(sr.IsBuy && e.Direction == -1 || sr.IsSell && e.Direction == 1)
            return;
          if(sr.CanTrade) {
            if(sr.InManual)
              handleActiveExitLevel(sr);
            else
              crossedEnter(s, e);
          } else if(Trades.Any(t => t.IsBuy == sr.IsSell))
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
          if(DoAdjustExitLevelByTradeTime)
            AdjustExitLevelsByTradeTime(adjustExitLevels);
          else
            adjustExitLevels(BuyLevel.Rate, SellLevel.Rate);
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

        var corridorMovedTrigger = new ValueTrigger<ValueTrigger<bool>>(false) { Value = new ValueTrigger<bool>(false) };
        var dateTrigger = new ValueTrigger<DateTime>(false);
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
            if(i.i >= wf.Count || i.c() || i.o.OfType<WF.MustExit>().Any(me => me())) {
              i.o.OfType<WF.OnExit>().ForEach(a => a());
              i.o.Clear();
              i = new { i = 0, o = i.o, i.c };
            }
            var o = wf[i.i](i.o);// Side effect
            o.Item2.OfType<WF.OnLoop>().ToList().ForEach(ol => ol(o.Item2));
            try {
              return new { i = (i.i + o.Item1).Max(0), o = o.Item2, i.c };
            } finally {
              if(o.Item1 != 0)
                workflowSubject.Repeat(1);
            }
          });

        var workflowSubjectDynamic = new Subject<IList<Func<ExpandoObject, Tuple<int, ExpandoObject>>>>();
        var workFlowObservableDynamic = workflowSubjectDynamic.WFFactory(cancelWorkflow);
        #endregion

        #region Funcs
        Func<Func<Rate, double>, double> getRateLast = (f) => f(RateLast) > 0 ? f(RateLast) : f(RatePrev);
        Func<bool> runOnce = null;
        #endregion

        #region adjustEnterLevels
        Func<Trade, bool> minPLOk = t => IsAutoStrategy && false ? CurrentGrossInPips == 0 : t.NetPL2 > 0;
        Action adjustEnterLevels = () => {
          if(firstTime) {
            onOpenTradeLocal += t => { };
          }
          switch(TrailingDistanceFunction) {
            #region SimpleMove
            case TrailingWaveMethod.SimpleMove: {
                var conditions = MonoidsCore.ToFunc(() => new { TrailingDistanceFunction });
                var tci_ = MonoidsCore.ToFunc(() => TradeConditionsInfo((d, p, n) => new { n, v = d(), d }).ToArray());

                var toai = MonoidsCore.ToFunc(() => TradeOpenActionsInfo((d, n) => new { n, d }).ToArray());
                Action setLevels = () => {
                  if(IsAsleep || IsHedgedTrading || isHedgeChild) {
                    TradingMacrosByPair()
                    .OrderByDescending(tm => tm._RatesMax - tm._RatesMin)
                    .Take(1)
                    .ForEach(tm => {
                      var offset = (tm._RatesMax - tm._RatesMin) / 20;
                      SellLevel.Rate = tm._RatesMax + offset;
                      BuyLevel.Rate = tm._RatesMin - offset;
                    });
                  } else if(!TradeConditionsHaveSetCorridor())
                    TradeConditionsCanSetCorridor()
                    .Where(td => td.HasAny())
                    .ForEach(_ => SetTradeLevelsToLevelBy(GetTradeLevel)());
                  if(false && IsTrader)
                    adjustExitLevels2();

                };
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
                      //if(!TradeConditionsHave(Tip2Ok))
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
                    //if(!HaveTrades()) {
                    if(_buySellLevels.All(sr => sr.InManual && !sr.CanTrade))
                      _buySellLevelsForEach(sr => sr.InManual = false);
                    if(minPLOk(t) || hasTradeCountOff.Any()) {
                      BuyLevel.InManual = SellLevel.InManual = false;
                      turnItOff(true, () => {
                        if(canTradeOff)
                          IsTradingActive = false;
                      });
                    }
                    if(false && CurrentGrossInPipTotal > 0)
                      BroadcastCloseAllTrades();
                    BuyCloseLevel.InManual = SellCloseLevel.InManual = false;
                    CorridorStartDate = null;
                    //}
                    setLevels();
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
                      Log = new Exception(nameof(TradeOpenAction) + ": " + x.n);
                      x.d(t);
                    });
                  };
                }
                #endregion
                setLevels();
                if(IsTrader) {
                  try {
                    TradeDirectionTriggersRun();
                    TradeConditionsTrigger();
                  } catch(Exception exc) {
                    Log = exc;
                  }
                }
              }
              break;
              #endregion
          }
          if(firstTime) {
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
          if(onCloseTradeLocal != null)
            onCloseTradeLocal(t);
          if(TurnOffOnProfit && t.PL >= PriceSpreadAverageInPips) {
            Strategy = Strategy & ~Strategies.Auto;
          }
          CloseAtZero = false;
        };
        #endregion

        #region On Trade Open
        _strategyExecuteOnTradeOpen = trade => {
          SuppRes.ForEach(sr => sr.ResetPricePosition());
          onOpenTradeLocal?.Invoke(trade);
          IsTradingActive = IsTradingActive || IsInVirtualTrading;
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
        _adjustEnterLevels += setLevelPrices;
        _adjustEnterLevels += () => { if(runOnce != null && runOnce()) runOnce = null; };
        #endregion

      }

      #region if (!IsInVitualTrading) {
      if(!IsInVirtualTrading) {
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
