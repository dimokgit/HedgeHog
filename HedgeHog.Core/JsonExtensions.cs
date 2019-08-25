using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Net;

namespace HedgeHog.Core {
  public static class JsonExtensions {
    public static IEnumerable<T> LoadJson<T>(string path, Action<Exception> error) =>
      from json in Common.LoadText(path).Catch<string, Exception>(exc => {
        if(error == null) ExceptionDispatchInfo.Capture(exc).Throw();
        error(exc);
        return new string[0];
      })
      from t in json.FromJson<T>(error)
      select t;

    public static string ToJson(this object obj) {
      var settings = new Newtonsoft.Json.JsonSerializerSettings() {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
      };
      settings.Converters.Add(new StringEnumConverter());
      return Newtonsoft.Json.JsonConvert.SerializeObject(obj, settings);
    }
    public static IEnumerable<T> FromJson<T>(this string json) => json.FromJson<T>(null);
    public static IEnumerable<T> FromJson<T>(this string json, Action<Exception> error) {
      var hasError = false;
      var settings = new Newtonsoft.Json.JsonSerializerSettings {
        Error = (s, e) => {
          if(error == null)
            ExceptionDispatchInfo.Capture(e.ErrorContext.Error).Throw();
          e.ErrorContext.Handled = true;
          error(e.ErrorContext.Error);
          hasError = true;
        }
      };
      settings.Converters.Add(new StringEnumConverter());
      var ret= JsonConvert.DeserializeObject<T>(json, settings);
      if(hasError) yield break;
      yield return ret;
    }
    public static bool TryFromJson<T>(this string json, out T v) {
      try {
        v = json.FromJson<T>().Single();
        return true;
      } catch {
        v = default(T);
        return false;
      }
    }

    public static string ToJson<T>(this T o, bool format) => o.ToJson(format ? Formatting.Indented : Formatting.None);
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
    /// <summary>
    /// Perform a deep Copy of the object, using Json as a serialisation method. NOTE: Private members are not cloned using this method.
    /// </summary>
    /// <typeparam name="T">The type of object being copied.</typeparam>
    /// <param name="source">The object instance to copy.</param>
    /// <returns>The copied object.</returns>
    public static T CloneJson<T>(this T source) {
      // Don't serialize a null object, simply return the default for that object
      if(Object.ReferenceEquals(source, null)) {
        return default(T);
      }

      // initialize inner objects individually
      // for example in default constructor some list property initialized with some values,
      // but in 'source' these items are cleaned -
      // without ObjectCreationHandling.Replace default constructor values will be added to result
      var deserializeSettings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };

      return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source), deserializeSettings);
    }
  }
}
