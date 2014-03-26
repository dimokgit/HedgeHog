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
using Microsoft.Research.DynamicDataDisplay.Charts.Shapes;
using Microsoft.Research.DynamicDataDisplay.Charts;
using Microsoft.Research.DynamicDataDisplay;
using HedgeHog.Bars;
using HedgeHog.DateTimeZone;
using System.Collections.ObjectModel;
using HedgeHog.Models;
using HedgeHog.Shared;
using HedgeHog.Charter.Metadata;
using Microsoft.Research.DynamicDataDisplay.DataSources;
using Microsoft.Research.DynamicDataDisplay.ViewportRestrictions;
using System.Windows.Threading;
using HedgeHog.Charter;
using HedgeHog.Metadata;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using HedgeHog.Shared.Messages;
using System.Reactive.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using ReactiveUI;
using System.Collections.Specialized;
using Microsoft.Research.DynamicDataDisplay.Charts.Navigation;
using System.Globalization;
using System.Threading;
using System.Windows.Interactivity;
using Microsoft.Expression.Interactivity.Layout;

namespace HedgeHog {
  public class CharterControlMessage : GalaSoft.MvvmLight.Messaging.Messenger { }
  /// <summary>
  /// Interaction logic for CharterControl.xaml
  /// </summary>
  public partial class CharterControl : Models.UserControlModel{
    public enum MessageType { Add, Remove }
    public CharterControl():this("",null) {
    }
    public class ColorPalette {
      public string BackgroundDefault { get; set; }
      public string BackgroundNotActive { get; set; }
      public string GraphBuy { get; set; }
      public string GraphSell { get; set; }
      public string LevelBuy { get; set; }
      public string LevelSell { get; set; }
      public string BackgroundComposite { get { return BackgroundNotActive + "|" + BackgroundNotActive + "|" + BackgroundDefault; } }
    }
    static ColorPalette ColorPaletteDefault =       new ColorPalette(){
        BackgroundDefault="#FFF7F3F7",
        BackgroundNotActive = "#44F75D59",
        GraphBuy  = "DarkGreen",
        GraphSell = "DarkRed",
        LevelBuy = "DarkRed",
        LevelSell = "Navy"
      };

    List<ColorPalette> ColorPaletteList = new List<ColorPalette>() { ColorPaletteDefault };

    #region ColorPalette
    private ColorPalette _ColorPaletteCurrent = ColorPaletteDefault;
    public ColorPalette ColorPaletteCurrent {
      get { return _ColorPaletteCurrent; }
      set {
        if (_ColorPaletteCurrent != value) {
          _ColorPaletteCurrent = value;
          OnPropertyChanged("ColorPalette");
          OnPropertyChanged("BackgroundCurrent");
        }
      }
    }
    public string BackgroundCurrent {
      get {
        return IsActive ? ColorPaletteCurrent.BackgroundDefault : ColorPaletteCurrent.BackgroundNotActive;
      }
    }
    #endregion
    public CharterControl(string name, CompositionContainer container = null) {
      if (container != null) container.SatisfyImportsOnce(this);
      this.Name = name.Replace("/", "");
      InitializeComponent();
      DispatcherScheduler.Current.Schedule(() => OnPropertyChanged(Metadata.CharterControlMetadata.Header));
    }

    #region tm
    private object _tm;
    public object tm {
      get { return _tm; }
      set {
        if (_tm != value) {
          _tm = value;
          OnPropertyChanged("tm");
        }
      }
    }
    
    #endregion

    #region Attached Properties


    public static DateTime GetTime(DependencyObject obj) {
      return (DateTime)obj.GetValue(TimeProperty);
    }

    public static void SetTime(DependencyObject obj, DateTime value) {
      obj.SetValue(TimeProperty, value);
    }

    // Using a DependencyProperty as the backing store for Time.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.RegisterAttached("Time", typeof(DateTime), typeof(CharterControl));


    #region IsInteractive
    public static bool GetIsInteractive(DependencyObject obj) {
      return (bool)obj.GetValue(IsInteractiveProperty);
    }

    public static void SetIsInteractive(DependencyObject obj, bool value) {
      obj.SetValue(IsInteractiveProperty, value);
    }

    // Using a DependencyProperty as the backing store for IsInteractive.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.RegisterAttached("IsInteractive", typeof(bool), typeof(CharterControl));

    #endregion

    #region Friend


    public static IPlotterElement GetFriend(DependencyObject obj) {
      return (IPlotterElement)obj.GetValue(FriendProperty);
    }

    public static void SetFriend(DependencyObject obj, IPlotterElement value) {
      obj.SetValue(FriendProperty, value);
    }

    // Using a DependencyProperty as the backing store for Friend.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty FriendProperty =
        DependencyProperty.RegisterAttached("Friend", typeof(IPlotterElement), typeof(CharterControl));

    #endregion

    #endregion



    public bool IsParentHidden {
      get;
      set;
    }

    // Using a DependencyProperty as the backing store for IsParentHidden.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsParentHiddenProperty =
        DependencyProperty.Register("IsParentHidden", typeof(bool), typeof(CharterControl), new UIPropertyMetadata((d, p) => {
          ((CharterControl)d).IsParentHidden = (bool)p.NewValue;
        }));

    private int _barsPeriod;
    public int BarsPeriod {
      get { return _barsPeriod; }
      set {
        if (_barsPeriod == value) return;
        _barsPeriod = value;
        OnPropertyChanged(CharterControlMetadata.BarsPeriod);
        OnPropertyChanged(CharterControlMetadata.Header);
      }
    }

    private int _BarsCount;
    public int BarsCount {
      get { return _BarsCount; }
      set {
        if (_BarsCount != value) {
          _BarsCount = value;
          OnPropertyChanged(CharterControlMetadata.BarsCount);
          OnPropertyChanged(Metadata.CharterControlMetadata.Header);
        }
      }
    }


    //†‡∆
    string _HeaderText;

    public string HeaderText {
      get { return _HeaderText; }
      set {
        if (_HeaderText != value) {
          _HeaderText = value;
          OnPropertyChanged("Header");
        }
      }
    }
    public string Header { get { return Name + HeaderText; } }

    public bool IsActive {
      get { return (bool)GetValue(IsActiveProperty); }
      set { 
        SetValue(IsActiveProperty, value);
      }
    }

