﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Concurrent;
using HedgeHog;
using System.Collections;

namespace HedgeHog {
  public static class RelativeStDevStore {
    public class Rsd {
      public double RSD { get; set; }
      public int Height { get; set; }
      public int Count { get; set; }
      public override string ToString() {
        return new { Height, Count, RSD }.ToString();
      }
      public override bool Equals(object obj) {
        var o = (Rsd)obj;
        return obj == null || this == null ? false : o.Height == Height && o.Count == Count && o.RSD == RSD;
      }
    }
    public static readonly ConcurrentDictionary<int, ConcurrentDictionary<int, double>> RSDs = new ConcurrentDictionary<int, ConcurrentDictionary<int, double>>();
    public static double Get(int height, int count) {
      return RSDs.GetOrAdd(height, (h) => {
        var dic = new ConcurrentDictionary<int, double>();
        dic.TryAdd(count, Calc(h, count));
        return dic;
      }).GetOrAdd(count, c => Calc(height, c));
    }
    public static double Calc(int height, int count) {
      var step = height / (count - 1.0);
      var range = Enumerable.Range(0, count).Select(i => i * step).ToArray();
      return Math.Round(range.StDev() / height, 4);
    }

  }
  public static class MathExtensions {
    public class Box<T> {
      public T Value { get; set; }
      public Box(T v) {
        Value = v;
      }
      public override string ToString() {
        return Value.ToString();
      }
      public static implicit operator T(Box<T> m) {
        return m.Value;
      }
    }
    public static readonly double StDevRatioMax = 0.288675135;

    public static IEnumerable<double[]> PrevNext(this IList<double> bars){
      return bars.Take(bars.Count - 1).Zip(bars.Skip(1), (r1, r2) => new[] { r1, r2 });
    }


    public static double ComplexValue(this alglib.complex b) { return Math.Sqrt(b.x * b.x + b.y * b.y); }

    public static double FftFrequency(this IEnumerable<double> signalIn, bool reversed) {
      alglib.complex[] bins;
      return signalIn.FftFrequency(reversed, out bins);
    }
    public static double FftFrequency(this IEnumerable<double> signalIn, bool reversed, out alglib.complex[] bins) {
      bins = (alglib.complex[])FftSignalBins(signalIn, reversed);
      return bins.Select(ComplexValue).Skip(1).Take(5).Max();
    }

    public static IList<alglib.complex> FftSignalBins(this IEnumerable<double> signalIn, bool reversed = false) {
      alglib.complex[] bins;
      double[] signal = signalIn.SafeArray();
      var line = signal.ToArray().Regression(1);
      IEnumerable<double> ratesFft = signal;
      if (reversed) ratesFft = ratesFft.Reverse();
      alglib.fftr1d(ratesFft.Zip(line, (r, l) => r - l).ToArray(), out bins);
      return bins;
    }
    public static IList<alglib.complex> FftBins(this IEnumerable<double> values) {
      alglib.complex[] bins;
      var signal = values.SafeArray();
      var avg = signal.Average();
      var line = Enumerable.Repeat(avg, signal.Length);
      alglib.fftr1d(signal.Zip(line, (r, l) => r - l).ToArray(), out bins);
      return bins;
    }
    public static IList<alglib.complex> FftHarmonic(this IList<alglib.complex> bins, int harmonic) {
      Func<int, IEnumerable<alglib.complex>> repeat = (count) => { return Enumerable.Repeat(new alglib.complex(0), count); };
      return bins.Take(1)
        .Concat(repeat(harmonic - 1))
        .Concat(new[] { bins[harmonic] })
        .Concat(repeat(bins.Count - harmonic - 1))
        .ToArray();
    }

    public static IList<alglib.complex> FftHarmonic(this IList<alglib.complex> bins, int harmonic, int range) {
      var c = new alglib.complex(0);
      Func<int, IEnumerable<alglib.complex>> repeat = (count) => { return Enumerable.Repeat(c, count); };
      return bins.Select((b, i) => {
        if (i == 0) return b;
        if (i < harmonic) return c;
        if (i.Between(harmonic, harmonic + range - 1)) return b;
        return c;
      }).ToArray();
    }

