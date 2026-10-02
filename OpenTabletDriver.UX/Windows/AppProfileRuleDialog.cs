using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.AppProfiler;

#nullable enable

namespace OpenTabletDriver.UX.Windows
{
    public class AppProfileRuleDialog : Dialog<AppProfileRule?>
    {
        private readonly TextBox _ruleNameTextBox;
        private readonly DropDown _targetTypeDropDown;
        private readonly DropDown _matchTypeDropDown;
        private readonly TextBox _patternTextBox;
        private readonly DropDown _presetDropDown;
        private readonly DropDown _modeDropDown;
        private readonly DropDown _displayMappingDropDown;
        private readonly TextBox _targetMonitorTextBox;
        private readonly Button _detectButton;
        private readonly AppProfileRule? _originalRule;

        public AppProfileRuleDialog(
            AppProfileRule? existingRule,
            IReadOnlyList<string> presetNames,
            IReadOnlyList<(string Key, string Name)> outputModes)
        {
            _originalRule = existingRule;
            Title = existingRule != null ? "✎ Edit Application Rule" : "+ Add Application Rule";
            Resizable = false;
            Padding = new Padding(16);
            MinimumSize = new Size(540, -1);

            _ruleNameTextBox = new TextBox
            {
                Text = existingRule?.Name ?? string.Empty,
                PlaceholderText = "e.g. Krita Drawing"
            };

            _detectButton = new Button { Text = "🎯 Detect Active Target" };
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
                                MessageBox.Show(this, "No active window class, title, or layer surface was detected.", MessageBoxType.Information);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error detecting target: {ex.Message}", MessageBoxType.Error);
                }
                finally
                {
                    _detectButton.Enabled = true;
                    _detectButton.Text = "🎯 Detect Active Target";
                }
            };

            // Target Criteria
            _targetTypeDropDown = new DropDown();
            _targetTypeDropDown.Items.Add(new ListItem { Text = "Window Class (e.g. krita, steam_app_.*)", Key = nameof(RuleTargetType.WindowClass) });
            _targetTypeDropDown.Items.Add(new ListItem { Text = "Window Title (e.g. artwork.kra)", Key = nameof(RuleTargetType.WindowTitle) });
            _targetTypeDropDown.Items.Add(new ListItem { Text = "Wayland Layer Surface (e.g. noctalia, rofi)", Key = nameof(RuleTargetType.LayerNamespace) });
            _targetTypeDropDown.SelectedKey = (existingRule?.TargetType ?? RuleTargetType.WindowClass).ToString();

            _matchTypeDropDown = new DropDown();
            _matchTypeDropDown.Items.Add(new ListItem { Text = "Exact (Case-Insensitive)", Key = nameof(RuleMatchType.Exact) });
            _matchTypeDropDown.Items.Add(new ListItem { Text = "Contains (Substring)", Key = nameof(RuleMatchType.Contains) });
            _matchTypeDropDown.Items.Add(new ListItem { Text = "Regex (Regular Expression)", Key = nameof(RuleMatchType.Regex) });
            _matchTypeDropDown.SelectedKey = (existingRule?.MatchType ?? RuleMatchType.Exact).ToString();

            _patternTextBox = new TextBox
            {
                Text = existingRule?.Pattern ?? string.Empty,
                PlaceholderText = "Pattern to match (e.g. krita, org.kde.*, or layer namespace)"
            };

            // Action Overrides
            _presetDropDown = new DropDown();
            _presetDropDown.Items.Add(new ListItem { Text = "— (Inherit / Unchanged) —", Key = "" });
            foreach (var p in presetNames)
                _presetDropDown.Items.Add(new ListItem { Text = p, Key = p });
            _presetDropDown.SelectedKey = existingRule?.PresetName ?? "";

            _modeDropDown = new DropDown();
            _modeDropDown.Items.Add(new ListItem { Text = "— (Inherit / Unchanged) —", Key = "" });
            foreach (var m in outputModes)
                _modeDropDown.Items.Add(new ListItem { Text = m.Name, Key = m.Key });
            _modeDropDown.SelectedKey = existingRule?.OutputMode ?? "";

            // Display Mapping
            _displayMappingDropDown = new DropDown();
            _displayMappingDropDown.Items.Add(new ListItem { Text = "Inherit (Unchanged)", Key = nameof(RuleDisplayMapping.Inherit) });
            _displayMappingDropDown.Items.Add(new ListItem { Text = "Follow Focus (Active Window Monitor)", Key = nameof(RuleDisplayMapping.FollowFocus) });
            _displayMappingDropDown.Items.Add(new ListItem { Text = "Specific Monitor", Key = nameof(RuleDisplayMapping.SpecificMonitor) });
            _displayMappingDropDown.SelectedKey = (existingRule?.DisplayMapping ?? RuleDisplayMapping.Inherit).ToString();

