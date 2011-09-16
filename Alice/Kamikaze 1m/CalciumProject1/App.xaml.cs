using System.IO;
using System.Windows;

using DanielVaughan.Calcium;

namespace CalciumProject1 {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App {
    public App() {
      /* Intentionally left blank. */
    }

    void Application_Startup(object sender, StartupEventArgs e) {
      /* This causes log4net to initalise. 
     * We need this for the ClientLogging library 
     * to be able to log using log4net. 
     * It triggers reading the config etc. [DV] */
      log4net.Config.XmlConfigurator.ConfigureAndWatch(new FileInfo("Log4Net.config"));

      Log.Info("Client starting.");

      /* To substitute the unity container with your own, enabling replacement 
       * of the splash screen and shell etc, initialize the ServiceLocatorSingleton as shown. */
      //ServiceLocatorSingleton.Instance.InitializeServiceLocator(new UnityContainer());

      var starter = new AppStarter();
      /* To customize the splash screen image use the StartupOptions as shown below. */
      //starter.StartupOptions.SplashImagePackUri = new Uri("pack://application:,,,/YourAssembly;component/YourImage.jpg");

      /* To exclude default modules use the ExcludedModules list. */
      //starter.StartupOptions.ModuleCatalogOptions.ExcludedModules.Add(ModuleNames.OutputDisplay);
      //starter.StartupOptions.ModuleCatalogOptions.ExcludedModules.AddRange(ModuleNames.DefaultModuleNames);
      starter.Start();
    }
  }
}