using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Research.DynamicDataDisplay.Common;

namespace Microsoft.Research.DynamicDataDisplay
{
	public static class DataRectExtensions
	{
		internal static bool IsNaN(this DataRect rect)
		{
			return !rect.IsEmpty &&
				(
				rect.XMin.IsNaN() ||
				rect.YMin.IsNaN() ||
				rect.XMax.IsNaN() ||
				rect.YMax.IsNaN()
				);
		}

		public static Point GetCenter(this DataRect rect)
		{
			return new Point(rect.XMin + rect.Width * 0.5, rect.YMin + rect.Height * 0.5);
		}

		public static DataRect Zoom(this DataRect rect, Point to, double ratio)
		{
			return CoordinateUtilities.RectZoom(rect, to, ratio);
		}

		public static DataRect ZoomX(this DataRect rect, Point to, double ratio)
		{
			return CoordinateUtilities.RectZoomX(rect, to, ratio);
		}

		public static DataRect ZoomY(this DataRect rect, Point to, double ratio)
		{
			return CoordinateUtilities.RectZoomY(rect, to, ratio);
		}

	}
}
