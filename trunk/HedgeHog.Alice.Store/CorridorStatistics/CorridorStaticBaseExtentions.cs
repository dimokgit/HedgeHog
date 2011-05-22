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
    private static IEnumerable<CorridorStatistics.LegInfo> GetLegInfo(this Rate[] rates
      ,Func<Rate, double> linePrice, Func<Rate, double> highPrice, Func<Rate, double> lowPrice
      , TimeSpan interval, double pointSize, out Rate[] ratesHigh, out Rate[] ratesLow) {
      var highs = rates.Select((r, i) => new { rate = r, height = r.RegressionHeight(linePrice,highPrice,lowPrice, true) }).ToArray();
      var lows = rates.Select((r, i) => new { rate = r, height = r.RegressionHeight(linePrice, highPrice, lowPrice, false) }).ToArray();
      ratesHigh = highs.AverageByIterations(a => a.height, (a, v) => a.height >= v, 4)
        .Select(a => a.rate).OrderByDescending(r => r.StartDate).ToArray();
      ratesLow = lows.AverageByIterations(a => a.height, (a, v) => a.height >= v, 4)
        .Select(a => a.rate).OrderByDescending(r => r.StartDate).ToArray();
      var rateHigh = ratesHigh.FirstOrDefault();
      var rateLow = ratesLow.FirstOrDefault();
      var legInfos = new List<CorridorStatistics.LegInfo>();
      while (rateHigh != null && rateLow != null) {
        legInfos.Add(new CorridorStatistics.LegInfo(rateHigh, rateLow, interval, pointSize));
        var startdate = rateHigh.StartDate.Min(rateLow.StartDate);
        rateHigh = ratesHigh.FirstOrDefault(r => r.StartDate <= startdate);
        rateLow = ratesLow.FirstOrDefault(r => r.StartDate <= startdate);
      }
      return legInfos;
    }
    private static double RegressionHeight(this Rate rate,Func<Rate, double> linePrice, Func<Rate, double> highPrice, Func<Rate, double> lowPrice, bool isUp) {
      return isUp ? highPrice(rate) - linePrice(rate): linePrice(rate) - lowPrice(rate);
    }

    private static IEnumerable<CorridorStatistics.LegInfo> GetLegInfo(IList<Rate> rates, double[] coeffs, TimeSpan interval, double pointSize, out Rate[] ratesHigh, out Rate[] ratesLow) {
      var highs = rates.Select((r, i) => new { rate = r, height = GetRateRegressionHeight(coeffs, true, r, i) }).ToArray();
      var lows = rates.Select((r, i) => new { rate = r, height = GetRateRegressionHeight(coeffs, false, r, i) }).ToArray();
      ratesHigh = highs.AverageByIterations(a => a.height, (a, v) => a.height >= v, 4)
        .Select(a => a.rate).OrderByDescending(r => r.StartDate).ToArray();
      ratesLow = lows.AverageByIterations(a => a.height, (a, v) => a.height >= v, 4)
        .Select(a => a.rate).OrderByDescending(r => r.StartDate).ToArray();
      var rateHigh = ratesHigh.FirstOrDefault();
      var rateLow = ratesLow.FirstOrDefault();
      var legInfos = new List<CorridorStatistics.LegInfo>();
      while (rateHigh != null && rateLow != null) {
        var startdate = rateHigh.StartDate.Min(rateLow.StartDate);
        legInfos.Add(new CorridorStatistics.LegInfo(rateHigh, rateLow, interval, pointSize));
        rateHigh = ratesHigh.FirstOrDefault(r => r.StartDate <= startdate);
        rateLow = ratesLow.FirstOrDefault(r => r.StartDate <= startdate);
      }
      return legInfos;
    }

    private static double GetRateRegressionHeight(double[] coeffs, bool isUp, Rate rate, int index) {
      return isUp ? rate.AskHigh - coeffs.RegressionValue(index) : coeffs.RegressionValue(index) - rate.BidLow;
    }

    public static Dictionary<int, CorridorStatistics> GetCorridornesses(
      this ICollection<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength
      ,TimeSpan barsInterval,double pointSize, Predicate<CorridorStatistics> exitCondition) {
      return rates.GetCorridornessesCore(priceHigh, priceLow, periodsStart, periodsLength,barsInterval,pointSize, exitCondition);
    }
    static Dictionary<int, CorridorStatistics> GetCorridornessesCore(this ICollection<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength
      , TimeSpan barsInterval, double pointSize, Predicate<CorridorStatistics> exitCondition) {
      var corridornesses = new Dictionary<int, CorridorStatistics>();
      if (rates.Count() > 2) {
        try {
          if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToArray();
          else rates = rates.ToArray();
          {
            //Stopwatch sw = Stopwatch.StartNew(); int swCount = 1;            /*sw.Stop();*/ Debug.WriteLine("ScanCorridorWithAngle " + (swCount) + ":" + sw.ElapsedMilliseconds + " ms."); //sw.Restart();
            var periodsEnd = Math.Min(rates.Count(), periodsStart + periodsLength);
            periodsStart = Math.Min(rates.Count() - 1, periodsStart);
            for (var i = periodsStart; i < periodsEnd; i++ /*= i + Math.Max(1, i / 100.0).Ceiling() * Math.Max(1, i / 1000.0).Ceiling()*/) {
              var ratesForCorr = rates.Take(i).ToArray();
              var cs = ratesForCorr.ScanCorridorWithAngle(priceHigh, priceLow, barsInterval, pointSize);
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

    public static CorridorStatistics ScanCorridorWithAngle(this ICollection<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, TimeSpan barsInterval, double pointSize, bool userRegression = true) {
      try {
        rates = rates.ToArray();
        #region Funcs
        double[] linePrices = new double[rates.Count()];
        Func<int, double> lineGet = index => linePrices[index];
        Action<int, double> lineSet = (index, d) => linePrices[index] = d;
        var coeffs = rates.SetRegressionPrice(1, rate => rate.PriceAvg, lineSet);
        Func<Rate,int, double> heightHigh = (rate,index) => priceHigh(rate) - lineGet(index);
        Func<Rate,int, double> heightLow = (rate,index) => lineGet(index) - priceLow(rate);
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
          height0 = rates.StDev(r => r.PriceAvg);// rates.StDev((r, i) => r.PriceAvg > lineGet(i) ? priceHigh(r) : priceLow(r));// ratesForHeight.StDev();
          height = height0 * 2;
          density = (heightDown + heightUp) / periods;
        return new CorridorStatistics(rates.ToArray(),density, coeffs, height0, height0, height, height, lineHigh, lineLow, periods, rates.First().StartDate, rates.Last().StartDate) {
          priceLine = linePrices, priceHigh = priceHigh, priceLow = priceLow
        };
      } catch (Exception exc) {
        return null;
        Debug.Fail(exc + "");
        throw;
      } finally {
      }
    }
  }

}
