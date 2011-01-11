using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace HedgeHog.Alice.Server {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    public static Order2GoAddIn.CoreFX CoreFX = new Order2GoAddIn.CoreFX();
    public App() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize();
      this.DispatcherUnhandledException += new System.Windows.Threading.DispatcherUnhandledExceptionEventHandler(App_DispatcherUnhandledException);
    }

    void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
      MessageBox.Show(e.Exception + "");
      e.Handled = true;
    }
  }
}
