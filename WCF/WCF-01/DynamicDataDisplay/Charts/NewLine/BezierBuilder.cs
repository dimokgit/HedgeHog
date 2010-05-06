using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace Microsoft.Research.DynamicDataDisplay.Charts.NewLine
{
	class BezierResult
	{
		public Point StartPoint { get; set; }
		public IList<Point> OtherPoints { get; set; }
	}

	class BezierBuilder
	{
		public BezierResult Build(IEnumerable<Point> points)
		{
			throw new NotImplementedException();

			var list = points.Select(p => p.ToVector()).ToList();

			var result = new BezierResult();
			result.StartPoint = list[0].ToPoint();

			var pt = -list[0];
			var pt_1 = new Vector();
			var pt_2 = -list[0];
			double coeff_1 = 2;
			double coeff_2 = 0;
			for (int i = 1; i < list.Count; i++)
			{
				var old_1 = pt_1;
				var old_2 = pt_2;

			}
		}
	}
}
