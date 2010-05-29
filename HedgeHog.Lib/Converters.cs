using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

namespace HedgeHog {
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
      var d = (double?)value;
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
