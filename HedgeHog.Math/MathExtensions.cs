using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Concurrent;
using HedgeHog;
using System.Collections;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reflection;

namespace HedgeHog {
  public static class RelativeStDevStore {
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
  public static partial class MathExtensions {
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

    public static IEnumerable<double> Distances(this IEnumerable<double> source, Action<double, double> onScan = null) {
      return source
        .Scan(new { prev = 0.0, next = 0.0 }, (prev, next) => new { prev = prev.next, next })
        .Skip(1)
        .Scan(0.0, (d, x) => {
          if(onScan != null)
            onScan(x.prev, x.next);
          return d + x.prev.Abs(x.next);
        });
    }
    public static IEnumerable<Tuple<T,double>> Distances<T>(this IEnumerable<T> source,Func<T,double> map, Action<double, double> onScan = null) {
      return source
        .Scan(new {t=default(T),  prev = 0.0, next = 0.0 }, (prev, next) => new {t=next, prev = prev.next, next = map(next) })
        .Skip(1)
        .Scan(Tuple.Create(default(T), 0.0), (t, x) => {
          if(onScan != null)
            onScan(x.prev, x.next);
          return Tuple.Create(x.t, t.Item2 + x.prev.Abs(x.next));
        });
    }
    public static IEnumerable<double[]> PrevNext(this IList<double> bars) {
      return bars.Take(bars.Count - 1).Zip(bars.Skip(1), (r1, r2) => new[] { r1, r2 });
    }

