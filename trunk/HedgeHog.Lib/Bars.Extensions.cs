using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Windows;
using System.Threading.Tasks;

namespace HedgeHog.Bars {
  public class RsiStatistics {
    public double Buy { get { return BuyAverage - BuyStd; } }
    public double Sell { get { return SellAverage + SellStd; } }
    public double Buy1 { get { return BuyMin + BuyStd * 2; } }
    public double Sell1 { get { return SellMax - SellStd * 2; } }
    public double BuyAverage { get; set; }
    public double SellAverage { get; set; }
    public double BuyStd { get; set; }
    public double SellStd { get; set; }
    public double BuyMin { get; set; }
    public double SellMax { get; set; }

    public RsiStatistics(double? BuyAverage, double? BuyStd, double? BuyMin, double? SellAverage, double? SellStd, double? SellMax) {
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

    public static void SetStartDateForChart<TBar>(this IEnumerable<TBar> bars) where TBar : BarBaseDate {
      var period = bars.GetPeriod();
      if (period == TimeSpan.Zero)
        bars.AsParallel().ForAll(b => b.StartDateContinuous = b.StartDate);
      else {
        var rateLast = bars.OrderBars().Last();
        rateLast.StartDateContinuous = rateLast.StartDate;
        bars.OrderBarsDescending().Aggregate((bp, bn) => {
          bn.StartDateContinuous = bp.StartDateContinuous - period;
          return bn;
        });
      }
    }

    public static TimeSpan GetPeriod<TBar>(this IEnumerable<TBar> bars) where TBar : BarBaseDate {
      if (bars.Count() < 2) return TimeSpan.Zero;
      var periods = new List<double>();
      bars.Aggregate((bp, bn) => { periods.Add((bp.StartDate - bn.StartDate).Duration().TotalMinutes); return bn; });
      var periodGroups = from p in periods
                         group p by p into pg
                         select new { Period = pg.Key, Count = pg.Count() };
      return TimeSpan.FromMinutes(periodGroups.OrderBy(pg => pg.Count).Last().Period);
                         
      //var period = TimeSpan.FromMinutes(periods.ToArray().AverageByIterations((d, a) => d <= a, 3).Average().ToInt());
      //return period;
    }

    static void SetRegressionPrice(this IEnumerable<Rate> ticks, double[] coeffs, Action<Rate, double> a) {
      int i1 = 0;
      foreach (var tick in ticks) {
        double y1 = coeffs.RegressionValue(i1++);
        a(tick, y1);// *poly2Wieght + y2 * (1 - poly2Wieght);
      }
    }
    public static double[] SetRegressionPrice(this IEnumerable<Rate> ticks, int polyOrder, Func<Rate, double> readFrom, Action<Rate, double> writeTo) {
      var coeffs = Lib.Regress(ticks.Select(readFrom).ToArray(), polyOrder);
      ticks.SetRegressionPrice(coeffs, writeTo);
      return coeffs;
    }

    public static double[] SetCorridorPrices(this IEnumerable<Rate> rates, double heightUp0, double heightDown0, double heightUp, double heightDown,
      Func<Rate, double> getPriceForLine, Func<Rate, double> getPriceLine, Action<Rate, double> setPriceLine
      , Action<Rate, double> setPriceHigh0, Action<Rate, double> setPriceLow0
      , Action<Rate, double> setPriceHigh, Action<Rate, double> setPriceLow
      ) {
      var coeffs = rates.SetRegressionPrice(1, getPriceForLine, setPriceLine);
      //var stDev = rates.Select(r => (getPriceForLine(r) - getPriceLine(r)).Abs()).ToArray().StdDev();
      rates.ToList().ForEach(r => {
        setPriceHigh0(r, r.PriceAvg1 + heightUp0);
        setPriceLow0(r, r.PriceAvg1 - heightDown0);
        setPriceHigh(r, r.PriceAvg1 + heightUp);
        setPriceLow(r, r.PriceAvg1 - heightDown);
      });
      return coeffs;// heightAvg * 2;// heightAvgUp + heightAvgDown;
    }

    public static Rate CalculateMagnetLevel(this ICollection<Rate> rateForCom, bool up) {
      var rs = rateForCom.FindRatesByPrice(rateForCom.MagnetPrice());
      return up ? rs.OrderBy(r => r.PriceHigh).Last() : rs.OrderBy(r => r.PriceLow).First();
    }


    public static Rate CenterOfMass(this ICollection<Rate> rates, bool up) {
      var coms = rates.CentersOfMass();
      return up ? coms.OrderBy(r => r.PriceHigh).Last() : coms.OrderBy(r => r.PriceLow).First();
    }
    public static Rate CenterOfMass(this ICollection<Rate> rates) {
      return rates.CentersOfMass().OrderBy(r => r.Spread).First();
    }
    public static Rate CenterOfMass(this Rate[][] ovelaps) {
      return ovelaps.CentersOfMass().OrderBy(r => r.Spread).FirstOrDefault();
    }
    public static Rate[] CentersOfMass(this ICollection<Rate> rates) {
      return rates.Overlaps().OrderBy(rm => rm.Length).LastOrDefault() ?? new Rate[] { };
    }
    public static Rate[] CentersOfMass(this Rate[][] overlaps) {
      return overlaps.OrderBy(rm => rm.Length).Last();
    }

    public static Rate[][] Overlaps(this ICollection<Rate> rates,int iterationsForSpread = 0) {
      if (iterationsForSpread > 0) {
        var spreadAverage = rates.Select(r => r.Spread).ToArray()
          .AverageByIterations(iterationsForSpread, true)
          .AverageByIterations(iterationsForSpread, false)
          .Average();
        rates = rates.Where(r => r.Spread <= spreadAverage).ToArray();
      }
      return rates.AsParallel().Select(rate => rates.Where(r => r.OverlapsWith(rate) != OverlapType.None).ToArray()).ToArray();
    }

    public static IEnumerable<TBar> FindRatesByPrice<TBar>(this ICollection<TBar> Rates, double price) where TBar : BarBase {
      return Rates.Where(r => price.Between(r.PriceLow, r.PriceHigh));
    }

    #region Level
    public static double Level<TBar>(this ICollection<TBar> rates, bool up, int iterations)where TBar:BarBase {
      var ratesForLevel = rates.DistanceAverage(up, iterations);
      return up ? ratesForLevel.Average(r => r.PriceHigh) : ratesForLevel.Average(r => r.PriceLow);
    }
    static TBar[] DistanceAverage<TBar>(this ICollection<TBar> rates, bool up, int iterations) where TBar:BarBase{
      TBar[] ratesToReturn = rates.ToArray();
      var clusters = new List<TBar[]>(new TBar[][] { rates.ToArray() });
      do {
        var cs = clusters.Flatten().DistanceAverage(up).ToList();
        if (cs.Count == 0) break;
        clusters = cs;
        var ll = clusters.Select(c => (double)c.Length).Distinct().ToArray();
        var avg = ll.Average();
        var stDev = ll.StDev();
        clusters = clusters.Where(c => c.Length.Between(avg - stDev, avg + stDev)).ToList();
      } while (--iterations > 0 && clusters.Count > 2);
      return clusters.OrderBy(c=>c.Length).Last();
    }

    static T[] Flatten<T>(this ICollection<T[]> clusters) {
      if (clusters.Count == 0) return new T[0];
      if (clusters.Count == 1) return clusters.First();
      var list = new List<T>(clusters.First());
      clusters.Skip(1).ToList().ForEach(c => list.AddRange(c));
      return list.ToArray();
    }
    static IEnumerable<TBar[]> DistanceAverage<TBar>(this ICollection<TBar> rates, bool up) where TBar : BarBase {
      return rates.DistanceAverage(up ? (Func<TBar, double>)(r => r.PriceHigh) : r => r.PriceLow);
    }
    static IEnumerable<TBar[]> DistanceAverage<TBar>(this ICollection<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      Func<LinkedListNode<TBar>, double> getDistance = node =>
        Math.Min((getPrice(node.Value) - getPrice(node.Previous.Value)).Abs(), (getPrice(node.Value) - getPrice(node.Next.Value)).Abs());
      var nodeStart = new LinkedList<TBar>(rates.OrderBy(getPrice).ToArray()).First.Next;
      var distances = new List<double>();
      while (nodeStart.Next != null) {
        distances.Add(getDistance(nodeStart));
        nodeStart = nodeStart.Next;
      }
      nodeStart = nodeStart.List.First.Next;
      var distanceAverage = distances.Average();
      var cluster = new List<TBar>();
      while (nodeStart.Next != null) {
        var dist = getDistance(nodeStart);
        if (dist <= distanceAverage)
          cluster.Add(nodeStart.Value);
        else {
          if (cluster.Count > 0) {
            yield return cluster.ToArray();
            cluster.Clear();
          }
        }
        nodeStart = nodeStart.Next;
      }
    }
    #endregion

    public static double MagnetPrice(this ICollection<Rate> rates) {
      var priceHeights = "".Select(o => new { price = 0.0, height = 0.0 }).ToList();
      var ms = rates.Take(rates.Count - 1)
        .Select(r1 => rates.Where(r2 => r2 > r1)
          .Select(r3 => new {
            price = (r1.PriceHigh + r3.PriceLow) / 2,
            height = new[] { (r1.PriceLow - r3.PriceLow).Abs(), (r1.PriceHigh - r3.PriceHigh).Abs(), (r1.PriceHigh - r3.PriceLow).Abs(), (r1.PriceLow - r3.PriceHigh).Abs() }.Min()
          }).OrderBy(m=>m.price).ToArray()).ToList();
      ms.ForEach(m => priceHeights.AddRange(m));
      priceHeights.Sort((ph1, ph2) => ph1.price.CompareTo(ph2.price));
      var heightIterations = 6;
      var minimumHeight = priceHeights.Select(m => m.height).ToArray().AverageByIterations(heightIterations, true).Average();
      priceHeights.RemoveAll(m => m.height > minimumHeight);
      var prices = priceHeights.Select(ph => ph.price).Distinct().ToArray();

      var priceDistances = new List<double>();
      var pds = prices.Take(prices.Count() - 1)
        .Select(p1 => prices.Where(p2 => p2 != p1)
          .Select(p3 => new {
            price1 = p1, price2 = p3, distance = (p1 - p3).Abs()
          }).ToList()).ToList();
      pds.ForEach(pd => priceDistances.AddRange(pd.Select(a=>a.distance)));
      var distanceIterations = 5;
      var minimumDistance = priceDistances.AverageByIterations(distanceIterations, true).Average();
      var distanceCount = priceDistances.Count(pd => pd < minimumDistance);
      pds.ForEach(pd => pd.RemoveAll(pd1 => pd1.distance > minimumDistance));
      pds.Sort((l1, l2) => l2.Count.CompareTo(l1.Count));
      return pds[0][0].price1;
    }
    public static void GetCorridorHeights(this IEnumerable<Rate> rates, Func<Rate, double> priceLine, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, Func<double, double, bool> avgCompare, int minimumCount, int iterations, out double heightAvgUp, out double heightAvgDown) {
      rates.GetCorridorHeights(new Rate[0], new Rate[0], priceLine, priceHigh, priceLow, avgCompare, minimumCount, iterations, out heightAvgUp, out heightAvgDown);
    }
    public static void GetCorridorHeights(this IEnumerable<Rate> rates, Rate[] lineHigh, Rate[] lineLow, Func<Rate, double> priceLine, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, Func<double, double, bool> avgCompare, int minimumCount, int iterations, out double heightAvgUp, out double heightAvgDown) {
      Func<Rate, double> getHeightHigh = r => priceHigh(r) - priceLine(r);
      Func<Rate, double> getHeightLow = r => priceLine(r) - priceLow(r);
      //var treshold = .1;
      var heightsUp = (lineHigh.Length > 0 ? lineHigh : rates.Where(r => getHeightHigh(r) > 0))
        .Where(r => priceLine(r) > 0).ToArray();
      //.AverageByPercantage(getHeightHigh, treshold, minimumCount);
      heightAvgUp = iterations > 5
      ? heightsUp.AverageByPercantage(getHeightHigh, iterations/100.0, 3).Average(getHeightHigh)
      : heightsUp.Select(getHeightHigh).ToArray().AverageByIterations(avgCompare, iterations).Average();
      //heightAvgUp = heightsUp.Min();

      var heightsDown = (lineLow.Length > 0 ? lineLow : rates.Where(r => getHeightLow(r) > 0))
        .Where(r => priceLine(r) > 0).ToArray();
      //.AverageByPercantage(getHeightLow, treshold, minimumCount);
      heightAvgDown = iterations > 5
        ? heightsDown.AverageByPercantage(getHeightLow, iterations/100.0, 3).Average(getHeightLow)
        : heightsDown.Select(getHeightLow).ToArray().AverageByIterations(avgCompare, iterations).Average();
    }
    public static void Index(this Rate[] rates) {
      rates[0].Index = 0;
      rates.Aggregate((rp, rn) => { rn.Index = rp.Index + 1; return rn; });
    }

    public static double[] FindLine<TBar>(this TBar[] bars, Func<TBar, double> getPtice, double[] defaultCoefs) where TBar : BarBase {
      if (bars.Count() < 2) return defaultCoefs;
      if (bars.Count() == 2) {
        return new[] { bars.First().PriceAvg, RateSlope(bars.First(), bars.Last()) };
      }
      return MathExtensions.Linear(bars.Select(r => (double)r.Index).ToArray(), bars.Select(getPtice).ToArray());
    }

    private static double RateSlope<TBar>(TBar r1, TBar r2) where TBar : BarBase {
      return Slope(new Point(r1.Index, r1.PriceAvg), new Point(r2.Index, r2.PriceAvg));
    }

    private static double Slope(Point p1, Point p2) {
      return (p1.Y - p2.Y) / (p2.X - p1.X);
    }
    public static TBar[] FindExtreams<TBar>(this TBar[] bars, Func<TBar, TBar, TBar> aggregate, int margin = 2) where TBar : BarBase {
      if (bars.Length == 0) return new TBar[0];
      var count = bars.Length - margin * 2;
      var extreams = new List<TBar>();
      //Parallel.For(0, count, i => {
      //  lock (bars) {
      //    var a = bars.Skip(i).Take(margin * 2 + 1).ToArray();
      //    var a2 = a[margin];
      //    var a1 = a.Aggregate(aggregate);
      //    if (a1 == a2) extreams.Add(a2);
      //  }
      //});
      for (var i = 0; i < count; i++) {
        var a = bars.Skip(i).Take(margin * 2 + 1).ToArray();
        if (a.Aggregate(aggregate) == a[margin]) extreams.Add(a[margin]);
      }
      return extreams.ToArray();
    }

    public static T[][] GetIntervals<T>(this ICollection<Tuple<int, T>> barPoints, int margin) {
      var intervals = new List<int>();
      var barIntervals = new List<T[]>();
      var barInterval = new List<T>();
      if (barPoints.Count < 2) {
        barIntervals.Add(barPoints.Select(bp => bp.Item2).ToArray());
      } else {
        intervals = new List<int>();
        Func<Tuple<int, T>, Tuple<int, T>, Tuple<int, T>> getInterval = (bpp, bpn) => {
          var i = (bpp.Item1 - bpn.Item1).Abs() == 1 ? 1 : 0;
          if (i == 0) {
            if (barInterval.Count > margin * 2) {
              barIntervals.Add(barInterval.ToArray());
              barInterval.Clear();
            }
          } else {
            if (barInterval.Count == 0) barInterval.Add(bpp.Item2);
            barInterval.Add(bpn.Item2);
          }
          return bpn;
        };
        barPoints.Aggregate(getInterval);
        if (barInterval.Count > margin * 2) barIntervals.Add(barInterval.ToArray());
      }
      if (barIntervals.Count == 0)
        if (margin > 0) return barPoints.GetIntervals(margin - 1);
        else barPoints.Select(bp => bp.Item2).ToList().ForEach(b => barIntervals.Add(new T[] { b }));
      return barIntervals.ToArray();
    }

    #region AverageBy
    public static TBar[] AverageByPercantage<TBar>(this TBar[] values, Func<TBar, double> getPrice, double percentage, int minimumCount) where TBar : BarBase {
      double timeSpanRatio;
      return values.AverageByPercantage(getPrice, percentage, minimumCount, out timeSpanRatio);
    }
    public static TBar[] AverageByPercantage<TBar>(this TBar[] values, Func<TBar, double> getPrice, double percentage, int minimumCount, out double timeSpanRatio) where TBar : BarBase {
      if (values.Length == 0) { timeSpanRatio = 0; return values; }
      var average = 0.0;
      var countOriginal = values.Count();
      var countCurrent = countOriginal + 1.0;
      var timeSpanOriginal = values.Max(v => v.StartDate) - values.Min(v => v.StartDate);
      var minutesToSkip = timeSpanOriginal.TotalMinutes / 10;
      do {
        average = values.Where(v => getPrice(v) >= average).Average(getPrice);
        var vs = values.Where(v => getPrice(v) >= average).ToArray();
        if (vs.Length < minimumCount) break;
        values = vs;
        if (countCurrent == values.Count()) break;
        countCurrent = values.Length;
      } while (countCurrent / countOriginal > percentage);
      var timeSpanCurrent = values.Max(v => v.StartDate) - values.Min(v => v.StartDate);
      timeSpanRatio = timeSpanCurrent.TotalMinutes / timeSpanOriginal.TotalMinutes;
      return values;
    }

    #region AverageByIterations
    public static TBar[] AverageByIterations<TBar>(this ICollection<TBar> values, Func<TBar, double> getPrice, double iterations) where TBar : BarBaseDate {
      return values.AverageByIterations(getPrice, (v, a) => v >= a, iterations);
    }
    public static TBar[] AverageByIterations<TBar>(this ICollection<TBar> values, Func<TBar, double> getPrice, double iterations,out double average) where TBar : BarBaseDate {
      return values.AverageByIterations(getPrice, (v, a) => v >= a, iterations,out average);
    }
    public static TBar[] AverageByIterations<TBar>(this ICollection<TBar> values, Func<TBar, double> getPrice, Func<double, double, bool> compare, double iterations) where TBar : BarBaseDate {
      double average;
      return AverageByIterations<TBar>(values, getPrice, compare, iterations, out average);
    }
    static TBar[] AverageByIterations<TBar>(this ICollection<TBar> values, Func<TBar, double> getPrice, Func<double, double, bool> compare, double iterations, out double average) where TBar : BarBaseDate {
      var avg = values.Count() == 0 ? 0 : values.Average(getPrice);
      for (int i = 1; i < iterations && values.Count() > 0; i++) {
        values = values.Where(r => compare(getPrice(r), avg)).ToArray();
        if (values.Count == 0) break;
        avg = values.Average(getPrice);
      }
      average = avg;
      return values.ToArray();
    }
    #endregion
    #endregion


    #region Wave
    public static double GetWaveHeight(this Rate[] rates, int barFrom, int barTo) {
      var barPrev = rates.GetBarHeightBase(barFrom);
      var barPrev1 = barPrev;
      var bars = new List<double>(new[] { barPrev });
      var barsForAverage = new List<double>();
      for (var i = barFrom + 1; i <= barTo; i++) {
        var bar = rates.GetBarHeightBase(i);
        //if (bar / barPrev < 1) 
        barsForAverage.Add(barPrev);// return new[] { barPrev, barPrev1, bar }.Min();
        barPrev1 = barPrev;
        barPrev = bar;
        bars.Add(bar);
      }
      barsForAverage = barsForAverage.DefaultIfEmpty(bars.Max()).ToList();
      var barsAverage = barsForAverage.Average();
      return barsForAverage.Where(b => b >= barsAverage).Average();
    }


    public static double GetBarHeight(this Rate[] rates, int period) {
      if (rates.Count() == 0) return 0;
      //var sw = Stopwatch.StartNew();
      var bhList = new List<double>();
      var dateStart = rates[0].StartDate;
      var minutes = (rates.Last().StartDate - rates.First().StartDate).TotalMinutes.ToInt();
      for (var i = 0; i < minutes - period; i++)
        bhList.Add(rates.SkipWhile(r => r.StartDate < dateStart.AddMinutes(i)).ToArray().GetBarHeightBase(period));
      return bhList.Average();
      //Debug.WriteLine("GetBarHeight:{0} - {1:n0} ms", tm.Pair, sw.Elapsed.TotalMilliseconds);
    }

    public static double GetBarHeightBase(this Rate[] rates, int barPeriod) {
      var rates60 = rates.GetMinuteTicks(barPeriod);
      if (rates60.Count() == 0) return 0;
      var hs = rates60.Select(r => r.Spread).ToArray();
      var hsAvg = hs.Average();
      hs = hs.Where(h => h >= hsAvg).ToArray();
      hsAvg = hs.Average();
      return hs.Where(h => h >= hsAvg).Average();
    }
    #endregion

    public static void AddRange<T>(this IList<T> bars, IEnumerable<T> barsToAdd) {
      barsToAdd.ToList().ForEach(b => bars.Add(b));
    }
    public static void RemoveRange<T>(this IList<T> bars, int startIndex, int count) {
      while (count-- > 0 && bars.Count() >= startIndex)
        bars.Remove(bars[startIndex]);
    }
    public static void RemoveAll<T>(this IList<T> bars, Func<T,bool> filter) {
      bars.Where(filter).ToList().ForEach(b => bars.Remove(b));
    }

    public static Rate High(this ICollection<Rate> rates) {
      return rates.OrderBy(r => r.PriceAvg).Last();
    }
    public static Rate Low(this ICollection<Rate> rates) {
      return rates.OrderBy(r => r.PriceAvg).First();
    }
    public static double Height(this ICollection<Rate> rates) {
      return rates.Max(r => r.PriceAvg) - rates.Min(r => r.PriceAvg);
    }
    public static TimeSpan Duration(this ICollection<Rate> rates) {
      return rates.Max(r => r.StartDate) - rates.Min(r => r.StartDate);
    }
    public static TimeSpan Duration(this ICollection<Rate> rates, TimeSpan durationMax) {
      var sw = Stopwatch.StartNew();
      Rate prev = null; ;
      TimeSpan duration = TimeSpan.Zero;
      foreach (var rate in rates)
        if (prev == null) prev = rate;
        else {
          var d = (rate.StartDate - prev.StartDate).Duration();
          if (d <= durationMax)
            duration = duration.Add(d);
          prev = rate;
        }
      Debug.WriteLine("Duration:{0:n}", sw.ElapsedMilliseconds);
      return duration;
    }

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

      var RsiStdHigh = rsiHigh.StDev(r => r.PriceRsi);
      var RsiStdLow = rsiLow.StDev(r => r.PriceRsi);

      var rsiSellHigh = rsiHigh.Max(r => r.PriceRsi);
      var rsiBuyLow = rsiLow.Min(r => r.PriceRsi);


      return new RsiStatistics(RsiAverageLow, RsiStdLow, rsiBuyLow, RsiAverageHigh, RsiStdHigh, rsiSellHigh);
    }

