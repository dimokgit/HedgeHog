using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HedgeHog {
  partial class Lib {
    public static string ParseParamRange(this string param) {
      var range = new System.Text.RegularExpressions.Regex(@"(?<from>[\d.]+)-(?<to>[\d.]+),(?<step>[-\d.]+)");
      var m = range.Match(param);
      if(!m.Success)
        return param;
      var l = new List<double>();
      var from = double.Parse(m.Groups["from"].Value);
      var to = double.Parse(m.Groups["to"].Value);
      var step = double.Parse(m.Groups["step"].Value);
      for(var d = from; step > 0 && d <= to || step < 0 && d >= to; d += step)
        l.Add(d);
      return string.Join("\t", l);
    }
    static bool IsGistUrl(string url) {
      Uri uri;
      if(!Uri.TryCreate(url, UriKind.Absolute, out uri))
        return false;
      return uri.Host.ToLower() == "gist.github.com";
    }
    static Dictionary<string, string> MakeJsonParameter(string json) {
      return new Dictionary<string, string> { { "json", json } };
    }
    public static Dictionary<string, string> ReadTestParameters(string testFileName) {
      if(!File.Exists(testFileName))
        return new Dictionary<string, string>();
      var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      var lines = File.ReadAllLines(Path.Combine(path, testFileName));
      return IsValidJson(string.Join(TestParametersRowDelimiter[0]+"", lines),MakeJsonParameter,ReadParametersFromString);
    }
    public static async Task<IEnumerable<Dictionary<string, string>>> ReadTestParametersFromGist(string strategy) {
      var strategies = (await Cloud.GitHub.GistStrategyFindByName(strategy));
      return strategies
        .SelectMany(gist => gist.Files.Select(file => file.Value.Content))
        .IfEmpty(() => { throw new Exception(new { strategy, message = "Not Found" } + ""); })
        .Select(content => IsValidJson(content, MakeJsonParameter, ReadParametersFromString));
    }
    public static readonly string TestParametersRowDelimiter = "\n";
    public static Dictionary<string, string> ReadParametersFromString(string parameters) {
      return IsValidJson(parameters)
        ? ((IDictionary<string, JToken>)JToken.Parse(parameters)).ToDictionary(kv => kv.Key, kv => kv.Value.Value<string>() ?? "")
        : ReadTestParameters(parameters.Split(TestParametersRowDelimiter[0]));
    }
    public static string ReadParametersToString(IEnumerable<string> parameters) {
      return string.Join(Lib.TestParametersRowDelimiter, parameters);
    }
    public static Dictionary<string, string> ReadTestParameters(string[] lines) {
      var separator = "=";
      return lines
        .Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("//"))
        .Select(pl => pl.Trim().Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries))
        //.Where(a => a.Length > 1 && !string.IsNullOrWhiteSpace(a[1]))
        .Select(a => new { name = a[0], value = string.Join(separator, a.Skip(1)).Trim() })
        .ToDictionary(p => p.name, p => ParseParamRange(p.value));
    }
    public delegate Dictionary<string, string> MakeParamsDelegate(string settings);
    public static bool IsValidJson(string strInput) {
      strInput = strInput.Trim();
      if((strInput.StartsWith("{") && strInput.EndsWith("}")) || //For object
          (strInput.StartsWith("[") && strInput.EndsWith("]"))) //For array
      {
        try {
          JToken.Parse(strInput);
          return true;
        } catch(JsonReaderException) { }
      }
      return false;
    }
    public static Dictionary<string, string> IsValidJson(string strInput, MakeParamsDelegate ifJson, MakeParamsDelegate ifNot) {
      strInput = strInput.Trim();
      if((strInput.StartsWith("{") && strInput.EndsWith("}")) || //For object
          (strInput.StartsWith("[") && strInput.EndsWith("]"))) //For array
      {
        try {
          JToken.Parse(strInput);
          return ifJson(strInput);
        } catch(JsonReaderException) { }
      }
      return ifNot(strInput);
    }
  }
}
