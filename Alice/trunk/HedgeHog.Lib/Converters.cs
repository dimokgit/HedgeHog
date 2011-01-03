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
    private static readonly CompareValueConverter defaultInstance = new CompareValueConverter();
    public static CompareValueConverter Default { get { return defaultInstance; } }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      var parameters = (parameter+"").Split(new[]{'|'}, StringSplitOptions.RemoveEmptyEntries);
      if( parameters.Length < 3)return null;
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
  public class PassThroughConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      return value;
    }
  }
}
