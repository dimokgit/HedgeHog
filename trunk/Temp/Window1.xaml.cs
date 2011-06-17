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
using HedgeHog;
using HedgeHog.Bars;
using HedgeHog.Rsi;
using HedgeHog.Alice.Store;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.Data.Objects.DataClasses;
using System.Collections.ObjectModel;
using Temp.Models;
using HedgeHog.Shared;
using HedgeHog.DB;

namespace Temp {
  /// <summary>
  /// Interaction logic for Window1.xaml
  /// </summary>
  public partial class Window1 : HedgeHog.Models.WindowModel {
    FXW fw { get { return Temp.App.fw; } }
    HedgeHog.Schedulers.ThreadScheduler statsScheduler;
    List<Rate> rates = new List<Rate>();
    ObservableCollection<string> instruments { get; set; }
    public ListCollectionView Instruments { get; set; }
    bool resetRates;
    //Corridors charter;
    int _daysBack;
    public bool NoGaps { get; set; }
    public int PeriodsBack {
      get { return _daysBack; }
      set { _daysBack = value; resetRates = true; }
    }
    double _rsiDays;
    public double RsiPeriods {
      get { return _rsiDays; }
      set { _rsiDays = value;  }
    }
    int _period = 1;
    public int Period {
      get { return _period; }
      set { _period = value; resetRates = true; }
    }
    public Window1() {
      Instruments = new ListCollectionView(instruments = new ObservableCollection<string>(new string[] { "" }));
      Instruments.CurrentChanged += Instruments_CurrentChanged;
      InitializeComponent();
      if (!fw.CoreFX.LogOn("MICR498120001", "6648", true)) System.Diagnostics.Debug.Fail("Login");
      else {
        fw.PendingOrderCompleted += fw_PendingOrderCompleted;
        fw.TradeAdded += fw_TradeAdded;
        statsScheduler = new HedgeHog.Schedulers.ThreadScheduler(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(0.1), () => Corridor(), (s, e) => AddLog(e.Exception.Message));
        fw.GetOffers().Select(o => o.Pair).ToList().ForEach(i => instruments.Add(i));
        Instruments.MoveCurrentToFirst();
        //charter = new Corridors();
        //charter.Show();
      }
      new HedgeHog.Statistics.StatisticsWindow().Show();
    }

    void Instruments_CurrentChanged(object sender, EventArgs e) {
      fw.Pair = (sender as ListCollectionView).CurrentItem + "";
      resetRates = true;
    }

    private bool _UseStDev;
    public bool UseStDev {
      get { return _UseStDev; }
      set {
        if (_UseStDev != value) {
          _UseStDev = value;
          RaisePropertyChangedCore();
        }
      }
    }

    private int _CorridorIterations;
    public int CorridorIterations {
      get { return _CorridorIterations; }
      set {
        if (_CorridorIterations != value) {
          _CorridorIterations = value;
          RaisePropertyChangedCore();
        }
      }
    }


