using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WC = WatiN.Core;
using System.Runtime.CompilerServices;
namespace Manheim.Web {
  public static class ManheimExtensions {
    #region Browser
    static WC.Browser _browser;
    public static WC.Browser Browser {
      [MethodImpl(MethodImplOptions.Synchronized)]
      get {
        return _browser ?? (_browser = new WC.IE(GetHomeUrl(null)));
      }
      set {
        if (_browser != null) {
          _browser.Close();
          _browser.Dispose();
        }
        _browser = value;
      }
    }
    #endregion

    public static string GetHomeUrl(this object o) { return "manheim.com"; }
    public static string GetLoginUrl(this object o) { return GetHomeUrl(o) + "/login"; }
    public static string GetUserName(this object o) { return "afservices"; }
    public static string GetPassword(this object o) { return "password"; }
    public static string GetTabBuyPreSaleId(this object o) { return "tab_buy_pre_sale"; }
  }
}
