﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Linq.Dynamic;
using LE = System.Linq.Expressions;
using System.Linq.Expressions;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Xml.Linq;
using System.IO;
using System.Collections.Concurrent;
using HedgeHog;
using System.Web.Script.Serialization;
using Newtonsoft.Json.Converters;

namespace ControlExtentions {
  public static class AAA {
    public static void ResetText(this ComboBox ComboBox) {
      var t = ComboBox.Text;
      ComboBox.Text = "";
      ComboBox.Text = t;
    }
  }
}
namespace HedgeHog {
  public static partial class Lib {
    public static double WeightedAverage<T>(this IList<T> values, Func<T, double> value, Func<T, double> weight) {
      return values.Sum(a => value(a) * weight(a)) / values.Sum(a => weight(a));
    }
    public static void Sort<T>(this List<T> list, Func<T, T, bool> comparrison) {
      list.Sort(LambdaComparisson.Factory(comparrison));
    }
    public static void Sort<T>(this List<T> list, Func<T, double> accessor) {
      list.Sort((a, b) => Math.Sign(accessor(a) - accessor(b)));
    }

    public static void Sort<T>(this List<T> list, Func<T, T, int> comparrison) {
      list.Sort(LambdaComparisson.Factory(comparrison));
    }

    public static T Maximum<T, V>(this IEnumerable<T> values, Func<T, V> value) where V : IComparable {
      return values.Aggregate((p, n) => value(p).CompareTo(value(n)) > 0 ? p : n);
    }
    public static T Minimum<T, V>(this IEnumerable<T> values, Func<T, V> value) where V : IComparable {
      return values.Aggregate((p, n) => value(p).CompareTo(value(n)) > 0 ? n : p);
    }
    private static IEnumerable<T> EnumerableFrom<T>(this T item) {
      return new T[] { item };
    }
    public static IEnumerable<T> Append<T>(this IEnumerable<T> that, T item) {
      IEnumerable<T> itemAsSequence = new T[] { item };
      return that.Concat(itemAsSequence);
    }

    public static IEnumerable<Tuple<T, T>> Permutation<T>(this IList<T> source) {
      IEnumerable<T[]> list = new T[0][];
      for(var skip = 0; skip < source.Count - 1; skip++)
        list = list.Concat(source.Skip(skip).Select(t => new[] { t })
          .Scan((t1, t2) => new[] { t1[0], t2[0] }));
      return list.Select(a => Tuple.Create(a[0], a[1]));
    }
    public static IEnumerable<U> Permutation<T, U>(this IList<T> source, Func<T, T, U> map) {
      IEnumerable<T[]> list = new T[0][];
      for(var skip = 0; skip < source.Count - 1; skip++)
        list = list.Concat(source.Skip(skip).Select(t => new[] { t })
          .Scan((t1, t2) => new[] { t1[0], t2[0] }));
      return list.Select(a => map(a[0], a[1]));
    }
    public static IEnumerable<IEnumerable<T>> Permutation<T>(this IEnumerable<T> source, int len) {
      var source2 = source.Select((n, i) => new { n, i }).ToArray();
      var source3 = Enumerable.Range(0, len).Select(j => source2.Skip(j).Take(2).ToArray())/*.Take(len)*/.ToArray();
      var t = new[] { new { n = default(T), i = 0 } };
      var comp = MonoidsCore.ToFunc(t, t, (a1, a2) => a1.Select(z => z.n).SequenceEqual(a2.Select(z => z.n)));
      return source3.CartesianProduct()
        .Select(x => x.OrderBy(i1 => i1.i).Distinct().ToArray())
        .Where(x => x.Count() == len)
        .Distinct(LambdaComparer.Factory(comp))
        .Select(x => x.Select(z => z.n));

    }
    public static IEnumerable<IList<T>> CartesianProductSelf<T>(this IEnumerable<T> source) {
      var source2 = source.Select((v, i) => new { v, i }).ToArray();
      return new[] { source2, source2 }.CartesianProduct()
        .Select(x => x.ToArray())
        .Where(x => x[0].i != x[1].i)
        .Select(x => x.Select(x2 => x2.v).ToArray());
    }
    public static IEnumerable<IEnumerable<T>> CartesianProduct<T>(this
    IEnumerable<IEnumerable<T>> inputs) {
      return inputs.Aggregate(
          EnumerableFrom(Enumerable.Empty<T>()),
          (soFar, input) =>
              from prevProductItem in soFar
              from item in input
              select prevProductItem.Append(item));
    }

    //public static void GetProperties(this )

