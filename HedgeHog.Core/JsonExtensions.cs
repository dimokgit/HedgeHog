using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HedgeHog.Core {
  public static class JsonExtensions {
    public static string ToJson(this object obj) {
      var settings = new Newtonsoft.Json.JsonSerializerSettings() {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
      };
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

    /// <summary>
    /// Convert object to JSON string
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="o"></param>
    /// <param name="formatting">Default to Formatting.Indented</param>
    /// <returns></returns>
    public static string ToJson<T>(this T o, Formatting formatting) {
      var toJson = o == null ? null : o.GetType().GetMethod("ToJson");
      if(toJson != null)
        return toJson.Invoke(o, new object[] { }) as string;
      return o.ToJsonImpl(formatting);
    }
    static string ToJsonImpl<T>(this T o, Formatting formatting) {
      try {
        return JsonConvert.SerializeObject(o, formatting, new Newtonsoft.Json.Converters.StringEnumConverter());
      } catch(Exception exc) {
        var type = typeof(T).FullName;
        var value = o == null ? "null" : string.Join(",", o.ToDictionary().Select(kv => new { kv.Key, kv.Value } + ""));
        throw new Exception(new { type, value } + "", exc);
      }
    }
    public static string ToJson(this ExpandoObject expando) {
      return JsonConvert.SerializeObject(expando, Formatting.Indented);
    }
    public static Dictionary<string, object> ToDictionary(this object instance) {
      return instance.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(instance));
    }
    public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> instance) {
      return instance.ToDictionary(p => p.Key, p => p.Value);
    }
  }
}
