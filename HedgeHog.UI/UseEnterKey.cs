using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;

namespace HedgeHog.UI {
  public static class UseEnterKey {

    public static FocusNavigationDirection GetEnterKeyDirection(DependencyObject obj) {
      return (FocusNavigationDirection)obj.GetValue(EnterKeyDirectionProperty);
    }

    public static void SetEnterKeyDirection(DependencyObject obj, FocusNavigationDirection value) {
      obj.SetValue(EnterKeyDirectionProperty, value);
    }

    // Using a DependencyProperty as the backing store for EnterKeyDirection.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty EnterKeyDirectionProperty =
        DependencyProperty.RegisterAttached("EnterKeyDirection", typeof(FocusNavigationDirection), typeof(UseEnterKey), new UIPropertyMetadata((dp, e) => {
          var control = dp as Control;
          if (control == null || !control.Focusable) return;
          control.KeyDown -= new System.Windows.Input.KeyEventHandler(control_KeyDown);
          control.KeyDown += new System.Windows.Input.KeyEventHandler(control_KeyDown);
        }));

    static void control_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
      if (e.Key != Key.Enter) return;
      (sender as Control).MoveFocus(new TraversalRequest(GetEnterKeyDirection(sender as DependencyObject)));
    }

  }
}
