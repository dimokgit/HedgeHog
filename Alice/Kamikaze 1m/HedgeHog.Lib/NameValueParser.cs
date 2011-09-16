using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HedgeHog {
  public class NameValueParser : Dictionary<string, object> {
    public NameValueParser(string text)
      : base(StringComparer.CurrentCultureIgnoreCase) {
      var pattern = "((?<name>[^=]*)=(?<value>[^;]*));";
      var ms = Regex.Matches(text, pattern);
      foreach (Match m in ms)
        Add(m.Groups["name"] + "", m.Groups["value"] + "");
    }
    public string Get(string key) { return Get<string>(key); }
    public int GetInt(string key) { return Get<int>(key); }
    public double GetDouble(string key) { return Get<double>(key); }
    public bool GetBool(string key) {
      var v = this[key] + "";
      return v.Length == 1 ? v == "Y" : Get<bool>(key);
    }
    public DateTime GetDateTime(string key) { return Get<DateTime>(key); }
    public T Get<T>(string key) {
      return (T)Convert.ChangeType(this[key], typeof(T));
    }
  }
}
