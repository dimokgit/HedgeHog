﻿using HedgeHog.Bars;
using HedgeHog.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Reactive.Threading;
using System.Text.RegularExpressions;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {
    #region Trade Direction Triggers
    void TriggerOnOutside(Func<Func<TradingMacro, Rate.TrendLevels>, TradeDirections> isOutside, Func<TradingMacro, Rate.TrendLevels> trendLevels) {
      var td = isOutside(trendLevels);
      if(td.HasAny()) {
        if(TradeConditionsEval().All(b => b.HasAny()))
          TradeDirection = td;
        if(!HasTradeConditions) {
          BuyLevel.CanTradeEx = td.HasUp();
          SellLevel.CanTradeEx = td.HasDown();
        }
      }
    }
    #region Triggers
    [TradeDirectionTrigger]
    public void OnTradeCondOk() {
      if(!TradeConditionsHaveTurnOff() && Trades.Length == 0 && TradeConditionsEval().Any(b => b.HasAny())) {
        var bs = new[] { BuyLevel, SellLevel };
        double sell = GetTradeLevel(false, SellLevel.RateEx), buy = GetTradeLevel(true, BuyLevel.Rate);
        if(new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(sell, buy))) {
          BuyLevel.ResetPricePosition();
          BuyLevel.Rate = buy;// GetTradeLevel(true, BuyLevel.Rate);
          SellLevel.ResetPricePosition();
          SellLevel.Rate = sell;// GetTradeLevel(false, SellLevel.RateEx);
          // init trade levels
          bs.ForEach(sr => {
            sr.TradesCount = TradeCountStart;
            sr.CanTrade = true;
            sr.InManual = true;
          });
        }
      }
    }
    [TradeDirectionTrigger]
    public void OnBlueOk() {
      if(!TradeConditionsHaveTurnOff() && Trades.Length == 0 && TradeConditionsEval().Any(b => b.HasAny())) {
        var bs = new[] { BuyLevel, SellLevel };
        UseRates(ra => {
          var startIndex = ra.Count - (TLRed.Count - 10);
          var count = TLRed.Count - TLGreen.Count + 10;
          if(startIndex <= 0 || count >= startIndex)
            return new List<Rate>();
          return ra.GetRange(ra.Count - startIndex, count);
        })
        .Where(range => range.Any())
        .ForEach(range => {
          range.Sort(r => r.PriceAvg);
          var buy = range.Last().AskHigh;
          var sell = range.First().BidLow;
          //if(new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(sell, buy))) {
          BuyLevel.ResetPricePosition();
          BuyLevel.Rate = buy;// GetTradeLevel(true, BuyLevel.Rate);
          SellLevel.ResetPricePosition();
          SellLevel.Rate = sell;// GetTradeLevel(false, SellLevel.RateEx);
                                // init trade levels
          bs.ForEach(sr => {
            sr.TradesCount = TradeCountStart;
            sr.CanTrade = true;
            sr.InManual = true;
          });
          //}
        });
      }
    }
    [TradeDirectionTrigger]
    public void OnOkDoGreen() {
      TradeCorridorByGRB(TLGreen);
    }
    [TradeDirectionTrigger]
    public void OnOkDoBlue() {
      TradeCorridorByGRB(TLBlue);
    }
    [TradeDirectionTrigger]
    public void OnOkTip2() {
      TradeCorridorByTradeLevel2();
    }
    public void TradeCorridorByGRB(Rate.TrendLevels tls) {
      var tcEval = TradeConditionsEval().ToArray();
      if(BarsCountCalc > BarsCount && tcEval.Any(b => b.HasAny())) {
        var bs = new[] { BuyLevel, SellLevel };
        UseRates(ra => ra.GetRange(ra.Count - tls.Count, tls.Count))
          .Where(ra => ra.Count > 0)
          .Select(ra => new { buy = ra.Max(r => r.AskHigh), sell = ra.Min(r => r.BidLow), eval = tcEval.Single() })
          .Where(x => InPips(x.buy.Abs(x.sell)) > 5)
          .Where(x => new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(x.sell, x.buy)))
          .ForEach(x => {
            var buy = x.eval.HasUp() ? x.buy : UseRates(ra => ra.Max(r => r.AskHigh)).Single();
            var sell = x.eval.HasDown() ? x.sell : UseRates(ra => ra.Min(r => r.BidLow)).Single();
            var maxHeight = bs.Any(sr => sr.InManual) ? BuyLevel.Rate.Abs(SellLevel.Rate) : double.MaxValue;
            if(buy.Abs(sell) < maxHeight) {

              BuyLevel.ResetPricePosition();
              BuyLevel.Rate = buy;// GetTradeLevel(true, BuyLevel.Rate);

              SellLevel.ResetPricePosition();
              SellLevel.Rate = sell;// GetTradeLevel(false, SellLevel.RateEx);

              var hasUp = tcEval.First().HasUp();
              var hasDown = tcEval.First().HasDown();

              if(Trades.Length == 0) {
                BuyLevel.TradesCount = TradeCountStart + (hasUp ? 0 : 1);
                SellLevel.TradesCount = TradeCountStart + (hasDown ? 0 : 1);

                BuyLevel.CanTrade = true;
                SellLevel.CanTrade = true;

                bs.ForEach(sr => sr.InManual = true);
              }
            }
            //}
          });
      }
    }
    static object _rateLocker = new object();
    public void TradeCorridorByTradeLevel2() {
      if(CanTriggerTradeDirection() && Trades.Length == 0) {
        var bsl = new[] { BuyLevel, SellLevel };
        var buy = GetTradeLevel(true, double.NaN);
        var sell = GetTradeLevel(false, double.NaN);
        var zip = bsl.Zip(new[] { buy, sell }, (sr, bs) => new { sr, bs });
        zip.Where(x => !x.sr.InManual)
          .ForEach(x => {
            x.sr.InManual = true;
            x.sr.ResetPricePosition();
            x.sr.Rate = x.bs;
          });
        var bsHeight = BuyLevel.Rate.Abs(SellLevel.Rate);
        var tlHeight = buy.Abs(sell);
        var tlAvg = buy.Avg(sell);
        var canSetLevel = (!tlAvg.Between(SellLevel.Rate, BuyLevel.Rate) || tlHeight < bsHeight);
        var currentPrice = new[] { CurrentEnterPrice(true), CurrentEnterPrice(false) };
        if(canSetLevel)
          lock (_rateLocker) {
            var tipOk = TradeConditionsEval().Single();
            zip.ForEach(x => {
              var rate = x.bs;
              var rateJump = InPips(rate.Abs(x.sr.Rate));
              var reset = rateJump > 1;
              if(reset)
                x.sr.ResetPricePosition();
              x.sr.Rate = rate;
              if(reset)
                x.sr.ResetPricePosition();
              var canTrade = x.sr.IsBuy && tipOk.HasUp() || x.sr.IsSell && tipOk.HasDown();
              x.sr.CanTrade = canTrade;
            });
          }
      }
    }
    public void TradeCorridorTurnOnIfManual(TradeDirections tds) {
      var bsl = new[] { BuyLevel, SellLevel };
      if(tds.HasNone()) {
        if(!IsTurnOnOnly)
          bsl.ForEach(sr => sr.CanTrade = false);
      } else if(CanTriggerTradeDirection() && !TradeConditionsHaveTurnOff()) {
        var cps = new[] { CurrentEnterPrice(true), CurrentEnterPrice(false) };
        var canTradeAny = bsl.Any(sr => sr.CanTrade);
        Func<bool> isIn = () => cps.All(cp => cp.Between(bsl[1].Rate, bsl[0].Rate));
        Func<SuppRes, int> tradeCount = sr => sr.IsBuy ? tds.HasUp() ? 0 : 1 : tds.HasDown() ? 0 : 1;
        Func<SuppRes, bool> canTrade = sr => sr.IsBuy ? tds.HasUp() : tds.HasDown();
        if(!canTradeAny && bsl.All(sr => sr.InManual/* && isIn()*/))
          bsl
            .Where(sr => sr.HasRateCanTradeChanged)
            .ForEach(sr => {
              sr.ResetPricePosition();
              sr.CanTrade = canTrade(sr);
              sr.TradesCount = TradeCountStart;
            });
      }
    }

    bool AreTrendLevelsInSync<T>(Func<TradingMacro, Rate.TrendLevels> tls, Func<Rate.TrendLevels, T> eval) {
      return TradingMacrosByPair()
        .Select(tm => eval(tls(tm)))
        .Distinct()
        .Count() == 1;
    }
    bool _voltsOk = true;
    static ISubject<Action> _canTriggerTradeDirectionSubject = new Subject<Action>();
    static IDisposable _canTriggerTradeDirectionDisp = _canTriggerTradeDirectionSubject.Sample(5.FromSeconds()).Subscribe(a => a());
    private bool CanTriggerTradeDirection() {
      var canTriggerTradeDirection = RatesLengthBy != RatesLengthFunction.DistanceMinSmth || IsRatesLengthStable;
      if(IsInVirtualTrading && (!canTriggerTradeDirection || !_voltsOk))
        _canTriggerTradeDirectionSubject.OnNext(() => Log = new Exception(new { canTriggerTradeDirection, IsRatesLengthStable, _voltsOk } + ""));
      return canTriggerTradeDirection;
    }


    #region Move Trade corridor
    [TradeDirectionTrigger]
    public void OnCantTradeMove() {
      var bs = new[] { BuyLevel, SellLevel };
      if(bs.All(sr => sr.InManual)) {
        var buyOffset = CurrentPrice.Ask - BuyLevel.Rate;
        if(buyOffset > 0 && !BuyLevel.CanTrade)
          bs.ForEach(sr => sr.Rate += buyOffset);
        var sellOffset = CurrentPrice.Bid - SellLevel.Rate;
        if(sellOffset < 0 && !SellLevel.CanTrade)
          bs.ForEach(sr => sr.Rate += sellOffset);
      }
    }
    [TradeDirectionTrigger]
    public void OnPriceOutDisarm() {
      (from bsl in BuySellLevels.ToSupressesList()
       where !HaveTrades()
       from x in bsl.IfAllManual()
       from y in x.IfAnyCanTrade()
       from z in y.If(srs => !IsCurrentPriceInsideTradeLevels3(srs.Height()))
       select z
       ).ForEach(srs => srs.ForEach(sr => sr.InManual = sr.CanTrade = false));

    }
    [TradeDirectionTrigger]
    public void OnCantTradeFreeze() {
      (from bsl in BuySellLevels.ToSupressesList()
       from x in bsl.IfAllNonManual()
       from y in x.IfAnyCanTrade()
       select y
       ).ForEach(srs => srs.ForEach(sr => sr.InManual = true));
    }
    [TradeDirectionTrigger]
    public void OnManualColdMove() {
      (from bsl in BuySellLevels.ToSupressesList()
       where !HaveTrades()
       from x in bsl.IfAllManual()
         //from y in x.IfAnyCanTrade()
       select x
        ).ForEach(_ => {
          UseRates(rates => {
            var start = (rates.Count * .9).ToInt();
            var count = (rates.Count * 0.081).ToInt();
            return rates.GetRange(start, count);
          }).ForEach(tail => {
            tail
            .MinMax(r => r.AskHigh, r => r.BidLow)
            .Buffer(2)
            .ForEach(minMax => {
              Func<SuppRes, bool> canTrade = sr => sr.TradesCount <= 0 && sr.CanTrade;
              Func<SuppRes, SuppRes, bool> isCold = (sr, sr2) => canTrade(sr) && canTrade(sr2);
              Action setCorr = () => HaveTrades()
                .YieldIf(b => !b)
                .ForEach(__ => BuySellLevels.ForEach(sr => sr.Rate = GetTradeLevel(sr.IsBuy, sr.Rate)));
              var tradeLevels = BuySellLevels.ToArray(sr => sr.Rate);
              var isOut = minMax.Any(mm => !mm.Between(tradeLevels));
              if(isOut)
                Observable.Start(setCorr).Subscribe(x => Log = new Exception("Corridor moved"));
            });
          });
        });
    }


    [TradeDirectionTrigger]
    public void OnTradesCountMove() {
      if(Trades.Length == 0) {
        var bs = new[] { BuyLevel, SellLevel };
        if(bs.All(sr => sr.InManual)) {
          var buyOffset = CurrentPrice.Ask - BuyLevel.Rate;
          if(buyOffset > 0 && BuyLevel.TradesCount > 0)
            bs.ForEach(sr => sr.Rate += buyOffset);
          var sellOffset = CurrentPrice.Bid - SellLevel.Rate;
          if(sellOffset < 0 && SellLevel.TradesCount > 0)
            bs.ForEach(sr => sr.Rate += sellOffset);
        }
      }
    }
    #endregion

    [TradeDirectionTrigger]
    public void OnHaveTurnOff() {
      if(TradeConditionsHaveTurnOff() && Trades.IsEmpty())
        TurnOfManualCorridor();
    }

    private void TurnOfManualCorridor() {
      new[] { BuyLevel, SellLevel }
      .Where(sr => sr.InManual)
      .Do(sr => {
        sr.InManual = sr.CanTrade = false;
        if(!Trades.IsEmpty())
          sr.TradesCount = -(TradeCountMax + 1);
      })
      .Take(1)
      .ForEach(_ => Log = new Exception("TurnOfManualCorridor"));
    }


    #endregion

    #region Infrastructure
    public Action[] _tradeDirectionTriggers = new Action[0];
    bool HasTradeDirectionTriggers { get { return _tradeDirectionTriggers.Length > 0; } }
    public Action[] GetTradeDirectionTriggers() {
      return this.GetMethodsByAttibute<TradeDirectionTriggerAttribute>()
        .Select(me => (Action)me.CreateDelegate(typeof(Action), this))
        .ToArray();
    }
    public Action[] TradeDirectionTriggersSet(IList<string> names) {
      return _tradeDirectionTriggers = GetTradeDirectionTriggers().Where(tc => names.Contains(tc.Method.Name)).ToArray();
    }
    static IEnumerable<T> TradeDirectionTriggersInfo<T>(IList<Action> tradeDirectionTrggers, Func<Action, string, T> map) {
      return tradeDirectionTrggers.Select(tc => map(tc, tc.Method.Name));
    }
    public IEnumerable<T> TradeDirectionTriggersInfo<T>(Func<Action, string, T> map) {
      return TradeDirectionTriggersInfo(_tradeDirectionTriggers, map);
    }
    public IEnumerable<T> TradeDirectionTriggersAllInfo<T>(Func<Action, string, T> map) {
      return TradeDirectionTriggersInfo(GetTradeDirectionTriggers(), map);
    }
    void TradeDirectionTriggersRun() {
      if(IsTrader)
        TradeDirectionTriggersInfo((a, n) => a).ForEach(a => a());
    }
    [DisplayName("Trade Dir. Trgs")]
    [Description("Trade Direction Triggers")]
    [Category(categoryActiveFuncs)]
    public string TradeDirectionTriggerssSave {
      get { return string.Join(MULTI_VALUE_SEPARATOR, TradeDirectionTriggersInfo((tc, name) => name)); }
      set {
        TradeDirectionTriggersSet(value.Split(MULTI_VALUE_SEPARATOR[0]));
      }
    }
    #endregion
    #endregion
  }
}
