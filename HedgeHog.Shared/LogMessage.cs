using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class LogMessage {
    public Exception Exception { get; set; }
    public LogMessage(Exception exception) { this.Exception = exception; }
  }
}