            _targetMonitorTextBox = new TextBox
            {
                Text = existingRule?.TargetMonitor ?? string.Empty,
                PlaceholderText = "e.g. DP-1, HDMI-A-1, or monitor name",
                Enabled = existingRule?.DisplayMapping == RuleDisplayMapping.SpecificMonitor
            };

            _displayMappingDropDown.SelectedValueChanged += (s, e) =>
            {
                _targetMonitorTextBox.Enabled = _displayMappingDropDown.SelectedKey == nameof(RuleDisplayMapping.SpecificMonitor);
            };

            // Dialog buttons
            var saveButton = new Button { Text = existingRule != null ? "Save Changes" : "Add Rule" };
            saveButton.Click += (s, e) =>
            {
                var pattern = _patternTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    MessageBox.Show(this, "Please specify a pattern to match.", MessageBoxType.Warning);
                    return;
                }

                var ruleName = _ruleNameTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(ruleName))
                    ruleName = pattern;

                Enum.TryParse<RuleTargetType>(_targetTypeDropDown.SelectedKey, out var targetType);
                Enum.TryParse<RuleMatchType>(_matchTypeDropDown.SelectedKey, out var matchType);
                Enum.TryParse<RuleDisplayMapping>(_displayMappingDropDown.SelectedKey, out var displayMapping);

                var presetKey = string.IsNullOrEmpty(_presetDropDown.SelectedKey) ? null : _presetDropDown.SelectedKey;
                var modeKey = string.IsNullOrEmpty(_modeDropDown.SelectedKey) ? null : _modeDropDown.SelectedKey;
                var targetMonitor = string.IsNullOrWhiteSpace(_targetMonitorTextBox.Text) ? null : _targetMonitorTextBox.Text.Trim();

                var resultRule = _originalRule ?? new AppProfileRule();
                resultRule.Name = ruleName;
                resultRule.TargetType = targetType;
                resultRule.MatchType = matchType;
                resultRule.Pattern = pattern;
                resultRule.PresetName = presetKey;
                resultRule.OutputMode = modeKey;
                resultRule.DisplayMapping = displayMapping;
                resultRule.TargetMonitor = targetMonitor;

                Close(resultRule);
            };

            var cancelButton = new Button { Text = "Cancel" };
            cancelButton.Click += (s, e) => Close(null);

            DefaultButton = saveButton;
            AbortButton = cancelButton;

            // Layout
            var nameLayout = new TableLayout
            {
                Spacing = new Size(8, 6),
                Rows =
                {
                    new TableRow(
                        new TableCell(new Label { Text = "Rule Name:", VerticalAlignment = VerticalAlignment.Center }, false),
                        new TableCell(_ruleNameTextBox, true),
                        new TableCell(_detectButton, false)
                    )
                }
            };

            var criteriaGroup = new GroupBox
            {
                Text = "Target Criteria",
                Padding = new Padding(12, 10),
                Content = new TableLayout
                {
                    Spacing = new Size(12, 8),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Target Type:", VerticalAlignment = VerticalAlignment.Center }, _targetTypeDropDown),
                        new TableRow(new Label { Text = "Match Type:", VerticalAlignment = VerticalAlignment.Center }, _matchTypeDropDown),
                        new TableRow(new Label { Text = "Pattern:", VerticalAlignment = VerticalAlignment.Center }, _patternTextBox)
                    }
                }
            };

            var actionsGroup = new GroupBox
            {
                Text = "Action Overrides (Optional)",
                Padding = new Padding(12, 10),
                Content = new TableLayout
                {
                    Spacing = new Size(12, 8),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Mapped Preset:", VerticalAlignment = VerticalAlignment.Center }, _presetDropDown),
                        new TableRow(new Label { Text = "Mapped Output Mode:", VerticalAlignment = VerticalAlignment.Center }, _modeDropDown)
                    }
                }
            };

            var displayGroup = new GroupBox
            {
                Text = "Display Mapping",
                Padding = new Padding(12, 10),
                Content = new TableLayout
                {
                    Spacing = new Size(12, 8),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Display Mapping:", VerticalAlignment = VerticalAlignment.Center }, _displayMappingDropDown),
                        new TableRow(new Label { Text = "Target Monitor:", VerticalAlignment = VerticalAlignment.Center }, _targetMonitorTextBox)
                    }
                }
            };

            var buttonBar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Items = { cancelButton, saveButton }
            };

            Content = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 12,
                Items =
                {
                    nameLayout,
                    criteriaGroup,
                    actionsGroup,
                    displayGroup,
                    new StackLayoutItem(buttonBar, false)
                }
            };
        }
    }
}
