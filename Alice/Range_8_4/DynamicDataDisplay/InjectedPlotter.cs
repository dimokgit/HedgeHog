using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using Microsoft.Research.DynamicDataDisplay.Common;

namespace Microsoft.Research.DynamicDataDisplay
{
	[SkipPropertyCheck]
	public class InjectedPlotter : ChartPlotter, IPlotterElement
	{
		public InjectedPlotter()
			: base(PlotterLoadMode.Empty)
		{
			ViewportPanel = new Canvas();
			Grid.SetColumn(ViewportPanel, 1);
			Grid.SetRow(ViewportPanel, 1);

			Viewport = new Viewport2D(ViewportPanel, this);
		}

		#region IPlotterElement Members

		void IPlotterElement.OnPlotterAttached(Plotter plotter)
		{
			this.plotter = (Plotter2D)plotter;

			plotter.MainGrid.Children.Add(ViewportPanel);

			HeaderPanel = plotter.HeaderPanel;
			FooterPanel = plotter.FooterPanel;

			LeftPanel = plotter.LeftPanel;
			BottomPanel = plotter.BottomPanel;
			RightPanel = plotter.RightPanel;
			TopPanel = plotter.BottomPanel;

			MainCanvas = plotter.MainCanvas;
			CentralGrid = plotter.CentralGrid;
			MainGrid = plotter.MainGrid;
			ParallelCanvas = plotter.ParallelCanvas;

			OnLoaded();
		}

		void IPlotterElement.OnPlotterDetaching(Plotter plotter)
		{
			plotter.MainGrid.Children.Remove(ViewportPanel);

			this.plotter = null;
		}

		private Plotter2D plotter;
		public Plotter2D Plotter
		{
			get { return plotter; }
		}

		Plotter IPlotterElement.Plotter
		{
			get { return plotter; }
		}

		#endregion
	}
}
