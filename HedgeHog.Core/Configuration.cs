using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class ConfigGlobal {
    public static bool DoRunTest = true;
  }
  public abstract class AppSettingsBase<T> where T: class {
    static AppSettingsBase() {
      if(ConfigGlobal.DoRunTest)
        typeof(T).GetProperties().Select(pi => pi.GetValue(null, null)).Count();
    }

    [Serializable]
    public class MissingKeyException :ApplicationException {
      public MissingKeyException(string key):base($"appSettings section  does not have key [{key}]") {
      }
      protected MissingKeyException(System.Runtime.Serialization.SerializationInfo serializationInfo, System.Runtime.Serialization.StreamingContext streamingContext) :base(serializationInfo,streamingContext){
        throw new NotImplementedException();
      }
    }
    #region Helpers
    protected static string Value([CallerMemberName] string key = "") => String(key);
    protected static T Value<T>([CallerMemberName] string key = "")
      => string.IsNullOrWhiteSpace(String(key))
      ? default(T)
      : (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(String(key));

    protected static string Required([CallerMemberName] string key = "") => Required<string>(key);
    protected static T Required<T>([CallerMemberName] string key = "")
      => string.IsNullOrWhiteSpace(String(key))
      ? throw new MissingKeyException(key)
      : (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(String(key));

    protected static int Int([CallerMemberName] string Caller = "") {
      return Convert.ToInt32(String(Caller));
    }
    protected static string String([CallerMemberName] string Caller = "") {
      return ConfigurationManager.AppSettings[Caller];
    }
    #endregion Helpers
  }
}
