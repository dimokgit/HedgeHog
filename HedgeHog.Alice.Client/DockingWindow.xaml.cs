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
using Gala = GalaSoft.MvvmLight.Command;
using System.Collections.ObjectModel;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for DockingWindow.xaml
  /// </summary>
  public partial class DockingWindow : WindowModel {
    #region Attached Properties
    #region IsCharterContainer
    /// <summary>
    /// The IsCharterContainer attached property's name.
    /// </summary>
    public const string IsCharterContainerPropertyName = "IsCharterContainer";

    /// <summary>
    /// Gets the value of the IsCharterContainer attached property 
    /// for a given dependency object.
    /// </summary>
    /// <param name="obj">The object for which the property value
    /// is read.</param>
    /// <returns>The value of the IsCharterContainer property of the specified object.</returns>
    public static bool GetIsCharterContainer(DependencyObject obj) {
      return (bool)obj.GetValue(IsCharterContainerProperty);
    }

    /// <summary>
    /// Sets the value of the IsCharterContainer attached property
    /// for a given dependency object. 
    /// </summary>
    /// <param name="obj">The object to which the property value
    /// is written.</param>
    /// <param name="value">Sets the IsCharterContainer value of the specified object.</param>
    public static void SetIsCharterContainer(DependencyObject obj, bool value) {
      obj.SetValue(IsCharterContainerProperty, value);
    }

    /// <summary>
    /// Identifies the IsCharterContainer attached property.
    /// </summary>
    public static readonly DependencyProperty IsCharterContainerProperty = DependencyProperty.RegisterAttached(
        IsCharterContainerPropertyName,
        typeof(bool),
        typeof(DockingWindow),
        new UIPropertyMetadata(false));
    #endregion

    #region ParentRadPaneGroup
    /// <summary>
    /// The ParentRadPaneGroup attached property's name.
    /// </summary>
    public const string ParentRadPaneGroupPropertyName = "ParentRadPaneGroup";

    /// <summary>
    /// Gets the value of the ParentRadPaneGroup attached property 
    /// for a given dependency object.
    /// </summary>
    /// <param name="obj">The object for which the property value
    /// is read.</param>
    /// <returns>The value of the ParentRadPaneGroup property of the specified object.</returns>
    public static RadPaneGroup GetParentRadPaneGroup(DependencyObject obj) {
      return (RadPaneGroup)obj.GetValue(ParentRadPaneGroupProperty);
    }

    /// <summary>
    /// Sets the value of the ParentRadPaneGroup attached property
    /// for a given dependency object. 
    /// </summary>
    /// <param name="obj">The object to which the property value
    /// is written.</param>
    /// <param name="value">Sets the ParentRadPaneGroup value of the specified object.</param>
    public static void SetParentRadPaneGroup(DependencyObject obj, RadPaneGroup value) {
      obj.SetValue(ParentRadPaneGroupProperty, value);
    }

    /// <summary>
    /// Identifies the ParentRadPaneGroup attached property.
    /// </summary>
    public static readonly DependencyProperty ParentRadPaneGroupProperty = DependencyProperty.RegisterAttached(
        ParentRadPaneGroupPropertyName,
        typeof(RadPaneGroup),
        typeof(DockingWindow),
        new UIPropertyMetadata(null));
    #endregion
    #endregion

    #region Properties
    static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    static string SettingsFileName { get { return System.IO.Path.Combine(CurrentDirectory, "RadDocking.xml"); } }
    public MemoryStream _originalLayout { get; set; }
    ObservableCollection<RadPane> _hiddenCharters = new ObservableCollection<RadPane>();
    public ObservableCollection<RadPane> Charters { get { return _hiddenCharters; } }
    void AddCharterToHidden(RadPane pane) {
      if (pane != null) {
        _hiddenCharters.Where(p => RadDocking.GetSerializationTag(p) == RadDocking.GetSerializationTag(pane))
          .ToList().ForEach(p => _hiddenCharters.Remove(p));
        _hiddenCharters.Add(pane);
      }
    }
    void RemoveCharterFromHidden(RadPane pane) {
      if (pane != null && _hiddenCharters.Contains(pane))
        _hiddenCharters.Remove(pane);
    }
    Dictionary<string, bool> _canUserCloseDictionary = new Dictionary<string, bool>();
    bool CanUserClose(RadPane pane) {
      bool canUserClose;
      var hasValue = _canUserCloseDictionary.TryGetValue(RadDocking.GetSerializationTag(pane),out canUserClose);
      return !hasValue || canUserClose;
    }

    ObservableCollection<RadPane> _Views = new ObservableCollection<RadPane>();
    public ObservableCollection<RadPane> Views {
      get { return _Views; }
    }

    [Import("MainWindowModel")]
    public object ViewModel { set { RootVisual.DataContext = value; } }
    #endregion

    #region Ctor
    public DockingWindow() {
      StyleManager.ApplicationTheme = new VistaTheme();
      InitializeComponent();
      App.container.SatisfyImportsOnce(this);
      this.Title = "HedgeHog in " + CurrentDirectory.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();
      ((INotifyPropertyChanged)RootVisual.DataContext).PropertyChanged += DataContext_PropertyChanged;
      #region Window Events
      Closing += new System.ComponentModel.CancelEventHandler(DockingWindow_Closing);
      #endregion
      #region Message Registration
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<CharterControl>(this, (object)CharterControl.MessageType.Add, AddCharter);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<CharterControl>(this, (object)CharterControl.MessageType.Remove, RemoveCharter);
      #endregion
      #region RootVisual Events
      RootVisual.Loaded += RootVisual_Loaded;
      RootVisual.Close += RootVisual_Close;
      RootVisual.PaneStateChange += RootVisual_PaneStateChange;
      RootVisual.ElementCleaning += RootVisual_ElementCleaning;
      RootVisual.ElementLoaded += RootVisual_ElementLoaded;
      #endregion
    }
    #endregion

    #region RootVisual Event Handlers
    void RootVisual_Loaded(object sender, RoutedEventArgs e) {
      SaveOriginalLayout();
      LoadLayoutFromFile(SettingsFileName);
    }

    void RootVisual_ElementCleaning(object sender, LayoutSerializationEventArgs e) {
      var pane = e.AffectedElement as RadPane;
      if (pane != null) {
        _canUserCloseDictionary[RadDocking.GetSerializationTag(pane)] = pane.CanUserClose;
      }
    }

    void RootVisual_ElementLoaded(object sender, LayoutSerializationEventArgs e) {
      var pane = e.AffectedElement as RadPane;
      if (pane != null) {
        var canClose = CanUserClose(pane);
        //pane.CanUserClose = CanUserClose(pane);
        if (!canClose) {
          Views.Add(pane);
          pane.CanUserClose = true;
        }
        if (IsCharterPane(pane)) {
          AddCharterToHidden(pane);
          SetCharterPaneBindings(pane);
        }
      }
    }


    void RootVisual_PaneStateChange(object sender, Telerik.Windows.RadRoutedEventArgs e) {
      var pane = e.OriginalSource as RadPane;
      if (pane != null && pane.IsInDocumentHost)
        DockingWindow.SetParentRadPaneGroup(pane, pane.PaneGroup);
    }

    void RootVisual_Close(object sender, Telerik.Windows.Controls.Docking.StateChangeEventArgs e) {
      e.Panes.ToList().ForEach(pane => {
        if( CanUserClose(pane) && !IsCharterPane(pane))
          pane.RemoveFromParent();
      });
    }
    #endregion

    #region Window Event Handlers
    void DataContext_PropertyChanged(object sender, PropertyChangedEventArgs e) {
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

    public void DockingWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      SaveLayout(SettingsFileName);
    }

    #endregion

    #region Commands

    #region TileCommand

    ICommand _TileChartsCommand;
    public ICommand TileChartsCommand {
      get {
        if (_TileChartsCommand == null) {
          _TileChartsCommand = new Gala.RelayCommand(TileCharts, () => true);
        }

        return _TileChartsCommand;
      }
    }
    void TileCharts() {
      var charterPanes = RootVisual.Panes.Where(pane => !pane.IsHidden && IsCharterPane(pane)).Union(Charters).ToList();
      var charterPaneGroup = (charterPanes.FirstOrDefault(pane => pane.IsInDocumentHost) ?? charterPanes.FirstOrDefault()).PaneGroup;
      foreach (var pane in charterPanes.Skip(1).Reverse().ToList()) {
          pane.RemoveFromParent();
          pane.Width = pane.Height = double.NaN;
          charterPaneGroup.AddItem(pane, Telerik.Windows.Controls.Docking.DockPosition.Bottom);
      }
    }

    #endregion

    #region ResetLayoutCommand

    ICommand _ResetLayoutCommand;
    public ICommand ResetLayoutCommand {
      get {
        if (_ResetLayoutCommand == null) {
          _ResetLayoutCommand = new Gala.RelayCommand(ResetLayout, () => true);
        }

        return _ResetLayoutCommand;
      }
    }
    void ResetLayout() {
      LoadOriginalLayout();
    }

    #endregion

    #region ShowAllPanesCommand
    ICommand _ShowAllPanesCommand;
    public ICommand ShowAllPanesCommand {
      get {
        if (_ShowAllPanesCommand == null) {
          _ShowAllPanesCommand = new Gala.RelayCommand(ShowAllPanes, () => true);
        }
        return _ShowAllPanesCommand;
      }
    }
    void ShowAllPanes() {
      RootVisual.Panes.ToList().ForEach(p => p.IsHidden = false);
    }
    #endregion

    #region LoadLayoutCommand
    ICommand _LoadLayoutCommand;
    public ICommand LoadLayoutCommand {
      get {
        if (_LoadLayoutCommand == null) {
          _LoadLayoutCommand = new Gala.RelayCommand(LoadLayout, () => true);
        }

        return _LoadLayoutCommand;
      }
    }
    #endregion

    #region SaveLayoutAsCommand
    ICommand _SaveLayoutAsCommand;
    public ICommand SaveLayoutAsCommand {
      get {
        if (_SaveLayoutAsCommand == null) {
          _SaveLayoutAsCommand = new Gala.RelayCommand(SaveLayoutAs, () => true);
        }

        return _SaveLayoutAsCommand;
      }
    }
    void SaveLayoutAs() {
      string fileName = SaveLayoutPath();
      if (!string.IsNullOrWhiteSpace(fileName))
        SaveLayout(fileName);
    }
    #endregion

    #endregion

    #region Load/Save Layout
    private void LoadLayoutFromFile(string fileName) {
      if (File.Exists(fileName))
        try {
          using (var file = File.OpenText(fileName)) {
            if (file.BaseStream.Length > 10)
              RootVisual.LoadLayout(file.BaseStream);
          }
        } catch (Exception exc) {
          MessageBox.Show(exc + "");
        }
    }

    void LoadLayout() {
      string fileName = OpenLayoutPath();
      if (!string.IsNullOrWhiteSpace(fileName))
        LoadLayoutFromFile(fileName);
    }
    private void LoadOriginalLayout() {
      RootVisual.LoadLayout(_originalLayout);
      _originalLayout.Position = 0;
    }
    private void SaveOriginalLayout() {
      _originalLayout = new MemoryStream();
      RootVisual.SaveLayout(_originalLayout);
      _originalLayout.Position = 0;
    }
    private void SaveLayout(string fileName) {
      using (var file = File.CreateText(fileName)) {
        RootVisual.SaveLayout(file.BaseStream, true);
      }
    }
    public static string OpenLayoutPath() {
      var dlg = new Microsoft.Win32.OpenFileDialog();
      dlg.DefaultExt = ".xml";
      dlg.Filter = "XML File(.xml)|*.xml";
      if (dlg.ShowDialog() != true) return "";
      return dlg.FileName;
    }
    public static string SaveLayoutPath() {
      var dlg = new Microsoft.Win32.SaveFileDialog();
      dlg.DefaultExt = ".xml";
      dlg.Filter = "XML File(.xml)|*.xml";
      if (dlg.ShowDialog() != true) return "";
      return dlg.FileName;
    }
    #endregion

    #region Add/Remove Charter
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
      SetCharterPaneBindings(pane);
      createPane = pane.GetParent<RadSplitContainer>() == null;
      if (createPane) {
        var chartsSplitter = FindChartsSplitter();
        var paneGroup = chartsSplitter.ChildrenOfType<RadPaneGroup>().LastOrDefault();
        if (paneGroup == null) {
          paneGroup = new RadPaneGroup();
          chartsSplitter.Items.Add(paneGroup);
        }
        paneGroup.AddItem(pane, Telerik.Windows.Controls.Docking.DockPosition.Right);
      }
      AddCharterToHidden(pane);
    }

    private CharterControl GetCharterFromPane(RadPane pane) { return pane.Content as CharterControl; }
    private bool IsCharterPane(RadPane pane) { return pane.Content is CharterControl; }
    private static void SetCharterPaneBindings(RadPane pane) {
      var b = new Binding("Header") { Source = pane.Content };
      pane.SetBinding(RadPane.HeaderProperty, b);
    }
    RadPane FindChartPaneByName(string charterName) {
      return RootVisual.Panes.Where(p => RadDocking.GetSerializationTag(p) == charterName).FirstOrDefault();
    }
    RadSplitContainer FindChartsSplitter() {
      var chart = RootVisual.Panes.Where(pane=>pane.Content is CharterControl).LastOrDefault();
      if (chart == null) return ChartsSplitter;
      return chart.GetParent<RadSplitContainer>() ?? ChartsSplitter;
    }
    #endregion

  }
}
