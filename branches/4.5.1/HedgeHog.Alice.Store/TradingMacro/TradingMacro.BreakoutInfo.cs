using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store {
  public partial class TradingMacro {

    class BreakoutInfo1 {
      public bool CanMoveLevelUp { get { return true || dateCrossedUp >= dateLeveledUp; } }
      public bool CanMoveLevelDown { get { return true || dateCrossedDown >= dateLeveledDown; } }
      DateTime dateCrossedUp;
      DateTime dateCrossedDown;
      DateTime dateLeveledUp;
      DateTime dateLeveledDown;
      bool? direction;
      public double? AngleLast { get; set; }
      public void Next(TradingMacro tm, bool leveledUp, bool leveledDown) {
        var direction = tm.RateLast.PriceAvg > tm.MagnetPrice;
        var crossed = direction != this.direction;
        this.direction = direction;
        if (crossed) {
          if (direction)
            dateCrossedUp = tm.ServerTime;
          if (!direction)
            dateCrossedDown = tm.ServerTime;
        }
        if (leveledUp)
          dateLeveledUp = tm.ServerTime;
        if (leveledDown)
          dateLeveledDown = tm.ServerTime;
      }
      public static bool DoExitByAngle(double angle) { return angle.Abs() < 2; }
    }
  }
}