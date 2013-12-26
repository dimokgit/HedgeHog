using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Windows;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Concurrent;

namespace HedgeHog.Bars {
  public class RsiStatistics {
    public double Buy { get { return BuyAverage - BuyStd; } }
    public double Sell { get { return SellAverage + SellStd; } }
    public double Buy1 { get { return BuyMin + BuyStd * 2; } }
    public double Sell1 { get { return SellMax - SellStd * 2; } }
    public double BuyAverage { get; set; }
    public double SellAverage { get; set; }
    public double BuyStd { get; set; }
    public double SellStd { get; set; }
    public double BuyMin { get; set; }
    public double SellMax { get; set; }

    public RsiStatistics(double? BuyAverage, double? BuyStd, double? BuyMin, double? SellAverage, double? SellStd, double? SellMax) {
      this.BuyAverage = BuyAverage.GetValueOrDefault();
      this.BuyStd = BuyStd.GetValueOrDefault();
      this.BuyMin = BuyMin.GetValueOrDefault();
      this.SellAverage = SellAverage.GetValueOrDefault();
      this.SellStd = SellStd.GetValueOrDefault();
      this.SellMax = SellMax.GetValueOrDefault();
    }
    public override string ToString() {
      return string.Format("{0:n1}={1:n1}-{2:n1},{3:n1}={4:n1}+{5:n1}", Buy, BuyAverage, BuyStd, Sell, SellAverage, SellStd);
    }
  }

  public static class Extensions {

    public static IList<TBar> FindWave<TBar>(this IList<IList<TBar>> waves, IList<TBar> wave) where TBar:BarBase {
      if (wave == null || wave.Count == 0) return null;
      var start = waves.SkipWhile(w => w[0] > wave[0]).ToArray();
      return start.TakeWhile(w => w.LastBC() <= wave.LastBC()).DefaultIfEmpty(start.FirstOrDefault() ?? waves[0]).OrderByDescending(w => w.MaxStDev()).First();
    }
    public static IList<TBar> FindWaveByDate<TBar>(this IList<IList<TBar>> waves, DateTime date) where TBar : BarBaseDate {
      return waves.FirstOrDefault(w => date.Between(w.LastBC().StartDate, w[0].StartDate));
    }

    public static double Volatility<T>(this IList<T> rates, Func<T, double> value1, Func<T, double> value2, bool useSpearmanVolatility) {
      var corr1 = rates.Select(value1).ToArray();
      var corr2 = rates.Select(value2).ToArray();
      return !useSpearmanVolatility ? AlgLib.correlation.pearsoncorrelation(ref corr1, ref corr2, corr1.Length)
        : AlgLib.correlation.spearmanrankcorrelation(corr1, corr2, corr1.Length); ;
    }

    /// <summary>
    /// Reverse bars if they are not reversed
    /// </summary>
    /// <typeparam name="TBar"></typeparam>
    /// <param name="bars"></param>
    /// <returns></returns>
    public static IList<TBar> ReverseIfNot<TBar>(this IList<TBar> bars) where TBar : BarBaseDate {
      return bars[bars.Count()-1].StartDate > bars[0].StartDate? bars.Reverse().ToArray():bars;
    }
    public static IList<TBar> UnReverseIfIs<TBar>(this IList<TBar> bars) where TBar : BarBaseDate {
      return bars[bars.Count() - 1].StartDate < bars[0].StartDate ? bars.Reverse().ToArray() : bars;
    }
    public static TBar[] LineCrosses<TBar>(this ICollection<TBar> bars) where TBar : BarBase {
      var crosses = bars.Where(b => b.PriceAvg1.Between(b.AskHigh, b.BidLow)).ToArray();
      var distances = new List<double>();
      crosses.Aggregate((bp, bn) => { distances.Add((bp.StartDate - bn.StartDate).Duration().TotalMinutes); return bn; });
      var distance = distances.AverageByIterations(2).Average();
      var crossesOut = new List<TBar>(crosses.Take(1));
      crosses.Skip(1).ToList().ForEach(b => {
        if ((crossesOut.Last().StartDate - b.StartDate).Duration().TotalMinutes > distance)
          crossesOut.Add(b);
      });
      return crossesOut.ToArray();
    }

    public static double PriceMin<TBar>(this IEnumerable<TBar> list, int count = 0) where TBar : BarBase {
      return list.Any() ? (count == 0 ? list : list.Take(count)).Min(b => b.PriceAvg) : double.NaN;
    }
    public static double PriceMax<TBar>(this IEnumerable<TBar> list, int count = 0) where TBar : BarBase {
      return list.Any() ? (count == 0 ? list : list.Take(count)).Max(b => b.PriceAvg) : double.NaN;
    }
    public static double PriceAvg<TBar>(this IEnumerable<TBar> list, int count = 0) where TBar : BarBase {
      return list.Any() ? (count == 0 ? list : list.Take(count)).Average(b => b.PriceAvg) : double.NaN;
    }

    public static TBar Next<TBar>(this List<TBar> rates, TBar rate) where TBar : BarBase {
      var i = rates.IndexOf(rate) + 1;
      return i.Between(1, rates.Count - 1) ? rates[i] : null;
      //LinkedList<TBar> ll = new LinkedList<TBar>(rates);
      //var node = ll.Find(rate);
      //return node.Next == null ? null : node.Next.Value;
    }
    public static TBar Previous<TBar>(this List<TBar> rates, TBar rate) where TBar : BarBase {
      var i = rates.IndexOf(rate) - 1;
      return i.Between(0, rates.Count - 1) ? rates[i] : null;
    }
    static TBar Previous_<TBar>(this List<TBar> rates, TBar rate) where TBar : BarBase {
      if (rates[0] == rate) return null;
      var index = rates.IndexOf(rate);
      return rates[index - 1];
    }

    public static void SavePairCsv<TBar>(this ICollection<TBar> bars,string pair) where TBar : Bars.BarBase {
      var path = AppDomain.CurrentDomain.BaseDirectory + "\\CSV";
      Directory.CreateDirectory(path);
      File.WriteAllText(path + "\\" + pair.Replace("/", "") + ".csv", bars.Csv());
    }
    public static void SavePairCsv<TBar>(this ICollection<TBar> bars, string pair, string format, params Func<TBar, object>[] foos) where TBar : Bars.BarBase {
      var path = AppDomain.CurrentDomain.BaseDirectory + "\\CSV";
      Directory.CreateDirectory(path);
      File.WriteAllText(path + "\\" + pair.Replace("/", "") + ".csv", bars.Csv(format,foos));
    }
    public static string Csv<TBar>(this ICollection<TBar> bars) where TBar : BarBase {
      return string.Join(Environment.NewLine, bars.Select(b => string.Format("{0},{1},{2}", b.StartDate, b.PriceHigh, b.PriceLow)));
    }
    public static string Csv<TBar>(this ICollection<TBar> bars,string format,params Func<TBar,object>[] foos ) where TBar : BarBase {
      Func<TBar,object[]> parms = bar=> foos.Select(foo=>foo(bar)).ToArray();
      return string.Join(Environment.NewLine, bars.Select(b => string.Format(format, parms(b))));
    }
    public static bool IsReversed(this IEnumerable<BarBase> bars) {
      return bars.Last().StartDate < bars.First().StartDate;
    }

    public static void SetStartDateForChart<TBar>(this IEnumerable<TBar> bars) where TBar : BarBaseDate {
      bars.SetStartDateForChart(bars.GetPeriod());
    }
    public static void SetStartDateForChart<TBar>(this IEnumerable<TBar> bars,TimeSpan period) where TBar : BarBaseDate {
      if (period > TimeSpan.Zero) {
        bars = bars.OrderBarsDescending();
        var rateLast = bars.First();
        rateLast.StartDateContinuous = rateLast.StartDate;
        bars.OrderBarsDescending().Aggregate((bp, bn) => {
          bn.StartDateContinuous = bp.StartDateContinuous - period;
          return bn;
        });
      }
    }

    public static void SetBidHighToAskLowRatioMA<TBar>(this IList<TBar> bars, int period) where TBar : BarBase {
      bars.SetMA(period, r => r.BidHighAskLowDiference, (r, d) => r.BidHighAskLowDifferenceMA = d);
    }
    public static void SetMA<TBar>(this IList<TBar> bars, int period,Func<TBar,double> getValue,Action<TBar,double> setValue) where TBar : BarBase {
      for (int i = 0; i < bars.Count; i++) {
        if (i >= period - 1) {
          double total = 0;
          for (int x = i; x > (i - period); x--)
            total += getValue(bars[x]);
          double average = total / period;
          setValue(bars[i], average);
        }
      }
    }

    static SortedList<T, double> MovingAverage<T>(this SortedList<T, double> series, int period) {
      var result = new SortedList<T, double>();
      for (int i = 0; i < series.Count(); i++) {
        if (i >= period - 1) {
          double total = 0;
          for (int x = i; x > (i - period); x--)
            total += series.Values[x];
          double average = total / period;
          result.Add(series.Keys[i], average);
        }
      } return result;
    }

    public static TimeSpan GetPeriod<TBar>(this IEnumerable<TBar> bars) where TBar : BarBaseDate {
      if (bars.Count() < 2) return TimeSpan.Zero;
      var periods = new List<double>();
      bars.Aggregate((bp, bn) => { periods.Add((bp.StartDate - bn.StartDate).Duration().TotalMinutes); return bn; });
      var periodGroups = from p in periods
                         group p by p into pg
                         select new { Period = pg.Key, Count = pg.Count() };
      return TimeSpan.FromMinutes(periodGroups.OrderBy(pg => pg.Count).Last().Period);
                         
      //var period = TimeSpan.FromMinutes(periods.ToArray().AverageByIterations((d, a) => d <= a, 3).Average().ToInt());
      //return period;
    }

    public static IEnumerable<double> GetPriceForStats<TBar>(this ICollection<TBar> rates, Func<TBar, double> lineGet, Func<TBar, double> priceHigh, Func<TBar, double> priceLow)where TBar:BarBase {
      return rates.Select(r => r.PriceAvg > lineGet(r) ? priceHigh(r) : priceLow(r));
    }
    public static IEnumerable<double> GetPriceForStats<T>(this ICollection<T> rates,Func<T,double>price, Func<int, double> lineGet, Func<T, double> priceHigh, Func<T, double> priceLow) {
      return rates.Select((r, i) => price(r) > lineGet(i) ? priceHigh(r) : priceLow(r));
    }

