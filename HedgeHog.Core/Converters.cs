using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace HedgeHog {
  [ValueConversion(typeof(object), typeof(string))]
  public class StringFormatConverter : IValueConverter {
    private static readonly StringFormatConverter defaultInstance = new StringFormatConverter();
    public static StringFormatConverter Default { get { return defaultInstance; } }
    private static string _customPatternt = "[[][[](.)[]][]]";
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        return DependencyProperty.UnsetValue;
      if (value == null)
        return null;
      var customFormat = Regex.Match(parameter + "", _customPatternt).Groups[1].Value;
      return string.IsNullOrWhiteSpace(customFormat)
        ? string.Format(culture, (string)parameter, value)
        : string.Format(new CustomStringFormat(), "{0:" + customFormat + "}", value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        return DependencyProperty.UnsetValue;
      if (value == null)
        return null;
      var customFormat = Regex.Match(parameter + "", _customPatternt).Groups[1].Value;
      return string.IsNullOrWhiteSpace(customFormat)
        ? value
        : string.Format(new CustomStringFormat(), "{0:" + customFormat + "}", value);
    }
  }
  public class CustomStringFormat : IFormatProvider, ICustomFormatter {
    public object GetFormat(Type formatType) {
      if (formatType == typeof(ICustomFormatter))
        return this;
      else
        return null;

    }
    public string Format(string format, object arg, IFormatProvider formatProvider) {
      string result = arg.ToString();

      switch (format.ToUpper()) {
        case "U": return result.ToUpper();
        case "L": return result.ToLower();
        //more custom formats
        default: return result;
      }
    }
  }
}
