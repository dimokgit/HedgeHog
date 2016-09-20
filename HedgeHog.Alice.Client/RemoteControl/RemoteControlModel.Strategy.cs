using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Alice.Store;

namespace HedgeHog.Alice.Client {
  partial class RemoteControlModel {
    public static async Task<IEnumerable<IEnumerable<T>>> ReadStrategies<T>(TradingMacro tmTrader, Func<string, string, string, Uri, string[], T> map) {
      var localMap = MonoidsCore.ToFunc("", "", "", (Uri)null, (name, description, content, uri) => new { name, description, content, uri });
      var strategiesAll = (await Cloud.GitHub.GistStrategies(localMap)).ToArray();
      var activeSettings = tmTrader.TradingMacrosByPair().ToArray(tm => tm.Serialize(true));
      return (from strategies in strategiesAll
              let diffs = strategies.Zip(activeSettings, (strategy, activeSetting) => TradingMacro.ActiveSettingsDiff(strategy.content, activeSetting).ToDictionary())
              .Select((diff, i) => diff.Select(kv => new { diff = i + ":" + kv.Key + "= " + kv.Value[0] + " {" + kv.Value[1] + "}", lev = Lib.LevenshteinDistance(kv.Value[0], kv.Value[1]) }).ToArray())
              //.OrderBy(x=> x.Sum(d=>d.diff.Length))//.ThenBy(x=>x[0], diff.Sum(x => x.lev), strategy.name
              orderby diffs.Sum(d => d.Length)
              select strategies.Select(strategy => map(strategy.name, strategy.description, strategy.content, strategy.uri, diffs.SelectMany(a => a.Select(x => x.diff)).ToArray()))
               );
    }

    //private static string StrategiesPath(string pathEnd = "") {
    //  return Path.Combine(Directory.GetCurrentDirectory(), "..", "Strategies", pathEnd);
    //}

    public static async Task SaveStrategy(TradingMacro tm, string nick) {
      await tm.SaveActiveSettings(nick, TradingMacro.ActiveSettingsStore.Gist);
    }
    public static async Task RemoveStrategy(string name, bool permanent) {
      await Cloud.GitHub.GistStrategyDeleteByName(name, permanent);
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
