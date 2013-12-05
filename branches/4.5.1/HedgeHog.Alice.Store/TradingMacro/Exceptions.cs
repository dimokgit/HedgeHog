using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store {
  public class SetLotSizeException:Exception {
    public SetLotSizeException(string message, Exception inner) : base(message, inner) { }
  }
}
