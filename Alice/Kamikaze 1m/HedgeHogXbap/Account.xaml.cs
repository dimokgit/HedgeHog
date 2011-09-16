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

namespace HedgeHogXbap {
  /// <summary>
  /// Interaction logic for Page1.xaml
  /// </summary>
  public partial class Account : Page {
    public Account() {
      InitializeComponent();
    }

    private void button1_Click(object sender, RoutedEventArgs e) {
      textBox1.Text = new HedgeHogService.Service1Client().GetData(49);
    }
  }
}
