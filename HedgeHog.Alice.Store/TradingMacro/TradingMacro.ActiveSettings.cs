using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity.Core.Objects.DataClasses;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using MoreLinq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace HedgeHog.Alice.Store {
  public class TradingMacroSerializer : JsonConverter {
    readonly bool excludeNotStrategy;
    public TradingMacroSerializer(bool excludeNotStrategy) {
      this.excludeNotStrategy = excludeNotStrategy;
    }
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
      var tm = value as TradingMacro;
      if(tm != null) {
        var categories = TradingMacro.GetActiveSettings2(excludeNotStrategy, tm, (c, i) => new { c, i }, (n, v) => new { n, v }, (key, values) => new { key, values = values.ToArray() }).ToArray();
        writer.WriteStartObject();
        foreach(var category in categories.OrderBy(c => c.key.i).ThenBy(c => c.key.c)) {
          writer.WriteWhitespace("\r\n");
          writer.WriteComment(category.key.c);
          foreach(var v in category.values.OrderBy(v => v.n)) {
            writer.WritePropertyName(v.n);
            serializer.Serialize(writer, v.v);
          }
        }
        writer.WriteEndObject();
      } else {
        JToken t = JToken.FromObject(value);
        if(t.Type != JTokenType.Object) {
          t.WriteTo(writer);
        }
      }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
      throw new NotImplementedException();
    }

    public override bool CanRead {
      get {
        return false;
      }
    }
    public override bool CanConvert(Type objectType) {
      return typeof(TradingMacro).IsAssignableFrom(objectType);
    }
  }

  partial class TradingMacro {
    public string Serialize(bool excludeNotStrategy) {
      var settings = new Newtonsoft.Json.JsonSerializerSettings();
      settings.Converters.Add(new TradingMacroSerializer(excludeNotStrategy));
      settings.Converters.Add(new StringEnumConverter());
      return JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented, settings);

    }
    string ActiveSettingsPath() { return Path.Combine(Lib.CurrentDirectory, "Settings\\{0}({1})_Last.txt".Formater(Pair.Replace("/", ""), PairIndex)); }
    void SaveActiveSettings() {
      try {
        string path = ActiveSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        SaveActiveSettings(path);
      } catch(Exception exc) { Log = exc; }
    }
    public void SaveActiveSettings(string path) {
      try {
        if(string.IsNullOrWhiteSpace(path))
          SaveActiveSettings();
        else {
          File.WriteAllText(path, Serialize(false));
          Log = new Exception("Setting saved to " + path);
        }
      } catch(Exception exc) {
        throw new Exception(new { path } + "", exc);
      }
    }
    public async Task SaveActiveSettings(string path, ActiveSettingsStore store) {
      switch(store) {
        case ActiveSettingsStore.Gist:
          await SaveActiveSettingsToGist(path);
          break;
        case ActiveSettingsStore.Local:
          SaveActiveSettings(path);
          break;
        default:
          throw new ArgumentException("Not implemented", new { store = store + "" } + "");
      }

    }

    private async Task SaveActiveSettingsToGist(string path) {
      var fullName = PairPlain.ToLower() + "(" + BarPeriod + ")" + string.Join("-", TradeConditionsInfo((tc, p, name) => ParseTradeConditionToNick(name)));
      var settings = new[] { Serialize(true) }
      .Concat(TradingMacroOther().Select(tm => tm.Serialize(true)));
      await Cloud.GitHub.GistStrategyAddOrUpdate(path, fullName, settings.ToArray());
    }

    static string[] _excludeDataMembers = new[] {
      Lib.GetLambda<TradingMacro>(tm=>tm.TradingMacroName),
      Lib.GetLambda<TradingMacro>(tm=>tm.TradingGroup),
      Lib.GetLambda<TradingMacro>(tm=>tm.PairIndex)
    }
    .Select(s=>s.ToLower()).ToArray();
    static bool IsMemberExcluded(string name) { return _excludeDataMembers.Contains(name.ToLower()); }

    public IEnumerable<string> GetActiveSettings(bool excludeNotStrategy) {
      return GetActiveSettings(excludeNotStrategy, this);
    }
    public static IEnumerable<string> GetActiveSettings(bool excludeNotStrategy, TradingMacro tm) {
      var type = typeof(TradingMacro);
      var cat = type.GetPropertiesByAttibute(() => (CategoryAttribute)null, (c, pi) => new { c.Category, pi, s = 0 });
      var dm = type.GetPropertiesByAttibute(() => (EdmScalarPropertyAttribute)null, (c, pi) => new { Category = "DataMember", pi, s = 1, key = c.EntityKeyProperty })
        .Where(x => !x.key /*&& !IsMemberExcluded(x.pi.Name)*/)
        .Select(x => new { x.Category, x.pi, x.s });
      var nonStrategy = GetNotStrategyActiveSettings();
      return
        from setting in cat.Concat(dm).Distinct(x => x.pi)
        join ns in nonStrategy on setting.pi.Name equals ns into gss
        where !(excludeNotStrategy && gss.Any())
        where IsNotDnr(setting.pi) && (!excludeNotStrategy || IsStrategy(setting.pi))
        group setting by new { setting.Category, setting.s } into g
        orderby g.Key.s, g.Key.Category
        from g2 in new[] { "//{0}//".Formater(g.Key.Category) }
        .Concat(g
        .Select(p => new { p, v = p.pi.GetValue(tm, null) })
        .Where(x => x.v != null)
        .Select(x => "{0}={1}".Formater(x.p.pi.Name, x.v))
        .OrderBy(s => s))
        .Concat(new[] { Lib.TestParametersRowDelimiter })
        select g2;
    }
    public static IEnumerable<TOut> GetActiveSettings2<TKey, TValue, TOut>(bool excludeNotStrategy, TradingMacro tm, Func<string, int, TKey> grouper, Func<string, object, TValue> valuer, Func<TKey, IEnumerable<TValue>, TOut> outer) {
      var gr = MonoidsCore.ToFunc("", 0, (Category, s) => new { Category, s });
      var type = typeof(TradingMacro);
      var cat = type.GetPropertiesByAttibute(() => (CategoryAttribute)null, (c, pi) => new { c.Category, pi, s = 0 });
      var dm = type.GetPropertiesByAttibute(() => (EdmScalarPropertyAttribute)null, (c, pi) => new { Category = "DataMember", pi, s = 1, key = c.EntityKeyProperty })
        .Where(x => !x.key /*&& !IsMemberExcluded(x.pi.Name)*/)
        .Select(x => new { x.Category, x.pi, x.s });
      var nonStrategy = GetNotStrategyActiveSettings();
      return
        (from setting in cat.Concat(dm).Distinct(x => x.pi)
         join ns in nonStrategy on setting.pi.Name equals ns into gss
         where !excludeNotStrategy || gss.IsEmpty()
         where IsNotDnr(setting.pi) && (!excludeNotStrategy || IsStrategy(setting.pi))
         group setting by grouper(setting.Category, setting.s) into gs
         select outer(gs.Key, gs.Select(v => valuer(v.pi.Name, v.pi.GetValue(tm, null))))
        );
    }
    public static IEnumerable<T> GetActiveProprties<T>(bool excludeNotStrategy, Func<PropertyInfo, string, T> map) {
      var type = typeof(TradingMacro);
      var cat = type.GetPropertiesByAttibute(() => (CategoryAttribute)null, (c, pi) => new { c.Category, pi, s = 0 });
      var dm = type.GetPropertiesByAttibute(() => (EdmScalarPropertyAttribute)null, (c, pi) => new { Category = "DataMember", pi, s = 1, key = c.EntityKeyProperty })
        .Where(x => !x.key /*&& !IsMemberExcluded(x.pi.Name)*/)
        .Select(x => new { x.Category, x.pi, x.s });
      var nonStrategy = GetNotStrategyActiveSettings();
      return
        from setting in cat.Concat(dm).Distinct(x => x.pi)
        join ns in nonStrategy on setting.pi.Name equals ns into gss
        where !(excludeNotStrategy && gss.Any())
        where IsNotDnr(setting.pi) && (!excludeNotStrategy || IsStrategy(setting.pi))
        orderby setting.Category, setting.s
        select map(setting.pi, setting.s + ":" + setting.Category);
    }
    void LoadActiveSettings() { LoadActiveSettings(ActiveSettingsPath()); }
    public enum ActiveSettingsStore {
      Local, Gist
    }
    public async Task LoadActiveSettings(string path, ActiveSettingsStore store) {
      switch(store) {
        case ActiveSettingsStore.Gist:
          TradingMacrosByPair()
            .Zip(await Lib.ReadTestParametersFromGist(path), (tm, settings) => new { tm, settings })
            .ForEach(x => x.tm.LoadActiveSettings(x.settings, path + "[" + x.tm.PairIndex + "]"));
          break;
        case ActiveSettingsStore.Local:
          LoadActiveSettings(path);
          break;
        default:
          throw new ArgumentException("Not implemented", new { store = store + "" } + "");
      }
    }
    public void LoadActiveSettings(string path) {
      var settings = Lib.ReadTestParameters(path);
      if(settings.IsEmpty()) {
        Log = new Exception(new { path, isEmpty = true } + "");
        return;
      }
      LoadActiveSettings(settings, path);
      File.Delete(path);
    }

    public void Patch(string patch) {
      using(var reader = new StringReader(patch)) {
        new JsonSerializer().Populate(reader, this);
      }
    }
    bool IsJsonSettings(Dictionary<string, string> settings) {
      return settings.Count == 1 && settings.ContainsKey("json");
    }
    public void LoadActiveSettings(Dictionary<string, string> settings, string source) {
      try {
        if(IsJsonSettings(settings))
          Patch(settings.First().Value);
        else
          settings.ForEach(tp => {
            try {
              LoadSetting(tp);
            } catch(Exception exc) {
              Log = new Exception(new { tp.Key, tp.Value } + "", exc);
            }
          });
        Log = new Exception("{0}[{2}] Settings loaded from {1}.".Formater(Pair, source, PairIndex));
      } catch(Exception exc) {
        Log = exc;
      }
    }

    public static IDictionary<string, string[]> ActiveSettingsDiff(string settings1, string settings2) {

      var jSetitings1 = (IDictionary<string, JToken>)(Lib.IsValidJson(settings1) ? JToken.Parse(settings1) : JToken.FromObject(Lib.ReadParametersFromString(settings1)));
      var jSetitings2 = (IDictionary<string, JToken>)JObject.Parse(settings2);
      Func<KeyValuePair<string, JToken>, string> fromKV = kv => kv.Value.Value<string>() ?? "";
      var diffs2 = jSetitings1
        .FullGroupJoin(jSetitings2, kv => kv.Key, kv => kv.Key, (key, kv1, kv2) => new { key, kv1, kv2 }, JToken.EqualityComparer)
        .Where(x => x.kv1.Zip(x.kv2, (kv1, kv2) => JToken.DeepEquals(kv1.Value, kv2.Value)).Where(b => b).IsEmpty())
        .OrderBy(x => x.key)
        .Select(x => new { key = x.key.Value<string>(), value = x.kv1.Select(fromKV).DefaultIfEmpty("").Concat(x.kv2.Select(fromKV).DefaultIfEmpty("")).ToArray() })
        .ToDictionary(x => x.key, x => x.value);

      return diffs2;

      string[] exclude = GetNotStrategyActiveSettings();

      var d1 = Lib.ReadParametersFromString(settings1);
      var d2 = Lib.ReadParametersFromString(settings2);
      var diff = from kv1 in d1
                 join kv2 in d2 on kv1.Key equals kv2.Key
                 where !exclude.Contains(kv1.Key) && kv1.Value != kv2.Value
                 select new { key = kv1.Key, value = new[] { kv1.Value, kv2.Value } };
      return diff.OrderBy(d => d.key).ToDictionary(d => d.key, d => d.value);
    }

    private static string[] GetNotStrategyActiveSettings() {
      var exclude1 = typeof(TradingMacro)
        .GetPropertiesByAttibute<IsNotStrategyAttribute>(a => true)
        .Select(t => t.Item2.Name);
      var exclude2 = typeof(TradingMacro)
        .GetPropertiesByAttibute<CategoryAttribute>(a => a.Category.StartsWith("Test"))
        .Select(t => t.Item2.Name);
      var exclude = exclude1.Concat(exclude2).ToArray();
      return exclude;
    }

    public void LoadSetting<T>(KeyValuePair<string, T> tp) {
      if(!IsMemberExcluded(tp.Key))
        this.SetProperty(tp.Key, (object)tp.Value, p => p != null && IsNotDnr(p));
    }

    private static bool IsNotDnr(PropertyInfo p) {
      return p.GetCustomAttribute<DnrAttribute>() == null;
    }
    private static bool IsStrategy(PropertyInfo p) {
      return p.GetCustomAttribute<IsNotStrategyAttribute>() == null;
    }
    private static bool HasNot<A>(PropertyInfo p) where A : Attribute {
      return p.GetCustomAttribute<A>() == null;
    }
  }
}