    /// <summary>
    /// Used to get a 'typed' reference to anonymous class
    /// </summary>
    /// <typeparam name="T">Will be inferred by compiler</typeparam>
    /// <param name="obj">Anonymous object</param>
    /// <param name="type">Dummy lambda returning object of T type</param>
    /// <returns></returns>
    public static T Cast<T>(this object obj, Func<T> type) {
      return (T)obj;
    }

    public static T Caster<T>(this T c, object any) {
      return (T)any;
    }
    public static Func<T> Caster<T>(this T c, out object any) {
      Func<object, T> f = o => (T)o;
      any = c;
      return () => f(c);
    }
    public static Func<object, T> Caster<T>(this T c) {
      return o => (T)o;
    }
    public static Func<U, T> Caster<U, T>(this T c, Func<U, object> func) {
      return u => (T)func(u);
    }

    public static ConcurrentDictionary<K, V> ToConcurrentDictionary<T, K, V>(this IEnumerable<T> list, Func<T, K> keyFactory, Func<T, V> valueFactory) {
      return new ConcurrentDictionary<K, V>(list.ToDictionary(keyFactory, valueFactory));
    }
    public static T[] ToArray<T>(this IEnumerable<T> list, int dimention) {
      return new T[dimention];
    }
    public static ConcurrentQueue<T> ToConcurrentQueue<T>(this IEnumerable<T> list) {
      return new ConcurrentQueue<T>(list);
    }
    #region AreaInfo
    public class AreaInfo {
      public int Start { get; set; }
      public int Stop { get; set; }
      public double Area { get; set; }
      public AreaInfo() { }
      public AreaInfo(int start, int stop, double area) {
        this.Start = start;
        this.Stop = stop;
        this.Area = area;
      }
      public override string ToString() { return new { Start, Stop, Area }.ToString(); }
    }
    #endregion
    public static List<AreaInfo> Areas<T>(this IList<T> list, Func<T, int> getPosition, Func<T, double> getValue) {
      var anonArea = new AreaInfo { Start = getPosition(list[0]), Stop = getPosition(list[0]), Area = 0 };
      var areas = new List<AreaInfo>() { anonArea };
      list.Aggregate((p, n) => {
        var nValue = getValue(n);
        var nPosition = getPosition(n);
        if(nValue.Sign() == getValue(p).Sign()) {
          var area = areas.LastBC();
          area.Area += nValue;
          area.Stop = nPosition;
        } else {
          areas.Add(new AreaInfo(nPosition, nPosition, nValue));
        }
        return n;
      });
      return areas;
    }
    public static void FillGaps<T>(this IList<T> rates, Func<T, bool> isEmpty, Func<T, double> getValue, Action<T, double> setValue) {
      if(!rates.Any())
        return;
      var rates1 = rates.Select((r, i) => new { r, i }).ToArray();
      rates1.Where(r => !isEmpty(r.r))
      .Aggregate((p, n) => {
        var diffDist = getValue(n.r) - getValue(p.r);
        var diffIndex = (n.i - p.i - 1).Max(1);
        var step = diffDist / (diffIndex + 1).Max(1);
        for(var i = p.i + 1; i < n.i; i++)
          setValue(rates1[i].r, getValue(rates1[i - 1].r) + step);
        return n;
      });
    }
    public static Delegate Compile<T>(this string expression, params ParameterExpression[] parameters) {
      return System.Linq.Dynamic.DynamicExpression.ParseLambda(parameters, typeof(T), expression).Compile();
    }
    public static T Evaluate<T>(this string expression, params ParameterExpression[] parameters) {
      return string.IsNullOrWhiteSpace(expression)
        ? default(T)
        : (T)System.Linq.Dynamic.DynamicExpression.ParseLambda(parameters, typeof(T), expression).Compile().DynamicInvoke();
    }
    public static U[] ToArray<T, U>(this IEnumerable<T> es, Func<T, U> a) {
      return es.Select(a).ToArray();
    }
    public static List<U> ToList<T, U>(this IEnumerable<T> es, Func<T, U> a) {
      return es.Select(a).ToList();
    }
    public static T FirstOrDefault<T>(this IEnumerable<T> v, T defaultValue) {
      return v.Take(1).DefaultIfEmpty(defaultValue).Single();
    }
    public static TOut FirstOrDefault<TIn, TOut>(this IEnumerable<TIn> v, Func<TIn, TOut> projector, TOut defaultValue) {
      return v.Take(1).Select(projector).DefaultIfEmpty(defaultValue).Single();
    }
    public static string Formater(this string format, params object[] args) {
      return string.Format(format, args);
    }

