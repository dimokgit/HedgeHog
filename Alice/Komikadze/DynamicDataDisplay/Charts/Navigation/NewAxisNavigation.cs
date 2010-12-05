using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Research.DynamicDataDisplay.Charts.Axes;
using Microsoft.Research.DynamicDataDisplay.Common.Auxiliary;

namespace Microsoft.Research.DynamicDataDisplay.Charts.Navigation
{
	public class NewAxisNavigation : DependencyObject, IPlotterElement
	{
		public AxisPlacement Placement
		{
			get { return (AxisPlacement)GetValue(PlacementProperty); }
			set { SetValue(PlacementProperty, value); }
		}

		public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
		  "Placement",
		  typeof(AxisPlacement),
		  typeof(NewAxisNavigation),
		  new FrameworkPropertyMetadata(AxisPlacement.Left, OnPlacementChanged));

		private static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			NewAxisNavigation navigation = (NewAxisNavigation)d;
			navigation.OnPlacementChanged();
		}

		private Panel listeningPanel;
		private void OnPlacementChanged()
		{
			SetListeningPanel();
		}

		private void SetListeningPanel()
		{
			if (plotter == null) return;

			AxisPlacement placement = Placement;
			switch (placement)
			{
				case AxisPlacement.Left:
					listeningPanel = plotter.LeftPanel;
					break;
				case AxisPlacement.Right:
					listeningPanel = plotter.RightPanel;
					break;
				case AxisPlacement.Top:
					listeningPanel = plotter.TopPanel;
					break;
				case AxisPlacement.Bottom:
					listeningPanel = plotter.BottomPanel;
					break;
				default:
					break;
			}
		}

		private CoordinateTransform Transform
		{
			get { return plotter.Viewport.Transform; }
		}

		private Panel hostPanel;

		#region IPlotterElement Members

		public void OnPlotterAttached(Plotter plotter)
		{
			this.plotter = (Plotter2D)plotter;

			hostPanel = plotter.MainGrid;

			SetListeningPanel();

			if (plotter.MainGrid != null)
			{
				hostPanel.MouseLeftButtonUp += OnMouseLeftButtonUp;
				hostPanel.MouseLeftButtonDown += OnMouseLeftButtonDown;
				hostPanel.MouseMove += OnMouseMove;
				hostPanel.MouseWheel += OnMouseWheel;
			}
		}

		private const double wheelZoomSpeed = 1.2;
		private void OnMouseWheel(object sender, MouseWheelEventArgs e)
		{
			Point mousePos = e.GetPosition(listeningPanel);

			UpdateActivePlotter(e);

			int delta = -e.Delta;

			Point zoomTo = mousePos.ScreenToViewport(activePlotter.Transform);

			double zoomSpeed = Math.Abs(delta / Mouse.MouseWheelDeltaForOneLine);
			zoomSpeed *= wheelZoomSpeed;
			if (delta < 0)
			{
				zoomSpeed = 1 / zoomSpeed;
			}

			DataRect visible = activePlotter.Viewport.Visible.Zoom(zoomTo, zoomSpeed);
			DataRect oldVisible = activePlotter.Viewport.Visible;
			if (Placement.IsHorizontal())
			{
				visible.YMin = oldVisible.YMin;
				visible.Height = oldVisible.Height;
			}
			else
			{
				visible.XMin = oldVisible.XMin;
				visible.Width = oldVisible.Width;
			}
			activePlotter.Viewport.Visible = visible;

			e.Handled = true;
		}

		private void OnMouseMove(object sender, MouseEventArgs e)
		{
			if (lmbPressed)
			{
				Point screenMousePos = e.GetPosition(listeningPanel);
				Point dataMousePos = screenMousePos.ScreenToViewport(activePlotter.Transform);
				DataRect visible = activePlotter.Viewport.Visible;
				double delta;
				if (Placement.IsHorizontal())
				{
					delta = (dataMousePos - dragStart).X;
					visible.XMin -= delta;
				}
				else
				{
					delta = (dataMousePos - dragStart).Y;
					visible.YMin -= delta;
				}

				if (screenMousePos != lmbInitialPosition)
				{
					listeningPanel.Cursor = Placement.IsHorizontal() ? Cursors.ScrollWE : Cursors.ScrollNS;
				}

				activePlotter.Viewport.Visible = visible;

				e.Handled = true;
			}
		}

		private Point lmbInitialPosition;
		protected Point LmbInitialPosition
		{
			get { return lmbInitialPosition; }
		}

		private readonly SolidColorBrush fillBrush = new SolidColorBrush(Color.FromRgb(255, 228, 209)).MakeTransparent(0.2);
		private bool lmbPressed;
		private Point dragStart;
		private Plotter2D activePlotter;
		private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			lmbInitialPosition = e.GetPosition(listeningPanel);

			var foundActivePlotter = UpdateActivePlotter(e);
			if (foundActivePlotter)
			{
				dragStart = lmbInitialPosition.ScreenToViewport(activePlotter.Transform);

				listeningPanel.Background = fillBrush;
				listeningPanel.CaptureMouse();

				e.Handled = true;
			}
		}

		private bool UpdateActivePlotter(MouseEventArgs e)
		{
			var axes = listeningPanel.Children.OfType<GeneralAxis>();

			foreach (var axis in axes)
			{
				var positionInAxis = e.GetPosition(axis);
				Rect axisBounds = new Rect(axis.RenderSize);
				if (axisBounds.Contains(positionInAxis))
				{
					lmbPressed = true;
					activePlotter = axis.Plotter;

					return true;
				}
			}

			return false;
		}

		private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (lmbPressed)
			{
				lmbPressed = false;
				listeningPanel.ClearValue(Panel.CursorProperty);
				listeningPanel.Background = Brushes.Transparent;
				listeningPanel.ReleaseMouseCapture();

				e.Handled = true;
			}
		}

		public void OnPlotterDetaching(Plotter plotter)
		{
			if (plotter.MainGrid != null)
			{
				plotter.MainGrid.PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
				plotter.MainGrid.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
				plotter.MainGrid.PreviewMouseMove += OnMouseMove;
				plotter.MainGrid.PreviewMouseWheel += OnMouseWheel;
			}
			this.plotter = null;
		}

		private Plotter2D plotter;
		Plotter IPlotterElement.Plotter
		{
			get { return plotter; }
		}

		#endregion
	}
}
