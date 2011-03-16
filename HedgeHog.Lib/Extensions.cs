using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace HedgeHog {
  public static class DependencyObjectExtensions {
    public static TParent GetParent<TParent>(this DependencyObject dp) where TParent : DependencyObject {
      if (dp == null) return null;
      var fwParent = dp is FrameworkElement ? ((FrameworkElement)dp).Parent : null;
      if (fwParent != null && fwParent is TParent) return fwParent as TParent;
      var vtParent = VisualTreeHelper.GetParent(dp);
      if (vtParent != null && vtParent is TParent) return vtParent as TParent;
      return GetParent<TParent>(fwParent) ?? GetParent<TParent>(vtParent);
    }
  }
}
