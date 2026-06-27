using System;
using System.Linq;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class HoldingBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Holding Binding";
        private const char KEYS_SPLITTER = '+';

        private string? _tapKeysString;
        private string? _holdKeysString;

        // Lazily resolved after keyboard is injected by [OnDependencyLoad]
        private string[] _tapKeys = [];
        private string[] _holdKeys = [];

        private readonly HPETDeltaStopwatch _pressStopwatch = new HPETDeltaStopwatch(startRunning: false);

        [Resolved]
        public IVirtualKeyboard? Keyboard { set; get; }

        [OnDependencyLoad]
        public void OnDependencyLoad()
        {
            if (Keyboard == null)
                Log.Write(PLUGIN_NAME,
                    $"{nameof(IVirtualKeyboard)} unavailable. {PLUGIN_NAME} will not work.", LogLevel.Error);

            // Re-parse now that keyboard is injected and SupportedKeys is available.
            // Properties are set before [Resolved] runs, so we must re-parse here.
            _tapKeys = ParseKeys(_tapKeysString);
            _holdKeys = ParseKeys(_holdKeysString);
        }

        [Property("Tap Keys")]
        [ToolTip("Key combo to activate on a short press (e.g. Ctrl+Z). Separate keys with '+'.")]
        public string? TapKeys
        {
            get => _tapKeysString;
            set
            {
                _tapKeysString = value;
                _tapKeys = ParseKeys(value); // Keyboard may still be null here; OnDependencyLoad re-parses
            }
        }

        [Property("Hold Keys")]
        [ToolTip("Key combo to activate when held past the threshold (e.g. Ctrl+Shift+Z). Separate keys with '+'.")]
        public string? HoldKeys
        {
            get => _holdKeysString;
            set
            {
                _holdKeysString = value;
                _holdKeys = ParseKeys(value); // Keyboard may still be null here; OnDependencyLoad re-parses
            }
        }

        [SliderProperty("Hold Threshold (ms)", 50f, 2000f, 400f)]
        [Unit("ms")]
        public float HoldThresholdMs { get; set; } = 400f;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            // Just record when the button went down. No keys fire on press.
            _pressStopwatch.Restart();
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            // Measure how long the button was held, then decide which action to fire.
            double elapsedMs = _pressStopwatch.Stop().TotalMilliseconds;
            bool isHold = elapsedMs >= HoldThresholdMs;

            var keys = isHold ? _holdKeys : _tapKeys;
            if (keys.Length > 0)
            {
                Keyboard?.Press(keys);
                Keyboard?.Release(keys);
            }
        }

        private string[] ParseKeys(string? str)
        {
            if (str == null) return [];
            var parts = str.Split(KEYS_SPLITTER, StringSplitOptions.TrimEntries);

            // If keyboard isn't resolved yet, store the parts and let OnDependencyLoad validate them.
            if (Keyboard == null) return parts;

            return parts.All(k => Keyboard.SupportedKeys.Contains(k)) ? parts : [];
        }

        public override string ToString() =>
            $"{PLUGIN_NAME}: tap=[{TapKeys}] hold=[{HoldKeys}] @{HoldThresholdMs}ms";
    }
}