    public static string CallingMethod(int skipFrames = 1) {
      return new StackFrame(skipFrames + 1).GetMethod().Name;
    }


    public static string Csv(this IEnumerable<double> values) {
      return values.Csv("{0}", d => d);
    }
    public static string Csv(this IEnumerable<double> values, string format, params Func<double, object>[] foos) {
      return values.Csv<double>(format, foos);
    }
    public static string Csv<T>(this IEnumerable<T> values, string format, params Func<T, object>[] foos) {
      Func<T, object[]> parms = bar => foos.Select(foo => foo(bar)).ToArray();
      return string.Join(Environment.NewLine, values.Select(b => string.Format(format, parms(b))));
    }

    public static double Height<T>(this IEnumerable<T> rates, Func<T, double> getValue) {
      double min, max;
      return rates.Height(getValue, out min, out max);
    }
    public static double Height<T>(this IEnumerable<T> rates, Func<T, double> getValue, out double min, out double max) {
      return rates.Height(getValue, getValue, out min, out max);
    }
    public static double Height<T>(this IList<T> rates, Func<T, double> valueHigh, Func<T, double> valueLow) {
      double min, max;
      return rates.Height(valueHigh, valueLow, out min, out max);
    }
    public static double Height<T>(this IEnumerable<T> rates, Func<T, double> valueMax, Func<T, double> valueMin, out double min, out double max) {
      if(!rates.Any())
        return min = max = double.NaN;
      min = rates.Min(valueMin);
      max = rates.Max(valueMax);
      return max - min;
    }
    public static double Height<T>(this IEnumerable<T> rates, Func<T, double> valueMax, Func<T, double> valueMin, out T min, out T max) {
      if(!rates.Any()) {
        min = max = default(T);
        return 0;
      }
      min = rates.OrderBy(valueMin).First();
      max = rates.OrderByDescending(valueMax).First();
      return valueMax(max) - valueMin(min);
    }
    public static double Height(this IList<double> rates, out double min, out double max) {
      if(rates.Count == 0)
        return min = max = double.NaN;
      min = rates.Min();
      max = rates.Max();
      return max - min;
    }

    public static IEnumerable<double> Shrink(this IList<double> values, int groupLength) {
      return from r in values.Select((r, i) => new { r, i = i / groupLength })
             group r by r.i into g
             select g.Average(a => a.r);
    }
    public static IEnumerable<double> Shrink(this IEnumerable<double> values, int groupLength) {
      return from r in values.Select((r, i) => new { r = r, i = i / groupLength })
             group r by r.i into g
             select g.Average(a => a.r);
    }
    public static IEnumerable<double> Shrink<T>(this IEnumerable<T> values, Func<T, double> getValue, int groupLength) {
      return from r in values.Select((r, i) => new { r = getValue(r), i = i / groupLength })
             group r by r.i into g
             select g.Average(a => a.r);
    }
    public static IEnumerable<double> UnShrink(this IEnumerable<double> values, int groupLength) {
      return values.Select(v => Enumerable.Repeat(v, groupLength)).SelectMany(v => v);
    }

    public static IEnumerable<R> Shrink<T, R>(this IEnumerable<T> values, Func<T, double> getValue, Func<int, int> nextStep, Func<double, int, R> getOut) {
      return from r in values.Select((r, i) => new { r = getValue(r), i = i / nextStep(i), groupBy = nextStep(i) })
             group r by new { r.i, r.groupBy } into g
             select getOut(g.Average(a => a.r), g.Key.i * g.Key.groupBy);
    }

    public static IEnumerable<T> TakeEx<T>(this IEnumerable<T> list, int count) {
      return count >= 0 ? list.Take(count) : list.Skip(list.Count() + count);
    }
    public static T Last<T>(this IList<T> list, int index) {
      if(list.Count == 0)
        return default(T);
      return list[list.Count - index - 1];
    }
    /// <summary>
    /// Last By Count
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <returns></returns>
    public static T LastBC<T>(this IList<T> list, int positionFromEnd = 1) {
      if(list.Count == 0)
        return default(T);
      return list[list.Count - positionFromEnd];
    }
    public static IEnumerable<T> LastBCs<T>(this IEnumerable<T> list, int positionFromEnd = 1) {
      return list.Skip(list.Count() - positionFromEnd);
    }
    public static T LastByCountOrDefault<T>(this IList<T> list) {
      return list.Count == 0 ? default(T) : list.LastBC();
    }
    public static T LastByCountOrDefault<T>(this IList<T> list, T defaultValue) {
      return list.Count == 0 ? defaultValue : list.LastBC();
    }
    public static T Pop<T>(this List<T> list, int position = 0) {
      try {
        return list[position];
      } finally {
        list.RemoveAt(position);
      }
    }

