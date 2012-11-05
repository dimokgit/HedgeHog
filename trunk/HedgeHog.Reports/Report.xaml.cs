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
using System.Reflection;

namespace HedgeHog.Reports {
  /// <summary>
  /// Interaction logic for Report.xaml
  /// </summary>
  public partial class Report : Window {

    #region Properties
    #region Container
    private static CompositionContainer Container {
      get {
        AggregateCatalog catalog = new AggregateCatalog();
        // Add the EmailClient.Presentation assembly into the catalog
        catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetExecutingAssembly()));
        catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetEntryAssembly()));
        // Add the EmailClient.Applications assembly into the catalog
        //catalog.Catalogs.Add(new AssemblyCatalog(typeof(IMainModel).Assembly));

        var container = new CompositionContainer(catalog);
        CompositionBatch batch = new CompositionBatch();
        batch.AddExportedValue(container);
        container.Compose(batch);
        return container;
      }
    }
    #endregion
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
    public Report(FXCoreWrapper fw) {
      this.fw = fw;
      try {
        InitializeComponent();
      } catch (Exception exc) {
        throw;
      }
    }

    public Report() {
      Container.SatisfyImportsOnce(this);

      InitializeComponent();
      if (fw != null)
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
      if (fw.IsLoggedIn) {
        try {// Load Report
          var trades = fw.GetTradesFromReport(DateTime.Now.AddMonths(-1), DateTime.Now.AddDays(1).Date);
          if (logOff) fw.CoreFX.Logout();
          // Load data by setting the CollectionViewSource.Source property:
          tradeViewSource.Source = trades;
        } catch (Exception exc) {
          MessageBox.Show(exc.ToString());
        }
      } else
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
