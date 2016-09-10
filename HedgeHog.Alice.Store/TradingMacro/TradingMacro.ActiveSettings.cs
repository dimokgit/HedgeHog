using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  partial class TradingMacro {
    string ActiveSettingsPath() { return Lib.CurrentDirectory + "\\Settings\\{0}({1})_Last.txt".Formater(Pair.Replace("/", ""), PairIndex); }
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
          File.WriteAllLines(path, GetActiveSettings().ToArray());
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
      var settings = new[] { Lib.ReadParametersToString(GetActiveSettings(true)) }
      .Concat(TradingMacroOther().Select(tm => Lib.ReadParametersToString(tm.GetActiveSettings(true))));
      await Cloud.GitHub.GistStrategyAddOrUpdate(path, fullName, settings.ToArray());
    }

    public IEnumerable<string> GetActiveSettings(bool excludeNotStrategy = false) {
      var exclude = new[] { "UID", "EntityKey", "SuppRes" };
      var cat = this.GetPropertiesByAttibute(() => (CategoryAttribute)null, (c, pi) => new { c.Category, pi,s=0 });
      var dm = this.GetPropertiesByAttibute(() => (DataMemberAttribute)null, (c, pi) => new { Category="DataMember", pi,s=1 });
      return
        from setting in cat.Concat(dm).Distinct(x=>x.pi)
        where IsNotDnr(setting.pi) && (!excludeNotStrategy || IsStrategy(setting.pi)) && !exclude.Contains(setting.pi.Name)
        group setting by new { setting.Category, setting.s } into g
        orderby g.Key.s,g.Key.Category
        from g2 in new[] { "//{0}//".Formater(g.Key.Category) }
        .Concat(g.Select(p => "{0}={1}".Formater(p.pi.Name, p.pi.GetValue(this, null))).OrderBy(s => s))
        .Concat(new[] { Lib.TestParametersRowDelimiter })
        select g2;
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
            .ForEach(x => x.tm.LoadActiveSettings(x.settings));
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
      LoadActiveSettings(settings);
    }

    public void LoadActiveSettings(Dictionary<string, string> settings) {
      try {
        settings.ForEach(tp => {
          try {
            LoadSetting(tp);
          } catch(Exception exc) {
            Log = new Exception(new { tp.Key, tp.Value } + "", exc);
          }
        });
        Log = new Exception("{0} Settings loaded.".Formater(Pair));
      } catch(Exception exc) {
        Log = exc;
      }
    }

    public static IDictionary<string, string[]> ActiveSettingsDiff(string settings1, string settings2) {
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
      this.SetProperty(tp.Key, (object)tp.Value, p => p != null && IsNotDnr(p) && HasNot<DataMemberAttribute>(p));
    }

    private static bool IsNotDnr(PropertyInfo p) {
      return p.GetCustomAttribute<DnrAttribute>() == null;
    }
    private static bool IsStrategy(PropertyInfo p) {
      return p.GetCustomAttribute<IsNotStrategyAttribute>() == null;
    }
    private static bool HasNot<A>(PropertyInfo p)where A:Attribute {
      return p.GetCustomAttribute<A>() == null;
    }
  }
}
