﻿using System;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;

namespace HedgeHog.Alice.Client {
  /// <summary>
  /// Interaction logic for RemoteControl.xaml
  /// </summary>
  public partial class RemoteControlView : UserControl {
    public RemoteControlView() {
      InitializeComponent();
    }

    private void DataGrid_KeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Escape)
        (sender as Selector).SelectedIndex = -1;
    }
  }
}
