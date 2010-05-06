using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace HedgeHog {
  public class ClosingBalanceChangedEventArgs : EventArgs {
    public int ClosingBalance { get; set; }
    public ClosingBalanceChangedEventArgs(int ClosingBalance) {
      this.ClosingBalance = ClosingBalance;
    }
  }
  public partial class App : Application {
    public event EventHandler<ClosingBalanceChangedEventArgs> ClosingBalanceChanged;
    public List<HedgeHogMainWindow> MainWindows = new List<HedgeHogMainWindow>();
    public Order2GoAddIn.CoreFX FXCM = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX Login(string accountNumber, string password, string serverUrl, bool isDemo) {
      if (!FXCM.IsLoggedIn)
        try {
          FXCM.LogOn(accountNumber, password, serverUrl, isDemo);
        } catch (Exception exc) {
          MessageBox.Show(exc.Message + Environment.StackTrace);
          return null;
        }
      return FXCM;
    }

    public void RaiseClosingalanceChanged(HedgeHogMainWindow Window, int ClosingBalance) {
      if (ClosingBalanceChanged != null)
        ClosingBalanceChanged(Window, new ClosingBalanceChangedEventArgs(ClosingBalance));
    }
    private void Application_Exit(object sender, ExitEventArgs e) {
      FXCM.Dispose();
    }
  }
}
