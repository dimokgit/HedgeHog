using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Globalization;

namespace HedgeHog.Alice.Client {


  public class ProfitToColorConverter : IValueConverter {
    private static readonly ProfitToColorConverter defaultInstance = new ProfitToColorConverter();

    public static ProfitToColorConverter Default { get { return defaultInstance; } }


    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      var colors = (parameter + "").Split(new[]{'|'}, StringSplitOptions.RemoveEmptyEntries);//.Select(r => (Colors)Enum.Parse(typeof(Colors), r, true)).ToArray();
      if (colors.Length == 0) colors = new string[] { Colors.Transparent + "", TrueFalseColors.False, TrueFalseColors.True };

      if (value == null) return colors[0];

      if (value is bool?)
        return ((bool?)value).Value ? colors[2] : colors[1];

      if (value is bool) 
        return (bool)value ? colors[2] : colors[1];

      var d = value is double? ? (double?)value : System.Convert.ToDouble(value);
      return d.GetValueOrDefault() == 0 ? colors[0] : d > 0 ? colors[2] : colors[1];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      throw new NotImplementedException();
    }
  }


}
