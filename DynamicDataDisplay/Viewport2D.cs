using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Research.DynamicDataDisplay.Common.Auxiliary;
using Microsoft.Research.DynamicDataDisplay.ViewportRestrictions;
using System.Diagnostics;
using Microsoft.Research.DynamicDataDisplay;
using Microsoft.Research.DynamicDataDisplay.Common.UndoSystem;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Windows.Data;
using Microsoft.Research.DynamicDataDisplay.Common;

namespace Microsoft.Research.DynamicDataDisplay
{
	/// <summary>
	/// Viewport2D provides virtual coordinates.
	/// </summary>
	public sealed partial class Viewport2D : DependencyObject
	{
		private readonly Plotter2D plotter;
		private readonly FrameworkElement hostElement;
		internal Viewport2D(FrameworkElement host, Plotter2D plotter)
		{
			this.hostElement = host;
			host.ClipToBounds = true;
			host.SizeChanged += new SizeChangedEventHandler(OnHostElementSizeChanged);

			this.plotter = plotter;
			plotter.Children.CollectionChanged += OnPlotterChildrenChanged;

			restrictions = new RestrictionCollection(this);
			restrictions.Add(new MinimalSizeRestriction());
			restrictions.CollectionChanged += restrictions_CollectionChanged;

			fitToViewRestrictions = new RestrictionCollection(this);
			fitToViewRestrictions.CollectionChanged += fitToViewRestrictions_CollectionChanged;

			readonlyContentBoundsHosts = new ReadOnlyObservableCollection<IPlotterElement>(contentBoundsHosts);

			UpdateVisible();
			UpdateTransform();
		}

		private void OnHostElementSizeChanged(object sender, SizeChangedEventArgs e)
		{
			SetValue(OutputPropertyKey, new Rect(e.NewSize));
			CoerceValue(VisibleProperty);
		}

		private void fitToViewRestrictions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (IsFittedToView)
			{
				CoerceValue(VisibleProperty);
			}
		}

