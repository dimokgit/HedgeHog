using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Converters;

namespace HedgeHog {
  public static class Common {
    public static string ToJson(this object obj) {
      var settings = new Newtonsoft.Json.JsonSerializerSettings();
      settings.Converters.Add(new StringEnumConverter());
      return Newtonsoft.Json.JsonConvert.SerializeObject(obj, settings);
    }
    public static T FromJson<T>(this string json) {
      var settings = new Newtonsoft.Json.JsonSerializerSettings();
      settings.Converters.Add(new StringEnumConverter());
      return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, settings);
    }
    public static bool TryFromJson<T>(this string json, out T v) {
      try {
        v = json.FromJson<T>();
        return true;
      } catch {
        v = default(T);
        return false;
      }
    }

    public static string CurrentDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    public static string ExecDirectory() {
      return CurrentDirectory.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();
    }
  }
}
