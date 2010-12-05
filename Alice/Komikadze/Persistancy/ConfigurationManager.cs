using System;
using System.Reflection;
using System.Diagnostics;

namespace WpfPersist.Demo
{

    /// <summary>
    /// provides access to the configuration system internals. 
    /// implementation has been tested with .NET 2.0 only.
    /// </summary>
    public sealed class ConfigurationManager
    {
        #region public

        public static ConfigurationManager Instance
        {
            get
            {
                return instance;
            }
        }

		public string ApplicationConfigUri
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public string ExeLocalConfigDirectory
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public string ExeLocalConfigPath
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public string ExeProductName
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public string ExeProductVersion
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public string ExeRoamingConfigDirectory
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public string ExeRoamingConfigPath
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public string MachineConfigPath
		{
			get
			{
				return (string)GetProperty();
			}
		}

		public bool SetConfigurationSystemInProgress
		{
			get
			{
				return (bool)GetProperty();
			}
		}

		public bool SupportsUserConfig
		{
			get
			{
				return (bool)GetProperty();
			}
		}

		public string UserConfigFilename
		{
			get
			{
				return (string)GetProperty();
			}
		}

		#endregion

        #region private

        private ConfigurationManager()
        {
            Type configMgrType = Type.GetType("System.Configuration.Internal.ConfigurationManagerInternal, System.Configuration, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", true);
            configMgrItf = configMgrType.GetInterface("IConfigurationManagerInternal");
            configMgrInstance = Activator.CreateInstance(configMgrType, true);
        }

        private readonly Type configMgrItf;
        private readonly object configMgrInstance;

        private static readonly ConfigurationManager instance = new ConfigurationManager();

        private object GetProperty()
        {
            string propertyName = new StackFrame(1).GetMethod().Name.Substring(4);
            return configMgrItf.GetProperty(propertyName).GetValue(configMgrInstance, null);
        }

        #endregion
	}
}
