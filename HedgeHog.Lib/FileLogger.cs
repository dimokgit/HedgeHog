using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Windows;

namespace HedgeHog {
  public static class FileLogger {
    private static string logFileName = "Log.txt";
    //private static Logger nLogger = LogManager.GetCurrentClassLogger();

    static FileLogger() {
      System.IO.File.Delete(logFileName);
    }
    public static Exception LogToFile(Exception exc) { return LogToFile(exc, logFileName); }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public static Exception LogToFile(Exception exc, string fileName) {
      try {
        if(exc != null) {
          var text =  $"{Environment.NewLine}*****{DateTime.Now.ToString("[dd HH:mm:ss.fff] ")} *****{Environment.NewLine}";
          text += string.Join(Environment.NewLine, ExceptionMessages(exc));
          LogToFile(fileName, text);
          //nLogger.Error(text);
        }

      } catch {
        try {
          if(System.Windows.Application.Current !=null)
          MessageBox.Show(Application.Current.MainWindow, exc.ToString());
          else MessageBox.Show(exc.ToString());
        } catch { }
      }
      return exc;
    }

    public static IEnumerable<string> ExceptionMessages(Exception exc) {
      while(exc != null) {
        yield return exc.Message + (exc?.StackTrace.IsNullOrWhiteSpace() == true ? "" : Environment.NewLine) + exc.StackTrace;
        exc = exc.InnerException;
      }
    }

    public static void LogToFile(string text) {
      LogToFile(logFileName, text);
    }
    //[MethodImpl(MethodImplOptions.Synchronized)]
    public static void LogToFile(string fileName, string text) {
      System.IO.File.AppendAllText(fileName, text);
    }
  }
}
