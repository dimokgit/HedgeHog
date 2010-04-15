using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Bars {
  public static class Extensions {
    /// <summary>
    /// Fills bar.PriceSpeed with angle from linear regression.
    /// Bars must be sorted by DateStart Descending
    /// </summary>
    /// <typeparam name="TBar"></typeparam>
    /// <param name="bars"></param>
    /// <param name="bar">bar to fill with speed</param>
    /// <param name="pricer">lambda with price source</param>
    public static void FillSpeed<TBar>(this List<TBar> bars, TBar bar, Func<TBar, double> price) where TBar : BarBase {
      (bars as IEnumerable<TBar>).FillSpeed(bar, price);
    }
    /// <summary>
    /// Fills bar.PriceSpeed with angle from linear regression.
    /// Bars must be sorted by DateStart Descending
    /// </summary>
    /// <typeparam name="TBar"></typeparam>
    /// <param name="bars"></param>
    /// <param name="bar">bar to fill with speed</param>
    /// <param name="pricer">lambda with price source</param>
    public static void FillSpeed<TBar>(this TBar[] bars, TBar bar, Func<TBar, double> price) where TBar : BarBase {
      (bars as IEnumerable<TBar>).FillSpeed(bar, price);
    }
    /// <summary>
    /// Fills bar.PriceSpeed with angle from linear regression.
    /// Bars must be sorted by DateStart Descending
    /// </summary>
    /// <typeparam name="TBar"></typeparam>
    /// <param name="bars"></param>
    /// <param name="bar">bar to fill with speed</param>
    /// <param name="pricer">lambda with price source</param>
    private static void FillSpeed<TBar>(this IEnumerable<TBar> bars,TBar bar,Func<TBar,double> price) where TBar:BarBase{
      double a, b;
      Lib.LinearRegression(bars.Select(price).ToArray(), out b, out a);
      bar.PriceSpeed = b;
    }

    public static void AddUp<TBar>(this List<TBar> ticks, IEnumerable<TBar> ticksToAdd) where TBar : BarBase {
      var lastDate = ticksToAdd.Min(t => t.StartDate);
      ticks.RemoveAll(t => t.StartDate > lastDate);
      ticks.AddRange(ticksToAdd);
    }
    public static IEnumerable<TBar> AddUp<TBar>(this IEnumerable<TBar> ticks, IEnumerable<TBar> ticksToAdd) where TBar : BarBase {
      var lastDate = ticksToAdd.Min(t => t.StartDate);
      return ticks.Where(t => t.StartDate <= lastDate).Concat(ticksToAdd.SkipWhile(t => t.StartDate == lastDate));
    }
    public static IEnumerable<TBar> AddDown<TBar>(this IEnumerable<TBar> ticks, IEnumerable<TBar> ticksToAdd) where TBar : BarBase {
      var lastDate = ticksToAdd.Max(t => t.StartDate);
      ticks = ticks.SkipWhile(t => t.StartDate <= lastDate).ToArray();
      return ticksToAdd.Concat(ticks);
    }

    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, DateTime bar1, DateTime bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1, bar2));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TBar bar1, TimeSpan periodTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1.StartDate, bar1.StartDate + periodTo));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TBar bar1, DateTime dateTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1.StartDate, dateTo));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TimeSpan periodFrom, TBar bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar2.StartDate - periodFrom, bar2.StartDate));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, DateTime dateFrom, TBar bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(dateFrom, bar2.StartDate));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TBar bar1, TBar bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1.StartDate, bar2.StartDate));
    }

    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, TBar barFrom, TBar barTo) where TBar : BarBase {
      return bars.TradesPerMinute(barFrom.StartDate, barTo.StartDate);
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, TBar barFrom, TimeSpan intervalTo) where TBar : BarBase {
      return bars.TradesPerMinute(barFrom.StartDate, barFrom.StartDate + intervalTo);
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, TimeSpan intervalFrom, TBar barTo) where TBar : BarBase {
      return bars.TradesPerMinute(barTo.StartDate-intervalFrom, barTo.StartDate);
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, DateTime DaterFrom, DateTime DateTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(DaterFrom, DateTo)).TradesPerMinute();
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      var count = bars.Count();
      var bo = bars.OrderBars().ToArray();
      return count / (bo.Last().StartDate - bo.First().StartDate).TotalMinutes;
    }

    public static Rate[] GetMinuteTicks<TBar>(this IEnumerable<TBar> fxTicks, int period, bool round) where TBar : BarBase {
      if (!round) return GetMinuteTicks(fxTicks, period);
      var timeRounded = fxTicks.Min(t => t.StartDate).Round().AddMinutes(1);
      return GetMinuteTicks(fxTicks.Where(t => t.StartDate >= timeRounded), period);
    }
    public static Rate[] GetMinuteTicks<TBar>(this IEnumerable<TBar> fxTicks, int period) where TBar : BarBase {
      var startDate = fxTicks.Min(t => t.StartDate);
      return (from t in fxTicks
              where period > 0
              group t by (((int)Math.Floor((t.StartDate - startDate).TotalMinutes) / period)) * period into tg
              orderby tg.Key
              select new Rate() {
                AskHigh = tg.Max(t => t.AskHigh),
                AskLow = tg.Min(t => t.AskLow),
                AskAvg = tg.Average(t => (t.AskHigh + t.AskLow) / 2),
                AskOpen = tg.First().AskOpen,
                AskClose = tg.Last().AskClose,
                BidHigh = tg.Max(t => t.BidHigh),
                BidLow = tg.Min(t => t.BidLow),
                BidAvg = tg.Average(t => (t.BidHigh + t.BidLow) / 2),
                BidOpen = tg.First().BidOpen,
                BidClose = tg.Last().BidClose,
                Mass = tg.Sum(t => t.Mass),
                StartDate = startDate.AddMinutes(tg.Key)
              }
                ).ToArray();
    }
    public static IEnumerable<Rate> GroupTicksToRates(this IEnumerable<Rate> ticks) {
      return from tick in ticks
             group tick by tick.StartDate into gt
             select new Rate() {
               StartDate = gt.Key,
               AskOpen = gt.First().AskOpen,
               AskClose = gt.Last().AskClose,
               AskHigh = gt.Max(t => t.AskHigh),
               AskLow = gt.Min(t => t.AskLow),
               BidOpen = gt.First().BidOpen,
               BidClose = gt.Last().BidClose,
               BidHigh = gt.Max(t => t.BidHigh),
               BidLow = gt.Min(t => t.BidLow),
               Mass = gt.Sum(t => t.Mass)
             };
    }

    static void FillPower<TBar>(this TBar[] barsSource, List<TBar> bars, double deleteRatio) where TBar : BarBase {
      barsSource.FillPower(bars.ToArray());
      var barsDelete = new List<TBar>();
      for (int i = 0; i < bars.Count - 2; i++)
        if (bars[i + 1].Ph.Work * deleteRatio < bars[i].Ph.Work && bars[i + 2].Ph.Work * deleteRatio < bars[i].Ph.Work)
          barsDelete.AddRange(new[] { bars[++i], bars[++i] });
      for (int i = 2; i < bars.Count; i++)
        if (bars[i - 1].Ph.Work * deleteRatio < bars[i].Ph.Work && bars[i - 2].Ph.Work * deleteRatio < bars[i].Ph.Work)
          barsDelete.AddRange(new[] { bars[i - 1], bars[i - 2] });
      barsDelete.Distinct().ToList().ForEach(b => bars.Remove(b));
    }
    public static void FillPower<TBar>(this TBar[] barsSource, TBar[] bars) where TBar : BarBase {
      foreach (var bar in bars)
        bar.Ph = null;
      TBar barPrev = null;
      foreach (var bar in bars)
        if (barPrev == null) barPrev = bar;
        else {
          barsSource.Where(bs => bs.StartDate.Between(bar.StartDate, barPrev.StartDate)).ToArray().FillPower(barPrev);
          barPrev = bar;
        }

    }
    public static void FillPower<TBar>(this TBar[] bars, TimeSpan period) where TBar : BarBase {
      bars.FillMass();
      var dateStart = bars.OrderBars().First().StartDate + period;
      foreach (var bar in bars.Where(b => b.StartDate > dateStart).OrderBars().Where(b => !b.Ph.Mass.HasValue).ToArray())
        bars.Where(b => b.StartDate.Between(bar.StartDate - period, bar.StartDate)).ToArray().FillPower(bar, period);
    }
    public static void FillPower<TBar>(this IEnumerable<TBar> bars, TBar barSource) where TBar : BarBase {
      bars.FillPower(barSource, TimeSpan.Zero);
    }
    public static void FillPower<TBar>(this IEnumerable<TBar> bars, TBar barSource, TimeSpan period) where TBar : BarBase {
      barSource.Ph.Mass = bars.SumMass();
      var barsOrdered = bars.OrderBy(b => b.PriceAvg);
      var barsMinMax = new[] { barsOrdered.First(), barsOrdered.Last() }.OrderBars().ToArray();
      barSource.Ph.Height = barsMinMax[1].PriceAvg - barsMinMax[0].PriceAvg;// bars.Last().PriceHeight(bars.First());
      var barsByDate = bars.OrderBars().ToArray();
      barSource.Ph.Time = (barsByDate.Last().StartDate - barsByDate.First().StartDate);
      barSource.Ph.Trades = bars.Count();
      if (bars.Count() == 0) {
        barsByDate.SaveToFile(b => b.PriceHigh, b => b.PriceLow, "C:\\bars.csv");
      }
      if (barSource.Ph.Time == TimeSpan.Zero) barSource.Ph.Time = period;
      if (period == TimeSpan.Zero) {
        barSource.Ph.Work = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.Work * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
        barSource.Ph.Power = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.Power * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
        barSource.Ph.K = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.K * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
      } else
        barSource.Ph.Work = barSource.Ph.Power = null;

      if (barSource != null) return;
    }

    public static IEnumerable<TBar> FixFractals<TBar>(this IEnumerable<TBar> fractals) where TBar : BarBase {
      var fractalsNew = new List<TBar>();
      foreach (var f in fractals)
        if (fractalsNew.Count > 0 && fractalsNew.Last().Fractal == f.Fractal)
          fractalsNew[fractalsNew.Count - 1] = BarBase.BiggerFractal(fractalsNew.Last(), f);
        else fractalsNew.Add(f);
      return fractalsNew;
    }
    public static void SaveToFile<T, D>(this IEnumerable<T> rates, Func<T, D> price, Func<T, D> price1, Func<T, D> price2, string fileName) where T : BarBase {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator,Indicator1,Indicator2" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate + "," + r.PriceClose + "," + price(r) + "," + price1(r) + "," + price2(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
        f.Close();
      }
    }
    public static void SaveToFile<T, D>(this IEnumerable<T> rates, Func<T, D> price, Func<T, D> price1, string fileName) where T : BarBase {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator,Indicator1" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate + "," + r.PriceClose + "," + price(r) + "," + price1(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
      }
    }

    public static void SaveToFile<T, D>(this IEnumerable<T> rates, Func<T, D> price, string fileName) where T : BarBase {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate + "," + r.PriceClose + "," + price(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
      }
    }

    //public static void RunTotal<TBar>(this IEnumerable<TBar> bars, Func<TBar, double?> source) where TBar : BarBase {
    //  bars.RunTotal(source, (barPrev, barNext) => barNext.RunningTotal = (barPrev ?? barNext).RunningTotal + source(barNext));
    //}
    public static void RunTotal<TBar>(this IEnumerable<TBar> bars, Func<TBar, double?> source) where TBar : BarBase {
      TBar barPrev = null;
      foreach (var bar in bars) {
        if (!bar.RunningTotal.HasValue) {
          bar.RunningTotal = ((barPrev ?? bar).RunningTotal ?? 0) + source(bar);
        }
        barPrev = bar;
      }
    }
    public static double SumMass<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      var mass = 0.0;
      foreach (var bar in bars.Where(b => b.Mass.HasValue))
        mass += bar.Mass.Value;
      return mass;
    }
    public static void FillMass<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      TBar barPrev = null;
      foreach (var bar in bars)
        if (barPrev == null) barPrev = bar;
        else {
          barPrev.Mass = Math.Abs(barPrev.PriceAvg - bar.PriceAvg);
          barPrev = bar;
        }
    }
    public static List<TBar> FindFractalTicks<TBar>(this IEnumerable<TBar> ticks, double waveHeight, TimeSpan period, double padRight, int count
  , TBar[] fractalsToSkip) where TBar : Rate {
      return ticks.FindFractalTicks(waveHeight, period, padRight, count, fractalsToSkip, r => r.PriceHigh, r => r.PriceLow);
    }
    public static List<TBar> FindFractalTicks<TBar>(this IEnumerable<TBar> ticks, double waveHeight, TimeSpan period, double padRight, int count
      , TBar[] fractalsToSkip, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) where TBar : Rate {
      var fractals = ticks.GetMinuteTicks(1).OrderBarsDescending().FindFractals(waveHeight, period, padRight, count, fractalsToSkip, priceHigh, priceLow);
      return fractals.Select(f => {
        var tt = ticks.Where(t => t.StartDate.Between(f.StartDate.AddSeconds(-70), f.StartDate.AddSeconds(70))).OrderBy(t => t.PriceByFractal(f.Fractal));
        var fractal = (f.Fractal == FractalType.Buy ? tt.First() : tt.Last()).Clone() as TBar;
        fractal.Fractal = f.Fractal;
        return fractal;
      }).ToList();
    }
    public static List<TBar> FindFractals<TBar>(this IEnumerable<TBar> bars, double waveHeight, TimeSpan period, double padRight, int count) where TBar : BarBase {
      return bars.FindFractals(waveHeight, period, padRight, count, new TBar[] { });
    }
    public static List<TBar> FindFractals<TBar>(this IEnumerable<TBar> bars, double waveHeight, TimeSpan period, double padRight, int count
      , TBar[] fractalsToSkip) where TBar : BarBase {
      return bars.FindFractals(waveHeight, period, padRight, count, fractalsToSkip, r => r.PriceHigh, r => r.PriceLow);
    }
    public static List<TBar> FindFractals<TBar>(this IEnumerable<TBar> rates, double waveHeight, TimeSpan period, double padRight, int count
      , TBar[] fractalsToSkip, Func<TBar, double> priceHigh, Func<TBar, double> priceLow) where TBar : BarBase {
      var halfPeriod = TimeSpan.FromSeconds(period.TotalSeconds / 2.0);
      var rightPeriod = TimeSpan.FromSeconds(period.TotalSeconds * padRight);
      DateTime nextDate = DateTime.MaxValue;
      var fractals = new List<TBar>();
      var dateFirst = rates.Min(r => r.StartDate) + rightPeriod;
      var dateLast = rates.Max(r => r.StartDate) - rightPeriod;
      var waveFractal = 0D;
      foreach (var rate in rates.Where(r => r.StartDate.Between(dateFirst, dateLast))) {
        UpdateFractal(rates, rate, period, waveHeight, priceHigh, priceLow);
        if (rate.HasFractal && !fractalsToSkip.Contains(rate)) {
          if (fractals.Count == 0) {
            fractals.Add(rate);
            waveFractal = waveHeight;
            waveHeight = 0;
          } else {
            if (rate.Fractal == fractals.Last().Fractal) {
              if (HedgeHog.Bars.BarBase.BiggerFractal(rate, fractals.Last()) == rate)
                fractals[fractals.Count - 1] = rate;
            } else {
              //var range = rates.Where(r => r.StartDate.Between(rate.StartDate, fractals.Last().StartDate)).ToArray();
              if (rate.FractalWave(fractals[fractals.Count - 1]) >= waveFractal && (fractals.Last().StartDate - rate.StartDate).Duration().TotalSeconds >= period.TotalSeconds / 30)
                fractals.Add(rate);
            }
          }
        }
        if (fractals.Count == count) break;
      }
      return fractals;
    }
    static double RangeHeight<TBar>(this IEnumerable<TBar> rates) where TBar : BarBase {
      return rates.Count() == 0 ? 0 : rates.Max(r => r.PriceHigh) - rates.Min(r => r.PriceLow);
    }
    static void UpdateFractal<TBars>(IEnumerable<TBars> rates, TBars rate, TimeSpan period, double waveHeight
      , Func<TBars, double> priceHigh, Func<TBars, double> priceLow) where TBars : BarBase {
      //var wavePeriod = TimeSpan.FromSeconds(period.TotalSeconds * 1.5);
      //var dateFrom = rate.StartDate - wavePeriod;
      //var dateTo = rate.StartDate + wavePeriod;
      //var ratesInRange = rates.Where(r => r.StartDate.Between(dateFrom, dateTo)).ToArray();
      //if ( waveHeight > 0 && ratesInRange.RangeHeight() < waveHeight) return;
      var dateFrom = rate.StartDate - period;
      var dateTo = rate.StartDate + period;
      var ratesLeft = rates.Where(r => r.StartDate.Between(dateFrom.AddSeconds(-period.TotalSeconds), rate.StartDate)).ToArray();
      var ratesRight = rates.Where(r => r.StartDate.Between(rate.StartDate, dateTo.AddSeconds(period.TotalSeconds * 30))).ToArray();
      var ratesInRange = rates.Where(r => r.StartDate.Between(dateFrom, dateTo)).ToArray();
      rate.FractalSell =
        priceHigh(rate) >= ratesInRange.Max(priceHigh)
        &&
        (//(rate.PriceHigh - ratesLeft.Min(r => r.PriceLow)) >= waveHeight        || 
        (priceHigh(rate) - ratesRight.Min(priceLow)) >= waveHeight
        )
        ? HedgeHog.Bars.FractalType.Sell : HedgeHog.Bars.FractalType.None;
      rate.FractalBuy =
        priceLow(rate) <= ratesInRange.Min(priceLow)
        &&
        (//(ratesLeft.Max(r => r.PriceHigh) - rate.PriceLow) >= waveHeight ||
        (ratesRight.Max(priceHigh) - priceLow(rate)) >= waveHeight
        )
        ? HedgeHog.Bars.FractalType.Buy : HedgeHog.Bars.FractalType.None;

      //dateFrom = rate.StartDate.AddSeconds(-period.TotalSeconds * 2);
      //dateTo = rate.StartDate.AddSeconds(+period.TotalSeconds * 2);
      //ratesInRange = rates.Where(r => r.StartDate.Between(dateFrom, dateTo)).ToArray();
      //if (waveHeight > 0 &&
      //  (ratesInRange.Where(r => r.StartDate.Between(dateFrom, rate.StartDate)).RangeHeight() < waveHeight
      //    ||
      //   ratesInRange.Where(r => r.StartDate.Between(rate.StartDate, dateTo)).RangeHeight() < waveHeight)
      //) return;
    }


    public static void FillOverlaps<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      foreach (var bar in bars)
        bar.FillOverlap(bars.Where(r => r.StartDate < bar.StartDate).Take(10));
    }
    public static void SetCMA<TBars>(this IEnumerable<TBars> bars, Func<TBars, double> cmaSource, int cmaPeriod) where TBars : BarBase {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      foreach (var bar in bars) {
        bar.PriceCMA = new double[3];
        bar.PriceCMA[2] = (cma3 = Lib.CMA(cma3, cmaPeriod, (cma2 = Lib.CMA(cma2, cmaPeriod, (cma1 = Lib.CMA(cma1, cmaPeriod, cmaSource(bar))).Value)).Value)).Value;
        bar.PriceCMA[1] = cma2.Value;
        bar.PriceCMA[0] = cma1.Value;
      }
    }
    public static void SetCMA<TBars>(this IEnumerable<TBars> ticks, int cmaPeriod) where TBars : BarBase {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      ticks.ToList().ForEach(t => {
        t.PriceCMA = new double[3];
        t.PriceCMA[2] = (cma3 = Lib.CMA(cma3, cmaPeriod, (cma2 = Lib.CMA(cma2, cmaPeriod, (cma1 = Lib.CMA(cma1, cmaPeriod, t.PriceAvg)).Value)).Value)).Value;
        t.PriceCMA[1] = cma2.Value;
        t.PriceCMA[0] = cma1.Value;
      });
    }
    public static DataPoint[] GetCurve(IEnumerable<BarBase> ticks, int cmaPeriod) {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      int i = 0;
      return (from tick in ticks
              select
              new DataPoint() {
                Value = (cma3 = Lib.CMA(cma3, cmaPeriod, (cma2 = Lib.CMA(cma2, cmaPeriod, (cma1 = Lib.CMA(cma1, cmaPeriod, tick.PriceAvg)).Value)).Value)).Value,
                Date = tick.StartDate,
                Index = i++
              }
                  ).ToArray();
    }
    public static IEnumerable<T> OrderBars<T>(this IEnumerable<T> rates) where T : BarBase {
      return typeof(T) == typeof(Tick) ?
        rates.Cast<Tick>().OrderBy(r => r.StartDate).ThenBy(r => r.Row).Cast<T>() : rates.OrderBy(r => r.StartDate);
    }
    public static IEnumerable<T> OrderBarsDescending<T>(this IEnumerable<T> rates) where T : BarBase {
      return typeof(T) == typeof(Tick) ?
        rates.OfType<Tick>().OrderByDescending(r => r.StartDate).ThenByDescending(r => r.Row).OfType<T>() : rates.OrderByDescending(r => r.StartDate);
    }
  }
}
