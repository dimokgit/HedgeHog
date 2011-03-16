using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;
using System.Globalization;
using System.ComponentModel;

namespace HedgeHog {
  public static class TrueFalseColors {
    public static string False = Colors.LightPink + "";
    public static string True = "#3A98EF71";
  }

  [ValueConversion(typeof(string), typeof(DateTime?))]
  public class DateTimeConverter : IValueConverter {
    #region IValueConverter Members
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      if( (value+"") == "" )return null;
      DateTime d;
      if (!DateTime.TryParse(value + "", out d)) return value;
      if (d.Date == DateTime.MinValue) return DateTime.Today + d.TimeOfDay;
      return value;
    }

    #endregion
  }


  public class AnyToVisibilityConverter : IValueConverter {
    private static readonly AnyToVisibilityConverter defaultInstance = new AnyToVisibilityConverter();
    public static AnyToVisibilityConverter Default { get { return defaultInstance; } }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="value"></param>
    /// <param name="targetType"></param>
    /// <param name="parameter"></param>
    /// <param name="culture"></param>
    /// <returns></returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      var parameters = (parameter + "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
      if (parameters.Count < 2) {
        parameters.Clear();
        parameters.AddRange(new[] { Visibility.Collapsed + "", Visibility.Collapsed + "", Visibility.Visible + "" });
      }
      bool b;
      if (bool.TryParse(value + "", out b)) {
        Visibility visGood = Visibility.Visible;
        Enum.TryParse<Visibility>(b ? parameters[2] : parameters[1], out visGood);
        return visGood;
      }
      Visibility visBad = Visibility.Visible;
      Enum.TryParse<Visibility>(parameters[0], out visBad);
      return visBad;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      throw new NotImplementedException();
    }
  }

  public class NumberToStringAutoFormatConverter : IValueConverter {
    private static readonly NumberToStringAutoFormatConverter defaultInstance = new NumberToStringAutoFormatConverter();
    public static NumberToStringAutoFormatConverter Default { get { return defaultInstance; } }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      double d;
      if (typeof(double) == targetType) d = (double)value;
      else if (!double.TryParse(value + "", out d)) return value;
      var precision = 1;
      if (!string.IsNullOrWhiteSpace(parameter + ""))
        int.TryParse(parameter + "", out precision);
      return string.Format("{0:n" + (int)Math.Ceiling((Math.Abs(Math.Log10(Math.Abs(d)))) + precision) + "}", d);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      throw new NotImplementedException();
    }
  }


  public class CompareValueConverter : IValueConverter {
    static string[] trueFalseDefaultColors = new[] { TrueFalseColors.True, TrueFalseColors.False };
    private static readonly CompareValueConverter defaultInstance = new CompareValueConverter();
    public static CompareValueConverter Default { get { return defaultInstance; } }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      var parameters = (parameter+"").Split(new[]{'|'}, StringSplitOptions.RemoveEmptyEntries).ToList();
      if( parameters.Count < 1)return null;
      if (parameters.Count == 1) parameters.AddRange(trueFalseDefaultColors);
      var ret = false;
      if (!targetType.IsValueType)
        ret = (value + "") == parameters[0];
      else {
        var tc = TypeDescriptor.GetConverter(targetType);
        if (tc.IsValid(parameters[0])) {
          var compareTo = tc.ConvertFromString(parameters[0]);
          ret = value == compareTo;
        }
      }
      return ret ? parameters[2] : parameters[1];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      throw new NotImplementedException();
    }
  }

  [ValueConversion(typeof(bool?), typeof(Color))]
  public class BoolToColorConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      var colors = (parameter + "").Split('|');//.Select(r => (Colors)Enum.Parse(typeof(Colors), r, true)).ToArray();
      var color = value == null ? colors[0] : (bool)value ? colors[2] : colors[1];
      return color;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      throw new NotImplementedException();
    }
  }
  [ValueConversion(typeof(bool), typeof(string))]
  public class BoolToSrtingConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      var colors = (parameter + "").Split('|');//.Select(r => (Colors)Enum.Parse(typeof(Colors), r, true)).ToArray();
      var color = value == null ? colors[0] : (bool)value ? colors[2] : colors[1];
      return color;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      throw new NotImplementedException();
    }
  }

  [ValueConversion(typeof(double?), typeof(Color))]
  public class DoubleToColorConverter : IValueConverter {
    private static readonly DoubleToColorConverter defaultInstance = new DoubleToColorConverter();
    public static DoubleToColorConverter Default { get { return defaultInstance; } }
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      var colors = (parameter + "").Split('|');//.Select(r => (Colors)Enum.Parse(typeof(Colors), r, true)).ToArray();
      var d = value is double? ? (double?)value : System.Convert.ToDouble(value);
      var color = d.GetValueOrDefault() == 0 ? colors[0] : d > 0 ? colors[2] : colors[1];
      return color;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      throw new NotImplementedException();
    }
  }


  [ValueConversion(typeof(object), typeof(object))]
  public class TemplatedParentDataContextConverter : IValueConverter {
    static TemplatedParentDataContextConverter _Default;
    public static TemplatedParentDataContextConverter Default {
      get {
        if (TemplatedParentDataContextConverter._Default == null) 
          TemplatedParentDataContextConverter._Default = new TemplatedParentDataContextConverter();
        return TemplatedParentDataContextConverter._Default;
      }
    }
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      var fe = value as FrameworkElement;
      if (fe == null) return value;
      fe = fe.TemplatedParent as FrameworkElement;
      if (fe == null) return value;
      return fe.DataContext;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      return value;
    }
  }

  [ValueConversion(typeof(object), typeof(object))]
  public class PassThroughConverter : IValueConverter {
    static PassThroughConverter _Default;
    public static PassThroughConverter Default {
      get {
        if (PassThroughConverter._Default == null) PassThroughConverter._Default = new PassThroughConverter();
        return PassThroughConverter._Default;
      }
    }
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      return value;
    }
  }
}
