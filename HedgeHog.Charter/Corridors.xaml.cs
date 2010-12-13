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


namespace HedgeHog {
  /// <summary>
  /// Interaction logic for Corridors.xaml
  /// </summary>
  public partial class Corridors : Window {
    List<DateTime> animatedTimeX = new List<DateTime>();
    List<double> animatedPriceY = new List<double>();
    EnumerableDataSource<double> animatedDataSource = null;

    List<DateTime> animatedVoltTimeX = new List<DateTime>();
    List<double> animatedVoltValueY = new List<double>();
    EnumerableDataSource<double> animatedVoltDataSource = null;

    List<DateTime> animatedVolt1TimeX = new List<DateTime>();
    List<double> animatedVolt1ValueY = new List<double>();
    EnumerableDataSource<double> animatedVolt1DataSource = null;

    TextBlock infoBox = new TextBlock() { FontFamily = new FontFamily("Courier New") };
    ViewportUIContainer viewPortContainer = new ViewportUIContainer();

    #region Lines
    HorizontalLine lineMax = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.DarkOrange),StrokeThickness = 1 };
    public double LineMax { set { lineMax.Value = value; } }

    HorizontalLine lineMaxAvg = new HorizontalLine() { StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.Brown) };
    public double LineMaxAvg { set { lineMaxAvg.Value = value; } }

    HorizontalLine lineMin = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.LimeGreen),StrokeThickness = 1,StrokeDashArray={1} };
    public double LineMin {      set { lineMin.Value = value; }    }

    HorizontalLine lineMinAvg = new HorizontalLine() { StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.Navy) };
    public double LineMinAvg { set { lineMinAvg.Value = value; } }

    HorizontalLine lineNetSell = new HorizontalLine() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkBlue) };
    public double LineNetSell { set { lineNetSell.Value = value; } }

    HorizontalLine lineNetBuy = new HorizontalLine() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    public double LineNetBuy { set { lineNetBuy.Value = value; } }

    HorizontalLine lineAvgAsk = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Pink) };
    public double LineAvgAsk { set { lineAvgAsk.Value = value; } }

    HorizontalLine lineAvgBid = new HorizontalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Pink) };
    public double LineAvgBid { set { lineAvgBid.Value = value; } }

    VerticalLine lineTimeMax = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Brown) };
    public DateTime LineTimeMax { set { lineTimeMax.Value = dateAxis.ConvertToDouble(value); } }

    VerticalLine lineTimeMin = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.Navy) };
    public DateTime LineTimeMin { set { lineTimeMin.Value = dateAxis.ConvertToDouble(value); } }

    VerticalLine lineTimeAvg = new VerticalLine() { StrokeDashArray = { 2 }, StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkGreen) };
    public DateTime LineTimeAvg { set { lineTimeAvg.Value = dateAxis.ConvertToDouble(value); } }

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
        var height = CorridorHeightMultiplier*(value[0].PriceAvg2 - value[0].PriceAvg3);
        trendLine11.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg2 + height);
        trendLine11.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].PriceAvg2 + height);
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
        trendLine22.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDateContinuous), value[0].PriceAvg3 - height);
        trendLine22.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDateContinuous), value[1].PriceAvg3 - height);
      }
    }

    List<HorizontalLine> otherHLines = new List<HorizontalLine>();
    List<VerticalLine> otherVLines = new List<VerticalLine>();
    #endregion

    #region Ctor
    public Corridors()      : this("") {    }
    public Corridors(string name) {
      this.Name = name.Replace("/", "");
      InitializeComponent();
      this.Title += ": " + name;
      plotter.Children.RemoveAll<AxisNavigation>();

      Closing += new System.ComponentModel.CancelEventHandler(Corridors_Closing);
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

    private void CreateCurrencyDataSource(bool doVolts) {
      plotter.Children.RemoveAt(0);

      #region Add Main Graph
      {
        EnumerableDataSource<DateTime> xSrc = new EnumerableDataSource<DateTime>(animatedTimeX);
        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedDataSource = new EnumerableDataSource<double>(animatedPriceY);
        animatedDataSource.SetYMapping(y => y);
        this.PriceLineGraph = plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource), priceLineGraphColor, 1, "");
        this.PriceLineGraph.Description.LegendItem.Visibility = System.Windows.Visibility.Collapsed;
        Border infoBorder = new Border() { 
          BorderBrush = new SolidColorBrush(Colors.Maroon), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
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
        innerPlotter.AddLineGraph(new CompositeDataSource(xSrc, animatedVoltDataSource), Colors.DarkOrange, 1, "")
          .Description.LegendItem.Visibility = Visibility.Collapsed;

        xSrc = new EnumerableDataSource<DateTime>(animatedVolt1TimeX);
        xSrc.SetXMapping(x => dateAxis.ConvertToDouble(x));
        animatedVolt1DataSource = new EnumerableDataSource<double>(animatedVolt1ValueY);
        animatedVolt1DataSource.SetYMapping(y => y);
        innerPlotter.AddLineGraph(new CompositeDataSource(xSrc, animatedVolt1DataSource), Colors.LimeGreen, 1, "")
          .Description.LegendItem.Visibility = Visibility.Collapsed;

        //innerPlotter.Children.Remove(plotter.Children.OfType<HorizontalAxis>().Single());
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
      plotter.Children.Add(lineTimeMax);
      plotter.Children.Add(trendLine);
      plotter.Children.Add(trendLine1);
      plotter.Children.Add(trendLine11);
      plotter.Children.Add(trendLine2);
      plotter.Children.Add(trendLine22);
      #endregion
    }

    #region Update Ticks
    void UpdateTicks(ObservableCollection<ChartTick> dest, List<ChartTick> src) {
      var srcDict = new Dictionary<DateTime, ChartTick>();
      src.ForEach(s => srcDict.Add(s.Time, s));
      dest.ToList().ForEach(d => {
        if (srcDict.ContainsKey(d.Time)) d.Price = srcDict[d.Time].Price;
      });
      if (((double)dest.Count / src.Count).Between(0.5, 1.5)) {
        //var ddd = dest.Except(src,new Tick()).ToArray();
        var delete = dest.Except(src,new ChartTick()).ToList();
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
        dest.AddMany(src.OrderBy(t=>t.Time));
      }
    }
    void UpdateTicks(ObservableCollection<Point> dest, List<Point> src,TimeSpan periodSpan) {
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
          var time = dateAxis.ConvertToDouble( dateAxis.ConvertFromDouble(dest.Max(t => t.X)).AddMinutes(-1));
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
    bool Initialized;
    DateTime lastDate = DateTime.MinValue;
    public void AddTicks(Price lastPrice, List<Rate> ticks, List<Volt> voltsByTick,
  double voltageHigh, double voltageCurr, double priceMaxAvg, double priceMinAvg,
  double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, double[] priceAverageAskBid) {
      AddTicks(lastPrice, ticks, null,new string[0],null, voltageHigh, voltageCurr, priceMaxAvg, priceMinAvg,
                      netBuy, netSell, timeHigh, timeCurr, DateTime.MinValue, priceAverageAskBid);
    }
    public void AddTicks(Price lastPrice, List<Rate> ticks, PriceBar[][] voltsByTicks, string[] info, bool? trendHighlight,
  double voltageHigh, double voltageCurr, double priceMaxAvg, double priceMinAvg,
  double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, DateTime timeLow, double[] priceAverageAskBid) {
    var voltsByTick = voltsByTicks[0];
      try {
        ticks = ticks.ToList();
      } catch {
        ticks = ticks.ToList();
      }
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
        var rateFirst = ticks.First(r => r.PriceAvg1 != 0);
        var rateLast = ticks.Last(r => r.PriceAvg1 != 0);
        var ratesForTrend = new[] { rateFirst, rateLast };
        TrendLine = TrendLine1 = TrendLine11 = TrendLine2 = TrendLine22 = ratesForTrend;
        if (trendHighlight.HasValue)
          if (trendHighlight.Value) {
            trendLine1.StrokeThickness = 2;
            trendLine2.StrokeThickness = 1;
          } else {
            trendLine1.StrokeThickness = 1;
            trendLine2.StrokeThickness = 2;
          }
        //TicksAvg1.Clear();
        //var avg = ticks.Count > maxTicks ? FXW.GetMinuteTicks(ticks.Select(t => new FXW.Tick(t.StartDateContinuous, t.PriceAvg1, t.PriceAvg1, false)), 1).Select(rateToTick) :
        //  ticks.Select(t => new FXW.Tick(t.StartDateContinuous, t.PriceAvg1, t.PriceAvg1, false)).Select(tickToTick);
        //UpdateTicks(TicksAvg1, avg);
      }
      var aw = plotter.ActualWidth;
      #endregion
      if (!Initialized) {
        Initialized = true;
        CreateCurrencyDataSource(voltsByTick != null);
      }
      #region Update Main Chart
      {
        var correlation = global::alglib.pearsoncorrelation(animatedPriceY.ToArray(), ticks.Select(r => r.PriceAvg).ToArray());
        if (correlation < 1.99) {
          ReAdjustXY(animatedTimeX, animatedPriceY, ticks.Count);
          for (var i = 0; i < ticks.Count(); i++) {
            animatedPriceY[i] = ticks[i].PriceAvg;
            animatedTimeX[i] = ticks[i].StartDateContinuous;
          }

          if (voltsByTick != null) {
            ReAdjustXY(animatedVoltTimeX, animatedVoltValueY, voltsByTick.Length);
            for (var i = 0; i < voltsByTick.Count(); i++) {
              animatedVoltValueY[i] = PriceBarValue(voltsByTick[i]);
              animatedVoltTimeX[i] = voltsByTick[i].StartDateContinuous;
            }
          }
          if (voltsByTicks!= null && voltsByTicks.Length >1) {
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
        //var up = animatedPriceY.Last() < (animatedPriceY.Max() + animatedPriceY.Min()) / 2;
        var up = animatedPriceY.First() < (animatedPriceY.Max() + animatedPriceY.Min()) / 2;
        var yHeight = animatedPriceY.Max() - animatedPriceY.Min();
        var xWidth = dateAxis.ConvertToDouble(animatedTimeX.Max()) - dateAxis.ConvertToDouble(animatedTimeX.Min());
        var yOffset = yHeight * infoBox.ActualHeight / plotter.ActualHeight / 2;
        var xOffset = xWidth * infoBox.ActualWidth / plotter.ActualWidth / 2;
        var y = (up ? animatedPriceY.Max() - yOffset : animatedPriceY.Min() + yOffset);
        infoBox.Text = string.Join(Environment.NewLine, info);
        viewPortContainer.Position = new Point(dateAxis.ConvertToDouble(animatedTimeX.Min()) + xOffset, y);
      }
      animatedDataSource.RaiseDataChanged();
      animatedVoltDataSource.RaiseDataChanged();
      animatedVolt1DataSource.RaiseDataChanged();
      #endregion

      //plotter.FitToView();
      //System.Diagnostics.Debug.WriteLine("AddTicks:" + (DateTime.Now - d).TotalMilliseconds + " ms.");
      #region Set Lines
      LineMax = voltageHigh;
      LineMaxAvg = priceMaxAvg;
      lineMaxAvg.ToolTip = priceMaxAvg;
      LineMinAvg = priceMinAvg;
      lineMinAvg.ToolTip = priceMinAvg;
      LineMin = voltageCurr;
      LineTimeMax = timeHigh;
      lineTimeMax.ToolTip = ticks.First(r => r.StartDateContinuous == timeHigh).StartDate; ;
      LineTimeMin = timeCurr;
      LineTimeAvg = timeLow;
      LineNetSell = netSell;
      LineNetBuy = netBuy;
      #endregion
    }

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

    public double CorridorHeightMultiplier { get; set; }
    public Func<PriceBar, double> PriceBarValue;

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
  }

  public class ChartTick : INotifyPropertyChanged,IEqualityComparer<ChartTick>{
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

public event PropertyChangedEventHandler  PropertyChanged;

#endregion

#region IEqualityComparer<Tick> Members

public bool Equals(ChartTick x, ChartTick y) {
  return x.Time == y.Time && x.Price == y.Price;
}

public int GetHashCode(ChartTick obj) {
  return obj.Time.GetHashCode() ^ obj.Price.GetHashCode() ;
}

#endregion
  }
}
