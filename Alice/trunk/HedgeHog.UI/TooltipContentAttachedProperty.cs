using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace HedgeHog.UI {
  public class Tooltip {


    public static object GetContent(DependencyObject obj) {
      return (object)obj.GetValue(ContentProperty);
    }

    public static void SetContent(DependencyObject obj, object value) {
      obj.SetValue(ContentProperty, value);
    }

    // Using a DependencyProperty as the backing store for Content.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.RegisterAttached("Content", typeof(object), typeof(Tooltip), new UIPropertyMetadata(null, (o, dp) => {
          var fe = o as FrameworkElement;
          var tt = fe.ToolTip as ToolTip;
          if (tt == null) fe.ToolTip = tt = new ToolTip();
          tt.Content = dp.NewValue;
          tt.PlacementTarget = o as UIElement;
          tt.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
          tt.IsOpen = dp.NewValue + "" != "";
        }));
    
  }
}
