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
    public static Dictionary<int, CorridorStatistics> GetCorridornesses(this ICollection<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength, Predicate<CorridorStatistics> exitCondition) {
      return rates.GetCorridornessesCore(priceHigh, priceLow, periodsStart, periodsLength, exitCondition);
    }
    static Dictionary<int, CorridorStatistics> GetCorridornessesCore(this ICollection<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength, Predicate<CorridorStatistics> exitCondition) {
      var corridornesses = new Dictionary<int, CorridorStatistics>();
      if (rates.Count() > 2) {
        try {
          if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
          else rates = rates.ToArray();
          {
            //Stopwatch sw = Stopwatch.StartNew(); int swCount = 1;            /*sw.Stop();*/ Debug.WriteLine("ScanCorridorWithAngle " + (swCount) + ":" + sw.ElapsedMilliseconds + " ms."); //sw.Restart();
            rates.OrderBars().ToArray().Index();
            var periodsEnd = Math.Min(rates.Count(), periodsStart + periodsLength);
            periodsStart = Math.Min(rates.Count() - 1, periodsStart);
            for (var i = periodsStart; i < periodsEnd; i++ /*= i + Math.Max(1, i / 100.0).Ceiling() * Math.Max(1, i / 1000.0).Ceiling()*/) {
              var ratesForCorr = rates.Take(i).ToArray();
              var cs = ratesForCorr.ScanCorridorWithAngle(priceHigh, priceLow);
              if (cs != null) {
                corridornesses.Add(i, cs);
                if (exitCondition(cs)) break;
              }
            }
          }
        } catch (Exception exc) {
          Debug.Fail(exc + "");
          throw;
        }
      }
      return corridornesses;
    }
    public static Func<double, double, bool> priceHeightComparer = (d1, d2) => d1 >= d2;

    public static CorridorStatistics ScanCorridorWithAngle(this IEnumerable<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, bool userRegression = true) {
      try {
        #region Funcs
        Func<Rate, double> priceGet = rate => rate.PriceAvg4;
        Action<Rate, double> priceSet = (rate, d) => rate.PriceAvg4 = d;
        var coeffs = rates.SetRegressionPrice(1, rate => rate.PriceAvg, priceSet);
        Func<Rate, double> heightHigh = rate => priceHigh(rate) - priceGet(rate);
        Func<Rate, double> heightLow = rate => priceGet(rate) - priceLow(rate);
        #endregion
        #region Locals
        var heightUp = 0.0;
        var heightDown = 0.0;
        var density = 0.0;
        var periods = rates.Count();
        var lineLow = new LineInfo(new Rate[0], 0, 0);
        var lineHigh = new LineInfo(new Rate[0], 0, 0);
        double height0;
        double height;
        #endregion
          var ratesForHeight = rates.Select(heightHigh).Union(rates.Select(heightLow)).ToArray();
          height0 = ratesForHeight.StDev();
          height = height0 * 2;
          //rates.GetCorridorHeights(new Rate[0], new Rate[0], priceGet, priceHigh, priceLow, priceHeightComparer, 2, out heightUp, out heightDown);
          density = (heightDown + heightUp) / periods;
        rates.ToList().ForEach(r => priceSet(r, 0));
        return new CorridorStatistics(rates.ToArray(),density, coeffs, height0, height0, height, height, lineHigh, lineLow, periods, rates.First().StartDate, rates.Last().StartDate) {
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
