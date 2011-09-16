using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using X = System.Xml;
using XQ = System.Xml.Linq;
using System.Data;
using Order2GoAddIn;
using HedgeHog.Shared;
using Telerik.Windows.Controls.GridView.Settings;
using System.ComponentModel.Composition;
using System.Threading;
using System.ComponentModel.Composition.Hosting;

namespace HedgeHog.Reports {
  /// <summary>
  /// Interaction logic for Report.xaml
  /// </summary>
  public partial class Report : Window {

    #region Properties
    FXCoreWrapper _fw;
    [Import]
    FXCoreWrapper fw {
      get { return _fw; }
      set { _fw = value; }
    }
    string fileName { get { return Environment.CurrentDirectory + "\\RadGridView.txt"; } }
    RadGridViewSettings settings = null;
    bool isMainWindow;
    #endregion

    #region Ctor
    public Report() {
      fw = new ComposablePartExportProvider().GetExport<FXCoreWrapper>().Value;
      //App.container.SatisfyImportsOnce(this);
      InitializeComponent();
      fw.CoreFX.LoginError += CoreFX_LoginError;
    }
    ~Report() {
      try {
        if (fw != null)
          fw.CoreFX.LoginError -= CoreFX_LoginError;
      } catch { }
    }
    #endregion

    #region Event Handlers
    void CoreFX_LoginError(Exception exc) {
      MessageBox.Show(exc + "", "Login Problem");
    }

    private void WindowModel_Loaded(object sender, RoutedEventArgs e) {
      isMainWindow = Application.Current.MainWindow == this;

      settings = new RadGridViewSettings(rgvReport);
      settings.LoadState(fileName);

      System.Windows.Data.CollectionViewSource tradeViewSource = ((System.Windows.Data.CollectionViewSource)(this.FindResource("tradeViewSource")));
      var accountId = "dturbo";
      var password = "1234";
      var isDemo = true;
      var logOff = false;
      logOff = !fw.IsLoggedIn && fw.CoreFX.LogOn(accountId, password, isDemo);
      if (fw.IsLoggedIn)
        #region Load Report
        try {

          var trades = fw.GetTradesFromReport(DateTime.Now.AddMonths(-1), DateTime.Now.AddDays(1).Date);
          if (logOff) fw.LogOff();
          // Load data by setting the CollectionViewSource.Source property:
          tradeViewSource.Source = trades;
        } catch (Exception exc) {
          MessageBox.Show(exc.ToString());
        }
        #endregion
 else
        tradeViewSource.Source = new Trade[] { };

    }
    #endregion


    private void WindowModel_Unloaded(object sender, RoutedEventArgs e) {
      settings.SaveState(fileName);
      if (isMainWindow && Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
        Application.Current.Shutdown();
    }

  }
}
