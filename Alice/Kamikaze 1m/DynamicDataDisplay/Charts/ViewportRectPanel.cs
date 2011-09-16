#if DEBUG
#define _stats
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using Microsoft.Research.DynamicDataDisplay.Common;
using Microsoft.Research.DynamicDataDisplay.Common.Auxiliary;

namespace Microsoft.Research.DynamicDataDisplay.Charts
{
	public class ViewportRectPanel : IndependentArrangePanel, IPlotterElement
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ViewportRectPanel"/> class.
		/// </summary>
		public ViewportRectPanel()
		{
#if stats
			Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;
#endif
		}

#if stats
		void Dispatcher_ShutdownStarted(object sender, EventArgs e)
		{
			Debug.WriteLine("Measured " + measureCount + " times");
			Debug.WriteLine("Arranged " + arrangeCount + " times");
			Debug.WriteLine("Child added " + childAddedCount + " times");
		}
#endif

		#region Properties

		#region ViewportBounds

		public static DataRect GetViewportBounds(DependencyObject obj)
		{
			return (DataRect)obj.GetValue(ViewportBoundsProperty);
		}

		public static void SetViewportBounds(DependencyObject obj, DataRect value)
		{
			obj.SetValue(ViewportBoundsProperty, value);
		}

		public static readonly DependencyProperty ViewportBoundsProperty = DependencyProperty.RegisterAttached(
		  "ViewportBounds",
		  typeof(DataRect),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(DataRect.Empty, OnLayoutPropertyChanged));

		#endregion

		#region ViewportX

		[AttachedPropertyBrowsableForChildren]
		public static double GetViewportX(DependencyObject obj)
		{
			return (double)obj.GetValue(ViewportXProperty);
		}

		public static void SetViewportX(DependencyObject obj, double value)
		{
			obj.SetValue(ViewportXProperty, value);
		}

		public static readonly DependencyProperty ViewportXProperty = DependencyProperty.RegisterAttached(
		  "ViewportX",
		  typeof(double),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(Double.NaN, OnLayoutPropertyChanged));

		#endregion

		#region ViewportY

		[AttachedPropertyBrowsableForChildren]
		public static double GetViewportY(DependencyObject obj)
		{
			return (double)obj.GetValue(ViewportYProperty);
		}

		public static void SetViewportY(DependencyObject obj, double value)
		{
			obj.SetValue(ViewportYProperty, value);
		}

		public static readonly DependencyProperty ViewportYProperty = DependencyProperty.RegisterAttached(
		  "ViewportY",
		  typeof(double),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(Double.NaN, OnLayoutPropertyChanged));

		#endregion

		#region ViewportWidth

		[AttachedPropertyBrowsableForChildren]
		public static double GetViewportWidth(DependencyObject obj)
		{
			return (double)obj.GetValue(ViewportWidthProperty);
		}

		public static void SetViewportWidth(DependencyObject obj, double value)
		{
			obj.SetValue(ViewportWidthProperty, value);
		}

		public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.RegisterAttached(
		  "ViewportWidth",
		  typeof(double),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(Double.NaN, OnLayoutPropertyChanged));

		#endregion

		#region ViewportHeight

		[AttachedPropertyBrowsableForChildren]
		public static double GetViewportHeight(DependencyObject obj)
		{
			return (double)obj.GetValue(ViewportHeightProperty);
		}

		public static void SetViewportHeight(DependencyObject obj, double value)
		{
			obj.SetValue(ViewportHeightProperty, value);
		}

		public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.RegisterAttached(
		  "ViewportHeight",
		  typeof(double),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(Double.NaN, OnLayoutPropertyChanged));

		#endregion

		#region ScreenOffsetX

		[AttachedPropertyBrowsableForChildren]
		public static double GetScreenOffsetX(DependencyObject obj)
		{
			return (double)obj.GetValue(ScreenOffsetXProperty);
		}

		public static void SetScreenOffsetX(DependencyObject obj, double value)
		{
			obj.SetValue(ScreenOffsetXProperty, value);
		}

