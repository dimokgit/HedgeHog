using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;
using System.Windows;

namespace HedgeHog.Alice.Store {
  public class LineInfo {
    public Rate[] Line { get; set; }
    public double Offset { get; set; }
    public double Slope { get; set; }
    public LineInfo(Rate[] line, double offset, double slope) {
      this.Line = line;
      this.Offset = offset;
      this.Slope = slope;
    }
  }
  public static class CorridorStaticBaseExtentions {
    public static Dictionary<int, CorridorStatistics> GetCorridornesses(this IEnumerable<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, bool useStDev, int periodsStart, int periodsLength) {
      try {
        if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
        else rates = rates.ToArray();
        var corridornesses = new Dictionary<int, CorridorStatistics>();
        {
          //Stopwatch sw = Stopwatch.StartNew(); int swCount = 1;            /*sw.Stop();*/ Debug.WriteLine("ScanCorridorWithAngle " + (swCount) + ":" + sw.ElapsedMilliseconds + " ms."); //sw.Restart();
          rates.ElementAt(0).Index = 0;
          rates.Aggregate((rp, rn) => { rn.Index = rp.Index + 1; return rn; });
          var periodsEnd = Math.Min(rates.Count(), periodsStart + periodsLength);
          for (var i = periodsStart; i < periodsEnd; i = i + Math.Max(1, i / 100.0).Floor())
            corridornesses.Add(i, rates.Take(i).ToArray().ScanCorridorWithAngle( priceHigh, priceLow, useStDev));
        }
        return corridornesses;
      } catch (Exception exc) {
        Debug.Fail(exc + "");
        throw;
      }
    }

    static CorridorStatistics ScanCorridorWithAngle(this IEnumerable<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, bool useStDev) {
      try {
        Func<Rate, double> priceGet = rate => rate.PriceAvg4;
        Action<Rate, double> priceSet = (rate, d) => rate.PriceAvg4 = d;
        var coeffs = rates.SetRegressionPrice(1, rate => rate.PriceAvg, priceSet);
        Func<Rate, double> heightHigh = rate => priceHigh(rate) - priceGet(rate);
        Func<Rate, double> heightLow = rate => priceGet(rate) - priceLow(rate);
        Func<double, double, bool> comparer = (d1, d2) => d1 >= d2;
        Func<Point, Point, double> slope = (p1, p2) => (p1.Y - p2.Y) / (p2.X - p1.X);
        Func<Rate, Rate, double> rateSlope = (r1, r2) => slope(new Point(r1.PriceAvg, r1.Index), new Point(r2.PriceAvg, r2.Index));
        Func<Rate[],double> ratesSlope = rs=>rateSlope(rs.First(),rs.Last());
        var minimumCount = 1;
        var percentage = .20;
        var margin = Math.Max(2, rates.Count() / 100.0).ToInt();
        var iterations = 3;
        //var c = MathExtensions.Linear(rates.Select(r => (r.StartDate - dateStart).TotalMinutes).ToArray(), rates.Select(r => r.PriceAvg).ToArray());

        double timeSpanRatioHigh;
        Func<Rate, Rate, Rate> peak = (ra, rn) => new[] { ra, rn }.OrderBy(r => heightHigh(r)).Last();
        var highs = rates.Where(r => heightHigh(r) > 0).ToArray();
        highs = highs.AverageByIterations(heightHigh,comparer,iterations)
          .FindExtreams(peak,margin)
          .AverageByIterations(heightHigh,comparer,2);
          //.AverageByPercantage( heightHigh, percentage, minimumCount, out timeSpanRatioHigh);
          //.FindExtreams(peak);
        var coefsHigh = highs.FindLine(priceHigh, new[] { 0, -2.0 });
          //highs.Length < 3 ? new[]{0,-2.0} : MathExtensions.Linear(highs.Select(r => (double)r.Index).ToArray(), highs.Select(priceHigh).ToArray());
        var angleHigh = coefsHigh[1].Angle();
        var lineHigh = new LineInfo(highs, coefsHigh[0], coefsHigh[1]);

        double timeSpanRatioLow;
        Func<Rate, Rate, Rate> valley = (ra, rn) => new[] { ra, rn }.OrderBy(r => heightLow(r)).Last();
        var lows = rates.Where(r => heightLow(r) > 0).ToArray();
        lows = lows.AverageByIterations(heightLow, comparer, iterations)
          .FindExtreams(valley,margin)
          .AverageByIterations(heightLow, comparer, 2);
        //.AverageByPercantage(heightLow, percentage, minimumCount, out timeSpanRatioLow);
          //.FindExtreams(valley);
        var coefsLow = lows.FindLine(priceLow, new[] { 0, 1.0 });
          //lows.Length < 3 ? new []{0,1.0} : MathExtensions.Linear(lows.Select(r => (double)r.Index).ToArray(), lows.Select(priceLow).ToArray());
        var angleLow = coefsLow[1].Angle();
        var lineLow = new LineInfo(lows, coefsLow[0], coefsLow[1]);

        //var angleHighLow = new[] { lineHigh.Slope, lineLow.Slope }.Average();
        var angles =  new[] { 
            //new{Angle = angleHigh, Diff = DifferenceRatio(coeffs[1], angleHigh)},
            //new {Angle = angleLow,Diff =  DifferenceRatio(coeffs[1], angleLow)},
            //new {Angle = angleHighLow,Diff =  DifferenceRatio(coeffs[1], angleHighLow)},
            new {Angle = new[]{angleHigh,angleLow}.Average(),Diff = DifferenceRatio(angleHigh, angleLow) }}
            .OrderBy(d => d.Diff)
            .Take(1).ToArray();
        var heightUp = highs.Select(heightHigh).DefaultIfEmpty().Average();
        var heightDown = lows.Select(heightLow).DefaultIfEmpty().Average();
        var periods = rates.Count();
        var density = angles.Average(d => d.Diff);
        //density = (heightDown + heightUp) / periods;
        rates.ToList().ForEach(r => priceSet(r, 0));
        return new CorridorStatistics(density,heightUp,heightDown,lineHigh,lineLow, periods, rates.First().StartDate, rates.Last().StartDate);
      } catch (Exception exc) {
        Debug.Fail(exc + "");
        throw;
      } finally {
      }
    }

    private static double DifferenceRatio(double value1, double value2) {
      return (value2.Abs() > value1.Abs()) ? ((value1 - value2) / value2).Abs() : ((value1 - value2) / value1).Abs();
    }
    static double[] AverageByPercantage(double[] values, double percentage) {
      var average = 0.0;
      var countOriginal = values.Count();
      var countCurrent = countOriginal + 1.0;
      do {
        average = values.Where(v => v >= average).Average();
        values = values.Where(v => v >= average).ToArray();
        if (countCurrent == values.Count()) break;
        countCurrent = values.Length;
      } while (countCurrent / countOriginal > percentage);
      return values;
    }
  }

}
