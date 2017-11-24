using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public abstract class EnumClassUtils<TClass>
  where TClass : class {

    public static TEnum Parse<TEnum>(string value, bool ignoreCase = false) where TEnum : struct, TClass {
      return (TEnum)Enum.Parse(typeof(TEnum), value, ignoreCase);
    }
    public static bool Contains<TEnum>(string enumValue) where TEnum : struct, TClass
      => Enum.GetNames(typeof(TEnum)).Select(s => s.ToLower()).Contains(enumValue.ToLower());

  }

  public class EnumUtils : EnumClassUtils<Enum> {
  }
}
