using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Research.DynamicDataDisplay;
using Microsoft.Research.DynamicDataDisplay.Charts;
using Microsoft.Research.DynamicDataDisplay.DataSources;
using System.Windows.Threading;
using HedgeHog.Shared;
using HedgeHog.DB;
using ReactiveUI;
using ReactiveUI.Xaml;
using System.Collections.Specialized;
using System.Reactive.Concurrency;

namespace HedgeHog {
  public class DatePoint{
    public DateTime X { get; set; }
    public double Y { get; set; }
  }
  /// <summary>
  /// Interaction logic for CharterSnapshotControl.xaml
  /// </summary>
  public partial class CharterSnapshotControl : Models.UserControlModel {
    #region Ctor
    public CharterSnapshotControl() {
      InitializeComponent();

      this.SizeChanged += (s, e) => FitToView();

      MessageBus.Current.Listen<HedgeHog.NewsCaster.NewsEvent>("Snapshot")
        .Throttle(1.FromSeconds(),DispatcherScheduler.Current)
        .Where(ne => VisualParent != null)
        .Subscribe(ne => {
          try {
            var monthOffset = 3;
            var dateStart = ne.Time.AddMonths(-monthOffset*2);
            var dateEnd = ne.Time.AddDays(-1);
            ReadNewsEventsHistoryFromDB(ne.Country, ne.Name, dateStart, dateEnd);
            NewsHistoryCurrent = NewsEventHistory.First();
            NewsEventCurrent = NewsEvents.FirstOrDefault(n => n.Country == ne.Country && n.Name == ne.Name);
            DateStart = NewsEventHistory.First().Time.DateTime;
            Show();
          } catch (Exception exc) { LogMessage.Send(exc); }
        });

      Pairs.AddRange(ForexStorage.UseForexContext(c => c.v_Pair.ToArray(), (c, ex) => LogMessage.Send(ex)));
      otherVLines.Changing.ObserveOnDispatcher()
        .Subscribe(ev => {
          switch (ev.Action) {
            case NotifyCollectionChangedAction.Reset:
            case NotifyCollectionChangedAction.Remove:
              try {
                ev.OldItems.Cast<IPlotterElement>().ToList().ForEach(vl => plotter.Children.Remove(vl));
              } catch (Exception exc) {
                LogMessage.Send(exc);
              }
              return;
            case NotifyCollectionChangedAction.Add:
              ev.NewItems.Cast<VerticalLine>().ForEach(vl => plotter.Children.Add(vl));
              return;
          }
        });
      DispatcherScheduler.Current.Schedule(1.FromSeconds(), () => {
        Event__News newsEventPrev = null;
        this.ObservableForProperty(me => me.NewsEventCurrent, ne => ne).Subscribe(ne => {
          try {
            if (ne == null || CompareNewsEvents(ne, newsEventPrev)) return;
            newsEventPrev = ne;
            DateStart = ne.Time.DateTime;
            Show();
            var monthOffset = 3;
            var dateStart = ne.Time.AddMonths(-monthOffset);
            var dateEnd = ne.Time.AddMonths(monthOffset);
            ReadNewsEventsHistoryFromDB(ne.Country, ne.Name, dateStart, dateEnd);
            NewsHistoryCurrent = NewsEventHistory.FirstOrDefault(n =>
              n.Country == NewsEventCurrent.Country && n.Name == NewsHistoryCurrent.Name && n.Time == NewsEventCurrent.Time);
          } catch (Exception ex) { LogMessage.Send(ex); }
        }, ex => LogMessage.Send(ex), () => LogMessage.Send("Done with NewsEventCurrent."));

        this.ObservableForProperty(me => me.NewsHistoryCurrent, nh => nh).Subscribe(nh => {
          try {
            if (nh == null || CompareNewsEvents(NewsEventCurrent, nh)) return;
            DateStart = nh.Time.DateTime;
            Show();
            var newsEvent = NewsEvents.SingleOrDefault(ne => ne.Country == nh.Country && ne.Name == nh.Name);
            if (newsEvent != null)
              NewsEventCurrent = newsEventPrev = newsEvent;
          } catch (Exception ex) { LogMessage.Send(ex); }
        });

        this.ObservableForProperty(me => me.PairCurrent).Subscribe(oc => Show());
      });
    }
    delegate int NewsEventComparisonDelegate(Event__News left,Event__News right);
    NewsEventComparisonDelegate _newsEventComparicon = delegate(Event__News left, Event__News rigt) {
      return left.Time.CompareTo(rigt.Time);
    };
    private void ReadNewsEventsHistoryFromDB(string country,string name, DateTimeOffset dateStart, DateTimeOffset dateEnd) {
      var newsHistory = ForexStorage.UseForexContext(c =>
        c.Event__News.Where(dbEv => dbEv.Name == name && dbEv.Country == country &&
          dbEv.Time >= dateStart && dbEv.Time <= dateEnd)
        .OrderByDescending(db => db.Time).ToArray()
      );
      NewsEventHistory.Clear();
      NewsEventHistory.AddRange(newsHistory);
      NewsEventHistory.Sort(new Comparison<Event__News>(_newsEventComparicon));
    }
    #endregion

