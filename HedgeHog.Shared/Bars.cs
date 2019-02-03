using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
namespace HedgeHog.Bars {
  public enum FractalType {
    None = 0, Buy = -1, Sell = 1
  };
  public enum OverlapType {
    None = 0, Up = 1, Down = -1
  };
  [DataContract]
  public abstract class BarBaseDate {
    #region StartDate2
    private DateTimeOffset _StartDate2;
    public DateTimeOffset StartDate2 {
      get { return _StartDate2; }
      set {
        if(_StartDate2 != value) {
          _StartDate2 = value;
          StartDate = _StartDate2.LocalDateTime;
        }
      }
    }

    #endregion
    DateTime _StartDate;
    [DataMember]
    public DateTime StartDate {
      get { return _StartDate; }
      private set {
        if(_StartDate == value)
          return;
        _StartDate = StartDateContinuous = value;
      }
    }
    [DataMember]
    public DateTime StartDateContinuous { get; set; }
    public virtual object Clone() {
      return MemberwiseClone();
    }
    public override string ToString() {
      return StartDate.ToString("MM/dd/yyyy HH:mm:ss");
    }
  }
  [DataContract]
  public abstract class BarBase : BarBaseDate, IEquatable<BarBase>, IComparable<BarBase>, ICloneable {
    [DataMember]
    public bool IsHistory;
    [DataMember]
    public int Row { get; set; }

    #region Bid/Ask
    #region Ask
    double _AskHigh = double.NaN;
    [DataMember]
    public double AskHigh {
      get { return _AskHigh; }
      set {
        _AskHigh = value;
        AskAvg = (_AskHigh + _AskLow) / 2;
      }
    }
    [DataMember]
    double _AskLow = double.NaN;
    public double AskLow {
      get { return _AskLow; }
      set {
        _AskLow = value;
        AskAvg = (_AskHigh + _AskLow) / 2;
      }
    }
    [DataMember]
    private double _askAvg = double.NaN;
    public double AskAvg {
      get { return _askAvg; }
      set {
        _askAvg = value;
        PriceAvg = (AskAvg + BidAvg) / 2;
      }
    }
    #endregion

    #region Bid
    double _BidHigh = double.NaN;
    [DataMember]
    public double BidHigh {
      get { return _BidHigh; }
      set {
        _BidHigh = value;
        BidAvg = (_BidHigh + _BidLow) / 2;
      }
    }
    double _BidLow = double.NaN;
    [DataMember]
    public double BidLow {
      get { return _BidLow; }
      set {
        _BidLow = value;
        BidAvg = (_BidHigh + _BidLow) / 2;
      }
    }
    [DataMember]
    private double _bidAvg = double.NaN;
    public double BidAvg {
      get { return _bidAvg; }
      set {
        _bidAvg = value;
        PriceAvg = (AskAvg + BidAvg) / 2;
      }
    }
    #endregion
    [DataMember]
    public double AskClose { get; set; }
    [DataMember]
    public double AskOpen { get; set; }
    [DataMember]
    public double BidClose { get; set; }
    [DataMember]
    public double BidOpen { get; set; }

    [DataMember]
    public int Volume { get; set; }

    public double BidHighAskLowDiference { get { return BidHigh - AskLow; } }
    private double _BidHighAskLowDifferenceMA = double.NaN;
    public double BidHighAskLowDifferenceMA {
      get { return _BidHighAskLowDifferenceMA; }
      set { _BidHighAskLowDifferenceMA = value; }
    }
    #endregion

    #region Spread
    /// <summary>
    /// Spread between Ask and Bid
    /// </summary>
    public double Spread { get { return (AskHigh - AskLow + BidHigh - BidLow) / 2; } }
    public double SpreadMax { get { return Math.Max(AskHigh - AskLow, BidHigh - BidLow); } }
    public double SpreadMin { get { return Math.Min(AskHigh - AskLow, BidHigh - BidLow); } }
    /// <summary>
    /// Bar height
    /// </summary>
    public double PriceSpread { get { return (AskHigh - BidHigh + AskLow - BidLow) / 2; } }
    #endregion

