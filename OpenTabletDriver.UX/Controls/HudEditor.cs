using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Hud;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Controls.Generic;

namespace OpenTabletDriver.UX.Controls
{
    public class HudEditor : Panel
    {
        private static readonly string[] SlicePositions =
        {
            "0: Top (12:00)",
            "1: Top-Right (1:30)",
            "2: Right (3:00)",
            "3: Bottom-Right (4:30)",
            "4: Bottom (6:00)",
            "5: Bottom-Left (7:30)",
            "6: Left (9:00)",
            "7: Top-Left (10:30)"
        };

        private bool _isUpdating;

        private readonly DropDown _formFactorDropDown = new();
        private readonly DropDown _themeStyleDropDown = new();
        private readonly DropDown _fontFamilyDropDown = new();
        private readonly TextBox _fontFamilyTextBox = new() { Width = 140, PlaceholderText = "Font family name" };
        private readonly FloatSlider _radiusSlider = new() { Minimum = 80, Maximum = 250, StepSize = 5 };
        private readonly FloatSlider _deadzoneSlider = new() { Minimum = 15, Maximum = 80, StepSize = 5 };
        private readonly FloatSlider _opacitySlider = new() { Minimum = 30, Maximum = 100, StepSize = 5 };
        private readonly CheckBox _anchorCheckBox = new() { Text = "Anchor pointer at gesture start during HUD flick" };
        private readonly CheckBox _rightHandedCheckBox = new() { Text = "Right-handed layout (Quick Bar)" };

        private readonly List<TextBox> _labelBoxes = new();
        private readonly List<BindingDisplay> _bindingDisplays = new();

        public HudEditor()
        {
            // Form Factor options
            _formFactorDropDown.Items.Add(new ListItem { Text = "Radial Menu (Pie)", Key = "0" });
            _formFactorDropDown.Items.Add(new ListItem { Text = "Quick Bar (Dock)", Key = "1" });
            _formFactorDropDown.SelectedValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                if (int.TryParse(_formFactorDropDown.SelectedKey, out var ff))
                {
                    CurrentConfig.FormFactor = (HudFormFactor)ff;
                }
            };

            // Theme Style options
            _themeStyleDropDown.Items.Add(new ListItem { Text = "Translucent (Glass & Blur)", Key = "0" });
            _themeStyleDropDown.Items.Add(new ListItem { Text = "Solid (Matte Dark)", Key = "1" });
            _themeStyleDropDown.SelectedValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                if (int.TryParse(_themeStyleDropDown.SelectedKey, out var ts))
                {
                    CurrentConfig.ThemeStyle = (HudThemeStyle)ts;
                }
            };

