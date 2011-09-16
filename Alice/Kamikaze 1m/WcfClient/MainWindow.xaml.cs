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

namespace WcfClient {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {


    public string TextFromWcf {
      get { return (string)GetValue(TextFromWcfProperty); }
      set { SetValue(TextFromWcfProperty, value); }
    }

    // Using a DependencyProperty as the backing store for TextFromWcf.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty TextFromWcfProperty =
        DependencyProperty.Register("TextFromWcf", typeof(string), typeof(MainWindow));

    public MainWindow() {
      InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e) {
      TextFromWcf = new WcfServiceReference.Service1Client().GetData(5) + "";
    }
  }
}
