using System;
using System.ComponentModel;
using System.Composition.Hosting ;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using HedgeHog.Alice.Store;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Threading.Tasks;
using Owin;
using Microsoft.Owin.Cors;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using System.Reactive.Subjects;
using HedgeHog.Shared;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;

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

        string url = "http://+:80/";
        try {
          _webApp = WebApp.Start<StartUp>(url);
        } catch (Exception exc) {
          MessageBox.Show(exc + "");
        }

    }
    IDisposable _webApp;

    protected override void OnExit(ExitEventArgs e) {
      container.Dispose();

      base.OnExit(e);
    }
    #region SignalRSubject Subject
    public static Action ResetSignalR = ()=> { };
    public static int SignalRInterval = 5;
    static object _SignalRSubjectSubjectLocker = new object();
    static IDisposable _SignalRSubjectSubject;
    public static IDisposable SetSignalRSubjectSubject(Action action) {
      lock (_SignalRSubjectSubjectLocker)
        if (_SignalRSubjectSubject == null) {
          _SignalRSubjectSubject = 
            new[] { action }.ToObservable()
            .Select(a=>
              Observable
              .Interval(TimeSpan.FromSeconds(SignalRInterval))
              .StartWith(0)
              .Select(x=>a))
              .Switch()
              .Subscribe(a => action());
          ResetSignalR = () => {
            if (_SignalRSubjectSubject != null) {
              _SignalRSubjectSubject.Dispose();
              _SignalRSubjectSubject = null;
            }
            SetSignalRSubjectSubject(action);
          };
        }
      return _SignalRSubjectSubject;
    }
    #endregion
  }
  public class StartUp {
    public string Pair { get{return "usdjpy"; }}
    public void Configuration(IAppBuilder app) {

      // Static content
      var fileSystem = new PhysicalFileSystem("./www");
      var fsOptions = new FileServerOptions {
        EnableDirectoryBrowsing = true,
        FileSystem = fileSystem
      };
      app.UseFileServer(fsOptions);

      // SignalR timer
      var remoteControl = App.container.GetExport<RemoteControlModel>();
      app.UseCors(CorsOptions.AllowAll);
      app.Use((context, next) => {
        try {
          App.SetSignalRSubjectSubject(() => {
            var trmc = remoteControl.Value.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == Pair);
            if (trmc != null) {
              var rl = trmc.RateLast;
              if (rl != null) {
                try {
                  GlobalHost.ConnectionManager.GetHubContext<MyHub>()
                    .Clients.All.addMessage(trmc.ServerTime + "", rl.PriceAvg + "");
                } catch (Exception exc) {
                  GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
                }
              }
            }
          });

          if (context.Request.Path.Value.ToLower() == "/hello") {
            return context.Response.WriteAsync("privet:" + DateTimeOffset.Now);
          }
          var path = context.Request.Path.Value.ToLower().Substring(1);
          var rc = remoteControl.Value;
          var tm = rc.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == path);
          if (tm != null) {
            context.Response.ContentType = "image/png";
            return context.Response.WriteAsync(rc.GetCharter(tm).GetPng());
          }
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
        }
        return next();
      });
      // SignalR
      app.MapSignalR();
    }
  }
  public class PairHub : Hub {

  }
  public class MyHub : Hub {
    Lazy<RemoteControlModel> remoteControl;
    public MyHub() {
      remoteControl = App.container.GetExport<RemoteControlModel>();
    }
    public MyHub(TradingMacro tm) {
      Clients.All.newInfo(tm.RateLast.PriceAvg + "");
    }
    public void Send(string pair, string newInyterval) {
      try {
        App.SignalRInterval = int.Parse(newInyterval);
        App.ResetSignalR();
      } catch (Exception exc) {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exc));
      }
    }
    public void ResetPlotter(string pair) {
      var tm = GetTradingMacro(pair);
      if (tm != null)
        Clients.All.resetPlotter(tm.ServerTime + "", tm.RateLast.PriceAvg + "");
    }
    public void StopTrades(string pair) { SetCanTrade(pair, false); }
    public void StartTrades(string pair) { SetCanTrade(pair, true); }
    void SetCanTrade(string pair,bool canTrade) {
      var tm = GetTradingMacro(pair);
      if (tm != null)
        tm.SetCanTrade(canTrade);
    }

    private TradingMacro GetTradingMacro(string pair) {
      var rc = remoteControl.Value;
      var tm = rc.TradingMacrosCopy.FirstOrDefault(t => t.PairPlain == pair);
      return tm;
    }
  }
}

