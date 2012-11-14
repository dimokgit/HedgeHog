﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;
using HedgeHog.Alice.Store;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    public static bool IsInDesignMode { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
  static public CompositionContainer container;
  static public List<Window> ChildWindows = new List<Window>();
    App() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize();
      DataFlowProcessors.Initialize();
      this.DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    public ResourceDictionary Resources
    {
        get { return base.Resources; }
        set { base.Resources = value; }
    }

    void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
      try {
        var mm = container.GetExportedValue<TraderModelBase>();
        if (mm != null) mm.Log = e.Exception;
      } catch (ObjectDisposedException) {
        MessageBox.Show(e.Exception + "");
      }
      if (!(e.Exception is System.Windows.Markup.XamlParseException))
        e.Handled = true;
    }

    private void Application_Exit(object sender, ExitEventArgs e) {
      if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return;
      GlobalStorage.UseAliceContext(c => c.SaveChanges());
      var Connection = GlobalStorage.UseAliceContext(c => c.Connection);
      var newName = Path.Combine(
        Path.GetDirectoryName(Connection.DataSource),
        Path.GetFileNameWithoutExtension(Connection.DataSource)
        ) + ".backup" + Path.GetExtension(Connection.DataSource);
      if (File.Exists(newName)) File.Delete(newName);
      File.Copy(Connection.DataSource, newName);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send("Shutdown");
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