    #region Price
    public virtual double PriceHigh { get { return (AskHigh + BidHigh) / 2; } }
    public virtual double PriceLow { get { return (AskLow + BidLow) / 2; } }
    public double PriceClose { get { return (AskClose + BidClose) / 2; } }
    public double PriceOpen { get { return (AskOpen + BidOpen) / 2; } }
    double _PriceAvg;
    public double PriceAvg {
      get { return _PriceAvg; }
      set {
        _PriceAvg = value;
        RunningLow = RunningHigh = value;
      }
    }
    public double PriceHLC { get { return (PriceHigh + PriceLow + PriceClose) / 3; } }
    //public double PriceHeight(BarBase bar) { return Math.Abs(PriceAvg - bar.PriceAvg); }
    #endregion

    #region PriceAvgs
    #region PriceChartHigh
    double _PriceChartAsk = double.NaN;
    public double PriceChartAsk {
      get { return double.IsNaN(_PriceChartAsk) ? AskHigh : _PriceChartAsk; }
      set {
        if(_PriceChartAsk != value)
          _PriceChartAsk = value;
      }
    }
    #endregion
    #region PriceChartBid
    private double _PriceChartBid = double.NaN;
    public double PriceChartBid {
      get { return double.IsNaN(_PriceChartBid) ? BidLow : _PriceChartBid; }
      set {
        if(_PriceChartBid != value) {
          _PriceChartBid = value;
        }
      }
    }
    #endregion
    public void SetPriceChart() {
      _PriceChartAsk = _PriceChartBid = double.NaN;
    }
    double _PriceAvg1 = double.NaN;
    [DataMember]
    public double PriceAvg1 {
      get { return _PriceAvg1; }
      set {
        _PriceAvg1 = value;
        if(double.IsNaN(_PriceAvg1)) {
          PriceChartAsk = PriceChartBid = double.NaN;
        } else if(double.IsNaN(PriceChartAsk)) {
          SetPriceChart();
        }
      }
    }
    [DataMember]
    public double PriceAvg21 { get; set; }
    [DataMember]
    public double PriceAvg2 { get; set; }
    [DataMember]
    public double PriceAvg02 { get; set; }

    public double PriceHeight2 { get { return PriceAvg1 != 0 && PriceAvg2 != 0 ? PriceAvg2 - PriceAvg : 0; } }

    [DataMember]
    public double PriceAvg31 { get; set; }
    [DataMember]
    public double PriceAvg3 { get; set; }
    [DataMember]
    public double PriceAvg03 { get; set; }

    public double PriceHeight3 { get { return PriceAvg1 != 0 && PriceAvg3 != 0 ? PriceAvg3 - PriceAvg : 0; } }

    [DataMember]
    public double PriceAvg4 { get; set; }
    #endregion

    #region Gunn Angles
    double[] _gannPrices = new double[0];
    [DataMember]
    public double[] GannPrices {
      get { return _gannPrices; }
      set { _gannPrices = value; }
    }
    public double GannPrice1x1 { get { return GannPrices.Length == 0 ? 0 : GannPrices[GannPrices.Length / 2]; } }
    [DataMember]
    public double TrendLine { get; set; }
    #endregion


    #region Price Indicators
    [DataMember]
    public double? PriceSpeed { get; set; }

    double _PriceHedge = double.NaN;
    [DataMember]
    public double PriceHedge {
      get { return _PriceHedge; }
      set { _PriceHedge = value; }
    }

    [DataMember]
    public double? PriceRsi { get; set; }
    [DataMember]
    public double? PriceRsi1 { get; set; }

    double _PriceRsiP = double.NaN;
    [DataMember]
    public double PriceRsiP {
      get { return _PriceRsiP; }
      set { _PriceRsiP = value; }
    }
    [DataMember]
    public double PriceRsiN { get; set; }
    [DataMember]
    public double? PriceRsiCR { get; set; }
    [DataMember]
    public double? PriceRlw { get; set; }
    [DataMember]
    public double? PriceTsi { get; set; }
    [DataMember]
    public double? PriceTsiCR { get; set; }
    double _PriceCMALast = double.NaN;
    public double PriceCMALast {
      get {
        return _PriceCMALast;
      }
      set {
        _PriceCMALast = value;
      }
    }

