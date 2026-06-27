using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        // Populated in OnDependencyLoad, after Keyboard is injected.
        private string[] _tapKeys = [];
        private string[] _holdKeys = [];

        // --- State machine ---
        // _holdActivated  : set to true once the threshold is reached and we DECIDE to run the hold action.
        // _holdKeysDown   : set to true only AFTER Keyboard.Press(_holdKeys) physically returns.
        //                   Whoever sets this to false is responsible for calling Keyboard.Release.
        private bool _isPressed;
        private bool _holdActivated;
        private bool _holdKeysDown;

        private CancellationTokenSource? _cts;
        private readonly object _stateLock = new object();

        private readonly HPETDeltaStopwatch _stopwatch = new HPETDeltaStopwatch(startRunning: false);

        [Resolved]
        public IVirtualKeyboard? Keyboard { set; get; }

        [OnDependencyLoad]
        public void OnDependencyLoad()
        {
            if (Keyboard == null)
                Log.Write(PLUGIN_NAME,
                    $"{nameof(IVirtualKeyboard)} unavailable. {PLUGIN_NAME} will not work.", LogLevel.Error);

            // Properties are set before [Resolved] runs, so Keyboard was null during the setters.
            // Re-parse now that the keyboard is available for SupportedKeys validation.
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
                _tapKeys = ParseKeys(value);
            }
        }

        [Property("Hold Keys")]
        [ToolTip("Key combo to activate when held past the threshold. Fires immediately when threshold is reached. Separate keys with '+'.")]
        public string? HoldKeys
        {
            get => _holdKeysString;
            set
            {
                _holdKeysString = value;
                _holdKeys = ParseKeys(value);
            }
        }

        [SliderProperty("Hold Threshold (ms)", 50f, 2000f, 400f)]
        [Unit("ms")]
        public float HoldThresholdMs { get; set; } = 400f;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            CancellationTokenSource? oldCts;
            CancellationTokenSource newCts;

            lock (_stateLock)
            {
                if (_isPressed)
                    return;

                _isPressed = true;
                _holdActivated = false;
                _holdKeysDown = false;

                oldCts = _cts;
                newCts = _cts = new CancellationTokenSource();
            }

            // Cancel any stale task from a previous press (belt-and-suspenders).
            oldCts?.Cancel();
            oldCts?.Dispose();

            _stopwatch.Restart();

            var token = newCts.Token;
            var delayMs = (int)Math.Max(1d, HoldThresholdMs);

            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for the hold threshold. Throws OperationCanceledException if button
                    // is released before the delay completes.
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    // Button was released before the threshold — tap fires in Release().
                    return;
                }

                // --- Threshold reached ---

                // Check (under lock) that the button is still held before committing.
                lock (_stateLock)
                {
                    if (!_isPressed)
                        return; // Released at exactly the threshold moment; tap fires in Release().

                    _holdActivated = true;
                }

                // Press the hold keys. This happens OUTSIDE the lock to avoid blocking
                // the report pipeline, but we reconcile state immediately after.
                Keyboard?.Press(_holdKeys);

                // Now mark the keys as physically down (or immediately release if the button
                // was released between Keyboard.Press and this point).
                bool needImmediateRelease;
                lock (_stateLock)
                {
                    if (_isPressed)
                    {
                        _holdKeysDown = true;
                        needImmediateRelease = false;
                    }
                    else
                    {
                        // Release() already ran and saw _holdKeysDown=false, so it did not
                        // release the keys. We must do it here.
                        needImmediateRelease = true;
                    }
                }

                if (needImmediateRelease)
                    Keyboard?.Release(_holdKeys);
            }, token);
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            bool wasHold, shouldReleaseHoldKeys;
            CancellationTokenSource? ctsToCancel;

            lock (_stateLock)
            {
                if (!_isPressed)
                    return;

                _isPressed = false;
                wasHold = _holdActivated;
                _holdActivated = false;
                shouldReleaseHoldKeys = _holdKeysDown;
                _holdKeysDown = false;
                ctsToCancel = _cts;
                _cts = null;
            }

            // Cancel the hold-detection task if it hasn't reached the threshold yet.
            ctsToCancel?.Cancel();
            ctsToCancel?.Dispose();

            if (shouldReleaseHoldKeys)
            {
                // Hold keys were physically down — release them.
                Keyboard?.Release(_holdKeys);
            }
            else if (!wasHold)
            {
                // Neither hold state was set: this was a tap.
                // Fire tap as a momentary press+release.
                if (_tapKeys.Length > 0)
                {
                    Keyboard?.Press(_tapKeys);
                    Keyboard?.Release(_tapKeys);
                }
            }
            // If wasHold=true but shouldReleaseHoldKeys=false, the background task pressed
            // the keys but hasn't set _holdKeysDown yet. The task's second lock block will
            // detect _isPressed=false and call Release itself. Nothing to do here.
        }

        private string[] ParseKeys(string? str)
        {
            if (str == null) return [];
            var parts = str.Split(KEYS_SPLITTER, StringSplitOptions.TrimEntries);

            // If keyboard isn't injected yet, store the raw parts.
            // OnDependencyLoad will re-parse and validate against SupportedKeys.
            if (Keyboard == null) return parts;

            return parts.All(k => Keyboard.SupportedKeys.Contains(k)) ? parts : [];
        }

        public override string ToString() =>
            $"{PLUGIN_NAME}: tap=[{TapKeys}] hold=[{HoldKeys}] @{HoldThresholdMs}ms";
    }
}
