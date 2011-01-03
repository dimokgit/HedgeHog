using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Data;
using System.ComponentModel;
using System.Configuration;
using System.Xml.Serialization;
using System.Diagnostics;

namespace WpfPersist {
  /// <summary>
  /// provides storage for the UserSettingsExtension.
  /// </summary>
  public static class UserSettingsStorage_ {
    static UserSettingsStorage_() {
      Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
    }

    static void MainWindow_Closing(object sender, CancelEventArgs e) {
      settings.Save();
    }

    private static Settings settings;

    public static StringDictionary Dictionary {
      get {
        if (settings == null) {
          settings = new Settings("XamlPersist");
        }
        return settings.Dictionary;
      }
    }

    #region private types

    [SettingsGroupName("AppPersist")]
    private sealed class Settings : ApplicationSettingsBase {
      internal Settings(string settingsKey)
        : base(settingsKey) {
      }

      [UserScopedSetting]
      public StringDictionary Dictionary {
        get {
          if (this["Dictionary"] == null) {
            this["Dictionary"] = new StringDictionary();
          }
          return ((StringDictionary)(this["Dictionary"]));
        }
        set {
          this["Dictionary"] = value;
        }
      }
    }


    #endregion
  }

  public delegate void SaveDelegate();
  public interface IUserSettingsStorage {
    SaveDelegate Save { get; set; }
  }
  public static class UserSettingsStorage {
    static string SettingsFileName { get { return AppDomain.CurrentDomain.BaseDirectory + "Settings.xml"; } }
    static UserSettingsStorage() {
      Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
      var usWindow = Application.Current.MainWindow as IUserSettingsStorage;
      if (usWindow != null)
        usWindow.Save = Save;
    }

    static void Save() {
      var xs = new XmlSerializer(typeof(StringDictionary));
      var stream = new System.IO.StreamWriter(SettingsFileName, false);
      xs.Serialize(stream, settings.Dictionary);
      stream.Close();
    }
    static void MainWindow_Closing(object sender, CancelEventArgs e) {
      Save();
    }

    public static Settings settings;

    public static StringDictionary Dictionary {
      get {
        if (settings == null) {
          settings = new Settings();
        }
        return settings.Dictionary;
      }
    }

    #region private types

    [SettingsGroupName("AppPersist")]
    public sealed class Settings {
      StringDictionary _dictionary;
      public StringDictionary Dictionary {
        get {
          if (_dictionary == null) {
            if (File.Exists(SettingsFileName)) {
              var s = new System.IO.StreamReader(SettingsFileName);
              _dictionary = new XmlSerializer(typeof(StringDictionary))
                .Deserialize(s) as StringDictionary;
              s.Close();
            } else
              _dictionary = new StringDictionary();
          }
          return _dictionary;
        }
        set {
          _dictionary = value;
        }
      }
    }


    #endregion
  }


  /// <summary>
  /// This class is a markup extension implementation. Markup extension classes exist mainly to provide 
  /// infrastructure support for some aspect of the WPF XAML reader implementation, and the members exposed by 
  /// a markup extension are not typically called from user code. 
  /// This extension supports the x:UserSettings Markup Extension usage from XAML.
  /// 
  /// example usage: 
  ///   Width="{app:UserSettings Default=Auto,Key=MainWindow.Grid.Column1}"
  /// </summary>
  public class UserSettingsExtension : MarkupExtension {
    public UserSettingsExtension() {
    }
    public UserSettingsExtension(string defaultValue):base() {
      this.defaultValue = defaultValue;
    }

    #region private fields

    private string defaultValue;
    private string key;

    #endregion

    #region MarkupExtension overrides

