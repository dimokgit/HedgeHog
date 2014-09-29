using System.Windows;
using Manheim.ViewModel;

namespace Manheim {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    public MainWindow() {
      InitializeComponent();
      Closing += (s, e) => ViewModelLocator.Cleanup();
    }
  }
}
