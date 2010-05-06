using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace Microsoft.Research.DynamicDataDisplay
{
	public sealed partial class Viewport2D
	{
		#region IsContentBoundsHost attached property

		public static bool GetIsContentBoundsHost(DependencyObject obj)
		{
			return (bool)obj.GetValue(IsContentBoundsHostProperty);
		}

		public static void SetIsContentBoundsHost(DependencyObject obj, bool value)
		{
			obj.SetValue(IsContentBoundsHostProperty, value);
		}

		public static readonly DependencyProperty IsContentBoundsHostProperty = DependencyProperty.RegisterAttached(
		  "IsContentBoundsHost",
		  typeof(bool),
		  typeof(Viewport2D),
		  new FrameworkPropertyMetadata(false, OnIsContentBoundsChanged));

		private static void OnIsContentBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			IPlotterElement plotterElement = d as IPlotterElement;
			if (plotterElement != null && plotterElement.Plotter != null)
			{
				Plotter2D plotter2d = (Plotter2D)plotterElement.Plotter;
				plotter2d.Viewport.UpdateContentBoundsHosts();
			}
		} 

		#endregion

		#region ContentBounds attached property

		public static DataRect GetContentBounds(DependencyObject obj)
		{
			return (DataRect)obj.GetValue(ContentBoundsProperty);
		}

		public static void SetContentBounds(DependencyObject obj, DataRect value)
		{
			obj.SetValue(ContentBoundsProperty, value);
		}

		public static readonly DependencyProperty ContentBoundsProperty = DependencyProperty.RegisterAttached(
		  "ContentBounds",
		  typeof(DataRect),
		  typeof(Viewport2D),
		  new FrameworkPropertyMetadata(DataRect.Empty, OnContentBoundsChanged));

		private static void OnContentBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			IPlotterElement element = d as IPlotterElement;
			if (element != null)
			{
				Plotter2D plotter2d = element.Plotter as Plotter2D;
				if (plotter2d != null)
				{
					plotter2d.Viewport.UpdateVisible();
				}
			}
		}

		#endregion
	}
}
