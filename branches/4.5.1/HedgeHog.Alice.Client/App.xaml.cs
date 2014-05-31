﻿using System;
using System.ComponentModel;
using System.Composition.Hosting ;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;
using System.Reflection;
using HedgeHog.Alice.Store;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    public static bool IsInDesignMode { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
  static public  CompositionContainer container;
  static public List<Window> ChildWindows = new List<Window>();
    App() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize();
      DataFlowProcessors.Initialize();
      this.DispatcherUnhandledException += App_DispatcherUnhandledException;
      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
      TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e) {
      MessageBox.Show(e.Exception.ToString());
    }

    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      MessageBox.Show(((Exception)e.ExceptionObject).ToString());
    }

    public ResourceDictionary Resources
    {
        get { return base.Resources; }
        set { base.Resources = value; }
    }

    void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
      try {
          var markUpException = e.Exception as System.Windows.Markup.XamlParseException;
          if (markUpException != null)
          {
              var message = new Dictionary<string, string>();
              message.Add("mesasge", markUpException + "");
              //message.Add("file", ((e.Exception as System.Windows.Markup.XamlParseException)).BaseUri +"");
              //message.Add("line", ((e.Exception as System.Windows.Markup.XamlParseException)).LineNumber + "");
              var rtlException = markUpException.InnerException as System.Reflection.ReflectionTypeLoadException;
              if (rtlException!=null)
              {
                  rtlException.LoaderExceptions.ToList().Select((le, i) => { message.Add("Errors[" + i + "]", le.Message); return 0; }).ToArray();
              }
              MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, message.Select(kv => kv.Key + ":" + kv.Value)));
          }
          else
          {
              var mm = GetTraderModelBase();
              if (mm != null) mm.Log = e.Exception;
          }
      } catch (ObjectDisposedException) {
        MessageBox.Show(e.Exception + "");
      }
      if (!(e.Exception is System.Windows.Markup.XamlParseException))
        e.Handled = true;
    }

    public static TraderModelBase GetTraderModelBase() {
      return container.GetExportedValue<TraderModelBase>();
    }

    private void Application_Exit(object sender, ExitEventArgs e) {
      if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return;
      GlobalStorage.UseAliceContext(c => c.SaveChanges());
      if (GlobalStorage.IsLocalDB) {
        var Connection = GlobalStorage.UseAliceContext(c => c.Connection);
        var newName = Path.Combine(
          Path.GetDirectoryName(Connection.DataSource),
          Path.GetFileNameWithoutExtension(Connection.DataSource)
          ) + ".backup" + Path.GetExtension(Connection.DataSource);
        if (File.Exists(newName)) File.Delete(newName);
        File.Copy(Connection.DataSource, newName);
      }
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send("Shutdown");
    }

    //public static void Compose(this object subject) { new CompositionContainer(new DirectoryCatalog(@".\")).ComposeParts(subject); }
    protected override void OnStartup(StartupEventArgs e) {
      base.OnStartup(e);


      AggregateCatalog catalog = new AggregateCatalog();
      var dirCatalog = new DirectoryCatalog(@".\");
      // Add the EmailClient.Presentation assembly into the catalog
      catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetExecutingAssembly()));
      catalog.Catalogs.Add(dirCatalog);
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
