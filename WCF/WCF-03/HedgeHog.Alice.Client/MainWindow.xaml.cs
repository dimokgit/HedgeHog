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
using System.Windows.Controls.Primitives;

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
    }

    private void DataGrid_KeyDown(object sender, KeyEventArgs e) {

    }

    private void SlaveModelGrid_KeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Escape)
        (sender as Selector).SelectedIndex = -1;
    }

  }
}
