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

namespace HedgeHog {
  public class CharterControlMessage : GalaSoft.MvvmLight.Messaging.Messenger { }
  /// <summary>
  /// Interaction logic for CharterControl.xaml
  /// </summary>
  public partial class CharterControl : Models.UserControlModel{
    public enum MessageType { Add, Remove }
    public CharterControl():this("",null) {
    }
    public CharterControl(string name, CompositionContainer container = null) {
      if (container != null) container.SatisfyImportsOnce(this);
      this.Name = name.Replace("/", "");
      InitializeComponent();
      OnPropertyChanged("Header");
    }
    #region Attached Properties
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

    private int _barsPeriod;
    public int BarsPeriod {
      get { return _barsPeriod; }
      set {
        if (_barsPeriod == value) return;
        _barsPeriod = value;
        OnPropertyChanged("BarsPeriod");
        OnPropertyChanged("Header");
      }
    }

    private int _BarsCount;
    public int BarsCount {
      get { return _BarsCount; }
      set {
        if (_BarsCount != value) {
          _BarsCount = value;
          OnPropertyChanged("BarsCount");
          OnPropertyChanged("Header");
        }
      }
    }


    public string Header { get { return Name + ":" + (BarsPeriodType)BarsPeriod + "×" + BarsCount; } }

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

    List<double> animatedPriceBidY = new List<double>();
    EnumerableDataSource<double> animatedDataSourceBid = null;

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
    public Func<Rate, double> GetPriceHigh { get; set; }
    public Func<Rate, double> GetPriceLow { get; set; }

    public double CenterOfMassBuy { get; set; }
    public double CenterOfMassSell { get; set; }


    #region Lines
    public LineGraph PriceLineGraph { get; set; }
    public LineGraph PriceLineGraphBid { get; set; }
    static Color priceLineGraphColor = Colors.Black;
    static Color priceLineGraphColorBuy = Colors.DarkGreen;
    static Color priceLineGraphColorSell = Colors.DarkRed;
    bool? buySell;
    public void SetPriceLineColor(bool? buySell) {
      if (PriceLineGraph!=null && this.buySell != buySell) {
        var brush = new SolidColorBrush(buySell.HasValue ? buySell.Value ? priceLineGraphColorBuy : priceLineGraphColorSell : priceLineGraphColor);
        PriceLineGraph.LinePen.Brush = brush;
        PriceLineGraphBid.LinePen.Brush = brush;
        this.buySell = buySell;
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
      get { return _DoShowCenterOfMass; }
      set {
        if (_DoShowCenterOfMass != value) {
          _DoShowCenterOfMass = value;
          OnPropertyChanged("DoShowCenterOfMass");
        }
      }
    }

    static Brush centerOfMassBrush = new SolidColorBrush(Colors.SteelBlue);
    HorizontalLine _centerOfMassHLineHigh;
    HorizontalLine centerOfMassHLineHigh {
      get {
        if (_centerOfMassHLineHigh == null) {
          _centerOfMassHLineHigh = new HorizontalLine() { StrokeThickness = 2, Stroke = centerOfMassBrush };
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
          _centerOfMassHLineLow = new HorizontalLine() { StrokeThickness = 2, Stroke = centerOfMassBrush };
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
    double LineAvgAsk { set { lineAvgAsk.Value = value; } }

    HorizontalLine lineAvgBid = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DodgerBlue) };
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
          _GannAngleOffsetPoint = new GannAngleOffsetDraggablePoint(dateAxis.ConvertFromDouble, new NumberToStringAutoFormatConverter());
          _GannAngleOffsetPoint.PositionChanged += _GannAngleOffsetPoint_PositionChanged;
        }
        return _GannAngleOffsetPoint;
      }
    }

