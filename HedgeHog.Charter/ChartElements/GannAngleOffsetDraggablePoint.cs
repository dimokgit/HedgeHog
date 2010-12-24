using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Research.DynamicDataDisplay.Charts.Shapes;
using Microsoft.Research.DynamicDataDisplay.Charts;
using System.Windows;
using Microsoft.Research.DynamicDataDisplay;
using System.Windows.Data;

namespace HedgeHog.Charter {
  public class GannAngleOffsetDraggablePoint :DraggablePoint{
    Func<double,DateTime> convertFromDouble;
    #region Anchor
    public Point Anchor {
      get { return (Point)GetValue(AnchorProperty); }
      set { SetValue(AnchorProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Anchor.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty AnchorProperty =
        DependencyProperty.Register("Anchor", typeof(Point), typeof(GannAngleOffsetDraggablePoint), new UIPropertyMetadata(new Point(), (o, dpe) => {
          o.InvalidateProperty(ToolTipProperty);
        }));
    #endregion
    public TimeSpan BarPeriod;
    public double AngleOffset {
      get {
        var bars = (convertFromDouble(Position.X) - convertFromDouble(Anchor.X)).Ticks / BarPeriod.Ticks;
        return (Position.Y - Anchor.Y) / bars;
      }
    }
    public GannAngleOffsetDraggablePoint() {
      Init();
    }
    void Init() {
      var plotter = (Parent as ChartPlotter).Children.OfType<DateTimeAxis>().First();
      convertFromDouble = plotter.ConvertFromDouble;
      SetBinding(ToolTipProperty, new Binding("AngleOffset") { Source = this });
         
    }
  }
}
