using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Telerik.Windows.Controls;
using Telerik.Windows.Data;
using Telerik.Windows.Controls.GridView;
using System.Windows.Controls;
using System.Runtime.Serialization;
using System.IO;
using System.IO.IsolatedStorage;

namespace Telerik.Windows.Controls.GridView.Settings
{
    public class RadGridViewSettings
    {
        public RadGridViewSettings()
        {
            //
        }

        public class RadGridViewApplicationSettings : Dictionary<string, object>
        {
            private RadGridViewSettings settings;

            private DataContractSerializer serializer = null;

            public RadGridViewApplicationSettings()
            {
                //
            }

            public RadGridViewApplicationSettings(RadGridViewSettings settings)
            {
                this.settings = settings;

                List<Type> types = new List<Type>();
                types.Add(typeof(List<ColumnSetting>));
                types.Add(typeof(List<FilterSetting>));
                types.Add(typeof(List<GroupSetting>));
                types.Add(typeof(List<SortSetting>));
                types.Add(typeof(List<PropertySetting>));

                this.serializer = new DataContractSerializer(typeof(RadGridViewApplicationSettings), types);
            }

            public string PersistID
            {
                get
                {
                    if (!ContainsKey("PersistID") && settings.grid != null)
                    {
                        this["PersistID"] = settings.grid.Name;
                    }

                    return (string) this["PersistID"];
                }
            }

            public int FrozenColumnCount
            {
                get
                {
                    if (!ContainsKey("FrozenColumnCount"))
                    {
                        this["FrozenColumnCount"] = 0;
                    }

                    return (int) this["FrozenColumnCount"];
                }
                set
                {
                    this["FrozenColumnCount"] = value;
                }
            }

            public List<ColumnSetting> ColumnSettings
            {
                get
                {
                    if (!ContainsKey("ColumnSettings"))
                    {
                        this["ColumnSettings"] = new List<ColumnSetting>();
                    }

                    return (List<ColumnSetting>) this["ColumnSettings"];
                }
            }

            public List<SortSetting> SortSettings
            {
                get
                {
                    if (!ContainsKey("SortSettings"))
                    {
                        this["SortSettings"] = new List<SortSetting>();
                    }

                    return (List<SortSetting>) this["SortSettings"];
                }
            }

            public List<GroupSetting> GroupSettings
            {
                get
                {
                    if (!ContainsKey("GroupSettings"))
                    {
                        this["GroupSettings"] = new List<GroupSetting>();
                    }

                    return (List<GroupSetting>) this["GroupSettings"];
                }
            }

            public List<FilterSetting> FilterSettings
            {
                get
                {
                    if (!ContainsKey("FilterSettings"))
                    {
                        this["FilterSettings"] = new List<FilterSetting>();
                    }

                    return (List<FilterSetting>) this["FilterSettings"];
                }
            }

            public void Reload()
            {
                try
                {
                    using (IsolatedStorageFile file = IsolatedStorageFile.GetUserStoreForApplication())
                    {
                        using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(PersistID, FileMode.Open, file))
                        {
                            if (stream.Length > 0)
                            {
                                RadGridViewApplicationSettings loaded = (RadGridViewApplicationSettings) serializer.ReadObject(stream);

                                FrozenColumnCount = loaded.FrozenColumnCount;

                                ColumnSettings.Clear();
                                foreach (ColumnSetting cs in loaded.ColumnSettings)
                                {
                                    ColumnSettings.Add(cs);
                                }

                                FilterSettings.Clear();
                                foreach (FilterSetting fs in loaded.FilterSettings)
                                {
                                    FilterSettings.Add(fs);
                                }

                                GroupSettings.Clear();
                                foreach (GroupSetting gs in loaded.GroupSettings)
                                {
                                    GroupSettings.Add(gs);
                                }

                                SortSettings.Clear();
                                foreach (SortSetting ss in loaded.SortSettings)
                                {
                                    SortSettings.Add(ss);
                                }
                            }
                        }
                    }
                }
                catch
                {

                }
            }

