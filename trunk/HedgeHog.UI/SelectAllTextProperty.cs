using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using C = System.Windows.Controls;

namespace HedgeHog.UI {
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Input;
  using System.Windows.Media;

  public class SelectTextOnFocus : DependencyObject {
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active",
        typeof(bool),
        typeof(SelectTextOnFocus),
        new PropertyMetadata(false, ActivePropertyChanged));

    private static void ActivePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
      if (d is C.TextBox) {
        C.TextBox textBox = d as C.TextBox;
        if ((e.NewValue as bool?).GetValueOrDefault(false)) {
          textBox.GotKeyboardFocus += OnKeyboardFocusSelectText;
          textBox.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
        } else {
          textBox.GotKeyboardFocus -= OnKeyboardFocusSelectText;
          textBox.PreviewMouseLeftButtonDown -= OnMouseLeftButtonDown;
        }
      }
    }

    private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      DependencyObject dependencyObject = GetParentFromVisualTree(e.OriginalSource);

      if (dependencyObject == null) {
        return;
      }

      var textBox = (C.TextBox)dependencyObject;
      if (!textBox.IsKeyboardFocusWithin) {
        textBox.Focus();
        e.Handled = true;
      }
    }

    private static DependencyObject GetParentFromVisualTree(object source) {
      DependencyObject parent = source as UIElement;
      while (parent != null && !(parent is C.TextBox)) {
        parent = VisualTreeHelper.GetParent(parent);
      }

      return parent;
    }

    private static void OnKeyboardFocusSelectText(object sender, KeyboardFocusChangedEventArgs e) {
      C.TextBox textBox = e.OriginalSource as C.TextBox;
      if (textBox != null) {
        textBox.SelectAll();
      }
    }

    [AttachedPropertyBrowsableForChildrenAttribute(IncludeDescendants = false)]
    [AttachedPropertyBrowsableForType(typeof(C.TextBox))]
    public static bool GetActive(DependencyObject @object) {
      return (bool)@object.GetValue(ActiveProperty);
    }

    public static void SetActive(DependencyObject @object, bool value) {
      @object.SetValue(ActiveProperty, value);
    }
  }
}