    public static T[] PopRange<T>(this List<T> list, int count = 1, int position = 0) {
      try {
        return list.Skip(position).Take(count).ToArray();
      } finally {
        list.RemoveRange(position, count);
      }
    }
    public static string ToXml(this object o, SaveOptions saveOptions = SaveOptions.None) {
      var x = new XElement(o.GetType().Name,
      o.GetType().GetProperties().Select(p => new XElement(p.Name, p.GetValue(o, null) + "")));
      return x.ToString(saveOptions);
    }

    public static object ToDataObject(this object o) {
      var d = o.GetType().GetProperties().Select(s => new DynamicProperty(s.Name, s.PropertyType));
      Type t = System.Linq.Dynamic.DynamicExpression.CreateClass(d.ToArray());
      object ret = Activator.CreateInstance(t);
      foreach(var e in d)
        ret.SetProperty(e.Name, o.GetProperty(e.Name));
      return ret;
    }

    public static TReturn Invoke<TReturn>(this object o, string methodName, object[] parameters) {
      var t = o.GetType();
      var mi = t.GetMethod(methodName);
      if(mi != null)
        return (TReturn)mi.Invoke(o, parameters);
      throw new NotImplementedException("Property/Field " + methodName + " is not implemented in " + o.GetType().Name + ".");
    }
    public static void Invoke(this object o, string methodName, object[] parameters) {
      var t = o.GetType();
      var mi = t.GetMethod(methodName);
      if(mi != null) {
        mi.Invoke(o, parameters);
        return;
      }
      throw new NotImplementedException("Property/Field " + methodName + " is not implemented in " + o.GetType().Name + ".");
    }
    public static IEnumerable<MethodInfo> GetMethodsByAttibute<A>(this object that) where A : Attribute {
      return that.GetType().GetMethods().Where(mi => mi.GetCustomAttributes<A>().Any());
    }

    public static IEnumerable<U> GetPropertiesByAttibute<A, U>(this object that, Func<A> attr, Func<A, PropertyInfo, U> map) where A : Attribute {
      return from p in that.GetType().GetProperties()
             from a in p.GetCustomAttributes(false).OfType<A>().Take(1)
             select map(a, p);
    }
    public static IEnumerable<U> GetPropertiesByAttibute<A, U>(this Type type, Func<A> attr, Func<A, PropertyInfo, U> map) where A : Attribute {
      return from p in type.GetProperties()
             from a in p.GetCustomAttributes(false).OfType<A>().Take(1)
             select map(a, p);
    }

    public static IEnumerable<Tuple<A, PropertyInfo>> GetPropertiesByAttibute<A>(this object that, Func<A, bool> predicate) where A : Attribute {
      var type = that.GetType();
      foreach(var p in type.GetProperties()) {
        if(p.GetCustomAttributes(typeof(A), false).Cast<A>().Where(predicate).Any())
          yield return new Tuple<A, PropertyInfo>(p.GetCustomAttributes(false).OfType<A>().First(), p);
      }
    }
    public static IEnumerable<Tuple<A, PropertyInfo>> GetPropertiesByAttibute<A>(this Type type, Func<A, bool> predicate) where A : Attribute {
      foreach(var p in type.GetProperties()) {
        if(p.GetCustomAttributes(typeof(A), false).Cast<A>().Where(predicate).Any())
          yield return new Tuple<A, PropertyInfo>(p.GetCustomAttributes(false).OfType<A>().First(), p);
      }
    }
    public static IEnumerable<U> GetPropertiesByType<T, U>(this object that, Func<T> sample, Func<T, PropertyInfo, U> map) {
      return from p in that.GetType().GetProperties()
             where p.PropertyType == typeof(T)
             select map((T)p.GetValue(that), p);
    }
    public static IEnumerable<U> GetPropertiesByTypeAndAttribute<T, A, U>(
      this object that, Func<T> sample,
      Func<A, bool> attributePredicate,
      Func<T, PropertyInfo, U> map)
      where A : Attribute {
      return from p in that.GetType().GetProperties()
             where p.PropertyType == typeof(T) &&
             (attributePredicate == null || p.GetCustomAttributes(typeof(A), false).Cast<A>().Where(attributePredicate).Any())
             select map((T)p.GetValue(that), p);
    }

