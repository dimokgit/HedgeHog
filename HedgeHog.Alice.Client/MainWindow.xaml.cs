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
using System.Data.Objects;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    public MainWindow() {
      InitializeComponent();
    }

    private System.Data.Objects.ObjectQuery<Models.TradingAccount> GetTradingAccountsQuery(Models.AliceEntities aliceEntities) {
      // Auto generated code

      System.Data.Objects.ObjectQuery<HedgeHog.Alice.Client.Models.TradingAccount> tradingAccountsQuery = aliceEntities.TradingAccounts;
      // Returns an ObjectQuery.
      return tradingAccountsQuery;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
      return;
      HedgeHog.Alice.Client.Models.AliceEntities aliceEntities = new HedgeHog.Alice.Client.Models.AliceEntities();
      // Load data into TradingAccounts. You can modify this code as needed.
      CollectionViewSource tradingAccountsViewSource = ((CollectionViewSource)(this.FindResource("tradingAccountsViewSource")));
      ObjectQuery<HedgeHog.Alice.Client.Models.TradingAccount> tradingAccountsQuery = this.GetTradingAccountsQuery(aliceEntities);
      tradingAccountsViewSource.Source = tradingAccountsQuery.Execute(MergeOption.AppendOnly);
    }

  }
}
