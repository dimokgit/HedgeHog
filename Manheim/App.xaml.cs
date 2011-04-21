using System.Windows;
using GalaSoft.MvvmLight.Threading;
using System;

namespace Manheim {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    static App() {
      DispatcherHelper.Initialize();
    }

    private void Application_Startup(object sender, StartupEventArgs e) {
      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      MessageBox.Show((e.ExceptionObject as Exception) + "");
    }
  }
}
