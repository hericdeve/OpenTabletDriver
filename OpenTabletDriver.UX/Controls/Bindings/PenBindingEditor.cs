using System.Collections.Generic;
using Eto.Forms;
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
                                                        Content = tipButton = new BindingDisplay()
                                                    },
                                                    new UnitGroup
                                                    {
                                                        Text = "Tip Threshold",
                                                        ToolTip = "The minimum threshold in order for the assigned binding to activate.",
                                                        Orientation = Orientation.Horizontal,
                                                        Content = tipThreshold = new FloatSlider(),
                                                        Unit = "%"
                                                    },
                                                    new Group
                                                    {
                                                        Text = "Deep Press Action",
                                                        TitleWidth = 110,
                                                        TitleVerticalAlignment = VerticalAlignment.Top,
                                                        ExpandContent = true,
                                                        Orientation = Orientation.Horizontal,
                                                        ToolTip = "Secondary action executed when pressing firmly past the deep press threshold (e.g. Right Click, Floating HUD)",
                                                        Content = tipDeepPressButton = new BindingDisplay()
                                                    },
                                                    new UnitGroup
                                                    {
                                                        Text = "Deep Press Threshold",
                                                        ToolTip = "Pressure percentage required to activate the deep press action.",
                                                        Orientation = Orientation.Horizontal,
                                                        Content = tipDeepPressThreshold = new FloatSlider
                                                        {
                                                            Minimum = 50,
                                                            Maximum = 98
                                                        },
                                                        Unit = "%"
                                                    },
                                                    new UnitGroup
                                                    {
                                                        Text = "Deep Press Delay",
                                                        ToolTip = "Duration in milliseconds the deep press force must be held before activating to prevent misfires during quick downstrokes.",
                                                        Orientation = Orientation.Horizontal,
                                                        Content = tipDeepPressDelay = new FloatSlider
                                                        {
                                                            Minimum = 0,
                                                            Maximum = 300,
                                                            StepSize = 10
                                                        },
                                                        Unit = "ms"
                                                    },
                                                    new Group
                                                    {
                                                        Orientation = Orientation.Horizontal,
                                                        ToolTip = "Suppresses the normal drawing stroke when deep press is engaged to prevent ink marks on context menus",
                                                        Content = tipDeepPressSuppressStroke = new CheckBox
                                                        {
                                                            Text = "Suppress Stroke on Deep Press"
                                                        }
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
            tipDeepPressButton.StoreBinding.Bind(SettingsBinding.Child(c => c.TipDeepPressButton));
            tipDeepPressThreshold.ValueBinding.Bind(SettingsBinding.Child(c => c.TipDeepPressThreshold));
            tipDeepPressDelay.ValueBinding.Bind(SettingsBinding.Child(c => c.TipDeepPressHoldDelayMs));
            tipDeepPressSuppressStroke.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.TipDeepPressSuppressStroke));
            eraserButton.StoreBinding.Bind(SettingsBinding.Child(c => c.EraserButton));
            tipThreshold.ValueBinding.Bind(SettingsBinding.Child(c => c.TipActivationThreshold));
            eraserThreshold.ValueBinding.Bind(SettingsBinding.Child(c => c.EraserActivationThreshold));
            penButtons.ItemSourceBinding.Bind(SettingsBinding.Child(c => (IList<PluginSettingStore>)c.PenButtons)!);
            disablePressure.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisablePressure));
            uniformStrokePressure.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.UniformStrokePressure));
            disableTilt.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisableTilt));
            disableRotation.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisableRotation));
            enableDragBindings.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.EnableDragBindings));
        }

        private BindingDisplay tipButton, tipDeepPressButton, eraserButton;
        private FloatSlider tipThreshold, tipDeepPressThreshold, tipDeepPressDelay, eraserThreshold;
        private CheckBox tipDeepPressSuppressStroke, disablePressure, uniformStrokePressure, disableTilt, disableRotation, enableDragBindings;
        private BindingDisplayList penButtons;
    }
}