		private void restrictions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			CoerceValue(VisibleProperty);
		}

    static Rect[] _rectArray = new Rect[] { new Rect(), new Rect() };
		private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
      if (e.Property.Name == "Output") {
        var rectArray = new Rect[] { (Rect)e.NewValue , (Rect)e.OldValue };
        if (!rectArray.Except(_rectArray).Any()) return;
        _rectArray = rectArray;
      }
			Viewport2D viewport = (Viewport2D)d;
			viewport.UpdateTransform();
			viewport.RaisePropertyChangedEvent(e);
		}

		public BindingExpressionBase SetBinding(DependencyProperty property, BindingBase binding)
		{
			return BindingOperations.SetBinding(this, property, binding);
		}

		/// <summary>
		/// Forces viewport to go to fit to view mode - clears locally set value of <see cref="Visible"/> property
		/// and sets it during the coercion process to a value of united content bounds of all charts inside of <see cref="Plotter"/>.
		/// </summary>
		public void FitToView()
		{
			if (!IsFittedToView)
			{
				ClearValue(VisibleProperty);
				CoerceValue(VisibleProperty);
			}
		}

		/// <summary>
		/// Gets a value indicating whether Viewport is fitted to view.
		/// </summary>
		/// <value>
		/// 	<c>true</c> if Viewport is fitted to view; otherwise, <c>false</c>.
		/// </value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsFittedToView
		{
			get { return ReadLocalValue(VisibleProperty) == DependencyProperty.UnsetValue; }
		}

		internal void UpdateVisible()
		{
			if (IsFittedToView)
			{
				CoerceValue(VisibleProperty);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		[EditorBrowsable(EditorBrowsableState.Never)]
		public Plotter2D Plotter
		{
			get { return plotter; }
		}

		private readonly RestrictionCollection restrictions;
		/// <summary>
		/// Gets the collection of <see cref="ViewportRestriction"/>s that are applied each time <see cref="Visible"/> is updated.
		/// </summary>
		/// <value>The restrictions.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
		public RestrictionCollection Restrictions
		{
			get { return restrictions; }
		}


		private readonly RestrictionCollection fitToViewRestrictions;

		/// <summary>
		/// Gets the collection of <see cref="ViewportRestriction"/>s that are applied only when Viewport is fitted to view.
		/// </summary>
		/// <value>The fit to view restrictions.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
		public RestrictionCollection FitToViewRestrictions
		{
			get { return fitToViewRestrictions; }
		}


		#region Output property

		/// <summary>
		/// Gets the rectangle in screen coordinates that is output.
		/// </summary>
		/// <value>The output.</value>
		public Rect Output
		{
			get { return (Rect)GetValue(OutputProperty); }
		}

		[SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
		private static readonly DependencyPropertyKey OutputPropertyKey = DependencyProperty.RegisterReadOnly(
			"Output",
			typeof(Rect),
			typeof(Viewport2D),
			new FrameworkPropertyMetadata(new Rect(0, 0, 1, 1), OnPropertyChanged));

		/// <summary>
		/// Identifies the <see cref="Output"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty OutputProperty = OutputPropertyKey.DependencyProperty;

		#endregion

		#region ContentBounds property

		/// <summary>
		/// Gets the united content bounds of all the charts.
		/// </summary>
		/// <value>The content bounds.</value>
		public DataRect ContentBounds
		{
			get { return (DataRect)GetValue(UnitedContentBoundsProperty); }
			internal set { SetValue(UnitedContentBoundsProperty, value); }
		}

		/// <summary>
		/// Identifies the <see cref="UnitedContentBounds"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty UnitedContentBoundsProperty = DependencyProperty.Register(
		  "UnitedContentBounds",
		  typeof(DataRect),
		  typeof(Viewport2D),
		  new FrameworkPropertyMetadata(DataRect.Empty));

		#endregion

		#region Visible property

		/// <summary>
		/// Gets or sets the visible rectangle.
		/// </summary>
		/// <value>The visible.</value>
		public DataRect Visible
		{
			get { return (DataRect)GetValue(VisibleProperty); }
			set { SetValue(VisibleProperty, value); }
		}

		/// <summary>
		/// Identifies the <see cref="Visible"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty VisibleProperty =
			DependencyProperty.Register("Visible", typeof(DataRect), typeof(Viewport2D),
			new FrameworkPropertyMetadata(
				new DataRect(0, 0, 1, 1),
				OnPropertyChanged,
				OnCoerceVisible),
			ValidateVisibleCallback);

		private static bool ValidateVisibleCallback(object value)
		{
			DataRect rect = (DataRect)value;

			return !rect.IsNaN();
		}

		private void UpdateContentBoundsHosts()
		{
			contentBoundsHosts.Clear();
			foreach (var item in plotter.Children)
			{
				DependencyObject dependencyObject = item as DependencyObject;
				if (dependencyObject != null)
				{
					bool isContentBoundsHost = Viewport2D.GetIsContentBoundsHost(dependencyObject);
					if (isContentBoundsHost)
					{
						contentBoundsHosts.Add(item);
					}
				}
			}

			UpdateVisible();
		}

		private readonly ObservableCollection<IPlotterElement> contentBoundsHosts = new ObservableCollection<IPlotterElement>();
		private readonly ReadOnlyObservableCollection<IPlotterElement> readonlyContentBoundsHosts;
		/// <summary>
		/// Gets the collection of all charts that can has its own content bounds.
		/// </summary>
		/// <value>The content bounds hosts.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ReadOnlyObservableCollection<IPlotterElement> ContentBoundsHosts
		{
			get { return readonlyContentBoundsHosts; }
		}

		private DataRect CoerceVisible(DataRect newVisible)
		{
			if (Plotter == null)
			{
				return newVisible;
			}

			bool isDefaultValue = newVisible == (DataRect)VisibleProperty.DefaultMetadata.DefaultValue;
			if (isDefaultValue)
			{
				newVisible = DataRect.Empty;
			}

			if (isDefaultValue && IsFittedToView)
			{
				// determining content bounds
				DataRect bounds = DataRect.Empty;

				foreach (var item in contentBoundsHosts)
				{
					var visual = plotter.VisualBindings[item];
					if (visual.Visibility == Visibility.Visible)
					{
						DataRect contentBounds = (DataRect)visual.GetValue(Viewport2D.ContentBoundsProperty);
						bounds.UnionFinite(contentBounds);
					}
				}

				DataRect viewportBounds = bounds;
				ContentBounds = bounds;

				// applying fit-to-view restrictions
				bounds = fitToViewRestrictions.Apply(Visible, bounds, this);

				// enlarging
				if (!bounds.IsEmpty)
				{
					bounds = CoordinateUtilities.RectZoom(bounds, bounds.GetCenter(), clipToBoundsEnlargeFactor);
				}
				else
				{
					bounds = (DataRect)VisibleProperty.DefaultMetadata.DefaultValue;
				}
				newVisible.Union(bounds);
			}

			if (newVisible.IsEmpty)
			{
				newVisible = (DataRect)VisibleProperty.DefaultMetadata.DefaultValue;
			}
			else if (newVisible.Width == 0 || newVisible.Height == 0)
			{
				DataRect defRect = (DataRect)VisibleProperty.DefaultMetadata.DefaultValue;
				Size size = newVisible.Size;
				Point loc = newVisible.Location;

				if (newVisible.Width == 0)
				{
					size.Width = defRect.Width;
					loc.X -= size.Width / 2;
				}
				if (newVisible.Height == 0)
				{
					size.Height = defRect.Height;
					loc.Y -= size.Height / 2;
				}

				newVisible = new DataRect(loc, size);
			}

			// apply domain restriction
			newVisible = domainRestriction.Apply(Visible, newVisible, this);

			// apply other restrictions
			newVisible = restrictions.Apply(Visible, newVisible, this);

			// applying transform's data domain restriction
			if (!transform.DataTransform.DataDomain.IsEmpty)
			{
				var newDataRect = newVisible.ViewportToData(transform);
				newDataRect = DataRect.Intersect(newDataRect, transform.DataTransform.DataDomain);
				newVisible = newDataRect.DataToViewport(transform);
			}

			if (newVisible.IsEmpty) newVisible = new Rect(0, 0, 1, 1);

			return newVisible;
		}

		private static object OnCoerceVisible(DependencyObject d, object newValue)
		{
			Viewport2D viewport = (Viewport2D)d;

			DataRect newRect = viewport.CoerceVisible((DataRect)newValue);

			if (newRect.Width == 0 || newRect.Height == 0)
			{
				// doesn't apply rects with zero square
				return DependencyProperty.UnsetValue;
			}
			else
			{
				return newRect;
			}
		}

		#endregion

		#region Domain

		private readonly DomainRestriction domainRestriction = new DomainRestriction { Domain = Rect.Empty };
		/// <summary>
		/// Gets or sets the domain - rectangle in viewport coordinates that limits maximal size of <see cref="Visible"/> rectangle.
		/// </summary>
		/// <value>The domain.</value>
		public DataRect Domain
		{
			get { return domainRestriction.Domain; }
			set
			{
				if (domainRestriction.Domain != value)
				{
					domainRestriction.Domain = value;
					DomainChanged.Raise(this);
					CoerceValue(VisibleProperty);
				}
			}
		}

		/// <summary>
		/// Occurs when <see cref="Domain"/> property changes.
		/// </summary>
		public event EventHandler DomainChanged;

		#endregion

		private double clipToBoundsEnlargeFactor = 1.10;
		/// <summary>
		/// Gets or sets the viewport enlarge factor.
		/// </summary>
		/// <remarks>
		/// Default value is 1.10.
		/// </remarks>
		/// <value>The clip to bounds factor.</value>
		public double ClipToBoundsEnlargeFactor
		{
			get { return clipToBoundsEnlargeFactor; }
			set
			{
				if (clipToBoundsEnlargeFactor != value)
				{
					clipToBoundsEnlargeFactor = value;
					UpdateVisible();
				}
			}
		}

		private void UpdateTransform()
		{
			transform = transform.WithRects(Visible, Output);
		}

		private CoordinateTransform transform = CoordinateTransform.CreateDefault();
		/// <summary>
		/// Gets or sets the coordinate transform of Viewport.
		/// </summary>
		/// <value>The transform.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		[NotNull]
		public CoordinateTransform Transform
		{
			get { return transform; }
			set
			{
				value.VerifyNotNull();

				if (value != transform)
				{
					var oldTransform = transform;

					transform = value;

					RaisePropertyChangedEvent("Transform", oldTransform, transform);
				}
			}
		}

		/// <summary>
		/// Occurs when viewport property changes.
		/// </summary>
		public event EventHandler<ExtendedPropertyChangedEventArgs> PropertyChanged;

		private void RaisePropertyChangedEvent(string propertyName, object oldValue, object newValue)
		{
			if (PropertyChanged != null)
			{
				PropertyChanged(this, new ExtendedPropertyChangedEventArgs { PropertyName = propertyName, OldValue = oldValue, NewValue = newValue });
			}
		}

		private void RaisePropertyChangedEvent(string propertyName)
		{
			if (PropertyChanged != null)
			{
				PropertyChanged(this, new ExtendedPropertyChangedEventArgs { PropertyName = propertyName });
			}
		}

		private void RaisePropertyChangedEvent(DependencyPropertyChangedEventArgs e)
		{
			if (PropertyChanged != null)
			{
				PropertyChanged(this, ExtendedPropertyChangedEventArgs.FromDependencyPropertyChanged(e));
			}
		}

		private void OnPlotterChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			bool updateVisible = false;
			if (e.NewItems != null)
			{
				foreach (IPlotterElement item in e.NewItems)
				{
					if (Viewport2D.GetIsContentBoundsHost(plotter.VisualBindings[item]))
					{
						DebugVerify.Is(!contentBoundsHosts.Contains(item));

						updateVisible = true;
						contentBoundsHosts.Add(item);
					}
				}
			}
			if (e.OldItems != null)
			{
				foreach (IPlotterElement item in e.OldItems)
				{
					if (contentBoundsHosts.Contains(item))
					{
						updateVisible = true;
						contentBoundsHosts.Remove(item);
					}
				}
			}

			if (updateVisible && IsFittedToView)
			{
				UpdateVisible();
			}
		}
	}
}
