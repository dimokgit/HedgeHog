using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Windows;

namespace HedgeHog.Alice.Server {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    static Order2GoAddIn.CoreFX _CoreFX;
    public static Order2GoAddIn.CoreFX CoreFX {
      get {
        if (_CoreFX == null) _CoreFX = new Order2GoAddIn.CoreFX();
        return _CoreFX;
      }
    }
    ServiceHost _priceServiceHost;
    public App() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize();
      _priceServiceHost = new ServiceHost(new PriceService());
      _priceServiceHost.Open();
    }

    void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
      MessageBox.Show(e.Exception + "");
      e.Handled = true;
    }

    private void Application_Exit(object sender, ExitEventArgs e) {
      _priceServiceHost.Close();
      CoreFX.Dispose();
    }
  }
}
