using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.UX.Controls.Generic;
using OpenTabletDriver.UX.Controls.Generic.Reflection;

namespace OpenTabletDriver.UX.Controls
{
    public class AppProfileMappingRow
    {
        public string WindowClass { get; set; } = string.Empty;
        public string PresetName { get; set; } = string.Empty;
        public string OutputModeName { get; set; } = string.Empty;
        public string OutputModeFriendlyName { get; set; } = string.Empty;
    }

    public class AppProfileEditor : Panel
    {
        private bool _isUpdating;

        private readonly CheckBox _enableCheckBox = new() { Text = "Enable automatic app profiling" };
        private readonly CheckBox _syncFocusCheckBox = new() { Text = "Sync focus on window change" };

        private readonly DropDown _defaultPresetDropDown = new();
        private readonly DropDown _defaultModeDropDown = new();

        private readonly GridView<AppProfileMappingRow> _mappingsGrid = new();
        private readonly TextBox _classTextBox = new();
        private readonly DropDown _presetDropDown = new();
        private readonly DropDown _modeDropDown = new();

        private readonly Button _detectWindowButton = new() { Text = "🎯 Detect Window" };
        private readonly Button _addRuleButton = new() { Text = "+ Add / Update Rule" };
        private readonly Button _removeRuleButton = new() { Text = "- Remove Selected", Enabled = false };
        private readonly Button _saveButton = new() { Text = "Save Settings" };
        private readonly Label _statusLabel = new() { Text = "" };

        private readonly List<(string Key, string Name)> _outputModes = new();
        private readonly List<string> _presetNames = new();

        public AppProfileEditor()
        {
            Padding = new Padding(10);

            _mappingsGrid.Size = new Size(-1, 200);
            _mappingsGrid.ShowHeader = true;

            _mappingsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Window Class / Application",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileMappingRow, string>(r => r.WindowClass) },
                Width = 220
            });

