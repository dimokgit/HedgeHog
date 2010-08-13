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

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for Report.xaml
  /// </summary>
  public partial class Report : HedgeHog.Models.WindowModel {
    public ObjectSet<Models.ClosedTrade> ClosedTrades { get { return GlobalStorage.Context.ClosedTrades; } }
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
      var accountId = "dturbo";
      var fileName = @"E:\Data\Dev\combined_account_statement (2).xml";
      //DataSet ds = new Temp.Workbook();
      //ds.ReadXml(fileName);
      var fw = Temp.App.fw;
      if (!fw.IsLoggedIn) fw.CoreFX.LogOn(accountId, "1234", true);

      Temp.AliceEntities aliceEntities = new Temp.AliceEntities();
      var lastDate = aliceEntities.ClosedTrades.Max(ct => ct.TimeClose).Date;
      var xml = fw.GetReport(lastDate, DateTime.Now.Date);
      var xDoc = XQ.XElement.Parse(xml);
      var ss = xDoc.GetNamespaceOfPrefix("ss").NamespaceName;
      var worksheet = xDoc.Element(XQ.XName.Get("Worksheet", ss));
      var ticketNode = worksheet.Descendants(XQ.XName.Get("Data", ss)).Where(x => x.Value == "Ticket #");
      var ticketRow = ticketNode.First().Ancestors(XQ.XName.Get("Row", ss)).First();
      var row = ticketRow.NextNode as XQ.XElement;
      List<Trade> trades = new List<Trade>();
      Func<int,XQ.XElement> getData = i=>row.Descendants(XQ.XName.Get("Data", ss)).ElementAt(i);
      while (row.Elements().Count() == 13) {
        var ticket = getData(0);
        if (ticket == null) {
          MessageBox.Show("Can't find [Ticket #] column"); return;
        }
        var pair = getData(1).Value;
        var volume = (int)double.Parse(getData(2).Value.Replace(",",""));
        var timeOpen = DateTime.Parse(getData(3).Value);
        var isBuy = getData(4).Value == "";
        row = row.NextNode as XQ.XElement;
        var timeClose = DateTime.Parse(getData(3).Value);
        var grossPL = double.Parse(getData(6).Value);
        var commission = double.Parse(getData(7).Value);
        var rollover = double.Parse(getData(8).Value);
        trades.Add(new Trade() { Pair = pair, Buy = isBuy, Commission = commission + rollover, GrossPL = grossPL, Id = ticket.Value, IsBuy = isBuy, Lots = volume, Time = timeOpen, TimeClose = timeClose,OpenOrderID="",OpenOrderReqID="" });
        row = row.NextNode as XQ.XElement;
      }
      lastDate = trades.Min(t => t.TimeClose);
      var ctDelete = aliceEntities.ClosedTrades.Where(ct => ct.TimeClose >= lastDate);
      foreach (var ct in ctDelete)
          aliceEntities.ClosedTrades.DeleteObject(ct);
      aliceEntities.SaveChanges();

      foreach (var trade in trades)
        aliceEntities.ClosedTrades.AddObject(Temp.ClosedTrade.CreateClosedTrade(
          trade.Buy, trade.Close, trade.CloseInPips, trade.GrossPL, trade.Id, trade.IsBuy, trade.IsParsed, trade.Limit, trade.LimitAmount, trade.LimitInPips, trade.Lots, trade.Open, trade.OpenInPips, trade.OpenOrderID, trade.OpenOrderReqID, trade.Pair, trade.PipValue, trade.PL, trade.PointSize, trade.PointSizeFormat, trade.Remark + "", trade.Stop, trade.StopAmount, trade.StopInPips, trade.Time, trade.TimeClose, trade.UnKnown + "", accountId, trade.Commission));

      aliceEntities.SaveChanges();
      // Load data into ClosedTrades. You can modify this code as needed.
      System.Windows.Data.CollectionViewSource closedTradesViewSource = ((System.Windows.Data.CollectionViewSource)(this.FindResource("closedTradesViewSource")));
      System.Data.Objects.ObjectQuery<Temp.ClosedTrade> closedTradesQuery = this.GetClosedTradesQuery(aliceEntities);
      closedTradesViewSource.Source = closedTradesQuery.Execute(System.Data.Objects.MergeOption.AppendOnly);
    }

  }
}