    public static TBar[] FindWaves<TBar>(
      this ICollection<TBar> bars, Func<TBar, int> Sign, Func<TBar, double?> Sort) where TBar : BarBase {
      bars = bars.Where(b => Sort(b).GetValueOrDefault(50) != 50).OrderBars().ToArray();
      var barPrev = bars.First();
      var waves = new List<TBar>();
      var wave = new List<TBar>() { barPrev };
      var average = bars.Average(Sort).GetValueOrDefault();
      var stDev = bars.StDev(Sort);
      Func<TBar, double, double, bool> where = (r, a, s) => Sort(r) > (a + s) || Sort(r) < (a - s);
      Sign = (r) => Math.Sign(Sort(r).Value - average);
      bars = bars.Where(b => where(b, average, stDev)).Skip(1).ToArray();
      foreach (var bar in bars) {
        if (Sign(barPrev) == Sign(bar))
          wave.Add(bar);
        else {
          GetRisFractal<TBar>(Sign, Sort, barPrev, waves, wave);
          wave.Clear();
          wave.Add(bar);
        }
        barPrev = bar;
      }
      GetRisFractal<TBar>(Sign, Sort, barPrev, waves, wave);
      return waves.OrderBarsDescending().ToArray();
      /*
      var ws = waves.OrderBarsDescending().ToArray();
      ws.FillHeight(Sort);
      var wh = ws.Where(w => Sign(w) > 0).ToArray();
      var std = wh.StdDev(Sort);
      var avg = wh.Average(Sort);
      wh = wh.Where(w => Sort(w) >= (avg + std)).ToArray();
      var wl = ws.Where(w => Sign(w) < 0).ToArray();
      std = wl.StdDev(Sort);
      avg = wl.Average(Sort);
      wl = wl.Where(w => Sort(w) <= (avg-std)).ToArray();
      return wh.Concat(wl).OrderBarsDescending().ToArray().FixFractals(Sort).ToArray();
      */
    }