    public static ParallelQuery<double> Mirror(this ParallelQuery<double> prices, double linePrice) {
      return prices.Select(p => linePrice * 2 - p);
    }
    public static IEnumerable<double> Mirror(this IList<double> prices, double linePrice) {
      return prices.Select(p => linePrice * 2 - p);
    }
    public static int GetFftHarmonicsByRatesCountAndRatio(int frameLength, double fftHarmsRatio) {
      return (frameLength * fftHarmsRatio / 100).ToInt();
    }
    /// <summary>
    /// Interpolate values using FFT
    /// </summary>
    /// <param name="prices"></param>
    /// <param name="lastHarmonicRatioIndex">Ranges from 0 to 100. 100 being 1:1</param>
    /// <returns></returns>
    public static IList<double> Fft(this IList<double> prices, double lastHarmonicRatioIndex) {
      var lastHarmonic = GetFftHarmonicsByRatesCountAndRatio(prices.Count, 0.1.Max(lastHarmonicRatioIndex));
      Func<int, IEnumerable<alglib.complex>> repeat = (count) => { return Enumerable.Repeat(new alglib.complex(0), count); };
      var mirror = prices.Mirror(prices.Last()).Reverse().ToArray();
      mirror = prices.Concat(mirror).ToArray();
      var mirror2 = prices.Mirror(prices[0]).Reverse().ToArray();
      mirror = mirror2.Concat(mirror).ToArray();
      var bins = mirror.Fft0();
      var bins1 = bins.Take(lastHarmonic).Concat(repeat(bins.Count - lastHarmonic)).ToArray();
      double[] ifft; alglib.fftr1dinv(bins1, out ifft);
      return ifft.CopyToArray(prices.Count, prices.Count);
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
      if(reversed) ratesFft = ratesFft.Reverse();
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
    public static IList<alglib.complex> Fft0(this IEnumerable<double> values) {
      alglib.complex[] bins;
      alglib.fftr1d(values.SafeArray(), out bins);
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
        if(i == 0) return b;
        if(i < harmonic) return c;
        if(i.Between(harmonic, harmonic + range - 1)) return b;
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
    public static List<T> SafeList<T>(this IEnumerable<T> values) {
      return values as List<T> ?? values.ToList();
    }
    /// <summary>
    /// Calculate overlapped area for two areas
    /// </summary>
    /// <param name="low1"></param>
    /// <param name="high1"></param>
    /// <param name="low2"></param>
    /// <param name="high2"></param>
    /// <returns>Persentage of overlapped area relative to average height of both areas</returns>
    public static double OverlapRatio(double low1, double high1, double low2, double high2) {
      var heightAvg = high1.Abs(low1).Avg(high2.Abs(low2));
      var overlap = high2.Max(low2).Min(high1.Max(low1)) - low1.Min(high1).Max(low2.Min(high2));
      return overlap / heightAvg;
    }

    /// <summary>
    /// Ratio between Max(a,b)/Min(a,b)
    /// </summary>
    /// <param name="v"></param>
    /// <param name="other"></param>
    /// <returns></returns>
    public static double Ratio(this double v, double other) {
      return v.Abs() > other.Abs() ? v / other : other / v;
    }
    public static double Ratio(this int v, double other) {
      return other.Ratio(v);
    }
    public static double Percentage<T>(this int v, double other) {
      return other.Percentage(v);
    }
    /// <summary>
    /// (one - other) / Math.Max(one, other);
    /// </summary>
    /// <param name="v"></param>
    /// <param name="other"></param>
    /// <returns></returns>
    public static double Percentage(this double v, double other) {
      var max = Math.Max(Math.Abs(v), Math.Abs(other));
      var min = Math.Min(Math.Abs(v), Math.Abs(other));
      return (v - other).Abs() / max;
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
    public static ILookup<bool, TBar> Fractals<TBar>(this IList<TBar> rates1, int fractalLength, Func<TBar, double> priceHigh, Func<TBar, double> priceLow, bool includeTails = false) {
      var rates = rates1.Select(r => new { r, h = priceHigh(r), l = priceLow(r) }).ToArray();
      var indexMiddle = fractalLength / 2;
      var zipped = rates.Zip(rates.Skip(1), (f, s) => new[] { f, s }.ToList());
      for(var i = 2; i < fractalLength; i++)
        zipped = zipped.Zip(rates.Skip(i), (z, v) => { z.Add(v); return z; });
      return zipped.AsParallel()
        .Where(z => includeTails || z.Count == fractalLength)
        .Select(z => new { max = z.Max(a => a.h), min = z.Min(a => a.l), middle = z[indexMiddle] })
        .Select(a => new { rate = a.middle, isUp = a.max == a.middle.h, IsDpwm = a.middle.l == a.min })
        .Where(a => a.isUp || a.IsDpwm)
        .ToLookup(a => a.isUp, a => a.rate.r);
    }

    public static IList<double> Wavelette(this IList<double> values) {
      var sign = Math.Sign(values[1] - values[0]);
      var wavelette = new List<double>(values.Take(2));
      var prev = values[1];
      foreach(var curr in values.Skip(2)) {
        var s = Math.Sign(curr - prev);
        if(s == -sign) break;
        if(sign == 0) sign = s;
        wavelette.Add(curr);
        prev = curr;
      }
      return wavelette;
    }
    public static IList<double> CrossesInMiddle(this IEnumerable<double> valuesIn, double value, out int index) {
      var values = valuesIn.Select(v1 => v1 - value);
      return CrossesInMiddle(values, out index);
    }
    public static IList<double> CrossesInMiddle(this IEnumerable<double> values1, IEnumerable<double> values2) {
      int index;
      return values1.CrossesInMiddle(values2, out index);
    }
    public static IList<double> CrossesInMiddle(this IEnumerable<double> values1, IEnumerable<double> values2, out int index) {
      var values = values1.Zip(values2, (v1, v2) => v1 - v2);
      return CrossesInMiddle(values, out index);
    }

    private static IList<double> CrossesInMiddle(IEnumerable<double> values) {
      int index;
      return values.CrossesInMiddle(out index);
    }
    private static IList<double> CrossesInMiddle(this IEnumerable<double> values, out int index) {
      var last = new Box<double>(values.First());
      var counts = "".Select(s => new { index = new Box<int>(0), last = new Box<double>(0) }).ToList();
      counts.Add(new { index = new Box<int>(0), last });
      Func<double, double, double>[] comp = new[] { Math.Min, (Func<double, double, double>)null, Math.Max };
      index = 0;
      foreach(var v in values.Skip(1)) {
        index++;
        var sign = Math.Sign(v);
        if(sign != 0) {
          if(sign == Math.Sign(last.Value)) {
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
      foreach(var v in values.Skip(1)) {
        var sign = Math.Sign(v);
        if(sign != 0) {
          if(sign == Math.Sign(last.Value)) {
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
    public static IList<double> CrossesSmoothed(this IList<double> sin, IList<double> cos) {
      var zip = sin.Zip(cos, (s, c) => s.SignUp(c))
        .Select((sign, i) => new { sign, i })
        .DistinctUntilChanged(x => x.sign)
        .Select(x => x.i)
        .Concat(new[] { sin.Count })
        .Select((c, i) => new { c, i })
        .ToArray();
      var zip2 = zip.Zip(zip.Skip(1), (p, n) => new { start = p.i, count = n.c - p.c, step = 1.0 / (n.c - p.c) }).ToArray();
      var zip3 = zip2.SelectMany(z => Enumerable.Range(0, z.count).Select(i => z.start + z.step * i)).ToArray();
      return zip3;
    }


    public static IEnumerable<Tuple<T, T>> Mash<T>(this IList<T> list) {
      return list.Zip(list.Skip(1), (f, s) => new Tuple<T, T>(f, s));
    }
    public static IEnumerable<Tuple<T, T, int>> MashWithIndex<T>(this IList<T> list) {
      var index = 0;
      return list.Zip(list.Skip(1), (f, s) => new Tuple<T, T, int>(f, s, index++));
    }
    public static IEnumerable<T> Crosses<T>(this IList<T> list, IList<T> signal, Func<T, double> getValue) {
      Func<T, T, double> sign = (v1, v2) => Math.Sign(getValue(v1) - getValue(v2));
      return list.Mash()
        .Zip(signal, (m, s) => new { signFirst = sign(m.Item1, s), signSecond = sign(m.Item2, s), first = m.Item1 })
        .Where(a => a.signFirst != a.signSecond)
        .Select(a => a.first);
    }
    public static IEnumerable<T> Crosses<T>(this IList<T> list, double signal, Func<T, double> getValue) {
      if(double.IsNaN(signal)) return new T[0];
      return list.Crosses(Enumerable.Repeat(signal, list.Count).ToArray(), getValue);
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

    public static IEnumerable<Tuple<double, int>> Crosses3(this IList<double> list, IList<double> signal) {
      var zipped = list.Zip(signal, (l, s) => new { l, s, sign = Math.Sign(l - s) }).ToArray();
      return zipped
        .Zip(zipped.Skip(1), (z1, z2) => new { z1, z2 })
        .Select((z, i) => new { z, i })
        .Where(a => a.z.z1.sign != a.z.z2.sign)
        .Select(a => new Tuple<double, int>(a.z.z1.s, a.i));
    }

    public static IEnumerable<Tuple<double, int>> CrossesWithIndex(this IList<double> list, IList<double> signal) {
      Func<double, double, int> sign = (d1, d2) => { var s = Math.Sign(d1 - d2); return s >= 0 ? 1 : -1; };
      return list.MashWithIndex()
        .Zip(signal, (m, s) => new { signFirst = sign(m.Item1, s), signSecond = sign(m.Item2, s), first = m.Item1, index = m.Item3 })
        .Where(a => a.signFirst != a.signSecond)
        .Select(a => new Tuple<double, int>(a.first, a.index));
    }

    public static double CrossesAverageRatio(this IList<double> rates, double step, int averageIterations) {
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

    public static double[] Sin(int sinLength, int waveLength, double aplitude, double yOffset, int wavesCount) {
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

      if((fcompensate) && (value.Length > 1000)) {
        int n = (int)(value.Length * 0.1);
        for(int i = 0; i <= (n - 1); i++) {
          mean = mean + value[i];
        }
        mean = mean / n;
      }

      for(int i = 1; i <= value.Length; i++) {
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

      if(fcompensate) {
        for(i = 0; i <= (n - 1); i++) {
          sum = sum + x[i];
        }
        mean = sum / n;
      }

      //
      // Variance (using corrected two-pass algorithm)
      //
      if(n != 1) {
        v1 = 0; v2 = 0;
        for(i = 0; i <= n - 1; i++) {
          v1 = v1 + (x[i] - mean) * (x[i] - mean);
          v2 = v2 + (x[i] - mean);
        }

        v2 = v2 * v2 / n;
        variance = (v1 - v2) / (n - 1);
        if((double)(variance) < (double)(0)) {
          variance = 0;
        }
        var stddev = Math.Sqrt(variance);
        return stddev;
      }
      return 0;
    }

    //public static double[] Trima(this double[] inReal, int period, out int outBegIdx, out int outNBElement) {
    //  try {
    //    if (inReal.Length < period) {
    //      outBegIdx = 0;
    //      outNBElement = 0;
    //      return new double[0];
    //    }
    //    double[] outReal = new double[inReal.Count() - period + 1];
    //    //TicTacTec.TA.Library.Core.Sma(0, d.Count - 1, inReal, period, out outBegIdx, out outNBElement, outReal);
    //    TicTacTec.TA.Library.Core.Trima(0, inReal.Count() - 1, inReal, period, out outBegIdx, out outNBElement, outReal);
    //    return outReal;
    //  } catch (Exception exc) {
    //    Debug.WriteLine(exc);
    //    throw;
    //  }
    //}

    public static SortedList<T, double> MovingAverage<T>(this SortedList<T, double> series, int period) {
      var result = new SortedList<T, double>();
      double total = 0;
      for(int i = 0; i < series.Count(); i++) {
        if(i >= period) {
          total -= series.Values[i - period];
        }
        total += series.Values[i];
        if(i >= period - 1) {
          double average = total / period;
          result.Add(series.Keys[i], average);
        }
      } return result;
    }
    public static SortedList<T, double> MovingAverage_<T>(this SortedList<T, double> series, int period) {
      var result = new SortedList<T, double>();
      for(int i = 0; i < series.Count(); i++) {
        if(i >= period - 1) {
          double total = 0;
          for(int x = i; x > (i - period); x--)
            total += series.Values[x];
          double average = total / period;
          result.Add(series.Keys[i], average);
        }
      }
      return result;
    }
    public static double[] Linear(double[] x, double[] y, out alglib.lrreport lr) {
      double[,] m = new double[x.Length, 2];
      for(int i = 0; i < x.Length; i++) {
        m[i, 0] = x[i];
        m[i, 1] = y[i];
      }
      int info, nvars;
      double[] c;
      alglib.linearmodel lm;
      //alglib.lrreport lr;
      alglib.lrbuild(m, x.Length, 1, out info, out lm, out lr);
      alglib.lrunpack(lm, out c, out nvars);
      return new[] { c[1], c[0] };
    }

    public static void SetProperty<T>(this object o, string p, T v) {
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if(pi != null) pi.SetValue(o, v, new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if(fi == null) throw new NotImplementedException("Property " + p + " is not implemented in " + o.GetType().FullName + ".");
        fi.SetValue(o, v);
      }
    }


    public static void SetProperty(this object o, string p, object v) {
      if(o == null)
        throw new NullReferenceException(new { o, p, v } + "");
      o.SetProperty(p, v, pi => pi.GetSetMethod() != null || pi.GetSetMethod(true) != null);
    }
    public static void SetProperty(this object o, string p, object v, Func<PropertyInfo, bool> propertyPredicate = null) {
      var convert = new Func<object, Type, object>((value, type) => {
        if(value != null) {
          Type tThis = Nullable.GetUnderlyingType(type);
          var isNullable = true;
          if(tThis == null) {
            tThis = type;
            isNullable = false;
          }
          if(tThis.IsEnum)
            try {
              return Enum.Parse(tThis, v + "", true);
            } catch(Exception exc) {
              throw new ArgumentException(new { property = p } + "", exc);
            }
          return string.IsNullOrWhiteSpace((v ?? "") + "") && isNullable ? null : Convert.ChangeType(v, tThis, null);
        }
        return value;
      });
      var t = o.GetType();
      var pi = t.GetProperty(p);
      if(propertyPredicate != null) {
        if(pi == null)
          throw new MissingMemberException(t.Name, p);
        if(!propertyPredicate(pi))
          return;
      }
      if(pi != null) pi.SetValue(o, v = convert(v, pi.PropertyType), new object[] { });
      else {
        System.Reflection.FieldInfo fi = o.GetType().GetField(p);
        if(fi == null) throw new MissingMemberException(t.Name, p);
        fi.SetValue(o, convert(v, fi.FieldType));
      }
    }

    public static double Angle(this double tangent, double barMinutes, double divideBy = 1) {
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
    public static double StDev(this IList<double> values, out double max, out double min) {
      double avg;
      return values.StDev(out avg, out max, out min);
    }
    public static double StDev(this IList<double> values, out double avgOut, out double maxOut, out double minOut) {
      double ret = 0, avg = 0, max = double.MinValue, min = double.MaxValue;
      if(values.Count > 0) {
        avg = values.Average(v => {
          if(max < v) max = v;
          if(min > v) min = v;
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
    public static double StandardDeviation(this IEnumerable<double> valueList) {
      double avg, max, min;
      return valueList.StandardDeviation(out avg, out max, out min);
    }
    public static double StandardDeviation(this IEnumerable<double> valueList, out double avg) {
      double max, min;
      return valueList.StandardDeviation(out avg, out max, out min);
    }
    public static double StandardDeviation(this IEnumerable<double> valueList, out double max, out double min) {
      double avg;
      return valueList.StandardDeviation(out avg, out max, out min);
    }
    public static double StandardDeviation(this IEnumerable<double> valueList, out double avg, out double max, out double min) {
      double M = 0.0;
      double S = 0.0;
      double summ = 0.0;
      int k = 1;
      max = double.MinValue;
      min = double.MaxValue;
      foreach(double value in valueList) {
        double tmpM = M;
        M += (value - tmpM) / k;
        S += (value - tmpM) * (value - M);
        summ += value;
        k++;
        if(max < value)
          max = value;
        if(min > value)
          min = value;
      }
      avg = summ / k;
      return Math.Sqrt(S / (k - 2));
    }
    public static double StandardDeviation<T>(this IEnumerable<T> valueList, Func<T, double> get) {
      double max, min;
      return valueList.StandardDeviation(get, out max, out min);
    }
    public static double StandardDeviation<T>(this IEnumerable<T> valueList, Func<T, double> get, out double max, out double min) {
      double M = 0.0;
      double S = 0.0;
      int k = 1;
      max = double.MinValue;
      min = double.MaxValue;
      foreach(T v in valueList) {
        var value = get(v);
        double tmpM = M;
        M += (value - tmpM) / k;
        S += (value - tmpM) * (value - M);
        k++;
        if(max < value) max = value;
        if(min > value) min = value;
      }
      return Math.Sqrt(S / (k - 2));
    }
    public static double RelativeStandardDeviationSmoothed(this IEnumerable<double> dbls) {
      double avg, std;
      var rsd = dbls.RelativeStandardDeviation(out std, out avg);
      while(rsd > 1) {
        rsd = dbls.Where(d => d < std).RelativeStandardDeviation(out std,out avg);
      }
      return rsd;
    }
    public static double RelativeStandardDeviationSmoothed(this IEnumerable<double> dbls,int smoothCount) {
      double avg, std;
      var rsd = dbls.RelativeStandardDeviation(out std, out avg);
      while(--smoothCount >= 0) {
        rsd = dbls.Where(d => d < std).RelativeStandardDeviation(out std, out avg);
      }
      return rsd;
    }
    public static IEnumerable<double> SmoothedByRsd(this IEnumerable<double> dbls) {
      double avg, std;
      var rsd = dbls.RelativeStandardDeviation(out std, out avg);
      return dbls.Where(d => d <= std);
    }
    public static double RelativeStandardDeviation(this IEnumerable<double> dbls) {
      double avg, std;
      return dbls.RelativeStandardDeviation(out std, out avg);
    }
    public static double RelativeStandardDeviation(this IEnumerable<double> dbls,out double std, out double avg) {
      std = dbls.StandardDeviation(out avg);
      return std / avg;
    }
    public static double RelativeToHeightStandardDeviation(this IList<double> dbls) {
      double min = double.NaN, max = double.NaN;
      var std = dbls.StandardDeviation(out max, out min);
      return std / (max - min);
    }
    public static double AverageByStandardDeviation(this IList<double> dbls) {
      var min = dbls.Min();
      dbls = dbls.Select(d => d - min).ToArray();
      var avg = dbls.Average();
      var std = dbls.StandardDeviation();
      return std + avg;
    }
    public static double AverageByAverageUp(this IList<double> dbls) {
      var avg0 = dbls.DefaultIfEmpty(double.NaN).Average();
      var avg1 = dbls.Where(d => d <= avg0).DefaultIfEmpty(avg0).Average();
      return dbls.Where(d => d >= avg1).DefaultIfEmpty(avg1).Average();
    }
    public static IList<T> AverageByAverageUp<T>(this IList<T> source,Func<T,double> projector) {
      var dbls = source.Select(projector).ToArray();
      var avg0 = dbls.DefaultIfEmpty(double.NaN).Average();
      var avg1 = dbls.Where(d => d <= avg0).DefaultIfEmpty(avg0).Average();
      return source.Where(d => projector(d) >= avg1).ToArray();
    }
    public static T[] CopyToArray<T>(this IList<T> values, int count) {
      return values.SafeArray().CopyToArray(count);
    }
    public static T[] CopyToArray<T>(this T[] values, int count) {
      var array = new T[count];
      Array.Copy(values, array, array.Length);
      return array;
    }
    public static T[] CopyToArray<T>(this IList<T> values, int start, int count) {
      return values.SafeArray().CopyToArray(start, count);
    }
    public static T[] CopyToArray<T>(this T[] values, int start, int count) {
      var array = new T[count];
      Array.Copy(values, start, array, 0, array.Length);
      return array;
    }
    public static double Offset(double slope, double pointSize) {
      return Math.Sin(Math.PI / 2 - slope / pointSize);
    }
    private static bool IsNaN(this double d) { return double.IsNaN(d); }
    public class Extream<T> {
      public T Element { get; set; }
      public T Element0 { get; set; }
      public double Slope { get; set; }
      public int Index { get; set; }
      internal Extream() { }
      public Extream(T element, double slope, int index) {
        Element = element;
        Slope = slope;
        Index = index;
      }
      public override string ToString() {
        return new { Index = Index, Slope = Slope, Element0 = Element0 } + "";
      }
    }
    public static IList<Extream<T>> Extreams<T>(this IEnumerable<T> input, Func<T, double> value, int range, Func<Extream<T>, bool> mustContinue) {
      var extreams = Enumerable.Repeat(new Extream<T>(), 0).ToList();
      var pp = Partitioner.Create(input.Integral(range).Select((rates, i) => new { rates, i }).ToArray(), true);
      var datas = pp
        .AsParallel().AsOrdered()
        .Select(d => new Extream<T>(d.rates[0], d.rates.Regress(1, value).LineSlope(), d.i))
        .TakeWhile(mustContinue)
        .OrderBy(d => d.Index).ToArray();
      datas
        .SkipWhile(d => d.Slope.IsNaN())
        .TakeWhile(d => !d.Slope.IsNaN())
        .Aggregate((p, n) => {
          if (n.Slope.Sign() != p.Slope.Sign()) extreams.Add(p);
          return n;
        });
      return extreams.OrderBy(d => d.Index).ToArray();
    }
    public static IList<Extream<T>> Extreams<T>(this IEnumerable<T> input, Func<T, double> value, int range, Action<string> error = null) {
      input = input.SafeArray();
      var extreams = Enumerable.Repeat(new { i = 0, slope = 0.0, rate = default(T) }, 0).ToList();
      var datas = input.Integral(range)
        .Select((rates, i) => new { rates, i })
        .AsParallel()
        .Select(d => new { d.i, slope = d.rates.Regress(1, value).LineSlope(), rate = d.rates[0] })
        .OrderBy(d => d.i).ToArray();
      datas
        .SkipWhile(d => d.slope.IsNaN())
        .TakeWhile(d => !d.slope.IsNaN())
        .DefaultIfEmpty()
        .Aggregate((p, n) => {
          if (n.slope.Sign() != p.slope.Sign()) extreams.Add(p);
          return n;
        });
      Func<Extream<T>, Extream<T>> fill = ext => {
        Func<Func<IEnumerable<T>, Func<T, double>, IOrderedEnumerable<T>>> s = () => ext.Slope > 0 ? (Func<IEnumerable<T>, Func<T, double>, IOrderedEnumerable<T>>)Enumerable.OrderByDescending : Enumerable.OrderBy;
        ext.Element0 = s()(input.SafeArray().CopyToArray(ext.Index, range), value).First();
        return ext;
      };
      return extreams.Where(d => d != null).OrderBy(d => d.i).Select(d => fill(new Extream<T>(d.rate, d.slope, d.i))).ToArray();
    }

    public static IEnumerable<Tuple<int, U, double>> Extreams<T,U>(this IEnumerable<T> values, int waveWidth, Func<T, double> value, Func<T, U> date) {
      return values
        .Select((rate, i) => new { y = value(rate), x = date(rate), i })
        .Where(x => !x.y.IsNaN())
        .Buffer(waveWidth, 1)
        .Where(chank => chank.Count == waveWidth)
        .Select(chunk => {
          var slope = chunk.LinearSlope(r => r.y);
          var list = chunk.SafeList();
          list.SortByLambda(c => c.y);
          var extream = slope > 0 ? list.Last() : list.First();
          return new { slope, extream.x, i = extream.i };
        })
        .DistinctLastUntilChanged(a => a.slope.SignUp())
        .Select(r => Tuple.Create(r.i, r.x, r.slope));//.SkipLast(1);
    }
    /// <summary>
    /// Try not to materialize it.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="rates"></param>
    /// <param name="chunksLength"></param>
    /// <returns></returns>
    public static IEnumerable<IList<T>> Integral<T>(this IEnumerable<T> ratesOriginal, int chunksLength) {
      //return ratesOriginal.Buffer(chunksLength, 1).TakeWhile(b => b.Count == chunksLength);
      var rates = ratesOriginal.SafeArray();
      return Enumerable.Range(0, Math.Max(rates.Length - chunksLength, 1))
        .Select(start => rates.CopyToArray(start, Math.Min(chunksLength, rates.Count())));
    }
    public static IEnumerable<U> Integral<T, U>(this IEnumerable<T> ratesOriginal, int chunksLength, Func<IList<T>, U> func) {
      return ratesOriginal.Integral(chunksLength).Select(func);
    }
    /// <summary>
    /// Returns Value - avg
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    static IEnumerable<double> Normalize<T>(this IList<T> values, Func<T, double> value, out double level) {
      var l = level = (values.Max(value) + values.Min(value)) / 2;
      return values.Select(v => value(v) - l);
    }
    static IEnumerable<double> Normalize(this IList<double> values, out double level) {
      var l = level = (values.Max() + values.Min()) / 2;
      return values.Select(v => v - l);
    }
    public static double Rsd(this IList<double> values) {
      var stDev = values.StDev();
      var height = values.Height();
      return stDev / height;
    }
    public static double StDevByRegressoin(this IList<double> values) {
      return values.StDevByRegressoin(d => { });
    }
    public static double StDevByRegressoin(this IList<double> values, Action<double> callCC) {
      return values.StDevByRegressoin(null, callCC);
    }
    public static double StDevByRegressoin(this IEnumerable<double> values, int valuesCount, double[] coeffs) {
      if (coeffs == null) throw new ArgumentNullException("coeffs");
      var line = new double[valuesCount];
      coeffs.SetRegressionPrice(0, valuesCount, (i, v) => line[i] = v);
      return line.Zip(values, (l, v) => v - l).StandardDeviation();
    }
    public static double StDevByRegressoin(this IList<double> values, double[] coeffs) {
      if (coeffs == null || coeffs.Length == 0) coeffs = values.Linear();
      var line = new double[values.Count];
      coeffs.SetRegressionPrice(0, values.Count, (i, v) => line[i] = v);
      return line.Zip(values, (l, v) => v - l).StandardDeviation();
    }
    public static double StDevByRegressoin(this IList<double> values, double[] coeffs, Action<double> callCC) {
      if (coeffs == null || coeffs.Length == 0) coeffs = values.Linear();
      var line = new double[values.Count];
      coeffs.SetRegressionPrice(0, values.Count, (i, v) => line[i] = v);
      return line.Zip(values, (l, v) => v - l).Do(callCC).StandardDeviation();
    }
    public static double HeightByRegressoin(this IList<double> values) {
      return values.HeightByRegressoin(values.Linear());
    }
    public static double HeightByRegressoin(this IList<double> values, double[] coeffs) {
      if (coeffs == null || coeffs.Length == 0) coeffs = values.Linear();
      var line = new double[values.Count];
      coeffs.SetRegressionPrice(0, values.Count, (i, v) => line[i] = v);
      double max = double.MinValue, min = double.MaxValue;
      var n = values.Count;
      for (var i = 0; i < n; i++) {
        var v = line[i] - values[i];
        if (max < v) max = v;
        if (min > v) min = v;
      }
      return max - min;
    }
    public static double HeightByRegressoin2(this IList<double> values, double[] coeffs = null) {
      if (coeffs == null || coeffs.Length == 0) coeffs = values.Linear();
      var line = new double[values.Count];
      coeffs.SetRegressionPrice(0, values.Count, (i, v) => line[i] = v);
      var diffs = line.Zip(values, (l, v) => l - v).ToArray().GroupBy(a => a.Sign());
      return diffs.Select(g => g.Select(v => v)).SelectMany(a => a).ToArray().StDev() * 4;
    }
    static IEnumerable<int> IteratonSequence(int start, int end) {
      return IteratonSequence(start, end, NestStep);
    }
    private static IEnumerable<int> IteratonSequence(int start, int end, Func<int, int> nextStep) {
      for (var i = start; i < end; i += nextStep(i))
        yield return i;
    }

    public static int NestStep(int rc) {
      return (rc / 100.0).ToInt() + 1;
    }
    /// <summary>
    /// Scan values for Regression's slope sign changes,
    /// then return lengths of start and end of every wave.
    /// </summary>
    /// <param name="values"></param>
    /// <param name="startIndex"></param>
    /// <returns>One or two elements with slopes length</returns>
    public static IEnumerable<Tuple<int, int, int>> ScanTillFlat(IList<double> values, int startIndex) {
      return (
        from length in IteratonSequence(startIndex, values.Count)
        select new { length, slopeSign = values.CopyToArray(0, length).Regress(1).LineSlope().Sign() }
      ).DistinctUntilChanged(d => d.slopeSign)
      .Take(2)
      .Integral(2, a => Tuple.Create(a[0].length, a.Last().length, a.Last().slopeSign));
    }
    public static IEnumerable<T> ScanTillFlat<T>(IList<double> values, int startIndex, Func<int, int, T> lengthAndSlope) {
      return (
        from length in IteratonSequence(startIndex, values.Count)
        select new { length, slopeSign = values.CopyToArray(0, length).Regress(1).LineSlope().Sign() }
      ).DistinctUntilChanged(d => d.slopeSign)
      .Take(2)
      .Select(a => lengthAndSlope(a.length, a.slopeSign));
    }
    public static double RsdNormalized(this IList<double> values) {
      double level;
      var norm = values.Normalize(out level).ToArray();
      return norm.Rsd();
    }
    public static double RsdNormalized<T>(this IList<T> values, Func<T, double> value) {
      double level;
      var norm = values.Normalize(value, out level).ToArray();
      return norm.Rsd();
    }

    public static double RsdIntegral(this IList<double> values, int period) {
      return values.Integral(period).Select(i => i.Rsd()).Average();
    }

    static double Height(this IList<double> rates) {
      double min, max;
      return rates.Height(out min, out max);
    }
    static double Height(this IList<double> rates, out double min, out double max) {
      if (!rates.Any())
        return min = max = double.NaN;
      min = rates.Min();
      max = rates.Max();
      return max - min;
    }

    public static double OpsiteCathetusByFegrees(this double adjacentCathetus, double angleInDegrees) {
      return adjacentCathetus * Math.Tan(angleInDegrees.Radians());
    }

    public static IEnumerable<double> AverageByStDev(this IList<double> values) {
      if(values.Count < 2)
        return values.DefaultIfEmpty(double.NaN);
      var avg = values.Average();
      var stDev = values.StDev();
      var r1 = avg - stDev;
      var r2 = avg + stDev;
      return values.Where(v => v.Between(r1, r2));
    }
    public static IEnumerable<double> AverageByStDevHigh(this IList<double> values) {
      if(values.Count < 2)
        return values.DefaultIfEmpty(double.NaN);
      var avg = values.Average();
      var stDev = values.StDev();
      var r1 = avg + stDev;
      var r2 = avg + stDev * 2;
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
      if (iterations == 0) return values;
      var avg = values.Select(getValue).DefaultIfEmpty(double.NaN).Average();
      if (averagesOut != null) averagesOut.Insert(0, avg);
      return values.Distinct().Count() < 2  
        ? values 
        : values
        .Where(r => compare(getValue(r), avg))
        .ToArray()
        .AverageByIterations(getValue, compare, iterations - 1, averagesOut);
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
    public static double Cma(this double? MA, double Periods, double NewValue) {
      if (!MA.HasValue) return NewValue;// Else CMA = MA + (NewValue - MA) / (Periods + 1)
      return MA.Value + (NewValue - MA.Value) / (Periods + 1);
    }
    public static double Cma(this double MA, double Periods, double NewValue) {
      if (double.IsNaN(MA)) return NewValue;// Else CMA = MA + (NewValue - MA) / (Periods + 1)
      return MA + (NewValue - MA) / (Periods + 1);
    }
    static double Cma(double MA, double zeroValue, double Periods, double NewValue) {
      if (MA == zeroValue) return NewValue;// Else CMA = MA + (NewValue - MA) / (Periods + 1)
      return Cma(MA, Periods, NewValue);
    }

    //public static bool IsMax(this DateTime d) { return d == DateTime.MaxValue; }
    //public static bool IsMin(this DateTime d) { return d == DateTime.MinValue; }
    public enum RoundTo { Second, Minute, MinuteFloor, MinuteCieling, Hour, HourFloor, Day, DayFloor, Month, MonthEnd, Week }
    public static DateTimeOffset Round(this DateTimeOffset d, RoundTo rt) {
      DateTimeOffset dtRounded = new DateTimeOffset();
      switch (rt) {
        case RoundTo.Second:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Offset);
          if (d.Millisecond >= 500) dtRounded = dtRounded.AddSeconds(1);
          break;
        case RoundTo.Minute:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
          if (d.Second >= 30) dtRounded = dtRounded.AddMinutes(1);
          break;
        case RoundTo.MinuteFloor:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
          break;
        case RoundTo.MinuteCieling:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
          if (d.Second > 0) dtRounded = dtRounded.AddMinutes(1);
          break;
        case RoundTo.Hour:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, d.Offset);
          if (d.Minute >= 30) dtRounded = dtRounded.AddHours(1);
          break;
        case RoundTo.HourFloor:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, d.Offset);
          break;
        case RoundTo.DayFloor:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, d.Offset);
          break;
        case RoundTo.Day:
          dtRounded = new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, d.Offset);
          if (d.Hour >= 12) dtRounded = dtRounded.AddDays(1);
          break;
        case RoundTo.Month:
          dtRounded = new DateTimeOffset(d.Year, d.Month, 1, 0, 0, 0, d.Offset);
          break;
        case RoundTo.MonthEnd:
          dtRounded = new DateTimeOffset(d.Year, d.Month, 1, 0, 0, 0, d.Offset).AddMonths(1).AddDays(-1);
          break;
        case RoundTo.Week:
          dtRounded = d.AddDays(-(int)d.DayOfWeek).Date;
          break;
      }
      return dtRounded;
    }
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
        case RoundTo.MinuteFloor:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
          break;
        case RoundTo.MinuteCieling:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
          if (d.Second > 0) dtRounded = dtRounded.AddMinutes(1);
          break;
        case RoundTo.Hour:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0);
          if (d.Minute >= 30) dtRounded = dtRounded.AddHours(1);
          break;
        case RoundTo.HourFloor:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0);
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
    public static DateTimeOffset Round(this DateTimeOffset dt) { return new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Offset); }
    public static DateTimeOffset Round_(this DateTimeOffset dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static DateTimeOffset Round(this DateTimeOffset dt, int minutes) {
      dt = dt.Round();
      return dt.AddMinutes(dt.Minute / minutes * minutes - dt.Minute);
    }

    public static DateTime Round(this DateTime dt) { return DateTime.SpecifyKind(new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0), dt.Kind); }
    public static DateTime Round_(this DateTime dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static DateTime Round(this DateTime dt, int period) {
      dt = dt.Round();
      return dt.AddMinutes(dt.Minute / period * period - dt.Minute);
    }
    public static bool DoSetsOverlap(this IList<double> bsRates, params double[] bsRatesCT) {
      return !(bsRates.Min() > bsRatesCT.Max() || bsRates.Max() < bsRatesCT.Min());
    }

  }
}