    public List<double> PriceCMAOther { get; set; }

    [DataMember]
    public double PriceStdDev { get; set; }

    [DataMember]
    public double Corridorness { get; set; }

    double _Kurtosis = double.NaN;
    public double Kurtosis { get { return _Kurtosis; } set { _Kurtosis = value; } }
    double _Skewness = double.NaN;
    public double Skewness { get { return _Skewness; } set { _Skewness = value; } }

    double _density = double.NaN;
    [DataMember]
    public double Density {
      get { return _density; }
      set { _density = value; }
    }
    #endregion

    public double? RunningTotal { get; set; }

    double _RunningHigh = double.NaN;
    public double RunningHigh {
      get { return _RunningHigh; }
      set { _RunningHigh = value; }
    }

    double _RunningLow = double.NaN;
    public double RunningLow {
      get { return _RunningLow; }
      set { _RunningLow = value; }
    }

    public double RunningHeight { get { return RunningHigh - RunningLow; } }

    #region Fractals
    public FractalType Fractal {
      get { return (int)FractalSell + FractalBuy; }
      set {
        if(value == FractalType.None)
          FractalBuy = FractalSell = FractalType.None;
        else if(value == FractalType.Buy)
          FractalBuy = value;
        else
          FractalSell = value;
      }
    }
    [DataMember]
    public FractalType FractalBuy { get; set; }
    [DataMember]
    public FractalType FractalSell { get; set; }
    public double? FractalPrice {
      get {
        return Fractal == FractalType.None ? PriceAvg : Fractal == FractalType.Buy ? PriceLow : PriceHigh;
      }
    }
    public bool HasFractal { get { return Fractal != FractalType.None; } }
    public bool HasFractalSell { get { return FractalSell == FractalType.Sell; } }
    public bool HasFractalBuy { get { return FractalBuy == FractalType.Buy; } }
    public double FractalWave(BarBase rate) { return Math.Abs((this.FractalPrice - rate.FractalPrice).Value); }
    public double PriceByFractal(FractalType fractalType) {
      return fractalType == FractalType.None ? PriceAvg : fractalType == FractalType.Buy ? PriceLow : PriceHigh;
    }
    #endregion

    #region Phycics
    double _Distance = double.NaN;
    public double Distance {
      get { return _Distance; }
      set { _Distance = value; }
    }
    [DataMember]
    public double? Mass { get; set; }

    [DataContract]
    public class PhClass : ICloneable {
      [DataMember]
      public double? Height { get; set; }
      [DataMember]
      public TimeSpan? Time { get; set; }
      [DataMember]
      public double? Mass { get; set; }
      [DataMember]
      public double? Trades { get; set; }
      public double? TradesPerMinute { get { return Time.HasValue ? Trades / Time.Value.TotalMinutes : null; } }
      public double? MassByTradesPerMinute { get { return Mass * TradesPerMinute; } }
      public double? Speed { get { return Height / Time.Value.TotalSeconds; } }
      public double? Density { get { return Mass / Height; } }
      double? _work = null;
      public double? Work {
        get { return _work ?? (Mass * Height); }
        set {
          _work = value;
        }
      }
      [DataMember]
      double? _power = null;
      public double? Power { get { return _power ?? (Mass * Speed); } set { _power = value; } }
      [DataMember]
      double? _k = null;
      public double? K { get { return _k ?? (Mass * Speed * Speed / 2); } set { _k = value; } }


      #region ICloneable Members
      public object Clone() {
        return MemberwiseClone() as PhClass;
      }
      #endregion
    }
    [DataMember]
    PhClass _Ph = null;
    public PhClass Ph {
      get {
        if(_Ph == null)
          _Ph = new PhClass();
        return _Ph;
      }
      set {
        _Ph = value;
      }
    }
    #endregion

    #region Trend
    [DataContract]
    public class TrendInfo {
      [DataMember]
      public double? PriceAngle { get; set; }
      [DataMember]
      public TimeSpan Period { get; set; }
      /// <summary>
      /// Ticks per minute
      /// </summary>
      [DataMember]
      public int Volume { get; set; }
      [DataMember]
      public double? VolumeAngle { get; set; }
      public TrendInfo() { }
      public TrendInfo(TimeSpan Period, double PriceAngle, int Volume, double VolumeAngle) {
        this.Period = Period;
        this.PriceAngle = PriceAngle;
        this.Volume = Volume;
        this.VolumeAngle = VolumeAngle;
      }
    }
    [DataMember]
    TrendInfo _trend;

