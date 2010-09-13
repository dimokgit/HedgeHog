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

namespace HedgeHog {
  /// <summary>
  /// Interaction logic for Corridors.xaml
  /// </summary>
  public partial class Corridors : Window {
    public ObservableCollection<Point> Ticks = null;
    public ObservableCollection<ChartTick> TicksAvg1 = new ObservableCollection<ChartTick>();
    public ObservableCollection<ChartTick> TicksAvg2 = new ObservableCollection<ChartTick>();
    public ObservableCollection<ChartTick> TicksAvg3 = new ObservableCollection<ChartTick>();
    public ObservableCollection<ChartTick> Volts = null;
    public ObservableCollection<Volt> VoltsPoly = new ObservableCollection<Volt>();
    private DateTime tickerTime = DateTime.MinValue;

    #region Lines
    HorizontalLine lineMax = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.Brown) };
    public double LineMax { set { lineMax.Value = value; } }

    HorizontalLine lineMaxAvg = new HorizontalLine() { StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.Brown) };
    public double LineMaxAvg { set { lineMaxAvg.Value = value; } }

    HorizontalLine lineMin = new HorizontalLine() { Stroke = new SolidColorBrush(Colors.Navy) };
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

    Segment trendLine = new Segment() { StrokeThickness = 1, Stroke = new SolidColorBrush(Colors.DarkRed) };
    Rate[] TrendLine {
      set {
        trendLine.StartPoint = new Point(dateAxis.ConvertToDouble(value[0].StartDate), value[0].PriceAvg1);
        trendLine.EndPoint = new Point(dateAxis.ConvertToDouble(value[1].StartDate), value[1].PriceAvg1);
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

    EnumerableDataSource<Point> ds = null;
    EnumerableDataSource<ChartTick> dsAvg1 = null;
    EnumerableDataSource<ChartTick> dsAvg2 = null;
    EnumerableDataSource<ChartTick> dsAvg3 = null;
    EnumerableDataSource<ChartTick> dsVolts = null;
    EnumerableDataSource<Volt> dsVoltsPoly = null;
    private void CreateCurrencyDataSource() {
      //var ticks = new Data.ForexDBDataContext().t_Ticks;
      //ds = new EnumerableDataSource<Point>(Ticks);
      //ds.SetXMapping(ci => ci.X);//dateAxis.ConvertToDouble(
      //ds.SetYMapping(ci => ci.Y);
      if (TicksAvg1.Count > 0) {
        dsAvg1 = new EnumerableDataSource<ChartTick>(TicksAvg1);
        dsAvg1.SetXMapping(ci => dateAxis.ConvertToDouble(ci.Time));
        dsAvg1.SetYMapping(ci => ci.Price);
      }
      dsAvg2 = new EnumerableDataSource<ChartTick>(TicksAvg2);
      dsAvg2.SetXMapping(ci => dateAxis.ConvertToDouble(ci.Time));
      dsAvg2.SetYMapping(ci => ci.Price);
      if (TicksAvg3.Count > 0) {
        dsAvg3 = new EnumerableDataSource<ChartTick>(TicksAvg3);
        dsAvg3.SetXMapping(ci => dateAxis.ConvertToDouble(ci.Time));
        dsAvg3.SetYMapping(ci => ci.Price);
      }
      if (Volts != null) {
        innerPlotter.Viewport.Restrictions.Add(new InjectionDelegateRestriction(
          plotter.Viewport,
          rect => {
            rect.XMin = plotter.Viewport.Visible.XMin;
            rect.Width = plotter.Viewport.Visible.Width;
            return rect;
          }));
        dsVolts = new EnumerableDataSource<ChartTick>(Volts);
        dsVolts.SetYMapping(v => v.Price);
        dsVolts.SetXMapping(ci => dateAxis.ConvertToDouble(ci.Time));
        innerPlotter.Children.Remove(plotter.Children.OfType<HorizontalAxis>().Single());
      } else {
        innerPlotter.Children.Remove(innerPlotter.Children.OfType<VerticalAxis>().Single());
        //plotter.Children.Remove(innerPlotter);
      }
      plotter.Children.RemoveAt(0);
      var ticksLineGraph = plotter.AddLineGraph(Ticks.AsDataSource(), Colors.Black, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
      if (dsAvg1 != null)
        plotter.AddLineGraph(dsAvg1, Colors.DarkRed, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
      if (dsAvg2 != null)
        plotter.AddLineGraph(dsAvg2, Colors.IndianRed, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
      if (dsAvg3 != null)
        plotter.AddLineGraph(dsAvg3, Colors.DarkOliveGreen, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
      if (Volts!=null)
        innerPlotter.AddLineGraph(dsVolts, Colors.DarkOrange, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
      //else {
      //  new VerticalAxis() { Placement = AxisPlacement.Right }.AddToPlotter(plotter);
      //  plotter.VerticalAxis.Placement = AxisPlacement.Right;
      //}
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
    }

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
    void UpdateTicks(ObservableCollection<Point> dest, List<Point> src) {
      //var srcDict = new Dictionary<double, Point>();
      //src.ForEach(s => srcDict.Add(s.X, s));
      //dest.ToList().ForEach(d => {
      //  if (srcDict.ContainsKey(d.X)) d.Y = srcDict[d.X].Y;
      //});
      if (((double)dest.Count / src.Count).Between(0.5, 1.5)) {
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

    public Point[] AddTicks(Price lastPrice, List<Rate> ticks, List<Volt> voltsByTick,
  double voltageHigh, double voltageCurr, double priceMaxAvg, double priceMinAvg,
  double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, double[] priceAverageAskBid) {
      return AddTicks(lastPrice, ticks, voltsByTick, voltageHigh, voltageCurr, priceMaxAvg, priceMinAvg,
                      netBuy, netSell, timeHigh, timeCurr, DateTime.MinValue, priceAverageAskBid);
    }
    public Point[] AddTicks(Price lastPrice, List<Rate> ticks, List<Volt> voltsByTick,
  double voltageHigh, double voltageCurr, double priceMaxAvg, double priceMinAvg,
  double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr,DateTime timeLow, double[] priceAverageAskBid) {
    try {
      ticks = ticks.ToList();
    } catch {
      ticks = ticks.ToList();
    }
      DateTime d = DateTime.Now;
      var rateToTick = new Func<Rate, ChartTick>(t => new ChartTick() { Price = t.PriceAvg, Time = t.StartDate });
      var voltToTick = new Func<Volt, ChartTick>(t => new ChartTick() { Price = t.Volts, Time = t.StartDate });
      var tickToTick = new Func<Rate, ChartTick>(t => new ChartTick() { Price = t.PriceAvg, Time = t.StartDate });
      var rateToPoint = new Func<Rate, Point>(t => new Point(dateAxis.ConvertToDouble(t.StartDate), t.PriceAvg.Round(lastPrice.Digits-1)));  
      IEnumerable<Point> minuteTicks = null;
      if (ticks.Any(t => t!= null && t.PriceAvg1 != 0)) {
        TrendLine = new[] { ticks.First(), ticks.Last() };
        //TicksAvg1.Clear();
        //var avg = ticks.Count > maxTicks ? FXW.GetMinuteTicks(ticks.Select(t => new FXW.Tick(t.StartDate, t.PriceAvg1, t.PriceAvg1, false)), 1).Select(rateToTick) :
        //  ticks.Select(t => new FXW.Tick(t.StartDate, t.PriceAvg1, t.PriceAvg1, false)).Select(tickToTick);
        //UpdateTicks(TicksAvg1, avg);
      }
      if ((ticks[1].StartDate - ticks[0].StartDate).TotalSeconds < 59)
        ticks = ticks.GetMinuteTicks(1, true).OrderBars().ToList();
      var aw = plotter.ActualWidth;
      var period = Math.Max(1, Math.Ceiling(ticks.Count / aw).ToInt());
      if (ticks != null && ticks.Count > 0) {
        minuteTicks = ticks./*GetMinuteTicks(period, true).OrderBars().*/Select(rateToPoint).ToList();
        //minuteTicks.ToList().ForEach(t => t.Price = t.Price.Round(lastPrice.Digits - 1));
        TicksAvg2.Clear();
        if (ticks.Any(t => t.PriceAvg2 != 0)) {
          var avg = ticks.Select(t => new Rate(t.StartDate, t.PriceAvg2, t.PriceAvg2, t.IsHistory)).ToArray().GetMinuteTicks(period, true).Select(rateToTick);
          TicksAvg2.AddMany(avg);
        }
        TicksAvg3.Clear();
        if (ticks.Any(t => t.PriceAvg3 != 0)) {
          var avg = ticks.Select(t => new Rate(t.StartDate, t.PriceAvg3, t.PriceAvg3, t.IsHistory)).ToArray().GetMinuteTicks(period, true).Select(rateToTick);
          TicksAvg3.AddMany(avg);
        }
      } else {
        minuteTicks = voltsByTick.Select(v => new Rate(v.StartDate, v.AskMax, v.BidMin,false)).ToArray().GetMinuteTicks( period, true).Select(rateToPoint);
      }
      if (voltsByTick != null && voltsByTick.Count > 0) {
        var minuteVolts =
          voltsByTick.Select(v => new Rate(v.StartDate, v.Volts, v.Volts, false)).ToArray().GetMinuteTicks(period, true)
          .Select(t=>new ChartTick() { Price = Math.Round(t.PriceAvg,2), Time = t.StartDate }).OrderBy(t => t.Time).ToList();
        if (Volts == null) Volts = new ObservableCollection<ChartTick>();
        UpdateTicks( Volts,minuteVolts);

        if (voltsByTick.Any(v => v.VoltsPoly != 0)) {
          if (dsVoltsPoly == null) {
            dsVoltsPoly = new EnumerableDataSource<Volt>(VoltsPoly);
            dsVoltsPoly.SetYMapping(v => v.VoltsPoly);
            dsVoltsPoly.SetXMapping(ci => dateAxis.ConvertToDouble(ci.StartDate));
            innerPlotter.AddLineGraph(dsVoltsPoly, Colors.DarkOrange, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
          }
          var minuteVoltsPoly =
            voltsByTick.Select(v => new Rate(v.StartDate, v.VoltsPoly, v.VoltsPoly, false)).ToArray().GetMinuteTicks(period, true)
            .Select(t => new Volt() { StartDate = t.StartDate, VoltsPoly = t.PriceAvg });
          VoltsPoly.Clear();
          VoltsPoly.AddMany(minuteVoltsPoly);
        }
      }

      if (Ticks == null) {
        Ticks = new ObservableCollection<Point>();
        CreateCurrencyDataSource();
      }
      //Ticks.Clear();
      //minuteTicks.ToList().ForEach(mt => Ticks.Add(mt));
      UpdateTicks(Ticks, minuteTicks.OrderBy(t=>t.X).ToList());
      //plotter.FitToView();
      //System.Diagnostics.Debug.WriteLine("AddTicks:" + (DateTime.Now - d).TotalMilliseconds + " ms.");
      LineMax = voltageHigh;
      LineMaxAvg = priceMaxAvg;
      lineMaxAvg.ToolTip = priceMaxAvg;
      LineMinAvg = priceMinAvg;
      lineMinAvg.ToolTip = priceMinAvg;
      LineMin = voltageCurr;
      LineTimeMax = timeHigh;
      LineTimeMin = timeCurr;
      LineTimeAvg = timeLow;
      LineNetSell = netSell;
      LineNetBuy = netBuy;
      return Ticks.ToArray();
      Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
        (DispatcherOperationCallback)delegate(object o)
      {
        LineAvgAsk = priceAverageAskBid.OrderBy(p => p).Last();
        LineAvgBid = priceAverageAskBid.OrderBy(p => p).First();

        otherHLines.ForEach(l => { l.Remove(); });
        otherVLines.ForEach(l => { l.Remove(); });
        return null;
      });

      //if (lineAndTimes != null) {
      //  lineAndTimes.ForEach(lt => {
      //    var color = Colors.SpringGreen;
      //    var h = new HorizontalLine(lt.Value) { Stroke = new SolidColorBrush(color), StrokeThickness = 1 };
      //    otherHLines.Add(h);
      //    plotter.Children.Add(h);
      //    var v = new VerticalLine() { Value = dateAxis.ConvertToDouble(lt.Time), Stroke = new SolidColorBrush(color), StrokeThickness = 1, StrokeDashArray = { 2 } };
      //    otherVLines.Add(v);
      //    plotter.Children.Add(v);
      //  });
      //}
    }

    ChartTick[] minuteTicks = null;
    public ChartTick[] MinuteTicks {
      get { return minuteTicks; }
      set { minuteTicks = value; }
    }
    ChartTick[] GetMinuteTicks(HB.Tick[] fxTicks) {
      return (from t in fxTicks
                     group t by t.StartDate.Round() into tg
                     orderby tg.Key
                     select new ChartTick() { Price = tg.Average(t => (t.AskOpen + t.BidOpen) / 2), Time = tg.Key }).ToArray();
    }

    Volt[] GetMinuteVolts(IEnumerable<Volt> fxVolts) {
      return (from t in fxVolts
              group t by t.StartDate.AddSeconds(-t.StartDate.Second) into tg
              orderby tg.Key
              select new Volt() { Volts = tg.Average(t => t.Volts), StartDate = tg.Key }).ToArray();
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
