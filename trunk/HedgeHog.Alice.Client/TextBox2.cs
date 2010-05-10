using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;

namespace HedgeHog.Alice.Client {
  class TextBox2:TextBox {

    public string Text2 {
      get { return (string)GetValue(Text2Property); }
      set { SetValue(Text2Property, value); }
    }

    public static readonly DependencyProperty Text2Property =
        DependencyProperty.Register("Text2", typeof(string), typeof(TextBox2));

    

    protected override void OnTextChanged(TextChangedEventArgs e) {
      base.OnTextChanged(e);
      Text2 = Text;
    }
  }
}