            _mappingsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Mapped Preset",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileMappingRow, string>(r => r.PresetName) },
                Width = 180
            });

            _mappingsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Mapped Output Mode",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileMappingRow, string>(r => r.OutputModeFriendlyName) },
                Width = 220
            });

            _mappingsGrid.SelectedRowsChanged += (s, e) =>
            {
                if (_isUpdating) return;
                if (_mappingsGrid.SelectedItem is AppProfileMappingRow row)
                {
                    _classTextBox.Text = row.WindowClass;
                    _presetDropDown.SelectedKey = row.PresetName == "—" ? "" : row.PresetName;
                    _modeDropDown.SelectedKey = row.OutputModeName;
                    _removeRuleButton.Enabled = true;
                }
                else
                {
                    _removeRuleButton.Enabled = false;
                }
            };

            _enableCheckBox.CheckedChanged += async (s, e) =>
            {
                if (_isUpdating) return;
                _syncFocusCheckBox.Enabled = _enableCheckBox.Checked ?? false;
                await SaveSettingsAsync();
            };

            _syncFocusCheckBox.CheckedChanged += async (s, e) =>
            {
                if (_isUpdating) return;
                await SaveSettingsAsync();
            };

            _defaultPresetDropDown.SelectedValueChanged += async (s, e) =>
            {
                if (_isUpdating) return;
                await SaveSettingsAsync();
            };

            _defaultModeDropDown.SelectedValueChanged += async (s, e) =>
            {
                if (_isUpdating) return;
                await SaveSettingsAsync();
            };

            _detectWindowButton.Click += async (s, e) =>
            {
                _detectWindowButton.Enabled = false;
                _detectWindowButton.Text = "Detecting in 2s...";
                await Task.Delay(2000);
                try
                {
                    var daemon = App.Driver?.Instance;
                    if (daemon != null)
                    {
                        var winClass = await daemon.GetActiveWindowClass();
                        if (!string.IsNullOrWhiteSpace(winClass))
                        {
                            _classTextBox.Text = winClass;
                        }
                        else
                        {
                            MessageBox.Show("Could not detect active window class.", MessageBoxType.Information);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error detecting window: {ex.Message}", MessageBoxType.Error);
                }
                finally
                {
                    _detectWindowButton.Enabled = true;
                    _detectWindowButton.Text = "🎯 Detect Window";
                }
            };

            _addRuleButton.Click += async (s, e) =>
            {
                var winClass = _classTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(winClass))
                {
                    MessageBox.Show("Please enter or detect a window class first.", MessageBoxType.Warning);
                    return;
                }

                var settings = App.Current.AppProfilerSettings ?? new AppProfilerSettings();
                settings.AppProfiles ??= new Dictionary<string, string>();
                settings.AppOutputModes ??= new Dictionary<string, string>();

                var presetKey = _presetDropDown.SelectedKey;
                if (!string.IsNullOrEmpty(presetKey))
                    settings.AppProfiles[winClass] = presetKey;
                else
                    settings.AppProfiles.Remove(winClass);

                var modeKey = _modeDropDown.SelectedKey;
                if (!string.IsNullOrEmpty(modeKey))
                    settings.AppOutputModes[winClass] = modeKey;
                else
                    settings.AppOutputModes.Remove(winClass);

                await SaveSettingsAsync();
                RefreshGrid();
            };

            _removeRuleButton.Click += async (s, e) =>
            {
                var winClass = _classTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(winClass) && _mappingsGrid.SelectedItem is AppProfileMappingRow row)
                    winClass = row.WindowClass;

                if (string.IsNullOrWhiteSpace(winClass))
                    return;

                var settings = App.Current.AppProfilerSettings;
                if (settings != null)
                {
                    settings.AppProfiles?.Remove(winClass);
                    settings.AppOutputModes?.Remove(winClass);
                    await SaveSettingsAsync();
                    RefreshGrid();
                }
            };

            _saveButton.Click += async (s, e) => await SaveSettingsAsync();

            BuildLayout();

            LoadDropdownOptions();
            LoadSettings();

            App.Current.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(App.AppProfilerSettings))
                {
                    Application.Instance.AsyncInvoke(LoadSettings);
                }
            };
        }

        private void BuildLayout()
        {
            var generalGroup = new Group
            {
                Text = "General Options",
                Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 6,
                    Items =
                    {
                        _enableCheckBox,
                        _syncFocusCheckBox
                    }
                }
            };

            var fallbacksGroup = new Group
            {
                Text = "Default Fallbacks",
                Content = new TableLayout
                {
                    Spacing = new Size(10, 6),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Default App Profile:" }, _defaultPresetDropDown),
                        new TableRow(new Label { Text = "Default Output Mode:" }, _defaultModeDropDown),
                        null
                    }
                }
            };

            var editRuleLayout = new TableLayout
            {
                Spacing = new Size(8, 6),
                Rows =
                {
                    new TableRow(
                        new Label { Text = "Window Class:" },
                        _classTextBox,
                        _detectWindowButton
                    ),
                    new TableRow(
                        new Label { Text = "Preset:" },
                        _presetDropDown,
                        null
                    ),
                    new TableRow(
                        new Label { Text = "Output Mode:" },
                        _modeDropDown,
                        null
                    ),
                    null
                }
            };

            var ruleButtonsLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Items =
                {
                    _addRuleButton,
                    _removeRuleButton
                }
            };

            var mappingsGroup = new Group
            {
                Text = "Application Profiles (Window Mappings)",
                Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 8,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new StackLayoutItem(_mappingsGrid, true),
                        editRuleLayout,
                        ruleButtonsLayout
                    }
                }
            };

            var bottomBar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    _saveButton,
                    _statusLabel
                }
            };

            Content = new Scrollable
            {
                Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 12,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        generalGroup,
                        fallbacksGroup,
                        new StackLayoutItem(mappingsGroup, true),
                        bottomBar
                    }
                }
            };
        }

        private void LoadDropdownOptions()
        {
            _presetNames.Clear();
            _presetNames.Add("(None)");
            try
            {
                AppInfo.PresetManager.Refresh();
                foreach (var p in AppInfo.PresetManager.GetPresets())
                {
                    _presetNames.Add(p.Name);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }

            _outputModes.Clear();
            _outputModes.Add(("", "(None)"));
            try
            {
                var types = AppInfo.PluginManager.GetChildTypes<IOutputMode>()
                    .Where(t => t.FullName != null)
                    .OrderBy(t => t.GetFriendlyName());
                foreach (var t in types)
                {
                    _outputModes.Add((t.FullName!, t.GetFriendlyName()));
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }

            PopulateDropdowns();
        }

        private void PopulateDropdowns()
        {
            _defaultPresetDropDown.Items.Clear();
            _presetDropDown.Items.Clear();
            foreach (var name in _presetNames)
            {
                var key = name == "(None)" ? "" : name;
                _defaultPresetDropDown.Items.Add(new ListItem { Text = name, Key = key });
                _presetDropDown.Items.Add(new ListItem { Text = name, Key = key });
            }

            _defaultModeDropDown.Items.Clear();
            _modeDropDown.Items.Clear();
            foreach (var (key, name) in _outputModes)
            {
                _defaultModeDropDown.Items.Add(new ListItem { Text = name, Key = key });
                _modeDropDown.Items.Add(new ListItem { Text = name, Key = key });
            }
        }

        private void LoadSettings()
        {
            var settings = App.Current.AppProfilerSettings;
            if (settings == null) return;

            _isUpdating = true;
            try
            {
                _enableCheckBox.Checked = settings.EnableAppProfiler;
                _syncFocusCheckBox.Checked = settings.SyncFocus;
                _syncFocusCheckBox.Enabled = settings.EnableAppProfiler;

                _defaultPresetDropDown.SelectedKey = settings.DefaultAppProfile ?? "";
                _defaultModeDropDown.SelectedKey = settings.DefaultOutputMode ?? "";

                RefreshGrid();
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void RefreshGrid()
        {
            var settings = App.Current.AppProfilerSettings;
            if (settings == null)
            {
                _mappingsGrid.DataStore = new List<AppProfileMappingRow>();
                return;
            }

            var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings.AppProfiles != null)
            {
                foreach (var k in settings.AppProfiles.Keys)
                    classes.Add(k);
            }
            if (settings.AppOutputModes != null)
            {
                foreach (var k in settings.AppOutputModes.Keys)
                    classes.Add(k);
            }

            var rows = new List<AppProfileMappingRow>();
            foreach (var cls in classes.OrderBy(c => c))
            {
                var preset = settings.AppProfiles != null && settings.AppProfiles.TryGetValue(cls, out var p) ? p : "—";
                var mode = settings.AppOutputModes != null && settings.AppOutputModes.TryGetValue(cls, out var m) ? m : string.Empty;
                var friendlyMode = "—";
                if (!string.IsNullOrEmpty(mode))
                {
                    var found = _outputModes.FirstOrDefault(o => o.Key == mode);
                    friendlyMode = !string.IsNullOrEmpty(found.Name) ? found.Name : mode.Split('.').Last();
                }

                rows.Add(new AppProfileMappingRow
                {
                    WindowClass = cls,
                    PresetName = preset,
                    OutputModeName = mode,
                    OutputModeFriendlyName = friendlyMode
                });
            }

            _mappingsGrid.DataStore = rows;
        }

        private async Task SaveSettingsAsync()
        {
            if (_isUpdating) return;
            try
            {
                var settings = App.Current.AppProfilerSettings ?? new AppProfilerSettings();
                settings.EnableAppProfiler = _enableCheckBox.Checked ?? false;
                settings.SyncFocus = _syncFocusCheckBox.Checked ?? false;
                settings.DefaultAppProfile = string.IsNullOrEmpty(_defaultPresetDropDown.SelectedKey) ? null : _defaultPresetDropDown.SelectedKey;
                settings.DefaultOutputMode = string.IsNullOrEmpty(_defaultModeDropDown.SelectedKey) ? null : _defaultModeDropDown.SelectedKey;

                await App.Driver.Instance.SetAppProfilerSettings(settings);
                _statusLabel.Text = "Settings saved.";
                _ = Task.Delay(2500).ContinueWith(_ => Application.Instance.AsyncInvoke(() => _statusLabel.Text = ""));
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                MessageBox.Show($"Failed to save app profiler settings: {ex.Message}", MessageBoxType.Error);
            }
        }
    }
}
