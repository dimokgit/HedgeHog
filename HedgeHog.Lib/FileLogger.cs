using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Windows;
using NLog;

namespace HedgeHog {
  public static class FileLogger {
    private static string logFileName = "Log.txt";
    private static Logger nLogger = LogManager.GetCurrentClassLogger();
    
    static FileLogger() {
      System.IO.File.Delete(logFileName);
    }
    public static Exception LogToFile(Exception exc) { return LogToFile(exc, logFileName); }
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static Exception LogToFile(Exception exc,string fileName) {
      try {
        if (exc != null) {
          var text = DateTime.Now.ToString("[dd HH:mm:ss.fff] ") +" **************** Exception ***************" + Environment.NewLine;
          while (exc != null) {
            text += exc.Message + Environment.NewLine + exc.StackTrace + Environment.NewLine;
            exc = exc.InnerException;
          }
          System.IO.File.AppendAllText(fileName, text);
          nLogger.Error(text);
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