    static void SetRegressionPrice<T>(this ParallelQuery<T> ticks, double[] coeffs, Action<T, double> a) {
      ticks.Select((tick, i) => {
        a(tick, coeffs.RegressionValue(i));
        return i;
      }).ToArray();
    }
    static void SetRegressionPrice<T>(this IEnumerable<T> ticks, double[] coeffs, Action<T, double> a) {
      int i1 = 0;
      foreach (var tick in ticks) {
        double y1 = coeffs.RegressionValue(i1++);
        a(tick, y1);// *poly2Wieght + y2 * (1 - poly2Wieght);
      }
    }
    public static double[] SetRegressionPrice<T>(this IEnumerable<T> ticks, int polyOrder, Func<T, double> readFrom, Action<int, double> writeTo) {
      var coeffs = ticks.Select(readFrom).ToArray().Regress( polyOrder);
      coeffs.SetRegressionPrice(0, ticks.Count(), writeTo);
      return coeffs;
    }
    public static double[] SetRegressionPriceP<T>(this ParallelQuery<T> ticks, int polyOrder, Func<T, double> readFrom, Action<T, double> writeTo) {
      var coeffs = ticks.Select(readFrom).ToArray().Regress(polyOrder);
      ticks.SetRegressionPrice(coeffs, writeTo);
      return coeffs;
    }

    public static void SetCorridorPrices<T>(this IList<T> rates, double[] coeffs
      , double heightUp0, double heightDown0
      , double heightUp, double heightDown
      , double heightUp1, double heightDown1
      , Func<T, double> getPriceLine, Action<T, double> setPriceLine
      , Action<T, double> setPriceHigh0, Action<T, double> setPriceLow0
      , Action<T, double> setPriceHigh, Action<T, double> setPriceLow
      , Action<T, double> setPriceHigh1, Action<T, double> setPriceLow1
      ) {
      rates.SetRegressionPrice(coeffs, setPriceLine);
      rates.AsParallel().ForAll(r => {
        var pl = getPriceLine(r);
        setPriceHigh0(r, pl + heightUp0);
        setPriceLow0(r, pl - heightDown0);
        setPriceHigh(r, pl + heightUp);
        setPriceLow(r, pl - heightDown);
        setPriceHigh1(r, pl + heightUp1);
        setPriceLow1(r, pl - heightDown1);
      });
    }

    public static IList<Tuple<TBar, double>> GetStDevPrice<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      var list = new Tuple<TBar, double>[rates.Count-1];
      Enumerable.Range(1, rates.Count() - 1).AsParallel()
        .ForAll(i => list[i-1] = new Tuple<TBar, double>(rates[i], rates.Take(i + 1).Select(r1 => getPrice(r1)).ToArray().StDevP()));
      return list;
    }
    public class ValueHolder<T> {
      public T Value { get; set; }
      public ValueHolder(T v) {
        Value = v;
      }
    }

    public static Rate GetWaveStopRate(this IList<Rate> wave) {
      bool up;
      return wave.GetWaveStopRate(out up);
    }
    public static Rate GetWaveStopRate(this IList<Rate> wave, out bool up) {
      up = wave.LastBC().PriceAvg < wave[0].PriceAvg;
      var wave1 = wave.OrderBy(r => r.PriceAvg).ToArray();
      return up ? wave1.LastBC() : wave1[0];
    }

    public static Rate[] GetWaveStartStopRates(this IList<Rate> wave) {
      bool up = wave.LastBC().PriceAvg < wave[0].PriceAvg;
      var wave1 = wave.OrderBy(r => r.PriceAvg > r.PriceCMALast ? r.PriceHigh : r.PriceLow).ToArray();
      return new Rate[] { wave1[0], wave1.LastBC() }.OrderBars().ToArray();
    }

