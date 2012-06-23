using System;
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

namespace ControlExtentions {
  public static class AAA {
    public static void ResetText(this ComboBox ComboBox) {
      var t = ComboBox.Text; ComboBox.Text = ""; ComboBox.Text = t;
    }
  }
}
namespace HedgeHog {
  public static class Lib {

    public static string CallingMethod(int skipFrames=1) {
      return new StackFrame(skipFrames + 1).GetMethod().Name;
    }

    public static IEnumerable<T> TakeEx<T>(this IEnumerable<T> list,int count) {
      return count >= 0 ? list.Take(count) : list.Skip(list.Count() + count);
    }
    public static T LastByCount<T>(this IList<T> list) {
      return list[list.Count - 1];
    }
    public static T LastByCountOrDefault<T>(this IList<T> list) {
      return list.Count == 0 ? default(T) : list.LastByCount();
    }
    public static T LastByCountOrDefault<T>(this IList<T> list,T defaultValue) {
      return list.Count == 0 ? defaultValue : list.LastByCount();
    }
    public static T Pop<T>(this List<T> list, int position = 0) {
      try {
        return list[position];
      } finally {
        list.RemoveAt(position);
      }
    }

    public static T[] PopRange<T>(this List<T> list,int count = 1, int position = 0) {
      try {
        return list.Skip(position).Take(count).ToArray();
      } finally {
        list.RemoveRange(position, count);
      }
    }

    public static IDisposable SubscribeToPropertyChanged<TPropertySource>(this TPropertySource source, Expression<Func<TPropertySource, object>> property, Action<TPropertySource> onNext) where TPropertySource : class, INotifyPropertyChanged {
      var propertyName = Lib.GetLambda(property);
      var propertyDelegate = new Func<TPropertySource, object>(property.Compile());
      return (from e in Observable.FromEventPattern<PropertyChangedEventArgs>(source, "PropertyChanged")
              where e.EventArgs.PropertyName == propertyName
              select e.Sender as TPropertySource
              ).DistinctUntilChanged(propertyDelegate).Subscribe(onNext);
    }

    public static string GetLambda<TPropertySource>(Expression<Func<TPropertySource, object>> expression) {
      var lambda = expression as LambdaExpression;
      MemberExpression memberExpression;
      if (lambda.Body is UnaryExpression) {
        var unaryExpression = lambda.Body as UnaryExpression;
        memberExpression = unaryExpression.Operand as MemberExpression;
      } else {
        memberExpression = lambda.Body as MemberExpression;
      }

      Debug.Assert(memberExpression != null, "Please provide a lambda expression like 'n => n.PropertyName'");

      if (memberExpression != null) {
        var propertyInfo = memberExpression.Member as PropertyInfo;

        return propertyInfo.Name;
      }

      return null;
    }

    public static string GetLambda(LE.Expression<Func<object>> func) { return func.Name(); }
    public static string[] GetLambdas(params LE.Expression<Func<object>>[] funcs) { return funcs.Names(); }
    public static string[] Names(this LE.Expression<Func<object>>[] funcs) {
      var names = new List<string>();
      foreach (var e in funcs)
        names.Add(e.Name());
      return names.ToArray();
    }

    public static string Name(this Expression<Func<object>> propertyLamda) {
      var body = propertyLamda.Body as UnaryExpression;
      if (body == null) {
        return ((propertyLamda as LambdaExpression).Body as MemberExpression).Member.Name;
      } else {
        var operand = body.Operand as MemberExpression;
        var member = operand.Member;
        return member.Name;
      }
    }

    public static string Name(this LambdaExpression propertyExpression) {
        var body = propertyExpression.Body as MemberExpression;
        if (body == null)
          throw new ArgumentException("'propertyExpression' should be a member expression");

        // Extract the right part (after "=>")
        var vmExpression = body.Expression as ConstantExpression;
        if (vmExpression == null)
          throw new ArgumentException("'propertyExpression' body should be a constant expression");

        // Create a reference to the calling object to pass it as the sender
        LambdaExpression vmlambda = System.Linq.Expressions.Expression.Lambda(vmExpression);
        Delegate vmFunc = vmlambda.Compile();
        object vm = vmFunc.DynamicInvoke();
        return body.Member.Name;
    }


    public static object ToDataObject(this object o) {
      var d = o.GetType().GetProperties().Select(s => new DynamicProperty(s.Name, s.PropertyType));
      Type t = System.Linq.Dynamic.DynamicExpression.CreateClass(d.ToArray());
      object ret = Activator.CreateInstance(t);
      foreach (var e in d)
        ret.SetProperty(e.Name, o.GetProperty(e.Name));
      return ret;
    }