    public static T GetProperty<T>(this object o, string p) {
      var t = o.GetType();
      System.Reflection.PropertyInfo pi = t.GetProperty(p);
      if(pi != null)
        return (T)pi.GetValue(o, null);
      System.Reflection.FieldInfo fi = t.GetField(p);
      if(fi != null)
        return (T)fi.GetValue(o);
      throw new NotImplementedException("Property/Field " + p + " is not implemented in " + o.GetType().Name + ".");
    }

    public static object GetProperty(this object o, string p) {
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if(pi != null)
        return pi.GetValue(o, null);
      System.Reflection.FieldInfo fi = o.GetType().GetField(p);
      if(fi != null)
        return fi.GetValue(o);
      throw new NotImplementedException("Property/Field " + p + " is not implemented in " + o.GetType().Name + ".");
    }

    public static double Mean(this IEnumerable<double> values) {
      var max = double.MinValue;
      var min = double.MaxValue;
      values.ForEach(v => { max = max.Max(v); min = min.Min(v); });
      return (max + min) / 2;
    }
    public static double Deviation(IEnumerable<double> Values, DeviationType CalculationType) {
      double SumOfValuesSquared = 0;
      double SumOfValues = 0;
      var count = Values.Count();
      //Calculate the sum of all the values
      foreach(double item in Values) {
        SumOfValues += item;
      }
      //Calculate the sum of all the values squared
      foreach(double item in Values) {
        SumOfValuesSquared += Math.Pow(item, 2);
      }
      if(CalculationType == DeviationType.Sample) {
        return Math.Sqrt((SumOfValuesSquared - Math.Pow(SumOfValues, 2) / count) / (count - 1));
      } else {
        return Math.Sqrt((SumOfValuesSquared - Math.Pow(SumOfValues, 2) / count) / count);
      }
    }
    public enum DeviationType {
      Population,
      Sample
    }

    public static double AverageWeighted<T>(this IList<T> values, Func<T, double> avg, Func<T, double> weight) {
      return values.Select(v => avg(v) * weight(v)).Sum() / values.Sum(weight);
    }
    public static double MeanAverage(this IList<double> values, int iterations = 3) {
      var valueLow = values.AverageByIterations(iterations, true);
      var valueHight = values.AverageByIterations(iterations, false);
      if(valueLow.Count == 0 && valueHight.Count == 0)
        return values.MeanAverage(iterations - 1);
      return values.Except(valueLow.Concat(valueHight)).DefaultIfEmpty(values.Average()).Average();
    }

    public static double SquareMeanRoot(this double d1, double d2) { return Math.Sqrt((d1 * d1 + d2 * d2) / 2); }
    public static double RootMeanSquare(this double d1, double d2) { return Math.Pow((Math.Sqrt(d1) + Math.Sqrt(d2)) / 2, 2); }
    public static double RootMeanPower(this double d1, double d2, double power) { return Math.Pow((Math.Pow(d1, 1 / power) + Math.Pow(d2, 1 / power)) / 2, power); }
    public static double RootMeanPower(this IEnumerable<int> source, double power) {
      return source.Select(i => (double)i).RootMeanPower(power);
    }
    public static double AverageByPosition(this IList<double> source) {
      double l = source.Count;
      var weights = Enumerable.Range(1, source.Count).Select(w => l / w).ToArray();
      return source.Zip(weights, (s, w) => s * w).Sum() / weights.Sum();
    }
    public static double RootMeanPower(this IEnumerable<double> source, double power = 2) {
      var avg = source.Select(d => Math.Pow(d.Abs(), 1 / power) * d.Sign()).Average();
      return Math.Pow(avg.Abs(), power) * avg.Sign();
    }
    public static double? RootMeanPower(this IEnumerable<double?> source, double power = 2) {
      var avg = source.Select(d => (double?)Math.Pow(d.Value.Abs(), 1 / power) * d.Value.Sign()).Average();
      return avg.HasValue ? Math.Pow(avg.Value.Abs(), power) * avg.Value.Sign() : (double?)null;
    }
    public static double RootMeanPowerByPosition(this IEnumerable<double> source, double power) {
      var avg = source.Select(d => Math.Pow(d, 1 / power)).ToList().AverageByPosition();
      return Math.Pow(avg, power);
    }
    public static double SquareMeanRoot(this IEnumerable<double> source) {
      return source.RootMeanPower(0.5);
    }
    public static double? SquareMeanRoot(this IEnumerable<double?> source) {
      return source.RootMeanPower(0.5);
    }
    public static double StandardDeviation(this List<double> doubleList) {
      double average = doubleList.Average();
      double sumOfDerivation = 0;
      doubleList.ForEach(v => sumOfDerivation += Math.Pow(v, 2));
      double sumOfDerivationAverage = sumOfDerivation / doubleList.Count;
      return Math.Sqrt(sumOfDerivationAverage - Math.Pow(average, 2));
    }

