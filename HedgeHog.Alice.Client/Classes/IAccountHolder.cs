﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Client {
  interface IAccountHolder {
    bool Login(string account, string password, bool isDemo);
  }
}