    /// <summary>
    /// If values is array returns values as T[], othervise values.ToArra()
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public static T[] SafeArray<T>(this IEnumerable<T> values) {
      return values as T[] ?? values.ToArray();
    }

    public static ILookup<bool, double> Fractals(this IList<double> rates, int fractalLength) {
      return rates.Fractals(fractalLength, d => d, d => d);
    }
    /// <summary>
    /// Find fractals
    /// </summary>
    /// <param name="rates"></param>
    /// <param name="fractalLength">Periods count</param>
    /// <param name="priceHigh"></param>
    /// <param name="priceLow"></param>
    /// <returns>Fractal Bars by Ups and Downs</returns>
    public static ILookup<bool, TBar> Fractals<TBar>(this IList<TBar> rates1, int fractalLength, Func<TBar, double> priceHigh, Func<TBar, double> priceLow,bool includeTails = false) {
      var rates = rates1.Select(r => new { r, h = priceHigh(r), l = priceLow(r) }).ToArray();
      var indexMiddle = fractalLength / 2;
      var zipped = rates.Zip(rates.Skip(1), (f, s) => new[] { f, s }.ToList());
      for (var i = 2; i < fractalLength; i++)
        zipped = zipped.Zip(rates.Skip(i), (z, v) => { z.Add(v); return z; });
      return zipped.AsParallel()
        .Where(z => includeTails || z.Count == fractalLength)
        .Select(z => new { max = z.Max(a => a.h), min = z.Min(a => a.l), middle = z[indexMiddle] })
        .Select(a => new { rate = a.middle, isUp = a.max == a.middle.h, IsDpwm = a.middle.l == a.min })
        .Where(a => a.isUp || a.IsDpwm)
        .ToLookup(a => a.isUp, a => a.rate.r);
    }

    public static IList<T> Wavelette<T>(this IList<T> values, Func<T, double> value) {
      var wavelette = new List<T>(values.Take(2));
      if (values.Count > 1) {
        var sign = Math.Sign(value(values[1]) - value(values[0]));
        var prev = values[1];
        foreach (var curr in values.Skip(2)) {
          var s = Math.Sign(value(curr) - value(prev));
          if (s == -sign) break;
          if (sign == 0) sign = s;
          wavelette.Add(curr);
          prev = curr;
        }
      }
      return wavelette;
    }
    public static IList<double> Wavelette(this IList<double> values) {
      var sign = Math.Sign(values[1] - values[0]);
      var wavelette = new List<double>(values.Take(2));
      var prev = values[1];
      foreach (var curr in values.Skip(2)) {
        var s = Math.Sign(curr - prev);
        if (s == -sign) break;
        if (sign == 0) sign = s;
        wavelette.Add(curr);
        prev = curr;
      }
      return wavelette;
    }
    public static IList<double> CrossesInMiddle(this IEnumerable<double> valuesIn, double value, out int index) {
      var values = valuesIn.Select(v1 => v1 - value);
      return CrossesInMiddle(values,out index);
    }
    public static IList<double> CrossesInMiddle(this IEnumerable<double> values1, IEnumerable<double> values2) {
      int index;
      return values1.CrossesInMiddle(values2, out index);
    }
    public static IList<double> CrossesInMiddle(this IEnumerable<double> values1, IEnumerable<double> values2, out int index) {
      var values = values1.Zip(values2, (v1, v2) => v1 - v2);
      return CrossesInMiddle(values,out index);
    }

