using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.AppProfiler;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.UX.Controls.Generic.Reflection;

#nullable enable

namespace OpenTabletDriver.UX.Controls
{
    public class AppProfileEditor : Panel
    {
        private bool _isUpdating;
        private UITimer? _telemetryTimer;

        // Telemetry header
        private readonly Label _activeContextLabel = new()
        {
            Text = "Active Context: Connecting to daemon...",
            Font = SystemFonts.Bold(11),
            TextColor = Colors.SteelBlue
        };

        // General & Fallback controls
        private readonly CheckBox _enableCheckBox = new() { Text = "Enable automatic app profiling" };
        private readonly CheckBox _syncFocusCheckBox = new() { Text = "Sync focus on window change (global fallback)" };

        private readonly DropDown _defaultPresetDropDown = new();
        private readonly DropDown _defaultModeDropDown = new();

        // Rules Grid & Actions
        private readonly GridView<AppProfileRule> _rulesGrid = new();
        private readonly Button _newRuleButton = new() { Text = "+ Add Rule..." };
        private readonly Button _editRuleButton = new() { Text = "✎ Edit Rule...", Enabled = false };
        private readonly Button _deleteRuleButton = new() { Text = "- Delete Rule", Enabled = false };
        private readonly Button _moveUpButton = new() { Text = "▲ Move Up", Enabled = false };
        private readonly Button _moveDownButton = new() { Text = "▼ Move Down", Enabled = false };

        private readonly Button _saveButton = new() { Text = "Save Settings" };
        private readonly Label _statusLabel = new() { Text = "" };

        private readonly List<(string Key, string Name)> _outputModes = new();
        private readonly List<string> _presetNames = new();

        private AppProfileRule? _selectedRule;

        public AppProfileEditor()
        {
            InitializeGrid();
            InitializeEventHandlers();
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

            StartTelemetryTimer();
        }

