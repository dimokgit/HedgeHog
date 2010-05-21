using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Data;
using HedgeHog.Alice.Client.TradeExtenssions;

namespace HedgeHog.Alice.Client {
  [ValueConversion(typeof(object), typeof(TradeUnKNown))]
  public class ObjectToTradeUnKnownConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      return value as TradeUnKNown;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
      throw new NotImplementedException();
    }
  }
}
