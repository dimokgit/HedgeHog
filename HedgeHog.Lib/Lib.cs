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

namespace ControlExtentions {
  public static class AAA {
    public static void ResetText(this ComboBox ComboBox) {
      var t = ComboBox.Text; ComboBox.Text = ""; ComboBox.Text = t;
    }
  }
}
namespace HedgeHog {
  public static class Lib {
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

    public static object GetProperty(this object o, string p) {
      System.Reflection.PropertyInfo pi = o.GetType().GetProperty(p);
      if (pi != null) return pi.GetValue(o, null);
      System.Reflection.FieldInfo fi = o.GetType().GetField(p);
      if (fi != null) return fi.GetValue(o);
      throw new NotImplementedException("Property/Field " + p + " is not implemented in " + o.GetType().Name + ".");
    }

    public static double[] Regress(double[] prices, int polyOrder) {
      var coeffs = new[] { 0.0, 0.0 };
      Lib.LinearRegression(prices, out coeffs[1], out coeffs[0]);
      return coeffs;
      //return Regression.Regress(prices, polyOrder);
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
    public static double StdDev<T>(this IEnumerable<T> values, Func<T, double?> value) {
      return values.Where(v=>value(v).HasValue).Select(v=>value(v).Value).ToArray().StdDev();
    }
    public static double StdDev(this double[] values) {
      double ret = 0;
      if (values.Count() > 0) {
        double avg = values.Average();
        double sum = values.Sum(d => (d - avg) * (d - avg));
        ret = Math.Sqrt(sum / (values.Count() - 1));
      }
      return ret;
    }

    public static double FibRatioSign(double d1, double d2) { return d1 / d2 - d2 / d1; }
    public static double FibRatio(double d1, double d2) { return Math.Abs(d1 / d2 - d2 / d1); }
    public static double StandardDeviation(List<double> doubleList) {
      double average = doubleList.Average();
      double sumOfDerivation = 0;
      doubleList.ForEach(v => sumOfDerivation += Math.Pow(v, 2));
      double sumOfDerivationAverage = sumOfDerivation / doubleList.Count;
      return Math.Sqrt(sumOfDerivationAverage - Math.Pow(average, 2));
    }

    public static void LinearRegression(double[] values, out double a, out double b) {
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
        v2 += Math.Pow(x - xAvg, 2);
      }
      a = v1 / v2;
      b = yAvg - a * xAvg;
      //Console.WriteLine("y = ax + b");
      //Console.WriteLine("a = {0}, the slope of the trend line.", Math.Round(a, 2));
      //Console.WriteLine("b = {0}, the intercept of the trend line.", Math.Round(b, 2));

    }
    public static double CMA(double? MA, double Periods, double NewValue) {
      if (!MA.HasValue) return NewValue;// Else CMA = MA + (NewValue - MA) / (Periods + 1)
      return MA.Value + (NewValue - MA.Value) / (Periods + 1);
    }
    public static double CMA(double MA,double zeroValue, double Periods, double NewValue) {
      if (MA == zeroValue) return NewValue;// Else CMA = MA + (NewValue - MA) / (Periods + 1)
      return CMA((double?)MA, Periods, NewValue);
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
    public static DateTime Max(DateTime d1, DateTime d2) {
      return d1 > d2 ? d1 : d2;
    }
    public static DateTime Min(DateTime d1, DateTime d2) {
      return d1 < d2 ? d1 : d2;
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

    public static double Round(this double v,int decimals) { return Math.Round(v,decimals); }
    public static double? Round(this double? v,int decimals) { return v.HasValue ? v.Value.Round(decimals) : (double?)null; }

    public static TimeSpan FromMinutes(this double number){ return TimeSpan.FromMinutes(number); }
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
    public static TimeSpan Multiply(this TimeSpan span, double d) {
      return TimeSpan.FromMilliseconds(span.TotalMilliseconds * d);
    }
    #endregion

    public static T FirstOrLast<T>(this IEnumerable<T> e, bool last) {
      return last ? e.Last() : e.First();
    }
    public static double AverageHeight(this IEnumerable<double> values) {
      return values.Skip(1).Select((d, i) => Math.Abs(d - values.ElementAt(i))).Average();
    }
    public static int Floor(this double d) { return (int)Math.Floor(d); }
    public static int Ceiling(this double d) { return (int)Math.Ceiling(d); }
    public static int ToInt(this double d) { return (int)Math.Round(d, 0); }
    public enum RoundTo { Second, Minute, Hour, Day }
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
        case RoundTo.Day:
          dtRounded = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0);
          if (d.Hour >= 12) dtRounded = dtRounded.AddDays(1);
          break;
      }
      return dtRounded;
    }
    public static DateTime Round(this DateTime dt) { return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0); }
    public static DateTime Round_(this DateTime dt) { return dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond); }
    public static DateTime Round(this DateTime dt,int period) {
      dt = dt.Round();
      var evenMinutes = dt.Minute / period * period;
      return dt.AddMinutes(-dt.Minute).AddMinutes(evenMinutes);
    }
    public static double? Cma(this double? d, int period, double? newValue) { return Lib.CMA(d, period, newValue.Value); }

    #region Between
    public static bool Between(this int value, double d1, double d2) {
      return Math.Min(d1, d2) <= value && value <= Math.Max(d1, d2);
    }
    public static bool Between(this double value, double d1, double d2) {
      return Math.Min(d1, d2) <= value && value <= Math.Max(d1, d2);
    }
    public static bool Between(this DateTime value, DateTime d1, DateTime d2) {
      return d1 <= d2 ? d1 <= value && value <= d2 : d2 <= value && value <= d1;
    }
    public static bool Between(this TimeSpan value, TimeSpan d1, TimeSpan d2) {
      return d1 < d2 ? d1 <= value && value <= d2 : d1 <= value || value <= d2;
    }
    #endregion

    public static double Position(this double Price,double Up,double Down){
      return (Price-Down)/(Up-Down);
    }
    public static void SetProperty(this object o, string p, object v) {
      var convert = new Func<object, Type, object>((valie, type) => {
        if (valie != null) {
          Type tThis = Nullable.GetUnderlyingType(type);
          if (tThis == null) tThis = type;
          valie = Convert.ChangeType(v, tThis, null);
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