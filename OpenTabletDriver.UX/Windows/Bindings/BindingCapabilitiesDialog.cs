using System;
using System.Globalization;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Controls;
using OpenTabletDriver.UX.Controls.Generic;

namespace OpenTabletDriver.UX.Windows.Bindings
{
    public class BindingCapabilitiesDialog : Dialog<PluginSettingStore?>
    {
        private readonly CheckBox _doubleClickCheckBox;
        private readonly Panel _doubleClickContainer;
        private readonly BindingDisplay _doubleClickDisplay;
        private readonly FloatSlider _doubleClickWindowSlider;

        private readonly CheckBox _holdCheckBox;
        private readonly Panel _holdContainer;
        private readonly BindingDisplay _holdDisplay;
        private readonly FloatSlider _holdThresholdSlider;
        private readonly BindingDisplay _holdLiftDisplay;
        private readonly DropDown _liftTriggerDropDown;

        private readonly CheckBox _deepClickCheckBox;
        private readonly Panel _deepClickContainer;
        private readonly BindingDisplay _deepClickDisplay;
        private readonly FloatSlider _deepClickThresholdSlider;
        private readonly FloatSlider _deepClickHoldDelaySlider;
        private readonly CheckBox _deepClickSuppressStrokeCheckBox;
        private readonly BindingDisplay _deepClickLiftDisplay;

        private PluginSettingStore? _tapStore;

        public BindingCapabilitiesDialog(PluginSettingStore? currentStore)
        {
            Title = "Button Capabilities";
            Resizable = false;
            Padding = new Padding(12);

            // Extract existing configuration if current store is already a MultiActionBinding
            PluginSettingStore? dblStore = null;
            PluginSettingStore? holdStore = null;
            PluginSettingStore? holdLiftStore = null;
            PluginSettingStore? deepStore = null;
            PluginSettingStore? deepLiftStore = null;

            float dblWindow = 250f;
            float holdThreshold = 400f;
            string liftTrigger = "Button Release";
            float deepThreshold = 80f;
            float deepDelay = 60f;
            bool deepSuppress = true;

            if (currentStore?.Path == typeof(MultiActionBinding).FullName)
            {
                _tapStore = currentStore.GetNestedStore(nameof(MultiActionBinding.TapAction));
                dblStore = currentStore.GetNestedStore(nameof(MultiActionBinding.DoubleClickAction));
                holdStore = currentStore.GetNestedStore(nameof(MultiActionBinding.HoldAction));
                holdLiftStore = currentStore.GetNestedStore(nameof(MultiActionBinding.HoldLiftAction));
                deepStore = currentStore.GetNestedStore(nameof(MultiActionBinding.DeepClickAction));
                deepLiftStore = currentStore.GetNestedStore(nameof(MultiActionBinding.DeepClickLiftAction));

                var dwVal = currentStore.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.DoubleClickWindowMs))?.Value;
                if (dwVal != null && float.TryParse(dwVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float dw))
                    dblWindow = dw;

