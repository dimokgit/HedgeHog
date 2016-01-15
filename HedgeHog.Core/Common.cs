using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class Common {
    public static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    public static string ExecDirectory() {
      return CurrentDirectory.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();
    }
  }
}