    private static IList<double> CrossesInMiddle(IEnumerable<double> values) {
      int index;
      return values.CrossesInMiddle(out index);
    }
    private static IList<double> CrossesInMiddle(this IEnumerable<double> values,out int index) {
      var last = new Box<double>(values.First());
      var counts = "".Select(s=> new {index = new Box<int>(0),last = new Box<double>(0)}).ToList();
      counts.Add(new { index = new Box<int>(0), last });
      Func<double, double, double>[] comp = new[] { Math.Min, (Func<double, double, double>)null, Math.Max };
      index = 0;
      foreach (var v in values.Skip(1)) {
        index++;
        var sign = Math.Sign(v);
        if (sign != 0) {
          if (sign == Math.Sign(last.Value)) {
            var compI = sign > 0 ? 2 : 0;
            last.Value = comp[compI](last.Value, v);
            counts.Last().index.Value = index;
          } else {
            last = last = new Box<double>(v);
            counts.Add(new { index = new Box<int>(index), last });
          }
        }
      }
      var ret = counts.Skip(1).Take(Math.Max(counts.Count - 2, 0)).ToArray();
      index = ret.Select(b => b.index.Value).LastOrDefault();
      return ret.Select(b => b.last.Value).ToArray();
    }
    public static IList<double[]> Crosses(this IList<double> values1, IList<double> values2) {
      var values = values1.Zip(values2, (v1, v2) => v1 - v2).ToList();
      var last = new Box<double>(values[0]);
      var counts = new List<Box<double>>() { last };
      Func<double, double, double>[] comp = new[] { Math.Min, (Func<double, double, double>)null, Math.Max };
      foreach (var v in values.Skip(1)) {
        var sign = Math.Sign(v);
        if (sign != 0) {
          if (sign == Math.Sign(last.Value)) {
            var compI = sign > 0 ? 2 : 0;
            last.Value = comp[compI](last.Value, v);
          } else
            counts.Add(last = new Box<double>(v));
        }
      }
      var pos = counts.Where(v => v.Value > 0).Select(b => b.Value).ToArray();
      var neg = counts.Where(v => v.Value < 0).Select(b => b.Value).ToArray();
      return new[] { pos, neg };
    }
    public static IEnumerable<Tuple<T, T>> Mash<T>(this IList<T> list) {
      return list.Zip(list.Skip(1), (f, s) => new Tuple<T, T>(f, s));
    }
    public static IEnumerable<T> Crosses<T>(this IList<T> list, IList<T> signal,Func<T,double> getValue ) {
      Func<T, T, double> sign = (v1, v2) => Math.Sign(getValue(v1) - getValue(v2));
      return list.Mash()
        .Zip(signal, (m, s) => new { signFirst = sign(m.Item1, s), signSecond = sign(m.Item2, s), first = m.Item1 })
        .Where(a => a.signFirst != a.signSecond)
        .Select(a => a.first);
    }
    public static IEnumerable<T> Crosses<T>(this IList<T> list, IList<double> signal, Func<T, double> getValue) {
      Func<T, double, double> sign = (v1, v2) => Math.Sign(getValue(v1) - v2);
      return list.Mash()
        .Zip(signal, (m, s) => new { signFirst = sign(m.Item1, s), signSecond = sign(m.Item2, s), first = m.Item1 })
        .Where(a => a.signFirst != a.signSecond)
        .Select(a => a.first);
    }
    /// <summary>
    /// Find crossed elements of array over <paramref name="signal"/>
    /// </summary>
    /// <param name="list"></param>
    /// <param name="signal"></param>
    /// <returns>Elements at cross point </returns>
    public static IEnumerable<double> Crosses2(this IList<double> list, IList<double> signal) {
      return list.Mash()
        .Zip(signal, (m, s) => new { signFirst = Math.Sign(m.Item1 - s), signSecond = Math.Sign(m.Item2 - s), first = m.Item1 })
        .Where(a => a.signFirst != a.signSecond)
        .Select(a => a.first);
    }

    public static double CrossesAverageRatio(this IList<double> rates, double step,int averageIterations) {
      return rates.CrossesAverage(step, averageIterations) / (double)rates.Count;
    }
    public static double CrossesAverage(this IList<double> rates, double step, int averageIterations) {
      var min = rates.Min();
      var max = rates.Max();
      var height = max - min;
      var linesCount = (height / step).ToInt();
      var levels = ParallelEnumerable.Range(0, linesCount).Select(level => min + level * step);
      return levels.Aggregate(new List<double>(), (list, level) => {
        var line = Enumerable.Repeat(level, rates.Count).ToArray();
        var crosses = rates.Crosses2(line).Count();
        list.Add(crosses);
        return list;
      }, list => list.AverageByIterations(averageIterations).Average());
    }

