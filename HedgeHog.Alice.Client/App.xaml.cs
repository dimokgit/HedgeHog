﻿using System;
using System.ComponentModel;
using System.Composition.Hosting;
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
using ReactiveUI;
using HedgeHog.Shared.Messages;
using static HedgeHog.Core.JsonExtensions;
using System.Diagnostics;
using System.Windows.Threading;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App :Application {
    public static bool IsInDesignMode { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    static public CompositionContainer container;
    static public List<Window> ChildWindows = new List<Window>();
    static public List<string> WwwMessageWarning = new List<string>();
    static App(){
      MessageBus.Current.Listen<LogMessage>().Subscribe(lm => AsyncMessageBox.BeginMessageBoxAsync(lm.Exception + ""));
      ReactiveUI.MessageBus.Current.Listen<WwwWarningMessage>().Subscribe(wm => WwwMessageWarning.Add(wm.Message));
    }

    App() {
      DataFlowProcessors.Initialize();
      this.DispatcherUnhandledException += App_DispatcherUnhandledException;
      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
      TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

      var me = Process.GetCurrentProcess();
      Process[] Runs() => Process.GetProcessesByName(me.ProcessName).Where(p => p.MainModule.FileName == me.MainModule.FileName).ToArray();
      Observable.Interval(TimeSpan.FromSeconds(1)).ObserveOnDispatcher()
      .SkipWhile(_ => Runs().Length > 1)
      .Timeout(DateTimeOffset.Now.AddSeconds(60))
      .Catch<long, Exception>(exc => {
        AsyncMessageBox.BeginMessageBoxAsync($"{me.MainModule.FileName} is already running.");
        return Observable.Return(-1L);
      })
      .Take(1)
      .Where(l => l >= 0)
      .Subscribe(_ => {
        try {
          var trader = container.GetExportedValue<TraderModel>();
          if(trader.IpPort > 0) {
            trader.IpPortActual = trader.IpPort;
            var protocol = trader.IpPort == 443 ? "https" : "http";
            while(true) {
              string url = $"{protocol}://+:{trader.IpPortActual}/";
              LogMessage.Send(new { trying = new { url } });
              try {
                _webApp = WebApp.Start<StartUp>(url);
                LogMessage.Send(new { url });
                break;
              } catch(Exception exc) {
                var he = exc.InnerException as System.Net.HttpListenerException;
                if(exc == null || he.ErrorCode != 183) {
                  LogMessage.Send(new Exception(new { url } + "", exc));
                  return;
                }
                LogMessage.Send(new { port = trader.IpPortActual, isBusy = true } + "");
                trader.IpPortActual++;
                if(trader.IpPortActual > trader.IpPort + 10) {
                  LogMessage.Send(new { trader.IpPortActual, trader.IpPort, Limit = 10 } + "");
                  return;
                }
              }
            }
          }
        } catch(CompositionException cex) {
          LogMessage.Send(cex);
          AsyncMessageBox.BeginMessageBoxAsync(cex + "");
        } catch(Exception exc) {
          AsyncMessageBox.BeginMessageBoxAsync(exc + "");
        }
      });
    }

    void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e) {
      try {
        LogMessage.Send(e.Exception);
      } catch { }
      if(!IsHandled(e.Exception))
        AsyncMessageBox.BeginMessageBoxAsync(e.Exception.ToString());
    }

    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      var exc = (Exception)e.ExceptionObject;
      try {
        LogMessage.Send(exc);
      } catch {
      }
      FileLogger.LogToFile(exc);
      AsyncMessageBox.BeginMessageBoxAsync(string.Join("\n", ((Exception)e.ExceptionObject).Inners().Reverse().Select(ex => ex + "")));
    }
    bool IsHandled(AggregateException e) {
      return e != null && e.Flatten().InnerException.InnerException is System.Net.HttpListenerException;
    }
    public new ResourceDictionary Resources {
      get { return base.Resources; }
      set { base.Resources = value; }
    }

    void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
      try {
        var markUpException = e.Exception as System.Windows.Markup.XamlParseException;
        if(markUpException != null) {
          var message = new Dictionary<string, string>();
          message.Add("mesasge", markUpException + "");
          //message.Add("file", ((e.Exception as System.Windows.Markup.XamlParseException)).BaseUri +"");
          //message.Add("line", ((e.Exception as System.Windows.Markup.XamlParseException)).LineNumber + "");
          var rtlException = markUpException.InnerException as System.Reflection.ReflectionTypeLoadException;
          if(rtlException != null) {
            rtlException.LoaderExceptions.ToList().Select((le, i) => { message.Add("Errors[" + i + "]", le.Message); return 0; }).ToArray();
          }
          AsyncMessageBox.BeginMessageBoxAsync(string.Join(Environment.NewLine + Environment.NewLine, message.Select(kv => kv.Key + ":" + kv.Value)));
        } else {
          FileLogger.LogToFile("App_DispatcherUnhandledException");
          FileLogger.LogToFile(e.Exception);
          //AsyncMessageBox.BeginMessageBoxAsync(e.Exception + "");
        }
      } catch(ObjectDisposedException) {
        AsyncMessageBox.BeginMessageBoxAsync(e.Exception + "");
      }
      if(!(e.Exception is System.Windows.Markup.XamlParseException))
        e.Handled = true;
    }

    public static TraderModelBase GetTraderModelBase() {
      return container.GetExportedValue<TraderModelBase>();
    }

    private void Application_Exit(object sender, ExitEventArgs e) {
      if(GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic)
        return;
      /*
      if(GlobalStorage.IsLocalDB) {
        var Connection = GlobalStorage.UseAliceContext(c => c.Connection);
        var newName = Path.Combine(
          Path.GetDirectoryName(Connection.DataSource),
          Path.GetFileNameWithoutExtension(Connection.DataSource)
          ) + ".backup" + Path.GetExtension(Connection.DataSource);
        if(File.Exists(newName))
          File.Delete(newName);
        File.Copy(Connection.DataSource, newName);
      }
      */
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
    IDisposable _webApp;

    protected override void OnExit(ExitEventArgs e) {
      MessageBus.Current.SendMessage(new AppExitMessage());
      container.Dispose();
      base.OnExit(e);
    }
    #region SignalRSubject Subject
    public static Action ResetSignalR = () => { };
    public static int SignalRInterval = 1;
    static object _SignalRSubjectSubjectLocker = new object();
    static IDisposable _SignalRSubjectSubject;
    public static IDisposable SetSignalRSubjectSubject(Action action) {
      lock(_SignalRSubjectSubjectLocker)
        if(_SignalRSubjectSubject == null) {
          _SignalRSubjectSubject =
            new[] { action }.ToObservable()
            .Select(a =>
              Observable
              .Interval(TimeSpan.FromSeconds(SignalRInterval))
              .StartWith(0)
              .Select(x => a))
              .Switch()
              .Subscribe(a => action());
          ResetSignalR = () => {
            if(_SignalRSubjectSubject != null) {
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
}