    public void Corridor() {
      if (fw.Pair == "") return;
      var minutesBack = Period * PeriodsBack;
      lock (rates) {
        if (resetRates) {
          rates.Clear();
          resetRates = false;
        }
        ClearLog();
        AddLog("Rates.");
        fw.GetBars(fw.Pair, Period,0, DateTime.Now.AddMinutes(-minutesBack), DateTime.FromOADate(0), rates);
        AddLog("Scan with StDev:" + UseStDev + ". " + CorridorIterations+" iterations.");
        CorridorStatistics corridorStats = null;// rates.ScanCorridornesses(CorridorIterations, rates.GetCorridornesses(UseStDev, 30, 180), CorridorIterations, 0);
        var corridorMinutes = corridorStats.Periods * Period;
        AddLog("Chart Corridor["+corridorStats.Periods+"] minutes.");
        //new HedgeHog.Schedulers.Scheduler(charter.Dispatcher).Command = () =>
        //  charter.AddTicks(null, rates, null, 0, 0, 0, 0, 0, 0, rates.Last().StartDate.AddMinutes(-corridorStats.Periods*Period), DateTime.MinValue, new double[0]);
      }
    }
    public void MinuteRsi() {
      if (fw.Pair == "") return;
      var minutesBack = Period * PeriodsBack;
      lock (rates) {
        if (resetRates) {
          rates.Clear();
          resetRates = false;
        }
        ClearLog();
        AddLog("Rates.");
        fw.GetBars(fw.Pair, 1,0, DateTime.Now.AddMinutes(-minutesBack), DateTime.FromOADate(0), rates);
        AddLog("RSI.");
        rates.ToArray().Rsi((Period * RsiPeriods).ToInt(),true);
        var rsiStats = rates.RsiStats();
        var ratesToChart = rates.Where(t => t.PriceRsi.GetValueOrDefault(50) != 50).ToList();
        if (NoGaps) {
          var i = 0;
          var startDate = ratesToChart.Max(r => r.StartDate);
          ratesToChart.OrderBarsDescending().Skip(1).ToList().ForEach(r => r.StartDate = startDate.AddMinutes(-(++i)));
        }
        var rsi = ratesToChart.Select(t => new Volt() { Volts = t.PriceRsi.GetValueOrDefault(), StartDate = t.StartDate }).ToList();
        AddLog("Chart.");
        //new HedgeHog.Schedulers.Scheduler(charter.Dispatcher).Command = () =>
        //  charter.AddTicks(null, ratesToChart, rsi, rsiStats.Sell, rsiStats.Buy, rsiStats.SellAverage, rsiStats.BuyAverage, 0, 0, DateTime.MinValue, DateTime.MinValue, new double[0]);
        AddLog("Done.");
        return;
        var statName = "MinuteRsi";
        var context = new ForexEntities();
        var a = typeof(t_Stat).GetCustomAttributes(typeof(EdmEntityTypeAttribute), true).Cast<EdmEntityTypeAttribute>();
        context.ExecuteStoreCommand("DELETE " + a.First().Name + " WHERE Name={0}", statName);
        var stats = context.t_Stat;
        ratesToChart.ForEach(t =>
          stats.AddObject(new t_Stat() {
            Time = t.StartDate, Name = statName, Price = t.PriceAvg,
            Value1 = 0,
            Value2 = 0,
            Value3 = t.PriceRsi.Value
          }));
        context.SaveChanges();
      }
    }
    void fw_TradeAdded(object sender,TradeEventArgs e) {
      ShowTables();
    }

    void fw_PendingOrderCompleted(object sender, Order2GoAddIn.PendingOrderEventArgs e) {
      AddLog(e.Order.ToString(Environment.NewLine));
      var order = fw.GetOrders("").FirstOrDefault(o => o.OrderID == e.Order.OrderId);
      if (order != null) AddLog(order.ToString(Environment.NewLine));
      ShowTables();
    }

    void AddLog(string text) {
      try {
        Dispatcher.Invoke(new Action(() => {
          txtMessage.Text += text + Environment.NewLine;
        }));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }

    void ClearLog() {
      txtMessage.Dispatcher.Invoke(new Action(() => txtMessage.Text = ""));
    }

    private void Button_Click(object sender, RoutedEventArgs e) {
      var aid = fw.GetAccount().ID;
      var pair = "USD/JPY";
      var price = fw.GetPrice(pair).Average + fw.InPoints(pair, 2);
      var limit = price + fw.InPoints(pair, 15);
      var stop = price - fw.InPoints(pair, 25);
      fw.FixOrderOpenEntry(pair, true, 1000, price, stop, limit, "Dimok");
      price = fw.GetPrice(pair).Average - fw.InPoints(pair, 2);
      limit = price + fw.InPoints(pair, 15);
      stop = price - fw.InPoints(pair, 25);
      fw.FixOrderOpenEntry(pair, true, 1000, price, stop, limit, "Dimok");
    }

    private void ShowTrades_Click(object sender, RoutedEventArgs e) {
      ShowTables();
    }

    private void ShowTables() {
      dgOrders.ItemsSource = fw.GetOrders("");
      dgTrades.ItemsSource = fw.GetTrades("");
    }

    private void OpenTrade_Click(object sender, RoutedEventArgs e) {
      var pair = "EUR/JPY";
      var price = fw.GetPrice(pair).Average + fw.InPoints(pair, 2);
      var limit = price + fw.InPoints(pair, 15);
      var stop = price - fw.InPoints(pair, 25);
      fw.FixOrderOpen(pair, true, 1000, 0, 0, "Dimon");
      fw.FixOrderOpen(pair, true, 1000, 0, 0, "Dimon");
    }

    private void CloseTrade_Click(object sender, RoutedEventArgs e) {
      var tradeId = fw.GetTrades("").OrderBy(t => t.Time).Last().Id;
      fw.FixOrderClose(tradeId,"");
    }
  }
}
