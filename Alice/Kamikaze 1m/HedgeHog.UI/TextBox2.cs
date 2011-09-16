using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;

namespace HedgeHog.UI {
  public class TextBox2 : System.Windows.Controls.TextBox {
    public bool isTextBeingUpdeted;

    public string Text2 {
      get { return (string)GetValue(Text2Property); }
      set { SetValue(Text2Property, value); }
    }

    public static readonly DependencyProperty Text2Property =
        DependencyProperty.Register("Text2", typeof(string), typeof(TextBox2), new PropertyMetadata((a, b) => {
          var tb2 = a as TextBox2;
          if (!tb2.isTextBeingUpdeted)
            tb2.Text = b.NewValue + "";
        }));



    protected override void OnTextChanged(TextChangedEventArgs e) {
      base.OnTextChanged(e);
      isTextBeingUpdeted = true;
      Text2 = Text;
      var b = GetBindingExpression(Text2Property);
      if(b!=null)
        b.UpdateSource();
      isTextBeingUpdeted = false;
    }
  }
}