            public void Reset()
            {
                try
                {
                    using (IsolatedStorageFile file = IsolatedStorageFile.GetUserStoreForApplication())
                    {
                        file.DeleteFile(PersistID);
                    }
                }
                catch
                {
                    //
                }
            }

            public void Save()
            {
                try
                {
                    using (IsolatedStorageFile file = IsolatedStorageFile.GetUserStoreForApplication())
                    {
                        using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(PersistID, FileMode.Create, file))
                        {
                            serializer.WriteObject(stream, this);
                        }
                    }
                }
                catch
                {
                    //
                }
            }
        }

        private RadGridView grid = null;

        public RadGridViewSettings(RadGridView grid)
        {
            this.grid = grid;
        }

        public static readonly DependencyProperty IsEnabledProperty
           = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(RadGridViewSettings),
                new PropertyMetadata(new PropertyChangedCallback(OnIsEnabledPropertyChanged)));

        public static bool GetIsEnabled(DependencyObject dependencyObject)
        {
            return (bool) dependencyObject.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject dependencyObject, bool enabled)
        {
            dependencyObject.SetValue(IsEnabledProperty, enabled);
        }

        private static void OnIsEnabledPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            RadGridView grid = dependencyObject as RadGridView;
            if (grid != null)
            {
                if ((bool) e.NewValue)
                {
                    RadGridViewSettings settings = new RadGridViewSettings(grid);
                    settings.Attach();
                }
            }
        }

        public virtual void LoadState()
        {
            try
            {
                Settings.Reload();
            }
            catch
            {
                Settings.Reset();
            }

            if (this.grid != null)
            {
                grid.FrozenColumnCount = Settings.FrozenColumnCount;

                if (Settings.ColumnSettings.Count > 0)
                {
                    foreach (ColumnSetting setting in Settings.ColumnSettings)
                    {
                        ColumnSetting currentSetting = setting;

                        GridViewDataColumn column = (from c in grid.Columns.OfType<GridViewDataColumn>()
                                                     where c.UniqueName == currentSetting.UniqueName
                                                     select c).FirstOrDefault();

                        if (column != null)
                        {
                            if (currentSetting.DisplayIndex != null)
                            {
                                column.DisplayIndex = currentSetting.DisplayIndex.Value;
                            }

                            if (setting.Width != null)
                            {
                                column.Width = new GridViewLength(setting.Width.Value);
                            }
                        }
                    }
                }
                using (grid.DeferRefresh())
                {
                    if (Settings.SortSettings.Count > 0)
                    {
                        grid.SortDescriptors.Clear();

                        foreach (SortSetting setting in Settings.SortSettings)
                        {
                            Telerik.Windows.Data.SortDescriptor d = new Telerik.Windows.Data.SortDescriptor();
                            d.Member = setting.PropertyName;
                            d.SortDirection = setting.SortDirection;

                            grid.SortDescriptors.Add(d);
                        }
                    }

                    if (Settings.GroupSettings.Count > 0)
                    {
                        grid.GroupDescriptors.Clear();

                        foreach (GroupSetting setting in Settings.GroupSettings)
                        {
                            Telerik.Windows.Data.GroupDescriptor d = new Telerik.Windows.Data.GroupDescriptor();
                            d.Member = setting.PropertyName;
                            d.SortDirection = setting.SortDirection;
                            d.DisplayContent = setting.DisplayContent;

                            grid.GroupDescriptors.Add(d);
                        }
                    }

                    if (Settings.FilterSettings.Count > 0)
                    {
                        foreach (FilterSetting setting in Settings.FilterSettings)
                        {
                            FilterSetting currentSetting = setting;

                            GridViewDataColumn matchingColumn =
                            (from column in grid.Columns.OfType<GridViewDataColumn>()
                             where column.DataMemberBinding.Path.Path == currentSetting.PropertyName
                             select column).FirstOrDefault();

                            if (matchingColumn != null)
                            {
                                ColumnFilterDescriptor cfd = new ColumnFilterDescriptor(matchingColumn);

                                cfd.FieldFilter.Filter1.Member = setting.Filter1.Member;
                                cfd.FieldFilter.Filter1.Operator = setting.Filter1.Operator;
                                cfd.FieldFilter.Filter1.Value = setting.Filter1.Value;

                                cfd.FieldFilter.Filter2.Member = setting.Filter2.Member;
                                cfd.FieldFilter.Filter2.Operator = setting.Filter2.Operator;
                                cfd.FieldFilter.Filter2.Value = setting.Filter2.Value;

                                foreach (Telerik.Windows.Data.FilterDescriptor descriptor in setting.SelectedDistinctValues)
                                {
                                    cfd.DistinctFilter.FilterDescriptors.Add(descriptor);
                                }

                                this.grid.FilterDescriptors.Add(cfd);
                            }
                        }
                    }
                }
            }
        }

        public virtual void ResetState()
        {
            Settings.Reset();
        }

        public virtual void SaveState()
        {
            Settings.Reset();

            if (grid != null)
            {
                if (grid.Columns != null)
                {
                    Settings.ColumnSettings.Clear();

                    foreach (GridViewColumn column in grid.Columns)
                    {
                        if (column is GridViewDataColumn)
                        {
                            GridViewDataColumn dataColumn = (GridViewDataColumn) column;

                            ColumnSetting setting = new ColumnSetting();
                            setting.PropertyName = dataColumn.DataMemberBinding.Path.Path;
                            setting.UniqueName = dataColumn.UniqueName;
                            setting.Header = dataColumn.Header;
                            setting.Width = dataColumn.ActualWidth;
                            setting.DisplayIndex = dataColumn.DisplayIndex;
 
                            Settings.ColumnSettings.Add(setting);
                        }
                    }
                }

                if (grid.FilterDescriptors != null)
                {
                    Settings.FilterSettings.Clear();

                    foreach (IColumnFilterDescriptor cfd in grid.FilterDescriptors.OfType<IColumnFilterDescriptor>())
                    {
                        FilterSetting setting = new FilterSetting();

                        setting.Filter1 = new Telerik.Windows.Data.FilterDescriptor();
                        setting.Filter1.Member = cfd.FieldFilter.Filter1.Member;
                        setting.Filter1.Operator = cfd.FieldFilter.Filter1.Operator;
                        setting.Filter1.Value = cfd.FieldFilter.Filter1.Value;
                        setting.Filter1.MemberType = null;

                        setting.Filter2 = new Telerik.Windows.Data.FilterDescriptor();
                        setting.Filter2.Member = cfd.FieldFilter.Filter2.Member;
                        setting.Filter2.Operator = cfd.FieldFilter.Filter2.Operator;
                        setting.Filter2.Value = cfd.FieldFilter.Filter2.Value;
                        setting.Filter2.MemberType = null;

                        foreach (Telerik.Windows.Data.FilterDescriptor fd in cfd.DistinctFilter.FilterDescriptors.OfType<Telerik.Windows.Data.FilterDescriptor>())
                        {
                            Telerik.Windows.Data.FilterDescriptor newFd = new Telerik.Windows.Data.FilterDescriptor();
                            newFd.Member = fd.Member;
                            newFd.Operator = fd.Operator;
                            newFd.Value = fd.Value;
                            newFd.MemberType = null;
                            setting.SelectedDistinctValues.Add(newFd);
                        }

                        setting.PropertyName = cfd.Column.DataMemberBinding.Path.Path;

                        Settings.FilterSettings.Add(setting);
                    }
                }

                if (grid.SortDescriptors != null)
                {
                    Settings.SortSettings.Clear();

                    foreach (Telerik.Windows.Data.SortDescriptor d in grid.SortDescriptors)
                    {
                        SortSetting setting = new SortSetting();

                        setting.PropertyName = d.Member;
                        setting.SortDirection = d.SortDirection;

                        Settings.SortSettings.Add(setting);
                    }
                }

                if (grid.GroupDescriptors != null)
                {
                    Settings.GroupSettings.Clear();

                    foreach (Telerik.Windows.Data.GroupDescriptor d in grid.GroupDescriptors)
                    {
                        GroupSetting setting = new GroupSetting();

                        setting.PropertyName = d.Member;
                        setting.SortDirection = d.SortDirection;
                        setting.DisplayContent = d.DisplayContent;

                        Settings.GroupSettings.Add(setting);
                    }
                }

                Settings.FrozenColumnCount = grid.FrozenColumnCount;
            }

            Settings.Save();
        }

        private void Attach()
        {
            if (this.grid != null)
            {
                this.grid.LayoutUpdated += new EventHandler(LayoutUpdated);
                this.grid.Loaded += Loaded;
                Application.Current.Exit += Current_Exit;
            }
        }

        void Current_Exit(object sender, EventArgs e)
        {
            SaveState();
        }

        void Loaded(object sender, EventArgs e)
        {
            LoadState();
        }

        void LayoutUpdated(object sender, EventArgs e)
        {
            if (grid.Parent == null)
            {
                SaveState();
            }
        }

        private RadGridViewApplicationSettings gridViewApplicationSettings = null;

        protected virtual RadGridViewApplicationSettings CreateRadGridViewApplicationSettingsInstance()
        {
            return new RadGridViewApplicationSettings(this);
        }

        protected RadGridViewApplicationSettings Settings
        {
            get
            {
                if (gridViewApplicationSettings == null)
                {
                    gridViewApplicationSettings = CreateRadGridViewApplicationSettingsInstance();
                }
                return gridViewApplicationSettings;
            }
        }
    }

    public class PropertySetting
    {
        string _PropertyName;
        public string PropertyName 
        {
            get
            {
                return _PropertyName;
            }
            set
            {
                _PropertyName = value;
            }
        }
    }

    public class SortSetting : PropertySetting
    {
        ListSortDirection _SortDirection;
        public ListSortDirection SortDirection 
        {
            get
            {
                return _SortDirection;
            }
            set
            {
                _SortDirection = value;
            }
        }
    }

    public class GroupSetting : PropertySetting
    {
        object _DisplayContent;
        public object DisplayContent 
        {
            get
            {
                return _DisplayContent;
            }
            set
            {
                _DisplayContent = value;
            }
        }

        ListSortDirection? _SortDirection;
        public ListSortDirection? SortDirection 
        {
            get
            {
                return _SortDirection;
            }
            set
            {
                _SortDirection = value;
            }
        }
    }

    public class FilterSetting : PropertySetting
    {
        List<Telerik.Windows.Data.FilterDescriptor> _SelectedDistinctValues;
        public List<Telerik.Windows.Data.FilterDescriptor> SelectedDistinctValues 
        {
            get
            {
                if (_SelectedDistinctValues == null)
                {
                    _SelectedDistinctValues = new List<Telerik.Windows.Data.FilterDescriptor>();
                }
                return _SelectedDistinctValues;
            }
        }

        Telerik.Windows.Data.FilterDescriptor _Filter1;
        public Telerik.Windows.Data.FilterDescriptor Filter1 
        {
            get
            {
                return _Filter1;
            }
            set
            {
                _Filter1 = value;
            }
        }

        Telerik.Windows.Data.FilterDescriptor _Filter2;
        public Telerik.Windows.Data.FilterDescriptor Filter2 
        {
            get
            {
                return _Filter2;
            }
            set
            {
                _Filter2 = value;
            }
        }
    }

    public class ColumnSetting : PropertySetting
    {
        string _UniqueName;
        public string UniqueName 
        {
            get
            {
                return _UniqueName;
            }
            set
            {
                _UniqueName = value;
            }
        }

        object _Header;
        public object Header 
        {
            get
            {
                return _Header;
            }
            set
            {
                _Header = value;
            }
        }

        double? _Width;
        public double? Width 
        {
            get
            {
                return _Width;
            }
            set
            {
                _Width = value;
            }
        }

        int? _DisplayIndex;
        public int? DisplayIndex
        {
            get
            {
                return _DisplayIndex;
            }
            set
            {
                _DisplayIndex = value;
            }
        }
    }
}
