using System;
using System.Globalization;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Controls.Generic.Text;
using OpenTabletDriver.UX.Windows.Bindings;

namespace OpenTabletDriver.UX.Controls
{
    public class BindingDisplay : Panel
    {
        public BindingDisplay() : this(true)
        {
        }

        public BindingDisplay(bool allowSecondaryModes)
        {
            _allowSecondaryModes = allowSecondaryModes;

            _topRow = new StackLayout
            {
                Spacing = 5,
                MinimumSize = new Size(300, 0),
                Orientation = Orientation.Horizontal,
                Items =
                {
                    new StackLayoutItem
                    {
                        Expand = true,
                        Control = _mainButton = new Button()
                    },
                    new StackLayoutItem
                    {
                        Control = _modesToggleButton = new Button
                        {
                            Text = "Modes ▸",
                            Visible = allowSecondaryModes,
                            ToolTip = "Toggle secondary modes (Hold, Double-Click)"
                        }
                    },
                    new StackLayoutItem
                    {
                        Control = _advancedButton = new Button
                        {
                            Text = "...",
                            Width = 28,
                            ToolTip = "Configure binding / Change type"
                        }
                    },
                    new StackLayoutItem
                    {
                        Control = _clearButton = new Button
                        {
                            Text = "✕",
                            Width = 28,
                            ToolTip = "Clear binding"
                        }
                    }
                }
            };

            _mainLayout = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 3,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    _topRow
                }
            };

