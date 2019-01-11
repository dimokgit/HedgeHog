using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class ExceptionExtensions {
    public static string GetExceptionShort(this Exception exc) {
        var header = (exc.TargetSite == null ? "" : exc.TargetSite.DeclaringType.Name + "." + exc.TargetSite.Name + ": ");
        var data = new List<string>() { exc.Message };
        foreach (System.Collections.DictionaryEntry d in exc.Data)
            data.Add(d.Key + ":" + d.Value);

        return header + string.Join(Environment.NewLine, data); ;
    }

  }
}
