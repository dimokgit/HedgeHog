using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HedgeHog.Charter {
  /// <summary>
  /// Interaction logic for GraggableLine.xaml
  /// </summary>
  public partial class GraggableLine : UserControl {
    public GraggableLine() {
      InitializeComponent();
    }
  }
}
/*
<d3:ContentGraph x:Class="ChartExtensions.LineCursorGraph"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d3="http://research.microsoft.com/DynamicDataDisplay/1.0"
    xmlns:local="clr-namespace:ChartExtensions"
    IsHitTestVisible="false" Panel.ZIndex="1">
    <d3:ContentGraph.Resources>
        <Style x:Key="outerBorderStyle" TargetType="{x:Type Rectangle}" >
            <Setter Property="RadiusX" Value="10"/>
            <Setter Property="RadiusY" Value="10"/>
            <Setter Property="Stroke" Value="LightGray"/>
            <Setter Property="StrokeThickness" Value="1"/>
            <Setter Property="Fill" Value="#88FFFFFF"/>
        </Style>

        <Style x:Key="innerBorderStyle" TargetType="{x:Type Border}">
            <Setter Property="CornerRadius" Value="4"/>
            <!--<Setter Property="Background" Value="{Binding 
				RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type local:LineCursorGraph}},
				Path=LineStroke}"/>-->
            <Setter Property="Margin" Value="8,4,8,4"/>
        </Style>

        <Style x:Key="textStyle" TargetType="{x:Type TextBlock}">
            <Setter Property="Margin" Value="2,1,2,1"/>
            <Setter Property="Foreground" Value="{Binding 
				RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type local:LineCursorGraph}},
				Path=LineStroke}"/>
        </Style>

        <Style x:Key="lineStyle" TargetType="{x:Type Line}">
            <Setter Property="Stroke" Value="{Binding 
				RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type local:LineCursorGraph}},
				Path=LineStroke}"/>
            <Setter Property="StrokeThickness" Value="{Binding 
				RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type local:LineCursorGraph}},
				Path=LineStrokeThickness}"/>
            <Setter Property="StrokeDashArray" Value="{Binding 
				RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type local:LineCursorGraph}},
				Path=LineStrokeDashArray}"/>
            <Setter Property="IsHitTestVisible" Value="true"/>
        </Style>
    </d3:ContentGraph.Resources>
    <Canvas Name="content" Cursor="None" Background="Transparent" IsHitTestVisible="true">
        <Line Name="horizLine" Style="{StaticResource lineStyle}"/>
        <Line Name="vertLine" Style="{StaticResource lineStyle}"/>

        <Grid Name="horizGrid" Canvas.Top="10">
            <!--<Rectangle Style="{StaticResource outerBorderStyle}"/>-->
            <Border Style="{StaticResource innerBorderStyle}">
                <TextBlock Name="horizTextBlock" Style="{StaticResource textStyle}"/>
            </Border>
        </Grid>

        <Grid Name="vertGrid" Canvas.Left="5" Visibility="Collapsed">
            <Rectangle Style="{StaticResource outerBorderStyle}"/>
            <Border Style="{StaticResource innerBorderStyle}">
                <TextBlock Name="vertTextBlock" Style="{StaticResource textStyle}"/>
            </Border>
        </Grid>
    
    </Canvas>
</d3:ContentGraph>
    
    
C#:

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Microsoft.Research.DynamicDataDisplay.Charts;
using Microsoft.Research.DynamicDataDisplay.Common.Auxiliary;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Research.DynamicDataDisplay;
using System.Diagnostics;

namespace ChartExtensions
{
    /// <summary>
    /// Interaction logic for LineCursorGraph.xaml
    /// </summary>
   
        public partial class LineCursorGraph : ContentGraph
        {
            Cursor DefaultParentCursor
            {
                get
                {
                    return Cursors.Arrow;
                }
            }

            #region Enums
            public enum CursorType
            {
                CursorA,
                CursorB,
                Waveform,
                Slicer
            }

            public enum LineCursorOrientation
            {
                Vertical,
                Horizontal
            }
            #endregion

            #region Dependency Properties
            #region CursorType
            public static readonly DependencyProperty CursorTypeProperty = DependencyProperty.Register("CursorType",
                typeof(CursorType), typeof(LineCursorGraph),
                new PropertyMetadata(CursorType.CursorA, OnCursorTypeChanged));

            public CursorType Type
            {
                get { return (CursorType)GetValue(CursorTypeProperty); }
                set { SetValue(CursorTypeProperty, value); }
            }

            private static void OnCursorTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                LineCursorGraph line = (LineCursorGraph)d;
                line.OnCursorTypeChanged();
            }

            protected virtual void OnCursorTypeChanged()
            {
                switch (Type)
                {
                    case CursorType.CursorA:
                        //_parentCursor = Cursors.SizeNS;
                        LineStroke = Brushes.White;
                        Orientation = LineCursorOrientation.Vertical;
                        Canvas.SetTop(horizGrid, 10);
                        break;
                    default:
                    case CursorType.CursorB:
                        //_parentCursor = Cursors.SizeWE;
                        LineStroke = Brushes.Yellow;
                        Orientation = LineCursorOrientation.Vertical;
                        Canvas.SetTop(horizGrid, 50);
                        break;
                    case CursorType.Waveform:
                        LineStroke = Brushes.LimeGreen;
                        Orientation = LineCursorOrientation.Horizontal;
                        break;
                    case CursorType.Slicer:
                        LineStroke = Brushes.Red;
                        Orientation = LineCursorOrientation.Vertical;
                        break;
                }

                UpdateUIRepresentation();
            }
            #endregion

            #region Orientation
            public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register("Orientation",
                typeof(LineCursorOrientation), typeof(LineCursorGraph),
                new PropertyMetadata(LineCursorOrientation.Vertical, OnOrientationChanged));

            public LineCursorOrientation Orientation
            {
                get { return (LineCursorOrientation)GetValue(OrientationProperty); }
                set { SetValue(OrientationProperty, value); }
            }

            private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                LineCursorGraph line = (LineCursorGraph)d;
                line.OnOrientationChanged();
            }

            protected virtual void OnOrientationChanged()
            {
                switch (Orientation)
                {
                    case LineCursorOrientation.Horizontal:
                        _parentCursor = Cursors.SizeNS;
                        break;
                    default:
                    case LineCursorOrientation.Vertical:
                        _parentCursor = Cursors.SizeWE;
                        break;
                }

                UpdateUIRepresentation();
            }
            #endregion

            #region XValue
            /// <summary>
            /// Identifies Value dependency property.
            /// </summary>
            public static readonly DependencyProperty XValueProperty =
                DependencyProperty.Register(
                  "XValue",
                  typeof(double),
                  typeof(LineCursorGraph),
                  new PropertyMetadata(
                      0.0, OnXValueChanged));


            /// <summary>
            /// Gets or sets the value in data coordinates
            /// </summary>
            public double XValue
            {
                get { return (double)GetValue(XValueProperty); }
                set { SetValue(XValueProperty, value); }
            }

            private static void OnXValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                LineCursorGraph line = (LineCursorGraph)d;
                line.OnXValueChanged();
            }

            protected virtual void OnXValueChanged()
            {
                UpdateUIRepresentation();

                if (SyncValue) // if this is an initial value set, then the other cursors should be synchronized
                {
                    RaiseEvent(new RoutedEventArgs(CursorMovedEvent));
                    SyncValue = false;
                }
            }
            #endregion

            #region YValue
            /// <summary>
            /// Identifies Value dependency property.
            /// </summary>
            public static readonly DependencyProperty YValueProperty =
                DependencyProperty.Register(
                  "YValue",
                  typeof(double),
                  typeof(LineCursorGraph),
                  new PropertyMetadata(
                      0.0, OnYValueChanged));


            /// <summary>
            /// Gets or sets the value in data coordinates
            /// </summary>
            public double YValue
            {
                get { return (double)GetValue(YValueProperty); }
                set { SetValue(YValueProperty, value); }
            }

            private static void OnYValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                LineCursorGraph line = (LineCursorGraph)d;
                line.OnYValueChanged();
            }

            protected virtual void OnYValueChanged()
            {
                UpdateUIRepresentation();

                //if (SyncValue) // if this is an initial value set, then the other cursors should be synchronized
                //{
                //    RaiseEvent(new RoutedEventArgs(CursorMovedEvent));
                //    SyncValue = false;
                //}
            }
            #endregion
            #endregion

            #region Public Properties
            private string customXFormat = null;
            /// <summary>
            /// Gets or sets the custom format string of x label.
            /// </summary>
            /// <value>The custom X format.</value>
            public string CustomXFormat
            {
                get { return customXFormat; }
                set
                {
                    if (customXFormat != value)
                    {
                        customXFormat = value;
                        UpdateUIRepresentation();
                    }
                }
            }

            private string customYFormat = null;
            /// <summary>
            /// Gets or sets the custom format string of y label.
            /// </summary>
            /// <value>The custom Y format.</value>
            public string CustomYFormat
            {
                get { return customYFormat; }
                set
                {
                    if (customYFormat != value)
                    {
                        customYFormat = value;
                        UpdateUIRepresentation();
                    }
                }
            }

            private bool showHorizontalLine = true;
            /// <summary>
            /// Gets or sets a value indicating whether to show horizontal line.
            /// </summary>
            /// <value><c>true</c> if horizontal line is shown; otherwise, <c>false</c>.</value>
            public bool ShowHorizontalLine
            {
                get { return showHorizontalLine; }
                set
                {
                    if (showHorizontalLine != value)
                    {
                        showHorizontalLine = value;
                        UpdateVisibility();
                    }
                }
            }

            private bool showVerticalLine = true;
            /// <summary>
            /// Gets or sets a value indicating whether to show vertical line.
            /// </summary>
            /// <value><c>true</c> if vertical line is shown; otherwise, <c>false</c>.</value>
            public bool ShowVerticalLine
            {
                get { return showVerticalLine; }
                set
                {
                    if (showVerticalLine != value)
                    {
                        showVerticalLine = value;
                        UpdateVisibility();
                    }
                }
            }

            private bool showCursorText = true;
            /// <summary>
            /// Gets or sets a value indicating whether to display the cursor text.
            /// </summary>
            public bool ShowCursorText
            {
                get { return showCursorText; }
                set
                {
                    showCursorText = value;
                    if (showCursorText)
                        horizGrid.Visibility = Visibility.Visible;
                    else
                        horizGrid.Visibility = Visibility.Collapsed;
                }

            }

            public bool SyncValue { get; set; }

            #endregion

            #region Private Fields
            FrameworkElement _parent;

            Vector blockShift = new Vector(3, 3);

            bool _drag = false;

            Cursor _parentCursor = Cursors.SizeWE;
            #endregion

            public static RoutedEvent CursorMovedEvent = EventManager.RegisterRoutedEvent("CursorMoved",
                RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LineCursorGraph));


            public event RoutedEventHandler CursorMoved
            {
                add { AddHandler(CursorMovedEvent, value); }
                remove { RemoveHandler(CursorMovedEvent, value); }
            }

            public LineCursorGraph()
            {
                InitializeComponent();

                CursorMoved += new RoutedEventHandler(CursorMovedEventHandler);

                // default settings
                Orientation = LineCursorOrientation.Vertical;
                //LineStroke = Brushes.Black;
            }

            protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
            {
                base.OnMouseLeftButtonDown(e);
            }

            protected override void OnPlotterAttached()
            {
                base.OnPlotterAttached();

                _parent = (FrameworkElement)Parent;

                _parent.MouseMove += new MouseEventHandler(parent_MouseMove);
                _parent.MouseEnter += new MouseEventHandler(parent_MouseEnter);
                _parent.MouseLeave += new MouseEventHandler(parent_MouseLeave);
                _parent.MouseLeftButtonDown += new MouseButtonEventHandler(parent_MouseLeftButtonDown);
                _parent.MouseLeftButtonUp += new MouseButtonEventHandler(parent_MouseLeftButtonUp);

                UpdateUIRepresentation();
            }

            void parent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            {
                _drag = false;
                _parent.Cursor = DefaultParentCursor;
                _parent.ReleaseMouseCapture();
            }

            void parent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            {
                if (IsMouseNearLine(e.GetPosition(this)))
                {
                    _drag = true;
                    _parent.CaptureMouse();
                }
            }

            void parent_MouseLeave(object sender, MouseEventArgs e)
            {
                _parent.ReleaseMouseCapture();
            }

            void parent_MouseEnter(object sender, MouseEventArgs e)
            {
            }

            void parent_MouseMove(object sender, MouseEventArgs e)
            {
                if (!_drag)
                {
                    if (IsMouseNearLine(e.GetPosition(this)))
                    {
                        _parent.Cursor = _parentCursor;
                        e.Handled = true;
                    }
                    else
                    {
                        _parent.Cursor = DefaultParentCursor;
                    }
                }

                if (_drag)
                {
                    double value = 0;
                    if (Orientation == LineCursorOrientation.Vertical)
                    {
                        value = e.GetPosition(Plotter2D.CentralGrid).ScreenToData(Plotter2D.Viewport.Transform).X;
                        if ( Plotter2D.Viewport.Visible.XMin <= value &&
                             Plotter2D.Viewport.Visible.XMax >= value )
                            XValue = value;
                    }
                    else
                        XValue = e.GetPosition(Plotter2D.CentralGrid).ScreenToData(Plotter2D.Viewport.Transform).Y;

                    RaiseEvent(new RoutedEventArgs(CursorMovedEvent));
                }
            }

            bool IsMouseNearLine(Point mousePos)
            {
                Point dataPoint = mousePos.ScreenToData(Plotter2D.Viewport.Transform);
                if (Orientation == LineCursorOrientation.Vertical)
                {
                    Point cursorPoint = new Point(XValue, 0);
                    Point screenPoint = cursorPoint.DataToScreen(Plotter2D.Viewport.Transform);
                    return Math.Abs(screenPoint.X - mousePos.X) < 5;
                }
                else
                {
                    Point cursorPoint = new Point(0, YValue);
                    Point screenPoint = cursorPoint.DataToScreen(Plotter2D.Viewport.Transform);
                    return Math.Abs(screenPoint.Y - mousePos.Y) < 5;
                }
            }

            void CursorMovedEventHandler(object sender, RoutedEventArgs e)
            {
                LineCursorGraph cursor = (LineCursorGraph)sender;
                if (this != cursor && this.Type == cursor.Type ) 
                {
                    XValue = cursor.XValue;
                    //e.Handled = true;
                }
            }

            protected override void OnViewportPropertyChanged(ExtendedPropertyChangedEventArgs e)
            {
                UpdateUIRepresentation();
            }

            protected override void OnPlotterDetaching()
            {
                base.OnPlotterDetaching();
            }

            protected void UpdateUIRepresentation()
            {
                if (Plotter2D == null) return;

                var transform = Plotter2D.Viewport.Transform;
                DataRect visible = Plotter2D.Viewport.Visible;
                Rect output = Plotter2D.Viewport.Output;

                Point dataPosition = new Point(XValue, 0);
                Point actualPosition = dataPosition.DataToScreen(transform);

                vertLine.X1 = actualPosition.X;
                vertLine.X2 = actualPosition.X;
                vertLine.Y1 = output.Top;
                vertLine.Y2 = output.Bottom;
            }

            private string GetRoundedValue(double min, double max, double value)
            {
                double roundedValue = value;
                //var log = RoundingHelper.GetDifferenceLog(min, max);
                var log = (int)Math.Round(Math.Log10(Math.Abs(max - min)));
                string format = "G3";
                double diff = Math.Abs(max - min);
                if (1E3 < diff && diff < 1E6)
                {
                    format = "F0";
                }
                if (log < 0)
                    format = "G" + (-log + 2).ToString();

                return roundedValue.ToString(format);
            }

            private void UpdateVisibility()
            {
                horizLine.Visibility = vertGrid.Visibility = GetHorizontalVisibility();
                vertLine.Visibility = horizGrid.Visibility = GetVerticalVisibility();
            }

            private Visibility GetHorizontalVisibility()
            {
                return showHorizontalLine ? Visibility.Visible : Visibility.Hidden;
            }

            private Visibility GetVerticalVisibility()
            {
                return showVerticalLine ? Visibility.Visible : Visibility.Hidden;
            }

            #region LineStroke property

            public Brush LineStroke
            {
                get { return (Brush)GetValue(LineStrokeProperty); }
                set { SetValue(LineStrokeProperty, value); }
            }

            public static readonly DependencyProperty LineStrokeProperty = DependencyProperty.Register(
              "LineStroke",
              typeof(Brush),
              typeof(LineCursorGraph),
              new PropertyMetadata( Brushes.Black));

            #endregion

            #region LineStrokeThickness property

            public double LineStrokeThickness
            {
                get { return (double)GetValue(LineStrokeThicknessProperty); }
                set { SetValue(LineStrokeThicknessProperty, value); }
            }

            public static readonly DependencyProperty LineStrokeThicknessProperty = DependencyProperty.Register(
              "LineStrokeThickness",
              typeof(double),
              typeof(LineCursorGraph),
              new PropertyMetadata(2.0));

            #endregion

            #region LineStrokeDashArray property

            [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
            public DoubleCollection LineStrokeDashArray
            {
                get { return (DoubleCollection)GetValue(LineStrokeDashArrayProperty); }
                set { SetValue(LineStrokeDashArrayProperty, value); }
            }

            public static readonly DependencyProperty LineStrokeDashArrayProperty = DependencyProperty.Register(
              "LineStrokeDashArray",
              typeof(DoubleCollection),
              typeof(LineCursorGraph),
              new FrameworkPropertyMetadata(DoubleCollectionHelper.Create(3, 3)));

            #endregion
        }
}

 


*/