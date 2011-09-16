using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;

namespace HedgeHog.Alice.Client {
  public class DataGridStyleSelector : StyleSelector {
    public override Style SelectStyle(object item,
        DependencyObject container) {
      var dgr = container as DataGridRow;
      var db = new Binding("GrossPL");
      db.Converter = new ProfitToColorConverter();
      db.ConverterParameter = Colors.Transparent + "|" + Colors.Pink + "|" + Colors.LightGreen;
      db.Source = item;
      dgr.SetBinding(DataGridRow.BackgroundProperty, db);
      return null;
    }
  }

}
