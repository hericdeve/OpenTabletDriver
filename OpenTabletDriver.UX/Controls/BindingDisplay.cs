using System;
using System.Globalization;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Windows.Bindings;

namespace OpenTabletDriver.UX.Controls
{
    public class BindingDisplay : Panel
    {
        public BindingDisplay() : this(true, false)
        {
        }

        public BindingDisplay(bool allowSecondaryModes, bool allowDeepClick = false)
        {
            _allowCapabilities = allowSecondaryModes;
            _allowDeepClick = allowDeepClick;

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
                        Control = _capabilitiesButton = new Button
                        {
                            Text = "⚙",
                            Width = 32,
                            Visible = _allowCapabilities,
                            ToolTip = _allowDeepClick
                                ? "Configure button capabilities (Double-Click, Hold, Deep Click)"
                                : "Configure button capabilities (Double-Click, Hold)"
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

            this.Content = _topRow;

            _mainButton.Click += async (sender, e) =>
            {
                PluginSettingStore? currentMainStore = IsMultiAction(Store)
                    ? Store!.GetNestedStore("TapAction")
                    : Store;

                PluginSettingStore? newStore;
                if (IsComplexBinding(currentMainStore))
                {
                    var dialog = new AdvancedBindingEditorDialog(currentMainStore, allowHudOnly: _isHudBinding);
                    newStore = await dialog.ShowModalAsync(this);
                }
                else
                {
                    var dialog = new BindingEditorDialog(currentMainStore);
                    newStore = await dialog.ShowModalAsync(this);
                }

                ApplyMainStore(newStore);
            };

            _capabilitiesButton.Click += async (sender, e) =>
            {
                var dialog = new BindingCapabilitiesDialog(Store, AllowDeepClick);
                var updatedStore = await dialog.ShowModalAsync(this);
                if (dialog.Applied)
                {
                    this.Store = updatedStore;
                }
            };

            _advancedButton.Click += async (sender, e) =>
            {
                PluginSettingStore? targetStore = IsMultiAction(Store)
                    ? Store!.GetNestedStore("TapAction")
                    : Store;

                var dialog = new AdvancedBindingEditorDialog(targetStore, allowHudOnly: _isHudBinding);
                var result = await dialog.ShowModalAsync(this);
                ApplyMainStore(result);
            };

            _clearButton.Click += (sender, e) =>
            {
                if (IsMultiAction(Store))
                {
                    var hold = Store!.GetNestedStore("HoldAction");
                    var holdLift = Store!.GetNestedStore("HoldLiftAction");
                    var dbl = Store!.GetNestedStore("DoubleClickAction");
                    var deep = _allowDeepClick ? Store!.GetNestedStore("DeepClickAction") : null;
                    bool hasCapabilities = hold != null || holdLift != null || dbl != null || deep != null;
                    var tap = Store!.GetNestedStore("TapAction");

                    if (tap != null && hasCapabilities)
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
        private readonly Button _capabilitiesButton;
        private readonly Button _advancedButton;
        private readonly Button _clearButton;
        private readonly StackLayout _topRow;

        private bool _isUpdating;
        private bool _allowCapabilities = true;
        private bool _allowDeepClick;
        private bool _isHudBinding;

        public bool IsHudBinding
        {
            get => _isHudBinding;
            set => _isHudBinding = value;
        }

        public bool AllowSecondaryModes
        {
            get => _allowCapabilities;
            set
            {
                _allowCapabilities = value;
                _capabilitiesButton.Visible = value;
            }
        }

        public bool AllowDeepClick
        {
            get => _allowDeepClick;
            set
            {
                _allowDeepClick = value;
                UpdateControlsFromStore();
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

        private void UpdateControlsFromStore()
        {
            if (_isUpdating)
                return;

            _isUpdating = true;
            try
            {
                if (IsMultiAction(store))
                {
                    var tapStore = store!.GetNestedStore("TapAction");
                    _mainButton.Text = tapStore != null ? tapStore.GetHumanReadableString() : "Unassigned";

                    bool hasActiveCapabilities = store!.GetNestedStore("HoldAction") != null ||
                                                 store!.GetNestedStore("HoldLiftAction") != null ||
                                                 store!.GetNestedStore("DoubleClickAction") != null ||
                                                 (_allowDeepClick && store!.GetNestedStore("DeepClickAction") != null);

                    _capabilitiesButton.Text = hasActiveCapabilities ? "⚙*" : "⚙";
                    _capabilitiesButton.ToolTip = hasActiveCapabilities
                        ? (_allowDeepClick
                            ? "Active capabilities (Hold / Lift / Double-Click / Deep Click). Click to configure."
                            : "Active capabilities (Hold / Lift / Double-Click). Click to configure.")
                        : (_allowDeepClick
                            ? "Configure button capabilities (Hold, Double-Click, Deep Click)"
                            : "Configure button capabilities (Hold, Double-Click)");
                }
                else
                {
                    _mainButton.Text = store != null ? store.GetHumanReadableString() : "Unassigned";
                    _capabilitiesButton.Text = "⚙";
                    _capabilitiesButton.ToolTip = _allowDeepClick
                        ? "Configure button capabilities (Hold, Double-Click, Deep Click)"
                        : "Configure button capabilities (Hold, Double-Click)";
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void ApplyMainStore(PluginSettingStore? newMainStore)
        {
            if (!AllowSecondaryModes || !IsMultiAction(Store))
            {
                this.Store = newMainStore;
                return;
            }

            Store!["TapAction"].SetValue(newMainStore);
            var hold = Store!.GetNestedStore("HoldAction");
            var holdLift = Store!.GetNestedStore("HoldLiftAction");
            var dbl = Store!.GetNestedStore("DoubleClickAction");
            var deep = _allowDeepClick ? Store!.GetNestedStore("DeepClickAction") : null;

            if (hold == null && holdLift == null && dbl == null && deep == null)
            {
                this.Store = newMainStore;
            }
            else
            {
                this.Store = new PluginSettingStore(Store!.Path!, Store!.Settings);
            }
        }

        private static bool IsMultiAction(PluginSettingStore? s) =>
            s?.Path == "OpenTabletDriver.Desktop.Binding.MultiActionBinding";

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