    public class MovingStDevP {
      double _total = 0;
      double _max = double.NaN;
      double _min = double.NaN;
      int _count = 0;

      private double _sumOfDerivation;
      double _average { get { return _total / _count; } }
      double _height { get { return _max - _min; } }

      public double NextR(double d, double heightMin) {
        var std = Next(d);
        return std / _height.Max(heightMin);
      }
      public double Next(double d) {
        _total += d;
        _max = _max.Max();
        _min = _min.Max();
        _count++;
        _sumOfDerivation += d * d;
        double sumOfDerivationAverage = _sumOfDerivation / _count;
        return Math.Sqrt(sumOfDerivationAverage - Math.Pow(_average, 2)).IfNaN(0);
      }

      public double FirstR(IList<double> n, double heightMin) {
        var std = First(n);
        return std / _height.Max(heightMin);
      }
      public double First(IList<double> n) {
        _total = n.Sum();
        _count = n.Count;
        _max = n.Max();
        _min = n.Min();
        _sumOfDerivation = n.Sum(v => v * v);
        double sumOfDerivationAverage = _sumOfDerivation / _count;
        return Math.Sqrt(sumOfDerivationAverage - Math.Pow(_average, 2)).IfNaN(0);
      }
    }

    public static double StDevP(this ICollection<double> n) {
      double total = 0, average = 0;

      foreach(double num in n) {
        total += num;
      }

      average = total / n.Count;

      double runningTotal = 0;

      foreach(double num in n) {
        runningTotal += ((num - average) * (num - average));
      }

      double calc = runningTotal / n.Count;
      double standardDeviationP = Math.Sqrt(calc);

      return standardDeviationP;
    }

    public static double[] LinearRegression(this double[] valuesY) {
      double slope, value;
      LinearRegression(valuesY, out slope, out value);
      return new[] { value, slope };
    }
    public static T LinearRegression<T>(this double[] valuesY, Func<double, double, T> map) {
      double slope, value;
      LinearRegression(valuesY, out slope, out value);
      return map(value, slope);
    }
    public static void LinearRegression(this double[] valuesY, out double a, out double b) {
      var valuesX = new double[valuesY.Length];
      for(var i = 0; i < valuesY.Length; i++)
        valuesX[i] = i;
      LinearRegression_(valuesX, valuesY, out a, out b);
    }
    public static void LinearRegression_(double[] valuesX, double[] valuesY, out double a, out double b) {
      double xAvg = 0;
      double yAvg = 0;
      for(int x = 0; x < valuesY.Length; x++) {
        xAvg += valuesX[x];
        yAvg += valuesY[x];
      }
      xAvg = xAvg / valuesY.Length;
      yAvg = yAvg / valuesY.Length;
      double v1 = 0;
      double v2 = 0;
      for(int x = 0; x < valuesY.Length; x++) {
        v1 += (x - xAvg) * (valuesY[x] - yAvg);
        v2 += Math.Pow(x - xAvg, 2);
      }
      a = v1 / v2;
      b = yAvg - a * xAvg;
      //Console.WriteLine("y = ax + b");
      //Console.WriteLine("a = {0}, the slope of the trend line.", Math.Round(a, 2));
      //Console.WriteLine("b = {0}, the intercept of the trend line.", Math.Round(b, 2));

    }

    static void LinearRegression_(double[] values, out double a, out double b) {
      double xAvg = 0;
      double yAvg = 0;
      for(int x = 0; x < values.Length; x++) {
        xAvg += x;
        yAvg += values[x];
      }
      xAvg = xAvg / values.Length;
      yAvg = yAvg / values.Length;
      double v1 = 0;
      double v2 = 0;
      for(int x = 0; x < values.Length; x++) {
        v1 += (x - xAvg) * (values[x] - yAvg);
        v2 += (x - xAvg) * (x - xAvg);
        v2 += Math.Pow(x - xAvg, 2);
      }
      a = v1 / v2;
      b = yAvg - a * xAvg;
      //Console.WriteLine("y = ax + b");
      //Console.WriteLine("a = {0}, the slope of the trend line.", Math.Round(a, 2));
      //Console.WriteLine("b = {0}, the intercept of the trend line.", Math.Round(b, 2));

    }

