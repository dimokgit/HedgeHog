using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class GenericsCore {
    public static bool IsDefault<T>(this T value) => EqualityComparer<T>.Default.Equals(value, default(T));
  }
}
