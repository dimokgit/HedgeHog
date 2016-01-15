using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Alice.Store;

namespace HedgeHog.Alice.Client {
  partial class RemoteControlModel {
    public static async Task<IEnumerable<T>> ReadStrategies<T>(TradingMacro tm, Func<string, string, Uri, string[], T> map) {
      var localMap = MonoidsCore.ToFunc("", "", "", (Uri)null, 0, (name, description, content, uri, index) => new { name, description, content, uri, index });
      var strategies = await Cloud.GitHub.GistStrategies(localMap);
      var activeSettings = Lib.ReadParametersToString(tm.GetActiveSettings());
      return (from strategy in strategies
              where strategy.index == 0
              let diffs = TradingMacro.ActiveSettingsDiff(strategy.content, activeSettings).ToDictionary()
              let diff = diffs.Select(kv => new { diff = kv.Key + "= " + kv.Value[0] + " {" + kv.Value[1] + "}", lev = Lib.LevenshteinDistance(kv.Value[0], kv.Value[1]) }).ToArray()
              orderby diff.Length, diff.Sum(x => x.lev), strategy.name
              select map(strategy.name, strategy.description, strategy.uri, diff.Select(x => x.diff).ToArray())
               );
      //Func<IDictionary<string,string>, IDictionary<string, string>> joinDicts = (d)
      //(from strategy in strategies
      // from content in strategy.content
      // join )
      //Func<string, string> name = s =>
      //  Regex.Matches(s, @"\{(.+)\}").Cast<Match>().SelectMany(m => m.Groups.Cast<Group>()
      //  .Skip(1).Take(1)
      //  .Select(g => g.Value))
      //  .DefaultIfEmpty(s).First();
      //return Directory.GetFiles(StrategiesPath())
      //  .OrderBy(file => file)
      //  .Select(file => map(name(Path.GetFileNameWithoutExtension(file)), Path.GetFileNameWithoutExtension(file), file));
    }

    //private static string StrategiesPath(string pathEnd = "") {
    //  return Path.Combine(Directory.GetCurrentDirectory(), "..", "Strategies", pathEnd);
    //}

    public static async Task SaveStrategy(TradingMacro tm, string nick) {
      await tm.SaveActiveSettings(nick, TradingMacro.ActiveSettingsStore.Gist);
    }
    public static async Task RemoveStrategy(string name) {
      await Cloud.GitHub.GistStrategyDeleteByName(name);
      //File.Delete(path);
    }
    public static async Task UpdateStrategy(TradingMacro tm, string nick) {
      await tm.SaveActiveSettings(nick, TradingMacro.ActiveSettingsStore.Gist);
      //File.Delete(path);
    }
    public static async Task LoadStrategy(TradingMacro tm, string strategy) {
      await tm.LoadActiveSettings(strategy, TradingMacro.ActiveSettingsStore.Gist);
    }

  }
}