    public static double CrossesAverageRatioByRegression(this IList<double> rates, double step, int averageIterations) {
      return rates.CrossesAverageByRegression(step, averageIterations) / (double)rates.Count;
    }
    public static double CrossesAverageByRegression(this IList<double> rates, double step, int averageIterations) {
      var regressionLine = rates.Regression(1);
      var zipped = regressionLine.Zip(rates, (l, r) => r - l).ToArray();
      var min = zipped.Min();
      var max = zipped.Max();
      var height = max - min;
      var point = step;
      var offsets = ParallelEnumerable.Range(0, (height / step).ToInt()).Select(h => min + h * step);
      return offsets.Aggregate(new List<double>(), (list, offset) => {
        var line = regressionLine.Select(p => p + offset).ToArray();
        var crosses = rates.Crosses2(line).Count();
        list.Add(crosses);
        return list;
      }, list => list.AverageByIterations(averageIterations).Average());
    }

    public static double[] Sin(int sinLength, int waveLength, double aplitude,double yOffset, int wavesCount) {
      var sin = new double[waveLength];
      var xOffset = (Math.PI / 180) * wavesCount * sinLength / waveLength;
      Enumerable.Range(0, waveLength).AsParallel().ForAll(i => sin[i] = Math.Sin(i * xOffset) * aplitude + yOffset);
      return sin;
    }

    public static double ValueByPosition(this int sampleCurrent, double sampleLow, double sampleHigh, double realLow, double realHigh) {
      return ((double)sampleCurrent).ValueByPosition(sampleLow, sampleHigh, realLow, realHigh);
    }
    public static double ValueByPosition(this double sampleCurrent, double sampleLow, double sampleHigh, double realLow, double realHigh) {
      return sampleCurrent.PositionRatio(sampleLow, sampleHigh) * (realHigh - realLow) + realLow;
    }
    /// <summary>
    /// Realtive position of value between low and high in percent: 0% = low - 100% = hihg
    /// </summary>
    /// <param name="current"></param>
    /// <param name="low"></param>
    /// <param name="high"></param>
    /// <returns></returns>
    public static double PositionRatio(this double current, double low, double high) {
      return (current - low) / (high - low);
    }
    public static double StDevP_(this double[] value, bool fcompensate = true) {
      double[] average = { 0.0, 0.0 };
      double stdev = 0;
      double mean;
      double variance;
      mean = 0; // adjust values

      if ((fcompensate) && (value.Length > 1000)) {
        int n = (int)(value.Length * 0.1);
        for (int i = 0; i <= (n - 1); i++) {
          mean = mean + value[i];
        }
        mean = mean / n;
      }

      for (int i = 1; i <= value.Length; i++) {
        average[i % 2] = average[(i + 1) % 2] + (value[i - 1] - mean - average[(i + 1) % 2]) / i;
        stdev += (value[i - 1] - mean - average[(i + 1) % 2]) * (value[i - 1] - mean - average[i % 2]);
      }

      mean = average[value.Length % 2];
      variance = stdev / (value.Length - 1);

      return Math.Sqrt(variance);
    }

    public static double StDevAL(this double[] x, bool fcompensate = true) {//, int n, ref double stddev, ref double mean, bool fcompensate) {
      int i;
      double v1 = 0;
      double v2 = 0;
      double variance;
      double sum = 0;
      double mean = 0;
      int n = x.Length;

      if (fcompensate) {
        for (i = 0; i <= (n - 1); i++) {
          sum = sum + x[i];
        }
        mean = sum / n;
      }

      //
      // Variance (using corrected two-pass algorithm)
      //
      if (n != 1) {
        v1 = 0; v2 = 0;
        for (i = 0; i <= n - 1; i++) {
          v1 = v1 + (x[i] - mean) * (x[i] - mean);
          v2 = v2 + (x[i] - mean);
        }

        v2 = v2 * v2 / n;
        variance = (v1 - v2) / (n - 1);
        if ((double)(variance) < (double)(0)) {
          variance = 0;
        }
        var stddev = Math.Sqrt(variance);
        return stddev;
      }
      return 0;
    }

