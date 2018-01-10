using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class MovingAverageExtensions {
    public static IList<double> Cma_Slow(this IEnumerable<double> rates, double period) {
      var x = rates.Scan(double.NaN, (ma, r) => ma.Cma(period, r)).ToList();
      x.Reverse();
      var y = x.Scan(double.NaN, (ma, d) => ma.Cma(period, d)).ToList();
      y.Reverse();
      return y;
    }
    public static IList<double> Cma(this IList<double> rates, double period) {
      var x = GetCmasList(rates, period);
      x.Reverse();
      var y = GetCmasList(x, period);
      y.Reverse();
      return y;
    }

    private static List<double> GetCmasList_Slow(IList<double> source, double period) {
      var ma = double.NaN;
      var maList = new List<double>(source.Count);
      for(var i = 0; i < source.Count; i++)
        maList.Add(ma = ma.Cma(period, source[i]));
      return maList;
    }
    public static List<double> GetCmasList(this IList<double> source, double period) {
      var ma = double.NaN;
      var maList = new double[source.Count];
      for(var i = 0; i < source.Count; i++)
        maList[i] = (ma = ma.Cma(period, source[i]));
      return maList.ToList();
    }

    public static IList<U> Cma<T, U>(this IList<T> rates, Func<T, double> value, double period, Func<T, double, U> map)
      => rates.Cma(value, period, 1, map);
    public static IList<U> Cma<T, U>(this IList<T> rates, Func<T, double> value, double period, int passes, Func<T, double, U> map)
      => rates.Zip(rates.Cma(value, period, passes), map).ToArray();
    public static void Cma<T>(this IList<T> rates, Func<T, double> value, double period, Action<T, double> setMA) {
      rates.Cma(value, period, 1, setMA);
    }
    public static void Cma<T>(this IList<T> rates, Func<T, double> value, double period, int passes, Action<T, double> setMA) {
      var cmas = Cma(rates, value, period, passes);
      for(var i = 0; i < cmas.Count; i++)
        setMA(rates[i], cmas[i]);
    }

    public static IList<double> Cma<T>(this IList<T> rates, Func<T, double> value, double period, int passes) {
      var cmas = rates.Cma(value, period);
      for(var i = passes; i > 1; i--)
        cmas = cmas.Cma(period);
      return cmas;
    }
    public static IList<double> Cma(this IList<double> rates, double period, int passes) {
      var cmas = rates.Cma(period);
      for(var i = passes; i > 1; i--)
        cmas = cmas.Cma(period);
      return cmas;
    }
    public static IList<double> Cma(this IEnumerable<double> rates, double period, int passes) {
      var cmas = rates.Cma(period);
      for(var i = passes; i > 1; i--)
        cmas = cmas.Cma(period);
      return cmas;
    }
    public static IList<double> Cma<T>(this IEnumerable<T> rates, Func<T, double> value, double period) {
      var x = rates.Scan(double.NaN, (ma, r) => ma.Cma(period, value(r))).ToList();
      x.Reverse();
      var y = x.Scan(double.NaN, (ma, d) => ma.Cma(period, d)).ToList();
      y.Reverse();
      return y;
    }
    public static IList<double> Cma(this IEnumerable<double> rates, double period) {
      var x = rates.Scan(double.NaN, (ma, r) => ma.Cma(period, r)).ToList();
      x.Reverse();
      var y = x.Scan(double.NaN, (ma, d) => ma.Cma(period, d)).ToList();
      y.Reverse();
      return y;
    }

    public static IEnumerable<U> MovingAverage<T, U>(this IEnumerable<T> inputStream, Func<T, double> selector, int period, Func<T, double, U> map) {
      var ma = new MovingAverage(period);
      foreach(var item in inputStream) {
        ma.Push(selector(item));
        yield return map(item, ma.Current);
      }
    }

    public static IEnumerable<double> MovingAverage(this IEnumerable<double> inputStream, int period) {
      var ma = new MovingAverage(period);
      foreach(var item in inputStream) {
        ma.Push(item);
        yield return ma.Current;
      }
    }
  }
  public class MovingAverage {
    private readonly int _length;
    private int _circIndex = -1;
    private bool _filled;
    private double _current = double.NaN;
    private readonly double _oneOverLength;
    private readonly double[] _circularBuffer;
    private double _total;

    public MovingAverage(int length) {
      _length = length;
      _oneOverLength = 1.0 / length;
      _circularBuffer = new double[length];
    }

    public MovingAverage Update(double value) {
      double lostValue = _circularBuffer[_circIndex];
      _circularBuffer[_circIndex] = value;

      // Maintain totals for Push function
      _total += value;
      _total -= lostValue;

      // If not yet filled, just return. Current value should be double.NaN
      if(!_filled) {
        _current = double.NaN;
        return this;
      }

      // Compute the average
      double average = 0.0;
      for(int i = 0; i < _circularBuffer.Length; i++) {
        average += _circularBuffer[i];
      }

      _current = average * _oneOverLength;

      return this;
    }

    public MovingAverage Push(double value) {
      // Apply the circular buffer
      if(++_circIndex == _length) {
        _circIndex = 0;
      }

      double lostValue = _circularBuffer[_circIndex];
      _circularBuffer[_circIndex] = value;

      // Compute the average
      _total += value;
      _total -= lostValue;

      // If not yet filled, just return. Current value should be double.NaN
      if(!_filled && _circIndex != _length - 1) {
        _current = double.NaN;
        return this;
      } else {
        // Set a flag to indicate this is the first time the buffer has been filled
        _filled = true;
      }

      _current = _total * _oneOverLength;

      return this;
    }

    public int Length { get { return _length; } }
    public double Current { get { return _current; } }
  }
}