            if (allowSecondaryModes)
            {
                _holdDisplay = new BindingDisplay(allowSecondaryModes: false);
                _holdLiftDisplay = new BindingDisplay(allowSecondaryModes: false);
                _doubleClickDisplay = new BindingDisplay(allowSecondaryModes: false);
                _holdThresholdBox = new FloatNumberBox { Width = 55, ToolTip = "Hold activation threshold in milliseconds", Value = 400f };
                _doubleClickWindowBox = new FloatNumberBox { Width = 55, ToolTip = "Double-click detection window in milliseconds", Value = 250f };
                _liftTriggerDropDown = new DropDown
                {
                    Width = 115,
                    ToolTip = "Trigger for lift action:\n• Button Release: when the held button is released\n• Pen Tip Lift: when the stylus tip is lifted off the tablet\n• Either: whichever occurs first"
                };
                _liftTriggerDropDown.Items.Add(new ListItem { Text = "Button Release", Key = "Button Release" });
                _liftTriggerDropDown.Items.Add(new ListItem { Text = "Pen Tip Lift", Key = "Pen Tip Lift" });
                _liftTriggerDropDown.Items.Add(new ListItem { Text = "Either", Key = "Either" });
                _liftTriggerDropDown.SelectedKey = "Button Release";

                _secondaryPanel = new StackLayout
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 4,
                    Padding = new Padding(12, 4, 0, 4),
                    Visible = false,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new StackLayoutItem
                        {
                            Control = new StackLayout
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 5,
                                VerticalContentAlignment = VerticalAlignment.Center,
                                Items =
                                {
                                    new StackLayoutItem
                                    {
                                        Control = new Label { Text = "Hold:", Width = 80, VerticalAlignment = VerticalAlignment.Center }
                                    },
                                    new StackLayoutItem
                                    {
                                        Expand = true,
                                        Control = _holdDisplay
                                    },
                                    new StackLayoutItem
                                    {
                                        Control = _holdThresholdBox
                                    },
                                    new StackLayoutItem
                                    {
                                        Control = new Label { Text = "ms", VerticalAlignment = VerticalAlignment.Center }
                                    }
                                }
                            }
                        },
                        new StackLayoutItem
                        {
                            Control = new StackLayout
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 5,
                                VerticalContentAlignment = VerticalAlignment.Center,
                                Items =
                                {
                                    new StackLayoutItem
                                    {
                                        Control = new Label { Text = "On Lift:", Width = 80, VerticalAlignment = VerticalAlignment.Center }
                                    },
                                    new StackLayoutItem
                                    {
                                        Expand = true,
                                        Control = _holdLiftDisplay
                                    },
                                    new StackLayoutItem
                                    {
                                        Control = _liftTriggerDropDown
                                    }
                                }
                            }
                        },
                        new StackLayoutItem
                        {
                            Control = new StackLayout
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 5,
                                VerticalContentAlignment = VerticalAlignment.Center,
                                Items =
                                {
                                    new StackLayoutItem
                                    {
                                        Control = new Label { Text = "Double-Click:", Width = 80, VerticalAlignment = VerticalAlignment.Center }
                                    },
                                    new StackLayoutItem
                                    {
                                        Expand = true,
                                        Control = _doubleClickDisplay
                                    },
                                    new StackLayoutItem
                                    {
                                        Control = _doubleClickWindowBox
                                    },
                                    new StackLayoutItem
                                    {
                                        Control = new Label { Text = "ms", VerticalAlignment = VerticalAlignment.Center }
                                    }
                                }
                            }
                        }
                    }
                };

                _mainLayout.Items.Add(_secondaryPanel);

                _holdDisplay.StoreChanged += (sender, e) =>
                {
                    if (_isUpdating) return;
                    SyncSecondaryToStore();
                };

                _holdLiftDisplay.StoreChanged += (sender, e) =>
                {
                    if (_isUpdating) return;
                    SyncSecondaryToStore();
                };

                _doubleClickDisplay.StoreChanged += (sender, e) =>
                {
                    if (_isUpdating) return;
                    SyncSecondaryToStore();
                };

                _liftTriggerDropDown.SelectedValueChanged += (sender, e) =>
                {
                    if (_isUpdating) return;
                    if (IsMultiAction(this.Store))
                    {
                        this.Store!["LiftTrigger"].SetValue(_liftTriggerDropDown.SelectedKey ?? "Button Release");
                        StoreChanged?.Invoke(this, EventArgs.Empty);
                    }
                };

                _holdThresholdBox.ValueChanged += (sender, e) =>
                {
                    if (_isUpdating) return;
                    if (IsMultiAction(this.Store))
                    {
                        this.Store!["HoldThresholdMs"].SetValue(_holdThresholdBox.Value);
                        StoreChanged?.Invoke(this, EventArgs.Empty);
                    }
                };

                _doubleClickWindowBox.ValueChanged += (sender, e) =>
                {
                    if (_isUpdating) return;
                    if (IsMultiAction(this.Store))
                    {
                        this.Store!["DoubleClickWindowMs"].SetValue(_doubleClickWindowBox.Value);
                        StoreChanged?.Invoke(this, EventArgs.Empty);
                    }
                };
            }

            this.Content = _mainLayout;

            _mainButton.Click += async (sender, e) =>
            {
                PluginSettingStore? currentMainStore = IsMultiAction(Store)
                    ? Store!.GetNestedStore("TapAction")
                    : Store;

                PluginSettingStore? newStore;
                if (IsComplexBinding(currentMainStore))
                {
                    var dialog = new AdvancedBindingEditorDialog(currentMainStore);
                    newStore = await dialog.ShowModalAsync(this);
                }
                else
                {
                    var dialog = new BindingEditorDialog(currentMainStore);
                    newStore = await dialog.ShowModalAsync(this);
                }

                ApplyMainStore(newStore);
            };

            _modesToggleButton.Click += (sender, e) =>
            {
                SetSecondaryModesVisible(_secondaryPanel == null || !_secondaryPanel.Visible);
            };

            _advancedButton.Click += async (sender, e) =>
            {
                var dialog = new AdvancedBindingEditorDialog(Store);
                this.Store = await dialog.ShowModalAsync(this);
            };

            _clearButton.Click += (sender, e) =>
            {
                if (IsMultiAction(Store))
                {
                    var hold = Store!.GetNestedStore("HoldAction");
                    var holdLift = Store!.GetNestedStore("HoldLiftAction");
                    var dbl = Store!.GetNestedStore("DoubleClickAction");
                    bool hasSecondary = hold != null || holdLift != null || dbl != null;
                    var tap = Store!.GetNestedStore("TapAction");

                    if (tap != null && hasSecondary)
                    {
                        ApplyMainStore(null);
                    }
                    else
                    {
                        this.Store = null;
                    }
                }
                else
                {
                    this.Store = null;
                }
            };

            _clearButton.GetEnabledBinding().Bind(this.StoreBinding.Convert(s => s != null));

            UpdateControlsFromStore();
        }

        private readonly Button _mainButton;
        private readonly Button _modesToggleButton;
        private readonly Button _advancedButton;
        private readonly Button _clearButton;
        private readonly StackLayout _topRow;
        private readonly StackLayout? _secondaryPanel;
        private readonly StackLayout _mainLayout;

        private readonly BindingDisplay? _holdDisplay;
        private readonly BindingDisplay? _holdLiftDisplay;
        private readonly BindingDisplay? _doubleClickDisplay;
        private readonly FloatNumberBox? _holdThresholdBox;
        private readonly FloatNumberBox? _doubleClickWindowBox;
        private readonly DropDown? _liftTriggerDropDown;

        private bool _isUpdating;
        private bool _allowSecondaryModes = true;

        public bool AllowSecondaryModes
        {
            get => _allowSecondaryModes;
            set
            {
                _allowSecondaryModes = value;
                _modesToggleButton.Visible = value;
                if (!value && _secondaryPanel != null)
                    _secondaryPanel.Visible = false;
            }
        }

        public event EventHandler<EventArgs>? StoreChanged;

        private PluginSettingStore? store;
        public PluginSettingStore? Store
        {
            set
            {
                this.store = value;
                UpdateControlsFromStore();
                StoreChanged?.Invoke(this, new EventArgs());
            }
            get => this.store;
        }

        public BindableBinding<BindingDisplay, PluginSettingStore?> StoreBinding
        {
            get
            {
                return new BindableBinding<BindingDisplay, PluginSettingStore?>(
                    this,
                    c => c.Store,
                    (c, v) => c.Store = v,
                    (c, h) => c.StoreChanged += h,
                    (c, h) => c.StoreChanged -= h
                );
            }
        }

        private void SetSecondaryModesVisible(bool visible)
        {
            if (_secondaryPanel != null)
                _secondaryPanel.Visible = visible;
            UpdateModesButtonText();
        }

        private void UpdateModesButtonText()
        {
            if (!AllowSecondaryModes || _secondaryPanel == null)
                return;

            bool hasActiveSecondary = false;
            if (IsMultiAction(Store))
            {
                hasActiveSecondary = Store!.GetNestedStore("HoldAction") != null ||
                                     Store!.GetNestedStore("HoldLiftAction") != null ||
                                     Store!.GetNestedStore("DoubleClickAction") != null;
            }
            else if (_holdDisplay?.Store != null || _holdLiftDisplay?.Store != null || _doubleClickDisplay?.Store != null)
            {
                hasActiveSecondary = true;
            }

            string arrow = _secondaryPanel.Visible ? "▾" : "▸";
            _modesToggleButton.Text = hasActiveSecondary ? $"Modes* {arrow}" : $"Modes {arrow}";
            _modesToggleButton.ToolTip = hasActiveSecondary
                ? "Secondary modes active (Hold / On Lift / Double-Click). Click to toggle view."
                : "Toggle secondary modes (Hold, On Lift, Double-Click)";
        }

        private void UpdateControlsFromStore()
        {
            if (_isUpdating)
                return;

            _isUpdating = true;
            try
            {
                if (!AllowSecondaryModes || _secondaryPanel == null || _holdDisplay == null || _holdLiftDisplay == null || _doubleClickDisplay == null || _holdThresholdBox == null || _doubleClickWindowBox == null || _liftTriggerDropDown == null)
                {
                    _mainButton.Text = store != null ? store.GetHumanReadableString() : "Unassigned";
                    return;
                }

                if (IsMultiAction(store))
                {
                    var tapStore = store!.GetNestedStore("TapAction");
                    var holdStore = store!.GetNestedStore("HoldAction");
                    var holdLiftStore = store!.GetNestedStore("HoldLiftAction");
                    var dblStore = store!.GetNestedStore("DoubleClickAction");

                    float holdMs = GetFloatSetting(store!, "HoldThresholdMs", 400f);
                    float dblMs = GetFloatSetting(store!, "DoubleClickWindowMs", 250f);
                    string liftTrigger = store!.Settings.FirstOrDefault(s => s.Property == "LiftTrigger")?.Value?.ToString() ?? "Button Release";

                    _mainButton.Text = tapStore != null ? tapStore.GetHumanReadableString() : "Unassigned";

                    _holdDisplay.Store = holdStore;
                    _holdLiftDisplay.Store = holdLiftStore;
                    _doubleClickDisplay.Store = dblStore;
                    _holdThresholdBox.Value = holdMs;
                    _doubleClickWindowBox.Value = dblMs;
                    _liftTriggerDropDown.SelectedKey = liftTrigger;

                    bool hasSecondary = holdStore != null || holdLiftStore != null || dblStore != null;
                    if (hasSecondary && !_secondaryPanel.Visible)
                    {
                        _secondaryPanel.Visible = true;
                    }
                    UpdateModesButtonText();
                }
                else
                {
                    _mainButton.Text = store != null ? store.GetHumanReadableString() : "Unassigned";

                    _holdDisplay.Store = null;
                    _holdLiftDisplay.Store = null;
                    _doubleClickDisplay.Store = null;
                    _holdThresholdBox.Value = 400f;
                    _doubleClickWindowBox.Value = 250f;
                    _liftTriggerDropDown.SelectedKey = "Button Release";
                    UpdateModesButtonText();
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void ApplyMainStore(PluginSettingStore? newMainStore)
        {
            if (!AllowSecondaryModes || _secondaryPanel == null || _holdDisplay == null || _holdLiftDisplay == null || _doubleClickDisplay == null)
            {
                this.Store = newMainStore;
                return;
            }

            if (IsMultiAction(Store))
            {
                Store!["TapAction"].SetValue(newMainStore);
                var hold = Store!.GetNestedStore("HoldAction");
                var holdLift = Store!.GetNestedStore("HoldLiftAction");
                var dbl = Store!.GetNestedStore("DoubleClickAction");

                if (hold == null && holdLift == null && dbl == null)
                {
                    this.Store = newMainStore;
                }
                else
                {
                    this.Store = new PluginSettingStore(Store!.Path!, Store!.Settings);
                }
            }
            else
            {
                var holdStore = _holdDisplay?.Store;
                var holdLiftStore = _holdLiftDisplay?.Store;
                var dblStore = _doubleClickDisplay?.Store;
                bool hasSecondary = holdStore != null || holdLiftStore != null || dblStore != null;

                if (hasSecondary)
                {
                    var multi = EnsureMultiActionStore();
                    multi["TapAction"].SetValue(newMainStore);
                    this.Store = multi;
                }
                else
                {
                    this.Store = newMainStore;
                }
            }
        }

        private void SyncSecondaryToStore()
        {
            if (!AllowSecondaryModes || _holdDisplay == null || _holdLiftDisplay == null || _doubleClickDisplay == null || _holdThresholdBox == null || _doubleClickWindowBox == null || _liftTriggerDropDown == null)
                return;

            var holdStore = _holdDisplay.Store;
            var holdLiftStore = _holdLiftDisplay.Store;
            var dblStore = _doubleClickDisplay.Store;
            bool hasSecondary = holdStore != null || holdLiftStore != null || dblStore != null;

            if (hasSecondary)
            {
                var multi = EnsureMultiActionStore();
                multi["HoldAction"].SetValue(holdStore);
                multi["HoldLiftAction"].SetValue(holdLiftStore);
                multi["LiftTrigger"].SetValue(_liftTriggerDropDown.SelectedKey ?? "Button Release");
                multi["DoubleClickAction"].SetValue(dblStore);
                multi["HoldThresholdMs"].SetValue(_holdThresholdBox.Value > 0 ? _holdThresholdBox.Value : 400f);
                multi["DoubleClickWindowMs"].SetValue(_doubleClickWindowBox.Value > 0 ? _doubleClickWindowBox.Value : 250f);

                this.Store = multi;
            }
            else
            {
                if (IsMultiAction(this.Store))
                {
                    var tapStore = this.Store!.GetNestedStore("TapAction");
                    this.Store = tapStore;
                }
            }
            UpdateModesButtonText();
        }

        private PluginSettingStore EnsureMultiActionStore()
        {
            if (IsMultiAction(this.Store))
                return this.Store!;

            var multiStore = new PluginSettingStore(typeof(MultiActionBinding));
            if (this.Store != null)
            {
                multiStore["TapAction"].SetValue(this.Store);
            }
            float holdMs = _holdThresholdBox?.Value ?? 400f;
            float dblMs = _doubleClickWindowBox?.Value ?? 250f;
            multiStore["HoldThresholdMs"].SetValue(holdMs > 0 ? holdMs : 400f);
            multiStore["DoubleClickWindowMs"].SetValue(dblMs > 0 ? dblMs : 250f);
            multiStore["LiftTrigger"].SetValue(_liftTriggerDropDown?.SelectedKey ?? "Button Release");
            return multiStore;
        }

        private static bool IsMultiAction(PluginSettingStore? s) =>
            s?.Path == "OpenTabletDriver.Desktop.Binding.MultiActionBinding";

        private static float GetFloatSetting(PluginSettingStore store, string propertyName, float defaultValue)
        {
            var setting = store.Settings.FirstOrDefault(s => s.Property == propertyName);
            if (setting != null && setting.HasValue)
            {
                try
                {
                    return setting.GetValue<float>();
                }
                catch
                {
                    if (float.TryParse(setting.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float val))
                        return val;
                }
            }
            return defaultValue;
        }

        private static bool IsComplexBinding(PluginSettingStore? store)
        {
            if (store?.Path == null)
                return false;

            var simpleTypes = new[]
            {
                "OpenTabletDriver.Desktop.Binding.KeyBinding",
                "OpenTabletDriver.Desktop.Binding.MultiKeyBinding",
                "OpenTabletDriver.Desktop.Binding.MouseBinding",
                "OpenTabletDriver.Desktop.Binding.ScrollUpBinding",
                "OpenTabletDriver.Desktop.Binding.ScrollDownBinding",
                "OpenTabletDriver.Desktop.Binding.ScrollLeftBinding",
                "OpenTabletDriver.Desktop.Binding.ScrollRightBinding",
                "OpenTabletDriver.Desktop.Binding.ZoomInBinding",
                "OpenTabletDriver.Desktop.Binding.ZoomOutBinding"
            };

            return !simpleTypes.Contains(store.Path);
        }
    }
}
