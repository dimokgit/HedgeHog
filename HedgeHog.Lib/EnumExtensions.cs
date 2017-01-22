using System;
using System.Collections;
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
    public static ExpandoObject Merge(this ExpandoObject expando, object merge,Func<bool> condition ) {
      return condition == null || condition() ? expando.Merge(merge.ToExpando()) : expando;
    }
    public static ExpandoObject Add(this ExpandoObject expando, object merge) {
      var d = expando as IDictionary<string, object>;
      merge.GetType().GetProperties().ForEach(p => d[p.Name] = p.GetValue(merge));
      return expando;
    }
    public static ExpandoObject Add(this ExpandoObject expando, IDictionary<string,object> merge) {
      var d = expando as IDictionary<string, object>;
      merge.ForEach(p => d[p.Key] = p.Value);
      return expando;
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
    /// <summary>
    /// Extension method that turns a dictionary of string and object to an ExpandoObject
    /// </summary>
    public static ExpandoObject ToExpando(this IDictionary<string, object> dictionary) {
      var expando = new ExpandoObject();
      var expandoDic = (IDictionary<string, object>)expando;

      // go through the items in the dictionary and copy over the key value pairs)
      foreach (var kvp in dictionary) {
        // if the value can also be turned into an ExpandoObject, then do it!
        if (kvp.Value is IDictionary<string, object>) {
          var expandoValue = ((IDictionary<string, object>)kvp.Value).ToExpando();
          expandoDic.Add(kvp.Key, expandoValue);
        } else if (kvp.Value is ICollection) {
          // iterate through the collection and convert any strin-object dictionaries
          // along the way into expando objects
          var itemList = new List<object>();
          foreach (var item in (ICollection)kvp.Value) {
            if (item is IDictionary<string, object>) {
              var expandoItem = ((IDictionary<string, object>)item).ToExpando();
              itemList.Add(expandoItem);
            } else {
              itemList.Add(item);
            }
          }

          expandoDic.Add(kvp.Key, itemList);
        } else {
          expandoDic.Add(kvp);
        }
      }

      return expando;
    }
    public static ExpandoObject CreateExpando(params object[] keyValue) {
      if (keyValue.Length % 2 != 0)
        throw new ArgumentException("keyValue parameter must be an array of [name1,value1,name2,value2...] pairs.");
      var e = (IDictionary<string, object>)new ExpandoObject();
      keyValue.Buffer(2)
        .Select(b => { e.Add(b[0] + "", b[1]); return DateTime.Now; }).Count();
      return e as ExpandoObject;
    }
    public static Dictionary<string, T> ToIgnoreCaseDictionary<U, T>(this IEnumerable<U> values, Func<U, string> keySelector, Func<U, T> valueSelector) {
      return new Dictionary<string, T>(values.ToDictionary(keySelector, valueSelector), StringComparer.OrdinalIgnoreCase);
    }
    public static Dictionary<string, object> ToDictionary(this object instance) {
      return instance.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(instance));
    }
    public static Dictionary<TKey, TValue> ToDictionary<TKey,TValue>(this IEnumerable<KeyValuePair<TKey,TValue>> instance) {
      return instance.ToDictionary(p => p.Key, p => p.Value);
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
