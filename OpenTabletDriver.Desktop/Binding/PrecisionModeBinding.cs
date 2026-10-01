using System;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName("Precision Mode")]
    public class PrecisionModeBinding : IStateBinding, IPrecisionModifier
    {
        public static string[] ValidModes => new[] { "Hold", "Toggle" };

        private bool _isActive;
        private string _mode = "Hold";

        public bool IsActive => _isActive;

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

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (Mode == "Toggle")
                _isActive = !_isActive;
            else
                _isActive = true;
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            if (Mode == "Hold")
                _isActive = false;
        }
    }
}
