using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class MultiActionBinding : IStateBinding, IContinuousBinding, IPointerSuppressor
    {
        private const string PLUGIN_NAME = "Multi-Action Binding";
        private const char KEYS_SPLITTER = '+';
        private static readonly string[] _validLiftTriggers = { "Button Release", "Pen Tip Lift", "Either" };

        private IBinding? _tapBinding;
        private IBinding? _doubleClickBinding;
        private IBinding? _holdBinding;
        private IBinding? _holdLiftBinding;
        private IDeviceReport? _latestReport;
        private bool _wasTipDown;
        private bool _liftActionFired;

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
        private DateTime _lastReleaseTime;

        private static DateTime _lastHoldActionFiredGlobal;
        private readonly DateTime _createdAt = DateTime.Now;
        private readonly bool _isGhostInstance;
        private bool _firstPressSeen;

        public MultiActionBinding()
        {
            _isGhostInstance = (DateTime.Now - _lastHoldActionFiredGlobal < TimeSpan.FromMilliseconds(1000));
        }

        // ── Dependency injection ─────────────────────────────────────────────────

        [Resolved]
        public IMouseButtonHandler? MouseButtonHandler { set; get; }

        [Resolved]
        public IMouseScrollHandler? MouseScrollHandler { set; get; }

        [Resolved]
        public IPenActionHandler? PenActionHandler { set; get; }

        [Resolved]
        public IDriverDaemon? Daemon { set; get; }

        [Resolved]
        public IActiveAppContext? AppContext { set; get; }

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

            _tapBinding = TapAction?.Construct<IBinding>(sm, Tablet);
            _doubleClickBinding = DoubleClickAction?.Construct<IBinding>(sm, Tablet);
            _holdBinding = HoldAction?.Construct<IBinding>(sm, Tablet);
            _holdLiftBinding = HoldLiftAction?.Construct<IBinding>(sm, Tablet);
        }

        public bool IsActive => (_holdKeysDown || _holdActivated) && _holdBinding is IPointerSuppressor { IsActive: true };
        public bool SuppressMotion => (_holdKeysDown || _holdActivated) && _holdBinding is IPointerSuppressor { SuppressMotion: true };
        public bool SuppressTip => (_holdKeysDown || _holdActivated) && _holdBinding is IPointerSuppressor { SuppressTip: true };

        public void Update(TabletReference tablet, IDeviceReport report)
        {
            _latestReport = report;

            if ((_holdKeysDown || _holdActivated) && _holdBinding is IContinuousBinding continuous)
            {
                continuous.Update(tablet, report);
            }

            if (_holdKeysDown || _holdActivated)
            {
                if (report is ITabletReport tabletReport)
                {
                    bool isTipDown = tabletReport.Pressure > 0;
                    if (_wasTipDown && !isTipDown)
                    {
                        // Stylus tip lifted while holding
                        if (ShouldTriggerLiftOnPenLift() && !_liftActionFired)
                        {
                            _liftActionFired = true;
                            FireAction(_holdLiftBinding, tablet, report);
                        }
                    }
                    _wasTipDown = isTipDown;
                }
            }
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

        [Property("Hold Lift Action")]
        [ToolTip("Optional action to evoke on lift after holding (e.g. switch back to Brush or previous tool).")]
        public PluginSettingStore? HoldLiftAction { get; set; }

        [Property("Lift Trigger"), PropertyValidated(nameof(ValidLiftTriggers))]
        [ToolTip("Specifies what constitutes 'lift': 'Button Release' (releasing held button), 'Pen Tip Lift' (lifting stylus tip off tablet), or 'Either'.")]
        public string LiftTrigger { get; set; } = "Button Release";

        [SliderProperty("Hold Threshold (ms)", 50f, 2000f, 400f)]
        [Unit("ms")]
        public float HoldThresholdMs { get; set; } = 400f;

        [SliderProperty("Double-Click Window (ms)", 50f, 800f, 250f)]
        [Unit("ms")]
        public float DoubleClickWindowMs { get; set; } = 250f;

        public static IEnumerable<string> ValidLiftTriggers => _validLiftTriggers;

        // ── IStateBinding ─────────────────────────────────────────────────────────

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (DateTime.Now - _lastReleaseTime < TimeSpan.FromMilliseconds(30))
                return; // Hardware debounce: ignore micro-presses that occur immediately after a release

            if (_isGhostInstance && !_firstPressSeen)
            {
                _firstPressSeen = true;
                if (DateTime.Now - _createdAt < TimeSpan.FromMilliseconds(500))
                    return; // Ignore ghost presses that happen when the daemon hot-reloads while the button is physically held
            }
            _firstPressSeen = true;

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
                        _liftActionFired = false;
                        _wasTipDown = report is ITabletReport { Pressure: > 0 };
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

            if (newHoldCts != null && _holdBinding != null)
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
                            postAction = () =>
                            {
                                (_holdBinding as IStateBinding)?.Release(tablet, _latestReport ?? report);
                                if (ShouldTriggerLiftOnButtonRelease() && !_liftActionFired)
                                {
                                    _liftActionFired = true;
                                    FireAction(_holdLiftBinding, tablet, _latestReport ?? report);
                                }
                            };
                        }
                        else if (holdPending)
                        {
                            // Hold task is between Keyboard.Press and setting _holdKeysDown.
                            // The task's second lock block will detect state != FirstPressHeld
                            // and call Keyboard.Release itself.
                            _state = GestureState.Idle;
                            postAction = () =>
                            {
                                if (ShouldTriggerLiftOnButtonRelease() && !_liftActionFired)
                                {
                                    _liftActionFired = true;
                                    FireAction(_holdLiftBinding, tablet, _latestReport ?? report);
                                }
                            };
                        }
                        else if (_doubleClickBinding == null)
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
            _lastReleaseTime = DateTime.Now;
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
                {
                    _lastHoldActionFiredGlobal = DateTime.Now;
                    stateHold.Press(tablet, _latestReport ?? report);
                }

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

        private bool ShouldTriggerLiftOnButtonRelease()
        {
            if (HoldLiftAction == null)
                return false;

            return string.Equals(LiftTrigger, "Button Release", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(LiftTrigger, "Either", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldTriggerLiftOnPenLift()
        {
            if (HoldLiftAction == null)
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

        public override string ToString()
        {
            var liftStr = HoldLiftAction != null ? $" lift={HoldLiftAction.Name ?? "Set"}" : "";
            return $"{PLUGIN_NAME}: tap={TapAction?.Name ?? "None"} dbl={DoubleClickAction?.Name ?? "None"} hold={HoldAction?.Name ?? "None"}{liftStr}";
        }
    }
}


