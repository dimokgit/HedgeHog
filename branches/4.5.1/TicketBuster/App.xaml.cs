using HedgeHog.Schedulers;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace TicketBuster {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    public App() {
      TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
      Dispatcher.UnhandledException += Dispatcher_UnhandledException;
    }

    void Dispatcher_UnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
      throw new NotImplementedException();
    }

    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      throw new NotImplementedException();
    }

    void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e) {
      throw new NotImplementedException();
    }
  }
}
