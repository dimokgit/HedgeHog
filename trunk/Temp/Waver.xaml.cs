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
using System.Windows.Shapes;
using System.ComponentModel.Composition;
using Order2GoAddIn;
using HedgeHog;
using HedgeHog.Bars;
using Telerik.Windows.Controls.Charting;
using System.Windows.Threading;
using HedgeHog.Models;
using System.Timers;
using System.Threading;
using System.Diagnostics;

namespace Temp {
  /// <summary>
  /// Interaction logic for Waver.xaml
  /// </summary>
  public partial class Waver : WindowModel {
    [Import]
    FXCoreWrapper fw { get; set; }
    private string[] _Pairs;
    public string[] Pairs {
      get { return _Pairs; }
      set {
        if (_Pairs != value) {
          _Pairs = value;
          RaisePropertyChangedCore("Pairs");
        }
      }
    }

    string _pair;

    public string Pair {
      get { return _pair; }
      set { _pair = value; }
    }

    int _MinutesBack = 60 * 3;
    public int MinutesBack {
      get { return _MinutesBack; }
      set { _MinutesBack = value; }
    }

    int _BarMinutesMax;

    public int BarMinutesMax {
      get { return _BarMinutesMax; }
      set { _BarMinutesMax = value; }
    }

    private bool _IsBusy;
    public bool IsBusy {
      get { return _IsBusy; }
      set {
        if (_IsBusy != value) {
          _IsBusy = value;
          RaisePropertyChangedCore("IsBusy");
          RaisePropertyChangedCore("IsNotBusy");
        }
      }
    }

    private double _BarHeightInPips;
    public double BarHeightInPips {
      get { return _BarHeightInPips; }
      set {
        if (_BarHeightInPips != value) {
          _BarHeightInPips = value;
          RaisePropertyChangedCore("BarHeightInPips");
        }
      }
    }

    private double _BarHeightSpeed;
    public double BarHeightSpeed {
      get { return _BarHeightSpeed; }
      set {
        if (_BarHeightSpeed != value) {
          _BarHeightSpeed = value;
          RaisePropertyChangedCore("BarHeightSpeed");
        }
      }
    }


    public bool IsNotBusy { get { return !IsBusy; } }

    List<double> barRatios = new List<double>();
    System.Threading.Timer timer;
    public Waver() {
      HedgeHog.MEF.Container.SatisfyImportsOnce(this);

      if (fw == null) fw = new FXCoreWrapper();
      fw.CoreFX.LoginError += exc => MessageBox.Show(exc + "");
      if (!fw.IsLoggedIn) fw.CoreFX.LogOn("MICR512463001", "6131", true);
      if (fw.IsLoggedIn) {
        Pairs = fw.CoreFX.Instruments;
        Dispatcher.BeginInvoke(new Action(() => {
          timer = new System.Threading.Timer((o) => FillRatios(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }));
      }

      InitializeComponent();
      Loaded += Waver_Loaded;
      Unloaded += new RoutedEventHandler(Waver_Unloaded);
      radChart1.DefaultView.ChartLegend.Visibility = System.Windows.Visibility.Collapsed;
      //radChart1.ItemsSource = barRatios;
    }

    void FillRatios() {
      try {
        IsBusy = true;
        var rates = fw.GetBarsBase(Pair, 1, DateTime.Now.AddMinutes(-MinutesBack), DateTime.FromOADate(0)).ToArray();
        var barHeights = new List<double>();
        List<double> ratios;
        var bars1 = GetBarsAndRatios(rates,BarMinutesMax, out ratios);
        var ratios1 = ratios.Select((r, i) => new { Index = i, Value = r }).ToList();
        var index = ratios1.Where(r => r.Value < 1).OrderBy(r => r.Index).First().Index;
        var barHeight = bars1[index];
        var sw = Stopwatch.StartNew();
        BarHeightInPips = fw.InPips(Pair, rates.GetWaveHeight(4, BarMinutesMax));
        BarHeightSpeed = sw.Elapsed.TotalMilliseconds.Round(0);

        var chA = radChart1.DefaultView.ChartArea;
        DataSeries ds = new DataSeries();
        Dispatcher.Invoke(new Action(() => {
          radChart1.DefaultView.ChartTitle.Content = Pair;
          chA.DataSeries.Clear();
          ratios1.ForEach(r => ds.Add(new Telerik.Windows.Controls.Charting.DataPoint(r.Value) {
            Label = (index == r.Index ? "X:" : "") + fw.InPips(Pair, bars1[r.Index]).ToString("n1")
          }));
          chA.DataSeries.Add(ds);
        }));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      } finally {
        IsBusy = false;
      }
      return;
      //var c = 2;
      //for (; c < bars.Count; c++)
      //  if (bars[c - 1] / bars[c - 2] < bars[c] / bars[c-1]) break;
      //return;
    }

    private static double[] GetBarsAndRatios(Rate[] rates,int barMax, out List<double> ratios) {
      var bars = new List<double>();
      ratios = new List<double>();
      for (var i = 1; i <= barMax; i++)
        bars.Add(rates.GetBarHeight(i));
      var b = 1;
      for (; b < bars.Count; b++)
        ratios.Add(bars[b] / bars[b - 1]);
      return bars.ToArray();
    }
    void Waver_Loaded(object sender, RoutedEventArgs e) {
    }
    void Waver_Unloaded(object sender, RoutedEventArgs e) {
      fw.LogOff();
      if (App.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
        App.Current.Shutdown();
    }

    private void Button_Click(object sender, RoutedEventArgs e) {

    }

    private void LoadRatios_Click(object sender, RoutedEventArgs e) {
      timer.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
      if (fw.IsLoggedIn && !string.IsNullOrWhiteSpace(Pair))
        new Thread(() => FillRatios()).Start();
    }
  }
}
