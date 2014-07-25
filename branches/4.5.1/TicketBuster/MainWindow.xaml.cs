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
using System.Diagnostics;
namespace TicketBuster {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    public MainWindow() {
      InitializeComponent();
      var vm = new MainWIndowViewModel("Model1");
      vm.ObservableForProperty(_ => _.Alert)
        .Where(m => m.Value != null)
        .ObserveOnDispatcher()
        .Subscribe(o => {
          try {
            MessageBox.Show(App.Current.MainWindow, o.Value);
          } catch (Exception exc) {
            if (Debugger.IsAttached)
              Debug.Fail(exc + "");
          }
        }, exc => {
          if (Debugger.IsAttached)
            Debug.Fail(exc + "");
        });
      DataContext = vm;
    }
  }
}
