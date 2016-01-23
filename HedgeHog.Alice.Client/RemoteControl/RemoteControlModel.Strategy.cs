using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Alice.Store;

namespace HedgeHog.Alice.Client {
  partial class RemoteControlModel {
    public static async Task<IEnumerable<T>> ReadStrategies<T>(TradingMacro tm, Func<string, string, string, Uri, string[], T> map) {
      var localMap = MonoidsCore.ToFunc("", "", "", (Uri)null, 0, (name, description, content, uri, index) => new { name, description, content, uri, index });
      var strategies = await Cloud.GitHub.GistStrategies(localMap);
      var activeSettings = Lib.ReadParametersToString(tm.GetActiveSettings(true));
      return (from strategy in strategies
              where strategy.index == 0
              let diffs = TradingMacro.ActiveSettingsDiff(strategy.content, activeSettings).ToDictionary()
              let diff = diffs.Select(kv => new { diff = kv.Key + "= " + kv.Value[0] + " {" + kv.Value[1] + "}", lev = Lib.LevenshteinDistance(kv.Value[0], kv.Value[1]) }).ToArray()
              orderby diff.Length, diff.Sum(x => x.lev), strategy.name
              select map(strategy.name, strategy.description, strategy.content, strategy.uri, diff.Select(x => x.diff).ToArray())
               );
    }

    //private static string StrategiesPath(string pathEnd = "") {
    //  return Path.Combine(Directory.GetCurrentDirectory(), "..", "Strategies", pathEnd);
    //}

    public static async Task SaveStrategy(TradingMacro tm, string nick) {
      await tm.SaveActiveSettings(nick, TradingMacro.ActiveSettingsStore.Gist);
    }
    public static async Task RemoveStrategy(string name,bool permanent) {
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