		public static readonly DependencyProperty ScreenOffsetXProperty = DependencyProperty.RegisterAttached(
		  "ScreenOffsetX",
		  typeof(double),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(Double.NaN, OnLayoutPropertyChanged));

		#endregion

		#region ScreenOffsetY

		[AttachedPropertyBrowsableForChildren]
		public static double GetScreenOffsetY(DependencyObject obj)
		{
			return (double)obj.GetValue(ScreenOffsetYProperty);
		}

		public static void SetScreenOffsetY(DependencyObject obj, double value)
		{
			obj.SetValue(ScreenOffsetYProperty, value);
		}

		public static readonly DependencyProperty ScreenOffsetYProperty = DependencyProperty.RegisterAttached(
		  "ScreenOffsetY",
		  typeof(double),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(Double.NaN, OnLayoutPropertyChanged));

		#endregion

		#region HorizontalAlignment

		[AttachedPropertyBrowsableForChildren]
		public static HorizontalAlignment GetViewportHorizontalAlignment(DependencyObject obj)
		{
			return (HorizontalAlignment)obj.GetValue(ViewportHorizontalAlignmentProperty);
		}

		public static void SetViewportHorizontalAlignment(DependencyObject obj, HorizontalAlignment value)
		{
			obj.SetValue(ViewportHorizontalAlignmentProperty, value);
		}

		public static readonly DependencyProperty ViewportHorizontalAlignmentProperty = DependencyProperty.RegisterAttached(
		  "ViewportHorizontalAlignment",
		  typeof(HorizontalAlignment),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(HorizontalAlignment.Center, OnLayoutPropertyChanged));

		#endregion

		#region VerticalAlignment

		[AttachedPropertyBrowsableForChildren]
		public static VerticalAlignment GetViewportVerticalAlignment(DependencyObject obj)
		{
			return (VerticalAlignment)obj.GetValue(ViewportVerticalAlignmentProperty);
		}

		public static void SetViewportVerticalAlignment(DependencyObject obj, VerticalAlignment value)
		{
			obj.SetValue(ViewportVerticalAlignmentProperty, value);
		}

		public static readonly DependencyProperty ViewportVerticalAlignmentProperty = DependencyProperty.RegisterAttached(
		  "ViewportVerticalAlignment",
		  typeof(VerticalAlignment),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(VerticalAlignment.Center, OnLayoutPropertyChanged));

		#endregion

		#region ActualViewportBounds

		public static DataRect GetActualViewportBounds(DependencyObject obj)
		{
			return (DataRect)obj.GetValue(ActualViewportBoundsProperty);
		}

		public static void SetActualViewportBounds(DependencyObject obj, DataRect value)
		{
			obj.SetValue(ActualViewportBoundsProperty, value);
		}

		public static readonly DependencyProperty ActualViewportBoundsProperty = DependencyProperty.RegisterAttached(
		  "ActualViewportBounds",
		  typeof(DataRect),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(DataRect.Empty));

		#endregion

		#region PrevActualViewportBounds

		public static DataRect GetPrevActualViewportBounds(DependencyObject obj)
		{
			return (DataRect)obj.GetValue(PrevActualViewportBoundsProperty);
		}

		public static void SetPrevActualViewportBounds(DependencyObject obj, DataRect value)
		{
			obj.SetValue(PrevActualViewportBoundsProperty, value);
		}

		public static readonly DependencyProperty PrevActualViewportBoundsProperty = DependencyProperty.RegisterAttached(
		  "PrevActualViewportBounds",
		  typeof(DataRect),
		  typeof(ViewportRectPanel),
		  new FrameworkPropertyMetadata(DataRect.Empty));

		#endregion

