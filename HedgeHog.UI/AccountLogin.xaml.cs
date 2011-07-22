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
using System.Diagnostics;

namespace HedgeHog.UI {
  /// <summary>
  /// Interaction logic for AccountLogin.xaml
  /// </summary>
  public partial class AccountLoginView : UserControl {

    #region Ctor
    public AccountLoginView() {
      InitializeComponent();
    }
    #endregion

    #region AccountLoginCommand
    public AccountLoginRelayCommand AccountLoginCommand {
      get { return (AccountLoginRelayCommand)GetValue(AccountLoginCommandProperty); }
      set { SetValue(AccountLoginCommandProperty, value); }
    }
    public static readonly DependencyProperty AccountLoginCommandProperty =
      DependencyProperty.Register("AccountLoginCommand", typeof(AccountLoginRelayCommand), typeof(AccountLoginView));
    #endregion

    #region OpenNewAccount Command


    public OpenNewAccountRelayCommand OpenNewAccountCommand {
      get { return (OpenNewAccountRelayCommand)GetValue(OpenNewAccountCommandProperty); }
      set { SetValue(OpenNewAccountCommandProperty, value); }
    }
    public static readonly DependencyProperty OpenNewAccountCommandProperty =
        DependencyProperty.Register("OpenNewAccountCommand", typeof(OpenNewAccountRelayCommand), typeof(AccountLoginView));


    #endregion

    #region TradingAccount
    public string TradingAccount {
      get { return (string)GetValue(TradingAccountProperty); }
      set { SetValue(TradingAccountProperty, value); }
    }
    public static readonly DependencyProperty TradingAccountProperty = DependencyProperty.Register("TradingAccount", typeof(string), typeof(AccountLoginView));
    #endregion

    #region TradingPassword
    public string TradingPassword {
      get { return (string)GetValue(TradingPasswordProperty); }
      set { SetValue(TradingPasswordProperty, value); }
    }
    public static readonly DependencyProperty TradingPasswordProperty = DependencyProperty.Register("TradingPassword", typeof(string), typeof(AccountLoginView));
    #endregion

    #region TradingAccountType
    public bool TradingDemo {
      get { return (bool)GetValue(TradingDemoProperty); }
      set { SetValue(TradingDemoProperty, value); }
    }
    public static readonly DependencyProperty TradingDemoProperty = DependencyProperty.Register("TradingDemo", typeof(bool), typeof(AccountLoginView));
    #endregion




    public bool IsLoggedIn {
      get { return (bool)GetValue(IsLoggedInProperty); }
      set { SetValue(IsLoggedInProperty, value); }
    }
    public static readonly DependencyProperty IsLoggedInProperty = DependencyProperty.Register("IsLoggedIn", typeof(bool), typeof(AccountLoginView), new UIPropertyMetadata(false));



    public object Error {
      get { return (object)GetValue(ErrorProperty); }
      set { SetValue(ErrorProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Error.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty ErrorProperty =
        DependencyProperty.Register("Error", typeof(object), typeof(AccountLoginView), new UIPropertyMetadata(null));

    #region Clicks
    private void NewAccount_Click(object sender, RoutedEventArgs e) {
      try {
        var li = new LoginInfo("", "", true);
        if (OpenNewAccountCommand != null) {
          OpenNewAccountCommand.Execute(li);
          if (!li.Canceled) {
            this.TradingAccount = li.Account;
            this.TradingPassword = li.Password;
            this.TradingDemo = li.IsDemo;
          }
        }
      } catch (Exception exc) {
        Error = exc;
      }
    }
    #endregion
  }

  #region Command classes
  public class OpenNewAccountRelayCommand : GalaSoft.MvvmLight.Command.RelayCommand<LoginInfo> {
    public OpenNewAccountRelayCommand(Action<LoginInfo> a) : base(a) { }
    public OpenNewAccountRelayCommand(Action<LoginInfo> a, Predicate<LoginInfo> p) : base(a, p) { }
  }

  public class AccountLoginRelayCommand : GalaSoft.MvvmLight.Command.RelayCommand<LoginInfo> {
    public AccountLoginRelayCommand(Action<LoginInfo> a) : base(a) { }
    public AccountLoginRelayCommand(Action<LoginInfo> a, Predicate<LoginInfo> p) : base(a, p) { }
  }
  public class LoginInfo{
    public string Account { get; set; }
    public string Password { get; set; }
    public bool IsDemo { get; set; }
    public bool Canceled { get; set; }
    public LoginInfo(string account,string password,bool isDemo) {
      this.Account = account;
      this.Password = password;
      this.IsDemo = isDemo;
    }
  }
#endregion
}
