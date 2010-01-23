﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Research.DynamicDataDisplay.ViewportRestrictions;

namespace Microsoft.Research.DynamicDataDisplay.Charts
{
	/// <summary>
	/// Represents an axis with ticks of <see cref="System.DateTime"/> type.
	/// </summary>
	public class DateTimeAxis : AxisBase<DateTime>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="DateTimeAxis"/> class.
		/// </summary>
		public DateTimeAxis()
			: base(new DateTimeAxisControl(), DoubleToDate,
				dt => dt.Ticks / 10000000000.0)
		{ }

		private ViewportRestriction restriction = new DateTimeHorizontalAxisRestriction();
		protected ViewportRestriction Restriction
		{
			get { return restriction; }
			set { restriction = value; }
		}

		protected override void OnPlotterAttached(Plotter2D plotter)
		{
			base.OnPlotterAttached(plotter);

			plotter.Viewport.Restrictions.Add(restriction);
		}

		protected override void OnPlotterDetaching(Plotter2D plotter)
		{
			plotter.Viewport.Restrictions.Remove(restriction);

			base.OnPlotterDetaching(plotter);
		}

		private static readonly long minTicks = DateTime.MinValue.Ticks;
		private static readonly long maxTicks = DateTime.MaxValue.Ticks;
		private static DateTime DoubleToDate(double d)
		{
			long ticks = (long)(d * 10000000000L);

			// todo should we throw an exception if number of ticks is too big or small?
			if (ticks < minTicks)
				ticks = minTicks;
			else if (ticks > maxTicks)
				ticks = maxTicks;

			return new DateTime(ticks);
		}

		/// <summary>
		/// Sets conversions of axis - functions used to convert values of axis type to and from double values of viewport.
		/// Sets both ConvertToDouble and ConvertFromDouble properties.
		/// </summary>
		/// <param name="min">The minimal viewport value.</param>
		/// <param name="minValue">The value of axis type, corresponding to minimal viewport value.</param>
		/// <param name="max">The maximal viewport value.</param>
		/// <param name="maxValue">The value of axis type, corresponding to maximal viewport value.</param>
		public override void SetConversion(double min, DateTime minValue, double max, DateTime maxValue)
		{
			var conversion = new DateTimeToDoubleConversion(min, minValue, max, maxValue);

			ConvertToDouble = conversion.ToDouble;
			ConvertFromDouble = conversion.FromDouble;
		}
	}
}