    private static void GetRisFractal<TBar>(Func<TBar, int> Sign, Func<TBar, double?> Sort, TBar barPrev, List<TBar> waves, List<TBar> wave) where TBar : BarBase {
      var sort = wave.OrderBy(Sort);
      var w = (Sign(barPrev) >= 0 ? sort.Last() : sort.First()).Clone() as TBar;
      w.Fractal = Sign(barPrev) >= 0 ? FractalType.Sell : FractalType.Buy;
      waves.Add(w);
    }
    private static TBar[] GetRsiFractals<TBar>(this TBar[] rsiHigh) where TBar : BarBase {
      var sell = rsiHigh[0].PriceRsi > 50;
      var fs = new List<TBar>();
      TBar prev = rsiHigh[0];
      var rateMax = new List<TBar>(new[] { prev });
      foreach (var rate in rsiHigh.Skip(1)) {
        if ((prev.StartDate - rate.StartDate) <= TimeSpan.FromMinutes(1))
          rateMax.Add(rate);
        else {
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
              var ticksForPeak = ticks.Where(ticksToRegress.Last(), TimeSpan.FromSeconds(period.TotalSeconds / 2)).ToArray();
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

    public static void FillFlatness(this Rate[] bars, int BarsMin) {
      bars = bars.OrderBarsDescending().ToArray();
      foreach (var bar in bars)
        if (!bar.Flatness.HasValue)
          bar.Flatness = bars.Where(TimeSpan.FromMinutes(120), bar).ToArray().GetFlatness(BarsMin);
    }
    public static TimeSpan GetFlatness(this Rate[] ticks, int barsMin) {
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
      return new[] { TimeSpan.FromMinutes(3), (flats.First().StartDate - flats.Last().StartDate).Add(TimeSpan.FromMinutes(1)) }.Max();

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
    private static void FillSpeed<TBar>(this IEnumerable<TBar> bars, TBar bar, Func<TBar, double> price) where TBar : BarBase {
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
      return bars.TradesPerMinute(barTo.StartDate - intervalFrom, barTo.StartDate);
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, DateTime DaterFrom, DateTime DateTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(DaterFrom, DateTo)).TradesPerMinute();
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      var count = bars.Count();
      var bo = bars.OrderBars().ToArray();
      return count / (bo.Last().StartDate - bo.First().StartDate).TotalMinutes;
    }

    public static Rate[] GetMinuteTicks<TBar>(this ICollection<TBar> fxTicks, int period) where TBar : BarBase {
      return fxTicks.GetMinuteTicks(period, false);
    }
    //static Rate[] GetMinuteTicksCore<TBar>(this IEnumerable<TBar> fxTicks, int period, bool round) where TBar : BarBase {
    //  if (!round) return GetMinuteTicksCore(fxTicks, period,false);
    //  var timeRounded = fxTicks.Min(t => t.StartDate).Round().AddMinutes(1);
    //  return GetMinuteTicksCore(fxTicks.Where(t => t.StartDate >= timeRounded), period,false);
    //}
    public static Rate[] GetMinuteTicks<TBar>(this ICollection<TBar> fxTicks, int period, bool Round, bool startFromEnd = true) where TBar : BarBase {
      fxTicks = startFromEnd ? fxTicks.OrderBarsDescending().ToArray() : fxTicks.OrderBars().ToArray();
      if (fxTicks.Count() == 0) return new Rate[] { };
      var startDate = startFromEnd ? fxTicks.Max(t => t == null ? DateTime.MinValue : t.StartDate) : fxTicks.Min(t => t == null ? DateTime.MinValue : t.StartDate);
      if (Round) startDate = startDate.Round().AddMinutes(1);
      double? tempRsi;
      var rsiAverage = fxTicks.Average(t => t.PriceRsi.GetValueOrDefault());
      return (from t in fxTicks
              where period > 0
              group t by (((int)Math.Floor((startDate - t.StartDate).TotalMinutes) / period)) * period into tg
              orderby startFromEnd ? tg.Key : -tg.Key
              select new Rate() {
                AskHigh = tg.Max(t => t.AskHigh),
                AskLow = tg.Min(t => t.AskLow),
                AskAvg = tg.Average(t => (t.AskHigh + t.AskLow) / 2),
                AskOpen = tg.Last().AskOpen,
                AskClose = tg.First().AskClose,
                BidHigh = tg.Max(t => t.BidHigh),
                BidLow = tg.Min(t => t.BidLow),
                BidAvg = tg.Average(t => (t.BidHigh + t.BidLow) / 2),
                BidOpen = tg.Last().BidOpen,
                BidClose = tg.First().BidClose,
                Mass = tg.Sum(t => t.Mass),
                PriceRsi = !(tempRsi = tg.Average(t => t.PriceRsi)).HasValue ? tempRsi
                             : tempRsi > rsiAverage ? tg.Max(t => t.PriceRsi) : tg.Min(t => t.PriceRsi),
                StartDate = startDate.AddMinutes(-tg.Key)
              }
                ).ToArray();
    }
    public static IEnumerable<Rate> GroupTicksToRates(this IEnumerable<Rate> ticks) {
      return from tick in ticks
             group tick by tick.StartDate.AddMilliseconds(-tick.StartDate.Millisecond) into gt
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
    public static void FillHeight<TBar>(this TBar[] bars, Func<TBar, double?> Price) where TBar : BarBase {
      var i = 0;
      bars.Take(bars.Length - 1).ToList()
        .ForEach(b => {
          b.Ph.Height = (Price(b) - Price(bars[++i])).Abs();
        });
    }
    public static void FillFractalHeight<TBar>(this TBar[] bars) where TBar : BarBase {
      bars.FillFractalHeight(b => b.FractalPrice);
    }
    public static void FillFractalHeight<TBar>(this TBar[] bars, Func<TBar, double?> getPrice) where TBar : BarBase {
      var i = 0;
      bars.Take(bars.Length - 1).ToList()
        .ForEach(b => {
          b.Ph.Height = (getPrice(b) - getPrice(bars[++i])).Abs();
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
    public static List<TBar> FixFractals<TBar>(this IEnumerable<TBar> fractals) where TBar : BarBase {
      return fractals.FixFractals(b => b.FractalPrice);
    }
    public static List<TBar> FixFractals<TBar>(this IEnumerable<TBar> fractals, Func<TBar, double?> Price) where TBar : BarBase {
      var fractalsNew = new List<TBar>();
      foreach (var f in fractals)
        if (fractalsNew.Count > 0 && fractalsNew.Last().Fractal == f.Fractal)
          fractalsNew[fractalsNew.Count - 1] = BarBase.BiggerFractal(fractalsNew.Last(), f, Price);
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
      var period = rates.Length - 2;
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
        if ((ts.Last().StartDate - ts.First().StartDate) > RsiPeriod) {
          ts = ts.Where(RsiPeriod, rate).ToArray();
          rate.PriceRsi = ts.FillRsi(t => t.PriceAvg).Last().PriceRsi;
          if (!rate.PriceRsi.HasValue || double.IsNaN(rate.PriceRsi.Value))
            rate.PriceRsi = rsiPrev;
          rsiPrev = rate.PriceRsi.Value;
        } else rate.PriceRsi = 50;
      }
    }
    public static void FillRsis<TBar>(this TBar[] bars, int RsiTicks) where TBar : BarBase {
      double rsiPrev = 0;
      foreach (var rate in bars.Where(t => !t.PriceRsi.HasValue).AsParallel()) {
        var ts = bars.TakeWhile(t => t <= rate).ToArray();
        if (ts.Length > RsiTicks) {
          ts = ts.Skip(ts.Count() - RsiTicks).ToArray();
          rate.PriceRsi = ts./*GetMinuteTicks(1).*/OrderBars().ToArray().FillRsi(t => t.PriceAvg).Last().PriceRsi;
          if (!rate.PriceRsi.HasValue || double.IsNaN(rate.PriceRsi.Value))
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


    public static IEnumerable<TBar> FillOverlaps<TBar>(this IEnumerable<TBar> bars, TimeSpan period) where TBar : BarBase {
      foreach (var bar in bars)
        bar.FillOverlap(bars.Where(r => r.StartDate < bar.StartDate)/*.Take(10)*/, period);
      return bars;
    }
    public static void SetCMA<TBars>(this ICollection<TBars> bars, Func<TBars, double> cmaSource, int cmaPeriod) where TBars : BarBase {
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
    public static void SetCMA<TBars>(this ICollection<TBars> ticks, int cmaPeriod
      , double? cma1 = null, double? cma2 = null, double? cma3 = null) where TBars : BarBase {
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
    public static TBar FindBar<TBar>(this IEnumerable<TBar> bars, DateTime startDate) where TBar : BarBaseDate {
      if (bars.Count() < 2) return bars.FirstOrDefault();
      for (var node = new LinkedList<TBar>(bars.OrderBars()).First; node.Next != null; node = node.Next)
        if (startDate >= node.Value.StartDate && startDate < node.Next.Value.StartDate)
          return new[] { node.Value, node.Next.Value }.OrderBy(b => (b.StartDate - startDate).Duration()).First();
      return null;
    }
    public static IEnumerable<T> OrderBars<T>(this IEnumerable<T> rates) where T : BarBaseDate {
      return typeof(T) == typeof(Tick) ?
        rates.Cast<Tick>().OrderBy(r => r.StartDate).ThenBy(r => r.Row).Cast<T>() : rates.OrderBy(r => r.StartDate);
    }
    public static IEnumerable<T> OrderBarsDescending<T>(this IEnumerable<T> rates) where T : BarBaseDate {
      return typeof(T) == typeof(Tick) ?
        rates.OfType<Tick>().OrderByDescending(r => r.StartDate).ThenByDescending(r => r.Row).OfType<T>() : rates.ToArray().OrderByDescending(r => r.StartDate);
    }

    class DistanceInfo {
      public double Distance { get; set; }
      public double Ask { get; set; }
      public double Bid { get; set; }
      public DateTime StartDate { get; set; }
      public DistanceInfo(double ask, double bid, DateTime startDate) {
        this.Ask = ask;
        this.Bid = bid;
        this.StartDate = startDate;

      }
    }
    public static RateDistance[] GetDistances(this ICollection<Rate> ticks) {
      var period = ticks.GetPeriod();
      //var tickDistances = (from t in ticks
      //                     //orderby t.StartDate
      //                     select t into t
      //                     join t1 in ticks on t.Index equals t1.Index + 1.0
      //                     select new { Distance = Math.Abs((double)(t.AskClose - t1.AskClose)), Ask = t.AskClose, Bid = t.BidClose, StartDate = t.StartDate.AddMilliseconds(-t.StartDate.Millisecond) }).ToArray();
      if (period >= TimeSpan.FromMinutes(1)) {
        var rds = new List<RateDistance>();
        ticks.ToList().ForEach(t => {
          rds.Insert(0, new RateDistance(t.PriceHigh, t.PriceLow, 0, t.StartDate));
        });
        return rds.ToArray();
      }
      var distances = new List<DistanceInfo>();
      ticks.Aggregate((tp, tn) => {
        distances.Insert(0,
          new DistanceInfo(tn.AskClose, tn.BidClose, tn.StartDate.AddMilliseconds(-tn.StartDate.Millisecond)));
        return tn;
      });
      int i = 0;
      return (from t in
                from t in distances
                //orderby t.StartDate descending
                select t
              group t by t.StartDate into g
              let l = ++i
              select new {
                g, MA = g.Skip(l).Take(5).DefaultIfEmpty().Average(ga => {
                  if (ga != null) {
                    return (ga.Ask + ga.Bid) / 2.0;
                  }
                  return 0.0;
                })
              }).Select(gMA => {
                if (gMA.g == null) {
                  Debugger.Break();
                }
                return new RateDistance(
                  gMA.g.Average(t => t.Ask),
                  gMA.g.Average(t => t.Bid),
                  gMA.MA,
                  gMA.g.Key);
              }).ToArray<RateDistance>();
    }
    public static PriceBar[] GetPriceBars(this ICollection<Rate> Rates, double PointSize, int rowCountOffset) {
      int periodMin = 1;
      double rowCurr;
      double spreadAsk;
      double spreadBid;
      double askMax = double.MinValue;
      double askMin = double.MaxValue;
      double bidMax = double.MinValue;
      double bidMin = double.MaxValue;
      DateTime firstBarDate = DateTime.MaxValue;
      int digits = 4;
      Func<int, double> calcRowOffest = i => Math.Pow(rowCountOffset, 1 / Math.Pow(i, 1 / 4.0));
      var rates_01 = Rates.Select(((r, i) =>{
        return
new {
  AskHigh = askMax = Math.Max(askMax, r.AskHigh).Round(digits),
  AskLow = askMin = Math.Min(askMin, r.AskLow).Round(digits),
  BidLow = bidMin = Math.Round(Math.Min(bidMin, r.BidLow), digits),
  BidHigh = bidMax = Math.Round(Math.Max(bidMax, r.BidHigh), digits),
  SpreadAsk = spreadAsk = Math.Max(r.AskHigh - askMin/* Rates.Take((i + 1)).Min((al => al.AskLow))*/,
               /*Rates.Take(i + 1).Max((al => al.AskHigh))*/askMax - r.AskLow),
  SpreadBid = spreadBid = Math.Max(r.BidHigh - bidMin/*Rates.Take(i + 1).Min(al => al.BidLow)*/,
               /*Rates.Take(i + 1).Max(((al => al.BidHigh)))*/bidMax - r.BidLow),
  StartDate = (i == 0) ? (firstBarDate = r.StartDate) : r.StartDate,
  Row = rowCurr = Math.Min(/*(serverTime - firstBarDate).TotalMinutes / (periodMin)*/0, 0.0) + i,
  SpeedAsk = spreadAsk / ((rowCurr + calcRowOffest(i + 1)) * periodMin),
  SpeedBid = spreadBid / ((rowCurr + calcRowOffest(i + 1)) * periodMin)
};
})).ToArray();
      return rates_01
        .Select(((r, i) => new PriceBar { AskHigh = r.AskHigh, AskLow = r.AskLow, BidLow = r.BidLow, BidHigh = r.BidHigh, 
          Spread = ((r.SpreadAsk + r.SpreadBid) / 2.0) / PointSize, 
          Speed = ((r.SpeedAsk + r.SpeedBid) / 2.0) / PointSize, Row = r.Row, StartDate = r.StartDate })).ToArray();
    }

  }
}
