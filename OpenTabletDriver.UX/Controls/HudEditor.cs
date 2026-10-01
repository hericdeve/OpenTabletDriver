using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Hud;
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
        private readonly FloatSlider _radiusSlider = new() { Minimum = 80, Maximum = 250, StepSize = 5 };
        private readonly FloatSlider _deadzoneSlider = new() { Minimum = 15, Maximum = 80, StepSize = 5 };
        private readonly FloatSlider _opacitySlider = new() { Minimum = 30, Maximum = 100, StepSize = 5 };
        private readonly CheckBox _anchorCheckBox = new() { Text = "Anchor pointer at gesture start during HUD flick" };
        private readonly CheckBox _rightHandedCheckBox = new() { Text = "Right-handed layout (Quick Bar)" };

        private readonly List<TextBox> _labelBoxes = new();
        private readonly List<DropDown> _typeDropDowns = new();
        private readonly List<TextBox> _valueBoxes = new();

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
                Spacing = new Size(8, 6),
                Padding = new Padding(4)
            };

            // Header row
            itemsTable.Rows.Add(new TableRow
            {
                Cells =
                {
                    new Label { Text = "Position", Font = SystemFonts.Bold() },
                    new Label { Text = "Label", Font = SystemFonts.Bold() },
                    new Label { Text = "Action Type", Font = SystemFonts.Bold() },
                    new Label { Text = "Value / Shortcut", Font = SystemFonts.Bold() }
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

                var labelBox = new TextBox { Width = 100 };
                labelBox.TextChanged += (s, e) =>
                {
                    if (_isUpdating) return;
                    if (index < CurrentConfig.Items.Count)
                    {
                        CurrentConfig.Items[index].Label = labelBox.Text;
                    }
                };
                _labelBoxes.Add(labelBox);

                var typeDropDown = new DropDown { Width = 140 };
                typeDropDown.Items.Add(new ListItem { Text = "Key Sequence", Key = "0" });
                typeDropDown.Items.Add(new ListItem { Text = "Driver Command", Key = "1" });
                typeDropDown.Items.Add(new ListItem { Text = "Mouse Click", Key = "2" });
                typeDropDown.Items.Add(new ListItem { Text = "Shell Command", Key = "3" });
                typeDropDown.SelectedValueChanged += (s, e) =>
                {
                    if (_isUpdating) return;
                    if (index < CurrentConfig.Items.Count && int.TryParse(typeDropDown.SelectedKey, out var typeVal))
                    {
                        CurrentConfig.Items[index].Action.Type = (HudActionType)typeVal;
                    }
                };
                _typeDropDowns.Add(typeDropDown);

                var valueBox = new TextBox { Width = 180 };
                valueBox.TextChanged += (s, e) =>
                {
                    if (_isUpdating) return;
                    if (index < CurrentConfig.Items.Count)
                    {
                        CurrentConfig.Items[index].Action.Value = valueBox.Text;
                    }
                };
                _valueBoxes.Add(valueBox);

                itemsTable.Rows.Add(new TableRow
                {
                    Cells = { posLabel, labelBox, typeDropDown, valueBox }
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
                Text = "HUD Slices & Actions (Apply or Save to take effect)",
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
                    Application.Instance.AsyncInvoke(() => LoadSettings(App.Current.Settings.Hud));
                }
            };
        }

        private HudConfiguration CurrentConfig =>
            App.Current.Settings?.Hud ?? (App.Current.Settings.Hud = HudConfiguration.GetDefaults());

        public void LoadSettings(HudConfiguration? config)
        {
            config ??= HudConfiguration.GetDefaults();
            _isUpdating = true;
            try
            {
                _formFactorDropDown.SelectedKey = ((int)config.FormFactor).ToString();
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
                    _typeDropDowns[i].SelectedKey = ((int)item.Action.Type).ToString();
                    _valueBoxes[i].Text = item.Action.Value ?? string.Empty;
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }
    }
}
