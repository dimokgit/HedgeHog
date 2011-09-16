using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using C=System.Windows.Controls;
using System.Windows;
using System.Windows.Controls;

namespace HedgeHog.UI {
  public class TextBox : C.TextBox {
    public bool isTextBeingUpdeted;

    public string Text2 {
      get { return (string)GetValue(Text2Property); }
      set { SetValue(Text2Property, value); }
    }

    public static readonly DependencyProperty Text2Property =
        DependencyProperty.Register("Text2", typeof(string), typeof(TextBox), new PropertyMetadata((a, b) => {
          var tb2 = a as TextBox;
          if (!tb2.isTextBeingUpdeted) {
            tb2.Text = b.NewValue + "";
            var be = tb2.GetBindingExpression(TextBox.TextProperty);
            if(be!=null)be.UpdateSource();
          }
        }));



    protected override void OnTextChanged(C.TextChangedEventArgs e) {
      base.OnTextChanged(e);
      isTextBeingUpdeted = true;
      Text2 = Text;
      isTextBeingUpdeted = false;
    }
  }
}
