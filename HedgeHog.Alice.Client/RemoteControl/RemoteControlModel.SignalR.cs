using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Alice.Store;
using HedgeHog.Bars;
using HedgeHog.DateTimeZone;
using HedgeHog.Shared;

namespace HedgeHog.Alice.Client {
  partial class RemoteControlModel {

    public object[] ServeChart(int chartWidth, DateTimeOffset dateStart, DateTimeOffset dateEnd, TradingMacro tm) {
      var cp = tm.CurrentPrice?.Average;
      var histVol = tm.StraddleRange(tm.StraddleGap).With(hv => new { hv.up, hv.down });
      var digits = tm.Digits();
      if(dateEnd > tm.LoadRatesStartDate2)
        dateEnd = tm.LoadRatesStartDate2;
      else
        dateEnd = dateEnd.AddMinutes(-tm.BarPeriodInt.Min(2));
      string pair = tm.Pair;
      Func<Rate, double> rateHL = rate => (rate.PriceAvg >= rate.PriceCMALast ? rate.PriceHigh : rate.PriceLow).Round(digits);
      #region map
      var rth = new[] { new TimeSpan(9, 30, 0), new TimeSpan(16, 0, 0) };
      var rthDates = MonoidsCore.ToFunc((DateTime dt) => dt.Date.With(d => rth.Select(h => d + h)).ToArray()).MemoizeLast(dt => dt.Date);
      bool isRth(DateTime dt) { return dt.Between(rthDates(dt)); }
      var doShowVolt = tm.VoltageFunction != VoltageFunction.None;
      var doShowVolt2 = tm.VoltageFunction2 != VoltageFunction.None || tm.VoltageFunction == VoltageFunction.PPMH;
      var lastVolt = tm.GetLastVolt().DefaultIfEmpty().Memoize();
      var lastVolt2 = tm.GetLastVolt(tm.GetVoltage2).DefaultIfEmpty().Memoize();
      var lastCma = tm.UseRates(TradingMacro.GetLastRateCma).SelectMany(cma => cma).FirstOrDefault();
      var tsMin = TimeSpan.FromMinutes(tm.BarPeriodInt);
      var priceHedge = MonoidsCore.ToFunc((Rate r) => r.PriceHedge).MemoizePrev(d => d.IsZeroOrNaN());
      var map = MonoidsCore.ToFunc((Rate)null, rate => new {
        //d = rate.StartDate2,
        d = tm.BarPeriod == BarsPeriodType.t1 ? rate.StartDate2 : rate.StartDate2.Round().With(d => d == rate.StartDate2 ? d : d + tsMin),
        c = rateHL(rate),
        v = doShowVolt ? tm.GetVoltage(rate).IfNaNOrZero(lastVolt) : 0,
        v2 = doShowVolt2 ? tm.GetVoltage2(rate).IfNaNOrZero(lastVolt2) : 0,
        m = rate.PriceCMALast.IfNaNOrZero(lastCma).Round(digits),
        a = rate.AskHigh.Round(digits),
        b = rate.BidLow.Round(digits),
        h = isRth(rate.StartDate.InNewYork()),
        p = priceHedge(rate)
      });
      #endregion
      var exit = false;// doShowVolt && lastVolt.IsEmpty() || doShowVolt2 && lastVolt2.IsEmpty();
      if(exit || tm.RatesArray.Count == 0 || tm.IsTrader && tm.BuyLevel == null)
        return new[] { new { rates = new int[0] } };

      var tmTrader = tm.TradingMacroTrader().Single();
      var tpsHigh = tm.GetVoltageHigh().SingleOrDefault();
      var tpsLow = tm.GetVoltageAverage().SingleOrDefault();
      var tps2High = tm.GetVoltage2High().Where(v => !v.IsNaN()).ToArray();
      var tps2Low = tm.GetVoltage2Low().Where(v => !v.IsNaN()).ToArray();
      var tpsCurr2 = tm.UseRates(ra => ra.BackwardsIterator().Select(tm.GetVoltage2).SkipWhile(double.IsNaN).FirstOrDefault()).DefaultIfEmpty(0).Single();


      var ratesForChart = tm.UseRates(rates => rates.Where(r => r.StartDate2 >= dateEnd/* && !tm.GetVoltage(r).IsNaNOrZero()*/).ToList()).FirstOrDefault();
      if(ratesForChart == null)
        return new object[0];
      var ratesForChart2 = tm.UseRates(rates => rates.Where(r => r.StartDate2 < dateStart/* && !tm.GetVoltage(r).IsNaNOrZero()*/).ToList()).FirstOrDefault();
      if(ratesForChart2 == null)
        return new object[0];

