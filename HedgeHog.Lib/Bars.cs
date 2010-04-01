using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
[assembly:CLSCompliant(true)]
namespace HedgeHog.Bars {
  public enum FractalType {None = 0, Buy = -1, Sell = 1 };
  public enum OverlapType { None = 0, Up = 1, Down = -1 };
  public abstract class BarBase : IEquatable<BarBase>,ICloneable {
    public DateTime StartDate { get; set; }
    public readonly bool IsHistory;

    #region Bid/Ask
    public double AskHigh { get; set; }
    public double AskLow { get; set; }
    public double BidHigh { get; set; }
    public double BidLow { get; set; }
    public double AskClose { get; set; }
    public double AskOpen { get; set; }
    public double BidClose { get; set; }
    public double BidOpen { get; set; }
    #endregion

    #region Spread
    public double Spread { get { return (AskHigh - AskLow + BidHigh - BidLow) / 2; } }
    public double SpreadMax { get { return Math.Max(AskHigh - AskLow, BidHigh - BidLow); } }
    public double SpreadMin { get { return Math.Min(AskHigh - AskLow, BidHigh - BidLow); } }
    #endregion

    #region Price
    public double PriceHigh { get { return (AskHigh + BidHigh) / 2; } }
    public double PriceLow { get { return (AskLow + BidLow) / 2; } }
    public double PriceClose { get { return (AskClose + BidClose) / 2; } }
    public double PriceOpen { get { return (AskOpen + BidOpen) / 2; } }
    public double PriceAvg { get { return (PriceHigh + PriceLow + PriceOpen + PriceClose) / 4; } }

    public double PriceHeight(BarBase bar) { return Math.Abs(PriceAvg - bar.PriceAvg); }

    public double PriceAvg1 { get; set; }
    public double PriceAvg2 { get; set; }
    public double PriceAvg3 { get; set; }
    public double PriceAvg4 { get; set; }
    public double PriceWave { get; set; }
    public double? PriceRsi { get; set; }
    public double PriceRsiP { get; set; }
    public double PriceRsiN { get; set; }
    public double? PriceRsiCR { get; set; }
    public double? PriceRlw { get; set; }
    public double? PriceTsi { get; set; }
    public double? PriceTsiCR { get; set; }
    public double[] PriceCMA { get; set; }
    public double PriceStdDev { get; set; }
    #endregion

    public double? RunningTotal { get; set; }

    #region Fractals
    public FractalType Fractal {
      get { return (int)FractalSell + FractalBuy; }
      set {
        if (value == FractalType.None) FractalBuy = FractalSell = FractalType.None;
        else if (value == FractalType.Buy) FractalBuy = value;
        else FractalSell = value;
      }
    }
    public FractalType FractalBuy { get; set; }
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
    public double? Mass { get; set; }

    public class PhClass:ICloneable {
      public double? Height { get; set; }
      public TimeSpan? Time { get; set; }
      public double? Mass { get; set; }
      public double? Trades { get; set; }
      public double? TradesPerMinute { get { return Time.HasValue ? Trades / Time.Value.TotalMinutes : null; } }
      public double? MassPerTradesPerMinute { get { return Mass / TradesPerMinute; } }
      public double? Speed { get { return Height / Time.Value.TotalSeconds; } }
      public double? Density { get { return Mass / Height; } }
      double? _work = null;
      public double? Work { get { return _work ?? (Mass * Height); } set { 
        _work = value; 
      } }
      double? _power = null;
      public double? Power { get { return _power ?? (Mass * Speed); } set { _power = value; } }
      double? _k = null;
      public double? K { get { return _k ?? (Mass * Speed * Speed / 2); } set { _k = value; } }


      #region ICloneable Members
      public object Clone() {
        return MemberwiseClone() as PhClass;
      }
      #endregion
    }
    PhClass _Ph = null;
    public PhClass Ph {
      get {
        if (_Ph == null) _Ph = new PhClass();
        return _Ph;
      }
      set {
        _Ph = value;
      }
    }
    #endregion

