using HedgeHog.Bars;
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
    bool MySelf(TradingMacro tm) { return tm == this; }
    bool MySelfNext(TradingMacro tm) { return tm.PairIndex > this.PairIndex; }
    #region Triggers
    [TradeDirectionTrigger]
    public void Limie() {
      var tlCount = TrendLines0Trends.Count;
      UseRates(rates => rates.GetRange(rates.Count - tlCount, tlCount.Div(1.05).ToInt())).ForEach(range => {
        var minMax = range.Select((r, i) => new { r, i }).MinMax(x => x.r.PriceAvg);
        var fibRange = Fibonacci.Levels(minMax[1].r.AskHigh, minMax[0].r.BidLow).Skip(4).Take(2).ToArray();
        CenterOfMassBuy = fibRange[1];
        CenterOfMassSell = fibRange[0];
        var isUp = minMax[0].r.StartDate < minMax[1].r.StartDate;
        if(HaveTrades())
          return;
        double buy, sell;
        if(isUp) {
          buy = minMax[1].r.AskHigh;
          sell = range.GetRange(range.Count - minMax[1].i).Select(r => r.BidLow).DefaultIfEmpty(minMax[0].r.BidLow).Min();
          if(!sell.Between(fibRange))
            return;
        } else {
          sell = minMax[0].r.BidLow;
          buy = range.GetRange(range.Count - minMax[0].i).Select(r => r.AskHigh).DefaultIfEmpty(minMax[1].r.AskHigh).Max();
          if(!buy.Between(fibRange))
            return;
        }
        if(BuyLevel.RateEx != buy && SellLevel.RateEx != sell)
          BuySellLevels.ForEach(sr => sr.CanTradeEx = false);
        if(BuySellLevels.Any(sr => sr.CanTrade) || InPips(buy.Abs(sell)) < GetVoltageHigh())
          return;
        BuyLevel.RateEx = buy;
        SellLevel.RateEx = sell;
      });
    }


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
          var startIndex = ra.Count - (TrendLinesTrends.Count - 10);
          var count = TrendLinesTrends.Count - TrendLines1Trends.Count + 10;
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
      TradeCorridorByGRB(TrendLines1Trends);
    }
    [TradeDirectionTrigger]
    public void OnOkDoBlue() {
      TradeCorridorByGRB(TrendLines2Trends);
    }
    [TradeDirectionTrigger]
    public void OnOkTip2() {
      TradeCorridorByTradeLevel2();
    }
    [TradeDirectionTrigger]
    public void OnOkTip3() {
      TradeCorridorByTradeLevel3();
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
    public void TradeCorridorByTradeLevel3() {
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
        var thinWaveOk = GRBRatioOk().HasAny();
        var canSetLevel = thinWaveOk && (!tlAvg.Between(SellLevel.Rate, BuyLevel.Rate) || tlHeight < bsHeight);
        //var currentPrice = new[] { CurrentEnterPrice(true), CurrentEnterPrice(false) };
        if(canSetLevel)
          lock (_rateLocker) {
            zip.ForEach(x => {
              var rate = x.bs;
              var rateJump = InPips(rate.Abs(x.sr.Rate));
              var reset = rateJump > 1;
              if(reset)
                x.sr.ResetPricePosition();
              x.sr.Rate = rate;
              if(reset)
                x.sr.ResetPricePosition();
            });
          }
        var tipOk = TradeConditionsEval().Single();
        var setTradeCount = false;
        bsl.ForEach(sr => {
          var canTrade = sr.IsBuy && tipOk.HasUp() || sr.IsSell && tipOk.HasDown();
          if(!IsTurnOnOnly || canTrade) {
            sr.CanTrade = canTrade;
            setTradeCount = canTrade;
          }
        });
        if(setTradeCount)
          bsl.ForEach(sr => sr.TradesCount = TradeCountStart);
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
    private bool CanTriggerTradeDirection() {
      var canTriggerTradeDirection = TrendLines2Trends.Count > BarsCount && _isRatesLengthStable;
      if(!canTriggerTradeDirection)
        Log = new Exception(new { canTriggerTradeDirection, _isRatesLengthStable } + "");
      return canTriggerTradeDirection;
    }

    //[TradeDirectionTrigger]
    public void OnAngRGOk() {
      var bs = new[] { BuyLevel, SellLevel };
      if(bs.Any(sr => sr.InManual))
        return;
      if(!TradeConditionsHaveTurnOff() && Trades.Length == 0 && TradeConditionsEval().Any(b => b.HasAny())) {
        var angCondsAll = new Dictionary<TradeConditionDelegate, Rate.TrendLevels> {
          { GreenAngOk,TrendLines1Trends  },
          { RedAngOk,TrendLinesTrends  },
          { BlueAngOk,TrendLines2Trends  }
        };
        var angConds = from ac in angCondsAll
                       join tc in TradeConditionsInfo() on ac.Key equals tc
                       select ac;
        var anonBS = MonoidsCore.ToFunc(0.0, 0.0, (buy, sell) => new { buy, sell });
        var getBS = MonoidsCore.ToFunc(0, count => {
          double buy, sell;
          RatesArray.GetRange(RatesArray.Count - count, count).Height(out sell, out buy);
          return anonBS(buy, sell);
        });
        var setLevels = MonoidsCore.ToFunc(anonBS(0, 0), x => {
          BuyLevel.ResetPricePosition();
          BuyLevel.Rate = x.buy;// GetTradeLevel(true, BuyLevel.Rate);
          SellLevel.ResetPricePosition();
          SellLevel.Rate = x.sell;// GetTradeLevel(false, SellLevel.RateEx);
                                  // init trade levels
          bs.ForEach(sr => {
            sr.TradesCount = TradeCountStart;
            sr.CanTrade = true;
            sr.InManual = true;
          });
          return true;
        });
        angConds
          //.Where(kv => kv.Key().HasAny()) //only need this for OR conditions
          .MaxBy(kv => kv.Value.Count)
          .Select(kv => getBS(kv.Value.Count))
          .Where(x => new[] { CurrentPrice.Ask, CurrentPrice.Bid }.All(cp => cp.Between(x.sell, x.buy)))
          .Select(x => MonoidsCore.ToFunc(() => setLevels(x)))
          .ForEach(a => a());
      }
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
    public void OnManuaColdMove() {
      (from bsl in BuySellLevels.ToSupressesList()
       where !HaveTrades()
       from x in bsl.IfAllManual()
       from y in x.IfAnyCanTrade()
       select y
        ).ForEach(_ => {
          var buy = CurrentEnterPrice(true) - BuyLevel.Rate;
          var sell = CurrentEnterPrice(false) - SellLevel.Rate;
          Func<SuppRes, bool> canTrade = sr => sr.TradesCount <= 0 && sr.CanTrade;
          Func<SuppRes, SuppRes, bool> isCold = (sr, sr2) => canTrade(sr) && canTrade(sr2);
          Action setCorr = () => BuySellLevels.ForEach(sr => sr.Rate = GetTradeLevel(sr.IsBuy, sr.Rate));
          var tradeLevels = BuySellLevels.Select(sr => sr.Rate).OrderByDescending(tl => tl);
          var isOut = CurrentEnterPrices()
              .OrderBy(cp => cp)
              .Zip(tradeLevels, (cp, tl) => new { cp, tl })
              .Buffer(2)
              .Select(b => b[0].cp > b[0].tl || b[1].cp < b[1].tl)
              .Single();
          if(isOut)
            Observable.Start(setCorr).Subscribe(x => Log = new Exception("Corridor moved"));
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
    [TradeDirectionTrigger]
    public void OnManualMove() {
      if(TradeConditionsHave(TimeFrameOk) && TimeFrameOk().HasAny()) {
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
      .Where(sr => sr.CanTrade)
      .Do(sr => {
        sr.CanTrade = false;
        if(!Trades.IsEmpty())
          sr.TradesCount = -(TradeCountMax + 1);
      })
      .Take(1)
      .ForEach(_ => Log = new Exception("TurnOfManualCorridor"));
    }


    [TradeDirectionTrigger]
    public void OnOutsideRed() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridorSelf, tm => tm.TrendLinesTrends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideRed2() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridor2Self, tm => tm.TrendLinesTrends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideGreen() {
      Func<Func<TradingMacro, Rate.TrendLevels>, TradeDirections> f = foo =>
        IsCurrentPriceOutsideCorridor(MySelf, foo, tl => tl.PriceAvg3, tl => tl.PriceAvg2, false);
      TriggerOnOutside(IsCurrentPriceOutsideCorridorSelf, tm => tm.TrendLines1Trends);
    }
    [TradeDirectionTrigger]
    public void OnOutsideBlue() {
      TriggerOnOutside(IsCurrentPriceOutsideCorridorSelf, tm => tm.TrendLines2Trends);
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
