using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Research.DynamicDataDisplay.Common;

namespace Microsoft.Research.DynamicDataDisplay
{
	public static class PlotterChildrenCollectionExtensions
	{
		public static void RemoveAll<T>(this PlotterChildrenCollection children)
		{
			var childrenToDelete = children.OfType<T>().ToList();

			foreach (var child in childrenToDelete)
			{
				children.Remove(child as IPlotterElement);
			}
		}
	}
}
