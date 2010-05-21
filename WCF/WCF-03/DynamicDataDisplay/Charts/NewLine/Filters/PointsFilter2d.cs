using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace Microsoft.Research.DynamicDataDisplay.Charts.NewLine.Filters
{
	public abstract class PointsFilter2d : DependencyObject, IPointsFilter2d
	{
		protected DataRect ViewportRect
		{
			get { return DataSource2dContext.GetVisibleRect(this); }
		}

		protected Rect ScreenRect
		{
			get { return DataSource2dContext.GetScreenRect(this); }
		}

		protected static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			PointsFilter2d filter = (PointsFilter2d)d;
			filter.RaiseChanged();
		}

		protected void RaiseChanged()
		{
			Changed.Raise(this);
		}

		public event EventHandler Changed;

		public abstract IEnumerable<Point> Filter(IEnumerable<Point> series);
	}
}
