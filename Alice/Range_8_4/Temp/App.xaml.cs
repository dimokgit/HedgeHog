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


      AggregateCatalog catalog = new AggregateCatalog();
      // Add the EmailClient.Presentation assembly into the catalog
      catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetExecutingAssembly()));
      // Add the EmailClient.Applications assembly into the catalog
      //catalog.Catalogs.Add(new AssemblyCatalog(typeof(IMainModel).Assembly));

      container = new CompositionContainer(catalog);
      CompositionBatch batch = new CompositionBatch();
      batch.AddExportedValue(container);
      container.Compose(batch);

      //ApplicationController controller = container.GetExportedValue<ApplicationController>();
      //controller.Initialize();
      //controller.Run();
    }

    protected override void OnExit(ExitEventArgs e) {
      container.Dispose();

      base.OnExit(e);
    }
  }
}
