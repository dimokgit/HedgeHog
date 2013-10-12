using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class EnumsExtensions {
    public static IList<string> HasDuplicates(this Enum me) {
      var type = me.GetType();
      return Enum.GetNames(type)
          .Select(e => new { name = e, value = (int)Enum.Parse(type, e) })
          .GroupBy(g => g.value)
          .Where(g => g.Count() > 1)
          .Select(g => type.Name + " has duplicates:" + string.Join(",", g))
          .ToArray();
    }
  }
}
