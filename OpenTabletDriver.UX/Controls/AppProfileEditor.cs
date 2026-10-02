using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.AppProfiler;
using OpenTabletDriver.Desktop.Interop.Display;
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

        // Rules Grid
        private readonly GridView<AppProfileRule> _rulesGrid = new();
        private readonly Button _moveUpButton = new() { Text = "▲ Move Up", Enabled = false };
        private readonly Button _moveDownButton = new() { Text = "▼ Move Down", Enabled = false };
        private readonly Button _newRuleButton = new() { Text = "+ New Rule" };
        private readonly Button _deleteRuleButton = new() { Text = "- Delete Rule", Enabled = false };

        // Detail Editor Form
        private readonly TextBox _ruleNameTextBox = new() { PlaceholderText = "e.g. Krita Drawing" };
        private readonly DropDown _targetTypeDropDown = new();
        private readonly DropDown _matchTypeDropDown = new();
        private readonly TextBox _patternTextBox = new() { PlaceholderText = "e.g. krita, steam_app_.*, or layer namespace" };
        private readonly DropDown _presetDropDown = new();
        private readonly DropDown _modeDropDown = new();
        private readonly DropDown _displayMappingDropDown = new();
        private readonly TextBox _targetMonitorTextBox = new() { PlaceholderText = "e.g. DP-1, HDMI-A-1", Enabled = false };

        private readonly Button _detectButton = new() { Text = "🎯 Detect Active Target" };
        private readonly Button _applyRuleButton = new() { Text = "Apply Rule to List" };
        private readonly Button _saveButton = new() { Text = "Save Settings" };
        private readonly Label _statusLabel = new() { Text = "" };

        private readonly List<(string Key, string Name)> _outputModes = new();
        private readonly List<string> _presetNames = new();

        private AppProfileRule? _selectedRule;

        public AppProfileEditor()
        {
            Padding = new Padding(10);

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
            _rulesGrid.Size = new Size(-1, 220);
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
                Width = 140
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
                Width = 160
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
                UpdateEditorFieldsFromSelectedRule();
                UpdateGridActionButtons();
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

            _displayMappingDropDown.SelectedValueChanged += (s, e) =>
            {
                var isSpecific = _displayMappingDropDown.SelectedKey == nameof(RuleDisplayMapping.SpecificMonitor);
                _targetMonitorTextBox.Enabled = isSpecific;
            };

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
                    await SaveSettingsAsync();
                }
            };

            _newRuleButton.Click += (s, e) =>
            {
                _rulesGrid.SelectedRow = -1;
                _selectedRule = null;
                _ruleNameTextBox.Text = string.Empty;
                _targetTypeDropDown.SelectedKey = nameof(RuleTargetType.WindowClass);
                _matchTypeDropDown.SelectedKey = nameof(RuleMatchType.Exact);
                _patternTextBox.Text = string.Empty;
                _presetDropDown.SelectedKey = "";
                _modeDropDown.SelectedKey = "";
                _displayMappingDropDown.SelectedKey = nameof(RuleDisplayMapping.Inherit);
                _targetMonitorTextBox.Text = string.Empty;
                _targetMonitorTextBox.Enabled = false;
                _ruleNameTextBox.Focus();
                UpdateGridActionButtons();
            };

            _deleteRuleButton.Click += async (s, e) =>
            {
                var settings = App.Current.AppProfilerSettings;
                if (settings == null || _selectedRule == null) return;
                settings.Rules.Remove(_selectedRule);
                _selectedRule = null;
                RefreshGrid();
                UpdateGridActionButtons();
                await SaveSettingsAsync();
            };

            _detectButton.Click += async (s, e) =>
            {
                _detectButton.Enabled = false;
                _detectButton.Text = "Detecting in 2s...";
                await Task.Delay(2000);
                try
                {
                    var daemon = App.Driver?.Instance;
                    if (daemon != null)
                    {
                        var context = await daemon.GetActiveAppProfileContext();
                        if (context != null)
                        {
                            if (context.IsHoveringLayer && !string.IsNullOrEmpty(context.LayerNamespace))
                            {
                                _targetTypeDropDown.SelectedKey = nameof(RuleTargetType.LayerNamespace);
                                _patternTextBox.Text = context.LayerNamespace;
                                if (string.IsNullOrWhiteSpace(_ruleNameTextBox.Text))
                                    _ruleNameTextBox.Text = $"Layer: {context.LayerNamespace}";
                            }
                            else if (!string.IsNullOrEmpty(context.WindowClass))
                            {
                                _targetTypeDropDown.SelectedKey = nameof(RuleTargetType.WindowClass);
                                _patternTextBox.Text = context.WindowClass;
                                if (string.IsNullOrWhiteSpace(_ruleNameTextBox.Text))
                                    _ruleNameTextBox.Text = context.WindowClass;
                            }
                            else if (!string.IsNullOrEmpty(context.WindowTitle))
                            {
                                _targetTypeDropDown.SelectedKey = nameof(RuleTargetType.WindowTitle);
                                _patternTextBox.Text = context.WindowTitle;
                                if (string.IsNullOrWhiteSpace(_ruleNameTextBox.Text))
                                    _ruleNameTextBox.Text = context.WindowTitle;
                            }
                            else
                            {
                                MessageBox.Show("No active window class, title, or layer surface was detected.", MessageBoxType.Information);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error detecting target: {ex.Message}", MessageBoxType.Error);
                }
                finally
                {
                    _detectButton.Enabled = true;
                    _detectButton.Text = "🎯 Detect Active Target";
                }
            };

            _applyRuleButton.Click += async (s, e) =>
            {
                var pattern = _patternTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    MessageBox.Show("Please specify a pattern to match.", MessageBoxType.Warning);
                    return;
                }

                var ruleName = _ruleNameTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(ruleName))
                    ruleName = pattern;

                var settings = App.Current.AppProfilerSettings ?? new AppProfilerSettings();

                Enum.TryParse<RuleTargetType>(_targetTypeDropDown.SelectedKey, out var targetType);
                Enum.TryParse<RuleMatchType>(_matchTypeDropDown.SelectedKey, out var matchType);
                Enum.TryParse<RuleDisplayMapping>(_displayMappingDropDown.SelectedKey, out var displayMapping);

                var presetKey = string.IsNullOrEmpty(_presetDropDown.SelectedKey) ? null : _presetDropDown.SelectedKey;
                var modeKey = string.IsNullOrEmpty(_modeDropDown.SelectedKey) ? null : _modeDropDown.SelectedKey;
                var targetMonitor = string.IsNullOrWhiteSpace(_targetMonitorTextBox.Text) ? null : _targetMonitorTextBox.Text.Trim();

                if (_selectedRule != null && settings.Rules.Contains(_selectedRule))
                {
                    _selectedRule.Name = ruleName;
                    _selectedRule.TargetType = targetType;
                    _selectedRule.MatchType = matchType;
                    _selectedRule.Pattern = pattern;
                    _selectedRule.PresetName = presetKey;
                    _selectedRule.OutputMode = modeKey;
                    _selectedRule.DisplayMapping = displayMapping;
                    _selectedRule.TargetMonitor = targetMonitor;
                }
                else
                {
                    var newRule = new AppProfileRule
                    {
                        Name = ruleName,
                        Enabled = true,
                        TargetType = targetType,
                        MatchType = matchType,
                        Pattern = pattern,
                        PresetName = presetKey,
                        OutputMode = modeKey,
                        DisplayMapping = displayMapping,
                        TargetMonitor = targetMonitor
                    };
                    settings.Rules.Add(newRule);
                    _selectedRule = newRule;
                }

                RefreshGrid();
                if (_selectedRule != null)
                {
                    _rulesGrid.SelectedRow = settings.Rules.IndexOf(_selectedRule);
                }
                await SaveSettingsAsync();
            };

            _saveButton.Click += async (s, e) => await SaveSettingsAsync();
        }

        private void UpdateEditorFieldsFromSelectedRule()
        {
            if (_selectedRule == null) return;

            _ruleNameTextBox.Text = _selectedRule.Name;
            _targetTypeDropDown.SelectedKey = _selectedRule.TargetType.ToString();
            _matchTypeDropDown.SelectedKey = _selectedRule.MatchType.ToString();
            _patternTextBox.Text = _selectedRule.Pattern;
            _presetDropDown.SelectedKey = _selectedRule.PresetName ?? "";
            _modeDropDown.SelectedKey = _selectedRule.OutputMode ?? "";
            _displayMappingDropDown.SelectedKey = _selectedRule.DisplayMapping.ToString();
            _targetMonitorTextBox.Text = _selectedRule.TargetMonitor ?? "";
            _targetMonitorTextBox.Enabled = _selectedRule.DisplayMapping == RuleDisplayMapping.SpecificMonitor;
        }

        private void UpdateGridActionButtons()
        {
            var settings = App.Current.AppProfilerSettings;
            if (settings == null || _selectedRule == null)
            {
                _moveUpButton.Enabled = false;
                _moveDownButton.Enabled = false;
                _deleteRuleButton.Enabled = false;
                return;
            }

            var idx = settings.Rules.IndexOf(_selectedRule);
            _moveUpButton.Enabled = idx > 0;
            _moveDownButton.Enabled = idx >= 0 && idx < settings.Rules.Count - 1;
            _deleteRuleButton.Enabled = idx >= 0;
        }

        private void BuildLayout()
        {
            var telemetryBox = new GroupBox
            {
                Text = "Live Profiler Telemetry",
                Content = new StackLayout
                {
                    Padding = new Padding(8, 6),
                    Orientation = Orientation.Vertical,
                    Spacing = 4,
                    Items = { _activeContextLabel }
                }
            };

            var generalGroup = new GroupBox
            {
                Text = "General Options",
                Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 6,
                    Items = { _enableCheckBox, _syncFocusCheckBox }
                }
            };

            var fallbacksGroup = new GroupBox
            {
                Text = "Default Fallbacks (When No Rule Matches)",
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

            var gridToolbar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Items =
                {
                    _newRuleButton,
                    _deleteRuleButton,
                    new StackLayoutItem(null, true),
                    _moveUpButton,
                    _moveDownButton
                }
            };

            var ruleForm = new TableLayout
            {
                Spacing = new Size(8, 6),
                Rows =
                {
                    new TableRow(new Label { Text = "Rule Name:" }, _ruleNameTextBox, _detectButton),
                    new TableRow(new Label { Text = "Target Type:" }, _targetTypeDropDown, new Label { Text = "Target criteria" }),
                    new TableRow(new Label { Text = "Match Type:" }, _matchTypeDropDown, new Label { Text = "Matching algorithm" }),
                    new TableRow(new Label { Text = "Pattern:" }, _patternTextBox, null),
                    new TableRow(new Label { Text = "Mapped Preset:" }, _presetDropDown, null),
                    new TableRow(new Label { Text = "Mapped Output Mode:" }, _modeDropDown, null),
                    new TableRow(new Label { Text = "Display Mapping:" }, _displayMappingDropDown, null),
                    new TableRow(new Label { Text = "Target Monitor:" }, _targetMonitorTextBox, null),
                    new TableRow(null, _applyRuleButton, null),
                    null
                }
            };

            var rulesGroup = new GroupBox
            {
                Text = "Application & Layer Profile Rules (Evaluated Top-to-Bottom)",
                Content = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 8,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new StackLayoutItem(_rulesGrid, true),
                        gridToolbar,
                        new GroupBox { Text = "Rule Configuration", Content = ruleForm }
                    }
                }
            };

            var bottomBar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _saveButton, _statusLabel }
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
                        telemetryBox,
                        generalGroup,
                        fallbacksGroup,
                        new StackLayoutItem(rulesGroup, true),
                        bottomBar
                    }
                }
            };
        }

        private void LoadDropdownOptions()
        {
            _targetTypeDropDown.Items.Clear();
            _targetTypeDropDown.Items.Add(new ListItem { Text = "Window Class (e.g. krita)", Key = nameof(RuleTargetType.WindowClass) });
            _targetTypeDropDown.Items.Add(new ListItem { Text = "Window Title (e.g. artwork.kra)", Key = nameof(RuleTargetType.WindowTitle) });
            _targetTypeDropDown.Items.Add(new ListItem { Text = "Wayland Layer Surface (e.g. noctalia)", Key = nameof(RuleTargetType.LayerNamespace) });
            _targetTypeDropDown.SelectedKey = nameof(RuleTargetType.WindowClass);

            _matchTypeDropDown.Items.Clear();
            _matchTypeDropDown.Items.Add(new ListItem { Text = "Exact (Case-Insensitive)", Key = nameof(RuleMatchType.Exact) });
            _matchTypeDropDown.Items.Add(new ListItem { Text = "Contains (Substring)", Key = nameof(RuleMatchType.Contains) });
            _matchTypeDropDown.Items.Add(new ListItem { Text = "Regex (Regular Expression)", Key = nameof(RuleMatchType.Regex) });
            _matchTypeDropDown.SelectedKey = nameof(RuleMatchType.Exact);

            _displayMappingDropDown.Items.Clear();
            _displayMappingDropDown.Items.Add(new ListItem { Text = "Inherit (Unchanged)", Key = nameof(RuleDisplayMapping.Inherit) });
            _displayMappingDropDown.Items.Add(new ListItem { Text = "Follow Window Focus", Key = nameof(RuleDisplayMapping.FollowFocus) });
            _displayMappingDropDown.Items.Add(new ListItem { Text = "Specific Monitor Name/ID", Key = nameof(RuleDisplayMapping.SpecificMonitor) });
            _displayMappingDropDown.SelectedKey = nameof(RuleDisplayMapping.Inherit);

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
                MessageBox.Show($"Failed to save app profiler settings: {ex.Message}", MessageBoxType.Error);
            }
        }

        private void StartTelemetryTimer()
        {
            _telemetryTimer = new UITimer { Interval = 1.0 };
            _telemetryTimer.Elapsed += async (s, e) =>
            {
                if (!Visible || App.Driver?.Instance == null) return;
                try
                {
                    var ctx = await App.Driver.Instance.GetActiveAppProfileContext();
                    if (ctx != null)
                    {
                        string contextDesc = ctx.IsHoveringLayer
                            ? $"[Layer: {ctx.LayerNamespace}]"
                            : $"[Class: {(string.IsNullOrEmpty(ctx.WindowClass) ? "(Desktop)" : ctx.WindowClass)}]";

                        string matchDesc = !string.IsNullOrEmpty(ctx.MatchedRuleName)
                            ? $"➔ Matched: \"{ctx.MatchedRuleName}\""
                            : "➔ (Default Fallback / Base Profile)";

                        string profileDesc = !string.IsNullOrEmpty(ctx.ActivePreset)
                            ? $" [Preset: {ctx.ActivePreset}]"
                            : "";

                        _activeContextLabel.Text = $"Active Context: {contextDesc} {matchDesc}{profileDesc}";
                    }
                }
                catch
                {
                    // Ignore transient IPC timeouts
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
