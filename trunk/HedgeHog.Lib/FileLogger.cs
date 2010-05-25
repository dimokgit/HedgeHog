using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Windows;

namespace HedgeHog {
  public static class FileLogger {
    private static string logFileName = "Log.txt";
    static FileLogger() {
      System.IO.File.Delete(logFileName);
    }
    public static Exception LogToFile(Exception exc) { return LogToFile(exc, logFileName); }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static Exception LogToFile(Exception exc,string fileName) {
      try {
        if (exc != null) {
          var text = "**************** Exception ***************" + Environment.NewLine;
          while (exc != null) {
            text += exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
            exc = exc.InnerException;
          }
          System.IO.File.AppendAllText(fileName, text);
        }

      } catch {
        try {
          MessageBox.Show(System.Windows.Application.Current.MainWindow, exc.ToString());
        } catch { }
      }
      return exc;
    }
  }
}
