using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
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
      var settings = Lib.ReadParametersToString(GetActiveSettings());
      await Cloud.GitHub.GistStrategyAddOrUpdate(path, fullName, settings);
    }

    public IEnumerable<string> GetActiveSettings() {
      return
        from setting in this.GetPropertiesByAttibute<CategoryAttribute>(a => true)
        where IsNotDnr(setting.Item2)
        group setting by setting.Item1.Category into g
        orderby g.Key
        from g2 in new[] { "//{0}//".Formater(g.Key) }
        .Concat(g.Select(p => "{0}={1}".Formater(p.Item2.Name, p.Item2.GetValue(this, null))).OrderBy(s => s))
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
          LoadActiveSettings(await Lib.ReadTestParametersFromGist(path));
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

    public void LoadSetting<T>(KeyValuePair<string, T> tp) {
      this.SetProperty(tp.Key, (object)tp.Value, p => p != null && IsNotDnr(p));
    }

    private static bool IsNotDnr(PropertyInfo p) {
      return p.GetCustomAttribute<DnrAttribute>() == null;
    }
  }
}
