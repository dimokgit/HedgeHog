using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class LogMessage {
    public Exception Exception { get; set; }
    public LogMessage(object message) : this(new Exception(message + "")) { }
    public LogMessage(Exception exception) { this.Exception = exception; }
    public static void Send(string message) { GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(new Exception(message)); }
    public static void Send(Exception exc) { GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(exc); }

  }
}
