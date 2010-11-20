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
using System.Windows.Controls.Primitives;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for OrderTemplates.xaml
  /// </summary>
  public partial class OrderTemplatesView : UserControl {
    public OrderTemplatesView() {
      InitializeComponent();
      if (App.IsInDesignMode)
        DataContext = new OrderTemplatesModel();
      else
        DataContext = App.container.GetExportedValue<OrderTemplatesModel>();
    }

    private void DataGrid_KeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Escape)
        (sender as Selector).SelectedIndex = -1;
    }
  }
}
