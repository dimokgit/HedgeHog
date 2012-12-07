﻿using System;
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
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using HedgeHog.Shared.Messages;

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
      OnPropertyChanged(Metadata.CharterControlMetadata.Header);
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

    private double _CorridorAngle;
    public double CorridorAngle {
      get { return _CorridorAngle; }
      set {
        if (_CorridorAngle != value) {
          _CorridorAngle = value;
          OnPropertyChanged(CharterControlMetadata.CorridorAngle);
          OnPropertyChanged(Metadata.CharterControlMetadata.Header);
        }
      }
    }

    private double _HeightInPips;
    public double HeightInPips {
      get { return _HeightInPips; }
      set {
        if (_HeightInPips != value) {
          _HeightInPips = value;
          OnPropertyChanged(CharterControlMetadata.HeightInPips);
          OnPropertyChanged(Metadata.CharterControlMetadata.Header);
        }
      }
    }

    private double _CorridorHeightInPips;
    public double CorridorHeightInPips {
      get { return _CorridorHeightInPips; }
      set {
        if (_CorridorHeightInPips != value) {
          _CorridorHeightInPips = value;
          OnPropertyChanged(CharterControlMetadata.CorridorHeightInPips);
          OnPropertyChanged(Metadata.CharterControlMetadata.Header);
        }
      }
    }

    private double _RatesStDevInPips;
    public double RatesStDevInPips {
      get { return _RatesStDevInPips; }
      set {
        if (_RatesStDevInPips != value) {
          _RatesStDevInPips = value;
          OnPropertyChanged(Metadata.CharterControlMetadata.Header);
        }
      }
    }
    #region CorridorSpread
    private double _CorridorRatesStDevInPips;
    public double CorridorRatesStDevInPips {
      get { return _CorridorRatesStDevInPips; }
      set {
        if (_CorridorRatesStDevInPips != value) {
          _CorridorRatesStDevInPips = value;
          OnPropertyChanged("CorridorRatesStDevInPips");
        }
      }
    }

    #endregion
    public double SpreadForCorridor { get; set; }

    //†‡∆
    public string HeaderText;
    public string Header { get { return Name + HeaderText; } }

    public bool IsActive {
      get { return (bool)GetValue(IsActiveProperty); }
      set { SetValue(IsActiveProperty, value); }
    }

    // Using a DependencyProperty as the backing store for IsActive.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register("IsActive", typeof(bool), typeof(CharterControl), new UIPropertyMetadata(false));



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

    #region PlotterColor
    private string _PlotterColor;
    public string PlotterColor {
      get { return _PlotterColor ?? "#FFF7F3F7"; }
      set {
        if (_PlotterColor != value) {
          _PlotterColor = value;
          RaisePropertyChangedCore();
        }
      }
    }

    #endregion
    public double SuppResMinimumDistance { get; set; }

    List<DateTime> animatedTimeX = new List<DateTime>();
    List<DateTime> animatedTime0X = new List<DateTime>();
    List<double> animatedPriceY = new List<double>();
    EnumerableDataSource<double> animatedDataSource = null;

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

    public Func<Rate, double> GetPriceFunc { get; set; }
    public Func<Rate, double> GetPriceHigh { get; set; }
    public Func<Rate, double> GetPriceLow { get; set; }

    public double CenterOfMassBuy { get; set; }
    public double CenterOfMassSell { get; set; }


    #region Lines
    public LineGraph PriceLineGraph { get; set; }
    public LineGraph PriceLineGraphBid { get; set; }
    static Color priceLineGraphColorAsk = Colors.Black;
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
    public double LineAvgAsk { set { lineAvgAsk.Value = value; } }

    HorizontalLine lineTakeProfitLimit = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.LimeGreen) };
    public double LineTakeProfitLimit { set { lineTakeProfitLimit.Value = value; } }

    HorizontalLine lineAvgBid = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DodgerBlue) };
    public double LineAvgBid { set { lineAvgBid.Value = value; } }

    #region TimeLines
    #region TimeShort
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
            _lineTimeMiddle = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkGreen) };
            _lineTimeMiddle.SetAnchor(_lineTimeMiddleDraggablePoint = new DraggablePoint());
            plotter.Children.Add(_lineTimeMiddle);
            plotter.Children.Add(_lineTimeMiddleDraggablePoint);
            _lineTimeMiddleDraggablePoint.PositionChanged += _lineTimeMiddleDraggablePoint_PositionChanged;
          }
          _lineTimeMiddleDraggablePoint.Position = new Point(dateAxis.ConvertToDouble(value.StartDateContinuous), CorridorStartPointX.Position.Y + 20 * PipSize);
          _lineTimeMiddleDraggablePoint.ToolTip = value.StartDate + Environment.NewLine + "Dist:" + value.Distance;
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

    VerticalLine lineTimeMin = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Navy) };
    DateTime LineTimeMin { set { lineTimeMin.Value = dateAxis.ConvertToDouble(value); } }

    VerticalLine lineTimeAvg = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkGreen) };
    DateTime LineTimeAvg {
      set {
        lineTimeAvg.Value = dateAxis.ConvertToDouble(value);
        if (!CorridorStopPointX.IsMouseCaptured) {
          CorridorStopPointX.Position = new Point(dateAxis.ConvertToDouble(value), CorridorStartPointX.Position.Y);
          CorridorStopPointX.ToolTip = value.ToString("MM/dd/yyyy HH:mm");
        }

      }
    }

    VerticalLine lineTimeTakeProfit = new VerticalLine() {  StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.LimeGreen) };
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

    Action<DateTime>[] _lineTimeTakeProfits;

    public Action<DateTime>[] LineTimeTakeProfits {
      get {
        return _lineTimeTakeProfits ?? (_lineTimeTakeProfits = new Action<DateTime>[] { d => LineTimeTakeProfit = d, d => LineTimeTakeProfit1 = d, d => LineTimeTakeProfit2 = d, d => LineTimeTakeProfit3 = d });
      }
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
    public void SetTrendLines(Rate[] rates,bool showTrendLines) {
      if (!rates.Any()) return;
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(() => {
        TrendLine = TrendLine2 = TrendLine02 = TrendLine3 = TrendLine03 = TrendLine21 = TrendLine31 = showTrendLines ? rates : null;

        var timeHigh = rates[0].StartDateContinuous;
        var corridorTime = rates[0].StartDate;
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
        }
      }
    }

    Segment trendLine21 = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine21 {
      set {
        if (value == null)
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
        if (value == null)
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
        if (value == null)
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
        if (value == null)
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
        if (value == null)
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
        if (value == null)
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

    Schedulers.Scheduler GannAngleChangedScheduler = new Schedulers.Scheduler(Application.Current.Dispatcher, TimeSpan.FromSeconds(.1));
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
    EnumerableDataSource<Volt> dsVoltsPoly = null;
    #endregion


    class DraggablePointInfo {
      public object DataContext { get; set; }
      public DraggablePoint DraggablePoint { get; set; }
      //public ObservableValue<double> TradesCount { get; set; }
      public DraggablePointInfo(DraggablePoint dp,object dataContext) {
        this.DraggablePoint = dp;
        this.DataContext = dataContext;
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
    public void SetTradeLines(ICollection<Trade> trades, double spread) {
      var a = new Action(() => {
        var tradesAdd = from value in trades.Select(t => t.Id).Except(this.tradeLines.Select(t => t.Key))
                        join trade in trades on value equals trade.Id
                        select trade;
        foreach (var t in tradesAdd) {
          var y = t.Open + (t.Buy ? +1 : -1) * spread;
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
        Dictionary<Guid, DraggablePointInfo> rates = isBuy ? BuyRates : SellRates;
        var uid = suppRes.Key;
        var rate = suppRes.Value.Rate;
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
            if (!dragPoint.IsMouseOver) return;
            _dragPointPositionChanged = true;
          };
          //dragPoint.ToolTip = "UID:" + uid;
          plotter.PreviewMouseLeftButtonDown += (s, e) => {
            if (!dragPoint.IsMouseOver) return;
            _dragPointPositionChanged = false;
            Action a = () => {
              if (!_dragPointPositionChanged)
                dragPoint.DataContext.SetProperty("CanTrade", !dragPoint.DataContext.GetProperty<bool>("CanTrade"));
            };
            a.ScheduleOnUI(0.5.FromSeconds());
          };
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
                dragPoint.DataContext.Invoke("OnScan",null);
                break;
              case Key.T:
                dragPoint.DataContext.SetProperty("CanTrade", !dragPoint.DataContext.GetProperty<bool>("CanTrade"));
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
          var dpi = new DraggablePointInfo(dragPoint,suppRes.Value.DataContext);
          rates.Add(uid, dpi);
          dragPoint.DataContext = dpi.DataContext;
        }
        var dp = rates[uid].DraggablePoint;
        dp.Dispatcher.BeginInvoke(new Action(() => {
          var raiseChanged = rate == 0;
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

    void plotter_MouseDown(object sender, MouseButtonEventArgs e) {
      throw new NotImplementedException();
    }

    private Point CreatePointY(double y) { return new Point(dateAxis.ConvertToDouble(animatedTimeX[0]), y); }

    List<HorizontalLine> FibLevels = new List<HorizontalLine>();
    List<ColoredSegment> GannAngles = new List<ColoredSegment>();
    Dictionary<SimpleLine, DraggablePoint> LineToPoint = new Dictionary<SimpleLine, DraggablePoint>();
    private void CreateCurrencyDataSource(bool doVolts) {
      if (IsPlotterInitialised) return;
      dateAxis.MayorLabelProvider = null;
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

        EnumerableDataSource<double> animatedDataSource1 = new EnumerableDataSource<double>(animatedPrice1Y);
        animatedDataSource1.SetYMapping(y => y);
        plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource1), Colors.DarkGray, 1, "")
          .Description.LegendItem.Visibility = Visibility.Collapsed;

        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedDataSource = new EnumerableDataSource<double>(animatedPriceY);
        animatedDataSource.SetYMapping(y => y);
        this.PriceLineGraph = plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource), priceLineGraphColorAsk, 1, "");
        this.PriceLineGraph.Description.LegendItem.Visibility = System.Windows.Visibility.Collapsed;
        
        if (false) {
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
      plotter.Children.Add(lineNetSell);
      plotter.Children.Add(lineNetBuy);

      plotter.Children.Add(lineAvgAsk);
      plotter.Children.Add(lineAvgBid);
      plotter.Children.Add(lineTakeProfitLimit);
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

    Schedulers.Scheduler _suppResChangeScheduler = new Schedulers.Scheduler(Application.Current.Dispatcher, TimeSpan.FromSeconds(.3));
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
    #endregion

    public void FitToView() {
      plotter.Dispatcher.BeginInvoke(new Action(() => {
        try {
          plotter.FitToView();
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(new LogMessage(exc));
        }
      }), DispatcherPriority.ContextIdle);
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
        var correlation = 0;// global::alglib.pearsoncorrelation(animatedPriceY.ToArray(), ticks.Select(r => r.PriceAvg).ToArray());
        if (correlation < 1.99) {
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
              animatedVolt1ValueY[i] = PriceBarValue(voltsByTicks[1][i]);
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
            if (viewPortContainer.Visibility == Visibility.Visible) {
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

          LineNetSell = netSell;
          LineNetBuy = netBuy;
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

    private void SetPoint(int i, double high, double low, double ma, Rate rateLast) {
      animatedPriceY[i] = high;
      animatedPriceBidY[i] = low;
      animatedPrice1Y[i] = double.IsNaN(ma) ? (high + low) / 2 : ma;
      animatedTimeX[i] = rateLast.StartDateContinuous;
      animatedTime0X[i] = rateLast.StartDate;
    }

    Func<Rate, bool> hasGannAnglesFilter = r => r.GannPrice1x1 > 0;
    private LineGraph _voltGraph;
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
