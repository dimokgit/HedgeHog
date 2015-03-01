using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;

namespace HedgeHog {
  public static class EnumsExtensions {
    public static ExpandoObject Merge<T>(this ExpandoObject expando, string key, T value) {
      var e = new ExpandoObject();
      e.AddOrUpdate(key, value);
      return expando.Merge(e);
    }
    public static void AddOrUpdate<T>(this ExpandoObject expando, string key, T value) {
      var d = (IDictionary<string, object>)expando;
      d[key] = value;
    }
    public static ExpandoObject Merge(this ExpandoObject expando, object merge) {
      return expando.Merge(merge.ToExpando());
    }
    public static ExpandoObject Merge(this ExpandoObject expando, ExpandoObject merge) {
      var e = new ExpandoObject();
      var d = (IDictionary<string, object>)e;
      ((IDictionary<string, object>)expando)
        .Concat(((IDictionary<string, object>)merge))
        .ForEach(kv => d[kv.Key] = kv.Value);
      return e;
    }
    public static ExpandoObject ToExpando<T>(this T valaue, string key) {
      var e = new ExpandoObject();
      ((IDictionary<string, object>)e).Add(key, valaue);
      return e;
    }
    public static ExpandoObject ToExpando(this object instance) {
      dynamic e = new ExpandoObject();
      instance.GetType().GetProperties().ForEach(p => ((IDictionary<String, Object>)e).Add(p.Name, p.GetValue(instance)));
      return e;
    }
    public static Dictionary<string, T> ToIgnoreCaseDictionary<U, T>(this IEnumerable<U> values, Func<U, string> keySelector, Func<U, T> valueSelector) {
      return new Dictionary<string, T>(values.ToDictionary(keySelector, valueSelector), StringComparer.OrdinalIgnoreCase);
    }
    public static Dictionary<string, object> ToDictionary(this object instance) {
      return instance.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(instance));
    }
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
