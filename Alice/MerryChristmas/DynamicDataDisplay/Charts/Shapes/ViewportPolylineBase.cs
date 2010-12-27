using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows;

namespace Microsoft.Research.DynamicDataDisplay.Charts.Shapes
{
	public abstract class ViewportPolylineBase : ViewportShape
	{
		#region Properties

		public PointCollection Points
		{
			get { return (PointCollection)GetValue(PointsProperty); }
			set { SetValue(PointsProperty, value); }
		}

		public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
		  "Points",
		  typeof(PointCollection),
		  typeof(ViewportPolylineBase),
		  new FrameworkPropertyMetadata(new PointCollection(), OnPropertyChanged));

		private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			ViewportPolylineBase polyline = (ViewportPolylineBase)d;
			polyline.UpdateUIRepresentation();
		}

		public FillRule FillRule
		{
			get { return (FillRule)GetValue(FillRuleProperty); }
			set { SetValue(FillRuleProperty, value); }
		}

		public static readonly DependencyProperty FillRuleProperty = DependencyProperty.Register(
		  "FillRule",
		  typeof(FillRule),
		  typeof(ViewportPolylineBase),
		  new FrameworkPropertyMetadata(FillRule.EvenOdd, OnPropertyChanged));

		#endregion

		private PathGeometry geometry = new PathGeometry();
		protected PathGeometry PathGeometry
		{
			get { return geometry; }
		}

		protected sealed override Geometry DefiningGeometry
		{
			get { return geometry; }
		}
	}
}
