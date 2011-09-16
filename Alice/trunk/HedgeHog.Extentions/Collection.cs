using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;

namespace HedgeHog {
  public static class CollectionEx {
    public static void RemoveAll<T>(this IList<T> c,IEnumerable<T> elements ){
      foreach (var e in elements.ToList())
        c.Remove(e);
    }
  }
}