    public TrendInfo Trend {
      get {
        if(_trend == null)
          Trend = new TrendInfo();
        return _trend;
      }
      set { _trend = value; }
    }
    #endregion

    #region Overlap
    [DataMember]
    public TimeSpan? Flatness;
    public OverlapType OverlapsWith(BarBase bar) {
      if(this.PriceLow.Between(bar.PriceLow, bar.PriceHigh))
        return OverlapType.Up;
      else if(this.PriceHigh.Between(bar.PriceLow, bar.PriceHigh))
        return OverlapType.Down;
      else if(bar.PriceLow.Between(this.PriceLow, this.PriceHigh))
        return OverlapType.Down;
      else if(bar.PriceHigh.Between(this.PriceLow, this.PriceHigh))
        return OverlapType.Up;
      return OverlapType.None;
    }
    public OverlapType FillOverlap(BarBase bar) {
      OverlapType ret = OverlapType.None;
      if(this.PriceLow.Between(bar.PriceLow, bar.PriceHigh))
        ret = OverlapType.Up;
      else if(this.PriceHigh.Between(bar.PriceLow, bar.PriceHigh))
        ret = OverlapType.Down;
      else if(bar.PriceLow.Between(this.PriceLow, this.PriceHigh))
        ret = OverlapType.Down;
      else if(bar.PriceHigh.Between(this.PriceLow, this.PriceHigh))
        ret = OverlapType.Up;
      //else if (bar.PriceLow.Between(this.PriceLow, this.PriceHigh)) ret = OverlapType.Down;
      //else if (this.PriceHigh.Between(bar.PriceLow, bar.PriceHigh)) ret = OverlapType.Down;
      //else if (bar.PriceHigh.Between(this.PriceLow, this.PriceHigh)) ret = OverlapType.Up;
      if(ret != OverlapType.None)
        Overlap = this.StartDate - bar.StartDate;
      return ret;
    }
    [DataMember]
    public TimeSpan Overlap { get; set; }

    public void FillOverlap<TBar>(IEnumerable<TBar> bars, TimeSpan period) where TBar : BarBase {
      TBar barPrev = null;
      foreach(var bar in bars) {
        if(barPrev != null && (barPrev.StartDate - bar.StartDate).Duration() > period)
          return;
        if(FillOverlap(bar) == OverlapType.None)
          return;
        barPrev = bar;
      }
    }
    #endregion

    [DataMember]
    public int Count { get; set; }

    public BarBase() { }
    public BarBase(bool isHistory) { IsHistory = isHistory; }

    protected void SetAsk(double ask) { AskOpen = AskLow = AskClose = AskHigh = ask; }
    protected void SetBid(double bid) { BidOpen = BidLow = BidClose = BidHigh = bid; }

    public BarBase(DateTimeOffset startDate, double ask, double bid, bool isHistory) {
      SetAsk(ask);
      SetBid(bid);
      StartDate2 = startDate.ToUniversalTime();
      IsHistory = isHistory;
    }
    public void AddTick(Price price) { AddTick(price.Time.ToUniversalTime(), price.Ask, price.Bid); }
    public void AddTick(DateTime startDate, double ask, double bid) {
      if(startDate.Kind != DateTimeKind.Utc)
        throw new ArgumentException("startDate must be of Utc kind");
      if(Count++ == 0) {
        SetAsk(ask);
        SetBid(bid);
        StartDate2 = startDate;
      } else {
        if(ask > AskHigh)
          AskHigh = ask;
        if(ask < AskLow)
          AskLow = ask;
        if(bid > BidHigh)
          BidHigh = bid;
        if(bid < BidLow)
          BidLow = bid;
      }
      AskClose = ask;
      BidClose = bid;
      IsHistory = false;
    }