    #region NewsHistoryCurrent
    private Event__News _NewsHistoryCurrent;
    public Event__News NewsHistoryCurrent {
      get { return _NewsHistoryCurrent; }
      set {
        if (_NewsHistoryCurrent != value) {
          _NewsHistoryCurrent = value;
          OnPropertyChanged("NewsHistoryCurrent");
        }
      }
    }

    #endregion
    ReactiveList<Event__News> _newsEventHistory = new ReactiveList<Event__News>();
    public ReactiveList<Event__News> NewsEventHistory {
      get { return _newsEventHistory; }
    }

    #region Properties

    #region BarPeriod
    private int _BarPeriod = 1;
    public int BarPeriod {
      get { return _BarPeriod; }
      set {
        if (_BarPeriod != value) {
          _BarPeriod = value;
          OnPropertyChanged("BarPeriod");
        }
      }
    }

    #endregion

    #region DateStart
    private DateTime? _DateStart;
    public DateTime? DateStart {
      get { return _DateStart; }
      set {
        if (_DateStart != value) {
          _DateStart = value;
          OnPropertyChanged("DateStart");
        }
      }
    }
    #endregion

    #region DateLengthText
    private string _DateLengthText;
    public string DateLengthText {
      get { return _DateLengthText; }
      set {
        if (_DateLengthText != value) {
          _DateLengthText = value;
          DateLength = DateLengthText.Evaluate<int>();
          OnPropertyChanged("DateLengthText");
        }
      }
    }

    #endregion

    #region DateLength
    private int _DateLength = 1440;
    public int DateLength {
      get { return _DateLength; }
      set {
        if (_DateLength != value) {
          _DateLength = value;
          OnPropertyChanged("DateLength");
        }
      }
    }

    #endregion

    #region IsContinuous
    private bool _IsContinuous;
    public bool IsContinuous {
      get { return _IsContinuous; }
      set {
        if (_IsContinuous != value) {
          _IsContinuous = value;
          OnPropertyChanged("IsContinuous");
          otherVLines.Clear();
          if(DateStart.HasValue) Show();
        }
      }
    }

    #endregion

    ReactiveList<Event__News> _NewsEvents = new ReactiveList<Event__News>();
    public ReactiveList<Event__News> NewsEvents {
      get { return _NewsEvents; }
    }

    ReactiveList<v_Pair> _pairs = new ReactiveList<v_Pair>();
    public ReactiveList<v_Pair> Pairs {
      get { return _pairs; }
    }

    #region PairCurrent
    private string _PairCurrent;
    public string PairCurrent {
      get { return _PairCurrent; }
      set {
        if (_PairCurrent != value) {
          _PairCurrent = value;
          OnPropertyChanged("PairCurrent");
        }
      }
    }
    #endregion

    #region NewsEventCurrent
    private Event__News _NewsEventCurrent;
    public Event__News NewsEventCurrent {
      get { return _NewsEventCurrent; }
      set {
        if (_NewsEventCurrent != value) {
          _NewsEventCurrent = value;
          OnPropertyChanged("NewsEventCurrent");
        }
      }
    }
    #endregion

    static LambdaComparer<Event__News> _newsEventComparer = null;
    static CharterSnapshotControl() {
      _newsEventComparer = new LambdaComparer<Event__News>((l, r) => CompareNewsEvents(l, r));
    }

