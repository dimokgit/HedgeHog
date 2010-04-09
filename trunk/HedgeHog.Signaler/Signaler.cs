using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using HedgeHog.Bars;
using Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;

namespace HedgeHog {
  public static class Signaler {

    #region FindMaximas
    class PairOfVolts {
      public VoltForGrid Peak { get; set; }
      public VoltForGrid Valley { get; set; }
      public double Spread { get { return Valley.AverageBid == 0 ? 0 : Peak.AverageAsk - Valley.AverageBid; } }
      public PairOfVolts(VoltForGrid peak, VoltForGrid valley) {
        Peak = peak;
        Valley = valley;
      }
    }
    public class DataPoint{
      public double Value { get; set; }
      public DataPoint Next { get; set; }
      public DateTime Date{get;set;}
      public int Index { get; set; }
      public int Slope { get { return Math.Sign(Next.Value - Value); } }
      public DataPoint() {}
      public DataPoint(double value,DateTime date,int index) {
        Value = value;
        Date = date;
        Index = index;
      }
      public override string ToString() {
        return string.Format("{0:dd HH:mm:ss}:{1}/{2}", Date, Value, Index);
      }
    }
    public static DataPoint[] GetCurve<TBar>(this IEnumerable<TBar> ticks, int cmaPeriod) where TBar : Bars.BarBase {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      int i = 0;
      return (from tick in ticks
                   select
                   new DataPoint(){
                     Value = (cma3 = CMA(cma3, cmaPeriod, (cma2 = CMA(cma2, cmaPeriod, (cma1 = CMA(cma1, cmaPeriod, tick.PriceAvg)).Value)).Value)).Value,
                     Date = tick.StartDate,
                     Index = i++
                   }
                  ).ToArray();
    }
    public static DataPoint[] GetWaves(this DataPoint[] curve) {
      int skip = 1;
      var d1 = curve.Skip(skip).Take(curve.Length - 2).Select((dp, i) => { dp.Next = curve[i + skip + 1]; return dp; }).ToArray();
      var n = d1.Where(dp => dp.Next == null);
      var d2 = from dp1 in d1
               join dp2 in d1 on dp1.Index equals dp2.Index + 1
               where dp1.Slope != dp2.Slope
               select dp1;
      return d2.ToArray();
    }
    public static double[] GetWaves<TBar>(this IEnumerable<TBar> rates, int cmaPeriod) where TBar : Bars.BarBase {
      double? cma1 = null;
      var cmas3 = rates.GetCurve(cmaPeriod);
      cma1 = null;
      int i=0;
      var cmas = (from cma in cmas3
                  select new { ma1 = (cma1 = CMA(cma1, cmaPeriod, cma.Value)).Value, ma3 = cma, i = i++}
                  ).ToArray();
      i = 0;
      var wPeak = (from ma2 in cmas
              join ma1 in cmas on ma2.i equals ma1.i + 1
              where ma1.ma3.Value >= ma1.ma1 && ma2.ma3.Value <= ma2.ma1
              orderby ma1.i descending
              select new { ma = ma1.ma3, i = i++ }
              ).ToArray();
      i = 0;
      var wValley = (from ma2 in cmas
                   join ma1 in cmas on ma2.i equals ma1.i + 1
                     where ma1.ma3.Value < ma1.ma1 && ma2.ma3.Value > ma2.ma1
                   orderby ma1.i descending
                   select new { ma = ma1.ma3, i = i++ }
              ).ToArray();


      var w2 = from ma1 in wPeak
               join ma2 in wValley on ma1.i equals ma2.i
               select Math.Abs(ma1.ma.Value - ma2.ma.Value);
      return w2.ToArray();
    }

    public class WaveStats {
      public double Average;
      public double StDev;
      public double AverageUp;
      public double AverageDown;
      public DateTime Time = DateTime.MinValue;
      public WaveStats() { }
      public WaveStats(double Avg,double StDev,double AverageUp,double AverageDown) {
        this.Average = Avg;
        this.StDev = StDev;
        this.AverageUp = AverageUp;
        this.AverageDown = AverageDown;
        Time = DateTime.Now;
      }
    }

    public static WaveStats GetWaveStats(this double[] waves) {
      //waves = waves.OrderBy(w => w).Take(waves.Count() - 1).ToArray();
      var wa = waves.Average();
      var wst = waves.StdDev();
      return new WaveStats(wa, wst
        , waves.Where(w => w >= wa).DefaultIfEmpty(wa).Average()
        , waves.Where(w => w <= wa).DefaultIfEmpty(wa).Average());
    }

