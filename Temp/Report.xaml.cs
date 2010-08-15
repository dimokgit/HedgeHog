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
using System.Data.Objects;
using X = System.Xml;
using XQ = System.Xml.Linq;
using System.Data;
using Order2GoAddIn;
using HedgeHog.Shared;
using Telerik.Windows.Controls.GridView.Settings;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for Report.xaml
  /// </summary>
  public partial class Report : Window {
    string fileName { get { return Environment.CurrentDirectory + "\\RadGridView.txt"; } }
    RadGridViewSettings settings = null;

    public Report() {
      InitializeComponent();
    }

    private ObjectQuery<Temp.ClosedTrade> GetClosedTradesQuery(Temp.AliceEntities aliceEntities) {
      // Auto generated code

      System.Data.Objects.ObjectQuery<Temp.ClosedTrade> closedTradesQuery = aliceEntities.ClosedTrades;
      // Returns an ObjectQuery.
      return closedTradesQuery;
    }

    private void WindowModel_Loaded(object sender, RoutedEventArgs e) {

      System.Windows.Data.CollectionViewSource tradeViewSource = ((System.Windows.Data.CollectionViewSource)(this.FindResource("tradeViewSource")));
      Temp.AliceEntities aliceEntities = new Temp.AliceEntities();
      var fw = new FXCoreWrapper();
      var accountId = "dturbo";
      var password = "1234";
      var isDemo = true;
      if (!fw.IsLoggedIn) fw.CoreFX.LogOn(accountId, password, isDemo);
      if( fw.IsLoggedIn)
      #region Load Report
        try {

          var trades = GetTradesFromReport(fw);

          // Load data by setting the CollectionViewSource.Source property:
          tradeViewSource.Source = trades;
        } catch (Exception exc) {
          MessageBox.Show(exc.ToString());
        }
      #endregion
      else
        tradeViewSource.Source = new Trade[] { };

      settings = new RadGridViewSettings(rgvReport);
      settings.LoadState(fileName);
    }

    private static List<Trade> GetTradesFromReport(FXCoreWrapper fw) {
      var xml = fw.GetReport(DateTime.Now.AddMonths(-1), DateTime.Now.Date);
      var xDoc = XQ.XElement.Parse(xml);
      var ss = xDoc.GetNamespaceOfPrefix("ss").NamespaceName;
      var worksheet = xDoc.Element(XQ.XName.Get("Worksheet", ss));
      var ticketNode = worksheet.Descendants(XQ.XName.Get("Data", ss)).Where(x => x.Value == "Ticket #");
      var ticketRow = ticketNode.First().Ancestors(XQ.XName.Get("Row", ss)).First();
      var row = ticketRow.NextNode as XQ.XElement;
      var trades = new List<Trade>();
      Func<int, XQ.XElement> getData = i => row.Descendants(XQ.XName.Get("Data", ss)).ElementAt(i);
      while (row.Elements().Count() == 13) {
        var ticket = getData(0);
        if (ticket == null) new Exception("Can't find [Ticket #] column");
        var pair = getData(1).Value;
        var volume = (int)double.Parse(getData(2).Value.Replace(",", ""));
        var timeOpen = DateTime.Parse(getData(3).Value);
        var isBuy = getData(4).Value == "";
        row = row.NextNode as XQ.XElement;
        var timeClose = DateTime.Parse(getData(3).Value);
        var grossPL = double.Parse(getData(6).Value);
        var commission = double.Parse(getData(7).Value);
        var rollover = double.Parse(getData(8).Value);
        trades.Add(new Trade() { Pair = pair, Buy = isBuy, Commission = commission + rollover, GrossPL = grossPL, Id = ticket.Value, IsBuy = isBuy, Lots = volume, Time = timeOpen, TimeClose = timeClose, OpenOrderID = "", OpenOrderReqID = "" });
        row = row.NextNode as XQ.XElement;
      }
      return trades;
    }

    private void WindowModel_Unloaded(object sender, RoutedEventArgs e) {
      settings.SaveState(fileName);
      Application.Current.Shutdown();
    }

  }
}