    public static double[] Trima(this double[] inReal, int period, out int outBegIdx, out int outNBElement) {
      try {
        if (inReal.Length < period) {
          outBegIdx = 0;
          outNBElement = 0;
          return new double[0];
        }
        double[] outReal = new double[inReal.Count() - period + 1];
        //TicTacTec.TA.Library.Core.Sma(0, d.Count - 1, inReal, period, out outBegIdx, out outNBElement, outReal);
        TicTacTec.TA.Library.Core.Trima(0, inReal.Count() - 1, inReal, period, out outBegIdx, out outNBElement, outReal);
        return outReal;
      } catch (Exception exc) {
        Debug.WriteLine(exc);
        throw;
      }
    }

    public static SortedList<T, double> MovingAverage<T>(this SortedList<T, double> series, int period) {
      var result = new SortedList<T, double>();
      double total = 0;
      for (int i = 0; i < series.Count(); i++) {
        if (i >= period) {
          total -= series.Values[i - period];
        }
        total += series.Values[i];
        if (i >= period - 1) {
          double average = total / period;
          result.Add(series.Keys[i], average);
        }
      } return result;
    }
    public static SortedList<T, double> MovingAverage_<T>(this SortedList<T, double> series, int period) {
      var result = new SortedList<T, double>();
      for (int i = 0; i < series.Count(); i++) {
        if (i >= period - 1) {
          double total = 0;
          for (int x = i; x > (i - period); x--)
            total += series.Values[x];
          double average = total / period;
          result.Add(series.Keys[i], average);
        }
      }
      return result;
    }
    public static double[] Linear(double[] x, double[] y) {
      double[,] m = new double[x.Length, 2];
      for (int i = 0; i < x.Length; i++) {
        m[i, 0] = x[i];
        m[i, 1] = y[i];
      }
      int info, nvars;
      double[] c;
      alglib.linearmodel lm;
      alglib.lrreport lr;
      alglib.lrbuild(m, x.Length, 1, out info, out lm, out lr);
      alglib.lrunpack(lm, out c, out nvars);
      return new[] { c[1], c[0] };
    }

    public static void SetProperty<T>(this object o, string p, T v) {
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if (pi != null) pi.SetValue(o, v, new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if (fi == null) throw new NotImplementedException("Property " + p + " is not implemented in " + o.GetType().FullName + ".");
        fi.SetValue(o, v);
      }
    }


    public static void SetProperty(this object o, string p, object v) {
      var convert = new Func<object, Type, object>((valie, type) => {
        if (valie != null) {
          Type tThis = Nullable.GetUnderlyingType(type);
          if (tThis == null) tThis = type;
          valie = tThis.IsEnum ? Enum.Parse(tThis, v + "") : Convert.ChangeType(v, tThis, null);
        }
        return valie;
      });
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if (pi != null) pi.SetValue(o, v = convert(v, pi.PropertyType), new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if (fi == null) throw new NotImplementedException("Property " + p + " is not implemented in " + o.GetType().FullName + ".");
        fi.SetValue(o, convert(v, fi.FieldType));
      }
    }

    public static double Angle(this double tangent, int barMinutes, double divideBy = 1) {
      return Math.Atan(tangent / divideBy) * (180 / Math.PI) / barMinutes;
    }
    public static double Radians(this double angleInDegrees) { return angleInDegrees * Math.PI / 180; }

    public static double StDevRatio(this IList<double> values) {
      var stDev = values.StDev();
      var range = values.Max() - values.Min();
      return stDev / range;
    }
    public static double StDev<T>(this IList<T> values, Func<T, int, double> value) {
      return values.Select((v, i) => value(v, i)).ToArray().StDev();
    }
    public static double StDev<T>(this IList<T> values, Func<T, double> value) {
      return values.Select(v => value(v)).ToArray().StDev();
    }
    public static double StDev<T>(this ICollection<T> values, Func<T, double?> value) {
      return values.Where(v => value(v).HasValue).Select(v => value(v).Value).ToArray().StDev();
    }
    public static double StDev(this IList<double> values) {
      double avg, max, min;
      return values.StDev(out avg, out max, out min);
    }
    public static double StDev(this IList<double> values, out double avg) {
      double max, min;
      return values.StDev(out avg, out max, out min);
    }
    public static double StDev(this IList<double> values, out double max,out double min) {
      double avg;
      return values.StDev(out avg, out max, out min);
    }
    public static double StDev(this IList<double> values, out double avgOut, out double maxOut, out double minOut) {
      double ret = 0, avg = 0, max = double.MinValue, min = double.MaxValue;
      if (values.Count() > 0) {
        avg = values.Average(v => {
          if (max < v) max = v;
          if (min > v) min = v;
          return v;
        });
        double sum = values.Sum(d => (d - avg) * (d - avg));
        ret = Math.Sqrt(sum / (values.Count - 1));
      }
      avgOut = avg;
      maxOut = max;
      minOut = min;
      return ret;
    }