    public static T GetProperty<T>(this object o, string p) {
      var t = o.GetType();
      System.Reflection.PropertyInfo pi = t.GetProperty(p);
      if (pi != null) return (T)pi.GetValue(o, null);
      System.Reflection.FieldInfo fi = t.GetField(p);
      if (fi != null) return (T)fi.GetValue(o);
      throw new NotImplementedException("Property/Field " + p + " is not implemented in " + o.GetType().Name + ".");
    }

    public static TReturn Invoke<TReturn>(this object o, string methodName, object[] parameters) {
      var t = o.GetType();
      var mi = t.GetMethod(methodName);
      if (mi != null) return (TReturn)mi.Invoke(o, parameters);
      throw new NotImplementedException("Property/Field " + methodName + " is not implemented in " + o.GetType().Name + ".");
    }
    public static void Invoke(this object o, string methodName, object[] parameters) {
      var t = o.GetType();
      var mi = t.GetMethod(methodName);
      if (mi != null) {
        mi.Invoke(o, parameters);
        return;
      }
      throw new NotImplementedException("Property/Field " + methodName + " is not implemented in " + o.GetType().Name + ".");
    }

    public static object GetProperty(this object o, string p) {
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if (pi != null) return pi.GetValue(o, null);
      System.Reflection.FieldInfo fi = o.GetType().GetField(p);
      if (fi != null) return fi.GetValue(o);
      throw new NotImplementedException("Property/Field " + p + " is not implemented in " + o.GetType().Name + ".");
    }

    public static double Mean(this IEnumerable<double> values) {
      var vs = values.OrderBy(v => v).ToList();
      return (vs.LastByCount() - vs[0]) / 2;
    }
    public static double Deviation(IEnumerable<double> Values, DeviationType CalculationType) {
      double SumOfValuesSquared = 0;
      double SumOfValues = 0;
      var count = Values.Count();
      //Calculate the sum of all the values
      foreach (double item in Values) {
        SumOfValues += item;
      }
      //Calculate the sum of all the values squared
      foreach (double item in Values) {
        SumOfValuesSquared += Math.Pow(item, 2);
      }
      if (CalculationType == DeviationType.Sample) {
        return Math.Sqrt((SumOfValuesSquared - Math.Pow(SumOfValues, 2) / count) / (count - 1));
      } else {
        return Math.Sqrt((SumOfValuesSquared - Math.Pow(SumOfValues, 2) / count) / count);
      }
    }
    public enum DeviationType {
      Population,
      Sample
    }

    public static double MeanAverage(this IList<double> values, int iterations = 3) {
      var valueLow = values.AverageByIterations(iterations, true);
      var valueHight = values.AverageByIterations(iterations, false);
      if (valueLow.Count == 0 && valueHight.Count == 0)
        return values.MeanAverage(iterations - 1);
      return values.Except(valueLow.Concat(valueHight)).DefaultIfEmpty(values.Average()).Average();
    }

    public static double StDevRatio(this ICollection<double> values) {
      var stDev = values.StDev();
      var range = values.Max() - values.Min();
      return stDev / range;
    }
    public static double StDev<T>(this ICollection<T> values, Func<T,int, double> value) {
      return values.Select((v,i) => value(v,i)).ToArray().StDev();
    }
    public static double StDev<T>(this ICollection<T> values, Func<T, double> value) {
      return values.Select(v => value(v)).ToArray().StDev();
    }
    public static double StDev<T>(this ICollection<T> values, Func<T, double?> value) {
      return values.Where(v => value(v).HasValue).Select(v => value(v).Value).ToArray().StDev();
    }
    public static double StDev(this ICollection<double> values) {
      double ret = 0;
      if (values.Count() > 0) {
        double avg = values.Average();
        double sum = values.Sum(d => (d - avg) * (d - avg));
        ret = Math.Sqrt(sum / (values.Count() - 1));
      }
      return ret;
    }

    public static double StandardDeviation(this List<double> doubleList) {
      double average = doubleList.Average();
      double sumOfDerivation = 0;
      doubleList.ForEach(v => sumOfDerivation += Math.Pow(v, 2));
      double sumOfDerivationAverage = sumOfDerivation / doubleList.Count;
      return Math.Sqrt(sumOfDerivationAverage - Math.Pow(average, 2));
    }

    public static double StDevP(this ICollection<double> n) {
      double total = 0, average = 0;

      foreach (double num in n) {
        total += num;
      }

      average = total / n.Count;

      double runningTotal = 0;

      foreach (double num in n) {
        runningTotal += ((num - average) * (num - average));
      }

      double calc = runningTotal / n.Count;
      double standardDeviationP = Math.Sqrt(calc);

      return standardDeviationP;
    }

