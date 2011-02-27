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
    public static Dictionary<int, CorridorStatistics> GetCorridornesses(this List<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength, int iterationsForHeights, bool useRegression = true) {
      return rates.GetCorridornessesCore(priceHigh, priceLow, periodsStart, periodsLength,iterationsForHeights, useRegression);
    }
    static Dictionary<int, CorridorStatistics> GetCorridornessesCore(this IEnumerable<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength, int iterationsForHeights, bool useRegression = true) {
      try {
        if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
        else rates = rates.ToArray();
        var corridornesses = new Dictionary<int, CorridorStatistics>();
        {
          //Stopwatch sw = Stopwatch.StartNew(); int swCount = 1;            /*sw.Stop();*/ Debug.WriteLine("ScanCorridorWithAngle " + (swCount) + ":" + sw.ElapsedMilliseconds + " ms."); //sw.Restart();
          rates.OrderBars().ToArray().Index();
          var periodsEnd = Math.Min(rates.Count(), periodsStart + periodsLength);
          periodsStart = Math.Min(rates.Count() - 1, periodsStart);
          for (var i = periodsStart; i < periodsEnd; i = i + Math.Max(1, i / 100.0).Ceiling() * Math.Max(1, i / 1000.0).Ceiling()) {
            var cs = rates.Take(i).ToArray().ScanCorridorWithAngle(priceHigh, priceLow,iterationsForHeights, useRegression);
            if (cs != null)
              corridornesses.Add(i, cs);
          }
        }
        return corridornesses;
      } catch (Exception exc) {
        Debug.Fail(exc + "");
        throw;
      }
    }
    public static Func<double, double, bool> priceHeightComparer = (d1, d2) => d1 >= d2;

    public static CorridorStatistics ScanCorridorWithAngle(this IEnumerable<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int iterationsForHeights, bool userRegression = true) {
      try {
        #region Funcs
        Func<Rate, double> priceGet = rate => rate.PriceAvg4;
        Action<Rate, double> priceSet = (rate, d) => rate.PriceAvg4 = d;
        var coeffs = rates.SetRegressionPrice(1, rate => rate.PriceAvg, priceSet);
        Func<Rate, double> heightHigh = rate => priceHigh(rate) - priceGet(rate);
        Func<Rate, double> heightLow = rate => priceGet(rate) - priceLow(rate);
        #endregion
        #region Locals
        var minimumCount = 1;
        var percentage = .20;
        var margin = Math.Max(2, rates.Count() / 100.0).ToInt();
        var iterations = 2;
        var heightUp = 0.0;
        var heightDown = 0.0;
        var heightUp0 = 0.0;
        var heightDown0 = 0.0;
        var density = 0.0;
        var periods = rates.Count();
        var lineLow = new LineInfo(new Rate[0], 0, 0);
        var lineHigh = new LineInfo(new Rate[0], 0, 0);
        #endregion
        if (userRegression) {
          #region UseRegression
          Func<Rate, Rate, Rate> peak = (ra, rn) => heightHigh(ra) > heightHigh(rn) ? ra : rn;
          var highs = rates.Where(r => heightHigh(r) > 0).ToArray();
          highs = highs.AverageByIterations(heightHigh, priceHeightComparer, iterations);
          if (userRegression)
            highs = highs.FindExtreams(peak, margin).AverageByIterations(heightHigh, priceHeightComparer, iterations - 1);
          var coefsHigh = userRegression ? highs.FindLine(priceHigh, new[] { 0, -2.0 }) : new[] { 0.0, 0.0 };
          var angleHigh = coefsHigh[1].Angle();
          lineHigh = new LineInfo(highs, coefsHigh[0], coefsHigh[1]);

          Func<Rate, Rate, Rate> valley = (ra, rn) => heightLow(ra) > heightLow(rn) ? ra : rn;
          var lows = rates.Where(r => heightLow(r) > 0).ToArray();
          lows = lows.AverageByIterations(heightLow, priceHeightComparer, iterations);
          if (userRegression)
            lows = lows.FindExtreams(valley, margin).AverageByIterations(heightLow, priceHeightComparer, iterations - 1);
          var coefsLow = userRegression ? lows.FindLine(priceLow, new[] { 0, 1.0 }) : new[] { 0.0, 0.0 };
          var angleLow = coefsLow[1].Angle();
          lineLow = new LineInfo(lows, coefsLow[0], coefsLow[1]);

          var angles = new[] { 
            new{Angle = angleHigh, Diff = angleHigh.Error(coeffs[1])},
            new {Angle = angleLow,Diff =  angleLow.Error(coeffs[1])},
          }.OrderBy(d => d.Diff).Take(2).ToArray();
          heightUp = highs.Select(heightHigh).DefaultIfEmpty().Average();
          heightDown = lows.Select(heightLow).DefaultIfEmpty().Average();
          density = angles.Average(d => d.Diff);
          #endregion
        } else {
          rates.GetCorridorHeights(new Rate[0], new Rate[0], priceGet, priceHigh, priceLow, priceHeightComparer, 2, 1, out heightUp0, out heightDown0);
          heightUp0 = heightDown0 = rates.Select(heightHigh).Union(rates.Select(heightLow)).ToArray().StDev();
          rates.GetCorridorHeights(new Rate[0], new Rate[0], priceGet, priceHigh, priceLow, priceHeightComparer, 2, iterationsForHeights, out heightUp, out heightDown);
          heightUp = heightDown = heightDown0 * 2;
          density = (heightDown + heightUp) / periods;
        }
        rates.ToList().ForEach(r => priceSet(r, 0));
        var height0 = Math.Max(heightUp0, heightDown0);
        var height = Math.Max(heightUp, heightDown);
        return new CorridorStatistics(density, -coeffs[1], height0, height0, height, height, lineHigh, lineLow, periods, rates.First().StartDate, rates.Last().StartDate) {
          priceLine = priceGet, priceHigh = priceHigh, priceLow = priceLow
        };
      } catch (Exception exc) {
        return null;
        Debug.Fail(exc + "");
        throw;
      } finally {
      }
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
