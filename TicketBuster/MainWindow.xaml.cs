using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Reactive.Linq;
using ReactiveUI;
namespace TicketBuster {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    public MainWindow() {
      InitializeComponent();
      var vm = new MainWIndowViewModel();
      vm.ObservableForProperty(_ => _.MessageBox2).ObserveOnDispatcher().Subscribe(o => MessageBox.Show(App.Current.MainWindow, o.Value));
      DataContext = vm;
    }
  }
}
