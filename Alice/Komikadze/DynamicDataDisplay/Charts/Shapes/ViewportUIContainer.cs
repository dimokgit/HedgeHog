#define old

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;
using Microsoft.Research.DynamicDataDisplay;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Markup;
using System.Collections.ObjectModel;

namespace Microsoft.Research.DynamicDataDisplay.Charts
{
	public sealed class PositionChangedEventArgs : EventArgs
	{
		public Point Position { get; internal set; }
		public Point PreviousPosition { get; internal set; }
	}

	public delegate Point PositionCoerceCallback(ViewportUIContainer container, Point position);

	public class ViewportUIContainer : ContentControl, IPlotterElement
	{
		static ViewportUIContainer()
		{
			Type type = typeof(ViewportUIContainer);

			// todo subscribe for properties changes
			HorizontalContentAlignmentProperty.AddOwner(
				type, new FrameworkPropertyMetadata(HorizontalAlignment.Center));
			VerticalContentAlignmentProperty.AddOwner(
				type, new FrameworkPropertyMetadata(VerticalAlignment.Center));
		}

		protected override void OnChildDesiredSizeChanged(UIElement child)
		{
			UpdateUIRepresentation();
		}

		public Point Position
		{
			get { return (Point)GetValue(PositionProperty); }
			set { SetValue(PositionProperty, value); }
		}

		public static readonly DependencyProperty PositionProperty =
			DependencyProperty.Register(
			  "Position",
			  typeof(Point),
			  typeof(ViewportUIContainer),
			  new FrameworkPropertyMetadata(new Point(0, 0), OnPositionChanged, CoercePosition));

		private static object CoercePosition(DependencyObject d, object value)
		{
			ViewportUIContainer owner = (ViewportUIContainer)d;
			if (owner.positionCoerceCallbacks.Count > 0)
			{
				Point position = (Point)value;
				foreach (var callback in owner.positionCoerceCallbacks)
				{
					position = callback(owner, position);
				}
				value = position;
			}
			return value;
		}

		private readonly ObservableCollection<PositionCoerceCallback> positionCoerceCallbacks = new ObservableCollection<PositionCoerceCallback>();
		/// <summary>
		/// Gets the list of callbacks which are called every time Position changes to coerce it.
		/// </summary>
		/// <value>The position coerce callbacks.</value>
		public ObservableCollection<PositionCoerceCallback> PositionCoerceCallbacks
		{
			get { return positionCoerceCallbacks; }
		}

		private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			ViewportUIContainer container = (ViewportUIContainer)d;
			container.OnPositionChanged(e);
		}

		public event EventHandler<PositionChangedEventArgs> PositionChanged;

		private void OnPositionChanged(DependencyPropertyChangedEventArgs e)
		{
			if (e.Property == PositionProperty)
			{
				PositionChanged.Raise(this, new PositionChangedEventArgs { Position = (Point)e.NewValue, PreviousPosition = (Point)e.OldValue });
			}
			UpdateUIRepresentation();
		}

		public Vector Shift
		{
			get { return (Vector)GetValue(ShiftProperty); }
			set { SetValue(ShiftProperty, value); }
		}

		public static readonly DependencyProperty ShiftProperty =
			DependencyProperty.Register(
			  "Shift",
			  typeof(Vector),
			  typeof(ViewportUIContainer),
			  new FrameworkPropertyMetadata(new Vector(), OnPositionChanged));

		#region IPlotterElement Members

		private const string canvasName = "ViewportUIContainer_Canvas";
		private Plotter2D plotter;
		void IPlotterElement.OnPlotterAttached(Plotter plotter)
		{
			Panel hostPanel = GetHostPanel(plotter);

#if !old
			Canvas hostCanvas = (Canvas)hostPanel.FindName(canvasName);
			if (hostCanvas == null)
			{
				hostCanvas = new Canvas { ClipToBounds = true };
				Panel.SetZIndex(hostCanvas, 1);

				INameScope nameScope = NameScope.GetNameScope(hostPanel);
				if (nameScope == null)
				{
					nameScope = new NameScope();
					NameScope.SetNameScope(hostPanel, nameScope);
				}

				hostPanel.RegisterName(canvasName, hostCanvas);
				hostPanel.Children.Add(hostCanvas);
			}

			hostCanvas.Children.Add(this);
#else
			hostPanel.Children.Add(this);
#endif

			Plotter2D plotter2d = (Plotter2D)plotter;
			this.plotter = plotter2d;
			plotter2d.Viewport.PropertyChanged += Viewport_PropertyChanged;

			UpdateUIRepresentation();
		}

		private Panel GetHostPanel(Plotter plotter)
		{
#if !old
			return plotter.CentralGrid;
#else
			//return plotter.CentralGrid;
			return plotter.MainCanvas;
#endif
		}

		private void Viewport_PropertyChanged(object sender, ExtendedPropertyChangedEventArgs e)
		{
			UpdateUIRepresentation();
		}

		private void UpdateUIRepresentation()
		{
			if (plotter == null)
				return;

			var transform = Plotter.Viewport.Transform;

			Point position = Position.DataToScreen(transform);
			position += Shift;

			double x = position.X;
			double y = position.Y;

			UIElement content = Content as UIElement;
			if (content != null)
			{
				if (!content.IsMeasureValid)
				{
					content.InvalidateMeasure();

					Dispatcher.BeginInvoke(
						((Action)(() => { UpdateUIRepresentation(); })), DispatcherPriority.Background);
					return;
				}

				Size contentSize = content.DesiredSize;

				switch (HorizontalContentAlignment)
				{
					case HorizontalAlignment.Center:
						x -= contentSize.Width / 2;
						break;
					case HorizontalAlignment.Left:
						break;
					case HorizontalAlignment.Right:
						x -= contentSize.Width;
						break;
					case HorizontalAlignment.Stretch:
						break;
					default:
						break;
				}

				switch (VerticalContentAlignment)
				{
					case VerticalAlignment.Bottom:
						y -= contentSize.Height;
						break;
					case VerticalAlignment.Center:
						y -= contentSize.Height / 2;
						break;
					case VerticalAlignment.Stretch:
						break;
					case VerticalAlignment.Top:
						break;
					default:
						break;
				}
			}

			Canvas.SetLeft(this, x);
			Canvas.SetTop(this, y);
		}

		void IPlotterElement.OnPlotterDetaching(Plotter plotter)
		{
			Plotter2D plotter2d = (Plotter2D)plotter;
			plotter2d.Viewport.PropertyChanged -= Viewport_PropertyChanged;

			Panel hostPanel = (Panel)GetHostPanel(plotter);
#if !old
			Canvas hostCanvas = (Canvas)hostPanel.FindName(canvasName);

			if (hostCanvas.Children.Count == 1)
			{
				// only this ViewportUIContainer left
				hostPanel.Children.Remove(hostCanvas);
			}
			hostCanvas.Children.Remove(this);
#else
			hostPanel.Children.Remove(this);
#endif

			this.plotter = null;
		}

		public Plotter2D Plotter
		{
			get { return plotter; }
		}

		Plotter IPlotterElement.Plotter
		{
			get { return plotter; }
		}

		#endregion
	}
}