    public static void LinearRegression_(double[] valuesX, double[] valuesY, out double a, out double b) {
      double xAvg = 0;
      double yAvg = 0;
      for (int x = 0; x < valuesY.Length; x++) {
        xAvg += valuesX[x];
        yAvg += valuesY[x];
      }
      xAvg = xAvg / valuesY.Length;
      yAvg = yAvg / valuesY.Length;
      double v1 = 0;
      double v2 = 0;
      for (int x = 0; x < valuesY.Length; x++) {
        v1 += (x - xAvg) * (valuesY[x] - yAvg);
        v2 += Math.Pow(x - xAvg, 2);
      }
      a = v1 / v2;
      b = yAvg - a * xAvg;
      //Console.WriteLine("y = ax + b");
      //Console.WriteLine("a = {0}, the slope of the trend line.", Math.Round(a, 2));
      //Console.WriteLine("b = {0}, the intercept of the trend line.", Math.Round(b, 2));

    }

    public static void LinearRegression_(double[] values, out double a, out double b) {
      double xAvg = 0;
      double yAvg = 0;
      for (int x = 0; x < values.Length; x++) {
        xAvg += x;
        yAvg += values[x];
      }
      xAvg = xAvg / values.Length;
      yAvg = yAvg / values.Length;
      double v1 = 0;
      double v2 = 0;
      for (int x = 0; x < values.Length; x++) {
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

    public class CmaWalker:Models.ModelBase {
      public double Current { get; set; }
      public double Difference { get { return Prev - Last; } }
      public double Last { get { return CmaArray.Last().GetValueOrDefault(); } }
      public double Prev { get { return CmaArray.Length == 1 ? Current : CmaArray[CmaArray.Length - 2].GetValueOrDefault(); } }
      public double Max { get { return CmaArray.Concat(new double?[] { Current }).Max().GetValueOrDefault(); } }
      public double Min { get { return CmaArray.Concat(new double?[] { Current }).Min().GetValueOrDefault(); } }
      public double?[] CmaArray;
      public CmaWalker(int cmaCount) {
        if (cmaCount < 1) throw new ArgumentException("Array length must be more then zero.", "cmaCount");
        this.CmaArray = new double?[cmaCount];
      }
      public void Add(double value, double cmaPeriod) {
        Current = value;
        for (var i = 0; i < CmaArray.Length; i++) {
          CmaArray[i] = Lib.Cma(CmaArray[i], cmaPeriod, value);
          value = CmaArray[i].Value;
        }
        RaisePropertyChanged("Difference");
        RaisePropertyChanged("Current");
        RaisePropertyChanged("Last");
        RaisePropertyChanged("Prev");
      }
      public void Clear() {
        for (var i = 0; i < CmaArray.Length; i++)
          CmaArray[i] = null;
      }
      public void Reset(int newCmaCount) {
        var offset = newCmaCount - CmaArray.Length;
        if( offset == 0 )return;
        if (offset > 0)
          CmaArray = CmaArray.Concat(new double?[newCmaCount - CmaArray.Length]).ToArray();
        else CmaArray = CmaArray.Take(newCmaCount).ToArray();
      }
      public double Diff(double current) { return current - CmaArray[0].Value; }
      public double[] Diffs() {
        var diffs = new List<double>();
        for (var i = 1; i < CmaArray.Length; i++)
          diffs.Add((CmaArray[i-1].GetValueOrDefault() - CmaArray[i]).GetValueOrDefault());
        return diffs.ToArray();
      }
      public double FromEnd(int position) {
        return CmaArray.Reverse().Take(position+1).Last().Value;
      }
    }


    public static TimeSpan FromSeconds(this int i) { return TimeSpan.FromSeconds(i); }
    public static TimeSpan FromSeconds(this double i) { return TimeSpan.FromSeconds(i); }
    public static TimeSpan FromMinutes(this int i) { return TimeSpan.FromMinutes(i); }
    public static TimeSpan FromMinutes(this double i) { return TimeSpan.FromMinutes(i); }

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
    public static double IfNaN(this double d, double defaultValue) {
      return double.IsNaN(d) ? defaultValue : d;
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
    public static DateTime Max(this DateTime d1, DateTime d2) {
      return d1 >= d2 ? d1 : d2;
    }
    public static DateTime Min(this DateTime d1, DateTime d2) {
      return d1 <= d2 ? d1 : d2;
    }
    public static double? Abs(this double? v) {
      return v.HasValue ? v.Value.Abs() : (double?)null;
    }
    public static double Abs(this double v) {
      return Math.Abs(v);
    }
    public static int Abs(this int v) {
      return Math.Abs(v);
    }
    public static double Sign(this double v) {
      return Math.Sign(v);
    }
    public static double Max(this double v, double other) {
      return double.IsNaN(v) ? other : double.IsNaN(other) ? v : Math.Max(v, other);
    }
    public static double Max(this double v, params double[] other) {
      return other.Aggregate(v, (p, n) => p.Max(n));
    }
    public static int Max(this int v, int other) {
      return Math.Max(v, other);
    }
    public static int Max(this int v, params int[] other) {
      return other.Aggregate(v, (p, n) => p.Max(n));
    }
    public static double Min(this double v, double other) {
      return double.IsNaN(v) ? other : double.IsNaN(other) ? v : Math.Min(v, other);
    }
    public static double Min(this double v,params double[] other) {
      return other.Aggregate(v, (p, n) => p.Min(n));
    }
    public static int Min(this int v, int other) {
      return Math.Min(v, other);
    }
    public static int Min(this int v, params int[] other) {
      return other.Aggregate(v, (p, n) => p.Min(n));
    }

    public static double Round(this double v,int decimals) { return Math.Round(v,decimals); }
    public static double? Round(this double? v,int decimals) { return v.HasValue ? v.Value.Round(decimals) : (double?)null; }

    public static double Error(this double experimantal, double original) {
      return ((experimantal - original).Abs() / original).Abs();
    }


    static double GetTextBoxTextDouble(TextBox TextBox) { return double.Parse("0" + GetTextBoxText(TextBox)); }
    public static int GetTextBoxTextInt(TextBox TextBox) {
      var t = GetTextBoxText(TextBox);
      int i;
      if( !int.TryParse(t,out i) )throw new FormatException(t+" is not an integer.");
      return i; 
    }
    public static string GetTextBoxText(TextBox TextBox) {
      return TextBox.Dispatcher.Invoke(
        DispatcherPriority.Input,
        (DispatcherOperationCallback)delegate(object o) { return TextBox.Text; },
        null
      ) + "";
    }
    public static string SetTextBoxText(TextBox TextBox,string Text) {
      TextBox.Dispatcher.BeginInvoke(
        DispatcherPriority.Background,
        (DispatcherOperationCallback)delegate(object o) { TextBox.Text = Text; return null; },
        null
      );
      return Text;
    }
    public static void SetChecked(CheckBox CheckBox, bool IsChecked) {
      SetChecked(CheckBox, IsChecked, false);
    }
    public static void SetChecked(CheckBox CheckBox, bool IsChecked,bool Force) {
      var d = (DispatcherOperationCallback)delegate(object o) { CheckBox.IsChecked = IsChecked; return null; };
      if( Force )
        CheckBox.Dispatcher.Invoke(DispatcherPriority.Send, d, null);
      else
      CheckBox.Dispatcher.BeginInvoke(DispatcherPriority.Send, d, null);
    }
    public static bool? GetChecked(CheckBox CheckBox) {
      return (bool?)
      CheckBox.Dispatcher.Invoke(
        DispatcherPriority.Input,
        (DispatcherOperationCallback)delegate(object o) { return CheckBox.IsChecked; },
        null
      );
    }
  }

  #region Extentions
  public static class Extentions {

    public static string PropertiesToString(this object o, string separator) {
      List<string> props = new List<string>();
      foreach (var prop in o.GetType().GetProperties().OrderBy(p => p.Name))
        props.Add(prop.Name + ":" + prop.GetValue(o, new object[0]));
      return string.Join(separator, props);
    }


    #region TimeSpan
    public static TimeSpan Max(this IEnumerable<TimeSpan> span) {
      return TimeSpan.FromMilliseconds(span.Max(s => s.TotalMilliseconds));
    }
    public static TimeSpan Average(this IEnumerable<TimeSpan> span) {
      return TimeSpan.FromMilliseconds(span.Average(s => s.TotalMilliseconds));
    }
    public static TimeSpan Multiply(this TimeSpan span, TimeSpan d) {
      return TimeSpan.FromMilliseconds(span.TotalMilliseconds * d.TotalMilliseconds);
    }
    public static TimeSpan Multiply(this TimeSpan span, double d) {
      return TimeSpan.FromMilliseconds(span.TotalMilliseconds * d);
    }
    public static TimeSpan Divide(this TimeSpan span, TimeSpan d) {
      return TimeSpan.FromMilliseconds(span.TotalMilliseconds / d.TotalMilliseconds);
    }
    #endregion

    public static T FirstOrLast<T>(this IEnumerable<T> e, bool last) {
      return last ? e.Last() : e.First();
    }
    public static double AverageHeight(this IEnumerable<double> values) {
      return values.Skip(1).Select((d, i) => Math.Abs(d - values.ElementAt(i))).Average();
    }

    public static double Position(this double Price,double Up,double Down){
      return (Price-Down)/(Up-Down);
    }
  }

  #endregion

  public struct LineAndTime {
    public double Value;
    public DateTime Time;
    public LineAndTime(double value,DateTime time) {
      Value = value;
      Time = time;
    }
  }
}
