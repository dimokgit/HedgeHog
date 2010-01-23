using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace HedgeHog {
    public partial class App : Application {
      public List<HedgeHogMainWindow> MainWindows = new List<HedgeHogMainWindow>();
      public Order2GoAddIn.CoreFX FXCM = new Order2GoAddIn.CoreFX();
      public Order2GoAddIn.CoreFX Login(string accountNumber, string password, string serverUrl,bool isDemo) {
        if (!FXCM.IsLoggedIn)
          try {
            FXCM.LogOn(accountNumber, password, serverUrl, isDemo);
          } catch (Exception exc) {
            MessageBox.Show(exc.Message + Environment.StackTrace);
            return null;
          }
        return FXCM;
      }

      private void Application_Exit(object sender, ExitEventArgs e) {
        FXCM.Dispose();
      }
    }
}