    #region Overlap
    public OverlapType HasOverlap(BarBase bar) {
      if (this.PriceLow.Between(bar.PriceLow, bar.PriceHigh)) return OverlapType.Up;
      else if (this.PriceHigh.Between(bar.PriceLow, bar.PriceHigh)) return OverlapType.Down;
      else if (bar.PriceLow.Between(this.PriceLow, this.PriceHigh)) return OverlapType.Down;
      else if (bar.PriceHigh.Between(this.PriceLow, this.PriceHigh)) return OverlapType.Up;
      return OverlapType.None;
    }
    public OverlapType GetOverlap(BarBase bar) {
      OverlapType ret = OverlapType.None;
      if (this.PriceLow.Between(bar.PriceLow, bar.PriceHigh)) ret = OverlapType.Up;
      else if (this.PriceHigh.Between(bar.PriceLow, bar.PriceHigh)) ret = OverlapType.Down;
      else if (bar.PriceLow.Between(this.PriceLow, this.PriceHigh)) ret = OverlapType.Down;
      else if (bar.PriceHigh.Between(this.PriceLow, this.PriceHigh)) ret = OverlapType.Up;
      //else if (bar.PriceLow.Between(this.PriceLow, this.PriceHigh)) ret = OverlapType.Down;
      //else if (this.PriceHigh.Between(bar.PriceLow, bar.PriceHigh)) ret = OverlapType.Down;
      //else if (bar.PriceHigh.Between(this.PriceLow, this.PriceHigh)) ret = OverlapType.Up;
      if (ret != OverlapType.None) Overlap = this.StartDate - bar.StartDate;
      return ret;
    }
    public TimeSpan Overlap { get; set; }

    public void FillOverlap<TBar>(IEnumerable<TBar> bars) where TBar : BarBase {
      foreach (var bar in bars) {
        if (GetOverlap(bar) == OverlapType.None) return;
      }
    } 
    #endregion

    public int Count { get; set; }

    public BarBase() { }
    public BarBase(bool isHistory) { IsHistory = isHistory; }

    void SetAsk(double ask) { AskOpen = AskLow = AskClose = AskHigh = ask; }
    void SetBid(double bid) { BidOpen = BidLow = BidClose = BidHigh = bid; }

    public BarBase(DateTime startDate, double ask, double bid, bool isHistory) {
      SetAsk(ask);
      SetBid(bid);
      StartDate = startDate;
      IsHistory = isHistory;
    }
    public void AddTick(DateTime startDate, double ask, double bid) {
      if (Count++ == 0) {
        SetAsk(ask);
        SetBid(bid);
        StartDate = startDate.Round();
      } else {
        if (ask > AskHigh) AskHigh = ask;
        if (ask < AskLow) AskLow = ask;
        if (bid > BidHigh) BidHigh = bid;
        if (bid < BidLow) BidLow = bid;
      }
      AskClose = ask;
      BidClose = bid;
    }


    #region Operators
    public static bool operator ==(BarBase b1, BarBase b2) { return (object)b1 == null && (object)b2 == null ? true : (object)b1 == null ? false : b1.Equals(b2); }
    public static bool operator !=(BarBase b1, BarBase b2) { return (object)b1 == null ? (object)b2 == null ? false : !b2.Equals(b1) : !b1.Equals(b2); }
    #endregion

    public static TBar BiggerFractal<TBar>(TBar b1, TBar b2) where TBar : BarBase{
      if (b1.Fractal == b2.Fractal) {
        if (b1.Fractal == FractalType.Buy) return b1.FractalPrice < b2.FractalPrice ? b1 : b2;
        if (b1.Fractal == FractalType.Sell) return b1.FractalPrice > b2.FractalPrice ? b1 : b2;
        return null;
      } else return null;
    }

    #region Overrides
    public override string ToString() {
      return string.Format("{0:dd HH:mm:ss}:{1}/{2}", StartDate, AskHigh, BidLow);
    }
    public override bool Equals(object obj) {
      return obj is BarBase ? Equals(obj as BarBase) : false;
    }
    public virtual bool Equals(BarBase other) {
      if ((object)other == null || StartDate != other.StartDate) return false;
      return true;
    }
    public override int GetHashCode() { return StartDate.GetHashCode(); }
    #endregion

    #region ICloneable Members

    public virtual object Clone() {
      var bb =  MemberwiseClone() as BarBase;
      bb.Ph = this.Ph.Clone() as PhClass;
      return bb;
    }

    #endregion
  }
  [Serializable]
  public class Price {
    public double Bid { get; set; }
    public double Ask { get; set; }
    public double Average { get { return (Ask + Bid) / 2; } }
    public double Spread { get { return Ask - Bid; } }
    public DateTime Time { get; set; }
    public string Pair { get; set; }
    public int BidChangeDirection { get; set; }
    public int AskChangeDirection { get; set; }
  }

