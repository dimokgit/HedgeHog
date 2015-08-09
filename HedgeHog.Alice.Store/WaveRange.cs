using HedgeHog.Bars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public class WaveRange {
    #region Properties
    public bool IsEmpty { get { return Count == 0; } }
    public bool IsTail { get; set; }
    public double UID { get; set; }
    public List<Rate> Range { get; set; }
    public double PointSize { get; set; }
    public DateTime StartDate { get; set; }
    double _height = double.NaN;
    public double Height {
      get { return _height.IfNaN(Max - Min) / PointSize; }
      set { _height = value; }
    }
    public double Distance { get; set; }
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
      StartDate = range[0].StartDate;
      EndDate = range.Last().StartDate;
      if (EndDate < StartDate)
        throw new InvalidOperationException("StartDate;{0} must be less then EndDate:{1}".Formater(StartDate, EndDate));
      Max = range.Max(r => r.PriceAvg);
      Min = range.Min(r => r.PriceAvg);
      Distance = range.Select(r => r.PriceCMALast).Distances().Last() / pointSize;
      TotalSeconds = EndDate.Subtract(StartDate).TotalSeconds;
      CalcTrendLine(range, pointSize, period);
      this.UID = Math.Sqrt(Distance / Height);
    }
    #endregion

    #region Methods
    double Smooth(double angle, double[] smoothies) {
      return Math.Pow(Math.Abs(angle), smoothies[0]) * smoothies[1] * Math.Sign(angle);
    }
    void CalcTrendLine(IList<Rate> range, double pointSize,BarsPeriodType period) {
      if (range.Count == 0) return;
      var minutes = (range.Last().StartDate - range[0].StartDate).Duration().TotalMinutes;
      Func<TimeSpan, IEnumerable<double>> groupped = ts => range.GroupAdjacentTicks(ts
        , rate => rate.StartDate
        , g => g.Average(rate => rate.PriceAvg));
      var doubles = period == BarsPeriodType.t1 ? groupped(1.FromSeconds()).ToList() : range.Select(r => r.PriceAvg).ToList();
      if (doubles.Count < 2) doubles = groupped(1.FromSeconds()).ToList();
      var coeffs = doubles.Linear();
      this.Slope = coeffs.LineSlope();
      this.Angle = Slope.Angle(period == BarsPeriodType.t1 ? 1.0 / 60 : (int)period, pointSize);
      this.StDev = doubles.StDevByRegressoin(coeffs) / pointSize;
      this.InterseptStart = coeffs.RegressionValue(0);
      this.InterseptEnd = coeffs.RegressionValue(doubles.Count - 1);
      this.DistanceByRegression = DistanceByHeightAndAngle(InterseptStart.Abs(InterseptEnd), Angle).Abs() / pointSize;
    }
    #endregion

    public BarsPeriodType Period { get; set; }
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
