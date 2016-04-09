using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace HedgeHog.Alice.Store {
  public class WaveRange {
    #region Properties
    public bool IsEmpty { get { return Count == 0; } }
    public bool IsTail { get; set; }
    public List<Rate> Range { get; set; }
    public double PointSize { get; set; }
    public DateTime StartDate { get; set; }
    double _height = double.NaN;
    public double Height {
      get { return _height.IfNaN(Max - Min) / PointSize; }
      set { _height = value; }
    }
    public double Distance { get; set; }
    public double DistanceCma { get; set; }
    public double Slope { get; private set; }
    public double TotalSeconds { get; private set; }
    double _workByTime = double.NaN;
    public double WorkByTime {
      get { return _workByTime.IfNaN(TotalSeconds * Angle.Abs()); }
      set { _workByTime = value; }
    }
    double _workByDistance = double.NaN;
    double WorkByDistance {
      get { return _workByDistance.IfNaN(Distance * Angle.Abs()); }
      set { _workByDistance = value; }
    }
    double _workByCount = double.NaN;
    double WorkByCount {
      get { return _workByCount.IfNaN(Count * Angle.Abs()); }
      set { _workByCount = value; }
    }
    public double _workByHeight = double.NaN;
    public double WorkByHeight {
      get { return _workByHeight.IfNaN((Max - Min) * Angle.Abs()); }
      set { _workByHeight = value; }
    }
    public double DistanceByRegression { get; set; }
    public DateTime EndDate { get; set; }
    public int Count { get; set; }
    public double Max { get; set; }
    public double Min { get; set; }
    public double Angle { get; set; }
    public double InterseptStart { get; set; }
    public double InterseptEnd { get; set; }
    public double StDev { get; set; }
    /// <summary>
    /// Number from 1 to 5 from Elliot sequence
    /// </summary>
    public int ElliotIndex { get; set; }
    public bool IsSuper { get; set; }
    #endregion
    #region Calculated
    static double DistanceByHeightAndAngle(double heigth, double angle) {
      var a = angle.Abs();
      return a >= 90 ? heigth * a / 90 : heigth / Math.Sin(a.Radians());
    }
    static double DistanceByHeightAndSlope(double heigth, double slope) {
      return heigth / Math.Sin(Math.Atan(slope));
    }
    static double DistanceByLengthAndSlope(double length, double slope) {
      return length / Math.Cos(Math.Atan(slope));
    }
    #endregion
    public static WaveRange Merge(IEnumerable<WaveRange> wrs) {
      var ps = wrs.Select(wr => new { wr.PointSize, wr.Period }).FirstOrDefault();
      return ps == null ? new WaveRange(0) : new WaveRange(wrs.Select(wr => wr.Range).Aggregate((p, n) => p.Concat(n).ToList()).ToList(), ps.PointSize, ps.Period);
    }
    #region ctor

    public WaveRange() : this(double.NaN) { }
    public WaveRange(double pointSize) {
      this.Range = new List<Rate>();
      this.PointSize = pointSize;
    }
    public WaveRange(List<Rate> range, double pointSize,BarsPeriodType period)
      : base() {
        this.Period = period;
      this.PointSize = pointSize;
      this.Range = range;
      Count = range.Count;
      if(Count == 0)
        return;
      StartDate = range[0].StartDate;
      EndDate = range.Last().StartDate;
      if (EndDate < StartDate)
        throw new InvalidOperationException("StartDate;{0} must be less then EndDate:{1}".Formater(StartDate, EndDate));
      Max = range.Max(r => r.PriceAvg);
      Min = range.Min(r => r.PriceAvg);
      var priceAvgs = range.ToArray(r => r.PriceAvg);
      Distance = priceAvgs.Distances().DefaultIfEmpty().Last() / pointSize;
      var lastCmas = range.ToArray(r => r.PriceCMALast);
      DistanceCma = lastCmas.Distances().DefaultIfEmpty().Last() / pointSize;
      TotalSeconds = EndDate.Subtract(StartDate).TotalSeconds;
      CalcTrendLine(range, pointSize, period);
      //this.Fatness = AlgLib.correlation.pearsoncorrelation(priceAvgs, lastCmas).Abs();
    }
    #endregion

    #region Methods
    double Smooth(double angle, double[] smoothies) {
      return Math.Pow(Math.Abs(angle), smoothies[0]) * smoothies[1] * Math.Sign(angle);
    }
    void CalcTrendLine(List<Rate> range, double pointSize, BarsPeriodType period) {
      if(range.Count == 0) return;
      var minutes = (range.Last().StartDate - range[0].StartDate).Duration().TotalMinutes;
      Func<TimeSpan, IEnumerable<double>> groupped = ts => range.GroupedDistinct(rate => rate.StartDate.AddMilliseconds(-rate.StartDate.Millisecond), g => g.Average(rate => rate.PriceAvg));
      var doubles = period == BarsPeriodType.t1 ? groupped(1.FromSeconds()).ToArray() : range.Select(r => r.PriceAvg).ToArray();
      if(doubles.Length < 2) doubles = groupped(1.FromSeconds()).ToArray();
      var coeffs = doubles.Linear();
      this.Slope = coeffs.LineSlope();
      this.Angle = Slope.Angle(period == BarsPeriodType.t1 ? 1.0 / 60 : (int)period, pointSize);
      this.StDev = doubles.StDevByRegressoin(coeffs) / pointSize;
      this.InterseptStart = coeffs.RegressionValue(0);
      this.InterseptEnd = coeffs.RegressionValue(doubles.Length - 1);
      this.DistanceByRegression = DistanceByHeightAndAngle(InterseptStart.Abs(InterseptEnd), Angle).Abs() / pointSize;
    }
    #endregion


    public static void CalcValues(IList<Point> data,
                                                        out double slope, out double intercept, out double rSquared) {
      double xSum = 0;
      double ySum = 0;
      double xySum = 0;
      double xSqSum = 0;
      double ySqSum = 0;

      foreach(var point in data) {
        var x = point.X;
        var y = point.Y;

        xSum += x;
        ySum += y;
        xySum += (x * y);
        xSqSum += (x * x);
        ySqSum += (y * y);
      }

      slope = ((data.Count * xySum) - (xSum * ySum)) /
                   ((data.Count * xSqSum) - (xSum * xSum));

      intercept = ((xSqSum * ySum) - (xSum * xySum)) /
                        ((data.Count * xSqSum) - (xSum * xSum));

      var a = ((data.Count * xySum) - (xSum * ySum));
      var b = (((data.Count * xSqSum) - (xSum * xSum)) *
                   ((data.Count * ySqSum) - (ySum * ySum)));
      rSquared = (a * a) / b;
    }
    /// <summary>
    /// Rotates one point around another
    /// </summary>
    /// <param name="pointToRotate">The point to rotate.</param>
    /// <param name="centerPoint">The center point of rotation.</param>
    /// <param name="angleInDegrees">The rotation angle in degrees.</param>
    /// <returns>Rotated point</returns>
    static Point RotatePoint(Point pointToRotate, Point centerPoint, double angleInRadians) {
      //double angleInRadians = angleInDegrees * (Math.PI / 180);
      double cosTheta = Math.Cos(angleInRadians);
      double sinTheta = Math.Sin(angleInRadians);
      return new Point {
        X =
              (cosTheta * (pointToRotate.X - centerPoint.X) -
              sinTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.X),
        Y =
              (sinTheta * (pointToRotate.X - centerPoint.X) +
              cosTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.Y)
      };
    }
    static public double GetRSquared(double[] array1, double[] array2) {
      double R = 0;

      try {
        // sum(xy)
        double sumXY = 0;
        for(int c = 0; c <= array1.Length - 1; c++) {
          sumXY = sumXY + array1[c] * array2[c];
        }

        // sum(x)
        double sumX = 0;
        for(int c = 0; c <= array1.Length - 1; c++) {
          sumX = sumX + array1[c];
        }

        // sum(y)
        double sumY = 0;
        for(int c = 0; c <= array2.Length - 1; c++) {
          sumY = sumY + array2[c];
        }

        // sum(x^2)
        double sumXX = 0;
        for(int c = 0; c <= array1.Length - 1; c++) {
          sumXX = sumXX + array1[c] * array1[c];
        }

        // sum(y^2)
        double sumYY = 0;
        for(int c = 0; c <= array2.Length - 1; c++) {
          sumYY = sumYY + array2[c] * array2[c];
        }

        // n
        int n = array1.Length;

        R = (n * sumXY - sumX * sumY) / (Math.Pow((n * sumXX - Math.Pow(sumX, 2)), 0.5) * Math.Pow((n * sumYY - Math.Pow(sumY, 2)), 0.5));
      } catch(Exception ex) {
        throw (ex);
      }

      return R * R;
    }

    public BarsPeriodType Period { get; set; }
    public bool IsFatnessOk {
      get;
      internal set;
    }
    public bool IsDistanceCmaOk {
      get;
      internal set;
    }
  }
  public static class WaveRangesMixin{
      public static Func<WaveRange, double> BestFitProp(this WaveRange wa) {
      Func<Func<WaveRange, double>, Func<WaveRange, double>> foo = f => f;
      var foos = new[] { foo(w => w.DistanceByRegression), foo(w => w.WorkByTime) };
      return foos.OrderBy(f => f(wa)).First();
    }

    public static int Index(this IList<WaveRange> wrs, Func<WaveRange, double> value) {
      return wrs.OrderByDescending(value).Take(1).Select(w => wrs.IndexOf(w)).DefaultIfEmpty(-1).First();
    }
    public static int Index(this IList<WaveRange> wrs, WaveRange wr, Func<WaveRange, double> value) {
      return wrs.OrderByDescending(value).ToList().IndexOf(wr);
    }
    public static int Index(this  WaveRange wr, IList<WaveRange> wrs, Func<WaveRange, double> value) {
      return wrs.OrderByDescending(value).ToList().IndexOf(wr);
    }
    public static IList<Tuple<WaveRange, int>> WaveRangesOrder(IList<WaveRange> wrs, Func<WaveRange, double> value) {
      return wrs.OrderByDescending(value).Select((wr, i) => Tuple.Create(wr, i)).ToArray();
    }


}
}
