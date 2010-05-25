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
using FXW = Order2GoAddIn.FXCoreWrapper;

namespace Temp {
  /// <summary>
  /// Interaction logic for Window1.xaml
  /// </summary>
  public partial class Window1 : Window {
    FXW fw = new FXW(new Order2GoAddIn.CoreFX());
    public Window1() {
      InitializeComponent();
      if (!fw.CoreFX.LogOn("MICR485510001", "9071", true)) System.Diagnostics.Debug.Fail("Login");
      else {
        fw.PendingOrderCompleted += fw_PendingOrderCompleted;
        fw.TradeAdded += fw_TradeAdded;
      }
    }

    void fw_TradeAdded(HedgeHog.Shared.Trade trade) {
      ShowTables();
    }

    void fw_PendingOrderCompleted(object sender, Order2GoAddIn.PendingOrderEventArgs e) {
      AddLog(e.Order.ToString(Environment.NewLine));
      var order = fw.GetOrders("").FirstOrDefault(o => o.OrderID == e.Order.OrderId);
      if (order != null) AddLog(order.ToString(Environment.NewLine));
      ShowTables();
    }

    void AddLog(string text) {
      txtMessage.Text += text + Environment.NewLine;
    }

    void ClearLog() {
      txtMessage.Text = "";
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
