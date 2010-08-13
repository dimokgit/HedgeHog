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

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for Report.xaml
  /// </summary>
  public partial class Report : HedgeHog.Models.WindowModel {
    public ObjectSet<Models.ClosedTrade> ClosedTrades { get { return GlobalStorage.Context.ClosedTrades; } }
    public Report() {
      InitializeComponent();
    }

    private ObjectQuery<Models.ClosedTrade> GetClosedTradesQuery(Models.AliceEntities aliceEntities) {
      // Auto generated code

      System.Data.Objects.ObjectQuery<HedgeHog.Alice.Client.Models.ClosedTrade> closedTradesQuery = aliceEntities.ClosedTrades;
      // Returns an ObjectQuery.
      return closedTradesQuery;
    }

    private void WindowModel_Loaded(object sender, RoutedEventArgs e) {

      HedgeHog.Alice.Client.Models.AliceEntities aliceEntities = new HedgeHog.Alice.Client.Models.AliceEntities();
      // Load data into ClosedTrades. You can modify this code as needed.
      System.Windows.Data.CollectionViewSource closedTradesViewSource = ((System.Windows.Data.CollectionViewSource)(this.FindResource("closedTradesViewSource")));
      System.Data.Objects.ObjectQuery<HedgeHog.Alice.Client.Models.ClosedTrade> closedTradesQuery = this.GetClosedTradesQuery(aliceEntities);
      closedTradesViewSource.Source = closedTradesQuery.Execute(System.Data.Objects.MergeOption.AppendOnly);
    }
  }
}
