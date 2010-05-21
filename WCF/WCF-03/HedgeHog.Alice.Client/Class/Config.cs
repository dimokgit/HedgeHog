using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Client {
  public static class Config {
    public static int PipsDifferenceToSync { get { return int.Parse(ConfigurationManager.AppSettings["PipsDifferenceToSync"]); } }
    public static int SecondsDifferenceToSync { get { return int.Parse(ConfigurationManager.AppSettings["SecondsDifferenceToSync"]); } }
  }
}
