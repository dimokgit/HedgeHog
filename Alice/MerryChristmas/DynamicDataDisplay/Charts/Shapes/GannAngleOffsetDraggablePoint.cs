using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Research.DynamicDataDisplay.Charts.Shapes;
using Microsoft.Research.DynamicDataDisplay.Charts;
using System.Windows;
using Microsoft.Research.DynamicDataDisplay;
using System.Windows.Data;
using System.Globalization;

namespace HedgeHog.Charter {
  public class GannAngleOffsetDraggablePoint :DraggablePoint{
    public IValueConverter NumberToStringConverter { get; set; }
    Func<double,DateTime> ConvertFromDouble;
    #region Anchor
    public Point Anchor {
      get { return (Point)GetValue(AnchorProperty); }
      set { SetValue(AnchorProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Anchor.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty AnchorProperty =
        DependencyProperty.Register("Anchor", typeof(Point), typeof(GannAngleOffsetDraggablePoint), new UIPropertyMetadata(new Point(), (o, dpe) => {
          RefreshTooltip(o);
        }));

    #endregion
    public TimeSpan BarPeriod { get; set; }
    public double Angle {
      get {
        return GetAngleByPosition(Position);
      }
    }

    public double GetOffset(Point curernt, Point previous) {
      return GetAngleByPosition(curernt) - GetAngleByPosition(previous);
    }
    public double GetAngleByPosition(Point position) {
      if (BarPeriod.Ticks == 0) return 0;
      var bars = (ConvertFromDouble(position.X) - ConvertFromDouble(Anchor.X)).Ticks / BarPeriod.Ticks;
      return bars == 0 ? double.MaxValue : (position.Y - Anchor.Y) / bars;
    }
    public GannAngleOffsetDraggablePoint(Func<double,DateTime> convertFromDouble,IValueConverter numberToStringConverter) {
      this.ConvertFromDouble = convertFromDouble;
      this.NumberToStringConverter = numberToStringConverter;

      SetBinding(ToolTipProperty, new Binding("Angle") { Source = this, Converter = NumberToStringConverter });
      PositionChanged += new EventHandler<PositionChangedEventArgs>(GannAngleOffsetDraggablePoint_PositionChanged);
    }

    void GannAngleOffsetDraggablePoint_PositionChanged(object sender, PositionChangedEventArgs e) {
      RefreshTooltip(this);      
    }
    private static void RefreshTooltip(DependencyObject o) {
      BindingOperations.GetBindingExpression(o, ToolTipProperty).UpdateTarget();
    }
  }

}
