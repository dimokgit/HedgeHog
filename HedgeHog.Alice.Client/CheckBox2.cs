using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;

namespace HedgeHog.Alice.Client {
  class CheckBox2:CheckBox {

    public bool? IsChecked2 {
      get { return (bool?)GetValue(Text2Property); }
      set { SetValue(Text2Property, value); }
    }

    public static readonly DependencyProperty Text2Property =
        DependencyProperty.Register("IsChecked2", typeof(bool?), typeof(CheckBox2));



    protected override void OnChecked(RoutedEventArgs e) {
      base.OnChecked(e);
      IsChecked2 = IsChecked;
    }
  }
}