    public static double OpsiteCathetusByFegrees(this double adjacentCathetus, double angleInDegrees) {
      return adjacentCathetus * Math.Tan(angleInDegrees.Radians());
    }

    public static IEnumerable<double> AverageByStDev(this IList<double> values) {
      if (values.Count < 2) return values.DefaultIfEmpty(double.NaN);
      var avg = values.Average();
      var stDev = values.StDev();
      var r1 = avg - stDev;
      var r2 = avg + stDev;
      return values.Where(v => v.Between(r1, r2));
    }

    public static IEnumerable<double> AverageInRange(this IList<double> a, int high) {
      return a.AverageInRange(high, high - 1);
    }
    public static IEnumerable<double> AverageInRange(this IList<double> a, int high, int low) {
      double b = double.NaN, c = double.NaN;
      return a.Where(v => {
        if (double.IsNaN(b)) {
          b = a.AverageByIterations(high, false).Average();
          c = a.AverageByIterations(low).Average();
        }
        return v.Between(c, b);
      });
    }

    public static IList<double> AverageByIterations(this IList<double> values, double iterations, List<double> averagesOut = null) {
      return values.AverageByIterations(Math.Abs(iterations), iterations < 0, averagesOut);
    }
    public static IList<double> AverageByIterations(this IList<double> values, double iterations, bool low, List<double> averagesOut = null) {
      return values.AverageByIterations(low ? new Func<double, double, bool>((v, a) => v <= a) : new Func<double, double, bool>((v, a) => v >= a), iterations, averagesOut);
    }
    public static IList<double> AverageByIterations(this IList<double> values, Func<double, double, bool> compare, double iterations, List<double> averagesOut = null) {
      return values.AverageByIterations<double>(v => v, compare, iterations, averagesOut);
    }

    public static IList<T> AverageByIterations_<T>(this IList<T> values, Func<T, double> getValue, Func<T, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Add(avg);
      if (values.Count == 0) return values.ToArray();
      for (int i = 0; i < iterations; i++) {
        var vs = values.Where(r => compare(r, avg)).ToArray();
        if (vs.Length == 0) break;
        values = vs;
        avg = values.Average(getValue);
        if (averagesOut != null) averagesOut.Insert(0, avg);
      }
      return values;
    }
    public static IList<T> AverageByIterations<T>(this IList<T> values, Func<T, double> getValue, Func<double, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      return values.Distinct().Count() < 2 || iterations == 0 ? values : values.Where(r => compare(getValue(r), avg)).ToArray().AverageByIterations(getValue, compare, iterations - 1, averagesOut);
    }

    public static IList<T> AverageByIterations<T>(this IList<T> values, Func<T, double> getValue, Func<T, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      return values.Count < 2 || iterations == 0 ? values : values.Where(r => compare(r, avg)).ToArray().AverageByIterations(getValue, compare, iterations - 1, averagesOut);
    }