    #region Show Command
    ICommand _ShowCommand;
    public ICommand ShowCommand {
      get {
        if (_ShowCommand == null) {
          _ShowCommand = new GalaSoft.MvvmLight.Command.RelayCommand(Show, () => true);
        }

        return _ShowCommand;
      }
    }
    void Show() { Show(null); }
    void Show(DateTime? dateStart) {
      var ticks = ForexStorage.UseForexContext(c => {
        var period = BarPeriod;
        var pair = PairCurrent;
        if (string.IsNullOrWhiteSpace(PairCurrent)) throw new Exception("Piar is empty.");
        var q = c.t_Bar.Where(b => b.Period == period && b.Pair == pair && b.StartDateLocal < DateStart.Value).OrderByDescending(b => b.StartDate).Take(DateLength / 2);
        if( !dateStart.HasValue)dateStart = q.Min(b => b.StartDateLocal);
        return c.t_Bar.Where(b => b.Period == period && b.Pair == pair && b.StartDateLocal >= dateStart)
          .Take(DateLength).OrderBy(b => b.StartDate)
          .Select(b => new DatePoint() { X = b.StartDateLocal.Value, Y = (b.AskClose + b.AskOpen) / 2 })
          .ToArray();
      }, (c, e) => LogMessage.Send(e));
      AddTicks(ticks);
      var newsStart = ticks[0].X;
      var newsEnd = ticks.Last().X;
      var news = ForexStorage.UseForexContext(
        c => c.Event__News.Where(ne => ne.Time >= newsStart && ne.Time <= newsEnd).ToArray(),
        (c, exc) => LogMessage.Send(exc));
      NewsEvents.Except(news, _newsEventComparer).ToList().ForEach(ne => NewsEvents.Remove(ne));
      NewsEvents.AddRange(news.Except(NewsEvents, _newsEventComparer));
      NewsEvents.Sort(new Comparison<Event__News>(_newsEventComparicon));
      //NewsEventCurrent = NewsEvents.FirstOrDefault(CompareNewsEvents);
      DrawVertivalLines(new DateTime[0]);
      DrawVertivalLines(news.Select(ne => ne.Time.DateTime).ToArray());
    }
    #endregion

    #region Plotter related
    public bool IsPlotterInitialised { get; set; }

    static Color priceLineGraphColorAsk = Colors.Maroon;
    static Color priceLineGraphColorBid = Colors.Navy;

    List<DateTime> animatedTimeX = new List<DateTime>();
    List<DateTime> animatedTime0X = new List<DateTime>();
    List<double> animatedPrice1Y = new List<double>();

    List<double> animatedPriceBidY = new List<double>();
    EnumerableDataSource<double> animatedDataSourceBid = null;

    List<double> animatedPriceY = new List<double>();
    EnumerableDataSource<double> animatedDataSource = null;
    private bool inRendering;

    public LineGraph PriceLineGraph { get; set; }
    public LineGraph PriceLineGraphBid { get; set; }
    #endregion

    #endregion

    #region Draw 
    ReactiveList<VerticalLine> otherVLines = new ReactiveList<VerticalLine>();
    public void DrawVertivalLines(IList<DateTime> times) {
      var times0 = times.Select(t => dateAxis.ConvertToDouble(GetPriceStartDateContinuous(t))).ToArray();
      var timeSelectedDouble = dateAxis.ConvertToDouble(GetPriceStartDateContinuous(DateStart.GetValueOrDefault()));
      var newLines = times0.Except(otherVLines.Select(vl => vl.Value)).ToArray();
      var startDateDouble = dateAxis.ConvertToDouble(animatedTimeX[0]);
      var endDateDouble = dateAxis.ConvertToDouble(animatedTimeX.Last());
      newLines.Where(nl => nl.Between(startDateDouble,endDateDouble)).ForEach(nl =>
        otherVLines.Add(new VerticalLine() { Value = nl, StrokeDashArray = { 2 }, Stroke = new SolidColorBrush(Colors.MediumVioletRed), StrokeThickness = 1 })
      );

      otherVLines.Where(vl => !vl.Value.Between(startDateDouble,endDateDouble)).ToList().ForEach(vl => otherVLines.Remove(vl));

      var lines = otherVLines.Select(t => new { l = t, d = t.Value.Abs(timeSelectedDouble) }).OrderBy(t => t.d).ToArray();
      lines.Where(l => l.d == 0).Take(1).ForEach(l => l.l.StrokeThickness = 2);
      lines.Where(l => l.d != 0).ForEach(l => l.l.StrokeThickness = 1);
    }