		protected static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			UIElement uiElement = d as UIElement;
			if (uiElement != null)
			{
				ViewportRectPanel panel = VisualTreeHelper.GetParent(uiElement) as ViewportRectPanel;
				if (panel != null)
				{
					// invalidating not self arrange, but calling Arrange method of only that uiElement which has changed position

					panel.InvalidateArrange(uiElement);
				}
			}
		}

		protected void InvalidateArrange(UIElement uiElement)
		{
			if (plotter == null)
				return;

			var transform = plotter.Transform;

			var childSize = GetElementSize(uiElement, availableSize, transform);
			uiElement.Measure(childSize);

			var childBounds = GetElementScreenBounds(transform, uiElement);
			uiElement.Arrange(childBounds);
		}

		#endregion

		#region Panel methods override

#if stats
		private int childAddedCount = 0;
#endif
		protected internal override void OnChildAdded(UIElement child)
		{
#if stats
			childAddedCount++;
#endif

			if (plotter == null) return;

			var transform = plotter.Viewport.Transform;

			Size elementSize = GetElementSize(child, availableSize, transform);
			child.Measure(elementSize);

			Rect bounds = GetElementScreenBounds(transform, child);
			child.Arrange(bounds);
		}

#if stats
		private int arrangeCount = 0;
		private int measureCount = 0;