  public enum BarsPeriodType { t1 = 0, m1 = 1, m5 = 5, m15 = 15, m30 = 30, H1 = 60, D1 = 24, W1 = 7 }
  public class Rate : BarBase {
    public Rate() { }
    public Rate(bool isHistory) : base(isHistory) { }
    public Rate(DateTime Time, double Ask, double Bid, bool isHistory) : base(Time, Ask, Bid, isHistory) { }
    public Rate(Price price, bool isHistory) : this(price.Time, price.Ask, price.Bid, isHistory) { }
  }
  public class Tick : Rate {
    public int Row { get; set; }
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
    #region IEquatable<Tick> Members

    public override bool Equals(BarBase other) {
      try {
        return (object)other != null && StartDate == other.StartDate && ((Tick)other).Row == Row;
      } catch (Exception) {
        throw;
      }
    }
    public override int GetHashCode() {
      return StartDate.GetHashCode() ^ Row.GetHashCode();
    }

    #endregion
  }


  public class DataPoint {
    public double Value { get; set; }
    public DataPoint Next { get; set; }
    public DateTime Date { get; set; }
    public int Index { get; set; }
    public int Slope { get { return Math.Sign(Next.Value - Value); } }
  }
  public static class Extensions {
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, DateTime bar1, DateTime bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1, bar2));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TBar bar1, TBar bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1.StartDate, bar2.StartDate));
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, TBar barFrom, TBar barTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(barFrom.StartDate, barTo.StartDate)).TradesPerMinute();
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      var count = bars.Count();
      var bo = bars.OrderBars().ToArray();
      return count / (bo.Last().StartDate - bo.First().StartDate).TotalMinutes;
    }

    public static Rate[] GetMinuteTicks<TBar>(this IEnumerable<TBar> fxTicks, int period, bool round) where TBar : BarBase {
      if (!round) return GetMinuteTicks(fxTicks, period);
      var timeRounded = fxTicks.Min(t => t.StartDate).Round().AddMinutes(1);
      return GetMinuteTicks(fxTicks.Where(t => t.StartDate >= timeRounded), period);
    }
    public static Rate[] GetMinuteTicks<TBar>(this IEnumerable<TBar> fxTicks, int period) where TBar : BarBase {
      var startDate = fxTicks.Min(t => t.StartDate);
      return (from t in fxTicks
              where period > 0
              group t by (((int)Math.Floor((t.StartDate - startDate).TotalMinutes) / period)) * period into tg
              orderby tg.Key
              select new Rate() {
                AskHigh = tg.Max(t => t.AskHigh),
                AskLow = tg.Min(t => t.AskLow),
                AskOpen = tg.First().AskOpen,
                AskClose = tg.Last().AskClose,
                BidHigh = tg.Max(t => t.BidHigh),
                BidLow = tg.Min(t => t.BidLow),
                BidOpen = tg.First().BidOpen,
                BidClose = tg.Last().BidClose,
                Mass = tg.Sum(t => t.Mass),
                StartDate = startDate.AddMinutes(tg.Key)
              }
                ).ToArray();
    }
    public static IEnumerable<Rate> GroupTicksToRates(this IEnumerable<Rate> ticks) {
      return from tick in ticks
             group tick by tick.StartDate into gt
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
          barsDelete.AddRange(new[] { bars[i-1], bars[i-2] });
      barsDelete.Distinct().ToList().ForEach(b => bars.Remove(b));
    }
    public static void FillPower<TBar>(this TBar[] barsSource, TBar[] bars) where TBar : BarBase {
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
      var barsMinMax = new[]{ barsOrdered.First(),barsOrdered.Last()}.OrderBars().ToArray();
      barSource.Ph.Height = barsMinMax[1].PriceAvg - barsMinMax[0].PriceAvg;// bars.Last().PriceHeight(bars.First());
      var barsByDate = bars.OrderBars().ToArray();
      barSource.Ph.Time = (barsByDate.Last().StartDate - barsByDate.First().StartDate);
      barSource.Ph.Trades = bars.Count();
      if (bars.Count() == 0) {
        barsByDate.SaveToFile(b => b.PriceHigh, b => b.PriceLow, "C:\\bars.csv");
      }
      if (barSource.Ph.Time == TimeSpan.Zero) barSource.Ph.Time = period;
      if (period == TimeSpan.Zero) {
        barSource.Ph.Work = bars.Sum(b => !b.Ph.Work.HasValue?0: b.Ph.Work * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
        barSource.Ph.Power = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.Power  * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
        barSource.Ph.K = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.K * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
      } else
        barSource.Ph.Work = barSource.Ph.Power = null;

      if( barSource != null) return;
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
      foreach (var bar in bars.Where(b=>b.Mass.HasValue))
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
    public static List<TBar> FindFractalTicks<TBar>(this IEnumerable<TBar> ticks, double waveHeight, TimeSpan period, double padRight, int count)where TBar:BarBase {
      var fractals = ticks.GetMinuteTicks(1).OrderBarsDescending().FindFractals(waveHeight, period, padRight, count);
      return fractals.Select(f => {
        var tt = ticks.Where(t => t.StartDate.Between(f.StartDate.AddSeconds(-60), f.StartDate.AddSeconds(60))).OrderBy(t => t.PriceByFractal(f.Fractal));
        var fractal = (f.Fractal == FractalType.Buy ? tt.First() : tt.Last()).Clone() as TBar;
        fractal.Fractal = f.Fractal;
        return fractal;
      }).ToList();
    }
    public static List<TBars> FindFractals<TBars>(this IEnumerable<TBars> rates, double waveHeight, TimeSpan period, double padRight, int count) where TBars : BarBase {
      var halfPeriod = TimeSpan.FromSeconds(period.TotalSeconds / 2.0);
      var rightPeriod = TimeSpan.FromSeconds(period.TotalSeconds * padRight);
      DateTime nextDate = DateTime.MaxValue;
      var fractals = new List<TBars>();
      var dateFirst = rates.Min(r => r.StartDate) + rightPeriod;
      var dateLast = rates.Max(r => r.StartDate) - rightPeriod;
      var waveFractal = 0D;
      foreach (var rate in rates.Where(r => r.StartDate.Between(dateFirst, dateLast))) {
        UpdateFractal(rates, rate, period, waveHeight);
        if (rate.HasFractal) {
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
              if (rate.FractalWave(fractals[fractals.Count - 1]) >= waveFractal && (fractals.Last().StartDate - rate.StartDate).Duration().TotalSeconds >= period.TotalSeconds/2)
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
    static void UpdateFractal<TBars>(IEnumerable<TBars> rates, TBars rate, TimeSpan period, double waveHeight) where TBars : BarBase {
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
        rate.PriceHigh >= ratesInRange.Max(r => r.PriceHigh)
        &&
        (//(rate.PriceHigh - ratesLeft.Min(r => r.PriceLow)) >= waveHeight        || 
        (rate.PriceHigh - ratesRight.Min(r => r.PriceLow)) >= waveHeight
        )
        ? HedgeHog.Bars.FractalType.Sell : HedgeHog.Bars.FractalType.None;
      rate.FractalBuy =
        rate.PriceLow <= ratesInRange.Min(r => r.PriceLow)
        &&
        (//(ratesLeft.Max(r => r.PriceHigh) - rate.PriceLow) >= waveHeight ||
        (ratesRight.Max(r => r.PriceHigh) - rate.PriceLow) >= waveHeight
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


    public static void FillOverlaps<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      foreach (var bar in bars)
        bar.FillOverlap(bars.Where(r => r.StartDate < bar.StartDate).Take(10));
    }
    public static void SetCMA<TBars>(this IEnumerable<TBars> bars, Func<TBars, double> cmaSource, int cmaPeriod) where TBars : BarBase {
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
    public static void SetCMA<TBars>(this IEnumerable<TBars> ticks, int cmaPeriod) where TBars : BarBase {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
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
    public static IEnumerable<T> OrderBars<T>(this IEnumerable<T> rates) where T : BarBase {
      return typeof(T) == typeof(Tick) ?
        rates.Cast<Tick>().OrderBy(r => r.StartDate).ThenBy(r => r.Row).Cast<T>() : rates.OrderBy(r => r.StartDate);
    }
    public static IEnumerable<T> OrderBarsDescending<T>(this IEnumerable<T> rates) where T : BarBase {
      return typeof(T) == typeof(Tick) ?
        rates.OfType<Tick>().OrderByDescending(r => r.StartDate).ThenByDescending(r => r.Row).OfType<T>() : rates.OrderByDescending(r => r.StartDate);
    }
  }
}