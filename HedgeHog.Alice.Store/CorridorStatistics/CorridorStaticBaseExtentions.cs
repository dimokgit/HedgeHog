using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using System.Diagnostics;
using System.Windows;
using HedgeHog.Bars;

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
    /// <summary>
    /// 
    /// </summary>
    /// <param name="rates"></param>
    /// <param name="priceHigh"></param>
    /// <param name="priceLow"></param>
    /// <param name="periodsStart">Must be >= 1</param>
    /// <param name="periodsLength"></param>
    /// <param name="barsInterval"></param>
    /// <param name="pointSize"></param>
    /// <param name="exitCondition"></param>
    /// <returns></returns>
    public static Dictionary<int, CorridorStatistics> GetCorridornesses(
      this ICollection<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength
      , TimeSpan barsInterval, double pointSize, CorridorCalculationMethod corridorMethod, Predicate<CorridorStatistics> exitCondition) {
        return rates.GetCorridornessesCore(priceHigh, priceLow, periodsStart, periodsLength, barsInterval, pointSize, corridorMethod, exitCondition);
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="rates"></param>
    /// <param name="priceHigh"></param>
    /// <param name="priceLow"></param>
    /// <param name="periodsStart">Must be >= 1</param>
    /// <param name="periodsLength"></param>
    /// <param name="barsInterval"></param>
    /// <param name="pointSize"></param>
    /// <param name="exitCondition"></param>
    /// <returns></returns>
    static Dictionary<int, CorridorStatistics> GetCorridornessesCore(this ICollection<Rate> rates
      , Func<Rate, double> priceHigh, Func<Rate, double> priceLow, int periodsStart, int periodsLength
      , TimeSpan barsInterval, double pointSize,CorridorCalculationMethod corridorMethod, Predicate<CorridorStatistics> exitCondition) {
      var corridornesses = new Dictionary<int, CorridorStatistics>();
      if (rates.Count() > 2) {
        try {
          if (rates.Last().StartDate > rates.First().StartDate) rates = rates.Reverse().ToList();
          else rates = rates.ToArray();
          {
            ////Stopwatch sw = Stopwatch.StartNew(); int swCount = 1;            /*sw.Stop();*/ Debug.WriteLine("ScanCorridorWithAngle " + (swCount) + ":" + sw.ElapsedMilliseconds + " ms."); //sw.Restart();
            //var periodsEnd = Math.Min(rates.Count(), periodsStart + periodsLength);
            //periodsStart = Math.Min(rates.Count() - 1, periodsStart);
            //for (var i = periodsStart; i < periodsEnd; i++ /*= i + Math.Max(1, i / 100.0).Ceiling() * Math.Max(1, i / 1000.0).Ceiling()*/) {
            periodsStart = periodsStart.Min(rates.Count);
            periodsLength = periodsLength.Min(rates.Count - periodsStart + 1);
            foreach(var i in Enumerable.Range(periodsStart, periodsLength)){
              var ratesForCorr = rates.Take(i).ToList();
              var cs = ratesForCorr.ScanCorridorWithAngle(priceHigh, priceLow, barsInterval, pointSize, corridorMethod);
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

    public static CorridorStatistics ScanCorridorWithAngle(this IList<Rate> rates, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, TimeSpan barsInterval, double pointSize, CorridorCalculationMethod corridorMethod) {
      try {
        #region Funcs
        double[] linePrices = new double[rates.Count()];
        Func<int, double> priceLine = index => linePrices[index];
        Action<int, double> lineSet = (index, d) => linePrices[index] = d;
        var coeffs = rates.SetRegressionPrice(1, rate => rate.PriceAvg, lineSet);
        var sineOffset = Math.Sin(Math.PI / 2 - coeffs[1] / pointSize);
        Func<Rate, int, double> heightHigh = (rate, index) => (priceHigh(rate) - priceLine(index)) * sineOffset;
        Func<Rate, int, double> heightLow = (rate, index) => (priceLine(index) - priceLow(rate)) * sineOffset;
        #endregion
        #region Locals
        var lineLow = new LineInfo(new Rate[0], 0, 0);
        var lineHigh = new LineInfo(new Rate[0], 0, 0);
        double stDev = double.NaN;
        double height;
        #endregion

        var stDevDict = new Dictionary<CorridorCalculationMethod, double>();
        if( corridorMethod == CorridorCalculationMethod.Minimum || corridorMethod == CorridorCalculationMethod.Maximum){
          stDevDict.Add(CorridorCalculationMethod.HeightUD,rates.Select(heightHigh).Union(rates.Select(heightLow)).ToList().StDevP());
          stDevDict.Add(CorridorCalculationMethod.Height, rates.Select((r, i) => heightHigh(r, i).Abs() + heightLow(r, i).Abs()).ToList().StDevP());
          if (corridorMethod == CorridorCalculationMethod.Minimum)
            stDevDict.Add(CorridorCalculationMethod.Price, rates.GetPriceForStats(priceLine, priceHigh, priceLow).ToList().StDevP());
          else
            stDevDict.Add(CorridorCalculationMethod.PriceAverage, rates.StDev(r => r.PriceAvg));
        }else
          switch (corridorMethod) {
            case CorridorCalculationMethod.Minimum:
              stDevDict.Add(CorridorCalculationMethod.Minimum, stDev = stDevDict.Values.Min()); break;
            case CorridorCalculationMethod.Maximum:
              stDevDict.Add(CorridorCalculationMethod.Maximum, stDev = stDevDict.Values.Max()); break;
            case CorridorCalculationMethod.Height:
              stDevDict.Add(CorridorCalculationMethod.Height, stDev = rates.Select((r, i) => heightHigh(r, i).Abs() + heightLow(r, i).Abs()).ToList().StDevP()); break;
            case CorridorCalculationMethod.HeightUD:
              stDevDict.Add(CorridorCalculationMethod.HeightUD, stDev = rates.Select(heightHigh).Union(rates.Select(heightLow)).ToList().StDevP()); break;
            case CorridorCalculationMethod.Price:
              stDevDict.Add(CorridorCalculationMethod.Price, stDev = rates.GetPriceForStats(priceLine, priceHigh, priceLow).ToList().StDevP()); break;
            default:
              throw new NotSupportedException(new { corridorMethod } + "");
          }
        stDevDict.Add(CorridorCalculationMethod.PriceAverage, rates.StDev(r => r.PriceAvg));
        height = stDev * 2;
        return new CorridorStatistics(rates, stDev, coeffs, stDev, stDev, height, height) {
          priceLine = linePrices, priceHigh = priceHigh, priceLow = priceLow, StDevs = stDevDict
        };
      } catch (Exception exc) {
        Debug.WriteLine(exc);
        throw;
      }
    }

  }

}
