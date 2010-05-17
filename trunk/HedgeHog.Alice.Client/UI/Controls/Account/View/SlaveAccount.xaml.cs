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

namespace HedgeHog.Alice.Client.UI.Controls {
  /// <summary>
  /// Interaction logic for SlaveAccount.xaml
  /// </summary>
  public partial class SlaveAccount : UserControl {
    public SlaveAccount() {
      InitializeComponent();

    }

    #region Dependency Properties

    public TraderModel MasterModel {
      get { return (TraderModel)GetValue(MasterModelProperty); }
      set { SetValue(MasterModelProperty, value); }
    }
    public static readonly DependencyProperty MasterModelProperty =
        DependencyProperty.Register("MasterModel", typeof(TraderModel), typeof(SlaveAccount), new UIPropertyMetadata((d,p) => {
          var sa = d as SlaveAccount;
          var sam = sa.LayoutRoot.DataContext as SlaveAccountModel;
          if (sam.MasterModel != p.NewValue as TraderModel)
            sam.MasterModel = p.NewValue as TraderModel;
        }));

    

    public string TradingAccount {
      get { return (string)GetValue(TradingAccountProperty); }
      set { SetValue(TradingAccountProperty, value); }
    }

    // Using a DependencyProperty as the backing store for TradingAccount.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty TradingAccountProperty =
        DependencyProperty.Register("TradingAccount", typeof(string), typeof(SlaveAccount));



    public string TradingPassword {
      get { return (string)GetValue(TradingPasswordProperty); }
      set { SetValue(TradingPasswordProperty, value); }
    }

    // Using a DependencyProperty as the backing store for TradingPassword.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty TradingPasswordProperty =
        DependencyProperty.Register("TradingPassword", typeof(string), typeof(SlaveAccount));



    public bool TradingDemo {
      get { return (bool)GetValue(TradingDemoProperty); }
      set { SetValue(TradingDemoProperty, value); }
    }

    // Using a DependencyProperty as the backing store for TradingDemo.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty TradingDemoProperty =
        DependencyProperty.Register("TradingDemo", typeof(bool), typeof(SlaveAccount));
    #endregion

  }
}
