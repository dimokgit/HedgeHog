using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Research.DynamicDataDisplay.Charts;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;

namespace HedgeHog.Charter {
  public class ColoredSegment :Segment{

    protected override void OnInitialized(EventArgs e) {
      base.OnInitialized(e);
      var color = (Stroke as SolidColorBrush).Color;
      SetBinding(StrokeProperty, new Binding("SelectedValue") {
        Source = this, Converter = DoubleToColorConverter.Default, ConverterParameter = SelectedColor + "|" + color + "|" + color
      });
    }

    internal Color originalColor;


    public Color SelectedColor {
      get { return (Color)GetValue(SelectedColorProperty); }
      set { SetValue(SelectedColorProperty, value); }
    }

    // Using a DependencyProperty as the backing store for SelectedColor.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register("SelectedColor", typeof(Color), typeof(ColoredSegment), new UIPropertyMetadata((o, dpe) => {
        }));

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
      base.OnPropertyChanged(e);
      if (e.Property == Segment.StrokeProperty && e.OldValue != e.NewValue)
        originalColor = (e.OldValue as SolidColorBrush).Color;
    }


    // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty MyPropertyProperty =
        DependencyProperty.Register("MyProperty", typeof(int), typeof(ColoredSegment), new UIPropertyMetadata(0));

    

    public double SelectedValue {
      get { return (double)GetValue(SelectedValueProperty); }
      set { SetValue(SelectedValueProperty, value); }
    }

    // Using a DependencyProperty as the backing store for SelectedValue.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register("SelectedValue", typeof(double), typeof(ColoredSegment), new UIPropertyMetadata(-1.0, (o, dpe) => {
        }));



    public double? SelectValue {
      get { return (double)GetValue(SelectValueProperty); }
      set { SetValue(SelectValueProperty, value); }
    }

    // Using a DependencyProperty as the backing store for SelectValue.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty SelectValueProperty =
        DependencyProperty.Register("SelectValue", typeof(double), typeof(Segment), new UIPropertyMetadata(null));


  }
}
