using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Client {
  public interface IMainModel {
    Order2GoAddIn.CoreFX CoreFX { get; }
  }
}
