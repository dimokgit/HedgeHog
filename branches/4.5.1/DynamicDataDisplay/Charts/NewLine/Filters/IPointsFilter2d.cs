using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace Microsoft.Research.DynamicDataDisplay.Charts.NewLine.Filters
{
	public interface IPointsFilter2d
	{
		event EventHandler Changed;

		IEnumerable<Point> Filter(IEnumerable<Point> series);
	}
}