    public static IList<T> AverageByPercentage<T>(this IList<T> values, Func<T, double> getValue, Func<double, double, bool> compare, double iterations, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      var countMax = values.Count * iterations;
      while (values.Count > countMax) {
        var vs = values.Where(v => compare(getValue(v), avg)).ToArray();
        if (vs.Count() == 0) break;
        avg = vs.Average(getValue);
        if (averagesOut != null) averagesOut.Insert(0, avg);
        values = vs;
        if (values.Count == 1) break;
      }
      return values;
    }
    public static IList<T> AverageByCount<T>(this IList<T> values, Func<T, double> getValue, Func<double, double, bool> compare, double countMin, List<double> averagesOut = null) {
      var avg = values.DefaultIfEmpty().Average(getValue);
      if (averagesOut != null) averagesOut.Insert(0, avg);
      while (values.Count > countMin) {
        var vs = values.Where(v => compare(getValue(v), avg)).ToArray();
        if (vs.Count() == 0) break;
        avg = vs.Average(getValue);
        if (averagesOut != null) averagesOut.Insert(0, avg);
        values = vs;
        if (values.Count == 1) break;
      }
      return values;
    }

    public static double Average(this IEnumerable<double> values, Func<double> defaultValue) {
      return values.Count() == 0 ? defaultValue() : values.Average();
    }

    public static int Floor(this double d) { return (int)Math.Floor(d); }
    public static int Ceiling(this double d) { return (int)Math.Ceiling(d); }
    public static int ToInt(this double d, bool useCeiling) {
      return (int)(useCeiling ? Math.Ceiling(d) : Math.Floor(d));
    }
    public static int ToInt(this double d) { return (int)Math.Round(d, 0); }
    //public static bool IsMax(this DateTime d) { return d == DateTime.MaxValue; }
    //public static bool IsMin(this DateTime d) { return d == DateTime.MinValue; }
    public enum RoundTo { Second, Minute, Hour, Day,DayFloor, Month, MonthEnd, Week }
    public static DateTime Round(this DateTime d, RoundTo rt) {
      DateTime dtRounded = new DateTime();
      switch (rt) {
        case RoundTo.Second:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second);
          if (d.Millisecond >= 500) dtRounded = dtRounded.AddSeconds(1);
          break;
        case RoundTo.Minute:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
          if (d.Second >= 30) dtRounded = dtRounded.AddMinutes(1);
          break;
        case RoundTo.Hour:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0);
          if (d.Minute >= 30) dtRounded = dtRounded.AddHours(1);
          break;
        case RoundTo.DayFloor:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0);
          break;
        case RoundTo.Day:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0);
          if (d.Hour >= 12) dtRounded = dtRounded.AddDays(1);
          break;
        case RoundTo.Month:
          dtRounded = new DateTime(d.Year, d.Month, 1, 0, 0, 0);
          break;
        case RoundTo.MonthEnd:
          dtRounded = new DateTime(d.Year, d.Month, 1, 0, 0, 0).AddMonths(1).AddDays(-1);
          break;
        case RoundTo.Week:
          dtRounded = d.AddDays(-(int)d.DayOfWeek).Date;
          break;
      }
      return dtRounded;
    }
    public static DateTimeOffset Round(this DateTimeOffset dt) { return new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute,0, dt.Offset); }
    public static DateTimeOffset Round_(this DateTimeOffset dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static DateTimeOffset Round(this DateTimeOffset dt, int period) {
      dt = dt.Round();
      return dt.AddMinutes(dt.Minute / period * period - dt.Minute);
    }

    public static DateTime Round(this DateTime dt) { return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0); }
    public static DateTime Round_(this DateTime dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static DateTime Round(this DateTime dt, int period) {
      dt = dt.Round();
      return dt.AddMinutes(dt.Minute / period * period - dt.Minute);
    }

    #region Between
    public static bool Between(this int value, double d1, double d2) {
      return Math.Min(d1, d2) <= value && value <= Math.Max(d1, d2);
    }
    public static bool Between(this double value, double[] dd) {
      return value.Between(dd[0], dd[1]);
    }
    public static bool Between(this double value, double d1, double d2) {
      return d1 < d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this DateTime value, DateTime d1, DateTime d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this DateTime value, DateTimeOffset d1, DateTimeOffset d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this DateTimeOffset value, DateTimeOffset d1, DateTimeOffset d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this TimeSpan value, TimeSpan d1, TimeSpan d2) {
      return d1 < d2 ? d1 <= value && value <= d2 : d1 <= value || value <= d2;
    }
    #endregion
  }
}