            // Font options
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "Sans (System Default)", Key = "Sans" });
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "Inter", Key = "Inter" });
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "SF Pro Display", Key = "SF Pro Display" });
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "Segoe UI", Key = "Segoe UI" });
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "Roboto", Key = "Roboto" });
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "Cantarell", Key = "Cantarell" });
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "Fira Sans", Key = "Fira Sans" });
            _fontFamilyDropDown.Items.Add(new ListItem { Text = "Custom...", Key = "Custom" });

            _fontFamilyDropDown.SelectedValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                var key = _fontFamilyDropDown.SelectedKey;
                if (key != "Custom" && !string.IsNullOrEmpty(key))
                {
                    _fontFamilyTextBox.Text = key;
                    CurrentConfig.FontFamily = key;
                }
            };

            _fontFamilyTextBox.TextChanged += (s, e) =>
            {
                if (_isUpdating) return;
                CurrentConfig.FontFamily = string.IsNullOrWhiteSpace(_fontFamilyTextBox.Text) ? "Sans" : _fontFamilyTextBox.Text.Trim();
            };

            // Slider handlers
            _radiusSlider.ValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                CurrentConfig.Radius = _radiusSlider.Value;
            };

            _deadzoneSlider.ValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                CurrentConfig.DeadzoneRadius = _deadzoneSlider.Value;
            };

            _opacitySlider.ValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                CurrentConfig.Opacity = _opacitySlider.Value / 100f;
            };

            _anchorCheckBox.CheckedChanged += (s, e) =>
            {
                if (_isUpdating) return;
                CurrentConfig.KeepCursorAnchored = _anchorCheckBox.Checked ?? true;
            };

            _rightHandedCheckBox.CheckedChanged += (s, e) =>
            {
                if (_isUpdating) return;
                CurrentConfig.RightHanded = _rightHandedCheckBox.Checked ?? true;
            };

            // Build layout
            var modeGroup = new Group
            {
                Text = "HUD Mode & Geometry",
                Content = new StackLayout
                {
                    Spacing = 8,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new Group
                        {
                            Text = "Form Factor",
                            Orientation = Orientation.Horizontal,
                            Content = _formFactorDropDown
                        },
                        new Group
                        {
                            Text = "Theme Design Style",
                            ToolTip = "Translucent enables optical glass, specular reflection, and compositor blur. Solid uses high-contrast matte dark styling.",
                            Orientation = Orientation.Horizontal,
                            Content = _themeStyleDropDown
                        },
                        new Group
                        {
                            Text = "HUD Font Family",
                            ToolTip = "Font face used for radial slices and badges.",
                            Orientation = Orientation.Horizontal,
                            Content = new StackLayout
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 6,
                                Items = { _fontFamilyDropDown, _fontFamilyTextBox }
                            }
                        },
                        new Group
                        {
                            Text = "Outer Radius (px)",
                            ToolTip = "Outer radius of the radial menu in pixels.",
                            Orientation = Orientation.Horizontal,
                            Content = _radiusSlider
                        },
                        new Group
                        {
                            Text = "Center Deadzone (px)",
                            ToolTip = "Deadzone radius in center circle. Releasing inside this deadzone cancels the gesture.",
                            Orientation = Orientation.Horizontal,
                            Content = _deadzoneSlider
                        },
                        new Group
                        {
                            Text = "Opacity (%)",
                            ToolTip = "HUD overlay opacity percentage.",
                            Orientation = Orientation.Horizontal,
                            Content = _opacitySlider
                        },
                        _anchorCheckBox,
                        _rightHandedCheckBox
                    }
                }
            };

            // Build items table
            var itemsTable = new TableLayout
            {
                Spacing = new Size(10, 8),
                Padding = new Padding(4)
            };

            // Header row
            itemsTable.Rows.Add(new TableRow
            {
                Cells =
                {
                    new Label { Text = "Position", Font = SystemFonts.Bold() },
                    new Label { Text = "Label", Font = SystemFonts.Bold() },
                    new TableCell(new Label { Text = "Assigned Binding", Font = SystemFonts.Bold() }, scaleWidth: true)
                }
            });

            for (int i = 0; i < 8; i++)
            {
                int index = i;
                var posLabel = new Label
                {
                    Text = SlicePositions[i],
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 140
                };

                var labelBox = new TextBox { Width = 110 };
                labelBox.TextChanged += (s, e) =>
                {
                    if (_isUpdating) return;
                    if (index < CurrentConfig.Items.Count)
                    {
                        CurrentConfig.Items[index].Label = labelBox.Text;
                    }
                };
                _labelBoxes.Add(labelBox);

                var bindingDisplay = new BindingDisplay(allowSecondaryModes: false) { IsHudBinding = true };
                bindingDisplay.StoreChanged += (s, e) =>
                {
                    if (_isUpdating) return;
                    if (index < CurrentConfig.Items.Count)
                    {
                        var item = CurrentConfig.Items[index];
                        item.Binding = bindingDisplay.Store;
                        item.SyncActionFromBinding();
                    }
                };
                _bindingDisplays.Add(bindingDisplay);

                itemsTable.Rows.Add(new TableRow
                {
                    Cells =
                    {
                        posLabel,
                        labelBox,
                        new TableCell(bindingDisplay, scaleWidth: true)
                    }
                });
            }

            var resetButton = new Button
            {
                Text = "Reset Slices to Default"
            };
            resetButton.Click += (s, e) =>
            {
                var defs = HudConfiguration.GetDefaults();
                CurrentConfig.Items = defs.Items;
                LoadSettings(CurrentConfig);
            };

            var itemsGroup = new Group
            {
                Text = "HUD Slices & Actions (Full Binding Support - Click to Edit / Keybind, '...' for Advanced)",
                Content = new StackLayout
                {
                    Spacing = 8,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        itemsTable,
                        new StackLayoutItem(resetButton, HorizontalAlignment.Left)
                    }
                }
            };

            Content = new Scrollable
            {
                Border = BorderType.None,
                Content = new StackLayout
                {
                    Spacing = 12,
                    Padding = new Padding(8),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        modeGroup,
                        itemsGroup
                    }
                }
            };

            LoadSettings(App.Current.Settings?.Hud);

            App.Current.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(App.Settings) && App.Current.Settings?.Hud != null)
                {
                    Application.Instance.AsyncInvoke(() => LoadSettings(App.Current.Settings?.Hud));
                }
            };
        }

        private static HudConfiguration CurrentConfig
        {
            get
            {
                if (App.Current.Settings is { } settings)
                    return settings.Hud ??= HudConfiguration.GetDefaults();

                return HudConfiguration.GetDefaults();
            }
        }

        public void LoadSettings(HudConfiguration? config)
        {
            config ??= HudConfiguration.GetDefaults();
            _isUpdating = true;
            try
            {
                _formFactorDropDown.SelectedKey = ((int)config.FormFactor).ToString();
                _themeStyleDropDown.SelectedKey = ((int)config.ThemeStyle).ToString();
                _fontFamilyTextBox.Text = string.IsNullOrWhiteSpace(config.FontFamily) ? "Sans" : config.FontFamily;

                bool foundFont = false;
                foreach (var item in _fontFamilyDropDown.Items)
                {
                    if (item.Key == _fontFamilyTextBox.Text)
                    {
                        _fontFamilyDropDown.SelectedKey = item.Key;
                        foundFont = true;
                        break;
                    }
                }
                if (!foundFont)
                    _fontFamilyDropDown.SelectedKey = "Custom";

                _radiusSlider.Value = config.Radius;
                _deadzoneSlider.Value = config.DeadzoneRadius;
                _opacitySlider.Value = (float)Math.Round(config.Opacity * 100f);
                _anchorCheckBox.Checked = config.KeepCursorAnchored;
                _rightHandedCheckBox.Checked = config.RightHanded;

                // Ensure 8 items exist
                if (config.Items.Count < 8)
                {
                    var defs = HudConfiguration.GetDefaults();
                    while (config.Items.Count < 8 && config.Items.Count < defs.Items.Count)
                    {
                        config.Items.Add(defs.Items[config.Items.Count]);
                    }
                }

                for (int i = 0; i < 8 && i < config.Items.Count; i++)
                {
                    var item = config.Items[i];
                    _labelBoxes[i].Text = item.Label ?? string.Empty;
                    _bindingDisplays[i].Store = item.GetEffectiveBinding();
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }
    }
}
