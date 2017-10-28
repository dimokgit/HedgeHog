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
    /// <param name="corridorMethod"></param>
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
    /// <param name="corridorMethod"></param>
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
      return rates.ScanCorridorWithAngle(r => r.PriceAvg, priceHigh, priceLow, barsInterval, pointSize, corridorMethod);
    }
    public static CorridorStatistics ScanCorridorWithAngle(this IList<Rate> source,Func<Rate,double>price, Func<Rate, double> priceHigh, Func<Rate, double> priceLow, TimeSpan barsInterval, double pointSize, CorridorCalculationMethod corridorMethod) {
      try {
        if (source == null) throw new ArgumentNullException("Rates list must not be null.");
        if (source.Count == 0) throw new ArgumentOutOfRangeException("Rates list must not be empty.");
        var rates = source.ToList();
        #region Funcs
        double[] linePrices = new double[rates.Count()];
        Func<int, double> priceLine = index => linePrices[index];
        Action<int, double> lineSet = (index, d) => linePrices[index] = d;
        var coeffs = rates.SetRegressionPrice(price, lineSet);
        var sineOffset = 1;// Math.Sin(Math.PI / 2 - coeffs[1] / pointSize);
        Func<Rate, int, double> heightHigh = (rate, index) => (priceHigh(rate) - priceLine(index)) * sineOffset;
        Func<Rate, int, double> heightLow = (rate, index) => (priceLow(rate) - priceLine(index)) * sineOffset;
        #endregion
        #region Locals
        var lineLow = new LineInfo(new Rate[0], 0, 0);
        var lineHigh = new LineInfo(new Rate[0], 0, 0);
        double stDev = double.NaN;
        double height = double.NaN;
        #endregion

        var stDevDict = new Dictionary<CorridorCalculationMethod, double>();
        if( corridorMethod == CorridorCalculationMethod.Minimum || corridorMethod == CorridorCalculationMethod.Maximum){
          stDevDict.Add(CorridorCalculationMethod.HeightUD,rates.Select(heightHigh).Union(rates.Select(heightLow)).ToList().StDevP());
          stDevDict.Add(CorridorCalculationMethod.Height, rates.Select((r, i) => heightHigh(r, i).Abs() + heightLow(r, i).Abs()).ToList().StDevP());
          if (corridorMethod == CorridorCalculationMethod.Minimum)
            stDevDict.Add(CorridorCalculationMethod.Price, rates.GetPriceForStats(price, priceLine, priceHigh, priceLow).ToList().StDevP());
          else
            stDevDict.Add(CorridorCalculationMethod.PriceAverage, rates.StDev(price));
        }else
          switch (corridorMethod) {
            case CorridorCalculationMethod.Minimum:
              stDevDict.Add(CorridorCalculationMethod.Minimum, stDev = stDevDict.Values.Min()); break;
            case CorridorCalculationMethod.Maximum:
              stDevDict.Add(CorridorCalculationMethod.Maximum, stDev = stDevDict.Values.Max()); break;
            case CorridorCalculationMethod.MinMax:
            case CorridorCalculationMethod.MinMaxMM:
            case CorridorCalculationMethod.Height:
              double max = double.MinValue, min = double.MaxValue;
              var sd = rates.Select(price).ToArray().StDevByRegressoin(coeffs, diffs => { max = diffs.Max(max); min = diffs.Min(min); });
              height = max - min;
              stDevDict.Add(CorridorCalculationMethod.Height, stDev = sd/* heights.StDevP()*/);
              break;
            case CorridorCalculationMethod.HeightUD:
              stDevDict.Add(CorridorCalculationMethod.Height, stDev = rates.Select(heightHigh).Union(rates.Select(heightLow)).ToList().StDevP());
              break;
            case CorridorCalculationMethod.Price:
              stDevDict.Add(CorridorCalculationMethod.Price, stDev = rates.GetPriceForStats(price, priceLine, priceHigh, priceLow).ToList().StDevP()); break;
            default:
              throw new NotSupportedException(new { corridorMethod } + "");
          }
        stDevDict.Add(CorridorCalculationMethod.PriceAverage, rates.StDev(price));

        return new CorridorStatistics(rates, stDev, coeffs, stDev, stDev, stDev * 2, stDev * 2) {
          priceLine = linePrices, priceHigh = priceHigh, priceLow = priceLow,
          StDevs = stDevDict, HeightByRegression = height.IfNaN(stDevDict[CorridorCalculationMethod.Height] * 4)
        };
      } catch (Exception exc) {
        Debug.WriteLine(exc);
        throw;
      }
    }

  }

}