    public void AddTicks(IList<DatePoint> ticks) {
      if (inRendering) return;
      var barsPeriod = (ticks[0].X - ticks[1].X).Duration().TotalMinutes.ToInt();

      ReAdjustXY(animatedTimeX, animatedPriceY, ticks.Count());
      ReAdjustXY(animatedTime0X, ticks.Count());
      ReAdjustXY(animatedPriceBidY, ticks.Count());
      ReAdjustXY(animatedPrice1Y, ticks.Count());

      var startDate = ticks[0].X;
      Enumerable.Range(0, ticks.Count).ForEach(i => {
        animatedPriceY[i] = ticks[i].Y;
        animatedPriceBidY[i] = ticks[i].Y;
        animatedPrice1Y[i] = ticks[i].Y;
        animatedTime0X[i] = ticks[i].X;
        animatedTimeX[i] = IsContinuous ? startDate.AddMinutes(i) : animatedTime0X[i];
      });

      CreateCurrencyDataSource();

      Action a = () => {
        try {
          animatedDataSource.RaiseDataChanged();
        } catch (Exception exc) { LogMessage.Send(exc); }

      };

      DispatcherScheduler.Current.Schedule(0.5.FromSeconds(), () => FitToView());
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
    #endregion

    #region Methods

    private bool CompareNewsEvents(Event__News neLeft) { return CompareNewsEvents(neLeft, NewsEventCurrent); }
    private static bool CompareNewsEvents(Event__News neLeft,Event__News neRight) {
      return neLeft!=null && neRight!=null &&
        neLeft.Name == neRight.Name &&
        neLeft.Time == neRight.Time &&
        neLeft.Country == neRight.Country;
    }

    private void CreateCurrencyDataSource() {
      if (IsPlotterInitialised) return;
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

      #region Plotter
      IsPlotterInitialised = true;
      plotter.Children.RemoveAt(0);
      var verticalAxis = plotter.Children.OfType<VerticalAxis>().First();
      verticalAxis.FontSize = 10;
      verticalAxis.ShowMinorTicks = false;
      innerPlotter.Children.Remove(innerPlotter.Children.OfType<VerticalAxis>().Single());
      plotter.Children.OfType<VerticalAxis>().First().Placement = AxisPlacement.Right;
      #endregion

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

        if (true) {
          animatedDataSourceBid = new EnumerableDataSource<double>(animatedPriceBidY);
          animatedDataSourceBid.SetYMapping(y => y);
          this.PriceLineGraphBid = plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSourceBid), priceLineGraphColorBid, 1, "");
          this.PriceLineGraphBid.Description.LegendItem.Visibility = Visibility.Collapsed;
        }
      }
      //var ticksLineGraph = plotter.AddLineGraph(Ticks.AsDataSource(), Colors.Black, 1, "1M").Description.LegendItem.Visibility = Visibility.Collapsed;
      #endregion

      plotter.KeyDown += new KeyEventHandler(plotter_KeyDown);




    }
    DateTime GetPriceStartDateContinuous(DateTime startDate) {
      var x = animatedTime0X.OrderBy(d => (d - startDate).Duration()).First();
      return animatedTimeX[animatedTime0X.IndexOf(x)];
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

    private void ReAdjustXY<T>(List<T> X, int count) {
      while (X.Count > count) {
        X.RemoveAt(0);
      }
      while (X.Count < count) {
        X.Add(default(T));
      }
    }

    DifferenceIn? GetDifference(TimeSpan span) {
      span = span.Duration();

      DifferenceIn? diff;
      if (span.Days > 365)
        diff = DifferenceIn.Year;
      else if (span.Days > 30)
        diff = DifferenceIn.Month;
      else if (span.Days > 3)
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

    public void FitToView() {
      plotter.Dispatcher.BeginInvoke(new Action(() => {
        try {
          plotter.Viewport.ClearValue(Viewport2D.VisibleProperty);
          plotter.Viewport.CoerceValue(Viewport2D.VisibleProperty);

          plotter.FitToView();
        } catch (InvalidOperationException) {
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(new LogMessage(exc));
        }
      }), DispatcherPriority.DataBind);
    }
    void plotter_KeyDown(object sender, KeyEventArgs e) {
      if (!new[] { Key.Oem2, Key.OemComma, Key.OemPeriod, Key.P }.Contains(e.Key))
        e.Handled = true;
      try {
        switch (e.Key) {
          case Key.H:
            try { FitToView(); } catch { }
            break;
          default: break;
        }
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }
    #endregion
  }
}