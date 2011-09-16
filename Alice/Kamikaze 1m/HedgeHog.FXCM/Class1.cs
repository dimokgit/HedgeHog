using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WatiN.Core;

namespace HedgeHog.FXCM {
  public static class Lib {
    public static void GetNewAccount(out string Account, out string Password) {
      // Windows
      WatiN.Core.IE window = new WatiN.Core.IE("http://www.forexmicrolot.com/open-free-micro-uk.jsp");

      // Frames

      // Model
      var frame = ((WatiN.Core.Document)(window)).Frames[0];
      TextField txt_FNAME = frame.TextField(Find.ByName("FNAME"));
      TextField txt_LNAME = frame.TextField(Find.ByName("LNAME"));
      TableCell td_ = frame.TableCell(Find.ByText(""));
      SelectList sel_COUNTRY = frame.SelectList(Find.ByName("COUNTRY"));
      TextField txt_EMAIL = frame.TextField(Find.ByName("EMAIL"));
      Image img_submit = frame.Image(Find.ByName("submit"));

      // Code
      txt_FNAME.Click();
      txt_FNAME.TypeText("a");
      txt_LNAME.Click();
      txt_LNAME.TypeText("a");
      td_.Click();
      sel_COUNTRY.SelectByValue("Afghanistan");
      txt_EMAIL.Click();
      txt_EMAIL.TypeText("a@a.com");
      td_.Click();
      img_submit.Click();
      window.WaitForComplete();
      frame = ((WatiN.Core.Document)(window)).Frames[0];
      var TD = frame.TableCell(td => td.Text == "User ID");
      Account = TD.ContainingTableRow.OwnTableCells[2].Text;
      TD = frame.TableCell(td => td.Text == "Password");
      Password = TD.ContainingTableRow.OwnTableCells[2].Text;
      window.Dispose();
    }
  }
}