      double cmaPeriod = tm.CmaPeriodByRatesCount();
      if(tm.IsTicks) {
        Action<IList<Rate>, Rate> volts = (gr, r) => {
          if(doShowVolt)
            tm.SetVoltage(r, gr.Select(tm.GetVoltage).Where(v => v.IsNotNaN()).DefaultIfEmpty(lastVolt.First()).Average());
          if(doShowVolt2)
            tm.SetVoltage2(r, gr.Select(tm.GetVoltage2).Where(v => v.IsNotNaN()).DefaultIfEmpty(lastVolt2.First()).Average());
        };
        cmaPeriod /= tm.TicksPerSecondAverage;
        if(ratesForChart.Count > 1)
          ratesForChart = TradingMacro.GroupTicksToSeconds(ratesForChart, volts).ToList();
        if(ratesForChart2.Count > 1)
          ratesForChart2 = TradingMacro.GroupTicksToSeconds(ratesForChart2, volts).ToList();
      }
      var getRates = MonoidsCore.ToFunc((IList<Rate>)null, rates3 => rates3.Select(map).ToList());
      var tradeLevels = !(tmTrader.Strategy.HasFlag(Strategies.Universal) && tmTrader.HasBuyLevel) ? new object { } : new {
        buy = tmTrader.BuyLevel.Rate.Round(digits),
        buyClose = tmTrader.BuyCloseLevel.Rate.Round(digits),
        canBuy = tmTrader.BuyLevel.CanTrade,
        manualBuy = tmTrader.BuyLevel.InManual,
        buyCount = tmTrader.BuyLevel.TradesCount,
        sell = tmTrader.SellLevel.Rate.Round(digits),
        sellClose = tmTrader.SellCloseLevel.Rate.Round(digits),
        canSell = tmTrader.SellLevel.CanTrade,
        manualSell = tmTrader.SellLevel.InManual,
        sellCount = tmTrader.SellLevel.TradesCount,
      };
      /*
      if (tm.IsAsleep) {
        var o = new object();
        var a = new object[0];
        return new {
          rates = getRates(ratesForChart),
          rates2 = getRates(ratesForChart2),
          ratesCount = tm.RatesArray.Count,
          dateStart = tm.RatesArray[0].StartDate2,
          trendLines = o,
          trendLines2 = o,
          trendLines1 = o,
          isTradingActive = tm.IsTradingActive,
          tradeLevels = o,
          trades = a,
          askBid = o,
          hasStartDate = tm.CorridorStartDate.HasValue,
          cmp = cmaPeriod,
          tpsAvg = 0,
          isTrader = tm.IsTrader,
          canBuy = false,
          canSell = false,
          waveLines = a
        };
      }
      */
      Func<Lazy<IList<Rate>>, IList<Rate>> safeTLs = tls => tls.Value ?? new List<Rate>();
      var trends = safeTLs(tm.TrendLines);
      var trendLines = new {
        dates = trends.Count > 1
        ? new DateTimeOffset[]{
          trends[0].StartDate2,
          trends[1].StartDate2}
        : new DateTimeOffset[0],
        close1 = trends.ToArray(t => t.Trends.PriceAvg1.Round(digits)),
        close2 = trends.ToArray(t => t.Trends.PriceAvg2.Round(digits)),
        close3 = trends.ToArray(t => t.Trends.PriceAvg3.Round(digits)),
        sel = TradingMacro.IsTrendsEmpty(trends).IsSelected
        //close21 = trends.ToArray(t => t.Trends.PriceAvg21.Round(digits)),
        //close31 = trends.ToArray(t => t.Trends.PriceAvg31.Round(digits))
      };
      var ratesLastStartDate2 = tm.RatesArray.Last().StartDate2;

      var trends2 = safeTLs(tm.TrendLines2);
      var trendLines2 = new {
        dates = trends2.Count == 0
        ? new DateTimeOffset[0]
        : new DateTimeOffset[]{
          trends2[0].StartDate2,
          trends2[1].StartDate2},
        close1 = trends2.ToArray(t => t.Trends.PriceAvg1.Round(digits)),
        close2 = trends2.ToArray(t => t.Trends.PriceAvg2.Round(digits)),
        close3 = trends2.ToArray(t => t.Trends.PriceAvg3.Round(digits)),
        sel = TradingMacro.IsTrendsEmpty(trends2).IsSelected
      };

