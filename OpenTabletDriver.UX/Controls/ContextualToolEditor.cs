using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Tools;

namespace OpenTabletDriver.UX.Controls
{
    public class ContextualToolEditor : Panel
    {
        private readonly ListBox toolListBox;
        private readonly TextBox nameTextBox;
        private readonly TextBox defaultKeyTextBox;
        private readonly GridView<ToolAppOverride> overridesGrid;
        private readonly Button removeToolButton;
        private readonly Button resetToolButton;
        private readonly TextBox newClassTextBox;
        private readonly TextBox newKeyTextBox;
        private readonly Button detectWindowButton;
        private readonly Button addOverrideButton;

        private ContextualToolsConfiguration? config;
        private ToolDefinition? selectedTool;
        private bool _isUpdating;

        public ContextualToolEditor()
        {
            toolListBox = new ListBox
            {
                Size = new Size(180, 300)
            };
            toolListBox.SelectedIndexChanged += OnToolSelectionChanged;

            var addToolButton = new Button { Text = "+ Add Tool" };
            addToolButton.Click += OnAddToolClicked;

            removeToolButton = new Button { Text = "- Remove", Enabled = false };
            removeToolButton.Click += OnRemoveToolClicked;

            var toolListToolbar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items =
                {
                    new StackLayoutItem(addToolButton, true),
                    new StackLayoutItem(removeToolButton, true)
                }
            };

            var leftPanel = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Width = 200,
                Items =
                {
                    new Label { Text = "Tools", Font = SystemFonts.Bold() },
                    new StackLayoutItem(toolListBox, true),
                    toolListToolbar
                }
            };

            nameTextBox = new TextBox();
            nameTextBox.TextChanged += (s, e) =>
            {
                if (_isUpdating) return;
                if (selectedTool != null && !selectedTool.IsBuiltIn && !string.IsNullOrWhiteSpace(nameTextBox.Text))
                {
                    selectedTool.Name = nameTextBox.Text;
                    var idx = toolListBox.SelectedIndex;
                    if (idx >= 0 && idx < toolListBox.Items.Count)
                    {
                        var icon = selectedTool.IsBuiltIn ? "✦ " : "• ";
                        toolListBox.Items[idx].Text = $"{icon}{selectedTool.Name}";
                    }
                }
            };

            resetToolButton = new Button { Text = "Reset Defaults", Enabled = false };
            resetToolButton.Click += OnResetToolClicked;

            defaultKeyTextBox = new TextBox();
            defaultKeyTextBox.TextChanged += (s, e) =>
            {
                if (_isUpdating) return;
                if (selectedTool != null)
                    selectedTool.DefaultBinding = defaultKeyTextBox.Text;
            };

            overridesGrid = new GridView<ToolAppOverride>
            {
                Size = new Size(-1, 150),
                ShowHeader = true
            };

            overridesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Application (Window Class)",
                DataCell = new TextBoxCell { Binding = Binding.Property<ToolAppOverride, string>(o => o.WindowClass) },
                AutoSize = true
            });

            overridesGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Keybind Sequence",
                DataCell = new TextBoxCell { Binding = Binding.Property<ToolAppOverride, string>(o => o.KeySequence) },
                Width = 160
            });

            var removeOverrideButton = new Button { Text = "- Remove Selected" };
            removeOverrideButton.Click += (sender, e) =>
            {
                if (overridesGrid.SelectedItem is ToolAppOverride item && selectedTool != null)
                {
                    selectedTool.AppOverrides.Remove(item);
                    RefreshOverridesGrid();
                }
            };

            newClassTextBox = new TextBox { PlaceholderText = "e.g. xournalpp or obsidian" };
            newKeyTextBox = new TextBox { PlaceholderText = "e.g. Shift+Control+E", Width = 140 };

            detectWindowButton = new Button { Text = "🎯 Detect Window" };
            detectWindowButton.Click += OnDetectWindowClicked;

            addOverrideButton = new Button { Text = "+ Add Override" };
            addOverrideButton.Click += OnAddOverrideClicked;

            var addOverrideRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Items =
                {
                    new StackLayoutItem(newClassTextBox, true),
                    newKeyTextBox,
                    addOverrideButton,
                    removeOverrideButton,
                    detectWindowButton
                }
            };

            var detailsGroup = new GroupBox
            {
                Text = "Tool Configuration",
                Padding = 10,
                Content = new StackLayout
                {
                    Spacing = 10,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Items =
                            {
                                new Label { Text = "Label:", VerticalAlignment = VerticalAlignment.Center },
                                new StackLayoutItem(nameTextBox, true),
                                resetToolButton
                            }
                        },
                        new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Items =
                            {
                                new Label { Text = "Default Keybind:", VerticalAlignment = VerticalAlignment.Center },
                                new StackLayoutItem(defaultKeyTextBox, true),
                                new Label { Text = "(Fallback when app unmapped)", TextColor = Colors.Gray, VerticalAlignment = VerticalAlignment.Center }
                            }
                        },
                        new Label { Text = "Application Specific Overrides", Font = SystemFonts.Bold() },
                        overridesGrid,
                        addOverrideRow
                    }
                }
            };

            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Padding = 10,
                Items =
                {
                    leftPanel,
                    new StackLayoutItem(detailsGroup, true)
                }
            };

            LoadConfiguration(App.Current.Settings?.ContextualTools);

            App.Current.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(App.Settings) && App.Current.Settings?.ContextualTools != null)
                {
                    Application.Instance.AsyncInvoke(() => LoadConfiguration(App.Current.Settings.ContextualTools));
                }
            };
        }

        public void LoadConfiguration(ContextualToolsConfiguration? toolsConfig)
        {
            if (App.Current.Settings != null)
            {
                config = App.Current.Settings.ContextualTools ??= (toolsConfig ?? ContextualToolsConfiguration.GetDefaults());
            }
            else
            {
                config = toolsConfig ?? ContextualToolsConfiguration.GetDefaults();
            }

            config.Deduplicate();

            RefreshToolList();
            if (toolListBox.Items.Count > 0)
                toolListBox.SelectedIndex = 0;
        }

        private void RefreshToolList()
        {
            _isUpdating = true;
            try
            {
                var prevIndex = toolListBox.SelectedIndex;
                toolListBox.Items.Clear();

                if (config?.Tools == null)
                    return;

                config.Deduplicate();

                foreach (var t in config.Tools)
                {
                    var icon = t.IsBuiltIn ? "✦ " : "• ";
                    toolListBox.Items.Add(new ListItem { Key = t.Id, Text = $"{icon}{t.Name}" });
                }

                if (prevIndex >= 0 && prevIndex < toolListBox.Items.Count)
                    toolListBox.SelectedIndex = prevIndex;
                else if (toolListBox.Items.Count > 0)
                    toolListBox.SelectedIndex = 0;
            }
            finally
            {
                _isUpdating = false;
            }

            OnToolSelectionChanged(this, EventArgs.Empty);
        }

        private void OnToolSelectionChanged(object? sender, EventArgs e)
        {
            _isUpdating = true;
            try
            {
                if (config == null || toolListBox.SelectedIndex < 0 || toolListBox.SelectedIndex >= config.Tools.Count)
                {
                    selectedTool = null;
                    nameTextBox.Text = string.Empty;
                    defaultKeyTextBox.Text = string.Empty;
                    overridesGrid.DataStore = null;
                    removeToolButton.Enabled = false;
                    resetToolButton.Enabled = false;
                    return;
                }

                selectedTool = config.Tools[toolListBox.SelectedIndex];
                nameTextBox.Text = selectedTool.Name;
                nameTextBox.ReadOnly = selectedTool.IsBuiltIn;
                defaultKeyTextBox.Text = selectedTool.DefaultBinding;
                removeToolButton.Enabled = !selectedTool.IsBuiltIn;
                resetToolButton.Enabled = selectedTool.IsBuiltIn;

                RefreshOverridesGrid();
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void RefreshOverridesGrid()
        {
            if (selectedTool == null)
            {
                overridesGrid.DataStore = null;
                return;
            }

            overridesGrid.DataStore = selectedTool.AppOverrides.ToList();
        }

        private void OnAddToolClicked(object? sender, EventArgs e)
        {
            if (config == null) return;

            var dialog = new Dialog<string>
            {
                Title = "Add Custom Tool",
                ClientSize = new Size(320, 130),
                Padding = 10
            };

            var nameInput = new TextBox { PlaceholderText = "Tool Name (e.g. Highlighter)" };
            var okButton = new Button { Text = "Add" };
            okButton.Click += (s, ev) => dialog.Close(nameInput.Text);
            var cancelButton = new Button { Text = "Cancel" };
            cancelButton.Click += (s, ev) => dialog.Close(null);

            dialog.Content = new StackLayout
            {
                Spacing = 10,
                Items =
                {
                    new Label { Text = "Enter name for new tool:" },
                    nameInput,
                    new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalContentAlignment = HorizontalAlignment.Right,
                        Items = { cancelButton, okButton }
                    }
                }
            };

            var result = dialog.ShowModal(this);
            if (!string.IsNullOrWhiteSpace(result))
            {
                var newTool = new ToolDefinition
                {
                    Id = Guid.NewGuid().ToString("N")[..8],
                    Name = result.Trim(),
                    IsBuiltIn = false,
                    DefaultBinding = string.Empty,
                    AppOverrides = new List<ToolAppOverride>()
                };
                config.Tools.Add(newTool);
                RefreshToolList();
                toolListBox.SelectedIndex = config.Tools.Count - 1;
            }
        }

        private void OnRemoveToolClicked(object? sender, EventArgs e)
        {
            if (config == null || selectedTool == null || selectedTool.IsBuiltIn)
                return;

            config.Tools.Remove(selectedTool);
            RefreshToolList();
        }

        private void OnResetToolClicked(object? sender, EventArgs e)
        {
            if (selectedTool == null || !selectedTool.IsBuiltIn) return;

            var defaults = ContextualToolsConfiguration.GetDefaultTools();
            var match = defaults.FirstOrDefault(d => d.Id == selectedTool.Id);
            if (match != null)
            {
                selectedTool.Name = match.Name;
                selectedTool.DefaultBinding = match.DefaultBinding;
                selectedTool.AppOverrides = match.AppOverrides.Select(o => new ToolAppOverride(o.WindowClass, o.KeySequence)).ToList();

                _isUpdating = true;
                try
                {
                    nameTextBox.Text = selectedTool.Name;
                    defaultKeyTextBox.Text = selectedTool.DefaultBinding;
                    RefreshOverridesGrid();
                }
                finally
                {
                    _isUpdating = false;
                }
            }
        }

        private void OnAddOverrideClicked(object? sender, EventArgs e)
        {
            if (selectedTool == null) return;

            var winClass = newClassTextBox.Text?.Trim();
            var keys = newKeyTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(winClass) || string.IsNullOrWhiteSpace(keys))
            {
                MessageBox.Show("Please specify both a Window Class and a Keybind sequence.", MessageBoxType.Warning);
                return;
            }

            var existing = selectedTool.AppOverrides.FirstOrDefault(o =>
                string.Equals(o.WindowClass, winClass, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                existing.KeySequence = keys;
            else
                selectedTool.AppOverrides.Add(new ToolAppOverride(winClass, keys));

            newClassTextBox.Text = string.Empty;
            newKeyTextBox.Text = string.Empty;
            RefreshOverridesGrid();
        }

        private async void OnDetectWindowClicked(object? sender, EventArgs e)
        {
            detectWindowButton.Enabled = false;
            detectWindowButton.Text = "Detecting in 2s...";

            // Delay 2 seconds to allow the user to switch focus to their target window
            await Task.Delay(2000);

            try
            {
                var daemon = App.Driver?.Instance;
                if (daemon != null)
                {
                    var winClass = await daemon.GetActiveWindowClass();
                    if (!string.IsNullOrWhiteSpace(winClass))
                    {
                        newClassTextBox.Text = winClass;
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
                detectWindowButton.Enabled = true;
                detectWindowButton.Text = "🎯 Detect Window";
            }
        }
    }
}
