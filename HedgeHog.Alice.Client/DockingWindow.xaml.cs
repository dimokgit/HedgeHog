using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.ComponentModel.Composition;
using System.IO;
using System.Diagnostics;
using HedgeHog.Models;
using Telerik.Windows.Controls;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for DockingWindow.xaml
  /// </summary>
  public partial class DockingWindow : Window {
    static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    static string SettingsFileName { get { return System.IO.Path.Combine(CurrentDirectory, "RadDocking.xml"); } }
    [Import("MainWindowModel")]
    public object ViewModel { set { this.DataContext = value; } }

    #region Ctor
    public DockingWindow() {
      App.container.SatisfyImportsOnce(this);
      InitializeComponent();
      this.Title = "HedgeHog in " + CurrentDirectory.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();
      Loaded += new RoutedEventHandler(DockingWindow_Loaded);
      Closing += new System.ComponentModel.CancelEventHandler(DockingWindow_Closing);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<CharterControl>(this, (object)CharterControl.MessageType.Add, AddCharter);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<CharterControl>(this, (object)CharterControl.MessageType.Remove, RemoveCharter);
      ((INotifyPropertyChanged)this.DataContext).PropertyChanged += DockingWindow_PropertyChanged;
    }

    void DockingWindow_PropertyChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == "IsLogExpanded") {
        Dispatcher.BeginInvoke(new Action(() => {
          try {
            Log.IsPinned = (bool)sender.GetProperty(e.PropertyName);
          } catch (Exception exc) {
            Debug.WriteLine(exc);
          }
        }));
      }
    }
    #endregion

    #region Window EventHandlers
    void DockingWindow_Loaded(object sender, RoutedEventArgs e) {
      if (File.Exists(SettingsFileName))
        try {
          using (var file = File.OpenText(SettingsFileName)) {
            if(file.BaseStream.Length>10)
              RootVisual.LoadLayout(file.BaseStream);
          }
        } catch (Exception exc) {
          MessageBox.Show(exc + "");
        }
    }

    public void DockingWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      using (var file = File.CreateText(SettingsFileName)) {
        RootVisual.SaveLayout(file.BaseStream);
      }
    }
    #endregion

    #region Methods
    public void RemoveCharter(CharterControl charter) {
      var pane = FindChartPaneByName(charter.Name);
      if (pane != null) {
        pane.RemoveFromParent();
        pane.IsHidden = true;
        RadDocking.SetSerializationTag(pane, "");
        pane.Content = null;
      }
    }
    public void AddCharter(CharterControl charter) {
      var pane = FindChartPaneByName(charter.Name);
      var createPane = pane == null;
      if (pane == null) {
        pane = new RadPane();
        RadDocking.SetSerializationTag(pane, charter.Name);
      }
      pane.IsHidden = false;
      pane.Content = charter;
      var b = new Binding("Header") { Source = charter };
      createPane = pane.GetParent<RadSplitContainer>() == null;
      pane.SetBinding(RadPane.HeaderProperty,b);
      if (createPane) {
        var chartsSplitter = FindChartsSplitter();
        var paneGroup = chartsSplitter.ChildrenOfType<RadPaneGroup>().LastOrDefault();
        if (paneGroup == null) {
          paneGroup = new RadPaneGroup();
          chartsSplitter.Items.Add(paneGroup);
        }
        paneGroup.AddItem(pane, Telerik.Windows.Controls.Docking.DockPosition.Right);
      }
    }
    RadPane FindChartPaneByName(string charterName) {
      return RootVisual.Panes.Where(p => RadDocking.GetSerializationTag(p) == charterName).FirstOrDefault();
    }
    RadSplitContainer FindChartsSplitter() {
      var chart = RootVisual.ChildrenOfType<CharterControl>().FirstOrDefault();
      if (chart == null) return ChartsSplitter;
      return chart.GetParent<RadSplitContainer>() ?? ChartsSplitter;
    }
    #endregion

  }
}
