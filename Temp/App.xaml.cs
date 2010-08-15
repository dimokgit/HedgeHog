using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using FXW = Order2GoAddIn.FXCoreWrapper;

namespace Temp {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    static FXW _fw;
    public static FXW fw {
      get {
        if (_fw == null) _fw = new FXW();
        return _fw;
      }
    }
  }
}