        private void InitializeGrid()
        {
            _rulesGrid.Size = new Size(-1, 300);
            _rulesGrid.ShowHeader = true;

            var enabledBinding = Binding.Property<AppProfileRule, bool?>(r => r.Enabled);
            enabledBinding.Changed += async (s, e) =>
            {
                if (_isUpdating) return;
                await SaveSettingsAsync();
            };

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "On",
                DataCell = new CheckBoxCell { Binding = enabledBinding },
                Width = 40,
                Editable = true
            });

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Rule Name",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileRule, string>(r => r.Name) },
                Width = 160
            });

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Target",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileRule, string>(r => FormatTargetType(r.TargetType)) },
                Width = 110
            });

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Match",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileRule, string>(r => r.MatchType.ToString()) },
                Width = 80
            });

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Pattern",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileRule, string>(r => r.Pattern) },
                Width = 170
            });

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Preset",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileRule, string>(r => string.IsNullOrEmpty(r.PresetName) ? "—" : r.PresetName) },
                Width = 120
            });

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Output Mode",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileRule, string>(r => FormatOutputMode(r.OutputMode)) },
                Width = 140
            });

            _rulesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Display",
                DataCell = new TextBoxCell { Binding = Binding.Property<AppProfileRule, string>(r => FormatDisplayMapping(r)) },
                Width = 130
            });

            _rulesGrid.SelectedRowsChanged += (s, e) =>
            {
                if (_isUpdating) return;
                _selectedRule = _rulesGrid.SelectedItem as AppProfileRule;
                UpdateGridActionButtons();
            };

            _rulesGrid.CellDoubleClick += async (s, e) =>
            {
                await EditSelectedRuleAsync();
            };
        }

        private static string FormatTargetType(RuleTargetType type) => type switch
        {
            RuleTargetType.WindowClass => "Window Class",
            RuleTargetType.WindowTitle => "Window Title",
            RuleTargetType.LayerNamespace => "Layer Surface",
            _ => type.ToString()
        };

        private string FormatOutputMode(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "—";
            var found = _outputModes.FirstOrDefault(o => o.Key == path);
            return !string.IsNullOrEmpty(found.Name) ? found.Name : path.Split('.').Last();
        }

        private static string FormatDisplayMapping(AppProfileRule rule) => rule.DisplayMapping switch
        {
            RuleDisplayMapping.FollowFocus => "Follow Focus",
            RuleDisplayMapping.SpecificMonitor => $"Monitor: {rule.TargetMonitor}",
            _ => "Inherit"
        };

        private void InitializeEventHandlers()
        {
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

            _newRuleButton.Click += async (s, e) => await AddNewRuleAsync();
            _editRuleButton.Click += async (s, e) => await EditSelectedRuleAsync();
            _deleteRuleButton.Click += async (s, e) => await DeleteSelectedRuleAsync();

            _moveUpButton.Click += async (s, e) =>
            {
                var settings = App.Current.AppProfilerSettings;
                if (settings == null || _selectedRule == null) return;
                var idx = settings.Rules.IndexOf(_selectedRule);
                if (idx > 0)
                {
                    settings.Rules.RemoveAt(idx);
                    settings.Rules.Insert(idx - 1, _selectedRule);
                    RefreshGrid();
                    _rulesGrid.SelectedRow = idx - 1;
                    UpdateGridActionButtons();
                    await SaveSettingsAsync();
                }
            };

            _moveDownButton.Click += async (s, e) =>
            {
                var settings = App.Current.AppProfilerSettings;
                if (settings == null || _selectedRule == null) return;
                var idx = settings.Rules.IndexOf(_selectedRule);
                if (idx >= 0 && idx < settings.Rules.Count - 1)
                {
                    settings.Rules.RemoveAt(idx);
                    settings.Rules.Insert(idx + 1, _selectedRule);
                    RefreshGrid();
                    _rulesGrid.SelectedRow = idx + 1;
                    UpdateGridActionButtons();
                    await SaveSettingsAsync();
                }
            };

            _saveButton.Click += async (s, e) => await SaveSettingsAsync();
        }

        private async Task AddNewRuleAsync()
        {
            var dialog = new Windows.AppProfileRuleDialog(null, _presetNames, _outputModes);
            var newRule = dialog.ShowModal(this);
            if (newRule != null)
            {
                var settings = App.Current.AppProfilerSettings ?? new AppProfilerSettings();
                settings.Rules.Add(newRule);
                _selectedRule = newRule;
                RefreshGrid();
                _rulesGrid.SelectedRow = settings.Rules.IndexOf(newRule);
                UpdateGridActionButtons();
                await SaveSettingsAsync();
            }
        }

        private async Task EditSelectedRuleAsync()
        {
            if (_selectedRule == null) return;
            var dialog = new Windows.AppProfileRuleDialog(_selectedRule, _presetNames, _outputModes);
            var result = dialog.ShowModal(this);
            if (result != null)
            {
                RefreshGrid();
                UpdateGridActionButtons();
                await SaveSettingsAsync();
            }
        }

        private async Task DeleteSelectedRuleAsync()
        {
            var settings = App.Current.AppProfilerSettings;
            if (settings == null || _selectedRule == null) return;

            var confirm = MessageBox.Show(
                this,
                $"Are you sure you want to delete rule '{_selectedRule.Name}'?",
                "Delete Rule",
                MessageBoxButtons.YesNo,
                MessageBoxType.Question);

            if (confirm != DialogResult.Yes) return;

            settings.Rules.Remove(_selectedRule);
            _selectedRule = null;
            RefreshGrid();
            UpdateGridActionButtons();
            await SaveSettingsAsync();
        }

        private void UpdateGridActionButtons()
        {
            var settings = App.Current.AppProfilerSettings;
            var hasSelection = _selectedRule != null && settings != null;

            _editRuleButton.Enabled = hasSelection;
            _deleteRuleButton.Enabled = hasSelection;

            if (!hasSelection || settings == null)
            {
                _moveUpButton.Enabled = false;
                _moveDownButton.Enabled = false;
                return;
            }

            var idx = settings.Rules.IndexOf(_selectedRule!);
            _moveUpButton.Enabled = idx > 0;
            _moveDownButton.Enabled = idx >= 0 && idx < settings.Rules.Count - 1;
        }

        private void BuildLayout()
        {
            var telemetryBox = new GroupBox
            {
                Text = "Live Profiler Telemetry",
                Padding = new Padding(12, 10),
                Content = _activeContextLabel
            };

            var optionsBox = new GroupBox
            {
                Text = "General Options & Default Fallbacks",
                Padding = new Padding(14, 12),
                Content = new TableLayout
                {
                    Spacing = new Size(24, 12),
                    Rows =
                    {
                        new TableRow(
                            new TableCell(_enableCheckBox, true),
                            new TableCell(_syncFocusCheckBox, true)
                        ),
                        new TableRow(
                            new TableLayout
                            {
                                Spacing = new Size(10, 6),
                                Rows =
                                {
                                    new TableRow(
                                        new Label { Text = "Default App Profile:", VerticalAlignment = VerticalAlignment.Center },
                                        _defaultPresetDropDown
                                    )
                                }
                            },
                            new TableLayout
                            {
                                Spacing = new Size(10, 6),
                                Rows =
                                {
                                    new TableRow(
                                        new Label { Text = "Default Output Mode:", VerticalAlignment = VerticalAlignment.Center },
                                        _defaultModeDropDown
                                    )
                                }
                            }
                        )
                    }
                }
            };

            var gridToolbar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    _newRuleButton,
                    _editRuleButton,
                    _deleteRuleButton,
                    new StackLayoutItem(null, true),
                    _moveUpButton,
                    _moveDownButton
                }
            };

            var rulesGroup = new GroupBox
            {
                Text = "Application & Layer Profile Rules (Evaluated Top-to-Bottom)",
                Padding = new Padding(14, 12),
                Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 10,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new StackLayoutItem(_rulesGrid, true),
                        gridToolbar
                    }
                }
            };

            var bottomBar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _saveButton, _statusLabel }
            };

            Content = new Scrollable
            {
                Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Padding = new Padding(16, 14),
                    Spacing = 14,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        telemetryBox,
                        optionsBox,
                        new StackLayoutItem(rulesGroup, true),
                        bottomBar
                    }
                }
            };
        }

        private void LoadDropdownOptions()
        {
            _presetNames.Clear();
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

            PopulateDefaultDropdowns();
        }

        private void PopulateDefaultDropdowns()
        {
            _defaultPresetDropDown.Items.Clear();
            _defaultPresetDropDown.Items.Add(new ListItem { Text = "— (None) —", Key = "" });
            foreach (var name in _presetNames)
            {
                _defaultPresetDropDown.Items.Add(new ListItem { Text = name, Key = name });
            }

            _defaultModeDropDown.Items.Clear();
            _defaultModeDropDown.Items.Add(new ListItem { Text = "— (None) —", Key = "" });
            foreach (var (key, name) in _outputModes)
            {
                _defaultModeDropDown.Items.Add(new ListItem { Text = name, Key = key });
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
            if (settings == null || settings.Rules == null)
            {
                _rulesGrid.DataStore = new List<AppProfileRule>();
                return;
            }

            _rulesGrid.DataStore = settings.Rules.ToList();
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
                _statusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private void StartTelemetryTimer()
        {
            _telemetryTimer = new UITimer { Interval = 1.0 };
            _telemetryTimer.Elapsed += async (s, e) =>
            {
                try
                {
                    var daemon = App.Driver?.Instance;
                    if (daemon == null) return;

                    var context = await daemon.GetActiveAppProfileContext();
                    if (context == null) return;

                    var text = "Active Context: ";
                    if (context.IsHoveringLayer && !string.IsNullOrEmpty(context.LayerNamespace))
                    {
                        text += $"[Layer: {context.LayerNamespace}]";
                    }
                    else if (!string.IsNullOrEmpty(context.WindowClass))
                    {
                        text += $"[Class: {context.WindowClass}]";
                    }
                    else
                    {
                        text += "[Desktop / Unfocused]";
                    }

                    if (!string.IsNullOrEmpty(context.MatchedRuleName))
                    {
                        text += $" ➔ Matched Rule: \"{context.MatchedRuleName}\"";
                        if (!string.IsNullOrEmpty(context.ActivePreset))
                            text += $" [Preset: {context.ActivePreset}]";
                    }
                    else
                    {
                        text += " ➔ (Default Fallback / Base Profile)";
                    }

                    _activeContextLabel.Text = text;
                }
                catch
                {
                    // Ignore transient communication errors while daemon is busy
                }
            };
            _telemetryTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _telemetryTimer?.Stop();
                _telemetryTimer?.Dispose();
                _telemetryTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}
