using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;

namespace HedgeHog.Alice.Client {
  public static class FileLogger {
    private static string logFileName = "Log.txt";
    static FileLogger() {
      System.IO.File.Delete(logFileName);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public static Exception LogToFile(Exception exc) {
      if (exc != null) {
        var text = "**************** Exception ***************" + Environment.NewLine;
        while (exc != null) {
          text += exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
          exc = exc.InnerException;
        }
        System.IO.File.AppendAllText(logFileName, text);
      }

      return exc;
    }
  }
}
