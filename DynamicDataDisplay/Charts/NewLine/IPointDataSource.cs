using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace Microsoft.Research.DynamicDataDisplay.Charts.NewLine
{
	public interface IPointDataSource : IEnumerable<Point>
	{
		event EventHandler Changed;
	}
}