                var htVal = currentStore.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.HoldThresholdMs))?.Value;
                if (htVal != null && float.TryParse(htVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float ht))
                    holdThreshold = ht;

                var ltVal = currentStore.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.LiftTrigger))?.Value?.ToString();
                if (!string.IsNullOrEmpty(ltVal))
                    liftTrigger = ltVal;

                var dctVal = currentStore.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.DeepClickThreshold))?.Value;
                if (dctVal != null && float.TryParse(dctVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float dct))
                    deepThreshold = dct;

                var dcdVal = currentStore.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.DeepClickHoldDelayMs))?.Value;
                if (dcdVal != null && float.TryParse(dcdVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float dcd))
                    deepDelay = dcd;

                var dcsVal = currentStore.Settings.FirstOrDefault(s => s.Property == nameof(MultiActionBinding.DeepClickSuppressStroke))?.Value;
                if (dcsVal != null && bool.TryParse(dcsVal.ToString(), out bool dcs))
                    deepSuppress = dcs;
            }
            else
            {
                _tapStore = currentStore;
            }

            // ── Double-Click Section ───────────────────────────────────────────
            _doubleClickCheckBox = new CheckBox
            {
                Text = "Enable Double-Click",
                Checked = dblStore != null
            };
            _doubleClickDisplay = new BindingDisplay(allowSecondaryModes: false) { Store = dblStore };
            _doubleClickWindowSlider = new FloatSlider
            {
                Minimum = 50,
                Maximum = 800,
                StepSize = 50,
                Value = dblWindow
            };

            var doubleClickContent = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Padding = new Padding(16, 4, 4, 4),
                Items =
                {
                    new TableLayout
                    {
                        Spacing = new Size(8, 4),
                        Rows =
                        {
                            new TableRow(new Label { Text = "Action:", VerticalAlignment = VerticalAlignment.Center }, _doubleClickDisplay),
                            new TableRow(new Label { Text = "Detection Window (ms):", VerticalAlignment = VerticalAlignment.Center }, _doubleClickWindowSlider)
                        }
                    }
                }
            };
            _doubleClickContainer = new Panel { Content = doubleClickContent, Visible = _doubleClickCheckBox.Checked == true };
            _doubleClickCheckBox.CheckedChanged += (s, e) =>
            {
                _doubleClickContainer.Visible = _doubleClickCheckBox.Checked == true;
            };

            // ── Hold Section ───────────────────────────────────────────────────
            _holdCheckBox = new CheckBox
            {
                Text = "Enable Hold",
                Checked = holdStore != null || holdLiftStore != null
            };
            _holdDisplay = new BindingDisplay(allowSecondaryModes: false) { Store = holdStore };
            _holdThresholdSlider = new FloatSlider
            {
                Minimum = 50,
                Maximum = 2000,
                StepSize = 50,
                Value = holdThreshold
            };
            _holdLiftDisplay = new BindingDisplay(allowSecondaryModes: false) { Store = holdLiftStore };
            _liftTriggerDropDown = new DropDown
            {
                DataStore = new[] { "Button Release", "Pen Tip Lift", "Either" },
                SelectedValue = liftTrigger
            };

            var holdContent = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Padding = new Padding(16, 4, 4, 4),
                Items =
                {
                    new TableLayout
                    {
                        Spacing = new Size(8, 4),
                        Rows =
                        {
                            new TableRow(new Label { Text = "Hold Action:", VerticalAlignment = VerticalAlignment.Center }, _holdDisplay),
                            new TableRow(new Label { Text = "Hold Threshold (ms):", VerticalAlignment = VerticalAlignment.Center }, _holdThresholdSlider),
                            new TableRow(new Label { Text = "On Lift Action (Optional):", VerticalAlignment = VerticalAlignment.Center }, _holdLiftDisplay),
                            new TableRow(new Label { Text = "Lift Trigger:", VerticalAlignment = VerticalAlignment.Center }, _liftTriggerDropDown)
                        }
                    }
                }
            };
            _holdContainer = new Panel { Content = holdContent, Visible = _holdCheckBox.Checked == true };
            _holdCheckBox.CheckedChanged += (s, e) =>
            {
                _holdContainer.Visible = _holdCheckBox.Checked == true;
            };

            // ── Deep Click Section ─────────────────────────────────────────────
            _deepClickCheckBox = new CheckBox
            {
                Text = "Enable Deep Click (Pressure / 3D Touch)",
                Checked = deepStore != null
            };
            _deepClickDisplay = new BindingDisplay(allowSecondaryModes: false) { Store = deepStore };
            _deepClickThresholdSlider = new FloatSlider
            {
                Minimum = 50,
                Maximum = 98,
                StepSize = 1,
                Value = deepThreshold
            };
            _deepClickHoldDelaySlider = new FloatSlider
            {
                Minimum = 0,
                Maximum = 300,
                StepSize = 10,
                Value = deepDelay
            };
            _deepClickSuppressStrokeCheckBox = new CheckBox
            {
                Text = "Suppress drawing stroke during deep click",
                Checked = deepSuppress
            };
            _deepClickLiftDisplay = new BindingDisplay(allowSecondaryModes: false) { Store = deepLiftStore };

            var deepClickContent = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Padding = new Padding(16, 4, 4, 4),
                Items =
                {
                    new TableLayout
                    {
                        Spacing = new Size(8, 4),
                        Rows =
                        {
                            new TableRow(new Label { Text = "Deep Click Action:", VerticalAlignment = VerticalAlignment.Center }, _deepClickDisplay),
                            new TableRow(new Label { Text = "Activation Threshold (%):", VerticalAlignment = VerticalAlignment.Center }, _deepClickThresholdSlider),
                            new TableRow(new Label { Text = "Hold Delay (ms):", VerticalAlignment = VerticalAlignment.Center }, _deepClickHoldDelaySlider),
                            new TableRow(new Label { Text = "Stroke Suppression:", VerticalAlignment = VerticalAlignment.Center }, _deepClickSuppressStrokeCheckBox),
                            new TableRow(new Label { Text = "On Lift Action (Optional):", VerticalAlignment = VerticalAlignment.Center }, _deepClickLiftDisplay)
                        }
                    }
                }
            };
            _deepClickContainer = new Panel { Content = deepClickContent, Visible = _deepClickCheckBox.Checked == true };
            _deepClickCheckBox.CheckedChanged += (s, e) =>
            {
                _deepClickContainer.Visible = _deepClickCheckBox.Checked == true;
            };

            // ── Dialog Layout ──────────────────────────────────────────────────
            var applyButton = new Button { Text = "Apply" };
            applyButton.Click += (s, e) => Apply();

            var cancelButton = new Button { Text = "Cancel" };
            cancelButton.Click += (s, e) => Close(currentStore);

            Content = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 12,
                Items =
                {
                    new GroupBox
                    {
                        Text = "Capabilities",
                        Content = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 10,
                            Padding = new Padding(8),
                            Items =
                            {
                                _doubleClickCheckBox,
                                _doubleClickContainer,
                                CreateDivider(),
                                _holdCheckBox,
                                _holdContainer,
                                CreateDivider(),
                                _deepClickCheckBox,
                                _deepClickContainer
                            }
                        }
                    },
                    new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalContentAlignment = HorizontalAlignment.Right,
                        Items =
                        {
                            cancelButton,
                            applyButton
                        }
                    }
                }
            };
        }

        private static Panel CreateDivider() => new Panel { Height = 1, BackgroundColor = Colors.DarkGray };

        private void Apply()
        {
            bool hasDbl = _doubleClickCheckBox.Checked == true && _doubleClickDisplay.Store != null;
            bool hasHold = _holdCheckBox.Checked == true && (_holdDisplay.Store != null || _holdLiftDisplay.Store != null);
            bool hasDeep = _deepClickCheckBox.Checked == true && _deepClickDisplay.Store != null;

            if (!hasDbl && !hasHold && !hasDeep)
            {
                // All capabilities off: return clean primary action without MultiActionBinding overhead
                Close(_tapStore);
                return;
            }

            // At least one capability is enabled: wrap inside MultiActionBinding
            var store = new PluginSettingStore(typeof(MultiActionBinding));
            if (_tapStore != null)
                store[nameof(MultiActionBinding.TapAction)].SetValue(_tapStore);

            if (hasDbl)
            {
                store[nameof(MultiActionBinding.DoubleClickAction)].SetValue(_doubleClickDisplay.Store);
                store[nameof(MultiActionBinding.DoubleClickWindowMs)].SetValue(_doubleClickWindowSlider.Value);
            }

            if (hasHold)
            {
                if (_holdDisplay.Store != null)
                    store[nameof(MultiActionBinding.HoldAction)].SetValue(_holdDisplay.Store);
                if (_holdLiftDisplay.Store != null)
                    store[nameof(MultiActionBinding.HoldLiftAction)].SetValue(_holdLiftDisplay.Store);

                store[nameof(MultiActionBinding.HoldThresholdMs)].SetValue(_holdThresholdSlider.Value);
                store[nameof(MultiActionBinding.LiftTrigger)].SetValue(_liftTriggerDropDown.SelectedValue?.ToString() ?? "Button Release");
            }

            if (hasDeep)
            {
                store[nameof(MultiActionBinding.DeepClickAction)].SetValue(_deepClickDisplay.Store);
                if (_deepClickLiftDisplay.Store != null)
                    store[nameof(MultiActionBinding.DeepClickLiftAction)].SetValue(_deepClickLiftDisplay.Store);

                store[nameof(MultiActionBinding.DeepClickThreshold)].SetValue(_deepClickThresholdSlider.Value);
                store[nameof(MultiActionBinding.DeepClickHoldDelayMs)].SetValue(_deepClickHoldDelaySlider.Value);
                store[nameof(MultiActionBinding.DeepClickSuppressStroke)].SetValue(_deepClickSuppressStrokeCheckBox.Checked == true);
            }

            Close(store);
        }
    }
}
