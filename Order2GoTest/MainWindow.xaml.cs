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
using System.Windows.Navigation;
using System.Windows.Shapes;
using O2G = Order2GoAddIn;
using HedgeHog.Models;

namespace Order2GoTest {
  public partial class MainWindow : WindowModel {
    public MainWindow() {
      InitializeComponent();
    }

    O2G.CoreFX _coreFx = new O2G.CoreFX();
    O2G.FXCoreWrapper _fw;

    #region LotsToBuy
    private int _LotsToBuy;
    public int LotsToBuy {
      get { return _LotsToBuy; }
      set {
        if (_LotsToBuy != value) {
          _LotsToBuy = value;
          OnPropertyChanged("LotsToBuy");
        }
      }
    }
    #endregion
    #region LotsToSell
    private int _LotsToSell;
    public int LotsToSell {
      get { return _LotsToSell; }
      set {
        if (_LotsToSell != value) {
          _LotsToSell = value;
          OnPropertyChanged("LotsToSell");
        }
      }
    }
    
    #endregion

    #region Log
    private string _Log = "";
    public string Log {
      get { return _Log; }
      set {
        _Log += value + Environment.NewLine;
        OnPropertyChanged("Log");
      }
    }
    
    #endregion
    string pair = "EUR/USD";

    private void btnBuy_Click(object sender, RoutedEventArgs e) {
      try {
        _fw.OpenTrade(pair,true,LotsToBuy,0,0,0,"");
        Log = string.Format("Bought {0}K of {1}",LotsToBuy,pair);
      } catch (Exception exc) {
        Log = exc + "";
      }
    }

    private void btnSell_Click(object sender, RoutedEventArgs e) {
      try {
        _fw.ClosePair(pair, true, LotsToSell);
        Log = string.Format("Sold {0}K of {1}", LotsToSell, pair);
      } catch (Exception exc) {
        Log = exc + "";
      }
    }

    private void btnLogin_Click(object sender, RoutedEventArgs e) {
      if (_coreFx.LogOn("D31538164001", "8802", true)) {
        _fw = new O2G.FXCoreWrapper(_coreFx);
        _fw.Error += new EventHandler<HedgeHog.Shared.ErrorEventArgs>(_fw_Error);
        _fw.OrderError += new EventHandler<O2G.OrderErrorEventArgs>(_fw_OrderError);
        Log = "Logged in.";
      }
    }

    void _fw_OrderError(object sender, O2G.OrderErrorEventArgs e) {
      Log = e.Error + "";
    }

    void _fw_Error(object sender, HedgeHog.Shared.ErrorEventArgs e) {
      Log = e.Error + "";
    }


  }
}
