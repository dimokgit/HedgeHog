using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace HedgeHog {

  [ValueConversion(typeof(string), typeof(DateTime?))]
  public class DateTimeConverter : IValueConverter {
    #region IValueConverter Members
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      if( (value+"") == "" )return null;
      DateTime d;
      if (!DateTime.TryParse(value + "", out d)) return value;
      if (d.Date == DateTime.MinValue) return DateTime.Today + d.TimeOfDay;
      return value;
    }

    #endregion
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
