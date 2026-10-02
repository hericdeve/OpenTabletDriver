using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Controls.Generic;

namespace OpenTabletDriver.UX.Controls.Bindings
{
    public sealed class PenBindingEditor : BindingEditor
    {
        public PenBindingEditor()
        {
            this.Content = new Scrollable
            {
                Border = BorderType.None,
                Content = new StackLayout
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new TableLayout
                        {
                            Rows =
                            {
                                new TableRow
                                {
                                    Cells =
                                    {
                                        new Group
                                        {
                                            Text = "Tip Settings",
                                            Content = new StackLayout
                                            {
                                                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                                                Spacing = 5,
                                                Items =
                                                {
                                                    new Group
                                                    {
                                                        Text = "Tip Binding",
                                                        TitleWidth = 110,
                                                        TitleVerticalAlignment = VerticalAlignment.Top,
                                                        Orientation = Orientation.Horizontal,
                                                        ExpandContent = true,
                                                        ToolTip = "Configure primary tip action and optional capabilities (Double-Click, Hold, Deep Click via ⚙)",
                                                        Content = tipButton = new BindingDisplay(allowSecondaryModes: true, allowDeepClick: true)
                                                    },
                                                    new UnitGroup
                                                    {
                                                        Text = "Tip Threshold",
                                                        ToolTip = "The minimum threshold in order for the assigned binding to activate.",
                                                        Orientation = Orientation.Horizontal,
                                                        Content = tipThreshold = new FloatSlider(),
                                                        Unit = "%"
                                                    }
                                                }
                                            }
                                        },
                                        new Group
                                        {
                                            Text = "Eraser Settings",
                                            Content = new StackLayout
                                            {
                                                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                                                Spacing = 5,
                                                Items =
                                                {
                                                    new Group
                                                    {
                                                        Text = "Eraser Binding",
                                                        TitleWidth = 110,
                                                        TitleVerticalAlignment = VerticalAlignment.Top,
                                                        ExpandContent = true,
                                                        Orientation = Orientation.Horizontal,
                                                        ToolTip = "Configure eraser action and optional capabilities (Double-Click, Hold, Deep Click via ⚙)",
                                                        Content = eraserButton = new BindingDisplay()
                                                    },
                                                    new UnitGroup
                                                    {
                                                        Text = "Eraser Threshold",
                                                        ToolTip = "The minimum threshold in order for the assigned binding to activate.",
                                                        Orientation = Orientation.Horizontal,
                                                        Content = eraserThreshold = new FloatSlider(),
                                                        Unit = "%"
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        new Group
                        {
                            Text = "Pen Buttons",
                            Content = penButtons = new BindingDisplayList
                            {
                                Prefix = "Button",
                                TitleWidth = 140,
                                GetTitleFunc = index => index switch
                                {
                                    0 => "Button 1 (Lower)",
                                    1 => "Button 2 (Upper)",
                                    _ => $"Button {index + 1}"
                                }
                            }
                        },
                        new Group {
                            Text = "Miscellaneous",
                            Content = new StackLayout {
                                Orientation = Orientation.Horizontal,
                                Items = {
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = "Disable pressure if it is available",
                                        Content = disablePressure = new CheckBox {
                                             Text = "Disable Pressure",
                                        }
                                    },
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = "Locks drawing stroke pressure to constant 100% for uniform handwriting and circuit lines",
                                        Content = uniformStrokePressure = new CheckBox {
                                             Text = "Uniform Stroke Pressure",
                                        }
                                    },
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = "Disable tilt if it is available",
                                        Content = disableTilt = new CheckBox {
                                             Text = "Disable Tilt",
                                        }
                                    },
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = "Disable rotation if it is available",
                                        Content = disableRotation = new CheckBox {
                                             Text = "Disable Rotation",
                                        }
                                    },
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = "Pen Bindings require pressure to activate",
                                        Content = enableDragBindings = new CheckBox {
                                             Text = "Drag Bindings",
                                        }
                                    },
                                }
                            }
                        }
                    }
                }
            };

            tipButton.StoreBinding.Bind(SettingsBinding.Child(c => c.TipButton));
            eraserButton.StoreBinding.Bind(SettingsBinding.Child(c => c.EraserButton));
            tipThreshold.ValueBinding.Bind(SettingsBinding.Child(c => c.TipActivationThreshold));
            eraserThreshold.ValueBinding.Bind(SettingsBinding.Child(c => c.EraserActivationThreshold));
            penButtons.ItemSourceBinding.Bind(SettingsBinding.Child(c => (IList<PluginSettingStore>)c.PenButtons)!);
            disablePressure.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisablePressure));
            uniformStrokePressure.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.UniformStrokePressure));
            disableTilt.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisableTilt));
            disableRotation.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisableRotation));
            enableDragBindings.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.EnableDragBindings));

            // Sync TipButton capabilities with daemon TipDeepPress settings
            tipButton.StoreChanged += HandleTipStoreChanged;
            DataContextChanged += HandleDataContextChanged;
        }

        private void HandleDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is Desktop.Profiles.BindingSettings settings)
            {
                // If legacy TipDeepPressButton exists and TipButton is not yet a MultiAction with deep click, migrate it
                if (settings.TipDeepPressButton != null && settings.TipButton?.Path != typeof(MultiActionBinding).FullName)
                {
                    var multi = new PluginSettingStore(typeof(MultiActionBinding));
                    if (settings.TipButton != null)
                        multi[nameof(MultiActionBinding.TapAction)].SetValue(settings.TipButton);

                    multi[nameof(MultiActionBinding.DeepClickAction)].SetValue(settings.TipDeepPressButton);
                    multi[nameof(MultiActionBinding.DeepClickLiftAction)].SetValue(settings.TipDeepPressLiftButton);
                    multi[nameof(MultiActionBinding.DeepClickThreshold)].SetValue(settings.TipDeepPressThreshold);
                    multi[nameof(MultiActionBinding.DeepClickHoldDelayMs)].SetValue(settings.TipDeepPressHoldDelayMs);
                    multi[nameof(MultiActionBinding.DeepClickSuppressStroke)].SetValue(settings.TipDeepPressSuppressStroke);

                    settings.TipButton = multi;
                    tipButton.Store = multi;
                }
            }
        }

        private void HandleTipStoreChanged(object? sender, EventArgs e)
        {
            if (DataContext is Desktop.Profiles.BindingSettings settings)
            {
                var store = tipButton.Store;
                if (store != null && store.Path == typeof(MultiActionBinding).FullName)
                {
                    settings.TipDeepPressButton = store.GetNestedStore(nameof(MultiActionBinding.DeepClickAction));
                    settings.TipDeepPressLiftButton = store.GetNestedStore(nameof(MultiActionBinding.DeepClickLiftAction));

                    var dct = store.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.DeepClickThreshold));
                    if (dct?.Value != null && float.TryParse(dct.Value.ToString(), out float tVal))
                        settings.TipDeepPressThreshold = tVal;

                    var dcd = store.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.DeepClickHoldDelayMs));
                    if (dcd?.Value != null && float.TryParse(dcd.Value.ToString(), out float dVal))
                        settings.TipDeepPressHoldDelayMs = dVal;

                    var dcs = store.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.DeepClickSuppressStroke));
                    if (dcs?.Value != null && bool.TryParse(dcs.Value.ToString(), out bool sVal))
                        settings.TipDeepPressSuppressStroke = sVal;
                }
                else
                {
                    settings.TipDeepPressButton = null;
                    settings.TipDeepPressLiftButton = null;
                }
            }
        }

        private BindingDisplay tipButton, eraserButton;
        private FloatSlider tipThreshold, eraserThreshold;
        private CheckBox disablePressure, uniformStrokePressure, disableTilt, disableRotation, enableDragBindings;
        private BindingDisplayList penButtons;
    }
}