    Scheduler GannAngleChangedScheduler = new Scheduler(Application.Current.Dispatcher, TimeSpan.FromSeconds(.1));
    void _GannAngleOffsetPoint_PositionChanged(object sender, PositionChangedEventArgs e) {
      var offset = GannAngleOffsetPoint.GetAngleByPosition(e.Position, animatedTimeX.ToArray(), ConvertToDateTime);
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

    DraggablePoint _ActiveDraggablePoint;

    public DraggablePoint ActiveDraggablePoint {
      get { return _ActiveDraggablePoint; }
      set { _ActiveDraggablePoint = value; }
    }

    List<HorizontalLine> otherHLines = new List<HorizontalLine>();
    List<VerticalLine> otherVLines = new List<VerticalLine>();
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
    EnumerableDataSource<ChartTick> dsAvg1 = null;
    EnumerableDataSource<ChartTick> dsAvg2 = null;
    EnumerableDataSource<ChartTick> dsAvg3 = null;
    EnumerableDataSource<ChartTick> dsVolts = null;
    EnumerableDataSource<Volt> dsVoltsPoly = null;
    #endregion


    class DraggablePointInfo {
      public DraggablePoint DraggablePoint { get; set; }
      public ObservableValue<double> TradesCount { get; set; }
      public DraggablePointInfo(DraggablePoint dp, double tradesCount) {
        this.DraggablePoint = dp;
        this.TradesCount = new ObservableValue<double>() { Value = tradesCount };
      }
    }

    Dictionary<Guid, DraggablePointInfo> BuyRates = new Dictionary<Guid, DraggablePointInfo>();
    Dictionary<Guid, DraggablePointInfo> SellRates = new Dictionary<Guid, DraggablePointInfo>();

    public class BuySellLevel {
      public double Rate { get; set; }
      public double CrossCount { get; set; }
      public bool IsBuy { get; set; }
      public BuySellLevel(double rate, double crossCount, bool isBuy) {
        this.Rate = rate;
        this.CrossCount = crossCount;
        this.IsBuy = isBuy;
      }
    }

    static double[] StrokeArrayForTrades = new double[] { 5, 2, 2, 2 };
    Dictionary<string, HorizontalLine> tradeLines = new Dictionary<string, HorizontalLine>();
    public void SetTradeLines(ICollection<Trade> trades, double spread) {
      var a = new Action(() => {
        var tradesAdd = from value in trades.Select(t => t.Id).Except(this.tradeLines.Select(t => t.Key))
                        join trade in trades on value equals trade.Id
                        select trade;
        foreach (var t in tradesAdd) {
          var hl = new HorizontalLine(t.Open + (t.Buy ? +1 : -1) * spread) { ToolTip = t.Open + " @ " + t.Time, StrokeDashArray = new DoubleCollection(StrokeArrayForTrades), StrokeThickness = 1, Stroke = new SolidColorBrush(t.Buy ? priceLineGraphColorBuy : priceLineGraphColorSell) };
          plotter.Children.Add(hl);
          this.tradeLines.Add(t.Id, hl);
        }
        var tradesDelete = this.tradeLines.Select(t => t.Key).Except(trades.Select(t => t.Id)).ToArray();
        foreach (var t in tradesDelete) {
          var hl = tradeLines[t];
          plotter.Children.Remove(hl);
          tradeLines.Remove(t);
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
    void SetBuySellRates(Dictionary<Guid, BuySellLevel> suppReses) {
      foreach (var suppRes in suppReses) {
        var isBuy = suppRes.Value.IsBuy;
        Dictionary<Guid, DraggablePointInfo> rates = isBuy ? BuyRates : SellRates;
        var uid = suppRes.Key;
        var rate = suppRes.Value.Rate;
        var tradesCount = suppRes.Value.CrossCount;
        if (!rates.ContainsKey(uid)) {
          string anchorTemplateName = "DraggArrow" + (isBuy ? "Up" : "Down");
          Brush brush = new SolidColorBrush(isBuy ? Colors.DarkRed : Colors.Navy);
          var line = new HorizontalLine() { Stroke = brush, StrokeDashArray = { 2 } };
          var dragPoint = new TemplateableDraggablePoint() { MarkerTemplate = FindResource(anchorTemplateName) as ControlTemplate };
          SetFriend(dragPoint, line);
          plotter.Children.Add(line);
          plotter.Children.Add(dragPoint);
          //dragPoint.SetBinding(DraggablePoint.PositionProperty, new Binding("Value") { Source = ov });
          dragPoint.PositionChanged += (s, e) => {
            OnSupportResistanceChanged(s as DraggablePoint, uid, e.PreviousPosition, e.Position);
          };
          dragPoint.ToolTip = "UID:" + uid;
          plotter.PreviewKeyDown += (s, e) => {
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
            }
          };
          DraggableManager.SetHorizontalAnchor(line, dragPoint);
          var dpi = new DraggablePointInfo(dragPoint, tradesCount);
          rates.Add(uid, dpi);
          dragPoint.DataContext = dpi;
        }
        var dp = rates[uid].DraggablePoint;
        dp.Dispatcher.BeginInvoke(new Action(() => {
          var raiseChanged = rate == 0;
          if (raiseChanged) rate = animatedPriceY.Average();
          dp.Position = CreatePointY(rate);
          rates[uid].TradesCount.Value = tradesCount;
        }));
      }
    }

    private Point CreatePointY(double y) { return new Point(dateAxis.ConvertToDouble(animatedTimeX[0]), y); }

    List<HorizontalLine> FibLevels = new List<HorizontalLine>();
    List<ColoredSegment> GannAngles = new List<ColoredSegment>();
    Dictionary<SimpleLine, DraggablePoint> LineToPoint = new Dictionary<SimpleLine, DraggablePoint>();
    private void CreateCurrencyDataSource(bool doVolts) {
      if (IsPlotterInitialised) return;
      plotter.KeyUp += (s, e) => {
        if (e.Key == Key.RightShift || e.Key == Key.LeftShift)
          _isShiftDown = false;
      };
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

        animatedDataSourceBid = new EnumerableDataSource<double>(animatedPriceBidY);
        animatedDataSourceBid.SetYMapping(y => y);
        this.PriceLineGraphBid = plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSourceBid), priceLineGraphColor, 1, "");
        this.PriceLineGraphBid.Description.LegendItem.Visibility = Visibility.Collapsed;

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

      plotter.Children.Add(centerOfMassHLineHigh);
      plotter.Children.Add(centerOfMassHLineLow);

      plotter.Children.Add(lineTimeMax);
      plotter.Children.Add(CorridorStartPointX);
      LineToPoint.Add(lineTimeMax, CorridorStartPointX);

      plotter.Children.Add(GannAngleOffsetPoint);

      InsertFibLines();

      plotter.KeyDown += new KeyEventHandler(plotter_KeyDown);
      plotter.PreviewKeyDown += new KeyEventHandler(plotter_PreviewKeyDown);

      #endregion
    }

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

    double GuessPipSize(double price) { return price < 10 ? 0.0001 : 0.01; }

    void plotter_PreviewKeyDown(object sender, KeyEventArgs e) {
    }

    private void AdjustDraggablePointByPip(DraggablePoint dp, KeyEventArgs e) {
      if (dp.IsMouseOver) {
        var pip = GuessPipSize(dp.Position.Y);
        var step = e.Key == Key.Down ? -pip : e.Key == Key.Up ? pip : 0;
        if (step != 0) {
          e.Handled = true;
          SetIsInteractive(dp, true);
          dp.Position = new Point(dp.Position.X, dp.Position.Y + step);
          SetIsInteractive(dp, false);
        }
      }
    }

    void plotter_KeyDown(object sender, KeyEventArgs e) {

      e.Handled = true;
    }

    #region Event Handlers
    DateTime CorridorStartPositionOld;
    DateTime GetPriceStartDate(DateTime startDateContinuous) {
      var x = animatedTimeX.OrderBy(d => (d - startDateContinuous).Duration()).First();
      return animatedTime0X[animatedTimeX.IndexOf(x)];
    }
    ThreadScheduler corridorStartDateScheduler;
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

    public event EventHandler<CorridorPositionChangedEventArgs> CorridorStartPositionChanged;
    private void OnCorridorStartPositionChanged() {
      var x = GetPriceStartDate(ConvertToDateTime(CorridorStartPointX.Position.X));
      CorridorStartPositionChanged(this, new CorridorPositionChangedEventArgs(x, CorridorStartPositionOld));
    }

    Scheduler _suppResChangeScheduler = new Scheduler(Application.Current.Dispatcher, TimeSpan.FromSeconds(.3));
    public event EventHandler<SupportResistanceChangedEventArgs> SupportResistanceChanged;
    protected void OnSupportResistanceChanged(DraggablePoint dp, Guid uid, Point positionOld, Point positionNew) {
      var isMouseCaptured = dp.IsMouseCaptured;
      var isInteractive = GetIsInteractive(dp);
      if ((isMouseCaptured || isInteractive) && SupportResistanceChanged != null) {
        _suppResChangeScheduler.Cancel();
        _suppResChangeScheduler.Command = () => {
          SupportResistanceChanged(this, new SupportResistanceChangedEventArgs(uid, positionNew.Y, positionOld.Y));
          if (_isShiftDown) {
            var isBuy = BuyRates.Any(br => br.Key == uid);
            var next = (isBuy ? SellRates : BuyRates).OrderBy(bs => (bs.Value.DraggablePoint.Position.Y - dp.Position.Y).Abs()).First();
            var distance = (isBuy ? -1 : 1) * SuppResMinimumDistance;
            var newNextPosition = new Point(positionNew.X, positionNew.Y + distance);
            next.Value.DraggablePoint.Position = newNextPosition;
            SupportResistanceChanged(this, new SupportResistanceChangedEventArgs(next.Key, newNextPosition.Y, newNextPosition.Y));
          }
        };
      }
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
      ticks = new List<Rate>(ticks).ToArray();
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
          ReAdjustXY(animatedPriceBidY, ticks.Count());
          ReAdjustXY(animatedPrice1Y, ticks.Count());
          {
            var i = 0;
            var lastRate = ticks.Aggregate((rp, rn) => {
              SetPoint(i++, GetPriceHigh(rp), GetPriceLow(rp)/* < rn.PriceAvg ? rp.PriceLow : rp.PriceHigh*/, rp.PriceCMA, rp);
              return rn;
            });
            SetPoint(i, GetPriceHigh(lastRate), GetPriceLow(lastRate), lastRate.PriceCMA, lastRate);
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
          SetGannAngles(ticks, SelectedGannAngleIndex);
          animatedDataSource.RaiseDataChanged();
          animatedDataSourceBid.RaiseDataChanged();
          animatedDataSource1.RaiseDataChanged();
          if( doVolts )
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
          if (viewPortContainer.Visibility == Visibility.Visible && viewPortContainer.ActualWidth < 10 && infoBox.ActualWidth > 0) {
            plotter.Children.Remove(viewPortContainer);
            var child = viewPortContainer.Content;
            viewPortContainer.Content = null;
            viewPortContainer = new ViewportUIContainer();
            viewPortContainer.Content = child;
            //plotter.Children.Add(viewPortContainer);
          }
          viewPortContainer.Position = new Point(dateAxis.ConvertToDouble(animatedTimeXMin) + xOffset, y);
          viewPortContainer.InvalidateVisual();

          #region Set Lines

          LineAvgAsk = lastPrice.Ask;
          LineAvgBid = lastPrice.Bid;

          CenterOfMassHLineHigh = CenterOfMassBuy;
          CenterOfMassHLineLow = CenterOfMassSell;

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

    private void SetPoint(int i, double high, double low, double[] cma, Rate rateLast) {
      animatedPriceY[i] = high;
      animatedPriceBidY[i] = low;
      animatedPrice1Y[i] = cma == null ? (high + low) / 2 : cma[2];
      animatedTimeX[i] = rateLast.StartDateContinuous;
      animatedTime0X[i] = rateLast.StartDate;
    }

    Func<Rate, bool> hasGannAnglesFilter = r => r.GannPrice1x1 > 0;
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


  }
}