    public static List<Volt> FindMaximasPeakAndValley(
      IEnumerable<Rate> ticks, int voltageCMA, bool saveVoltsToFile, ref VoltForGrid PeakVolt, ref VoltForGrid ValleyVolt)
    {
      var time = DateTime.Now;
      var tickTimeFirst = ticks.Min(t => t.StartDate);
      List<Volt> voltagesByTick = GetVoltageByTick(ticks, voltageCMA);
      var peakVolts = (from v2 in voltagesByTick
                       join v1 in voltagesByTick on v2.Index equals v1.Index + voltageCMA
                       join v3 in voltagesByTick on v2.Index equals v3.Index - voltageCMA
                       where v1.PriceAvg <= v2.PriceAvg && v2.PriceAvg >= v3.PriceAvg
                       select v1).ToArray();
      var valleyVolts = (from v2 in voltagesByTick
                         join v1 in voltagesByTick on v2.Index equals v1.Index + voltageCMA
                         join v3 in voltagesByTick on v2.Index equals v3.Index - voltageCMA
                         where v1.PriceAvg >= v2.PriceAvg && v2.PriceAvg <= v3.PriceAvg
                         select v1).ToArray();
      var pairOfVolts = new List<PairOfVolts>();
      double a = 0, b = 0;
      Lib.LinearRegression(ticks.Select(t => t.PriceAvg).Reverse().ToArray(), out a, out b);
      DateTime dateMin = ticks.Min(t => t.StartDate), dateMax = ticks.Max(t => t.StartDate);
      var borderDate = dateMin.AddSeconds((dateMax - dateMin).TotalSeconds / 2);
      var peakLambda = new Func<Volt, bool>(
        (t) => a > 0 ? t.StartDate > borderDate : t.StartDate < borderDate);
      var valleyLambda = new Func<Volt, bool>(
        (t) => a < 0 ? t.StartDate > borderDate : t.StartDate < borderDate);
      {
        var voltPeak = voltagesByTick.Where(peakLambda).Where(rd => rd.PriceAvg > rd.PriceAvgAvg).OrderBy(rd => rd.VoltsCMA).LastOrDefault();
        if (voltPeak != null) {
          var voltValley = voltagesByTick.Where(valleyLambda).Where(rd => rd.PriceAvg < rd.PriceAvgAvg).OrderBy(rd => rd.VoltsCMA).LastOrDefault();
          if (voltValley != null) {
            pairOfVolts.Add(new PairOfVolts(
            new VoltForGrid(voltPeak.StartDate, voltPeak.VoltsCMA, voltPeak.AskMax, voltPeak.BidMax,voltPeak.PriceAvg1),
            new VoltForGrid(voltValley.StartDate, voltValley.VoltsCMA, voltValley.AskMin, voltValley.BidMin, voltValley.PriceAvg1)
            ));
            
          }
        }
      }
      #region peak Up/Down
      if (false) {
        var peakDown = voltagesByTick.Where(t => t.StartDate < borderDate).Where(rd => rd.PriceAvg > rd.PriceAvgAvg).OrderBy(rd => rd.VoltsCMA).Last();
        var valleyDown = voltagesByTick.Where(t => t.StartDate > borderDate).Where(rd => rd.PriceAvg < rd.PriceAvgAvg).OrderBy(rd => rd.VoltsCMA).Last();
        var peakUp = voltagesByTick.Where(t => t.StartDate > borderDate).Where(rd => rd.PriceAvg > rd.PriceAvgAvg).OrderBy(rd => rd.VoltsCMA).Last();
        var valleyUp = voltagesByTick.Where(t => t.StartDate < borderDate).Where(rd => rd.PriceAvg < rd.PriceAvgAvg).OrderBy(rd => rd.VoltsCMA).Last();
      }
      #endregion
      if (valleyVolts.Count() > 0 && peakVolts.Count() > 0) {
        {
          var voltsAvg = voltagesByTick.Average(v => v.VoltsCMA);
          var voltsMax = voltagesByTick.Max(v => v.VoltsCMA);
          var voltsMin = voltagesByTick.Min(v => v.VoltsCMA);
          var voltsTreashold = voltsAvg + (voltsMax - voltsMin) / 4;
          {
            var peakDown = peakVolts.Where(t => t.StartDate < borderDate && t.VoltsCMA > voltsTreashold).OrderBy(rd => rd.PriceAvg).LastOrDefault();
            var valleyDown = valleyVolts.Where(t => t.StartDate > borderDate && t.VoltsCMA > voltsTreashold).OrderBy(rd => rd.PriceAvg).FirstOrDefault();
            var peakUp = peakVolts.Where(t => t.StartDate > borderDate && t.VoltsCMA > voltsTreashold).OrderBy(rd => rd.PriceAvg).LastOrDefault();
            var valleyUp = valleyVolts.Where(t => t.StartDate < borderDate && t.VoltsCMA > voltsTreashold).OrderBy(rd => rd.PriceAvg).FirstOrDefault();
            if (peakDown != null && peakUp != null && valleyDown != null && valleyUp != null) {
              if (peakDown.PriceAvg - valleyDown.PriceAvg > peakUp.PriceAvg - valleyUp.PriceAvg) {
                pairOfVolts.Add(new PairOfVolts(
                new VoltForGrid(peakDown.StartDate, peakDown.VoltsCMA, peakDown.AskMax, peakDown.BidMax, peakDown.PriceAvg1),
                new VoltForGrid(valleyDown.StartDate, valleyDown.VoltsCMA, valleyDown.AskMin, valleyDown.BidMin, valleyDown.PriceAvg1))
                );
              } else {
                pairOfVolts.Add(new PairOfVolts(
                new VoltForGrid(peakUp.StartDate, peakUp.VoltsCMA, peakUp.AskMax, peakUp.BidMax, peakUp.PriceAvg1),
                new VoltForGrid(valleyUp.StartDate, valleyUp.VoltsCMA, valleyUp.AskMin, valleyUp.BidMin, valleyUp.PriceAvg1))
                );
              }
            }
          }
          {
            var peakDown = peakVolts.Where(t => t.StartDate < borderDate).OrderBy(rd => rd.VoltsCMA).LastOrDefault() ?? new Volt();
            var valleyDown = valleyVolts.Where(t => t.StartDate > borderDate).OrderBy(rd => rd.VoltsCMA).LastOrDefault() ?? new Volt();
            var peakUp = peakVolts.Where(t => t.StartDate > borderDate).OrderBy(rd => rd.VoltsCMA).LastOrDefault() ?? new Volt();
            var valleyUp = valleyVolts.Where(t => t.StartDate < borderDate).OrderBy(rd => rd.VoltsCMA).LastOrDefault() ?? new Volt();
            if ( peakDown.PriceAvg - valleyDown.PriceAvg > peakUp.PriceAvg - valleyUp.PriceAvg) {
              pairOfVolts.Add(new PairOfVolts(
            new VoltForGrid(peakDown.StartDate, peakDown.VoltsCMA, peakDown.AskMax, peakDown.BidMax, peakDown.PriceAvg1),
            new VoltForGrid(valleyDown.StartDate, valleyDown.VoltsCMA, valleyDown.AskMin, valleyDown.BidMin, valleyDown.PriceAvg1))
            );
            } else {
              pairOfVolts.Add(new PairOfVolts(
              new VoltForGrid(peakUp.StartDate, peakUp.VoltsCMA, peakUp.AskMax, peakUp.BidMax, peakUp.PriceAvg1),
              new VoltForGrid(valleyUp.StartDate, valleyUp.VoltsCMA, valleyUp.AskMin, valleyUp.BidMin, valleyUp.PriceAvg1))
              );
            }
          }
        }
      }
      if (pairOfVolts.Count > 1) {
        var cross = (from p in pairOfVolts.Select(pv => pv.Peak)
                     from v in pairOfVolts.Select(pv => pv.Valley)
                     select new { Peak = p, Valley = v, Spread = p.Average - v.Average }
                    ).OrderBy(pv => pv.Spread).Last();
        PeakVolt = cross.Peak;
        ValleyVolt = cross.Valley;
      } else {
        var pair = pairOfVolts.Where(p => p.Spread > 0).OrderBy(p => p.Spread).LastOrDefault();
        if (pair != null) {
          PeakVolt = pair.Peak;
          ValleyVolt = pair.Valley;
        }
      }
      return voltagesByTick;
    }
    #endregion