    public static LinkedList<T> ToLinkedList<T>(this IEnumerable<T> list) { return new LinkedList<T>(list); }
    public static IList<IList<T>> Partition<T>(this IList<T> list, Func<T, bool> condition) {
      var alist = list.ToArray();
      var o = new List<IList<T>>();
      int start = 0, end = 0;
      var ts = new List<Tuple<int, int>>();
      while (end < alist.Length) {
        if (condition(alist[end])) {
          end++;
        } else {
          ts.Add(new Tuple<int, int>(start, end));
          start = ++end;
        }
      }
      --end;
      if (start <= end)
        ts.Add(new Tuple<int, int>(start, end));
      foreach (var t in ts) {
        var a = new T[t.Item2 - t.Item1 + 1];
        Array.Copy(alist, t.Item1, a, 0, a.Length);
        o.Add(a);
      }
      return o;
    }
    public static IList<TBar> SetStDevPricesFast<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice, double heightMin) where TBar : BarBase {
      var mstd = new HedgeHog.Lib.MovingStDevP();
      rates[0].PriceStdDev = 0;
      rates[1].PriceStdDev = mstd.FirstR(rates.Take(2).Select(getPrice).ToArray(), heightMin);
      for (var i = 2; i < rates.Count; i++) {
        rates[i].PriceStdDev = mstd.NextR(getPrice(rates[i]), heightMin);
        if (rates[i].PriceStdDev < rates[i - 1].PriceStdDev) {
          if (i == rates.Count - 1) break;
          rates[i++].PriceStdDev = 0;
          rates[i].PriceStdDev = mstd.FirstR(new[] { getPrice(rates[i - 1]), getPrice(rates[i]) }, heightMin);
        }
      }
      return rates;
    }
    public static IList<TBar> SetStDevPrices<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      var list = new List<TBar>(rates.Take(2));
      list.SetStDevPrice(getPrice);
      var stDevLast = list.LastBC().PriceStdDev;
      int counter = 0;
      Func<int, int, List<Tuple<TBar,ValueHolder<double>>>> getWave = (start, count) => {
        var rates1 = rates.Skip(start).Take(count).Select(r => new Tuple<TBar,ValueHolder<double>>( r,  new ValueHolder<double>(0.0) )).ToArray();
        rates1.SetStDevPrice_4(t => getPrice(t.Item1), (t, d) => t.Item2.Value = d);
        var node = rates1.ToLinkedList().First;
        var rates2 = new List<Tuple<TBar,ValueHolder<double>>> { node.Value };
        for (; node.Next != null && node.Value.Item2.Value < node.Next.Value.Item2.Value; node = node.Next)
          rates2.Add(node.Next.Value);
        return rates2;
      };
      //var step = rates.Count / rates.Partition(r => r.PriceStdDev != 0).Select(l => (double)l.Count).ToList().AverageByIterations(1, true).Average().ToInt();
      var step = rates.Count / rates.Partition(r => r.PriceStdDev != 0).Count;
      for (int i = 0; i < rates.Count; ) {
        var rates3 = getWave(i, step);
        for (var i1 = 1; i + i1 * step < rates.Count && rates3.Count == step * i1; )
          rates3 = getWave(i, step * ++i1);
        i += rates3.Count;
        var stDevChanged = false;
        rates3.ForEach(t => {
          if (t.Item1.PriceStdDev != t.Item2.Value)
            stDevChanged = true;
          t.Item1.PriceStdDev = t.Item2.Value;
        });
        if (!stDevChanged) break;
        //if (++counter > 2 && step > 1)
        //  break;
      }
      if (step < 0)
        foreach (var r in rates.Skip(2)) {
          list.Add(r);
          list.SetStDevPrice(getPrice);
          var stDev = list.LastBC().PriceStdDev;
          if (stDev >= stDevLast) {
            stDevLast = stDev;
          } else {
            list.RemoveRange(0, list.Count - 1);
            stDevLast = 0;
            list[0].PriceStdDev = 0;
            if (++counter > 2 && rates.LastBC().PriceStdDev > 0)
              break;
          }
        }
      return rates;
    }

    public static IList<TBar> SetStDevPrice<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice)where TBar:BarBase {
      return rates.Count < 2000 ? rates.SetStDevPrice_4(getPrice) : rates.SetStDevPrice_(getPrice);
      /*
      rates[0].PriceStdDev = 0;
      var list = new List<double> { getPrice(rates[0]) };
      for (var i = 1; i < rates.Count; i++ ) {
        list.Add(getPrice(rates[i]));
        var stDev = rates[i].PriceStdDev = list.StDevP();
        rates[i].Corridorness = list.Count / stDev;
      }
      //Enumerable.Range(1, rates.Count() - 1).AsParallel()
      //  .ForAll(i => rates[i].PriceStdDev = rates.Take(i + 1).Select(r1 => getPrice(r1)).ToList().StDevP());
      return rates;
       * */
    }

    public static IList<TBar> SetStDevPrice_4<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      return rates.SetStDevPrice_4(getPrice, (r, d) => r.PriceStdDev = d);
    }
    public static IList<TBar> SetStDevPrice_4<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice,Action<TBar,double> setPrice)  {
      setPrice(rates[0], 0);
      var list = rates.Select(getPrice).ToArray();
      var dp = new StaticRangePartitioner(Enumerable.Range(0, list.Length+1).ToArray(), 0.5);
      dp.AsParallel().ForAll(range => {
        for (int i = range.Item1.Max(2); i < range.Item2 && i <= list.Length; i++) {
          var l = new double[i];
          Array.Copy(list, l, i);
          try {
            var stDev = l.StDevP();
            setPrice(rates[i - 1], stDev);
          } catch {
            Debugger.Break();
          }
        }
      });
      return rates;
    }
    public static IList<TBar> SetStDevPrice_3<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      rates[0].PriceStdDev = 0;
      var list = rates.Select(getPrice).ToArray();
      var dp = new StaticRangePartitioer(Enumerable.Range(2, list.Length - 1).ToArray(), 0.25);
      dp.AsParallel().ForAll(i => {
        var l = new double[i];
        Array.Copy(list, l, i);
        var stDev = rates[i - 1].PriceStdDev = l.StDevP();
        rates[i - 1].Corridorness = i / stDev;
      });
      return rates;
    }
    public static IList<TBar> SetStDevPrice_2<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      rates[0].PriceStdDev = 0;
      var list = rates.Select(getPrice).ToArray();
      var dp = Partitioner.Create(2, list.Length+1, Environment.ProcessorCount);
      Parallel.ForEach(dp, range => {
        for (int i = range.Item1; i < range.Item2; i++) {
          var l = new double[i];
          Array.Copy(list, l, i);
          var stDev = rates[i - 1].PriceStdDev = l.StDevP();
          rates[i - 1].Corridorness = i / stDev;
        }
      });
      return rates;
    }
    public static IList<TBar> SetStDevPrice_1<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      rates[0].PriceStdDev = 0;
      var list = rates.Select(getPrice).ToArray();
      var dp = Partitioner.Create(Enumerable.Range(2, list.Length - 1).ToList(),true);
      Parallel.ForEach(dp, i => {
        var l = new double[i];
        Array.Copy(list, l, i);
        var stDev = rates[i - 1].PriceStdDev = l.StDevP();
        rates[i - 1].Corridorness = i / stDev;
      });
      return rates;
    }
    public static IList<TBar> SetStDevPrice_<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      rates[0].PriceStdDev = 0;
      var list = rates.Select(getPrice).ToArray();
      var dp = new OrderableListPartitioner<int>(Enumerable.Range(2, list.Length - 1).ToArray());
      Parallel.ForEach(dp, i => {
        var l = new double[i];
        Array.Copy(list,l,i);
        var stDev = rates[i - 1].PriceStdDev = l.StDevP();
        rates[i - 1].Corridorness = i / stDev;
      });
      return rates;
    }

    public static Rate CalculateMagnetLevel(this ICollection<Rate> rateForCom, bool up) {
      var rs = rateForCom.FindRatesByPrice(rateForCom.MagnetPrice());
      return up ? rs.OrderBy(r => r.PriceHigh).Last() : rs.OrderBy(r => r.PriceLow).First();
    }


    public static Rate CenterOfMass(this ICollection<Rate> rates, bool up) {
      var coms = rates.CentersOfMass();
      return up ? coms.OrderBy(r => r.PriceHigh).Last() : coms.OrderBy(r => r.PriceLow).First();
    }
    public static Rate CenterOfMass(this ICollection<Rate> rates) {
      return rates.CentersOfMass().OrderBy(r => r.Spread).First();
    }
    public static Rate CenterOfMass(this Rate[][] ovelaps) {
      return ovelaps.CentersOfMass().OrderBy(r => r.Spread).FirstOrDefault();
    }
    public static Rate[] CentersOfMass(this ICollection<Rate> rates) {
      return rates.Overlaps().OrderBy(rm => rm.Length).LastOrDefault() ?? new Rate[] { };
    }
    public static Rate[] CentersOfMass(this Rate[][] overlaps) {
      return overlaps.OrderBy(rm => rm.Length).Last();
    }

    public static Rate[][] Overlaps(this ICollection<Rate> rates,int iterationsForSpread = 0) {
      if (iterationsForSpread > 0) {
        var spreadAverage = rates.Select(r => r.Spread).ToArray()
          .AverageByIterations(iterationsForSpread, true)
          .AverageByIterations(iterationsForSpread, false)
          .Average();
        rates = rates.Where(r => r.Spread <= spreadAverage).ToArray();
      }
      return rates.AsParallel().Select(rate => rates.Where(r => r.OverlapsWith(rate) != OverlapType.None).ToArray()).ToArray();
    }

    public static IEnumerable<TBar> FindRatesByPrice<TBar>(this ICollection<TBar> Rates, double price) where TBar : BarBase {
      return Rates.Where(r => price.Between(r.PriceLow, r.PriceHigh));
    }

    #region Level
    public static double Level<TBar>(this ICollection<TBar> rates, bool up, int iterations)where TBar:BarBase {
      var ratesForLevel = rates.DistanceAverage(up, iterations);
      return up ? ratesForLevel.Average(r => r.PriceHigh) : ratesForLevel.Average(r => r.PriceLow);
    }
    static TBar[] DistanceAverage<TBar>(this ICollection<TBar> rates, bool up, int iterations) where TBar:BarBase{
      TBar[] ratesToReturn = rates.ToArray();
      var clusters = new List<TBar[]>(new TBar[][] { rates.ToArray() });
      do {
        var cs = clusters.Flatten().DistanceAverage(up).ToList();
        if (cs.Count == 0) break;
        clusters = cs;
        var ll = clusters.Select(c => (double)c.Length).Distinct().ToArray();
        var avg = ll.Average();
        var stDev = ll.StDev();
        clusters = clusters.Where(c => c.Length.Between(avg - stDev, avg + stDev)).ToList();
      } while (--iterations > 0 && clusters.Count > 2);
      return clusters.OrderBy(c=>c.Length).Last();
    }

    static T[] Flatten<T>(this ICollection<T[]> clusters) {
      if (clusters.Count == 0) return new T[0];
      if (clusters.Count == 1) return clusters.First();
      var list = new List<T>(clusters.First());
      clusters.Skip(1).ToList().ForEach(c => list.AddRange(c));
      return list.ToArray();
    }
    public static double Distance<TBar>(this IList<TBar> rates) where TBar : BarBase {
      return rates.Distance(r => r.PriceAvg);
    }
    public static double Distance<TBar>(this IList<TBar> rates,Func<TBar,double> getPrice ) where TBar : BarBase {
      double distance = (rates.LastBC().Distance - rates[0].Distance).Abs();
      if (distance == 0)
        rates.Aggregate((p, n) => { distance += (getPrice(p) - getPrice(n)).Abs(); return n; });
      if (distance.IsNaN()) throw new InvalidDataException("Distance calculation resulted in NaN.");
      return distance;
    }

    public static double CalcDistance<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice) {
      double distance = 0;
      if (rates.Count > 0)
        rates.Aggregate((p, n) => { distance += (getPrice(p) - getPrice(n)).Abs(); return n; });
      return distance;
    }

    public static double Distance2<TBar>(this IList<TBar> rates, Func<TBar, double> getPrice,IList<double> distances = null) where TBar : BarBase {
      var prices = rates.Select(getPrice).ToArray();
      return rates.Zip(rates.Skip(1), (p, n) => {
        var dist = getPrice(p).Abs(getPrice(n));
        if (distances != null) distances.Add(distances.LastOrDefault() + dist);
        return dist;
      }).Sum();
    }

    public static double WeightedAverage<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      double wa = 0, s = 0;
      bars.Aggregate((p, n) => {
        var d = p.PriceSpread;
        wa += p.PriceAvg * d;
        s += d;
        return n;
      });
      return wa / s;
    }
    public static IEnumerable<TBar> TouchDowns<TBar>(this IList<TBar> rates, double high, double low,Func<TBar,double> getPrice = null)where TBar:BarBase {
      if (getPrice == null) getPrice = (r) => r.PriceAvg;
      int tochType = 0;//1-high,-1 low
      foreach (var rate in rates) {
        if (tochType != 1 && getPrice(rate) > high) {
          tochType = 1;
          yield return rate;
        }
        if (tochType != -1 && getPrice(rate) < low) {
          tochType = -1;
          yield return rate;
        }
      }
    }
    public static IList<TBar> TouchDowns<TBar>(this IList<TBar> rates, double high, double low) where TBar : BarBase {
      int tochType = 0;//1-high,-1 low
      var tochDowns = new List<TBar>();
      foreach (var rate in rates) {
        if (tochType != 1 && high.Between(rate.PriceLow, rate.PriceHigh)) {
          tochDowns.Add(rate);
          tochType = 1;
        }
        if (tochType != -1 && low.Between(rate.PriceLow, rate.PriceHigh)) {
          tochDowns.Add(rate);
          tochType = -1;
        }
      }
      return tochDowns.ToArray();
    }

    public static double[] TouchDownsHighLow<TBar>(this IList<TBar> rates, Func<TBar, double> price, double[] regressionCoeffs, double skpiRatio) where TBar : BarBase {
      var line = new double[rates.Count];
      regressionCoeffs.SetRegressionPrice(0, line.Length, (i, d) => line[i] = d);
      var hl = rates.Select((r, i) => price(r) - line[i]).Skip((rates.Count * skpiRatio).ToInt()).ToArray();
      var h = hl.Max() / 2;
      var l = hl.Min().Abs() / 2;
      return new[] { h, l };
    }

    public static void FillRunningValue<TBar>(this IEnumerable<TBar> bars, Action<TBar, double> setRunningValue, Func<TBar, double> getRunningValue, Func<TBar, TBar, double> getValue,double initialValue = 0) where TBar : BarBase {
      setRunningValue(bars.First(), initialValue);
      bars.Aggregate((p, n) => {
        setRunningValue(n, getRunningValue(p) + getValue(p, n));
        return n;
      });
    }

    public static void FillRunningHeight(this IList<Rate> rates) {
      rates.FillRunningValue((r, d) => r.Distance = d, r => r.Distance, (p, n) => {
        n.RunningLow = p.RunningLow.Min(n.PriceAvg);
        n.RunningHigh = p.RunningHigh.Max(n.PriceAvg);
        return 0;// (p.PriceHigh - p.PriceLow) * (p.PriceCMALast - n.PriceCMALast).Abs() * pipSize;
      });
    }
    public static void FillDistanceByHeight<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      bars.First().Distance = 0;
      bars.Aggregate((p, n) => {
        n.Distance = p.Distance + (p.PriceHigh - p.PriceLow);
        return n;
      });
    }
    public static IList<TBar> FillDistance<TBar>(this IList<TBar> bars, Action<TBar, TBar, double> setDistance = null) where TBar : BarBase {
      (bars as IEnumerable<TBar>).FillDistance();
      return bars;
    }
    public static void FillDistance<TBar>(this IEnumerable<TBar> bars, Action<TBar, TBar, double> setDistance = null) where TBar : BarBase {
      if (setDistance == null)
        setDistance = (n, p, diff) => n.Distance = p.Distance + diff;
      var count = 0;
      bars.Aggregate((p, n) => {
        if (count++ == 0) p.Distance = 0;
        var diff = (p.PriceAvg - n.PriceAvg).Abs();
        setDistance(n, p, diff);
        return n;
      });
    }
    static IEnumerable<TBar[]> DistanceAverage<TBar>(this ICollection<TBar> rates, bool up) where TBar : BarBase {
      return rates.DistanceAverage(up ? (Func<TBar, double>)(r => r.PriceHigh) : r => r.PriceLow);
    }
    static IEnumerable<TBar[]> DistanceAverage<TBar>(this ICollection<TBar> rates, Func<TBar, double> getPrice) where TBar : BarBase {
      Func<LinkedListNode<TBar>, double> getDistance = node =>
        Math.Min((getPrice(node.Value) - getPrice(node.Previous.Value)).Abs(), (getPrice(node.Value) - getPrice(node.Next.Value)).Abs());
      var nodeStart = new LinkedList<TBar>(rates.OrderBy(getPrice).ToArray()).First.Next;
      var distances = new List<double>();
      while (nodeStart.Next != null) {
        distances.Add(getDistance(nodeStart));
        nodeStart = nodeStart.Next;
      }
      nodeStart = nodeStart.List.First.Next;
      var distanceAverage = distances.Average();
      var cluster = new List<TBar>();
      while (nodeStart.Next != null) {
        var dist = getDistance(nodeStart);
        if (dist <= distanceAverage)
          cluster.Add(nodeStart.Value);
        else {
          if (cluster.Count > 0) {
            yield return cluster.ToArray();
            cluster.Clear();
          }
        }
        nodeStart = nodeStart.Next;
      }
    }
    #endregion

    public static double MagnetPrice(this ICollection<Rate> rates) {
      var priceHeights = "".Select(o => new { price = 0.0, height = 0.0 }).ToList();
      var ms = rates.Take(rates.Count - 1)
        .Select(r1 => rates.Where(r2 => r2 > r1)
          .Select(r3 => new {
            price = (r1.PriceHigh + r3.PriceLow) / 2,
            height = new[] { (r1.PriceLow - r3.PriceLow).Abs(), (r1.PriceHigh - r3.PriceHigh).Abs(), (r1.PriceHigh - r3.PriceLow).Abs(), (r1.PriceLow - r3.PriceHigh).Abs() }.Min()
          }).OrderBy(m=>m.price).ToArray()).ToList();
      ms.ForEach(m => priceHeights.AddRange(m));
      priceHeights.Sort((ph1, ph2) => ph1.price.CompareTo(ph2.price));
      var heightIterations = 6;
      var minimumHeight = priceHeights.Select(m => m.height).ToArray().AverageByIterations(heightIterations, true).Average();
      priceHeights.RemoveAll(m => m.height > minimumHeight);
      var prices = priceHeights.Select(ph => ph.price).Distinct().ToArray();

      var priceDistances = new List<double>();
      var pds = prices.Take(prices.Count() - 1)
        .Select(p1 => prices.Where(p2 => p2 != p1)
          .Select(p3 => new {
            price1 = p1, price2 = p3, distance = (p1 - p3).Abs()
          }).ToList()).ToList();
      pds.ForEach(pd => priceDistances.AddRange(pd.Select(a=>a.distance)));
      var distanceIterations = 5;
      var minimumDistance = priceDistances.AverageByIterations(distanceIterations, true).Average();
      var distanceCount = priceDistances.Count(pd => pd < minimumDistance);
      pds.ForEach(pd => pd.RemoveAll(pd1 => pd1.distance > minimumDistance));
      pds.Sort((l1, l2) => l2.Count.CompareTo(l1.Count));
      return pds[0][0].price1;
    }

    public static double Slope(Rate rate1, Rate rate2,TimeSpan interval) {
      return (rate2.PriceAvg - rate1.PriceAvg) / (rate2.StartDate - rate1.StartDate).Divide(interval).TotalMinutes;
    }

    public static TBar[] FindExtreams<TBar>(this IList<TBar> bars, Func<TBar, TBar, TBar> aggregate, int margin = 2) where TBar : BarBase {
      if (bars.Count == 0) return new TBar[0];
      var count = bars.Count - margin * 2;
      var extreams = new List<TBar>();
      //Parallel.For(0, count, i => {
      //  lock (bars) {
      //    var a = bars.Skip(i).Take(margin * 2 + 1).ToArray();
      //    var a2 = a[margin];
      //    var a1 = a.Aggregate(aggregate);
      //    if (a1 == a2) extreams.Add(a2);
      //  }
      //});
      for (var i = 0; i < count; i++) {
        var a = bars.Skip(i).Take(margin * 2 + 1).ToArray();
        if (a.Aggregate(aggregate) == a[margin]) extreams.Add(a[margin]);
      }
      return extreams.ToArray();
    }

    public static T[][] GetIntervals<T>(this ICollection<Tuple<int, T>> barPoints, int margin) {
      var intervals = new List<int>();
      var barIntervals = new List<T[]>();
      var barInterval = new List<T>();
      if (barPoints.Count < 2) {
        barIntervals.Add(barPoints.Select(bp => bp.Item2).ToArray());
      } else {
        intervals = new List<int>();
        Func<Tuple<int, T>, Tuple<int, T>, Tuple<int, T>> getInterval = (bpp, bpn) => {
          var i = (bpp.Item1 - bpn.Item1).Abs() == 1 ? 1 : 0;
          if (i == 0) {
            if (barInterval.Count > margin * 2) {
              barIntervals.Add(barInterval.ToArray());
              barInterval.Clear();
            }
          } else {
            if (barInterval.Count == 0) barInterval.Add(bpp.Item2);
            barInterval.Add(bpn.Item2);
          }
          return bpn;
        };
        barPoints.Aggregate(getInterval);
        if (barInterval.Count > margin * 2) barIntervals.Add(barInterval.ToArray());
      }
      if (barIntervals.Count == 0)
        if (margin > 0) return barPoints.GetIntervals(margin - 1);
        else barPoints.Select(bp => bp.Item2).ToList().ForEach(b => barIntervals.Add(new T[] { b }));
      return barIntervals.ToArray();
    }

    #region AverageBy
    public static TBar[] AverageByPercantage<TBar>(this TBar[] values, Func<TBar, double> getPrice, double percentage, int minimumCount) where TBar : BarBase {
      double timeSpanRatio;
      return values.AverageByPercantage(getPrice, percentage, minimumCount, out timeSpanRatio);
    }
    public static TBar[] AverageByPercantage<TBar>(this TBar[] values, Func<TBar, double> getPrice, double percentage, int minimumCount, out double timeSpanRatio) where TBar : BarBase {
      if (values.Length == 0) { timeSpanRatio = 0; return values; }
      var average = 0.0;
      var countOriginal = values.Count();
      var countCurrent = countOriginal + 1.0;
      var timeSpanOriginal = values.Max(v => v.StartDate) - values.Min(v => v.StartDate);
      var minutesToSkip = timeSpanOriginal.TotalMinutes / 10;
      do {
        average = values.Where(v => getPrice(v) >= average).Average(getPrice);
        var vs = values.Where(v => getPrice(v) >= average).ToArray();
        if (vs.Length < minimumCount) break;
        values = vs;
        if (countCurrent == values.Count()) break;
        countCurrent = values.Length;
      } while (countCurrent / countOriginal > percentage);
      var timeSpanCurrent = values.Max(v => v.StartDate) - values.Min(v => v.StartDate);
      timeSpanRatio = timeSpanCurrent.TotalMinutes / timeSpanOriginal.TotalMinutes;
      return values;
    }

    #region AverageByIterations
    public static TBar[] AverageByIterations<TBar>(this IList<TBar> values, Func<TBar, double> getPrice, double iterations) where TBar : BarBaseDate {
      return values.AverageByIterations(getPrice, (v, a) => v >= a, iterations);
    }
    public static TBar[] AverageByIterations<TBar>(this IList<TBar> values, Func<TBar, double> getPrice, double iterations,out double average) where TBar : BarBaseDate {
      return values.AverageByIterations(getPrice, (v, a) => v >= a, iterations,out average);
    }
    public static TBar[] AverageByIterations<TBar>(this IList<TBar> values, Func<TBar, double> getPrice, Func<double, double, bool> compare, double iterations) where TBar : BarBaseDate {
      double average;
      return AverageByIterations<TBar>(values, getPrice, compare, iterations, out average);
    }
    static TBar[] AverageByIterations<TBar>(this IList<TBar> values, Func<TBar, double> getPrice, Func<double, double, bool> compare, double iterations, out double average) where TBar : BarBaseDate {
      var avg = values.Count() == 0 ? 0 : values.Average(getPrice);
      for (int i = 1; i < iterations && values.Count() > 0; i++) {
        values = values.Where(r => compare(getPrice(r), avg)).ToArray();
        if (values.Count == 0) break;
        avg = values.Average(getPrice);
      }
      average = avg;
      return values.ToArray();
    }
    #endregion
    #endregion


    #region Wave
    public static double GetWaveHeight(this IList<Rate> rates, int barFrom, int barTo) {
      var barPrev = rates.GetBarHeightBase(barFrom);
      var barPrev1 = barPrev;
      var bars = new List<double>(new[] { barPrev });
      var barsForAverage = new List<double>();
      for (var i = barFrom + 1; i <= barTo; i++) {
        var bar = rates.GetBarHeightBase(i);
        //if (bar / barPrev < 1) 
        barsForAverage.Add(barPrev);// return new[] { barPrev, barPrev1, bar }.Min();
        barPrev1 = barPrev;
        barPrev = bar;
        bars.Add(bar);
      }
      barsForAverage = barsForAverage.DefaultIfEmpty(bars.Max()).ToList();
      var barsAverage = barsForAverage.Average();
      return barsForAverage.Where(b => b >= barsAverage).Average();
    }


    public static double GetBarHeight(this Rate[] rates, int period) {
      if (rates.Count() == 0) return 0;
      //var sw = Stopwatch.StartNew();
      var bhList = new List<double>();
      var dateStart = rates[0].StartDate;
      var minutes = (rates.Last().StartDate - rates.First().StartDate).TotalMinutes.ToInt();
      for (var i = 0; i < minutes - period; i++)
        bhList.Add(rates.SkipWhile(r => r.StartDate < dateStart.AddMinutes(i)).ToArray().GetBarHeightBase(period));
      return bhList.Average();
      //Debug.WriteLine("GetBarHeight:{0} - {1:n0} ms", tm.Pair, sw.Elapsed.TotalMilliseconds);
    }

    public static double GetBarHeightBase(this IList<Rate> rates, int barPeriod) {
      var rates60 = rates.GetMinuteTicks(barPeriod);
      if (rates60.Count() == 0) return 0;
      var hs = rates60.Select(r => r.Spread).ToArray();
      var hsAvg = hs.Average();
      hs = hs.Where(h => h >= hsAvg).ToArray();
      hsAvg = hs.Average();
      return hs.Where(h => h >= hsAvg).Average();
    }
    #endregion

    public static void AddRange<T>(this IList<T> bars, IEnumerable<T> barsToAdd) {
      barsToAdd.ToList().ForEach(b => bars.Add(b));
    }
    public static void RemoveRange<T>(this IList<T> bars, int startIndex, int count) {
      while (count-- > 0 && bars.Count() >= startIndex)
        bars.Remove(bars[startIndex]);
    }
    public static void RemoveAll<T>(this IList<T> bars, Func<T,bool> filter) {
      bars.Where(filter).ToList().ForEach(b => bars.Remove(b));
    }

    public static Rate High(this ICollection<Rate> rates) {
      return rates.OrderBy(r => r.PriceAvg).Last();
    }
    public static Rate Low(this ICollection<Rate> rates) {
      return rates.OrderBy(r => r.PriceAvg).First();
    }
    public static double Middle<TBar>(this IList<TBar> bars) where TBar : BarBase {
      double min, max;
      var height = bars.Height(out min, out max);
      return min + height / 2;
    }
    public static double Height<TBar>(this IEnumerable<TBar> rates,out double min,out double max) where TBar : BarBase {
      return rates.Height(r => r.PriceAvg, out min, out max);
    }
    public static double Height<TBar>(this IEnumerable<TBar> rates) where TBar : BarBase {
      return rates.Height(r => r.PriceAvg);
    }
    public static double Density(this ICollection<Rate> rates) {
      return rates.Average(r => r.BidHigh- r.AskLow);
    }

    public static TimeSpan Duration(this ICollection<Rate> rates) {
      return rates.Max(r => r.StartDate) - rates.Min(r => r.StartDate);
    }
    public static TimeSpan Duration(this ICollection<Rate> rates, TimeSpan durationMax) {
      var sw = Stopwatch.StartNew();
      Rate prev = null; ;
      TimeSpan duration = TimeSpan.Zero;
      foreach (var rate in rates)
        if (prev == null) prev = rate;
        else {
          var d = (rate.StartDate - prev.StartDate).Duration();
          if (d <= durationMax)
            duration = duration.Add(d);
          prev = rate;
        }
      Debug.WriteLine("Duration:{0:n}", sw.ElapsedMilliseconds);
      return duration;
    }

    public static RsiStatistics RsiStats(this List<Rate> ratesByMinute) { return ratesByMinute.RsiStatsCore(); }
    public static RsiStatistics RsiStats(this Rate[] ratesByMinute) { return ratesByMinute.RsiStatsCore(); }
    static RsiStatistics RsiStatsCore<TBar>(this IEnumerable<TBar> ratesByMinute) where TBar : BarBase {

      //var rsiHigh = ratesByMinute.Where(r => r.PriceRsi > 50).ToArray();
      //var rsiLow = ratesByMinute.Where(r => r.PriceRsi < 50).ToArray();

      //var fractals = GetRsiFractals(rsiHigh).Concat(GetRsiFractals(rsiLow)).OrderBarsDescending().FixFractals();

      var RsiAverageHigh = ratesByMinute.Where(r => r.PriceRsi.GetValueOrDefault() != 50).Average(r => r.PriceRsi).Value;
      var RsiAverageLow = RsiAverageHigh;
      var rsiHigh = ratesByMinute.Where(r => r.PriceRsi > RsiAverageHigh).ToArray();
      var rsiLow = ratesByMinute.Where(r => r.PriceRsi < RsiAverageLow).ToArray();
      RsiAverageHigh = rsiHigh.Average(r => r.PriceRsi).Value;
      RsiAverageLow = rsiLow.Average(r => r.PriceRsi).Value;


      rsiHigh = rsiHigh.Where(r => r.PriceRsi > RsiAverageHigh).ToArray();
      rsiLow = rsiLow.Where(r => r.PriceRsi < RsiAverageLow).ToArray();
      RsiAverageHigh = rsiHigh.Average(r => r.PriceRsi).Value;
      RsiAverageLow = rsiLow.Average(r => r.PriceRsi).Value;

      rsiHigh = rsiHigh.Where(r => r.PriceRsi > RsiAverageHigh).ToArray();
      rsiLow = rsiLow.Where(r => r.PriceRsi < RsiAverageLow).ToArray();
      RsiAverageHigh = rsiHigh.Average(r => r.PriceRsi).Value;
      RsiAverageLow = rsiLow.Average(r => r.PriceRsi).Value;

      var RsiStdHigh = rsiHigh.StDev(r => r.PriceRsi);
      var RsiStdLow = rsiLow.StDev(r => r.PriceRsi);

      var rsiSellHigh = rsiHigh.Max(r => r.PriceRsi);
      var rsiBuyLow = rsiLow.Min(r => r.PriceRsi);


      return new RsiStatistics(RsiAverageLow, RsiStdLow, rsiBuyLow, RsiAverageHigh, RsiStdHigh, rsiSellHigh);
    }

    public static TBar[] FindWaves<TBar>(
      this ICollection<TBar> bars, Func<TBar, int> Sign, Func<TBar, double?> Sort) where TBar : BarBase {
      bars = bars.Where(b => Sort(b).GetValueOrDefault(50) != 50).OrderBars().ToArray();
      var barPrev = bars.First();
      var waves = new List<TBar>();
      var wave = new List<TBar>() { barPrev };
      var average = bars.Average(Sort).GetValueOrDefault();
      var stDev = bars.StDev(Sort);
      Func<TBar, double, double, bool> where = (r, a, s) => Sort(r) > (a + s) || Sort(r) < (a - s);
      Sign = (r) => Math.Sign(Sort(r).Value - average);
      bars = bars.Where(b => where(b, average, stDev)).Skip(1).ToArray();
      foreach (var bar in bars) {
        if (Sign(barPrev) == Sign(bar))
          wave.Add(bar);
        else {
          GetRisFractal<TBar>(Sign, Sort, barPrev, waves, wave);
          wave.Clear();
          wave.Add(bar);
        }
        barPrev = bar;
      }
      GetRisFractal<TBar>(Sign, Sort, barPrev, waves, wave);
      return waves.OrderBarsDescending().ToArray();
      /*
      var ws = waves.OrderBarsDescending().ToArray();
      ws.FillHeight(Sort);
      var wh = ws.Where(w => Sign(w) > 0).ToArray();
      var std = wh.StdDev(Sort);
      var avg = wh.Average(Sort);
      wh = wh.Where(w => Sort(w) >= (avg + std)).ToArray();
      var wl = ws.Where(w => Sign(w) < 0).ToArray();
      std = wl.StdDev(Sort);
      avg = wl.Average(Sort);
      wl = wl.Where(w => Sort(w) <= (avg-std)).ToArray();
      return wh.Concat(wl).OrderBarsDescending().ToArray().FixFractals(Sort).ToArray();
      */
    }

    private static void GetRisFractal<TBar>(Func<TBar, int> Sign, Func<TBar, double?> Sort, TBar barPrev, List<TBar> waves, List<TBar> wave) where TBar : BarBase {
      var sort = wave.OrderBy(Sort);
      var w = (Sign(barPrev) >= 0 ? sort.Last() : sort.First()).Clone() as TBar;
      w.Fractal = Sign(barPrev) >= 0 ? FractalType.Sell : FractalType.Buy;
      waves.Add(w);
    }
    private static TBar[] GetRsiFractals<TBar>(this TBar[] rsiHigh) where TBar : BarBase {
      var sell = rsiHigh[0].PriceRsi > 50;
      var fs = new List<TBar>();
      TBar prev = rsiHigh[0];
      var rateMax = new List<TBar>(new[] { prev });
      foreach (var rate in rsiHigh.Skip(1)) {
        if ((prev.StartDate - rate.StartDate) <= TimeSpan.FromMinutes(1))
          rateMax.Add(rate);
        else {
          if (rateMax.Count > 4) {
            var ro = rateMax.OrderBy(r => r.PriceAvg);
            var fractal = (sell ? ro.Last() : ro.First()).Clone() as TBar;
            fractal.Fractal = sell ? FractalType.Sell : FractalType.Buy;
            fractal.PriceRsi = sell ? rateMax.Max(r => r.PriceRsi) : rateMax.Min(r => r.PriceRsi);

            fs.Add(fractal);
          }
          rateMax.Clear();
          rateMax.Add(rate);
        }
        prev = rate;
      }
      return fs.ToArray();
    }

    enum WaveDirection { Up = 1, Down = -1, None = 0 };
    public static Rate[] FindWaves(this IEnumerable<Rate> ticks) {
      return ticks.FindWaves(0);
    }
    public static Rate[] FindWaves(this IEnumerable<Rate> ticks, int wavesCount) {
      List<Rate> waves = new List<Rate>();
      WaveDirection waveDirection = WaveDirection.None;
      ticks = ticks.OrderBarsDescending().ToArray();
      var logs = new Dictionary<int, double>();
      foreach (var rate in ticks) {
        var period = TimeSpan.FromMinutes(5);
        var ticksToRegress = ticks.Where(period, rate).ToArray();
        var coeffs = ticksToRegress.Select(t => t.PriceAvg).ToArray().Regress(1);
        if (!double.IsNaN(coeffs[1])) {
          var wd = (WaveDirection)Math.Sign(coeffs[1]);
          if (waveDirection != WaveDirection.None) {
            if (wd != waveDirection && wd != WaveDirection.None) {
              var ticksForPeak = ticks.Where(ticksToRegress.Last(), TimeSpan.FromSeconds(period.TotalSeconds / 2)).ToArray();
              waves.Add(wd == WaveDirection.Up
                ? ticksForPeak.OrderBy(t => t.PriceLow).First()
                : ticksForPeak.OrderBy(t => t.PriceHigh).Last());
              waves.Last().Fractal = wd == WaveDirection.Up ? FractalType.Buy : FractalType.Sell;
              if (waves.Count == wavesCount) break;
            }
          }
          waveDirection = wd;
        }
      }
      return waves.ToArray();
    }

    public static void FillFlatness(this Rate[] bars, int BarsMin) {
      bars = bars.OrderBarsDescending().ToArray();
      foreach (var bar in bars)
        if (!bar.Flatness.HasValue)
          bar.Flatness = bars.Where(TimeSpan.FromMinutes(120), bar).ToArray().GetFlatness(BarsMin);
    }
    public static TimeSpan GetFlatness(this Rate[] ticks, int barsMin) {
      var ticksByMinute = ticks.GetMinuteTicks(1);
      var tickLast = ticksByMinute.First();
      var flats = new List<Rate>(new[] { tickLast });
      var barsCount = 1;
      foreach (var tick in ticksByMinute.Skip(1)) {
        if (tick.OverlapsWith(tickLast) != OverlapType.None) flats.Add(tick);
        if (++barsCount >= barsMin && (double)flats.Count / barsCount < .7) break;
        if (barsCount > 20) {
          var i = 0;
          i++;
        }
      }
      return new[] { TimeSpan.FromMinutes(3), (flats.First().StartDate - flats.Last().StartDate).Add(TimeSpan.FromMinutes(1)) }.Max();

      //var ol = ticksByMinute.Skip(1).Where(t => t.OverlapsWith(tickLast) != OverlapType.None)
      //  .Concat(new[] { tickLast }).OrderBars().ToArray();
      //return ol.Length == 0 ? TimeSpan.Zero : (ol.Last().StartDate - ol.First().StartDate);
    }


    /// <summary>
    /// Fills bar.PriceSpeed with angle from linear regression.
    /// Bars must be sorted by DateStart Descending
    /// </summary>
    /// <typeparam name="TBar"></typeparam>
    /// <param name="bars"></param>
    /// <param name="bar">bar to fill with speed</param>
    /// <param name="pricer">lambda with price source</param>
    public static void FillSpeed<TBar>(this List<TBar> bars, TBar bar, Func<TBar, double> price) where TBar : BarBase {
      (bars as IEnumerable<TBar>).FillSpeed(bar, price);
    }
    /// <summary>
    /// Fills bar.PriceSpeed with angle from linear regression.
    /// Bars must be sorted by DateStart Descending
    /// </summary>
    /// <typeparam name="TBar"></typeparam>
    /// <param name="bars"></param>
    /// <param name="bar">bar to fill with speed</param>
    /// <param name="pricer">lambda with price source</param>
    public static void FillSpeed<TBar>(this TBar[] bars, TBar bar, Func<TBar, double> price) where TBar : BarBase {
      (bars as IEnumerable<TBar>).FillSpeed(bar, price);
    }
    /// <summary>
    /// Fills bar.PriceSpeed with angle from linear regression.
    /// Bars must be sorted by DateStart Descending
    /// </summary>
    /// <typeparam name="TBar"></typeparam>
    /// <param name="bars"></param>
    /// <param name="bar">bar to fill with speed</param>
    /// <param name="pricer">lambda with price source</param>
    private static void FillSpeed<TBar>(this IEnumerable<TBar> bars, TBar bar, Func<TBar, double> price) where TBar : BarBase {
      bar.PriceSpeed = bars.Select(price).ToArray().Regress(1)[1];
    }
