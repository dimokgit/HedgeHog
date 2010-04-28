using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Bars {
  public class RsiStatistics {
    public double Buy { get { return BuyAverage - BuyStd; } }
    public double Sell { get { return SellAverage + SellStd; } }
    public double Buy1 { get { return BuyMin + BuyStd*2; } }
    public double Sell1 { get { return SellMax - SellStd*2; } }
    public double BuyAverage { get; set; }
    public double SellAverage { get; set; }
    public double BuyStd { get; set; }
    public double SellStd { get; set; }
    public double BuyMin { get; set; }
    public double SellMax { get; set; }

    public RsiStatistics(double? BuyAverage,double? BuyStd,double? BuyMin,double? SellAverage,double? SellStd,double? SellMax ) {
      this.BuyAverage = BuyAverage.GetValueOrDefault();
      this.BuyStd = BuyStd.GetValueOrDefault();
      this.BuyMin = BuyMin.GetValueOrDefault();
      this.SellAverage = SellAverage.GetValueOrDefault();
      this.SellStd = SellStd.GetValueOrDefault();
      this.SellMax = SellMax.GetValueOrDefault();
    }
    public override string ToString() {
      return string.Format("{0:n1}={1:n1}-{2:n1},{3:n1}={4:n1}+{5:n1}", Buy, BuyAverage, BuyStd, Sell, SellAverage, SellStd);
    }
  }
  public static class Extensions {

    public static RsiStatistics RsiStats(this List<Rate> ratesByMinute) { return ratesByMinute.RsiStatsCore(); }
    public static RsiStatistics RsiStats(this Rate[] ratesByMinute) { return ratesByMinute.RsiStatsCore(); }
    static RsiStatistics RsiStatsCore<TBar>(this IEnumerable<TBar> ratesByMinute) where TBar : BarBase {

      //var rsiHigh = ratesByMinute.Where(r => r.PriceRsi > 50).ToArray();
      //var rsiLow = ratesByMinute.Where(r => r.PriceRsi < 50).ToArray();

      //var fractals = GetRsiFractals(rsiHigh).Concat(GetRsiFractals(rsiLow)).OrderBarsDescending().FixFractals();

      var RsiAverageHigh = ratesByMinute.Where(r => r.PriceRsi.GetValueOrDefault() != 50).Average(r => r.PriceRsi).Value;
      var RsiAverageLow = RsiAverageHigh;
      var rsiHigh = ratesByMinute.Where(r => r.PriceRsi > RsiAverageHigh).ToArray();
      var rsiLow = ratesByMinute.Where(r => r.PriceRsi < RsiAverageLow).ToArray();
      RsiAverageHigh = rsiHigh.Average(r => r.PriceRsi).Value;
      RsiAverageLow = rsiLow.Average(r => r.PriceRsi).Value;


      rsiHigh = rsiHigh.Where(r => r.PriceRsi > RsiAverageHigh).ToArray();
      rsiLow = rsiLow.Where(r => r.PriceRsi < RsiAverageLow).ToArray();
      RsiAverageHigh = rsiHigh.Average(r => r.PriceRsi).Value;
      RsiAverageLow = rsiLow.Average(r => r.PriceRsi).Value;

      rsiHigh = rsiHigh.Where(r => r.PriceRsi > RsiAverageHigh).ToArray();
      rsiLow = rsiLow.Where(r => r.PriceRsi < RsiAverageLow).ToArray();
      RsiAverageHigh = rsiHigh.Average(r => r.PriceRsi).Value;
      RsiAverageLow = rsiLow.Average(r => r.PriceRsi).Value;

      var RsiStdHigh = rsiHigh.StdDev(r => r.PriceRsi);
      var RsiStdLow = rsiLow.StdDev(r => r.PriceRsi);

      var rsiSellHigh = rsiHigh.Max(r => r.PriceRsi);
      var rsiBuyLow = rsiLow.Min(r => r.PriceRsi);


      return new RsiStatistics(RsiAverageLow, RsiStdLow, rsiBuyLow, RsiAverageHigh, RsiStdHigh, rsiSellHigh);
    }

    private static TBar[] GetRsiFractals<TBar>(this TBar[] rsiHigh) where TBar : BarBase {
      var sell = rsiHigh[0].PriceRsi > 50;
      var fs = new List<TBar>();
      TBar prev = rsiHigh[0];
      var rateMax = new List<TBar>(new[] { prev });
      foreach (var rate in rsiHigh.Skip(1)) {
        if ((prev.StartDate - rate.StartDate) <= TimeSpan.FromMinutes(1))
          rateMax.Add(rate);
        else  {
          if (rateMax.Count > 4) {
            var ro = rateMax.OrderBy(r => r.PriceAvg);
            var fractal = (sell ? ro.Last() : ro.First()).Clone() as TBar;
            fractal.Fractal = sell ? FractalType.Sell : FractalType.Buy;
            fractal.PriceRsi = sell ? rateMax.Max(r => r.PriceRsi) : rateMax.Min(r => r.PriceRsi);

            fs.Add(fractal);
          }
          rateMax.Clear();
          rateMax.Add(rate);
        }
        prev = rate;
      }
      return fs.ToArray();
    }

    enum WaveDirection { Up = 1, Down = -1, None = 0 };
    public static Rate[] FindWaves(this IEnumerable<Rate> ticks) {
      return ticks.FindWaves(0);
    }
    public static Rate[] FindWaves(this IEnumerable<Rate> ticks, int wavesCount) {
      List<Rate> waves = new List<Rate>();
      WaveDirection waveDirection = WaveDirection.None;
      ticks = ticks.OrderBarsDescending().ToArray();
      var logs = new Dictionary<int, double>();
      foreach (var rate in ticks) {
        var period = TimeSpan.FromMinutes(5);
        var ticksToRegress = ticks.Where(period, rate).ToArray();
        var coeffs = Lib.Regress(ticksToRegress.Select(t => t.PriceAvg).ToArray(), 1);
        if (!double.IsNaN(coeffs[1])) {
          var wd = (WaveDirection)Math.Sign(coeffs[1]);
          if (waveDirection != WaveDirection.None) {
            if (wd != waveDirection && wd != WaveDirection.None) {
              var ticksForPeak = ticks.Where(ticksToRegress.Last(), TimeSpan.FromSeconds(period.TotalSeconds/2)).ToArray();
              waves.Add(wd == WaveDirection.Up
                ? ticksForPeak.OrderBy(t => t.PriceLow).First()
                : ticksForPeak.OrderBy(t => t.PriceHigh).Last());
              waves.Last().Fractal = wd == WaveDirection.Up ? FractalType.Buy : FractalType.Sell;
              if (waves.Count == wavesCount) break;
            }
          }
          waveDirection = wd;
        }
      }
      return waves.ToArray();
    }

    public static void FillFlatness(this Rate[] bars,int BarsMin) {
      bars = bars.OrderBarsDescending().ToArray();
      foreach (var bar in bars)
        if (!bar.Flatness.HasValue)
          bar.Flatness = bars.Where(TimeSpan.FromMinutes(120), bar).ToArray().GetFlatness(BarsMin);
    }
    public static TimeSpan GetFlatness(this Rate[] ticks,int barsMin) {
      var ticksByMinute = ticks.GetMinuteTicks(1);
      var tickLast = ticksByMinute.First();
      var flats = new List<Rate>(new[] { tickLast });
      var barsCount = 1;
      foreach (var tick in ticksByMinute.Skip(1)) {
        if (tick.OverlapsWith(tickLast) != OverlapType.None) flats.Add(tick);
        if (++barsCount >= barsMin && (double)flats.Count / barsCount < .7) break;
        if (barsCount > 20) {
          var i = 0;
          i++;
        }
      }
      return new []{TimeSpan.FromMinutes(3), (flats.First().StartDate - flats.Last().StartDate).Add(TimeSpan.FromMinutes(1))}.Max();

      //var ol = ticksByMinute.Skip(1).Where(t => t.OverlapsWith(tickLast) != OverlapType.None)
      //  .Concat(new[] { tickLast }).OrderBars().ToArray();
      //return ol.Length == 0 ? TimeSpan.Zero : (ol.Last().StartDate - ol.First().StartDate);
    }


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

    public static Rate[] GetMinuteTicks<TBar>(this List<TBar> fxTicks, int period, bool round) where TBar : BarBase {
      return fxTicks.GetMinuteTicksCore(period, round);
    }
    public static Rate[] GetMinuteTicks<TBar>(this List<TBar> fxTicks, int period) where TBar : BarBase {
      return fxTicks.GetMinuteTicksCore(period);
    }
    public static Rate[] GetMinuteTicks<TBar>(this TBar[] fxTicks, int period, bool round) where TBar : BarBase {
      return fxTicks.GetMinuteTicksCore(period, round);
    }
    static Rate[] GetMinuteTicksCore<TBar>(this IEnumerable<TBar> fxTicks, int period, bool round) where TBar : BarBase {
      if (!round) return GetMinuteTicksCore(fxTicks, period);
      var timeRounded = fxTicks.Min(t => t.StartDate).Round().AddMinutes(1);
      return GetMinuteTicksCore(fxTicks.Where(t => t.StartDate >= timeRounded), period);
    }
    public static Rate[] GetMinuteTicks<TBar>(this TBar[] fxTicks, int period) where TBar : BarBase {
      return fxTicks.GetMinuteTicksCore(period);
    }
    static Rate[] GetMinuteTicksCore<TBar>(this IEnumerable<TBar> fxTicks, int period) where TBar : BarBase {
      if (fxTicks.Count() == 0) return new Rate[] { };
      var startDate = fxTicks.Max(t => t.StartDate);
      double? tempRsi=null;
      return (from t in fxTicks.OrderBarsDescending().ToArray()
              where period > 0
              group t by (((int)Math.Floor((startDate - t.StartDate).TotalMinutes) / period)) * period into tg
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
                PriceRsi = !(tempRsi = tg.Average(t => t.PriceRsi)).HasValue ? tempRsi
                : tempRsi == 50 ? 50
                : tempRsi > 50 ? tg.Max(t => t.PriceRsi) : tg.Min(t => t.PriceRsi),
                StartDate = startDate.AddMinutes(-tg.Key)
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
    static void FillPower<TBar>(this TBar[] barsSource, TBar[] bars) where TBar : BarBase {
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
    public static void FillFractalHeight<TBar>(this TBar[] bars) where TBar : BarBase {
      var i=0;
      bars.Take(bars.Length - 1).ToList()
        .ForEach(b => {
          b.Ph.Height = (b.FractalPrice - bars[++i].FractalPrice).Abs();
          b.Ph.Time = b.StartDate - bars[i].StartDate;
        });
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


    public static TBar[] FillRsi<TBar>(this TBar[] rates, Func<TBar, double> getPrice) where TBar : BarBase {
      //var period = Math.Floor((rates.Last().StartDate - rates.First().StartDate).TotalMinutes).ToInt();
      var period = rates.Length-2;
      return rates.FillRsi(period, getPrice);
    }
    public static TBar[] FillRsi<TBar>(this TBar[] rates, int period, Func<TBar, double> getPrice) where TBar : BarBase {
      for (int i = period; i < rates.Length; i++) {
        UpdateRsi(period, rates, i, getPrice);
      }
      return rates;
    }
    static void UpdateRsi<TBar>(int numberOfPeriods, TBar[] rates, int period, Func<TBar, double> getPrice) where TBar : BarBase {
      if (period >= numberOfPeriods) {
        var i = 0;
        var sump = 0.0;
        var sumn = 0.0;
        var positive = 0.0;
        var negative = 0.0;
        var diff = 0.0;
        if (period == numberOfPeriods) {
          for (i = period - numberOfPeriods + 1; i <= period; i++) {
            diff = getPrice(rates[i]) - getPrice(rates[i - 1]);
            if (diff >= 0)
              sump = sump + diff;
            else
              sumn = sumn - diff;
          }
          positive = sump / numberOfPeriods;
          negative = sumn / numberOfPeriods;
        } else {
          diff = getPrice(rates[period]) - getPrice(rates[period - 1]);
          if (diff > 0)
            sump = diff;
          else
            sumn = -diff;
          positive = (rates[period - 1].PriceRsiP * (numberOfPeriods - 1) + sump) / numberOfPeriods;
          negative = (rates[period - 1].PriceRsiN * (numberOfPeriods - 1) + sumn) / numberOfPeriods;
        }
        rates[period].PriceRsiP = positive;
        rates[period].PriceRsiN = negative;
        rates[period].PriceRsi = 100 - (100 / (1 + positive / negative));
      }
    }


    public static void FillRsis<TBar>(this TBar[] bars, TimeSpan RsiPeriod) where TBar : BarBase {
      double rsiPrev = 0;
      foreach (var rate in bars.Where(t => !t.PriceRsi.HasValue)) {
        var ts = bars.TakeWhile(t => t <= rate).ToArray();
        if ((ts.Last().StartDate-ts.First().StartDate) > RsiPeriod) {
          ts = ts.Where(RsiPeriod,rate).ToArray();
          rate.PriceRsi = ts.FillRsi(t => t.PriceAvg).Last().PriceRsi;
          if (!rate.PriceRsi.HasValue || double.IsNaN(rate.PriceRsi.Value))
            rate.PriceRsi = rsiPrev;
          rsiPrev = rate.PriceRsi.Value;
        } else rate.PriceRsi = 50;
      }
    }
    public static void FillRsis<TBar>(this TBar[] bars, int RsiTicks) where TBar : BarBase {
      double rsiPrev = 0;
      foreach (var rate in bars.Where(t => !t.PriceRsi.HasValue)) {
        var ts = bars.TakeWhile(t => t <= rate).ToArray();
        if (ts.Length > RsiTicks) {
          ts = ts.Skip(ts.Count() - RsiTicks).ToArray();
          rate.PriceRsi = ts./*GetMinuteTicks(1).*/OrderBars().ToArray().FillRsi(t => t.PriceAvg).Last().PriceRsi;
          if (!rate.PriceRsi.HasValue || double.IsNaN(rate.PriceRsi.Value) )
            rate.PriceRsi = rsiPrev;
          rsiPrev = rate.PriceRsi.Value;
        } else rate.PriceRsi = 50;
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
      var fractals = ticks.ToArray().GetMinuteTicks(1).OrderBarsDescending().FindFractals(waveHeight, period, padRight, count, fractalsToSkip, priceHigh, priceLow);
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
