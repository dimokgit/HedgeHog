using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Research.DynamicDataDisplay.Common;
using System.Collections.Specialized;
using System.Windows;

namespace Microsoft.Research.DynamicDataDisplay.Charts.NewLine.Filters
{
	public sealed class FilterCollection : D3Collection<IPointsFilter2d>
	{
		protected override void OnItemAdding(IPointsFilter2d item)
		{
			if (item == null)
				throw new ArgumentNullException("item");
		}

		protected override void OnItemAdded(IPointsFilter2d item)
		{
			item.Changed += OnItemChanged;
		}

		private void OnItemChanged(object sender, EventArgs e)
		{
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}

		protected override void OnItemRemoving(IPointsFilter2d item)
		{
			item.Changed -= OnItemChanged;
		}

		internal IEnumerable<Point> Filter(IEnumerable<Point> points, Viewport2D viewport)
		{
			var screenRect = viewport.Output;
			var visibleRect = viewport.Visible;

			foreach (var filter in Items)
			{
				DependencyObject dependencyObject = filter as DependencyObject;
				if (dependencyObject != null)
				{
					DataSource2dContext.SetScreenRect(dependencyObject, screenRect);
					DataSource2dContext.SetVisibleRect(dependencyObject, visibleRect);
				}

				points = filter.Filter(points);
			}

			return points;
		}
	}
}
