using System;
using System.Collections.Generic;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName("Precision Mode")]
    public class PrecisionModeBinding : IStateBinding, IPrecisionModifier, IContinuousBinding
    {
        public static string[] ValidModes => new[] { "Hold", "Toggle" };
        private static readonly string[] _validLiftTriggers = { "Button Release", "Pen Tip Lift", "Either" };

        private bool _isActive;
        private string _mode = "Hold";
        private IBinding? _onLiftBinding;
        private bool _wasTipDown;
        private bool _liftActionFired;

        public bool IsActive => _isActive;

        [Resolved]
        public IDriverDaemon? Daemon { set; get; }

        [Resolved]
        public IActiveAppContext? AppContext { set; get; }

        [Resolved]
        public IMouseButtonHandler? MouseButtonHandler { set; get; }

        [Resolved]
        public IMouseScrollHandler? MouseScrollHandler { set; get; }

        [Resolved]
        public IPenActionHandler? PenActionHandler { set; get; }

        [Resolved]
        public IVirtualKeyboard? Keyboard { set; get; }

        [TabletReference]
        public TabletReference? Tablet { set; get; }

        [OnDependencyLoad]
        public void OnDependencyLoad()
        {
            var sm = new ServiceManager();
            if (Daemon != null) sm.AddService(() => Daemon);
            if (AppContext != null) sm.AddService(() => AppContext);
            if (Keyboard != null) sm.AddService(() => Keyboard);
            if (MouseButtonHandler != null) sm.AddService(() => MouseButtonHandler);
            if (MouseScrollHandler != null) sm.AddService(() => MouseScrollHandler);
            if (PenActionHandler != null) sm.AddService(() => PenActionHandler);

            _onLiftBinding = OnLiftAction?.Construct<IBinding>(sm, Tablet);
        }

        [Property("Mode"),
         PropertyValidated(nameof(ValidModes)),
         DefaultPropertyValue("Hold"),
         ToolTip("Hold: active only while holding the button. Toggle: press once to turn on, press again to turn off.")]
        public string Mode
        {
            get => _mode;
            set
            {
                if (value == "Hold" || value == "Toggle")
                    _mode = value;
            }
        }

        [Property("Sensitivity"),
         DefaultPropertyValue(30),
         Unit("%"),
         ToolTip("Movement sensitivity percentage (e.g. 30% for fine linework precision, or >100% for speed mode).")]
        public int Sensitivity { get; set; } = 30;

        public float Scale => Math.Clamp(Sensitivity / 100f, 0.05f, 5.0f);

        [BooleanProperty("Reset on Lift", "Behavior"),
         DefaultPropertyValue(true),
         ToolTip("Whether the reference point re-anchors to the pen's new contact position when lifting the pen, enabling seamless multi-stroke drawing.")]
        public bool ReanchorOnLift { get; set; } = true;

        [Property("On Lift Action")]
        [ToolTip("Optional action to evoke when lifting / exiting Hold mode.")]
        public PluginSettingStore? OnLiftAction { get; set; }

        [Property("Lift Trigger"), PropertyValidated(nameof(ValidLiftTriggers))]
        [ToolTip("Specifies what constitutes 'lift': 'Button Release' (releasing held button), 'Pen Tip Lift' (lifting stylus tip off tablet), or 'Either'.")]
        public string LiftTrigger { get; set; } = "Button Release";

        public static IEnumerable<string> ValidLiftTriggers => _validLiftTriggers;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            _liftActionFired = false;
            _wasTipDown = report is ITabletReport { Pressure: > 0 };

            if (Mode == "Toggle")
                _isActive = !_isActive;
            else
                _isActive = true;
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            if (Mode == "Hold")
            {
                _isActive = false;

                if (ShouldTriggerLiftOnButtonRelease() && !_liftActionFired)
                {
                    _liftActionFired = true;
                    FireAction(_onLiftBinding, tablet, report);
                }
            }
        }

        public void Update(TabletReference tablet, IDeviceReport report)
        {
            if (Mode != "Hold" || !_isActive)
                return;

            if (report is ITabletReport tabletReport)
            {
                bool isTipDown = tabletReport.Pressure > 0;
                if (_wasTipDown && !isTipDown)
                {
                    if (ShouldTriggerLiftOnPenLift() && !_liftActionFired)
                    {
                        _liftActionFired = true;
                        FireAction(_onLiftBinding, tablet, report);
                    }
                }
                _wasTipDown = isTipDown;
            }
        }

        private bool ShouldTriggerLiftOnButtonRelease()
        {
            if (OnLiftAction == null)
                return false;

            return string.Equals(LiftTrigger, "Button Release", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(LiftTrigger, "Either", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldTriggerLiftOnPenLift()
        {
            if (OnLiftAction == null)
                return false;

            return string.Equals(LiftTrigger, "Pen Tip Lift", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(LiftTrigger, "Either", StringComparison.OrdinalIgnoreCase);
        }

        private static void FireAction(IBinding? binding, TabletReference tablet, IDeviceReport report)
        {
            if (binding is IStateBinding stateBinding)
            {
                stateBinding.Press(tablet, report);
                stateBinding.Release(tablet, report);
            }
        }
    }
}
