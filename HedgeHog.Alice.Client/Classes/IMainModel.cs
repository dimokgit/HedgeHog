﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Client {
  public interface IMainModel {
    event EventHandler<MasterTradeEventArgs> MasterTradeAdded;
    event EventHandler<MasterTradeEventArgs> MasterTradeRemoved;
    Order2GoAddIn.CoreFX CoreFX { get; }
    Exception Log { set; }
    double CurrentLoss { set; }
  }
}