#endif
		private Size availableSize;
		protected override Size MeasureOverride(Size availableSize)
		{
#if stats
			measureCount++;
			Debug.WriteLine("Measure: " + Environment.TickCount);
			Debug.WriteLine("Children.Count=" + Children.Count);
#endif

			this.availableSize = availableSize;

			if (plotter == null)
				return availableSize;

			var transform = plotter.Viewport.Transform;

			foreach (UIElement child in InternalChildren)
			{
				if (child != null)
				{
					Size elementSize = GetElementSize(child, availableSize, transform);
					child.Measure(elementSize);
				}
			}

			return availableSize;
		}

		protected virtual Size GetElementSize(UIElement child, Size availableSize, CoordinateTransform transform)
		{
			Size res = availableSize;

			DataRect ownViewportBounds = GetViewportBounds(child);
			if (!ownViewportBounds.IsEmpty)
			{
				res = ownViewportBounds.ViewportToScreen(transform).Size;
			}
			else
			{
				double viewportWidth = GetViewportWidth(child);
				double viewportHeight = GetViewportHeight(child);

				bool hasViewportWidth = viewportWidth.IsNotNaN();
				bool hasViewportHeight = viewportHeight.IsNotNaN();

				DataRect bounds = new DataRect(new Size(hasViewportWidth ? viewportWidth : availableSize.Width,
										   hasViewportHeight ? viewportHeight : availableSize.Height));
				Rect screenBounds = bounds.ViewportToScreen(transform);

				res = new Size(hasViewportWidth ? screenBounds.Width : availableSize.Width,
					hasViewportHeight ? screenBounds.Height : availableSize.Height);
			}

			return res;
		}

		protected Rect GetElementScreenBounds(CoordinateTransform transform, UIElement child)
		{
			Rect screenBounds = GetElementScreenBoundsCore(transform, child);

			DataRect viewportBounds = screenBounds.ScreenToViewport(transform);
			DataRect prevViewportBounds = GetActualViewportBounds(child);
			SetPrevActualViewportBounds(child, prevViewportBounds);
			SetActualViewportBounds(child, viewportBounds);

			return screenBounds;
		}

		protected virtual Rect GetElementScreenBoundsCore(CoordinateTransform transform, UIElement child)
		{
			Rect bounds = new Rect(0, 0, 1, 1);

			DataRect ownViewportBounds = GetViewportBounds(child);
			if (!ownViewportBounds.IsEmpty)
			{
				bounds = ownViewportBounds.ViewportToScreen(transform);
			}
			else
			{
				double viewportX = GetViewportX(child);
				double viewportY = GetViewportY(child);

				if (viewportX.IsNaN() || viewportY.IsNaN())
					return bounds;

				double viewportWidth = GetViewportWidth(child);
				double viewportHeight = GetViewportHeight(child);

				bool hasViewportWidth = viewportWidth.IsNotNaN();
				bool hasViewportHeight = viewportHeight.IsNotNaN();

				DataRect r = new DataRect(new Size(hasViewportWidth ? viewportWidth : child.DesiredSize.Width,
										   hasViewportHeight ? viewportHeight : child.DesiredSize.Height));
				r = r.ViewportToScreen(transform);

				double screenWidth = hasViewportWidth ? r.Width : child.DesiredSize.Width;
				double screenHeight = hasViewportHeight ? r.Height : child.DesiredSize.Height;

				Point location = new Point(viewportX, viewportY).ViewportToScreen(transform);

				double screenX = location.X;
				double screenY = location.Y;

				HorizontalAlignment horizAlignment = GetViewportHorizontalAlignment(child);
				switch (horizAlignment)
				{
					case HorizontalAlignment.Stretch:
					case HorizontalAlignment.Center:
						screenX -= screenWidth / 2;
						break;
					case HorizontalAlignment.Left:
						break;
					case HorizontalAlignment.Right:
						screenX -= screenWidth;
						break;
				}

				VerticalAlignment vertAlignment = GetViewportVerticalAlignment(child);
				switch (vertAlignment)
				{
					case VerticalAlignment.Bottom:
						screenY -= screenHeight;
						break;
					case VerticalAlignment.Center:
					case VerticalAlignment.Stretch:
						screenY -= screenHeight / 2;
						break;
					case VerticalAlignment.Top:
						break;
					default:
						break;
				}

				bounds = new Rect(screenX, screenY, screenWidth, screenHeight);
			}

			// applying screen offset

			double screenOffsetX = GetScreenOffsetX(child);
			if (screenOffsetX.IsNaN()) screenOffsetX = 0;
			double screenOffsetY = GetScreenOffsetY(child);
			if (screenOffsetY.IsNaN()) screenOffsetY = 0;

			Vector screenOffset = new Vector(screenOffsetX, screenOffsetY);
			bounds.Offset(screenOffset);
			bounds.Offset(-VisualOffset);

			return bounds;
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
#if stats
			arrangeCount++;
#endif

			if (plotter == null)
				return finalSize;

			var transform = plotter.Viewport.Transform;

			foreach (UIElement child in InternalChildren)
			{
				if (child != null)
				{
					Rect bounds = GetElementScreenBounds(transform, child);
					child.Arrange(bounds);
				}
			}

			return finalSize;
		}

		#endregion

		#region IPlotterElement Members

		private Plotter2D plotter;
		protected Plotter2D Plotter
		{
			get { return plotter; }
		}

		public virtual void OnPlotterAttached(Plotter plotter)
		{
			this.plotter = (Plotter2D)plotter;
			plotter.CentralGrid.Children.Add(this);
			this.plotter.Viewport.PropertyChanged += Viewport_PropertyChanged;
		}

		DataRect visibleWhileCreation;
		protected virtual void Viewport_PropertyChanged(object sender, ExtendedPropertyChangedEventArgs e)
		{
			if (e.PropertyName == "Visible")
			{
				DataRect visible = (DataRect)e.NewValue;

				if (visibleWhileCreation.Size == visible.Size)
				{
					Point prevLocation = visibleWhileCreation.Location.ViewportToScreen(plotter.Transform);
					Point location = visible.Location.ViewportToScreen(plotter.Transform);

					VisualOffset = prevLocation - location;
					Debug.WriteLine("Visual offset = " + VisualOffset);
				}
				else
				{
					visibleWhileCreation = visible;
					VisualOffset = new Vector();
					InvalidateMeasure();
				}
			}
			else if (e.PropertyName == "Output")
			{
				VisualOffset = new Vector();
				InvalidateMeasure();
			}
		}

		public virtual void OnPlotterDetaching(Plotter plotter)
		{
			this.plotter.Viewport.PropertyChanged -= Viewport_PropertyChanged;
			plotter.CentralGrid.Children.Remove(this);
			this.plotter = null;
		}

		Plotter IPlotterElement.Plotter
		{
			get { return plotter; }
		}

		#endregion
	}
}
