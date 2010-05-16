﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HedgeHog {
  public partial class App : Application {
    ServiceHost wcfTrader;
    public event EventHandler<ClosingBalanceChangedEventArgs> ClosingBalanceChanged;
    public List<HedgeHogMainWindow> MainWindows = new List<HedgeHogMainWindow>();
    public Order2GoAddIn.CoreFX FXCM = new Order2GoAddIn.CoreFX();
    public string WcfTraderAddress { get { return wcfTrader.BaseAddresses[0] + ""; } }

    public App() {
      ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
      Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
        try {
          HedgeHog.Wcf.Trader = (Wcf.ITraderServer)MainWindow;
        } catch (Exception exc) {
          MessageBox.Show(exc.Message + Environment.NewLine + exc.StackTrace);
        }
      }));
      try {
        var wcfPort = ConfigurationManager.AppSettings["wcfPort"];
        wcfTrader = new ServiceHost(typeof(HedgeHog.Alice.WCF.TraderService), new Uri("net.tcp://localhost:" + wcfPort + "/"));
        wcfTrader.Open();
      } catch (Exception exc) {
        MessageBox.Show(exc.Message + Environment.NewLine + exc.StackTrace);
        Shutdown(-1);
      }

    }
    public void RaiseClosingalanceChanged(HedgeHogMainWindow Window, int ClosingBalance) {
      if (ClosingBalanceChanged != null)
        ClosingBalanceChanged(Window, new ClosingBalanceChangedEventArgs(ClosingBalance));
    }
    private void Application_Exit(object sender, ExitEventArgs e) {
    }

    private void Application_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e) {
    }
  }
  public class ClosingBalanceChangedEventArgs : EventArgs {
    public int ClosingBalance { get; set; }
    public ClosingBalanceChangedEventArgs(int ClosingBalance) {
      this.ClosingBalance = ClosingBalance;
    }
  }
}
