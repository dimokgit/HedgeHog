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
  /// Interaction logic for CursorH.xaml
  /// </summary>
  public partial class CursorH : UserControl {
    public CursorH() {
      InitializeComponent();
    }
  }
}
/*
    <d3:PositionalViewportUIContainer x:Class="Microsoft.Research.DynamicDataDisplay.Charts.Shapes.CursorH"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d3="http://research.microsoft.com/DynamicDataDisplay/1.0"
        xmlns:d3b="clr-namespace:Microsoft.Research.DynamicDataDisplay.Charts;assembly=DynamicDataDisplay"
        ToolTip="{Binding Position, RelativeSource={RelativeSource Self}}" Height="80" Width="897">
            <d3:PositionalViewportUIContainer.Style>
                <Style TargetType="{x:Type d3:PositionalViewportUIContainer}">
                    <Style.Resources>
                        <Storyboard x:Key="story">
                        </Storyboard>
                    </Style.Resources>

                    <Setter Property="Focusable" Value="False"/>
                    <Setter Property="Opacity" Value="10"/>
                    <Setter Property="Cursor" Value="ScrollAll"/>
                    <Setter Property="HorizontalContentAlignment" Value="Center"/>
                    <Setter Property="VerticalContentAlignment" Value="Center"/>

                    <Style.Triggers>
                        <MultiTrigger>
                            <MultiTrigger.Conditions>
                                <Condition Property="IsMouseOver" Value="True"/>
                                <Condition Property="IsMouseCaptured" Value="False"/>
                            </MultiTrigger.Conditions>
                            <MultiTrigger.EnterActions>
                                <BeginStoryboard Name="storyboard" Storyboard="{StaticResource story}"/>
                            </MultiTrigger.EnterActions>
                            <MultiTrigger.ExitActions>
                                <RemoveStoryboard BeginStoryboardName="storyboard"/>
                            </MultiTrigger.ExitActions>
                        </MultiTrigger>
                    </Style.Triggers>
                </Style>
            </d3:PositionalViewportUIContainer.Style>
            <Rectangle Name = "cursorgraph"  Fill="Gray" Stroke="Transparent" Margin="0,0,0,0" Grid.ColumnSpan="5" Height="4" VerticalAlignment="Center"/>
        </d3:PositionalViewportUIContainer>


// CursorH.xaml.cs
        
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
using System.Windows.Controls.Primitives;
using Microsoft.Research.DynamicDataDisplay;

namespace Microsoft.Research.DynamicDataDisplay.Charts.Shapes
{
    /// <summary>
    /// Interaction logic for CursorH.xaml
    /// </summary>
    public partial class CursorH : Microsoft.Research.DynamicDataDisplay.Charts.PositionalViewportUIContainer
    {
        HorizontalLine hLine = new HorizontalLine();
        ChartPlotter cPlotter = null;

        public CursorH()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CursorH"/> class.
        /// </summary>
        /// <param name="position">The position of CursorH.</param>
        public CursorH(Point position) : this()
        {
            Position = position;
        }

        bool dragging = false;
        Point dragStart;
        Vector shift;

        private void setCursor()
        {
            if (cPlotter != null)
            {
                Width = cPlotter.Viewport.Visible.XMax - cPlotter.Viewport.Visible.XMin;
                cursorgraph.Width = hLine.Width;
            }
        }
        
        public void SetCursor(ChartPlotter plotter)
        {
            Visibility = Visibility.Visible;
            cPlotter = plotter;
            setCursor();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Plotter == null)
                return;

            dragStart = e.GetPosition(Plotter.ViewportPanel).ScreenToData(Plotter.Viewport.Transform);
            setCursor();
            shift = Position - dragStart;
            dragging = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            ReleaseMouseCapture();
            dragging = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging)
            {
                if (IsMouseCaptured)
                    ReleaseMouseCapture();

                return;
            }

            if (!IsMouseCaptured)
                CaptureMouse();

            Point mouseInData = e.GetPosition(Plotter.ViewportPanel).ScreenToData(Plotter.Viewport.Transform);

            if (mouseInData != dragStart)
            {
                Position = mouseInData + shift;
                setCursor();
                e.Handled = true;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (dragging)
            {
                dragging = false;
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                    hLine.Value = Position.Y;
                    e.Handled = true;
                }
            }
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            return base.ArrangeOverride(arrangeBounds);
        }
    
    }
}


// implementation in a Window.xaml.cs file

        CursorH hCursor1 = new CursorH(new Point(0,0));

        // after data loaded
        
            Point pCursorH = new Point(0, 1).DataToScreen(Plotter.Viewport.Transform);
            hCursor1.Position = pCursorH;
            hCursor1.ToolTip = "H. cursor 1";
            Plotter.Children.Add(hCursor1);
            hCursor1.SetCursor(Plotter);

            hCursor1.Visibility = Visibility.Collapsed;
        

            // to show/hide the cursor
            hCursor1.Visibility = Visibility.Visible;
            hCursor1.Visibility = Visibility.Hidden;
        
*/
