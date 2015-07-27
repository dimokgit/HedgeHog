using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  [Flags]
  public enum TradeDirections { None = 0, Up = 1, Down = 1 << 1, Both = Up | Down, Auto = 1 << 3 }
  public static class TradeDirectionsMixin {
    public static bool IsAny(this TradeDirections td) { return td.HasUp() || td.HasDown(); }
    public static bool HasUp(this TradeDirections td) { return td.HasFlag(TradeDirections.Up); }
    public static bool HasDown(this TradeDirections td) { return td.HasFlag(TradeDirections.Down); }
    public static bool IsAuto(this TradeDirections td) { return td.HasFlag(TradeDirections.Auto); }
  }
}