      var trends0 = safeTLs(tm.TrendLines0);
      var trendLines0 = new {
        dates = trends0.Count == 0
        ? new DateTimeOffset[0]
        : new DateTimeOffset[]{
          trends0[0].StartDate2,
          trends0[1].StartDate2},
        close2 = trends0.ToArray(t => t.Trends.PriceAvg2.Round(digits)),
        close3 = trends0.ToArray(t => t.Trends.PriceAvg3.Round(digits)),
        sel = TradingMacro.IsTrendsEmpty(trends0).IsSelected
      };

      var trends1 = safeTLs(tm.TrendLines1);
      var trendLines1 = new {
        dates = trends1.Count == 0
        ? new DateTimeOffset[0]
        : new DateTimeOffset[]{
          trends1[0].StartDate2,
          trends1[1].StartDate2},
        close2 = trends1.ToArray(t => t.Trends.PriceAvg2.Round(digits)),
        close3 = trends1.ToArray(t => t.Trends.PriceAvg3.Round(digits)),
        sel = TradingMacro.IsTrendsEmpty(trends1).IsSelected
      };

      var trends3 = safeTLs(tm.TrendLines3);
      var trendLines3 = new {
        dates = trends3.Count == 0
        ? new DateTimeOffset[0]
        : new DateTimeOffset[]{
          trends3[0].StartDate2,
          trends3[1].StartDate2},
        close2 = trends3.ToArray(t => t.Trends.PriceAvg2.Round(digits)),
        close3 = trends3.ToArray(t => t.Trends.PriceAvg3.Round(digits)),
        sel = TradingMacro.IsTrendsEmpty(trends3).IsSelected
      };

      var waveLines = tm.WaveRangesWithTail
        .ToArray(wr => new {
          dates = new[] { wr.StartDate, wr.EndDate },
          isept = new[] { wr.InterseptStart, wr.InterseptEnd },
          isOk = wr.IsFatnessOk
        });
      var tmg = TradesManager;
      var trades0 = tmg.GetTrades();
      Trade[] getTrades(bool isBuy) => trades0.Where(t => t.IsBuy == isBuy).ToArray();
      var trades = new ExpandoObject();
      var tradeFoo = MonoidsCore.ToFunc((bool isBuy) => new { o = getTrades(isBuy).NetOpen(), t = getTrades(isBuy).Max(t => t.Time) });
      getTrades(true).Take(1).ForEach(_ => trades.Add(new { buy = tradeFoo(true) }));
      getTrades(false).Take(1).ForEach(_ => trades.Add(new { sell = tradeFoo(false) }));
      if(!tmg.TryGetPrice(pair, out var price)) return new object[0];
      var askBid = new { ask = price.Ask.Round(digits), bid = price.Bid.Round(digits) };
      var ish = tm.IsPairHedged;
      var hph = !tm.PairHedge.IsNullOrWhiteSpace();
      var ret = tm.UseRates(ratesArray => ratesArray.Take(1).ToArray(), x => x).ToArray(_ => new {
        rates = getRates(ratesForChart),
        rates2 = getRates(ratesForChart2),
        ratesCount = tm.RatesArray.Count,
        dateStart = tm.RatesArray[0].StartDate2,
        trendLines0,
        trendLines,
        trendLines2,
        trendLines1,
        trendLines3,
        isTradingActive = tmTrader.IsTradingActive,
        tradeLevels = tradeLevels,
        trades,
        askBid,
        hasStartDate = tm.CorridorStartDate.HasValue,
        cmp = cmaPeriod,
        tpsHigh,
        tps2Low,
        tps2High,
        tpsLow,
        tpsCurr2,
        isTrader = tm.IsTrader,
        canBuy = tmTrader.CanOpenTradeByDirection(true),
        canSell = tmTrader.CanOpenTradeByDirection(false),
        waveLines,
        barPeriod = tm.BarPeriodInt,
        ish,
        hph,
        vfs = tm.IsVoltFullScale ? 1 : 0,
        vfss = ish || tm.IsVoltFullScale ? tm.VoltsFullScaleMinMax : new[] { 0.0, 0.0 },
        histVol
      });
      return ret;
    }
  }
}
