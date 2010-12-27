using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Research.DynamicDataDisplay;
using Microsoft.Research.DynamicDataDisplay.Charts;
using Microsoft.Research.DynamicDataDisplay.ViewportRestrictions;
using Microsoft.Research.DynamicDataDisplay.Charts.Navigation;
using Microsoft.Research.DynamicDataDisplay.Common;
using Microsoft.Research.DynamicDataDisplay.DataSources;
using HedgeHog.Bars;
using HB = HedgeHog.Bars;
using Order2GoAddIn;
using FXW = Order2GoAddIn.FXCoreWrapper;
using HedgeHog.Shared;
using System.Diagnostics;
using Microsoft.Research.DynamicDataDisplay.Charts.Shapes;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using HedgeHog.Charter;


namespace HedgeHog {
  /// <summary>
  /// Interaction logic for Corridors.xaml
  /// </summary>
  public partial class Corridors : HedgeHog.Models.WindowModel {

    private bool _IsInPlay;
    public bool IsInPlay {
      get { return _IsInPlay; }
      set {
        if (_IsInPlay != value) {
          _IsInPlay = value;
          RaisePropertyChangedCore();
          OnPlay(value, PlayStartDate, DelayInSeconds);
        }
      }
    }

    private double _DelayInSeconds;
    public double DelayInSeconds {
      get { return _DelayInSeconds; }
      set {
        if (_DelayInSeconds != value) {
          _DelayInSeconds = value;
          RaisePropertyChangedCore();
        }
      }
    }

    private DateTime _PlayStartDate;
    public DateTime PlayStartDate {
      get { return _PlayStartDate; }
      set {
        if (_PlayStartDate != value) {
          _PlayStartDate = value;
          RaisePropertyChangedCore();
        }
      }
    }

    List<DateTime> animatedTimeX = new List<DateTime>();
    List<DateTime> animatedTime0X = new List<DateTime>();
    List<double> animatedPriceY = new List<double>();
    EnumerableDataSource<double> animatedDataSource = null;

    List<double> animatedPrice1Y = new List<double>();
    EnumerableDataSource<double> animatedDataSource1 = null;

    List<DateTime> animatedVoltTimeX = new List<DateTime>();
    List<double> animatedVoltValueY = new List<double>();
    EnumerableDataSource<double> animatedVoltDataSource = null;

    List<DateTime> animatedVolt1TimeX = new List<DateTime>();
    List<double> animatedVolt1ValueY = new List<double>();
    EnumerableDataSource<double> animatedVolt1DataSource = null;

    TextBlock infoBox = new TextBlock() { FontFamily = new FontFamily("Courier New") };
    ViewportUIContainer viewPortContainer = new ViewportUIContainer();

    public double CorridorHeightMultiplier { get; set; }
    public Func<PriceBar, double> PriceBarValue;

    public Func<Rate, double> GetPriceFunc { get; set; }

    public double CenterOfMass { get; set; }


    #region Lines
    public LineGraph PriceLineGraph { get; set; }
    static Color priceLineGraphColor = Colors.Black;
    static Color priceLineGraphColorBuy = Colors.DarkGreen;
    static Color priceLineGraphColorSell = Colors.Navy;
    bool? buySell;
    public void SetPriceLineColor(bool? buySell) {
      if (this.buySell != buySell) {
        this.buySell = buySell;
        PriceLineGraph.LinePen.Brush =
          new SolidColorBrush(buySell.HasValue ? buySell.Value ? priceLineGraphColorBuy : priceLineGraphColorSell : priceLineGraphColor);
      }
    }

