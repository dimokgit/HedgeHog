using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    static App() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize();
    }

    private void Application_Exit(object sender, ExitEventArgs e) {
      if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return;
      var Connection = GlobalStorage.Context.Connection;
      var newName = Path.Combine(
        Path.GetDirectoryName(Connection.DataSource),
        Path.GetFileNameWithoutExtension(Connection.DataSource)
        ) + ".backup" + Path.GetExtension(Connection.DataSource);
      if (File.Exists(newName)) File.Delete(newName);
      File.Copy(Connection.DataSource, newName);

    }
  }
}
