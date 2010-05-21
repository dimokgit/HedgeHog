using System;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Research.DynamicDataDisplay.DataSources;
using Microsoft.Research.DynamicDataDisplay.Filters;
using Microsoft.Research.DynamicDataDisplay.PointMarkers;
using System.ComponentModel.Design.Serialization;
using System.ComponentModel;
using System.Windows;
using Microsoft.Research.DynamicDataDisplay.Common;
using System.Collections.Generic;

namespace Microsoft.Research.DynamicDataDisplay
{
	/// <summary>Control for plotting 2d images</summary>
	public class Plotter2D : Plotter
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Plotter2D"/> class.
		/// </summary>
		public Plotter2D()
			: base(PlotterLoadMode.Normal)
		{
			InitViewport();
		}

		private void InitViewport()
		{
			viewportPanel = new Canvas();
			Grid.SetColumn(viewportPanel, 1);
			Grid.SetRow(viewportPanel, 1);

			viewport = new Viewport2D(viewportPanel, this);
			if (LoadMode != PlotterLoadMode.Empty)
			{
				MainGrid.Children.Add(viewportPanel);
			}
		}

		protected Plotter2D(PlotterLoadMode loadMode)
			: base(loadMode)
		{
			if (loadMode != PlotterLoadMode.Empty)
			{
				InitViewport();
			}
		}

		private Panel viewportPanel;
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Panel ViewportPanel
		{
			get { return viewportPanel; }
			protected set { viewportPanel = value; }
		}

		private Viewport2D viewport;

		/// <summary>
		/// Gets the viewport.
		/// </summary>
		/// <value>The viewport.</value>
		[NotNull]
		public Viewport2D Viewport
		{
			get { return viewport; }
			protected set { viewport = value; }
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public DataTransform DataTransform
		{
			get { return viewport.Transform.DataTransform; }
			set { viewport.Transform = viewport.Transform.WithDataTransform(value); }
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public CoordinateTransform Transform
		{
			get { return viewport.Transform; }
			set { viewport.Transform = value; }
		}

		public void FitToView()
		{
			viewport.FitToView();
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public DataRect Visible
		{
			get { return viewport.Visible; }
			set { viewport.Visible = value; }
		}
	}
}