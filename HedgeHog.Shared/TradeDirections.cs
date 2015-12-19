using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  [Flags]
  public enum TradeDirections {
    None = 0, Up = 1, Down = 1 << 1, Both = Up | Down, Auto = 1 << 3, Upper = Up | 1 << 4, Downer = Down | 1 << 5
  }
  public static class TradeDirectionsMixin {
    public static bool HasNone(this TradeDirections td) { return !td.HasUp() && !td.HasDown(); }
    public static bool HasAny(this TradeDirections td) { return td.HasUp() || td.HasDown(); }
    public static bool HasUp(this TradeDirections td) { return td.HasFlag(TradeDirections.Up); }
    public static bool HasDown(this TradeDirections td) { return td.HasFlag(TradeDirections.Down); }
    public static bool IsAuto(this TradeDirections td) { return td.HasFlag(TradeDirections.Auto); }
    public static bool IsUpper(this TradeDirections td) { return td == TradeDirections.Upper; }
    public static bool IsDowner(this TradeDirections td) { return td == TradeDirections.Downer; }
  }
}
