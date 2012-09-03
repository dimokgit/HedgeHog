using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Research.DynamicDataDisplay.Charts;
using Microsoft.Research.DynamicDataDisplay.Charts.Shapes;
using System.Windows;
using System.Diagnostics;
using System.Windows.Data;

namespace HedgeHog.Charter {
  public static class DraggableManager {
    public static void SetAnchor(this HorizontalLine line, DraggablePoint draggablePoint) {
      var b = new Binding() { Path = new PropertyPath("Position.Y"), Source = draggablePoint };
      line.SetBinding(HorizontalLine.ValueProperty, b);
      line.SetBinding(HorizontalLine.ToolTipProperty, b);
    }

    public static void SetAnchor(this VerticalLine line, DraggablePoint draggablePoint) {
      var b = new Binding() { Path = new PropertyPath("Position.X"), Source = draggablePoint };
      line.SetBinding(VerticalLine.ValueProperty, b);
      line.SetBinding(VerticalLine.ToolTipProperty, b);
    }
  }
}