    HorizontalLine lineMax = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.DarkOrange), StrokeThickness = 1 };
    public double LineMax { set { lineMax.Value = value; } }

    HorizontalLine lineMaxAvg = new HorizontalLine() { StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.Brown) };
    public double LineMaxAvg {
      set {
        lineMaxAvg.Value = value;
        lineMaxAvg.ToolTip = value;
      }
    }

    HorizontalLine lineMin = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.LimeGreen), StrokeThickness = 1, StrokeDashArray = { 1 } };
    public double LineMin { set { lineMin.Value = value; } }

    HorizontalLine lineMinAvg = new HorizontalLine() { StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.Navy) };
    double LineMinAvg {
      set {
        lineMinAvg.Value = value;
        lineMinAvg.ToolTip = value;
      }
    }

    HorizontalLine centerOfMassHLine = new HorizontalLine() { StrokeThickness = 2, Stroke = new SolidColorBrush(Colors.SteelBlue) };
    double CenterOfMassHLine {
      set {
        centerOfMassHLine.Value = value;
        centerOfMassHLine.ToolTip = value;
      }
    }


    HorizontalLine lineNetSell = new HorizontalLine() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkBlue) };
    double LineNetSell { set { lineNetSell.Value = value; } }

    HorizontalLine lineNetBuy = new HorizontalLine() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    double LineNetBuy { set { lineNetBuy.Value = value; } }

    HorizontalLine lineAvgAsk = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Pink) };
    double LineAvgAsk { set { lineAvgAsk.Value = value; } }

    HorizontalLine lineAvgBid = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Pink) };
    public double LineAvgBid { set { lineAvgBid.Value = value; } }

    #region TimeLines
    VerticalLine _lineTimeMax;
    VerticalLine lineTimeMax {
      get {
        if (_lineTimeMax == null) {
          _lineTimeMax = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Brown) };
          _lineTimeMax.MouseLeftButtonDown += new MouseButtonEventHandler(DraggablePoint_MouseLeftButtonDown);
          _lineTimeMax.SetBinding(SimpleLine.StrokeThicknessProperty, new Binding("IsMouseDirectlyOver") {
            Source = _lineTimeMax,
            Converter = new BoolToSrtingConverter(),
            ConverterParameter = "1|1|2"
          });
        }
        return _lineTimeMax;
      }
    }
    DateTime LineTimeMax { set { lineTimeMax.Value = dateAxis.ConvertToDouble(value); } }

    VerticalLine lineTimeMin = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Navy) };
    DateTime LineTimeMin { set { lineTimeMin.Value = dateAxis.ConvertToDouble(value); } }

    VerticalLine lineTimeAvg = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkGreen) };
    DateTime LineTimeAvg { set { lineTimeAvg.Value = dateAxis.ConvertToDouble(value); } }
    #endregion

    #region

    Segment gannLine = new Segment() { StrokeThickness = 2, StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.Green) };
    Rate[] GannLine {
      set {
        gannLine.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].TrendLine);
        gannLine.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].TrendLine);
      }
    }


    Segment trendLine = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkGray) };
    Rate[] TrendLine {
      set {
        trendLine.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg1);
        trendLine.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].PriceAvg1);
      }
    }

    Segment trendLine1 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine1 {
      set {
        trendLine1.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg2);
        trendLine1.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].PriceAvg2);
      }
    }

    Segment trendLine11 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed), StrokeDashArray = { 2 } };
    Rate[] TrendLine11 {
      set {
        var height = CorridorHeightMultiplier * (value[0].PriceAvg2 - value[0].PriceAvg3);
        trendLine11.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg02);
        trendLine11.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].PriceAvg02);
      }
    }

    Segment trendLine2 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine2 {
      set {
        trendLine2.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg3);
        trendLine2.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].PriceAvg3);
      }
    }

    Segment trendLine22 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed), StrokeDashArray = { 2 } };
    Rate[] TrendLine22 {
      set {
        var height = CorridorHeightMultiplier * (value[0].PriceAvg2 - value[0].PriceAvg3);
        trendLine22.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg03);
        trendLine22.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].PriceAvg03);
      }
    }
    #endregion

    private GannAngleOffsetDraggablePoint _GannAngleOffsetPoint;
    public GannAngleOffsetDraggablePoint GannAngleOffsetPoint {
      get {
        if (_GannAngleOffsetPoint == null) {
          _GannAngleOffsetPoint = new GannAngleOffsetDraggablePoint(dateAxis.ConvertFromDouble,new NumberToStringAutoFormatConverter());
          _GannAngleOffsetPoint.PositionChanged += _GannAngleOffsetPoint_PositionChanged;
        }
        return _GannAngleOffsetPoint; 
      }
    }

    Scheduler GannAngleChangedScheduler = new Scheduler(Application.Current.Dispatcher, TimeSpan.FromSeconds(.5));
    void _GannAngleOffsetPoint_PositionChanged(object sender, PositionChangedEventArgs e) {
      var offset = GannAngleOffsetPoint.GetAngleByPosition(e.Position);
      //GannAngleOffsetPoint.ToolTip = string.Format("Tangent:{0}", offset);
      if (GannAngleOffsetPoint.IsMouseCaptured)
        GannAngleChangedScheduler.TryRun(() => OnGannAngleChanged(offset));
    }

    public event EventHandler<GannAngleOffsetChangedEventArgs> GannAngleOffsetChanged;
    private void OnGannAngleChanged(double offset) {
      if (GannAngleOffsetChanged != null)
        GannAngleOffsetChanged(this, new GannAngleOffsetChangedEventArgs(offset));
    }

    DraggablePoint _CorridorStartPointX;
    DraggablePoint CorridorStartPointX {
      get {
        if (_CorridorStartPointX == null) {
          _CorridorStartPointX = new DraggablePoint();

          _CorridorStartPointX.PositionChanged += CorridorStartPointX_PositionChanged;
          _CorridorStartPointX.IsMouseCapturedChanged += CorridorStartPointX_IsMouseCapturedChanged;

          //_CorridorStartPointX.MouseLeftButtonDown += new MouseButtonEventHandler(DraggablePoint_MouseLeftButtonDown);
          //_CorridorStartPointX.PreviewMouseLeftButtonUp += new MouseButtonEventHandler(_CorridorStartPointX_PreviewMouseLeftButtonDown);
          //_CorridorStartPointX.PreviewMouseUp += new MouseButtonEventHandler(_CorridorStartPointX_PreviewMouseUp);
          //_CorridorStartPointX.GotFocus += new RoutedEventHandler(_CorridorStartPointX_GotFocus);
          //_CorridorStartPointX.KeyDown += new KeyEventHandler(DraggablePoint_KeyDown);

          corridorStartDateScheduler = new ThreadScheduler(OnCorridorStartPositionChanged, (s, e) => { });
        }
        return _CorridorStartPointX;
      }
    }

    void _CorridorStartPointX_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    }

    void _CorridorStartPointX_GotFocus(object sender, RoutedEventArgs e) {
      ActiveDraggablePoint = (DraggablePoint)sender;
    }

    void _CorridorStartPointX_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      DraggablePoint_MouseLeftButtonDown(sender, e);
    }

    void DraggablePoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {

      ActiveDraggablePoint = LineToPoint[(SimpleLine)sender];
      var b = ActiveDraggablePoint.Focus();
    }
    void DraggablePoint_KeyDown(object sender, KeyEventArgs e) {
      var dp = (DraggablePoint)sender;
      if (e.Key == Key.Escape)
        dp.Dispatcher.BeginInvoke(new Action(() => dp.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))));
      if (new[] { Key.Left, Key.Right }.Contains(e.Key)) {
        e.Handled = true;
        var x = ConvertToDateTime(ActiveDraggablePoint.Position.X);
        var i = animatedTimeX.FindIndex(d => d == x);
        var step = e.Key == Key.Right ? 1 : -1;
        dp.Position = new Point(ConvertToDouble(animatedTimeX[i + step]), ActiveDraggablePoint.Position.Y);
      }
    }
    DraggablePoint SupportPointY = new DraggablePoint();
    DraggablePoint ResistancePointY = new DraggablePoint();

    DraggablePoint _ActiveDraggablePoint;

    public DraggablePoint ActiveDraggablePoint {
      get { return _ActiveDraggablePoint; }
      set { _ActiveDraggablePoint = value; }
    }

    List<HorizontalLine> otherHLines = new List<HorizontalLine>();
    List<VerticalLine> otherVLines = new List<VerticalLine>();
    #endregion

    #region Ctor
    public

    Corridors() : this("",null) { }
    public Corridors(string name,CompositionContainer container) {
      if( container!=null) container.SatisfyImportsOnce(this);
      this.Name = name.Replace("/", "");
      InitializeComponent();
      this.Title += ": " + name;
      plotter.Children.RemoveAll<AxisNavigation>();

      Closing += new System.ComponentModel.CancelEventHandler(Corridors_Closing);
      PlayStartDateBox.TextChanged += new TextChangedEventHandler(PlayStartDateBox_TextChanged);
    }

    void PlayStartDateBox_TextChanged(object sender, TextChangedEventArgs e) {
    }
    #endregion

    #region Window Events
    void Corridors_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      e.Cancel = true;
      Application.Current.Dispatcher.BeginInvoke(
        DispatcherPriority.Background,
        (DispatcherOperationCallback)delegate(object o) {
        Hide();
        return null;
      },
          null);

    }
    #endregion

    #region DataSources
    EnumerableDataSource<Point> ds = null;
    EnumerableDataSource<ChartTick> dsAvg1 = null;
    EnumerableDataSource<ChartTick> dsAvg2 = null;
    EnumerableDataSource<ChartTick> dsAvg3 = null;
    EnumerableDataSource<ChartTick> dsVolts = null;
    EnumerableDataSource<Volt> dsVoltsPoly = null;
    #endregion

    List<HorizontalLine> FibLevels = new List<HorizontalLine>();
    List<ColoredSegment> GannAngles = new List<ColoredSegment>();
    Dictionary<SimpleLine, DraggablePoint> LineToPoint = new Dictionary<SimpleLine, DraggablePoint>();
    private void CreateCurrencyDataSource(bool doVolts) {
      if (IsPlotterInitialised) return;
      IsPlotterInitialised = true;
      plotter.Children.RemoveAt(0);

      #region Add Main Graph
      {
        EnumerableDataSource<DateTime> xSrc = new EnumerableDataSource<DateTime>(animatedTimeX);
        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedDataSource = new EnumerableDataSource<double>(animatedPriceY);
        animatedDataSource.SetYMapping(y => y);
        this.PriceLineGraph = plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource), priceLineGraphColor, 1, "");
        this.PriceLineGraph.Description.LegendItem.Visibility = System.Windows.Visibility.Collapsed;

        animatedDataSource1 = new EnumerableDataSource<double>(animatedPrice1Y);
        animatedDataSource1.SetYMapping(y => y);
        plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource1), Colors.DarkGray, 1, "")
          .Description.LegendItem.Visibility = Visibility.Collapsed;

        Border infoBorder = new Border() {
          BorderBrush = new SolidColorBrush(Colors.Maroon), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3)
        };
        infoBorder.Child = infoBox;
        viewPortContainer.Content = infoBorder;
        plotter.Children.Add(viewPortContainer);
      }
      //var ticksLineGraph = plotter.AddLineGraph(Ticks.AsDataSource(), Colors.Black, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
      #endregion

      #region Add Volts Graph
      if (doVolts) {
        innerPlotter.Viewport.Restrictions.Add(new InjectionDelegateRestriction(
          plotter.Viewport,
          rect => {
            rect.XMin = plotter.Viewport.Visible.XMin;
            rect.Width = plotter.Viewport.Visible.Width;
            return rect;
          }));
        EnumerableDataSource<DateTime> xSrc = new EnumerableDataSource<DateTime>(animatedVoltTimeX);
        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedVoltDataSource = new EnumerableDataSource<double>(animatedVoltValueY);
        animatedVoltDataSource.SetYMapping(y => y);
        innerPlotter.AddLineGraph(new CompositeDataSource(xSrc, animatedVoltDataSource), Colors.Tan, 1, "")
          .Description.LegendItem.Visibility = Visibility.Collapsed;

        xSrc = new EnumerableDataSource<DateTime>(animatedVolt1TimeX);
        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedVolt1DataSource = new EnumerableDataSource<double>(animatedVolt1ValueY);
        animatedVolt1DataSource.SetYMapping(y => y);
        var lg = innerPlotter.AddLineGraph(new CompositeDataSource(xSrc, animatedVolt1DataSource), Colors.LimeGreen, 1, "");
          lg.Description.LegendItem.Visibility = Visibility.Collapsed;
          lg.Opacity = .25;
        //innerPlotter.Children.Remove(plotter.Children.OfType<HorizontalAxis>().Single());
          plotter.Children.OfType<VerticalAxis>().First().Placement = AxisPlacement.Right;
          innerPlotter.Children.OfType<VerticalAxis>().First().Placement = AxisPlacement.Left;
      } else {
        innerPlotter.Children.Remove(innerPlotter.Children.OfType<VerticalAxis>().Single());
        plotter.Children.OfType<VerticalAxis>().First().Placement = AxisPlacement.Right;
      }
      #endregion



      #region Add Lines
      plotter.Children.Add(lineNetSell);
      plotter.Children.Add(lineNetBuy);
      innerPlotter.Children.Add(lineMax);
      plotter.Children.Add(lineMaxAvg);
      plotter.Children.Add(lineMinAvg);
      innerPlotter.Children.Add(lineMin);
      plotter.Children.Add(lineAvgAsk);
      plotter.Children.Add(lineAvgBid);
      plotter.Children.Add(lineTimeMin);
      plotter.Children.Add(lineTimeAvg);
      plotter.Children.Add(trendLine);
      plotter.Children.Add(trendLine1);
      plotter.Children.Add(trendLine11);
      plotter.Children.Add(trendLine2);
      plotter.Children.Add(trendLine22);
      plotter.Children.Add(gannLine);
      plotter.Children.Add(centerOfMassHLine);

      plotter.Children.Add(lineTimeMax);
      plotter.Children.Add(CorridorStartPointX);
      LineToPoint.Add(lineTimeMax, CorridorStartPointX);

      plotter.Children.Add(SupportPointY);
      plotter.Children.Add(ResistancePointY);

      plotter.Children.Add(GannAngleOffsetPoint);

      InsertFibLines();
      InsertGannLines();
      
      SupportPointY.PositionChanged += new EventHandler<PositionChangedEventArgs>(SupportPointY_PositionChanged);
      ResistancePointY.PositionChanged += new EventHandler<PositionChangedEventArgs>(ResistancePointY_PositionChanged);

      plotter.KeyDown += new KeyEventHandler(plotter_KeyDown);
      plotter.PreviewKeyDown += new KeyEventHandler(plotter_PreviewKeyDown);

      #endregion
    }

    private void InsertGannLines() {
      for (var i = 0; i < BarBase.GannAngles.Length; i++) {
        var color = BarBase.GannAngle1x1 == i ? Colors.Black : Colors.DarkGray;
        var hl = new ColoredSegment() { 
          Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeDashArray = { 2 }, SelectedColor = Colors.Maroon };
        GannAngles.Add(hl);
        plotter.Children.Add(hl);
      }
    }
    private void InsertFibLines() {
      foreach (var i in Enumerable.Range(0, Fibonacci.Levels(0, 0).Length)) {
        var hl = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.MidnightBlue), StrokeThickness = 1 };
        FibLevels.Add(hl);
        plotter.Children.Add(hl);
      }
    }

    void plotter_PreviewKeyDown(object sender, KeyEventArgs e) {
    }

    void plotter_KeyDown(object sender, KeyEventArgs e) {

      e.Handled = true;
    }

    #region Event Handlers
    void ResistancePointY_PositionChanged(object sender, PositionChangedEventArgs e) {
      LineMaxAvg = e.Position.Y;
      OnSupportResistanceChanged(false, e.Position);
    }

    void SupportPointY_PositionChanged(object sender, PositionChangedEventArgs e) {
      LineMinAvg = e.Position.Y;
      OnSupportResistanceChanged(true, e.Position);
    }
    DateTime CorridorStartPositionOld;
    DateTime GetPriceStartDate(DateTime startDateContinuous) {
      var x = animatedTimeX.OrderBy(d => (d - startDateContinuous).Duration()).First();
      return animatedTime0X[animatedTimeX.IndexOf(x)];
    }
    ThreadScheduler corridorStartDateScheduler;
    void CorridorStartPointX_IsMouseCapturedChanged(object sender, DependencyPropertyChangedEventArgs e) {
      if ((bool)e.NewValue) CorridorStartPositionOld = GetPriceStartDate(dateAxis.ConvertFromDouble(CorridorStartPointX.Position.X));
      else if (CorridorStartPositionChanged != null && !corridorStartDateScheduler.IsRunning){
        corridorStartDateScheduler.Run();
    }}

    void CorridorStartPointX_PositionChanged(object sender, PositionChangedEventArgs e) {
      if (CorridorStartPositionChanged != null && (ActiveDraggablePoint == sender || CorridorStartPointX.IsMouseCaptured) && !corridorStartDateScheduler.IsRunning) {
        corridorStartDateScheduler.Command = () => {
          CorridorStartPositionChanged(this,
          new CorridorPositionChangedEventArgs(GetPriceStartDate(dateAxis.ConvertFromDouble(e.Position.X)), dateAxis.ConvertFromDouble(e.PreviousPosition.X)));
        };
        corridorStartDateScheduler.Run();
      }
    }
    #endregion

    #region Events
    public event EventHandler<PlayEventArgs> Play;
    protected void OnPlay(bool play,DateTime startDate,double delayInSeconds) {
      if (Play != null) Play(this, new PlayEventArgs(play, startDate, delayInSeconds));
    }

    public event EventHandler<CorridorPositionChangedEventArgs> CorridorStartPositionChanged;
    private void OnCorridorStartPositionChanged() {
      var x = GetPriceStartDate(ConvertToDateTime(CorridorStartPointX.Position.X));
      CorridorStartPositionChanged(this, new CorridorPositionChangedEventArgs(x, CorridorStartPositionOld));
    }

    public event EventHandler<SupportResistanceChangedEventArgs> SupportResistanceChanged;
    protected void OnSupportResistanceChanged(bool isSupport, Point position) {
      SetFibLevels(ResistancePointY.Position.Y, SupportPointY.Position.Y);
      var isMouseCaptured = (isSupport ? SupportPointY : ResistancePointY).IsMouseCaptured;
      if (isMouseCaptured && SupportResistanceChanged != null)
        SupportResistanceChanged(this, new SupportResistanceChangedEventArgs(isSupport, position.Y, position.Y));
    }
    #endregion

    #region Update Ticks
    void UpdateTicks(ObservableCollection<ChartTick> dest, List<ChartTick> src) {
      var srcDict = new Dictionary<DateTime, ChartTick>();
      src.ForEach(s => srcDict.Add(s.Time, s));
      dest.ToList().ForEach(d => {
        if (srcDict.ContainsKey(d.Time)) d.Price = srcDict[d.Time].Price;
      });
      if (((double)dest.Count / src.Count).Between(0.5, 1.5)) {
        //var ddd = dest.Except(src,new Tick()).ToArray();
        var delete = dest.Except(src, new ChartTick()).ToList();
        //(from d in dest
        // join s in src on d.Time equals s.Time into grp
        // from g in grp.DefaultIfEmpty()
        // where g == null
        // select d).ToArray();
        delete.ForEach(d => dest.Remove(d));
        if (dest.Count > 0) {
          var time = dest.Max(t => t.Time).AddMinutes(-1);
          dest.Where(t => t.Time > time).ToList().ForEach(t => dest.Remove(t));
        }
        //var ddd = src.Intersect(dest,new Tick()).ToArray();
        delete = src.Intersect(dest, new ChartTick()).ToList();
        //(from s in src
        //        join d in dest on s.Time equals d.Time
        //        select s).ToList();
        delete.ForEach(d => src.Remove(d));
        if (dest.Count > 0) {
          var time = dest.Min(t => t.Time);
          src.Where(t => t.Time < time).OrderByDescending(t => t.Time).ToList().ForEach(s => dest.Insert(0, s));
          time = dest.Max(t => t.Time);
          src.Where(t => t.Time > time).OrderBy(t => t.Time).ToList().ForEach(s => dest.Add(s));
        } else dest.AddMany(src);
      } else {
        dest.Clear();
        dest.AddMany(src.OrderBy(t => t.Time));
      }
    }
    void UpdateTicks(ObservableCollection<Point> dest, List<Point> src, TimeSpan periodSpan) {
      if (true) {
        if (dest.Count() == 0)
          dest.AddMany(src);
        else {
          var lastPeriod = (dateAxis.ConvertFromDouble(dest.Last().X) - dateAxis.ConvertFromDouble(src.Last().X)).Duration();
          if (lastPeriod > periodSpan.Multiply(5)) {
            dest.Clear();
            UpdateTicks(dest, src, periodSpan);
          } else {
            dest.RemoveAt(0);
            dest.Add(src.Last());
          }
        }
        return;
      }
      //var srcDict = new Dictionary<double, Point>();
      //src.ForEach(s => srcDict.Add(s.X, s));
      //dest.ToList().ForEach(d => {
      //  if (srcDict.ContainsKey(d.X)) d.Y = srcDict[d.X].Y;
      //});
      if (((double)dest.Count / src.Count).Between(0.95, 1.05)) {
        var delete = dest.Except(src).ToList();
        delete.ForEach(d => dest.Remove(d));
        if (dest.Count > 0) {
          var time = dateAxis.ConvertToDouble(dateAxis.ConvertFromDouble(dest.Max(t => t.X)).AddMinutes(-1));
          dest.Where(t => t.X > time).ToList().ForEach(t => dest.Remove(t));
        }
        delete = src.Intersect(dest).ToList();
        delete.ForEach(d => src.Remove(d));
        if (dest.Count > 0) {
          var time = dest.Min(t => t.X);
          src.Where(t => t.X < time).OrderByDescending(t => t.X).ToList().ForEach(s => dest.Insert(0, s));
          time = dest.Max(t => t.X);
          src.Where(t => t.X > time).OrderBy(t => t.X).ToList().ForEach(s => dest.Add(s));
        } else dest.AddMany(src);
      } else {
        dest.Clear();
        dest.AddMany(src.OrderBy(t => t.X));
      }
    }
    #endregion

    bool inRendering;
    private bool IsPlotterInitialised;
    public void AddTicks(Price lastPrice, List<Rate> ticks, List<Volt> voltsByTick,
  double voltageHigh, double voltageCurr, double priceMaxAvg, double priceMinAvg,
  double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, double[] priceAverageAskBid) {
      AddTicks(lastPrice, ticks.ToArray(), null, new string[0], null, voltageHigh, voltageCurr, priceMaxAvg, priceMinAvg,
                      netBuy, netSell, timeHigh, timeCurr, DateTime.MinValue, priceAverageAskBid);
    }
    public void AddTicks(Price lastPrice, Rate[] ticks, PriceBar[][] voltsByTicks, string[] info, bool? trendHighlight,
                          double voltageHigh, double voltageCurr, double priceMaxAvg, double priceMinAvg,
                          double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, DateTime timeLow, double[] priceAverageAskBid) {
                            if (inRendering) return;
      var voltsByTick = voltsByTicks[0];
      #region Conversion Functions
      var rateToTick = new Func<Rate, ChartTick>(t => new ChartTick() { Price = t.PriceAvg, Time = t.StartDateContinuous });
      var roundTo = lastPrice.Digits - 1;
      var rateToPoint = new Func<Rate, Point>(t =>
        new Point(dateAxis.ConvertToDouble(t.StartDateContinuous), t.PriceAvg.Round(roundTo)));
      //(t.PriceAvg > t.PriceAvg1 ? t.PriceHigh : t.PriceAvg < t.PriceAvg1 ? t.PriceLow : t.PriceAvg).Round(roundTo)));  
      #endregion
      List<Point> minuteTicks = null;
      #region Set DataSources
      if (ticks.Any(t => t != null && t.PriceAvg1 != 0)) {
        #region Set Trendlines
        if (false && trendHighlight.HasValue)
          if (trendHighlight.Value) {
            trendLine1.StrokeThickness = 2;
            trendLine2.StrokeThickness = 1;
          } else {
            trendLine1.StrokeThickness = 1;
            trendLine2.StrokeThickness = 2;
          }
        #endregion
        //TicksAvg1.Clear();
        //var avg = ticks.Count > maxTicks ? FXW.GetMinuteTicks(ticks.Select(t => new FXW.Tick(t.StartDateContinuous, t.PriceAvg1, t.PriceAvg1, false)), 1).Select(rateToTick) :
        //  ticks.Select(t => new FXW.Tick(t.StartDateContinuous, t.PriceAvg1, t.PriceAvg1, false)).Select(tickToTick);
        //UpdateTicks(TicksAvg1, avg);
      }
      var aw = plotter.ActualWidth;
      #endregion
      #region Update Main Chart
      {
        var correlation = 0;// global::alglib.pearsoncorrelation(animatedPriceY.ToArray(), ticks.Select(r => r.PriceAvg).ToArray());
        if (correlation < 1.99) {
          ReAdjustXY(animatedTimeX, animatedPriceY, ticks.Count());
          ReAdjustXY(animatedTime0X, ticks.Count());
          ReAdjustXY(animatedPrice1Y, ticks.Count());
          {
            var i = 0;
            var lastRate = ticks.Aggregate((rp, rn) => {
              SetPoint(i++, rp.PriceAvg/* < rn.PriceAvg ? rp.PriceLow : rp.PriceHigh*/, rp.PriceCMA, rp);
              return rn;
            });
            SetPoint(i, lastRate.PriceClose, lastRate.PriceCMA, lastRate);
          }
          for (var i = 100000; i < ticks.Count(); i++) {
            animatedPriceY[i] = i < ticks.Count() - 1 ? GetPriceFunc(ticks[i]) : ticks[i].PriceClose;
            animatedTimeX[i] = ticks[i].StartDateContinuous;
            animatedTime0X[i] = ticks[i].StartDate;
          }
          if (voltsByTick != null) {
            ReAdjustXY(animatedVoltTimeX, animatedVoltValueY, voltsByTick.Length);
            for (var i = 0; i < voltsByTick.Count(); i++) {
              animatedVoltValueY[i] = PriceBarValue(voltsByTick[i]);
              animatedVoltTimeX[i] = voltsByTick[i].StartDateContinuous;
            }
          }
          if (voltsByTicks != null && voltsByTicks.Length > 1) {
            ReAdjustXY(animatedVolt1TimeX, animatedVolt1ValueY, voltsByTicks[1].Length);
            for (var i = 0; i < voltsByTicks[1].Count(); i++) {
              animatedVolt1ValueY[i] = voltsByTicks[1][i].Power;
              animatedVolt1TimeX[i] = voltsByTicks[1][i].StartDateContinuous;
            }
          }

        } else {
          var dateFirst = ticks.Min(r => r.StartDateContinuous);
          var remove = animatedTimeX.TakeWhile(t => t < dateFirst).ToArray();
          animatedTimeX.RemoveRange(0, remove.Length);
          animatedTimeX.Remove(animatedTimeX.Last());
          animatedPriceY.RemoveRange(0, remove.Length);
          animatedPriceY.Remove(animatedPriceY.Last());
          var dateLast = animatedTimeX.Last();
          var add = ticks.Where(t => t.StartDateContinuous > dateLast).ToArray();
          animatedPriceY.AddRange(add.Select(r => r.PriceAvg));
          animatedTimeX.AddRange(add.Select(r => r.StartDateContinuous));
        }
      }
      //animatedVoltDataSource.RaiseDataChanged();
      //animatedVolt1DataSource.RaiseDataChanged();
      #endregion

      //plotter.FitToView();
      //System.Diagnostics.Debug.WriteLine("AddTicks:" + (DateTime.Now - d).TotalMilliseconds + " ms.");

      var animatedPriceYMax = animatedPriceY.Max();
      var animatedPriceYMin = animatedPriceY.Min();
      var animatedTimeXMax = animatedTimeX.Max();
      var animatedTimeXMin = animatedTimeX.Min();
      var rateFirst = ticks.First(r => r.PriceAvg1 != 0);
      var rateLast = ticks.Last(r => r.PriceAvg1 != 0);
      var ratesForCorridor = new[] { rateFirst, rateLast };
      //var ratesforTrend = new[] { ticks.First(r => r.TrendLine > 0), ticks.Last(r => r.TrendLine > 0) };
      var errorMessage = "Period:" + (ticks[1].StartDate - ticks[0].StartDate).Duration().Minutes + " minutes.";
      Action a = () => {
        CreateCurrencyDataSource(voltsByTick != null);
        try {
          SetGannAngles(ticks, SelectedGannAngleIndex);
          animatedDataSource.RaiseDataChanged();
          animatedVoltDataSource.RaiseDataChanged();
          
        } catch (InvalidOperationException exc) {
          plotter.FitToView();
          throw new InvalidOperationException(errorMessage, exc);
        } finally {
          TrendLine = TrendLine1 = TrendLine11 = TrendLine2 = TrendLine22 = ratesForCorridor;
          //GannLine = ratesforTrend;
          infoBox.Text = string.Join(Environment.NewLine, info);
          //var up = animatedPriceY.Last() < (animatedPriceY.Max() + animatedPriceY.Min()) / 2;
          var up = animatedPriceY.First() < (animatedPriceYMax + animatedPriceYMin) / 2;
          var yHeight = animatedPriceYMax - animatedPriceYMin;
          var xWidth = dateAxis.ConvertToDouble(animatedTimeXMax) - dateAxis.ConvertToDouble(animatedTimeXMin);
          var yOffset = yHeight * infoBox.ActualHeight / plotter.ActualHeight / 2;
          var xOffset = xWidth * infoBox.ActualWidth / plotter.ActualWidth / 2;
          var y = (up ? animatedPriceYMax - yOffset : animatedPriceYMin + yOffset);
          if (viewPortContainer.ActualWidth < 10 && infoBox.ActualWidth > 0) {
            plotter.Children.Remove(viewPortContainer);
            var child = viewPortContainer.Content;
            viewPortContainer.Content = null;
            viewPortContainer = new ViewportUIContainer();
            viewPortContainer.Content = child;
            plotter.Children.Add(viewPortContainer);
          }
          viewPortContainer.Position = new Point(dateAxis.ConvertToDouble(animatedTimeXMin) + xOffset, y);
          viewPortContainer.InvalidateVisual();

          #region Set Lines
          LineMax = voltageHigh;
          LineMin = voltageCurr;

          LineMaxAvg = priceMaxAvg;
          ResistancePointY.Position = new Point(dateAxis.ConvertToDouble(animatedTimeX[0]), priceMaxAvg);

          LineMinAvg = priceMinAvg;
          SupportPointY.Position = new Point(dateAxis.ConvertToDouble(animatedTimeX[0]), priceMinAvg);

          CenterOfMassHLine = CenterOfMass;

          //SetFibLevels(priceMaxAvg, priceMinAvg);

          LineTimeMax = timeHigh;
          var corridorTime = ticks.First(r => r.StartDateContinuous == timeHigh).StartDate;
          lineTimeMax.ToolTip = corridorTime;
          if (!CorridorStartPointX.IsMouseCaptured) {
            CorridorStartPointX.Position = new Point(dateAxis.ConvertToDouble(timeHigh), ticks.Min(r => r.PriceAvg) + ticks.Height() / 2);
            CorridorStartPointX.ToolTip = corridorTime.ToString("MM/dd/yyyy HH:mm");
          }
          LineTimeMin = timeCurr;
          LineTimeAvg = timeLow;
          LineNetSell = netSell;
          LineNetBuy = netBuy;
          #endregion
        }
      };

      if (Dispatcher.CheckAccess())
        a();
      else
      this.Dispatcher.BeginInvoke(new Action(() => {
        inRendering = true;
        try {
          a();
        } finally {
          inRendering = false;
        }
      }), DispatcherPriority.ContextIdle);
    }

    private void SetPoint(int i,double y,double[] cma, Rate rateLast) {
      animatedPriceY[i] = y;
      animatedPrice1Y[i] = cma == null ? y : cma[2];
      animatedTimeX[i] = rateLast.StartDateContinuous;
      animatedTime0X[i] = rateLast.StartDate;
    }

    private void SetGannAngles(ICollection<Rate> rates,int selectedIndex) {
      var rateFirst = rates.First(r => r.GannPrices[0] > 0);
      var rateLast = rates.Reverse().First(r => r.GannPrices[0] > 0);
      foreach (var i in Enumerable.Range(0, GannAngles.Count)) {
        var gannPriceFirst = rateFirst.GannPrices[i];
        GannAngles[i].SelectedValue = selectedIndex - i;
        GannAngles[i].StartPoint = new Point(dateAxis.ConvertToDouble(rateFirst.StartDateContinuous), gannPriceFirst);
        GannAngles[i].EndPoint = new Point(dateAxis.ConvertToDouble(rateLast.StartDateContinuous), rateLast.GannPrices[i]);
        if (i == GannAngles.Count / 2) {
          GannAngleOffsetPoint.Anchor = new Point(ConvertToDouble(rateFirst.StartDate), rateFirst.GannPrice1x1);
          if (!GannAngleOffsetPoint.IsMouseCaptured) {
            GannAngleOffsetPoint.BarPeriod = TimeSpan.FromMinutes(1);
            GannAngleOffsetPoint.Position = new Point(ConvertToDouble(rateLast.StartDate), rateLast.GannPrice1x1);
          }
        }
      }
    }

    private void SetFibLevels(double priceMaxAvg, double priceMinAvg) {
      var fibLevels = Fibonacci.Levels(priceMaxAvg, priceMinAvg);
      foreach (var i in Enumerable.Range(0, FibLevels.Count)) {
        FibLevels[i].Value = fibLevels[i];
        FibLevels[i].ToolTip = fibLevels[i];
      }
    }

    #region Helpers
    private void ReAdjustXY(List<DateTime> X, List<double> Y, int count) {
      while (Y.Count > count) {
        X.RemoveAt(0);
        Y.RemoveAt(0);
      }
      while (Y.Count < count) {
        X.Add(DateTime.MinValue);
        Y.Add(0);
      }
    }
    private void ReAdjustXY(List<double> Y, int count) {
      while (Y.Count > count) {
        Y.RemoveAt(0);
      }
      while (Y.Count < count) {
        Y.Add(0);
      }
    }
    private void ReAdjustXY(List<DateTime> X, int count) {
      while (X.Count > count) {
        X.RemoveAt(0);
      }
      while (X.Count < count) {
        X.Add(DateTime.MinValue);
      }
    }
    public DateTime ConvertToDateTime(double d) { return dateAxis.ConvertFromDouble(d); }
    public double ConvertToDouble(DateTime d) { return dateAxis.ConvertToDouble(d); }
    #endregion


    public int SelectedGannAngleIndex { get; set; }
  }

  public class ChartTick : INotifyPropertyChanged, IEqualityComparer<ChartTick> {
    double price = 0;
    public double Price {
      get { return price; }
      set {
        if (price != value) {
          price = value;
          if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Price"));
        }
      }
    }
    public DateTime Time { get; set; }

    public override int GetHashCode() {
      return (int)this.Time.Ticks;
    }

    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler PropertyChanged;

    #endregion

    #region IEqualityComparer<Tick> Members

    public bool Equals(ChartTick x, ChartTick y) {
      return x.Time == y.Time && x.Price == y.Price;
    }

    public int GetHashCode(ChartTick obj) {
      return obj.Time.GetHashCode() ^ obj.Price.GetHashCode();
    }

    #endregion
  }

  public class PlayEventArgs : EventArgs {
    public bool Play { get; set; }
    public DateTime StartDate { get; set; }
    public TimeSpan Delay { get; set; }
    public PlayEventArgs(bool play, DateTime startDate, double delayInSeconds) : this(play,startDate, TimeSpan.FromSeconds(delayInSeconds)) { }
    public PlayEventArgs(bool play,DateTime startDate, TimeSpan delay) {
      this.Play = play;
      this.StartDate = startDate;
      this.Delay = delay;
    }
  }

  public class GannAngleOffsetChangedEventArgs : EventArgs {
    public double Offset { get; set; }
    public GannAngleOffsetChangedEventArgs(double offset) {
      this.Offset = offset;
    }
  }
  public class SupportResistanceChangedEventArgs : PositionChangedBaseEventArgs<double> {
    public bool IsSupport { get; set; }
    public SupportResistanceChangedEventArgs(bool isSupport, double newPosition, double oldPosition) : base(newPosition, oldPosition) {
      this.IsSupport = isSupport;
    }
  }

  public class CorridorPositionChangedEventArgs : PositionChangedBaseEventArgs<DateTime> {
    public CorridorPositionChangedEventArgs(DateTime newPosition, DateTime oldPosition) : base(newPosition, oldPosition) { }
  }
  public class PositionChangedBaseEventArgs<T> : EventArgs {
    public T NewPosition { get; set; }
    public T OldPosition { get; set; }
    public PositionChangedBaseEventArgs(T newPosition, T oldPosition) {
      this.NewPosition = newPosition;
      this.OldPosition = oldPosition;
    }
  }
}
