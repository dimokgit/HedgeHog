using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Security;
using System.Security.Permissions;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.ServiceModel;
using System.ServiceModel.Description;


namespace HedgeHog.Server {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public sealed partial class App : Application, IDisposable {
    private Order2GoAddIn.CoreFX _coreFX = new Order2GoAddIn.CoreFX();
    public Order2GoAddIn.CoreFX CoreFX { get { return _coreFX; } }
    public static List<ServerWindow> serverWindows = new List<ServerWindow>();
    public static ServerWindow AddMainWindow(ServerWindow mainWindow) {
      if (mainWindow == null) {
        var hidden = serverWindows.FirstOrDefault(w => w.Visibility != Visibility.Visible);
        if (hidden != null) {
          hidden.Show();
          hidden.Activate();
          return hidden;
        }
      }
      var mw = mainWindow != null ? mainWindow : new ServerWindow("ServerWindow" + serverWindows.Count + 1);
      serverWindows.Add(mw);
      HedgeHog.Wcf.RegisterServer(mw);
      mw.Show();
      return mw;
    }
    ServiceHost wcfHost;
    [EnvironmentPermission(SecurityAction.LinkDemand, Unrestricted = true)]
    public App() {
      try {
        //RemotingConfiguration.Configure(AppDomain.CurrentDomain.FriendlyName + ".config", false);
        RemotingConfiguration.Configure(AppDomain.CurrentDomain.BaseDirectory + "Remoting.xml", false);
        RemotingConfiguration.RegisterWellKnownServiceType(typeof(Remoter), "GET", WellKnownObjectMode.SingleCall);
        var channelUrl = ((System.Runtime.Remoting.Channels.ChannelDataStore)(((System.Runtime.Remoting.Channels.Tcp.TcpChannel)(System.Runtime.Remoting.Channels.ChannelServices.RegisteredChannels[0])).ChannelData)).ChannelUris[0];
        var port = int.Parse(channelUrl.Split(':')[2], CultureInfo.InvariantCulture);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
          try {
            AddMainWindow((ServerWindow)MainWindow);
            Remoter.Server = (IServer)MainWindow;
            ((ServerWindow)MainWindow).TcpPort = port;
            MessageBox.Show(wcfHost.BaseAddresses[0] + " is running.");
          } catch (Exception exc) {
            MessageBox.Show(exc.Message + Environment.NewLine + exc.StackTrace);
          }
        }));
        var wcfPort = ConfigurationManager.AppSettings["wcfPort"];
        wcfHost = new ServiceHost(typeof(HedgeHog.WCF.Trading),new Uri("net.tcp://localhost:"+wcfPort+"/"));
        wcfHost.Open();
      } catch (Exception exc) {
        MessageBox.Show(exc.Message);
        App.Current.Shutdown();
      }
    }

    private void Application_Exit(object sender, ExitEventArgs e) {
      if( wcfHost!=null && wcfHost.State != CommunicationState.Closed)
      wcfHost.Close();
      Dispose();
    }

    #region IDisposable Members
    ~App() {
      Dispose();
    }
    public void Dispose() {
      if (_coreFX != null)
        _coreFX.Dispose();
      GC.SuppressFinalize(this);
    }

    #endregion
  }
}