    public override object ProvideValue(IServiceProvider serviceProvider) {
      IProvideValueTarget provideValue = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
      if (provideValue == null || provideValue.TargetObject == null) {
        return null;
        //                throw new NotSupportedException("The IProvideValueTarget is not supported");
      }
      if (provideValue.TargetObject.GetType().FullName == "System.Windows.SharedDp")
        return this;

      DependencyObject targetObject = provideValue.TargetObject as DependencyObject;
      if (targetObject == null) {
        Debug.Fail(string.Format("can't persist type {0}, not a dependency object", provideValue.TargetObject));
        throw new NotSupportedException();
      }

      DependencyProperty targetProperty = provideValue.TargetProperty as DependencyProperty;
      if (targetProperty == null) {
        Debug.Fail(string.Format("can't persist type {0}, not a dependency property", provideValue.TargetProperty));
        throw new NotSupportedException();
      }
      Func<DependencyObject, Window> getParent = null;
      getParent =  new Func<DependencyObject, Window>(ui => {
        var uie = ui;
        while (!(uie is Window) && uie != null)
          uie = LogicalTreeHelper.GetParent(uie);
        if (uie == null)
          return getParent((ui as FrameworkElement).Parent);
        return uie as Window;
      });
      #region key
      if (key == null) {
        IUriContext uriContext = (IUriContext)serviceProvider.GetService(typeof(IUriContext));
        if (uriContext == null) {
          // fallback to default value if no key available
          DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(targetProperty, targetObject.GetType());
          return descriptor.GetValue(targetObject);
        }

        // UIElements have a 'PersistId' property that we can use to generate a unique key
        if (targetObject is UIElement) {
          Func<DependencyObject, string, object> foo = (dp, name) => {
            var en = dp.GetLocalValueEnumerator();
            while(en.MoveNext())
              if (en.Current.Property.Name.ToLower() == name.ToLower()) return en.Current.Value;
            return null;
          };
          string persistID = (foo(targetObject, "Name") ?? ((UIElement)targetObject).PersistId) + "";
          var window = getParent(targetObject as UIElement);
          if (window == null) System.Diagnostics.Debugger.Break();
          var windowName = window.Name;
          key = string.Format("{0}.{1}[{2}.{3}]{4}",
              uriContext.BaseUri.PathAndQuery,
              targetObject.GetType().Name,
              windowName,
              persistID,
              targetProperty.Name);
        }
          // use parent-child relation to generate unique key
        else if (LogicalTreeHelper.GetParent(targetObject) is UIElement) {
          UIElement parent = (UIElement)LogicalTreeHelper.GetParent(targetObject);
          int i = 0;
          foreach (object c in LogicalTreeHelper.GetChildren(parent)) {
            if (c == targetObject) {
              key = string.Format("{0}.{1}[{2}].{3}[{4}].{5}",
                  uriContext.BaseUri.PathAndQuery,
                  parent.GetType().Name, parent.PersistId,
                  targetObject.GetType().Name, i,
                  targetProperty.Name);
              break;
            }
            i++;
          }
        }
        //TODO:should do something clever here to get a good key for tags like GridViewColumn

        if (key == null) {
          Debug.Fail(string.Format("don't know how to automatically get a key for objects of type {0}\n use Key='...' option", targetObject.GetType()));

          // fallback to default value if no key available
          DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(targetProperty, targetObject.GetType());
          return descriptor.GetValue(targetObject);
        } else {
          //Debug.WriteLine(string.Format("key={0}", key));
        }
      } 
      #endregion

      if (!UserSettingsStorage.Dictionary.ContainsKey(key)) {
        UserSettingsStorage.Dictionary[key] = defaultValue;
      }

      object value = ConvertFromString(targetObject, targetProperty, UserSettingsStorage.Dictionary[key]);

      SetBinding(targetObject, targetProperty, key);

      return value;
    }

    #endregion

    #region static functions

    private static void SetBinding(DependencyObject targetObject, DependencyProperty targetProperty, string key) {
      Binding binding = new Binding();
      binding.Mode = BindingMode.OneWayToSource;
      binding.Path = new PropertyPath(string.Format("Dictionary[{0}]", key));
      binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
      binding.Source = new InternalBinder();
      BindingOperations.SetBinding(targetObject, targetProperty, binding);
    }

    private static object ConvertFromString(DependencyObject targetObject, DependencyProperty targetProperty, string stringValue) {
      DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(targetProperty, targetObject.GetType());
      return stringValue == null ? descriptor.GetValue(targetObject) : descriptor.Converter.ConvertFromInvariantString(stringValue);
    }

    #endregion

    #region public properties


    /// <summary>
    /// Gets or sets the key that is used for persistent storage.
    /// make sure that this key is unique for the application.
    /// </summary>
    public string Key {
      get { return key; }
      set { key = value; }
    }

    /// <summary>
    /// the default used when the value cannot be retrieved from persistent storage.
    /// </summary>
    public string Default {
      get { return defaultValue; }
      set { defaultValue = value; }
    }

    #endregion

    #region private types

    public class InternalBinder {
      public StringDictionary Dictionary {
        get {
          return UserSettingsStorage.Dictionary;
        }
      }
    }

    #endregion
  }

  /// <summary>
  /// Implements a collection of strongly typed string keys and values with  
  /// additional XML serializability.
  /// </summary>
  [XmlRoot("dictionary")]
  public class StringDictionary : Dictionary<string, string>, IXmlSerializable {
    #region IXmlSerializable Members

    public System.Xml.Schema.XmlSchema GetSchema() {
      return null;
    }

    public void ReadXml(System.Xml.XmlReader reader) {
      bool wasEmpty = reader.IsEmptyElement;
      reader.Read();
      if (wasEmpty)
        return;

      while (reader.Name == "item") {
        this.Add(reader["key"], reader["value"]);
        reader.Read();
      }
      reader.ReadEndElement();
    }

    public void WriteXml(System.Xml.XmlWriter writer) {
      foreach (string key in this.Keys) {
        writer.WriteStartElement("item");
        writer.WriteAttributeString("key", key);
        writer.WriteAttributeString("value", this[key]);
        writer.WriteEndElement();
      }
    }
    #endregion
  }

}