    // Using a DependencyProperty as the backing store for IsActive.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register("IsActive", typeof(bool), typeof(CharterControl), new UIPropertyMetadata(false, (d, p) => {
          (d as CharterControl).OnPropertyChanged("BackgroundCurrent");
        }));



    public bool IsSelected {
      get { return (bool)GetValue(IsSelectedProperty); }
      set { SetValue(IsSelectedProperty, value); }
    }

    // Using a DependencyProperty as the backing store for IsSelected.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register("IsSelected", typeof(bool), typeof(CharterControl), new UIPropertyMetadata(true));

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

    public double SuppResMinimumDistance { get; set; }

    List<DateTime> animatedTimeX = new List<DateTime>();
    List<DateTime> animatedTime0X = new List<DateTime>();
    List<double> animatedPriceY = new List<double>();
    EnumerableDataSource<double> animatedDataSource = null;
    EnumerableDataSource<double> animatedDataSource1 = null;

    List<double> animatedPriceBidY = new List<double>();
    EnumerableDataSource<double> animatedDataSourceBid = null;

    List<double> animatedPrice1Y = new List<double>();

    List<DateTime> animatedVoltTimeX = new List<DateTime>();
    List<double> animatedVoltValueY = new List<double>();
    EnumerableDataSource<double> animatedVoltDataSource = null;

    List<DateTime> animatedVolt1TimeX = new List<DateTime>();
    List<double> animatedVolt1ValueY = new List<double>();
    EnumerableDataSource<double> animatedVolt1DataSource = null;

    TextBlock _infoBox;
    TextBlock infoBox {
      get { return _infoBox ?? (_infoBox = new TextBlock() { FontFamily = new FontFamily("Courier New") }); }
    }
    ViewportUIContainer viewPortContainer = new ViewportUIContainer();

    public double CorridorHeightMultiplier { get; set; }
    public Func<PriceBar, double> PriceBarValue;

    public Func<Rate, double> GetPriceHigh { get; set; }
    public Func<Rate, double> GetPriceLow { get; set; }

    public double CenterOfMassBuy { get; set; }
    public double CenterOfMassSell { get; set; }


    #region Lines
    public LineGraph PriceLineGraph { get; set; }
    public LineGraph PriceLineGraphBid { get; set; }
    static Color priceLineGraphColorAsk = Colors.Maroon;
    static Color priceLineGraphColorBid = Colors.Navy;
    static Color priceLineGraphColorBuy = Colors.DarkGreen;
    static Color priceLineGraphColorSell = Colors.DarkRed;
    bool? isBuyOrSell;
    public void SetPriceLineColor(bool? isBuyOrSell) {
      if (PriceLineGraph!=null && this.isBuyOrSell != isBuyOrSell) {
        GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
          PriceLineGraph.LinePen.Brush = new SolidColorBrush(isBuyOrSell.HasValue ? isBuyOrSell.Value ? priceLineGraphColorBuy : priceLineGraphColorSell : priceLineGraphColorAsk);
          if (PriceLineGraphBid != null)
            PriceLineGraphBid.LinePen.Brush = new SolidColorBrush(isBuyOrSell.HasValue ? isBuyOrSell.Value ? priceLineGraphColorBuy : priceLineGraphColorSell : priceLineGraphColorBid);
          this.isBuyOrSell = isBuyOrSell;
        });
      }
    }


    HorizontalLine _voltageHigh;
    HorizontalLine voltageHigh {
      get {
        if (_voltageHigh == null) {
          _voltageHigh = new HorizontalLine { Stroke = new SolidColorBrush(Colors.OrangeRed), StrokeThickness = 1 };
          if (innerPlotter != null)
            innerPlotter.Children.Add(_voltageHigh);
        }
        return _voltageHigh;
      }
    }
    public double VoltageHigh { set { 
      voltageHigh.Value = value;
      voltageHigh.ToolTip = value;
    } }

    HorizontalLine _voltageAverage;
    HorizontalLine voltageAverage {
      get {
        if (_voltageAverage == null) {
          _voltageAverage = new HorizontalLine { Stroke = new SolidColorBrush(Colors.DarkOrange), StrokeThickness = 1 };
          if (innerPlotter != null)
            innerPlotter.Children.Add(_voltageAverage);
        }
        return _voltageAverage;
      }
    }
    public double VoltageAverage {
      set {
        voltageAverage.Value = value;
        voltageAverage.ToolTip = value;
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

    private bool _DoShowCenterOfMass;
    public bool DoShowCenterOfMass {
      get { return true|| _DoShowCenterOfMass; }
      set {
        if (_DoShowCenterOfMass != value) {
          _DoShowCenterOfMass = value;
          OnPropertyChanged("DoShowCenterOfMass");
        }
      }
    }

    static Brush centerOfMassBrush = new SolidColorBrush(Colors.SteelBlue);
    static int centerOfMassStrokeThickness = 0;
    static DoubleCollection centerOfMassStrokeDashArray = new DoubleCollection(new double[] { 5, 2, 2, 2 });
    HorizontalLine CenterOfMassFactory(double value = 0.0) {
      return new HorizontalLine() {
        StrokeThickness = centerOfMassStrokeThickness, StrokeDashArray = centerOfMassStrokeDashArray, Stroke = centerOfMassBrush
      };

    }
    HorizontalLine _centerOfMassHLineHigh;
    HorizontalLine centerOfMassHLineHigh {
      get {
        if (_centerOfMassHLineHigh == null) {
          _centerOfMassHLineHigh = CenterOfMassFactory();
          _centerOfMassHLineHigh.SetBinding(HorizontalLine.VisibilityProperty, new Binding("DoShowCenterOfMass") { Converter = new BooleanToVisibilityConverter() });
        }
        return _centerOfMassHLineHigh;
      }
    }
    double CenterOfMassHLineHigh {
      set {
        centerOfMassHLineHigh.Value = value;
        centerOfMassHLineHigh.ToolTip = value;
      }
    }
    HorizontalLine _centerOfMassHLineLow;
    HorizontalLine centerOfMassHLineLow {
      get {
        if (_centerOfMassHLineLow == null) {
          _centerOfMassHLineLow = CenterOfMassFactory();
          _centerOfMassHLineLow.SetBinding(HorizontalLine.VisibilityProperty, new Binding("DoShowCenterOfMass") { Converter = new BooleanToVisibilityConverter() });
        }
        return _centerOfMassHLineLow;
      }
    }
    double CenterOfMassHLineLow {
      set {
        centerOfMassHLineLow.Value = value;
        centerOfMassHLineLow.ToolTip = value;
      }
    }

    HorizontalLine magnetPrice;
    public double MagnetPrice {
      set {
        GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
          if (magnetPrice == null)
            plotter.Children.Add(magnetPrice = new HorizontalLine() { StrokeThickness = 2, Stroke = new SolidColorBrush(Colors.DarkViolet) });
          magnetPrice.Dispatcher.BeginInvoke(new Action(() => magnetPrice.Value = value));
        });
      }
    }

    HorizontalLine lineNetSell = new HorizontalLine() { StrokeThickness = 2, StrokeDashArray = new DoubleCollection(StrokeArrayForTrades), Stroke = new SolidColorBrush(Colors.DarkRed) };
    double LineNetSell { set { lineNetSell.Value = value; } }

    HorizontalLine lineNetBuy = new HorizontalLine() { StrokeThickness = 2, StrokeDashArray = new DoubleCollection(StrokeArrayForTrades), Stroke = new SolidColorBrush(Colors.DarkGreen) };
    double LineNetBuy { set { lineNetBuy.Value = value; } }

    HorizontalLine lineAvgAsk = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DodgerBlue) };
    public double LineAvgAsk { set { lineAvgAsk.Value = value; } }

    HorizontalLine lineAvgBid = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DodgerBlue) };
    public double LineAvgBid { set { lineAvgBid.Value = value; } }

    #region TimeLines
    bool showDrags = false;
    public bool ShowDrags {
      get { return showDrags; }
      set {
        if (showDrags == value) return;
        showDrags = value;
        OnPropertyChanged("ShowDrags");
      }
    }
    #region TimeShort
    Binding ShowDragBindingFactory() { return new Binding("ShowDrags") { Converter = new BooleanToVisibilityConverter() }; }
    DraggablePoint _lineTimeShortDraggablePoint;
    VerticalLine _lineTimeShort;
    public Rate LineTimeShort {
      set {
        plotter.Dispatcher.BeginInvoke(new Action(() => {
          if (_lineTimeShort == null) {
            _lineTimeShort = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.OrangeRed) };
            _lineTimeShort.SetAnchor(_lineTimeShortDraggablePoint = new DraggablePoint());
            plotter.Children.Add(_lineTimeShort);
            plotter.Children.Add(_lineTimeShortDraggablePoint);
            _lineTimeShortDraggablePoint.PositionChanged += _lineTimeShortDraggablePoint_PositionChanged;
            _lineTimeShortDraggablePoint.SetBinding(DraggablePoint.VisibilityProperty, ShowDragBindingFactory());
            //_lineTimeShortDraggablePoint.Visibility = System.Windows.Visibility.Visible;
          }
          _lineTimeShortDraggablePoint.Position = new Point(dateAxis.ConvertToDouble(value.StartDateContinuous), CorridorStartPointX.Position.Y - 20 * PipSize);
          _lineTimeShortDraggablePoint.ToolTip = value.StartDate + Environment.NewLine + "Dist:" + value.Distance;
        }));
      }
    }

    public event EventHandler<PositionChangedBaseEventArgs<DateTime>> LineTimeShortChanged;
    void _lineTimeShortDraggablePoint_PositionChanged(object sender, PositionChangedEventArgs e) {
      var dp = sender as DraggablePoint;
      var isMouseCaptured = dp.IsMouseCaptured;
      var isInteractive = GetIsInteractive(dp);
      if ((isMouseCaptured || isInteractive) && LineTimeShortChanged != null) {
        var now = GetPriceStartDate(ConvertToDateTime(e.Position.X));
        var then = GetPriceStartDate(ConvertToDateTime(e.PreviousPosition.X));
        LineTimeShortChanged(this, new PositionChangedBaseEventArgs<DateTime>(now, then));
      }
    }
    #endregion

    #region TimeMiddle
    DraggablePoint _lineTimeMiddleDraggablePoint;
    VerticalLine _lineTimeMiddle;
    public Rate LineTimeMiddle {
      set {
        plotter.Dispatcher.BeginInvoke(new Action(() => {
          if (_lineTimeMiddle == null) {
            _lineTimeMiddle = new VerticalLine() { StrokeDashArray = new DoubleCollection(StrokeArrayForTrades), StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Navy) };
            _lineTimeMiddle.SetAnchor(_lineTimeMiddleDraggablePoint = new DraggablePoint() { Visibility = Visibility.Collapsed });
            _lineTimeMiddle.SetBinding(VerticalLine.StrokeThicknessProperty, new Binding("IsMouseOver") { Source = _lineTimeMiddle, Converter = BoolToSrtingConverter.Default, ConverterParameter = "1|1|3" });
            _lineTimeMiddle.MouseLeftButtonDown += (s, e) => {
              _lineTimeMiddleDraggablePoint.Visibility = _lineTimeMiddleDraggablePoint.Visibility == Visibility.Visible 
                ? Visibility.Collapsed : Visibility.Visible;
            };
            _lineTimeMiddleDraggablePoint.PositionChanged += _lineTimeMiddleDraggablePoint_PositionChanged;
            plotter.Children.Add(_lineTimeMiddle);
            plotter.Children.Add(_lineTimeMiddleDraggablePoint);
          }
          if (value == null)
            _lineTimeMiddle.Visibility = System.Windows.Visibility.Collapsed;
          else {
            _lineTimeMiddle.Visibility = System.Windows.Visibility.Visible;
            _lineTimeMiddleDraggablePoint.Position = new Point(dateAxis.ConvertToDouble(value.StartDateContinuous), CorridorStartPointX.Position.Y + 20 * PipSize);
            _lineTimeMiddleDraggablePoint.ToolTip = value.StartDate + Environment.NewLine + "Dist:" + value.Distance;
          }
        }));
      }
    }


    public event EventHandler<PositionChangedBaseEventArgs<DateTime>> LineTimeMiddleChanged;
    void _lineTimeMiddleDraggablePoint_PositionChanged(object sender, PositionChangedEventArgs e) {
      var dp = sender as DraggablePoint;
      var isMouseCaptured = dp.IsMouseCaptured;
      var isInteractive = GetIsInteractive(dp);
      if ((isMouseCaptured || isInteractive) && LineTimeMiddleChanged != null) {
        var now = GetPriceStartDate(ConvertToDateTime(e.Position.X));
        var then = GetPriceStartDate(ConvertToDateTime(e.PreviousPosition.X));
        LineTimeMiddleChanged(this, new PositionChangedBaseEventArgs<DateTime>(now, then));
      }
    }
    #endregion

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

    VerticalLine lineTimeMin = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.LimeGreen) };
    public DateTime LineTimeMin {
      set {
        plotter.Dispatcher.Invoke(() => lineTimeMin.Value = dateAxis.ConvertToDouble(value));
      }
    }

    VerticalLine lineTimeAvg = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Navy), Opacity = .5 };
    DateTime LineTimeAvg {
      set {
        lineTimeAvg.Value = dateAxis.ConvertToDouble(value);
        if (!CorridorStopPointX.IsMouseCaptured) {
          CorridorStopPointX.Position = new Point(dateAxis.ConvertToDouble(value), CorridorStartPointX.Position.Y);
          CorridorStopPointX.ToolTip = value.ToString("MM/dd/yyyy HH:mm");
          //CorridorStopPointX.SetBinding(DraggablePoint.VisibilityProperty, ShowDragBindingFactory());
          CorridorStopPointX.Visibility = System.Windows.Visibility.Visible;
        }

      }
    }

    VerticalLine lineTimeTakeProfit = new VerticalLine() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.LimeGreen) };
    public DateTime LineTimeTakeProfit {
      set { GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => lineTimeTakeProfit.Value = dateAxis.ConvertToDouble(value)); }
    }
    VerticalLine lineTimeTakeProfit1 = new VerticalLine() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.LimeGreen) };
    public DateTime LineTimeTakeProfit1 {
      set { GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => lineTimeTakeProfit1.Value = dateAxis.ConvertToDouble(value)); }
    }
    VerticalLine lineTimeTakeProfit2 = new VerticalLine() {  StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.LimeGreen) };
    public DateTime LineTimeTakeProfit2 {
      set { GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => lineTimeTakeProfit2.Value = dateAxis.ConvertToDouble(value)); }
    }
    VerticalLine lineTimeTakeProfit3 = new VerticalLine() {  StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.LimeGreen) };
    public DateTime LineTimeTakeProfit3 {
      set { GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => lineTimeTakeProfit3.Value = dateAxis.ConvertToDouble(value)); }
    }

    #endregion

    #region

    Segment gannLine = new Segment() { StrokeThickness = 2, StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.Green) };
    Rate[] GannLine {
      set {
        gannLine.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].TrendLine);
        gannLine.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].TrendLine);
      }
    }

    double _trendLinesH;
    double _trendLinesY;
    public void SetTrendLines(Rate[] rates) {
      if (!rates.Any()) return;
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
        TrendLine = TrendLine2 = TrendLine02 = TrendLine3 = TrendLine03 = TrendLine21 = TrendLine31 =  rates;

        var rateLast = rates.LastBC();
        var timeHigh = rateLast.StartDateContinuous;
        var corridorTime = rateLast.StartDate;
        lineTimeMax.ToolTip = corridorTime;
        if (!CorridorStartPointX.IsMouseCaptured) {
          CorridorStartPointX.Position = 
            new Point(dateAxis.ConvertToDouble(timeHigh), _trendLinesY = rates.Min(r => r.PriceAvg) + (_trendLinesH = rates.Height()) / 2);
          CorridorStartPointX.ToolTip = corridorTime.ToString("MM/dd/yyyy HH:mm");
        }
      });
    }

    #region Trend Lines
    Segment trendLine = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkGray) };
    Rate[] TrendLine {
      set {
        if (value == null)
          trendLine.Visibility = System.Windows.Visibility.Collapsed;
        else {
          trendLine.Visibility = System.Windows.Visibility.Visible;
          trendLine.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg1);
          trendLine.EndPoint = new Point(dateAxis.ConvertToDouble(value.LastBC().StartDateContinuous), value.LastBC().PriceAvg1);
          TradeLineStartPosition = trendLine.StartPoint;
          TradeLineStopPosition = trendLine.EndPoint;
        }
      }
    }

    Segment trendLine21 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine21 {
      set {
        if (value == null || value[0].PriceAvg21 == 0)
          trendLine21.Visibility = System.Windows.Visibility.Collapsed;
        else {
          trendLine21.Visibility = System.Windows.Visibility.Visible;
          trendLine21.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg21);
          trendLine21.EndPoint = new Point(dateAxis.ConvertToDouble(value.LastBC().StartDateContinuous), value.LastBC().PriceAvg21);
        }
      }
    }

    Segment trendLine2 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine2 {
      set {
        if (value == null || value[0].PriceAvg2 == 0)
          trendLine2.Visibility = System.Windows.Visibility.Collapsed;
        else {
          trendLine2.Visibility = System.Windows.Visibility.Visible;
          trendLine2.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg2);
          trendLine2.EndPoint = new Point(dateAxis.ConvertToDouble(value.LastBC().StartDateContinuous), value.LastBC().PriceAvg2);
        }
      }
    }

    Segment trendLine02 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed), StrokeDashArray = { 2 } };
    Rate[] TrendLine02 {
      set {
        if (value == null || value[0].PriceAvg02 == 0)
          trendLine02.Visibility = System.Windows.Visibility.Collapsed;
        else {
          trendLine02.Visibility = System.Windows.Visibility.Visible;
          trendLine02.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg02);
          trendLine02.EndPoint = new Point(dateAxis.ConvertToDouble(value.LastBC().StartDateContinuous), value.LastBC().PriceAvg02);
        }
      }
    }

    Segment trendLine31 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine31 {
      set {
        if (value == null || value[0].PriceAvg31 == 0)
          trendLine31.Visibility = System.Windows.Visibility.Collapsed;
        else {
          trendLine31.Visibility = System.Windows.Visibility.Visible;
          trendLine31.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg31);
          trendLine31.EndPoint = new Point(dateAxis.ConvertToDouble(value.LastBC().StartDateContinuous), value.LastBC().PriceAvg31);
        }
      }
    }

    Segment trendLine3 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine3 {
      set {
        if (value == null || value[0].PriceAvg3 == 0)
          trendLine3.Visibility = System.Windows.Visibility.Collapsed;
        else {
          trendLine3.Visibility = System.Windows.Visibility.Visible;
          trendLine3.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg3);
          trendLine3.EndPoint = new Point(dateAxis.ConvertToDouble(value.Last().StartDateContinuous), value.Last().PriceAvg3);
        }
      }
    }

    Segment trendLine03 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed), StrokeDashArray = { 2 } };
    Rate[] TrendLine03 {
      set {
        if (value == null || value[0].PriceAvg03 == 0)
          trendLine03.Visibility = System.Windows.Visibility.Collapsed;
        else {
          trendLine03.Visibility = System.Windows.Visibility.Visible;
          trendLine03.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg03);
          trendLine03.EndPoint = new Point(dateAxis.ConvertToDouble(value.Last().StartDateContinuous), value.Last().PriceAvg03);
        }
      }
    }
    #endregion

    #endregion

    private GannAngleOffsetDraggablePoint _GannAngleOffsetPoint;
    public GannAngleOffsetDraggablePoint GannAngleOffsetPoint {
      get {
        if (_GannAngleOffsetPoint == null) {
          _GannAngleOffsetPoint = new GannAngleOffsetDraggablePoint(dateAxis.ConvertFromDouble, new NumberToStringAutoFormatConverter());
          _GannAngleOffsetPoint.PositionChanged += _GannAngleOffsetPoint_PositionChanged;
        }
        return _GannAngleOffsetPoint;
      }
    }

    void _GannAngleOffsetPoint_PositionChanged(object sender, PositionChangedEventArgs e) {
      var offset = GannAngleOffsetPoint.GetAngleByPosition(e.Position, animatedTimeX.ToArray(), ConvertToDateTime);
      //GannAngleOffsetPoint.ToolTip = string.Format("Tangent:{0}", offset);
      if (GannAngleOffsetPoint.IsMouseCaptured)
        DispatcherScheduler.Current.Schedule(() => OnGannAngleChanged(offset));
    }

    public event EventHandler<GannAngleOffsetChangedEventArgs> GannAngleOffsetChanged;
    private void OnGannAngleChanged(double offset) {
      if (GannAngleOffsetChanged != null)
        GannAngleOffsetChanged(this, new GannAngleOffsetChangedEventArgs(offset));
    }

    DraggablePoint _CorridorStopPointX;
    DraggablePoint CorridorStopPointX {
      get {
        if (_CorridorStopPointX == null) {
          _CorridorStopPointX = new DraggablePoint();

          _CorridorStopPointX.PositionChanged += CorridorStopPointX_PositionChanged;
          _CorridorStopPointX.IsMouseCapturedChanged += CorridorStopPointX_IsMouseCapturedChanged;

          corridorStartDateScheduler = new Schedulers.ThreadScheduler(OnCorridorStartPositionChanged, (s, e) => { });
        }
        return _CorridorStopPointX;
      }
    }
    void _CorridorStopPointX_GotFocus(object sender, RoutedEventArgs e) {
      ActiveDraggablePoint = (DraggablePoint)sender;
    }
    void _CorridorStopPointX_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      DraggablePoint_MouseLeftButtonDown(sender, e);
    }


    DraggablePoint _CorridorStartPointX;
    DraggablePoint CorridorStartPointX {
      get {
        if (_CorridorStartPointX == null) {
          _CorridorStartPointX = new DraggablePoint();

          _CorridorStartPointX.PositionChanged += CorridorStartPointX_PositionChanged;
          _CorridorStartPointX.IsMouseCapturedChanged += CorridorStartPointX_IsMouseCapturedChanged;
          //_CorridorStartPointX.SetBinding(DraggablePoint.VisibilityProperty, ShowDragBindingFactory());
          _CorridorStartPointX.Visibility = System.Windows.Visibility.Visible;

          //_CorridorStartPointX.MouseLeftButtonDown += new MouseButtonEventHandler(DraggablePoint_MouseLeftButtonDown);
          //_CorridorStartPointX.PreviewMouseLeftButtonUp += new MouseButtonEventHandler(_CorridorStartPointX_PreviewMouseLeftButtonDown);
          //_CorridorStartPointX.PreviewMouseUp += new MouseButtonEventHandler(_CorridorStartPointX_PreviewMouseUp);
          //_CorridorStartPointX.GotFocus += new RoutedEventHandler(_CorridorStartPointX_GotFocus);
          //_CorridorStartPointX.KeyDown += new KeyEventHandler(DraggablePoint_KeyDown);

          corridorStartDateScheduler = new Schedulers.ThreadScheduler(OnCorridorStartPositionChanged, (s, e) => { });
        }
        return _CorridorStartPointX;
      }
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

    DraggablePoint _ActiveDraggablePoint;

    public DraggablePoint ActiveDraggablePoint {
      get { return _ActiveDraggablePoint; }
      set { _ActiveDraggablePoint = value; }
    }

    ReactiveList<HorizontalLine> levelLines = new ReactiveList<HorizontalLine>();
    DispatcherScheduler _plotterScheduler = null;
    public DispatcherScheduler PlotterScheduler { get { return _plotterScheduler ?? (_plotterScheduler = new DispatcherScheduler(plotter.Dispatcher)); } }

    public void DrawLevels(IList<double> levelsToAdd) {
      PlotterScheduler.Schedule(() => {
        levelLines.ToArray().Skip(levelsToAdd.Count).ToList().ForEach(line => {
          levelLines.Remove(line);
          plotter.Children.Remove(line);
        });
        levelLines.Zip(levelsToAdd, (line, level) => new { line, level })
          .ToList().ForEach(a => a.line.Value = a.level);
        levelsToAdd.ToArray().Skip(levelLines.Count).ToList().ForEach(level => {
          var line = CenterOfMassFactory(level);
          levelLines.Add(line);
          plotter.Children.Add(line);
        });
      });
    }

    #region Reactive Lines
    void InitVLines<TLine>(ReactiveList<DateTime> times, ReactiveList<TLine> lines, Color color, Func<DateTime, string> tooltip, double[] strokeArray = null, double strokeThickness = double.NaN,double opacity=double.NaN) where TLine : SimpleLine, new() {
      times.ItemsAdded.ObserveOnDispatcher().Subscribe(dt => 
        OnOtherTimeAdded(dt, lines, color, strokeArray ?? new[] { 2.0, 3, 4, 3 }, strokeThickness.IfNaN(2), tooltip,opacity));
      times.ItemsRemoved.ObserveOnDispatcher().Subscribe(dt => OnOtherTimeRemoved(dt, lines));
      lines.ItemsRemoved.ObserveOnDispatcher().Subscribe(item => plotter.Children.Remove(item));
      lines.ItemsAdded.ObserveOnDispatcher().Subscribe(item => plotter.Children.Add(item));
    }
    void OnOtherTimeAdded<TLine>(DateTime date,ReactiveList<TLine> otherVLines,Color color,double[] strokeArray,double strokeThickness , Func<DateTime,string> tooltip,double opacity = double.NaN)where TLine:SimpleLine,new() {
      try {
        var vl = new TLine() {
          Value = dateAxis.ConvertToDouble(GetPriceStartDateContinuous(date)), StrokeDashArray = new DoubleCollection(strokeArray),
          Stroke = new SolidColorBrush(color),
          StrokeThickness = 2,
          ToolTip = tooltip(date),
          Opacity = opacity.IfNaN(strokeThickness > 1 ? .75 : 1)
        };
        //vl.SetBinding(SimpleLine.StrokeThicknessProperty, new Binding("IsMouseOver") {
        //  Source = vl,
        //  Converter = BoolToSrtingConverter.Default,
        //  ConverterParameter = "{0}|{0}|4".Formater(strokeThickness),
        //  Delay = 300
        //});
        SetTime(vl, date);
        otherVLines.Add(vl);
      } catch (Exception exc) {
        LogMessage.Send(exc);
      }
    }
    static void OnOtherTimeRemoved<TLine>(DateTime date, ReactiveList<TLine> otherVLines) where TLine : SimpleLine, new() {
      try {
        var vl = otherVLines.SingleOrDefault(l => GetTime(l) == date);
        if (vl != null) otherVLines.Remove(vl);
      } catch (Exception exc) {
        LogMessage.Send(exc);
      }
    }
    void DrawVertivalLines(IEnumerable<DateTime> times, ReactiveList<DateTime> otherTimes, ReactiveList<VerticalLine> timesVLines) {
      times = times.Distinct().Where(IsDateInChartRange).ToArray();
      otherTimes.RemoveAll(ot => !times.Contains(ot));
      otherTimes.AddRange(times.Except(otherTimes).Take(4));
      otherTimes.ToList().ForEach(time => {
        var line = timesVLines.SingleOrDefault(l => GetTime(l) == time);
        if (line != null) {
          var value = ConvertStartDateToContiniousDouble(time);
          if (line.Value != value) line.Value = value;
        }
      });
    }
    private bool IsDateInChartRange(DateTime date) {
      if (animatedTime0X == null || !animatedTime0X.Any()) return false;
      var dateStart = animatedTime0X[0];
      var dateEnd = animatedTime0X.Last();
      return date.Between(dateStart, dateEnd);
    }
    #endregion

    #region NewsTimes
    ReactiveList<DateTime> _NewsTimes;
    public ReactiveList<DateTime> NewsTimes {
      get {
        if (_NewsTimes == null) {
          _NewsTimes = new ReactiveList<DateTime>();
          InitVLines(NewsTimes, NewsTimesVLines, Colors.MediumPurple, d => "NewsTimes @ {0:g}".Formater(d), new[] { 2.0, 6, 2, 6 }, 2);
        }
        return _NewsTimes;
      }
    }
    ReactiveList<VerticalLine> NewsTimesVLines = new ReactiveList<VerticalLine>();
    public void DrawNewsTimes(IList<DateTime> times) { DrawVertivalLines(times, NewsTimes, NewsTimesVLines); }
    #endregion

    #region Trade Times
    ReactiveList<DateTime> _TradeTimes;
    public ReactiveList<DateTime> TradeTimes {
      get {
        if (_TradeTimes == null) {
          _TradeTimes = new ReactiveList<DateTime>();
          InitVLines(TradeTimes, TradeTimesVLines, Colors.Green, d => "Trade @ {0:g}".Formater(d));
        }
        return _TradeTimes;
      }
    }
    ReactiveList<VerticalLine> TradeTimesVLines = new ReactiveList<VerticalLine>();
    public void DrawTradeTimes(IEnumerable<DateTime> times) { DrawVertivalLines(times, TradeTimes, TradeTimesVLines); }
    #endregion
  
    #region NYTimes
    ReactiveList<VerticalRange> _NYSessions;
    public ReactiveList<VerticalRange> NYSessions {
      get {
        if (_NYSessions == null) {
          _NYSessions = new ReactiveList<VerticalRange>();
          _NYSessions.ItemsAdded.ObserveOnDispatcher().Subscribe(vr => {
            InitSessionVerticalRange(vr, Colors.RoyalBlue,0.075);
          });
          _NYSessions.ItemsRemoved.ObserveOnDispatcher().Subscribe(vr => plotter.Children.Remove(vr));
        }
        return _NYSessions;
      }
    }
    ReactiveList<DateTime> _NYTimes;
    public ReactiveList<DateTime> NYTimes {
      get {
        if (_NYTimes == null) {
          _NYTimes = new ReactiveList<DateTime>();
          InitVLines(NYTimes, NYTimesVLines, Colors.RoyalBlue, d => "NY Forex @ {0:g}".Formater(d), null, double.NaN, 0.075);
        }
        return _NYTimes;
      }
    }
    ReactiveList<VerticalLine> NYTimesVLines = new ReactiveList<VerticalLine>();
    public void DrawNYTimes(IList<DateTime> times) {
      DrawVertivalLines(times, NYTimes, NYTimesVLines);
      SetVerticalRanges(times, NYSessions);
    }
    #endregion

    #region LindonTimes
    ReactiveList<VerticalRange> _londonSessions;
    public ReactiveList<VerticalRange> LondonSessions {
      get {
        if (_londonSessions == null) {
          _londonSessions = new ReactiveList<VerticalRange>();
          _londonSessions.ItemsAdded.ObserveOnDispatcher().Subscribe(vr => {
            InitSessionVerticalRange(vr, Colors.MediumVioletRed, 0.04);
          });
          _londonSessions.ItemsRemoved.ObserveOnDispatcher().Subscribe(vr => plotter.Children.Remove(vr));
        }
        return _londonSessions;
      }
    }
    ReactiveList<DateTime> _LondonTimes;
    public ReactiveList<DateTime> LondonTimes {
      get {
        if (_LondonTimes == null) {
          _LondonTimes = new ReactiveList<DateTime>();
          InitVLines(LondonTimes, LondonTimesVLines, Colors.MediumVioletRed, d => "London Forex @ {0:g}".Formater(d),null,double.NaN,0.04);
        }
        return _LondonTimes;
      }
    }
    ReactiveList<VerticalLine> LondonTimesVLines = new ReactiveList<VerticalLine>();
    public void DrawLindonTimes(IList<DateTime> times) {
      DrawVertivalLines(times, LondonTimes, LondonTimesVLines);
      SetVerticalRanges(times, LondonSessions);
    }

    #endregion
    #region TokyoTimes
    ReactiveList<VerticalRange> _tokyoSessions;
    public ReactiveList<VerticalRange> tokyoSessions {
      get {
        if (_tokyoSessions == null) {
          _tokyoSessions = new ReactiveList<VerticalRange>();
          _tokyoSessions.ItemsAdded.ObserveOnDispatcher().Subscribe(vr => {
            InitSessionVerticalRange(vr, Colors.DarkGoldenrod, 0.0625);
          });
          _tokyoSessions.ItemsRemoved.ObserveOnDispatcher().Subscribe(vr => plotter.Children.Remove(vr));
        }
        return _tokyoSessions;
      }
    }

    ReactiveList<DateTime> _TokyoTimes;
    public ReactiveList<DateTime> TokyoTimes {
      get {
        if (_TokyoTimes == null) {
          _TokyoTimes = new ReactiveList<DateTime>();
          InitVLines(TokyoTimes, TokyoTimesVLines, Colors.DarkGoldenrod, d => "TokyoTimes @ {0:g}".Formater(d), null, double.NaN, 0.075);
        }
        return _TokyoTimes;
      }
    }
    ReactiveList<VerticalLine> TokyoTimesVLines = new ReactiveList<VerticalLine>();
    public void DrawTokyoTimes(IList<DateTime> times) {
      DrawVertivalLines(times, TokyoTimes, TokyoTimesVLines);
      SetVerticalRanges(times, tokyoSessions);
    }
    #endregion  
    private void SetVerticalRanges(IList<DateTime> times, ReactiveList<VerticalRange> vRanges) {
      var timePairs = times.Clump(2);
      while (vRanges.Count < timePairs.Count())
        vRanges.Add(new VerticalRange());
      while (vRanges.Count > timePairs.Count())
        vRanges.RemoveAt(vRanges.Count - 1);
      timePairs.ForEach((timePair, i) =>
        SetVerticalRange(vRanges[i], timePair.TakeLast(2).First(), timePair.Last()));
    }
    private void InitSessionVerticalRange(VerticalRange vr, Color fillColor,double opacity) {
      vr.Fill = new SolidColorBrush(fillColor);
      vr.StrokeThickness = 0;
      vr.Opacity = opacity;
      //vr.ObservableForProperty(v => v.Opacity).Subscribe(oc => { Debugger.Break(); });
      plotter.Children.Add(vr);
    }
    #endregion

    #region Window Events
    void Corridors_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      e.Cancel = true;
      Application.Current.Dispatcher.BeginInvoke(
        DispatcherPriority.Background,
        (DispatcherOperationCallback)delegate(object o) {
        //Hide();
        return null;
      },
          null);

    }
    #endregion

    #region DataSources
    EnumerableDataSource<Point> ds = null;
    EnumerableDataSource<Volt> dsVoltsPoly = null;
    #endregion


    class DraggablePointInfo {
      public object DataContext { get; set; }
      public DraggablePoint DraggablePoint { get; set; }
      public IDisposable MouseClickObserver { get; set; }
      //public ObservableValue<double> TradesCount { get; set; }
      public DraggablePointInfo(DraggablePoint dp,object dataContext) {
        this.DraggablePoint = dp;
        this.DataContext = dataContext;
      }
      ~DraggablePointInfo() {
        if (MouseClickObserver != null) MouseClickObserver.Dispose();
      }
    }

    Dictionary<Guid, DraggablePointInfo> BuyRates = new Dictionary<Guid, DraggablePointInfo>();
    Dictionary<Guid, DraggablePointInfo> SellRates = new Dictionary<Guid, DraggablePointInfo>();

    public class BuySellLevel {
      public object DataContext { get; set; }
      public double Rate { get; set; }
      public bool IsBuy { get; set; }
      public BuySellLevel(object dataContext, double rate, bool isBuy) {
        this.DataContext = dataContext;
        this.Rate = rate;
        this.IsBuy = isBuy;
      }
    }

    static double[] StrokeArrayForTrades = new double[] { 5, 2, 2, 2 };
    Dictionary<string, HorizontalLine> tradeLines = new Dictionary<string, HorizontalLine>();
    public void SetTradeLines(ICollection<Trade> trades) {
      var a = new Action(() => {
        var tradesAdd = from value in trades.Select(t => t.Id).Except(this.tradeLines.Select(t => t.Key))
                        join trade in trades on value equals trade.Id
                        select trade;
        foreach (var t in tradesAdd) {
          var y = t.Open;// +(t.Buy ? +1 : -1) * spread;
          var toolTip = t.Open + " @ " + t.Time;
          var stroke = new SolidColorBrush(t.Buy ? priceLineGraphColorBuy : priceLineGraphColorSell);
          HorizontalLine hl = null;
          var tl1 = this.tradeLines.FirstOrDefault(tl => tl.Value.Visibility == System.Windows.Visibility.Hidden);
          if (tl1.Value != null) {
            this.tradeLines.Remove(tl1.Key);
            hl = tl1.Value;
            hl.Visibility = System.Windows.Visibility.Visible;
          } else {
            hl = new HorizontalLine(y) { StrokeDashArray = new DoubleCollection(StrokeArrayForTrades), StrokeThickness = 1 };
            plotter.Children.Add(hl);
          }
          hl.Value = y;
          hl.Stroke = stroke;
          hl.ToolTip = toolTip;
          this.tradeLines.Add(t.Id, hl);
        }
        var tradesDelete = this.tradeLines.Select(t => t.Key).Except(trades.Select(t => t.Id)).ToArray();
        foreach (var t in tradesDelete) {
          tradeLines[t].Visibility = System.Windows.Visibility.Hidden;
        }
        lineNetBuy.Visibility = trades.IsBuy(true).Length > 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
        lineNetSell.Visibility = trades.IsBuy(false).Length > 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
      });
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(a);
    }

    public void SetBuyRates(Dictionary<Guid, BuySellLevel> rates) {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
        CleanSuppResRates(BuyRates, rates);
        SetBuySellRates(rates);
      });
    }
    public void SetSellRates(Dictionary<Guid, BuySellLevel> rates) {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
        CleanSuppResRates(SellRates, rates);
        SetBuySellRates(rates);
      });
    }

    private void CleanSuppResRates(Dictionary<Guid, DraggablePointInfo> dpRates, Dictionary<Guid, BuySellLevel> rates) {
      foreach (var guid in dpRates.Keys.Except(rates.Keys).ToArray()) {
        var rate = dpRates[guid];
        var dp = rate.DraggablePoint;
        var line = GetFriend(dp);
        SetFriend(dp, null);
        plotter.Children.Remove(dp);
        plotter.Children.Remove(line);
        dpRates.Remove(guid);
      }
    }

    bool _isShiftDown;
    bool _dragPointPositionChanged;
    void SetBuySellRates(Dictionary<Guid, BuySellLevel> suppReses) {
      foreach (var suppRes in suppReses) {
        var isBuy = suppRes.Value.IsBuy;
        var uid = suppRes.Key;
        var rate = suppRes.Value.Rate;
        SetTradingRange(suppRes.Value.DataContext, rate);
        
        Dictionary<Guid, DraggablePointInfo> rates = isBuy ? BuyRates : SellRates;
        if (!rates.ContainsKey(uid)) {
          string anchorTemplateName = "DraggArrow" + (isBuy ? "Up" : "Down");
          var dragPoint = new TemplateableDraggablePoint() { MarkerTemplate = FindResource(anchorTemplateName) as ControlTemplate };

          Brush brush = new SolidColorBrush(isBuy ? Colors.DarkRed : Colors.Navy);
          var line = new HorizontalLine() { Stroke = brush, StrokeDashArray = { 2 } };

          if (line == null) {
            var mdeb = new MouseDragElementBehavior();
            mdeb.Attach(line);
            mdeb.DragBegun += (s, e) => { e.Handled = true; };
            mdeb.DragFinished += (s, e) => {
              Point mouseInData = e.GetPosition(plotter.ViewportPanel).ScreenToData(plotter.Viewport.Transform);
              if (mouseInData != null)
                DispatcherScheduler.Current.Schedule(() => dragPoint.Position = new Point(dragPoint.Position.X, mouseInData.Y));
            };
          }

          line.SetBinding(HorizontalLine.OpacityProperty, new Binding("IsGhost") {
            Source = suppRes.Value.DataContext,
            Converter = BoolToSrtingConverter.Default,
            ConverterParameter = "1|1|0.3"
          });
          SetFriend(dragPoint, line);
          plotter.Children.Add(line);
          plotter.Children.Add(dragPoint);
          //dragPoint.SetBinding(DraggablePoint.PositionProperty, new Binding("Value") { Source = ov });
          dragPoint.PositionChanged += (s, e) => {
            var position = e.Position;
            if (double.IsNaN(e.Position.Y)) {
              var y = ((SimpleLine)GetFriend(s as DraggablePoint)).Value;
              position = new Point(e.Position.X, y);
            }
            OnSupportResistanceChanged(s as DraggablePoint, uid, e.PreviousPosition, position);
            if (!dragPoint.IsMouseOver) return;
            _dragPointPositionChanged = true;
          };
          ////dragPoint.ToolTip = "UID:" + uid;
          //plotter.PreviewMouseLeftButtonDown += (s, e) => {
          //  if (!dragPoint.IsMouseOver) return;
          //  _dragPointPositionChanged = false;
          //  Action a = () => {
          //    if (!_dragPointPositionChanged)
          //      TriggerCanTrade(dragPoint);
          //  };
          //  DispatcherScheduler.Current.Schedule(a);
          //};
          plotter.PreviewKeyDown += (s, e) => {
            var numericKeys = new[] { Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.NumPad0, Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4, Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9 };
            _isShiftDown = e.Key == Key.LeftShift || e.Key == Key.RightShift;
            if (!dragPoint.IsMouseOver) return;
            e.Handled = true;
            switch (e.Key) {
              case Key.Add:
                OnBuySellAdded(isBuy, dragPoint.Position.Y);
                break;
              case Key.Delete:
                OnBuySellRemoved(uid);
                plotter.Children.Remove(GetFriend(dragPoint));
                plotter.Children.Remove(dragPoint);
                rates.Remove(uid);
                break;
              case Key.S:
                dragPoint.DataContext.Invoke("OnScan", null);
                break;
              case Key.T:
                dragPoint.DataContext.SetProperty("CanTrade", !dragPoint.DataContext.GetProperty<bool>("CanTrade"));
                break;
              case Key.M:
                dragPoint.DataContext.SetProperty("InManual", !dragPoint.DataContext.GetProperty<bool>("InManual"));
                break;
              case Key.Subtract:
                dragPoint.DataContext.SetProperty("TradesCount", -dragPoint.DataContext.GetProperty<double>("TradesCount"));
                break;
              default:
                if (numericKeys.Contains(e.Key)) {
                  var i = int.Parse(new KeyConverter().ConvertToString(e.Key).Replace("NumPad", ""));
                  dragPoint.DataContext.SetProperty("TradesCount", i);
                }
                break;
            }
          };
          line.SetAnchor(dragPoint);
          var dpi = new DraggablePointInfo(dragPoint, suppRes.Value.DataContext) {
            MouseClickObserver = plotter.SubscribeToPlotterPreviewMouseLeftClick(me => dragPoint.IsMouseOver, () => TriggerCanTrade(dragPoint))
          };
          rates.Add(uid, dpi);
          dragPoint.DataContext = dpi.DataContext;
        }
        var dp = rates[uid].DraggablePoint;
        dp.Dispatcher.BeginInvoke(new Action(() => {
          var raiseChanged = rate.IfNaN(0) == 0;
          if (raiseChanged)
            try {
              rate = animatedPriceY.Average();
            } catch {
              rate = animatedPriceY.Average();
            }
          dp.Position = CreatePointY(rate);
        }));
      }
    }


    private static void TriggerCanTrade(TemplateableDraggablePoint dragPoint) {
      dragPoint.DataContext.SetProperty("CanTrade", !dragPoint.DataContext.GetProperty<bool>("CanTrade"));
    }

    void plotter_MouseDown(object sender, MouseButtonEventArgs e) {
      throw new NotImplementedException();
    }

    private Point CreatePointY(double y) { return new Point(dateAxis.ConvertToDouble(animatedTimeX[0]), y); }
    double slope(Point p1, Point p2) { return (p2.Y - p1.Y) / (p2.X - p1.X); }
    double y2(double slope, Point p1, double x2) { return slope * (x2 - p1.X) + p1.Y; }
    List<HorizontalLine> FibLevels = new List<HorizontalLine>();
    List<ColoredSegment> GannAngles = new List<ColoredSegment>();
    Dictionary<SimpleLine, DraggablePoint> LineToPoint = new Dictionary<SimpleLine, DraggablePoint>();
    DraggablePoint _tradeLineStartDraggablePoint = new DraggablePoint();
    DraggablePoint _tradeLineStopDraggablePoint = new DraggablePoint();
    private void ResetTradeLinePosition() {
      _tradeLineStartDraggablePoint.SetBinding(DraggablePoint.PositionProperty, new Binding("TradeLineStartPosition"));
      _tradeLineStopDraggablePoint.SetBinding(DraggablePoint.PositionProperty, new Binding("TradeLineStopPosition"));
    }
    public double TradeLineSlope { get { return slope(_tradeLineStartDraggablePoint.Position, _tradeLineStopDraggablePoint.Position); } }
    public Point TradeLineEndPoint {
      get {
        var x = trendLine.StartPoint.X;
        var y = y2(TradeLineSlope, _tradeLineStartDraggablePoint.Position, x);
        return new Point(x, y);
      }
    }
    Point _tradeLineStartPosition;
    public Point TradeLineStartPosition {
      get { return _tradeLineStartPosition; }
      set {
        _tradeLineStartPosition = value;
        OnPropertyChanged("TradeLineStartPosition");
      }
    }
    Point _tradeLineStopPosition;
    public Point TradeLineStopPosition {
      get { return _tradeLineStopPosition; }
      set {
        _tradeLineStopPosition = value;
        OnPropertyChanged("TradeLineStopPosition");
      }
    }
    RectangleHighlight _shortWaveVerticalRange = null;
    HorizontalRange _tradingHorisontalRange = null;
    VerticalRange _londonSessionHorisontalRange = null;

    // Minor ticks
    DifferenceIn? GetDifference(TimeSpan span) {
      span = span.Duration();

      DifferenceIn? diff;
      if (span.Days > 365)
        diff = DifferenceIn.Year;
      else if (span.Days > 30)
        diff = DifferenceIn.Month;
      else if (span.Days > 4)
        diff = DifferenceIn.Day;
      else if (span.Hours > 0)
        diff = DifferenceIn.Hour;
      else if (span.Minutes > 0)
        diff = DifferenceIn.Minute;
      else if (span.Seconds > 0)
        diff = DifferenceIn.Second;
      else
        diff = DifferenceIn.Millisecond;

      return diff;
    }

    /*
Never mind i created CustomGenericLocationalTicksProvider and it worked like a charm... Thanks though.

 

   public ITicksInfo<TAxis> GetTicks(Range<TAxis> range, int ticksCount)
        {
            EnsureSearcher();

            //minResult = searcher.SearchBetween(range.Min, minResult);
            //maxResult = searcher.SearchBetween(range.Max, maxResult);

            minResult = searcher.SearchFirstLess(range.Min);
            maxResult = searcher.SearchGreater(range.Max);

            Double minRange = range.Min.ToDouble();
            Double maxRange = range.Max.ToDouble();
            Double minStep = (maxRange - minRange)*.10;

            if (!(minResult.IsEmpty && maxResult.IsEmpty))
            {
                int startIndex = !minResult.IsEmpty ? minResult.Index : 0;
                int endIndex = !maxResult.IsEmpty ? maxResult.Index : collection.Count - 1;

                int count = endIndex - startIndex + 1;

                TAxis[] ticks = new TAxis[count];
                double lastVal = 0;
                for (int i = startIndex; i <= endIndex; i++)
                {
                    var val = axisMapping(collection[i]);
                    if(val.ToDouble() - lastVal > minStep)
                   {
                        ticks[i - startIndex] = val;
                       lastVal = val.ToDouble();

                    }
                }
     */
    private void CreateCurrencyDataSource(bool doVolts) {
      if (IsPlotterInitialised) return;

      CursorCoordinateGraph ccg = new CursorCoordinateGraph() { ShowVerticalLine = true };
      ccg.XTextMapping = x => GetPriceStartDate(dateAxis.ConvertFromDouble(x)).ToString("ddd dd HH:mm");
      ccg.YTextMapping = x => x.Round(_roundTo) + "";
      plotter.Children.Add(ccg);

      dateAxis.MayorLabelProvider = null;
      var ticksProvider = ((Microsoft.Research.DynamicDataDisplay.Charts.TimeTicksProviderBase<System.DateTime>)(dateAxis.TicksProvider));
      ticksProvider.Strategy = new Microsoft.Research.DynamicDataDisplay.Charts.Axes.DateTime.Strategies.DelegateDateTimeStrategy(GetDifference);
      dateAxis.LabelProvider.SetCustomFormatter(info => {
        DifferenceIn differenceIn = (DifferenceIn)info.Info;
        if (differenceIn == DifferenceIn.Hour) {
            return info.Tick.ToString("H:");
        }
        return null;
      });
      dateAxis.LabelProvider.SetCustomView((li, uiElement) => {
        FrameworkElement element = (FrameworkElement)uiElement;
        element.LayoutTransform = new RotateTransform(-90, 0, 0);
      });
      var a = FindName("PART_AdditionalLabelsCanvas");

      plotter.KeyUp += (s, e) => {
        if (e.Key == Key.RightShift || e.Key == Key.LeftShift)
          _isShiftDown = false;
      };
      IsPlotterInitialised = true;
      plotter.Children.RemoveAt(0);
      var verticalAxis = plotter.Children.OfType<VerticalAxis>().First();
      verticalAxis.FontSize = 10;
      //verticalAxis.FontWeight = FontWeights.Black;
      verticalAxis.ShowMinorTicks = false;

      #region Add Main Graph
      {

        EnumerableDataSource<DateTime> xSrc = new EnumerableDataSource<DateTime>(animatedTimeX);

        animatedDataSource1 = new EnumerableDataSource<double>(animatedPrice1Y);
        animatedDataSource1.SetYMapping(y => y);
        plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource1), Colors.DarkGray, 1, "")
          .Description.LegendItem.Visibility = Visibility.Collapsed;

        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedDataSource = new EnumerableDataSource<double>(animatedPriceY);
        animatedDataSource.SetYMapping(y => y);
        this.PriceLineGraph = plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource), priceLineGraphColorAsk, 1, "");
        this.PriceLineGraph.Description.LegendItem.Visibility = System.Windows.Visibility.Collapsed;
        
        if (true) {
          animatedDataSourceBid = new EnumerableDataSource<double>(animatedPriceBidY);
          animatedDataSourceBid.SetYMapping(y => y);
          this.PriceLineGraphBid = plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSourceBid), priceLineGraphColorBid, 1, "");
          this.PriceLineGraphBid.Description.LegendItem.Visibility = Visibility.Collapsed;
        }

        Border infoBorder = new Border() {
          BorderBrush = new SolidColorBrush(Colors.Maroon), BorderThickness = new Thickness(1)
          ,
          CornerRadius = new CornerRadius(3), Visibility = Visibility.Hidden
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
        _voltGraph = innerPlotter.AddLineGraph(new CompositeDataSource(xSrc, animatedVoltDataSource), Colors.Tan, 1, "");
        _voltGraph.Description.LegendItem.Visibility = Visibility.Collapsed;

        xSrc = new EnumerableDataSource<DateTime>(animatedVolt1TimeX);
        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedVolt1DataSource = new EnumerableDataSource<double>(animatedVolt1ValueY);
        animatedVolt1DataSource.SetYMapping(y => y);
        var lg = innerPlotter.AddLineGraph(new CompositeDataSource(xSrc, animatedVolt1DataSource), Colors.LimeGreen, 1, "");
        lg.Description.LegendItem.Visibility = Visibility.Collapsed;
        lg.Opacity = .25;
        //innerPlotter.Children.Remove(plotter.Children.OfType<HorizontalAxis>().Single());
        verticalAxis.Placement = AxisPlacement.Right;
        var innerVA = innerPlotter.Children.OfType<VerticalAxis>().First();
        innerVA.Placement = AxisPlacement.Left;
        innerVA.ShowMinorTicks = false;
      } else {
        innerPlotter.Children.Remove(innerPlotter.Children.OfType<VerticalAxis>().Single());
        plotter.Children.OfType<VerticalAxis>().First().Placement = AxisPlacement.Right;
      }
      #endregion

      #region Add Lines

      _tradingHorisontalRange = new HorizontalRange() { Fill = new SolidColorBrush(Colors.LightBlue), StrokeThickness = 0 };
      _londonSessionHorisontalRange = new VerticalRange() { Fill = new SolidColorBrush(Colors.MediumVioletRed), StrokeThickness = 0, Opacity = .04 };

      plotter.Children.Add(_tradingHorisontalRange);
      plotter.Children.Add(_londonSessionHorisontalRange);

      _shortWaveVerticalRange = new RectangleHighlight() { Fill = new SolidColorBrush(Colors.LightBlue), StrokeThickness = 1, Opacity = _tradingHorisontalRange.Opacity };
      plotter.Children.Add(_shortWaveVerticalRange);

      plotter.Children.Add(lineNetSell);
      plotter.Children.Add(lineNetBuy);

      plotter.Children.Add(lineAvgAsk);
      plotter.Children.Add(lineAvgBid);
      plotter.Children.Add(lineTimeTakeProfit);
      plotter.Children.Add(lineTimeTakeProfit1);
      plotter.Children.Add(lineTimeTakeProfit2);
      plotter.Children.Add(lineTimeTakeProfit3);
      plotter.Children.Add(trendLine);
      plotter.Children.Add(trendLine21);
      plotter.Children.Add(trendLine2);
      plotter.Children.Add(trendLine02);
      plotter.Children.Add(trendLine31);
      plotter.Children.Add(trendLine3);
      plotter.Children.Add(trendLine03);
      plotter.Children.Add(gannLine);

      plotter.Children.Add(centerOfMassHLineHigh);
      plotter.Children.Add(centerOfMassHLineLow);

      plotter.Children.Add(lineTimeMin);

      plotter.Children.Add(lineTimeMax);
      plotter.Children.Add(CorridorStartPointX);
      LineToPoint.Add(lineTimeMax, CorridorStartPointX);
      
      plotter.Children.Add(lineTimeAvg);
      plotter.Children.Add(CorridorStopPointX);
      LineToPoint.Add(lineTimeAvg, CorridorStopPointX);

      plotter.Children.Add(GannAngleOffsetPoint);

      InsertFibLines();

      plotter.KeyDown += new KeyEventHandler(plotter_KeyDown);
      plotter.PreviewKeyDown += new KeyEventHandler(plotter_PreviewKeyDown);
      plotter.MouseDoubleClick += (s, e) => RaisePlotterKeyDown(Key.A);

      #endregion

      EventHandler<PositionChangedEventArgs> eh = (s, e) => {
        try {
          RaiseTrendLineDraw();
          var me = s == _tradeLineStopDraggablePoint ? _tradeLineStopDraggablePoint : _tradeLineStartDraggablePoint;
          var other = s != _tradeLineStopDraggablePoint ? _tradeLineStopDraggablePoint : _tradeLineStartDraggablePoint;
          if (!me.IsMouseCaptured) return;
          if (_isShiftDown)
            other.Position = new Point(other.Position.X + e.Position.X - e.PreviousPosition.X, other.Position.Y + e.Position.Y - e.PreviousPosition.Y);
          RaiseShowChart();
        } catch (Exception exc) {
          Debugger.Break();
        }
      };

      #region Trade Line
      var showTradeStuffBinding = new Binding("DoShowTradingLine") { Converter = new BooleanToVisibilityConverter() };
      plotter.Children.Add(_tradeLineStartDraggablePoint);
      _tradeLineStartDraggablePoint.PositionChanged += eh;
      _tradeLineStartDraggablePoint.SetBinding(DraggablePoint.VisibilityProperty, showTradeStuffBinding);

      plotter.Children.Add(_tradeLineStopDraggablePoint);
      _tradeLineStopDraggablePoint.PositionChanged += eh;
      _tradeLineStopDraggablePoint.SetBinding(DraggablePoint.VisibilityProperty, showTradeStuffBinding);

      ResetTradeLinePosition();

      var tradeSegment = new SegmentEx() { StrokeThickness = 1 };
      plotter.Children.Add(tradeSegment);
      tradeSegment.SetBinding(Segment.StartPointProperty, new Binding("Position") { Source = _tradeLineStopDraggablePoint });
      tradeSegment.SetBinding(Segment.EndPointProperty, new Binding(Lib.GetLambda(() => TradeLineEndPoint)));
      tradeSegment.SetBinding(Segment.ToolTipProperty, new Binding(Lib.GetLambda(() => TradeLineSlope)));
      tradeSegment.EndPositionChanged += (s, e) => { RaiseTradeLineChanged(e.NewPosition, e.OldPosition); };
      tradeSegment.SetBinding(Segment.VisibilityProperty, showTradeStuffBinding);
      #endregion
    }

    #region DoShowTradingLine
    private bool _DoShowTradingLine;
    public bool DoShowTradingLine {
      get { return _DoShowTradingLine; }
      set {
        if (_DoShowTradingLine != value) {
          _DoShowTradingLine = value;
          OnPropertyChanged("DoShowTradingLine");
        }
      }
    }
    
    #endregion
    class SegmentEx : Segment {
      protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
        base.OnPropertyChanged(e);
        if (e.Property == Segment.EndPointProperty) {
          RaiseEndPositionChanged(((Point)e.NewValue).Y, ((Point)e.OldValue).Y);
        }
      }
      public event EventHandler<PositionChangedBaseEventArgs<double>> EndPositionChanged;
      void RaiseEndPositionChanged(double xNow, double xPrevious) {
        if (EndPositionChanged != null) EndPositionChanged(this, new PositionChangedBaseEventArgs<double>(xNow, xPrevious));
      }
    }
    private void RaiseTrendLineDraw() {
      OnPropertyChanged("TradeLineSlope");
      OnPropertyChanged("TradeLineEndPoint");
    }

    #region TradeLineChanged Event
    event EventHandler<PositionChangedBaseEventArgs<double>> TradeLineChangedEvent;
    public event EventHandler<PositionChangedBaseEventArgs<double>> TradeLineChanged {
      add {
        if (TradeLineChangedEvent == null || !TradeLineChangedEvent.GetInvocationList().Contains(value))
          TradeLineChangedEvent += value;
      }
      remove {
        TradeLineChangedEvent -= value;
      }
    }
    protected void RaiseTradeLineChanged(double xNow,double xPrevious) {
      if (TradeLineChangedEvent != null) TradeLineChangedEvent(this, new PositionChangedBaseEventArgs<double>(xNow,xPrevious));
    }
    #endregion

    private int _gannAnglesCount;

    public int GannAnglesCount {
      get { return _gannAnglesCount; }
      set {
        if (_gannAnglesCount == value) return;
        _gannAnglesCount = value;
        GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(InsertGannLines);
      }
    }

    int _GannAngle1x1Index;
    public int GannAngle1x1Index {
      get { return _GannAngle1x1Index; }
      set {
        if (_GannAngle1x1Index == value) return;
        _GannAngle1x1Index = value;
        GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(InsertGannLines);
      }
    }

    private void InsertGannLines() {
      GannAngles.PopRange(GannAngles.Count).ToList().ForEach(ga => plotter.Children.Remove(ga));
      for (var i = 0; i < GannAnglesCount; i++) {
        var color = GannAngle1x1Index == i ? Colors.Black : Colors.DarkGray;
        var hl = new ColoredSegment() {
          Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeDashArray = { 2 }, SelectedColor = Colors.Maroon
        };
        GannAngles.Add(hl);
        plotter.Children.Add(hl);
      }
    }
    private bool _DoShowFibLines;
    public bool DoShowFibLines {
      get { return _DoShowFibLines; }
      set {
        if (_DoShowFibLines != value) {
          _DoShowFibLines = value;
          OnPropertyChanged(CharterControlMetadata.DoShowFibLines);
        }
      }
    }

    private void InsertFibLines() {
      foreach (var i in Enumerable.Range(0, Fibonacci.Levels(0, 0).Length)) {
        var hl = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.MidnightBlue), StrokeThickness = 1 };
        hl.SetBinding(HorizontalLine.VisibilityProperty, new Binding(CharterControlMetadata.DoShowFibLines) {
          Converter = AnyToVisibilityConverter.Default
        });
        FibLevels.Add(hl);
        plotter.Children.Add(hl);
      }
    }

    public double PipSize { get; set; }
    double GuessPipSize(double price) { return price < 10 ? 0.0001 : 0.01; }

    void plotter_PreviewKeyDown(object sender, KeyEventArgs e) {
    }

    private void AdjustDraggablePointByPip(DraggablePoint dp, KeyEventArgs e) {
      if (dp.IsMouseOver) {
        var pip = PipSize;
        var step = e.Key == Key.Down ? -pip : e.Key == Key.Up ? pip : 0;
        if (step != 0) {
          e.Handled = true;
          SetIsInteractive(dp, true);
          dp.Position = new Point(dp.Position.X, dp.Position.Y + step);
          SetIsInteractive(dp, false);
        }
      }
    }

    #region PlotterKeyDown Event
    public class PlotterKeyDownEventArgs : EventArgs {
      public Key Key { get; set; }
      public PlotterKeyDownEventArgs(Key key) {
        Key = key;
      }
    }
    event EventHandler<PlotterKeyDownEventArgs> PlotterKeyDownEvent;
    public event EventHandler<PlotterKeyDownEventArgs> PlotterKeyDown {
      add {
        if (PlotterKeyDownEvent == null || !PlotterKeyDownEvent.GetInvocationList().Contains(value))
          PlotterKeyDownEvent += value;
      }
      remove {
        PlotterKeyDownEvent -= value;
      }
    }
    protected void RaisePlotterKeyDown(Key key) {
      if (PlotterKeyDownEvent != null) PlotterKeyDownEvent(this,new PlotterKeyDownEventArgs(key));
    }
    #endregion


    void plotter_KeyDown(object sender, KeyEventArgs e) {
      if (!new[] { Key.Oem2, Key.OemComma, Key.OemPeriod, Key.P }.Contains(e.Key))
        e.Handled = true;
      try {
        switch (e.Key) {
          case Key.H:
            try { FitToView(); } catch { }
            break; 
          default:
            if(e.Key == Key.C)
              ResetTradeLinePosition();
            RaisePlotterKeyDown(e.Key); break;
        }
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }

    #region Event Handlers
    DateTime CorridorStopPositionOld;
    DateTime CorridorStartPositionOld;
    DateTime GetPriceStartDate(DateTime startDateContinuous) {
      var x = animatedTimeX.OrderBy(d => (d - startDateContinuous).Duration()).First();
      return animatedTime0X[animatedTimeX.IndexOf(x)];
    }
    DateTime GetPriceStartDateContinuous(DateTime startDate) {
      var x = animatedTime0X.OrderBy(d => (d - startDate).Duration()).First();
      return animatedTimeX[animatedTime0X.IndexOf(x)];
    }
    double ConvertStartDateToContiniousDouble(DateTime date){
      return dateAxis.ConvertToDouble(GetPriceStartDateContinuous(date)) ;
    }
    Schedulers.ThreadScheduler corridorStartDateScheduler;
    void CorridorStopPointX_IsMouseCapturedChanged(object sender, DependencyPropertyChangedEventArgs e) {
      if ((bool)e.NewValue) CorridorStopPositionOld = GetPriceStartDate(dateAxis.ConvertFromDouble(CorridorStopPointX.Position.X));
      else if (CorridorStopPositionChanged != null && !corridorStartDateScheduler.IsRunning) {
        corridorStartDateScheduler.Run();
      }
    }
    void CorridorStopPointX_PositionChanged(object sender, PositionChangedEventArgs e) {
      if (CorridorStopPositionChanged != null && (ActiveDraggablePoint == sender || CorridorStopPointX.IsMouseCaptured) && !corridorStartDateScheduler.IsRunning) {
        corridorStartDateScheduler.Command = () => {
          CorridorStopPositionChanged(this,
          new CorridorPositionChangedEventArgs(GetPriceStartDate(dateAxis.ConvertFromDouble(e.Position.X)), dateAxis.ConvertFromDouble(e.PreviousPosition.X)));
        };
        corridorStartDateScheduler.Run();
      }
    }


    void CorridorStartPointX_IsMouseCapturedChanged(object sender, DependencyPropertyChangedEventArgs e) {
      if ((bool)e.NewValue) CorridorStartPositionOld = GetPriceStartDate(dateAxis.ConvertFromDouble(CorridorStartPointX.Position.X));
      else if (CorridorStartPositionChanged != null && !corridorStartDateScheduler.IsRunning) {
        corridorStartDateScheduler.Run();
      }
    }

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
    public event EventHandler ShowChart;
    void RaiseShowChart() {
      if (ShowChart != null) ShowChart(this, EventArgs.Empty);
    }
    public event EventHandler<BuySellRateRemovedEventArgs> BuySellRemoved;
    protected void OnBuySellRemoved(Guid uid) {
      if (BuySellRemoved != null)
        BuySellRemoved(this, new BuySellRateRemovedEventArgs(uid));
    }
    public event EventHandler<BuySellRateAddedEventArgs> BuySellAdded;
    protected void OnBuySellAdded(bool isBuy, double rate) {
      if (BuySellAdded != null)
        BuySellAdded(this, new BuySellRateAddedEventArgs(isBuy, rate));
    }
    public event EventHandler<PlayEventArgs> Play;
    protected void OnPlay(bool play, DateTime startDate, double delayInSeconds) {
      if (Play != null) Play(this, new PlayEventArgs(play, startDate, delayInSeconds));
    }


    public event EventHandler<CorridorPositionChangedEventArgs> CorridorStopPositionChanged;
    private void OnCorridorStopPositionChanged() {
      var x = GetPriceStartDate(ConvertToDateTime(CorridorStopPointX.Position.X));
      CorridorStopPositionChanged(this, new CorridorPositionChangedEventArgs(x, CorridorStopPositionOld));
    }

    public event EventHandler<CorridorPositionChangedEventArgs> CorridorStartPositionChanged;
    private void OnCorridorStartPositionChanged() {
      var x = GetPriceStartDate(ConvertToDateTime(CorridorStartPointX.Position.X));
      CorridorStartPositionChanged(this, new CorridorPositionChangedEventArgs(x, CorridorStartPositionOld));
    }

    public event EventHandler<SupportResistanceChangedEventArgs> SupportResistanceChanged;
    protected void OnSupportResistanceChanged(DraggablePoint dp, Guid uid, Point positionOld, Point positionNew) {
      var isMouseCaptured = dp.IsMouseCaptured;
      var isInteractive = GetIsInteractive(dp);
      if ((isMouseCaptured || isInteractive) && SupportResistanceChanged != null) {
        DispatcherScheduler.Current.Schedule(
         () => {
           SupportResistanceChanged(this, new SupportResistanceChangedEventArgs(uid, positionNew.Y, positionOld.Y));
           var suppRes = BuyRates.Concat(SellRates).Single(sr => sr.Key == uid).Value.DataContext;//._IsExitOnly	true	bool
           SetTradingRange(suppRes, positionNew.Y);


           if (_isShiftDown) {
             var isBuy = BuyRates.Any(br => br.Key == uid);
             var next = (isBuy ? SellRates : BuyRates).OrderBy(bs => (bs.Value.DraggablePoint.Position.Y - dp.Position.Y).Abs()).First();
             var distance = (isBuy ? -1 : 1) * SuppResMinimumDistance;
             var newNextPosition = new Point(positionNew.X, positionNew.Y + distance);
             next.Value.DraggablePoint.Position = newNextPosition;
             SupportResistanceChanged(this, new SupportResistanceChangedEventArgs(next.Key, newNextPosition.Y, newNextPosition.Y));
           }
         });
      }
    }

    private void SetTradingRange(object suppRes, double position) {
      _tradingHorisontalRange.Value1 = centerOfMassHLineHigh.Value;
      _tradingHorisontalRange.Value2 = centerOfMassHLineLow.Value;
      //if (!suppRes.GetProperty<bool>("IsExitOnly"))
      //  if (suppRes.GetProperty<bool>("IsBuy")) _tradingHorisontalRange.Value1 = position;
      //  else _tradingHorisontalRange.Value2 = position;
    }
    #endregion

    #region Update Ticks
    #endregion

    public void FitToView() {
      plotter.Dispatcher.BeginInvoke(new Action(() => {
        try {
          plotter.FitToView();
        } catch (InvalidOperationException) {
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(new LogMessage(exc));
        }
      }), DispatcherPriority.DataBind);
    }


    bool inRendering;
    private bool IsPlotterInitialised;
    public void AddTicks(Price lastPrice, List<Rate> ticks, List<Volt> voltsByTick,
  double voltageHigh, double voltageCurr, double priceMaxAvg, double priceMinAvg,
  double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, double[] priceAverageAskBid) {
      AddTicks(lastPrice, ticks.ToArray(), null, new string[0], null, voltageHigh, voltageCurr, priceMaxAvg, priceMinAvg,
                      netBuy, netSell, timeHigh, timeCurr, DateTime.MinValue, priceAverageAskBid);
    }
    public void AddTicks(Price lastPrice, Rate[] ticks, PriceBar[][] voltsByTicks, string[] info, bool? trendHighlight,
                          double voltageHigh, double voltageAverage, double priceMaxAvg, double priceMinAvg,
                          double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, DateTime timeLow, double[] priceAverageAskBid) {
      if (inRendering) return;
      PriceBar[] voltsByTick = voltsByTicks.FirstOrDefault();
      #region Conversion Functions
      _roundTo = lastPrice.Digits;
      #endregion
      ticks = new List<Rate>(ticks).ToArray();
      #region Set DataSources
      if (ticks.Any(t => t != null && t.PriceAvg1 != 0)) {
        #region Set Trendlines
        if (false && trendHighlight.HasValue)
          if (trendHighlight.Value) {
            trendLine2.StrokeThickness = 2;
            trendLine3.StrokeThickness = 1;
          } else {
            trendLine2.StrokeThickness = 1;
            trendLine3.StrokeThickness = 2;
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
        {
          var weekEnd = new[] { DayOfWeek.Sunday, DayOfWeek.Saturday };
          Action<DateTimeOffset, DateTimeOffset, TimeZoneInfo, Action<IList<DateTime>>> marketTimes2 = (firstDate, lastDate, tz, drawTimes) => {
            var daysCount = (lastDate - firstDate).TotalDays.Ceiling() + 2;
            var dateStart = TimeZoneInfo.ConvertTime(firstDate, tz);
            var dateEnd = TimeZoneInfo.ConvertTime(lastDate, tz);
            dateStart = dateStart.Subtract(dateStart.TimeOfDay);
            var times = Enumerable.Range(0, daysCount)
              .Select(d => new[] { dateStart.AddDays(d).AddHours(8).ToLocalTime().DateTime, dateStart.AddDays(d).AddHours(16).ToLocalTime().DateTime })
              .SelectMany(d => d).Where(d => d.Between(firstDate, lastDate) && !weekEnd.Contains(d.DayOfWeek)).ToList();
            if (TimeZoneInfo.ConvertTime(times.LastOrDefault(), tz).Hour == 8) times.Add(times.Last().AddHours(8));
            if (TimeZoneInfo.ConvertTime(times.FirstOrDefault(), tz).Hour == 16) times.Insert(0, times[0].AddHours(-8));
            drawTimes(times);
          };
          var tickFirstDate = ticks[0].StartDate;
          var tickLastDate = ticks.Last().StartDate;
          marketTimes2(tickFirstDate, tickLastDate, TimeZoneInfo.Local, DrawNYTimes);
          marketTimes2(tickFirstDate, tickLastDate, DateTimeZone.DateTimeZone.TokyoZone, DrawTokyoTimes);
          marketTimes2(tickFirstDate, tickLastDate, DateTimeZone.DateTimeZone.LondonZone, DrawLindonTimes);
        }
        var correlation = 0;// global::alglib.pearsoncorrelation(animatedPriceY.ToArray(), ticks.Select(r => r.PriceAvg).ToArray());
        if (correlation < 1.99) try {
            ReAdjustXY(animatedTimeX, animatedPriceY, ticks.Count());
            ReAdjustXY(animatedTime0X, ticks.Count());
            ReAdjustXY(animatedPriceBidY, ticks.Count());
            ReAdjustXY(animatedPrice1Y, ticks.Count());
            var min = animatedPriceY.Min();
            var max = animatedPriceY.Max();
            _trendLinesH = max - min;
            _trendLinesY = min + (_trendLinesH) / 2;
            {
              var i = 0;
              var lastRate = ticks.Aggregate((rp, rn) => {
                SetPoint(i++, GetPriceHigh(rp), GetPriceLow(rp)/* < rn.PriceAvg ? rp.PriceLow : rp.PriceHigh*/, GetPriceMA(rp), rp);
                return rn;
              });
              SetPoint(i, CalculateLastPrice(lastRate, GetPriceHigh), CalculateLastPrice(lastRate, GetPriceLow), CalculateLastPrice(lastRate, GetPriceMA), lastRate);
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
                animatedVolt1ValueY[i] = PriceBarValue(voltsByTicks[1][i]);
                animatedVolt1TimeX[i] = voltsByTicks[1][i].StartDateContinuous;
              }
            }

          } catch (InvalidOperationException) {
            return;
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

      BarsPeriod = (animatedTimeX[0] - animatedTimeX[1]).Duration().TotalMinutes.ToInt();
      BarsCount = animatedTimeX.Count();
      var rateFirst = ticks.FirstOrDefault(r => r.PriceAvg1 != 0) ?? new Rate();
      var rateLast = ticks.LastOrDefault(r => r.PriceAvg1 != 0) ?? new Rate();
      var ratesForCorridor = new[] { rateFirst, rateLast };
      //var ratesforTrend = new[] { ticks.First(r => r.TrendLine > 0), ticks.Last(r => r.TrendLine > 0) };
      var errorMessage = "Period:" + (ticks[1].StartDate - ticks[0].StartDate).Duration().Minutes + " minutes.";
      Action a = () => {
        var doVolts = voltsByTick != null;
        CreateCurrencyDataSource(doVolts);
        try {
          //SetGannAngles(ticks, SelectedGannAngleIndex);
          animatedDataSource.RaiseDataChanged();
          //animatedDataSourceBid.RaiseDataChanged();
          //animatedDataSource1.RaiseDataChanged();
          if (doVolts) {
            VoltageHigh = voltageHigh;
            VoltageAverage = voltageAverage;
          }
          //animatedVoltDataSource.RaiseDataChanged();
          //_voltGraph.Stroke = new SolidColorBrush(animatedVoltValueY.Last() > 0 ? Colors.LimeGreen : Colors.Tan);

        } catch (InvalidOperationException) {
        } finally {
          try {
            //GannLine = ratesforTrend;
            if ( false && viewPortContainer.Visibility == Visibility.Visible) {
              double[] doubles = new double[animatedPriceY.Count];
              animatedPriceY.CopyTo(doubles);
              var animatedPriceYMax = animatedPriceY.Max();
              var animatedPriceYMin = animatedPriceY.Min();
              var animatedTimeXMax = animatedTimeX.Max();
              var animatedTimeXMin = animatedTimeX.Min();
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
                //plotter.Children.Add(viewPortContainer);
              }
              viewPortContainer.Position = new Point(dateAxis.ConvertToDouble(animatedTimeXMin) + xOffset, y);
              viewPortContainer.InvalidateVisual();
            }
          } catch {
          }
          #region Set Lines

          //LineAvgAsk = lastPrice.Ask;
          //LineAvgBid = lastPrice.Bid;
          CenterOfMassHLineHigh = CenterOfMassBuy;
          CenterOfMassHLineLow = CenterOfMassSell;

          //SetFibLevels(priceMaxAvg, priceMinAvg);

          LineTimeMax = timeHigh;
          LineTimeMin = timeCurr;
          if (timeLow > DateTime.MinValue)
            LineTimeAvg = timeLow;
          if (!timeHigh.IsMin() && !timeCurr.IsMin()) {
            var dateStart = timeHigh.Min(timeCurr);
            var dateEnd = timeHigh.Max(timeCurr);
            var indexStart = animatedTimeX.TakeWhile(t => t < dateStart).Count();
            var indexEnd = animatedTimeX.Skip(indexStart).TakeWhile(t => t <= dateEnd).Count();
            var yValues = animatedPriceY.GetRange(indexStart, indexEnd);
            double yMax = yValues.Max(), yMin = yValues.Min();
            var pointStart = new Point(lineTimeMin.Value.Min(lineTimeMax.Value), yMax);
            var pointEnd = new Point(lineTimeMin.Value.Max(lineTimeMax.Value), yMin);
            var rect = new Rect(pointStart, pointEnd);
            _shortWaveVerticalRange.Bounds = new DataRect(rect);
          }

          LineNetSell = netSell;
          LineNetBuy = netBuy;
          RaiseTrendLineDraw();
          #endregion
        }
      };

      if (Dispatcher.CheckAccess())
        a();
      else {
        inRendering = true;
        try {
          Dispatcher.Invoke(a);
        } finally {
          inRendering = false;
        }
      }
    }

    private void SetVerticalRange(VerticalRange range, DateTime date1, DateTime date2) {
      try {
        range.Value1 = ConvertStartDateToContiniousDouble(date1);
        range.Value2 = ConvertStartDateToContiniousDouble(date2);
      } catch { }
    }
    #region SetLastPoint Subject
    object _SetLastPointSubjectLocker = new object();
    ISubject<Action> _SetLastPointSubject;
    ISubject<Action> SetLastPointSubject {
      get {
        lock (_SetLastPointSubjectLocker)
          if (_SetLastPointSubject == null) {
            _SetLastPointSubject = new Subject<Action>();
            _SetLastPointSubject.SubscribeToLatestOnBGThread(action => action.InvoceOnUI(), LogMessage.Send);
          }
        return _SetLastPointSubject;
      }
    }
    public void SetLastPoint(double high, double low, double ma, Rate rate) {
      SetLastPointSubject.OnNext(() => _SetLastPoint(high, low, ma, rate));
    }
    #endregion


    private void _SetLastPoint(double high,double low, double ma, Rate rateLast) {
      try {
        SetPoint(animatedPriceY.Count - 1, high, low, ma, rateLast);
        animatedDataSourceBid.RaiseDataChanged();
        animatedDataSource.RaiseDataChanged();
        animatedDataSource1.RaiseDataChanged();
      } catch (Exception exc) {
        LogMessage.Send(exc);
      }
    }
    private void SetPoint(int i, double high, double low, double ma, Rate rateLast) {
      animatedPriceY[i] = high.IfNaN(ma);
      animatedPriceBidY[i] = low.IfNaN(ma);
      animatedPrice1Y[i] = double.IsNaN(ma) ? (high + low) / 2 : ma;
      animatedTimeX[i] = rateLast.StartDateContinuous;
      animatedTime0X[i] = rateLast.StartDate;
    }

    Func<Rate, bool> hasGannAnglesFilter = r => r.GannPrice1x1 > 0;
    private LineGraph _voltGraph;
    private int _roundTo;
    private void SetGannAngles(ICollection<Rate> rates, int selectedIndex) {
      var rateFirst = rates.FirstOrDefault(hasGannAnglesFilter);
      if (rateFirst == null) return;
      var rateLast = rates.Reverse().First(hasGannAnglesFilter);
      foreach (var i in Enumerable.Range(0, GannAnglesCount)) {
        var gannPriceFirst = rateFirst.GannPrices[i];
        GannAngles[i].SelectedValue = selectedIndex - i;
        GannAngles[i].StartPoint = new Point(dateAxis.ConvertToDouble(rateFirst.StartDateContinuous), gannPriceFirst);
        GannAngles[i].EndPoint = new Point(dateAxis.ConvertToDouble(rateLast.StartDateContinuous), rateLast.GannPrices[i]);
        if (i == GannAngle1x1Index) {
          GannAngleOffsetPoint.Anchor = new Point(ConvertToDouble(rateFirst.StartDate), rateFirst.GannPrice1x1);
          if (!GannAngleOffsetPoint.IsMouseCaptured) {
            var up = rateFirst.GannPrice1x1 < rateLast.GannPrice1x1;
            Rate rateForGannPoint;
            if (up) {
              var rateMax = rates.OrderBy(r => r.AskHigh).Last();
              rateForGannPoint = rates.Where(r => r.GannPrices.Length > 0 && r.GannPrice1x1 < rateMax.BidLow).DefaultIfEmpty(rateLast).Last();
              var dateMiddle = rateFirst.StartDateContinuous + (rateForGannPoint.StartDateContinuous - rateFirst.StartDateContinuous).Multiply(.5);
              rateForGannPoint = rates.Where(hasGannAnglesFilter).LastOrDefault(r => r.StartDateContinuous <= dateMiddle);
            } else {
              var rateMin = rates.OrderBy(r => r.BidLow).First();
              rateForGannPoint = rates.Where(r => r.GannPrices.Length > 0 && r.GannPrice1x1 > rateMin.AskHigh).DefaultIfEmpty(rateLast).Last();
              var dateMiddle = rateFirst.StartDateContinuous + (rateForGannPoint.StartDateContinuous - rateFirst.StartDateContinuous).Multiply(.5);
              rateForGannPoint = rates.Where(hasGannAnglesFilter).LastOrDefault(r => r.StartDateContinuous <= dateMiddle);
            }
            if (rateForGannPoint == null) {
              Debug.WriteLine("rateForGannPoint is null at:" + Environment.NewLine + new StackTrace(new StackFrame(true)));
              return;
            }
            GannAngleOffsetPoint.BarPeriod = TimeSpan.FromMinutes(1);
            GannAngleOffsetPoint.Position = new Point(ConvertToDouble(rateForGannPoint.StartDate), rateForGannPoint.GannPrices[GannAngle1x1Index]);
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



    public Func<Rate, double> GetPriceMA { get; set; }
    public Func<Rate, Func<Rate, double>, double> CalculateLastPrice { get; set; }

  }
}