    public class CmaWalker :Models.ModelBase {
      public double Current { get; set; }
      public double Difference { get { return Prev - Last; } }
      public double Last { get { return CmaArray.Last().GetValueOrDefault(); } }
      public double Prev { get { return CmaArray.Length == 1 ? Current : CmaArray[CmaArray.Length - 2].GetValueOrDefault(); } }
      public double Max { get { return CmaArray.Concat(new double?[] { Current }).Max().GetValueOrDefault(); } }
      public double Min { get { return CmaArray.Concat(new double?[] { Current }).Min().GetValueOrDefault(); } }
      public double?[] CmaArray;
      public CmaWalker(int cmaCount) {
        if(cmaCount < 1)
          throw new ArgumentException("Array length must be more then zero.", "cmaCount");
        this.CmaArray = new double?[cmaCount];
      }
      public void Add(double value, double cmaPeriod) {
        Current = value;
        for(var i = 0; i < CmaArray.Length; i++) {
          CmaArray[i] = CmaArray[i].Cma(cmaPeriod, value);
          value = CmaArray[i].Value;
        }
        RaisePropertyChanged("Difference");
        RaisePropertyChanged("Current");
        RaisePropertyChanged("Last");
        RaisePropertyChanged("Prev");
      }
      public void Clear() {
        for(var i = 0; i < CmaArray.Length; i++)
          CmaArray[i] = null;
      }
      public void Reset(int newCmaCount) {
        var offset = newCmaCount - CmaArray.Length;
        if(offset == 0)
          return;
        if(offset > 0)
          CmaArray = CmaArray.Concat(new double?[newCmaCount - CmaArray.Length]).ToArray();
        else
          CmaArray = CmaArray.Take(newCmaCount).ToArray();
      }
      public double Diff(double current) { return current - CmaArray[0].Value; }
      public double[] Diffs() {
        var diffs = new List<double>();
        for(var i = 1; i < CmaArray.Length; i++)
          diffs.Add((CmaArray[i - 1].GetValueOrDefault() - CmaArray[i]).GetValueOrDefault());
        return diffs.ToArray();
      }
      public double FromEnd(int position) {
        return CmaArray.Reverse().Take(position + 1).Last().Value;
      }
    }

