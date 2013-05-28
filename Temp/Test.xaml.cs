using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HedgeHog;
using HedgeHog.DB;
using HedgeHog.Models;
using HedgeHog.NewsCaster;
using System.ComponentModel.Composition;
using HedgeHog.UI;
using System.ComponentModel.Composition.Hosting;

namespace Temp {
  /// <summary>
  /// Interaction logic for Test.xaml
  /// </summary>
  public partial class Test : WindowModel {
    #region Log
    IList<string> _logs = new List<string>();
    public string Log {
      get { return string.Join(Environment.NewLine, _logs); }
      set {
        _logs.Add(value);
        OnPropertyChanged("Log");
        IsLogExpanded = true;
      }
    }
    #region IsLogExpanded
    private bool _IsLogExpanded;
    private IDisposable newHappened;
    public bool IsLogExpanded {
      get { return _IsLogExpanded; }
      set {
        if (_IsLogExpanded != value) {
          _IsLogExpanded = value;
          OnPropertyChanged("IsLogExpanded");
        }
      }
    }

    #endregion
    #endregion

    NewsCasterModel _NewsControl;

    [Import(typeof(NewsCasterModel))]
    public NewsCasterModel NewsControl {
      get { return _NewsControl; }
      set { _NewsControl = value; }
    }
   
    public Test() {
      InitializeComponent();
      Loaded += new RoutedEventHandler(Test_Loaded);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Register<Exception>(this, exc => Log = exc + "");
    }

    void Test_Loaded(object sender, RoutedEventArgs e) {
      try {
        this.Compose();
        newHappened = NewsControl.NewsHapenedSubject.Subscribe(nc => MessageBox.Show(nc + ""));
      } catch (Exception exc) {
        Log = exc+"";
      }
    }


    private void Window_Unloaded(object sender, RoutedEventArgs e) {
      if (App.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown) {
        App.Current.Shutdown();
      }
    }
    private void Window_Loaded(object sender, RoutedEventArgs e) {
    }

  }
}
