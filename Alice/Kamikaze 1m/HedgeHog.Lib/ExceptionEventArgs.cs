using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public class ExceptionEventArgs :EventArgs{
    public Exception Exception { get; set; }
    public ExceptionEventArgs(Exception exception) {
      this.Exception = exception;
    }
  }
}