    #region Operators
    public static bool operator <=(BarBase b1, BarBase b2) {
      return b1 is null || (object)b2 == null ? false : b1.StartDate < b2.StartDate || (b1.StartDate == b2.StartDate && b1.Row <= b2.Row);
    }
    public static bool operator >(BarBase b1, BarBase b2) {
      return (object)b1 == null || (object)b2 == null ? false : b1.StartDate > b2.StartDate || (b1.StartDate == b2.StartDate && b1.Row > b2.Row);
    }
    public static bool operator >=(BarBase b1, BarBase b2) {
      return (object)b1 == null || (object)b2 == null ? false : b1.StartDate > b2.StartDate || (b1.StartDate == b2.StartDate && b1.Row >= b2.Row);
    }
    public static bool operator <(BarBase b1, BarBase b2) {
      return (object)b1 == null || (object)b2 == null ? false : b1.StartDate < b2.StartDate || (b1.StartDate == b2.StartDate && b1.Row < b2.Row);
    }


    public static bool operator ==(BarBase b1, BarBase b2) {
      return (object)b1 == null && (object)b2 == null ? true : (object)b1 == null ? false : b1.Equals(b2);
    }
    public static bool operator !=(BarBase b1, BarBase b2) { return (object)b1 == null ? (object)b2 == null ? false : !b2.Equals(b1) : !b1.Equals(b2); }
    #endregion

    public static TBar BiggerFractal<TBar>(TBar b1, TBar b2) where TBar : BarBase {
      return BiggerFractal(b1, b2, b => b.FractalPrice);
    }
    public static TBar BiggerFractal<TBar>(TBar b1, TBar b2, Func<TBar, double?> Price) where TBar : BarBase {
      if(b1.Fractal == b2.Fractal) {
        if(b1.Fractal == FractalType.Buy)
          return Price(b1) < Price(b2) ? b1 : b2;
        if(b1.Fractal == FractalType.Sell)
          return Price(b1) > Price(b2) ? b1 : b2;
        return null;
      } else
        return null;
    }

    #region Overrides
    public override string ToString() {
      return string.Format("{0:dd HH:mm:ss}-{1:n5}/{2:n5}", StartDate, PriceHigh, PriceLow);
    }
    public override bool Equals(object obj) {
      return obj is BarBase ? Equals(obj as BarBase) : false;
    }
    public virtual bool Equals(BarBase other) {
      if((object)other == null || StartDate != other.StartDate || Row != other.Row)
        return false;
      return true;
    }
    //    public override int GetHashCode_() { return StartDate.GetHashCode(); }
    public override int GetHashCode() {
      unchecked // Overflow is fine, just wrap
      {
        return 31 * StartDate.GetHashCode() + (Row > 0 ? Row.GetHashCode() : 0);
      }
    }
    #endregion

    #region ICloneable Members

    public override object Clone() {
      var bb = base.Clone() as BarBase;
      bb.Ph = this.Ph.Clone() as PhClass;
      return bb;
    }

    #endregion

    #region IComparable<BarBase> Members

    public int CompareTo(BarBase other) {
      return other == null ? 1 : this == null ? -1 : this.StartDate.CompareTo(other.StartDate);
    }

    #endregion
  }
  //public enum BarsPeriodTypeFXCM { t1 = 0, m1 = 1, m5 = 5, m15 = 15, m30 = 30, H1 = 60, D1 = 24 * H1, W1 = 7 * D1 }
  public enum BarsPeriodType {
    t1 = 0, m1 = 1, m2 = 2, m3 = 3, m5 = 5, m10 = 10, m15 = 15, m30 = 30, H1 = 60, H2 = H1 * 2, H3 = H1 * 3, H4 = H1 * 4, H6 = H1 * 6, H8 = H1 * 8, H12 = H6 * 2, D1 = 24 * H1, W1 = 7 * D1, s1 = -60, none = int.MaxValue
  }
  [DataContract]
  public class Rate : BarBase {
    public static readonly Rate Empty = new Rate();
    public class TrendLevels {
      #region Voltages
      public void ResetVoltages() {
        PPMB = new double[0];
      }
      public double[] PPMB = new double[0];
      private double _priceAvg2 = double.NaN;
      #endregion
      public static readonly TrendLevels Empty;
      public static readonly Rate EmptyRate;
      public static readonly Func<TrendLevels, bool> NotEmpty = tl => !tl.IsEmpty;
      static TrendLevels() {
        Empty = new TrendLevels(new Rate[0], new[] { double.NaN, double.NaN }, double.NaN, DateTime.MaxValue, DateTime.MaxValue);
        EmptyRate = new Rate { Trends = Empty };
      }
      public string Color { get; set; }
      public double Distance { get; set; }
      public bool IsEmpty { get { return Count == 0; } }
      public bool IsSelected { get; set; }

