using OpenTabletDriver.Plugin.Platform.Pointer;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class MultiActionBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Multi-Action Binding";
        private const char KEYS_SPLITTER = '+';

        private IBinding? _tapBinding;
        private IBinding? _doubleClickBinding;
        private IBinding? _holdBinding;

        // ── State machine ────────────────────────────────────────────────────────
        //
        //  Idle  ──(press)──▶  FirstPressHeld  ──(release, no hold)──▶  WaitingForDoubleClick
        //                             │                                           │
        //                        (hold fires)                          (2nd press within window)
        //                             │                                           │
        //                             ▼                                           ▼
        //                     (release → Idle)                          SecondPressHeld
        //                                                                         │
        //                                                               (release → Idle, dbl fires)
        //
        //  WaitingForDoubleClick ──(window expires)──▶ Idle (tap fires)

        private enum GestureState { Idle, FirstPressHeld, WaitingForDoubleClick, SecondPressHeld }
        private GestureState _state = GestureState.Idle;

        // Hold-key flags: two separate flags to handle the window between
        // Keyboard.Press returning and the task setting _holdKeysDown.
        // Whoever clears _holdKeysDown is responsible for calling Keyboard.Release.
        private bool _holdActivated;  // threshold was crossed; hold action was decided
        private bool _holdKeysDown;   // Keyboard.Press(_holdKeys) has physically returned

        private CancellationTokenSource? _holdCts;
        private CancellationTokenSource? _doubleClickCts;
        private readonly object _stateLock = new object();

        // ── Dependency injection ─────────────────────────────────────────────────

        [Resolved]
        public IMouseButtonHandler? MouseButtonHandler { set; get; }

        [Resolved]
        public IMouseScrollHandler? MouseScrollHandler { set; get; }

        [Resolved]
        public IPenActionHandler? PenActionHandler { set; get; }

        [Resolved]

        [TabletReference]
        public TabletReference? Tablet { set; get; }

        [OnDependencyLoad]
        public void OnDependencyLoad()
        {
            var sm = new ServiceManager();
            if (MouseButtonHandler != null) sm.AddService(() => MouseButtonHandler);
            if (MouseScrollHandler != null) sm.AddService(() => MouseScrollHandler);
            if (PenActionHandler != null) sm.AddService(() => PenActionHandler);


            _tapBinding = TapAction?.Construct<IBinding>(sm, Tablet);
            _doubleClickBinding = DoubleClickAction?.Construct<IBinding>(sm, Tablet);
            _holdBinding = HoldAction?.Construct<IBinding>(sm, Tablet);
        }

        // ── Properties ───────────────────────────────────────────────────────────

        [Property("Tap Action")]
        [ToolTip("Action for a single short press. If Double-Click Action is configured, fires after the double-click window.")]
        public PluginSettingStore? TapAction { get; set; }

        [Property("Double-Click Action")]
        [ToolTip("Action to fire on two presses within the double-click window. Leave empty to skip the window and fire Tap immediately.")]
        public PluginSettingStore? DoubleClickAction { get; set; }

        [Property("Hold Action")]
        [ToolTip("Action to fire when the button is held past the hold threshold. Fires immediately when threshold is reached.")]
        public PluginSettingStore? HoldAction { get; set; }

        [SliderProperty("Hold Threshold (ms)", 50f, 2000f, 400f)]
        [Unit("ms")]
        public float HoldThresholdMs { get; set; } = 400f;

        [SliderProperty("Double-Click Window (ms)", 50f, 800f, 250f)]
        [Unit("ms")]
        public float DoubleClickWindowMs { get; set; } = 250f;

        // ── IStateBinding ─────────────────────────────────────────────────────────

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            CancellationTokenSource? dcCtsToCancel = null;
            CancellationTokenSource? oldHoldCts = null;
            CancellationTokenSource? newHoldCts = null;

            lock (_stateLock)
            {
                switch (_state)
                {
                    case GestureState.Idle:
                        _state = GestureState.FirstPressHeld;
                        _holdActivated = false;
                        _holdKeysDown = false;
                        oldHoldCts = _holdCts;
                        newHoldCts = _holdCts = new CancellationTokenSource();
                        break;

                    case GestureState.WaitingForDoubleClick:
                        // Second press within the double-click window — cancel the timer so
                        // the tap does not fire, then wait for the second release.
                        _state = GestureState.SecondPressHeld;
                        dcCtsToCancel = _doubleClickCts;
                        _doubleClickCts = null;
                        break;

                    // Spurious Press while already held — ignore.
                    default:
                        break;
                }
            }

            // Cancel / dispose OUTSIDE the lock to avoid re-entrancy from CTS callbacks.
            dcCtsToCancel?.Cancel();
            dcCtsToCancel?.Dispose();
            oldHoldCts?.Cancel();
            oldHoldCts?.Dispose();

            if (newHoldCts != null)
                StartHoldTask(newHoldCts, tablet, report);
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            // Decide what to do (inside the lock), then act (outside the lock).
            Action? postAction = null;
            CancellationTokenSource? holdCtsToCancel = null;

            lock (_stateLock)
            {
                switch (_state)
                {
                    case GestureState.FirstPressHeld:
                    {
                        bool shouldReleaseHold = _holdKeysDown;
                        bool holdPending = _holdActivated; // decided but Keyboard.Press not yet returned

                        _holdActivated = false;
                        _holdKeysDown = false;
                        holdCtsToCancel = _holdCts;
                        _holdCts = null;

                        if (shouldReleaseHold)
                        {
                            // Hold keys are physically down — release them and go idle.
                            _state = GestureState.Idle;
                            postAction = () => (_holdBinding as IStateBinding)?.Release(tablet, report);
                        }
                        else if (holdPending)
                        {
                            // Hold task is between Keyboard.Press and setting _holdKeysDown.
                            // The task's second lock block will detect state != FirstPressHeld
                            // and call Keyboard.Release itself.
                            _state = GestureState.Idle;
                            postAction = null;
                        }
                        else if (DoubleClickAction == null)
                        {
                            // Optimisation: no double-click action configured → fire tap immediately,
                            // skipping the double-click window entirely.
                            _state = GestureState.Idle;
                            postAction = () => FireAction(_tapBinding, tablet, report);
                        }
                        else
                        {
                            // Enter double-click wait.
                            _state = GestureState.WaitingForDoubleClick;
                            postAction = () => StartDoubleClickWait(tablet, report);
                        }
                        break;
                    }

                    case GestureState.SecondPressHeld:
                        _state = GestureState.Idle;
                        postAction = () => FireAction(_doubleClickBinding, tablet, report);
                        break;

                    // Spurious Release (Idle / WaitingForDoubleClick) — ignore.
                    default:
                        return;
                }
            }

            holdCtsToCancel?.Cancel();
            holdCtsToCancel?.Dispose();
            postAction?.Invoke();
        }

        // ── Background tasks ─────────────────────────────────────────────────────

        private void StartHoldTask(CancellationTokenSource cts, TabletReference tablet, IDeviceReport report)
        {
            var token = cts.Token;
            var delayMs = (int)Math.Max(1d, HoldThresholdMs);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    return; // Button released before threshold.
                }

                // Confirm the button is still in the first-press state.
                lock (_stateLock)
                {
                    if (_state != GestureState.FirstPressHeld)
                        return;

                    _holdActivated = true;
                }

                // Press hold keys outside the lock to avoid blocking the pipeline.
                if (_holdBinding is IStateBinding stateHold)
                    stateHold.Press(tablet, report);

                // Reconcile: did Release run while we were pressing?
                bool needImmediateRelease;
                lock (_stateLock)
                {
                    if (_state == GestureState.FirstPressHeld)
                    {
                        _holdKeysDown = true;
                        needImmediateRelease = false;
                    }
                    else
                    {
                        // Release() already ran and saw _holdKeysDown=false — it did not
                        // release the keys, so we must do it here.
                        needImmediateRelease = true;
                    }
                }

                if (needImmediateRelease)
                    (_holdBinding as IStateBinding)?.Release(tablet, report);
            }, token);
        }

        private void StartDoubleClickWait(TabletReference tablet, IDeviceReport report)
        {
            CancellationTokenSource newDcCts;
            lock (_stateLock)
            {
                newDcCts = _doubleClickCts = new CancellationTokenSource();
            }

            var token = newDcCts.Token;
            var windowMs = (int)Math.Max(1d, DoubleClickWindowMs);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(windowMs, token);
                }
                catch (OperationCanceledException)
                {
                    // Second press arrived — double-click fires in Release().
                    return;
                }

                // Window expired without a second press → fire tap.
                bool shouldFire;
                lock (_stateLock)
                {
                    shouldFire = _state == GestureState.WaitingForDoubleClick;
                    if (shouldFire)
                    {
                        _state = GestureState.Idle;
                        _doubleClickCts = null;
                    }
                }

                if (shouldFire)
                    FireAction(_tapBinding, tablet, report);
            }, token);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void FireAction(IBinding? binding, TabletReference tablet, IDeviceReport report)
        {
            if (binding is IStateBinding stateBinding)
            {
                stateBinding.Press(tablet, report);
                stateBinding.Release(tablet, report);
            }
        }

        public override string ToString() =>
            $"{PLUGIN_NAME}: tap={TapAction?.Name ?? "None"} dbl={DoubleClickAction?.Name ?? "None"} hold={HoldAction?.Name ?? "None"}";
    }
}