    #region Distance
    public static void GetDistances<TBar>(this IEnumerable<TBar> bars) where TBar : Bars.BarBase {

    }
    #endregion

    public static List<Volt> GetVoltageByTick(IEnumerable<Rate> ticks, int cmaPeriod) {
      var ticksByDate = ticks.OrderBarsDescending().Select((t, i) => new { t.StartDate,Ask = t.AskOpen,Bid = t.BidOpen, Row = i }).ToArray();
      DateTime d = DateTime.Now;
      var ticks_1 = (from tick1 in ticksByDate
                     join tick2 in ticksByDate on tick1.Row equals tick2.Row - 1
                     select new {
                       tick1.StartDate, tick2.Ask, tick2.Bid,
                       Diff = (Math.Abs(tick1.Ask - tick2.Ask) + Math.Abs(tick1.Bid - tick2.Bid)) / 2
                     }).ToArray();
      d = DateTime.Now;
      var ticks_2 = (from tick1 in ticks_1
                     join tick2 in ticks_1 on tick1.StartDate equals tick2.StartDate into tickGroup
                     select new {
                       tick1.StartDate,
                       Diff = tickGroup.Sum(tg => tg.Diff),
                       AskMax = tickGroup.Max(tg => tg.Ask),
                       BidMax = tickGroup.Max(tg => tg.Bid),
                       AskMin = tickGroup.Min(tg => tg.Ask),
                       BidMin = tickGroup.Min(tg => tg.Bid),
                       AskAgv = tickGroup.Average(tg => tg.Ask),
                       BidAvg = tickGroup.Average(tg => tg.Bid)
                     }).ToArray();
      d = DateTime.Now;
      double askAvg, bidAvg;
      var ticks_3 = (from t2 in ticks_2
                     orderby t2.StartDate descending
                     let tick3 = ticks_2.Where(t => t.StartDate > t2.StartDate.AddMinutes(-1)).ToArray().Where(t => t.StartDate <= t2.StartDate).ToArray()
                     select new {
                       t2.StartDate,
                       Diff = tick3.Sum(t => t.Diff),
                       AskMax = tick3.Max(t => t.AskMax),
                       BidMax = tick3.Max(t => t.BidMax),
                       AskMin = tick3.Min(t => t.AskMin),
                       BidMin = tick3.Min(t => t.BidMin),
                       AskAvg = askAvg = tick3.Average(t => t.AskAgv),
                       BidAvg = bidAvg = tick3.Average(t => t.BidAvg),
                       PriceAvg = (askAvg + bidAvg) / 2
                     }).ToArray();
      d = DateTime.Now;
      var ii = 0;
      //var avg = regression == RegressionMode.Average ? ticks_3.Average(t => (t.AskAvg + t.BidAvg) / 2) : 0;
      double a = 0, b = 0;
      double? cma1 = null, cma2 = null, cma3 = null;
      double? ccma1 = null;
      double? vcma1 = null, vcma2 = null, vcma3 = null;
      Lib.LinearRegression(ticks_3.Select(t => t.PriceAvg).Reverse().ToArray(), out a, out b);
      //var coeffs = Regression.Regress(ticks_3.Select((t, i) => (double)i).ToArray(), ticks_3.Select(t => t.PriceAvg).Reverse().ToArray());
      try {
        var volts = (from t in ticks_3
                     orderby t.StartDate
                     let l = ++ii
                     let spread = ((t.AskMax + t.BidMax) - (t.AskMin + t.BidMin)) / 2
                     //let avg = ticks_3.Skip(l).Take(200).DefaultIfEmpty()
                     where spread != 0
                     select new Volt(l, t.StartDate,
                       t.Diff / spread,
                       (vcma3 = CMA(vcma3, cmaPeriod, (vcma2 = CMA(vcma2, cmaPeriod, (vcma1 = CMA(vcma1, cmaPeriod, t.Diff / spread)).Value)).Value)).Value,
                       t.AskMax,t.AskMin,t.BidMax, t.BidMin, t.PriceAvg,
                       //avg == null ? t.PriceAvg : avg.Average(ta => ta == null ? t.PriceAvg : ta.PriceAvg),
                       (cma3 = CMA(cma3, cmaPeriod, (cma2 = CMA(cma2, cmaPeriod, (cma1 = CMA(cma1, cmaPeriod, (t.AskAvg + t.BidAvg) / 2)).Value)).Value)).Value,
                       (ccma1 = CMA(ccma1, cmaPeriod, cma3.Value)).Value,
                       a * (l-1) + b
                       //--coeffs[0] + coeffs[1] * (l - 1) + coeffs[2] * (l - 1) * (l - 1) + coeffs[3] * (l - 1) * (l - 1) * (l - 1)
                       //avg == null ? 0 : avg.Average(ta => ta == null ? 0 : (ta.AskAvg + ta.BidAvg) / 2)
                       )).ToArray();
        //Y = A + BX + CX2 + DX3
        var coeffs = Regression.Regress(volts.Select(t => t.Volts).ToArray(), 2);
        int i1 = 0;
        foreach (var volt in volts) {
          double y1 = 0; int j = 0;
          coeffs.ToList().ForEach(c => y1 += coeffs[j] * Math.Pow(i1, j++));
          volt.VoltsPoly = y1;// *poly2Wieght + y2 * (1 - poly2Wieght);
          i1++;
        }

        return volts.OrderByDescending(v => v.StartDate).ToList();
      } finally {
      }
    }


    public static double CMA(double? MA, double Periods, double NewValue) {
      if (!MA.HasValue) return NewValue;// Else CMA = MA + (NewValue - MA) / (Periods + 1)
      return MA.Value + (NewValue - MA.Value) / (Periods + 1);
    }
  }
}