      public double Slope { get; set; }
      public double StDev { get; set; }
      public int Count { get; set; }
      public double Angle { get; set; }

      public double PriceAvg1 { get; set; } = double.NaN;

      public double PriceAvg2 { get => _priceAvg2; set => _priceAvg2 = value; }
      public double PriceAvg02 { get; set; } = double.NaN;
      public double PriceAvg21 { get; set; } = double.NaN;
      public double PriceAvg22 { get; set; } = double.NaN;

      public double PriceAvg03 { get; set; } = double.NaN;
      public double PriceAvg3 { get; set; } = double.NaN;
      public double PriceAvg31 { get; set; } = double.NaN;
      public double PriceAvg32 { get; set; } = double.NaN;

      public double PriceAvgHeight { get { return PriceAvg2 - PriceAvg3; } }

      public double Height { get; set; } = double.NaN;

      public double[] Coeffs { get; private set; }

      public IEnumerable<double> HStdRatio { get { return RateHeight.Select(rh => rh / (StDev * 4)); } }
      public IEnumerable<double> PriceHeight { get { return PriceMax.Zip(PriceMin, (p1, p2) => p1.Abs(p2)); } }
      public IEnumerable<double> RateHeight { get { return RateMax.Zip(RateMin, (p1, p2) => p1.PriceAvg.Abs(p2.PriceAvg)); } }
      public IEnumerable<Rate> RateMax { get { return Sorted.Value.Skip(1); } }
      public IEnumerable<double> PriceMax { get { return RateMax.Select(r => r.AskHigh); } }
      public IEnumerable<Rate> RateMin { get { return Sorted.Value.Take(1); } }
      public IEnumerable<double> PriceMin { get { return RateMin.Select(r => r.BidLow); } }
      public Lazy<Rate[]> Sorted { get; set; } = Lazy.Create(() => new Rate[0]);

      public IEnumerable<Tuple<DateTime, double, double>> EdgeHigh {
        get;
        set;
      } = new Tuple<DateTime, double, double>[0];
      public IEnumerable<Tuple<DateTime, double, double>> EdgeLow {
        get;
        set;
      } = new Tuple<DateTime, double, double>[0];

      public IEnumerable<double> EdgeDiff { get { return EdgeHigh.SelectMany(h => EdgeLow.Select(l => h.Item2.Abs(l.Item2))); } }

      public DateTime StartDate { get; private set; }
      public DateTime EndDate { get; private set; }
      public TimeSpan TimeSpan { get { return EndDate - StartDate; } }

      public IList<Rate> Rates { get; private set; }
      public void ClearRates() => Rates = new Rate[0];

      public TrendLevels(IList<Rate> rates, double[] coeffs, double stDev, DateTime startDate, DateTime endDate) {
        this.Rates = rates;
        this.Count = rates.Count;
        this.Slope = coeffs.LineSlope();
        this.Coeffs = coeffs;
        this.StDev = stDev;
        this.StartDate = startDate;
        this.EndDate = endDate;
      }
    }
    public Rate() { }
    public Rate(bool isHistory) : base(isHistory) { }
    public static TBar Create<TBar>(DateTime Time, double Ask, double Bid, bool isHistory) where TBar : Rate, new() {
      var bar = new TBar();
      bar.StartDate2 = Time.ToUniversalTime();
      bar.SetAsk(Ask);
      bar.SetBid(Bid);
      bar.IsHistory = isHistory;
      return bar;
    }
    public Rate(DateTime Time, double Ask, double Bid, bool isHistory) : base(Time, Ask, Bid, isHistory) { }
    public Rate(Price price, bool isHistory) : this(price.Time, price.Ask, price.Bid, isHistory) { }
    public Rate(double AskHigh, double AskLow, double AskOpen, double AskClose,
                    double BidHigh, double BidLow, double BidOpen, double BidClose,
                    DateTime StartDate
      ) {
      if(StartDate.Kind != DateTimeKind.Utc)
        throw new ArgumentException("StartDate must be of Utc kind");
      this.AskHigh = AskHigh;
      this.AskLow = AskLow;
      this.AskOpen = AskOpen;
      this.AskClose = AskClose;

      this.BidHigh = BidHigh;
      this.BidLow = BidLow;
      this.BidOpen = BidOpen;
      this.BidClose = BidClose;

      this.StartDate2 = StartDate;
    }
    public TrendLevels Trends { get; set; }
    double _distanceHistory = double.NaN;
    public double DistanceHistory {
      get { return _distanceHistory; }
      set { _distanceHistory = value; }
    }

