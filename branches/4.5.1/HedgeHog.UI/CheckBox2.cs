using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;

namespace HedgeHog.UI {
  public class CheckBox2:CheckBox {

    public bool? IsChecked2 {
      get { return (bool?)GetValue(IsChecked2Property); }
      set { SetValue(IsChecked2Property, value); }
    }

    public static readonly DependencyProperty IsChecked2Property =
        DependencyProperty.Register("IsChecked2", typeof(bool?), typeof(CheckBox2));


    protected override void OnUnchecked(RoutedEventArgs e) {
      base.OnUnchecked(e);
      IsChecked2 = IsChecked;
    }
    protected override void OnChecked(RoutedEventArgs e) {
      base.OnChecked(e);
      IsChecked2 = IsChecked;
    }
  }
}
