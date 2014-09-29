using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class Persistance {
    //ApplicationSettingsBase _settings;
    //public Persistance(ApplicationSettingsBase settings) {
    //  this._settings = settings;
    //}
    public static void Load(object parent, ApplicationSettingsBase settings) {
      ProcessPersists(parent, settings, LoadProperty);
    }
    public static void Save(object parent, ApplicationSettingsBase settings) {
      ProcessPersists(parent,settings, SetSetting);
      settings.Save();
    }
    public static void SetSetting(object context, ApplicationSettingsBase settings, PropertyInfo p) {
      try {
        settings[p.Name] = p.GetValue(context);
      } catch (Exception) {
        //Framework.EventLogger.LogMe("JIRA Helpdesk", new { property = p.Name, exception = exc + "" }, EventLogEntryType.Error, 1);
        throw;
      }
    }
    public static void ProcessPersists(object parent, ApplicationSettingsBase settings, Action<object,ApplicationSettingsBase,PropertyInfo> processor) {
      parent.GetType().GetProperties().ToList()
        .Where(p => p.GetCustomAttributes(typeof(PersistAttribute), false).Any())
        .ToList().ForEach(p => processor(parent, settings, p));
    }

    static bool AddProperty(ApplicationSettingsBase settings,string settingName, Type type, params object[] defaultValue) {
      if (settings.Properties[settingName] == null) {
        var p = new SettingsProperty(settingName);
        p.Provider = settings.Providers["LocalFileSettingsProvider"];
        if (p.Provider == null) throw new Exception(new { LocalFileSettingsProvider = "Is missing" } + "");
        p.Attributes.Add(typeof(UserScopedSettingAttribute), new UserScopedSettingAttribute());
        p.PropertyType = type;
        settings.Properties.Add(p);
        if (settings[settingName] == null || defaultValue.Any()) {
          settings[settingName] = defaultValue.DefaultIfEmpty(GetDefault(type)).Single();
          return true;
        }
      }
      return false;
    }
    static void SetProperty(object parent, ApplicationSettingsBase settings,PropertyInfo p) {
      AddProperty(settings, p.Name, p.PropertyType, p.GetValue(parent));
    }
    public static void LoadProperty(object parent, ApplicationSettingsBase settings,PropertyInfo p) {
      if (!AddProperty(settings, p.Name, p.PropertyType))
        p.SetValue(parent, settings[p.Name]);
    }
    public static object GetDefault(Type type) {
      if (type.IsValueType) {
        return Activator.CreateInstance(type);
      }
      return null;
    }
    public class PersistAttribute : Attribute { }
  }
}