    double _Distance1 = double.NaN;
    public double Distance1 {
      get { return _Distance1; }
      set { _Distance1 = value; }
    }
    public double CrossesDensity { get; set; } = double.NaN;

    #region TpsAverage
    private double _TpsAverage = double.NaN;
    public double TpsAverage {
      get { return _TpsAverage; }
      set {
        if(_TpsAverage != value) {
          _TpsAverage = value;
        }
      }
    }

    #endregion
    #region VoltageLocal
    private double _VoltageLocal = double.NaN;
    public double VoltageLocal {
      get { return _VoltageLocal; }
      set { _VoltageLocal = value; }
    }
    #endregion
    double _VoltageLocal2 = double.NaN;
    public double VoltageLocal2 {
      get { return _VoltageLocal2; }
      set { _VoltageLocal2 = value; }
    }
    double _VoltageLocal3 = double.NaN;
    public double VoltageLocal3 {
      get { return _VoltageLocal3; }
      set { _VoltageLocal3 = value; }
    }
    #region VoltageLocal0
    private double[] _VoltageLocal0;
    public double[] VoltageLocal0 {
      get { return _VoltageLocal0 ?? (_VoltageLocal0 = new double[0]); }
      set { _VoltageLocal0 = value; }
    }

    #endregion
  }
  public class Tick : Rate {
    public Tick() { }
    public Tick(bool isHistory) : base(isHistory) { }
    public Tick(DateTime Time, double Ask, double Bid, int Row, bool isHistory)
      : base(Time, Ask, Bid, isHistory) {
      this.Row = Row;
    }
    public Tick(Price price, int row, bool isHistory)
      : base(price, isHistory) {
      Row = row;
    }

    public override double PriceHigh { get { return AskHigh; } }
    public override double PriceLow { get { return BidLow; } }


    #region IEquatable<Tick> Members

    public override bool Equals(BarBase other) {
      //return (object)other != null && StartDate == other.StartDate && AskOpen == other.AskOpen && BidOpen == other.BidOpen;
      return (object)other != null && StartDate2 == other.StartDate2 && Row == other.Row;
    }
    public override int GetHashCode() { return StartDate2.GetHashCode() ^ Row.GetHashCode(); }
    //public override int GetHashCode() { return StartDate.GetHashCode() ^ AskOpen.GetHashCode() ^ BidOpen.GetHashCode(); }

    #endregion
  }

  public class PriceBar : BarBaseDate {
    // Properties
    [DisplayName("")]
    public double AskHigh { get; set; }

    [DisplayName("")]
    public double AskLow { get; set; }

    [DisplayName("")]
    public double BidHigh { get; set; }

    [DisplayName("")]
    public double BidLow { get; set; }

    [DisplayFormat(DataFormatString = "{0:n1}"), DisplayName("")]
    public double Power {
      get {
        return (this.Spread * this.Speed);
      }
    }

    [DisplayName("Row"), DisplayFormat(DataFormatString = "{0:n1}")]
    public double Row { get; set; }

    [DisplayFormat(DataFormatString = "{0:n1}"), DisplayName("")]
    public double Speed { get; set; }

    [DisplayFormat(DataFormatString = "{0:n0}"), DisplayName("")]
    public double Spread { get; set; }

    public override string ToString() {
      return base.ToString() + " : " + Power;
    }
  }
}