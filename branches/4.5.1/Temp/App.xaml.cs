using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;

namespace Temp {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    static public CompositionContainer container;

    static FXW _fw;
    [Export]
    public static FXW fw {
      get {
        if (_fw == null) _fw = new FXW();
        return _fw;
      }
    }
    protected override void OnStartup(StartupEventArgs e) {
      base.OnStartup(e);
      return;
      AggregateCatalog catalog = new AggregateCatalog();
      catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetExecutingAssembly()));

      container = new CompositionContainer(catalog);
      CompositionBatch batch = new CompositionBatch();
      batch.AddExportedValue(container);
      container.Compose(batch);

    }

    protected override void OnExit(ExitEventArgs e) {
      //container.Dispose();

      base.OnExit(e);
    }
  }
}
