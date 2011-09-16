using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;
using Microsoft.Research.DynamicDataDisplay;
using System.Diagnostics;

namespace Microsoft.Research.DynamicDataDisplay.Charts.Shapes
{
	/// <summary>
	/// Represents a simple draggable point with position bound to point in viewport coordinates, which allows to drag iself by mouse.
	/// </summary>
	public partial class DraggablePoint : ViewportUIContainer
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="DraggablePoint"/> class.
		/// </summary>
		public DraggablePoint()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DraggablePoint"/> class.
		/// </summary>
		/// <param name="position">The position of DraggablePoint.</param>
		public DraggablePoint(Point position) : this() { Position = position; }

		bool dragging = false;
		Point dragStart;
		Vector shift;
		protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
		{
			dragStart = e.GetPosition(Plotter.ViewportPanel).ScreenToData(Plotter.Viewport.Transform);
			shift = Position - dragStart;
			dragging = true;

			CaptureMouse();

			e.Handled = true;
		}

		protected override void OnPreviewMouseMove(MouseEventArgs e)
		{
			if (!dragging) return;

			Point mouseInData = e.GetPosition(Plotter.ViewportPanel).ScreenToData(Plotter.Viewport.Transform);

			if (mouseInData != dragStart)
			{
				Position = mouseInData + shift;
			}

			e.Handled = true;
		}

		protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
		{
			ReleaseMouseCapture();

			dragging = false;

			e.Handled = true;
      Dispatcher.BeginInvoke(new Action(() => Focus()));
		}
    protected override void OnGotFocus(RoutedEventArgs e) {
      base.OnGotFocus(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e) {
      base.OnPreviewKeyDown(e);
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e) {
      Debugger.Break();
    }
	}
}