    static readonly Guid GuidEmpty1 = 1.Guid();
    public static bool HasValue(this Guid g) {
      return g != System.Guid.Empty || g == GuidEmpty1;
    }
    public static Guid ValueOrDefault(this Guid g, Guid defaultValue) {
      return g != System.Guid.Empty ? g : defaultValue;
    }
    public static Guid Guid(this int i) {
      if(!i.Between(0, 255))
        throw new ArgumentException("Value must be between 0 and 255.");
      var b = (byte)i;
      return new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, b });
    }

    public static TimeSpan FromSeconds(this int i) { return TimeSpan.FromSeconds(i); }
    public static TimeSpan FromSeconds(this double i) { return TimeSpan.FromSeconds(i); }
    public static TimeSpan FromMinutes(this int i) { return TimeSpan.FromMinutes(i); }
    public static TimeSpan FromMinutes(this double i) { return TimeSpan.FromMinutes(i); }
    public static TimeSpan FromHours(this double i) { return TimeSpan.FromHours(i); }

    public static IEnumerable<T> OfType<T>(this IEnumerable e, T type) {
      return e.OfType<T>();
    }
    public static IEnumerable<T> OfType<T>(this IEnumerable e, Func<T> type) {
      return e.OfType<T>();
    }
    public static Action<T> ToAction<T>(this T value, Action<T> action) {
      return action;
    }
    public static Action Do<T>(this Lazy<T> l, Action<T> selector) {
      return () => selector(l.Value);
    }
    //public static U Select<T, U>(this Lazy<T> l, Func<T, U> selector) {
    //  return selector(l.Value);
    //}
    public static Lazy<U> Bind<T, U>(this Lazy<T> l, Func<T, Lazy<U>> selector) {
      return new Lazy<U>(() => selector(l.Value).Value);
    }
    public static bool IsZeroOrNaN(this double d) {
      return d == 0 || double.IsNaN(d);
    }
    public static bool IsNaNOrZero(this double d) {
      return double.IsNaN(d) || d == 0;
    }
    public static double IfNotSetOrZero(this double d, double other) => d.IsNotSetOrZero() ? other : d;
    public static bool IsSetAndNotZero(this double d) => !d.IsNotSetOrZero();
    public static bool IsNotSetOrZero(this double d) =>double.IsNaN(d) || d == 0 || d == double.MaxValue || d == double.MinValue || double.IsInfinity(d);
    public static bool IsNaN(this double d) {
      return double.IsNaN(d);
    }
    public static bool IsNotNaN(this double d) {
      return !double.IsNaN(d);
    }
    public static double IfNaN(this double d, double defaultValue) {
      return double.IsNaN(d) ? defaultValue : d;
    }
    public static double IfNaNOrZero(this double d, IEnumerable<double> defaultValue) {
      return double.IsNaN(d) || d == 0 ? defaultValue.Single() : d;
    }
    public static double IfNaNOrZero(this double d, double defaultValue) {
      return double.IsNaN(d) || d == 0 ? defaultValue : d;
    }
    public static double IfNaNOrNegative(this double d, double defaultValue) {
      return double.IsNaN(d) || d <= 0 ? defaultValue : d;
    }
    public static double IfNaN(this double d, Func<double> defaultValue) {
      return double.IsNaN(d) ? defaultValue() : d;
    }
    public static double IfNaN(this double d, Lazy<double> defaultValue) {
      return double.IsNaN(d) ? defaultValue.Value : d;
    }
    public static double IfZero(this int d, double defaultValue) {
      return d == 0 ? defaultValue : d;
    }
    public static double Max3(double n1, double n2, double n3) {
      return Math.Max(Math.Max(n1, n2), n3);
    }
    public static decimal Max3(decimal n1, decimal n2, decimal n3) {
      return Math.Max(Math.Max(n1, n2), n3);
    }
    public static double Min3(double n1, double n2, double n3) {
      return Math.Min(Math.Min(n1, n2), n3);
    }
    public static decimal Min3(decimal n1, decimal n2, decimal n3) {
      return Math.Min(Math.Min(n1, n2), n3);
    }

    static double GetTextBoxTextDouble(TextBox TextBox) { return double.Parse("0" + GetTextBoxText(TextBox)); }
    public static int GetTextBoxTextInt(TextBox TextBox) {
      var t = GetTextBoxText(TextBox);
      int i;
      if(!int.TryParse(t, out i))
        throw new FormatException(t + " is not an integer.");
      return i;
    }
    public static string GetTextBoxText(TextBox TextBox) {
      return TextBox.Dispatcher.Invoke(
        DispatcherPriority.Input,
        (DispatcherOperationCallback)delegate (object o) { return TextBox.Text; },
        null
      ) + "";
    }
    public static string SetTextBoxText(TextBox TextBox, string Text) {
      TextBox.Dispatcher.BeginInvoke(
        DispatcherPriority.Background,
        (DispatcherOperationCallback)delegate (object o) { TextBox.Text = Text; return null; },
        null
      );
      return Text;
    }
    public static void SetChecked(CheckBox CheckBox, bool IsChecked) {
      SetChecked(CheckBox, IsChecked, false);
    }
    public static void SetChecked(CheckBox CheckBox, bool IsChecked, bool Force) {
      var d = (DispatcherOperationCallback)delegate (object o) { CheckBox.IsChecked = IsChecked; return null; };
      if(Force)
        CheckBox.Dispatcher.Invoke(DispatcherPriority.Send, d, null);
      else
        CheckBox.Dispatcher.BeginInvoke(DispatcherPriority.Send, d, null);
    }
    public static bool? GetChecked(CheckBox CheckBox) {
      return (bool?)
      CheckBox.Dispatcher.Invoke(
        DispatcherPriority.Input,
        (DispatcherOperationCallback)delegate (object o) { return CheckBox.IsChecked; },
        null
      );
    }
    public static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
  }

  #region Extentions
  public static class Extentions {

    public static string PropertiesToString(this object o, string separator) {
      List<string> props = new List<string>();
      foreach(var prop in o.GetType().GetProperties().OrderBy(p => p.Name))
        props.Add(prop.Name + ":" + prop.GetValue(o, new object[0]));
      return string.Join(separator, props);
    }

    public static IEnumerable<T> FirstOrLast<T>(this IEnumerable<T> e, bool last, int count = 1) {
      return last ? e.TakeLast(count) : e.Take(count);
    }
    public static T[] FirstAndLast<T>(this IList<T> e) {
      return new[] { e[0], e[e.Count - 1] };
    }
    public static double AverageHeight(this IEnumerable<double> values) {
      return values.Skip(1).Select((d, i) => Math.Abs(d - values.ElementAt(i))).Average();
    }
    public static double Height(this IEnumerable<double> values) {
      return values.Max() - values.Min();
    }

  }

  #endregion

  public struct LineAndTime {
    public double Value;
    public DateTime Time;
    public LineAndTime(double value, DateTime time) {
      Value = value;
      Time = time;
    }
  }
}