/// <summary>
/// Max StDev for bars
/// </summary>
/// <param name="wave"></param>
/// <returns></returns>
    public static double MaxStDev<TBar>(this IList<TBar> wave)where TBar:BarBase {
      return wave.Max(r => r.PriceStdDev);// .TakeEx(-2).First().PriceStdDev;
    }

    public static double StDevByCma<TBar>(this IList<TBar> bars) where TBar : BarBase {
      return bars.StDev(r => r.PriceAvg > r.PriceCMALast ? r.PriceHigh : r.PriceLow);
    }
    public static IEnumerable<double> PriceHikes<TBar>(this IList<TBar> bars) where TBar : BarBase {
      return bars.Take(bars.Count - 1).Zip(bars.Skip(1), (r1, r2) => (r1.PriceAvg - r2.PriceAvg).Abs());
    }

    public static double Spread(this IList<Rate> rates, int iterations = 2) {
      return rates.PriceHikes().Average();
      //return rates.Average(r => r.Spread);
      //var spreads = rates.Select(r => r.PriceHigh - r.PriceLow).ToArray();
      //if (spreads.Length == 0) return double.NaN;
      //return spreads.AverageInRange(iterations).Average();
    }

    public static void AddUp<TBar>(this List<TBar> ticks, IEnumerable<TBar> ticksToAdd) where TBar : BarBase {
      var lastDate = ticksToAdd.Min(t => t.StartDate);
      ticks.RemoveAll(t => t.StartDate > lastDate);
      ticks.AddRange(ticksToAdd);
    }
    public static IEnumerable<TBar> AddUp<TBar>(this IEnumerable<TBar> ticks, IEnumerable<TBar> ticksToAdd) where TBar : BarBase {
      var lastDate = ticksToAdd.Min(t => t.StartDate);
      return ticks.Where(t => t.StartDate <= lastDate).Concat(ticksToAdd.SkipWhile(t => t.StartDate == lastDate));
    }
    public static IEnumerable<TBar> AddDown<TBar>(this IEnumerable<TBar> ticks, IEnumerable<TBar> ticksToAdd) where TBar : BarBase {
      var lastDate = ticksToAdd.Max(t => t.StartDate);
      ticks = ticks.SkipWhile(t => t.StartDate <= lastDate).ToArray();
      return ticksToAdd.Concat(ticks);
    }

    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, DateTime bar1, DateTime bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1, bar2));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TBar bar1, TimeSpan periodTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1.StartDate, bar1.StartDate + periodTo));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TBar bar1, DateTime dateTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1.StartDate, dateTo));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TimeSpan periodFrom, TBar bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar2.StartDate - periodFrom, bar2.StartDate));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, DateTime dateFrom, TBar bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(dateFrom, bar2.StartDate));
    }
    public static IEnumerable<TBar> Where<TBar>(this IEnumerable<TBar> bars, TBar bar1, TBar bar2) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(bar1.StartDate, bar2.StartDate));
    }

    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, TBar barFrom, TBar barTo) where TBar : BarBase {
      return bars.TradesPerMinute(barFrom.StartDate, barTo.StartDate);
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, TBar barFrom, TimeSpan intervalTo) where TBar : BarBase {
      return bars.TradesPerMinute(barFrom.StartDate, barFrom.StartDate + intervalTo);
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, TimeSpan intervalFrom, TBar barTo) where TBar : BarBase {
      return bars.TradesPerMinute(barTo.StartDate - intervalFrom, barTo.StartDate);
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, DateTime DaterFrom, DateTime DateTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(DaterFrom, DateTo)).TradesPerMinute();
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars, DateTimeOffset DaterFrom, DateTimeOffset DateTo) where TBar : BarBase {
      return bars.Where(b => b.StartDate.Between(DaterFrom, DateTo)).TradesPerMinute();
    }
    public static double TradesPerMinute<TBar>(this IEnumerable<TBar> bars) where TBar : BarBase {
      var count = bars.Count();
      var bo = bars.OrderBars().ToArray();
      return count / (bo.Last().StartDate - bo.First().StartDate).TotalMinutes;
    }

    public static IList<Rate> GetMinuteTicks<TBar>(this IList<TBar> fxTicks, int period) where TBar : BarBase {
      return fxTicks.GetMinuteTicks(period, false);
    }
    //static Rate[] GetMinuteTicksCore<TBar>(this IEnumerable<TBar> fxTicks, int period, bool round) where TBar : BarBase {
    //  if (!round) return GetMinuteTicksCore(fxTicks, period,false);
    //  var timeRounded = fxTicks.Min(t => t.StartDate).Round().AddMinutes(1);
    //  return GetMinuteTicksCore(fxTicks.Where(t => t.StartDate >= timeRounded), period,false);
    //}
    public static IList<Rate> GetMinuteTicks<TBar>(this IList<TBar> fxTicks, int period, bool Round, bool startFromEnd = true) where TBar : BarBase {
      fxTicks = startFromEnd ? fxTicks.OrderBarsDescending().ToList() : fxTicks.OrderBars().ToList();
      if (fxTicks.Count() == 0) return new Rate[] { };
      var startDate2 = startFromEnd ? fxTicks.Max(t => t == null ? DateTime.MinValue : t.StartDate2) : fxTicks.Min(t => t == null ? DateTime.MinValue : t.StartDate2);
      if (Round) startDate2 = startDate2.Round().AddMinutes(1);
      double? tempRsi;
      var rsiAverage = fxTicks.Average(t => t.PriceRsi.GetValueOrDefault());
      return (from t in fxTicks
              where period > 0
              group t by (((int)Math.Floor((startDate2 - t.StartDate2).TotalMinutes) / period)) * period into tg
              orderby startFromEnd ? tg.Key : -tg.Key
              select new Rate() {
                AskHigh = tg.Max(t => t.AskHigh),
                AskLow = tg.Min(t => t.AskLow),
                AskAvg = tg.Average(t => (t.AskHigh + t.AskLow) / 2),
                AskOpen = tg.Last().AskOpen,
                AskClose = tg.First().AskClose,
                BidHigh = tg.Max(t => t.BidHigh),
                BidLow = tg.Min(t => t.BidLow),
                BidAvg = tg.Average(t => (t.BidHigh + t.BidLow) / 2),
                BidOpen = tg.Last().BidOpen,
                BidClose = tg.First().BidClose,
                Mass = tg.Sum(t => t.Mass),
                PriceRsi = !(tempRsi = tg.Average(t => t.PriceRsi)).HasValue ? tempRsi
                             : tempRsi > rsiAverage ? tg.Max(t => t.PriceRsi) : tg.Min(t => t.PriceRsi),
                StartDate2 = startDate2.AddMinutes(-tg.Key)
              }
                ).ToList();
    }
    public static IEnumerable<Rate> GroupTicksToRates(this IEnumerable<Rate> ticks) {
      return from tick in ticks
             group tick by tick.StartDate.AddMilliseconds(-tick.StartDate2.Millisecond) into gt
             select new Rate() {
               StartDate2 = gt.Key,
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
          barsDelete.AddRange(new[] { bars[i - 1], bars[i - 2] });
      barsDelete.Distinct().ToList().ForEach(b => bars.Remove(b));
    }
    static void FillPower<TBar>(this TBar[] barsSource, TBar[] bars) where TBar : BarBase {
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
    public static void FillHeight<TBar>(this TBar[] bars, Func<TBar, double?> Price) where TBar : BarBase {
      var i = 0;
      bars.Take(bars.Length - 1).ToList()
        .ForEach(b => {
          b.Ph.Height = (Price(b) - Price(bars[++i])).Abs();
        });
    }
    public static void FillFractalHeight<TBar>(this TBar[] bars) where TBar : BarBase {
      bars.FillFractalHeight(b => b.FractalPrice);
    }
    public static void FillFractalHeight<TBar>(this TBar[] bars, Func<TBar, double?> getPrice) where TBar : BarBase {
      var i = 0;
      bars.Take(bars.Length - 1).ToList()
        .ForEach(b => {
          b.Ph.Height = (getPrice(b) - getPrice(bars[++i])).Abs();
          b.Ph.Time = b.StartDate - bars[i].StartDate;
        });
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
      var barsMinMax = new[] { barsOrdered.First(), barsOrdered.Last() }.OrderBars().ToArray();
      barSource.Ph.Height = barsMinMax[1].PriceAvg - barsMinMax[0].PriceAvg;// bars.Last().PriceHeight(bars.First());
      var barsByDate = bars.OrderBars().ToArray();
      barSource.Ph.Time = (barsByDate.Last().StartDate - barsByDate.First().StartDate);
      barSource.Ph.Trades = bars.Count();
      if (bars.Count() == 0) {
        barsByDate.SaveToFile(b => b.PriceHigh, b => b.PriceLow, "C:\\bars.csv");
      }
      if (barSource.Ph.Time == TimeSpan.Zero) barSource.Ph.Time = period;
      if (period == TimeSpan.Zero) {
        barSource.Ph.Work = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.Work * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
        barSource.Ph.Power = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.Power * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
        barSource.Ph.K = bars.Sum(b => !b.Ph.Work.HasValue ? 0 : b.Ph.K * b.Ph.Time.Value.TotalSeconds) / barSource.Ph.Time.Value.TotalSeconds;
      } else
        barSource.Ph.Work = barSource.Ph.Power = null;

      if (barSource != null) return;
    }
    public static List<TBar> FixFractals<TBar>(this IEnumerable<TBar> fractals) where TBar : BarBase {
      return fractals.FixFractals(b => b.FractalPrice);
    }
    public static List<TBar> FixFractals<TBar>(this IEnumerable<TBar> fractals, Func<TBar, double?> Price) where TBar : BarBase {
      var fractalsNew = new List<TBar>();
      foreach (var f in fractals)
        if (fractalsNew.Count > 0 && fractalsNew.Last().Fractal == f.Fractal)
          fractalsNew[fractalsNew.Count - 1] = BarBase.BiggerFractal(fractalsNew.Last(), f, Price);
        else fractalsNew.Add(f);
      return fractalsNew;
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

    public static void SaveToFile<T, D>(this IEnumerable<T> rates, Func<T, D> price, string fileName) where T : BarBase {
      StringBuilder sb = new StringBuilder();
      sb.Append("Time,Price,Indicator" + Environment.NewLine);
      rates.ToList().ForEach(r => sb.Append(r.StartDate + "," + r.PriceClose + "," + price(r) + Environment.NewLine));
      using (var f = System.IO.File.CreateText(fileName)) {
        f.Write(sb.ToString());
      }
    }


    public static TBar[] FillRsi<TBar>(this TBar[] rates, Func<TBar, double> getPrice) where TBar : BarBase {
      //var period = Math.Floor((rates.Last().StartDate - rates.First().StartDate).TotalMinutes).ToInt();
      var period = rates.Length - 2;
      return rates.FillRsi(period, getPrice);
    }
    public static TBar[] FillRsi<TBar>(this TBar[] rates, int period, Func<TBar, double> getPrice) where TBar : BarBase {
      for (int i = period; i < rates.Length; i++) {
        UpdateRsi(period, rates, i, getPrice);
      }
      return rates;
    }
    static void UpdateRsi<TBar>(int numberOfPeriods, TBar[] rates, int period, Func<TBar, double> getPrice) where TBar : BarBase {
      if (period >= numberOfPeriods) {
        var i = 0;
        var sump = 0.0;
        var sumn = 0.0;
        var positive = 0.0;
        var negative = 0.0;
        var diff = 0.0;
        if (period == numberOfPeriods) {
          for (i = period - numberOfPeriods + 1; i <= period; i++) {
            diff = getPrice(rates[i]) - getPrice(rates[i - 1]);
            if (diff >= 0)
              sump = sump + diff;
            else
              sumn = sumn - diff;
          }
          positive = sump / numberOfPeriods;
          negative = sumn / numberOfPeriods;
        } else {
          diff = getPrice(rates[period]) - getPrice(rates[period - 1]);
          if (diff > 0)
            sump = diff;
          else
            sumn = -diff;
          positive = (rates[period - 1].PriceRsiP * (numberOfPeriods - 1) + sump) / numberOfPeriods;
          negative = (rates[period - 1].PriceRsiN * (numberOfPeriods - 1) + sumn) / numberOfPeriods;
        }
        rates[period].PriceRsiP = positive;
        rates[period].PriceRsiN = negative;
        rates[period].PriceRsi = 100 - (100 / (1 + positive / negative));
      }
    }


    public static void FillRsis<TBar>(this TBar[] bars, TimeSpan RsiPeriod) where TBar : BarBase {
      double rsiPrev = 0;
      foreach (var rate in bars.Where(t => !t.PriceRsi.HasValue)) {
        var ts = bars.TakeWhile(t => t <= rate).ToArray();
        if ((ts.Last().StartDate - ts.First().StartDate) > RsiPeriod) {
          ts = ts.Where(RsiPeriod, rate).ToArray();
          rate.PriceRsi = ts.FillRsi(t => t.PriceAvg).Last().PriceRsi;
          if (!rate.PriceRsi.HasValue || double.IsNaN(rate.PriceRsi.Value))
            rate.PriceRsi = rsiPrev;
          rsiPrev = rate.PriceRsi.Value;
        } else rate.PriceRsi = 50;
      }
    }
    public static void FillRsis<TBar>(this TBar[] bars, int RsiTicks) where TBar : BarBase {
      double rsiPrev = 0;
      foreach (var rate in bars.Where(t => !t.PriceRsi.HasValue).AsParallel()) {
        var ts = bars.TakeWhile(t => t <= rate).ToArray();
        if (ts.Length > RsiTicks) {
          ts = ts.Skip(ts.Count() - RsiTicks).ToArray();
          rate.PriceRsi = ts./*GetMinuteTicks(1).*/OrderBars().ToArray().FillRsi(t => t.PriceAvg).Last().PriceRsi;
          if (!rate.PriceRsi.HasValue || double.IsNaN(rate.PriceRsi.Value))
            rate.PriceRsi = rsiPrev;
          rsiPrev = rate.PriceRsi.Value;
        } else rate.PriceRsi = 50;
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
      foreach (var bar in bars.Where(b => b.Mass.HasValue))
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
    public static List<TBar> FindFractalTicks<TBar>(this IEnumerable<TBar> ticks, double waveHeight, TimeSpan period, double padRight, int count
  , TBar[] fractalsToSkip) where TBar : Rate {
      return ticks.FindFractalTicks(waveHeight, period, padRight, count, fractalsToSkip, r => r.PriceHigh, r => r.PriceLow);
    }
    public static List<TBar> FindFractalTicks<TBar>(this IEnumerable<TBar> ticks, double waveHeight, TimeSpan period, double padRight, int count
      , TBar[] fractalsToSkip, Func<Rate, double> priceHigh, Func<Rate, double> priceLow) where TBar : Rate {
      var fractals = ticks.ToArray().GetMinuteTicks(1).OrderBarsDescending().FindFractals(waveHeight, period, padRight, count, fractalsToSkip, priceHigh, priceLow);
      return fractals.Select(f => {
        var tt = ticks.Where(t => t.StartDate.Between(f.StartDate.AddSeconds(-70), f.StartDate.AddSeconds(70))).OrderBy(t => t.PriceByFractal(f.Fractal));
        var fractal = (f.Fractal == FractalType.Buy ? tt.First() : tt.Last()).Clone() as TBar;
        fractal.Fractal = f.Fractal;
        return fractal;
      }).ToList();
    }
    public static List<TBar> FindFractals<TBar>(this IEnumerable<TBar> bars, double waveHeight, TimeSpan period, double padRight, int count) where TBar : BarBase {
      return bars.FindFractals(waveHeight, period, padRight, count, new TBar[] { });
    }
    public static List<TBar> FindFractals<TBar>(this IEnumerable<TBar> bars, double waveHeight, TimeSpan period, double padRight, int count
      , TBar[] fractalsToSkip) where TBar : BarBase {
      return bars.FindFractals(waveHeight, period, padRight, count, fractalsToSkip, r => r.PriceHigh, r => r.PriceLow);
    }
    public static List<TBar> FindFractals<TBar>(this IEnumerable<TBar> rates, double waveHeight, TimeSpan period, double padRight, int count
      , TBar[] fractalsToSkip, Func<TBar, double> priceHigh, Func<TBar, double> priceLow) where TBar : BarBase {
      var halfPeriod = TimeSpan.FromSeconds(period.TotalSeconds / 2.0);
      var rightPeriod = TimeSpan.FromSeconds(period.TotalSeconds * padRight);
      DateTime nextDate = DateTime.MaxValue;
      var fractals = new List<TBar>();
      var dateFirst = rates.Min(r => r.StartDate) + rightPeriod;
      var dateLast = rates.Max(r => r.StartDate) - rightPeriod;
      var waveFractal = 0D;
      foreach (var rate in rates.Where(r => r.StartDate.Between(dateFirst, dateLast))) {
        UpdateFractal(rates, rate, period, waveHeight, priceHigh, priceLow);
        if (rate.HasFractal && !fractalsToSkip.Contains(rate)) {
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
              if (rate.FractalWave(fractals[fractals.Count - 1]) >= waveFractal && (fractals.Last().StartDate - rate.StartDate).Duration().TotalSeconds >= period.TotalSeconds / 30)
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
    static void UpdateFractal<TBars>(IEnumerable<TBars> rates, TBars rate, TimeSpan period, double waveHeight
      , Func<TBars, double> priceHigh, Func<TBars, double> priceLow) where TBars : BarBase {
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
        priceHigh(rate) >= ratesInRange.Max(priceHigh)
        &&
        (//(rate.PriceHigh - ratesLeft.Min(r => r.PriceLow)) >= waveHeight        || 
        (priceHigh(rate) - ratesRight.Min(priceLow)) >= waveHeight
        )
        ? HedgeHog.Bars.FractalType.Sell : HedgeHog.Bars.FractalType.None;
      rate.FractalBuy =
        priceLow(rate) <= ratesInRange.Min(priceLow)
        &&
        (//(ratesLeft.Max(r => r.PriceHigh) - rate.PriceLow) >= waveHeight ||
        (ratesRight.Max(priceHigh) - priceLow(rate)) >= waveHeight
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


    public static IEnumerable<TBar> FillOverlaps<TBar>(this IEnumerable<TBar> bars, TimeSpan period) where TBar : BarBase {
      foreach (var bar in bars)
        bar.FillOverlap(bars.Where(r => r.StartDate < bar.StartDate)/*.Take(10)*/, period);
      return bars;
    }
    public static void SetCMA<TBars>(this ICollection<TBars> bars, Func<TBars, double> cmaSource, int cmaPeriod) where TBars : BarBase {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      foreach (var bar in bars) {
        bar.PriceCMA = new List<double>(3);
        cma3 = Lib.Cma(cma3, cmaPeriod, (cma2 = Lib.Cma(cma2, cmaPeriod, (cma1 = Lib.Cma(cma1, cmaPeriod, cmaSource(bar))).Value)).Value);
        bar.PriceCMA.Add(cma1.Value);
        bar.PriceCMA.Add(cma2.Value);
        bar.PriceCMA.Add(cma3.Value);
      }
    }
    public static void SetCMA<TBars>(this ICollection<TBars> ticks, int cmaPeriod
      , double? cma1 = null, double? cma2 = null, double? cma3 = null) where TBars : BarBase {
      foreach (var t in ticks) {
        t.PriceCMA = new List<double>(3);
        cma3 = Lib.Cma(cma3, cmaPeriod, (cma2 = Lib.Cma(cma2, cmaPeriod, (cma1 = Lib.Cma(cma1, cmaPeriod, t.PriceAvg)).Value)).Value);
        t.PriceCMA.Add(cma1.Value);
        t.PriceCMA.Add(cma2.Value);
        t.PriceCMA.Add(cma3.Value);
      }
    }
    public static void SetCma<TBar>(this ICollection<TBar> ticks, Func<TBar, TBar, double> getValue,Func<TBar,List<double>>getCmaHolder, double cmaPeriod, int cmaLevels = 3) where TBar : BarBase {
      var cmas = new List<double>(cmaLevels);
      var first = ticks.First();
      var firstAvg = ticks.Take(cmaPeriod.ToInt()).Zip(ticks.Skip(1).Take(cmaPeriod.ToInt()), (f, s) => getValue(s, f)).Average();
      for (var i = 0; i < cmaLevels; i++)
        cmas.Add(firstAvg);

      getCmaHolder(first).Clear();
      getCmaHolder(first).AddRange(cmas); first.PriceCMALast = firstAvg;

      ticks.Aggregate((p, t) => {
        var cmaHolder = getCmaHolder(t);
          cmaHolder.Clear();
        cmaHolder.Add(cmas[0] = cmas[0].Cma(cmaPeriod, getValue(p, t)));
        for (var i = 1; i < cmaLevels; i++)
          cmaHolder.Add(cmas[i] = cmas[i].Cma(cmaPeriod, cmas[i - 1]));
        return t;
      });
    }
    public static void SetCma<TBar>(this ICollection<TBar> ticks, Func<TBar, TBar, double> getValue, double cmaPeriod, int cmaLevels = 3) where TBar : BarBase {
      if (cmaPeriod == 0)
        ticks.ToList().ForEach(t => t.PriceCMALast = double.NaN);
      else {
        var cmas = new List<double>(cmaLevels);
        var first = ticks.First();
        var firstAvg = ticks.Take(cmaPeriod.ToInt()).Zip(ticks.Skip(1).Take(cmaPeriod.ToInt()), (f, s) => getValue(s, f)).Average();
        for (var i = 0; i < cmaLevels; i++)
          cmas.Add(firstAvg);

        first.PriceCMA = cmas; first.PriceCMALast = firstAvg;

        ticks.Aggregate((p, t) => {
          if (t.PriceCMA != null) t.PriceCMA.Clear();
          else t.PriceCMA = new List<double>(cmaLevels);
          t.PriceCMA.Add(cmas[0] = cmas[0].Cma(cmaPeriod, getValue(p, t)));
          for (var i = 1; i < cmaLevels; i++)
            t.PriceCMA.Add(cmas[i] = cmas[i].Cma(cmaPeriod, cmas[i - 1]));
          t.PriceCMALast = t.PriceCMA[t.PriceCMA.Count - 1];
          return t;
        });
      }
    }
    public static void SetCma<TBars>(this ICollection<TBars> ticks, double cmaPeriod, int cmaLevels = 3) where TBars : BarBase {
      var cmas = new List<double>(cmaLevels);
      for (var i = 0; i < cmaLevels; i++)
        cmas.Add(double.NaN);
      foreach (var t in ticks) {
        if (t.PriceCMA != null) t.PriceCMA.Clear();
        else  t.PriceCMA = new List<double>(cmaLevels);
        t.PriceCMA.Add(cmas[0] = cmas[0].Cma(cmaPeriod, t.PriceAvg));
        for (var i = 1; i < cmaLevels; i++) {
          t.PriceCMA.Add(cmas[i] = cmas[i].Cma(cmaPeriod, cmas[i - 1]));
        }
        t.PriceCMALast = t.PriceCMA[t.PriceCMA.Count - 1];
      }
    }
    public static void SetTrima<TBars>(this IList<TBars> ticks, int period) where TBars : BarBase {
      int outBegIdx, outNBElement;
      var outTrima = ticks.Select(t => t.PriceAvg).ToArray().Trima(period, out outBegIdx, out outNBElement);
      var up = ticks.Count;
      for (; outBegIdx < up; outBegIdx++)
        ticks[outBegIdx].PriceTrima = outTrima[outBegIdx + 1 - period];
    }
    public static DataPoint[] GetCurve(IEnumerable<BarBase> ticks, int cmaPeriod) {
      double? cma1 = null;
      double? cma2 = null;
      double? cma3 = null;
      int i = 0;
      return (from tick in ticks
              select
              new DataPoint() {
                Value = (cma3 = Lib.Cma(cma3, cmaPeriod, (cma2 = Lib.Cma(cma2, cmaPeriod, (cma1 = Lib.Cma(cma1, cmaPeriod, tick.PriceAvg)).Value)).Value)).Value,
                Date = tick.StartDate,
                Index = i++
              }
                  ).ToArray();
    }
    public static TBar FindBar<TBar>(this IEnumerable<TBar> bars, DateTime startDate) where TBar : BarBaseDate {
      if (bars.Count() < 2) return bars.FirstOrDefault();
      var l = new LinkedList<TBar>(bars.OrderBars());
      for (var node = l.First; node.Next != null; node = node.Next)
        if (startDate >= node.Value.StartDate && startDate < node.Next.Value.StartDate)
          return new[] { node.Value, node.Next.Value }.OrderBy(b => (b.StartDate - startDate).Duration()).First();
      return l.Last.Value.StartDate == startDate ? l.Last.Value : null;
    }
    public static IEnumerable<T> OrderBars<T>(this IEnumerable<T> rates) where T : BarBaseDate {
      return typeof(T) == typeof(Tick) ?
        rates.Cast<Tick>().OrderBy(r => r.StartDate).ThenBy(r => r.Row).Cast<T>() : rates.OrderBy(r => r.StartDate);
    }
    public static IEnumerable<T> OrderBarsDescending<T>(this IEnumerable<T> rates) where T : BarBaseDate {
      return typeof(T) == typeof(Tick) ?
        rates.OfType<Tick>().OrderByDescending(r => r.StartDate).ThenByDescending(r => r.Row).OfType<T>() : rates.ToArray().OrderByDescending(r => r.StartDate);
    }

    class DistanceInfo {
      public double Distance { get; set; }
      public double Ask { get; set; }
      public double Bid { get; set; }
      public DateTime StartDate { get; set; }
      public DistanceInfo(double ask, double bid, DateTime startDate) {
        this.Ask = ask;
        this.Bid = bid;
        this.StartDate = startDate;

      }
    }
    public static RateDistance[] GetDistances(this ICollection<Rate> ticks) {
      var period = ticks.GetPeriod();
      //var tickDistances = (from t in ticks
      //                     //orderby t.StartDate
      //                     select t into t
      //                     join t1 in ticks on t.Index equals t1.Index + 1.0
      //                     select new { Distance = Math.Abs((double)(t.AskClose - t1.AskClose)), Ask = t.AskClose, Bid = t.BidClose, StartDate = t.StartDate.AddMilliseconds(-t.StartDate.Millisecond) }).ToArray();
      if (period >= TimeSpan.FromMinutes(1)) {
        var rds = new List<RateDistance>();
        ticks.ToList().ForEach(t => {
          rds.Insert(0, new RateDistance(t.PriceHigh, t.PriceLow, 0, t.StartDate));
        });
        return rds.ToArray();
      }
      var distances = new List<DistanceInfo>();
      ticks.Aggregate((tp, tn) => {
        distances.Insert(0,
          new DistanceInfo(tn.AskClose, tn.BidClose, tn.StartDate.AddMilliseconds(-tn.StartDate.Millisecond)));
        return tn;
      });
      int i = 0;
      return (from t in
                from t in distances
                //orderby t.StartDate descending
                select t
              group t by t.StartDate into g
              let l = ++i
              select new {
                g, MA = g.Skip(l).Take(5).DefaultIfEmpty().Average(ga => {
                  if (ga != null) {
                    return (ga.Ask + ga.Bid) / 2.0;
                  }
                  return 0.0;
                })
              }).Select(gMA => {
                if (gMA.g == null) {
                  Debugger.Break();
                }
                return new RateDistance(
                  gMA.g.Average(t => t.Ask),
                  gMA.g.Average(t => t.Bid),
                  gMA.MA,
                  gMA.g.Key);
              }).ToArray<RateDistance>();
    }
    public static PriceBar[] GetPriceBars(this ICollection<Rate> Rates, double PointSize, int rowCountOffset) {
      int periodMin = 1;
      double rowCurr;
      double spreadAsk;
      double spreadBid;
      double askMax = double.MinValue;
      double askMin = double.MaxValue;
      double bidMax = double.MinValue;
      double bidMin = double.MaxValue;
      DateTimeOffset firstBarDate = DateTimeOffset.MaxValue;
      int digits = 4;
      Func<int, double> calcRowOffest = i => Math.Pow(rowCountOffset, 1 / Math.Pow(i, 1 / 4.0));
      var rates_01 = Rates.Select(((r, i) =>{
        return
new {
  AskHigh = askMax = Math.Max(askMax, r.AskHigh).Round(digits),
  AskLow = askMin = Math.Min(askMin, r.AskLow).Round(digits),
  BidLow = bidMin = Math.Round(Math.Min(bidMin, r.BidLow), digits),
  BidHigh = bidMax = Math.Round(Math.Max(bidMax, r.BidHigh), digits),
  SpreadAsk = spreadAsk = Math.Max(r.AskHigh - askMin/* Rates.Take((i + 1)).Min((al => al.AskLow))*/,
               /*Rates.Take(i + 1).Max((al => al.AskHigh))*/askMax - r.AskLow),
  SpreadBid = spreadBid = Math.Max(r.BidHigh - bidMin/*Rates.Take(i + 1).Min(al => al.BidLow)*/,
               /*Rates.Take(i + 1).Max(((al => al.BidHigh)))*/bidMax - r.BidLow),
  StartDate2 = (i == 0) ? (firstBarDate = r.StartDate2) : r.StartDate2,
  Row = rowCurr = Math.Min(/*(serverTime - firstBarDate).TotalMinutes / (periodMin)*/0, 0.0) + i,
  SpeedAsk = spreadAsk / ((rowCurr + calcRowOffest(i + 1)) * periodMin),
  SpeedBid = spreadBid / ((rowCurr + calcRowOffest(i + 1)) * periodMin)
};
})).ToArray();
      return rates_01
        .Select(((r, i) => new PriceBar { AskHigh = r.AskHigh, AskLow = r.AskLow, BidLow = r.BidLow, BidHigh = r.BidHigh, 
          Spread = ((r.SpreadAsk + r.SpreadBid) / 2.0) / PointSize, 
          Speed = ((r.SpeedAsk + r.SpeedBid) / 2.0) / PointSize, Row = r.Row, StartDate2 = r.StartDate2 })).ToArray();
    }

  }
}
